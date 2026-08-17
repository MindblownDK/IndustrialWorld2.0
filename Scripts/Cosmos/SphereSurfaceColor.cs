// Assets/Scripts/VoxelEngine/Cosmos/SphereSurfaceColor.cs
//
// Shared sampled-surface colour palette for EVERY spherical body renderer
// (SpaceBodyRenderer sky proxies + orbital colour sampling).
//
// Both renderers read the same SphereDensity field and colour it with this one
// function, so a planet hanging in the sky shows EXACTLY the continents, oceans,
// deserts and snow caps you will walk on when you arrive — no hue pop, no
// "wrong planet" moment, just a resolution swap.
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Single source of truth for sampled terrain surface colours.
    /// </summary>
    public static class SphereSurfaceColor
    {
        /// <summary>
        /// Surface colour for a sampled terrain point based on altitude + latitude.
        /// Ocean basins are blue (darker with depth), beaches sandy, lowlands green
        /// near the equator / browner toward the poles, highlands rocky, peaks and
        /// poles snow-capped — so the planet reads as Earth from any distance.
        /// </summary>
        public static Color For(float altMetres, float latitude)
        {
            // Polar ice: near the poles, everything is white (ice/snow).
            if (latitude > 0.82f) return new Color(0.92f, 0.95f, 0.98f, 1f);

            if (altMetres < 0f)
            {
                float depth = Mathf.Clamp01(-altMetres / 40f);
                return Color.Lerp(new Color(0.20f, 0.45f, 0.70f, 1f), new Color(0.03f, 0.10f, 0.30f, 1f), depth);
            }
            if (altMetres < 2f) return new Color(0.80f, 0.74f, 0.52f, 1f);
            float h = Mathf.Clamp01(altMetres / 60f);
            // Equatorial = lush green; higher latitudes = browner/cooler.
            float greenness = Mathf.Clamp01(1f - latitude * 1.2f);
            Color lush = new Color(0.26f, 0.55f, 0.22f, 1f);
            Color dry  = new Color(0.50f, 0.45f, 0.28f, 1f);
            Color lowland = Color.Lerp(dry, lush, greenness);
            Color highland = new Color(0.50f, 0.40f, 0.28f, 1f);
            Color land = Color.Lerp(lowland, highland, h);
            // Snow caps on high peaks.
            if (h > 0.7f) land = Color.Lerp(land, Color.white, (h - 0.7f) / 0.3f);
            // Sub-polar regions get partial snow.
            if (latitude > 0.65f) land = Color.Lerp(land, new Color(0.85f, 0.88f, 0.92f, 1f), (latitude - 0.65f) / 0.17f);
            return land;
        }

        /// <summary>
        /// Apply a body's authored display colour as a SUBTLE personality tint.
        /// The sampled terrain always remains the dominant visual — a strong lerp
        /// (the old 0.72) washed the whole planet into a flat colored ball.
        /// </summary>
        public static Color WithDisplayTint(Color terrainColor, Color displayColor, float tintStrength = 0.18f)
        {
            if (displayColor.a <= 0.01f) return terrainColor;
            return Color.Lerp(terrainColor, displayColor, Mathf.Clamp01(tintStrength));
        }
    }
}
