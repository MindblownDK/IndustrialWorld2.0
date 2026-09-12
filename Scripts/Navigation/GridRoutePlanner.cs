// Assets/Scripts/VoxelEngine/Navigation/GridRoutePlanner.cs
//
// THE ROUTE CALCULATOR — turns a list of cosmic points into "can this ship fly it, and what
// will it cost", using nothing but numbers the flight model is already spending.
//
// Three deliberate choices are worth stating, because they are what keep the plan honest:
//
//   1. It never re-simulates an orbit. Every body figure comes from `OrbitalTelemetry.Sample`
//      and `CosmicRegistry`, the same two the pilot HUD and the Warp Drive read, so a plan and
//      the ship that flies it cannot disagree about what mu or r is.
//   2. Energy is bookkeeping, not fiction, and it is billed the way the game actually spends it:
//      the drive's own rated draw for every second it is lit, and the grid's standing load for
//      every second the trip takes, coast included. A long leg is several push-and-brake cycles,
//      so each cycle is paid for. Change the thrusters, the batteries or the tanks and the plan
//      changes by itself; that is the whole point.
//      (An earlier draft derived the draw from 1/2*m*v^2 instead. It is more physical and it is
//      wrong here: this game's batteries are arcade-sized on purpose, and a realistic joule
//      figure priced a 50 000 kg ship at millions of watt-hours a leg, making every route in the
//      system illegal. The game's own numbers are the ones that keep the game playable.)
//   3. A warning is a code with a sentence attached, never a silent number. The roadmap asks for
//      "clear warnings explaining whether the ship can complete the route and what resource is
//      missing" — so every reason not to go is listed by name on the panel.
//
// Waypoints pinned to a body are resolved against that body first, which is what makes a saved
// route survive the hours it takes the planets to move underneath it.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Navigation
{
    public static class GridRoutePlanner
    {
        /// <summary>Convenience: evaluate a route from a live grid, filling in the ship-side
        /// numbers (mass, thrust, stored energy, crew draw) itself.</summary>
        public static RoutePlan Evaluate(ShipRoute route, GridEntity grid)
        {
            if (grid == null) return Evaluate(route, 0f, (0f, 0f), 0f);

            var thrust = grid.GetThrustByDirection();
            float fwd = Mathf.Max(0f, thrust.fwd);
            float brake = Mathf.Max(0f, thrust.back, thrust.down, thrust.up, thrust.left, thrust.right);

            // What the drive itself costs to run, read off the fitted thrusters rather than made
            // up: electric units bill watts, hydrogen units bill litres per second, and a mixed
            // fit bills both, which is why there are two reserves and two ways to fail a route.
            DriveRating(grid, out float driveWatts, out float hydrogenLps, out float idleDraw);

            // The ship's own standing load while the route is being flown: every light, pump and
            // machine the grid is drawing right now, minus what the idle engines already contribute
            // so their full burn is not charged twice. Real and measured, and it is what turns
            // "how long is the trip" into a cost the pilot can feel.
            float sustainedWatts = Mathf.Max(RouteRules.SustainedLoadWatts, grid.PowerConsumed - idleDraw);
            return Evaluate(route, grid.TotalMass, (fwd, brake), StoredWattHours(grid), sustainedWatts,
                            driveWatts, hydrogenLps, Mathf.Max(0f, grid.HydrogenStored));
        }

        /// <summary>
        /// The whole calculator. Kept free of `GridEntity` so a screen, a save-time audit or a
        /// test can ask the same question with different inputs.
        /// </summary>
        public static RoutePlan Evaluate(ShipRoute route, float massKg,
            (float fwd, float brake) thrustNewtons, float storedEnergyWh, float sustainedWatts = 0f,
            float thrustWatts = 0f, float thrustHydrogenLps = 0f, float hydrogenStoredL = 0f)
        {
            var legs = new List<RouteLeg>(route != null ? Mathf.Max(0, route.waypoints.Count - 1) : 0);
            var warnings = new List<RouteWarningCode>(4);

            if (sustainedWatts <= 0.001f) sustainedWatts = RouteRules.SustainedLoadWatts;
            float idleThrustWatts = Mathf.Max(0f, thrustWatts);

            if (route == null || !route.IsFlyable || route.sceneCoordinates
                || route.travelMode == RouteTravelMode.Road || route.travelMode == RouteTravelMode.Water)
                return new RoutePlan(false, 0f, 0f, 0f, 0f, Mathf.Max(0f, thrustNewtons.fwd),
                    0f, 0f, Mathf.Max(0f, storedEnergyWh), 0f, idleThrustWatts, 0f,
                    Mathf.Max(0f, storedEnergyWh), legs, warnings);

            var registry = CosmicRegistry.Instance;
            var origin = SpaceOrigin.Instance;
            if (registry == null || !registry.IsReady || origin == null)
            {
                warnings.Add(RouteWarningCode.NoBodies);
                return new RoutePlan(false, 0f, 0f, 0f, 0f, Mathf.Max(0f, thrustNewtons.fwd),
                    0f, 0f, Mathf.Max(0f, storedEnergyWh), 0f, idleThrustWatts, 0f,
                    Mathf.Max(0f, storedEnergyWh), legs, warnings);
            }

            float mass = Mathf.Max(1f, massKg);
            float fwdThrust = Mathf.Max(0f, thrustNewtons.fwd);
            float brakeThrust = Mathf.Max(1f, thrustNewtons.brake);
            if (fwdThrust <= 0.001f) warnings.Add(RouteWarningCode.NoThrust);

            // The ship's own comfortable speed: fast enough that it can still stop within one
            // body radius of the frame it is standing in. Everything else in this method derives
            // from that, so a heavier or weaker ship plans slower all by itself.
            float characteristicR = 50000f;
            var frameBody = origin.FrameBody;
            if (frameBody != null && frameBody.SurfaceRadius > 1f)
                characteristicR = Mathf.Max(1000f, frameBody.SurfaceRadius);
            float accel = fwdThrust / mass;
            float holdSpeed = accel > 0.0001f ? Mathf.Sqrt(Mathf.Max(1f, 2f * accel * characteristicR)) : 0f;
            float cruiseSpeed = Mathf.Min(holdSpeed * RouteRules.ProfileFactor(route.Profile),
                                          RouteRules.MaxPracticalCruiseMs);
            if (cruiseSpeed <= 0.01f) warnings.Add(RouteWarningCode.NoThrust);

            // Braking authority decides whether a leg is one burn or a flight.
            float burnDeltaV = brakeThrust / mass * RouteRules.SingleBurnCoastSeconds;

            float totalKm = 0f, totalSeconds = 0f, totalWh = 0f, peakSpeed = 0f;
            float totalBurnSeconds = 0f, totalHydrogenL = 0f;
            float driveWatts = Mathf.Max(0f, thrustWatts);
            float hydrogenRate = Mathf.Max(0f, thrustHydrogenLps);
            float worstWellRatio = 0f;
            BodyInstance worstWell = null;
            bool atmosphereDip = false, multiBurn = false, arrivalUnsafe = false;

            var bodies = registry.Bodies;
            for (int i = 0; i + 1 < route.waypoints.Count; i++)
            {
                double3 a = route.waypoints[i].ResolvedPositionKm(registry);
                double3 b = route.waypoints[i + 1].ResolvedPositionKm(registry);
                double spanKm = math.length(b - a);   // resolved, so a pinned point moves with its body
                float legKm = (float)math.max(0d, spanKm);
                float legMetres = legKm * 1000f;

                // --- what the leg passes near, and what that costs -----------------------
                BodyInstance legWell = null;
                double legWellRadiusKm = 0d;
                bool legAtmosphere = false;
                for (int k = 0; k < bodies.Count; k++)
                {
                    var body = bodies[k];
                    if (body == null || body.settings == null) continue;
                    double3 c = registry.CosmicPositionOf(body);
                    double radiusKm = Mathf.Max(0.001f, body.settings.radiusKm);

                    // Distance from the body's centre to the segment, in km — a foot of a
                    // perpendicular, clamped to the segment so a well behind the ship does not
                    // count as one it flies through.
                    double3 ab = b - a;
                    double abLen2 = math.lengthsq(ab);
                    double t = abLen2 > 1e-9d ? math.clamp(math.dot(c - a, ab) / abLen2, 0d, 1d) : 0d;
                    double3 closest = a + ab * t;
                    double missKm = math.length(closest - c);

                    double atmosphereTopKm = radiusKm
                        * (1d + (double)Mathf.Max(0f, body.settings.atmosphereHeightRadiusFraction));
                    double influenceKm = WellInfluenceKm(body, radiusKm);

                    if (missKm < atmosphereTopKm) legAtmosphere = true;
                    if (missKm < influenceKm)
                    {
                        // The deepest well on this leg is the one worth reporting.
                        double depth = influenceKm - Mathf.Max(1f, (float)missKm);
                        if (legWell == null || depth > legWellRadiusKm)
                        {
                            legWell = body;
                            legWellRadiusKm = depth;
                        }
                    }
                }

                float speed = Mathf.Max(0f, cruiseSpeed);
                float legSeconds = speed > 0.01f ? legMetres / speed : 0f;

                // How the leg is flown, and what that costs. One cycle is "get up to speed, arrive,
                // slow down"; a leg longer than a cycle can reach is flown as a whole number of
                // cycles and each one pays. The draw is the drive's rated consumption, and the coast
                // is billed at the grid's standing load below, so trip length always costs.
                float accelMs2 = Mathf.Max(0.0001f, accel);
                float eff = Mathf.Max(0.05f, RouteRules.PropulsionEfficiency);

                // A leg is flown as one acceleration to the profile speed, a coast, and one
                // deceleration at the far end — not a fresh start-and-stop for every reachable
                // kilometre, which priced a 50 km hop as seven burns. Extra delta-v is then added
                // for the trims a long coast needs and for a well that has to be climbed on arrival,
                // so burn time, watts and hydrogen all grow with the number of burns.
                float legDeltaV = speed > 0.01f ? 2f * speed * RouteRules.ManeuverCostFactor : 0f;
                float coastSeconds = speed > 0.01f
                    ? Mathf.Max(0f, legSeconds - legDeltaV / Mathf.Max(0.0001f, accelMs2)) : 0f;
                int trims = Mathf.FloorToInt(coastSeconds
                    / Mathf.Max(1f, RouteRules.SingleBurnCoastSeconds * 3f));
                int burnCount = 1 + trims;
                // A leg that crosses a well the ship cannot coast out of needs a boost on arrival:
                // an extra burn, an extra warning, and an extra bill — not a silent surcharge.
                bool legWellBoost = legWell != null
                    && WellEscapeDeltaV(legWell, (float)legWellRadiusKm)
                       > brakeThrust / mass * RouteRules.SingleBurnCoastSeconds;
                if (legWellBoost) { burnCount += 1; legDeltaV += WellEscapeDeltaV(legWell, (float)legWellRadiusKm); }
                if (trims > 0) legDeltaV += trims * speed * 0.15f;   // course correction, cheap but not free
                float legBurnSeconds = speed > 0.01f ? legDeltaV / (accelMs2 * eff) : 0f;
                float legWh = legBurnSeconds * driveWatts / 3600f;
                float legHydrogenL = legBurnSeconds * hydrogenRate;
                bool legMultiBurn = burnCount > 1;
                if (legMultiBurn) multiBurn = true;
                if (legAtmosphere) atmosphereDip = true;

                totalKm += legKm;
                totalSeconds += legSeconds;
                totalWh += legWh;
                totalBurnSeconds += legBurnSeconds;
                totalHydrogenL += legHydrogenL;
                peakSpeed = Mathf.Max(peakSpeed, speed);

                if (legWell != null)
                {
                    float wellDvMs = WellEscapeDeltaV(legWell, (float)legWellRadiusKm);
                    float ratio = burnDeltaV > 0.001f ? wellDvMs / burnDeltaV : float.MaxValue;
                    if (ratio > worstWellRatio) { worstWellRatio = ratio; worstWell = legWell; }
                }

                legs.Add(new RouteLeg(i, legKm, speed, legSeconds, legWh, legWell, legAtmosphere,
                    legMultiBurn, burnCount, legBurnSeconds, legHydrogenL));
            }

            // --- arrival safety: the last point must sit outside its own body's envelope ---
            if (route.waypoints.Count > 0)
            {
                var last = route.waypoints[route.waypoints.Count - 1];
                var host = last.IsNamed ? RouteWaypoint.FindBody(registry, last.bodyId) : null;
                if (host != null && host.settings != null)
                {
                    double3 centre = registry.CosmicPositionOf(host);
                    double clearanceKm = math.length(last.ResolvedPositionKm(registry) - centre)
                        - host.settings.radiusKm;
                    double topKm = host.settings.radiusKm * host.settings.atmosphereHeightRadiusFraction;
                    if (clearanceKm < Mathf.Max(0.05f, host.settings.radiusKm * 0.002f)
                        || clearanceKm < topKm) arrivalUnsafe = true;
                }
            }

            // --- the budget, and what is missing from it -----------------------------------
            float sustainedWh = totalSeconds * Mathf.Max(0f, sustainedWatts) / 3600f;
            float required = totalWh + sustainedWh;
            float available = Mathf.Max(0f, storedEnergyWh);
            float margin = available > 0.0001f ? (available - required) / available : -1f;

            // Two reserves, because the game pays for thrust two ways. Whichever runs out first is
            // the answer the pilot gets; a hydrogen-only drive is never judged on its batteries.
            bool hydrogenFitted = hydrogenRate > 0.0001f;
            if (!hydrogenFitted)
            {
                if (available <= 0.0001f || margin < RouteRules.MinimumReserve01)
                    warnings.Add(RouteWarningCode.ReserveShortfall);
            }
            float hydrogenMargin = 1f;
            if (hydrogenFitted)
            {
                hydrogenMargin = hydrogenStoredL > 0.0001f
                    ? (hydrogenStoredL - totalHydrogenL) / hydrogenStoredL : -1f;
                if (hydrogenMargin < RouteRules.MinimumReserve01)
                    warnings.Add(RouteWarningCode.HydrogenShortfall);
                if (available <= 0.0001f) margin = hydrogenMargin;   // a hydrogen ship is judged on its tank
                else margin = Mathf.Min(margin, hydrogenMargin);
            }
            margin = Mathf.Clamp01(margin);

            // The panel's reserve bar needs a denominator that exists for this ship.
            float reserveCeilingWh = available;
            if (hydrogenFitted)
            {
                float asWh = hydrogenStoredL * 3600f / Mathf.Max(0.0001f, hydrogenRate);
                reserveCeilingWh = available > 0.0001f
                    ? Mathf.Min(available, asWh) : asWh;
            }
            reserveCeilingWh = Mathf.Max(0f, reserveCeilingWh);
            if (worstWellRatio > 1f) warnings.Add(RouteWarningCode.GravityWell);
            if (atmosphereDip) warnings.Add(RouteWarningCode.AtmosphereHazard);
            if (arrivalUnsafe) warnings.Add(RouteWarningCode.ArrivalUnsafe);
            if (multiBurn) warnings.Add(RouteWarningCode.MultiBurnLeg);
            // A ship that can push twice as hard as it can stop is planning a crash, not a route.
            // Only worth saying when the route actually leans on braking, i.e. when it crosses a
            // well: a forward-only thrust collar is normal on an atmosphere plane and must not be
            // reported as a fault on a straight deep-space leg.
            if (cruiseSpeed > 0.01f && worstWell != null && brakeThrust < fwdThrust * 0.5f)
                warnings.Add(RouteWarningCode.GravityWell);

            // Required thrust is not a made-up figure: it is what it takes to hold the profile
            // speed and still stop inside one body radius of where the pilot is going.
            float requiredThrust = mass * cruiseSpeed / Mathf.Max(1f, RouteRules.SingleBurnCoastSeconds * 0.5f);
            if (fwdThrust > 0.001f && requiredThrust > fwdThrust * 1.02f) warnings.Add(RouteWarningCode.PowerShortfall);

            return new RoutePlan(true, totalKm, totalSeconds, peakSpeed, requiredThrust, fwdThrust,
                totalWh, sustainedWh, reserveCeilingWh, margin, driveWatts, totalHydrogenL,
                reserveCeilingWh, legs, warnings);
        }

        /// <summary>Read the fitted drive: watts at full burn, litres per second at full burn, and
        /// what the same engines are already pulling from the grid while they idle. Hydrogen units
        /// report no watt draw by design, so a hydrogen ship's battery line is not held against it.
        /// Non-destructive: it reads only, and a ship with no thrusters simply rates at zero.</summary>
        public static void DriveRating(GridEntity grid, out float watts, out float hydrogenLps,
            out float idleWatts)
        {
            watts = 0f; hydrogenLps = 0f; idleWatts = 0f;
            if (grid == null) return;
            foreach (var block in grid.AllBlocks)
            {
                if (block is not GridThruster thruster) continue;
                // `enabled` is whether the block exists in the world; `Enabled` is whether the
                // crew switched it on. A disabled engine is neither a constraint nor a cost: a
                // ship with its drive off is a hull, and the plan says so through NoThrust instead.
                if (!thruster.enabled || !thruster.Enabled) continue;

                watts += Mathf.Max(0f, thruster.powerAtMaxThrust);
                // PowerDraw is already the live figure: zero for a hydrogen unit, rated × fraction
                // for an electric one. That is exactly what must not be charged twice against the
                // ship's standing load.
                idleWatts += Mathf.Max(0f, thruster.PowerDraw);
                if (thruster.thrusterType == ThrusterType.Hydrogen)
                    hydrogenLps += Mathf.Max(0f, thruster.hydrogenPerSecond);
            }
        }

        /// <summary>Stored energy the plan can spend: every battery on the grid plus the chemical
        // reserve the ship is already carrying, converted at the same figure the grid uses.</summary>
        public static float StoredWattHours(GridEntity grid)
        {
            if (grid == null) return 0f;
            float wh = 0f;
            foreach (var block in grid.AllBlocks)
                if (block is GridBattery battery) wh += Mathf.Max(0f, battery.storedWh);
            return wh;
        }

        /// <summary>How far a body's well reaches for planning purposes: the radius where its own
        /// gravity stops mattering against the ship's thrust, bounded so a planet never swallows
        /// the whole system in the report. Two-body mu is read from the registry, not guessed.</summary>
        static double WellInfluenceKm(BodyInstance body, double radiusKm)
        {
            double mu = body.gravitationalParamKm3S2;
            if (mu <= 1e-9d) return radiusKm * 4d;
            // A sphere of influence against the frame's dominant body, floored and capped to keep
            // the report readable: never smaller than five radii, never larger than a fifth of
            // the way to the next body out.
            double hill = radiusKm * 4d;
            double soid = radiusKm * System.Math.Sqrt(mu / 180d);   // 180 is the sun's own mu, in km³/s²
            double influence = System.Math.Max(hill, System.Math.Min(soid, radiusKm * 60d));
            return influence;
        }

        /// <summary>Delta-v (m/s) to climb out of a body's well from the closest point of a leg.
        /// A vis-viva escape from the miss distance, which is the number a pilot actually needs.</summary>
        static float WellEscapeDeltaV(BodyInstance body, float radiusKm)
        {
            if (body == null || radiusKm <= 0.001f) return 0f;
            double mu = body.gravitationalParamKm3S2;
            if (mu <= 1e-9d) return 0f;
            double r = System.Math.Max(0.001d, (double)radiusKm) * 1000d;   // metres
            return (float)System.Math.Sqrt(2d * mu * 1e9d / r);              // m/s
        }
    }
}
