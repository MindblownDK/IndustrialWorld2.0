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

        [Tooltip("Lock strength label only — the magnetic lock NEVER breaks on its own. "
               + "The player unlocks it manually with P or the terminal button.")]
        public float lockStrength = 500000f;

        public bool IsLocked => _joint != null;
        public bool isDeployed => IsLocked; // legacy alias

        public override float PowerDraw => Enabled ? 5f : 0f;

        private FixedJoint _joint;
        private float _checkTimer;

        // Player-requested unlock latch: once the player unlocks (P / UI) we stay unlocked
        // until they choose to lock again — auto-lock will NOT instantly re-grab the surface.
        private bool _manuallyUnlocked;

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Landing Gear";
        }

        private void FixedUpdate()
        {
            if (!Enabled) { Unlock(); return; }
            if (IsLocked) return;
            if (!autoLock) return;
            if (_manuallyUnlocked) return; // stay unlocked until the player re-locks

            _checkTimer += Time.fixedDeltaTime;
            if (_checkTimer < 0.2f) return;
            _checkTimer = 0f;
            TryLock();
        }

        /// <summary>Attempt to lock onto a surface near the gear. Casts in several directions
        /// so the gear grabs whatever solid surface it is resting against.</summary>
        public void TryLock()
        {
            if (IsLocked || Grid == null || Grid.Body == null) return;

            _manuallyUnlocked = false;
            float cs = Grid.gridSize.CellSize();
            float reach = cs * 0.9f;
            Vector3 origin = transform.position;

            // Probe down first (most common), then the other 5 faces so the gear also
            // locks to walls / ceilings it is pressed against.
            Vector3[] dirs =
            {
                -transform.up, -transform.forward, transform.forward,
                transform.right, -transform.right, transform.up
            };

            foreach (var dir in dirs)
            {
                if (!Physics.Raycast(origin, dir, out var hit, reach)) continue;
                var otherBlock = hit.collider.GetComponentInParent<GridBlock>();
                if (otherBlock != null && otherBlock.Grid == Grid) continue; // don't lock to own ship

                _joint = Grid.gameObject.AddComponent<FixedJoint>();
                // INFINITE break force/torque → the lock never snaps under load. The player
                // is the only thing that can release it (P key or terminal "Unlock").
                _joint.breakForce  = float.PositiveInfinity;
                _joint.breakTorque = float.PositiveInfinity;
                _joint.connectedBody = hit.collider.attachedRigidbody; // null = locked to static world
                _joint.enableCollision = false;
                return;
            }
        }

        public void Unlock()
        {
            if (_joint != null) { Destroy(_joint); _joint = null; }
            _manuallyUnlocked = true; // don't auto-relock until the player asks for it
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
