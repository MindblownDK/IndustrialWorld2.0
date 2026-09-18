// Assets/Scripts/VoxelEngine/Transport/CargoFlightRegistry.cs
//
// Every cargo shipment currently between worlds.
//
// WHY A SCENE-WIDE REGISTRY RATHER THAN STATE ON THE PAD
// A flight outlives its endpoints' loaded state. The origin pad is usually on a planet
// the player has left, and the destination pad is on one they have not arrived at yet -
// so if a flight lived on either pad it would stop being ticked exactly when it matters.
// Holding flights centrally means a shipment completes regardless of what is loaded,
// which is the entire promise of unattended freight.
//
// This is the same reasoning already applied three times in this codebase: trains walk a
// graph, satellites ride analytic rails, and deep ore nodes are derived. Anything the
// player expects to keep working while they are elsewhere must not depend on being
// simulated.
//
// DELIVERY IS NEVER LOST
// A flight whose destination pad is missing on arrival holds rather than evaporating.
// Cargo that vanished because the player demolished a pad mid-flight would be an
// invisible, unexplainable loss - the worst kind of bug in a logistics system.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Transport
{
    public static class CargoFlightRegistry
    {
        private static readonly List<CargoFlight> _flights = new(16);

        public static IReadOnlyList<CargoFlight> Flights => _flights;
        public static int FlightCount => _flights.Count;

        /// <summary>Guards against two pads ticking the registry in the same frame.</summary>
        private static int _lastTickFrame = -1;

        /// <summary>
        /// Cosmic time the registry last advanced. Flights are stepped against the SAVED
        /// cosmic clock rather than frame delta, so a shipment keeps closing the distance
        /// while both its endpoints are unloaded - or across a save and reload.
        /// </summary>
        private static double _lastCosmicTime = -1d;

        /// <summary>
        /// Advances every flight using elapsed COSMIC seconds.
        ///
        /// This exists because the old model was quietly broken: the registry was only
        /// ticked from a loaded pad's Update, so flying away from both ends of a route
        /// froze the cargo in transit indefinitely. Driving it from the cosmic clock means
        /// the delivery lands whether anyone is watching or not - which is the entire
        /// promise of unattended freight.
        /// </summary>
        public static void TickCosmic()
        {
            if (_lastTickFrame == Time.frameCount) return;
            _lastTickFrame = Time.frameCount;
            if (_flights.Count == 0) { _lastCosmicTime = CosmicNow; return; }

            double now = CosmicNow;
            if (_lastCosmicTime < 0d || now < _lastCosmicTime) { _lastCosmicTime = now; return; }

            double elapsed = now - _lastCosmicTime;
            _lastCosmicTime = now;
            if (elapsed <= 0d) return;

            // A very long absence should not let a flight overshoot into nonsense; the
            // longest useful step is the longest possible flight.
            Advance((float)System.Math.Min(elapsed, 3600d));
        }

        private static double CosmicNow
        {
            get
            {
                var registry = CosmicRegistry.Instance;
                return registry != null ? registry.SimulationSeconds : Time.timeAsDouble;
            }
        }

        /// <summary>Resets the clock reference, so a fresh load does not bank a huge step.</summary>
        public static void ResyncClock() => _lastCosmicTime = CosmicNow;

        public static void Launch(CargoFlight flight)
        {
            if (flight == null || flight.Item == null || flight.Count <= 0) return;
            _flights.Add(flight);
            Debug.Log($"[Cargo] {flight.OriginPad} launched {flight.Count} x " +
                      $"{flight.Item.displayName} to {flight.DestinationPad} " +
                      $"({flight.Total:0} s).");
        }

        /// <summary>
        /// Advances every flight. Called from each pad's Update, but guarded so the
        /// registry advances exactly once per frame no matter how many pads exist.
        /// </summary>
        /// <summary>
        /// Legacy per-frame entry point, kept so a loaded pad still drives deliveries at
        /// frame resolution. Defers to the cosmic path, which is the authoritative one.
        /// </summary>
        public static void Tick(float deltaTime) => TickCosmic();

        private static void Advance(float deltaTime)
        {
            if (_flights.Count == 0) return;

            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                var flight = _flights[i];
                if (flight == null) { _flights.RemoveAt(i); continue; }

                flight.Remaining -= deltaTime;
                if (flight.Remaining > 0f) continue;

                var pad = CargoLaunchPad.Find(flight.DestinationPad);
                if (pad == null)
                {
                    // Hold rather than discard. The destination may simply be in an
                    // unloaded chunk, and destroying cargo for that would be silent
                    // theft. Re-checked once a second until the pad turns up.
                    flight.Remaining = 1f;
                    continue;
                }

                int stored = pad.Deliver(flight.Item, flight.Count);
                if (stored <= 0)
                {
                    // Destination full: circle rather than dump. Same rule - a full hold
                    // costs the player time, never cargo.
                    flight.Remaining = 5f;
                    continue;
                }

                if (stored < flight.Count)
                {
                    // Partial delivery: keep the remainder in flight instead of losing it.
                    flight.Count -= stored;
                    flight.Remaining = 5f;
                    continue;
                }

                Debug.Log($"[Cargo] {flight.DestinationPad} received {stored} x " +
                          $"{flight.Item.displayName}.");
                _flights.RemoveAt(i);
            }
        }

        public static int CountInboundTo(string padName)
        {
            if (string.IsNullOrEmpty(padName)) return 0;
            int count = 0;
            for (int i = 0; i < _flights.Count; i++)
                if (_flights[i] != null && _flights[i].DestinationPad == padName) count++;
            return count;
        }

        public static void Clear() => _flights.Clear();

        /// <summary>
        /// Flight time between two bodies, in seconds.
        ///
        /// Derived from the real distance between them, so shipping to a moon is quick
        /// and shipping across the system is a commitment. Clamped at both ends: a floor
        /// so a launch is never instant, and a ceiling so an unlucky planetary alignment
        /// cannot strand cargo for an entire session.
        /// </summary>
        public static float EstimateFlightSeconds(string fromBody, string toBody)
        {
            const float sameBodySeconds = 20f;
            const float minSeconds = 30f;
            const float maxSeconds = 420f;

            if (string.IsNullOrEmpty(fromBody) || string.IsNullOrEmpty(toBody)) return minSeconds;
            if (fromBody == toBody) return sameBodySeconds;

            var registry = CosmicRegistry.Instance;
            if (registry == null) return minSeconds;

            BodyInstance a = FindBody(registry, fromBody);
            BodyInstance b = FindBody(registry, toBody);
            if (a == null || b == null) return minSeconds;

            var pa = registry.CosmicPositionOf(a);
            var pb = registry.CosmicPositionOf(b);

            double dx = pa.x - pb.x, dy = pa.y - pb.y, dz = pa.z - pb.z;
            double distanceKm = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);

            // Square root rather than linear: distances between planets differ by orders
            // of magnitude, and a linear mapping would make the far ones unusable while
            // the near ones were trivial.
            float seconds = (float)(System.Math.Sqrt(distanceKm) * 0.06d);
            return Mathf.Clamp(seconds, minSeconds, maxSeconds);
        }

        private static BodyInstance FindBody(CosmicRegistry registry, string name)
        {
            var bodies = registry.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                var body = bodies[i];
                if (body != null && body.DisplayName == name) return body;
            }
            return null;
        }

        // ── Persistence ──────────────────────────────────────────────────────────

        /// <summary>Snapshot for the save layer.</summary>
        public static List<CargoFlight> Snapshot() => new(_flights);

        /// <summary>Restores flights from a save.</summary>
        public static void Restore(IEnumerable<CargoFlight> flights)
        {
            _flights.Clear();
            if (flights == null) return;
            foreach (var flight in flights)
            {
                if (flight == null || flight.Item == null || flight.Count <= 0) continue;
                _flights.Add(flight);
            }
        }
    }
}
