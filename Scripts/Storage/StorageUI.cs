// Assets/Scripts/VoxelEngine/Storage/StorageUI.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        ALL STORAGE-SYSTEM UI PANELS — one static builder each  ║
// ║  Storage Terminal · Pattern Terminal · Crafting Terminal        ║
// ║  Storage Importer · Storage Exporter · Disk Manipulator · NAS  ║
// ║  Server Rack                                                    ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.UI;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Storage
{
    public static class StorageUI
    {
        // ════════════════════════════════════════════════════════════
        //                    STORAGE TERMINAL
        //  Big-chest slot-grid layout.  Each unique item type stored
        //  in the network gets its own icon slot.
        //  • Click a slot          → extract 1 (up to the matter-stack cap)
        //  • Shift+Click a slot    → extract a full matter stack
        //  • Shift+Click inventory → store into network  (handled by
        //    GameUIController.QuickTransfer via _openStorageTerminal)
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildTerminalPanel(
            StorageTerminal terminal,
            MachineUIs.SlotBuilder slotBuilder,
            Inventory playerInv)
        {
            // ── 14.40.0 full remake ───────────────────────────────
            // The terminal is the player's window into the network, so it gets
            // the whole right side of the screen (everything from the equipment
            // console to the right edge) and the same LCD chassis/phosphor
            // theme as the inventory itself. Count ON the icon, name under it,
            // 1-second hover tooltip with data size and weight, and every
            // readout self-refreshes - no more stale GB numbers.
            var p = new VisualElement { name = "StorageTerminalPanel" };
            p.style.position = Position.Absolute;
            p.style.top = 12; p.style.bottom = 72; p.style.right = 12;
            p.style.width = new StyleLength(new Length(54f, LengthUnit.Percent));
            p.style.minWidth = 540;
            p.style.paddingTop = 6; p.style.paddingBottom = 6;
            p.style.paddingLeft = 6; p.style.paddingRight = 6;
            p.style.overflow = Overflow.Hidden;
            LcdHudTheme.ApplyChassis(p, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.98f), 2f);

            var screen = new VisualElement { name = "StorageTerminalScreen" };
            screen.style.flexGrow = 1;
            screen.style.paddingTop = 10; screen.style.paddingBottom = 8;
            screen.style.paddingLeft = 10; screen.style.paddingRight = 10;
            LcdHudTheme.ApplyScreen(screen);
            p.Add(screen);

            var rack = terminal.ConnectedRack;
            bool online = rack != null && rack.IsOnline;

            // Security Block gate (14.39.0, network-based 14.40.0): rechecked on
            // every rebuild, so a mid-session mode change locks this panel out.
            if (rack != null)
            {
                var denier = SecurityBlock.DenierForRack(
                    rack, VoxelEngine.Networking.NetworkSession.LocalPlayerId ?? "");
                if (denier != null)
                {
                    BuildDeniedScreen(screen, denier);
                    return p;
                }
            }

            // ── Header (inventory-style caption + title + pill) ──
            var caption = LcdHudTheme.CaptionLabel(terminal.isWireless ? "REMOTE SYSTEMS" : "MASS STORAGE");
            screen.Add(caption);

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.style.marginBottom = 6;
            var title = new Label(terminal.isWireless ? "WIRELESS TERMINAL" : "STORAGE TERMINAL");
            title.style.flexGrow = 1;
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.6f;
            title.style.color = new StyleColor(LcdHudTheme.Phosphor);
            titleRow.Add(title);

            var pill = new Label(online ? "ONLINE" : "NO LINK");
            pill.style.fontSize = 9;
            pill.style.unityFontStyleAndWeight = FontStyle.Bold;
            pill.style.letterSpacing = 1.1f;
            pill.style.color = new StyleColor(online ? LcdHudTheme.Phosphor : UITheme.AccentRed);
            pill.style.backgroundColor = new StyleColor(LcdHudTheme.GlassDark);
            pill.style.paddingLeft = 8; pill.style.paddingRight = 8;
            pill.style.paddingTop = 3; pill.style.paddingBottom = 3;
            UITheme.Radius(pill, 2);
            titleRow.Add(pill);
            screen.Add(titleRow);

            if (!online)
            {
                var why = rack == null
                    ? "No Server Controller linked. Run a Data Pipe to this terminal or place it touching the system."
                    : rack.HasConflict
                        ? "Controller conflict on this network - remove the extra Server Controller."
                        : "System power is down. Check the Power Stations and their PSUs.";
                var msg = new Label(why);
                msg.style.color = new StyleColor(LcdHudTheme.PhosphorDim);
                msg.style.fontSize = 11;
                msg.style.whiteSpace = WhiteSpace.Normal;
                msg.style.marginTop = 8;
                screen.Add(msg);
                LcdHudTheme.AddScanlines(screen, 7, 40f, 60f);
                return p;
            }

            // ── Data readout: live used / total with real prefixes ─
            var dataRow = new VisualElement();
            dataRow.style.flexDirection = FlexDirection.Row;
            dataRow.style.alignItems = Align.Center;
            dataRow.style.marginBottom = 3;
            var dataCaption = LcdHudTheme.CaptionLabel("DATA STORED");
            dataCaption.style.flexGrow = 1;
            dataRow.Add(dataCaption);
            var dataLabel = new Label(StorageUnits.FormatPair(rack.TotalStoredGb, rack.TotalCapacity));
            dataLabel.style.fontSize = 11;
            dataLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            dataLabel.style.color = new StyleColor(LcdHudTheme.Phosphor);
            dataRow.Add(dataLabel);
            screen.Add(dataRow);

            var segTrack = LcdHudTheme.CreateSegmentTrack(22, out var segs, height: 8f);
            segTrack.style.marginBottom = 6;
            screen.Add(segTrack);

            void RefreshStats()
            {
                if (rack == null) return;
                dataLabel.text = StorageUnits.FormatPair(rack.TotalStoredGb, rack.TotalCapacity);
                float f01 = rack.TotalCapacity > 0 ? rack.TotalStoredGb / rack.TotalCapacity : 0f;
                LcdHudTheme.AnimateSegments(segs, f01, UITheme.AccentCyan);
                pill.text = rack.IsOnline ? "ONLINE" : "NO LINK";
                pill.style.color = new StyleColor(rack.IsOnline ? LcdHudTheme.Phosphor : UITheme.AccentRed);
            }
            RefreshStats();

            // ── Search + Sort ─────────────────────────────────────
            var searchRow = new VisualElement();
            searchRow.style.flexDirection = FlexDirection.Row;
            searchRow.style.alignItems = Align.Center;
            searchRow.style.marginBottom = 6;

            var searchField = new TextField { value = "" };
            searchField.style.flexGrow = 1;
            searchField.style.minHeight = 26;
            LcdHudTheme.ApplySearchField(searchField);
            searchRow.Add(searchField);

            var sortBtn = new Button { text = "COUNT ↑" };
            sortBtn.style.minHeight = 26;
            sortBtn.style.minWidth = 92;
            sortBtn.style.marginLeft = 6;
            sortBtn.style.fontSize = 9;
            sortBtn.style.letterSpacing = 0.8f;
            sortBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            LcdHudTheme.ApplyCommandButton(sortBtn, LcdHudTheme.Phosphor);
            sortBtn.tooltip = "Click to cycle sort:\n• Count ↑ (default)\n• Count ↓\n• Name A→Z\n• Name Z→A";
            searchRow.Add(sortBtn);
            screen.Add(searchRow);

            // ── Slot grid (fills the screen) ──────────────────────
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(scroll);
            scroll.style.flexGrow = 1;
            screen.Add(scroll);

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.paddingTop = 4;
            grid.style.paddingBottom = 4;
            scroll.Add(grid);

            static bool IsShiftHeld()
            {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
                var kb = UnityEngine.InputSystem.Keyboard.current;
                return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
#else
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            }

            int sortMode = 0;
            void ApplySortLabel()
            {
                sortBtn.text = sortMode switch
                {
                    0 => "COUNT ↑", 1 => "COUNT ↓",
                    2 => "NAME A→Z", 3 => "NAME Z→A",
                    _ => "COUNT ↑"
                };
            }

            // Fingerprint of the last grid build, so the 1 s auto-refresh only
            // rebuilds when the network contents actually changed (keeps hover
            // and tooltips stable while idle).
            string lastFingerprint = null;

            void RebuildGrid(string filterQ, bool force)
            {
                var allItems = rack.GetAllItems();

                var fp = new System.Text.StringBuilder();
                foreach (var e in allItems) { fp.Append(e.itemId); fp.Append(':'); fp.Append(e.count); fp.Append('|'); }
                fp.Append(filterQ); fp.Append('#'); fp.Append(sortMode);
                string fingerprint = fp.ToString();
                if (!force && fingerprint == lastFingerprint) return;
                lastFingerprint = fingerprint;

                grid.Clear();

                switch (sortMode)
                {
                    case 0: allItems.Sort((a, b) => a.count.CompareTo(b.count)); break;
                    case 1: allItems.Sort((a, b) => b.count.CompareTo(a.count)); break;
                    case 2: allItems.Sort((a, b) => string.Compare(a.displayName, b.displayName, System.StringComparison.OrdinalIgnoreCase)); break;
                    case 3: allItems.Sort((a, b) => string.Compare(b.displayName, a.displayName, System.StringComparison.OrdinalIgnoreCase)); break;
                }

                string q = filterQ.Trim().ToLowerInvariant();

                foreach (var entry in allItems)
                {
                    if (!string.IsNullOrEmpty(q) &&
                        !entry.displayName.ToLowerInvariant().Contains(q))
                        continue;

                    var def = FindItemDef(entry.itemId);
                    int maxExtract = def != null ? ItemStack.MaxItemsPerStack(def) : ItemContainer.DefaultMaxItemsPerStack;

                    // ── Cell: LCD slot + name underneath ─────────
                    var cellWrap = new VisualElement();
                    cellWrap.style.width = 56;
                    cellWrap.style.marginRight = 5;
                    cellWrap.style.marginBottom = 6;
                    cellWrap.style.alignItems = Align.Center;

                    var cell = new VisualElement();
                    cell.style.width = 54; cell.style.height = 54;
                    cell.style.flexShrink = 0;
                    cell.style.alignItems = Align.Center;
                    cell.style.justifyContent = Justify.Center;
                    cell.style.backgroundColor = new StyleColor(LcdHudTheme.GlassDark);
                    UITheme.Radius(cell, 1);
                    cell.style.borderTopWidth = cell.style.borderBottomWidth =
                    cell.style.borderLeftWidth = cell.style.borderRightWidth = 1;
                    var bezelBorder = new StyleColor(new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.96f));
                    cell.style.borderTopColor = cell.style.borderBottomColor =
                    cell.style.borderLeftColor = cell.style.borderRightColor = bezelBorder;
                    cellWrap.Add(cell);

                    if (def != null && def.icon != null)
                    {
                        var img = new Image { sprite = def.icon };
                        img.scaleMode = ScaleMode.ScaleToFit;
                        img.style.width = 42; img.style.height = 42;
                        img.pickingMode = PickingMode.Ignore;
                        cell.Add(img);
                    }
                    else
                    {
                        var box = new VisualElement();
                        box.style.width = 34; box.style.height = 34;
                        box.style.backgroundColor = new StyleColor(def != null ? def.iconTint : UITheme.AccentCyan);
                        UITheme.Radius(box, 2);
                        box.pickingMode = PickingMode.Ignore;
                        cell.Add(box);
                    }

                    // Count in the BOTTOM-LEFT corner of the icon (14.42.1,
                    // user request) with the exact same chip styling the
                    // inventory's LCD slots use, so the terminal reads like
                    // every other container in the game.
                    var countLbl = new Label(FormatCount(entry.count));
                    countLbl.style.position = Position.Absolute;
                    countLbl.style.bottom = 2; countLbl.style.left = 4;
                    countLbl.style.fontSize = 10;
                    countLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                    countLbl.style.color = new StyleColor(LcdHudTheme.Phosphor);
                    countLbl.style.backgroundColor = new StyleColor(LcdHudTheme.GlassDark);
                    countLbl.style.paddingLeft = 3; countLbl.style.paddingRight = 3;
                    countLbl.style.paddingTop = 0; countLbl.style.paddingBottom = 0;
                    T.Radius(countLbl, 1f);
                    countLbl.pickingMode = PickingMode.Ignore;
                    cell.Add(countLbl);

                    // Name under the slot.
                    var nameLbl = new Label(entry.displayName);
                    nameLbl.style.color = new StyleColor(LcdHudTheme.PhosphorDim);
                    nameLbl.style.fontSize = 8;
                    nameLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                    nameLbl.style.overflow = Overflow.Hidden;
                    nameLbl.style.maxWidth = 56;
                    nameLbl.style.whiteSpace = WhiteSpace.NoWrap;
                    nameLbl.pickingMode = PickingMode.Ignore;
                    cellWrap.Add(nameLbl);

                    string capturedId = entry.itemId;
                    string capturedName = entry.displayName;
                    int capturedCount = entry.count;
                    float capturedMass = entry.massPerUnit <= 0f ? 1f : entry.massPerUnit;
                    UnityEngine.Sprite capturedIcon = def?.icon;
                    var capturedDef = def;

                    cell.RegisterCallback<ClickEvent>(evt =>
                    {
                        if (playerInv == null) return;
                        bool shift = IsShiftHeld();
                        int amount = shift ? maxExtract : 1;
                        int got = rack.NetworkExtract(capturedId, amount);
                        if (got > 0)
                        {
                            var itemDef = FindItemDef(capturedId);
                            if (itemDef != null)
                                playerInv.Add(itemDef, got);
                            BuildFeedbackHud.Show(capturedName, $"+{got}", capturedIcon, UITheme.AccentCyan);
                            RebuildGrid(searchField.value, force: true);
                            RefreshStats();
                        }
                    });

                    // 1-second hover tooltip: item card + data size + weight.
                    IVisualElementScheduledItem hoverTip = null;
                    cell.RegisterCallback<MouseEnterEvent>(_ =>
                    {
                        cell.style.backgroundColor = new StyleColor(LcdHudTheme.Glass);
                        hoverTip?.Pause();
                        hoverTip = cell.schedule.Execute(() =>
                        {
                            if (capturedDef == null) return;
                            var synthetic = new ItemStack { item = capturedDef, count = capturedCount };
                            string extra =
                                $"Data Size:  {StorageUnits.Format(capturedMass)} / unit\n" +
                                $"Stored:     {StorageUnits.Format(capturedMass * capturedCount)} in network";
                            VoxelEngine.UI.Tooltip.ShowStackAt(
                                synthetic,
                                new Vector2(cell.worldBound.xMax + 6f, cell.worldBound.yMin),
                                extra);
                        });
                        hoverTip.ExecuteLater(1000);
                    });
                    cell.RegisterCallback<MouseLeaveEvent>(_ =>
                    {
                        cell.style.backgroundColor = new StyleColor(LcdHudTheme.GlassDark);
                        hoverTip?.Pause();
                        VoxelEngine.UI.Tooltip.HideSticky();
                    });

                    grid.Add(cellWrap);
                }

                if (grid.childCount == 0)
                {
                    var empty = new Label(string.IsNullOrEmpty(q) ? "STORAGE EMPTY" : "NO MATCH");
                    empty.style.color = new StyleColor(LcdHudTheme.PhosphorDim);
                    empty.style.fontSize = 10;
                    empty.style.letterSpacing = 1.2f;
                    empty.style.marginTop = 10;
                    grid.Add(empty);
                }
            }

            sortBtn.clicked += () =>
            {
                sortMode = (sortMode + 1) % 4;
                ApplySortLabel();
                RebuildGrid(searchField.value, force: true);
            };
            searchField.RegisterValueChangedCallback(e => RebuildGrid(e.newValue, force: true));
            ApplySortLabel();
            RebuildGrid("", force: true);

            // Self-refresh: stats every second, grid only when contents changed.
            // This is what keeps the GB readout live while importers run.
            screen.schedule.Execute(() =>
            {
                if (rack == null) return;
                RefreshStats();
                RebuildGrid(searchField.value, force: false);
            }).Every(1000);

            // ── Hint bar ─────────────────────────────────────────
            var hint = new Label("CLICK TAKE 1 · SHIFT+CLICK TAKE STACK · SHIFT+CLICK INVENTORY ITEM = STORE");
            hint.style.color = new StyleColor(LcdHudTheme.Caption);
            hint.style.fontSize = 8;
            hint.style.letterSpacing = 0.8f;
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            hint.style.marginTop = 6;
            screen.Add(hint);

            LcdHudTheme.AddScanlines(screen, 9, 44f, 55f);
            return p;
        }

        // Helper: compact number format (1,234 → 1.2k  for large counts).

        private static VisualElement RecipeIconSlot(Sprite sprite, Color tint)
        {
            // Small premium slot with the recipe's output icon; tint chip fallback.
            var slot = new VisualElement();
            slot.style.width = 26; slot.style.height = 26;
            slot.style.marginRight = 7;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            slot.style.backgroundColor = new StyleColor(new Color(0.09f, 0.10f, 0.13f, 0.9f));
            T.Radius(slot, 4);
            slot.pickingMode = PickingMode.Ignore;
            if (sprite != null)
            {
                var sImg = new Image { sprite = sprite };
                sImg.scaleMode = ScaleMode.ScaleToFit;
                sImg.style.width = 22; sImg.style.height = 22;
                sImg.pickingMode = PickingMode.Ignore;
                slot.Add(sImg);
            }
            else
            {
                var chip = new VisualElement();
                chip.style.width = 14; chip.style.height = 14;
                chip.style.backgroundColor = new StyleColor(tint);
                T.Radius(chip, 3);
                chip.pickingMode = PickingMode.Ignore;
                slot.Add(chip);
            }
            return slot;
        }

        private static string FormatCount(int n)
        {
            if (n >= 1_000_000) return $"{n / 1_000_000.0:0.#}M";
            if (n >= 10_000)    return $"{n / 1_000.0:0.#}k";
            return n.ToString("N0");
        }

        // ════════════════════════════════════════════════════════════
        //                   PATTERN TERMINAL
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildPatternTerminalPanel(
            PatternTerminal terminal,
            VoxelEngine.Crafting.RecipeRegistry recipeRegistry,
            Inventory playerInv)
        {
            var p = T.MachinePanel();

            var rack   = terminal.ConnectedRack;
            bool online = rack != null && rack.IsOnline;
            var crafter = rack?.GetComponent<AutoCrafter>();

            // Security Block gate (14.39.0).
            var secDenied = SecurityDeniedPanel(rack, p);
            if (secDenied != null) return secDenied;

            var (hdr, _, _, _) = T.HeaderRow("📋 Pattern Terminal",
                online ? "ONLINE" : "NO RACK",
                online ? T.AccentPurple : T.AccentRed);
            p.Add(hdr);
            p.Add(HighTechTheme.ScanDivider(online ? T.AccentPurple : T.AccentRed));

            if (!online)
            {
                p.Add(T.Body("No server rack connected."));
                HighTechTheme.Frame(p, online ? T.AccentPurple : T.AccentRed);
                return p;
            }

            int patUsed  = crafter?.patterns.Count ?? 0;
            int patTotal = rack.PatternSlots;
            p.Add(T.StatRow("🧠", "Patterns", $"{patUsed} / {patTotal}", T.AccentPurple));
            var (patBar, _) = T.ProgressBar(patTotal > 0 ? (float)patUsed / patTotal : 0,
                T.AccentPurple, 6, true);
            p.Add(patBar);
            p.Add(T.Divider());

            // Active patterns list.
            if (crafter != null && crafter.patterns.Count > 0)
            {
                p.Add(T.Subtitle("Active Patterns"));
                foreach (var pat in crafter.patterns)
                {
                    if (pat?.recipe == null) continue;
                    var row = new VisualElement();
                    row.style.flexDirection   = FlexDirection.Row;
                    row.style.alignItems      = Align.Center;
                    row.style.marginBottom    = 3;
                    row.style.paddingTop      = 4; row.style.paddingBottom = 4;
                    row.style.paddingLeft     = 8; row.style.paddingRight  = 8;
                    row.style.backgroundColor = new StyleColor(T.BgCard);
                    T.Radius(row, 4);

                    var nameL = new Label(pat.recipe.GetName());
                    nameL.style.color    = new StyleColor(T.TextPrimary);
                    nameL.style.fontSize = 12;
                    nameL.style.flexGrow = 1;
                    row.Add(nameL);

                    var localPat = pat;
                    var removeBtn = T.SmallButton("✕", () =>
                    {
                        crafter.patterns.Remove(localPat);
                        GameUIController.Instance?.RefreshCurrentPanel();
                    }, T.AccentRed);
                    row.Add(removeBtn);
                    p.Add(row);
                }
                p.Add(T.Divider());
            }
            else
            {
                p.Add(T.Muted("No patterns set. Browse recipes below and click ADD."));
                p.Add(T.Spacer(4));
            }

            // Recipe browser — add pattern.
            p.Add(T.Subtitle("Add Pattern from Recipe"));

            var recipes = recipeRegistry != null ? recipeRegistry.recipes
                          : new System.Collections.Generic.List<VoxelEngine.Crafting.RecipeDefinition>();
            var scroll  = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(scroll);   // themed slim scrollbar
            scroll.style.maxHeight = 220;
            scroll.style.marginTop = 4;

            foreach (var recipe in recipes)
            {
                if (recipe?.outputItem == null) continue;
                var row = new VisualElement();
                row.style.flexDirection   = FlexDirection.Row;
                row.style.alignItems      = Align.Center;
                row.style.marginBottom    = 3;
                row.style.paddingLeft     = 6; row.style.paddingRight = 6;
                row.style.paddingTop      = 3; row.style.paddingBottom = 3;
                row.style.backgroundColor = new StyleColor(T.BgSlot);
                T.Radius(row, 3);

                var n = new Label($"{recipe.GetName()} ×{recipe.outputCount}");
                n.style.color    = new StyleColor(T.TextSecondary);
                n.style.fontSize = 11;
                n.style.flexGrow = 1;
                row.Add(n);

                bool alreadyAdded = crafter != null &&
                    crafter.patterns.Exists(pp => pp.recipe == recipe);
                if (alreadyAdded)
                {
                    var tag = T.SmallButton("✓", null, T.AccentTeal);
                    tag.SetEnabled(false);
                    row.Add(tag);
                }
                else
                {
                    var localR = recipe;
                    var addBtn = T.SmallButton("ADD", () =>
                    {
                        if (crafter != null && crafter.AddPattern(localR))
                        {
                            BuildFeedbackHud.Show("Pattern Added", localR.GetName(),
                                localR.GetIcon(), T.AccentPurple);
                            GameUIController.Instance?.RefreshCurrentPanel();
                        }
                        else
                        {
                            BuildFeedbackHud.Show("No RAM Slot", "Install more RAM",
                                null, T.AccentRed);
                        }
                    }, T.AccentPurple);
                    row.Add(addBtn);
                }
                scroll.Add(row);
            }
            p.Add(scroll);
            p.Add(T.Spacer(4));
            p.Add(T.Muted("Patterns let the auto-crafter produce items automatically."));
            HighTechTheme.Frame(p, online ? T.AccentPurple : T.AccentRed);
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                   CRAFTING TERMINAL
        // ════════════════════════════════════════════════════════════
        public static VisualElement CreateCraftingTerminalPanel(
            CraftingTerminal terminal,
            Inventory playerInv)
        {
            var p = T.MachinePanel();

            var rack    = terminal.ConnectedRack;
            var crafter = terminal.ConnectedCrafter;
            bool online = rack != null && rack.IsOnline;

            // Security Block gate (14.39.0).
            var secDenied = SecurityDeniedPanel(rack, p);
            if (secDenied != null) return secDenied;

            var (hdr, _, _, _) = T.HeaderRow("🔨 Crafting Terminal",
                online ? "ONLINE" : "NO RACK",
                online ? T.AccentCyan : T.AccentRed);
            p.Add(hdr);
            p.Add(HighTechTheme.ScanDivider(online ? T.AccentCyan : T.AccentRed));

            if (!online)
            {
                p.Add(T.Body("No server rack connected."));
                HighTechTheme.Frame(p, online ? T.AccentCyan : T.AccentRed);
                return p;
            }

            p.Add(T.StatRow("⚡", "Craft Speed", $"{rack.CraftSpeedMultiplier:0.0}x", T.AccentGold));
            p.Add(T.StatRow("📋", "Patterns Available",
                $"{crafter?.patterns.Count ?? 0}", T.AccentPurple));
            p.Add(T.Divider());

            // Craft queue.
            if (crafter != null && crafter.craftQueue.Count > 0)
            {
                p.Add(T.Subtitle("Crafting Queue"));
                foreach (var job in crafter.craftQueue)
                {
                    if (job?.recipe == null) continue;
                    var row = new VisualElement();
                    row.style.flexDirection   = FlexDirection.Row;
                    row.style.alignItems      = Align.Center;
                    row.style.marginBottom    = 4;
                    row.style.paddingLeft     = 8; row.style.paddingRight = 8;
                    row.style.paddingTop      = 4; row.style.paddingBottom = 4;
                    row.style.backgroundColor = new StyleColor(T.BgCard);
                    T.Radius(row, 4);

                    row.Add(RecipeIconSlot(job.recipe.GetIcon(),
                        job.recipe.outputItem != null ? job.recipe.outputItem.iconTint : T.TextMuted));

                    var n = new Label($"{job.recipe.GetName()} ×{job.count}");
                    n.style.color    = new StyleColor(T.TextPrimary);
                    n.style.fontSize = 12;
                    n.style.flexGrow = 1;
                    row.Add(n);

                    float pct = job.recipe.craftSeconds > 0
                        ? Mathf.Clamp01(1f - job.timeRemaining / job.recipe.craftSeconds)
                        : 1f;
                    var (b, _) = T.ProgressBar(pct, T.AccentCyan, 6);
                    b.style.minWidth = 80;
                    row.Add(b);
                    p.Add(row);
                }
                p.Add(T.Divider());
            }

            // Request craft from patterns.
            if (crafter != null && crafter.patterns.Count > 0)
            {
                p.Add(T.Subtitle("Available Patterns"));
                var scroll = new ScrollView(ScrollViewMode.Vertical);
                VoxelEngine.UI.UITheme.StyleScroller(scroll);   // themed slim scrollbar
                scroll.style.maxHeight = 220;
                scroll.style.marginTop = 4;

                foreach (var pat in crafter.patterns)
                {
                    if (pat?.recipe == null) continue;
                    var row = new VisualElement();
                    row.style.flexDirection   = FlexDirection.Row;
                    row.style.alignItems      = Align.Center;
                    row.style.marginBottom    = 3;
                    row.style.paddingLeft     = 8; row.style.paddingRight = 8;
                    row.style.paddingTop      = 4; row.style.paddingBottom = 4;
                    row.style.backgroundColor = new StyleColor(T.BgSlot);
                    T.Radius(row, 4);

                    row.Add(RecipeIconSlot(pat.recipe.GetIcon(),
                        pat.recipe.outputItem != null ? pat.recipe.outputItem.iconTint : T.TextMuted));

                    var n = new Label(pat.recipe.GetName());
                    n.style.color    = new StyleColor(T.TextSecondary);
                    n.style.fontSize = 12;
                    n.style.flexGrow = 1;
                    row.Add(n);

                    // Check ingredients in network.
                    bool canCraft = true;
                    if (pat.recipe.inputs != null)
                    {
                        foreach (var ing in pat.recipe.inputs)
                        {
                            if (ing.item == null) continue;
                            if (rack.NetworkCount(ing.item.itemId) < ing.count)
                            { canCraft = false; break; }
                        }
                    }

                    var localPat = pat;
                    var craftBtn = T.SmallButton("CRAFT", () =>
                    {
                        if (crafter.RequestCraft(localPat.recipe, 1))
                            BuildFeedbackHud.Show("Queued", localPat.recipe.GetName(),
                                localPat.recipe.GetIcon(), T.AccentCyan);
                        else
                            BuildFeedbackHud.Show("Cannot Craft",
                                "Missing ingredients or queue full", null, T.AccentRed);
                    }, canCraft ? T.AccentCyan : T.TextMuted);
                    craftBtn.SetEnabled(canCraft);
                    row.Add(craftBtn);
                    scroll.Add(row);
                }
                p.Add(scroll);
            }
            else
            {
                p.Add(T.Muted("No patterns set. Use the Pattern Terminal to add recipes."));
            }

            p.Add(T.Spacer(4));
            p.Add(T.Muted("Items are auto-crafted from storage and deposited back."));
            HighTechTheme.Frame(p, online ? T.AccentCyan : T.AccentRed);
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                   STORAGE IMPORTER
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildImporterPanel(
            StorageImporter importer,
            MachineUIs.SlotBuilder slotBuilder)
        {
            importer.EnsureContainers();
            var p = T.MachinePanel();

            bool online = importer.ConnectedRack != null && importer.ConnectedRack.IsOnline;
            var (hdr, _, _, _) = T.HeaderRow("📥 Storage Importer",
                online ? "IMPORTING" : "NO RACK",
                online ? T.AccentGreen : T.AccentRed);
            p.Add(hdr);
            p.Add(HighTechTheme.ScanDivider(online ? T.AccentGreen : T.AccentRed));

            p.Add(T.StatRow("⏱", "Interval",  $"{importer.CurrentInterval:0.00}s", T.TextSecondary));
            p.Add(T.StatRow("📦", "Stack Size", $"{importer.CurrentStackSize}", T.AccentCyan));
            p.Add(T.StatRow("🔍", "Filter Mode",
                importer.filterMode == FilterMode.Whitelist ? "Whitelist" : "Blacklist",
                T.AccentGold));
            p.Add(T.Divider());

            // Upgrade slots.
            p.Add(T.Subtitle("Upgrade Slots"));
            var upgradeGrid = T.SlotGrid();
            for (int i = 0; i < importer.upgradeSlots.Size; i++)
                upgradeGrid.Add(slotBuilder(importer.upgradeSlots, i,
                    importer.upgradeSlots.GetSlot(i), false, true));
            p.Add(upgradeGrid);
            p.Add(T.Spacer(8));

            // Filter list toggle.
            p.Add(T.Subtitle("Item Filter"));
            if (importer.filterItemIds.Count == 0)
            {
                p.Add(T.Muted("No filter — imports everything from adjacent chests."));
            }
            else
            {
                foreach (var id in importer.filterItemIds)
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems    = Align.Center;
                    row.style.marginBottom  = 2;

                    var lbl = new Label(id);
                    lbl.style.color    = new StyleColor(T.TextSecondary);
                    lbl.style.fontSize = 11;
                    lbl.style.flexGrow = 1;
                    row.Add(lbl);
                    p.Add(row);
                }
            }

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Place adjacent to a chest. Imports items into the storage network automatically."));
            HighTechTheme.Frame(p, online ? T.AccentGreen : T.AccentRed);
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                EXTERNAL STORAGE (14.41.0)
        //  Bridges physical chests and lone drawers into the network.
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildExternalStoragePanel(ExternalStorageBlock ext)
        {
            var p = T.MachinePanel();
            p.style.width = 430;

            // The bridge exposes the network's items - same security gate as
            // the NAS shelves and terminals.
            var secDenied = SecurityDeniedPanelFor(ext, p);
            if (secDenied != null) return secDenied;

            ext.RefreshLinks();
            bool linked = ext.ConnectedRack != null;
            bool online = linked && ext.ConnectedRack.IsOnline;
            bool bridging = online && ext.Targets.Count > 0;

            string status = !linked ? "NO CONTROLLER" :
                            !online ? "STANDBY" :
                            ext.Targets.Count == 0 ? "NO CONTAINER" : "BRIDGING";
            Color statusCol = bridging ? T.AccentGreen :
                              online   ? T.AccentOrange :
                              linked   ? T.AccentOrange : T.TextMuted;

            var (hdr, _, _, pillLbl) = T.HeaderRow("🔗 External Storage", status, statusCol);
            p.Add(hdr);
            p.Add(HighTechTheme.ScanDivider(statusCol));

            // Mode toggle: what the NETWORK may do with the bridged containers.
            p.Add(T.Subtitle("Network Access"));
            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.marginBottom = 4;

            var modeBtns = new List<Button>();
            void StyleModeButtons()
            {
                var modes = new[] { ExternalStorageMode.InsertAndExtract,
                                    ExternalStorageMode.ExtractOnly,
                                    ExternalStorageMode.InsertOnly };
                for (int i = 0; i < modeBtns.Count; i++)
                {
                    bool active = ext.mode == modes[i];
                    modeBtns[i].style.backgroundColor = new StyleColor(active ? T.AccentCyan : T.BgSlot);
                    modeBtns[i].style.color = active ? Color.black : (Color)T.TextSecondary;
                }
            }
            Button ModeBtn(string txt, ExternalStorageMode m, string tip)
            {
                var b = new Button(() =>
                {
                    ext.SetMode(m);
                    StyleModeButtons();
                    MarkDirtyForSync(ext);
                }) { text = txt, tooltip = tip };
                b.style.flexGrow = 1;
                b.style.minHeight = 24;
                b.style.fontSize = 9;
                b.style.unityFontStyleAndWeight = FontStyle.Bold;
                T.Radius(b, 4f);
                T.Border(b, 1, T.BorderDim);
                modeBtns.Add(b);
                return b;
            }
            modeRow.Add(ModeBtn("IN + OUT", ExternalStorageMode.InsertAndExtract,
                "The network stores into AND pulls from the bridged containers."));
            modeRow.Add(ModeBtn("EXTRACT", ExternalStorageMode.ExtractOnly,
                "The network only pulls items out - it never stores here."));
            modeRow.Add(ModeBtn("INSERT", ExternalStorageMode.InsertOnly,
                "The network only stores items here - it never pulls them back."));
            StyleModeButtons();
            p.Add(modeRow);

            // Priority stepper - ranks against NAS shelves and drawer banks.
            p.Add(PriorityRow("Network Priority", () => ext.priority, v =>
            {
                ext.SetPriority(v);
                MarkDirtyForSync(ext);
            }));
            p.Add(T.Divider());

            // Bridged containers - live list (bridges re-probe every second).
            p.Add(T.Subtitle("Bridged Containers"));
            var listHost = new VisualElement();
            p.Add(listHost);
            var slotsRow = T.StatRow("▦", "Slots In Use", "", T.AccentCyan);
            var slotsVal = slotsRow.Q<Label>("stat-value");
            p.Add(slotsRow);

            void RebuildList()
            {
                listHost.Clear();
                if (ext.Targets.Count == 0)
                {
                    listHost.Add(T.Muted("No container touching this block. Place it flush " +
                                         "against a Chest or a lone Storage Drawer."));
                }
                else
                {
                    for (int i = 0; i < ext.Targets.Count; i++)
                    {
                        var row = new VisualElement();
                        row.style.flexDirection = FlexDirection.Row;
                        row.style.alignItems = Align.Center;
                        row.style.marginBottom = 2;
                        row.style.paddingLeft = 4; row.style.paddingTop = 2;
                        row.style.paddingBottom = 2;
                        row.style.backgroundColor = new StyleColor(T.BgCard);
                        T.Radius(row, 4f);

                        var dot = new VisualElement();
                        dot.style.width = 6; dot.style.height = 6;
                        dot.style.marginRight = 6;
                        T.Radius(dot, 3f);
                        dot.style.backgroundColor = new StyleColor(bridging ? T.AccentGreen : T.AccentOrange);
                        row.Add(dot);

                        var nameLbl = new Label(i < ext.TargetNames.Count ? ext.TargetNames[i] : "Container");
                        nameLbl.style.fontSize = 10;
                        nameLbl.style.color = new StyleColor(T.TextSecondary);
                        row.Add(nameLbl);
                        listHost.Add(row);
                    }
                }
                var (used, total) = ext.SlotStats();
                if (slotsVal != null) slotsVal.text = total > 0 ? $"{used} / {total}" : "—";
            }
            RebuildList();

            // Live repaint - containers appear/disappear as blocks are placed.
            int lastCount = ext.Targets.Count;
            p.schedule.Execute(() =>
            {
                if (ext == null) return;
                bool liveOnline = ext.ConnectedRack != null && ext.ConnectedRack.IsOnline;
                bool liveBridging = liveOnline && ext.Targets.Count > 0;
                if (pillLbl != null)
                    pillLbl.text = ext.ConnectedRack == null ? "NO CONTROLLER" :
                                   !liveOnline ? "STANDBY" :
                                   ext.Targets.Count == 0 ? "NO CONTAINER" : "BRIDGING";
                if (ext.Targets.Count != lastCount) { lastCount = ext.Targets.Count; RebuildList(); }
                else { var (used, total) = ext.SlotStats(); if (slotsVal != null) slotsVal.text = total > 0 ? $"{used} / {total}" : "—"; }
            }).Every(700);

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Bridges physical containers into the network: terminals see and use " +
                          "their items directly. Drawer Controllers don't need this block - they " +
                          "join by Data Pipe or touch on their own. Draws " +
                          $"{ExternalStorageBlock.DRAW_WATTS:0} W from the system."));
            HighTechTheme.Frame(p, statusCol);
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                   STORAGE EXPORTER
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildExporterPanel(
            StorageExporter exporter,
            MachineUIs.SlotBuilder slotBuilder)
        {
            exporter.EnsureContainers();
            var p = T.MachinePanel();

            bool online = exporter.ConnectedRack != null && exporter.ConnectedRack.IsOnline;
            var (hdr, _, _, _) = T.HeaderRow("📤 Storage Exporter",
                online ? "EXPORTING" : "NO RACK",
                online ? T.AccentOrange : T.AccentRed);
            p.Add(hdr);
            p.Add(HighTechTheme.ScanDivider(online ? T.AccentOrange : T.AccentRed));

            p.Add(T.StatRow("⏱", "Interval",   $"{exporter.CurrentInterval:0.00}s",  T.TextSecondary));
            p.Add(T.StatRow("📦", "Stack Size",  $"{exporter.CurrentStackSize}",       T.AccentCyan));
            p.Add(T.StatRow("🔍", "Filter Mode",
                exporter.filterMode == FilterMode.Whitelist ? "Whitelist" : "Blacklist",
                T.AccentGold));
            p.Add(T.Divider());

            // Upgrade slots.
            p.Add(T.Subtitle("Upgrade Slots"));
            var upgradeGrid = T.SlotGrid();
            for (int i = 0; i < exporter.upgradeSlots.Size; i++)
                upgradeGrid.Add(slotBuilder(exporter.upgradeSlots, i,
                    exporter.upgradeSlots.GetSlot(i), false, true));
            p.Add(upgradeGrid);
            p.Add(T.Spacer(8));

            // Filter list.
            p.Add(T.Subtitle("Item Filter (Whitelist = only these items)"));
            if (exporter.filterItemIds.Count == 0)
            {
                p.Add(T.Muted("No filter set — won't export anything in Whitelist mode."));
            }
            else
            {
                foreach (var id in exporter.filterItemIds)
                {
                    var lbl = new Label("· " + id);
                    lbl.style.color    = new StyleColor(T.TextSecondary);
                    lbl.style.fontSize = 11;
                    p.Add(lbl);
                }
            }

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Place adjacent to a chest. Exports items from the storage network."));
            HighTechTheme.Frame(p, online ? T.AccentOrange : T.AccentRed);
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                   DISK MANIPULATOR
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildDiskManipulatorPanel(
            DiskManipulator manipulator,
            MachineUIs.SlotBuilder slotBuilder)
        {
            manipulator.EnsureContainers();
            var p = T.MachinePanel();

            var statusColor = manipulator.IsTransferring ? T.AccentCyan :
                              manipulator.StatusText.Contains("complete") ? T.AccentGreen :
                              manipulator.StatusText.Contains("full") ? T.AccentRed : T.TextMuted;

            var (hdr, _, _, _) = T.HeaderRow("💿 Disk Manipulator",
                manipulator.StatusText, statusColor);
            p.Add(hdr);
            p.Add(HighTechTheme.ScanDivider(statusColor));

            // Progress bar.
            if (manipulator.IsTransferring)
            {
                p.Add(T.StatRow("⏱", "Transfer Progress",
                    $"{manipulator.Progress01 * 100f:0}%", T.AccentCyan));
                var (b, _) = T.ProgressBar(manipulator.Progress01, T.AccentCyan, 10, false);
                p.Add(b);
                p.Add(T.Divider());
            }

            // Slots.
            var slotRow = new VisualElement();
            slotRow.style.flexDirection  = FlexDirection.Row;
            slotRow.style.justifyContent = Justify.SpaceAround;
            slotRow.style.marginTop      = 8;

            var srcGrid = T.SlotGrid();
            srcGrid.Add(slotBuilder(manipulator.sourceSlot, 0,
                manipulator.sourceSlot.GetSlot(0), false, true));
            slotRow.Add(T.SlotCard("SOURCE DISK", srcGrid));

            // Arrow.
            var arrow = new Label("→");
            arrow.style.fontSize     = 28;
            arrow.style.color        = new StyleColor(T.AccentCyan);
            arrow.style.alignSelf    = Align.Center;
            arrow.style.marginLeft   = 14;
            arrow.style.marginRight  = 14;
            arrow.pickingMode        = PickingMode.Ignore;
            slotRow.Add(arrow);

            var dstGrid = T.SlotGrid();
            dstGrid.Add(slotBuilder(manipulator.destSlot, 0,
                manipulator.destSlot.GetSlot(0), false, true));
            slotRow.Add(T.SlotCard("DEST DISK", dstGrid));
            p.Add(slotRow);

            // Disk info.
            p.Add(T.Spacer(12));
            var srcSlot = manipulator.sourceSlot.GetSlot(0);
            var dstSlot = manipulator.destSlot.GetSlot(0);

            if (!srcSlot.IsEmpty && srcSlot.item is StorageDisk srcDisk)
                p.Add(T.StatRow("📀", "Source Disk", $"{srcDisk.tier}  ·  {srcDisk.MaxItems:N0} items", T.AccentCyan));
            if (!dstSlot.IsEmpty && dstSlot.item is StorageDisk dstDisk)
                p.Add(T.StatRow("💿", "Dest Disk", $"{dstDisk.tier}  ·  {dstDisk.MaxItems:N0} items", T.AccentGold));

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Insert source and destination disks. Items transfer automatically. " +
                          "Disks remember their contents when removed."));
            HighTechTheme.Frame(p, statusColor);
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                      NAS BLOCK
        //  14.40.0: real NAS front-panel look - 8 vertical drive bays,
        //  each with its disk slot, activity LED, tier label and a fill
        //  bar (green 0-70% · yellow 71-90% · red 91-100%), plus the
        //  per-shelf network priority stepper.
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildNASPanel(
            NASBlock nas,
            MachineUIs.SlotBuilder slotBuilder)
        {
            nas.EnsureContainers();
            var p = T.MachinePanel();
            p.style.width = 460;

            // Security Block gate: the NAS shelves hold the network's disks.
            var secDenied = SecurityDeniedPanelFor(nas, p);
            if (secDenied != null) return secDenied;

            var controller = StorageNetwork.ControllerOf(nas);
            bool linked = controller != null;
            bool online = linked && controller.IsOnline;

            string status = online ? "ONLINE" : linked ? "STANDBY" : "NO CONTROLLER";
            Color statusCol = online ? T.AccentGreen : linked ? T.AccentOrange : T.TextMuted;

            var (hdr, _, _, _) = T.HeaderRow("🗄 NAS", status, statusCol);
            p.Add(hdr);
            p.Add(HighTechTheme.ScanDivider(statusCol));

            // Totals - live-updated below while the panel is open (14.41.0).
            var dataRow = T.StatRow("💾", "Shelf Data",
                StorageUnits.FormatPair(nas.TotalStoredGb, nas.TotalCapacity), T.AccentCyan);
            var dataVal = dataRow.Q<Label>("stat-value");
            p.Add(dataRow);
            var drawRow = T.StatRow("⚡", "Draw", $"{nas.DrawWatts:0} W", T.AccentGold);
            var drawVal = drawRow.Q<Label>("stat-value");
            p.Add(drawRow);

            // Priority stepper - higher priority shelves fill first.
            p.Add(PriorityRow("Network Priority", () => nas.priority, v =>
            {
                nas.SetPriority(v);
                MarkDirtyForSync(nas);
            }));
            p.Add(T.Divider());

            // ── Drive bays (the NAS front) ────────────────────────
            // 14.41.0: every bay keeps live refs to its LED, label, fill bar and
            // percent readout, and a scheduled updater repaints them twice a
            // second - inserting a disk or watching items stream in no longer
            // requires closing and reopening the screen.
            var bayUpdaters = new List<System.Action>();
            for (int i = 0; i < NASBlock.BAYS; i++)
            {
                int bay = i;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 3;
                row.style.paddingTop = 2; row.style.paddingBottom = 2;
                row.style.paddingLeft = 4; row.style.paddingRight = 6;
                row.style.backgroundColor = new StyleColor(T.BgCard);
                T.Radius(row, 5f);
                T.Border(row, 1, T.BorderDim);

                // Disk slot (insert/remove disks directly in the bay).
                row.Add(slotBuilder(nas.diskSlots, bay, nas.diskSlots.GetSlot(bay), false, true));

                // Activity LED.
                var led = new VisualElement();
                led.style.width = 7; led.style.height = 7;
                led.style.marginLeft = 6; led.style.marginRight = 7;
                led.style.flexShrink = 0;
                T.Radius(led, 4f);
                row.Add(led);

                // Bay label: tier + fill bar.
                var info = new VisualElement();
                info.style.flexGrow = 1;
                info.style.minWidth = 0;
                var tierLbl = new Label();
                tierLbl.style.fontSize = 9;
                tierLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                tierLbl.style.overflow = Overflow.Hidden;
                tierLbl.style.whiteSpace = WhiteSpace.NoWrap;
                info.Add(tierLbl);

                var track = new VisualElement();
                track.style.height = 7;
                track.style.marginTop = 3;
                track.style.backgroundColor = new StyleColor(new Color(0.06f, 0.07f, 0.09f, 1f));
                T.Radius(track, 3f);
                info.Add(track);

                var barFill = new VisualElement();
                barFill.style.height = new StyleLength(new Length(100f, LengthUnit.Percent));
                barFill.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));
                T.Radius(barFill, 3f);
                track.Add(barFill);
                row.Add(info);

                // Percent label.
                var pct = new Label();
                pct.style.width = 34;
                pct.style.fontSize = 9;
                pct.style.unityFontStyleAndWeight = FontStyle.Bold;
                pct.style.unityTextAlign = TextAnchor.MiddleRight;
                row.Add(pct);

                void UpdateBay()
                {
                    float fill = nas.BayFill01(bay);
                    bool hasDisk = fill >= 0f;
                    bool liveOnline = controller != null && controller.IsOnline;

                    led.style.backgroundColor = new StyleColor(
                        !hasDisk   ? new Color(0.18f, 0.20f, 0.22f, 1f) :
                        liveOnline ? T.AccentGreen : T.AccentOrange);

                    var slotStack = nas.diskSlots.GetSlot(bay);
                    string tierTxt = "EMPTY BAY";
                    string capTxt = "Insert a Storage Disk";
                    if (hasDisk && slotStack.item is StorageDisk sd)
                    {
                        var data = bay < nas.activeDisks.Count ? nas.activeDisks[bay] : null;
                        tierTxt = sd.displayName;
                        capTxt = data != null
                            ? StorageUnits.FormatPair(data.UsedGigabytes, data.Capacity)
                            : StorageUnits.Format(sd.MaxGigabytes);
                    }
                    tierLbl.text = $"BAY {bay + 1}  ·  {tierTxt}";
                    tierLbl.style.color = new StyleColor(hasDisk ? T.TextSecondary : T.TextMuted);

                    Color barCol = fill > 0.90f ? T.AccentRed :
                                   fill > 0.70f ? T.AccentOrange : T.AccentGreen;
                    barFill.style.width = new StyleLength(
                        new Length(hasDisk ? Mathf.Clamp01(fill) * 100f : 0f, LengthUnit.Percent));
                    barFill.style.backgroundColor = new StyleColor(barCol);

                    pct.text = hasDisk ? $"{fill * 100f:0}%" : "";
                    pct.style.color = new StyleColor(!hasDisk ? T.TextMuted : barCol);

                    // Capacity readout under the tier name is too cramped; show
                    // it as a tooltip on the whole bay row instead.
                    row.tooltip = capTxt;
                }
                UpdateBay();
                bayUpdaters.Add(UpdateBay);

                p.Add(row);
            }

            // Live repaint: bays + totals, every 500 ms while the panel lives.
            p.schedule.Execute(() =>
            {
                if (nas == null) return;
                foreach (var u in bayUpdaters) u();
                if (dataVal != null)
                    dataVal.text = StorageUnits.FormatPair(nas.TotalStoredGb, nas.TotalCapacity);
                if (drawVal != null)
                    drawVal.text = $"{nas.DrawWatts:0} W";
            }).Every(500);

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Link to a Server Controller with Data Pipes or by touching blocks. " +
                          "Higher-priority shelves fill first. Disks remember their contents."));
            HighTechTheme.Frame(p, statusCol);
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                   SERVER CONTROLLER
        //  14.40.0: the rack is now the network's brain - RAM + CPU
        //  only. Disks live in NAS shelves, PSUs in Power Stations.
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildServerPanel(
            ServerRack rack,
            MachineUIs.SlotBuilder slotBuilder)
        {
            rack.EnsureContainers();
            var p = T.MachinePanel();
            p.style.width = 480;

            // Security Block gate.
            var secDenied = SecurityDeniedPanelFor(rack, p);
            if (secDenied != null) return secDenied;

            string status = rack.HasConflict   ? "CONTROLLER CONFLICT" :
                            rack.IsPowerShort  ? "POWER SHORT" :
                            rack.IsOnline      ? "ONLINE" : "NO POWER STATION";
            Color statusCol = rack.HasConflict  ? T.AccentRed :
                              rack.IsPowerShort ? T.AccentRed :
                              rack.IsOnline     ? T.AccentGreen : T.TextMuted;

            var (hdr, _, _, _) = T.HeaderRow("🖥 Server Controller", status, statusCol);
            p.Add(hdr);
            p.Add(HighTechTheme.ScanDivider(statusCol));

            if (rack.HasConflict)
            {
                var warn = T.StatLabel("⚠ Two Server Controllers share this network. Remove one.", T.AccentRed);
                warn.style.marginBottom = 4;
                p.Add(warn);
            }

            // ── Power budget ──────────────────────────────────────
            float draw = rack.SystemDrawWatts;
            float delivered = rack.DeliveredWatts;
            float rating = rack.StationRatingWatts;
            float powerFill = rating > 0 ? Mathf.Clamp01(draw / rating) : (draw > 0 ? 1f : 0f);
            Color pwrColor = rack.IsPowerShort ? T.AccentRed :
                             powerFill > 0.85f ? T.AccentOrange : T.AccentGold;

            p.Add(T.StatRow("⚡", "System Draw", $"{draw:0} W", pwrColor));
            p.Add(T.StatRow("🔌", "Delivered", $"{delivered:0} W of {rating:0} W PSU rating", pwrColor));
            var (pwrBar, _) = T.ProgressBar(powerFill, pwrColor, 8, false);
            p.Add(pwrBar);

            if (rack.IsPowerShort)
            {
                var warn = T.StatLabel("⚠ Power short - add PSUs to a Power Station or feed the stations more grid power.", T.AccentRed);
                warn.style.marginTop = 4;
                p.Add(warn);
            }
            else if (rack.StationCount == 0)
            {
                var warn = T.StatLabel("⚠ No Power Station on this network - the system is dark.", T.AccentOrange);
                warn.style.marginTop = 4;
                p.Add(warn);
            }
            p.Add(T.Divider());

            // ── Data + crafting stats ─────────────────────────────
            p.Add(T.StatRow("💾", "Network Data",
                StorageUnits.FormatPair(rack.TotalStoredGb, rack.TotalCapacity), T.AccentCyan));
            var (bar, _) = T.ProgressBar(
                rack.TotalCapacity > 0 ? rack.TotalStoredGb / rack.TotalCapacity : 0f,
                T.AccentCyan, 8, true);
            p.Add(bar);
            p.Add(T.StatRow("🧠", "Patterns", $"{rack.PatternSlots} slots", T.TextSecondary));
            p.Add(T.StatRow("⚙", "Craft Speed", $"{rack.CraftSpeedMultiplier:0.0}x", T.AccentGold));
            p.Add(T.Divider());

            // ── Hardware: RAM ×4 + CPU ────────────────────────────
            p.Add(T.Subtitle("Hardware"));
            var hwRow = new VisualElement();
            hwRow.style.flexDirection = FlexDirection.Row;
            hwRow.style.justifyContent = Justify.Center;

            // 14.41.0: the RAM grid must never wrap - 4 modules sit in ONE row
            // inside their card (the wrap default pushed the 4th slot out of
            // the box on narrower layouts).
            var ramGrid = T.SlotGrid();
            ramGrid.style.flexWrap = Wrap.NoWrap;
            ramGrid.style.flexShrink = 0;
            for (int i = 0; i < rack.ramSlots.Size; i++)
                ramGrid.Add(slotBuilder(rack.ramSlots, i, rack.ramSlots.GetSlot(i), false, true));
            hwRow.Add(T.SlotCard("RAM", ramGrid));
            hwRow.Add(T.Spacer(6));

            var cpuGrid = T.SlotGrid();
            cpuGrid.Add(slotBuilder(rack.cpuSlot, 0, rack.cpuSlot.GetSlot(0), false, true));
            hwRow.Add(T.SlotCard("CPU", cpuGrid));
            p.Add(hwRow);
            p.Add(T.Divider());

            // ── Network members ───────────────────────────────────
            p.Add(T.Subtitle("Network"));
            p.Add(T.StatRow("🗄", "NAS Shelves", rack.NasCount.ToString(), rack.NasCount > 0 ? T.AccentCyan : T.TextMuted));
            p.Add(T.StatRow("🔌", "Power Stations", rack.StationCount.ToString(), rack.StationCount > 0 ? T.AccentGold : T.AccentRed));
            p.Add(T.StatRow("🖥", "Terminals", rack.TerminalCount.ToString(), T.TextSecondary));
            p.Add(T.StatRow("▤", "Drawer Controllers", rack.DrawerCtrlCount.ToString(), T.TextSecondary));
            p.Add(T.StatRow("🔗", "External Storage", rack.ExternalCount.ToString(), T.TextSecondary));
            p.Add(T.StatRow("📡", "Wireless Transmitters", rack.TransmitterCount.ToString(), T.TextSecondary));
            p.Add(T.StatRow("🔒", "Security Blocks", rack.SecurityCount.ToString(),
                rack.SecurityCount > 0 ? T.AccentRed : T.TextMuted));

            p.Add(T.Spacer(4));
            p.Add(T.Muted("Blocks join the network through Data Pipes or by touching each other. " +
                          "RAM slots accept only RAM, the CPU slot only CPUs. Disks go in NAS shelves, PSUs in Power Stations."));
            HighTechTheme.Frame(p, statusCol);
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                     STORAGE DRAWER
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildDrawerPanel(StorageDrawer drawer, MachineUIs.SlotBuilder slotBuilder)
        {
            drawer.EnsureContainers();
            var p = T.MachinePanel();
            p.style.width = 500;
            bool hasItem = drawer.storedItem != null && drawer.storedCount > 0;
            var (hdr, _, _, _) = T.HeaderRow("▣ Storage Drawer", hasItem ? drawer.storedItem.displayName : "EMPTY",
                hasItem ? T.AccentTeal : T.TextMuted);
            p.Add(hdr);
            p.Add(HighTechTheme.ScanDivider(hasItem ? T.AccentTeal : T.TextMuted));
            p.Add(T.StatRow("📦", "Stored", $"{drawer.storedCount:N0} / {drawer.Capacity:N0}", T.AccentCyan));
            p.Add(T.StatRow("⇈", "Stack Limit", $"{drawer.StackMultiplier}x", T.AccentGold));
            p.Add(T.StatRow("🕳", "Overflow", drawer.HasVoidUpgrade ? "VOID" : "BLOCK", drawer.HasVoidUpgrade ? T.AccentPurple : T.TextMuted));
            var (bar, _) = T.ProgressBar(drawer.Capacity > 0 ? drawer.storedCount / (float)drawer.Capacity : 0f, T.AccentTeal, 8, true);
            p.Add(bar);
            p.Add(T.Divider());

            p.Add(T.Subtitle("Stored Item"));
            var itemGrid = T.SlotGrid();
            itemGrid.Add(slotBuilder(drawer, 0, drawer.GetSlot(0), false, true));
            p.Add(itemGrid);
            p.Add(T.Spacer(8));

            p.Add(T.Subtitle("Upgrade Slots (12)"));
            var upgradeGrid = T.SlotGrid();
            for (int i = 0; i < drawer.upgradeSlots.Size; i++)
                upgradeGrid.Add(slotBuilder(drawer.upgradeSlots, i, drawer.upgradeSlots.GetSlot(i), false, true));
            p.Add(upgradeGrid);
            p.Add(T.Divider());
            p.Add(T.Muted("LMB front = take 1 · Shift+LMB = take full stack · RMB with item = insert hand stack · Shift+RMB = insert all matching items. Break from sides/back."));
            HighTechTheme.Frame(p, hasItem ? T.AccentTeal : T.TextMuted);
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                  DRAWER CONTROLLER
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildDrawerControllerPanel(StorageDrawerController controller)
        {
            controller.RefreshLinks();
            var p = T.MachinePanel();
            p.style.width = 500;
            bool online = controller.ConnectedRack != null && controller.ConnectedRack.IsOnline;
            var (hdr, _, _, _) = T.HeaderRow("▤ Drawer Controller", online ? "LINKED" : "NO RACK",
                online ? T.AccentGreen : T.AccentRed);
            p.Add(hdr);
            p.Add(HighTechTheme.ScanDivider(online ? T.AccentGreen : T.AccentRed));
            p.Add(T.StatRow("🖥", "Server Controller", controller.ConnectedRack != null ? "Linked" : "None", online ? T.AccentGreen : T.TextMuted));
            p.Add(T.StatRow("▣", "Drawers", controller.Drawers.Count.ToString(), T.AccentCyan));
            p.Add(T.StatRow("📡", "Drawer Radius", $"{controller.drawerRadius:0} m", T.TextSecondary));
            p.Add(PriorityRow("Network Priority", () => controller.priority, v =>
            {
                controller.priority = Mathf.Clamp(v, -99, 999);
                MarkDirtyForSync(controller);
            }));
            p.Add(T.Divider());
            p.Add(T.Subtitle("Controller Item Storage"));
            var summary = controller.BuildItemSummary();
            if (summary.Count == 0)
            {
                p.Add(T.Muted("No stored items in linked drawers."));
            }
            else
            {
                var scroll = new ScrollView(ScrollViewMode.Vertical);
                VoxelEngine.UI.UITheme.StyleScroller(scroll);   // themed slim scrollbar
                scroll.style.maxHeight = 180;
                foreach (var entry in summary.Values)
                {
                    var item = FindItemDef(entry.itemId);
                    scroll.Add(T.StatRow("📦", entry.displayName, entry.count.ToString("N0"), item != null ? item.iconTint : T.AccentCyan));
                }
                p.Add(scroll);
            }

            p.Add(T.Divider());
            p.Add(T.Subtitle("Linked Drawers"));
            if (controller.Drawers.Count == 0)
            {
                p.Add(T.Muted("No drawers in range."));
            }
            else
            {
                foreach (var drawer in controller.Drawers)
                {
                    if (drawer == null) continue;
                    string name = drawer.storedItem != null ? drawer.storedItem.displayName : "Empty Drawer";
                    p.Add(T.StatRow("▣", name, $"{drawer.storedCount:N0}/{drawer.Capacity:N0}", drawer.storedItem != null ? T.AccentCyan : T.TextMuted));
                }
            }
            p.Add(T.Spacer(6));
            p.Add(T.Muted("RMB the controller with an item to import it into linked drawers. Shift+RMB imports every matching stack. Item pipes import/export through controller item ports."));
            HighTechTheme.Frame(p, online ? T.AccentGreen : T.AccentRed);
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                  STORAGE ITEM DISPLAY
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildItemDisplayPanel(StorageItemDisplayBlock display, MachineUIs.SlotBuilder slotBuilder)
        {
            var p = T.MachinePanel();
            p.style.width = 460;
            bool online = display.ConnectedRack != null && display.ConnectedRack.IsOnline;
            var (hdr, _, _, _) = T.HeaderRow("◫ Item Display", online ? "ONLINE" : "NO RACK", online ? T.AccentCyan : T.AccentRed);
            p.Add(hdr);
            p.Add(HighTechTheme.ScanDivider(online ? T.AccentCyan : T.AccentRed));
            p.Add(T.StatRow("🔎", "Filter", display.filterItem != null ? display.filterItem.displayName : "None", display.filterItem != null ? T.AccentGold : T.TextMuted));
            p.Add(T.StatRow("#", "System Amount", display.filterItem != null ? display.CurrentCount.ToString("N0") : "—", T.AccentCyan));
            p.Add(T.Divider());
            p.Add(T.Subtitle("Drag Item Filter"));
            var grid = T.SlotGrid();
            grid.Add(slotBuilder(display.FilterSlot, 0, display.FilterSlot.GetSlot(0), false, true));
            p.Add(grid);
            p.Add(T.Spacer(8));

            p.Add(T.Subtitle("Search Item"));
            var search = new TextField { value = "" };
            search.style.minHeight = 26;
            p.Add(search);
            var results = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(results);   // themed slim scrollbar
            results.style.maxHeight = 180;
            results.style.marginTop = 6;
            p.Add(results);

            void Rebuild(string q)
            {
                results.Clear();
                q = (q ?? string.Empty).Trim().ToLowerInvariant();
                if (q.Length < 2) { results.Add(T.Muted("Type at least 2 letters to search.")); return; }
                int shown = 0;
                foreach (var item in Resources.FindObjectsOfTypeAll<ItemDefinition>())
                {
                    if (item == null || string.IsNullOrEmpty(item.displayName)) continue;
                    if (!item.displayName.ToLowerInvariant().Contains(q) && !item.itemId.ToLowerInvariant().Contains(q)) continue;
                    var local = item;
                    var btn = T.SmallButton(local.displayName, () =>
                    {
                        display.SetFilter(local);
                        GameUIController.Instance?.RefreshCurrentPanel();
                    }, local.iconTint);
                    btn.style.marginBottom = 3;
                    results.Add(btn);
                    if (++shown >= 24) break;
                }
                if (shown == 0) results.Add(T.Muted("No matching item."));
            }
            search.RegisterValueChangedCallback(e => Rebuild(e.newValue));
            Rebuild("");
            p.Add(T.Spacer(6));
            p.Add(T.Muted("Shows a configured item icon and its total amount across the connected storage system."));
            HighTechTheme.Frame(p, online ? T.AccentCyan : T.AccentRed);
            return p;
        }

        // ── Helpers ────────────────────────────────────────────────
        internal static ItemDefinition FindItemDef(string id)
        {
            var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            foreach (var it in all) if (it.itemId == id) return it;
            return null;
        }

        // ════════════════════════════════════════════════════════════
        //              SECURITY BLOCK GATE (14.39.0)
        //  Every panel that exposes the digital storage network calls
        //  one of these first. Returning a non-null element replaces
        //  the whole panel with an ACCESS DENIED card. Rebuilds re-run
        //  the check, so a live mode change locks open panels out too.
        // ════════════════════════════════════════════════════════════

        // ════════════════════════════════════════════════════════════
        //          SHARED: priority stepper + multiplayer dirty mark
        // ════════════════════════════════════════════════════════════

        /// <summary>[-10][-1] value [+1][+10] stepper row for per-block
        /// network priority (higher = filled/drained first).</summary>
        private static VisualElement PriorityRow(
            string label, System.Func<int> get, System.Action<int> set)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 4; row.style.marginBottom = 2;

            var lbl = new Label(label);
            lbl.style.flexGrow = 1;
            lbl.style.fontSize = 10;
            lbl.style.color = new StyleColor(T.TextSecondary);
            row.Add(lbl);

            var valueLbl = new Label(get().ToString());
            valueLbl.style.minWidth = 36;
            valueLbl.style.fontSize = 11;
            valueLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            valueLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            valueLbl.style.color = new StyleColor(T.AccentCyan);

            Button Step(string txt, int delta)
            {
                var b = new Button(() =>
                {
                    set(get() + delta);
                    valueLbl.text = get().ToString();
                }) { text = txt };
                b.style.minWidth = 30; b.style.minHeight = 22;
                b.style.fontSize = 9;
                b.style.unityFontStyleAndWeight = FontStyle.Bold;
                b.style.color = Color.white;
                b.style.backgroundColor = new StyleColor(T.BgSlot);
                T.Radius(b, 4f);
                T.Border(b, 1, T.BorderDim);
                return b;
            }

            row.Add(Step("-10", -10));
            row.Add(Step("-1", -1));
            row.Add(valueLbl);
            row.Add(Step("+1", +1));
            row.Add(Step("+10", +10));
            row.tooltip = "Higher priority shelves are filled and drained first.";
            return row;
        }

        /// <summary>Flag the block's container state as locally touched so
        /// ContainerSync uploads it to the host (multiplayer).</summary>
        private static void MarkDirtyForSync(Component device)
        {
            if (device == null) return;
            var pb = device.GetComponentInParent<VoxelEngine.Building.PlacedBlock>();
            if (pb != null)
                VoxelEngine.Networking.ContainerSync.NotifyLocalInteraction(pb);
        }

        /// <summary>Gate for terminal panels: checks the connected network's
        /// Security Blocks. No controller = nothing to guard.</summary>
        private static VisualElement SecurityDeniedPanel(ServerRack rack, VisualElement p)
        {
            if (rack == null) return null;
            var denier = SecurityBlock.DenierForRack(
                rack, VoxelEngine.Networking.NetworkSession.LocalPlayerId ?? "");
            if (denier == null) return null;
            return FillDeniedPanel(p, denier);
        }

        /// <summary>Gate for data hardware panels (controller, NAS, station):
        /// resolves the device's own network and checks its Security Blocks.
        /// (14.40.0: replaced the old radius check with network membership.)</summary>
        private static VisualElement SecurityDeniedPanelFor(Component device, VisualElement p)
        {
            if (device == null) return null;
            var rack = device as ServerRack ?? StorageNetwork.ControllerOf(device);
            return SecurityDeniedPanel(rack, p);
        }

        private static VisualElement FillDeniedPanel(VisualElement p, SecurityBlock denier)
        {
            var (hdr, _, _, _) = T.HeaderRow("🔒 Access Denied", denier.ModeLabel(), T.AccentRed);
            p.Add(hdr);
            p.Add(HighTechTheme.ScanDivider(T.AccentRed));
            p.Add(T.Body("A Security Block guards this storage network."));
            p.Add(T.Spacer(8));
            p.Add(T.Muted("Access is restricted to " +
                (denier.Mode == StorageAccessMode.Private ? "the owner." : "the owner's team.")));
            p.Add(T.Spacer(4));
            p.Add(T.Muted("There is no hacking: destroy the Security Block or cut its power to get in."));
            HighTechTheme.Frame(p, T.AccentRed);
            return p;
        }

        /// <summary>LCD-styled denial content for the fullscreen terminal.</summary>
        private static void BuildDeniedScreen(VisualElement screen, SecurityBlock denier)
        {
            screen.Add(LcdHudTheme.CaptionLabel("SECURITY"));

            var title = new Label("ACCESS DENIED");
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.6f;
            title.style.color = new StyleColor(T.AccentRed);
            title.style.marginBottom = 8;
            screen.Add(title);

            var body = new Label(
                "A Security Block guards this storage network.\n\n" +
                "Access is restricted to " +
                (denier.Mode == StorageAccessMode.Private ? "the owner." : "the owner's team.") +
                "\n\nThere is no hacking: destroy the Security Block or cut its power to get in.");
            body.style.color = new StyleColor(LcdHudTheme.PhosphorDim);
            body.style.fontSize = 11;
            body.style.whiteSpace = WhiteSpace.Normal;
            screen.Add(body);

            LcdHudTheme.AddScanlines(screen, 7, 40f, 60f);
        }
    }
}
