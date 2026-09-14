using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Maritime;
using VoxelEngine.Navigation;

namespace IndustrialWorld.Navigation
{
    public static class PilotRouteAssessment
    {
        public struct BatteryStats
        {
            public float storedWh;
            public float capacityWh;
            public float maxDischargeW;
            public float maxChargeW;
            public float usableWh;
            public int count;
        }

        public static BatteryStats CollectBatteries(GridEntity grid)
        {
            var stats = new BatteryStats();
            if (grid == null) return stats;
            foreach (var block in grid.AllBlocks)
            {
                if (block is not GridBattery battery) continue;
                if (!battery.Enabled) continue;
                stats.count++;
                stats.storedWh += Mathf.Max(0f, battery.storedWh);
                stats.capacityWh += Mathf.Max(0f, battery.capacityWh);
                stats.maxDischargeW += Mathf.Max(0f, battery.maxDischargeRate);
                stats.maxChargeW += Mathf.Max(0f, battery.maxChargeRate);
                if (battery.CanDischarge)
                    stats.usableWh += Mathf.Max(0f, battery.storedWh);
            }
            return stats;
        }

        public static (float wheelWatts, float driveForce, int wheelCount, float avgSuspension) CollectWheels(GridEntity grid)
        {
            float watts = 0f;
            float drive = 0f;
            float suspSum = 0f;
            int count = 0;
            if (grid == null) return (0f, 0f, 0, 0f);
            foreach (var block in grid.AllBlocks)
            {
                if (block is not GridWheel wheel) continue;
                if (!wheel.Enabled) continue;
                count++;
                watts += Mathf.Max(0f, wheel.powerDrawWatts) * Mathf.Clamp01(wheel.suspensionStrength);
                drive += Mathf.Max(0f, wheel.driveForce);
                suspSum += Mathf.Clamp01(wheel.suspensionStrength);
            }
            float avg = count > 0 ? suspSum / count : 0f;
            return (watts, drive, count, avg);
        }

        public static (float electricalWatts, float mechanicalTorque, int engineCount, int generatorCount, float genRatedWatts, float genMaxRPM) CollectMaritime(GridEntity grid)
        {
            float elecW = 0f;
            float torque = 0f;
            int engines = 0;
            int gens = 0;
            float genRated = 0f;
            float genRpm = 0f;
            if (grid == null) return (0f, 0f, 0, 0, 0f, 0f);
            foreach (var block in grid.AllBlocks)
            {
                if (block is GridMaritimeEngine eng && eng.Enabled)
                {
                    engines++;
                    torque += Mathf.Max(0f, eng.maxTorque) * eng.TurboBoostTotal * eng.ModuleOutputMultiplier;
                }
                else if (block is GridMaritimeGenerator gen && gen.Enabled)
                {
                    gens++;
                    genRated += gen.EffectiveMaxWattOutput;
                    genRpm = Mathf.Max(genRpm, gen.maxRPM);
                }
                else if (block is GridElectricalPropeller eProp && eProp.Enabled)
                {
                    elecW += Mathf.Max(0f, eProp.powerDrawWatts);
                }
            }
            return (elecW, torque, engines, gens, genRated, genRpm);
        }

        public static (float thrustN, float powerW, float hydrogenLps, int thrusterCount) CollectThrusters(GridEntity grid)
        {
            float thrust = 0f;
            float power = 0f;
            float h2 = 0f;
            int count = 0;
            if (grid == null) return (0f, 0f, 0f, 0);
            foreach (var block in grid.AllBlocks)
            {
                if (block is not GridThruster thruster) continue;
                if (!thruster.enabled || !thruster.Enabled) continue;
                count++;
                thrust += Mathf.Max(0f, thruster.maxThrustN) * (thruster.thrusterType == ThrusterType.Atmospheric ? thruster.AtmosphericEfficiency : 1f);
                power += Mathf.Max(0f, thruster.powerAtMaxThrust);
                if (thruster.thrusterType == ThrusterType.Hydrogen)
                    h2 += Mathf.Max(0f, thruster.hydrogenPerSecond);
            }
            return (thrust, power, h2, count);
        }

        public static string BuildAssessment(ShipRoute route, GridEntity grid, AutoRunPilot pilot)
        {
            if (route == null) return "No route selected.";
            if (grid == null) return "Place pilot on a grid first.";

            var sb = new StringBuilder();
            var batteries = CollectBatteries(grid);
            var wheels = CollectWheels(grid);
            var maritime = CollectMaritime(grid);
            var thrusters = CollectThrusters(grid);

            float mass = grid.TotalMass;
            float totalBatteryDischarge = 0f;
            foreach (var block in grid.AllBlocks)
                if (block is GridBattery b) totalBatteryDischarge += Mathf.Max(0f, b.CurrentDischargeWatts);
            float genWithoutBattery = Mathf.Max(0f, grid.PowerGenerated - totalBatteryDischarge);

            sb.AppendLine($"{route.travelMode} · {route.waypoints.Count} points · {mass:0} kg");
            if (batteries.count > 0)
            {
                float pct = batteries.capacityWh > 0.01f ? batteries.storedWh / batteries.capacityWh * 100f : 0f;
                float usablePct = batteries.capacityWh > 0.01f ? batteries.usableWh / batteries.capacityWh * 100f : 0f;
                sb.AppendLine($"BATTERY {batteries.storedWh:0} / {batteries.capacityWh:0} Wh ({pct:0}% total, {usablePct:0}% usable) · {batteries.count} packs · Discharge {batteries.maxDischargeW:0} W · Charge {batteries.maxChargeW:0} W");
            }
            else
            {
                sb.AppendLine("BATTERY 0 Wh — no battery packs installed");
            }
            sb.AppendLine($"GENERATION {genWithoutBattery:0} W live (excl. battery discharge) · Total ledger {grid.PowerGenerated:0} W · Consumed {grid.PowerConsumed:0} W · Balance {grid.PowerBalance:0} W");

            float controlWatts = pilot != null ? Mathf.Max(0f, pilot.controlWatts) : 40f;
            sb.AppendLine($"CONTROL {controlWatts:0} W pilot overhead");

            if (route.travelMode == RouteTravelMode.Road || route.travelMode == RouteTravelMode.RoadNetwork)
            {
                float distance = EstimateRoadDistance(route, grid);
                float speedCap = route.travelMode == RouteTravelMode.RoadNetwork ? 1.5f : 4f;
                float timeSec = speedCap > 0.01f ? distance / speedCap : 0f;
                timeSec *= 1.25f;
                timeSec += route.travelMode == RouteTravelMode.RoadNetwork ? 10f : 5f;

                float wheelPropulsionW = wheels.wheelWatts;
                float standingW = Mathf.Max(380f, grid.PowerConsumed);
                float tripWh = (wheelPropulsionW + standingW + controlWatts) * timeSec / 3600f;

                float predictedArrivalPct = -1f;
                if (batteries.capacityWh > 0.01f)
                {
                    float afterTrip = batteries.storedWh - tripWh + genWithoutBattery * timeSec / 3600f;
                    predictedArrivalPct = Mathf.Clamp01(afterTrip / batteries.capacityWh) * 100f;
                }

                sb.AppendLine($"ROAD {distance:0} m est. · {speedCap:0.0} m/s cap · {timeSec:0} s est. (incl. 25% allowance)");
                sb.AppendLine($"WHEELS {wheels.wheelCount} · {wheels.driveForce:0} N total drive · {wheelPropulsionW:0} W (powerDrawWatts * suspensionStrength) · Avg susp {wheels.avgSuspension:0%}");
                sb.AppendLine($"TRIP {tripWh:0} Wh est. · Standing {standingW:0} W + Prop {wheelPropulsionW:0} W + Ctrl {controlWatts:0} W");

                if (predictedArrivalPct >= 0f)
                    sb.AppendLine($"ARRIVAL {predictedArrivalPct:0}% predicted (assumes current {genWithoutBattery:0} W generation)");
                else
                    sb.AppendLine("ARRIVAL N/A — no battery capacity");

                if (batteries.maxDischargeW > 0.01f && wheelPropulsionW + standingW + controlWatts > batteries.maxDischargeW)
                    sb.AppendLine($"WARNING Discharge limit {batteries.maxDischargeW:0} W < demand {wheelPropulsionW + standingW + controlWatts:0} W");

                if (batteries.capacityWh < 1f)
                    sb.AppendLine("WARNING No battery — road runs require stored energy");
            }
            else if (route.travelMode == RouteTravelMode.Water)
            {
                float distance = EstimateLocalDistance(route, grid);
                float speedCap = 2f;
                float timeSec = speedCap > 0.01f ? distance / speedCap : 0f;
                timeSec *= 1.25f;
                timeSec += 5f;

                float propW = maritime.electricalWatts;
                if (propW < 0.01f && maritime.mechanicalTorque > 0.01f)
                {
                    float omega = 1200f * 0.10471975512f;
                    propW = maritime.mechanicalTorque * omega * 0.85f * 0.01f;
                }

                float standingW = Mathf.Max(380f, grid.PowerConsumed);
                float tripWh = (propW + standingW + controlWatts) * timeSec / 3600f;

                float predictedArrivalPct = -1f;
                if (batteries.capacityWh > 0.01f)
                {
                    float afterTrip = batteries.storedWh - tripWh + genWithoutBattery * timeSec / 3600f;
                    predictedArrivalPct = Mathf.Clamp01(afterTrip / batteries.capacityWh) * 100f;
                }

                sb.AppendLine($"WATER {distance:0} m est. · {speedCap:0.0} m/s cap · {timeSec:0} s est. (incl. 25% allowance)");
                sb.AppendLine($"PROPULSION Electrical {maritime.electricalWatts:0} W · Mechanical torque {maritime.mechanicalTorque:0} Nm · Engines {maritime.engineCount} · Gens {maritime.generatorCount} ({maritime.genRatedWatts:0} W rated, {maritime.genMaxRPM:0} RPM max — dynamic from block stats)");
                sb.AppendLine($"TRIP {tripWh:0} Wh est. · Standing {standingW:0} W + Prop {propW:0} W + Ctrl {controlWatts:0} W");

                if (predictedArrivalPct >= 0f)
                    sb.AppendLine($"ARRIVAL {predictedArrivalPct:0}% predicted");
                else
                    sb.AppendLine("ARRIVAL N/A — no battery");

                if (maritime.engineCount == 0 && maritime.electricalWatts < 0.01f)
                    sb.AppendLine("WARNING No marine propulsion found — need propeller + engine or electrical propeller");
            }
            else if (route.travelMode == RouteTravelMode.Flight)
            {
                float distance = EstimateLocalDistance(route, grid);
                float speedCap = 4f;
                float timeSec = speedCap > 0.01f ? distance / speedCap : 0f;
                timeSec *= 1.25f;
                timeSec += 5f;

                float standingW = Mathf.Max(380f, grid.PowerConsumed);
                float tripWh = (thrusters.powerW + standingW + controlWatts) * timeSec / 3600f;

                float predictedArrivalPct = -1f;
                if (batteries.capacityWh > 0.01f)
                {
                    float afterTrip = batteries.storedWh - tripWh + genWithoutBattery * timeSec / 3600f;
                    predictedArrivalPct = Mathf.Clamp01(afterTrip / batteries.capacityWh) * 100f;
                }

                sb.AppendLine($"FLIGHT {distance:0} m est. · {speedCap:0.0} m/s cap · {timeSec:0} s est. (incl. 25% allowance)");
                sb.AppendLine($"THRUST {thrusters.thrustN:0} N total · {thrusters.powerW:0} W drive draw · {thrusters.thrusterCount} thrusters (dynamic from maxThrustN * AtmosphericEfficiency and powerAtMaxThrust)");
                if (thrusters.hydrogenLps > 0.01f)
                    sb.AppendLine($"H2 {thrusters.hydrogenLps:0.00} L/s burn · Stored {grid.HydrogenStored:0} L");
                sb.AppendLine($"TRIP {tripWh:0} Wh est. · Standing {standingW:0} W + Drive {thrusters.powerW:0} W + Ctrl {controlWatts:0} W");

                if (predictedArrivalPct >= 0f)
                    sb.AppendLine($"ARRIVAL {predictedArrivalPct:0}% predicted");
                else
                    sb.AppendLine("ARRIVAL N/A");

                if (thrusters.thrustN < mass * 1.5f)
                    sb.AppendLine($"WARNING Thrust {thrusters.thrustN:0} N < 1.5 m/s² margin for {mass:0} kg");
            }
            else if (route.travelMode == RouteTravelMode.LegacyFlight)
            {
                var plan = VoxelEngine.Navigation.GridRoutePlanner.Evaluate(route, grid);
                sb.AppendLine($"LEGACY SPACE {plan.TotalDistanceKm:0} km · {plan.TotalSeconds:0} s · {plan.PeakSpeedMs:0} m/s peak");
                sb.AppendLine($"THRUST Required {plan.RequiredThrustNewtons / 1000f:0.0} kN · Available {plan.AvailableThrustNewtons / 1000f:0.0} kN (dynamic from fitted thrusters)");
                sb.AppendLine($"ENERGY Drive {plan.EnergyWattHours:0} Wh + Sustained {plan.SustainedLoadWattHours:0} Wh = {plan.TotalWattHours:0} Wh · Stored {plan.StoredEnergyWh:0} Wh · Margin {plan.ReserveMargin01 * 100f:0}%");
                if (plan.ThrustPowerWatts > 0.01f)
                    sb.AppendLine($"DRIVE DRAW {plan.ThrustPowerWatts:0} W (powerAtMaxThrust live)");
                if (plan.HydrogenLitres > 0.01f)
                    sb.AppendLine($"H2 {plan.HydrogenLitres:0} L est.");
                foreach (var w in plan.Warnings)
                    sb.AppendLine($"WARNING {w} — {RoutePlan.TextFor(w)}");
            }

            sb.AppendLine("NOTE Estimates are rated-load planning, not guarantees. Refresh after refit/cargo/position change.");

            return sb.ToString();
        }

        private static float EstimateRoadDistance(ShipRoute route, GridEntity grid)
        {
            if (route == null) return 0f;
            if (route.waypoints.Count < 2) return 0f;
            float total = 0f;
            Vector3 prev = Vector3.zero;
            bool hasPrev = false;
            for (int i = 0; i < route.waypoints.Count; i++)
            {
                if (!RouteCoordinates.TryResolve(route, i, out var pos)) continue;
                if (hasPrev) total += Vector3.Distance(prev, pos);
                prev = pos;
                hasPrev = true;
            }
            if (route.travelMode == RouteTravelMode.RoadNetwork) total *= 1.3f;
            return total;
        }

        private static float EstimateLocalDistance(ShipRoute route, GridEntity grid)
        {
            if (route == null) return 0f;
            float total = 0f;
            Vector3 prev = Vector3.zero;
            bool hasPrev = false;
            for (int i = 0; i < route.waypoints.Count; i++)
            {
                if (!RouteCoordinates.TryResolve(route, i, out var pos)) continue;
                if (hasPrev) total += Vector3.Distance(prev, pos);
                prev = pos;
                hasPrev = true;
            }
            return total;
        }
    }
}
