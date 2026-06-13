// Assets/Scripts/VoxelEngine/GridSystem/GridWheel.cs
//
// Space-Engineers-style powered wheel/suspension block. Uses raycast suspension,
// powered drive force, steering, lateral friction, and supports authored wheel
// sizes (2x2, 3x3, 5x5) through wheelSizeCells.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridWheel : GridBlock
    {
        [Header("Wheel Size")]
        [Tooltip("Visual wheel diameter in large-grid cells: 2, 3, or 5.")]
        public int wheelSizeCells = 3;

        [Header("Suspension")]
        [Tooltip("Suspension travel distance in metres.")]
        public float suspensionLength = 1.4f;
        [Tooltip("Suspension spring force at full compression.")]
        public float springForce = 140000f;
        [Tooltip("Suspension damping.")]
        public float damping = 18000f;
        [Range(0.05f, 1f)] public float suspensionStrength = 0.55f;

        [Header("Drive")]
        [Tooltip("Drive force applied when throttle input is active.")]
        public float driveForce = 75000f;
        [Tooltip("Power consumed while powered and grounded.")]
        public float powerDrawWatts = 250f;
        [Tooltip("Steering angle in degrees for steerable wheels.")]
        public float steerAngle = 30f;
        [Tooltip("Set true for front/steering wheels.")]
        public bool isSteerable = true;

        public override float PowerDraw => Enabled && IsGrounded ? powerDrawWatts : 0f;
        public bool IsGrounded { get; private set; }

        private float _lastSpringLength;
        private Transform _visualPivot;

        private float WheelRadius => Mathf.Max(0.25f, wheelSizeCells * GridSize.Large.CellSize() * 0.5f);

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (blockName == "Armor Block" || string.IsNullOrEmpty(blockName))
                blockName = $"Wheel {wheelSizeCells}x{wheelSizeCells}";
            _visualPivot = transform.Find("WheelVisualPivot");
            ConfigureForSize();
        }

        private void ConfigureForSize()
        {
            wheelSizeCells = Mathf.Clamp(wheelSizeCells, 2, 5);
            if (wheelSizeCells <= 2)
            {
                suspensionLength = Mathf.Max(suspensionLength, 1.0f);
                springForce = Mathf.Max(springForce, 90000f);
                damping = Mathf.Max(damping, 12000f);
                driveForce = Mathf.Max(driveForce, 45000f);
                powerDrawWatts = Mathf.Max(powerDrawWatts, 150f);
            }
            else if (wheelSizeCells >= 5)
            {
                suspensionLength = Mathf.Max(suspensionLength, 2.2f);
                springForce = Mathf.Max(springForce, 260000f);
                damping = Mathf.Max(damping, 32000f);
                driveForce = Mathf.Max(driveForce, 145000f);
                powerDrawWatts = Mathf.Max(powerDrawWatts, 650f);
            }
        }

        public void UpdateWheel(GridEntity grid)
        {
            if (!Enabled || grid == null || grid.Body == null)
            {
                IsGrounded = false;
                return;
            }

            bool powered = grid.HasPower;
            Vector3 wheelDown = -transform.up;
            Vector3 wheelPos = transform.position;
            float radius = WheelRadius;

            // Steering visual/physical direction. We steer the visual pivot if present so
            // the root block orientation remains stable on the grid.
            float steer = isSteerable ? grid.ThrustInput.x * steerAngle : 0f;
            Quaternion steerRot = Quaternion.AngleAxis(steer, transform.up);
            Vector3 forward = steerRot * transform.forward;
            if (_visualPivot != null)
                _visualPivot.localRotation = Quaternion.Euler(0f, steer, 0f);

            // Suspension raycast. Start slightly above the hub so large wheels do not
            // begin their cast inside terrain on contact.
            float rayLen = suspensionLength + radius + 0.35f;
            Vector3 rayOrigin = wheelPos + transform.up * 0.2f;
            IsGrounded = Physics.Raycast(rayOrigin, wheelDown, out var hit, rayLen, ~0, QueryTriggerInteraction.Ignore);

            if (IsGrounded && powered)
            {
                float currentLength = hit.distance - radius;
                currentLength = Mathf.Clamp(currentLength, 0f, suspensionLength);
                float compression = suspensionLength > 0f ? (suspensionLength - currentLength) / suspensionLength : 0f;

                float springVelocity = (_lastSpringLength - currentLength) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
                float force = (compression * springForce * suspensionStrength) + (springVelocity * damping);
                force = Mathf.Max(0f, force);
                _lastSpringLength = currentLength;

                grid.Body.AddForceAtPosition(transform.up * force, wheelPos, ForceMode.Force);

                float throttle = grid.ThrustInput.z;
                if (Mathf.Abs(throttle) > 0.01f)
                {
                    Vector3 driveDir = Vector3.ProjectOnPlane(forward, hit.normal).normalized;
                    grid.Body.AddForceAtPosition(driveDir * throttle * driveForce, wheelPos, ForceMode.Force);
                }

                // Strong lateral tyre friction, scaled by mass for heavy rovers.
                Vector3 pointVelocity = grid.Body.GetPointVelocity(wheelPos);
                Vector3 lateral = Vector3.Project(pointVelocity, transform.right);
                float friction = Mathf.Clamp(grid.Body.mass * 2.2f, 2500f, 45000f);
                grid.Body.AddForceAtPosition(-lateral * friction, wheelPos, ForceMode.Force);
            }
            else
            {
                _lastSpringLength = suspensionLength;
            }
        }
    }
}
