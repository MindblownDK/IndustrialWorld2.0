using UnityEngine;

namespace IndustrialWorld.Navigation
{
    /// <summary>Conservative low-speed pursuit and stopping rules, independent of scene state.</summary>
    public static class RoadWheelMath
    {
        public const float CruiseSpeed = 4f;
        public const float BrakeAcceleration = 6f;
        public const float StartDelay = 5f;

        public static float StopDistance(float speed, float deceleration)
            => speed * speed / (2f * Mathf.Max(0.1f, deceleration)) + Mathf.Abs(speed) * 0.5f + 2f;

        public static float Steering(Vector3 forward, Vector3 toTarget, Vector3 up,
            float wheelbase, float maxAngle)
        {
            Vector3 direction = Vector3.ProjectOnPlane(toTarget, up);
            float distance = Mathf.Max(1f, direction.magnitude);
            float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(forward, up), direction, up) * Mathf.Deg2Rad;
            float wheelAngle = Mathf.Atan2(2f * wheelbase * Mathf.Sin(angle), distance) * Mathf.Rad2Deg;
            return Mathf.Clamp(wheelAngle / Mathf.Max(1f, maxAngle), -1f, 1f);
        }

        public static float SpeedLimit(float remaining, float steer, float deceleration)
            => Mathf.Min(CruiseSpeed * Mathf.Lerp(1f, 0.3f, Mathf.Abs(steer)),
                Mathf.Sqrt(2f * Mathf.Max(0.1f, deceleration) * Mathf.Max(0f, remaining - 1f)));
    }
}
