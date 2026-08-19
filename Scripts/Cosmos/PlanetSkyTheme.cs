// Assets/Scripts/VoxelEngine/Cosmos/PlanetSkyTheme.cs
//
// Authoritative sky-art catalogue. Every celestial body resolves to one theme
// from its name / climate, then optionally overlays designer colour overrides
// stored on BodySettings. Runtime systems (sky dome, sun, fog, nebulae, distant
// planet rims) all read from here so a volcanic world never wears an Earth sky.

using UnityEngine;

namespace VoxelEngine.Cosmos
{
    public enum PlanetSkyKind
    {
        Temperate = 0,
        Moon = 1,
        Ice = 2,
        Volcanic = 3,
        Acid = 4,
        Ocean = 5,
        Pirate = 6,
        Desolate = 7,
        Venus = 8,
        Mars = 9,
        Crystal = 10,
        Olympus = 11,
        Water = 12,
        Asteroid = 13,
    }

    /// <summary>Allocation-free sky palette. All fields are assigned by the catalogue.</summary>
    public struct PlanetSkyPalette
    {
        public PlanetSkyKind Kind;
        public Color Zenith;
        public Color Horizon;
        public Color GroundFog;
        public Color Sunset;
        public Color SunDay;
        public Color SunSunset;
        public Color AmbientDay;
        public Color AmbientNight;
        public Color UpperAir;
        public Color AtmosphereRim;
        public Color NebulaPrimary;
        public Color NebulaSecondary;
        public float SurfaceFogDensity;
        public float HazeStrength;
        public float AuroraStrength;
        public bool DustHaze;
    }

    public static class PlanetSkyCatalog
    {
        public static PlanetSkyKind ResolveKind(BodySettings settings)
        {
            if (settings == null) return PlanetSkyKind.Temperate;
            string name = settings.bodyName ?? string.Empty;
            float temperature = settings.temperature;

            if (Contains(name, "asteroid") || Contains(name, "belt"))
                return PlanetSkyKind.Asteroid;
            if (Contains(name, "moon") || Contains(name, "lunar") || (!settings.HasAtmosphere && !settings.HasOxygen))
                return PlanetSkyKind.Moon;
            if (Contains(name, "volcan") || Contains(name, "lava") || Contains(name, "magma"))
                return PlanetSkyKind.Volcanic;
            if (Contains(name, "venus"))
                return PlanetSkyKind.Venus;
            if (Contains(name, "mars") || Contains(name, "martian"))
                return PlanetSkyKind.Mars;
            if (Contains(name, "acid") || Contains(name, "toxic"))
                return PlanetSkyKind.Acid;
            if (Contains(name, "crystal") || Contains(name, "geode"))
                return PlanetSkyKind.Crystal;
            if (Contains(name, "pirate") || Contains(name, "scrap"))
                return PlanetSkyKind.Pirate;
            if (Contains(name, "desolate"))
                return PlanetSkyKind.Desolate;
            if (Contains(name, "olympus") || Contains(name, "greek") || Contains(name, "marble"))
                return PlanetSkyKind.Olympus;
            if (Contains(name, "ice") || Contains(name, "frost") || Contains(name, "europa") || temperature < -15f)
                return PlanetSkyKind.Ice;
            if (Contains(name, "ocean"))
                return PlanetSkyKind.Ocean;
            if (Contains(name, "water"))
                return PlanetSkyKind.Water;
            return PlanetSkyKind.Temperate;
        }

        public static PlanetSkyPalette ForKind(PlanetSkyKind kind)
        {
            switch (kind)
            {
                case PlanetSkyKind.Moon:
                    return Make(kind,
                        zenith: C(0.010f, 0.012f, 0.020f),
                        horizon: C(0.10f, 0.11f, 0.14f),
                        fog: C(0.08f, 0.08f, 0.10f),
                        sunset: C(0.72f, 0.74f, 0.78f),
                        sunDay: C(1.00f, 0.98f, 0.94f),
                        sunSet: C(0.92f, 0.88f, 0.78f),
                        ambDay: C(0.10f, 0.11f, 0.14f),
                        ambNight: C(0.012f, 0.014f, 0.022f),
                        upper: C(0.018f, 0.020f, 0.030f),
                        rim: C(0.42f, 0.44f, 0.48f),
                        nebA: C(0.18f, 0.22f, 0.38f),
                        nebB: C(0.42f, 0.28f, 0.55f),
                        fogDensity: 0f, haze: 0.08f, aurora: 0f, dust: false);
                case PlanetSkyKind.Ice:
                    return Make(kind,
                        zenith: C(0.10f, 0.18f, 0.34f),
                        horizon: C(0.62f, 0.82f, 0.88f),
                        fog: C(0.72f, 0.86f, 0.92f),
                        sunset: C(1.00f, 0.62f, 0.72f),
                        sunDay: C(0.92f, 0.96f, 1.00f),
                        sunSet: C(1.00f, 0.58f, 0.70f),
                        ambDay: C(0.42f, 0.55f, 0.68f),
                        ambNight: C(0.04f, 0.08f, 0.16f),
                        upper: C(0.08f, 0.16f, 0.30f),
                        rim: C(0.45f, 0.78f, 0.92f),
                        nebA: C(0.22f, 0.55f, 0.82f),
                        nebB: C(0.72f, 0.32f, 0.78f),
                        fogDensity: 0.0045f, haze: 0.42f, aurora: 0.85f, dust: false);
                case PlanetSkyKind.Volcanic:
                    return Make(kind,
                        zenith: C(0.18f, 0.07f, 0.04f),
                        horizon: C(0.72f, 0.28f, 0.08f),
                        fog: C(0.42f, 0.16f, 0.06f),
                        sunset: C(1.00f, 0.32f, 0.06f),
                        sunDay: C(1.00f, 0.72f, 0.42f),
                        sunSet: C(1.00f, 0.28f, 0.05f),
                        ambDay: C(0.42f, 0.20f, 0.10f),
                        ambNight: C(0.06f, 0.02f, 0.01f),
                        upper: C(0.16f, 0.06f, 0.03f),
                        rim: C(0.92f, 0.38f, 0.10f),
                        nebA: C(0.72f, 0.18f, 0.06f),
                        nebB: C(0.45f, 0.08f, 0.04f),
                        fogDensity: 0.012f, haze: 0.78f, aurora: 0f, dust: true);
                case PlanetSkyKind.Acid:
                    return Make(kind,
                        zenith: C(0.16f, 0.22f, 0.08f),
                        horizon: C(0.52f, 0.68f, 0.18f),
                        fog: C(0.40f, 0.52f, 0.16f),
                        sunset: C(0.78f, 0.62f, 0.12f),
                        sunDay: C(0.88f, 0.92f, 0.48f),
                        sunSet: C(0.82f, 0.58f, 0.10f),
                        ambDay: C(0.32f, 0.40f, 0.18f),
                        ambNight: C(0.04f, 0.06f, 0.02f),
                        upper: C(0.12f, 0.18f, 0.06f),
                        rim: C(0.48f, 0.72f, 0.22f),
                        nebA: C(0.32f, 0.62f, 0.18f),
                        nebB: C(0.55f, 0.42f, 0.08f),
                        fogDensity: 0.010f, haze: 0.70f, aurora: 0f, dust: true);
                case PlanetSkyKind.Ocean:
                    return Make(kind,
                        zenith: C(0.08f, 0.28f, 0.48f),
                        horizon: C(0.42f, 0.78f, 0.82f),
                        fog: C(0.55f, 0.78f, 0.84f),
                        sunset: C(1.00f, 0.62f, 0.32f),
                        sunDay: C(1.00f, 0.96f, 0.86f),
                        sunSet: C(1.00f, 0.52f, 0.24f),
                        ambDay: C(0.28f, 0.48f, 0.62f),
                        ambNight: C(0.02f, 0.05f, 0.10f),
                        upper: C(0.06f, 0.18f, 0.32f),
                        rim: C(0.18f, 0.55f, 0.78f),
                        nebA: C(0.12f, 0.38f, 0.62f),
                        nebB: C(0.55f, 0.28f, 0.48f),
                        fogDensity: 0.0065f, haze: 0.48f, aurora: 0f, dust: false);
                case PlanetSkyKind.Water:
                    return Make(kind,
                        zenith: C(0.10f, 0.32f, 0.42f),
                        horizon: C(0.48f, 0.82f, 0.72f),
                        fog: C(0.58f, 0.82f, 0.78f),
                        sunset: C(1.00f, 0.70f, 0.38f),
                        sunDay: C(1.00f, 0.97f, 0.88f),
                        sunSet: C(1.00f, 0.58f, 0.28f),
                        ambDay: C(0.30f, 0.52f, 0.55f),
                        ambNight: C(0.02f, 0.06f, 0.08f),
                        upper: C(0.07f, 0.20f, 0.28f),
                        rim: C(0.22f, 0.68f, 0.62f),
                        nebA: C(0.10f, 0.48f, 0.55f),
                        nebB: C(0.42f, 0.22f, 0.55f),
                        fogDensity: 0.007f, haze: 0.52f, aurora: 0f, dust: false);
                case PlanetSkyKind.Pirate:
                    return Make(kind,
                        zenith: C(0.16f, 0.10f, 0.08f),
                        horizon: C(0.58f, 0.36f, 0.18f),
                        fog: C(0.42f, 0.28f, 0.16f),
                        sunset: C(0.95f, 0.42f, 0.14f),
                        sunDay: C(1.00f, 0.82f, 0.58f),
                        sunSet: C(0.95f, 0.38f, 0.12f),
                        ambDay: C(0.36f, 0.24f, 0.16f),
                        ambNight: C(0.04f, 0.02f, 0.015f),
                        upper: C(0.12f, 0.07f, 0.05f),
                        rim: C(0.72f, 0.38f, 0.16f),
                        nebA: C(0.55f, 0.18f, 0.08f),
                        nebB: C(0.28f, 0.10f, 0.18f),
                        fogDensity: 0.008f, haze: 0.58f, aurora: 0f, dust: true);
                case PlanetSkyKind.Desolate:
                    return Make(kind,
                        zenith: C(0.22f, 0.20f, 0.16f),
                        horizon: C(0.62f, 0.54f, 0.38f),
                        fog: C(0.55f, 0.48f, 0.34f),
                        sunset: C(0.88f, 0.48f, 0.22f),
                        sunDay: C(1.00f, 0.90f, 0.72f),
                        sunSet: C(0.90f, 0.46f, 0.20f),
                        ambDay: C(0.40f, 0.36f, 0.28f),
                        ambNight: C(0.04f, 0.03f, 0.02f),
                        upper: C(0.16f, 0.14f, 0.10f),
                        rim: C(0.68f, 0.55f, 0.32f),
                        nebA: C(0.42f, 0.28f, 0.14f),
                        nebB: C(0.22f, 0.16f, 0.22f),
                        fogDensity: 0.0075f, haze: 0.55f, aurora: 0f, dust: true);
                case PlanetSkyKind.Venus:
                    return Make(kind,
                        zenith: C(0.32f, 0.28f, 0.08f),
                        horizon: C(0.78f, 0.66f, 0.22f),
                        fog: C(0.68f, 0.58f, 0.20f),
                        sunset: C(0.95f, 0.48f, 0.10f),
                        sunDay: C(1.00f, 0.88f, 0.48f),
                        sunSet: C(0.95f, 0.42f, 0.08f),
                        ambDay: C(0.48f, 0.40f, 0.16f),
                        ambNight: C(0.06f, 0.05f, 0.02f),
                        upper: C(0.24f, 0.20f, 0.06f),
                        rim: C(0.82f, 0.68f, 0.22f),
                        nebA: C(0.62f, 0.42f, 0.08f),
                        nebB: C(0.42f, 0.18f, 0.06f),
                        fogDensity: 0.018f, haze: 0.92f, aurora: 0f, dust: true);
                case PlanetSkyKind.Mars:
                    return Make(kind,
                        zenith: C(0.28f, 0.12f, 0.08f),
                        horizon: C(0.78f, 0.42f, 0.22f),
                        fog: C(0.62f, 0.34f, 0.18f),
                        sunset: C(1.00f, 0.48f, 0.32f),
                        sunDay: C(1.00f, 0.86f, 0.68f),
                        sunSet: C(1.00f, 0.42f, 0.28f),
                        ambDay: C(0.42f, 0.22f, 0.14f),
                        ambNight: C(0.04f, 0.02f, 0.015f),
                        upper: C(0.18f, 0.08f, 0.05f),
                        rim: C(0.78f, 0.38f, 0.18f),
                        nebA: C(0.62f, 0.22f, 0.12f),
                        nebB: C(0.28f, 0.12f, 0.22f),
                        fogDensity: 0.0055f, haze: 0.62f, aurora: 0f, dust: true);
                case PlanetSkyKind.Crystal:
                    return Make(kind,
                        zenith: C(0.14f, 0.08f, 0.28f),
                        horizon: C(0.58f, 0.42f, 0.82f),
                        fog: C(0.48f, 0.38f, 0.68f),
                        sunset: C(0.92f, 0.42f, 0.88f),
                        sunDay: C(0.90f, 0.88f, 1.00f),
                        sunSet: C(0.88f, 0.38f, 0.82f),
                        ambDay: C(0.32f, 0.24f, 0.52f),
                        ambNight: C(0.04f, 0.02f, 0.08f),
                        upper: C(0.10f, 0.06f, 0.20f),
                        rim: C(0.62f, 0.42f, 0.92f),
                        nebA: C(0.48f, 0.22f, 0.78f),
                        nebB: C(0.22f, 0.48f, 0.82f),
                        fogDensity: 0.005f, haze: 0.46f, aurora: 0.35f, dust: false);
                case PlanetSkyKind.Olympus:
                    return Make(kind,
                        zenith: C(0.22f, 0.38f, 0.62f),
                        horizon: C(0.92f, 0.84f, 0.62f),
                        fog: C(0.86f, 0.82f, 0.68f),
                        sunset: C(1.00f, 0.62f, 0.28f),
                        sunDay: C(1.00f, 0.96f, 0.84f),
                        sunSet: C(1.00f, 0.58f, 0.24f),
                        ambDay: C(0.48f, 0.50f, 0.52f),
                        ambNight: C(0.04f, 0.04f, 0.08f),
                        upper: C(0.14f, 0.22f, 0.38f),
                        rim: C(0.78f, 0.68f, 0.42f),
                        nebA: C(0.32f, 0.28f, 0.55f),
                        nebB: C(0.72f, 0.52f, 0.22f),
                        fogDensity: 0.004f, haze: 0.32f, aurora: 0f, dust: false);
                case PlanetSkyKind.Asteroid:
                    return Make(kind,
                        zenith: C(0.004f, 0.005f, 0.010f),
                        horizon: C(0.04f, 0.04f, 0.05f),
                        fog: C(0.03f, 0.03f, 0.035f),
                        sunset: C(0.70f, 0.68f, 0.62f),
                        sunDay: C(1.00f, 0.96f, 0.88f),
                        sunSet: C(0.88f, 0.78f, 0.62f),
                        ambDay: C(0.05f, 0.05f, 0.06f),
                        ambNight: C(0.008f, 0.008f, 0.012f),
                        upper: C(0.004f, 0.005f, 0.010f),
                        rim: C(0.38f, 0.36f, 0.34f),
                        nebA: C(0.28f, 0.16f, 0.42f),
                        nebB: C(0.12f, 0.32f, 0.48f),
                        fogDensity: 0f, haze: 0.04f, aurora: 0f, dust: false);
                default:
                    return Make(kind,
                        zenith: C(0.18f, 0.42f, 0.78f),
                        horizon: C(0.72f, 0.84f, 0.92f),
                        fog: C(0.70f, 0.80f, 0.90f),
                        sunset: C(1.00f, 0.55f, 0.25f),
                        sunDay: C(1.00f, 0.97f, 0.88f),
                        sunSet: C(1.00f, 0.55f, 0.25f),
                        ambDay: C(0.35f, 0.42f, 0.55f),
                        ambNight: C(0.02f, 0.03f, 0.08f),
                        upper: C(0.075f, 0.145f, 0.245f),
                        rim: C(0.18f, 0.42f, 0.78f),
                        nebA: C(0.22f, 0.28f, 0.62f),
                        nebB: C(0.55f, 0.22f, 0.48f),
                        fogDensity: 0.0035f, haze: 0.28f, aurora: 0f, dust: false);
            }
        }

        public static PlanetSkyPalette ForBody(BodySettings settings)
        {
            PlanetSkyPalette palette = ForKind(ResolveKind(settings));
            if (settings == null) return palette;
            if (settings.skyZenith.a > 0.01f) palette.Zenith = settings.skyZenith;
            if (settings.skyHorizon.a > 0.01f) palette.Horizon = settings.skyHorizon;
            if (settings.skySunset.a > 0.01f) palette.Sunset = settings.skySunset;
            if (settings.skyFog.a > 0.01f) palette.GroundFog = settings.skyFog;
            return palette;
        }

        public static PlanetSkyPalette DeepSpace()
        {
            PlanetSkyPalette palette = ForKind(PlanetSkyKind.Asteroid);
            palette.Zenith = C(0.002f, 0.004f, 0.012f);
            palette.Horizon = C(0.006f, 0.008f, 0.018f);
            palette.UpperAir = C(0.002f, 0.004f, 0.012f);
            palette.SurfaceFogDensity = 0f;
            palette.HazeStrength = 0f;
            return palette;
        }

        public static PlanetSkyPalette Lerp(in PlanetSkyPalette a, in PlanetSkyPalette b, float t)
        {
            t = Mathf.Clamp01(t);
            PlanetSkyPalette r;
            r.Kind = t < 0.5f ? a.Kind : b.Kind;
            r.Zenith = Color.Lerp(a.Zenith, b.Zenith, t);
            r.Horizon = Color.Lerp(a.Horizon, b.Horizon, t);
            r.GroundFog = Color.Lerp(a.GroundFog, b.GroundFog, t);
            r.Sunset = Color.Lerp(a.Sunset, b.Sunset, t);
            r.SunDay = Color.Lerp(a.SunDay, b.SunDay, t);
            r.SunSunset = Color.Lerp(a.SunSunset, b.SunSunset, t);
            r.AmbientDay = Color.Lerp(a.AmbientDay, b.AmbientDay, t);
            r.AmbientNight = Color.Lerp(a.AmbientNight, b.AmbientNight, t);
            r.UpperAir = Color.Lerp(a.UpperAir, b.UpperAir, t);
            r.AtmosphereRim = Color.Lerp(a.AtmosphereRim, b.AtmosphereRim, t);
            r.NebulaPrimary = Color.Lerp(a.NebulaPrimary, b.NebulaPrimary, t);
            r.NebulaSecondary = Color.Lerp(a.NebulaSecondary, b.NebulaSecondary, t);
            r.SurfaceFogDensity = Mathf.Lerp(a.SurfaceFogDensity, b.SurfaceFogDensity, t);
            r.HazeStrength = Mathf.Lerp(a.HazeStrength, b.HazeStrength, t);
            r.AuroraStrength = Mathf.Lerp(a.AuroraStrength, b.AuroraStrength, t);
            r.DustHaze = t < 0.5f ? a.DustHaze : b.DustHaze;
            return r;
        }

        private static PlanetSkyPalette Make(
            PlanetSkyKind kind,
            Color zenith, Color horizon, Color fog, Color sunset,
            Color sunDay, Color sunSet, Color ambDay, Color ambNight,
            Color upper, Color rim, Color nebA, Color nebB,
            float fogDensity, float haze, float aurora, bool dust)
        {
            PlanetSkyPalette p;
            p.Kind = kind;
            p.Zenith = zenith;
            p.Horizon = horizon;
            p.GroundFog = fog;
            p.Sunset = sunset;
            p.SunDay = sunDay;
            p.SunSunset = sunSet;
            p.AmbientDay = ambDay;
            p.AmbientNight = ambNight;
            p.UpperAir = upper;
            p.AtmosphereRim = rim;
            p.NebulaPrimary = nebA;
            p.NebulaSecondary = nebB;
            p.SurfaceFogDensity = fogDensity;
            p.HazeStrength = haze;
            p.AuroraStrength = aurora;
            p.DustHaze = dust;
            return p;
        }

        private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        private static bool Contains(string name, string token)
            => name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
