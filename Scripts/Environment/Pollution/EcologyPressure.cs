// Assets/Scripts/VoxelEngine/Environment/Pollution/EcologyPressure.cs
//
// Reversible ecological consequences derived from the authoritative pollution
// field and the active body's native-life profile. This is intentionally
// stateless: saves continue to persist pollution, while ecology recovers as the
// sampled air and runoff recover.

using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Environment
{
    public readonly struct EcologyReading
    {
        public readonly float Airborne01;
        public readonly float Runoff01;
        public readonly float Pressure01;
        public readonly float Vitality01;
        public readonly float FloraSpawnMultiplier;
        public readonly float PassiveActivity01;
        public readonly float HostilePressureMultiplier;
        public readonly bool SupportsNativeEcology;
        public readonly bool SupportsLivestock;
        public readonly string Status;

        public EcologyReading(float airborne01, float runoff01, float pressure01,
            float vitality01, float floraSpawnMultiplier, float passiveActivity01,
            float hostilePressureMultiplier, bool supportsNativeEcology,
            bool supportsLivestock, string status)
        {
            Airborne01 = airborne01;
            Runoff01 = runoff01;
            Pressure01 = pressure01;
            Vitality01 = vitality01;
            FloraSpawnMultiplier = floraSpawnMultiplier;
            PassiveActivity01 = passiveActivity01;
            HostilePressureMultiplier = hostilePressureMultiplier;
            SupportsNativeEcology = supportsNativeEcology;
            SupportsLivestock = supportsLivestock;
            Status = status;
        }
    }

    /// <summary>
    /// Central planet-aware ecology evaluation. The same reading drives scatter,
    /// wildlife, hostile pressure and map telemetry so those systems cannot
    /// disagree about whether a location is healthy.
    /// </summary>
    public static class EcologyPressure
    {
        public static EcologyReading Sample(Vector3 worldPosition)
        {
            float air = PollutionService.SampleAirborne01(worldPosition);
            float runoff = PollutionService.SampleRunoff01(worldPosition);

            BodySettings settings = GravityProvider.ActiveBody != null
                ? GravityProvider.ActiveBody.settings
                : null;
            if (settings == null)
                return new EcologyReading(air, runoff, 0f, 0f, 0f, 0f, 1f,
                    supportsNativeEcology: false, supportsLivestock: false, status: "NO BIOSPHERE");

            PlanetSkyKind kind = PlanetSkyCatalog.ResolveKind(settings);
            float nativeAbundance = NativeAbundance(kind);
            float sensitivity = PollutionSensitivity(kind);
            bool supportsEcology = nativeAbundance > 0.001f;
            bool supportsLivestock = SupportsConventionalLivestock(settings, kind);

            // Runoff weighs slightly more because it directly reaches roots and
            // drinking water. Max() prevents one clean channel from hiding a
            // severe burden in the other.
            float blended = 1f - (1f - air * 0.78f) * (1f - runoff * 0.92f);
            float pressure = Mathf.Clamp01(Mathf.Max(blended, Mathf.Max(air, runoff) * 0.86f)
                * sensitivity);
            float vitality = supportsEcology ? Mathf.Clamp01(1f - pressure * 0.92f) : 0f;
            float flora = supportsEcology
                ? nativeAbundance * Mathf.Lerp(1f, 0.12f, pressure)
                : 0f;
            float passive = supportsLivestock
                ? Mathf.Clamp01(nativeAbundance * Mathf.Lerp(1f, 0.08f, pressure))
                : 0f;
            float hostile = Mathf.Lerp(1f, 2.15f, pressure);

            return new EcologyReading(air, runoff, pressure, vitality, flora, passive,
                hostile, supportsEcology, supportsLivestock,
                supportsEcology ? StatusFor(pressure) : "BARREN");
        }

        public static bool IsLivingFlora(GameObject prefab)
        {
            if (prefab == null) return false;
            string name = prefab.name.ToLowerInvariant();
            if (ContainsAny(name, "dead", "dry", "rock", "boulder", "stone", "ruin",
                    "scrap", "crystal", "spire", "basalt", "vent", "marble", "ice"))
                return false;
            return ContainsAny(name, "tree", "cactus", "shrub", "bush", "reed",
                "fern", "fung", "moss", "palm", "cypress", "flora", "plant");
        }

        private static float NativeAbundance(PlanetSkyKind kind)
        {
            switch (kind)
            {
                case PlanetSkyKind.Olympus: return 1.30f;
                case PlanetSkyKind.Ocean:
                case PlanetSkyKind.Water: return 1.15f;
                case PlanetSkyKind.Temperate: return 1f;
                case PlanetSkyKind.Acid: return 0.85f;
                case PlanetSkyKind.Pirate: return 0.48f;
                case PlanetSkyKind.Crystal: return 0.34f;
                case PlanetSkyKind.Ice: return 0.20f;
                default: return 0f;
            }
        }

        private static float PollutionSensitivity(PlanetSkyKind kind)
        {
            switch (kind)
            {
                case PlanetSkyKind.Acid: return 0.68f;
                case PlanetSkyKind.Crystal: return 0.76f;
                case PlanetSkyKind.Ice: return 0.88f;
                default: return 1f;
            }
        }

        private static bool SupportsConventionalLivestock(BodySettings settings, PlanetSkyKind kind)
        {
            if (settings == null || !settings.HasAtmosphere || settings.oxygenLevel < 0.45f)
                return false;
            if (settings.temperature < -18f || settings.temperature > 46f)
                return false;
            return kind == PlanetSkyKind.Temperate || kind == PlanetSkyKind.Ocean
                || kind == PlanetSkyKind.Water || kind == PlanetSkyKind.Olympus
                || kind == PlanetSkyKind.Pirate;
        }

        private static string StatusFor(float pressure)
        {
            if (pressure < 0.08f) return "HEALTHY";
            if (pressure < 0.25f) return "WATCH";
            if (pressure < 0.50f) return "STRESSED";
            if (pressure < 0.75f) return "DECLINING";
            return "COLLAPSED";
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
                if (value.Contains(needles[i])) return true;
            return false;
        }
    }
}
