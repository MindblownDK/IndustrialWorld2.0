// Assets/Scripts/VoxelEngine/GridSystem/GridLandingGear.cs
//
// Landing gear — locks the ship in place when touching terrain or a surface.
// Toggle lock with the interact key while in cockpit, or auto-locks on contact.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridLandingGear : GridBlock
    {
        [Header("Landing Gear")]
        [Tooltip("Auto-lock when velocity is below this threshold.")]
        public float autoLockSpeed = 0.5f;
        [Tooltip("Force required to break the lock (N).")]
        public float lockStrength = 100000f;

        public bool IsLocked { get; private set; }
        public bool IsContactingSurface { get; private set; }

        private Rigidbody _gridBody;
        private Vector3 _lockPosition;
        private Quaternion _lockRotation;
        private float _contactTimer;

        public override void OnPlaced()
        {
            base.OnPlaced();
            _gridBody = Grid?.Body;
        }

        private void FixedUpdate()
        {
            if (Grid == null || _gridBody == null) return;

            if (IsLocked)
            {
                // Hold position.
                _gridBody.linearVelocity = Vector3.zero;
                _gridBody.angularVelocity = Vector3.zero;
                _gridBody.MovePosition(_lockPosition);
                _gridBody.MoveRotation(_lockRotation);
                _gridBody.useGravity = false;
            }
        }

        private void OnCollisionStay(Collision collision)
        {
            if (Grid == null || _gridBody == null) return;
            IsContactingSurface = true;

            // Auto-lock when slow enough.
            if (!IsLocked && _gridBody.linearVelocity.magnitude < autoLockSpeed)
            {
                _contactTimer += Time.fixedDeltaTime;
                if (_contactTimer > 0.5f)
                    Lock();
            }
        }

        private void OnCollisionExit(Collision collision)
        {
            IsContactingSurface = false;
            _contactTimer = 0;
        }

        public void Lock()
        {
            if (_gridBody == null) return;
            IsLocked = true;
            _lockPosition = _gridBody.position;
            _lockRotation = _gridBody.rotation;
            _gridBody.isKinematic = true;

            VoxelEngine.UI.BuildFeedbackHud.Show("Landing Gear Locked", "",
                null, VoxelEngine.UI.UITheme.AccentGreen);
        }

        public void Unlock()
        {
            if (_gridBody == null) return;
            IsLocked = false;
            _gridBody.isKinematic = false;
            _gridBody.useGravity = true;

            VoxelEngine.UI.BuildFeedbackHud.Show("Landing Gear Unlocked", "",
                null, VoxelEngine.UI.UITheme.AccentOrange);
        }

        public void Toggle()
        {
            if (IsLocked) Unlock(); else Lock();
        }
    }
}
