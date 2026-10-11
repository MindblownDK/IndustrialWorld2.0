// Assets/Scripts/VoxelEngine/Environment/Pollution/PollutionThreat.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║             INDUSTRIAL THREAT TIERS (17.18.0-dev)                ║
// ║  What PollutionScentRules deliberately left undecided: the      ║
// ║  organized responses. Scent rules answer "how many scouts does  ║
// ║  one active outlet recruit right now"; these rules answer "has  ║
// ║  the player's industrial history earned something organized".   ║
// ║                                                                  ║
// ║  Inputs are all existing readings: the spawner's recruitment     ║
// ║  heat (sustained activity, 17.17.2-dev), the instantaneous      ║
// ║  cell pressure at the outlet, and the residual climate load -   ║
// ║  the slow-decay channel every emission profile has always fed   ║
// ║  (about four hours of half-life, persisted in the pollution     ║
// ║  sidecar) that nothing ever read until now.                     ║
// ║                                                                  ║
// ║  Stateless by law: no history is invented here, everything is   ║
// ║  sampled from the simulation or owned by the spawner.           ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Environment
{
    /// <summary>How hard the ecology is currently answering a player's industry.</summary>
    public enum IndustrialThreatTier
    {
        /// <summary>No active outlet recruited at all: the ordinary ambient spawn
        /// cadence near players, untouched by industry.</summary>
        Quiet = 0,
        /// <summary>An outlet recruits but sustained-activity heat is not full yet:
        /// a lone scout may answer. The spawner's heat gate resolves this tier by
        /// releasing the source, so tier rules never need to return it.</summary>
        Scouts = 1,
        /// <summary>Packs, ambushes and raids: full recruitment heat, ordinary pressure.</summary>
        Packs = 2,
        /// <summary>Organized wave: a coordinated multi-ghoul arrival led by an elite.</summary>
        Wave = 3,
        /// <summary>Siege: a brute that walks past the player to dismantle the factory.</summary>
        Siege = 4,
    }

    public static class PollutionThreat
    {
        // Residual climate load (0..1, slow history channel) required for the
        // organized tiers. 0.30 is roughly an hour of a small factory cluster -
        // or a single furnace left burning for a long evening.
        public const float WaveResidual01 = 0.30f;
        public const float SiegeResidual01 = 0.55f;

        // Instantaneous cell pressure at the outlet required alongside the
        // history: a wave needs a genuinely dirty district, not old smoke.
        public const float WavePressure01 = 0.30f;
        public const float SiegePressure01 = 0.45f;

        // Wave shape. The ecological alive-cap always binds on top of these.
        public const int MinWaveSlots = 3;
        public const int MaxWaveSize = 6;
        public const int WaveAmbushers = 2;
        public const int SiegeEscortSize = 3;

        // Organized events keep their own cadence so a qualifying district is
        // under siege pressure, not under a spawn-loop machine gun.
        public const float WaveCooldownSeconds = 180f;
        public const float SiegeCooldownSeconds = 300f;

        /// <summary>
        /// Resolves the tier from live readings. The caller (the spawner) has
        /// already applied the recruitment-heat gate, so a recruited source here
        /// means sustained activity; heat below full never reaches this method
        /// with a source attached.
        /// </summary>
        public static IndustrialThreatTier ResolveTier(bool hasRecruitedSource,
            float sourcePressure01, float residual01)
        {
            if (!hasRecruitedSource) return IndustrialThreatTier.Quiet;
            float pressure = Mathf.Clamp01(sourcePressure01);
            float residual = Mathf.Clamp01(residual01);
            if (residual >= SiegeResidual01 && pressure >= SiegePressure01)
                return IndustrialThreatTier.Siege;
            if (residual >= WaveResidual01 && pressure >= WavePressure01)
                return IndustrialThreatTier.Wave;
            return IndustrialThreatTier.Packs;
        }

        /// <summary>Wave body count (elite included) grown by the residual load,
        /// before the ecological cap clamps it.</summary>
        public static int WaveSize(float residual01)
        {
            return Mathf.Clamp(4 + Mathf.FloorToInt(Mathf.Clamp01(residual01) * 3f), 4, MaxWaveSize);
        }

        /// <summary>Player-facing tier name for notices and readouts.</summary>
        public static string TierLabel(IndustrialThreatTier tier)
        {
            switch (tier)
            {
                case IndustrialThreatTier.Siege: return "SIEGE";
                case IndustrialThreatTier.Wave: return "ORGANIZED WAVE";
                case IndustrialThreatTier.Packs: return "PACKS";
                default: return "SCOUTS";
            }
        }
    }
}
