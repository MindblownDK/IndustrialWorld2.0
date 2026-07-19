// Assets/Scripts/VoxelEngine/GridSystem/GridLandingGear.cs
//
// Landing gear with grid-terminal magnetic locking. When auto-lock is on
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

        private const float AutoLockInterval = 0.1f;

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
            if (!Enabled)
            {
                UnlockInternal(manual: false);
                return;
            }

            if (!CanAutoLock()) return;

            _checkTimer += Time.fixedDeltaTime;
            if (_checkTimer < AutoLockInterval) return;
            _checkTimer = 0f;
            TryLockInternal();
        }

        private void OnCollisionStay(Collision collision)
        {
            if (!CanAutoLock() || collision == null) return;
            TryLockToCollider(collision.collider);
        }

        private bool CanAutoLock()
        {
            return Enabled && autoLock && !IsLocked && !_manuallyUnlocked && Grid != null && Grid.Body != null;
        }

        /// <summary>Attempt to lock onto a surface near the gear. Casts and contact checks in
        /// several directions so the gear grabs ground, walls, ceilings, bases, or another ship.</summary>
        public void TryLock()
        {
            _manuallyUnlocked = false;
            TryLockInternal();
        }

        private bool TryLockInternal()
        {
            if (!Enabled || IsLocked || Grid == null || Grid.Body == null) return false;

            float cs = EffectiveCellSize;
            float reach = cs * 2.5f;
            float probeRadius = Mathf.Max(0.08f, cs * 0.22f);

            // First check generous contact bubbles. This catches the common case where the
            // gear/root collider is already touching terrain and casts start inside/behind it.
            Vector3[] contactPoints =
            {
                transform.position,
                transform.position - transform.up * (cs * 0.55f),
                transform.position + Vector3.down * (cs * 0.55f)
            };
            for (int p = 0; p < contactPoints.Length; p++)
            {
                var overlaps = Physics.OverlapSphere(contactPoints[p], cs * 0.90f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < overlaps.Length; i++)
                {
                    if (TryLockToCollider(overlaps[i])) return true;
                }
            }

            // Probe local faces plus world-down. World-down makes auto-lock reliable even when
            // the landing gear block was placed with an unexpected rotation.
            Vector3[] dirs =
            {
                Vector3.down, -transform.up, -transform.forward, transform.forward,
                transform.right, -transform.right, transform.up, Vector3.up
            };

            for (int d = 0; d < dirs.Length; d++)
            {
                Vector3 dir = dirs[d].normalized;
                var hits = Physics.SphereCastAll(transform.position, probeRadius, dir, reach, ~0, QueryTriggerInteraction.Ignore);
                if (hits == null || hits.Length == 0) continue;

                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                for (int i = 0; i < hits.Length; i++)
                {
                    if (TryLockToCollider(hits[i].collider)) return true;
                }
            }

            return false;
        }

        private bool TryLockToCollider(Collider target)
        {
            if (target == null || target.isTrigger || IsLocked || Grid == null || Grid.Body == null) return false;

            // Never lock to our own grid, even when a cast starts inside the gear's own collider.
            var targetGrid = target.GetComponentInParent<GridEntity>();
            if (targetGrid == Grid) return false;

            var targetBlock = target.GetComponentInParent<GridBlock>();
            if (targetBlock != null && targetBlock.Grid == Grid) return false;

            Rigidbody connectedBody = target.attachedRigidbody;
            if (connectedBody == Grid.Body) return false;

            _joint = Grid.gameObject.AddComponent<FixedJoint>();
            // Infinite break force/torque: the magnetic lock never snaps under load. The player
            // is the only thing that can release it (P key or terminal "Unlock").
            _joint.breakForce = float.PositiveInfinity;
            _joint.breakTorque = float.PositiveInfinity;
            _joint.connectedBody = connectedBody; // null = locked to static world / terrain
            _joint.enableCollision = false;

            // Kill the last bit of impact drift so the ship feels magnetically clamped.
            Grid.Body.linearVelocity = Vector3.zero;
            Grid.Body.angularVelocity = Vector3.zero;
            _manuallyUnlocked = false;
            return true;
        }

        public void Unlock()
        {
            UnlockInternal(manual: true);
        }

        private void UnlockInternal(bool manual)
        {
            if (_joint != null)
            {
                Destroy(_joint);
                _joint = null;
            }

            if (manual) _manuallyUnlocked = true;
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
            UnlockInternal(manual: false);
        }
    }
}
