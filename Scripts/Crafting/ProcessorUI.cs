// Assets/Scripts/VoxelEngine/Crafting/ProcessorUI.cs
//
// UI panels for the stationary fluid processors — Oil Refinery, Chemical Plant
// and the Distillation Plant. Shows item slots, internal fluid tanks, the active
// recipe + progress, and per-tank controls.
//
// 9.38.0-dev: every panel's recipe book scrolls by name (a long recipe list used
// to overflow the fixed machine panel and become unreachable), each tank row has
// pour / draw / drain controls that work with a liquid canister in the hand, tank
// captions name what is actually inside the tank, slot cards and gauges keep their
// size instead of being squeezed by the panel, and the plant gets its own
// industrial panel — one analog dial per tank, updated in place every frame so
// the panel never has to rebuild itself under the player's scroll.

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
        /// <summary>
        /// Element handles for an open machine panel whose readouts move on their own.
        /// The controller updates these IN PLACE every frame (dials, readouts, status pill,
        /// progress bar) instead of rebuilding the panel — a rebuild re-created the page
        /// ScrollView and threw the player back to the top while scrolling (the 9.38
        /// Distillation Plant report, repeated on the Catalytic Cracker in 9.57.1-dev).
        /// Each panel fills in the fields it owns and leaves the rest null; Reset() drops
        /// every handle when the panel closes, so a stale label can never be written to.
        /// </summary>
        public sealed class MachinePanelLive
        {
            public VisualElement root;
            // Header + batch progress (every machine panel built through BuildShell).
            public VisualElement progressFill;
            public VisualElement statusPill;
            public Label statusLabel;
            public Label wattLabel;
            // Distillation Plant dial ring.
            public readonly List<int> tankIndices = new();
            public readonly List<LiquidType> captionTypes = new();
            public readonly List<VisualElement> needles = new();
            public readonly List<Label> values = new();
            // Straight gauges + the tanks behind them (Catalytic Cracker and any panel
            // that draws its tanks with FluidRow). The tank references themselves are
            // held so a tick never has to call FluidTanks — that property builds a new
            // array on every read and the tick runs every frame.
            public readonly List<MachineFluidTank> gaugeTanks = new();
            public readonly List<VisualElement> gaugeFills = new();
            public readonly List<Label> gaugeValues = new();
            public readonly List<LiquidType> gaugeCaptionTypes = new();
            // Catalytic Cracker kinetics readouts.
            public Label coreTempValue;
            public Label catalystValue;
            public Label efficiencyValue;
            public VisualElement efficiencyFill;

            public void Reset()
            {
                root = null;
                progressFill = null;
                statusPill = null;
                statusLabel = null;
                wattLabel = null;
                tankIndices.Clear();
                captionTypes.Clear();
                needles.Clear();
                values.Clear();
                gaugeTanks.Clear();
                gaugeFills.Clear();
                gaugeValues.Clear();
                gaugeCaptionTypes.Clear();
                coreTempValue = null;
                catalystValue = null;
                efficiencyValue = null;
                efficiencyFill = null;
            }
        }

        /// <summary>
        /// What to print on a tank's caption. An auto-typed tank that is empty has no
        /// contents, so it must not claim a liquid it has not been given yet — it
        /// reads "Empty" until something is poured in. Typed tanks (the plant's six
        /// products) name their liquid, because holding that one liquid IS their job.
        /// </summary>
        private static string TankCaption(MachineFluidTank t)
        {
            if (t == null) return "Tank";
            if (t.autoType && t.IsEmpty) return "Empty";
            return t.liquid.DisplayName();
        }
        // ── Oil Refinery (legacy machine — crude stays out of it since 9.38.0) ──
        public static VisualElement OilRefineryPanel(OilRefinery m, MachineUIs.SlotBuilder slot)
        {
            m.EnsureContainers();
            var p = BuildShell("⚗ Oil Refinery", m.IsOnline, m.Current, m.Progress01, m.CurrentWattage);
            FixWidth(p, 470f);

            FluidRow(p, m.FluidTanks, 150f, 108f);
            ItemSlots(p, "Inputs", m.inputC, slot);
            ItemSlots(p, "Outputs", m.outputC, slot);
            UpgradeSlots(p, "Upgrades", m.upgradeC, slot,
                "Universal machine modules only — Machine Speed Module (x1.25 throughput) and Machine Efficiency Module (x0.8 power draw). "
                + "The Quarry's Range / Speed / Efficiency Upgrades and the maritime engine modules are specific to those machines and do nothing here.");
            RecipeBook(p, m.knownRecipes, m.Current, m.selectedRecipe,
                rec => { m.selectedRecipe = rec; GameUIController.Instance?.RefreshCurrentPanel(); },
                scrollName: "RefineryRecipeBook");
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
                rec => { m.selectedRecipe = rec; GameUIController.Instance?.RefreshCurrentPanel(); },
                scrollName: "ChemicalPlantRecipeBook");
            return p;
        }

        // ── Distillation Plant (9.38.0) — the dedicated petroleum plant ──────
        public static VisualElement DistillationPlantPanel(DistillationPlant m, MachineUIs.SlotBuilder slot,
            MachinePanelLive live = null)
        {
            m.EnsureContainers();
            m.EnsureTanks();
            var p = BuildShell("🏭 Distillation Plant", m.IsOnline, m.Current, m.Progress01, m.CurrentWattage, live);
            FixWidth(p, 760f);

            // Everything below the header lives in a page scroll, so every dial, slot
            // and recipe stays reachable no matter how short the window is. The scroll
            // takes the remaining panel height (flexGrow) instead of a screen-size
            // guess, and it carries the controller's persistent name so its offset
            // survives the rebuilds that container changes still trigger.
            var page = new ScrollView(ScrollViewMode.Vertical) { name = "DistillationPlantPage" };
            page.style.marginTop = 2;
            page.style.flexGrow = 1;
            page.style.flexShrink = 1;
            page.style.minHeight = 180;   // never collapse to nothing on a very short window
            T.StyleScroller(page);
            p.Add(page);

            page.Add(GUI.SectionTitle("Plant Tanks — feed and the six products"));
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.justifyContent = Justify.Center;

            // Feed first (slightly larger dial), then the six products in
            // FractionSpecs order — heaviest last, matching the plant's pipe row.
            var feedCell = DialCell("FEED LINE", m.feed, "Crude for the Atmospheric Cut · Refined Oil for the Re-Run", 108f, live, 0);
            feedCell.style.marginRight = 10f;
            row.Add(feedCell);

            var tanks = m.FluidTanks; // feed + 6 products in FractionSpecs order
            for (int i = 1; i < tanks.Count; i++)
                row.Add(DialCell("PRODUCT", tanks[i], "Typed product tank — drained and read by its own run", 92f, live, i));
            page.Add(row);
            page.Add(T.Spacer(2));
            page.Add(T.Muted("▲ Pour / ▼ Draw work with a liquid canister in your hand (one click ≈ 0.5 L). ⊘ drains the tank. The dials on the plant move with these tanks."));

            page.Add(T.Spacer(8));
            ItemSlots(page, "Item Slots", m.inputC, slot);
            ItemSlots(page, "Outputs", m.outputC, slot);
            RecipeBook(page, m.knownRecipes, m.Current, m.selectedRecipe,
                rec => { m.selectedRecipe = rec; GameUIController.Instance?.RefreshCurrentPanel(); }, ownScroll: false);
            return p;
        }

        // ── Catalytic Cracker & Reformer (9.40.0 / Step 71) ─────────────────
        /// <summary>
        /// Core temperature tint: blue while the reactor is cold, gold while it is being
        /// brought up, orange once it is at cracking heat. Shared with the live tick so a
        /// readout can never drift from what a rebuild would have drawn.
        /// </summary>
        public static Color CrackerTempColor(float temperatureC)
            => temperatureC > 300f ? T.AccentOrange : (temperatureC > 100f ? T.AccentGold : T.AccentCyan);

        /// <summary>Catalyst bed tint: green while the bed is healthy, red once it is spent.</summary>
        public static Color CrackerCatalystColor(float bedPercent)
            => bedPercent > 50f ? T.AccentGreen : (bedPercent > 20f ? T.AccentAmber : T.AccentRed);

        public static VisualElement CatalyticCrackerPanel(CatalyticCracker m, MachineUIs.SlotBuilder slot,
            MachinePanelLive live = null)
        {
            m.EnsureContainers();
            var p = BuildShell("🔥 Catalytic Cracker & Reformer", m.IsOnline, m.Current, m.Progress01, m.CurrentWattage, live);
            FixWidth(p, 520f);

            var page = new ScrollView(ScrollViewMode.Vertical) { name = "CatalyticCrackerPage" };
            page.style.marginTop = 2;
            page.style.flexGrow = 1;
            page.style.flexShrink = 1;
            page.style.minHeight = 180;
            T.StyleScroller(page);
            p.Add(page);

            // Reactor Kinetics status box
            page.Add(GUI.SectionTitle("Reaction Kinetics & Catalyst Bed"));
            var kinRow = new VisualElement();
            kinRow.style.flexDirection = FlexDirection.Row;
            kinRow.style.justifyContent = Justify.SpaceBetween;
            kinRow.style.marginBottom = 4;

            var tempRow = T.StatRow("🌡", "Core Temp", $"{m.reactorTemperatureC:0}°C / {m.targetOperatingTempC:0}°C", CrackerTempColor(m.reactorTemperatureC));
            kinRow.Add(tempRow);

            var catRow = T.StatRow("🧪", "Catalyst Bed", $"{m.catalystBedPercent:0}%", CrackerCatalystColor(m.catalystBedPercent));
            kinRow.Add(catRow);
            page.Add(kinRow);
            if (live != null)
            {
                // Handles so the reactor can be watched warming up: the tick moves these
                // labels and bars in place, which leaves the page ScrollView (and the
                // player's scroll position) exactly where it was.
                live.coreTempValue = FindValueLabel(tempRow);
                live.catalystValue = FindValueLabel(catRow);
            }

            var (effBar, effFill) = T.ProgressBar(m.CrackingEfficiency01, T.AccentCyan, 8, true);
            effBar.style.marginTop = 2; effBar.style.marginBottom = 6;
            var effRow = T.StatRow("⚡", "Cracking Efficiency", $"{m.CrackingEfficiency01 * 100f:0}%", T.AccentCyan);
            page.Add(effRow);
            page.Add(effBar);
            if (live != null)
            {
                live.efficiencyValue = FindValueLabel(effRow);
                live.efficiencyFill = effFill;
            }

            // 4 Fluid Tanks (2 Inputs + 2 Outputs)
            FluidRow(page, m.FluidTanks, 115f, 96f, live);

            page.Add(T.Spacer(6));
            ItemSlots(page, "Catalysts & Feed Additives", m.inputC, slot);
            ItemSlots(page, "Synthesised Products", m.outputC, slot);

            page.Add(T.Spacer(6));
            RecipeBook(page, m.knownRecipes, m.Current, m.Current,
                rec => { m.SelectRecipe(rec); GameUIController.Instance?.RefreshCurrentPanel(); },
                ownScroll: false);

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
            ProcessingRecipe current, float progress01, float watts, MachinePanelLive live = null)
        {
            var p = T.MachinePanel();
            var (hdr, _, pill, pillLabel) = T.HeaderRow(title,
                !online ? "NO POWER" : current != null ? "PROCESSING" : "IDLE",
                !online ? T.AccentRed : current != null ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            var wattRow = T.StatRow("⚡", "Power Use", PowerFormat.Watts(watts), T.AccentGold);
            p.Add(wattRow);
            if (live != null)
            {
                // The live panel keeps the header + wattage honest without a rebuild.
                live.statusPill = pill;
                live.statusLabel = pillLabel;
                live.wattLabel = FindValueLabel(wattRow);
            }
            if (current != null)
            {
                p.Add(T.StatRow("⚙", "Recipe", current.GetDisplayName(), T.AccentCyan));
                var (bar, fill) = T.ProgressBar(progress01, T.AccentGreen, 8, true);
                p.Add(bar);
                if (live != null) live.progressFill = fill;
            }
            p.Add(T.Spacer(6));
            return p;
        }

        /// <summary>The value cell of a T.StatRow (its last label) — used by the live
        /// panel tick to rewrite a readout without rebuilding the row.</summary>
        private static Label FindValueLabel(VisualElement statRow)
        {
            if (statRow == null) return null;
            Label last = null;
            foreach (var l in statRow.Query<Label>().ToList()) last = l;
            return last;
        }

        /// <summary>
        /// A classic wrapped row of vertical tank gauges with the liquid's name
        /// on top and pour / draw / drain controls underneath (9.38.0).
        /// </summary>
        private static void FluidRow(VisualElement p, IReadOnlyList<MachineFluidTank> tanks,
            float gaugeWidth = 150f, float gaugeHeight = 108f, MachinePanelLive live = null)
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
                col.style.flexShrink = 0;
                var gauge = T.TankGaugeWithParts(TankCaption(t), t.Fill01, t.liquid.Color(),
                    $"{t.stored:0}/{t.capacity:0} L", gaugeWidth, gaugeHeight);
                // The caption names what is actually in the tank; the role lives in
                // the tooltip so "Fluid In" carries a value the player cannot use.
                gauge.column.tooltip = $"{t.label} — {(t.autoType && t.IsEmpty ? "empty, adopts the first liquid poured in" : t.liquid.DisplayName())}";
                if (live != null)
                {
                    live.gaugeTanks.Add(t);
                    live.gaugeFills.Add(gauge.fill);
                    live.gaugeValues.Add(gauge.value);
                    live.gaugeCaptionTypes.Add(t.liquid);
                }
                col.Add(gauge.column);
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
        private static VisualElement DialCell(string tankLabel, MachineFluidTank t, string hint, float dial = 92f,
            MachinePanelLive live = null, int tankIndex = -1)
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
            col.style.flexShrink = 0;
            if (!string.IsNullOrEmpty(hint)) col.tooltip = hint;
            T.Radius(col, 8);
            col.style.borderTopWidth = col.style.borderBottomWidth =
            col.style.borderLeftWidth = col.style.borderRightWidth = 1;
            var rim = new StyleColor(new Color(liquidColor.r, liquidColor.g, liquidColor.b, 0.35f));
            col.style.borderTopColor = col.style.borderBottomColor =
            col.style.borderLeftColor = col.style.borderRightColor = rim;

            // Liquid name (big, in a lightened liquid colour) + role label (small caps).
            // An empty auto-typed tank reads EMPTY: it has not adopted a liquid yet.
            bool emptyAuto = t.autoType && t.IsEmpty;
            var nameCol = emptyAuto ? T.TextSecondary : Color.Lerp(liquidColor, Color.white, 0.30f);
            var nameLbl = new Label(TankCaption(t).ToUpper());
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

            // Live handles (Distillation Plant only): needle sweep + readout.
            if (live != null)
            {
                live.needles.Add(needle);
                live.tankIndices.Add(tankIndex < 0 ? 0 : tankIndex);
                live.captionTypes.Add(t.liquid);
            }

            // Digital readout under the dial.
            var val = new Label($"{t.stored:0} / {t.capacity:0} L");
            val.style.color = new StyleColor(fill <= 0.001f ? T.TextMuted : T.TextSecondary);
            val.style.fontSize = 10;
            val.style.unityFontStyleAndWeight = FontStyle.Bold;
            val.style.marginTop = 2;
            val.style.marginBottom = 2;
            val.pickingMode = PickingMode.Ignore;
            col.Add(val);
            if (live != null) live.values.Add(val);

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

        private static void UpgradeSlots(VisualElement p, string label, ItemContainer c, MachineUIs.SlotBuilder slot,
            string hint = null)
        {
            if (c == null) return;
            p.Add(GUI.SectionTitle(label));
            var grid = T.SlotGrid(c.Size);
            for (int i = 0; i < c.Size; i++) grid.Add(slot(c, i, c.GetSlot(i), false, true));
            p.Add(grid);
            if (!string.IsNullOrEmpty(hint)) p.Add(T.Muted(hint));
        }

        /// <summary>Recipe list inside a scrollable region — long recipe sets can
        /// never overflow the machine panel again (9.38.0).</summary>
        private static void RecipeBook(VisualElement p, List<ProcessingRecipe> recipes,
            ProcessingRecipe current, ProcessingRecipe selected, System.Action<ProcessingRecipe> onSelect,
            bool ownScroll = true, string scrollName = null)
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
                // A NAME is what lets GameUIController carry the scroll offset across
                // the live panel rebuilds; an unnamed view always came back at the top.
                if (!string.IsNullOrEmpty(scrollName)) scroll.name = scrollName;
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
