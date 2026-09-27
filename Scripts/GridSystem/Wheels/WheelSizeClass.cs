// Assets/Scripts/VoxelEngine/GridSystem/Wheels/WheelSizeClass.cs
//
// ONE TABLE, THREE SIZES.
//
// Every tunable a hub or a tire needs comes from this file. A designer picks a
// size class in the inspector and the whole rig — collider radius, spring rate,
// damper rate, torque curve, steering authority, tire mass — re-derives itself
// from the authored reference row. Nothing else in the wheel system hard-codes a
// number that depends on how big the wheel is, which is what makes 2x2, 3x3 and
// 5x5 behave like the same machine at three scales instead of three machines.
//
// Reference row is authored for the Large grid cell (2.5 m). A different cell
// size scales lengths linearly and forces with the cube-ish mass growth the grid
// itself already uses, so a rig never gains or loses ride height on a size swap.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    /// <summary>Authored wheel footprints. The numeric value is the cell span.</summary>
    public enum WheelSizeClass
    {
        Size_2x2 = 2,
        Size_3x3 = 3,
        Size_5x5 = 5
    }

    /// <summary>Fully resolved tuning for one wheel size at one cell size.</summary>
    public readonly struct WheelPreset
    {
        public readonly WheelSizeClass SizeClass;
        public readonly int Cells;

        // Geometry (metres)
        public readonly float TireRadius;
        public readonly float TireWidth;
        public readonly float TreadDepth;
        public readonly float HubRadius;

        // Suspension
        public readonly float SpringStrength;   // N per metre of compression
        public readonly float DamperRate;       // N per m/s
        public readonly float RestLength;       // m, ride height target
        public readonly float MinTravel;        // m, hard bump stop
        public readonly float MaxTravel;        // m, full droop

        // Drive
        public readonly float MotorTorque;      // N·m at the axle
        public readonly float BrakeTorque;      // N·m
        public readonly float HandbrakeTorque;  // N·m
        public readonly float PowerDrawWatts;

        // Steering
        public readonly float MaxSteerAngle;    // degrees
        public readonly float SteerSpeed;       // degrees per second toward target
        public readonly float SteerReturnSpeed; // degrees per second back to centre

        // Tire
        public readonly float StaticFriction;
        public readonly float DynamicFriction;
        public readonly float LateralGrip;
        public readonly float RollingResistance;

        // Mass
        public readonly float HubMass;
        public readonly float TireMass;

        public WheelPreset(WheelSizeClass sizeClass, int cells, float tireRadius, float tireWidth,
            float treadDepth, float hubRadius, float springStrength, float damperRate, float restLength,
            float minTravel, float maxTravel, float motorTorque, float brakeTorque, float handbrakeTorque,
            float powerDrawWatts, float maxSteerAngle, float steerSpeed, float steerReturnSpeed,
            float staticFriction, float dynamicFriction, float lateralGrip, float rollingResistance,
            float hubMass, float tireMass)
        {
            SizeClass = sizeClass; Cells = cells;
            TireRadius = tireRadius; TireWidth = tireWidth; TreadDepth = treadDepth; HubRadius = hubRadius;
            SpringStrength = springStrength; DamperRate = damperRate; RestLength = restLength;
            MinTravel = minTravel; MaxTravel = maxTravel;
            MotorTorque = motorTorque; BrakeTorque = brakeTorque; HandbrakeTorque = handbrakeTorque;
            PowerDrawWatts = powerDrawWatts;
            MaxSteerAngle = maxSteerAngle; SteerSpeed = steerSpeed; SteerReturnSpeed = steerReturnSpeed;
            StaticFriction = staticFriction; DynamicFriction = dynamicFriction;
            LateralGrip = lateralGrip; RollingResistance = rollingResistance;
            HubMass = hubMass; TireMass = tireMass;
        }
    }

    /// <summary>Preset table plus the conversions every wheel script shares.</summary>
    public static class WheelTuning
    {
        /// <summary>Cell size the reference rows below were authored against.</summary>
        public const float ReferenceCellSize = 2.5f;

        public static int Cells(this WheelSizeClass sizeClass) => (int)sizeClass;

        public static string Label(this WheelSizeClass sizeClass)
        {
            int c = (int)sizeClass;
            return $"{c}x{c}";
        }

        /// <summary>Nearest authored size for a legacy cell-span integer.</summary>
        public static WheelSizeClass FromCells(int cells)
        {
            if (cells <= 2) return WheelSizeClass.Size_2x2;
            return cells >= 5 ? WheelSizeClass.Size_5x5 : WheelSizeClass.Size_3x3;
        }

        /// <summary>Resolved tuning for a size class at a given grid cell size.</summary>
        public static WheelPreset For(WheelSizeClass sizeClass, float cellSize = ReferenceCellSize)
        {
            float s = Mathf.Max(0.1f, cellSize) / ReferenceCellSize; // length scale
            float f = s * s;                                        // force scale (area/mass-like)

            switch (sizeClass)
            {
                case WheelSizeClass.Size_2x2:
                    return new WheelPreset(sizeClass, 2,
                        tireRadius: 2.50f * s, tireWidth: 0.90f * s, treadDepth: 0.10f * s, hubRadius: 0.75f * s,
                        springStrength: 185_000f * f, damperRate: 21_000f * f,
                        restLength: 0.80f * s, minTravel: 0.14f * s, maxTravel: 1.15f * s,
                        motorTorque: 560_000f * f, brakeTorque: 780_000f * f, handbrakeTorque: 1_250_000f * f,
                        powerDrawWatts: 450f,
                        maxSteerAngle: 38f, steerSpeed: 130f, steerReturnSpeed: 180f,
                        staticFriction: 1.15f, dynamicFriction: 0.95f, lateralGrip: 1.00f, rollingResistance: 0.022f,
                        hubMass: 1_900f, tireMass: 1_300f);

                case WheelSizeClass.Size_5x5:
                    return new WheelPreset(sizeClass, 5,
                        tireRadius: 6.25f * s, tireWidth: 2.05f * s, treadDepth: 0.26f * s, hubRadius: 1.55f * s,
                        springStrength: 470_000f * f, damperRate: 54_000f * f,
                        restLength: 1.85f * s, minTravel: 0.32f * s, maxTravel: 2.60f * s,
                        motorTorque: 8_400_000f * f, brakeTorque: 11_500_000f * f, handbrakeTorque: 18_000_000f * f,
                        powerDrawWatts: 1800f,
                        maxSteerAngle: 24f, steerSpeed: 62f, steerReturnSpeed: 95f,
                        staticFriction: 1.05f, dynamicFriction: 0.90f, lateralGrip: 1.25f, rollingResistance: 0.030f,
                        hubMass: 7_400f, tireMass: 4_600f);

                default:
                    return new WheelPreset(WheelSizeClass.Size_3x3, 3,
                        tireRadius: 3.75f * s, tireWidth: 1.35f * s, treadDepth: 0.17f * s, hubRadius: 1.10f * s,
                        springStrength: 300_000f * f, damperRate: 34_000f * f,
                        restLength: 1.25f * s, minTravel: 0.22f * s, maxTravel: 1.80f * s,
                        motorTorque: 2_000_000f * f, brakeTorque: 2_750_000f * f, handbrakeTorque: 4_300_000f * f,
                        powerDrawWatts: 900f,
                        maxSteerAngle: 32f, steerSpeed: 92f, steerReturnSpeed: 135f,
                        staticFriction: 1.10f, dynamicFriction: 0.92f, lateralGrip: 1.12f, rollingResistance: 0.026f,
                        hubMass: 4_100f, tireMass: 2_700f);
            }
        }

        /// <summary>
        /// Mix-and-match correction. A hub carries the spring rate its own frame was
        /// built for; bolting a bigger tire on raises the ride height and the unsprung
        /// mass, so the spring and the damper are re-rated by the radius ratio instead
        /// of letting the rig either bottom out or pogo.
        /// </summary>
        public static float MountRatio(WheelSizeClass hub, WheelSizeClass tire)
            => Mathf.Clamp((int)tire / Mathf.Max(1f, (int)hub), 0.4f, 2.5f);
    }
}
