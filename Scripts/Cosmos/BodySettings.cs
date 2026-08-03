// Assets/Scripts/VoxelEngine/Cosmos/BodySettings.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Biomes;
using VoxelEngine.Generation;     // OreLayer
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Shared physical / climate / terrain / ore settings for a celestial body.
    /// Embedded inside <see cref="PlanetTemplate"/> and <see cref="MoonTemplate"/>, so the
    /// exact same designer surface (gravity, oxygen, temperature, water, mountains, grass,
    /// wind, biome whitelist, ore tiers, surface mode) applies to both planets and moons.
    /// </summary>
    [Serializable]
    public class BodySettings
    {
        // ── Identity ──────────────────────────────────────────────
        [Header("Identity")]
        public string bodyName = "Earth";

        [Tooltip("Master randomisation seed. ONLY randomised at world creation; persisted & " +
                 "reused verbatim on every subsequent load (never regenerated).")]
        public int seed = 0;

        // ── Physics / Atmosphere ──────────────────────────────────
        [Header("Physics & Atmosphere")]
        [Range(0f, 3f)]
        [Tooltip("Surface gravity multiplier (1 = Earth-like). Moons typically 0.1–0.3.")]
        public float gravity = 1f;

        [Range(0f, 1f)]
        [Tooltip("Breathable oxygen fraction. 0 = no oxygen (player needs life support).")]
        public float oxygenLevel = 1f;

        /// <summary>True when there is enough oxygen to breathe without gear.</summary>
        public bool HasOxygen => oxygenLevel > 0.05f;

        // ── Atmosphere profile ─────────────────────────────────────
        // These values describe TOTAL air mass, independently from oxygenLevel. That lets
        // an airless moon, a thin non-breathable world, and a dense toxic atmosphere use
        // the same flight model without pretending every atmosphere is safe to breathe.
        [Header("Atmosphere Profile")]
        [Tooltip("Sea-level air density in kg/m³. 0 on an initialized profile means intentional vacuum.")]
        [Range(0f, 4f)] public float surfaceAtmosphereDensity = 0f;

        [Tooltip("Atmosphere-top height as a fraction of the runtime body radius. 0 uses the safe legacy fallback.")]
        [Range(0f, 2f)] public float atmosphereHeightRadiusFraction = 0f;

        [Tooltip("Exponential density scale height as a fraction of the runtime body radius. 0 uses the safe legacy fallback.")]
        [Range(0f, 1f)] public float atmosphereScaleHeightRadiusFraction = 0f;

        // Set only by the Voxel Engine Setup atmosphere step. It distinguishes a deliberately
        // authored zero-density vacuum profile from pre-7.5 assets whose new fields deserialize
        // as zero and therefore need the legacy oxygen-derived fallback at runtime.
        [HideInInspector] public int atmosphereProfileVersion = 0;

        /// <summary>Total sea-level air density, with a save-compatible fallback for pre-profile assets.</summary>
        public float ResolveSurfaceAtmosphereDensity()
        {
            if (atmosphereProfileVersion > 0)
                return Mathf.Max(0f, surfaceAtmosphereDensity);
            if (surfaceAtmosphereDensity > 0.0001f)
                return surfaceAtmosphereDensity;
            return HasOxygen ? 1.225f * Mathf.Clamp01(oxygenLevel) : 0f;
        }

        /// <summary>Atmosphere ceiling at a runtime radius. Uses fractions so the test planet and full planet agree.</summary>
        public float ResolveAtmosphereHeight(float runtimeSurfaceRadius)
        {
            if (ResolveSurfaceAtmosphereDensity() <= 0.0001f) return 0f;
            float fraction = atmosphereHeightRadiusFraction > 0.0001f
                ? atmosphereHeightRadiusFraction
                : 0.62f;
            return Mathf.Max(1f, runtimeSurfaceRadius * Mathf.Clamp(fraction, 0.01f, 2f));
        }

        /// <summary>Exponential density scale height at a runtime radius.</summary>
        public float ResolveAtmosphereScaleHeight(float runtimeSurfaceRadius)
        {
            float top = ResolveAtmosphereHeight(runtimeSurfaceRadius);
            if (top <= 0f) return 0f;
            float fraction = atmosphereScaleHeightRadiusFraction > 0.0001f
                ? atmosphereScaleHeightRadiusFraction
                : 0.15f;
            float scale = Mathf.Max(1f, runtimeSurfaceRadius * Mathf.Clamp(fraction, 0.005f, 1f));
            // Never allow an invalid profile to have a scale height above its atmosphere top.
            return Mathf.Min(scale, top * 0.75f);
        }

        /// <summary>True when this body has any gas for atmospheric propulsion or drag.</summary>
        public bool HasAtmosphere => ResolveSurfaceAtmosphereDensity() > 0.0001f;

        // ── Climate ───────────────────────────────────────────────
        [Header("Climate")]
        [Range(-80f, 80f)]
        [Tooltip("Mean surface temperature in °C. Biomes whose climate window is incompatible " +
                 "with this temperature are excluded from generation.")]
        public float temperature = 15f;

        [Range(0f, 10f)]
        [Tooltip("Ambient radiation damage per second. Zero is safe; armor shielding and Hazmat protection reduce this exposure.")]
        public float radiationLevel = 0f;

        [Range(0f, 3f)]
        [Tooltip("Base wind strength. The WindField modulates this with smooth gusts at runtime.")]
        public float windStrength = 1f;

        [Range(0f, 1f)]
        [Tooltip("How strongly the wind surges over time (0 = steady, 1 = very gusty).")]
        public float windGustiness = 0.4f;

        // ── Hydrosphere ───────────────────────────────────────────
        [Header("Hydrosphere")]
        [Range(0, 250)]
        [Tooltip("Sea level in voxels below the mean surface (0 = arid world).")]
        public int waterLevel = 96;

        [Range(0f, 3f)]
        [Tooltip("Relative water volume — scales ocean depth and lake fill.")]
        public float waterVolume = 1f;

        [Tooltip("Surface composition mode (solid / all-water / all-oil).")]
        public SurfaceMode surfaceMode = SurfaceMode.SolidSurface;

        // ── Terrain ───────────────────────────────────────────────
        [Header("Terrain")]
        [Range(0.1f, 5f)]
        [Tooltip("Mountain amplitude multiplier. Higher = taller peaks.")]
        public float mountainScale = 1f;

        [Range(0.1f, 5f)]
        [Tooltip("Continent size multiplier (higher = larger landmasses).")]
        public float continentScaleFactor = 1f;

        [Tooltip("If false, no grass is placed on this body (e.g. moons, deserts).")]
        public bool enableGrass = true;

        [Range(0.5f, 6f)]
        [Tooltip("Playable planet radius in km. The generator is radius-agnostic; this is the " +
                 "design target used by LOD/streaming budgets.")]
        public float radiusKm = 8f;

        [Tooltip("Custom colour used to render this body from far away (in the sky / space view). " +
                 "If clear, the renderer infers a colour from climate settings.")]
        public Color displayColor = new Color(0f, 0f, 0f, 0f);  // alpha 0 = auto

        // ── Biomes ────────────────────────────────────────────────
        [Header("Biomes")]
        [Tooltip("Whitelist of biomes that may generate on this body. Empty = use registry defaults.")]
        public BiomeDefinition[] allowedBiomes;

        // ── Ores & Minerals (two tiers + specials) ────────────────
        [Header("Sub-surface ores (common)")]
        public List<OreDeposit> subSurfaceOres = new List<OreDeposit>();

        [Header("Deep-core ores (rare)")]
        public List<OreDeposit> deepCoreOres = new List<OreDeposit>();

        [Header("Specials (crude oil, ice, …)")]
        public List<OreDeposit> specials = new List<OreDeposit>();

        /// <summary>Enumerate every deposit across all tiers.</summary>
        public IEnumerable<OreDeposit> AllOres()
        {
            if (subSurfaceOres != null) foreach (var o in subSurfaceOres) yield return o;
            if (deepCoreOres   != null) foreach (var o in deepCoreOres)   yield return o;
            if (specials       != null) foreach (var o in specials)       yield return o;
        }

        /// <summary>
        /// Build the flat list of <see cref="OreLayer"/> consumed by the existing Burst
        /// <c>ChunkGenJob</c>. Drop-in replacement for the old hardcoded per-planet fields.
        /// </summary>
        public List<OreLayer> BuildOreLayers()
        {
            var result = new List<OreLayer>(16);
            foreach (var d in AllOres())
            {
                if (d.material == MaterialId.Air) continue;
                result.Add(d.ToOreLayer());
            }
            return result;
        }

        /// <summary>
        /// Factory that returns an Earth-like body preset: breathable, temperate, grassy,
        /// with the full ore catalogue from the design brief (Iron … Uranium + Lithium),
        /// split across the two tiers plus specials.
        /// </summary>
        public static BodySettings CreateEarthlike()
        {
            var b = new BodySettings
            {
                bodyName             = "Earth",
                gravity              = 1f,
                oxygenLevel          = 1f,
                temperature          = 15f,
                windStrength         = 1f,
                windGustiness        = 0.4f,
                waterLevel           = 96,
                waterVolume          = 1f,
                surfaceMode          = SurfaceMode.SolidSurface,
                mountainScale        = 1.1f,
                continentScaleFactor = 1f,
                enableGrass          = true,
                radiusKm             = 8f,
            };

            // ── Sub-surface (common) ──────────────────────────────
            b.subSurfaceOres = new List<OreDeposit>
            {
                Deposit(MaterialId.Iron,     OreTier.SubSurface, 0.06f, 0.45f, 4,   80),
                Deposit(MaterialId.Copper,   OreTier.SubSurface, 0.07f, 0.55f, 6,   70),
                Deposit(MaterialId.Coal,     OreTier.SubSurface, 0.05f, 0.50f, 4,   60),
                Deposit(MaterialId.Nickel,   OreTier.SubSurface, 0.08f, 0.60f, 20,  120),
                Deposit(MaterialId.Silicon,  OreTier.SubSurface, 0.06f, 0.55f, 4,   90),
                Deposit(MaterialId.Cobalt,   OreTier.SubSurface, 0.09f, 0.65f, 30,  140),
                Deposit(MaterialId.Magnesium,OreTier.SubSurface, 0.08f, 0.62f, 15,  110),
                Deposit(MaterialId.Lithium,  OreTier.SubSurface, 0.07f, 0.60f, 10,  100),
            };

            // ── Deep-core (rare) ──────────────────────────────────
            b.deepCoreOres = new List<OreDeposit>
            {
                Deposit(MaterialId.Silver,   OreTier.DeepCore, 0.10f, 0.72f, 60,  200),
                Deposit(MaterialId.Gold,     OreTier.DeepCore, 0.11f, 0.78f, 80,  220),
                Deposit(MaterialId.Platinum, OreTier.DeepCore, 0.12f, 0.80f, 100, 240),
                Deposit(MaterialId.Uranium,  OreTier.DeepCore, 0.13f, 0.82f, 120, 250),
            };

            // ── Specials ──────────────────────────────────────────
            b.specials = new List<OreDeposit>
            {
                Deposit(MaterialId.CrudeOil, OreTier.Special, 0.04f, 0.70f, 25, 90),
                Deposit(MaterialId.Ice,      OreTier.Special, 0.05f, 0.65f, 0,  12),
            };

            return b;
        }

        // Tiny local constructor for tidy deposit tables above.
        private static OreDeposit Deposit(MaterialId m, OreTier t, float scale, float threshold,
                                          int minDepth, int maxDepth) => new OreDeposit
        {
            material  = m,
            tier      = t,
            scale     = scale,
            threshold = threshold,
            minDepth  = minDepth,
            maxDepth  = maxDepth,
            abundance = 1f,
        };
    }
}
