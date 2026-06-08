// Assets/Scripts/VoxelEngine/UI/ItemFilterDialog.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   ITEM FILTER DIALOG — modal prompt for a face's item filter     ║
// ║                                                                  ║
// ║   • Whitelist / Blacklist toggle for the face.                   ║
// ║   • Current items shown as chips with a delete ✕.                ║
// ║   • Search box → live result list → click to add.               ║
// ║   • DROP an item here from the inventory (drag-drop target).      ║
// ║   • SHIFT-CLICK an inventory slot while open → adds that item     ║
// ║     (GameUIController routes the click through TryCaptureItem).   ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.Transport;
using T = VoxelEngine.UI.UITheme;
// Disambiguate from UnityEngine.FilterMode (texture filter enum).
using FilterMode = VoxelEngine.Transport.FilterMode;

namespace VoxelEngine.UI
{
    public static class ItemFilterDialog
    {
        // While a dialog is open this captures items the player shift-clicks /
        // drops from their inventory. GameUIController checks IsCapturing first.
        private static Action<ItemDefinition> _captureSink;
        public static bool IsCapturing => _captureSink != null;

        /// <summary>Feed an item the player shift-clicked / dropped into the open dialog.</summary>
        public static bool TryCaptureItem(ItemDefinition item)
        {
            if (_captureSink == null || item == null) return false;
            _captureSink(item);
            return true;
        }

        private static ItemDefinition[] _allItems;

        /// <summary>
        /// Open the modal filter editor for one face of a machine.
        /// </summary>
        public static void Open(VisualElement uiRoot, ItemPortRouting routing, CubeFace face, Action onChanged)
        {
            if (uiRoot == null || routing == null) return;
            var panelRoot = uiRoot.panel?.visualTree ?? uiRoot;

            // ── Dim overlay ────────────────────────────────────────
            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0; overlay.style.top = 0; overlay.style.right = 0; overlay.style.bottom = 0;
            overlay.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.5f));
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;

            void Close()
            {
                _captureSink = null;
                PortConfigHud.IsAnyDropdownOpen = false;
                overlay.RemoveFromHierarchy();
                onChanged?.Invoke();
            }
            overlay.RegisterCallback<PointerDownEvent>(evt => { if (evt.target == overlay) Close(); });

            // ── Dialog card ────────────────────────────────────────
            var card = new VisualElement();
            card.style.width = 380;
            card.style.maxHeight = 520;
            card.style.backgroundColor = new StyleColor(T.BgPanel);
            card.style.paddingTop = 16; card.style.paddingBottom = 16;
            card.style.paddingLeft = 16; card.style.paddingRight = 16;
            T.Radius(card, 12f);
            T.Border(card, 1, T.BorderBright);
            overlay.Add(card);

            var title = T.Subtitle($"Filter · {FaceLabel(face)}");
            title.style.marginTop = 0;
            card.Add(title);

            // ── Whitelist / Blacklist toggle ───────────────────────
            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.marginTop = 10; modeRow.style.marginBottom = 6;
            card.Add(modeRow);

            Button wlBtn = null, blBtn = null;
            void RefreshModeButtons()
            {
                var mode = routing.GetFilterMode(face);
                StyleModeButton(wlBtn, mode == FilterMode.Whitelist, T.AccentGreen);
                StyleModeButton(blBtn, mode == FilterMode.Blacklist, T.AccentRed);
            }
            wlBtn = ModeButton("✓  WHITELIST", () => { routing.SetFilterMode(face, FilterMode.Whitelist); RefreshModeButtons(); onChanged?.Invoke(); });
            blBtn = ModeButton("✕  BLACKLIST", () => { routing.SetFilterMode(face, FilterMode.Blacklist); RefreshModeButtons(); onChanged?.Invoke(); });
            modeRow.Add(wlBtn); modeRow.Add(blBtn);
            RefreshModeButtons();

            var modeHint = T.Muted("Whitelist: only listed items pass.  Blacklist: listed items are blocked.");
            modeHint.style.fontSize = 9;
            card.Add(modeHint);

            // ── Current chips ──────────────────────────────────────
            var chips = new VisualElement();
            chips.style.flexDirection = FlexDirection.Row;
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.marginTop = 10;
            card.Add(chips);

            // ── Results list (shown while searching) ───────────────
            var search = new TextField { value = "" };
            search.style.marginTop = 10; search.style.height = 26; search.style.fontSize = 12;
            card.Add(search);

            var wm = new Label("🔍  Search items to add…");
            wm.style.position = Position.Absolute; wm.style.left = 24;
            wm.style.fontSize = 11; wm.style.color = new StyleColor(T.TextMuted);
            wm.pickingMode = PickingMode.Ignore;
            // Position the watermark over the search field after layout.
            search.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                wm.style.top = search.layout.y + 5;
            });
            card.Add(wm);

            var resultsScroll = new ScrollView(ScrollViewMode.Vertical);
            resultsScroll.style.marginTop = 4;
            resultsScroll.style.maxHeight = 200;
            card.Add(resultsScroll);

            // ── Drop target hint ───────────────────────────────────
            var drop = new VisualElement();
            drop.style.marginTop = 10;
            drop.style.height = 40;
            drop.style.alignItems = Align.Center;
            drop.style.justifyContent = Justify.Center;
            drop.style.backgroundColor = new StyleColor(new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.10f));
            T.Radius(drop, 8f);
            T.Border(drop, 1, new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.4f));
            var dropLbl = new Label("⤓  Drop an item here  ·  or Shift-click an inventory slot");
            dropLbl.style.color = new StyleColor(T.TextSecondary);
            dropLbl.style.fontSize = 10;
            dropLbl.pickingMode = PickingMode.Ignore;
            drop.Add(dropLbl);
            card.Add(drop);

            // ── Local refresh helpers ──────────────────────────────
            void RebuildChips()
            {
                chips.Clear();
                var items = routing.GetFilter(face);
                if (items.Count == 0)
                    chips.Add(T.Muted("No items yet — search, drop, or shift-click to add."));
                foreach (var it in items)
                    chips.Add(MakeChip(it, () => { routing.RemoveFilter(face, it); RebuildChips(); onChanged?.Invoke(); }));
            }

            void AddItem(ItemDefinition it)
            {
                if (it == null) return;
                routing.AddFilter(face, it);
                RebuildChips();
                onChanged?.Invoke();
                // Flash the drop zone to confirm.
                drop.style.backgroundColor = new StyleColor(new Color(T.AccentGreen.r, T.AccentGreen.g, T.AccentGreen.b, 0.25f));
                drop.schedule.Execute(() =>
                    drop.style.backgroundColor = new StyleColor(new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.10f))).StartingIn(180);
            }

            void Populate(string q)
            {
                resultsScroll.Clear();
                q = (q ?? "").Trim().ToLowerInvariant();
                if (q.Length == 0) { wm.style.display = DisplayStyle.Flex; return; }
                wm.style.display = DisplayStyle.None;
                EnsureItems();
                var owned = new HashSet<ItemDefinition>(routing.GetFilter(face));
                int shown = 0;
                foreach (var it in _allItems)
                {
                    if (it == null || owned.Contains(it)) continue;
                    if (!(it.displayName ?? "").ToLowerInvariant().Contains(q) &&
                        !(it.itemId ?? "").ToLowerInvariant().Contains(q)) continue;
                    resultsScroll.Add(MakeResultRow(it, () => { AddItem(it); search.value = ""; }));
                    if (++shown >= 80) break;
                }
                if (shown == 0) resultsScroll.Add(T.Muted("No matching items."));
            }

            search.RegisterValueChangedCallback(e => Populate(e.newValue));

            // ── Footer ─────────────────────────────────────────────
            var done = new Button { text = "DONE" };
            done.style.marginTop = 12; done.style.height = 32;
            done.style.color = Color.white; done.style.fontSize = 12;
            done.style.unityFontStyleAndWeight = FontStyle.Bold;
            done.style.backgroundColor = new StyleColor(new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.85f));
            T.Radius(done, 7f);
            done.clicked += Close;
            card.Add(done);

            RebuildChips();
            Populate("");

            // Capture shift-clicks / drops from the inventory while open.
            _captureSink = AddItem;
            PortConfigHud.IsAnyDropdownOpen = true;   // suspend the panel auto-refresh
            panelRoot.Add(overlay);
            search.schedule.Execute(() => search.Focus()).StartingIn(40);
        }

        // ── pieces ──────────────────────────────────────────────────
        private static Button ModeButton(string text, Action onClick)
        {
            var b = new Button { text = text };
            b.style.flexGrow = 1; b.style.height = 30; b.style.fontSize = 11;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.marginRight = 6;
            T.Radius(b, 7f);
            b.clicked += onClick;
            return b;
        }

        private static void StyleModeButton(Button b, bool active, Color accent)
        {
            if (b == null) return;
            b.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, active ? 0.85f : 0.14f));
            b.style.color = new StyleColor(active ? Color.white : T.TextSecondary);
            T.Border(b, 1, new Color(accent.r, accent.g, accent.b, active ? 0.7f : 0.3f));
        }

        private static VisualElement MakeChip(ItemDefinition item, Action onRemove)
        {
            var chip = new VisualElement();
            chip.style.flexDirection = FlexDirection.Row;
            chip.style.alignItems = Align.Center;
            chip.style.height = 24;
            chip.style.marginRight = 4; chip.style.marginBottom = 4;
            chip.style.paddingLeft = 6; chip.style.paddingRight = 4;
            chip.style.backgroundColor = new StyleColor(T.BgSlot);
            T.Radius(chip, 7f);
            T.Border(chip, 1, T.BorderDim);
            AddIcon(chip, item, 16);
            var name = new Label(item.displayName);
            name.style.color = new StyleColor(T.TextPrimary);
            name.style.fontSize = 10; name.style.marginRight = 4;
            name.pickingMode = PickingMode.Ignore;
            chip.Add(name);
            var x = new Button { text = "✕" };
            x.style.fontSize = 9; x.style.width = 16; x.style.height = 16;
            x.style.paddingLeft = 0; x.style.paddingRight = 0; x.style.paddingTop = 0; x.style.paddingBottom = 0;
            x.style.color = new StyleColor(T.TextDanger);
            x.style.backgroundColor = new StyleColor(Color.clear);
            x.clicked += () => onRemove?.Invoke();
            chip.Add(x);
            return chip;
        }

        private static VisualElement MakeResultRow(ItemDefinition item, Action onPick)
        {
            var row = new Button();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 30; row.style.marginBottom = 2;
            row.style.paddingLeft = 8; row.style.paddingRight = 8;
            row.style.backgroundColor = new StyleColor(T.BgSlot);
            T.Radius(row, 5f);
            T.Border(row, 1, T.BorderDim);
            AddIcon(row, item, 20);
            var name = new Label(item.displayName);
            name.style.color = new StyleColor(T.TextPrimary);
            name.style.fontSize = 11; name.style.flexGrow = 1;
            name.pickingMode = PickingMode.Ignore;
            row.Add(name);
            row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = new StyleColor(T.BgHover));
            row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = new StyleColor(T.BgSlot));
            row.clicked += () => onPick?.Invoke();
            return row;
        }

        private static void AddIcon(VisualElement parent, ItemDefinition item, float size)
        {
            if (item.icon != null)
            {
                var img = new Image { sprite = item.icon };
                img.style.width = size; img.style.height = size; img.style.marginRight = 6;
                img.pickingMode = PickingMode.Ignore;
                parent.Add(img);
            }
            else
            {
                var box = new VisualElement();
                box.style.width = size * 0.85f; box.style.height = size * 0.85f; box.style.marginRight = 6;
                box.style.backgroundColor = new StyleColor(item.iconTint);
                T.Radius(box, 3f);
                box.pickingMode = PickingMode.Ignore;
                parent.Add(box);
            }
        }

        private static string FaceLabel(CubeFace f) => f switch
        {
            CubeFace.PosY => "TOP", CubeFace.NegY => "BOTTOM",
            CubeFace.PosX => "RIGHT", CubeFace.NegX => "LEFT",
            CubeFace.PosZ => "FRONT", CubeFace.NegZ => "BACK",
            _ => f.ToString()
        };

        private static void EnsureItems()
        {
            if (_allItems != null && _allItems.Length > 0) return;
            _allItems = Resources.LoadAll<ItemDefinition>("")
                .Where(i => i != null && !string.IsNullOrEmpty(i.itemId))
                .OrderBy(i => i.displayName)
                .ToArray();
        }
    }
}
