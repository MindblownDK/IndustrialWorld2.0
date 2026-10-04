// Assets/Scripts/VoxelEngine/GridSystem/GridImpact.cs
//
// 14.52.0 - real destruction: a grid that slams into the world now PAYS for it.
//
// What this component does, on the machine that simulates the grid (host or
// offline - on guests the rigidbody is kinematic and this stays passive):
//   - Grid vs terrain/buildings: blocks around every contact point take damage
//     scaled by impact speed and the grid's own mass; a hard hit on voxel
//     terrain gouges a crater (replicated through the same TerrainSync path
//     explosions use) and throws impact FX + camera shake.
//   - Grid vs grid: both grids run their own OnCollisionEnter, so each side
//     wrecks its own contact blocks - the heavier, faster ship simply has the
//     HP budget to survive it.
//   - Grid vs player: deliberately ignored HERE. Player collision damage is
//     victim-side (PlayerImpactDamage), exactly like fall damage and PvP -
//     every machine hurts only its own crusader. A ship does not scratch its
//     paint on a body either.
//
// On EVERY machine (including guests with kinematic replicas) it tracks the
// grid's pose-delta velocity each physics step, so PlayerImpactDamage can ask
// "how fast was this grid moving at the point that hit me?" even when the
// rigidbody itself is kinematic and reports zero.
//
// Block damage replicates through the existing grid-state convergence
// (Damage01 is part of the sync hash); destroyed blocks go through the normal
// GridBlock.Damage -> RemoveBlock path, so structural splits and drops behave
// exactly like any other source of harm.

using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.GridSystem
{
    [RequireComponent(typeof(GridEntity))]
    public class GridImpact : MonoBehaviour
    {
        // ── balance ──────────────────────────────────────────────────
        /// <summary>Below this relative speed a touch is a landing, not a crash.</summary>
        public const float MinImpactSpeed = 6f;
        /// <summary>At this relative speed an impact is as bad as it gets.</summary>
        public const float MaxImpactSpeed = 30f;
        private const float ImpactCooldown = 0.25f;

        private GridEntity _grid;
        private Rigidbody _rb;
        private float _nextImpactAt;

        // Pose-delta velocity tracking - valid on kinematic replicas too.
        private Vector3 _lastPos;
        private Quaternion _lastRot;
        private Vector3 _trackedVelocity;
        private Vector3 _trackedAngular;   // radians/s, axis * speed

        private void Awake()
        {
            _grid = GetComponent<GridEntity>();
            _rb = GetComponent<Rigidbody>();
            _lastPos = transform.position;
            _lastRot = transform.rotation;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) return;
            _trackedVelocity = (transform.position - _lastPos) / dt;
            (transform.rotation * Quaternion.Inverse(_lastRot))
                .ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            _trackedAngular = axis * (angle * Mathf.Deg2Rad / dt);
            _lastPos = transform.position;
            _lastRot = transform.rotation;
        }

        /// <summary>World velocity of the grid surface at a world point -
        /// from pose deltas, so it works for kinematic replicas as well.</summary>
        public Vector3 VelocityAt(Vector3 worldPoint)
        {
            Vector3 r = worldPoint - transform.position;
            return _trackedVelocity + Vector3.Cross(_trackedAngular, r);
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Only the simulating machine judges the crash; replicas converge.
            if (_rb == null || _rb.isKinematic || _grid == null) return;
            if (Time.time < _nextImpactAt) return;

            // Players never blunt a hull - their damage is victim-side.
            if (collision.collider.GetComponentInParent<CharacterController>() != null) return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed < MinImpactSpeed) return;
            _nextImpactAt = Time.time + ImpactCooldown;

            float mass = Mathf.Max(100f, _rb.mass);
            float severity = Mathf.Clamp01((speed - MinImpactSpeed) / (MaxImpactSpeed - MinImpactSpeed));

            var otherGrid = collision.collider.GetComponentInParent<GridEntity>();
            bool hitStructure = otherGrid != null
                || collision.collider.GetComponentInParent<VoxelEngine.Building.PlacedBlock>() != null;

            int contacts = Mathf.Min(collision.contactCount, 3);
            Vector3 firstPoint = transform.position;
            for (int i = 0; i < contacts; i++)
            {
                var contact = collision.GetContact(i);
                if (i == 0) firstPoint = contact.point;
                DamageOwnBlocksAround(contact.point, severity, mass);
            }

            // ── the ground remembers: crater on raw voxel terrain ──
            if (!hitStructure && severity >= 0.2f)
                GougeTerrain(firstPoint, severity, mass);

            // ── drama scaled to the energy, never on a headless server ──
            if (!VoxelEngine.Networking.NetworkSession.IsDedicated && severity >= 0.2f)
            {
                Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(firstPoint);
                VoxelEngine.Combat.ExplosionFX.Spawn(firstPoint, up,
                    Mathf.Lerp(0.6f, 2.5f, severity), null);
                var ps = VoxelEngine.Player.PlayerStats.Instance;
                if (ps != null)
                {
                    float dist = Vector3.Distance(firstPoint, ps.transform.position);
                    float shake = Mathf.Clamp01(1f - dist / 60f) * severity * 0.9f;
                    if (shake > 0.05f) VoxelEngine.Player.CameraFeedback.AddShake(shake);
                }
            }
        }

        /// <summary>Wreck this grid's own blocks in a small cell neighborhood
        /// around a contact point. Damage grows with speed squared and the
        /// grid's own mass - a heavy ship arrives with more to answer for.</summary>
        private void DamageOwnBlocksAround(Vector3 worldPoint, float severity, float mass)
        {
            float damage = Mathf.Clamp(severity * severity * Mathf.Sqrt(mass) * 6f, 10f, 2500f);
            int reach = severity >= 0.6f ? 2 : 1;
            Vector3Int center = _grid.WorldToGrid(worldPoint);

            for (int dx = -reach; dx <= reach; dx++)
            for (int dy = -reach; dy <= reach; dy++)
            for (int dz = -reach; dz <= reach; dz++)
            {
                var cell = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                if (!_grid.Blocks.TryGetValue(cell, out var block) || block == null) continue;
                // The contact cell takes it raw; neighbors take falloff.
                int ring = Mathf.Max(Mathf.Abs(dx), Mathf.Max(Mathf.Abs(dy), Mathf.Abs(dz)));
                float share = ring == 0 ? 1f : (ring == 1 ? 0.45f : 0.2f);
                block.Damage(damage * share);
            }
        }

        /// <summary>Carve the impact crater and replicate it - same path and
        /// same loop as an explosion, so every machine shows the same hole.</summary>
        private void GougeTerrain(Vector3 point, float severity, float mass)
        {
            var world = ActiveWorld.Current;
            if (world == null) return;
            try
            {
                // Radius grows with speed and (gently) with mass: a scout
                // scuffs the dirt, a freighter rearranges the landscape.
                int vr = Mathf.Clamp(
                    Mathf.RoundToInt(severity * (1.5f + Mathf.Pow(mass, 1f / 3f) * 0.35f)), 1, 10);
                Vector3Int center = world.WorldToVoxel(point);
                VoxelEngine.Combat.Explosion.CarveCrater(world, center, vr);
                VoxelEngine.Networking.TerrainSync.AnnounceExplosion(
                    point, 2f + severity * 6f, center, vr);
            }
            catch { /* a failed terrain edit must never crash a collision */ }
        }
    }
}
