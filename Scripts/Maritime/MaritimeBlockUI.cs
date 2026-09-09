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
//    • GridMaritimeEngine      — fuel ETA at current burn rate, fuel tank /
//                                hopper, exhaust gas, heat (knocking/critical),
//                                upgrade module sockets, torque/speed/stress.
//    • GridMaritimeGenerator   — power + shaft-speed bonus, heat/coolant,
//                                upgrade module sockets, internal buffer.
//    • GridGearbox             — torque/speed in-out, 20-speed live gear
//                                selection, bidirectional flow, overstress.
//    • GridBilgePump           — draining status + radius.
//    • GridPropeller           — speed, torque, thrust (terminal only).
//    • GridElectricalPropeller — speed, thrust, power usage (terminal only).
//    • GridTurbocharger        — boost pressure + turbo rotations.
//    • GridWaterwheel          — dual-mode status.
//    • GridDriveShaft          — RPM passthrough.
//    • GridShaftHousing        — sealed shaft pass-through + RPM.
//    • GridExhaustPipe         — venting status.
//    • GridHelm                — throttle + steer status.

using System.Collections.Generic;
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
        /// <summary>True while a maritime numeric field owns keyboard input.</summary>
        public static bool IsNumericInputFocused { get; private set; }

        /// <summary>
        /// Hull-side heat of a working machine (9.31.0): what the block does to the
        /// plates around it, as opposed to its internal coolant temperature above.
        /// </summary>
        private static void AddHullHeatRows(VisualElement p, GridBlock block)
        {
            if (block is not VoxelEngine.Thermal.IHeatSourceBlock source || block.Grid == null) return;
            var thermal = block.Grid.GetComponent<VoxelEngine.Thermal.GridThermalSystem>();
            float surface = thermal != null ? thermal.TemperatureOf(block) : VoxelEngine.Thermal.ThermalRules.FallbackAmbientC;
            float tolerance = VoxelEngine.Thermal.ThermalRules.ToleranceC(block);
            var band = VoxelEngine.Thermal.ThermalRules.Band(surface, tolerance);
            p.Add(T.StatRow("♨", "Casing Surface",
                $"{surface:0}°C · {VoxelEngine.Thermal.ThermalRules.BandLabel(band)} · tolerance {tolerance:0}°C",
                VoxelEngine.Thermal.ThermalRules.BandColor(band)));
            if (source.SelfHeatC > 1f)
                p.Add(T.StatRow("🔥", "Heat Output",
                    $"+{source.SelfHeatC:0}°C self · +{source.NeighbourHeatC:0}°C into neighbours", T.AccentAmber));
        }

        /// <summary>
        /// What the compartment around this machine is doing to it, and what the machine
        /// is doing back (roadmap 5.1 item 14). Nothing is printed for a block under an
        /// open sky: a machine that can shed its heat into the planet has no engine room
        /// problem, and the panel should not pretend otherwise.
        /// </summary>
        private static void AddEngineRoomRows(VisualElement p, GridMaritimeEngine eng)
        {
            if (p == null || eng == null) return;
            var room = VoxelEngine.Pressure.GridPressureSystem.ConcealedRoom(eng);
            if (room == null) return;

            p.Add(T.Spacer(4));
            p.Add(GridUIHelpers.SectionTitle("Engine Room"));

            var band = room.Band;
            p.Add(T.StatRow("🏠", "Compartment Air",
                $"room air {room.AirTemperatureC:0}°C  ·  {VoxelEngine.Thermal.ThermalRules.RoomBandLabel(band)}",
                VoxelEngine.Thermal.ThermalRules.BandColor(band)));
            p.Add(T.StatRow("🫁", "Combustion Air", eng.AirSourceLabel,
                eng.OxygenStarved ? T.AccentRed : eng.DrawsRoomAir ? T.AccentAmber : T.AccentCyan));

            if (!eng.AirIndependent)
            {
                float perSecond = eng.RoomOxygenDemandPerSecond * GridMaritimeEngine.ThermalRoomOxygenDebit;
                string burn = perSecond > 0.001f
                    ? $"{perSecond:0.0} L/s of room air"
                    : "not drinking the room";
                p.Add(T.StatRow("O₂", "Room Draw", burn,
                    eng.DrawsRoomAir ? T.AccentAmber : T.TextSecondary));
            }

            if (eng.ConcealedVent01 > 0.02f)
                p.Add(T.StatRow("💨", "Trapped Exhaust",
                    $"{eng.ConcealedVent01 * 100f:0}% of the stack's gas stays in this volume  ·  back-pressure −{eng.ConcealedVent01 * 25f:0}%",
                    T.AccentRed));

            if (room.IsOverheating)
                p.Add(T.Muted("This volume is cooking. The space destroys what is inside it: clear it with an Exhaust Scrubber, open a hatch, or shut the engine down before the plates around it fail."));
            else if (eng.DrawsRoomAir)
                p.Add(T.Muted("Feeding on the room's own air. It works — until the room does not. Pipe oxygen in, fit a Closed-Cycle AIP module, or ventilate."));
        }

        /// <summary>Compartment readout for machinery with no combustion air of its own.</summary>
        private static void AddEngineRoomRows(VisualElement p, GridBlock block)
        {
            if (p == null || block == null) return;
            var room = VoxelEngine.Pressure.GridPressureSystem.ConcealedRoom(block);
            if (room == null) return;

            p.Add(T.Spacer(4));
            p.Add(GridUIHelpers.SectionTitle("Engine Room"));
            var band = room.Band;
            p.Add(T.StatRow("🏠", "Compartment Air",
                $"room air {room.AirTemperatureC:0}°C  ·  {VoxelEngine.Thermal.ThermalRules.RoomBandLabel(band)}",
                VoxelEngine.Thermal.ThermalRules.BandColor(band)));
            if (room.ExhaustLoad01 > 0.02f)
                p.Add(T.StatRow("💨", "Trapped Exhaust", $"{room.ExhaustLoad01 * 100f:0}%",
                    room.ExhaustLoad01 > 0.5f ? T.AccentRed : T.AccentAmber));
            if (room.IsOverheating)
                p.Add(T.Muted("The compartment cannot clear its own heat. Vent it, or the room will destroy what is inside it."));
        }

        /// <summary>Entry point — called by GridBlockUI.BuildPanel for maritime blocks.</summary>
        public static VisualElement BuildPanel(GridBlock block, MachineUIs.SlotBuilder slot = null)
        {
            return block switch
            {
                GridMaritimeEngine eng      => MakeScrollable(EnginePanel(eng, slot)),
                GridMaritimeGenerator gen   => MakeScrollable(GeneratorPanel(gen, slot)),
                GridGearbox gb              => GearboxPanel(gb),
                GridBilgePump bp            => BilgePumpPanel(bp),
                GridMarineWaterPump mwp     => MarineWaterPumpPanel(mwp),
                GridPropeller prop          => PropellerPanel(prop),
                GridElectricalPropeller ep  => EPropellerPanel(ep),
                GridTurbocharger tc         => TurbochargerPanel(tc),
                GridWaterwheel ww           => WaterwheelPanel(ww),
                GridDriveShaft ds           => DriveShaftPanel(ds),
                GridShaftHousing housing    => ShaftHousingPanel(housing),
                GridExhaustPipe ex          => ExhaustPipePanel(ex),
                GridHelm helm               => MakeScrollable(HelmPanel(helm)),
                GridHullBlock hull          => HullPanel(hull),
                _                           => null,
            };
        }

        /// <summary>
        /// Wraps a maritime machine panel's content in a vertical ScrollView so tall
        /// panels (engine/generator readouts, many rows) never clip their slots.
        /// </summary>
        private static VisualElement MakeScrollable(VisualElement panel)
        {
            if (panel == null) return panel;
            var children = new List<VisualElement>();
            foreach (var child in panel.Children()) children.Add(child);
            foreach (var child in children) child.RemoveFromHierarchy();

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            // Named so the panel rebuild can carry the scroll offset back over.
            scroll.name = "PanelScroll_" + (panel != null ? panel.name : "Machine");
            scroll.style.flexGrow = 1;
            scroll.style.marginTop = 2;
            T.StyleScroller(scroll);
            foreach (var child in children) scroll.Add(child);
            panel.Add(scroll);
            return panel;
        }

        // ════════════════════════════════════════════════════════════════
        //  ENGINE — fuel tank / burn rate, exhaust, usage, torque, stress
        // ════════════════════════════════════════════════════════════════
        private static VisualElement EnginePanel(GridMaritimeEngine eng, MachineUIs.SlotBuilder slot)
        {
            var p = T.MachinePanel();

            // ── Status determination ──────────────────────────────────
            string status;
            Color statusColor;
            if (eng.CriticalFailure)            { status = "⛔ CRITICAL HEAT"; statusColor = T.AccentRed; }
            else if (eng.IsOverstressShutdown)  { status = "⛔ OVERSTRESSED — STOPPED"; statusColor = T.AccentRed; }
            else if (eng.IsOverheating)         { status = "⚠ OVERHEATING"; statusColor = T.AccentAmber; }
            else if (eng.IsOverstressed)        { status = "⚠ HIGH MECHANICAL LOAD"; statusColor = T.AccentAmber; }
            else if (!eng.HasExhaust)    { status = "⚠ NO EXHAUST";  statusColor = T.AccentRed; }
            else if (eng.OxygenStarved)  { status = "⚠ NO OXYGEN";  statusColor = T.AccentRed; }
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
                // Coolant tank (only for HFO + MGO engines).
                if (eng.tier != EngineTier.Small)
                {
                    Color coolantColor = eng.UsingPremiumCoolant ? new Color(0.20f, 0.85f, 0.75f) : new Color(0.25f, 0.55f, 0.95f);
                    gaugeRow.Add(T.TankGauge("COOLANT", eng.CoolantFill01, coolantColor,
                        $"{eng.CoolantBuffer:0} / {eng.coolantCapacity:0} L", 70, 120));
                }
                p.Add(gaugeRow);
                // Fuel ETA at the CURRENT burn rate — throttles correctly.
                p.Add(T.Muted($"≈ {GridMaritimeEngine.FormatDuration(eng.EstimatedFuelSecondsRemaining)} of fuel at current burn rate"));
            }
            else
            {
                // Crude engine: solid fuel hopper + burn-time buffer.
                p.Add(GridUIHelpers.SectionTitle("Solid Fuel Buffer"));
                var (burnBar, _) = T.ProgressBar(eng.FuelFill01, T.AccentAmber, 14, true);
                p.Add(burnBar);
                p.Add(T.Muted($"Wood Logs / Planks / Coal · ≈ {GridMaritimeEngine.FormatDuration(eng.EstimatedFuelSecondsRemaining)} at current burn rate"));

                if (eng.SolidFuelInput != null && slot != null)
                {
                    p.Add(T.Spacer(6));
                    p.Add(GridUIHelpers.SectionTitle("Fuel Hopper"));
                    p.Add(GridUIHelpers.WeightHeader(MassUtil.ContainerMass(eng.SolidFuelInput), "Fuel"));
                    var hopper = T.SlotGrid(4);
                    for (int i = 0; i < eng.SolidFuelInput.Size; i++)
                        hopper.Add(slot(eng.SolidFuelInput, i, eng.SolidFuelInput.GetSlot(i), false, true));
                    p.Add(hopper);
                    p.Add(T.Muted("Insert solid fuel directly here, or keep matching fuel in connected cargo as backup."));
                }
            }

            // ── Combustion air: one intake, three possible sources ────
            p.Add(GridUIHelpers.SectionTitle("Combustion Air"));
            if (eng.AirIndependent)
            {
                p.Add(T.Muted("CLOSED-CYCLE AIP active — the oxygen loop is closed, no external air is burned and no atmosphere is required."));
            }
            else
            {
                Color o2Color = eng.OxygenFill01 > 0.25f ? T.AccentCyan : T.AccentRed;
                var (o2Bar, _) = T.ProgressBar(eng.OxygenFill01, o2Color, 8, true);
                p.Add(o2Bar);

                string intake = eng.StarvedOnPipedLine ? "PIPED LINE EMPTY — STRICT" : eng.AirSource switch
                {
                    VoxelEngine.Thermal.AirSource.PipedOxygen => "PIPED O₂ — no penalty, the line owns the intake",
                    VoxelEngine.Thermal.AirSource.RoomAir     => "ROOM AIR — 10% down on torque, and it drinks the compartment",
                    VoxelEngine.Thermal.AirSource.Atmosphere  => (1f - eng.AirQuality01) < 0.005f
                        ? "OPEN INTAKE — sea level air, no penalty"
                        : $"OPEN INTAKE — {(1f - eng.AirQuality01) * 100f:0}% down, thin air out here",
                    VoxelEngine.Thermal.AirSource.ClosedCycle  => "CLOSED LOOP",
                    _                                         => "NO AIR — the engine will stop",
                };
                Color intakeColor = eng.AirSource switch
                {
                    VoxelEngine.Thermal.AirSource.PipedOxygen  => T.AccentGreen,
                    VoxelEngine.Thermal.AirSource.RoomAir      => T.AccentAmber,
                    VoxelEngine.Thermal.AirSource.Atmosphere   => T.AccentCyan,
                    VoxelEngine.Thermal.AirSource.ClosedCycle  => T.AccentDim,
                    _                                          => T.AccentRed,
                };
                p.Add(T.StatRow("🌬", "Intake", intake, eng.StarvedOnPipedLine ? T.AccentRed : intakeColor));

                // Where the engine's own exhaust goes. A gas line clamped to the exhaust
                // output flange pumps it into the network (tanks, a scrubber, a vent); with
                // nothing there it simply vents overboard off the flange.
                p.Add(T.StatRow("💨", "Exhaust out",
                    eng.HasGasLineAtExhaustPort() ? "GAS LINE — pumped to the network" : "OVERBOARD — free vent",
                    eng.HasGasLineAtExhaustPort() ? T.AccentCyan : T.AccentDim));

                // ── Starved-line policy: hold to the pipe, or fall back to free air ──
                var fbRow = Row();
                fbRow.Add(T.SmallButton("FALLBACK ON", () =>
                {
                    eng.allowAirFallbackOnStarvedLine = true;
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, eng.allowAirFallbackOnStarvedLine ? T.AccentCyan : T.BgSlot));
                fbRow.Add(T.SmallButton("STRICT", () =>
                {
                    eng.allowAirFallbackOnStarvedLine = false;
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, !eng.allowAirFallbackOnStarvedLine ? T.AccentRed : T.BgSlot));
                fbRow.Add(T.Muted(eng.allowAirFallbackOnStarvedLine
                    ? "empty line → room/planet air"
                    : "empty line → engine stops"));
                p.Add(fbRow);
                p.Add(T.Muted(eng.allowAirFallbackOnStarvedLine
                    ? "When the piped oxygen runs out this engine drops back to whatever air it can reach and keeps "
                        + "running at that source's cost. Convenient on a ship whose supply is not yet trustworthy; "
                        + "the room still pays for it. Switch to STRICT if you want the engine to refuse any other "
                        + "intake than the one you plumbed."
                    : "STRICT: a plumbed line owns the intake absolutely. If it runs empty the engine stops with "
                        + "O2 LINE EMPTY instead of quietly eating the compartment's air — which is the behaviour a "
                        + "sealed engine room wants, because room air there is the crew's breath."));

                p.Add(T.Muted(eng.AirSource switch
                {
                    VoxelEngine.Thermal.AirSource.PipedOxygen =>
                        "A gas line is plumbed to Port_OxygenInput, so the engine breathes that line and nothing else — "
                        + "neither the compartment nor the sky. It is the only arrangement that runs at full torque, and "
                        + "it keeps a sealed engine room from being eaten alive. What happens when the line runs dry is "
                        + "your call: the policy row below either falls back to free air or stops the engine.",
                    VoxelEngine.Thermal.AirSource.RoomAir =>
                        "The bay is sealed and nothing is piped in, so the engine is burning the compartment's own air. "
                        + "It works at reduced torque until the room runs out. Pipe oxygen into Port_OxygenInput, or fit a "
                        + "Closed-Cycle AIP module, and the room stops being the fuel tank.",
                    VoxelEngine.Thermal.AirSource.Atmosphere =>
                        "The intake side of the block faces open sky, so the engine breathes the planet directly. Leave a hole "
                        + "in the wall there for the air to reach it — weld the bay shut and the intake closes. Raw atmosphere "
                        + "burns less cleanly than piped oxygen and the thinner the world the worse it gets; at sea-level "
                        + "pressure on a breathing planet there is no penalty worth naming.",
                    VoxelEngine.Thermal.AirSource.ClosedCycle =>
                        "Nothing is being drawn from around the block.",
                    _ =>
                        "No air: the engine has no intake at all. It needs a pipe on Port_OxygenInput, an open cell on its "
                        + "intake side on a planet with breath in it, or a compartment with oxygen left to burn.",
                }));

                if (eng.OxygenStarved)
                    p.Add(T.Muted(eng.DrawsAtmosphereAir
                        ? "INTAKE BLOCKED — the cell in front of the intake is no longer open. The run continues on the buffer, then stops."
                        : "NO COMBUSTION AIR — nothing to burn. The run continues on the buffer, then stops."));
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
                var warn = T.StatusPill("⚠ EXHAUST BACKING UP — VENT OR DUMP IT", T.AccentRed);
                p.Add(warn.pill);
                p.Add(T.Spacer(4));
            }

            if (eng.OxygenStarved && eng.RequiresExternalOxygen)
            {
                var o2Warn = T.StatusPill("⚠ OXYGEN STARVED — ENGINE STOPS", T.AccentRed);
                p.Add(o2Warn.pill);
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
            p.Add(T.StatRow("⛓", "Mechanical Load", $"{eng.MechanicalLoadRatio * 100f:0}%", eng.MechanicalLoadRatio > 1f ? T.AccentRed : T.AccentTeal));
            p.Add(T.Muted("Torque is the twisting force currently delivered (N·m). Mechanical Load is the downstream demand as a percentage of available torque. Generator banks, propellers, and gearbox ratios feed that demand back into stress and heat."));

            // Stress bar.
            Color stressColor = eng.IsOverstressed ? T.AccentRed
                              : eng.Stress01 > 0.7f ? T.AccentAmber
                              : T.AccentGreen;
            p.Add(T.StatRow("📈", "Stress", $"{eng.Stress01 * 100f:0}%", stressColor));
            var (stressBar, _) = T.ProgressBar(eng.Stress01, stressColor, 6, false);
            p.Add(stressBar);
            if (eng.IsOverstressShutdown)
                p.Add(T.Muted("OVERSTRESSED: engine protection tripped. Remove or disable load to reset immediately; otherwise it makes a guarded retry shortly after the trip."));

            // ── Heat ──────────────────────────────────────────────────
            p.Add(T.Spacer(6));
            p.Add(GridUIHelpers.SectionTitle("Thermal"));
            Color heatColor = eng.CriticalFailure ? T.AccentRed
                            : eng.TemperatureC >= GridMaritimeEngine.KnockingTemperatureC ? T.AccentAmber
                            : T.AccentGreen;
            p.Add(T.StatRow("🌡", "Temperature",
                $"{eng.TemperatureC:0}°C  ·  knocking ≥ {GridMaritimeEngine.KnockingTemperatureC:0}°  ·  critical ≥ {GridMaritimeEngine.CriticalTemperatureC:0}°",
                heatColor));
            var (heatBar, _) = T.ProgressBar(eng.Heat01, heatColor, 6, false);
            p.Add(heatBar);
            p.Add(T.Muted(eng.HasThermalPerformanceUpgrade
                ? "Performance hardware installed — high mechanical load can push this engine past the stock thermal envelope."
                : "Stock thermal governor active — without performance hardware this engine is capped at 89°C."));
            AddHullHeatRows(p, eng);
            AddEngineRoomRows(p, eng);

            if (eng.CriticalFailure)
            {
                p.Add(T.Spacer(4));
                var crit = T.StatusPill("⛔ CRITICAL HEAT — ENGINE SEIZED · REPAIR REQUIRED", T.AccentRed);
                p.Add(crit.pill);
            }
            else if (eng.IsOverheating)
            {
                p.Add(T.Spacer(4));
                var knock = T.StatusPill("⚠ KNOCKING — FUEL EFFICIENCY −25%", T.AccentAmber);
                p.Add(knock.pill);
            }

            // ── Seized engine → spare-parts repair ─────────────────────
            if (eng.NeedsRepair)
            {
                p.Add(T.Spacer(6));
                p.Add(GridUIHelpers.SectionTitle("Emergency Repair"));

                var inv = VoxelEngine.UI.GameUIController.Instance != null
                    ? VoxelEngine.UI.GameUIController.Instance.inventory
                    : null;

                if (eng.TemperatureC > GridMaritimeEngine.RecoverTemperatureC)
                    p.Add(T.Muted($"Too hot to work on — wait until the block cools below " +
                                  $"{GridMaritimeEngine.RecoverTemperatureC:0}°C (now {eng.TemperatureC:0}°C)."));

                bool afford = eng.CanAffordRepair(inv);
                foreach (var part in eng.RepairCost)
                {
                    if (part.item == null) continue;
                    int have = 0;
                    if (inv != null && inv.container != null)
                        for (int i = 0; i < inv.container.Size; i++)
                        {
                            var stack = inv.container.GetSlot(i);
                            if (stack != null && !stack.IsEmpty && stack.item == part.item) have += stack.count;
                        }
                    p.Add(T.StatRow("🔧", part.item.displayName,
                        $"{have} / {part.count}",
                        have >= part.count ? T.AccentGreen : T.AccentRed));
                }

                bool repairable = eng.TemperatureC <= GridMaritimeEngine.RecoverTemperatureC && afford;
                var repairBtn = T.ActionButton("🔧  REPAIR ENGINE", () =>
                {
                    if (eng.TryRepairCriticalFailure(inv))
                    {
                        VoxelEngine.UI.BuildFeedbackHud.Show(
                            "Engine repaired", $"{eng.blockName} restored to working order",
                            null, T.AccentGreen);
                    }
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, repairable ? T.AccentGreen : (Color?)null);
                repairBtn.SetEnabled(repairable);
                p.Add(repairBtn);
                if (!afford)
                    p.Add(T.Muted("Collect the spare parts above — a subset of this engine's " +
                                  "crafting recipe — then press repair."));
            }

            // ── Upgrade modules ───────────────────────────────────────
            var moduleSlots = eng.GetModuleSlots();
            if (moduleSlots != null && slot != null)
            {
                p.Add(T.Spacer(6));
                p.Add(GridUIHelpers.SectionTitle($"Upgrade Modules ({eng.MaxModuleSlots} slots)"));
                p.Add(GridUIHelpers.WeightHeader(MassUtil.ContainerMass(moduleSlots), "Modules"));
                var moduleGrid = T.SlotGrid(eng.MaxModuleSlots);
                for (int i = 0; i < moduleSlots.Size; i++)
                    moduleGrid.Add(slot(moduleSlots, i, moduleSlots.GetSlot(i), false, true));
                p.Add(moduleGrid);

                bool anyModule = eng.TurboModuleCount + eng.EfficiencyChipCount
                               + eng.InjectorModuleCount + eng.RadiatorModuleCount > 0;
                if (anyModule)
                {
                    p.Add(T.StatRow("🧩", "Module Bonus",
                        $"Output {eng.ModuleOutputMultiplier:0.##}× · Speed cap {eng.ModuleSpeedCapMultiplier:0.##}× · Fuel use {eng.ModuleFuelUseMultiplier:0.##}×",
                        T.AccentPurple));
                }
                else
                {
                    p.Add(T.Muted("Socket upgrade modules: High-Flow Turbocharger, Efficiency Tuning Chip, " +
                                  "Overclocked Fuel Injectors, Super-Cooler Radiator Jacket."));
                }

                if (eng.RadiatorModuleCount > 0)
                {
                    Color radColor = eng.RadiatorCoolingActive ? T.AccentCyan : T.AccentRed;
                    p.Add(T.StatRow("💧", "Radiator Water", eng.RadiatorCoolingActive ? "FLOWING" : "DRY — DRAWING", radColor));
                    var (radBar, _) = T.ProgressBar(eng.RadiatorWaterFill01, radColor, 5, false);
                    p.Add(radBar);
                }
            }

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Requires an adjacent Exhaust Pipe to vent gas. " +
                          "Without one the engine chokes and produces zero torque."));

            return p;
        }

        // ════════════════════════════════════════════════════════════════
        //  GENERATOR — power production + internal battery buffer
        // ════════════════════════════════════════════════════════════════
        private static VisualElement GeneratorPanel(GridMaritimeGenerator gen, MachineUIs.SlotBuilder slot)
        {
            var p = T.MachinePanel();

            string status;
            Color statusColor;
            if (gen.CriticalFailure)         { status = "⛔ CRITICAL HEAT"; statusColor = T.AccentRed; }
            else if (gen.GeneratedWatts > 1f) { status = "● GENERATING";   statusColor = T.AccentGreen; }
            else                              { status = "○ IDLE";         statusColor = T.AccentDim; }

            var (hdr, _, _, _) = T.HeaderRow("🔌 Maritime Generator", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentGreen));

            // ── Power production ──────────────────────────────────────
            p.Add(GridUIHelpers.SectionTitle("Power Production"));
            p.Add(T.StatRow("⚡", "Output", PowerFormat.Watts(gen.GeneratedWatts), T.AccentGreen));

            string rated = PowerFormat.Watts(gen.EffectiveMaxWattOutput);
            if (gen.ModuleOutputMultiplier > 1.001f)
                rated += $"  ({PowerFormat.Watts(gen.maxWattOutput)} × {gen.ModuleOutputMultiplier:0.##} modules)";
            p.Add(T.StatRow("📊", "Rated Max", rated, T.AccentCyan));
            p.Add(T.StatRow("⚙", "Shaft Speed", $"{gen.CurrentRPM:0} RPM", T.AccentTeal));

            // Speed bonus: the faster the shaft spins (relative to rated speed),
            // the more power the generator squeezes out — up to +50%.
            p.Add(T.StatRow("🚀", "Speed Bonus",
                $"×{gen.CurrentSpeedBonusMultiplier:0.00} output (max ×{1f + gen.maxSpeedBonus:0.00})",
                gen.CurrentSpeedBonusMultiplier > 1.01f ? T.AccentGold : T.TextSecondary));

            // Production bar.
            float prodRatio = gen.EffectiveMaxWattOutput > 0f ? Mathf.Clamp01(gen.GeneratedWatts / gen.EffectiveMaxWattOutput) : 0f;
            var (prodBar, _) = T.ProgressBar(prodRatio, T.AccentGreen, 8, true);
            p.Add(prodBar);

            p.Add(T.Spacer(8));

            // ── Temperature + coolant ─────────────────────────────────
            p.Add(GridUIHelpers.SectionTitle("Thermal"));
            Color heatColor = gen.CriticalFailure ? T.AccentRed
                            : gen.TemperatureC >= GridMaritimeEngine.KnockingTemperatureC ? T.AccentAmber
                            : T.AccentGreen;
            p.Add(T.StatRow("🌡", "Temperature", $"{gen.TemperatureC:0}°C", heatColor));
            var (heatBar, _) = T.ProgressBar(gen.Heat01, heatColor, 6, false);
            p.Add(heatBar);
            AddHullHeatRows(p, gen);
            AddEngineRoomRows(p, (GridBlock)gen);
            if (gen.CriticalFailure)
            {
                p.Add(T.Spacer(4));
                var crit = T.StatusPill("⛔ CRITICAL HEAT — OUTPUT CUT · COOL BELOW 80°C TO RECOVER", T.AccentRed);
                p.Add(crit.pill);
            }

            var gaugeRow = Row();
            gaugeRow.style.justifyContent = Justify.SpaceAround;
            Color batColor = gen.BufferFill01 > 0.2f ? T.AccentGreen : T.AccentRed;
            gaugeRow.Add(T.TankGauge("BUFFER", gen.BufferFill01, batColor,
                $"{gen.BufferCharge:0} / {gen.bufferCapacityWh:0} Wh", 70, 120));
            gaugeRow.Add(T.TankGauge("COOLANT", gen.CoolantFill01, new Color(0.25f, 0.55f, 0.95f),
                $"{gen.CoolantBuffer:0} / {gen.coolantCapacity:0} L", 70, 120));
            p.Add(gaugeRow);
            p.Add(T.Muted("Smooths output — the generator charges this buffer from " +
                          "shaft power, then feeds steady electricity to the grid."));

            // ── Upgrade modules ───────────────────────────────────────
            var moduleSlots = gen.GetModuleSlots();
            if (moduleSlots != null && slot != null)
            {
                p.Add(T.Spacer(6));
                p.Add(GridUIHelpers.SectionTitle($"Upgrade Modules ({GridMaritimeGenerator.MaxModuleSlots} slots)"));
                p.Add(GridUIHelpers.WeightHeader(MassUtil.ContainerMass(moduleSlots), "Modules"));
                var moduleGrid = T.SlotGrid(GridMaritimeGenerator.MaxModuleSlots);
                for (int i = 0; i < moduleSlots.Size; i++)
                    moduleGrid.Add(slot(moduleSlots, i, moduleSlots.GetSlot(i), false, true));
                p.Add(moduleGrid);

                if (gen.EfficiencyChipCount > 0)
                    p.Add(T.StatRow("🧩", "Efficiency Chip", $"Output ×{gen.ModuleOutputMultiplier:0.##} · requires active coolant flow", T.AccentPurple));
                else
                    p.Add(T.Muted("Socket an Efficiency Tuning Chip (+40% max output, requires coolant) " +
                                  "or a Super-Cooler Radiator Jacket (+200% heat dissipation, draws water)."));

                if (gen.RadiatorModuleCount > 0)
                {
                    Color radColor = gen.RadiatorCoolingActive ? T.AccentCyan : T.AccentRed;
                    p.Add(T.StatRow("💧", "Radiator Water", gen.RadiatorCoolingActive ? "FLOWING" : "DRY — DRAWING", radColor));
                    var (radBar, _) = T.ProgressBar(gen.RadiatorWaterFill01, radColor, 5, false);
                    p.Add(radBar);
                }
            }

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
            p.Add(T.StatRow("🔩", "Gear Ratio", $"{gb.EffectiveRatio:0.##}×", T.AccentGold));
            if (Mathf.Abs(gb.AppliedRatio - gb.EffectiveRatio) > 0.01f)
                p.Add(T.StatRow("⛔", "Governed Ratio", $"{gb.AppliedRatio:0.##}× at current RPM", T.AccentAmber));
            p.Add(T.StatRow("⚡", "Max Speed", $"{gb.maxOutputSpeed:0} RPM", T.AccentCyan));

            p.Add(T.Spacer(4));

            // Input stats.
            p.Add(T.StatRow("↙", "Input Speed", $"{gb.InputRPM:0} RPM", T.AccentTeal));
            p.Add(T.StatRow("↙", "Input Torque", $"{gb.InputTorque:0} N·m", T.TextSecondary));

            // Output stats.
            p.Add(T.StatRow("↗", "Output Speed", $"{gb.OutputRPM:0} RPM", T.AccentTeal));
            float outTorque = gb.gearRatio > 0.01f ? 1f / gb.gearRatio : 0f;
            p.Add(T.StatRow("↗", "Output Torque", $"{gb.OutputTorque:0} N·m · {outTorque * 100f:0}%", T.AccentGold));
            p.Add(T.StatRow("⛓", "Mechanical Load", $"{gb.MechanicalLoadRatio * 100f:0}%", gb.MechanicalLoadRatio > 1f ? T.AccentRed : T.AccentTeal));

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

            // ── Free-form ratio: type a number or drag the slider ─────
            p.Add(T.Spacer(6));
            p.Add(GridUIHelpers.SectionTitle("Gear Ratio"));

            var slider = new Slider(GridGearbox.MinGearRatio, GridGearbox.MaxGearRatio)
            {
                value = gb.EffectiveRatio,
                showInputField = true, // the player can type e.g. 6 for 6× directly
                lowValue = GridGearbox.MinGearRatio,
                highValue = GridGearbox.MaxGearRatio,
            };
            slider.style.marginTop = 2;

            var ratioSummary = new Label();
            ratioSummary.style.fontSize = 10;
            ratioSummary.style.marginTop = 4;
            ratioSummary.style.color = new StyleColor(T.TextSecondary);
            ratioSummary.style.whiteSpace = WhiteSpace.Normal;

            void UpdateSummary()
            {
                float r = gb.EffectiveRatio;
                ratioSummary.text =
                    $"Output: speed ×{r:0.##}  ·  torque ÷{r:0.##}" +
                    (r > 1f ? "   (high gear — speed build)" : r < 1f ? "   (low gear — heavy loads)" : "   (1:1 direct drive)");
            }

            // Do not write back into the slider while its text field is being edited:
            // that used to replace a partially typed value every 4 Hz panel refresh.
            slider.RegisterValueChangedCallback(evt =>
            {
                gb.SetRatio(evt.newValue);   // applies live through the propulsion job
                UpdateSummary();
            });

            var numberField = slider.Q<TextField>();
            if (numberField != null)
            {
                numberField.RegisterCallback<FocusInEvent>(_ => IsNumericInputFocused = true);
                numberField.RegisterCallback<FocusOutEvent>(_ => IsNumericInputFocused = false);
            }
            slider.RegisterCallback<DetachFromPanelEvent>(_ => IsNumericInputFocused = false);
            slider.RegisterCallback<WheelEvent>(evt =>
            {
                if (Mathf.Abs(evt.delta.y) < 0.001f) return;
                // Fine 0.05× adjustments by wheel; Shift accelerates to 0.25×.
                float step = evt.shiftKey ? 0.25f : 0.05f;
                gb.SetRatio(gb.EffectiveRatio + Mathf.Sign(-evt.delta.y) * step);
                slider.SetValueWithoutNotify(gb.EffectiveRatio);
                UpdateSummary();
                evt.StopPropagation();
            });
            UpdateSummary();

            p.Add(slider);
            p.Add(ratioSummary);
            p.Add(T.Spacer(4));
            p.Add(T.Muted("Bidirectional: power can enter from EITHER side — the opposite side " +
                          "automatically becomes the output. Higher ratio = faster output but less " +
                          "torque. Low ratios for heavy props, high ratios for generators."));

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
        // ════════════════════════════════════════════════════════════════
        //  MARINE WATER PUMP
        // ════════════════════════════════════════════════════════════════
        private static VisualElement MarineWaterPumpPanel(GridMarineWaterPump mwp)
        {
            var p = T.MachinePanel();

            string status = !mwp.IsSubmerged ? "⚠ NOT SUBMERGED" : mwp.IsPumping ? "● PUMPING" : "○ IDLE";
            Color statusColor = !mwp.IsSubmerged ? T.AccentRed : mwp.IsPumping ? T.AccentGreen : T.AccentDim;

            var (hdr, _, _, _) = T.HeaderRow("🌊 Marine Water Pump", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentBlue));

            var gaugeRow = Row();
            gaugeRow.style.justifyContent = Justify.Center;
            gaugeRow.Add(T.TankGauge("WATER", mwp.Fill01, new Color(0.25f, 0.55f, 0.95f),
                $"{mwp.Buffer:0} / {mwp.bufferCapacity:0} L", 70, 120));
            p.Add(gaugeRow);
            p.Add(T.Spacer(6));

            p.Add(T.StatRow("🚿", "Pump Rate", $"{mwp.pumpRate:0} L/s", T.AccentCyan));
            p.Add(T.StatRow("📏", "Suction Depth", $"{mwp.suctionDepth:0.#} m", T.AccentTeal));
            p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(mwp.PowerDraw), T.AccentGold));
            p.Add(T.StatRow("🌊", "Submerged", mwp.IsSubmerged ? "Yes — pumping from ocean" : "No — must be below waterline", T.TextSecondary));

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Sucks water from the ocean and pushes it into connected Water tanks. " +
                          "Place below the waterline. Used for engine coolant supply."));
            return p;
        }

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
            bool commanded = ep.CommandedPowerWatts > 0.01f;
            bool powerLimited = commanded && ep.PowerAvailability01 < 0.99f;
            string status = !ep.Enabled ? "○ OFFLINE"
                : !commanded ? "○ STANDBY"
                : powerLimited ? $"◐ POWER {ep.PowerAvailability01 * 100f:0}%"
                : spinning ? "● SPINNING"
                : "○ STOPPED";
            Color statusColor = !ep.Enabled ? T.AccentDim
                : powerLimited ? T.AccentAmber
                : spinning ? T.AccentGreen
                : T.AccentDim;

            var (hdr, _, _, _) = T.HeaderRow("⚡ Electrical Propeller", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentPurple));

            p.Add(GridUIHelpers.SectionTitle("Propulsion"));
            p.Add(T.StatRow("⚙", "Speed", $"{ep.CurrentRPM:0} RPM", T.AccentTeal));
            p.Add(T.StatRow("🚀", "Thrust", PowerFormat.Newtons(ep.CurrentThrustN), T.AccentGreen));
            p.Add(T.StatRow("📐", "Size", $"{ep.propellerSize:0}×", T.AccentCyan));

            p.Add(T.Spacer(4));
            p.Add(GridUIHelpers.SectionTitle("Power"));
            p.Add(T.StatRow("⌁", "Command", PowerFormat.Watts(ep.CommandedPowerWatts), T.AccentGold));
            p.Add(T.StatRow("⚡", "Delivered", PowerFormat.Watts(ep.DeliveredPowerWatts),
                ep.PowerAvailability01 >= 0.99f ? T.AccentGreen : T.AccentAmber));
            p.Add(T.StatRow("📊", "Grid Service", $"{ep.PowerAvailability01 * 100f:0}%", T.AccentCyan));
            p.Add(T.StatRow("▣", "Rated Max", PowerFormat.Watts(ep.powerDrawWatts), T.AccentAmber));
            var (powerBar, _) = T.ProgressBar(ep.PowerAvailability01, T.AccentCyan, 6, false);
            p.Add(powerBar);

            p.Add(T.Spacer(6));
            p.Add(T.Muted("Commanded draw is billed once by the grid power bus. Delivered power sets real RPM and thrust. " +
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
            p.Add(T.StatRow("⚡", "Boost Multiplier", $"{tc.EffectiveBoost:0.00}× torque ({tc.tier})", T.AccentGreen));

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
        //  WATERTIGHT SHAFT HOUSING — sealed mechanical pass-through
        // ════════════════════════════════════════════════════════════════
        private static VisualElement ShaftHousingPanel(GridShaftHousing housing)
        {
            var p = T.MachinePanel();
            bool spinning = housing.CurrentRPM > 1f;
            string status = spinning ? "● SEALED + ROTATING" : "● SEALED";
            Color statusColor = spinning ? T.AccentGreen : T.AccentTeal;

            var (hdr, _, _, _) = T.HeaderRow("◉ Watertight Shaft Housing", status, statusColor);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentTeal));
            p.Add(T.StatRow("⚙", "Shaft Speed", $"{housing.CurrentRPM:0} RPM", T.AccentTeal));
            p.Add(T.StatRow("⚡", "Max Safe RPM", $"{housing.maxSafeRPM:0} RPM", T.AccentAmber));
            p.Add(T.StatRow("💧", "Hull Seal", housing.waterproof ? "WATERTIGHT" : "CHECK SEAL", housing.waterproof ? T.AccentGreen : T.AccentRed));
            p.Add(T.Spacer(6));
            p.Add(T.Muted("A sealed hull block with a through-shaft. Use it where a mechanical line crosses the water-facing hull, then belt-link parallel shafts to branch additional outputs."));
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

            // Roadmap 5.1 item 14: where the gas goes matters as much as how much there is.
            if (ex.ConcealedVent01 > 0.02f)
            {
                p.Add(T.StatRow("🏥", "Venting Into",
                    $"{ex.ConcealedVent01 * 100f:0}% trapped in a sealed compartment", T.AccentRed));
                var sroom = ex.ServedRoom;
                if (sroom != null)
                    p.Add(T.StatRow("🔥", "Room Air",
                        $"{sroom.AirTemperatureC:0} °C  ·  {VoxelEngine.Thermal.ThermalRules.RoomBandLabel(sroom.Band)}",
                        VoxelEngine.Thermal.ThermalRules.BandColor(sroom.Band)));
                p.Add(T.Muted("The plume stopped at the bulkhead. The room keeps the heat, the casing runs hotter, and the engine it serves feels back-pressure — this funnel needs an exhaust scrubber, or the space needs an opening."));
            }

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
