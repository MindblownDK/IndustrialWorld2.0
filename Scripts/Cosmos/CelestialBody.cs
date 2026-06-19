// Assets/Scripts/VoxelEngine/Cosmos/CelestialBody.cs
//
// Runtime representation of a planet or moon. Owns its BodySettings + SphereGenParams, its
// transform (positioned by CosmicRegistry in cosmic space), and exposes the radial-gravity /
// "up" / atmosphere queries that the player, vehicles and atmosphere manager consume.
//
// Phase 1: standalone component you can drop on a body root for testing. Phase 2 wires the
// active body into VoxelWorld streaming + radial player reorientation.
using UnityEngine;
using VoxelEngine.Biomes;
using VoxelEngine.Generation;

namespace VoxelEngine.Cosmos
{
    public class CelestialBody : MonoBehaviour
    {
        [Tooltip("Designer settings (gravity, oxygen, temperature, ores, biomes, …).")]
        public BodySettings settings;

        [Tooltip("Filled from settings on Awake; tweak here for isolated testing.")]
        public SphereGenParams genParams;

        /// <summary>Is this the body the player currently calls home? Set by the world bootstrap.</summary>
        public bool IsActiveHome { get; set; }

        // ── Derived constants (set on Awake) ──────────────────────
        /// <summary>Surface radius in metres (mean, no terrain noise).</summary>
        public float SurfaceRadius => genParams.MeanSurfaceRadius;

        /// <summary>Sea-level radius in metres.</summary>
        public float SeaRadius => genParams.seaRadius;

        /// <summary>Surface gravity in m/s² (Earth ≈ 9.81).</summary>
        public float SurfaceGravity { get; private set; } = 9.81f;

        /// <summary>Display name of this body (from its settings).</summary>
        public string DisplayName => settings != null ? settings.bodyName : "Body";

        private void Awake()
        {
            ApplySettings();
        }

        /// <summary>Recompute gen params + derived physics from the current BodySettings.</summary>
        public void ApplySettings()
        {
            if (settings == null) return;

            // Convert designer-facing km radius → world metres. 1000 m/km.
            float radiusM = Mathf.Max(50f, settings.radiusKm * 1000f);
            genParams.seed                = settings.seed;
            genParams.radiusWorld         = radiusM;
            genParams.baseHeight          = settings.waterLevel;          // sea level offset
            genParams.seaRadius           = radiusM + settings.waterLevel;
            // Continent wavelength ≈ planet circumference / ~6 continents.
            float circumference           = Mathf.PI * 2f * radiusM;
            genParams.continentScaleDir   = (2f * Mathf.PI) / Mathf.Max(1f, circumference / 1200f);
            genParams.mountainScale       = settings.mountainScale;

            // Earth-like gravity baseline (9.81) scaled by the body's gravity multiplier.
            SurfaceGravity = 9.81f * Mathf.Clamp(settings.gravity, 0f, 5f);
        }

        // ── Spatial queries (body-relative) ───────────────────────
        /// <summary>Radial "up" at a world position — away from this body's core.</summary>
        public Vector3 UpAt(Vector3 worldPosition)
        {
            Vector3 d = worldPosition - transform.position;
            return d.sqrMagnitude < 1e-4f ? transform.up : d.normalized;
        }

        /// <summary>Gravity acceleration vector at a world position (points toward the core).</summary>
        public Vector3 GravityAt(Vector3 worldPosition)
        {
            Vector3 core = transform.position;
            Vector3 toCore = core - worldPosition;
            float dist = toCore.magnitude;
            if (dist < 1f) return Vector3.zero;

            // Inverse-square falloff above the surface; full strength inside the crust.
            float r = Mathf.Max(dist, SurfaceRadius);
            float g = SurfaceGravity * (SurfaceRadius * SurfaceRadius) / (r * r);
            return toCore / dist * g;
        }

        /// <summary>Altitude in metres above mean sea level (negative = below sea).</summary>
        public float AltitudeAt(Vector3 worldPosition)
            => (worldPosition - transform.position).magnitude - SeaRadius;

        /// <summary>Atmospheric density (kg/m³) at altitude — exponential falloff to vacuum.</summary>
        public float AirDensityAt(Vector3 worldPosition)
        {
            if (!settings.HasOxygen) return 0f;
            float alt = AltitudeAt(worldPosition);
            if (alt < 0f) alt = 0f;
            // Scale height ~8.5 km. Bodies with thin oxygen decay faster (handled via oxygenLevel).
            float scaleHeight = Mathf.Lerp(4000f, 9000f, settings.oxygenLevel);
            return Mathf.Exp(-alt / scaleHeight) * 1.225f * settings.oxygenLevel;
        }

        /// <summary>True if the position is above the Karman-ish line (space).</summary>
        public bool IsInSpace(Vector3 worldPosition)
            => AltitudeAt(worldPosition) > SurfaceRadius * 0.35f || AirDensityAt(worldPosition) < 0.08f;

        // ── Biome / climate filtering ─────────────────────────────
        /// <summary>
        /// Build the runtime biome list for this body: the designer whitelist, filtered by the
        /// body's mean temperature so e.g. a frozen moon can't spawn a desert. Returns a managed
        /// array the caller packs into a NativeArray<BiomeData> for Burst.
        /// </summary>
        public BiomeData[] BuildBiomeData(BiomeRegistry fallbackRegistry)
        {
            var src = settings.allowedBiomes;
            if (src == null || src.Length == 0)
            {
                src = fallbackRegistry != null ? fallbackRegistry.biomes.ToArray()
                                               : System.Array.Empty<BiomeDefinition>();
            }

            // Body temperature → climate-space 0..1 (map -40..40 °C onto 0.1..0.9).
            // (Reserved for future snow-line / biome weighting; currently the coarse gates below.)

            var list = new System.Collections.Generic.List<BiomeData>(src.Length);
            foreach (var def in src)
            {
                if (def == null) continue;
                // Exclude biomes whose temperature window is incompatible with the body's climate.
                if (settings.temperature < -5f && def.minTemperature > 0.55f) continue;   // cold body, hot biome
                if (settings.temperature > 35f && def.maxTemperature < 0.45f) continue;   // hot body, cold biome
                list.Add(BiomeData.FromDefinition(def));
            }
            if (list.Count == 0)
            {
                // Never leave a body with zero biomes — fall back to a plains-like default.
                list.Add(new BiomeData
                {
                    tempRange = new Unity.Mathematics.float2(0, 1),
                    humidRange = new Unity.Mathematics.float2(0, 1),
                    priority = 0,
                    heightOffset = 0, heightAmplitude = 12, heightFrequency = 0.02f, ridgedness = 0,
                    surfaceMat = (byte)Materials.MaterialId.Clay, surfaceDepth = 1,
                    subsurfaceMat = (byte)Materials.MaterialId.Clay, subsurfaceDepth = 4,
                    allowBeach = 1, isOceanic = 0,
                });
            }
            return list.ToArray();
        }

        /// <summary>Prebuilt ore layers for this body (common + rare + specials).</summary>
        public OreLayer[] BuildOreLayers()
            => settings != null ? settings.BuildOreLayers().ToArray() : System.Array.Empty<OreLayer>();
    }
}
