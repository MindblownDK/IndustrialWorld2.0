// Assets/Scripts/VoxelEngine/Simulation/OfflineProduction.cs
//
// PRODUCTION THAT SURVIVES YOU LEAVING.
//
// THE PROBLEM
// Every producing machine in the game ticks in Update: the deep core extractor, the
// biofarm, the livestock pen, the cargo pad. A GameObject that is unloaded - or simply
// on a planet the player has flown away from - stops ticking. So a base on a second
// world produces literally nothing the moment you leave it.
//
// That quietly undermines the whole interplanetary layer. 11.24.0 shipped cargo pads so
// a second planet could feed the first, but there was nothing to feed it WITH: the mine
// that was supposed to fill the pad was frozen the instant the player left orbit.
//
// CATCH-UP, NOT BACKGROUND SIMULATION
// The obvious fix - keep ticking unloaded machines - is the wrong one. It costs CPU
// forever, it scales with everything the player has ever built, and it means the game is
// running thousands of Updates for things nobody can see.
//
// Instead a machine records WHEN it was last serviced, and on waking asks how much
// simulated time passed. Producing N seconds' worth in one step gives the same result
// for a constant-rate machine as N seconds of ticking, at a fraction of the cost and
// with no per-frame work at all while away.
//
// This is the same principle already used three times in this codebase: satellites ride
// analytic orbits, trains walk a graph, deep ore nodes are derived. Anything the player
// expects to keep working while they are elsewhere must not depend on being simulated.
//
// WHY THE COSMIC CLOCK
// `CosmicRegistry.SimulationSeconds` is the one clock that is authoritative and SAVED,
// so it advances across a session boundary. `Time.time` resets on load and would hand a
// player a free harvest, or none at all, depending on load order.
//
// HONEST LIMITS - stated rather than hidden:
//   * Offline output is deliberately RATE-LIMITED below live output. A base you are
//     standing in should always be the better base, or the optimal play becomes logging
//     out, which is a miserable design.
//   * A machine that needs an input it cannot verify offline (power, feed, water) claims
//     a conservative fraction rather than assuming it ran perfectly.
//   * Catch-up is capped. A player returning after a very long absence gets a large but
//     bounded payout, not an infinite one.

using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Tracks elapsed simulated time for a machine that may be unloaded, and converts it
    /// into a bounded amount of catch-up work.
    ///
    /// A struct rather than a component: it is state a machine owns, not a behaviour, and
    /// making it a component would double the GameObject count of every base.
    /// </summary>
    [System.Serializable]
    public struct OfflineClock
    {
        /// <summary>Cosmic time the owner was last serviced. Negative means "never".</summary>
        public double lastServicedAt;

        /// <summary>
        /// Longest absence that still pays out, in simulated seconds. Twelve hours: long
        /// enough that a real break is rewarded, short enough that an idle world cannot
        /// bank an unbounded harvest.
        /// </summary>
        public const double MaxCatchUpSeconds = 12d * 3600d;

        /// <summary>
        /// Offline work is worth less than live work. A base the player is actually
        /// standing in must always out-produce one they abandoned, or the best strategy
        /// becomes logging out.
        /// </summary>
        public const float OfflineEfficiency = 0.45f;

        /// <summary>The authoritative, save-persisted clock. Falls back to play time.</summary>
        public static double Now
        {
            get
            {
                var registry = CosmicRegistry.Instance;
                return registry != null ? registry.SimulationSeconds : Time.timeAsDouble;
            }
        }

        /// <summary>Stamps the clock to now. Call when a machine is serviced or first placed.</summary>
        public void Touch() => lastServicedAt = Now;

        /// <summary>
        /// Seconds of catch-up owed, already clamped and efficiency-scaled, or zero when
        /// there is nothing to claim. Stamps the clock as a side effect, so a caller can
        /// never accidentally claim the same window twice.
        /// </summary>
        public float Claim()
        {
            double now = Now;

            // First ever call: start the clock rather than paying out for all of history.
            if (lastServicedAt <= 0d || lastServicedAt > now)
            {
                lastServicedAt = now;
                return 0f;
            }

            double elapsed = now - lastServicedAt;
            lastServicedAt = now;

            // Below a second is live ticking, not an absence - let Update handle it.
            if (elapsed < 1d) return 0f;

            double granted = System.Math.Min(elapsed, MaxCatchUpSeconds);
            return (float)(granted * OfflineEfficiency);
        }

        /// <summary>Seconds since last service, unscaled - for a readout, not for granting.</summary>
        public double ElapsedRaw => lastServicedAt <= 0d ? 0d : System.Math.Max(0d, Now - lastServicedAt);

        /// <summary>True when enough time has passed to be worth reporting to the player.</summary>
        public bool HasPendingAbsence => ElapsedRaw >= 60d;

        /// <summary>Human-readable absence, for a machine console.</summary>
        public string AbsenceLabel
        {
            get
            {
                double s = ElapsedRaw;
                if (s < 60d) return "just now";
                if (s < 3600d) return $"{s / 60d:0} min ago";
                if (s < MaxCatchUpSeconds) return $"{s / 3600d:0.#} h ago";
                return "over 12 h ago (capped)";
            }
        }
    }
}
