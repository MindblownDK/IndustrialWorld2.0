// Assets/Scripts/VoxelEngine/Maritime/MaritimeBlockUI.cs
//
//  ╔══════════════════════════════════════════════════════════════════╗
//  ║   MARITIME BLOCK UIs — industrial-themed panels for every         ║
//  ║   propulsion/power block. Uses the shared UITheme design system.  ║
//  ╚══════════════════════════════════════════════════════════════════╝
//
//  Routed from GridBlockUI.BuildPanel() — handles both right-click on a
//  placed block AND the ship master terminal.
//
//  Panels built:
//    • GridMaritimeEngine      — fuel tank / burn rate, exhaust gas, usage,
//                                torque, speed, stress, OVERSTRESSED.
//    • GridMaritimeGenerator   — power production + internal battery buffer.
//    • GridGearbox             — torque/speed in-out, gear ratio, overstress.
//    • GridBilgePump           — draining status + radius.
//    • GridPropeller           — speed, torque, thrust (terminal only).
//    • GridElectricalPropeller — speed, thrust, power usage (terminal only).
//    • GridTurbocharger        — boost pressure + turbo rotations.
//    • GridWaterwheel          — dual-mode status.
//    • GridDriveShaft          — RPM passthrough.
//    • GridExhaustPipe         — venting status.
//    • GridHelm                — throttle + steer status.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.GridSystem;
using VoxelEngine.GridSystem.UI;
using VoxelEngine.Items;
using VoxelEngine.UI;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Maritime
{
    public static class MaritimeBlockUI
    {
        /// <summary>Entry point — called by GridBlockUI.BuildPanel for maritime blocks.</summary>
        public static VisualElement BuildPanel(GridBlock block)
        {
            return block switch
            {
                GridMaritimeEngine eng      => EnginePanel(eng),
                GridMaritimeGenerator gen   => GeneratorPanel(gen),
                GridGearbox gb              => GearboxPanel(gb),
                GridBilgePump bp            => BilgePumpPanel(bp),
                GridPropeller prop          => PropellerPanel(prop),
                GridElectricalPropeller ep  => EPropellerPanel(ep),
                GridTurbocharger tc         => TurbochargerPanel(tc),
                GridWaterwheel ww           => WaterwheelPanel(ww),
                GridDriveShaft ds           => DriveShaftPanel(ds),
                GridExhaustPipe ex          => ExhaustPipePanel(ex),
                GridHelm helm               => HelmPanel(helm),
                GridHullBlock hull          => HullPanel(hull),
                _                           => null,
            };
        }

        // ════════════════════════════════════════════════════════════════
        //  ENGINE — fuel tank / burn rate, exhaust, usage, torque, stress
        // ════════════════════════════════════════════════════════════════
        private static VisualElement EnginePanel(GridMaritimeEngine eng)
        {
            var p = T.MachinePanel();

            // ── Status determination ──────────────────────────────────
            string status;
            Color statusColor;
            if (eng.IsOverstressed)      { status = "⚠ OVERSTRESSED"; statusColor = T.AccentRed; }
            else if (!eng.HasExhaust)    { status = "⚠ NO EXHAUST";  statusColor = T.AccentRed; }
            else if (eng.ExhaustFill01 >= 0.99f) { status = "⛔ CHOKED"; statusColor = T.AccentRed; }
            else if (eng.IsChoked)       { status = "⚠ BACK-PRESSURE"; statusColor = T.AccentAmber; }
            else if (eng.IsRunning)      { status = "● RUNNING";      statusColor = T.AccentGreen; }
            else                          { status = "○ IDLE";        statusColor = T.AccentDim; }

            var (hdr, _, _, _) = T.HeaderRow($"⚙ {eng.blockName}", status, statusColor);
            p.Add(hdr);

            // Accent divider colour based on tier.
            Color accent = eng.tier == EngineTier.Giant ? T.AccentGold
                         : eng.tier == EngineTier.Medium ? T.AccentOrange
                         : T.AccentAmber;
            p.Add(T.AccentDivider(accent));

            // ── Fuel display ──────────────────────────────────────────
            if (eng.fuelKind == MaritimeFuelKind.Liquid)
            {
                // Liquid engines: show a fuel tank gauge.
                string fuelName = eng.liquidFuel.DisplayName();
                Color fuelColor = eng.liquidFuel.Color();

                var gaugeRow = Row();
                gaugeRow.style.justifyContent = Justify.SpaceAround;
                gaugeRow.Add(T.TankGauge(fuelName, eng.FuelFill01, fuelColor,
                    $"{eng.FuelBuffer:0} / {eng.fuelBufferCapacity:0} L", 70, 120));
                // Exhaust gas tank.
                gaugeRow.Add(T.TankGauge("EXHAUST", eng.ExhaustFill01,
                    eng.ExhaustFill01 >= 0.8f ? T.AccentRed : new Color(0.4f, 0.35f, 0.3f),
                    $"{eng.ExhaustGas:0} / {eng.exhaustGasCapacity:0}", 70, 120));
                p.Add(gaugeRow);
            }
            else
            {
                // Small engine: solid fuel — show a burn-rate bar instead of a tank.
                p.Add(GridUIHelpers.SectionTitle("Solid Fuel Buffer"));
                var (burnBar, burnFill) = T.ProgressBar(eng.FuelFill01, T.AccentAmber, 14, true);
                p.Add(burnBar);
                p.Add(T.Muted($"Wood Logs / Planks / Coal · {eng.FuelBuffer:0}s remaining"));
            }

            p.Add(T.Spacer(6));

            // ── Exhaust gas warning ───────────────────────────────────
            if (!eng.HasExhaust)
            {
                var warn = T.StatusPill("⛔ NO EXHAUST PIPE — ENGINE CHOKED", T.AccentRed);
                p.Add(warn.pill);
                p.Add(T.Spacer(4));
            }
            else if (eng.ExhaustFill01 >= 0.8f)
            {
                var warn = T.StatusPill("⚠ EXHAUST BACKING UP — VENT FASTER!", T.AccentRed);
                p.Add(warn.pill);
                p.Add(T.Spacer(4));
            }

            // ── Stats ─────────────────────────────────────────────────
            p.Add(GridUIHelpers.SectionTitle("Performance"));

            if (eng.fuelKind == MaritimeFuelKind.Liquid)
                p.Add(T.StatRow("🛢", "Usage", $"{eng.CurrentUsage:0.##} L/s", T.AccentCyan));
            else
                p.Add(T.StatRow("🔥", "Burn Rate", $"{eng.fuelConsumptionRate:0.##} fuel/s", T.AccentCyan));

            p.Add(T.StatRow("🔄", "Torque", $"{eng.CurrentTorque:0} N·m", T.AccentGold));
            p.Add(T.StatRow("⚙", "Speed", $"{eng.CurrentRPM:0} RPM", T.AccentTeal));

            // Stress bar.
            Color stressColor = eng.IsOverstressed ? T.AccentRed
                              : eng.Stress01 > 0.7f ? T.AccentAmber
                              : T.AccentGreen;
            p.Add(T.StatRow("📈", "Stress", $"{eng.Stress01 * 100f:0}%", stressColor));
            var (stressBar, _) = T.ProgressBar(eng.Stress01, stressColor, 6, false);
            p.Add(stressBar);

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Requires an adjacent Exhaust Pipe to vent gas. " +
                          "Without one the engine chokes and produces zero torque."));

            return p;
        }

        // ════════════════════════════════════════════════════════════════
        //  GENERATOR — power production + internal battery buffer
        // ════════════════════════════════════════════════════════════════
        private static VisualElement GeneratorPanel(GridMaritimeGenerator gen)
        {
            var p = T.MachinePanel();

            string status = gen.GeneratedWatts > 1f ? "● GENERATING" : "○ IDLE";
            Color statusColor = gen.GeneratedWatts > 1f ? T.AccentGreen : T.AccentDim;

            var (hdr, _, _, _) = T.HeaderRow("🔌 Maritime Generator", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGreen));

            // ── Power production ──────────────────────────────────────
            p.Add(GridUIHelpers.SectionTitle("Power Production"));
            p.Add(T.StatRow("⚡", "Output", PowerFormat.Watts(gen.GeneratedWatts), T.AccentGreen));
            p.Add(T.StatRow("📊", "Rated Max", PowerFormat.Watts(gen.maxWattOutput), T.AccentCyan));
            p.Add(T.StatRow("⚙", "Shaft Speed", $"{gen.CurrentRPM:0} RPM", T.AccentTeal));

            // Production bar.
            float prodRatio = gen.maxWattOutput > 0f ? Mathf.Clamp01(gen.GeneratedWatts / gen.maxWattOutput) : 0f;
            var (prodBar, _) = T.ProgressBar(prodRatio, T.AccentGreen, 8, true);
            p.Add(prodBar);

            p.Add(T.Spacer(8));

            // ── Internal battery buffer ───────────────────────────────
            p.Add(GridUIHelpers.SectionTitle("Internal Battery Buffer"));
            var gaugeRow = Row();
            gaugeRow.style.justifyContent = Justify.Center;
            Color batColor = gen.BufferFill01 > 0.2f ? T.AccentGreen : T.AccentRed;
            gaugeRow.Add(T.TankGauge("BUFFER", gen.BufferFill01, batColor,
                $"{gen.BufferCharge:0} / {gen.bufferCapacityWh:0} Wh", 70, 120));
            p.Add(gaugeRow);
            p.Add(T.Muted("Smooths output — the generator charges this buffer from " +
                          "shaft power, then feeds steady electricity to the grid."));

            return p;
        }

        // ════════════════════════════════════════════════════════════════
        //  GEARBOX — torque/speed in-out, gear ratio, overstress
        // ════════════════════════════════════════════════════════════════
        private static VisualElement GearboxPanel(GridGearbox gb)
        {
            var p = T.MachinePanel();

            string status = gb.IsOverstressed ? "⚠ OVERSTRESSED" : "● OPERATIONAL";
            Color statusColor = gb.IsOverstressed ? T.AccentRed : T.AccentGreen;

            var (hdr, _, _, _) = T.HeaderRow("⚙ Gearbox", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentOrange));

            // ── Two-column: input vs output ───────────────────────────
            p.Add(GridUIHelpers.SectionTitle("Torque & Speed"));

            // Gear ratio display.
            p.Add(T.StatRow("🔩", "Gear Ratio", $"{gb.gearRatio:0.00}× ({gb.selectedGear})", T.AccentGold));
            p.Add(T.StatRow("⚡", "Max Speed", $"{gb.maxOutputSpeed:0} RPM", T.AccentCyan));

            p.Add(T.Spacer(4));

            // Input stats.
            p.Add(T.StatRow("↙", "Input Speed", $"{gb.InputRPM:0} RPM", T.AccentTeal));
            p.Add(T.StatRow("↙", "Input Torque", "~conserved", T.TextSecondary));

            // Output stats.
            p.Add(T.StatRow("↗", "Output Speed", $"{gb.OutputRPM:0} RPM", T.AccentTeal));
            float outTorque = gb.gearRatio > 0.01f ? 1f / gb.gearRatio : 0f;
            p.Add(T.StatRow("↗", "Output Torque", $"{outTorque * 100f:0}% of input", T.AccentGold));

            p.Add(T.Spacer(6));

            // Stress bar.
            Color stressColor = gb.IsOverstressed ? T.AccentRed
                              : gb.Stress01 > 0.7f ? T.AccentAmber
                              : T.AccentGreen;
            p.Add(T.StatRow("📈", "Stress", $"{gb.Stress01 * 100f:0}%", stressColor));
            var (stressBar, _) = T.ProgressBar(gb.Stress01, stressColor, 6, false);
            p.Add(stressBar);

            if (gb.IsOverstressed)
            {
                p.Add(T.Spacer(4));
                var warn = T.StatusPill("⚠ OVERSTRESSED — REDUCE GEAR RATIO!", T.AccentRed);
                p.Add(warn.pill);
            }

            // ── Gear selection ────────────────────────────────────────
            p.Add(T.Spacer(6));
            p.Add(GridUIHelpers.SectionTitle("Gear Selection"));
            var gearRow = Row();
            gearRow.style.flexWrap = Wrap.Wrap;
            float[] ratios = { 0.5f, 0.75f, 1f, 1.5f, 2.5f, 4f };
            for (int i = 0; i < ratios.Length; i++)
            {
                int gearNum = i + 1;
                bool active = gb.selectedGear == gearNum;
                var captured = gearNum;
                var btn = T.SmallButton($"G{gearNum}\n{ratios[i]}×", () =>
                {
                    gb.selectedGear = captured;
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, active ? T.AccentGold : (Color?)null);
                btn.style.width = 62;
                gearRow.Add(btn);
            }
            p.Add(gearRow);
            p.Add(T.Muted("Higher ratio = faster output but less torque. " +
                          "Use low gears for heavy props, high gears for generators."));

            return p;
        }

        // ════════════════════════════════════════════════════════════════
        //  BILGE PUMP — draining status
        // ════════════════════════════════════════════════════════════════
        private static VisualElement BilgePumpPanel(GridBilgePump bp)
        {
            var p = T.MachinePanel();

            bool hasPower = bp.Grid != null && bp.Grid.HasPower;
            string status = !hasPower ? "⚠ NO POWER" : bp.IsActive ? "● DRAINING" : "○ STANDBY";
            Color statusColor = !hasPower ? T.AccentRed : bp.IsActive ? T.AccentGreen : T.AccentDim;

            var (hdr, _, _, _) = T.HeaderRow("💧 Bilge Pump", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentBlue));

            p.Add(GridUIHelpers.SectionTitle("Draining"));
            p.Add(T.StatRow("🚿", "Drain Rate", $"{bp.drainRate:0.#} kg/s per hull", T.AccentCyan));
            p.Add(T.StatRow("📏", "Radius", $"{bp.drainRadiusCells:0} cells", T.AccentTeal));
            p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(bp.PowerDraw), T.AccentGold));
            p.Add(T.StatRow("📊", "Status", bp.IsActive ? "Actively draining waterlogged hulls" : "No waterlogged hulls in range", T.TextSecondary));

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Scans nearby hull blocks and removes absorbed water. " +
                          "Essential for untreated-wood ships in storms or after hull breaches."));

            return p;
        }

        // ════════════════════════════════════════════════════════════════
        //  PROPELLER — speed, torque, thrust
        // ════════════════════════════════════════════════════════════════
        private static VisualElement PropellerPanel(GridPropeller prop)
        {
            var p = T.MachinePanel();

            bool spinning = prop.CurrentRPM > 1f;
            string status = spinning ? "● SPINNING" : "○ STOPPED";
            Color statusColor = spinning ? T.AccentGreen : T.AccentDim;

            var (hdr, _, _, _) = T.HeaderRow($"🌀 {prop.blockName}", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            p.Add(GridUIHelpers.SectionTitle("Propulsion"));
            p.Add(T.StatRow("⚙", "Speed", $"{prop.CurrentRPM:0} RPM", T.AccentTeal));
            p.Add(T.StatRow("🌊", "Submergence", $"{prop.Submergence * 100f:0}%", T.AccentBlue));
            p.Add(T.StatRow("🚀", "Thrust", PowerFormat.Newtons(prop.CurrentThrustN), T.AccentGreen));
            p.Add(T.StatRow("📐", "Size", $"{prop.propellerSize:0}×", T.AccentCyan));

            // Submergence bar.
            var (subBar, _) = T.ProgressBar(prop.Submergence, T.AccentBlue, 6, false);
            p.Add(subBar);

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Thrust = RPM × Submergence × Size. " +
                          "Must be below the waterline to generate thrust."));

            return p;
        }

        // ════════════════════════════════════════════════════════════════
        //  ELECTRICAL PROPELLER — speed, thrust, power usage
        // ════════════════════════════════════════════════════════════════
        private static VisualElement EPropellerPanel(GridElectricalPropeller ep)
        {
            var p = T.MachinePanel();

            bool spinning = ep.CurrentRPM > 1f;
            string status = spinning ? "● SPINNING" : "○ STOPPED";
            Color statusColor = spinning ? T.AccentGreen : T.AccentDim;

            var (hdr, _, _, _) = T.HeaderRow("⚡ Electrical Propeller", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentPurple));

            p.Add(GridUIHelpers.SectionTitle("Propulsion"));
            p.Add(T.StatRow("⚙", "Speed", $"{ep.CurrentRPM:0} RPM", T.AccentTeal));
            p.Add(T.StatRow("🚀", "Thrust", PowerFormat.Newtons(ep.CurrentThrustN), T.AccentGreen));
            p.Add(T.StatRow("📐", "Size", $"{ep.propellerSize:0}×", T.AccentCyan));

            p.Add(T.Spacer(4));
            p.Add(GridUIHelpers.SectionTitle("Power"));
            p.Add(T.StatRow("⚡", "Power Usage", PowerFormat.Watts(ep.PowerDraw), T.AccentGold));
            p.Add(T.StatRow("📊", "Rated Max", PowerFormat.Watts(ep.powerDrawWatts), T.AccentAmber));

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Driven by grid electricity — fast spin-up, no shaft needed. " +
                          "Must be below the waterline to generate thrust."));

            return p;
        }

        // ════════════════════════════════════════════════════════════════
        //  TURBOCHARGER — boost pressure + turbo rotations
        // ════════════════════════════════════════════════════════════════
        private static VisualElement TurbochargerPanel(GridTurbocharger tc)
        {
            var p = T.MachinePanel();

            string status = tc.IsConnected ? "● BOOSTING" : "○ DISCONNECTED";
            Color statusColor = tc.IsConnected ? T.AccentGreen : T.AccentRed;

            var (hdr, _, _, _) = T.HeaderRow("🌀 Turbocharger", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGold));

            p.Add(GridUIHelpers.SectionTitle("Boost"));
            p.Add(T.StatRow("📊", "Boost Pressure", $"{tc.BoostPressure:0.##} bar", T.AccentGold));
            p.Add(T.StatRow("🔄", "Turbo Rotations", $"{tc.TurboRPM:0} RPM", T.AccentTeal));
            p.Add(T.StatRow("⚡", "Boost Multiplier", $"{tc.boostMultiplier:0.00}× torque", T.AccentGreen));

            // Pressure bar (0..4 bar range).
            float pressureRatio = Mathf.Clamp01(tc.BoostPressure / 4f);
            var (presBar, _) = T.ProgressBar(pressureRatio, T.AccentGold, 8, false);
            p.Add(presBar);

            p.Add(T.Spacer(6));
            if (!tc.IsConnected)
                p.Add(T.Muted("Place directly next to a Giant Diesel Engine to boost its torque by 40%."));
            else
                p.Add(T.Muted("Connected to a Giant Diesel. The red core glows under load."));

            return p;
        }

        // ════════════════════════════════════════════════════════════════
        //  WATERWHEEL — dual-mode status
        // ════════════════════════════════════════════════════════════════
        private static VisualElement WaterwheelPanel(GridWaterwheel ww)
        {
            var p = T.MachinePanel();

            bool spinning = ww.CurrentRPM > 1f;
            string status = spinning ? "● SPINNING" : "○ STILL";
            Color statusColor = spinning ? T.AccentGreen : T.AccentDim;

            var (hdr, _, _, _) = T.HeaderRow("🌊 Waterwheel", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentTeal));

            p.Add(GridUIHelpers.SectionTitle("Mechanical"));
            p.Add(T.StatRow("⚙", "Speed", $"{ww.CurrentRPM:0} RPM", T.AccentTeal));
            p.Add(T.StatRow("🌊", "Submergence", $"{ww.Submergence * 100f:0}%", T.AccentBlue));
            p.Add(T.StatRow("📐", "Wheel Size", $"{ww.wheelSize:0}×", T.AccentCyan));

            p.Add(T.Spacer(6));
            p.Add(T.Muted("DUAL-MODE: Generates torque from water flow when stationary. " +
                          "Produces paddle thrust when driven by a shaft on a moving ship."));

            return p;
        }

        // ════════════════════════════════════════════════════════════════
        //  DRIVE SHAFT — RPM passthrough
        // ════════════════════════════════════════════════════════════════
        private static VisualElement DriveShaftPanel(GridDriveShaft ds)
        {
            var p = T.MachinePanel();

            bool spinning = ds.CurrentRPM > 1f;
            string status = spinning ? "● ROTATING" : "○ STATIC";
            Color statusColor = spinning ? T.AccentGreen : T.AccentDim;

            var (hdr, _, _, _) = T.HeaderRow("🔗 Drive Shaft", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            p.Add(T.StatRow("⚙", "Speed", $"{ds.CurrentRPM:0} RPM", T.AccentTeal));
            p.Add(T.StatRow("⚡", "Max Safe RPM", $"{ds.maxSafeRPM:0} RPM", T.AccentAmber));

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Transmits torque from an engine to propellers, gearboxes, or generators. " +
                          "If disabled or destroyed, the propulsion chain stops downstream."));

            return p;
        }

        // ════════════════════════════════════════════════════════════════
        //  EXHAUST PIPE — venting status
        // ════════════════════════════════════════════════════════════════
        private static VisualElement ExhaustPipePanel(GridExhaustPipe ex)
        {
            var p = T.MachinePanel();

            string status = ex.IsVenting ? "● VENTING" : "○ IDLE";
            Color statusColor = ex.IsVenting ? T.AccentAmber : T.AccentDim;

            var (hdr, _, _, _) = T.HeaderRow("💨 Exhaust Pipe", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentAmber));

            p.Add(T.StatRow("🌫", "Smoke Rate", $"{ex.smokeRate:0}/s", T.AccentAmber));
            p.Add(T.StatRow("💨", "Status", ex.IsVenting ? "Venting gas from adjacent engine(s)" : "No active engines adjacent", T.TextSecondary));

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Every engine requires at least one adjacent exhaust pipe. " +
                          "Without one, exhaust gas backs up and the engine chokes. " +
                          "Emits visible smoke while venting — black for Giant Diesel, grey for small engines."));

            return p;
        }

        // ════════════════════════════════════════════════════════════════
        //  HELM — throttle + steer status
        // ════════════════════════════════════════════════════════════════
        private static VisualElement HelmPanel(GridHelm helm)
        {
            var p = T.MachinePanel();

            string status = helm.IsActive ? "● MANNED" : "○ UNMANNED";
            Color statusColor = helm.IsActive ? T.AccentGreen : T.AccentDim;

            var (hdr, _, _, _) = T.HeaderRow("🧭 Helm", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGold));

            if (helm.IsActive)
            {
                var maritime = helm.Grid?.Maritime;
                if (maritime != null)
                {
                    p.Add(GridUIHelpers.SectionTitle("Ship Controls"));
                    p.Add(T.StatRow("🚢", "Throttle", $"{maritime.Throttle * 100f:0}%", T.AccentGreen));
                    p.Add(T.StatRow("🧭", "Steer", $"{maritime.Steer:+0.00;-0.00;0}", T.AccentCyan));

                    // Throttle bar.
                    var (throttleBar, _) = T.ProgressBar(maritime.Throttle, T.AccentGreen, 8, true);
                    p.Add(throttleBar);
                }
            }

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Walk up and press E to take the helm. " +
                          "W = throttle up, S = throttle down, A/D = steer left/right."));

            return p;
        }

        // ════════════════════════════════════════════════════════════════
        //  HULL MATERIAL — buoyancy + waterlogging status
        // ════════════════════════════════════════════════════════════════
        private static VisualElement HullPanel(GridHullBlock hull)
        {
            var p = T.MachinePanel();

            string status = hull.WaterloggedMass > 0.1f ? "⚠ WATERLOGGED" : "● DRY";
            Color statusColor = hull.WaterloggedMass > 0.1f ? T.AccentAmber : T.AccentGreen;

            var (hdr, _, _, _) = T.HeaderRow($"🧱 {hull.blockName}", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentTeal));

            p.Add(GridUIHelpers.SectionTitle("Material"));
            p.Add(T.StatRow("🌊", "Buoyancy", $"{hull.buoyancyFactor * 100f:0}%", T.AccentBlue));
            p.Add(T.StatRow("💧", "Waterproof", hull.waterproof ? "Yes" : "No — absorbs water",
                hull.waterproof ? T.AccentGreen : T.AccentAmber));
            p.Add(T.StatRow("⚖", "Base Mass", MassFormat.Format(hull.BlockMass), T.AccentCyan));
            p.Add(T.StatRow("❤", "Integrity", $"{hull.currentHP:0} / {hull.maxHP:0}", T.AccentGreen));

            if (hull.maxWaterlogging > 0f)
            {
                p.Add(T.Spacer(4));
                p.Add(GridUIHelpers.SectionTitle("Waterlogging"));
                p.Add(T.StatRow("💧", "Absorbed Water", $"{hull.WaterloggedMass:0.#} / {hull.maxWaterlogging:0} kg",
                    hull.WaterlogFill01 > 0.5f ? T.AccentRed : T.AccentAmber));
                var (logBar, _) = T.ProgressBar(hull.WaterlogFill01, T.AccentBlue, 8, true);
                p.Add(logBar);
                p.Add(T.Muted("Soaks up water while submerged, increasing mass. " +
                              "Use a Bilge Pump to drain."));
            }

            return p;
        }

        // ── Helper ─────────────────────────────────────────────────────
        private static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.alignItems = Align.Center;
            return r;
        }
    }
}
