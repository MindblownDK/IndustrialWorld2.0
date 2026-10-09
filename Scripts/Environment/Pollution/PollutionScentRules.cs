// Assets/Scripts/VoxelEngine/Environment/Pollution/PollutionScentRules.cs
//
// Save-compatible rules for how far an active industrial source can recruit
// surface scouts, how large that arrival is, and how a map or notice points
// at the outlet. Historical attribution, organized waves, elites and siege
// creatures are intentionally not decided here.

using UnityEngine;

namespace VoxelEngine.Environment
{
    public static class PollutionScentRules
    {
        public const float RangeScaleAtFullPressure = 2f;
        public const float DetectionScaleAtFullPressure = 1.75f;
        public const int MaxPackSize = 3;
        public const float HereDistanceMetres = 4f;
        public const float AmbushPressure = 0.25f;
        public const float MinAmbushSeparationMetres = 18f;
        public const float AmbushApproachFraction = 0.55f;
        public const float AmbushLateralMetres = 8f;
        public const float AmbushHoldSeconds = 45f;

        /// <summary>
        /// Authored attraction radius at no pressure, up to twice that radius
        /// when the source cell is fully pressured. The authored value is scaled,
        /// never replaced.
        /// </summary>
        public static float EscalatedRange(float authoredRange, float pressure01)
        {
            float authored = Mathf.Max(1f, authoredRange);
            return authored * Mathf.Lerp(1f, RangeScaleAtFullPressure, Mathf.Clamp01(pressure01));
        }

        public static float SearchCeiling(float authoredRange) => EscalatedRange(authoredRange, 1f);

        /// <summary>
        /// Isolated scout below the stressed band, a pair through moderate pressure,
        /// and a capped trio once pressure is high. This is not a wave.
        /// </summary>
        public static int PackSize(float pressure01)
        {
            float pressure = Mathf.Clamp01(pressure01);
            if (pressure < 0.25f) return 1;
            if (pressure < 0.50f) return 2;
            return MaxPackSize;
        }

        /// <summary>
        /// One ambusher from a pack, never from a lone scout, and never the whole
        /// pack. Below the stressed band there is no ambush.
        /// </summary>
        public static int AmbushCount(float pressure01, int packSize)
        {
            if (packSize < 2 || pressure01 < AmbushPressure) return 0;
            return 1;
        }

        public static bool RaidsLogistics(float pressure01) => pressure01 >= AmbushPressure;

        /// <summary>
        /// A point on the approach from the source toward the focus, offset to one
        /// side. Too close to the source and the ambush would spawn on the player.
        /// </summary>
        public static bool TryAmbushPoint(Vector3 source, Vector3 focus, Vector3 up, int slot, out Vector3 point)
        {
            point = source;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            else up.Normalize();

            Vector3 flat = Vector3.ProjectOnPlane(focus - source, up);
            if (flat.sqrMagnitude < MinAmbushSeparationMetres * MinAmbushSeparationMetres) return false;
            Vector3 forward = flat.normalized;
            Vector3 lateral = Vector3.Cross(up, forward);
            if (lateral.sqrMagnitude < 0.0001f) return false;
            lateral.Normalize();
            float side = (slot & 1) == 0 ? 1f : -1f;
            point = source
                + forward * (flat.magnitude * AmbushApproachFraction)
                + lateral * (side * AmbushLateralMetres);
            return true;
        }

        public static float EscalatedDetectionRange(float authoredDetect, float pressure01)
        {
            float authored = Mathf.Max(1f, authoredDetect);
            return Mathf.Lerp(authored, authored * DetectionScaleAtFullPressure, Mathf.Clamp01(pressure01));
        }

        public static string FormatDirection(Vector3 from, Vector3 to, string hereLabel = "AT CELL")
        {
            ResolveSurfaceFrame(from, out Vector3 up, out Vector3 north);
            return FormatDirection(from, to, up, north, hereLabel);
        }

        public static string FormatDirection(Vector3 from, Vector3 to, Vector3 up, Vector3 north, string hereLabel = "AT CELL")
        {
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            else up.Normalize();

            Vector3 tangent = Vector3.ProjectOnPlane(to - from, up);
            float metres = tangent.magnitude;
            if (metres < HereDistanceMetres)
                return string.IsNullOrEmpty(hereLabel) ? "HERE" : hereLabel;

            north = Vector3.ProjectOnPlane(north, up);
            if (north.sqrMagnitude < 0.0001f) return $"{metres:0} m";
            north.Normalize();
            Vector3 east = Vector3.Cross(up, north);
            if (east.sqrMagnitude < 0.0001f) return $"{metres:0} m";
            east.Normalize();

            float angle = Mathf.Atan2(Vector3.Dot(tangent, east), Vector3.Dot(tangent, north)) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            return $"{Cardinal(angle)} · {metres:0} m";
        }

        /// <summary>
        /// Same tangent north as the logistics map: the body's forward, then up,
        /// then right, so a bearing on the hover card agrees with the sheet.
        /// </summary>
        public static void ResolveSurfaceFrame(Vector3 origin, out Vector3 up, out Vector3 north)
        {
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            up = body != null ? VoxelEngine.Cosmos.GravityProvider.GetUp(origin) : Vector3.up;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            else up.Normalize();

            north = Vector3.zero;
            if (body != null)
            {
                north = Vector3.ProjectOnPlane(body.transform.forward, up);
                if (north.sqrMagnitude < 0.0001f)
                    north = Vector3.ProjectOnPlane(body.transform.up, up);
                if (north.sqrMagnitude < 0.0001f)
                    north = Vector3.ProjectOnPlane(body.transform.right, up);
            }
            if (north.sqrMagnitude < 0.0001f)
                north = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (north.sqrMagnitude < 0.0001f)
                north = Vector3.ProjectOnPlane(Vector3.right, up);
            if (north.sqrMagnitude < 0.0001f) north = Vector3.forward;
            else north.Normalize();
        }

        public static string Cardinal(float angleDegrees)
        {
            int index = Mathf.RoundToInt(Mathf.Repeat(angleDegrees, 360f) / 45f) % 8;
            switch (index)
            {
                case 0: return "N";
                case 1: return "NE";
                case 2: return "E";
                case 3: return "SE";
                case 4: return "S";
                case 5: return "SW";
                case 6: return "W";
                default: return "NW";
            }
        }
    }
}
