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
            // Wider panel so the slot grid has room.
            var p = T.MachinePanel();
            p.style.width = 530;

            var rack   = terminal.ConnectedRack;
            bool online = rack != null && rack.IsOnline;

            // ── Header ────────────────────────────────────────────
            var (hdr, _, _, _) = T.HeaderRow(
                terminal.isWireless ? "📡 Wireless Terminal" : "💾 Storage Terminal",
                online ? "ONLINE" : "NO RACK",
                online ? T.AccentGreen : T.AccentRed);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            if (!online)
            {
                p.Add(T.Body("No server rack connected or rack is offline."));
                p.Add(T.Spacer(8));
                p.Add(T.Muted("Place a Server Rack nearby with a PSU and connect power."));
                return p;
            }

            // ── Storage fill bar ──────────────────────────────────
            p.Add(T.StatRow("💾", "Storage",
                $"{rack.TotalStored:N0} / {rack.TotalCapacity:N0} GB", T.AccentCyan));
            // Animated phosphor segment track — the LCD "good feel" fill.
            var segTrack = LcdHudTheme.CreateSegmentTrack(14, out var segs, height: 9f);
            segTrack.style.marginTop = 2;
            p.Add(segTrack);
            LcdHudTheme.AnimateSegments(segs,
                rack.TotalCapacity > 0 ? (float)rack.TotalStored / rack.TotalCapacity : 0f,
                T.AccentCyan);
            p.Add(T.Spacer(4));
            p.Add(T.Muted("Matter conversion: each stored unit is encoded as stable matter data. Heavier items consume more GB."));
            p.Add(T.Spacer(6));

            // ── Search + Sort ─────────────────────────────────────
            var searchRow = new VisualElement();
            searchRow.style.flexDirection = FlexDirection.Row;
            searchRow.style.alignItems    = Align.Center;
            searchRow.style.marginBottom  = 6;

            var searchIco = new Label("⚲");
            searchIco.style.fontSize    = 13;
            searchIco.style.color       = new StyleColor(T.TextMuted);
            searchIco.style.marginRight = 5;
            searchIco.pickingMode = PickingMode.Ignore;
            searchRow.Add(searchIco);

            var searchField = new TextField { value = "" };
            searchField.style.flexGrow  = 1;
            searchField.style.minHeight = 26;
            LcdHudTheme.ApplySearchField(searchField);
            searchRow.Add(searchField);

            // Sort selector — two modes × ascending/descending. Default = item
            // count ascending (smallest stacks first) per the new QoL spec.
            var sortBtn = new Button { text = "Count ↑" };
            sortBtn.style.minHeight     = 26;
            sortBtn.style.minWidth      = 90;
            sortBtn.style.marginLeft    = 6;
            sortBtn.style.fontSize      = 10;
            sortBtn.style.color         = Color.white;
            sortBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            sortBtn.style.backgroundColor = new StyleColor(T.BgSlot);
            T.Radius(sortBtn, 5f);
            T.Border(sortBtn, 1, T.BorderDim);
            LcdHudTheme.ApplyCommandButton(sortBtn, T.AccentCyan);
            sortBtn.tooltip = "Click to cycle sort:\n• Count ↑ (default)\n• Count ↓\n• Name A→Z\n• Name Z→A";
            searchRow.Add(sortBtn);
            p.Add(searchRow);

            // ── Slot grid (scrollable) ────────────────────────────
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(scroll);   // themed slim scrollbar
            scroll.style.flexGrow  = 1;
            scroll.style.maxHeight = 340;
            p.Add(scroll);

            // Grid holds the item slots.
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap      = Wrap.Wrap;
            grid.style.paddingTop    = 4;
            grid.style.paddingBottom = 4;
            grid.style.paddingLeft   = 2;
            grid.style.paddingRight  = 2;
            scroll.Add(grid);

            // Read input system shift state (supports both new & old Input).
            static bool IsShiftHeld()
            {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
                var kb = UnityEngine.InputSystem.Keyboard.current;
                return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
#else
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            }

            // Sort mode state — persists for the lifetime of this panel instance.
            // 0 = Count ascending (default), 1 = Count descending,
            // 2 = Name A→Z, 3 = Name Z→A.
            int sortMode = 0;

            void ApplySortLabel()
            {
                sortBtn.text = sortMode switch
                {
                    0 => "Count ↑",
                    1 => "Count ↓",
                    2 => "Name A→Z",
                    3 => "Name Z→A",
                    _ => "Count ↑"
                };
            }

            void RebuildGrid(string filterQ)
            {
                grid.Clear();

                var allItems = rack.GetAllItems();

                // Sort BEFORE filtering so the order is stable as the user types.
                switch (sortMode)
                {
                    case 0: allItems.Sort((a, b) => a.count.CompareTo(b.count)); break;
                    case 1: allItems.Sort((a, b) => b.count.CompareTo(a.count)); break;
                    case 2: allItems.Sort((a, b) => string.Compare(
                                a.displayName, b.displayName,
                                System.StringComparison.OrdinalIgnoreCase)); break;
                    case 3: allItems.Sort((a, b) => string.Compare(
                                b.displayName, a.displayName,
                                System.StringComparison.OrdinalIgnoreCase)); break;
                }

                string q = filterQ.Trim().ToLowerInvariant();

                foreach (var entry in allItems)
                {
                    if (!string.IsNullOrEmpty(q) &&
                        !entry.displayName.ToLowerInvariant().Contains(q))
                        continue;

                    var def = FindItemDef(entry.itemId);
                    int maxExtract = def != null ? ItemStack.MaxItemsPerStack(def) : ItemContainer.DefaultMaxItemsPerStack;

                    // ── Slot cell ────────────────────────────────
                    var cell = new VisualElement();
                    cell.style.width           = 68;
                    cell.style.height          = 72;
                    cell.style.marginRight     = 4;
                    cell.style.marginBottom    = 4;
                    cell.style.paddingTop      = 4;
                    cell.style.paddingBottom   = 2;
                    cell.style.paddingLeft     = 3;
                    cell.style.paddingRight    = 3;
                    cell.style.backgroundColor = new StyleColor(T.BgCard);
                    cell.style.alignItems      = Align.Center;
                    cell.style.justifyContent  = Justify.SpaceBetween;
                    T.Radius(cell, 6f);
                    T.Border(cell, 1, T.BorderDim);
                    // cursor is not needed — default pointer is fine

                    // Icon area (48×48 or coloured box fallback).
                    var iconWrap = new VisualElement();
                    iconWrap.style.width           = 44;
                    iconWrap.style.height          = 44;
                    iconWrap.style.alignItems      = Align.Center;
                    iconWrap.style.justifyContent  = Justify.Center;
                    iconWrap.pickingMode           = PickingMode.Ignore;

                    if (def != null && def.icon != null)
                    {
                        var img = new Image { sprite = def.icon };
                        img.scaleMode = ScaleMode.ScaleToFit; // match BuildSlot: tight-cropped generated icons must fit, not crop (fixes blank recipe/crafter icons)
                        img.style.width  = 40;
                        img.style.height = 40;
                        img.pickingMode  = PickingMode.Ignore;
                        iconWrap.Add(img);
                    }
                    else
                    {
                        // Coloured box placeholder.
                        var box = new VisualElement();
                        box.style.width           = 36;
                        box.style.height          = 36;
                        box.style.backgroundColor = new StyleColor(
                            def != null ? def.iconTint : T.AccentCyan);
                        T.Radius(box, 4f);
                        box.pickingMode = PickingMode.Ignore;
                        iconWrap.Add(box);
                    }
                    cell.Add(iconWrap);

                    // Stack count label.
                    var countLbl = new Label(FormatCount(entry.count));
                    countLbl.style.color                   = new StyleColor(T.AccentCyan);
                    countLbl.style.fontSize                = 10;
                    countLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                    countLbl.style.unityTextAlign          = TextAnchor.MiddleCenter;
                    countLbl.style.minWidth                = 62;
                    countLbl.pickingMode                   = PickingMode.Ignore;
                    cell.Add(countLbl);

                    // Name label (truncated).
                    var nameLbl = new Label(entry.displayName);
                    nameLbl.style.color         = new StyleColor(T.TextSecondary);
                    nameLbl.style.fontSize       = 8;
                    nameLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                    nameLbl.style.overflow       = Overflow.Hidden;
                    nameLbl.style.maxWidth       = 62;
                    nameLbl.style.whiteSpace     = WhiteSpace.NoWrap;
                    nameLbl.pickingMode          = PickingMode.Ignore;
                    cell.Add(nameLbl);

                    // ── Click handler ────────────────────────────
                    // Capture loop variable values.
                    string capturedId    = entry.itemId;
                    string capturedName  = entry.displayName;
                    UnityEngine.Sprite capturedIcon = def?.icon;

                    cell.RegisterCallback<ClickEvent>(evt =>
                    {
                        if (playerInv == null) return;
                        bool shift  = IsShiftHeld();
                        int  amount = shift ? maxExtract : 1;
                        int  got    = rack.NetworkExtract(capturedId, amount);
                        if (got > 0)
                        {
                            var itemDef = FindItemDef(capturedId);
                            if (itemDef != null)
                                playerInv.Add(itemDef, got);
                            BuildFeedbackHud.Show(
                                capturedName,
                                $"+{got}",
                                capturedIcon,
                                T.AccentCyan);
                            // Rebuild grid in-place (no full UI refresh needed).
                            RebuildGrid(searchField.value);
                        }
                    });

                    // Hover highlight.
                    cell.RegisterCallback<MouseEnterEvent>(_ =>
                    {
                        cell.style.backgroundColor = new StyleColor(T.BgHover);
                        T.Border(cell, 1, T.BorderBright);
                    });
                    cell.RegisterCallback<MouseLeaveEvent>(_ =>
                    {
                        cell.style.backgroundColor = new StyleColor(T.BgCard);
                        T.Border(cell, 1, T.BorderDim);
                    });

                    grid.Add(cell);
                }

                if (grid.childCount == 0)
                    grid.Add(T.Muted(string.IsNullOrEmpty(q)
                        ? "Storage is empty."
                        : "No items match the search."));
            }

            sortBtn.clicked += () =>
            {
                sortMode = (sortMode + 1) % 4;
                ApplySortLabel();
                RebuildGrid(searchField.value);
            };

            searchField.RegisterValueChangedCallback(e => RebuildGrid(e.newValue));
            ApplySortLabel();
            RebuildGrid("");

            // ── Hint bar ─────────────────────────────────────────
            p.Add(T.Spacer(4));
            var hint = T.Muted("Click = take 1  ·  Shift+Click = take stack  ·  Shift+Click inventory item = store");
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            p.Add(hint);

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

            var (hdr, _, _, _) = T.HeaderRow("📋 Pattern Terminal",
                online ? "ONLINE" : "NO RACK",
                online ? T.AccentPurple : T.AccentRed);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentPurple));

            if (!online)
            {
                p.Add(T.Body("No server rack connected."));
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

            var (hdr, _, _, _) = T.HeaderRow("🔨 Crafting Terminal",
                online ? "ONLINE" : "NO RACK",
                online ? T.AccentCyan : T.AccentRed);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            if (!online)
            {
                p.Add(T.Body("No server rack connected."));
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
            p.Add(T.AccentDivider(T.AccentGreen));

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
            p.Add(T.AccentDivider(T.AccentOrange));

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
            p.Add(T.AccentDivider(T.AccentCyan));

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
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                      NAS BLOCK
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildNASPanel(
            NASBlock nas,
            MachineUIs.SlotBuilder slotBuilder)
        {
            if (nas.diskSlots == null)
                nas.diskSlots = new ItemContainer("NAS Disks", 10);

            var p = T.MachinePanel();

            var (hdr, _, _, _) = T.HeaderRow("🗄 NAS Block",
                nas.TotalCapacity > 0 ? "CONNECTED" : "EMPTY",
                nas.TotalCapacity > 0 ? T.AccentGreen : T.TextMuted);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            p.Add(T.StatRow("💾", "Storage",
                $"{nas.TotalStored:N0} / {nas.TotalCapacity:N0} GB", T.AccentCyan));
            var (bar, _) = T.ProgressBar(
                nas.TotalCapacity > 0 ? (float)nas.TotalStored / nas.TotalCapacity : 0,
                T.AccentCyan, 8, true);
            p.Add(bar);
            p.Add(T.Divider());

            p.Add(T.Subtitle("Disk Slots (10)"));
            var diskGrid = T.SlotGrid();
            for (int i = 0; i < nas.diskSlots.Size; i++)
                diskGrid.Add(slotBuilder(nas.diskSlots, i,
                    nas.diskSlots.GetSlot(i), false, true));
            p.Add(diskGrid);

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Connect to a Server Rack via data cables to expand network storage. " +
                          "Each disk remembers its contents."));
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                     SERVER RACK
        // ════════════════════════════════════════════════════════════
        public static VisualElement BuildServerPanel(
            ServerRack rack,
            MachineUIs.SlotBuilder slotBuilder)
        {
            rack.EnsureContainers();
            var p = T.MachinePanel();
            p.style.width = 520;

            // Status: overloaded / online / offline.
            string status    = rack.IsPsuOverloaded ? "PSU OVERLOADED" :
                               rack.IsOnline        ? "ONLINE"         : "OFFLINE";
            Color  statusCol = rack.IsPsuOverloaded ? T.AccentRed :
                               rack.IsOnline        ? T.AccentGreen : T.TextMuted;

            var (hdr, _, _, _) = T.HeaderRow("🖥 Server Rack", status, statusCol);
            p.Add(hdr);

            // Power bar — tight fill with correct math.
            float powerUsed = 0;
            var pc = rack.GetComponent<VoxelEngine.Power.PowerConsumer>();
            if (pc != null) powerUsed = pc.wattsPerSecond;
            float powerMax  = rack.MaxPowerWatts;
            float powerFill = powerMax > 0 ? Mathf.Clamp01(powerUsed / powerMax) : 0f;

            Color pwrColor = rack.IsPsuOverloaded ? T.AccentRed :
                             powerFill > 0.85f     ? T.AccentOrange : T.AccentGold;
            p.Add(T.StatRow("⚡", "Power",
                $"{powerUsed:0} W / {powerMax:0} W", pwrColor));
            var (pwrBar, _) = T.ProgressBar(powerFill, pwrColor, 8, false);
            p.Add(pwrBar);

            if (rack.IsPsuOverloaded)
            {
                var warn = T.StatLabel("⚠ PSU Overloaded — add a PSU or Powerstation!", T.AccentRed);
                warn.style.marginTop = 4;
                p.Add(warn);
            }

            p.Add(T.AccentDivider(T.AccentCyan));

            // Stats.
            p.Add(T.StatRow("💾", "Storage",     $"{rack.TotalStored:N0} / {rack.TotalCapacity:N0} GB", T.AccentCyan));
            p.Add(T.StatRow("🧠", "Patterns",    $"{rack.PatternSlots} slots",       T.TextSecondary));
            p.Add(T.StatRow("⚡", "Craft Speed",  $"{rack.CraftSpeedMultiplier:0.0}x", T.AccentGold));
            p.Add(T.Divider());

            // Disk slots.
            p.Add(T.Subtitle("Storage Disks (6)"));
            var diskGrid = T.SlotGrid();
            for (int i = 0; i < rack.diskSlots.Size; i++)
                diskGrid.Add(slotBuilder(rack.diskSlots, i, rack.diskSlots.GetSlot(i), false, true));
            p.Add(diskGrid);
            p.Add(T.Spacer(6));

            // Hardware row — validated slots.
            p.Add(T.Subtitle("Hardware"));
            var hwRow = new VisualElement();
            hwRow.style.flexDirection  = FlexDirection.Row;
            hwRow.style.justifyContent = Justify.Center;

            var ramGrid = T.SlotGrid();
            for (int i = 0; i < rack.ramSlots.Size; i++)
                ramGrid.Add(slotBuilder(rack.ramSlots, i, rack.ramSlots.GetSlot(i), false, true));
            hwRow.Add(T.SlotCard("RAM", ramGrid));
            hwRow.Add(T.Spacer(6));

            var cpuGrid = T.SlotGrid();
            cpuGrid.Add(slotBuilder(rack.cpuSlot, 0, rack.cpuSlot.GetSlot(0), false, true));
            hwRow.Add(T.SlotCard("CPU", cpuGrid));
            hwRow.Add(T.Spacer(6));

            var psuGrid = T.SlotGrid();
            psuGrid.Add(slotBuilder(rack.psuSlot, 0, rack.psuSlot.GetSlot(0), false, true));
            hwRow.Add(T.SlotCard("PSU", psuGrid));
            p.Add(hwRow);
            p.Add(T.Divider());

            // Connected devices summary.
            p.Add(T.Subtitle("Network Connections"));
            int nasCnt = rack.connectedNAS?.Count ?? 0;
            if (nasCnt > 0)
            {
                int nasS = 0, nasCap = 0;
                foreach (var nas in rack.connectedNAS)
                    if (nas != null) { nasS += nas.TotalStored; nasCap += nas.TotalCapacity; }
                p.Add(T.StatRow("🗄", "NAS Blocks",
                    $"{nasCnt}x  ({nasS:N0}/{nasCap:N0} GB)", T.AccentCyan));
            }
            else
            {
                p.Add(T.Muted("No NAS blocks connected. Use data cables + wrench."));
            }

            p.Add(T.Spacer(4));
            p.Add(T.Muted("CPU accepts only CPU modules. RAM accepts only RAM. PSU accepts only PSU."));
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
            p.Add(T.AccentDivider(T.AccentTeal));
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
            p.Add(T.AccentDivider(T.AccentGreen));
            p.Add(T.StatRow("🖥", "Server Rack", controller.ConnectedRack != null ? controller.ConnectedRack.name : "None", online ? T.AccentGreen : T.TextMuted));
            p.Add(T.StatRow("▣", "Drawers", controller.Drawers.Count.ToString(), T.AccentCyan));
            p.Add(T.StatRow("📡", "Drawer Radius", $"{controller.drawerRadius:0} m", T.TextSecondary));
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
            p.Add(T.AccentDivider(T.AccentCyan));
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
            return p;
        }

        // ── Helpers ────────────────────────────────────────────────
        internal static ItemDefinition FindItemDef(string id)
        {
            var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            foreach (var it in all) if (it.itemId == id) return it;
            return null;
        }
    }
}
