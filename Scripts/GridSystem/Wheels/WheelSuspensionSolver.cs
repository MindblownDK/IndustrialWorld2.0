// Assets/Scripts/VoxelEngine/GridSystem/Wheels/WheelSuspensionSolver.cs
//
// THE WHEEL, WITHOUT A WHEELCOLLIDER.
//
// One raycast spring per hub plus an explicit tire force model. No
// UnityEngine.WheelCollider anywhere in this system: a WheelCollider owns its own
// hidden substep integrator, refuses to live under a re-parented block, and its
// friction curves cannot be driven per-surface from a ScriptableObject — all three
// are requirements here, so the contact patch is solved by hand.
//
// The solver is deliberately a pure function over a struct: it never touches a
// Transform, never allocates, and can be unit-tested or replayed. The hub owns the
// scene graph; this file owns the physics.
//
// Force model per grounded wheel, all clamped by the friction circle of its own
// normal load so nothing can push harder than the ground can hold:
//   suspension : F = (rest - length) * k  -  v_along_axis * c        (never negative)
//   longitudinal: motor torque / radius, braking, rolling resistance
//   lateral     : the impulse that would cancel sideways slip in one step
// Slip is reported, not faked: whatever the friction circle refuses to deliver is
// exactly the slip value the FX and audio hooks read.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    /// <summary>Everything the solver needs for one wheel in one FixedUpdate.</summary>
    public struct WheelSolveInput
    {
        public Rigidbody Body;
        public Vector3 Origin;          // suspension top, world space
        public Vector3 SuspensionDown;  // unit, direction the wheel travels to extend
        public Vector3 Forward;         // unit, steered rolling direction
        public Vector3 Right;           // unit, lateral axis
        public Vector3 GravityAccel;    // world gravity acting on the grid

        public float Radius;
        public float CastRadius;
        public float RestLength;
        public float MinTravel;
        public float MaxTravel;
        public float SpringStrength;
        public float DamperRate;

        public float Throttle;          // -1..1
        public float Brake;             // 0..1
        public float Handbrake;         // 0..1
        public float MotorTorque;
        public float BrakeTorque;
        public float HandbrakeTorque;
        public bool  Powered;

        public float StaticFriction;
        public float DynamicFriction;
        public float LateralGrip;

        public float SurfaceForward;    // SurfaceProfile multipliers
        public float SurfaceLateral;
        public float SurfaceRolling;

        public float MassShare;         // grid mass carried by this wheel
        public float DeltaTime;
        public int   IgnoreLayerMask;
        public GridEntity OwnerGrid;
    }

    /// <summary>Per-wheel state that must survive between steps.</summary>
    public struct WheelSolveState
    {
        public float LastLength;
        public float SpinDegrees;
        public float SpinRateDegPerSec;
        public bool  Initialised;
    }

    /// <summary>Forces and telemetry produced for one wheel.</summary>
    public struct WheelSolveOutput
    {
        public bool Grounded;
        public RaycastHit Hit;
        public float SpringLength;      // current extension, m
        public float Compression01;     // 0 = full droop, 1 = bump stop
        public float NormalLoad;        // N
        public Vector3 SuspensionForce;
        public Vector3 TireForce;
        public Vector3 ContactPoint;
        public float ForwardSpeed;      // m/s along the rolling axis
        public float LateralSpeed;      // m/s across it
        public float DriveSlip01;       // torque the ground refused
        public float LateralSlip01;     // sideways scrub
        public float Slip01;            // combined, for FX / audio
    }

    public static class WheelSuspensionSolver
    {
        private static readonly RaycastHit[] s_hits = new RaycastHit[12];

        /// <summary>Solves one wheel. Applies nothing — the caller decides.</summary>
        public static void Solve(in WheelSolveInput input, ref WheelSolveState state, out WheelSolveOutput output)
        {
            output = default;
            if (input.Body == null || input.DeltaTime <= 0f)
                return;

            float dt = input.DeltaTime;
            Vector3 down = input.SuspensionDown.sqrMagnitude > 0.0001f ? input.SuspensionDown.normalized : Vector3.down;
            Vector3 up = -down;
            float radius = Mathf.Max(0.05f, input.Radius);
            float maxTravel = Mathf.Max(input.MinTravel + 0.01f, input.MaxTravel);

            if (!state.Initialised)
            {
                state.LastLength = input.RestLength;
                state.Initialised = true;
            }

            if (!Probe(input, down, radius, maxTravel, out var hit))
            {
                // Airborne: the spring relaxes to full droop and the tire free-spins down.
                state.LastLength = Mathf.MoveTowards(state.LastLength, maxTravel, 6f * dt);
                state.SpinRateDegPerSec = Mathf.MoveTowards(state.SpinRateDegPerSec, 0f, 240f * dt);
                state.SpinDegrees += state.SpinRateDegPerSec * dt;
                output.SpringLength = state.LastLength;
                output.Compression01 = 0f;
                return;
            }

            output.Grounded = true;
            output.Hit = hit;
            output.ContactPoint = hit.point;

            float length = Mathf.Clamp(hit.distance - radius, input.MinTravel, maxTravel);
            float compressionMetres = Mathf.Max(0f, input.RestLength - length);
            float travelRange = Mathf.Max(0.001f, maxTravel - input.MinTravel);
            output.SpringLength = length;
            output.Compression01 = Mathf.Clamp01((maxTravel - length) / travelRange);

            // Damping reads the actual point velocity along the suspension axis rather than
            // differentiating the ray length: a ray that steps onto a kerb changes length
            // instantly and a length-derivative damper would answer with a launch impulse.
            Vector3 pointVelocity = input.Body.GetPointVelocity(input.Origin);
            float axisVelocity = Vector3.Dot(pointVelocity, up);
            float spring = compressionMetres * input.SpringStrength;
            float damper = axisVelocity * input.DamperRate;
            float normal = Mathf.Max(0f, spring - damper);

            // Bump stop: below MinTravel the frame is on the rubber, not the spring.
            if (length <= input.MinTravel + 0.001f)
                normal += (input.MinTravel + 0.001f - length) * input.SpringStrength * 4f;

            output.NormalLoad = normal;
            output.SuspensionForce = up * normal;

            // ── Contact frame ────────────────────────────────────────────────
            Vector3 groundNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : up;
            Vector3 forward = Vector3.ProjectOnPlane(input.Forward, groundNormal);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(input.Right, groundNormal);
            if (forward.sqrMagnitude < 0.0001f) { output.TireForce = Vector3.zero; return; }
            forward.Normalize();
            Vector3 lateral = Vector3.Cross(groundNormal, forward).normalized;

            Vector3 contactVelocity = input.Body.GetPointVelocity(hit.point);
            float vForward = Vector3.Dot(contactVelocity, forward);
            float vLateral = Vector3.Dot(contactVelocity, lateral);
            output.ForwardSpeed = vForward;
            output.LateralSpeed = vLateral;

            float muForward = Mathf.Max(0.02f, input.DynamicFriction * Mathf.Max(0.02f, input.SurfaceForward));
            float muLateral = Mathf.Max(0.02f, input.DynamicFriction * input.LateralGrip * Mathf.Max(0.02f, input.SurfaceLateral));
            float muStatic  = Mathf.Max(muForward, input.StaticFriction * Mathf.Max(0.02f, input.SurfaceForward));

            float gripForward = muForward * normal;
            float gripLateral = muLateral * normal;
            float massShare = Mathf.Max(1f, input.MassShare);

            // ── Longitudinal ────────────────────────────────────────────────
            float driveRequest = 0f;
            if (input.Powered && Mathf.Abs(input.Throttle) > 0.01f)
                driveRequest = Mathf.Clamp(input.Throttle, -1f, 1f) * (input.MotorTorque / radius);

            float drive = Mathf.Clamp(driveRequest, -gripForward, gripForward);
            output.DriveSlip01 = Mathf.Abs(driveRequest) > 1f
                ? Mathf.Clamp01(1f - Mathf.Abs(drive) / Mathf.Abs(driveRequest))
                : 0f;

            // Braking is an acceleration request, capped by both the pad and the ground:
            // a locked wheel on ice must slide, not stop the vehicle dead.
            float brake01 = Mathf.Clamp01(Mathf.Max(input.Brake, input.Handbrake));
            float brakeForce = 0f;
            if (brake01 > 0.001f)
            {
                float pad = Mathf.Max(input.Brake * input.BrakeTorque, input.Handbrake * input.HandbrakeTorque) / radius;
                float stopping = Mathf.Abs(vForward) * massShare / dt;
                // Hill hold: cancel the component of gravity trying to roll the wheel away.
                float slope = -Vector3.Dot(input.GravityAccel, forward) * massShare;
                brakeForce = Mathf.Min(pad, stopping + Mathf.Abs(slope));
                brakeForce = Mathf.Min(brakeForce, muStatic * normal);
                brakeForce *= brake01;
                brakeForce *= -Mathf.Sign(vForward == 0f ? Mathf.Sign(slope) : vForward);
                if (Mathf.Abs(vForward) < 0.05f && Mathf.Abs(slope) > 0.01f)
                    brakeForce = Mathf.Clamp(slope, -muStatic * normal, muStatic * normal) * brake01;
                drive *= 1f - brake01;
            }

            // Rolling resistance always opposes motion and never creates it.
            float rolling = -Mathf.Sign(vForward) * Mathf.Min(
                Mathf.Abs(vForward) * massShare / dt,
                input.SurfaceRolling * normal);

            float longitudinal = Mathf.Clamp(drive + brakeForce + rolling, -gripForward * 1.2f, gripForward * 1.2f);

            // ── Lateral ─────────────────────────────────────────────────────
            float lateralRequest = -vLateral * massShare / dt;
            float lateralForce = Mathf.Clamp(lateralRequest, -gripLateral, gripLateral);
            output.LateralSlip01 = Mathf.Abs(lateralRequest) > 1f
                ? Mathf.Clamp01(1f - Mathf.Abs(lateralForce) / Mathf.Abs(lateralRequest))
                : 0f;

            // ── Friction circle ─────────────────────────────────────────────
            Vector3 tire = forward * longitudinal + lateral * lateralForce;
            float circle = Mathf.Max(gripForward, gripLateral);
            if (circle > 0.01f && tire.magnitude > circle)
            {
                float scale = circle / tire.magnitude;
                tire *= scale;
                output.DriveSlip01 = Mathf.Max(output.DriveSlip01, 1f - scale);
            }
            output.TireForce = tire;
            output.Slip01 = Mathf.Clamp01(Mathf.Max(output.DriveSlip01, output.LateralSlip01));

            // ── Visual spin ─────────────────────────────────────────────────
            // Rolling speed plus wheelspin: a slipping driven wheel turns faster than the
            // ground under it, which is the only visual cue that traction was lost.
            float rollDeg = (vForward / radius) * Mathf.Rad2Deg;
            float spinBoost = output.DriveSlip01 * Mathf.Sign(driveRequest) * 240f;
            state.SpinRateDegPerSec = Mathf.Lerp(state.SpinRateDegPerSec, rollDeg + spinBoost, 1f - Mathf.Exp(-12f * dt));
            state.SpinDegrees += state.SpinRateDegPerSec * dt;
            if (state.SpinDegrees > 360f || state.SpinDegrees < -360f)
                state.SpinDegrees = Mathf.Repeat(state.SpinDegrees, 360f);
            state.LastLength = length;
        }

        /// <summary>
        /// Spherecast down the suspension axis, skipping our own grid. Uses the
        /// non-allocating overload so a 20-wheel convoy costs no garbage per step.
        /// </summary>
        private static bool Probe(in WheelSolveInput input, Vector3 down, float radius, float maxTravel, out RaycastHit best)
        {
            best = default;
            float castRadius = Mathf.Clamp(input.CastRadius > 0f ? input.CastRadius : radius * 0.35f, 0.05f, radius * 0.9f);
            float distance = maxTravel + radius + castRadius;
            Vector3 origin = input.Origin - down * castRadius;

            int count = Physics.SphereCastNonAlloc(origin, castRadius, down, s_hits, distance,
                input.IgnoreLayerMask == 0 ? ~0 : input.IgnoreLayerMask, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = s_hits[i];
                if (hit.collider == null) continue;
                if (hit.distance <= 0.0001f) continue; // started inside: no usable normal
                if (input.OwnerGrid != null && hit.collider.GetComponentInParent<GridEntity>() == input.OwnerGrid) continue;
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    best = hit;
                }
            }

            if (bestDistance >= float.MaxValue) return false;
            // Re-base onto the true suspension origin: the cast started a sphere radius
            // above it, so the raw hit distance would report the wheel riding too high.
            best.distance = Vector3.Dot(best.point - input.Origin, down);
            return best.distance > 0f && best.distance <= distance;
        }
    }
}
