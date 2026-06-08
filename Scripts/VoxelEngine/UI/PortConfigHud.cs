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
// Disambiguate from UnityEngine.FilterMode (texture filter enum).
using FilterMode = VoxelEngine.Transport.FilterMode;

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
        public static bool IsAnyDropdownOpen { get; set; }

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

        /// <summary>
        /// Build the premium item-port widget for a <see cref="Chest"/> (or any
        /// item endpoint exposing a PortConfig + per-face filters).
        /// OUTPUT faces push the chest's contents into adjacent pipes; INPUT faces
        /// accept pushed items. Each face carries an item whitelist (empty = all).
        /// </summary>
        public static VisualElement BuildItemPorts(Chest chest, Action onChanged = null)
        {
            return BuildItemPorts(chest as IItemPortHost,
                                  chest != null ? chest.Routing : null, onChanged);
        }

        /// <summary>
        /// Build the premium item-port widget for ANY <see cref="IItemPortHost"/>
        /// (chest, furnace, processor…). Each face cycles None → Input → Output,
        /// can be ROUTED to a specific internal container via a dropdown, and
        /// carries a searchable item whitelist (empty = all).
        /// </summary>
        public static VisualElement BuildItemPorts(IItemPortHost host,
                                                   ItemPortRouting routing,
                                                   Action onChanged = null)
        {
            if (host == null || host.PortConfig == null || routing == null)
                return T.Muted("No item ports on this machine.");

            var config = host.PortConfig;
            config.EnsureAllFaces();

            var root = new VisualElement();
            root.style.marginTop = 8;
            root.style.marginBottom = 4;

            // Header + legend.
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

            // Distribution mode toggle — only meaningful when >1 OUTPUT face.
            root.Add(BuildDistributionToggle(routing, onChanged));

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
                var fresh = BuildItemFaceCard(host, routing, config, face,
                    () => { RebuildCard(face); onChanged?.Invoke(); });
                parent.Insert(idx, fresh);
                cardRefs[face] = fresh;
            }

            foreach (var (face, _, _) in FACES)
            {
                var card = BuildItemFaceCard(host, routing, config, face,
                    () => { RebuildCard(face); onChanged?.Invoke(); });
                cardRefs[face] = card;
                grid.Add(card);
            }

            var hint = T.Muted("Click a face to cycle None → Input → Output.  " +
                               "OUTPUT pushes items into adjacent pipes; INPUT accepts them.  " +
                               "Pick the container and add item filters per face.");
            hint.style.marginTop = 10;
            root.Add(hint);

            return root;
        }

        // Round-Robin / Priority toggle for competing OUTPUT faces.
        private static VisualElement BuildDistributionToggle(ItemPortRouting routing, Action onChanged)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 8;

            var lbl = new Label("OUTPUT SHARING");
            lbl.style.color = new StyleColor(T.TextMuted);
            lbl.style.fontSize = 9;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.letterSpacing = 1f;
            lbl.style.flexGrow = 1;
            row.Add(lbl);

            Button rr = null, pr = null;
            void Restyle()
            {
                bool isRR = routing.distribution == DistributionMode.RoundRobin;
                StylePill(rr, isRR);
                StylePill(pr, !isRR);
            }
            rr = MiniPill("ROUND-ROBIN", () => { routing.distribution = DistributionMode.RoundRobin; Restyle(); onChanged?.Invoke(); });
            pr = MiniPill("PRIORITY",    () => { routing.distribution = DistributionMode.Priority;   Restyle(); onChanged?.Invoke(); });
            row.Add(rr); row.Add(pr);
            Restyle();
            return row;
        }

        private static Button MiniPill(string text, Action onClick)
        {
            var b = new Button { text = text };
            b.style.height = 22; b.style.fontSize = 9;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.marginLeft = 4;
            b.style.paddingLeft = 8; b.style.paddingRight = 8;
            T.Radius(b, 6f);
            b.clicked += onClick;
            return b;
        }

        private static void StylePill(Button b, bool active)
        {
            if (b == null) return;
            var c = T.AccentCyan;
            b.style.backgroundColor = new StyleColor(new Color(c.r, c.g, c.b, active ? 0.85f : 0.14f));
            b.style.color = new StyleColor(active ? Color.white : T.TextSecondary);
            T.Border(b, 1, new Color(c.r, c.g, c.b, active ? 0.7f : 0.3f));
        }

        private static VisualElement BuildItemFaceCard(IItemPortHost host, ItemPortRouting routing,
                                                       PortConfig config, CubeFace face, Action inlineChanged)
        {
            var meta    = GetFaceMeta(face);
            var dir     = config.GetDirection(face);
            var enabled = config.IsFaceEnabled(face);
            var bgTint  = DirectionColor(dir);

            // Strict 2-column grid: each card is exactly half the row width and
            // never grows/shrinks; inner-margin handles the gutter.
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

            if (enabled && dir != PortDirection.None)
            {
                // Container routing dropdown — only when the host has >1 container.
                var containers = host.GetPortContainers();
                if (containers != null && containers.Count > 1)
                    inner.Add(BuildContainerDropdown(routing, containers, face, dir, inlineChanged));

                // Searchable item filter.
                inner.Add(BuildFilterStrip(routing, face, inlineChanged));
            }

            return card;
        }

        // ── Container routing dropdown ──────────────────────────────
        private static VisualElement BuildContainerDropdown(ItemPortRouting routing,
            IReadOnlyList<ItemPortContainer> containers, CubeFace face,
            PortDirection dir, Action inlineChanged)
        {
            var row = new VisualElement();
            row.style.marginTop = 8;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.pickingMode = PickingMode.Ignore;

            var lbl = new Label("SLOT");
            lbl.style.color = new StyleColor(T.TextMuted);
            lbl.style.fontSize = 9;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.letterSpacing = 1f;
            lbl.style.marginRight = 6;
            row.Add(lbl);

            // Offer only containers valid for this direction.
            var choices = new List<string>();
            var indexMap = new List<int>();
            for (int i = 0; i < containers.Count; i++)
            {
                bool ok = dir == PortDirection.Output ? containers[i].CanOutput : containers[i].CanInput;
                if (!ok) continue;
                choices.Add(containers[i].Name);
                indexMap.Add(i);
            }
            if (choices.Count == 0) { row.Add(T.Muted("—")); return row; }

            int curContainer = routing.GetFaceContainer(face);
            int curChoice = indexMap.IndexOf(curContainer);
            if (curChoice < 0) { curChoice = 0; routing.SetFaceContainer(face, indexMap[0]); }

            var dd = new DropdownField { choices = choices, index = curChoice };
            dd.style.flexGrow = 1; dd.style.height = 22; dd.style.fontSize = 11;
            dd.RegisterCallback<PointerDownEvent>(_ => IsAnyDropdownOpen = true);
            dd.RegisterCallback<FocusOutEvent>(_ =>
                dd.schedule.Execute(() => IsAnyDropdownOpen = false).StartingIn(50));
            dd.RegisterValueChangedCallback(evt =>
            {
                int ix = choices.IndexOf(evt.newValue);
                if (ix >= 0) routing.SetFaceContainer(face, indexMap[ix]);
                IsAnyDropdownOpen = false;
                inlineChanged?.Invoke();
            });
            row.Add(dd);
            return row;
        }

        // ── Searchable item-filter strip (inline) ───────────────────
        private static VisualElement BuildFilterStrip(ItemPortRouting routing, CubeFace face, Action inlineChanged)
        {
            var wrap = new VisualElement();
            wrap.style.marginTop = 8;

            // Header: FILTER label + current mode badge (WHITELIST / BLACKLIST).
            var hdr = new VisualElement();
            hdr.style.flexDirection = FlexDirection.Row;
            hdr.style.alignItems = Align.Center;
            hdr.pickingMode = PickingMode.Ignore;
            var lbl = new Label("FILTER");
            lbl.style.color = new StyleColor(T.TextMuted);
            lbl.style.fontSize = 9;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.letterSpacing = 1f;
            lbl.style.flexGrow = 1;
            hdr.Add(lbl);
            if (routing.HasFilter(face))
            {
                bool wl = routing.GetFilterMode(face) == FilterMode.Whitelist;
                var modeCol = wl ? T.AccentGreen : T.AccentRed;
                var badge = new Label(wl ? "WHITELIST" : "BLACKLIST");
                badge.style.fontSize = 8;
                badge.style.unityFontStyleAndWeight = FontStyle.Bold;
                badge.style.color = new StyleColor(modeCol);
                badge.style.paddingLeft = 5; badge.style.paddingRight = 5;
                badge.style.paddingTop = 1; badge.style.paddingBottom = 1;
                badge.style.backgroundColor = new StyleColor(new Color(modeCol.r, modeCol.g, modeCol.b, 0.16f));
                T.Radius(badge, 6f);
                hdr.Add(badge);
            }
            else
            {
                var allLbl = new Label("ALL");
                allLbl.style.fontSize = 8;
                allLbl.style.color = new StyleColor(T.TextMuted);
                hdr.Add(allLbl);
            }
            wrap.Add(hdr);

            // Existing filter chips (each with a delete button).
            var chips = new VisualElement();
            chips.style.flexDirection = FlexDirection.Row;
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.marginTop = 4;
            wrap.Add(chips);
            foreach (var item in routing.GetFilter(face))
                chips.Add(MakeFilterChip(item, () =>
                {
                    routing.RemoveFilter(face, item);
                    inlineChanged?.Invoke();
                }));

            // ＋ Add Filter — opens the modal prompt (search / drop / shift-click).
            var add = new Button { text = "＋  Add Filter" };
            add.style.marginTop = 6; add.style.height = 24; add.style.fontSize = 10;
            add.style.color = new StyleColor(T.TextSecondary);
            add.style.backgroundColor = new StyleColor(new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.16f));
            T.Radius(add, 7f);
            T.Border(add, 1, new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.40f));
            add.clicked += () =>
            {
                var uiRoot = add.panel?.visualTree ?? add;
                ItemFilterDialog.Open(uiRoot, routing, face, onChanged: () => inlineChanged?.Invoke());
            };
            wrap.Add(add);

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
