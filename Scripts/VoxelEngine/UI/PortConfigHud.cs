// Assets/Scripts/VoxelEngine/UI/PortConfigHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║         PORT CONFIGURATION WIDGET — premium 3D-face grid        ║
// ║                                                                  ║
// ║   • Six face cards laid out as an unfolded cube cross so the     ║
// ║     player intuits which side of the machine they're editing.    ║
// ║   • Each card cycles None → In → Out with a chunky pill, AND    ║
// ║     hosts a dropdown filtered to ONLY the network types the      ║
// ║     machine actually supports (so a coal generator never offers  ║
// ║     "Fluid" or "Gas" by mistake).                                ║
// ║   • Static `IsAnyDropdownOpen` flag lets the surrounding HUD     ║
// ║     suspend its periodic full Refresh() so opening the dropdown  ║
// ║     no longer destroys the widget mid-click.                     ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Transport;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class PortConfigHud
    {
        // ────────────────────────────────────────────────────────────
        // Pretty face labels with arrows so the orientation is unambiguous.
        // ────────────────────────────────────────────────────────────
        private static readonly (CubeFace face, string label, string axisHint)[] FACES =
        {
            (CubeFace.PosY, "TOP",    "+Y"),
            (CubeFace.NegY, "BOTTOM", "−Y"),
            (CubeFace.PosX, "RIGHT",  "+X"),
            (CubeFace.NegX, "LEFT",   "−X"),
            (CubeFace.PosZ, "FRONT",  "+Z"),
            (CubeFace.NegZ, "BACK",   "−Z"),
        };

        // Direction-tinted colours used across every face card.
        private static readonly Color ColNone   = new(0.20f, 0.22f, 0.28f);
        private static readonly Color ColInput  = new(0.18f, 0.55f, 0.90f);  // cyan-ish "IN"
        private static readonly Color ColOutput = new(0.92f, 0.55f, 0.12f);  // amber-ish "OUT"

        private static readonly string[] AllNetworkTypeOptions =
            { "Any", "Power", "Data", "Fluid", "Gas" };

        /// <summary>
        /// Set to TRUE whenever a dropdown is open. The GameUIController checks this
        /// before its 1Hz "rebuild the right panel" tick so opening the dropdown
        /// doesn't destroy itself two seconds later. Cleared as soon as the
        /// dropdown closes.
        /// </summary>
        public static bool IsAnyDropdownOpen { get; private set; }

        // ────────────────────────────────────────────────────────────
        // PUBLIC ENTRY POINT
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Build the premium port-config widget.
        /// </summary>
        /// <param name="config">The PortConfig component to drive.</param>
        /// <param name="onChanged">Called after any face/dir/type edit.</param>
        /// <param name="allowedTypes">
        /// Optional whitelist — only these network types appear in the dropdown
        /// (plus "Any" if it's in the list). Pass NULL to show all 5 (default).
        /// E.g. a Coal Generator should pass <c>{PortNetworkType.Power}</c>.
        /// </param>
        public static VisualElement Build(PortConfig config,
                                          Action onChanged = null,
                                          PortNetworkType[] allowedTypes = null)
        {
            if (config == null)
            {
                return T.Muted("No PortConfig component found.");
            }

            config.EnsureAllFaces();

            // Resolve the filtered option list once — every dropdown reuses it.
            var typeOptions = BuildTypeOptions(allowedTypes);

            // ── Outer container ────────────────────────────────────
            var root = new VisualElement();
            root.style.marginTop    = 8;
            root.style.marginBottom = 4;

            // ── Header (title + legend) ────────────────────────────
            root.Add(BuildHeader());

            // ── 3×3 unfolded-cube layout ───────────────────────────
            //
            //    .  TOP    .
            //   LEFT FRONT RIGHT BACK
            //    .  BOT    .
            //
            // …but we collapse to a tidy 2-column responsive grid so
            // the panel stays elegant inside narrow side-bars.
            var grid = new VisualElement();
            grid.style.flexDirection   = FlexDirection.Row;
            grid.style.flexWrap        = Wrap.Wrap;
            grid.style.marginTop       = 8;
            root.Add(grid);

            // Capture state so we can rebuild ONE card in place after an edit
            // instead of nuking the whole widget (and any open dropdown).
            var cardRefs = new Dictionary<CubeFace, VisualElement>();
            void RebuildCard(CubeFace face)
            {
                if (!cardRefs.TryGetValue(face, out var oldCard)) return;
                int idx = oldCard.parent.IndexOf(oldCard);
                var parent = oldCard.parent;
                parent.Remove(oldCard);
                var fresh = BuildFaceCard(config, face,
                                          typeOptions,
                                          inlineChanged: () => { RebuildCard(face); onChanged?.Invoke(); });
                parent.Insert(idx, fresh);
                cardRefs[face] = fresh;
            }

            foreach (var (face, _, _) in FACES)
            {
                var card = BuildFaceCard(config, face,
                                         typeOptions,
                                         inlineChanged: () => { RebuildCard(face); onChanged?.Invoke(); });
                cardRefs[face] = card;
                grid.Add(card);
            }

            // ── Footer hint ────────────────────────────────────────
            var hint = T.Muted("Click a face to cycle  None → Input → Output.  " +
                               "Use the dropdown to lock the face to a specific network.");
            hint.style.marginTop = 10;
            root.Add(hint);

            return root;
        }

        // ────────────────────────────────────────────────────────────
        // SECTIONS
        // ────────────────────────────────────────────────────────────

        private static VisualElement BuildHeader()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.pickingMode = PickingMode.Ignore;

            var title = T.Subtitle("Port Configuration");
            title.style.flexGrow  = 1;
            title.style.marginTop = 0;
            row.Add(title);

            row.Add(MakeLegendChip(ColInput,  "IN"));
            row.Add(LegendSpacer());
            row.Add(MakeLegendChip(ColOutput, "OUT"));
            row.Add(LegendSpacer());
            row.Add(MakeLegendChip(ColNone,   "OFF"));

            return row;
        }

        private static VisualElement BuildFaceCard(PortConfig config, CubeFace face,
                                                    string[] typeOptions, Action inlineChanged)
        {
            var meta = GetFaceMeta(face);
            var dir       = config.GetDirection(face);
            var netType   = config.GetNetworkType(face);
            var enabled   = config.IsFaceEnabled(face);
            var bgTint    = DirectionColor(dir);

            // ── Card frame ─────────────────────────────────────────
            var card = new VisualElement();
            card.style.width        = Length.Percent(50);   // 2-up grid
            card.style.minWidth     = 190;
            card.style.paddingTop   = 8;
            card.style.paddingBottom= 8;
            card.style.paddingLeft  = 8;
            card.style.paddingRight = 8;

            var inner = new VisualElement();
            inner.style.backgroundColor = new StyleColor(T.BgCard);
            inner.style.paddingTop    = 10;
            inner.style.paddingBottom = 10;
            inner.style.paddingLeft   = 12;
            inner.style.paddingRight  = 12;
            T.Radius(inner, 10f);
            T.Border(inner, 1, enabled ? new Color(bgTint.r, bgTint.g, bgTint.b, 0.55f) : T.BorderDim);
            card.Add(inner);

            // ── Card header: face name + axis hint ────────────────
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems    = Align.Center;
            head.pickingMode = PickingMode.Ignore;

            var faceDot = new VisualElement();
            faceDot.style.width  = 10; faceDot.style.height = 10;
            faceDot.style.backgroundColor = new StyleColor(bgTint);
            T.Radius(faceDot, 5f);
            faceDot.style.marginRight = 6;
            head.Add(faceDot);

            var nameLbl = new Label(meta.label);
            nameLbl.style.color    = new StyleColor(T.TextPrimary);
            nameLbl.style.fontSize = 12;
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLbl.style.letterSpacing = 0.8f;
            nameLbl.style.flexGrow = 1;
            head.Add(nameLbl);

            var axisLbl = new Label(meta.axisHint);
            axisLbl.style.color    = new StyleColor(T.TextMuted);
            axisLbl.style.fontSize = 10;
            head.Add(axisLbl);

            inner.Add(head);

            // ── Direction pill (the big cyclable button) ──────────
            var pill = MakeDirectionPill(dir, enabled, bgTint);
            pill.clicked += () =>
            {
                var cur = config.GetDirection(face);
                PortDirection next;
                if (!config.IsFaceEnabled(face))
                {
                    config.SetFaceEnabled(face, true);
                    next = PortDirection.Input;
                }
                else
                {
                    next = cur switch
                    {
                        PortDirection.None   => PortDirection.Input,
                        PortDirection.Input  => PortDirection.Output,
                        PortDirection.Output => PortDirection.None,
                        _                    => PortDirection.None,
                    };
                    if (next == PortDirection.None) config.SetFaceEnabled(face, false);
                }
                config.SetDirection(face, next);
                config.RefreshIndicators();
                inlineChanged?.Invoke();
            };
            inner.Add(pill);

            // ── Network type dropdown ─────────────────────────────
            // Only show when the face is enabled — keeps "off" cards quiet.
            if (enabled && typeOptions != null && typeOptions.Length > 1)
            {
                var dropRow = new VisualElement();
                dropRow.style.marginTop = 8;
                dropRow.style.flexDirection = FlexDirection.Row;
                dropRow.style.alignItems = Align.Center;
                dropRow.pickingMode = PickingMode.Ignore;

                var lbl = new Label("NET");
                lbl.style.color    = new StyleColor(T.TextMuted);
                lbl.style.fontSize = 9;
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                lbl.style.letterSpacing = 1f;
                lbl.style.marginRight = 6;
                dropRow.Add(lbl);

                int initialIdx = Array.IndexOf(typeOptions, netType.ToString());
                if (initialIdx < 0) initialIdx = 0;

                var dropdown = new DropdownField
                {
                    choices = new List<string>(typeOptions),
                    index   = initialIdx,
                };
                dropdown.style.flexGrow = 1;
                dropdown.style.height   = 22;
                dropdown.style.fontSize = 11;

                // ── Suspend the panel's auto-refresh while open ───
                // PointerDown opens the menu → mark active.
                // Value-change OR pointer-leave closes it → mark idle.
                dropdown.RegisterCallback<PointerDownEvent>(_ => IsAnyDropdownOpen = true);
                dropdown.RegisterCallback<FocusOutEvent>(_  =>
                {
                    // Defer a frame so the value-change callback runs first.
                    dropdown.schedule.Execute(() => IsAnyDropdownOpen = false).StartingIn(50);
                });
                dropdown.RegisterValueChangedCallback(evt =>
                {
                    string picked = evt.newValue;
                    if (Enum.TryParse<PortNetworkType>(picked, out var parsed))
                    {
                        config.SetNetworkType(face, parsed);
                        config.RefreshIndicators();
                        inlineChanged?.Invoke();
                    }
                    IsAnyDropdownOpen = false;
                });

                dropRow.Add(dropdown);
                inner.Add(dropRow);
            }

            return card;
        }

        // ────────────────────────────────────────────────────────────
        // BACKWARDS-COMPAT SHIM (BuildWithDropdown kept for any caller)
        // ────────────────────────────────────────────────────────────

        /// <summary>Legacy entry point — now delegates to <see cref="Build"/>.</summary>
        public static VisualElement BuildWithDropdown(PortConfig config, Action onChanged = null)
            => Build(config, onChanged);

        // ════════════════════════════════════════════════════════════
        //   ITEM-PORT MODE  (chests & item endpoints)
        //   Per-face None → Input → Output PLUS an item whitelist chip
        //   strip so the player controls exactly what flows each side.
        // ════════════════════════════════════════════════════════════

        // Cache of every loadable item, sorted for the picker. Rebuilt lazily.
        private static ItemDefinition[] _allItemsCache;

        /// <summary>
        /// Build the premium item-port widget for a <see cref="Chest"/> (or any
        /// item endpoint exposing a PortConfig + per-face filters).
        /// OUTPUT faces push the chest's contents into adjacent pipes; INPUT faces
        /// accept pushed items. Each face carries an item whitelist (empty = all).
        /// </summary>
        public static VisualElement BuildItemPorts(Chest chest, Action onChanged = null)
        {
            if (chest == null) return T.Muted("No chest found.");
            var config = chest.GetComponent<PortConfig>();
            if (config == null) return T.Muted("No PortConfig component found.");
            config.EnsureAllFaces();

            var root = new VisualElement();
            root.style.marginTop = 8;
            root.style.marginBottom = 4;

            // Header + legend (reuse the network widget's chips, item-flavoured).
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            head.pickingMode = PickingMode.Ignore;
            var title = T.Subtitle("Item Ports");
            title.style.flexGrow = 1; title.style.marginTop = 0;
            head.Add(title);
            head.Add(MakeLegendChip(ColInput, "IN"));
            head.Add(LegendSpacer());
            head.Add(MakeLegendChip(ColOutput, "OUT"));
            head.Add(LegendSpacer());
            head.Add(MakeLegendChip(ColNone, "OFF"));
            root.Add(head);

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.width = Length.Percent(100);   // fill the panel so 50% cards = 2 columns
            grid.style.marginTop = 8;
            root.Add(grid);

            var cardRefs = new Dictionary<CubeFace, VisualElement>();
            void RebuildCard(CubeFace face)
            {
                if (!cardRefs.TryGetValue(face, out var oldCard)) return;
                var parent = oldCard.parent;
                int idx = parent.IndexOf(oldCard);
                parent.Remove(oldCard);
                var fresh = BuildItemFaceCard(chest, config, face,
                    () => { RebuildCard(face); onChanged?.Invoke(); });
                parent.Insert(idx, fresh);
                cardRefs[face] = fresh;
            }

            foreach (var (face, _, _) in FACES)
            {
                var card = BuildItemFaceCard(chest, config, face,
                    () => { RebuildCard(face); onChanged?.Invoke(); });
                cardRefs[face] = card;
                grid.Add(card);
            }

            var hint = T.Muted("Click a face to cycle None → Input → Output.  " +
                               "OUTPUT pushes items into adjacent pipes; INPUT accepts them.  " +
                               "Add filters to restrict a face to specific items.");
            hint.style.marginTop = 10;
            root.Add(hint);

            return root;
        }

        private static VisualElement BuildItemFaceCard(Chest chest, PortConfig config,
                                                       CubeFace face, Action inlineChanged)
        {
            var meta    = GetFaceMeta(face);
            var dir     = config.GetDirection(face);
            var enabled = config.IsFaceEnabled(face);
            var bgTint  = DirectionColor(dir);

            // Enforce a strict 2-column grid: each card is exactly half the row
            // width and never grows/shrinks. Spacing is done with the INNER box's
            // margin so the 50% width can never overflow and wrap to one column.
            var card = new VisualElement();
            card.style.width = Length.Percent(50);
            card.style.flexBasis = Length.Percent(50);
            card.style.flexGrow = 0;
            card.style.flexShrink = 0;
            card.style.minWidth = 0;
            card.style.paddingTop = 6; card.style.paddingBottom = 6;

            var inner = new VisualElement();
            inner.style.backgroundColor = new StyleColor(T.BgCard);
            inner.style.marginLeft = 4; inner.style.marginRight = 4;
            inner.style.paddingTop = 10; inner.style.paddingBottom = 10;
            inner.style.paddingLeft = 10; inner.style.paddingRight = 10;
            T.Radius(inner, 10f);
            T.Border(inner, 1, enabled ? new Color(bgTint.r, bgTint.g, bgTint.b, 0.55f) : T.BorderDim);
            card.Add(inner);

            // Header: name + axis.
            var hr = new VisualElement();
            hr.style.flexDirection = FlexDirection.Row;
            hr.style.alignItems = Align.Center;
            hr.pickingMode = PickingMode.Ignore;
            var dot = new VisualElement();
            dot.style.width = 10; dot.style.height = 10;
            dot.style.backgroundColor = new StyleColor(bgTint);
            T.Radius(dot, 5f); dot.style.marginRight = 6;
            hr.Add(dot);
            var nameLbl = new Label(meta.label);
            nameLbl.style.color = new StyleColor(T.TextPrimary);
            nameLbl.style.fontSize = 12;
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLbl.style.letterSpacing = 0.8f; nameLbl.style.flexGrow = 1;
            hr.Add(nameLbl);
            var axisLbl = new Label(meta.axisHint);
            axisLbl.style.color = new StyleColor(T.TextMuted);
            axisLbl.style.fontSize = 10;
            hr.Add(axisLbl);
            inner.Add(hr);

            // Direction pill (cyclable).
            var pill = MakeDirectionPill(dir, enabled, bgTint);
            pill.clicked += () =>
            {
                var cur = config.GetDirection(face);
                PortDirection next;
                if (!config.IsFaceEnabled(face))
                {
                    config.SetFaceEnabled(face, true);
                    next = PortDirection.Input;
                }
                else
                {
                    next = cur switch
                    {
                        PortDirection.None   => PortDirection.Input,
                        PortDirection.Input  => PortDirection.Output,
                        PortDirection.Output => PortDirection.None,
                        _                    => PortDirection.None,
                    };
                    if (next == PortDirection.None) config.SetFaceEnabled(face, false);
                }
                config.SetDirection(face, next);
                config.RefreshIndicators();
                inlineChanged?.Invoke();
            };
            inner.Add(pill);

            // Filter strip — only when the face is active.
            if (enabled && dir != PortDirection.None)
                inner.Add(BuildFilterStrip(chest, face, inlineChanged));

            return card;
        }

        private static VisualElement BuildFilterStrip(Chest chest, CubeFace face, Action inlineChanged)
        {
            var wrap = new VisualElement();
            wrap.style.marginTop = 8;

            var labelRow = new VisualElement();
            labelRow.style.flexDirection = FlexDirection.Row;
            labelRow.style.alignItems = Align.Center;
            labelRow.pickingMode = PickingMode.Ignore;
            var lbl = new Label(chest.HasFilter(face) ? "FILTER" : "FILTER · ALL");
            lbl.style.color = new StyleColor(T.TextMuted);
            lbl.style.fontSize = 9;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.letterSpacing = 1f;
            lbl.style.flexGrow = 1;
            labelRow.Add(lbl);
            wrap.Add(labelRow);

            // Chip flow.
            var chips = new VisualElement();
            chips.style.flexDirection = FlexDirection.Row;
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.marginTop = 4;
            wrap.Add(chips);

            foreach (var item in chest.GetFilter(face))
                chips.Add(MakeFilterChip(item, () =>
                {
                    chest.RemoveFilter(face, item);
                    inlineChanged?.Invoke();
                }));

            // "＋ Add" chip launches the picker popup.
            var addChip = new Button { text = "＋  Add" };
            addChip.style.height = 22;
            addChip.style.marginRight = 4; addChip.style.marginBottom = 4;
            addChip.style.paddingLeft = 8; addChip.style.paddingRight = 8;
            addChip.style.fontSize = 10;
            addChip.style.color = new StyleColor(T.TextSecondary);
            addChip.style.backgroundColor = new StyleColor(new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.16f));
            T.Radius(addChip, 7f);
            T.Border(addChip, 1, new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.40f));
            addChip.clicked += () => OpenItemPicker(addChip, chest, face, inlineChanged);
            chips.Add(addChip);

            return wrap;
        }

        private static VisualElement MakeFilterChip(ItemDefinition item, Action onRemove)
        {
            var chip = new VisualElement();
            chip.style.flexDirection = FlexDirection.Row;
            chip.style.alignItems = Align.Center;
            chip.style.height = 22;
            chip.style.marginRight = 4; chip.style.marginBottom = 4;
            chip.style.paddingLeft = 6; chip.style.paddingRight = 4;
            chip.style.backgroundColor = new StyleColor(T.BgSlot);
            T.Radius(chip, 7f);
            T.Border(chip, 1, T.BorderDim);

            if (item.icon != null)
            {
                var img = new Image { sprite = item.icon };
                img.style.width = 14; img.style.height = 14; img.style.marginRight = 4;
                img.pickingMode = PickingMode.Ignore;
                chip.Add(img);
            }
            else
            {
                var box = new VisualElement();
                box.style.width = 12; box.style.height = 12; box.style.marginRight = 4;
                box.style.backgroundColor = new StyleColor(item.iconTint);
                T.Radius(box, 3f);
                box.pickingMode = PickingMode.Ignore;
                chip.Add(box);
            }

            var name = new Label(item.displayName);
            name.style.color = new StyleColor(T.TextPrimary);
            name.style.fontSize = 10;
            name.style.marginRight = 4;
            name.pickingMode = PickingMode.Ignore;
            chip.Add(name);

            var x = new Button { text = "✕" };
            x.style.fontSize = 9;
            x.style.width = 16; x.style.height = 16;
            x.style.paddingLeft = 0; x.style.paddingRight = 0;
            x.style.paddingTop = 0; x.style.paddingBottom = 0;
            x.style.color = new StyleColor(T.TextDanger);
            x.style.backgroundColor = new StyleColor(Color.clear);
            x.clicked += () => onRemove?.Invoke();
            chip.Add(x);

            return chip;
        }

        // ── Item picker popup ───────────────────────────────────────
        private static void OpenItemPicker(VisualElement anchor, Chest chest,
                                           CubeFace face, Action inlineChanged)
        {
            var rootPanel = anchor.panel?.visualTree;
            if (rootPanel == null) return;

            EnsureItemCache();

            // Dim overlay that closes the picker on background click.
            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0; overlay.style.top = 0;
            overlay.style.right = 0; overlay.style.bottom = 0;
            overlay.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.45f));

            void Close()
            {
                IsAnyDropdownOpen = false;
                overlay.RemoveFromHierarchy();
            }
            overlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == overlay) Close();
            });

            // Centered card.
            var picker = new VisualElement();
            picker.style.position = Position.Absolute;
            picker.style.left = Length.Percent(50);
            picker.style.top = Length.Percent(50);
            picker.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            picker.style.width = 360;
            picker.style.maxHeight = 440;
            picker.style.backgroundColor = new StyleColor(T.BgPanel);
            picker.style.paddingTop = 14; picker.style.paddingBottom = 14;
            picker.style.paddingLeft = 14; picker.style.paddingRight = 14;
            T.Radius(picker, 12f);
            T.Border(picker, 1, T.BorderBright);
            overlay.Add(picker);

            var pTitle = T.Subtitle($"Add Item Filter · {GetFaceMeta(face).label}");
            pTitle.style.marginTop = 0;
            picker.Add(pTitle);

            var searchLbl = new Label("SEARCH");
            searchLbl.style.color = new StyleColor(T.TextMuted);
            searchLbl.style.fontSize = 9;
            searchLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            searchLbl.style.letterSpacing = 1f;
            searchLbl.style.marginTop = 8;
            picker.Add(searchLbl);

            var search = new TextField { value = "" };
            search.style.marginTop = 2; search.style.marginBottom = 8;
            picker.Add(search);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.maxHeight = 320;
            picker.Add(scroll);

            void Populate(string query)
            {
                scroll.Clear();
                var owned = new HashSet<ItemDefinition>(chest.GetFilter(face));
                var q = (query ?? "").Trim().ToLowerInvariant();
                int shown = 0;
                foreach (var item in _allItemsCache)
                {
                    if (item == null) continue;
                    if (owned.Contains(item)) continue;
                    if (q.Length > 0 &&
                        !(item.displayName ?? "").ToLowerInvariant().Contains(q) &&
                        !(item.itemId ?? "").ToLowerInvariant().Contains(q)) continue;

                    scroll.Add(MakePickerRow(item, () =>
                    {
                        chest.AddFilter(face, item);
                        Close();
                        inlineChanged?.Invoke();
                    }));
                    if (++shown >= 200) break; // guard absurd registries
                }
                if (shown == 0)
                    scroll.Add(T.Muted("No matching items."));
            }

            search.RegisterValueChangedCallback(evt => Populate(evt.newValue));
            Populate("");

            IsAnyDropdownOpen = true;
            rootPanel.Add(overlay);
            search.schedule.Execute(() => search.Focus()).StartingIn(30);
        }

        private static VisualElement MakePickerRow(ItemDefinition item, Action onPick)
        {
            var row = new Button();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 34;
            row.style.marginBottom = 4;
            row.style.paddingLeft = 8; row.style.paddingRight = 8;
            row.style.backgroundColor = new StyleColor(T.BgSlot);
            T.Radius(row, 6f);
            T.Border(row, 1, T.BorderDim);

            if (item.icon != null)
            {
                var img = new Image { sprite = item.icon };
                img.style.width = 22; img.style.height = 22; img.style.marginRight = 8;
                img.pickingMode = PickingMode.Ignore;
                row.Add(img);
            }
            else
            {
                var box = new VisualElement();
                box.style.width = 18; box.style.height = 18; box.style.marginRight = 8;
                box.style.backgroundColor = new StyleColor(item.iconTint);
                T.Radius(box, 3f);
                box.pickingMode = PickingMode.Ignore;
                row.Add(box);
            }

            var name = new Label(item.displayName);
            name.style.color = new StyleColor(T.TextPrimary);
            name.style.fontSize = 12; name.style.flexGrow = 1;
            name.pickingMode = PickingMode.Ignore;
            row.Add(name);

            row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = new StyleColor(T.BgHover));
            row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = new StyleColor(T.BgSlot));
            row.clicked += () => onPick?.Invoke();
            return row;
        }

        private static void EnsureItemCache()
        {
            if (_allItemsCache != null && _allItemsCache.Length > 0) return;
            _allItemsCache = Resources.LoadAll<ItemDefinition>("")
                .Where(i => i != null && !string.IsNullOrEmpty(i.itemId))
                .OrderBy(i => i.displayName)
                .ToArray();
        }

        // ────────────────────────────────────────────────────────────
        // HELPERS
        // ────────────────────────────────────────────────────────────

        private static Button MakeDirectionPill(PortDirection dir, bool enabled, Color tint)
        {
            string label = !enabled ? "DISABLED"
                        : dir == PortDirection.Input  ? "INPUT"
                        : dir == PortDirection.Output ? "OUTPUT"
                        : "OFF";

            var b = new Button();
            b.text = label;
            b.style.marginTop                 = 8;
            b.style.height                    = 32;
            b.style.color                     = Color.white;
            b.style.fontSize                  = 11;
            b.style.unityFontStyleAndWeight   = FontStyle.Bold;
            b.style.letterSpacing             = 1.2f;
            b.style.backgroundColor           = new StyleColor(new Color(tint.r, tint.g, tint.b, enabled ? 0.85f : 0.30f));
            T.Radius(b, 6f);
            T.Border(b, 1, new Color(tint.r, tint.g, tint.b, enabled ? 0.65f : 0.15f));
            // Hover sheen.
            b.RegisterCallback<PointerEnterEvent>(_ =>
                b.style.backgroundColor = new StyleColor(new Color(tint.r, tint.g, tint.b, enabled ? 1f : 0.45f)));
            b.RegisterCallback<PointerLeaveEvent>(_ =>
                b.style.backgroundColor = new StyleColor(new Color(tint.r, tint.g, tint.b, enabled ? 0.85f : 0.30f)));
            return b;
        }

        private static VisualElement MakeLegendChip(Color color, string label)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.paddingLeft   = 6; row.style.paddingRight  = 6;
            row.style.paddingTop    = 2; row.style.paddingBottom = 2;
            row.style.backgroundColor = new StyleColor(new Color(color.r, color.g, color.b, 0.18f));
            T.Radius(row, 8f);
            T.Border(row, 1, new Color(color.r, color.g, color.b, 0.4f));
            row.pickingMode = PickingMode.Ignore;

            var dot = new VisualElement();
            dot.style.width  = 6; dot.style.height = 6;
            dot.style.backgroundColor = new StyleColor(color);
            T.Radius(dot, 3f);
            dot.style.marginRight = 4;
            row.Add(dot);

            var lbl = new Label(label);
            lbl.style.color    = new StyleColor(new Color(color.r * 1.4f, color.g * 1.4f, color.b * 1.4f));
            lbl.style.fontSize = 9;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(lbl);
            return row;
        }

        private static VisualElement LegendSpacer()
        {
            var s = new VisualElement();
            s.style.width = 4;
            s.pickingMode = PickingMode.Ignore;
            return s;
        }

        private static (string label, string axisHint) GetFaceMeta(CubeFace face)
        {
            foreach (var f in FACES) if (f.face == face) return (f.label, f.axisHint);
            return (face.ToString(), "");
        }

        private static Color DirectionColor(PortDirection dir) => dir switch
        {
            PortDirection.Input  => ColInput,
            PortDirection.Output => ColOutput,
            _                    => ColNone
        };

        /// <summary>
        /// Convert the optional allow-list into a string array used by the
        /// runtime <see cref="DropdownField"/>. Always includes the current
        /// type so existing data is never silently rewritten.
        /// </summary>
        private static string[] BuildTypeOptions(PortNetworkType[] allowed)
        {
            if (allowed == null || allowed.Length == 0)
                return AllNetworkTypeOptions;

            var list = new List<string>();
            // Always offer "Any" as a fallback so the player can clear a lock.
            bool anyIncluded = false;
            foreach (var a in allowed)
            {
                if (a == PortNetworkType.Any) { anyIncluded = true; break; }
            }
            if (anyIncluded) list.Add("Any");

            foreach (var a in allowed)
            {
                if (a == PortNetworkType.Any) continue;
                string s = a.ToString();
                if (!list.Contains(s)) list.Add(s);
            }
            return list.ToArray();
        }
    }
}
