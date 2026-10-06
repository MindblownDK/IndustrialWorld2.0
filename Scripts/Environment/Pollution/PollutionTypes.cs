// Assets/Scripts/VoxelEngine/Environment/Pollution/PollutionTypes.cs
//
// Small serializable value types shared by the pollution simulation, save sidecar,
// multiplayer snapshot and map renderer. Airborne smog is the first live channel;
// the remaining fields keep the file/wire shape extension-safe for later phases.

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
    /// emissions (PM-eq). Gameplay remains tuned in sparse units while every player-facing
    /// value uses SI mass and mass-flow units.
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

        public static string FormatMass(float units)
        {
            float kilograms = ToKilograms(units);
            if (kilograms >= 1000f) return $"{kilograms / 1000f:0.##} t PM-eq";
            if (kilograms >= 1f) return $"{kilograms:0.##} kg PM-eq";
            float grams = kilograms * 1000f;
            return grams >= 1f ? $"{grams:0.#} g PM-eq" : $"{grams * 1000f:0.#} mg PM-eq";
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
        public readonly float SizeMetres;

        public PollutionMapCell(Vector3 world, float intensity01, float sizeMetres)
        {
            World = world;
            Intensity01 = Mathf.Clamp01(intensity01);
            SizeMetres = Mathf.Max(1f, sizeMetres);
        }
    }

    public readonly struct PollutionTelemetry
    {
        public readonly float LocalAir01;
        public readonly float BodyAir01;
        public readonly float TrendPerMinute;
        public readonly int ActiveCells;

        public PollutionTelemetry(float localAir01, float bodyAir01, float trendPerMinute, int activeCells)
        {
            LocalAir01 = Mathf.Clamp01(localAir01);
            BodyAir01 = Mathf.Clamp01(bodyAir01);
            TrendPerMinute = trendPerMinute;
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
    }
}
