// Assets/Scripts/VoxelEngine/GridSystem/GridSatelliteLab.cs
//
// Satellite Research Station: the block that unlocks orbital-only research.
//
// Some science genuinely cannot be done on the ground — long-baseline observation,
// microgravity work, whole-planet sensing. Rather than simulate all that, the game
// states the rule plainly: nodes flagged `requiresOrbitalLab` can only be researched
// at one of these, aboard a construct declared a SATELLITE and actually in orbit.
//
// That single rule is what turns the orbital programme from decoration into
// progression: to finish the tech tree you must build a satellite, get it up, and
// keep it there.

using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.GridSystem
{
    public class GridSatelliteLab : GridBlock
    {
        [Header("Satellite Lab")]
        [Tooltip("Power drawn while the lab is online and able to host orbital research.")]
        public float idleWatts = 180f;

        [Tooltip("Multiplier applied to research speed for nodes performed here.")]
        [Range(0.1f, 5f)] public float researchRateMultiplier = 1f;

        public override float PowerDraw => Enabled ? idleWatts : 0f;

        // ── Registry ─────────────────────────────────────────────────────────────
        private static readonly System.Collections.Generic.List<GridSatelliteLab> s_all = new();

        private void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
        private void OnDisable() { s_all.Remove(this); }

        /// <summary>
        /// Why this specific lab cannot host orbital research right now, or null if it can.
        /// Returned as text so the research UI can tell the player exactly what is missing
        /// rather than greying a node out with no explanation.
        /// </summary>
        public string BlockedReason()
        {
            if (!Enabled) return "Satellite lab is switched off.";
            if (Grid == null) return "Satellite lab is not attached to a construct.";
            if (!Grid.HasPower) return "Satellite lab has no power.";

            var identity = GridIdentity.Find(Grid);
            if (identity == null || !identity.IsSatellite)
                return "Host construct must be classified as a SATELLITE.";

            var rails = Grid.GetComponent<OrbitalRails>();
            if (rails == null || !rails.IsOnRails)
                return "Host satellite must be committed to a stable orbit.";

            return null;
        }

        /// <summary>True when this lab can host orbital-gated research.</summary>
        public bool IsOperational => BlockedReason() == null;

        // ── Global queries, used by the research system ──────────────────────────

        /// <summary>Any operational satellite lab anywhere in the world.</summary>
        public static GridSatelliteLab FindOperational()
        {
            for (int i = 0; i < s_all.Count; i++)
            {
                var lab = s_all[i];
                if (lab != null && lab.IsOperational) return lab;
            }
            return null;
        }

        /// <summary>True when the player owns at least one working orbital lab.</summary>
        public static bool AnyOperational() => FindOperational() != null;

        /// <summary>
        /// The most useful message to show when orbital research is unavailable: the reason
        /// from the closest-to-working lab, or a build prompt when none exist at all.
        /// </summary>
        public static string GlobalBlockedReason()
        {
            if (s_all.Count == 0)
                return "Requires a Satellite Research Station aboard a satellite in orbit.";

            // Surface the lab that is closest to working rather than an arbitrary one, so
            // the player is told the last thing standing in their way.
            string best = null;
            int bestRank = int.MaxValue;
            for (int i = 0; i < s_all.Count; i++)
            {
                var lab = s_all[i];
                if (lab == null) continue;
                string reason = lab.BlockedReason();
                if (reason == null) return null;

                int rank = RankReason(reason);
                if (rank < bestRank) { bestRank = rank; best = reason; }
            }
            return best ?? "Requires a Satellite Research Station aboard a satellite in orbit.";
        }

        /// <summary>Lower rank = closer to operational, so the player sees the final blocker.</summary>
        private static int RankReason(string reason)
        {
            if (reason.Contains("stable orbit")) return 0;
            if (reason.Contains("SATELLITE")) return 1;
            if (reason.Contains("no power")) return 2;
            if (reason.Contains("switched off")) return 3;
            return 4;
        }
    }
}
