// Assets/Scripts/VoxelEngine/Navigation/GridRoute.cs
//
// THE ROUTE MODEL — what a planned journey is, and what it costs this particular ship.
//
// Route planning has existed in the cockpit as a feeling: "we have enough charge for that,
// probably". The Warp Drive already answers "can I jump there"; nothing in the game answered
// "what does it cost to FLY there", which is the question that decides whether a haul route is
// a business or a one-way trip. This file is that answer, and it is deliberately arithmetic
// rather than a simulation — a route is planned in milliseconds, from the same numbers the
// flight model is already using, so a plan can never disagree with the ship that flies it.
//
// The model in one paragraph: a leg costs the energy to bring the ship's actual mass up to the
// speed the ship can actually hold, and the same again to throw it away at the far end. Thrust
// comes from `GridEntity.GetThrustByDirection()`, mass from `GridEntity.TotalMass`, gravity and
// atmosphere from `CosmicRegistry` and the body settings, and — crucially — the speed limit from
// `OrbitalTelemetry.Sample`, which is the same two-body solution the pilot HUD is showing. No
// constant in here invents a range; the constants only describe how generous the manoeuvre is.
//
// A route is stored in COSMIC KILOMETRES (`SpaceOrigin.GetCosmicKm`), not scene positions, because
// the scene origin moves and the planets move far more: a waypoint saved next to a moon has to
// still be next to that moon eight hours later, so a body-attached waypoint rides its parent.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Navigation
{
    /// <summary>One point on a journey: where, and what it is standing still relative to.</summary>
    [System.Serializable]
    public struct RouteWaypoint
    {
        /// <summary>Cosmic position in kilometres, captured in the current frame.</summary>
        public double3 positionKm;

        /// <summary>Body this point rides, when the author pinned it to one. A pinned waypoint is
        /// re-derived from its parent on every evaluation, so an orbit does not drift away from you.</summary>
        public string bodyId;

        /// <summary>Where the point sits relative to its parent, in kilometres. This is what makes
        /// pinning mean something: without it a pinned arrival would resolve to the body's own
        /// centre, i.e. inside the planet, and every recorded approach would be an impact.</summary>
        public double3 anchorOffsetKm;

        /// <summary>A named waymark this point stands for: a connector, a beacon, the smelter. When
        /// set it wins over the stored position, because the thing it names is where it is now, and a
        /// frozen copy of a moving pad is not a destination, it is a memory of one.</summary>
        public string waymarkName;

        /// <summary>Optional pilot note — where the anchor is, which berth, whose wreck it is.</summary>
        public string label;

        public RouteWaypoint(double3 posKm, BodyInstance rides, string note = null,
            CosmicRegistry registry = null)
        {
            label = note;
            waymarkName = null;   // a struct constructor must assign every field; a waymark is
                                  // written afterwards by whoever knows the player's name for it.
            if (rides == null)
            {
                positionKm = posKm;
                bodyId = null;
                anchorOffsetKm = double3.zero;
                return;
            }
            bodyId = rides.DisplayName;
            // Store the offset from the parent, not the absolute capture: the parent moves and the
            // offset does not. Without a registry to ask, the point is kept absolute and simply
            // will not follow the body — a conservative failure, not a wrong one.
            double3 centre = registry != null ? registry.CosmicPositionOf(rides) : double3.zero;
            if (registry != null)
            {
                anchorOffsetKm = posKm - centre;
                positionKm = posKm;
            }
            else
            {
                anchorOffsetKm = double3.zero;
                positionKm = posKm;
            }
        }

        /// <summary>Where this point is right now, in cosmic km, resolving its parent's motion.</summary>
        public double3 ResolvedPositionKm(CosmicRegistry registry)
        {
            // A waymark beats everything: it is live, it is named by the player, and it knows where
            // its block is this second. The stored position is what it degrades to when the source is
            // gone, which is exactly the "frozen, not crashed" rule the waymark model is built on.
            if (!string.IsNullOrEmpty(waymarkName))
            {
                var src = GridWaymark.FindSource(waymarkName);
                if (src != null)
                {
                    var origin = SpaceOrigin.Instance;
                    if (origin != null) return origin.GetCosmicKm(src.WaymarkWorldPosition);
                }
            }
            if (string.IsNullOrEmpty(bodyId) || registry == null) return positionKm;
            var body = FindBody(registry, bodyId);
            if (body == null) return positionKm;      // body gone (removed template): keep the frozen point
            // The body's position now, plus the offset it was written down at: this point is
            // "over there, relative to that world", and that is what survives the world moving.
            return registry.CosmicPositionOf(body) + anchorOffsetKm;
        }

        /// <summary>Distance between two points in the frame they are evaluated in — the only
        /// honest way to measure a leg, since a pinned point means something different every hour.</summary>
        public static double DistanceKm(RouteWaypoint a, RouteWaypoint b, CosmicRegistry registry)
        {
            return math.length(b.ResolvedPositionKm(registry) - a.ResolvedPositionKm(registry));
        }

        public static BodyInstance FindBody(CosmicRegistry registry, string id)
        {
            if (registry == null || string.IsNullOrEmpty(id)) return null;
            var bodies = registry.Bodies;
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i] != null && bodies[i].DisplayName == id) return bodies[i];
            return null;
        }

        public readonly bool IsNamed => !string.IsNullOrEmpty(bodyId);
    }

    /// <summary>How hard the ship is going to push: one profile, three authored multipliers.</summary>
    public enum RouteSpeedProfile { Economy = 0, Standard = 1, Sprint = 2 }

    /// <summary>A planned journey: a name, the points, and the profile to fly them on.</summary>
    public enum RouteTravelMode { LegacyFlight = 0, Road = 1, Water = 2, Flight = 3 }

    [System.Serializable]
    public class ShipRoute
    {
        public string routeName = "Route";
        // Additive metadata. Missing fields in older saves retain cosmic-flight semantics.
        public RouteTravelMode travelMode;
        public bool sceneCoordinates; // positionKm remains kilometres, even for a scene-local route.

        public List<RouteWaypoint> waypoints = new();

        /// <summary>The two names a shuttle loop runs between. They are waymarks, not waypoints: a
        /// loop is a promise to go back to a place, and a place that moves must be re-read every time,
        /// never replayed from a recording. Either may be empty, in which case the loop runs the
        /// recorded waypoints instead and only the return leg needs a name.</summary>
        public string startWaymark = "";
        public string endWaymark = "";
        public int speedProfileIndex = (int)RouteSpeedProfile.Standard;

        public RouteSpeedProfile Profile => (RouteSpeedProfile)Mathf.Clamp(speedProfileIndex, 0, 2);

        /// <summary>Leg count is point count minus one; a single point is an observation, not a route.</summary>
        public int LegCount => Mathf.Max(0, waypoints.Count - 1);

        public bool IsFlyable => waypoints.Count >= 2;

        public void AddWaypoint(RouteWaypoint wp) { waypoints.Add(wp); }

        public void Clear() { waypoints.Clear(); }

        /// <summary>A copy of this route from `fromIndex` onwards, with the given point bolted on the
        /// front. This is how the autopilot prices "what I still have to fly": the plan the recorder
        /// shows and the plan the loop refuses to fly must be produced by the same call, or the
        /// schedule and the arithmetic drift apart and the drift is what strands ships.</summary>
        public ShipRoute RemainingFrom(int fromIndex, RouteWaypoint at)
        {
            var rest = new ShipRoute
            {
                routeName = routeName + " (remaining)",
                travelMode = travelMode, sceneCoordinates = sceneCoordinates,
                speedProfileIndex = speedProfileIndex,
                startWaymark = startWaymark,
                endWaymark = endWaymark,
            };
            if (at.positionKm.x != 0d || at.positionKm.y != 0d || at.positionKm.z != 0d
                || !string.IsNullOrEmpty(at.waymarkName) || !string.IsNullOrEmpty(at.bodyId))
                rest.waypoints.Add(at);
            for (int i = Mathf.Max(0, fromIndex); i < waypoints.Count; i++) rest.waypoints.Add(waypoints[i]);
            return rest;
        }

        /// <summary>Drops one point. The shelf refuses to edit a route into something unflyable:
        /// two points is the smallest thing that is still a journey.</summary>
        public bool RemoveWaypointAt(int index)
        {
            if (index < 0 || index >= waypoints.Count) return false;
            if (waypoints.Count <= 2) return false;
            waypoints.RemoveAt(index);
            return true;
        }

        /// <summary>Flies the same road the other way. A recorded run is a road, and a road does
        /// not have a preferred direction — but the approach does, so the reversal keeps every
        /// point's own anchor and label and simply walks them backwards.</summary>
        public void Reverse()
        {
            if (waypoints.Count < 2) return;
            waypoints.Reverse();
        }

        /// <summary>Trailing capture points, keeping the opening leg(s) as authored.</summary>
        public void TrimTail(int keepFromEnd)
        {
            int keep = Mathf.Clamp(keepFromEnd, 1, waypoints.Count);
            while (waypoints.Count > keep) waypoints.RemoveAt(waypoints.Count - 1);
        }
    }

    /// <summary>One segment of an evaluated plan, ready to print.</summary>
    public readonly struct RouteLeg
    {
        public readonly int Index;
        public readonly float DistanceKm;
        public readonly float HoldSpeedMs;
        public readonly float Seconds;
        public readonly float WattHours;
        /// <summary>A body whose well this leg crosses or terminates in, or null.</summary>
        public readonly BodyInstance Well;
        /// <summary>True when the leg dips inside that body's atmosphere envelope.</summary>
        public readonly bool ThroughAtmosphere;
        /// <summary>True when the burn needed exceeds what the thrusters can hold this leg down.</summary>
        public readonly bool NeedsMultiBurn;

        /// <summary>How many push-and-brake cycles this leg costs, not one: a long leg is flown
        /// as a sequence of burns, and the plan has to bill for all of them.</summary>
        public readonly int BurnCount;

        /// <summary>Seconds the thrusters are actually lit on this leg (both directions, every burn).</summary>
        public readonly float BurnSeconds;

        /// <summary>Hydrogen the drive would burn on this leg, in litres, if the thrusters are
        /// hydrogen units. Electric thrusters report zero and never constrain the plan.</summary>
        public readonly float BurnHydrogenLitres;

        public RouteLeg(int index, float distanceKm, float holdSpeedMs, float seconds, float wattHours,
            BodyInstance well, bool throughAtmosphere, bool needsMultiBurn,
            int burnCount, float burnSeconds, float burnHydrogenLitres)
        {
            Index = index; DistanceKm = distanceKm; HoldSpeedMs = holdSpeedMs; Seconds = seconds;
            WattHours = wattHours; Well = well; ThroughAtmosphere = throughAtmosphere;
            NeedsMultiBurn = needsMultiBurn; BurnCount = burnCount; BurnSeconds = burnSeconds;
            BurnHydrogenLitres = burnHydrogenLitres;
        }
    }

    /// <summary>Why a route is a bad idea, if it is one. Codes, not prose, so the panel can colour them.</summary>
    public enum RouteWarningCode
    {
        PowerShortfall = 0,      // cannot hold the thrust the profile asks for
        ReserveShortfall = 1,    // the batteries cannot cover the plan plus the reserve margin
        GravityWell = 2,         // crosses a well this ship cannot climb out of on thrust alone
        AtmosphereHazard = 3,    // dips into an atmosphere the drive is not made for
        ArrivalUnsafe = 4,       // arrival sits inside the body's own envelope
        MultiBurnLeg = 5,        // fine, but it is not one burn: it is a flight
        NoThrust = 6,            // nothing pushing
        NoBodies = 7,            // no star map: nothing to measure against
        HydrogenShortfall = 8,   // the drive is thirsty and the ship is not
    }

    /// <summary>The whole evaluated plan.</summary>
    public readonly struct RoutePlan
    {
        public readonly bool IsValid;
        public readonly float TotalDistanceKm;
        public readonly float TotalSeconds;
        public readonly float PeakSpeedMs;
        public readonly float RequiredThrustNewtons;
        public readonly float AvailableThrustNewtons;
        public readonly float EnergyWattHours;
        public readonly float SustainedLoadWattHours;
        public readonly float StoredEnergyWh;
        public readonly float ReserveMargin01;
        /// <summary>Watts the drive pulls while it is actually lit — the grid's own thruster
        /// rating, summed. The coast phase bills at `SustainedLoadWattHours` instead.</summary>
        public readonly float ThrustPowerWatts;
        /// <summary>Litres of hydrogen the plan needs, when the drive is a hydrogen one.</summary>
        public readonly float HydrogenLitres;
        /// <summary>Watts the ship can put up the batteries' side, i.e. what its non-hydrogen
        /// thrusters are rated at. A hydrogen drive reports its capacity in litres instead, and
        /// this is what the reserve line shows for it.</summary>
        public readonly float AvailableThrustWatts;
        public readonly IReadOnlyList<RouteLeg> Legs;
        public readonly IReadOnlyList<RouteWarningCode> Warnings;

        public RoutePlan(bool isValid, float totalDistanceKm, float totalSeconds, float peakSpeedMs,
            float requiredThrustNewtons, float availableThrustNewtons, float energyWattHours,
            float sustainedLoadWattHours, float storedEnergyWh, float reserveMargin01,
            float thrustPowerWatts, float hydrogenLitres, float availableThrustWatts,
            IReadOnlyList<RouteLeg> legs, IReadOnlyList<RouteWarningCode> warnings)
        {
            IsValid = isValid; TotalDistanceKm = totalDistanceKm; TotalSeconds = totalSeconds;
            PeakSpeedMs = peakSpeedMs; RequiredThrustNewtons = requiredThrustNewtons;
            AvailableThrustNewtons = availableThrustNewtons; EnergyWattHours = energyWattHours;
            SustainedLoadWattHours = sustainedLoadWattHours; StoredEnergyWh = storedEnergyWh;
            ReserveMargin01 = reserveMargin01; ThrustPowerWatts = thrustPowerWatts;
            HydrogenLitres = hydrogenLitres; AvailableThrustWatts = availableThrustWatts;
            Legs = legs; Warnings = warnings;
        }

        public float TotalWattHours => EnergyWattHours + SustainedLoadWattHours;

        /// <summary>What the reserve is measured against, named honestly: a hydrogen drive has no
        /// battery line to read, and an electric one has no tank.</summary>
        public string StoreName
        {
            get
            {
                if (HydrogenLitres > 0.5f && EnergyWattHours <= 0.0001f) return "hydrogen";
                if (HydrogenLitres > 0.5f) return "batteries + hydrogen";
                return "batteries";
            }
        }

        /// <summary>Seconds of the trip the engine is lit, summed over the legs.</summary>
        public float TotalBurnSeconds
        {
            get
            {
                if (Legs == null) return 0f;
                float t = 0f;
                for (int i = 0; i < Legs.Count; i++) t += Legs[i].BurnSeconds;
                return t;
            }
        }
        public float EstimatedHours => TotalSeconds / 3600f;
        public bool HasWarnings => Warnings != null && Warnings.Count > 0;

        /// <summary>One readable line per warning. The panel prints these; the codes drive their colour.</summary>
        public static string TextFor(RouteWarningCode code) => code switch
        {
            RouteWarningCode.PowerShortfall     => "Thrusters cannot hold this profile — drop to economy or fit more thrust",
            RouteWarningCode.ReserveShortfall   => "Charge budget short of plan plus reserve",
            RouteWarningCode.GravityWell        => "Crosses a gravity well the ship cannot climb out of",
            RouteWarningCode.AtmosphereHazard   => "Leg dips inside an atmosphere envelope",
            RouteWarningCode.ArrivalUnsafe      => "Arrival point sits inside the body — raise the safe clearance",
            RouteWarningCode.MultiBurnLeg       => "Leg is longer than one burn — it will be flown, not jumped",
            RouteWarningCode.NoThrust           => "No usable thrust on this grid",
            RouteWarningCode.NoBodies           => "No star map loaded",
            RouteWarningCode.HydrogenShortfall  => "Not enough hydrogen in the ship for the burns this route needs",
            _ => "Unplannable",
        };
    }

    /// <summary>
    /// The rules: what a manoeuvre costs on top of the physics, and how much margin a pilot should
    /// insist on. Every number here is a design choice; nothing here is a range cap.
    /// </summary>
    public static class RouteRules
    {
        /// <summary>Fraction of the ideal two-burn energy spent on course correction, trim and
        /// the small nudges a real flight accumulates.</summary>
        public const float ManeuverCostFactor = 1.30f;

        /// <summary>How much of the power a thruster asks the grid for actually leaves as thrust.
        /// The grid bills the rating, so this is the share that becomes motion; the rest is heat and
        /// controller loss. Only the time cost of a burn is discounted by it — never the bill the
        /// batteries see, because the batteries are charged by the same grid that pays for the loss.</summary>
        public const float PropulsionEfficiency = 0.72f;

        /// <summary>Fallback for the ship's own standing draw while a route is flown, in watts.
        /// The evaluator prefers the grid's measured `PowerConsumed`; this figure only applies to a
        /// plan evaluated away from a live grid (a screen cache, a save-time audit).</summary>
        public const float SustainedLoadWatts = 380f;

        /// <summary>The share of stored energy a plan must leave behind, not spend. A route that
        /// uses the last watt is not a route, it is a rescue operation waiting to happen.</summary>
        public const float MinimumReserve01 = 0.25f;

        /// <summary>Arrival clearance, in grid cells, that the recorder adds above a body's own
        /// envelope when it snaps a waypoint to a destination.</summary>
        public const float ArrivalClearanceCells = 4f;

        /// <summary>What a planned run is allowed to average, in metres per second. Deliberately
        /// modest: at the ship's real terminal capability a planet is fifteen seconds away, there
        /// are no long-haul routes and no reason to own a route book. This one constant is what
        /// makes a 40 000 km passage a shift rather than a hop.</summary>
        /// <remarks>Physics note: the plan bills energy the way the game actually models it —
        /// the drive's own rated watts for as long as it is lit, and the grid's standing load for
        /// as long as the trip takes. Deriving watts from ½mv² instead would have priced a 50 000
        /// kg ship at millions of watt-hours a leg, because this game's batteries are arcade-sized
        /// on purpose and a realistic energy budget against them makes every route illegal.</remarks>
        public const float MaxPracticalCruiseMs = 150f;

        /// <summary>Speed a profile asks for, as a fraction of the ship's own comfortable holding
        /// speed (the speed at which its thrust can still brake inside one body radius).</summary>
        public static float ProfileFactor(RouteSpeedProfile profile) => profile switch
        {
            RouteSpeedProfile.Economy  => 0.45f,
            RouteSpeedProfile.Sprint   => 1.00f,
            _                          => 0.72f,
        };

        /// <summary>How long a ship may coast at its holding speed before the plan starts
        /// requiring burns instead of a single push: the crossover where one burn ends.</summary>
        public const float SingleBurnCoastSeconds = 900f;
    }
}
