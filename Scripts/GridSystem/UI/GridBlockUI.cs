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
            string state = bat.Grid == null ? "—"
                         : bat.Grid.PowerBalance > 0.1f ? "CHARGING"
                         : bat.Grid.PowerBalance < -0.1f ? "DISCHARGING" : "IDLE";
            Color sc = state == "CHARGING" ? T.AccentGreen
                     : state == "DISCHARGING" ? T.AccentAmber : T.AccentCyan;
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
            var (hdr, _, _, _) = T.HeaderRow("📦 Cargo Container",
                cc.Fill01 >= 0.99f ? "FULL" : "OK",
                cc.Fill01 >= 0.99f ? T.AccentRed : T.AccentGreen);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGold));

            // Mass-cap header (the headline "total weight at top of inventory").
            p.Add(GridUIHelpers.WeightCapHeader(cc.CurrentMassKg, cc.maxMassKg));
            var (fill, _) = T.ProgressBar(cc.Fill01,
                cc.Fill01 >= 0.99f ? T.AccentRed : T.AccentGold, 8, true);
            p.Add(fill);
            p.Add(T.Spacer(6));

            p.Add(GridUIHelpers.SectionTitle("Inventory"));
            var grid = T.SlotGrid(6);
            for (int i = 0; i < cc.container.Size; i++)
                grid.Add(slot(cc.container, i, cc.container.GetSlot(i), false, true));
            p.Add(grid);

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

        // ── FALLBACK ────────────────────────────────────────────────────────────
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
