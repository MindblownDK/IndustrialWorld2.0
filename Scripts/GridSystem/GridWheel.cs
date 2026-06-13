// Assets/Scripts/VoxelEngine/GridSystem/GridWheel.cs
//
// Wheel block for ground vehicles. Uses raycasting for suspension + drive force.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridWheel : GridBlock
    {
        [Header("Wheel")]
        [Tooltip("Suspension travel distance.")]
        public float suspensionLength = 0.8f;
        [Tooltip("Suspension spring force.")]
        public float springForce = 30000f;
        [Tooltip("Suspension damping.")]
        public float damping = 3000f;
        [Tooltip("Drive force applied when throttle input.")]
        public float driveForce = 15000f;
        [Tooltip("Steering angle (degrees) for front wheels.")]
        public float steerAngle = 30f;
        [Tooltip("Set true for front (steerable) wheels.")]
        public bool isSteerable;

        public override float PowerDraw => Enabled && IsGrounded ? 50f : 0f;
        public bool IsGrounded { get; private set; }

        private float _lastSpringLength;

        public void UpdateWheel(GridEntity grid)
        {
            if (!Enabled || grid == null || grid.Body == null) return;

            Vector3 wheelDown = -transform.up;
            Vector3 wheelPos = transform.position;

            // Steering.
            if (isSteerable)
            {
                float steer = grid.ThrustInput.x * steerAngle;
                transform.localRotation = Quaternion.Euler(0, steer, 0);
            }

            // Suspension raycast.
            float rayLen = suspensionLength + 0.3f;
            IsGrounded = Physics.Raycast(wheelPos, wheelDown, out var hit, rayLen);

            if (IsGrounded)
            {
                float currentLength = hit.distance - 0.3f;
                currentLength = Mathf.Clamp(currentLength, 0, suspensionLength);
                float compression = (suspensionLength - currentLength) / suspensionLength;

                // Spring + damper force.
                float springVel = (_lastSpringLength - currentLength) / Time.fixedDeltaTime;
                float force = (compression * springForce) + (springVel * damping);
                force = Mathf.Max(0, force);
                _lastSpringLength = currentLength;

                grid.Body.AddForceAtPosition(transform.up * force, wheelPos, ForceMode.Force);

                // Drive force (forward/backward from vertical input).
                float throttle = grid.ThrustInput.z;
                if (Mathf.Abs(throttle) > 0.01f && grid.HasPower)
                {
                    Vector3 driveDir = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
                    grid.Body.AddForceAtPosition(driveDir * throttle * driveForce, wheelPos, ForceMode.Force);
                }

                // Lateral friction to prevent sliding.
                Vector3 vel = grid.Body.GetPointVelocity(wheelPos);
                Vector3 lateral = Vector3.Project(vel, transform.right);
                grid.Body.AddForceAtPosition(-lateral * 2000f, wheelPos, ForceMode.Force);
            }
            else
            {
                _lastSpringLength = suspensionLength;
            }
        }
    }
}
