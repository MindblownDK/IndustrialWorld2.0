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

        // Session seed is runtime-only. It must never mutate the shared PlanetTemplate asset,
        // and it must survive repeated ApplySettings calls during bootstrap/streamer startup.
        [System.NonSerialized] private bool _hasRuntimeSeedOverride;
        [System.NonSerialized] private int _runtimeSeedOverride;

        public void SetRuntimeSeedOverride(int seed)
        {
            _runtimeSeedOverride = seed;
            _hasRuntimeSeedOverride = true;
        }

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
            genParams.seed                = _hasRuntimeSeedOverride ? _runtimeSeedOverride : settings.seed;
            genParams.radiusWorld         = radiusM;
            // Terrain height vs sea level: mean terrain should be SLIGHTLY above sea level so
            // we get a good mix of land (~60%) and ocean (~40%). Too high (+12) = no water;
            // too low (0) = all beach. +4m gives realistic continents with visible oceans.
            genParams.baseHeight          = settings.waterLevel + 4f;
            genParams.seaRadius           = radiusM + settings.waterLevel;
            // Direction-space noise receives a unit vector, not metre coordinates. The old
            // inverse-radius formula reduced continent frequency to ~0.15 on full-size worlds,
            // creating one rigid near-flat shell. Use a stable direction-space continental
            // scale, with the authored factor preserving designer control across planet sizes.
            genParams.continentScaleDir   = Mathf.Clamp(2.4f * settings.continentScaleFactor, 0.6f, 7f);
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

        /// <summary>Total sea-level gas density (kg/m³), independent from whether the gas is breathable.</summary>
        public float SurfaceAirDensity => settings != null ? settings.ResolveSurfaceAtmosphereDensity() : 0f;

        /// <summary>Altitude at which this body's atmosphere reaches vacuum.</summary>
        public float AtmosphereHeight => settings != null ? settings.ResolveAtmosphereHeight(SurfaceRadius) : 0f;

        /// <summary>Exponential falloff height for this body's total atmosphere.</summary>
        public float AtmosphereScaleHeight => settings != null ? settings.ResolveAtmosphereScaleHeight(SurfaceRadius) : 0f;

        /// <summary>True when this body has any atmospheric gas (breathable or otherwise).</summary>
        public bool HasAtmosphere => SurfaceAirDensity > 0.0001f && AtmosphereHeight > 0f;

        /// <summary>Atmospheric density (kg/m³) at altitude — exponential falloff to a real vacuum ceiling.</summary>
        public float AirDensityAt(Vector3 worldPosition)
        {
            if (!HasAtmosphere) return 0f;
            float altitude = Mathf.Max(0f, AltitudeAt(worldPosition));
            float atmosphereHeight = AtmosphereHeight;
            if (altitude >= atmosphereHeight) return 0f;

            float scaleHeight = Mathf.Max(1f, AtmosphereScaleHeight);
            return SurfaceAirDensity * Mathf.Exp(-altitude / scaleHeight);
        }

        /// <summary>True when the local profile has reached vacuum. This agrees with density, life support, and thruster logic.</summary>
        public bool IsInSpace(Vector3 worldPosition)
        {
            if (!HasAtmosphere) return true;
            return AltitudeAt(worldPosition) >= AtmosphereHeight || AirDensityAt(worldPosition) < 0.02f;
        }

        // ── Biome / climate filtering ─────────────────────────────
        /// <summary>
        /// Build the runtime biome list for this body: the designer whitelist, filtered by the
        /// body's mean temperature so e.g. a frozen moon can't spawn a desert. Returns a managed
        /// array the caller packs into a NativeArray<BiomeData> for Burst.
        /// </summary>
        public BiomeData[] BuildBiomeData(BiomeRegistry fallbackRegistry)
        {
            // Null-safe: if settings isn't configured yet (e.g. LOD OnEnable firing during
            // bootstrap before the body is fully wired), return a sensible single-biome default
            // instead of throwing.
            BiomeDefinition[] src;
            if (settings == null || settings.allowedBiomes == null || settings.allowedBiomes.Length == 0)
            {
                src = fallbackRegistry != null && fallbackRegistry.biomes != null
                        ? fallbackRegistry.biomes.ToArray()
                        : System.Array.Empty<BiomeDefinition>();
            }
            else
            {
                src = settings.allowedBiomes;
            }

            // Body temperature → climate-space 0..1 (map -40..40 °C onto 0.1..0.9).
            // (Reserved for future snow-line / biome weighting; currently the coarse gates below.)

            var list = new System.Collections.Generic.List<BiomeData>(src.Length);
            float temperature = settings != null ? settings.temperature : 15f;
            foreach (var def in src)
            {
                if (def == null) continue;
                // Exclude biomes whose temperature window is incompatible with the body's climate.
                if (temperature < -5f && def.minTemperature > 0.55f) continue;   // cold body, hot biome
                if (temperature > 35f && def.maxTemperature < 0.45f) continue;   // hot body, cold biome
                var data = BiomeData.FromDefinition(def);
                // PHASE 3: Runtime surface-material remap for realism. Biome assets were authored
                // with Clay (brown, id=3) as their grass-equivalent — that makes the terrain look
                // barren/ugly. Here we remap based on biome IDENTITY (name) so grass biomes ALWAYS
                // get green Grass, regardless of what the asset says. No dependency on the
                // normalize tool.
                RemapSurfaceForRealism(ref data, def.biomeName);
                list.Add(data);
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
                    surfaceMat = (byte)Materials.MaterialId.Grass, surfaceDepth = 1,
                    subsurfaceMat = (byte)Materials.MaterialId.Clay, subsurfaceDepth = 4,
                    allowBeach = 1, isOceanic = 0,
                });
            }
            var result = list.ToArray();
            // Diagnostic: log what biomes + surface materials this body will use.
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[CelestialBody] Biomes for '");
            sb.Append(DisplayName);
            sb.Append("' (");
            sb.Append(result.Length);
            sb.Append("): ");
            for (int i = 0; i < result.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(result[i].surfaceMat);
                sb.Append("(mat)");
            }
            Debug.Log(sb.ToString());
            return result;
        }

        /// <summary>Prebuilt ore layers for this body (common + rare + specials).</summary>
        public OreLayer[] BuildOreLayers()
        {
            if (settings == null) return System.Array.Empty<OreLayer>();
            var layers = settings.BuildOreLayers();
            // Raw crude markers support finite surface seeps on setup-authorized oil-rich
            // bodies. Infinite Jack Pump identity is checked separately and remains Pirate-only.
            if (!settings.CanGenerateFiniteCrudeOilSeeps)
                layers.RemoveAll(layer => layer.material == VoxelEngine.Materials.MaterialId.CrudeOil);
            return layers.ToArray();
        }

        /// <summary>
        /// Phase 3: remap biome surface materials by IDENTITY so the planet looks realistic.
        /// Grass biomes (Plains/Forest/Steppes) get green Grass, Desert gets Sand, Tundra gets
        /// Clay (frozen dirt). Runs at BUILD TIME so it works regardless of what the .asset says.
        /// </summary>
        private static void RemapSurfaceForRealism(ref BiomeData data, string biomeName)
        {
            if (string.IsNullOrEmpty(biomeName)) return;
            string n = biomeName.ToLowerInvariant();

            // ── Celestial world theming (Phase 2): themed biome name → themed surface material ──
            if (n.Contains("moon") || n.Contains("lunar")) { data.surfaceMat = (byte)Materials.MaterialId.Stone; data.subsurfaceMat = (byte)Materials.MaterialId.Stone; return; }
            if (n.Contains("mars") || n.Contains("martian")) { data.surfaceMat = (byte)Materials.MaterialId.MartianDust; data.subsurfaceMat = (byte)Materials.MaterialId.MartianDust; return; }
            if (n.Contains("venus")) { data.surfaceMat = (byte)Materials.MaterialId.VenusAsh; data.subsurfaceMat = (byte)Materials.MaterialId.VenusAsh; return; }
            if (n.Contains("acid")) { data.surfaceMat = (byte)Materials.MaterialId.AcidBog; data.subsurfaceMat = (byte)Materials.MaterialId.AcidBog; return; }
            if (n.Contains("volcanic") || n.Contains("lava") || n.Contains("magma")) { data.surfaceMat = (byte)Materials.MaterialId.VolcanicBasalt; data.subsurfaceMat = (byte)Materials.MaterialId.VolcanicBasalt; return; }
            if (n.Contains("crystal") || n.Contains("geode")) { data.surfaceMat = (byte)Materials.MaterialId.CrystalGeode; data.subsurfaceMat = (byte)Materials.MaterialId.CrystalGeode; return; }
            if (n.Contains("pirate")) { data.surfaceMat = (byte)Materials.MaterialId.Clay; data.subsurfaceMat = (byte)Materials.MaterialId.Clay; return; }
            if (n.Contains("desolate")) { data.surfaceMat = (byte)Materials.MaterialId.Clay; data.subsurfaceMat = (byte)Materials.MaterialId.Clay; return; }
            if (n.Contains("greek") || n.Contains("marble")) { data.surfaceMat = (byte)Materials.MaterialId.Sand; data.subsurfaceMat = (byte)Materials.MaterialId.Sand; return; }
            if (n.Contains("ice") || n.Contains("frozen") || n.Contains("glacial")) { data.surfaceMat = (byte)Materials.MaterialId.Ice; data.subsurfaceMat = (byte)Materials.MaterialId.Clay; return; }
            if (n.Contains("water") || n.Contains("ocean")) { data.surfaceMat = (byte)Materials.MaterialId.Sand; data.subsurfaceMat = (byte)Materials.MaterialId.Sand; return; }

            if (n.Contains("forest") || n.Contains("meadow"))
            {
                data.surfaceMat = (byte)Materials.MaterialId.Grass;
                data.subsurfaceMat = (byte)Materials.MaterialId.Clay;
                data.priority = 3;  // boost so Forest wins in humid temperate zones (over Plains)
                // WIDEN the climate window so Forest actually spawns. The authored asset has
                // T[0.3-0.65] H[0.55-0.95] which is too narrow. Widen to cover temperate-tropical.
                data.tempRange = new Unity.Mathematics.float2(0.2f, 0.85f);
                data.humidRange = new Unity.Mathematics.float2(0.4f, 1.0f);
            }
            else if (n.Contains("plains") || n.Contains("steppe") || n.Contains("grass"))
            {
                data.surfaceMat = (byte)Materials.MaterialId.Grass;
                data.subsurfaceMat = (byte)Materials.MaterialId.Clay;
                data.priority = 1;
            }
            else if (n.Contains("desert") || n.Contains("wasteland") || n.Contains("dune"))
            {
                data.surfaceMat = (byte)Materials.MaterialId.Sand;
                data.subsurfaceMat = (byte)Materials.MaterialId.Sand;
            }
            else if (n.Contains("tundra") || n.Contains("taiga"))
            {
                data.surfaceMat = (byte)Materials.MaterialId.Clay;
                data.subsurfaceMat = (byte)Materials.MaterialId.Clay;
            }
            else if (n.Contains("mountain") || n.Contains("peak"))
            {
                data.surfaceMat = (byte)Materials.MaterialId.Stone;
                data.subsurfaceMat = (byte)Materials.MaterialId.Stone;
            }
            else if (n.Contains("snow") || n.Contains("ice"))
            {
                data.surfaceMat = (byte)Materials.MaterialId.Ice;
                data.subsurfaceMat = (byte)Materials.MaterialId.Stone;
            }
            else if (n.Contains("beach") || n.Contains("ocean") || n.Contains("sea") || n.Contains("coast"))
            {
                data.surfaceMat = (byte)Materials.MaterialId.Sand;
                data.subsurfaceMat = (byte)Materials.MaterialId.Sand;
            }
        }
    }
}
