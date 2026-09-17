// Assets/Scripts/VoxelEngine/Player/HazardField.cs
//
// LOCALISED ENVIRONMENTAL HAZARDS — the zone layer.
//
// The game already had hazards, but they were WHOLE-PLANET CONSTANTS: a body has one
// radiation level and one surface temperature, so a planet was uniformly lethal or
// uniformly safe. That makes protection a packing-list item — check the number before
// you launch, bring the suit, never think about it again.
//
// The roadmap asks for radiation ZONES, heat ZONES and toxic atmosphere. A zone is a
// different design object entirely: it makes a planet somewhere you read as you cross
// it, with hot spots to route around, and it gives the Geiger counter something to do.
//
// HOW A ZONE EXISTS WITHOUT BEING STORED
// Zones are sampled from noise seeded by the body's own `genParams.seed`, exactly the
// way ore veins already work. That means:
//   • No save data. A zone is a pure function of (body, position), so it is identical
//     on every load and across a rebuild, and old saves gain zones for free.
//   • No spawning, no registry, no streaming. A zone exists at any position you ask
//     about, including inside chunks that were never loaded.
// This is the same reasoning that put satellites on analytic orbits and trains on a
// graph: derive it when cheap, rather than simulate and store it.
//
// THE PLANET CONSTANT BECOMES THE FLOOR, NOT THE ANSWER.
// A body's authored `radiationLevel` still applies everywhere as a baseline. Zones add
// on top. So no existing world gets safer, and no authored value is overridden.

using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Player
{
    /// <summary>What a hazard reading is made of, for the HUD and the Geiger counter.</summary>
    public readonly struct HazardSample
    {
        /// <summary>Radiation damage per second before armour mitigation.</summary>
        public readonly float Radiation;

        /// <summary>Heat damage per second before armour mitigation.</summary>
        public readonly float Heat;

        /// <summary>Toxic damage per second before filtration.</summary>
        public readonly float Toxicity;

        /// <summary>0..1 strength of the strongest local zone, for the warning meter.</summary>
        public readonly float ZoneIntensity;

        /// <summary>Which zone dominates here. None when only the planetary baseline applies.</summary>
        public readonly HazardKind Dominant;

        public HazardSample(float radiation, float heat, float toxicity,
            float zoneIntensity, HazardKind dominant)
        {
            Radiation = radiation; Heat = heat; Toxicity = toxicity;
            ZoneIntensity = zoneIntensity; Dominant = dominant;
        }

        public bool AnyHazard => Radiation > 0.001f || Heat > 0.001f || Toxicity > 0.001f;
    }

    public enum HazardKind { None, Radiation, Heat, Toxic }

    public static class HazardField
    {
        // ════════════════════════════════════════════════════════════════
        //  TUNING
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Size of a hazard cell in metres. Large enough that crossing one is a journey
        /// rather than a step, small enough that a planet has several.
        /// </summary>
        private const float ZONE_CELL_SIZE = 260f;

        /// <summary>
        /// How much of a cell is hazardous. Low on purpose: a zone must be avoidable, or it
        /// is just a higher planet constant wearing a costume.
        /// </summary>
        private const float ZONE_COVERAGE = 0.34f;

        /// <summary>Peak damage per second at the centre of a full-strength zone.</summary>
        private const float MAX_ZONE_RADIATION = 4.0f;
        private const float MAX_ZONE_HEAT = 3.5f;
        private const float MAX_ZONE_TOXIC = 2.5f;

        /// <summary>
        /// A planet with no atmosphere cannot have a toxic one. Below this air density the
        /// toxic channel is skipped entirely rather than fudged.
        /// </summary>
        private const float MIN_TOXIC_AIR_DENSITY = 0.08f;

        // ════════════════════════════════════════════════════════════════
        //  SAMPLING
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Full hazard reading at a world position on the active body. Cheap enough to call
        /// every frame, but callers that only need damage should prefer the cached values
        /// on <see cref="PlayerHazardService"/>.
        /// </summary>
        public static HazardSample Sample(Vector3 worldPosition)
        {
            var body = GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return default;

            var settings = body.settings;
            int seed = body.genParams.seed;

            // ── Baselines: the authored planet constants, unchanged. ──
            float radiation = Mathf.Max(0f, settings.radiationLevel);

            float heat = 0f;
            float surfaceC = VoxelEngine.Thermal.ThermalRules.SurfaceTemperatureC(body);
            if (surfaceC > PlayerHazardService.HeatDamageThresholdC)
            {
                float severity = Mathf.Clamp01(
                    (surfaceC - PlayerHazardService.HeatDamageThresholdC) / PlayerHazardService.HeatRampSpanC);
                heat = PlayerHazardService.MaxHeatDamagePerSecond * severity;
            }

            float toxicity = 0f;

            // ── Zones layered on top. ──
            float radZone = ZoneStrength(worldPosition, seed, 0x5241_4400u);
            float heatZone = ZoneStrength(worldPosition, seed, 0x4845_4100u);
            float toxZone = ZoneStrength(worldPosition, seed, 0x544F_5800u);

            // A world with no radiation at all stays clean: zones SCALE the planet's
            // character rather than inventing a hazard it was never authored to have.
            // A world with even a trace becomes genuinely patchy.
            if (radZone > 0f)
            {
                float planetFactor = settings.radiationLevel > 0.001f
                    ? Mathf.Clamp01(0.35f + settings.radiationLevel)
                    : 0f;
                radiation += MAX_ZONE_RADIATION * radZone * planetFactor;
            }

            // Heat zones are volcanic pockets, so they key off how warm the world already
            // is. A frozen moon does not grow lava fields.
            if (heatZone > 0f && surfaceC > -10f)
            {
                float planetFactor = Mathf.Clamp01((surfaceC + 10f) / 60f);
                heat += MAX_ZONE_HEAT * heatZone * planetFactor;
            }

            // Toxic air needs air. An unbreathable dense atmosphere is the dangerous case:
            // plenty of gas, none of it oxygen.
            float airDensity = AirDensity(settings);
            if (toxZone > 0f && airDensity > MIN_TOXIC_AIR_DENSITY && !settings.HasOxygen)
            {
                float planetFactor = Mathf.Clamp01(airDensity);
                toxicity += MAX_ZONE_TOXIC * toxZone * planetFactor;
            }

            // Report whichever zone is actually strongest here, so the HUD names the thing
            // the player should react to rather than an arbitrary priority order.
            HazardKind dominant = HazardKind.None;
            float best = 0.02f;
            if (radZone > best) { best = radZone; dominant = HazardKind.Radiation; }
            if (heatZone > best) { best = heatZone; dominant = HazardKind.Heat; }
            if (toxZone > best) { best = toxZone; dominant = HazardKind.Toxic; }

            // A zone the planet cannot express is not reported, or the Geiger counter would
            // scream on a world where nothing can hurt you.
            if (dominant == HazardKind.Radiation && radiation <= settings.radiationLevel + 0.001f)
                dominant = HazardKind.None;
            if (dominant == HazardKind.Toxic && toxicity <= 0.001f)
                dominant = HazardKind.None;

            float intensity = dominant == HazardKind.None ? 0f : best;

            return new HazardSample(radiation, heat, toxicity, intensity, dominant);
        }

        /// <summary>
        /// 0..1 strength of a hazard zone at a position, with soft edges.
        ///
        /// Worley (cellular) noise rather than fractal noise, for the same reason ore veins
        /// use it: Worley produces discrete blobs with clear centres and clear gaps, which
        /// is exactly what an avoidable zone needs. Fractal noise produces a smear the
        /// player can never be confident they have left.
        /// </summary>
        private static float ZoneStrength(Vector3 worldPosition, int seed, uint channel)
        {
            // Sampled on the horizontal plane only. A radiation field is a place on the map,
            // not a bubble you can fly over the top of.
            var p = new float3(worldPosition.x, 0f, worldPosition.z);

            float f = VeinNoise.Worley3(p, ZONE_CELL_SIZE, (uint)seed ^ channel);

            // Worley returns distance-like values near 0 at cell centres. Invert so the
            // centre is strongest, then cut everything outside the coverage fraction.
            float centred = 1f - Mathf.Clamp01(f);
            if (centred <= 1f - ZONE_COVERAGE) return 0f;

            float t = (centred - (1f - ZONE_COVERAGE)) / ZONE_COVERAGE;
            // Smoothstep so a zone fades in over its last stretch instead of switching on,
            // which is what makes a Geiger counter useful as a warning rather than an alarm.
            return Mathf.Clamp01(t * t * (3f - 2f * t));
        }

        /// <summary>Air density proxy from the authored atmosphere fields, 0..1.</summary>
        private static float AirDensity(BodySettings settings)
        {
            if (settings == null) return 0f;
            // Prefer the explicitly authored atmosphere; fall back to the legacy
            // oxygen-derived estimate for pre-atmosphere-step assets.
            float authored = settings.atmosphereHeightRadiusFraction;
            if (authored > 0.0001f) return Mathf.Clamp01(authored * 12f);
            return Mathf.Clamp01(settings.oxygenLevel);
        }

        /// <summary>
        /// Human-readable name of a hazard, for the HUD.
        /// </summary>
        public static string Label(HazardKind kind) => kind switch
        {
            HazardKind.Radiation => "RADIATION",
            HazardKind.Heat => "EXTREME HEAT",
            HazardKind.Toxic => "TOXIC ATMOSPHERE",
            _ => "",
        };

        /// <summary>What protects against a hazard, so the warning can tell the player what to do.</summary>
        public static string Mitigation(HazardKind kind) => kind switch
        {
            HazardKind.Radiation => "Hazmat plating or Radiation Shielding",
            HazardKind.Heat => "Heat Tolerance upgrades",
            HazardKind.Toxic => "A sealed helmet with oxygen",
            _ => "",
        };
    }
}
