// Assets/Scripts/VoxelEngine/Player/PlayerImpactDamage.cs
//
// 14.52.0 - collisions hurt. Two sources, both handled VICTIM-SIDE, the same
// law fall damage and PvP already follow: every machine damages only its own
// crusader, and replicated health tells everyone else.
//
//   1. Grids: fly into a hull - or get run over by one - and the impact speed
//      ALONG THE SURFACE NORMAL decides the damage, scaled by the grid's
//      mass. Standing on a moving ship is safe (zero normal speed); being
//      rammed by a freighter is not. Velocity comes from GridImpact's
//      pose-delta tracker, so kinematic replicas on guest machines hit just
//      as hard as the host's live rigidbody.
//
//   2. Other players: a proximity sweep against the replicated avatars. When
//      another crusader is within body contact range and the closing speed is
//      high enough, this machine hurts its own player - the other machine
//      runs the same sweep from its side, so BOTH parties bleed without a
//      single packet or any double-apply. Pure physics, no friendly-fire
//      gate: a mid-air collision does not care what team you are on.
//
// Attached automatically by PlayerController.Awake - nothing to set up.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Networking;

namespace VoxelEngine.Player
{
    public class PlayerImpactDamage : MonoBehaviour
    {
        // ── balance ──────────────────────────────────────────────────
        private const float GridMinSpeed = 8f;     // m/s into the surface before it hurts
        private const float GridLethalSpeed = 32f; // full-health kill at reference mass
        private const float PlayerMinSpeed = 9f;   // closing speed before body checks hurt
        private const float PlayerLethalSpeed = 30f;
        private const float DamageExponent = 1.35f; // same curve family as fall damage
        private const float ContactRange = 1.15f;   // chest-to-chest distance for a body hit
        private const float SpawnGraceSeconds = 3f;

        private const float WorldMinSpeed = 11f;   // m/s into a wall before it hurts (same start as fall damage)
        private const float WorldLethalSpeed = 30f;

        private PlayerController _player;
        private float _liveAt;
        private float _worldCooldownUntil;
        private readonly Dictionary<GridEntity, float> _gridCooldown = new();
        private readonly Dictionary<string, float> _avatarCooldown = new();

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            _liveAt = Time.time + SpawnGraceSeconds;
        }

        // ── 1. grids, via the controller's own collision callback ────
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (_player == null || Time.time < _liveAt) return;
            var grid = hit.collider != null ? hit.collider.GetComponentInParent<GridEntity>() : null;
            if (grid == null)
            {
                // 14.60.3 - the world hits back: flying into terrain, a building or
                // any static block at speed hurts, exactly like hitting a hull. Only
                // the closing speed INTO the surface counts, and ground landings
                // (surface normal near local up) stay fall damage's domain so a hard
                // landing is never billed twice. Players never crater terrain - the
                // only impact here is on the player.
                if (hit.collider == null || Time.time < _worldCooldownUntil) return;
                var rbHit = hit.collider.attachedRigidbody;
                if (rbHit != null && !rbHit.isKinematic) return; // dynamic props push, not hurt
                if (hit.collider.GetComponentInParent<PlayerController>() != null) return;

                Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
                if (Vector3.Angle(hit.normal, up) < 50f) return; // landing, not a crash

                float into = Vector3.Dot(_player.Velocity, -hit.normal);
                if (into < WorldMinSpeed) return;
                _worldCooldownUntil = Time.time + 1f;

                Hurt(into, WorldMinSpeed, WorldLethalSpeed, 0.85f,
                     "FLEW INTO THE SCENERY",
                     $"-{{0:0}} HP · hit the world at {into:0.0} m/s");
                return;
            }

            // Keyed by the entity itself - GetInstanceID is obsolete in this
            // Unity, and a reference key needs no id at all (14.52.1).
            if (_gridCooldown.TryGetValue(grid, out float until) && Time.time < until) return;

            var impact = grid.GetComponent<GridImpact>();
            Vector3 gridVel = impact != null ? impact.VelocityAt(hit.point) : Vector3.zero;
            // Only the closing speed INTO the surface counts: riding a deck or
            // sliding along a wall is contact at zero normal speed.
            float closing = Vector3.Dot(_player.Velocity - gridVel, -hit.normal);
            if (closing < GridMinSpeed) return;
            _gridCooldown[grid] = Time.time + 1f;

            float mass = grid.Body != null ? Mathf.Max(100f, grid.Body.mass) : 1000f;
            float massFactor = 0.4f + 0.6f * Mathf.Clamp01(mass / 4000f);
            Hurt(closing, GridMinSpeed, GridLethalSpeed, massFactor,
                 "CRUSHED BY " + GridIdentity.NameOf(grid).ToUpperInvariant(),
                 $"-{{0:0}} HP · hit {GridIdentity.NameOf(grid)} at {closing:0.0} m/s");
        }

        // ── 2. other players, via the avatar proximity sweep ─────────
        private void FixedUpdate()
        {
            if (_player == null || Time.time < _liveAt) return;
            if (NetworkSession.Mode == SessionMode.Offline) return;

            string me = NetworkSession.LocalPlayerId;
            Vector3 myChest = transform.position + transform.up * 1.0f;
            Vector3 myVel = _player.Velocity;

            foreach (var avatar in PlayerAvatar.All)
            {
                if (avatar == null || avatar.IsOwner) continue;
                string id = avatar.PlayerId;
                if (string.IsNullOrEmpty(id) || id == me) continue;
                if (avatar.HealthPercent <= 0) continue;

                Vector3 chest = avatar.transform.position + avatar.transform.up * 1.0f;
                if ((chest - myChest).sqrMagnitude > ContactRange * ContactRange) continue;

                if (_avatarCooldown.TryGetValue(id, out float until) && Time.time < until) continue;

                float closing = (myVel - avatar.EstimatedVelocity).magnitude;
                if (closing < PlayerMinSpeed) continue;
                _avatarCooldown[id] = Time.time + 1.2f;

                string name = string.IsNullOrEmpty(avatar.PlayerName) ? "ANOTHER CRUSADER" : avatar.PlayerName;
                Hurt(closing, PlayerMinSpeed, PlayerLethalSpeed, 0.9f,
                     "COLLIDED MIDAIR WITH " + name.ToUpperInvariant(),
                     $"-{{0:0}} HP · collided with {name} at {closing:0.0} m/s");
            }
        }

        /// <summary>The shared damage law: severity along the fall-damage
        /// curve, armor applied by PlayerStats, death cause kept honest.</summary>
        private void Hurt(float speed, float minSpeed, float lethalSpeed, float factor,
                          string deathCause, string noticeFormat)
        {
            var stats = PlayerStats.Instance != null ? PlayerStats.Instance : GetComponent<PlayerStats>();
            if (stats == null || stats.IsDead) return;

            float severity = Mathf.Clamp01((speed - minSpeed) / Mathf.Max(0.1f, lethalSpeed - minSpeed));
            float damage = Mathf.Pow(severity, DamageExponent) * stats.MaxHealth * factor;
            if (damage < 1f) return;

            PlayerStats.SetDeathCause(deathCause);
            stats.TakeDamage(damage);
            if (stats.Health > 0f) PlayerStats.ClearDeathCause();

            CameraFeedback.AddShake(Mathf.Clamp(damage / 40f, 0.2f, 1.2f));
            VoxelEngine.UI.BuildFeedbackHud.Show("Impact",
                string.Format(noticeFormat, damage), null, new Color(0.95f, 0.25f, 0.18f));
        }
    }
}
