// Assets/Scripts/VoxelEngine/GridSystem/AtmosphereManager.cs
//
// Authoritative atmosphere / upper-atmosphere / vacuum query layer. Flight,
// life support, grid thrusters, HUD, and visual systems all ask this one model
// so high altitude cannot disagree with the planet profile.

using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.GridSystem
{
    public enum AtmosphereBand
    {
        DenseAir = 0,
        UpperAtmosphere = 1,
        Vacuum = 2,
    }

    /// <summary>Allocation-free snapshot of the local atmospheric state.</summary>
    public readonly struct AtmosphereSample
    {
        public readonly float Altitude;
        public readonly float AirDensity;
        public readonly float SurfaceDensity;
        public readonly float Density01;
        public readonly float AtmosphereHeight;
        public readonly bool HasAtmosphere;
        public readonly bool IsInSpace;
        public readonly AtmosphereBand Band;

        internal AtmosphereSample(float altitude, float airDensity, float surfaceDensity,
            float atmosphereHeight, bool hasAtmosphere, bool isInSpace)
        {
            Altitude = altitude;
            AirDensity = Mathf.Max(0f, airDensity);
            SurfaceDensity = Mathf.Max(0f, surfaceDensity);
            // Flight/visual authority is referenced to Earth-like sea-level density,
            // not this planet's own sea-level profile. A 2%-dense Mars atmosphere must
            // not accidentally make atmospheric engines produce 100% thrust.
            Density01 = Mathf.Clamp01(AirDensity / AtmosphereManager.SeaLevelAirDensity);
            AtmosphereHeight = Mathf.Max(0f, atmosphereHeight);
            HasAtmosphere = hasAtmosphere;
            IsInSpace = isInSpace;
            Band = isInSpace
                ? AtmosphereBand.Vacuum
                : Density01 <= 0.35f ? AtmosphereBand.UpperAtmosphere : AtmosphereBand.DenseAir;
        }

        public string Label => Band switch
        {
            AtmosphereBand.DenseAir => "ATMOSPHERE",
            AtmosphereBand.UpperAtmosphere => "UPPER ATMOSPHERE",
            _ => "VACUUM",
        };
    }

    public static class AtmosphereManager
    {
        /// <summary>Earth-like sea-level density in kg/m³, retained for flat-world fallback compatibility.</summary>
        public const float SeaLevelAirDensity = 1.225f;
        /// <summary>Below this density the environment is treated as vacuum for flight/space state.</summary>
        public const float VacuumDensityThreshold = 0.02f;

        private const float FlatAtmosphereHeight = 24000f;
        private const float FlatAtmosphereScaleHeight = 5000f;

        /// <summary>Single authoritative environment sample at a world position.</summary>
        public static AtmosphereSample Sample(Vector3 worldPosition)
        {
            var body = GravityProvider.ActiveBody;

            // Deep space: the cosmos is active but no body holds the player — true vacuum,
            // regardless of scene Y (the old flat-world fallback would have reported air
            // at ground level, which is wrong a thousand kilometres from any planet).
            if (body == null && CosmicRegistry.Instance != null && CosmicRegistry.Instance.IsReady)
            {
                return new AtmosphereSample(float.PositiveInfinity, 0f, 0f, 0f, false, true);
            }

            if (body != null)
            {
                float altitude = body.AltitudeAt(worldPosition);
                float surfaceDensity = body.SurfaceAirDensity;
                float atmosphereHeight = body.AtmosphereHeight;
                bool hasAtmosphere = body.HasAtmosphere;
                float density = hasAtmosphere ? body.AirDensityAt(worldPosition) : 0f;
                bool inSpace = !hasAtmosphere
                    || altitude >= atmosphereHeight
                    || density < VacuumDensityThreshold;
                return new AtmosphereSample(altitude, density, surfaceDensity,
                    atmosphereHeight, hasAtmosphere, inSpace);
            }

            // Flat-world fallback preserves the old world-height model while making its
            // top-of-air and vacuum threshold explicit and internally consistent.
            float flatAltitude = Mathf.Max(0f, worldPosition.y);
            float flatDensity = flatAltitude >= FlatAtmosphereHeight
                ? 0f
                : Mathf.Exp(-flatAltitude / FlatAtmosphereScaleHeight) * SeaLevelAirDensity;
            bool flatSpace = flatAltitude >= FlatAtmosphereHeight || flatDensity < VacuumDensityThreshold;
            return new AtmosphereSample(flatAltitude, flatDensity, SeaLevelAirDensity,
                FlatAtmosphereHeight, true, flatSpace);
        }

        public static float GetAirDensity(Vector3 worldPosition) => Sample(worldPosition).AirDensity;
        public static float GetDensity01(Vector3 worldPosition) => Sample(worldPosition).Density01;
        public static float GetAltitude(Vector3 worldPosition) => Sample(worldPosition).Altitude;

        public static float GetGravityMultiplier(Vector3 worldPosition)
        {
            // Deep space: no gravity at all for legacy flat-world-style callers.
            if (GravityProvider.IsDeepSpace) return 0f;

            // When a celestial body is active, GridEntity uses GravityProvider directly.
            // Keep this ratio for legacy callers that only need the flat-world style multiplier.
            if (GravityProvider.ActiveBody != null)
            {
                float surfaceG = GravityProvider.ActiveBody.SurfaceGravity;
                return surfaceG > 0.01f
                    ? GravityProvider.GetGravity(worldPosition).magnitude / surfaceG
                    : 0f;
            }

            float height = worldPosition.y;
            if (height > 12000f) return 0.08f;
            return Mathf.Clamp01(1f - (height / 14000f));
        }

        public static bool IsInSpace(Vector3 worldPosition) => Sample(worldPosition).IsInSpace;
    }
}
