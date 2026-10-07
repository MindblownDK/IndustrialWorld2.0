using UnityEngine;
using VoxelEngine.Cosmos;

namespace IndustrialWorld.Simulation
{
    /// <summary>Designer-owned habitat and reversible pollution response; never persisted in saves.</summary>
    public sealed class EcologyProfile : ScriptableObject
    {
        [Min(0f)] public float nativeAbundance = 1f;
        [Min(0f)] public float pollutionSensitivity = 1f;
        public bool allowsConventionalLivestock = true;
        [Range(0f, 1f)] public float minimumLivestockOxygen = 0.45f;
        public float minimumLivestockTemperature = -18f;
        public float maximumLivestockTemperature = 46f;
        public bool allowsGhoulScouts = true;
        public bool ghoulsRequireAtmosphere = true;

        public bool SupportsLivestock(BodySettings body)
        {
            return allowsConventionalLivestock && body != null && body.HasAtmosphere
                && body.oxygenLevel >= minimumLivestockOxygen
                && body.temperature >= minimumLivestockTemperature
                && body.temperature <= maximumLivestockTemperature;
        }
    }

    /// <summary>Fixed resource identities avoid dependence on asset names or loading order.</summary>
    public static class EcologyProfiles
    {
        private static readonly EcologyProfile[] _profiles = new EcologyProfile[14];
        private static readonly bool[] _loaded = new bool[14];

        public static EcologyProfile Resolve(PlanetSkyKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= _profiles.Length) return null;
            if (!_loaded[index])
            {
                _profiles[index] = Resources.Load<EcologyProfile>("Ecology/Profile_" + kind);
                _loaded[index] = true;
            }
            return _profiles[index];
        }

        public static bool AllowsGhoul(BodySettings body)
        {
            if (body == null) return false;
            var profile = Resolve(PlanetSkyCatalog.ResolveKind(body));
            return profile != null
                ? profile.allowsGhoulScouts && (!profile.ghoulsRequireAtmosphere || body.HasAtmosphere)
                : body.HasAtmosphere;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            System.Array.Clear(_profiles, 0, _profiles.Length);
            System.Array.Clear(_loaded, 0, _loaded.Length);
        }
    }
}
