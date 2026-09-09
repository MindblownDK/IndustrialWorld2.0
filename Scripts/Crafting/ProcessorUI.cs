// Assets/Scripts/VoxelEngine/Crafting/ProcessorUI.cs
//
// UI panels for the stationary fluid processors — Oil Refinery, Chemical Plant
// and the Advanced Distillation Tower. Shows item slots, internal fluid tanks,
// the active recipe + progress, and per-tank controls.
//
// 9.38.0-dev: every panel's recipe book scrolls (a long recipe list used to
// overflow the fixed machine panel and become unreachable), each tank row gains
// pour / draw / drain controls that work with a liquid canister in the hand,
// and the tower gets its own industrial panel with one analog dial per tank.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Industrial;
using VoxelEngine.Items;
using VoxelEngine.UI;
using GUI = VoxelEngine.GridSystem.UI.GridUIHelpers;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Crafting
{
    public static class ProcessorUI
    {
        // ── Oil Refinery (legacy machine — crude stays out of it since 9.38.0) ──
        public static VisualElement OilRefineryPanel(OilRefinery m, MachineUIs.SlotBuilder slot)
        {
            m.EnsureContainers();
            var p = BuildShell("⚗ Oil Refinery", m.IsOnline, m.Current, m.Progress01, m.CurrentWattage);
            FixWidth(p, 470f);

            FluidRow(p, m.FluidTanks, 150f, 108f);
            ItemSlots(p, "Inputs", m.inputC, slot);
            ItemSlots(p, "Outputs", m.outputC, slot);
            UpgradeSlots(p, "Upgrades", m.upgradeC, slot);
            RecipeBook(p, m.knownRecipes, m.Current, m.selectedRecipe,
                rec => { m.selectedRecipe = rec; GameUIController.Instance?.RefreshCurrentPanel(); });
            return p;
        }

        // ── Stationary Chemical Plant ────────────────────────────────────────
        public static VisualElement ChemicalPlantPanel(StationaryChemicalPlant m, MachineUIs.SlotBuilder slot)
        {
            m.EnsureContainers();
            var p = BuildShell("🧪 Chemical Plant", m.IsOnline, m.Current, m.Progress01, m.CurrentWattage);
            FixWidth(p, 470f);

            FluidRow(p, m.FluidTanks, 150f, 108f);
            ItemSlots(p, "Inputs", m.inputC, slot);
            ItemSlots(p, "Outputs", m.outputC, slot);
            RecipeBook(p, m.knownRecipes, m.Current, m.selectedRecipe,
                rec => { m.selectedRecipe = rec; GameUIController.Instance?.RefreshCurrentPanel(); });
            return p;
        }

        // ── Advanced Distillation Tower (9.38.0) — the dedicated column ──────
        public static VisualElement DistillationTowerPanel(AdvancedDistillationTower m, MachineUIs.SlotBuilder slot)
        {
            m.EnsureContainers();
            m.EnsureTanks();
            var p = BuildShell("🏭 Advanced Distillation Tower", m.IsOnline, m.Current, m.Progress01, m.CurrentWattage);
            FixWidth(p, 720f);

            // The plant panel is tall; everything below the header lives in a page
            // scroll so every tank, slot and recipe stays reachable on short screens.
            var page = new ScrollView(ScrollViewMode.Vertical);
            page.style.maxHeight = Mathf.Max(220f, Screen.height - 210f);
            page.style.marginTop = 2;
            T.StyleScroller(page);
            p.Add(page);

            page.Add(GUI.SectionTitle("Column Tanks — feed and the six cuts"));
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.justifyContent = Justify.Center;

            var feedCell = DialCell("FEED LINE", m.feed, "Crude for the Atmospheric Cut · Refined Oil for the Re-Run", 108f);
            feedCell.style.marginRight = 10f;
            row.Add(feedCell);

            var tanks = m.FluidTanks; // feed + 6 cuts in FractionSpecs order
            for (int i = 1; i < tanks.Count; i++)
                row.Add(DialCell("PRODUCT CUT", tanks[i], "Typed cut — drained and read by its own run", 92f));
            page.Add(row);
            page.Add(T.Spacer(2));
            page.Add(T.Muted("▲ Pour / ▼ Draw work with a liquid canister in your hand (one click ≈ 0.5 L). ⊘ drains the tank."));

            page.Add(T.Spacer(8));
            ItemSlots(page, "Item Slots", m.inputC, slot);
            ItemSlots(page, "Outputs", m.outputC, slot);
            RecipeBook(page, m.knownRecipes, m.Current, m.selectedRecipe,
                rec => { m.selectedRecipe = rec; GameUIController.Instance?.RefreshCurrentPanel(); }, ownScroll: false);
            return p;
        }

        // ── shared building blocks ──────────────────────────────────────────────
        private static void FixWidth(VisualElement p, float px)
        {
            // MachinePanel sizes the panel as a % of the screen and caps it at 44%,
            // which silently squeezed the 9.38 column UI on narrower windows. Pin
            // the width (and the cap) in pixels so the layout is the same everywhere.
            p.style.width = px;
            p.style.maxWidth = px;
            p.style.minWidth = Mathf.Min(px, 280f);
        }

        private static VisualElement BuildShell(string title, bool online,
            ProcessingRecipe current, float progress01, float watts)
        {
            var p = T.MachinePanel();
            var (hdr, _, _, _) = T.HeaderRow(title,
                !online ? "NO POWER" : current != null ? "PROCESSING" : "IDLE",
                !online ? T.AccentRed : current != null ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(watts), T.AccentGold));
            if (current != null)
            {
                p.Add(T.StatRow("⚙", "Recipe", current.GetDisplayName(), T.AccentCyan));
                var (bar, _) = T.ProgressBar(progress01, T.AccentGreen, 8, true);
                p.Add(bar);
            }
            p.Add(T.Spacer(6));
            return p;
        }

        /// <summary>
        /// A classic wrapped row of vertical tank gauges with the liquid's name
        /// on top and pour / draw / drain controls underneath (9.38.0).
        /// </summary>
        private static void FluidRow(VisualElement p, IReadOnlyList<MachineFluidTank> tanks,
            float gaugeWidth = 150f, float gaugeHeight = 108f)
        {
            if (tanks == null || tanks.Count == 0) return;
            p.Add(GUI.SectionTitle("Fluid Tanks"));
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.justifyContent = Justify.Center;
            foreach (var t in tanks)
            {
                if (t == null) continue;
                var col = new VisualElement();
                col.style.alignItems = Align.Center;
                col.style.marginLeft = 5;
                col.style.marginRight = 5;
                col.style.marginBottom = 6;
                col.Add(T.TankGauge(t.liquid.DisplayName(), t.Fill01, t.liquid.Color(),
                    $"{t.stored:0}/{t.capacity:0} L", gaugeWidth, gaugeHeight));
                AddTankButtons(col, t);
                row.Add(col);
            }
            p.Add(row);
            p.Add(T.Spacer(2));
            p.Add(T.Muted("▲ Pour / ▼ Draw work with a liquid canister in your hand (one click ≈ 0.5 L). ⊘ drains the tank."));
            p.Add(T.Spacer(6));
        }

        private static void AddTankButtons(VisualElement parent, MachineFluidTank t)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 3;

            var pour = T.SmallButton("▲", () => GameUIController.Instance?.MachineTankSwap(t, true), T.AccentGreen);
            pour.tooltip = "Pour 0.5 L from the liquid canister in your hand into this tank.";
            var draw = T.SmallButton("▼", () => GameUIController.Instance?.MachineTankSwap(t, false), T.AccentCyan);
            draw.tooltip = "Draw 0.5 L from this tank into an empty (or matching) canister in your hand.";
            var drain = T.SmallButton("⊘", () => { t.Drain(); GameUIController.Instance?.RefreshCurrentPanel(); }, T.AccentRed);
            drain.tooltip = "Empty this tank entirely.";

            row.Add(pour);
            row.Add(draw);
            row.Add(drain);
            parent.Add(row);
        }

        /// <summary>
        /// One analog dial card for the tower panel: a round industrial gauge face
        /// with tick ring and a needle that sweeps -135°..+135° over the tank fill,
        /// rimmed in the liquid's own colour so feed and cuts are unmistakable.
        /// </summary>
        private static VisualElement DialCell(string tankLabel, MachineFluidTank t, string hint, float dial = 92f)
        {
            float fill = t.Fill01;
            var liquid = t.liquid;
            Color liquidColor = liquid.Color();

            var col = new VisualElement();
            col.style.alignItems = Align.Center;
            col.style.marginTop = col.style.marginBottom =
            col.style.marginLeft = col.style.marginRight = 4;
            col.style.paddingTop = 6;
            col.style.paddingBottom = 6;
            col.style.paddingLeft = 8;
            col.style.paddingRight = 8;
            col.style.backgroundColor = new StyleColor(T.BgCard);
            if (!string.IsNullOrEmpty(hint)) col.tooltip = hint;
            T.Radius(col, 8);
            col.style.borderTopWidth = col.style.borderBottomWidth =
            col.style.borderLeftWidth = col.style.borderRightWidth = 1;
            var rim = new StyleColor(new Color(liquidColor.r, liquidColor.g, liquidColor.b, 0.35f));
            col.style.borderTopColor = col.style.borderBottomColor =
            col.style.borderLeftColor = col.style.borderRightColor = rim;

            // Liquid name (big, in a lightened liquid colour) + role label (small caps).
            var nameCol = Color.Lerp(liquidColor, Color.white, 0.30f);
            var nameLbl = new Label(t.liquid.DisplayName().ToUpper());
            nameLbl.style.color = new StyleColor(nameCol);
            nameLbl.style.fontSize = 11;
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLbl.style.letterSpacing = 0.8f;
            nameLbl.style.marginTop = 2;
            nameLbl.pickingMode = PickingMode.Ignore;
            col.Add(nameLbl);

            var roleLbl = new Label(tankLabel.ToUpper());
            roleLbl.style.color = new StyleColor(T.TextSecondary);
            roleLbl.style.fontSize = 8;
            roleLbl.style.letterSpacing = 1.4f;
            roleLbl.style.marginBottom = 3;
            roleLbl.pickingMode = PickingMode.Ignore;
            col.Add(roleLbl);

            // The dial itself.
            var dialRoot = new VisualElement();
            dialRoot.style.width = dial;
            dialRoot.style.height = dial;
            dialRoot.style.position = Position.Relative;
            dialRoot.pickingMode = PickingMode.Ignore;
            col.Add(dialRoot);

            // Face.
            var face = new VisualElement();
            face.style.position = Position.Absolute;
            face.style.left = 0; face.style.top = 0;
            face.style.width = dial; face.style.height = dial;
            face.style.backgroundColor = new StyleColor(new Color(0.045f, 0.05f, 0.075f));
            T.Radius(face, dial / 2f);
            face.style.borderTopWidth = face.style.borderBottomWidth =
            face.style.borderLeftWidth = face.style.borderRightWidth = 2f;
            var faceRim = new StyleColor(new Color(liquidColor.r, liquidColor.g, liquidColor.b, 0.55f));
            face.style.borderTopColor = face.style.borderBottomColor =
            face.style.borderLeftColor = face.style.borderRightColor = faceRim;
            face.pickingMode = PickingMode.Ignore;
            dialRoot.Add(face);

            // Inner ring.
            var ring = new VisualElement();
            ring.style.position = Position.Absolute;
            ring.style.left = 5; ring.style.top = 5;
            ring.style.width = dial - 10; ring.style.height = dial - 10;
            T.Radius(ring, (dial - 10) / 2f);
            ring.style.borderTopWidth = ring.style.borderBottomWidth =
            ring.style.borderLeftWidth = ring.style.borderRightWidth = 1f;
            ring.style.borderTopColor = ring.style.borderBottomColor =
            ring.style.borderLeftColor = ring.style.borderRightColor = new StyleColor(new Color(1f, 1f, 1f, 0.10f));
            ring.pickingMode = PickingMode.Ignore;
            dialRoot.Add(ring);

            // Tick ring: eleven ticks from -135° to +135°.
            float cx = dial / 2f, cy = dial / 2f;
            float tickR = dial / 2f - 11f;
            for (int i = 0; i < 11; i++)
            {
                float deg = -135f + 27f * i;
                float rad = deg * Mathf.Deg2Rad;
                var tick = new VisualElement();
                tick.style.position = Position.Absolute;
                tick.style.width = 2; tick.style.height = 7;
                tick.style.left = cx + tickR * Mathf.Sin(rad) - 1;
                tick.style.top  = cy - tickR * Mathf.Cos(rad) - 3.5f;
                tick.style.rotate = new Rotate(new Angle(deg, AngleUnit.Degree));
                tick.style.backgroundColor = new StyleColor(i % 9 == 0 ? T.TextMuted : new Color(1f, 1f, 1f, 0.22f));
                tick.pickingMode = PickingMode.Ignore;
                dialRoot.Add(tick);
            }

            // Needle: rotates about its bottom centre (the dial hub).
            var needle = new VisualElement();
            needle.style.position = Position.Absolute;
            needle.style.width = 3.5f;
            float needleLen = dial / 2f - 16f;
            needle.style.height = needleLen;
            needle.style.left = cx - 1.75f;
            needle.style.top = cy - needleLen;
            needle.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(100f));
            float ang = -135f + 270f * Mathf.Clamp01(fill);
            needle.style.rotate = new Rotate(new Angle(ang, AngleUnit.Degree));
            needle.style.backgroundColor = new StyleColor(T.AccentRed);
            needle.pickingMode = PickingMode.Ignore;
            dialRoot.Add(needle);

            // Hub cap in the liquid's colour.
            var hub = new VisualElement();
            hub.style.position = Position.Absolute;
            hub.style.width = 12; hub.style.height = 12;
            hub.style.left = cx - 6; hub.style.top = cy - 6;
            hub.style.backgroundColor = new StyleColor(liquidColor);
            T.Radius(hub, 6f);
            hub.style.borderTopWidth = hub.style.borderBottomWidth =
            hub.style.borderLeftWidth = hub.style.borderRightWidth = 1.5f;
            hub.style.borderTopColor = hub.style.borderBottomColor =
            hub.style.borderLeftColor = hub.style.borderRightColor = new StyleColor(new Color(0.05f, 0.05f, 0.06f));
            hub.pickingMode = PickingMode.Ignore;
            dialRoot.Add(hub);

            // Digital readout under the dial.
            var val = new Label($"{t.stored:0} / {t.capacity:0} L");
            val.style.color = new StyleColor(fill <= 0.001f ? T.TextMuted : T.TextSecondary);
            val.style.fontSize = 10;
            val.style.unityFontStyleAndWeight = FontStyle.Bold;
            val.style.marginTop = 2;
            val.style.marginBottom = 2;
            val.pickingMode = PickingMode.Ignore;
            col.Add(val);

            AddTankButtons(col, t);
            return col;
        }

        private static void ItemSlots(VisualElement p, string label, ItemContainer c, MachineUIs.SlotBuilder slot)
        {
            if (c == null) return;
            p.Add(GUI.SectionTitle(label));
            p.Add(GUI.WeightHeader(MassUtil.ContainerMass(c)));
            var grid = T.SlotGrid(c.Size);
            for (int i = 0; i < c.Size; i++) grid.Add(slot(c, i, c.GetSlot(i), false, true));
            p.Add(grid);
        }

        private static void UpgradeSlots(VisualElement p, string label, ItemContainer c, MachineUIs.SlotBuilder slot)
        {
            if (c == null) return;
            p.Add(GUI.SectionTitle(label));
            var grid = T.SlotGrid(c.Size);
            for (int i = 0; i < c.Size; i++) grid.Add(slot(c, i, c.GetSlot(i), false, true));
            p.Add(grid);
        }

        /// <summary>Recipe list inside a scrollable region — long recipe sets can
        /// never overflow the machine panel again (9.38.0).</summary>
        private static void RecipeBook(VisualElement p, List<ProcessingRecipe> recipes,
            ProcessingRecipe current, ProcessingRecipe selected, System.Action<ProcessingRecipe> onSelect,
            bool ownScroll = true)
        {
            if (recipes == null) return;
            p.Add(GUI.SectionTitle("Recipes  (click to select · Auto by default)"));

            VisualElement host = p;
            if (ownScroll)
            {
                // 9.38.0: the recipe list lives in its own scroll region, so a long
                // recipe set can never overflow the machine panel and become
                // unreachable — the bug the playtest screenshot showed.
                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.style.maxHeight = 218;
                scroll.style.marginBottom = 6;
                T.StyleScroller(scroll, T.AccentCyan);
                p.Add(scroll);
                host = scroll.contentContainer;
            }

            // "Auto" option clears the lock.
            host.Add(RecipeRow("⟳  Auto (first available)", "", null, default, selected == null, current != null && selected == null,
                () => onSelect(null)));

            foreach (var r in recipes)
            {
                if (r == null) continue;
                var captured = r;
                // Icon: first item output's sprite, tinted chip as fallback.
                Sprite rIcon = null; Color rTint = T.TextMuted;
                if (r.outputs != null && r.outputs.Length > 0 && r.outputs[0].item != null)
                {
                    rIcon = r.outputs[0].item.icon;
                    rTint = r.outputs[0].item.iconTint;
                }
                host.Add(RecipeRow(r.GetDisplayName(), Summary(r), rIcon, rTint, selected == r, current == r,
                    () => onSelect(captured)));
            }
        }

        private static VisualElement RecipeRow(string name, string summary, Sprite icon, Color iconTint, bool selected, bool active, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.style.flexDirection = FlexDirection.Column;
            btn.style.alignItems = Align.FlexStart;
            btn.style.marginBottom = 2; btn.style.paddingTop = 4; btn.style.paddingBottom = 4;
            btn.style.paddingLeft = 8;
            btn.style.backgroundColor = new StyleColor(selected
                ? new Color(0.18f, 0.72f, 0.88f, 0.28f)
                : new Color(0.12f, 0.14f, 0.18f, 0.95f));
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.pickingMode = PickingMode.Ignore;
            if (icon != null || iconTint != default)
            {
                var iconSlot = new VisualElement();
                iconSlot.style.width = 26; iconSlot.style.height = 26;
                iconSlot.style.marginRight = 7;
                iconSlot.style.alignItems = Align.Center;
                iconSlot.style.justifyContent = Justify.Center;
                iconSlot.style.backgroundColor = new StyleColor(new Color(0.09f, 0.10f, 0.13f, 0.9f));
                T.Radius(iconSlot, 4);
                iconSlot.pickingMode = PickingMode.Ignore;
                if (icon != null)
                {
                    var iconImg = new Image { sprite = icon };
                    iconImg.scaleMode = ScaleMode.ScaleToFit;
                    iconImg.style.width = 22; iconImg.style.height = 22;
                    iconImg.pickingMode = PickingMode.Ignore;
                    iconSlot.Add(iconImg);
                }
                else
                {
                    var chip = new VisualElement();
                    chip.style.width = 16; chip.style.height = 16;
                    chip.style.backgroundColor = new StyleColor(iconTint);
                    T.Radius(chip, 3);
                    chip.pickingMode = PickingMode.Ignore;
                    iconSlot.Add(chip);
                }
                titleRow.Add(iconSlot);
            }
            var title = new Label((selected ? "◉ " : "○ ") + name);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(active ? T.AccentGreen : selected ? T.AccentCyan : new Color(0.85f,0.88f,0.92f));
            title.pickingMode = PickingMode.Ignore;
            titleRow.Add(title);
            btn.Add(titleRow);
            if (!string.IsNullOrEmpty(summary))
            {
                var sub = new Label(summary);
                sub.style.fontSize = 10; sub.style.color = new StyleColor(new Color(0.6f,0.64f,0.7f));
                btn.Add(sub);
            }
            return btn;
        }

        private static string Summary(ProcessingRecipe r)
        {
            var ins = new List<string>();
            if (r.HasFluidInputs) foreach (var f in r.fluidInputs) ins.Add($"{f.litres:0}L {f.liquid.DisplayName()}");
            if (r.HasItemInputs)  foreach (var i in r.inputs) if (i.item != null) ins.Add($"{i.count} {i.item.displayName}");
            var outs = new List<string>();
            if (r.HasFluidOutputs) foreach (var f in r.fluidOutputs) outs.Add($"{f.litres:0}L {f.liquid.DisplayName()}");
            if (r.HasItemOutputs)  foreach (var o in r.outputs) if (o.item != null) outs.Add($"{o.count} {o.item.displayName}");
            return string.Join(" + ", ins) + " → " + string.Join(" + ", outs);
        }
    }
}
