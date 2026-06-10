// Assets/Scripts/VoxelEngine/GridSystem/GridLandingGear.cs
//
// Landing gear with Space-Engineers-style magnetic locking. When auto-lock is on
// it snaps the grid to whatever solid surface it touches (ground, base, another
// ship) via a FixedJoint. Lock strength is the joint break force.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridLandingGear : GridBlock
    {
        [Header("Landing Gear")]
        [Tooltip("Automatically lock to a surface on contact.")]
        public bool autoLock = true;

        [Tooltip("Joint break force (N). Higher = stronger lock, like SE landing gear.")]
        public float lockStrength = 500000f;

        public bool IsLocked => _joint != null;
        public bool isDeployed => IsLocked; // legacy alias

        public override float PowerDraw => Enabled ? 5f : 0f;

        private FixedJoint _joint;
        private float _checkTimer;

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Landing Gear";
        }

        private float _unlockCooldown;

        private void FixedUpdate()
        {
            if (!Enabled) { Unlock(); return; }
            if (_unlockCooldown > 0f) { _unlockCooldown -= Time.fixedDeltaTime; return; }
            if (IsLocked) return;
            if (!autoLock) return;

            _checkTimer += Time.fixedDeltaTime;
            if (_checkTimer < 0.25f) return;
            _checkTimer = 0f;
            TryLock();
        }

        /// <summary>Attempt to lock onto a surface just below the gear.</summary>
        public void TryLock()
        {
            if (IsLocked || Grid == null || Grid.Body == null) return;

            float cs = Grid.gridSize.CellSize();
            // Cast straight "down" relative to the gear's mounting.
            Vector3 origin = transform.position;
            if (Physics.Raycast(origin, -transform.up, out var hit, cs * 0.75f))
            {
                var otherBlock = hit.collider.GetComponentInParent<GridBlock>();
                if (otherBlock != null && otherBlock.Grid == Grid) return; // don't lock to own ship

                _joint = Grid.gameObject.AddComponent<FixedJoint>();
                _joint.breakForce = lockStrength;
                _joint.breakTorque = lockStrength;
                var otherRb = hit.collider.attachedRigidbody;
                _joint.connectedBody = otherRb;  // null = locked to the world (static ground)
                _joint.enableCollision = false;
            }
        }

        public void Unlock()
        {
            if (_joint != null) { Destroy(_joint); _joint = null; }
            // Hold off auto-lock for a moment so a manual unlock doesn't instantly re-lock.
            _unlockCooldown = 1.5f;
        }

        public void ToggleLock()
        {
            if (IsLocked) Unlock();
            else TryLock();
        }

        // Legacy API kept for callers.
        public void Deploy() => TryLock();
        public void Retract() => Unlock();
        public void Toggle() => ToggleLock();

        public override void OnRemoved()
        {
            base.OnRemoved();
            Unlock();
        }
    }
}
