// Assets/Scripts/VoxelEngine/GridSystem/UI/GridBlockUI.cs
//
// Builds the right-hand machine panel for an interactable grid block. Hooked into
// GameUIController's existing panel system, so it gets the real drag/drop slot
// widget (BuildSlot) and live container watching for free.
//
// Handles: Liquid Tank, Gas Tank, H2/O2 Generator, Battery, Cargo Container.
// (Chemical Plant / Refinery panels arrive in Phase 3.)

using UnityEngine;
using System.Collections.Generic;
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
                case GridGasTank gt:        return GasTankPanel(gt, slot);
                case GridH2O2Generator h2:  return MakeScrollable(H2O2Panel(h2, slot));
                case GridBattery bat:       return BatteryPanel(bat, slot);
                case GridContainmentVault cv: return MakeScrollable(ContainmentVaultPanel(cv, slot));
                case GridSingularityHarvester sh: return MakeScrollable(HarvesterPanel(sh, slot));
                case GridLocatorBlock loc:  return MakeScrollable(LocatorPanel(loc));
                case GridCargoContainer cc: return CargoPanel(cc, slot);
                case GridWeapon gw:         return MakeScrollable(WeaponPanel(gw, slot));
                case GridRefinery rf:       return MakeScrollable(ProcessorPanel("⚗ Ship Refinery", rf.Current, rf.Progress01, rf.PowerDraw, rf.knownRecipes, rf.Grid, rf.selectedRecipe, r => rf.selectedRecipe = r));
                case GridChemicalPlant cp:  return MakeScrollable(ProcessorPanel("🧪 Ship Chemical Plant", cp.Current, cp.Progress01, cp.PowerDraw, cp.knownRecipes, cp.Grid, cp.selectedRecipe, r => cp.selectedRecipe = r));
                case GridPortableReactor pr: return MakeScrollable(ReactorPanel(pr, slot));
                case GridDockingPort dp:    return DockingPortPanel(dp, slot);
                case GridLandingGear lg:    return LandingGearPanel(lg);
                case GridWheel wh:          return WheelPanel(wh);
                case GridSolarPanel sp:     return SolarPanel(sp);
                case GridHydrogenEngine he: return MakeScrollable(HydrogenEnginePanel(he));
                case GridDrill dr:          return MakeScrollable(DrillPanel(dr, slot));
                case GridElectricFurnace ef: return MakeScrollable(FurnacePanel(ef, slot));
                case GridBeacon bc:         return BeaconPanel(bc);
                case GridOreDetector od:    return OreDetectorPanel(od);
                case GridBiofarm bio:      return MakeScrollable(BiofarmPanel(bio, slot));
                case GridCryobed cryo:      return MakeScrollable(CryobedPanel(cryo));
                case GridSlidingDoor door:  return SlidingDoorPanel(door);
                case VoxelEngine.Simulation.GridLightBlock gl: return GridLightPanel(gl);
                default:                    return GenericPanel(block);
            }
        }

        /// <summary>
        /// Wraps a machine panel's content in a vertical ScrollView so tall panels
        /// (recipe books, many slot rows, tank gauges) never clip their slots —
        /// everything stays inside the box, scrollable when needed.
        /// </summary>
        private static VisualElement MakeScrollable(VisualElement panel)
        {
            if (panel == null) return panel;
            var children = new List<VisualElement>();
            foreach (var child in panel.Children()) children.Add(child);
            foreach (var child in children) child.RemoveFromHierarchy();

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.marginTop = 2;
            T.StyleScroller(scroll);
            foreach (var child in children) scroll.Add(child);
            panel.Add(scroll);
            return panel;
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
                    if (active) return;
                    if (tank.stored <= 0.001f)
                    {
                        if (tank.SetLiquidType(captured))
                            VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                        return;
                    }

                    VoxelEngine.UI.GameUIController.Instance?.ShowTankTypeVoidConfirmation(
                        "Liquid", captured.DisplayName(), tank.stored, () =>
                        {
                            tank.Drain();
                            tank.SetLiquidType(captured);
                            VoxelEngine.UI.BuildFeedbackHud.Show("Liquid tank changed", captured.DisplayName(), null, captured.Color());
                        });
                }, active ? lt.Color() : (Color?)null);
                typeRow.Add(btn);
            }
            p.Add(typeRow);
            if (tank.stored > 0.001f)
                p.Add(T.Muted("Selecting another type asks whether to void liquid or cancel."));

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
        private static VisualElement GasTankPanel(GridGasTank tank, MachineUIs.SlotBuilder slot = null)
        {
            tank.EnsureContainers();
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
                    if (active) return;
                    if (tank.stored <= 0.001f)
                    {
                        if (tank.SetGasType(captured)) VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                        return;
                    }

                    VoxelEngine.UI.GameUIController.Instance?.ShowTankTypeVoidConfirmation(
                        "Gas", captured.ToString(), tank.stored, () =>
                        {
                            tank.Drain();
                            tank.SetGasType(captured);
                            VoxelEngine.UI.BuildFeedbackHud.Show("Gas tank changed", captured.ToString(), null, T.AccentCyan);
                        });
                }, active ? T.AccentCyan : (Color?)null);
                typeRow.Add(btn);
            }
            p.Add(typeRow);
            if (tank.stored > 0.001f) p.Add(T.Muted("Selecting another type asks whether to void gas or cancel."));
            p.Add(T.Spacer(6));
            var modeRow = Row();
            modeRow.Add(T.SmallButton("Auto", () => { tank.mode = GridTankMode.Auto; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                tank.mode == GridTankMode.Auto ? T.AccentGreen : T.BgSlot));
            modeRow.Add(T.SmallButton("Stockpile", () => { tank.mode = GridTankMode.Stockpile; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                tank.mode == GridTankMode.Stockpile ? T.AccentAmber : T.BgSlot));
            p.Add(modeRow);

            // Portable H₂ dock when configured for hydrogen.
            if (tank.IsHydrogenMode && slot != null)
            {
                p.Add(T.Spacer(8));
                p.Add(GridUIHelpers.SectionTitle("H₂ Fill Dock"));
                var dock = T.SlotGrid(1);
                dock.Add(slot(tank.PortableSlot, 0, tank.PortableSlot.GetSlot(0), false, true));
                p.Add(dock);
                p.Add(T.Muted("Place a Portable Hydrogen Tank or a hydrogen jetpack (Hydrogen Boost / Hybrid) here to fill it from bulk ship H₂."));
            }

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
        private static VisualElement BatteryPanel(GridBattery bat, MachineUIs.SlotBuilder slot)
        {
            var p = T.MachinePanel();
            Color initialStateColor = BatteryStateColor(bat);
            var (hdr, _, statePill, stateLabel) = T.HeaderRow("🔋 Battery", bat.TransferState, initialStateColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGreen));

            // Premium segmented charge gauge runs in place. The surrounding labels below
            // are also refreshed in place, so mode buttons never disappear/recreate on
            // every GridEntity power tick.
            p.Add(BuildBatterySegmentGauge(bat));
            p.Add(T.Spacer(6));
            p.Add(T.StatRow("⚡", "Max Charge", PowerFormat.Watts(bat.maxChargeRate), T.AccentCyan));
            p.Add(T.StatRow("🔌", "Max Discharge", PowerFormat.Watts(bat.maxDischargeRate), T.AccentAmber));
            Label chargingNow = AddLiveBatteryStat(p, "↘", "Charging Now", T.AccentGreen);
            Label dischargingNow = AddLiveBatteryStat(p, "↗", "Discharging Now", T.AccentAmber);
            p.Add(T.Spacer(4));

            var modeRow = Row();
            modeRow.Add(T.SmallButton("Auto", () => { bat.mode = GridBatteryMode.Auto; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                bat.mode == GridBatteryMode.Auto ? T.AccentGreen : T.BgSlot));
            modeRow.Add(T.SmallButton("Recharge", () => { bat.mode = GridBatteryMode.Recharge; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                bat.mode == GridBatteryMode.Recharge ? T.AccentCyan : T.BgSlot));
            modeRow.Add(T.SmallButton("Discharge", () => { bat.mode = GridBatteryMode.Discharge; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); },
                bat.mode == GridBatteryMode.Discharge ? T.AccentAmber : T.BgSlot));
            p.Add(modeRow);
            Label modeReadout = AddLiveBatteryStat(p, "⚙", "Mode", T.TextSecondary);
            Label gridBalance = bat.Grid != null ? AddLiveBatteryStat(p, "⚖", "Grid Balance", T.AccentGreen) : null;

            // The ship battery owns the same one-item rechargeable dock as the
            // world battery. It is deliberately part of this in-place panel so the
            // player can drag a portable battery / power jetpack in without flicker.
            bat.EnsureContainers();
            p.Add(T.Divider());
            p.Add(GridUIHelpers.SectionTitle("Device Charger"));
            var chargerGrid = T.SlotGrid(1);
            chargerGrid.Add(slot(bat.ChargeSlot, 0, bat.ChargeSlot.GetSlot(0), false, true));
            p.Add(chargerGrid);
            var chargerReadout = new Label("NO DEVICE DOCKED");
            chargerReadout.style.fontSize = 10;
            chargerReadout.style.color = new StyleColor(T.TextMuted);
            chargerReadout.style.marginTop = 4;
            chargerReadout.pickingMode = PickingMode.Ignore;
            p.Add(chargerReadout);
            var chargerFlow = new Label("HOLD A DEVICE + RMB  //  SHIFT + RMB = FULL");
            chargerFlow.style.fontSize = 8;
            chargerFlow.style.letterSpacing = 0.55f;
            chargerFlow.style.color = new StyleColor(T.TextMuted);
            chargerFlow.style.marginTop = 2;
            chargerFlow.pickingMode = PickingMode.Ignore;
            p.Add(chargerFlow);

            void RefreshLiveValues()
            {
                if (bat == null) return;
                Color stateColor = BatteryStateColor(bat);
                if (stateLabel != null)
                {
                    stateLabel.text = bat.TransferState;
                    stateLabel.style.color = new StyleColor(stateColor);
                }
                if (statePill != null)
                {
                    statePill.style.backgroundColor = new StyleColor(new Color(stateColor.r, stateColor.g, stateColor.b, 0.22f));
                    T.Border(statePill, 1f, new Color(stateColor.r, stateColor.g, stateColor.b, 0.55f));
                    if (statePill.childCount > 0 && statePill[0] is VisualElement dot)
                        dot.style.backgroundColor = new StyleColor(stateColor);
                }
                if (chargingNow != null)
                    chargingNow.text = PowerFormat.Watts(bat.CurrentChargeWatts);
                if (dischargingNow != null)
                    dischargingNow.text = PowerFormat.Watts(bat.CurrentDischargeWatts);
                if (modeReadout != null)
                    modeReadout.text = bat.mode.ToString();
                if (gridBalance != null && bat.Grid != null)
                {
                    float balance = bat.Grid.PowerBalance;
                    gridBalance.text = PowerFormat.Watts(balance);
                    gridBalance.style.color = new StyleColor(balance >= 0f ? T.AccentGreen : T.AccentRed);
                }
                if (chargerReadout != null)
                {
                    bat.GetDockedItemCharge(out int itemStored, out int itemCapacity);
                    var docked = bat.ChargeSlot != null ? bat.ChargeSlot.GetSlot(0) : null;
                    if (docked == null || docked.IsEmpty || itemCapacity <= 0)
                    {
                        chargerReadout.text = "NO DEVICE DOCKED";
                        chargerReadout.style.color = new StyleColor(T.TextMuted);
                    }
                    else
                    {
                        float fill = Mathf.Clamp01(itemStored / (float)itemCapacity);
                        chargerReadout.text = $"{docked.item.displayName}  ·  {itemStored} / {itemCapacity} Wh  ({fill * 100f:0}%)";
                        chargerReadout.style.color = new StyleColor(bat.IsChargingItem ? T.AccentGreen : T.TextSecondary);
                    }
                }
                if (chargerFlow != null)
                {
                    chargerFlow.text = bat.IsChargingItem
                        ? $"DEVICE CHARGING  ·  {PowerFormat.Watts(bat.CurrentDeviceChargeWatts)}"
                        : "HOLD A DEVICE + RMB  //  SHIFT + RMB = FULL";
                    chargerFlow.style.color = new StyleColor(bat.IsChargingItem ? T.AccentGreen : T.TextMuted);
                }
            }

            RefreshLiveValues();
            p.schedule.Execute(RefreshLiveValues).Every(100);
            return p;
        }

        private static Color BatteryStateColor(GridBattery battery)
        {
            if (battery == null) return T.TextMuted;
            return battery.IsCharging ? T.AccentGreen
                 : battery.IsDischarging ? T.AccentAmber
                 : T.AccentCyan;
        }

        private static Label AddLiveBatteryStat(VisualElement parent, string icon, string label, Color valueColor)
        {
            var row = T.StatRow(icon, label, "—", valueColor);
            parent.Add(row);
            return row.childCount > 0 ? row[row.childCount - 1] as Label : null;
        }

        /// <summary>
        /// 12-segment eased battery gauge for grid (ship/base) batteries — mirrors the
        /// world-battery panel look. Self-animating: eases from 0 on mount (power-on
        /// sweep) and then tracks fill + stored/capacity + % live every 30 ms.
        /// </summary>
        private static VisualElement BuildBatterySegmentGauge(GridBattery bat)
        {
            const int SegCount = 12;
            var gaugeRow = Row();
            gaugeRow.style.alignItems = Align.Center;
            gaugeRow.style.marginTop = 4;
            gaugeRow.style.marginBottom = 2;
            gaugeRow.pickingMode = PickingMode.Ignore;

            var segTrack = new VisualElement();
            segTrack.style.flexDirection = FlexDirection.Row;
            segTrack.style.flexGrow = 1;
            segTrack.style.height = 26;
            segTrack.style.paddingTop = 3;  segTrack.style.paddingBottom = 3;
            segTrack.style.paddingLeft = 3; segTrack.style.paddingRight = 3;
            segTrack.style.backgroundColor = new StyleColor(new Color(0.04f, 0.045f, 0.065f, 0.98f));
            T.Radius(segTrack, 7);
            segTrack.pickingMode = PickingMode.Ignore;

            var off = new Color(0.14f, 0.16f, 0.22f);
            var segs = new VisualElement[SegCount];
            for (int i = 0; i < SegCount; i++)
            {
                var seg = new VisualElement();
                seg.style.flexGrow = 1;
                seg.style.marginRight = i < SegCount - 1 ? 2 : 0;
                seg.style.backgroundColor = new StyleColor(off);
                T.Radius(seg, 2);
                seg.pickingMode = PickingMode.Ignore;
                segs[i] = seg;
                segTrack.Add(seg);
            }
            gaugeRow.Add(segTrack);

            var pct = new Label("0%");
            pct.style.width = 58;
            pct.style.fontSize = 18;
            pct.style.unityFontStyleAndWeight = FontStyle.Bold;
            pct.style.unityTextAlign = TextAnchor.MiddleRight;
            pct.style.color = T.TextPrimary;
            pct.style.marginLeft = 10;
            pct.pickingMode = PickingMode.Ignore;
            gaugeRow.Add(pct);

            var stored = new Label("—");
            stored.style.fontSize = 10;
            stored.style.color = T.TextMuted;
            stored.style.unityTextAlign = TextAnchor.MiddleCenter;
            stored.pickingMode = PickingMode.Ignore;

            float smooth = 0f;   // eased fill — drives the power-on sweep on mount
            gaugeRow.schedule.Execute(() =>
            {
                if (bat == null) return;
                float fill = bat.Fill01;
                smooth = Mathf.MoveTowards(smooth, fill, 0.03f * 1.2f);   // ~1.1 s sweep
                Color col = fill > 0.5f ? new Color(0.30f, 0.85f, 0.40f)
                          : fill > 0.25f ? T.AccentAmber : T.AccentRed;
                int lit = Mathf.RoundToInt(smooth * SegCount);
                for (int i = 0; i < SegCount; i++)
                    segs[i].style.backgroundColor = new StyleColor(
                        i < lit ? new Color(col.r, col.g, col.b, 0.88f) : off);
                pct.text = $"{fill * 100f:0}%";
                pct.style.color = new StyleColor(col);
                stored.text = $"{PowerFormat.WattHours(bat.storedWh)} / {PowerFormat.WattHours(bat.capacityWh)}";
            }).Every(30);

            var box = new VisualElement();
            box.pickingMode = PickingMode.Ignore;
            box.Add(gaugeRow);
            box.Add(stored);
            return box;
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
            // Animated phosphor segment fill (LCD cargo matrix style).
            var segTrack = LcdHudTheme.CreateSegmentTrack(14, out var segs, height: 9f);
            segTrack.style.marginTop = 2;
            p.Add(segTrack);
            LcdHudTheme.AnimateSegments(segs, cc.Fill01,
                cc.Fill01 >= 0.99f ? T.AccentRed : T.AccentGold);
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

        // ── CONTAINMENT VAULT ───────────────────────────────────────────────
        // The flagship panel of the Phase 5 exotic-matter loop: a live black-hole
        // visual, the containment pressure gauge with its stable band, power and
        // annihilation warnings — premium sci-fi, LCD-scanned like every machine.
        private static VisualElement ContainmentVaultPanel(GridContainmentVault cv, MachineUIs.SlotBuilder slot)
        {
            if (cv.container == null) cv.OnPlaced();
            var p = T.MachinePanel();
            p.style.width = 470;

            string status = cv.FieldStatus;
            Color statusColor = cv.FieldStatus switch
            {
                "ANNIHILATION" => T.AccentRed,
                "CRITICAL"     => T.AccentRed,
                "LOW PRESSURE" => T.AccentAmber,
                "NO POWER"     => T.AccentAmber,
                _              => T.AccentGreen,
            };
            var (hdr, _, _, _) = T.HeaderRow("◉ Containment Vault", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentPurple));

            // ── The contained singularity (live) ──
            // 168×168 stage; EVERY element is explicitly centred on (84, 84) so the
            // rings hug the black hole and the orbiting spots trace the disc ellipse.
            var coreBox = new VisualElement();
            coreBox.style.height = 168;
            coreBox.style.marginTop = 4;
            coreBox.style.marginBottom = 6;
            coreBox.pickingMode = PickingMode.Ignore;
            p.Add(coreBox);
            const float CX = 84f, CY = 84f;

            // Warm accretion glow behind everything (generated radial texture).
            var glow = new VisualElement();
            glow.style.position = Position.Absolute;
            glow.style.width = 150;
            glow.style.height = 150;
            glow.style.left = CX - 75f;
            glow.style.top = CY - 75f;
            glow.style.backgroundImage = new StyleBackground(Background.FromTexture2D(GlowTexture()));
            glow.pickingMode = PickingMode.Ignore;
            coreBox.Add(glow);

            // Lensed photon ring: three nested ellipses (bloom layering).
            var ringOuter = Ellipse(172, 62, new Color(1f, 0.55f, 0.22f, 0.16f), 2);
            var ringMid   = Ellipse(164, 56, new Color(1f, 0.62f, 0.28f, 0.38f), 3);
            var ringHot   = Ellipse(156, 50, new Color(1f, 0.86f, 0.68f, 0.85f), 2);
            PositionCentered(ringOuter, 172, 62, CX, CY);
            PositionCentered(ringMid, 164, 56, CX, CY);
            PositionCentered(ringHot, 156, 50, CX, CY);
            coreBox.Add(ringOuter); coreBox.Add(ringMid); coreBox.Add(ringHot);

            // The void itself.
            var core = new VisualElement();
            core.style.position = Position.Absolute;
            core.style.width = 64;
            core.style.height = 64;
            core.style.left = CX - 32f;
            core.style.top = CY - 32f;
            core.style.backgroundColor = new StyleColor(new Color(0.005f, 0.004f, 0.008f, 1f));
            UITheme.Radius(core, 32f);
            UITheme.Border(core, 1, new Color(0.55f, 0.16f, 0.10f, 0.55f));
            core.pickingMode = PickingMode.Ignore;
            coreBox.Add(core);

            // Orbiting hot-spots (animated along the disc ellipse).
            var spotA = SpotDot(); var spotB = SpotDot(); var spotC = SpotDot();
            coreBox.Add(spotA); coreBox.Add(spotB); coreBox.Add(spotC);

            // Violet containment field rings.
            var fieldOuter = Ellipse(196, 74, new Color(0.58f, 0.34f, 0.95f, 0.22f), 1);
            var fieldInner = Ellipse(184, 66, new Color(0.58f, 0.34f, 0.95f, 0.34f), 1);
            PositionCentered(fieldOuter, 196, 74, CX, CY);
            PositionCentered(fieldInner, 184, 66, CX, CY);
            coreBox.Add(fieldOuter); coreBox.Add(fieldInner);

            // ── Pressure gauge with the stable band ──
            p.Add(GridUIHelpers.SectionTitle("Containment Pressure"));
            var track = new VisualElement();
            track.style.height = 18;
            track.style.marginTop = 2;
            track.style.backgroundColor = new StyleColor(new Color(0.04f, 0.045f, 0.065f));
            UITheme.Radius(track, 4f);
            UITheme.Border(track, 1, T.BorderDim);
            track.style.overflow = Overflow.Hidden;
            track.pickingMode = PickingMode.Ignore;
            p.Add(track);

            float target = Mathf.Max(1f, cv.targetPressure);
            // Stable band (green).
            var band = new VisualElement();
            band.style.position = Position.Absolute;
            band.style.top = 2; band.style.bottom = 2;
            band.style.left = new StyleLength(new Length(Mathf.Clamp01(cv.stablePressureMin / target) * 100f, LengthUnit.Percent));
            band.style.width = new StyleLength(new Length(Mathf.Clamp01((cv.stablePressureMax - cv.stablePressureMin) / target) * 100f, LengthUnit.Percent));
            band.style.backgroundColor = new StyleColor(new Color(0.22f, 0.78f, 0.42f, 0.16f));
            band.pickingMode = PickingMode.Ignore;
            track.Add(band);
            // Critical line.
            var critLine = new VisualElement();
            critLine.style.position = Position.Absolute;
            critLine.style.top = 0; critLine.style.bottom = 0;
            critLine.style.left = new StyleLength(new Length(Mathf.Clamp01(cv.criticalPressure / target) * 100f, LengthUnit.Percent));
            critLine.style.width = 2;
            critLine.style.backgroundColor = new StyleColor(new Color(0.82f, 0.22f, 0.18f, 0.7f));
            critLine.pickingMode = PickingMode.Ignore;
            track.Add(critLine);
            // Pressure marker.
            var marker = new VisualElement();
            marker.style.position = Position.Absolute;
            marker.style.top = 1; marker.style.bottom = 1;
            marker.style.width = 3;
            marker.style.left = new StyleLength(new Length(50f, LengthUnit.Percent));
            marker.style.backgroundColor = new StyleColor(new Color(0.55f, 0.9f, 1f, 0.95f));
            marker.pickingMode = PickingMode.Ignore;
            track.Add(marker);

            var pressureText = T.Muted($"Pressure {cv.Pressure:0.0} · Target {target:0} · Stable {cv.stablePressureMin:0}–{cv.stablePressureMax:0}");
            pressureText.style.marginTop = 3;
            p.Add(pressureText);

            // ── Warning banner (live) ──
            var warn = new Label();
            warn.style.marginTop = 4;
            warn.style.paddingTop = 5;
            warn.style.paddingBottom = 5;
            warn.style.paddingLeft = 8;
            warn.style.paddingRight = 8;
            warn.style.fontSize = 10;
            warn.style.unityFontStyleAndWeight = FontStyle.Bold;
            warn.style.letterSpacing = 1.4f;
            warn.style.unityTextAlign = TextAnchor.MiddleCenter;
            warn.style.backgroundColor = new StyleColor(new Color(0.16f, 0.04f, 0.04f, 0.9f));
            UITheme.Radius(warn, 6f);
            UITheme.Border(warn, 1, new Color(0.82f, 0.22f, 0.18f, 0.6f));
            warn.style.color = new Color(1f, 0.35f, 0.25f);
            warn.style.display = DisplayStyle.None;
            p.Add(warn);

            p.Add(T.Spacer(6));

            // ── Live stats ──
            int am = 0, dm = 0;
            if (cv.container != null)
                for (int i = 0; i < cv.container.Size; i++)
                {
                    var st = cv.container.GetSlot(i);
                    if (st == null || st.IsEmpty || st.item == null) continue;
                    if (st.item.itemId != null && st.item.itemId.IndexOf("antimatter", System.StringComparison.OrdinalIgnoreCase) >= 0) am += st.count;
                    else if (st.item.itemId != null && st.item.itemId.IndexOf("dark", System.StringComparison.OrdinalIgnoreCase) >= 0) dm += st.count;
                }
            p.Add(T.StatRow("⚡", "Field Power", PowerFormat.Watts(cv.PowerDraw), cv.Grid != null && cv.Grid.HasPower ? T.AccentCyan : T.AccentAmber));
            p.Add(T.StatRow("◉", "Exotic Load", $"{cv.ExoticUnits} units", T.AccentPurple));
            p.Add(T.StatRow("💠", "Antimatter", $"{am}", new Color(1f, 0.4f, 0.7f)));
            p.Add(T.StatRow("🌌", "Dark Matter", $"{dm}", new Color(0.55f, 0.35f, 0.95f)));
            p.Add(T.Spacer(4));

            p.Add(GridUIHelpers.SectionTitle("Contained Matter"));
            var invScroll = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(invScroll);
            invScroll.style.maxHeight = 200;
            invScroll.style.flexShrink = 1;
            invScroll.contentContainer.style.width = Length.Percent(100);
            var grid = T.SlotGrid(6);
            if (cv.container != null)
                for (int i = 0; i < cv.container.Size; i++)
                    grid.Add(slot(cv.container, i, cv.container.GetSlot(i), false, true));
            invScroll.Add(grid);
            p.Add(invScroll);

            p.Add(T.Spacer(6));
            p.Add(T.SmallButton("🛰  Open Ship Terminal",
                () => VoxelEngine.UI.GameUIController.Instance?.OpenGridTerminal(cv.Grid), T.AccentCyan));

            // ── Live animation loop ──
            float shownPressure = cv.Pressure;
            var spots = new[] { spotA, spotB, spotC };
            p.schedule.Execute(() => AnimateVaultPanel(p, cv, spots, marker, warn, pressureText, ref shownPressure))
                .Every(80);
            return p;
        }

        private static void AnimateVaultPanel(VisualElement p, GridContainmentVault cv,
            VisualElement[] spots, VisualElement marker, Label warn, Label pressureText, ref float shownPressure)
        {
            // The panel may have been closed/rebuilt — stop animating detached trees.
            if (p == null || p.panel == null) return;
            float t = Time.unscaledTime;

            // ── Orbiting hot-spots along the disc ellipse ──
            const float ax = 76f, bx = 26f;   // ellipse radii
            for (int i = 0; i < spots.Length; i++)
            {
                var s = spots[i];
                if (s == null) continue;
                float phase = t * (0.9f + i * 0.45f) + i * 2.1f;
                float ca = Mathf.Cos(phase), sa = Mathf.Sin(phase);
                s.style.left = (84 + ax * ca) - 4f;
                s.style.top = (84 + bx * sa) - 4f;
                // Front of the disc (lower half) glows brighter — the tilted-disc read.
                float front = Mathf.Clamp01(sa * 0.85f + 0.55f);
                s.style.backgroundColor = new StyleColor(new Color(1f, 0.72f, 0.38f, 0.35f + 0.6f * front));
            }

            // ── Pressure marker (smoothed) ──
            if (marker != null && cv != null)
            {
                shownPressure = Mathf.MoveTowards(shownPressure, cv.Pressure, 9f * Time.unscaledDeltaTime);
                float target = Mathf.Max(1f, cv.targetPressure);
                marker.style.left = new StyleLength(new Length(Mathf.Clamp01(shownPressure / target) * 100f, LengthUnit.Percent));
                marker.style.backgroundColor = new StyleColor(
                    cv.Pressure < cv.criticalPressure ? new Color(1f, 0.25f, 0.2f, 0.95f)
                    : cv.Pressure < cv.stablePressureMin ? new Color(1f, 0.7f, 0.25f, 0.95f)
                    : new Color(0.55f, 0.95f, 1f, 0.95f));
                if (pressureText != null)
                    pressureText.text = $"Pressure {cv.Pressure:0.0} · Target {target:0} · Stable {cv.stablePressureMin:0}–{cv.stablePressureMax:0}";
            }

            // ── Warning banner (pulsing when the field degrades) ──
            if (warn != null && cv != null)
            {
                bool danger = cv.Pressure < cv.stablePressureMin;
                if (danger)
                {
                    warn.style.display = DisplayStyle.Flex;
                    bool critical = cv.Pressure < cv.criticalPressure;
                    warn.text = critical
                        ? "⚠ CONTAINMENT FIELD FAILING — EXOTIC MATTER AT RISK"
                        : "⚠ CONTAINMENT PRESSURE BELOW STABLE RANGE";
                    float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * (critical ? 5.5f : 3f)));
                    warn.style.color = new Color(1f, 0.3f, 0.22f, pulse);
                    warn.style.backgroundColor = new StyleColor(new Color(0.16f, 0.03f, 0.03f, 0.55f + 0.4f * pulse));
                }
                else
                {
                    warn.style.display = DisplayStyle.None;
                }
            }
        }

        private static VisualElement SpotDot()
        {
            var dot = new VisualElement();
            dot.style.position = Position.Absolute;
            dot.style.width = 8;
            dot.style.height = 8;
            UITheme.Radius(dot, 4f);
            dot.style.backgroundColor = new StyleColor(new Color(1f, 0.75f, 0.4f, 0.9f));
            dot.pickingMode = PickingMode.Ignore;
            return dot;
        }

        private static void PositionCentered(VisualElement el, float width, float height, float cx, float cy)
        {
            el.style.left = cx - width / 2f;
            el.style.top = cy - height / 2f;
        }

        // ── SINGULARITY HARVESTER ───────────────────────────────────────────
        // The harvester finally gets its flagship panel: same live black-hole
        // visual as the vault, plus the harvest loop — status, efficiency bar,
        // horizon distance, power and the internal buffer.
        private static VisualElement HarvesterPanel(GridSingularityHarvester sh, MachineUIs.SlotBuilder slot)
        {
            if (sh.buffer == null) sh.OnPlaced();
            var p = T.MachinePanel();
            p.style.width = 470;

            bool active = sh.IsHarvesting;
            var (hdr, _, _, _) = T.HeaderRow("◉ Singularity Harvester",
                active ? "HARVESTING" : sh.Status.ToUpperInvariant(),
                active ? T.AccentGreen : sh.Status == "No Power" ? T.AccentAmber : T.AccentDim);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentPurple));

            // ── The contained black hole (live) ──
            var coreBox = new VisualElement();
            coreBox.style.height = 150;
            coreBox.style.marginTop = 4;
            coreBox.style.marginBottom = 6;
            coreBox.pickingMode = PickingMode.Ignore;
            p.Add(coreBox);
            const float CX = 84f, CY = 75f;

            var glow = new VisualElement();
            glow.style.position = Position.Absolute;
            glow.style.width = 128;
            glow.style.height = 128;
            glow.style.left = CX - 64f;
            glow.style.top = CY - 64f;
            glow.style.backgroundImage = new StyleBackground(Background.FromTexture2D(GlowTexture()));
            glow.pickingMode = PickingMode.Ignore;
            coreBox.Add(glow);

            var ringOuter = Ellipse(148, 52, new Color(1f, 0.55f, 0.22f, 0.16f), 2);
            var ringMid = Ellipse(140, 47, new Color(1f, 0.62f, 0.28f, 0.38f), 3);
            var ringHot = Ellipse(134, 42, new Color(1f, 0.86f, 0.68f, 0.85f), 2);
            PositionCentered(ringOuter, 148, 52, CX, CY);
            PositionCentered(ringMid, 140, 47, CX, CY);
            PositionCentered(ringHot, 134, 42, CX, CY);
            coreBox.Add(ringOuter); coreBox.Add(ringMid); coreBox.Add(ringHot);

            var core = new VisualElement();
            core.style.position = Position.Absolute;
            core.style.width = 52;
            core.style.height = 52;
            core.style.left = CX - 26f;
            core.style.top = CY - 26f;
            core.style.backgroundColor = new StyleColor(new Color(0.005f, 0.004f, 0.008f, 1f));
            UITheme.Radius(core, 26f);
            UITheme.Border(core, 1, new Color(0.55f, 0.16f, 0.10f, 0.55f));
            core.pickingMode = PickingMode.Ignore;
            coreBox.Add(core);

            var hSpotA = SpotDot(); var hSpotB = SpotDot(); var hSpotC = SpotDot();
            coreBox.Add(hSpotA); coreBox.Add(hSpotB); coreBox.Add(hSpotC);

            // Containment coil rings (cyan — the block's real coils).
            var coilA = Ellipse(166, 60, new Color(0.2f, 0.65f, 0.9f, 0.30f), 1);
            var coilB = Ellipse(158, 54, new Color(0.2f, 0.65f, 0.9f, 0.42f), 1);
            PositionCentered(coilA, 166, 60, CX, CY);
            PositionCentered(coilB, 158, 54, CX, CY);
            coreBox.Add(coilA); coreBox.Add(coilB);

            // ── Efficiency gauge ──
            p.Add(GridUIHelpers.SectionTitle("Harvest Efficiency"));
            var track = new VisualElement();
            track.style.height = 14;
            track.style.marginTop = 2;
            track.style.backgroundColor = new StyleColor(new Color(0.04f, 0.045f, 0.065f));
            UITheme.Radius(track, 4f);
            UITheme.Border(track, 1, T.BorderDim);
            track.style.overflow = Overflow.Hidden;
            track.pickingMode = PickingMode.Ignore;
            p.Add(track);
            var effFill = new VisualElement();
            effFill.style.position = Position.Absolute;
            effFill.style.top = 1; effFill.style.bottom = 1; effFill.style.left = 1;
            effFill.style.width = Length.Percent(0f);
            effFill.style.backgroundColor = new StyleColor(new Color(0.58f, 0.34f, 0.95f, 0.85f));
            effFill.pickingMode = PickingMode.Ignore;
            track.Add(effFill);
            var effText = T.Muted("Efficiency 0%");
            effText.style.marginTop = 3;
            p.Add(effText);

            // ── Warning line (rare drops stuck) ──
            var hWarn = new Label();
            hWarn.style.marginTop = 4;
            hWarn.style.paddingTop = 4;
            hWarn.style.paddingBottom = 4;
            hWarn.style.paddingLeft = 8;
            hWarn.style.paddingRight = 8;
            hWarn.style.fontSize = 10;
            hWarn.style.unityFontStyleAndWeight = FontStyle.Bold;
            hWarn.style.letterSpacing = 1.2f;
            hWarn.style.unityTextAlign = TextAnchor.MiddleCenter;
            hWarn.style.backgroundColor = new StyleColor(new Color(0.16f, 0.04f, 0.04f, 0.9f));
            UITheme.Radius(hWarn, 6f);
            UITheme.Border(hWarn, 1, new Color(0.82f, 0.22f, 0.18f, 0.6f));
            hWarn.style.color = new Color(1f, 0.35f, 0.25f);
            hWarn.style.display = DisplayStyle.None;
            p.Add(hWarn);

            p.Add(T.Spacer(6));

            p.Add(T.StatRow("🎯", "Status", sh.Status, active ? T.AccentGreen : T.AccentDim));
            p.Add(T.StatRow("◉", "Remnant", sh.NearestRemnant, T.AccentPurple));
            p.Add(T.StatRow("📏", "Horizon Distance", sh.HorizonDistanceKm >= float.MaxValue ? "—" : $"{sh.HorizonDistanceKm:0} km", T.AccentCyan));
            p.Add(T.StatRow("⚡", "Power Draw", PowerFormat.Watts(sh.PowerDraw), sh.Grid != null && sh.Grid.HasPower ? T.AccentCyan : T.AccentAmber));
            p.Add(T.Spacer(4));

            p.Add(GridUIHelpers.SectionTitle("Internal Buffer"));
            var invScroll = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(invScroll);
            invScroll.style.maxHeight = 160;
            invScroll.style.flexShrink = 1;
            invScroll.contentContainer.style.width = Length.Percent(100);
            var grid = T.SlotGrid(4);
            if (sh.buffer != null)
                for (int i = 0; i < sh.buffer.Size; i++)
                    grid.Add(slot(sh.buffer, i, sh.buffer.GetSlot(i), false, true));
            invScroll.Add(grid);
            p.Add(invScroll);

            p.Add(T.Spacer(6));
            p.Add(T.SmallButton("🛰  Open Ship Terminal",
                () => VoxelEngine.UI.GameUIController.Instance?.OpenGridTerminal(sh.Grid), T.AccentCyan));

            var hSpots = new[] { hSpotA, hSpotB, hSpotC };
            p.schedule.Execute(() => AnimateHarvesterPanel(p, sh, hSpots, effFill, effText, hWarn)).Every(80);
            return p;
        }

        private static void AnimateHarvesterPanel(VisualElement p, GridSingularityHarvester sh,
            VisualElement[] spots, VisualElement effFill, Label effText, Label warn)
        {
            if (p == null || p.panel == null) return;
            float t = Time.unscaledTime;

            const float CX = 84f, CY = 75f, AX = 66f, BX = 21f;
            for (int i = 0; i < spots.Length; i++)
            {
                var s = spots[i];
                if (s == null) continue;
                float phase = t * (1.1f + i * 0.5f) + i * 2.1f;
                float ca = Mathf.Cos(phase), sa = Mathf.Sin(phase);
                s.style.left = (CX + AX * ca) - 4f;
                s.style.top = (CY + BX * sa) - 4f;
                float front = Mathf.Clamp01(sa * 0.85f + 0.55f);
                s.style.backgroundColor = new StyleColor(new Color(1f, 0.72f, 0.38f, 0.35f + 0.6f * front));
            }

            if (effFill != null && sh != null)
            {
                effFill.style.width = new StyleLength(new Length(Mathf.Clamp01(sh.Efficiency01) * 100f, LengthUnit.Percent));
                if (effText != null)
                    effText.text = $"Efficiency {sh.Efficiency01 * 100f:0}%";
            }

            if (warn != null && sh != null)
            {
                bool stuck = sh.Status != null && sh.Status.IndexOf("vault", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (stuck)
                {
                    warn.style.display = DisplayStyle.Flex;
                    warn.text = "⚠ EXOTIC MATTER BUFFERED — ADD A CONTAINMENT VAULT";
                    float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 3f));
                    warn.style.color = new Color(1f, 0.3f, 0.22f, pulse);
                }
                else warn.style.display = DisplayStyle.None;
            }
        }

        // ── STAR LOCATOR ────────────────────────────────────────────────────
        private static VisualElement LocatorPanel(GridLocatorBlock loc)
        {
            var p = T.MachinePanel();
            p.style.width = 460;

            bool tracking = loc.IsTracking;
            var (hdr, _, _, _) = T.HeaderRow("✦ Star Locator",
                tracking ? "TRACKING" : loc.Status.ToUpperInvariant(),
                tracking ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            // Target readout with a live pulse dot.
            var targetRow = new VisualElement();
            targetRow.style.flexDirection = FlexDirection.Row;
            targetRow.style.alignItems = Align.Center;
            targetRow.style.marginTop = 6;
            var pulseDot = new VisualElement();
            pulseDot.style.width = 10;
            pulseDot.style.height = 10;
            UITheme.Radius(pulseDot, 5f);
            pulseDot.style.backgroundColor = new StyleColor(new Color(0.2f, 0.9f, 1f));
            pulseDot.style.marginRight = 8;
            targetRow.Add(pulseDot);
            var targetLabel = new Label(loc.TargetName);
            targetLabel.style.fontSize = 16;
            targetLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            targetLabel.style.color = new Color(0.75f, 0.92f, 1f);
            targetRow.Add(targetLabel);
            p.Add(targetRow);

            var distLabel = T.Muted(loc.TargetDistanceKm >= 0d ? $"{loc.TargetDistanceKm:0} km away" : "—");
            p.Add(distLabel);
            p.Add(T.Spacer(6));

            // Mode toggle.
            p.Add(GridUIHelpers.SectionTitle("Tracking Mode"));
            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.marginBottom = 6;
            var autoBtn = T.SmallButton("◎ AUTO", () =>
            {
                loc.mode = GridLocatorBlock.LocatorMode.Auto;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, loc.mode == GridLocatorBlock.LocatorMode.Auto ? T.AccentGreen : T.AccentDim);
            autoBtn.style.marginRight = 6;
            modeRow.Add(autoBtn);
            var specBtn = T.SmallButton("◈ SPECIFIC", () =>
            {
                loc.mode = GridLocatorBlock.LocatorMode.Specific;
                if (loc.selectedTargetIndex < 0) loc.selectedTargetIndex = 0;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, loc.mode == GridLocatorBlock.LocatorMode.Specific ? T.AccentPurple : T.AccentDim);
            modeRow.Add(specBtn);
            p.Add(modeRow);

            // Target cycle (SPECIFIC mode).
            p.Add(GridUIHelpers.SectionTitle("Target Body"));
            var cycleRow = new VisualElement();
            cycleRow.style.flexDirection = FlexDirection.Row;
            cycleRow.style.alignItems = Align.Center;
            cycleRow.style.marginBottom = 6;
            var prevBtn = T.SmallButton("◀", () => CycleTarget(loc, -1), T.AccentCyan);
            prevBtn.style.marginRight = 8;
            cycleRow.Add(prevBtn);
            var selLabel = new Label(GridLocatorBlock.TargetNameAt(loc.selectedTargetIndex));
            selLabel.style.flexGrow = 1;
            selLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            selLabel.style.color = new Color(0.9f, 0.95f, 1f);
            cycleRow.Add(selLabel);
            var nextBtn = T.SmallButton("▶", () => CycleTarget(loc, 1), T.AccentCyan);
            nextBtn.style.marginLeft = 8;
            cycleRow.Add(nextBtn);
            p.Add(cycleRow);

            p.Add(T.StatRow("⚡", "Power Draw", PowerFormat.Watts(loc.PowerDraw), loc.Grid != null && loc.Grid.HasPower ? T.AccentCyan : T.AccentAmber));
            p.Add(T.StatRow("🎯", "Status", loc.Status, tracking ? T.AccentGreen : T.AccentDim));
            p.Add(T.Muted("Aim the ship at the waypoint marker and engage the warp drive to jump to the target."));
            p.Add(T.Spacer(6));
            p.Add(T.SmallButton("🛰  Open Ship Terminal",
                () => VoxelEngine.UI.GameUIController.Instance?.OpenGridTerminal(loc.Grid), T.AccentCyan));

            // Live refresh of the readouts.
            p.schedule.Execute(() =>
            {
                if (p == null || p.panel == null) return;
                pulseDot.style.backgroundColor = new StyleColor(
                    tracking ? new Color(0.2f, 0.9f, 1f, 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.4f))) : new Color(0.5f, 0.2f, 0.2f));
                targetLabel.text = loc.TargetName;
                distLabel.text = loc.TargetDistanceKm >= 0d ? $"{loc.TargetDistanceKm:0} km away" : "—";
                selLabel.text = GridLocatorBlock.TargetNameAt(loc.selectedTargetIndex);
                selLabel.style.color = loc.mode == GridLocatorBlock.LocatorMode.Specific
                    ? new Color(0.9f, 0.95f, 1f)
                    : new Color(0.5f, 0.55f, 0.65f);
            }).Every(400);
            return p;
        }

        private static void CycleTarget(GridLocatorBlock loc, int delta)
        {
            int count = GridLocatorBlock.TargetCount;
            if (count <= 0) return;
            loc.mode = GridLocatorBlock.LocatorMode.Specific;
            int cur = loc.selectedTargetIndex < 0 ? 0 : loc.selectedTargetIndex;
            loc.selectedTargetIndex = (cur + delta + count) % count;
        }

        private static VisualElement Ellipse(float width, float height, Color borderColor, float borderWidth)
        {
            var el = new VisualElement();
            el.style.position = Position.Absolute;
            el.style.width = width;
            el.style.height = height;
            el.style.borderTopWidth = borderWidth;
            el.style.borderBottomWidth = borderWidth;
            el.style.borderLeftWidth = borderWidth;
            el.style.borderRightWidth = borderWidth;
            el.style.borderTopColor = new StyleColor(borderColor);
            el.style.borderBottomColor = new StyleColor(borderColor);
            el.style.borderLeftColor = new StyleColor(borderColor);
            el.style.borderRightColor = new StyleColor(borderColor);
            el.style.borderTopLeftRadius = width / 2f;
            el.style.borderTopRightRadius = width / 2f;
            el.style.borderBottomLeftRadius = width / 2f;
            el.style.borderBottomRightRadius = width / 2f;
            el.pickingMode = PickingMode.Ignore;
            return el;
        }

        private static Texture2D _glowTex;
        private static Texture2D GlowTexture()
        {
            if (_glowTex != null) return _glowTex;
            const int S = 96;
            _glowTex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                name = "VaultGlowTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[S * S];
            Color inner = new Color(1f, 0.62f, 0.28f);
            Color outer = new Color(0.36f, 0.17f, 0.72f);
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S - 0.5f;
                    float dy = (y + 0.5f) / S - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.1f);
                    Color c = Color.Lerp(outer, inner, Mathf.Clamp01(1f - d * 2.4f));
                    px[y * S + x] = new Color(c.r, c.g, c.b, a);
                }
            }
            _glowTex.SetPixels32(px);
            _glowTex.Apply(false, false);
            return _glowTex;
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

        // ── BIOFARM (11.5) ─────────────────────────────────────────────────────
        private static VisualElement BiofarmPanel(GridBiofarm farm, MachineUIs.SlotBuilder slot)
        {
            if (farm.biomassInput == null) farm.OnPlaced();
            var p = T.MachinePanel();
            Color sc = farm.Status == "Producing" ? T.AccentGreen :
                       farm.Status == "No Power" ? T.AccentRed : T.AccentAmber;
            var (hdr, _, _, _) = T.HeaderRow($"🌿 {farm.blockName}", farm.Status.ToUpperInvariant(), sc);
            p.Add(hdr);
            p.Add(T.AccentDivider(new Color(0.35f, 0.85f, 0.45f)));

            var gaugeRow = Row();
            gaugeRow.style.justifyContent = Justify.SpaceAround;
            gaugeRow.Add(T.TankGauge("Water", farm.WaterFill01, new Color(0.25f,0.55f,0.95f), $"{farm.waterStored:0}/{farm.waterCapacity:0} L", 60, 100));
            gaugeRow.Add(T.TankGauge("O₂", farm.O2Fill01, new Color(0.35f,0.85f,0.55f), $"{farm.o2Stored:0}/{farm.o2Capacity:0} L", 60, 100));
            p.Add(gaugeRow);
            p.Add(T.Spacer(6));

            p.Add(T.StatRow("⚡", "Power Draw", PowerFormat.Watts(farm.powerDraw), farm.IsProducing ? T.AccentGreen : T.TextMuted));
            p.Add(T.StatRow("💧", "Water Use", $"{farm.waterConsumptionLps:0.00} L/s", T.AccentCyan));
            p.Add(T.StatRow("🌬", "O₂ Rate", $"{farm.oxygenPerSecond:0.00} L/s", new Color(0.35f,0.85f,0.55f)));
            p.Add(T.StatRow("⏳", "Fuel Time", farm.BiomassTimeRemaining > 0 ? $"{farm.BiomassTimeRemaining:0}s" : "Empty", T.AccentAmber));
            p.Add(T.Spacer(4));

            p.Add(GridUIHelpers.SectionTitle("Biomass Input (auto-pulled from cargo)"));
            if (farm.biomassInput != null)
                p.Add(GridUIHelpers.WeightHeader(MassUtil.ContainerMass(farm.biomassInput), "Biomass"));
            var grid = T.SlotGrid(farm.biomassInput != null ? farm.biomassInput.Size : 4);
            if (farm.biomassInput != null)
                for (int i = 0; i < farm.biomassInput.Size; i++)
                    grid.Add(slot(farm.biomassInput, i, farm.biomassInput.GetSlot(i), false, true));
            p.Add(grid);
            p.Add(T.Spacer(4));
            p.Add(T.Muted("Passive O₂: needs grid power + water tanks + biomass (wheat/corn/seeds). Slower than electrolyser but renewable and ideal for ships & cryobeds."));
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
