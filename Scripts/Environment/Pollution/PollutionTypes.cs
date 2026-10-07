// Assets/Scripts/VoxelEngine/Environment/Pollution/PollutionTypes.cs
//
// Small serializable value types shared by the pollution simulation, save sidecar,
// multiplayer snapshot and map renderer. Airborne smog and runoff are live channels;
// climate load and orbital debris keep the file/wire shape extension-safe for later phases.

using System;
using UnityEngine;

namespace VoxelEngine.Environment
{
    [Serializable]
    public struct PollutionLoad
    {
        [Min(0f)] public float airborneSmog;
        [Min(0f)] public float runoff;
        [Min(0f)] public float climateLoad;
        [Min(0f)] public float orbitalDebris;

        public float Total => Mathf.Max(0f, airborneSmog) + Mathf.Max(0f, runoff)
            + Mathf.Max(0f, climateLoad) + Mathf.Max(0f, orbitalDebris);

        public static PollutionLoad operator +(PollutionLoad a, PollutionLoad b) => new()
        {
            airborneSmog = a.airborneSmog + b.airborneSmog,
            runoff = a.runoff + b.runoff,
            climateLoad = a.climateLoad + b.climateLoad,
            orbitalDebris = a.orbitalDebris + b.orbitalDebris,
        };

        public static PollutionLoad operator *(PollutionLoad value, float scale) => new()
        {
            airborneSmog = value.airborneSmog * scale,
            runoff = value.runoff * scale,
            climateLoad = value.climateLoad * scale,
            orbitalDebris = value.orbitalDebris * scale,
        };
    }

    /// <summary>
    /// Scientific display conversion for the simulation's compact pollution units.
    /// One simulation unit represents 0.1 kg of particulate-matter-equivalent industrial
    /// emissions (PM-eq), or the same mass scale of contaminant-equivalent runoff.
    /// Gameplay remains tuned in sparse units while player-facing values use SI mass.
    /// </summary>
    public static class PollutionUnits
    {
        public const float KilogramsPerUnit = 0.1f;

        public static float ToKilograms(float units) => Mathf.Max(0f, units) * KilogramsPerUnit;

        public static string FormatRate(float unitsPerSecond)
        {
            float kgPerSecond = ToKilograms(unitsPerSecond);
            if (kgPerSecond >= 1f) return $"{kgPerSecond:0.##} kg/s PM-eq";
            float gramsPerSecond = kgPerSecond * 1000f;
            if (gramsPerSecond >= 1f) return $"{gramsPerSecond:0.#} g/s PM-eq";
            return $"{gramsPerSecond * 1000f:0.#} mg/s PM-eq";
        }

        public static string FormatContaminantRate(float unitsPerSecond)
            => FormatEquivalentRate(unitsPerSecond, "contaminant-eq");

        public static string FormatContaminantMass(float units)
            => FormatEquivalentMass(units, "contaminant-eq");

        private static string FormatEquivalentRate(float unitsPerSecond, string equivalent)
        {
            float kgPerSecond = ToKilograms(unitsPerSecond);
            if (kgPerSecond >= 1f) return $"{kgPerSecond:0.##} kg/s {equivalent}";
            float gramsPerSecond = kgPerSecond * 1000f;
            if (gramsPerSecond >= 1f) return $"{gramsPerSecond:0.#} g/s {equivalent}";
            return $"{gramsPerSecond * 1000f:0.#} mg/s {equivalent}";
        }

        public static string FormatMass(float units) => FormatEquivalentMass(units, "PM-eq");

        private static string FormatEquivalentMass(float units, string equivalent)
        {
            float kilograms = ToKilograms(units);
            if (kilograms >= 1000f) return $"{kilograms / 1000f:0.##} t {equivalent}";
            if (kilograms >= 1f) return $"{kilograms:0.##} kg {equivalent}";
            float grams = kilograms * 1000f;
            return grams >= 1f ? $"{grams:0.#} g {equivalent}" : $"{grams * 1000f:0.#} mg {equivalent}";
        }
    }

    [Serializable]
    public sealed class PollutionCellRecord
    {
        public string bodyName;
        public int x;
        public int y;
        public int z;
        public PollutionLoad load;
    }

    [Serializable]
    public sealed class PollutionBodyRecord
    {
        public string bodyName;
        public float airborneBurden01;
        public float runoffBurden01;
        public float climateBurden01;
        public float orbitalDebrisBurden01;
    }

    [Serializable]
    public sealed class PollutionStateData
    {
        public int formatVersion = 1;
        public float cellSizeMetres = 64f;
        public long revision;
        public PollutionCellRecord[] cells = Array.Empty<PollutionCellRecord>();
        public PollutionBodyRecord[] bodies = Array.Empty<PollutionBodyRecord>();
    }

    public readonly struct PollutionMapCell
    {
        public readonly Vector3 World;
        public readonly float Intensity01;
        public readonly float Runoff01;
        public readonly float SizeMetres;
        public readonly float AirborneUnits;
        public readonly float RunoffUnits;

        public PollutionMapCell(Vector3 world, float intensity01, float runoff01, float sizeMetres,
            float airborneUnits = 0f, float runoffUnits = 0f)
        {
            World = world;
            Intensity01 = Mathf.Clamp01(intensity01);
            Runoff01 = Mathf.Clamp01(runoff01);
            SizeMetres = Mathf.Max(1f, sizeMetres);
            AirborneUnits = Mathf.Max(0f, airborneUnits);
            RunoffUnits = Mathf.Max(0f, runoffUnits);
        }
    }

    /// <summary>One live machine/outlet contributing near a map cell.</summary>
    public readonly struct PollutionSourceReading
    {
        public readonly string Name;
        public readonly Vector3 World;
        public readonly float AirbornePerSecond;
        public readonly float RunoffPerSecond;

        public PollutionSourceReading(string name, Vector3 world,
            float airbornePerSecond, float runoffPerSecond)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Industrial Source" : name;
            World = world;
            AirbornePerSecond = Mathf.Max(0f, airbornePerSecond);
            RunoffPerSecond = Mathf.Max(0f, runoffPerSecond);
        }

        public float TotalPerSecond => AirbornePerSecond + RunoffPerSecond;
    }

    public readonly struct PollutionTelemetry
    {
        public readonly float LocalAir01;
        public readonly float BodyAir01;
        public readonly float LocalRunoff01;
        public readonly float BodyRunoff01;
        public readonly float TrendPerMinute;
        public readonly float RunoffTrendPerMinute;
        public readonly int ActiveCells;

        public PollutionTelemetry(float localAir01, float bodyAir01,
            float localRunoff01, float bodyRunoff01,
            float trendPerMinute, float runoffTrendPerMinute, int activeCells)
        {
            LocalAir01 = Mathf.Clamp01(localAir01);
            BodyAir01 = Mathf.Clamp01(bodyAir01);
            LocalRunoff01 = Mathf.Clamp01(localRunoff01);
            BodyRunoff01 = Mathf.Clamp01(bodyRunoff01);
            TrendPerMinute = trendPerMinute;
            RunoffTrendPerMinute = runoffTrendPerMinute;
            ActiveCells = Mathf.Max(0, activeCells);
        }

        public string Band => LocalAir01 switch
        {
            < 0.05f => "CLEAR",
            < 0.20f => "TRACE",
            < 0.45f => "HAZE",
            < 0.70f => "SMOG",
            _ => "SEVERE",
        };

        public string RunoffBand => LocalRunoff01 switch
        {
            < 0.03f => "CLEAN",
            < 0.18f => "TRACE",
            < 0.42f => "TAINTED",
            < 0.70f => "TOXIC",
            _ => "SEVERE",
        };
    }
}
