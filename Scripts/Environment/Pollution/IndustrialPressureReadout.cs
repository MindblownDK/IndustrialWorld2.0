// Assets/Scripts/VoxelEngine/Environment/Pollution/IndustrialPressureReadout.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        PLAYER-FACING INDUSTRIAL PRESSURE (17.30.0-dev)           ║
// ║                                                                  ║
// ║  The industrial threat tiers (17.18.0-dev) decide whether the    ║
// ║  ecology answers a dirty district with scouts, packs, an         ║
// ║  organized wave or a siege - and from the player's side that     ║
// ║  decision was invisible: two numbers, a machine panel that never ║
// ║  mentioned them and a map that showed the smog but not the       ║
// ║  history behind it. The readings themselves all existed          ║
// ║  (EcologyPressure's pressure, PollutionService's climate         ║
// ║  channel, PollutionThreat's gates); this file is the one place   ║
// ║  that turns them into words.                                     ║
// ║                                                                  ║
// ║  It READS, it never decides. Every rung named here comes from    ║
// ║  PollutionThreat.ResolveTier - the same call the spawner makes -  ║
// ║  so the panel and the map can never tell the player two          ║
// ║  different versions of the same rung. What the helper cannot     ║
// ║  read (the spawner's own recruitment heat, which one outlet has  ║
// ║  to hold for about four minutes) is stated in words rather than  ║
// ║  guessed at from private state.                                  ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Environment
{
    /// <summary>
    /// One translation of the two readings the industrial threat tiers are made from -
    /// the ecological pressure at an outlet and the residual climate load that outlet has
    /// fed - into the words a player can act on. Used by the machine emissions card and by
    /// the logistics map's pressure section, so both speak the same language.
    /// </summary>
    public static class IndustrialPressureReadout
    {
        /// <summary>
        /// Band name for the residual channel, named after the rungs it sits between:
        /// WARMING is short of the organized-wave gate, LOADED has reached it, SATURATED
        /// has reached the siege gate. Nothing here re-tunes a threshold - every boundary
        /// is read from <see cref="PollutionThreat"/>.
        /// </summary>
        public static string HistoryBand(float residual01)
        {
            float residual = Mathf.Clamp01(residual01);
            if (residual < 0.05f) return "CLEAN";
            if (residual < 0.15f) return "LIGHT";
            if (residual < PollutionThreat.WaveResidual01) return "WARMING";
            if (residual < PollutionThreat.SiegeResidual01) return "LOADED";
            return "SATURATED";
        }

        /// <summary>
        /// The rung these readings earn, resolved by the SAME call the spawner makes.
        /// <paramref name="live"/> is the caller's answer to "is an outlet emitting at this
        /// sample point right now" - the spawner's `hasRecruitedSource`. Passing false is
        /// what makes an idle machine or a clean map read QUIET instead of claiming the
        /// packs rung, since `ResolveTier` can only ever answer Packs, Wave or Siege once a
        /// source exists (the lone-scout rung below it is the spawner's heat gate releasing
        /// the source, which no reading can express).
        /// </summary>
        public static IndustrialThreatTier TierFor(bool live, float pressure01, float residual01)
            => PollutionThreat.ResolveTier(live, Mathf.Clamp01(pressure01), Mathf.Clamp01(residual01));

        /// <summary>
        /// The rung's player-facing name: QUIET when nothing is emitting, otherwise the
        /// tier's own label (PACKS, ORGANIZED WAVE, SIEGE).
        /// </summary>
        public static string TierLabel(bool live, float pressure01, float residual01)
        {
            IndustrialThreatTier tier = TierFor(live, pressure01, residual01);
            return tier == IndustrialThreatTier.Quiet ? "QUIET" : PollutionThreat.TierLabel(tier);
        }

        /// <summary>
        /// What the next rung still needs, in the player's own units (percent of the gate).
        /// Names only the inputs that are actually short, so a player who is one number away
        /// reads one instruction - and says plainly when nothing higher is earned.
        /// </summary>
        public static string NextRequirement(bool live, float pressure01, float residual01)
        {
            if (!live)
                return "Nothing is emitting here: a live outlet is what starts the count.";

            float pressure = Mathf.Clamp01(pressure01);
            float residual = Mathf.Clamp01(residual01);
            if (TierFor(true, pressure, residual) == IndustrialThreatTier.Siege)
                return "SIEGE is the top rung - nothing higher is earned here.";

            bool waveReady = residual >= PollutionThreat.WaveResidual01
                && pressure >= PollutionThreat.WavePressure01;
            string target = waveReady
                ? PollutionThreat.TierLabel(IndustrialThreatTier.Siege)
                : PollutionThreat.TierLabel(IndustrialThreatTier.Wave);
            float wantResidual = waveReady ? PollutionThreat.SiegeResidual01 : PollutionThreat.WaveResidual01;
            float wantPressure = waveReady ? PollutionThreat.SiegePressure01 : PollutionThreat.WavePressure01;

            float residualShort = wantResidual - residual;
            float pressureShort = wantPressure - pressure;
            string need = string.Empty;
            if (residualShort > 0.001f)
                need = $"history {wantResidual * 100f:0}% (short {residualShort * 100f:0}%)";
            if (pressureShort > 0.001f)
                need += need.Length > 0
                    ? $", pressure {wantPressure * 100f:0}% (short {pressureShort * 100f:0}%)"
                    : $"pressure {wantPressure * 100f:0}% (short {pressureShort * 100f:0}%)";
            return need.Length > 0
                ? $"{target} needs {need}."
                : $"{target} is earned as soon as one outlet runs hot here.";
        }

        /// <summary>
        /// The gate the readings cannot see: an organized answer needs the district's own
        /// activity to hold, not a furnace lit a minute ago. Taken from the spawner's own
        /// recruitment-heat ramp (240 s to full, 90 s to bleed off) and stated as the two
        /// things a player actually experiences - the four minutes, and the smaller
        /// presence that answers until they have passed.
        /// </summary>
        public const string ActivityNote =
            "One outlet has to run about four minutes before the tiers apply here; until then "
            + "this district answers with its ordinary, smaller presence.";

        /// <summary>
        /// One line for a survey surface: the rung, then both readings in percent.
        /// Example: "ORGANIZED WAVE   -   HISTORY 41% (LOADED)   -   PRESSURE 34%".
        /// </summary>
        public static string Summary(bool live, float pressure01, float residual01)
            => $"{TierLabel(live, pressure01, residual01)}   ·   "
             + $"HISTORY {Mathf.Clamp01(residual01) * 100f:0}% ({HistoryBand(residual01)})   ·   "
             + $"PRESSURE {Mathf.Clamp01(pressure01) * 100f:0}%";
    }
}
