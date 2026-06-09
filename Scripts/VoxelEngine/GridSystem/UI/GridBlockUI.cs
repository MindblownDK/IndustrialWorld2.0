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
                var btn = T.SmallButton(lt.DisplayName(), () => tank.SetLiquidType(captured),
                    active ? lt.Color() : (Color?)null);
                btn.SetEnabled(tank.stored <= 0.001f || active);
                typeRow.Add(btn);
            }
            p.Add(typeRow);
            if (tank.stored > 0.001f)
                p.Add(T.Muted("Drain the tank to change its liquid type."));

            p.Add(T.Spacer(8));
            var actions = Row();
            actions.Add(T.SmallButton("⊘  Drain (void)", () => tank.Drain(), T.AccentRed));
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

            // Internal water tank visual.
            var gaugeRow = Row();
            gaugeRow.style.justifyContent = Justify.Center;
            gaugeRow.Add(T.TankGauge("Water", gen.WaterFill01, new Color(0.25f, 0.55f, 0.95f),
                $"{gen.waterStored:0} / {gen.waterCapacity:0} L", 64, 110));
            p.Add(gaugeRow);
            p.Add(T.Spacer(6));

            p.Add(T.StatRow("⚡", "Power Use", $"{gen.CurrentWattage:0} W", T.AccentGold));
            p.Add(T.StatRow("🟦", "H₂ Rate", $"{gen.hydrogenPerSecond:0.#}/s", T.AccentCyan));
            p.Add(T.StatRow("🟩", "O₂ Rate", $"{gen.oxygenPerSecond:0.#}/s", T.AccentGreen));
            p.Add(T.Spacer(4));

            // 4 ice slots.
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
                $"{bat.storedWh:0} / {bat.capacityWh:0} Wh", 70, 120));
            p.Add(gaugeRow);
            p.Add(T.Spacer(6));

            var (fill, _) = T.ProgressBar(bat.Fill01, T.AccentGreen, 8, true);
            p.Add(fill);
            p.Add(T.Spacer(4));
            p.Add(T.StatRow("⚡", "Max Charge", $"{bat.maxChargeRate:0} W", T.AccentCyan));
            p.Add(T.StatRow("🔌", "Max Discharge", $"{bat.maxDischargeRate:0} W", T.AccentAmber));
            if (bat.Grid != null)
                p.Add(T.StatRow("⚖", "Grid Balance", $"{bat.Grid.PowerBalance:0} W",
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
            p.Add(T.StatRow("⚡", "Power/Shot", $"{gw.powerPerShot:0} W", T.AccentGold));
            p.Add(T.Spacer(4));

            p.Add(GridUIHelpers.SectionTitle("Ammunition"));
            p.Add(GridUIHelpers.WeightHeader(MassUtil.ContainerMass(gw.ammo), "Ammo"));
            var grid = T.SlotGrid(6);
            for (int i = 0; i < gw.ammo.Size; i++)
                grid.Add(slot(gw.ammo, i, gw.ammo.GetSlot(i), false, true));
            p.Add(grid);
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
            if (block.PowerDraw > 0)   p.Add(T.StatRow("⚡", "Power Use", $"{block.PowerDraw:0} W", T.AccentGold));
            if (block.PowerOutput > 0) p.Add(T.StatRow("🔌", "Power Out", $"{block.PowerOutput:0} W", T.AccentGreen));
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
