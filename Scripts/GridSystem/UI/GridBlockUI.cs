// Assets/Scripts/VoxelEngine/GridSystem/UI/GridBlockUI.cs
//
// Builds the right-hand machine panel for an interactable grid block. Hooked into
// GameUIController's existing panel system, so it gets the real drag/drop slot
// widget (BuildSlot) and live container watching for free.
//
// Handles: Liquid Tank, Gas Tank, H2/O2 Generator, Battery, Cargo Container.
// (Chemical Plant / Refinery panels arrive in Phase 3.)

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.UI;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.GridSystem.UI
{
    public static class GridBlockUI
    {
        public static VisualElement BuildPanel(GridBlock block, MachineUIs.SlotBuilder slot)
        {
            // Maritime blocks have their own UI system.
            var maritimePanel = VoxelEngine.Maritime.MaritimeBlockUI.BuildPanel(block, slot);
            if (maritimePanel != null) return maritimePanel;

            var ledStrip = block != null ? block.GetComponent<VoxelEngine.Simulation.LEDStrip>() : null;
            if (ledStrip != null) return LEDStripPanel(ledStrip, block);

            switch (block)
            {
                case GridLiquidTank lt:     return LiquidTankPanel(lt);
                case GridGasTank gt:        return GasTankPanel(gt);
                case GridH2O2Generator h2:  return H2O2Panel(h2, slot);
                case GridBattery bat:       return BatteryPanel(bat);
                case GridCargoContainer cc: return CargoPanel(cc, slot);
                case GridWeapon gw:         return WeaponPanel(gw, slot);
                case GridRefinery rf:       return ProcessorPanel("⚗ Ship Refinery", rf.Current, rf.Progress01, rf.PowerDraw, rf.knownRecipes, rf.Grid, rf.selectedRecipe, r => rf.selectedRecipe = r);
                case GridChemicalPlant cp:  return ProcessorPanel("🧪 Ship Chemical Plant", cp.Current, cp.Progress01, cp.PowerDraw, cp.knownRecipes, cp.Grid, cp.selectedRecipe, r => cp.selectedRecipe = r);
                case GridPortableReactor pr: return ReactorPanel(pr, slot);
                case GridDockingPort dp:    return DockingPortPanel(dp, slot);
                case GridLandingGear lg:    return LandingGearPanel(lg);
                case GridWheel wh:          return WheelPanel(wh);
                case GridSolarPanel sp:     return SolarPanel(sp);
                case GridHydrogenEngine he: return HydrogenEnginePanel(he);
                case GridDrill dr:          return DrillPanel(dr, slot);
                case GridElectricFurnace ef: return FurnacePanel(ef, slot);
                case GridBeacon bc:         return BeaconPanel(bc);
                case GridOreDetector od:    return OreDetectorPanel(od);
                case GridCryobed cryo:      return CryobedPanel(cryo);
                case GridSlidingDoor door:  return SlidingDoorPanel(door);
                case VoxelEngine.Simulation.GridLightBlock gl: return GridLightPanel(gl);
                default:                    return GenericPanel(block);
            }
        }

        // ── LIQUID TANK ───────────────────────────────────────────────────────
        private static VisualElement LiquidTankPanel(GridLiquidTank tank)
        {
            var p = T.MachinePanel();
            var (hdr, _, _, _) = T.HeaderRow($"🛢 Liquid Tank · {tank.liquidType.DisplayName()}",
                tank.Fill01 >= 0.99f ? "FULL" : "OK",
                tank.Fill01 >= 0.99f ? T.AccentAmber : T.AccentGreen);
            p.Add(hdr);
            p.Add(T.AccentDivider(tank.liquidType.Color()));

            // Visual tank gauge.
            var gaugeRow = Row();
            gaugeRow.style.justifyContent = Justify.Center;
            gaugeRow.Add(T.TankGauge(tank.liquidType.DisplayName(), tank.Fill01, tank.liquidType.Color(),
                $"{tank.stored:0} / {tank.capacity:0} L", 70, 120));
            p.Add(gaugeRow);
            p.Add(T.Spacer(6));

            p.Add(T.StatRow("⚖", "Stored Mass", MassFormat.Format(tank.ContentMass), T.AccentCyan));
            p.Add(T.Spacer(4));

            // Liquid type selector (only while empty).
            p.Add(GridUIHelpers.SectionTitle("Liquid Type"));
            var typeRow = Row(); typeRow.style.flexWrap = Wrap.Wrap;
            foreach (LiquidType lt in System.Enum.GetValues(typeof(LiquidType)))
            {
                var captured = lt;
                bool active = tank.liquidType == lt;
                var btn = T.SmallButton(lt.DisplayName(), () =>
                {
                    if (tank.SetLiquidType(captured))
                        VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();   // instant UI update
                }, active ? lt.Color() : (Color?)null);
                btn.SetEnabled(tank.stored <= 0.001f || active);
                typeRow.Add(btn);
            }
            p.Add(typeRow);
            if (tank.stored > 0.001f)
                p.Add(T.Muted("Drain the tank to change its liquid type."));

            p.Add(T.Spacer(6));
            var modeRow = Row();
            modeRow.Add(T.SmallButton("Auto", () => { tank.mode = GridTankMode.Auto; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                tank.mode == GridTankMode.Auto ? T.AccentGreen : T.BgSlot));
            modeRow.Add(T.SmallButton("Stockpile", () => { tank.mode = GridTankMode.Stockpile; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                tank.mode == GridTankMode.Stockpile ? T.AccentAmber : T.BgSlot));
            p.Add(modeRow);

            p.Add(T.Spacer(8));
            var actions = Row();
            actions.Add(T.SmallButton("⊘  Drain (void)", () =>
            {
                tank.Drain();
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, T.AccentRed));
            p.Add(actions);
            return p;
        }

        // ── GAS TANK ──────────────────────────────────────────────────────────
        private static VisualElement GasTankPanel(GridGasTank tank)
        {
            var p = T.MachinePanel();
            var (hdr, _, _, _) = T.HeaderRow($"🛢 Gas Tank · {tank.gasType}",
                tank.Fill01 >= 0.99f ? "FULL" : "OK",
                tank.Fill01 >= 0.99f ? T.AccentAmber : T.AccentGreen);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            var gaugeRow = Row();
            gaugeRow.style.justifyContent = Justify.Center;
            gaugeRow.Add(T.TankGauge(tank.gasType.ToString(), tank.Fill01, new Color(0.45f, 0.75f, 0.95f),
                $"{tank.stored:0} / {tank.capacity:0} L", 70, 120));
            p.Add(gaugeRow);
            p.Add(T.Spacer(6));
            p.Add(T.StatRow("⚖", "Stored Mass", MassFormat.Format(tank.ContentMass), T.AccentCyan));
            p.Add(T.Spacer(4));

            // Gas type selector (only while empty), like the liquid tank.
            p.Add(GridUIHelpers.SectionTitle("Gas Type"));
            var typeRow = Row(); typeRow.style.flexWrap = Wrap.Wrap;
            foreach (VoxelEngine.Gas.GasType gt in System.Enum.GetValues(typeof(VoxelEngine.Gas.GasType)))
            {
                if (gt == VoxelEngine.Gas.GasType.None) continue;
                var captured = gt;
                bool active = tank.gasType == gt;
                var btn = T.SmallButton(gt.ToString(), () =>
                {
                    if (tank.SetGasType(captured)) VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, active ? T.AccentCyan : (Color?)null);
                btn.SetEnabled(tank.stored <= 0.001f || active);
                typeRow.Add(btn);
            }
            p.Add(typeRow);
            if (tank.stored > 0.001f) p.Add(T.Muted("Empty the tank to change its gas type."));
            p.Add(T.Spacer(6));
            var modeRow = Row();
            modeRow.Add(T.SmallButton("Auto", () => { tank.mode = GridTankMode.Auto; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                tank.mode == GridTankMode.Auto ? T.AccentGreen : T.BgSlot));
            modeRow.Add(T.SmallButton("Stockpile", () => { tank.mode = GridTankMode.Stockpile; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                tank.mode == GridTankMode.Stockpile ? T.AccentAmber : T.BgSlot));
            p.Add(modeRow);
            return p;
        }

        // ── H2/O2 GENERATOR ────────────────────────────────────────────────────
        private static VisualElement H2O2Panel(GridH2O2Generator gen, MachineUIs.SlotBuilder slot)
        {
            var p = T.MachinePanel();
            Color sc = gen.Status == "Producing" ? T.AccentGreen
                     : gen.Status == "No Power" ? T.AccentRed : T.AccentAmber;
            var (hdr, _, _, _) = T.HeaderRow("⚗ H2/O2 Generator", gen.Status.ToUpper(), sc);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            // Three tanks: water buffer + H₂ and O₂ outputs.
            var gaugeRow = Row();
            gaugeRow.style.justifyContent = Justify.SpaceAround;
            gaugeRow.Add(T.TankGauge("Water", gen.WaterFill01, new Color(0.25f, 0.55f, 0.95f),
                $"{gen.waterStored:0}/{gen.waterCapacity:0} L", 60, 100));
            gaugeRow.Add(T.TankGauge("H₂", gen.H2Fill01, new Color(0.35f, 0.7f, 0.95f),
                $"{gen.h2Stored:0}/{gen.h2Capacity:0} L", 60, 100));
            gaugeRow.Add(T.TankGauge("O₂", gen.O2Fill01, new Color(0.4f, 0.9f, 0.5f),
                $"{gen.o2Stored:0}/{gen.o2Capacity:0} L", 60, 100));
            p.Add(gaugeRow);
            p.Add(T.Spacer(6));

            p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(gen.CurrentWattage), T.AccentGold));
            p.Add(T.StatRow("🟦", "H₂ Rate", $"{gen.hydrogenPerSecond:0.#}/s", T.AccentCyan));
            p.Add(T.StatRow("🟩", "O₂ Rate", $"{gen.oxygenPerSecond:0.#}/s", T.AccentGreen));
            p.Add(T.Spacer(4));

            // Overflow toggle.
            var ofRow = Row();
            ofRow.Add(T.SmallButton(gen.voidOverflow ? "⊘ Void Overflow: ON" : "⏸ Void Overflow: OFF",
                () => { gen.ToggleVoidOverflow(); VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                gen.voidOverflow ? T.AccentRed : T.AccentTeal));
            p.Add(ofRow);
            p.Add(T.Muted(gen.voidOverflow ? "Excess gas is vented when a tank is full."
                                           : "Production pauses when an output tank is full."));
            p.Add(T.Spacer(4));

            // 4 ice slots (water can also come from a connected Water Liquid Tank).
            p.Add(GridUIHelpers.SectionTitle("Ice Input (auto-pulled from cargo)"));
            if (gen.iceInput != null)
                p.Add(GridUIHelpers.WeightHeader(MassUtil.ContainerMass(gen.iceInput), "Ice"));
            var grid = T.SlotGrid(4);
            if (gen.iceInput != null)
                for (int i = 0; i < gen.iceInput.Size; i++)
                    grid.Add(slot(gen.iceInput, i, gen.iceInput.GetSlot(i), false, true));
            p.Add(grid);
            return p;
        }

        // ── BATTERY ────────────────────────────────────────────────────────────
        private static VisualElement BatteryPanel(GridBattery bat)
        {
            var p = T.MachinePanel();
            string state = bat.TransferState;
            Color sc = bat.IsCharging ? T.AccentGreen
                     : bat.IsDischarging ? T.AccentAmber : T.AccentCyan;
            var (hdr, _, _, _) = T.HeaderRow("🔋 Battery", state, sc);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGreen));

            var gaugeRow = Row();
            gaugeRow.style.justifyContent = Justify.Center;
            gaugeRow.Add(T.TankGauge("Charge", bat.Fill01, new Color(0.3f, 0.85f, 0.4f),
                $"{PowerFormat.WattHours(bat.storedWh)} / {PowerFormat.WattHours(bat.capacityWh)}", 70, 120));
            p.Add(gaugeRow);
            p.Add(T.Spacer(6));

            var (fill, _) = T.ProgressBar(bat.Fill01, T.AccentGreen, 8, true);
            p.Add(fill);
            p.Add(T.Spacer(4));
            p.Add(T.StatRow("⚡", "Max Charge", PowerFormat.Watts(bat.maxChargeRate), T.AccentCyan));
            p.Add(T.StatRow("🔌", "Max Discharge", PowerFormat.Watts(bat.maxDischargeRate), T.AccentAmber));
            p.Add(T.StatRow("↘", "Charging Now", PowerFormat.Watts(bat.CurrentChargeWatts), T.AccentGreen));
            p.Add(T.StatRow("↗", "Discharging Now", PowerFormat.Watts(bat.CurrentDischargeWatts), T.AccentAmber));
            p.Add(T.Spacer(4));
            var modeRow = Row();
            modeRow.Add(T.SmallButton("Auto", () => { bat.mode = GridBatteryMode.Auto; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                bat.mode == GridBatteryMode.Auto ? T.AccentGreen : T.BgSlot));
            modeRow.Add(T.SmallButton("Recharge", () => { bat.mode = GridBatteryMode.Recharge; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                bat.mode == GridBatteryMode.Recharge ? T.AccentCyan : T.BgSlot));
            modeRow.Add(T.SmallButton("Discharge", () => { bat.mode = GridBatteryMode.Discharge; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                bat.mode == GridBatteryMode.Discharge ? T.AccentAmber : T.BgSlot));
            p.Add(modeRow);
            p.Add(T.StatRow("⚙", "Mode", bat.mode.ToString(), T.TextSecondary));
            if (bat.Grid != null)
                p.Add(T.StatRow("⚖", "Grid Balance", PowerFormat.Watts(bat.Grid.PowerBalance),
                    bat.Grid.PowerBalance >= 0 ? T.AccentGreen : T.AccentRed));
            return p;
        }

        // ── CARGO CONTAINER ─────────────────────────────────────────────────────
        private static VisualElement CargoPanel(GridCargoContainer cc, MachineUIs.SlotBuilder slot)
        {
            if (cc.container == null) cc.OnPlaced();
            var p = T.MachinePanel();
            p.style.width = 460;
            var title = string.IsNullOrWhiteSpace(cc.blockName) || cc.blockName == "Armor Block" ? "Cargo Container" : cc.blockName;
            var (hdr, _, _, _) = T.HeaderRow($"📦 {title}",
                cc.Fill01 >= 0.99f ? "FULL" : "OK",
                cc.Fill01 >= 0.99f ? T.AccentRed : T.AccentGreen);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGold));

            // Mass-cap header (the headline "total weight at top of inventory").
            p.Add(GridUIHelpers.WeightCapHeader(cc.CurrentMassKg, cc.maxMassKg));
            var (fill, _) = T.ProgressBar(cc.Fill01,
                cc.Fill01 >= 0.99f ? T.AccentRed : T.AccentGold, 8, true);
            p.Add(fill);
            p.Add(T.Muted($"Cargo mass: {MassFormat.Format(cc.CurrentMassKg)} / {MassFormat.Format(cc.maxMassKg)}"));
            p.Add(T.Spacer(6));

            p.Add(GridUIHelpers.SectionTitle("Cargo Filter"));
            var filter = new TextField { value = cc.itemFilter ?? "" };
            filter.tooltip = "Optional filter. Matches item id, display name, or category. Empty accepts everything.";
            filter.RegisterValueChangedCallback(e => cc.SetItemFilter(e.newValue));
            filter.RegisterCallback<FocusInEvent>(_ => PortConfigHud.IsAnyDropdownOpen = true);
            filter.RegisterCallback<FocusOutEvent>(_ => PortConfigHud.IsAnyDropdownOpen = false);
            p.Add(filter);
            p.Add(T.Muted(string.IsNullOrWhiteSpace(cc.itemFilter) ? "Accepting all items." : $"Only accepting matches for: {cc.itemFilter}"));
            p.Add(T.Spacer(6));

            p.Add(GridUIHelpers.SectionTitle("Inventory"));
            var invScroll = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(invScroll);   // themed slim scrollbar
            invScroll.style.maxHeight = 360;
            invScroll.style.flexShrink = 1;
            invScroll.contentContainer.style.width = Length.Percent(100);
            var grid = T.SlotGrid(6);
            for (int i = 0; i < cc.container.Size; i++)
                grid.Add(slot(cc.container, i, cc.container.GetSlot(i), false, true));
            invScroll.Add(grid);
            p.Add(invScroll);

            p.Add(T.Spacer(6));
            p.Add(T.SmallButton("🛰  Open Ship Terminal",
                () => VoxelEngine.UI.GameUIController.Instance?.OpenGridTerminal(cc.Grid), T.AccentCyan));
            return p;
        }

        // ── GATLING WEAPON (ammo) ───────────────────────────────────────────────
        private static VisualElement WeaponPanel(GridWeapon gw, MachineUIs.SlotBuilder slot)
        {
            if (gw.ammo == null) gw.OnPlaced();
            var p = T.MachinePanel();
            var (hdr, _, _, _) = T.HeaderRow("🔫 Gatling Weapon", "ARMED", T.AccentRed);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentRed));

            p.Add(T.StatRow("💥", "Damage", $"{gw.damage:0}", T.AccentAmber));
            p.Add(T.StatRow("🎯", "Range", $"{gw.range:0} m", T.AccentCyan));
            p.Add(T.StatRow("⚡", "Power/Shot", PowerFormat.Watts(gw.powerPerShot), T.AccentGold));
            p.Add(T.Spacer(4));

            p.Add(GridUIHelpers.SectionTitle("Ammunition"));
            p.Add(GridUIHelpers.WeightHeader(MassUtil.ContainerMass(gw.ammo), "Ammo"));
            var grid = T.SlotGrid(6);
            for (int i = 0; i < gw.ammo.Size; i++)
                grid.Add(slot(gw.ammo, i, gw.ammo.GetSlot(i), false, true));
            p.Add(grid);
            return p;
        }

        // ── SHIP REFINERY / CHEMICAL PLANT (fluid processors) ────────────────────
        private static VisualElement ProcessorPanel(string title,
            VoxelEngine.Crafting.ProcessingRecipe current, float progress01, float powerDraw,
            System.Collections.Generic.List<VoxelEngine.Crafting.ProcessingRecipe> recipes,
            GridEntity grid,
            VoxelEngine.Crafting.ProcessingRecipe selected,
            System.Action<VoxelEngine.Crafting.ProcessingRecipe> onSelect)
        {
            var p = T.MachinePanel();
            p.style.width = 460;
            bool running = current != null;
            var (hdr, _, _, _) = T.HeaderRow(title, running ? "PROCESSING" : "IDLE",
                running ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(powerDraw), T.AccentGold));
            if (running)
            {
                p.Add(T.StatRow("⚙", "Recipe", current.GetDisplayName(), T.AccentCyan));
                var (bar, _) = T.ProgressBar(progress01, T.AccentGreen, 8, true);
                p.Add(bar);
            }
            p.Add(T.Spacer(6));

            // Connected liquid tanks on this grid (where fluids are drawn from / pushed to).
            p.Add(GridUIHelpers.SectionTitle("Connected Liquid Tanks"));
            var tankRow = Row(); tankRow.style.flexWrap = Wrap.Wrap;
            int shown = 0;
            if (grid != null && GridLiquidNetwork.Instance != null)
            {
                foreach (var t in GridLiquidNetwork.Instance.GetTanks(grid))
                {
                    if (t == null) continue;
                    tankRow.Add(T.TankGauge(t.liquidType.DisplayName(), t.Fill01, t.liquidType.Color(),
                        $"{t.stored:0}/{t.capacity:0} L", 60, 90));
                    if (++shown >= 6) break;
                }
            }
            if (shown == 0) tankRow.Add(T.Muted("No liquid tanks on this grid. Build Liquid Tanks for fluid recipes."));
            p.Add(tankRow);
            p.Add(T.Spacer(6));

            // Recipe book — click to lock a recipe, or Auto to let it pick.
            p.Add(GridUIHelpers.SectionTitle("Recipes  (click to select · Auto by default)"));
            p.Add(RecipeButton("⟳  Auto (first available)", "", selected == null, current != null && selected == null,
                () => { onSelect(null); VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); }));
            if (recipes != null)
                foreach (var r in recipes)
                {
                    if (r == null) continue;
                    var captured = r;
                    p.Add(RecipeButton(r.GetDisplayName(), RecipeSummary(r), selected == r, current == r,
                        () => { onSelect(captured); VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); }));
                }
            return p;
        }

        private static VisualElement RecipeButton(string name, string summary, bool selected, bool active, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.style.flexDirection = FlexDirection.Column;
            btn.style.alignItems = Align.FlexStart;
            btn.style.marginBottom = 2; btn.style.paddingTop = 4; btn.style.paddingBottom = 4; btn.style.paddingLeft = 8;
            btn.style.backgroundColor = new StyleColor(selected
                ? new Color(0.18f, 0.72f, 0.88f, 0.28f) : new Color(0.12f, 0.14f, 0.18f, 0.95f));
            var title = new Label((selected ? "◉ " : "○ ") + name);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(active ? T.AccentGreen : selected ? T.AccentCyan : new Color(0.85f,0.88f,0.92f));
            btn.Add(title);
            if (!string.IsNullOrEmpty(summary))
            {
                var sub = new Label(summary);
                sub.style.fontSize = 10; sub.style.color = new StyleColor(new Color(0.6f,0.64f,0.7f));
                btn.Add(sub);
            }
            return btn;
        }

        private static string RecipeSummary(VoxelEngine.Crafting.ProcessingRecipe r)
        {
            var ins = new System.Collections.Generic.List<string>();
            if (r.HasFluidInputs) foreach (var f in r.fluidInputs) ins.Add($"{f.litres:0}L {f.liquid.DisplayName()}");
            if (r.HasItemInputs)  foreach (var i in r.inputs) if (i.item != null) ins.Add($"{i.count} {i.item.displayName}");
            var outs = new System.Collections.Generic.List<string>();
            if (r.HasFluidOutputs) foreach (var f in r.fluidOutputs) outs.Add($"{f.litres:0}L {f.liquid.DisplayName()}");
            if (r.HasItemOutputs)  foreach (var o in r.outputs) if (o.item != null) outs.Add($"{o.count} {o.item.displayName}");
            return string.Join(" + ", ins) + "  →  " + string.Join(" + ", outs);
        }

        // ── PORTABLE REACTOR ──────────────────────────────────────────────────────
        private static VisualElement ReactorPanel(GridPortableReactor r, MachineUIs.SlotBuilder slot)
        {
            if (r.fuelC == null) r.OnPlaced();
            var p = T.MachinePanel();
            p.style.width = 420;
            var (hdr, _, _, _) = T.HeaderRow("☢ Portable Reactor",
                r.IsRunning ? "RUNNING" : "IDLE",
                r.IsRunning ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGreen));

            // Fuel-remaining gauge.
            var gaugeRow = Row();
            gaugeRow.style.justifyContent = Justify.Center;
            gaugeRow.Add(T.TankGauge("Fuel", r.FuelRemaining01, new Color(0.3f, 0.85f, 0.4f),
                $"{r.FuelRemaining01 * 100f:0}%", 64, 100));
            p.Add(gaugeRow);
            p.Add(T.Spacer(6));

            p.Add(T.StatRow("🔌", "Power Out", PowerFormat.Watts(r.PowerOutput), T.AccentGreen));
            p.Add(T.StatRow("🧊", "Ice / pellet", $"{r.icePerPellet}", T.AccentCyan));
            p.Add(T.Spacer(4));

            p.Add(GridUIHelpers.SectionTitle("LEU Fuel"));
            var fg = T.SlotGrid(r.fuelC.Size);
            for (int i = 0; i < r.fuelC.Size; i++) fg.Add(slot(r.fuelC, i, r.fuelC.GetSlot(i), false, true));
            p.Add(fg);

            p.Add(GridUIHelpers.SectionTitle("Ice Coolant"));
            var ig = T.SlotGrid(r.iceC.Size);
            for (int i = 0; i < r.iceC.Size; i++) ig.Add(slot(r.iceC, i, r.iceC.GetSlot(i), false, true));
            p.Add(ig);

            p.Add(GridUIHelpers.SectionTitle("Nuclear Waste"));
            var wg = T.SlotGrid(r.wasteC.Size);
            for (int i = 0; i < r.wasteC.Size; i++) wg.Add(slot(r.wasteC, i, r.wasteC.GetSlot(i), false, true));
            p.Add(wg);
            return p;
        }

        // ── SHIP ELECTRIC FURNACE ───────────────────────────────────────────────────
        private static VisualElement FurnacePanel(GridElectricFurnace f, MachineUIs.SlotBuilder slot)
        {
            if (f.inputC == null) f.OnPlaced();
            var p = T.MachinePanel();
            p.style.width = 420;
            var (hdr, _, _, _) = T.HeaderRow("🔥 Ship Electric Furnace",
                f.IsSmelting ? "SMELTING" : "IDLE", f.IsSmelting ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGold));

            p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(f.PowerDraw), T.AccentGold));
            if (f.IsSmelting) { var (bar,_) = T.ProgressBar(f.Progress01, T.AccentGreen, 8, true); p.Add(bar); }
            p.Add(T.Spacer(4));

            var apRow = Row();
            apRow.Add(T.SmallButton(f.autoPull ? "⤵ Auto-Pull: ON" : "⤵ Auto-Pull: OFF",
                () => { f.ToggleAutoPull(); VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                f.autoPull ? T.AccentGreen : T.BgSlot));
            p.Add(apRow);
            p.Add(T.Muted("Auto-smelts smeltable items from ship cargo into ingots."));

            p.Add(GridUIHelpers.SectionTitle("Input"));
            var ig = T.SlotGrid(f.inputC.Size);
            for (int i = 0; i < f.inputC.Size; i++) ig.Add(slot(f.inputC, i, f.inputC.GetSlot(i), false, true));
            p.Add(ig);
            p.Add(GridUIHelpers.SectionTitle("Output"));
            var og = T.SlotGrid(f.outputC.Size);
            for (int i = 0; i < f.outputC.Size; i++) og.Add(slot(f.outputC, i, f.outputC.GetSlot(i), false, true));
            p.Add(og);
            return p;
        }

        // ── MINING DRILL ────────────────────────────────────────────────────────────
        private static VisualElement DrillPanel(GridDrill d, MachineUIs.SlotBuilder slot)
        {
            if (d.buffer == null) d.OnPlaced();
            var p = T.MachinePanel();
            var (hdr, _, _, _) = T.HeaderRow("⛏ Mining Drill",
                d.IsActive ? "MINING" : "IDLE", d.IsActive ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGold));

            p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(d.PowerDraw), T.AccentGold));
            p.Add(T.StatRow("◎", "Radius", $"{d.drillRadius:0.#} m", T.AccentCyan));
            p.Add(T.Spacer(4));
            p.Add(GridUIHelpers.SectionTitle("Buffer (auto-pushed to cargo)"));
            var g = T.SlotGrid(d.buffer.Size);
            for (int i = 0; i < d.buffer.Size; i++) g.Add(slot(d.buffer, i, d.buffer.GetSlot(i), false, true));
            p.Add(g);
            return p;
        }

        // ── LANDING GEAR ───────────────────────────────────────────────────────────
        private static VisualElement LandingGearPanel(GridLandingGear lg)
        {
            var p = T.MachinePanel();
            var (hdr, _, _, _) = T.HeaderRow("🦿 Landing Gear",
                lg.IsLocked ? "LOCKED" : "UNLOCKED",
                lg.IsLocked ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGold));

            p.Add(T.StatRow("🔒", "Lock Strength", PowerFormat.Newtons(lg.lockStrength), T.AccentCyan));
            p.Add(T.Spacer(6));
            var row = Row();
            row.Add(T.SmallButton(lg.IsLocked ? "Unlock" : "Lock", () =>
            {
                lg.ToggleLock();
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, lg.IsLocked ? T.AccentRed : T.AccentGreen));
            row.Add(T.SmallButton(lg.autoLock ? "Auto-Lock: ON" : "Auto-Lock: OFF", () =>
            {
                lg.autoLock = !lg.autoLock;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, lg.autoLock ? T.AccentTeal : (Color?)null));
            p.Add(row);
            return p;
        }

        // ── WHEEL SUSPENSION ─────────────────────────────────────────────────────
        private static VisualElement WheelPanel(GridWheel wheel)
        {
            var p = T.MachinePanel();
            var (hdr, _, _, _) = T.HeaderRow($"Wheel Suspension {wheel.wheelSizeCells}x{wheel.wheelSizeCells}",
                wheel.IsGrounded ? "GROUNDED" : "AIRBORNE",
                wheel.IsGrounded ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));
            p.Add(T.StatRow("", "Power Use", PowerFormat.Watts(wheel.PowerDraw), T.AccentGold));
            p.Add(T.StatRow("", "Drive Force", PowerFormat.Newtons(wheel.driveForce), T.AccentCyan));
            p.Add(T.StatRow("", "Spring", PowerFormat.Newtons(wheel.springForce), T.AccentCyan));
            p.Add(T.StatRow("", "Travel", $"{wheel.suspensionLength:0.00} m", T.TextPrimary));
            p.Add(T.Spacer(6));

            p.Add(GridUIHelpers.SectionTitle("Suspension Tuning"));
            p.Add(SliderRow("Strength", wheel.suspensionStrength, 0.05f, 1f,
                v => { wheel.suspensionStrength = v; }, "0%", "100%"));
            p.Add(SliderRow("Travel", wheel.suspensionLength, 0.5f, 3.5f,
                v => { wheel.suspensionLength = v; }, "0.5m", "3.5m"));
            p.Add(SliderRow("Steer", wheel.steerAngle, 0f, 45f,
                v => { wheel.steerAngle = v; }, "0°", "45°"));
            p.Add(T.SmallButton(wheel.isSteerable ? "Steering: ON" : "Steering: OFF", () =>
            {
                wheel.isSteerable = !wheel.isSteerable;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, wheel.isSteerable ? T.AccentGreen : T.BgSlot));
            return p;
        }

        private static VisualElement SliderRow(string label, float value, float min, float max,
            System.Action<float> onChanged, string left, string right)
        {
            var box = new VisualElement();
            box.style.marginBottom = 6;
            var caption = new Label($"{label}: {value:0.##}");
            caption.style.color = new StyleColor(T.TextSecondary);
            caption.style.fontSize = 10;
            caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            box.Add(caption);
            var slider = new Slider(min, max) { value = value };
            slider.RegisterValueChangedCallback(e =>
            {
                onChanged?.Invoke(e.newValue);
                caption.text = $"{label}: {e.newValue:0.##}";
            });
            box.Add(slider);
            var range = new Label($"{left}  —  {right}");
            range.style.fontSize = 9;
            range.style.color = new StyleColor(T.TextMuted);
            box.Add(range);
            return box;
        }

        // ── SOLAR PANEL ──────────────────────────────────────────────────────────
        private static VisualElement SolarPanel(GridSolarPanel sp)
        {
            float eff = sp.Efficiency01;
            var p = T.MachinePanel();
            var (hdr, _, _, _) = T.HeaderRow("☀ Solar Panel",
                sp.CurrentOutput > 1f ? "GENERATING" : "IDLE",
                sp.CurrentOutput > 1f ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGold));

            p.Add(T.StatRow("🔌", "Output", PowerFormat.Watts(sp.CurrentOutput), T.AccentGreen));
            p.Add(T.StatRow("⚡", "Rated Max", PowerFormat.Watts(sp.maxOutput), T.AccentCyan));
            p.Add(T.StatRow("📈", "Efficiency", $"{eff * 100f:0}%",
                eff >= 0.66f ? T.AccentGreen : eff >= 0.33f ? T.AccentAmber : T.AccentRed));
            p.Add(T.Spacer(6));

            // Visual efficiency bar.
            var (bar, _) = T.ProgressBar(eff,
                eff >= 0.66f ? T.AccentGreen : eff >= 0.33f ? T.AccentAmber : T.AccentRed, 8, true);
            p.Add(bar);
            p.Add(T.Muted("Output scales with sun angle, shadowing and weather."));
            return p;
        }

        // ── HYDROGEN ENGINE ───────────────────────────────────────────────────────
        private static VisualElement HydrogenEnginePanel(GridHydrogenEngine he)
        {
            var p = T.MachinePanel();
            var (hdr, _, _, _) = T.HeaderRow("Hydrogen Engine",
                he.IsRunning ? "RUNNING" : "IDLE",
                he.IsRunning ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGreen));
            var gaugeRow = Row();
            gaugeRow.style.justifyContent = Justify.Center;
            gaugeRow.Add(T.TankGauge("H2 Buffer", he.Fill01, T.AccentCyan,
                $"{he.internalHydrogen:0}/{he.internalTankCapacity:0} L", 70, 120));
            p.Add(gaugeRow);
            p.Add(T.Spacer(6));
            p.Add(T.StatRow("", "Output", PowerFormat.Watts(he.PowerOutput), T.AccentGreen));
            p.Add(T.StatRow("", "Hydrogen Use", $"{he.hydrogenPerSecond:0.#} H2/s", T.AccentCyan));
            p.Add(T.Muted("Buffers hydrogen internally, then burns it into grid power. Feed it through gas pipes from H2/O2 generators and gas tanks."));
            return p;
        }

        // ── DOCKING PORT (inventory + I/O filter) ────────────────────────────────
        private static VisualElement DockingPortPanel(GridDockingPort dp, MachineUIs.SlotBuilder slot)
        {
            if (dp.container == null) dp.OnPlaced();
            var p = T.MachinePanel();
            p.style.width = 460;
            var (hdr, _, _, _) = T.HeaderRow("🔗 Docking Port",
                dp.IsDocked ? "DOCKED" : "FREE",
                dp.IsDocked ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGold));

            p.Add(T.StatRow("⚖", "Buffer Mass", MassFormat.Format(dp.ContentMass), T.AccentCyan));
            p.Add(T.StatRow("🔒", "Lock Strength", PowerFormat.Newtons(dp.lockStrength), T.AccentCyan));
            var dockRow = Row();
            dockRow.Add(T.SmallButton(dp.IsDocked ? "Undock" : "Dock", () =>
            {
                dp.ToggleDock();
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, dp.IsDocked ? T.AccentRed : T.AccentGreen));
            dockRow.Add(T.SmallButton(dp.autoDock ? "Auto-Dock: ON" : "Auto-Dock: OFF", () =>
            {
                dp.autoDock = !dp.autoDock;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, dp.autoDock ? T.AccentTeal : (Color?)null));
            p.Add(dockRow);
            p.Add(T.Spacer(4));

            p.Add(GridUIHelpers.SectionTitle("Buffer Inventory"));
            p.Add(GridUIHelpers.WeightHeader(MassUtil.ContainerMass(dp.container)));
            var grid = T.SlotGrid(6);
            for (int i = 0; i < dp.container.Size; i++)
                grid.Add(slot(dp.container, i, dp.container.GetSlot(i), false, true));
            p.Add(grid);

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Configure per-face Input / Output + item filters below."));
            // The port-config UI (direction + filters) is appended by GameUIController.
            return p;
        }

        // ── GRID SLIDING DOOR ────────────────────────────────────────────────────
        private static VisualElement SlidingDoorPanel(GridSlidingDoor door)
        {
            var p = T.MachinePanel();
            string state = !door.Enabled ? "OFF" : !door.HasPower ? "NO POWER" : door.IsOpen ? "OPEN" : "CLOSED";
            Color stateColor = !door.Enabled ? T.AccentDim : !door.HasPower ? T.AccentRed : door.IsOpen ? T.AccentGreen : T.AccentAmber;
            var (hdr, _, _, _) = T.HeaderRow("▣ " + door.SourceName, state, stateColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(door.PowerDraw), T.AccentGold));
            p.Add(T.StatRow("⇆", "Slide", $"{door.slideDistance:0.##} m", T.AccentCyan));
            p.Add(T.StatRow("◎", "Motion Radius", $"{door.motionRadius:0.#} m", T.AccentAmber));
            p.Add(T.Spacer(6));

            var row = Row();
            row.Add(T.SmallButton(door.Enabled ? "Turn OFF" : "Turn ON", () =>
            {
                door.Enabled = !door.Enabled;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, door.Enabled ? T.AccentRed : T.AccentGreen));
            row.Add(T.SmallButton(door.IsOpen ? "Close" : "Open", () =>
            {
                door.Toggle();
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, door.IsOpen ? T.AccentAmber : T.AccentGreen));
            row.Add(T.SmallButton(door.motionActivated ? "Motion: ON" : "Motion: OFF", () =>
            {
                door.SetMotionActivated(!door.motionActivated);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, door.motionActivated ? T.AccentGreen : T.BgSlot));
            p.Add(row);
            p.Add(T.Spacer(6));

            p.Add(GridUIHelpers.SectionTitle("Door Tuning"));
            p.Add(SliderRow("Motion Radius", door.motionRadius, 1f, 20f, v => door.motionRadius = v, "1m", "20m"));
            p.Add(SliderRow("Hold Time", door.motionGraceSeconds, 0.25f, 10f, v => door.motionGraceSeconds = v, "0.25s", "10s"));
            p.Add(SliderRow("Slide Speed", door.slideSpeed, 1f, 20f, v => door.slideSpeed = v, "1", "20"));
            p.Add(T.Muted("Motion mode opens the door when a player is nearby and closes after the hold time."));
            return p;
        }

        // ── GRID LED STRIP ───────────────────────────────────────────────────────
        private static VisualElement LEDStripPanel(VoxelEngine.Simulation.LEDStrip strip, GridBlock block)
        {
            var p = T.MachinePanel();
            string name = block != null && !string.IsNullOrWhiteSpace(block.blockName) && block.blockName != "Armor Block"
                ? block.blockName
                : "Grid LED Strip";
            bool online = block == null || (block.Enabled && block.Grid != null && block.Grid.HasPower);
            string state = block != null && !block.Enabled ? "OFF" : online ? "ON" : "NO POWER";
            Color stateColor = block != null && !block.Enabled ? T.AccentDim : online ? T.AccentGreen : T.AccentRed;
            var (hdr, _, _, _) = T.HeaderRow("▰ " + name, state, stateColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(strip.stripColor));

            p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(strip.wattsDraw), T.AccentGold));
            p.Add(T.StatRow("📏", "Length", $"{strip.stripLength:0.##} m", T.AccentCyan));
            p.Add(T.StatRow("🔆", "Brightness", $"{strip.brightness:0.##}", T.AccentAmber));
            p.Add(T.StatRow("◫", "Segments", strip.segmentCount.ToString(), T.TextSecondary));
            p.Add(T.Spacer(6));

            var toggleRow = Row();
            if (block != null)
            {
                toggleRow.Add(T.SmallButton(block.Enabled ? "Turn OFF" : "Turn ON", () =>
                {
                    block.Enabled = !block.Enabled;
                    strip.SetEnabled(block.Enabled);
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, block.Enabled ? T.AccentRed : T.AccentGreen));
            }
            toggleRow.Add(T.SmallButton("Static", () => { strip.SetMode(VoxelEngine.Simulation.LEDMode.Static); VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); }, strip.mode == VoxelEngine.Simulation.LEDMode.Static ? T.AccentGreen : T.BgSlot));
            toggleRow.Add(T.SmallButton("Pulse", () => { strip.SetMode(VoxelEngine.Simulation.LEDMode.Pulse); VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); }, strip.mode == VoxelEngine.Simulation.LEDMode.Pulse ? T.AccentCyan : T.BgSlot));
            toggleRow.Add(T.SmallButton("Blink", () => { strip.SetMode(VoxelEngine.Simulation.LEDMode.Blink); VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); }, strip.mode == VoxelEngine.Simulation.LEDMode.Blink ? T.AccentAmber : T.BgSlot));
            toggleRow.Add(T.SmallButton("Chase", () => { strip.SetMode(VoxelEngine.Simulation.LEDMode.Chase); VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); }, strip.mode == VoxelEngine.Simulation.LEDMode.Chase ? T.AccentTeal : T.BgSlot));
            toggleRow.Add(T.SmallButton(strip.showSegments ? "Segments: ON" : "Clean Strip", () =>
            {
                strip.SetSegmented(!strip.showSegments);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, strip.showSegments ? T.AccentCyan : T.BgSlot));
            p.Add(toggleRow);
            p.Add(T.Spacer(6));

            p.Add(GridUIHelpers.SectionTitle("Strip Tuning"));
            p.Add(SliderRow("Brightness", strip.brightness, 0f, 5f, v => strip.brightness = v, "0", "5"));
            p.Add(SliderRow("Length", strip.stripLength, 0.25f, 10f, v => strip.SetLength(v), "0.25m", "10m"));
            p.Add(SliderRow("Segments", strip.segmentCount, 2f, 32f, v => { strip.segmentCount = Mathf.RoundToInt(v); strip.SetLength(strip.stripLength); }, "2", "32"));

            p.Add(GridUIHelpers.SectionTitle("Motion Activation"));
            var motionRow = Row();
            motionRow.Add(T.SmallButton(strip.motionActivated ? "Motion: ON" : "Motion: OFF", () =>
            {
                strip.SetMotionActivated(!strip.motionActivated);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, strip.motionActivated ? T.AccentGreen : T.BgSlot));
            motionRow.Add(T.SmallButton(strip.motionChaseOnActivation ? "Wake Chase: ON" : "Wake Chase: OFF", () =>
            {
                strip.motionChaseOnActivation = !strip.motionChaseOnActivation;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, strip.motionChaseOnActivation ? T.AccentCyan : T.BgSlot));
            motionRow.Add(T.Muted("Turns on when a player is nearby."));
            p.Add(motionRow);
            p.Add(SliderRow("Sensor Radius", strip.motionRadius, 1f, 20f, v => strip.motionRadius = v, "1m", "20m"));
            p.Add(SliderRow("Hold Time", strip.motionGraceSeconds, 0.25f, 10f, v => strip.motionGraceSeconds = v, "0.25s", "10s"));

            p.Add(GridUIHelpers.SectionTitle("Color"));
            var colorRow = Row();
            colorRow.style.flexWrap = Wrap.Wrap;
            AddLedColorButton(colorRow, strip, "Cyan", T.AccentCyan);
            AddLedColorButton(colorRow, strip, "Blue", new Color(0.25f, 0.48f, 1f));
            AddLedColorButton(colorRow, strip, "Green", T.AccentGreen);
            AddLedColorButton(colorRow, strip, "Amber", T.AccentAmber);
            AddLedColorButton(colorRow, strip, "Red", T.AccentRed);
            AddLedColorButton(colorRow, strip, "White", Color.white);
            AddLedColorButton(colorRow, strip, "Violet", new Color(0.72f, 0.42f, 0.95f));
            p.Add(colorRow);

            p.Add(T.Spacer(4));
            p.Add(T.Muted("Length changes are live now; corner-to-corner placement will use this same runtime length foundation in the next build-tool pass."));
            return p;
        }

        private static void AddLedColorButton(VisualElement row, VoxelEngine.Simulation.LEDStrip strip, string label, Color color)
        {
            var btn = T.SmallButton(label, () =>
            {
                strip.SetColor(color);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, color);
            btn.style.marginRight = 4;
            btn.style.marginBottom = 4;
            row.Add(btn);
        }

        // ── GRID SPOTLIGHT / LIGHT ───────────────────────────────────────────────
        private static VisualElement GridLightPanel(VoxelEngine.Simulation.GridLightBlock light)
        {
            var p = T.MachinePanel();
            string state = !light.Enabled ? "OFF" : light.IsOnline ? "ON" : "NO POWER";
            Color stateColor = !light.Enabled ? T.AccentDim : light.IsOnline ? T.AccentGreen : T.AccentRed;
            var (hdr, _, _, _) = T.HeaderRow("💡 " + light.SourceName, state, stateColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(light.lightColor));

            p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(light.PowerDraw), T.AccentGold));
            p.Add(T.StatRow("📏", "Range", $"{light.range:0.#} m", T.AccentCyan));
            p.Add(T.StatRow("🔆", "Intensity", $"{light.intensity:0.#}", T.AccentAmber));
            p.Add(T.StatRow("◰", "Cone", $"{light.spotAngle:0}°", T.TextSecondary));
            p.Add(T.Spacer(6));

            var toggleRow = Row();
            toggleRow.Add(T.SmallButton(light.Enabled ? "Turn OFF" : "Turn ON", () =>
            {
                light.Enabled = !light.Enabled;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, light.Enabled ? T.AccentRed : T.AccentGreen));
            toggleRow.Add(T.SmallButton("Reset Defaults", () =>
            {
                light.SetColor(Color.white);
                light.SetIntensity(light.EffectiveGridSize == VoxelEngine.GridSystem.GridSize.Large ? 9.5f : 4.8f);
                light.SetRange(light.EffectiveGridSize == VoxelEngine.GridSystem.GridSize.Large ? 78f : 34f);
                light.spotAngle = 42f;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, T.BgSlot));
            p.Add(toggleRow);
            p.Add(T.Spacer(6));

            p.Add(GridUIHelpers.SectionTitle("Beam Tuning"));
            p.Add(SliderRow("Intensity", light.intensity, 0f, 15f, v => light.SetIntensity(v), "0", "15"));
            p.Add(SliderRow("Range", light.range, 2f, 120f, v => light.SetRange(v), "2m", "120m"));
            p.Add(SliderRow("Cone", light.spotAngle, 10f, 120f, v => light.spotAngle = v, "10°", "120°"));

            p.Add(GridUIHelpers.SectionTitle("Motion Activation"));
            var motionRow = Row();
            motionRow.Add(T.SmallButton(light.motionActivated ? "Motion: ON" : "Motion: OFF", () =>
            {
                light.SetMotionActivated(!light.motionActivated);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, light.motionActivated ? T.AccentGreen : T.BgSlot));
            motionRow.Add(T.Muted("Turns on when a player is nearby."));
            p.Add(motionRow);
            p.Add(SliderRow("Sensor Radius", light.motionRadius, 1f, 30f, v => light.motionRadius = v, "1m", "30m"));
            p.Add(SliderRow("Hold Time", light.motionGraceSeconds, 0.25f, 10f, v => light.motionGraceSeconds = v, "0.25s", "10s"));

            p.Add(GridUIHelpers.SectionTitle("Color"));
            var colorRow = Row();
            colorRow.style.flexWrap = Wrap.Wrap;
            AddColorButton(colorRow, light, "White", Color.white);
            AddColorButton(colorRow, light, "Warm", new Color(1f, 0.82f, 0.55f));
            AddColorButton(colorRow, light, "Cyan", T.AccentCyan);
            AddColorButton(colorRow, light, "Blue", new Color(0.25f, 0.48f, 1f));
            AddColorButton(colorRow, light, "Green", T.AccentGreen);
            AddColorButton(colorRow, light, "Amber", T.AccentAmber);
            AddColorButton(colorRow, light, "Red", T.AccentRed);
            p.Add(colorRow);

            p.Add(T.Spacer(4));
            p.Add(T.Muted("Right-click a spotlight to open this config. Settings apply live to all beams in dual-output lights."));
            return p;
        }

        private static void AddColorButton(VisualElement row, VoxelEngine.Simulation.GridLightBlock light, string label, Color color)
        {
            var btn = T.SmallButton(label, () =>
            {
                light.SetColor(color);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, color);
            btn.style.marginRight = 4;
            btn.style.marginBottom = 4;
            row.Add(btn);
        }

        // ── FALLBACK ────────────────────────────────────────────────────────────
        // ── BEACON ──────────────────────────────────────────────────────────
        private static VisualElement BeaconPanel(GridBeacon bc)
        {
            var p = T.MachinePanel();
            var (hdr, _, _, _) = T.HeaderRow("📡 Beacon", bc.IsActive ? "● ACTIVE" : "○ OFF",
                bc.IsActive ? T.AccentCyan : T.AccentDim);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));
            p.Add(T.StatRow("💡", "Power Use", PowerFormat.Watts(bc.PowerDraw), T.AccentGold));
            p.Add(T.StatRow("📊", "Beam Height", $"{bc.beamHeight:0} m", T.AccentCyan));
            p.Add(T.StatRow("🔄", "Rotation", $"{bc.rotationSpeed:0}°/s", T.AccentTeal));
            p.Add(T.Spacer(6));
            p.Add(T.SmallButton(bc.IsActive ? "Turn OFF" : "Turn ON", () =>
            {
                bc.Enabled = !bc.Enabled;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, bc.Enabled ? T.AccentRed : T.AccentGreen));
            p.Add(T.Spacer(4));
            p.Add(T.Muted("Projects a visible vertical light beam into the sky. Visible from far away for navigation."));
            return p;
        }

        // ── ORE DETECTOR ───────────────────────────────────────────────────
        private static VisualElement OreDetectorPanel(GridOreDetector od)
        {
            var p = T.MachinePanel();
            var (hdr, _, _, _) = T.HeaderRow("🔍 Ore Detector", od.IsScanning ? "● SCANNING" : "○ OFFLINE",
                od.IsScanning ? T.AccentGreen : T.AccentDim);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGreen));
            p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(od.PowerDraw), T.AccentGold));
            p.Add(T.StatRow("📏", "Scan Radius", $"{od.scanRadius:0} blocks", T.AccentCyan));
            p.Add(T.StatRow("⬇", "Scan Depth", $"{od.maxScanDepth:0} blocks", T.AccentTeal));
            p.Add(T.Spacer(6));

            p.Add(GridUIHelpers.SectionTitle($"Detected Deposits ({od.DetectedOres.Count})"));
            if (od.DetectedOres.Count == 0)
            {
                p.Add(T.Muted("No ore deposits detected within range."));
            }
            else
            {
                foreach (var ore in od.DetectedOres)
                {
                    var color = GridOreDetector.OreDisplayColor(ore.material);
                    p.Add(T.StatRow("◈", GridOreDetector.OreDisplayName(ore.material),
                        $"{ore.count} blocks @ {ore.depth:0}m deep", color));
                }
            }
            p.Add(T.Spacer(4));
            p.Add(T.Muted("Scans the terrain below for ore deposits. Updates every 2 seconds."));
            return p;
        }

        // ── CRYOBED ───────────────────────────────────────────────────────
        private static VisualElement CryobedPanel(GridCryobed cryo)
        {
            var p = T.MachinePanel();
            var online = cryo.IsAvailableForRespawn;
            var (hdr, _, _, _) = T.HeaderRow($"❄ {cryo.blockName}", cryo.AvailabilityText,
                online ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(new Color(0.45f, 0.85f, 1f)));
            p.Add(T.StatRow("⚡", "Power", cryo.PowerEstimateText, T.AccentGold));
            p.Add(T.StatRow("◉", "Oxygen", cryo.OxygenEstimateText, T.AccentCyan));
            p.Add(T.StatRow("⌂", "Ownership", cryo.claimedByLocalPlayer ? "Owned by you" : "Unclaimed", T.AccentTeal));
            p.Add(T.Spacer(6));

            var nameField = new TextField("Name") { value = cryo.blockName };
            nameField.RegisterValueChangedCallback(evt =>
            {
                cryo.blockName = string.IsNullOrWhiteSpace(evt.newValue) ? "Grid Cryobed" : evt.newValue.Trim();
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            });
            p.Add(nameField);
            p.Add(T.Spacer(6));

            var row = Row(); row.style.flexWrap = Wrap.Wrap;
            row.Add(T.SmallButton(cryo.claimedByLocalPlayer ? "Claimed" : "Claim", () =>
            {
                cryo.ClaimAsSpawn();
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, online ? T.AccentCyan : T.BgSlot));
            row.Add(T.SmallButton("Remove", () =>
            {
                cryo.claimedByLocalPlayer = false;
                var session = VoxelEngine.Menu.WorldSession.Instance;
                if (session != null && session.hasBedSpawn && (session.bedSpawnPoint - cryo.SpawnPoint).sqrMagnitude < 1.5f)
                {
                    session.hasBedSpawn = false;
                    session.SaveSpawnSidecar();
                }
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, T.AccentAmber));
            row.Add(T.SmallButton("Transfer", () =>
                VoxelEngine.UI.BuildFeedbackHud.Show("Transfer", "Multiplayer ownership transfer will unlock later", null, T.AccentAmber), T.AccentDim));
            p.Add(row);
            p.Add(T.Spacer(4));
            p.Add(T.Muted("Gas pipes can install a variable oxygen port on the hull. Pump oxygen from H2/O2 generators into the cryobed for offline reserve."));
            return p;
        }

        private static VisualElement GenericPanel(GridBlock block)
        {
            var p = T.MachinePanel();
            var (hdr, _, _, _) = T.HeaderRow(block.blockName, "INFO", T.AccentCyan);
            p.Add(hdr);
            p.Add(T.AccentDivider());
            p.Add(T.StatRow("❤", "Integrity", $"{block.currentHP:0} / {block.maxHP:0}", T.AccentGreen));
            p.Add(T.StatRow("⚖", "Mass", MassFormat.Format(block.TotalMass), T.AccentCyan));
            if (block.PowerDraw > 0)   p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(block.PowerDraw), T.AccentGold));
            if (block.PowerOutput > 0) p.Add(T.StatRow("🔌", "Power Out", PowerFormat.Watts(block.PowerOutput), T.AccentGreen));
            return p;
        }

        private static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.alignItems = Align.Center;
            return r;
        }
    }
}
