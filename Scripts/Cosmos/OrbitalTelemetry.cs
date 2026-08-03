// Assets/Scripts/VoxelEngine/Cosmos/OrbitalTelemetry.cs
//
// Pure, allocation-free orbital diagnostics for a grid's current coast path.
// The grid already uses inverse-square radial gravity; this service translates
// that real state into a readable flight-computer solution without altering
// physics, thrust, dampeners, or saves.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Cosmos
{
    public enum OrbitalFlightState
    {
        Unavailable = 0,
        Surface = 1,
        Atmospheric = 2,
        Suborbital = 3,
        Orbiting = 4,
        Escape = 5,
    }

    /// <summary>Instantaneous ballistic/coast-path solution around the active body.</summary>
    public readonly struct OrbitalTelemetrySample
    {
        public readonly bool IsAvailable;
        public readonly OrbitalFlightState State;
        public readonly float Altitude;
        public readonly float RadialSpeed;
        public readonly float TangentialSpeed;
        public readonly float CircularSpeed;
        public readonly float EscapeSpeed;
        public readonly float PeriapsisAltitude;
        public readonly float ApoapsisAltitude;
        public readonly float Eccentricity;
        public readonly float SpecificEnergy;

        internal OrbitalTelemetrySample(bool available, OrbitalFlightState state,
            float altitude, float radialSpeed, float tangentialSpeed,
            float circularSpeed, float escapeSpeed, float periapsisAltitude,
            float apoapsisAltitude, float eccentricity, float specificEnergy)
        {
            IsAvailable = available;
            State = state;
            Altitude = altitude;
            RadialSpeed = radialSpeed;
            TangentialSpeed = tangentialSpeed;
            CircularSpeed = circularSpeed;
            EscapeSpeed = escapeSpeed;
            PeriapsisAltitude = periapsisAltitude;
            ApoapsisAltitude = apoapsisAltitude;
            Eccentricity = eccentricity;
            SpecificEnergy = specificEnergy;
        }

        public bool HasBoundOrbit => State == OrbitalFlightState.Orbiting || State == OrbitalFlightState.Suborbital;
        public bool IsEscaping => State == OrbitalFlightState.Escape;
    }

    public static class OrbitalTelemetry
    {
        private const float MinimumRadius = 1f;
        private const float MinimumGravity = 0.0001f;

        /// <summary>
        /// Solves the coast path implied by the current position and velocity. Pass the grid's
        /// gravity scale so flight-computer values exactly match the construct's applied physics.
        /// </summary>
        public static OrbitalTelemetrySample Sample(Vector3 worldPosition, Vector3 worldVelocity,
            float gravityScale = 1f)
        {
            var body = GravityProvider.ActiveBody;
            if (body == null) return default;

            Vector3 radiusVector = worldPosition - body.transform.position;
            float radius = radiusVector.magnitude;
            if (radius < MinimumRadius) return default;

            GravityFieldSample gravity = GravityProvider.Sample(worldPosition, gravityScale);
            if (gravity.Magnitude < MinimumGravity) return default;

            Vector3 radialDirection = radiusVector / radius;
            float radialSpeed = Vector3.Dot(worldVelocity, radialDirection);
            Vector3 tangentialVelocity = worldVelocity - radialDirection * radialSpeed;
            float tangentialSpeed = tangentialVelocity.magnitude;
            float speedSquared = worldVelocity.sqrMagnitude;

            // μ = g(r) × r² for the same scaled inverse-square field used by GridEntity.
            float gravitationalParameter = gravity.Magnitude * radius * radius;
            if (gravitationalParameter < MinimumGravity) return default;

            float circularSpeed = Mathf.Sqrt(gravitationalParameter / radius);
            float escapeSpeed = Mathf.Sqrt(2f * gravitationalParameter / radius);
            float specificEnergy = speedSquared * 0.5f - gravitationalParameter / radius;
            float altitude = body.AltitudeAt(worldPosition);

            Vector3 angularMomentum = Vector3.Cross(radiusVector, worldVelocity);
            Vector3 eccentricityVector = Vector3.Cross(worldVelocity, angularMomentum) / gravitationalParameter - radialDirection;
            float eccentricity = eccentricityVector.magnitude;

            float periapsisAltitude = float.NaN;
            float apoapsisAltitude = float.NaN;
            bool bound = specificEnergy < -0.0001f;
            if (bound)
            {
                float semiMajorAxis = -gravitationalParameter / (2f * specificEnergy);
                float periapsisRadius = semiMajorAxis * Mathf.Max(0f, 1f - eccentricity);
                float apoapsisRadius = semiMajorAxis * (1f + eccentricity);
                periapsisAltitude = periapsisRadius - body.SurfaceRadius;
                apoapsisAltitude = apoapsisRadius - body.SurfaceRadius;
            }

            OrbitalFlightState state;
            float surfaceClearance = Mathf.Max(8f, body.SurfaceRadius * 0.01f);
            if (altitude <= surfaceClearance)
            {
                state = OrbitalFlightState.Surface;
            }
            else if (!AtmosphereManager.IsInSpace(worldPosition))
            {
                state = OrbitalFlightState.Atmospheric;
            }
            else if (!bound)
            {
                state = OrbitalFlightState.Escape;
            }
            else if (!IsFinite(periapsisAltitude) || periapsisAltitude <= surfaceClearance)
            {
                state = OrbitalFlightState.Suborbital;
            }
            else
            {
                state = OrbitalFlightState.Orbiting;
            }

            return new OrbitalTelemetrySample(true, state, altitude, radialSpeed,
                tangentialSpeed, circularSpeed, escapeSpeed, periapsisAltitude,
                apoapsisAltitude, eccentricity, specificEnergy);
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
