// Assets/Scripts/VoxelEngine/Combat/HostileSleepManager.cs
// Distance-based AI/physics sleep for authoritative hostile NPCs. Enemies are
// retained with health, identity, and network registration intact; nearby players
// wake them again without a despawn/respawn round trip.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Networking;

namespace VoxelEngine.Combat
{
    public static class HostileSleepManager
    {
        private const float ScanInterval = 0.5f;
        private const float SleepDistance = 80f;
        private const float WakeDistance = 55f;

        private sealed class SleepState
        {
            public bool AgentWasEnabled;
            public bool HadBody;
            public bool BodyWasKinematic;
            public bool BodyUsedGravity;
            public Vector3 LinearVelocity;
            public Vector3 AngularVelocity;
            public bool Sleeping;
        }

        private static readonly Dictionary<Damageable, SleepState> States = new();
        private static readonly List<Damageable> Stale = new();
        private static float _nextScanAt;

        /// <summary>Called by HostileSync's existing world pump. Client replicas are
        /// deliberately excluded: the host alone decides whether AI is simulated.</summary>
        public static void Pump(IEnumerable<Damageable> authoritativeHostiles)
        {
            if (NetworkSession.Mode == SessionMode.Client || authoritativeHostiles == null) return;
            if (Time.unscaledTime < _nextScanAt) return;
            _nextScanAt = Time.unscaledTime + ScanInterval;

            Stale.Clear();
            foreach (var entry in States)
                if (entry.Key == null) Stale.Add(entry.Key);
            for (int i = 0; i < Stale.Count; i++) States.Remove(Stale[i]);

            foreach (var enemy in authoritativeHostiles)
            {
                if (enemy == null || !enemy.gameObject.activeInHierarchy || !enemy.IsAlive) continue;
                var ai = GetHostileAgent(enemy);
                if (ai == null) continue;

                bool protectedObjective = enemy is EnemyGhoul ghoul && ghoul.HasActiveIndustrialTarget;
                if (protectedObjective)
                {
                    Wake(enemy);
                    continue;
                }

                float distanceSq = ClosestPlayerDistanceSquared(enemy.transform.position);
                bool hasPlayer = !float.IsPositiveInfinity(distanceSq);
                bool isSleeping = States.TryGetValue(enemy, out var state) && state.Sleeping;

                if (isSleeping)
                {
                    if (hasPlayer && distanceSq <= WakeDistance * WakeDistance) Wake(enemy);
                    continue;
                }

                // If another system intentionally disabled this AI, do not claim it
                // or re-enable it later. The seven hostile classes share this root
                // Damageable/AI component contract.
                if (!ai.enabled) continue;
                if (!hasPlayer || distanceSq >= SleepDistance * SleepDistance)
                    Sleep(enemy, ai);
            }
        }

        /// <summary>Whether the authoritative host has suspended this hostile's AI and body.</summary>
        public static bool IsSleeping(Damageable enemy)
        {
            return enemy != null && States.TryGetValue(enemy, out var state) && state.Sleeping;
        }

        /// <summary>Forget destroyed/dead entries without touching their teardown.</summary>
        public static void Forget(Damageable enemy)
        {
            if (enemy != null) States.Remove(enemy);
        }

        private static MonoBehaviour GetHostileAgent(Damageable enemy)
        {
            if (enemy is EnemyGhoul ghoul) return ghoul;
            if (enemy is EnemyBasilisk basilisk) return basilisk;
            if (enemy is EnemyGriffin griffin) return griffin;
            if (enemy is EnemyRoc roc) return roc;
            if (enemy is EnemyManticore manticore) return manticore;
            if (enemy is EnemyIfrit ifrit) return ifrit;
            if (enemy is EnemyKarkadann karkadann) return karkadann;
            return null;
        }

        private static float ClosestPlayerDistanceSquared(Vector3 from)
        {
            float closest = float.PositiveInfinity;
            var local = VoxelEngine.Player.PlayerStats.Instance;
            if (local != null)
                closest = (local.transform.position - from).sqrMagnitude;

            foreach (var avatar in PlayerAvatar.All)
            {
                if (avatar == null) continue;
                float distanceSq = (avatar.transform.position - from).sqrMagnitude;
                if (distanceSq < closest) closest = distanceSq;
            }
            return closest;
        }

        private static void Sleep(Damageable enemy, MonoBehaviour ai)
        {
            if (enemy == null || ai == null || States.ContainsKey(enemy)) return;
            var body = enemy.GetComponent<Rigidbody>();
            var state = new SleepState
            {
                AgentWasEnabled = ai.enabled,
                HadBody = body != null,
                BodyWasKinematic = body != null && body.isKinematic,
                BodyUsedGravity = body != null && body.useGravity,
                LinearVelocity = body != null && !body.isKinematic ? body.linearVelocity : Vector3.zero,
                AngularVelocity = body != null && !body.isKinematic ? body.angularVelocity : Vector3.zero,
                Sleeping = true,
            };
            States[enemy] = state;

            ai.enabled = false;
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }

        private static void Wake(Damageable enemy)
        {
            if (enemy == null || !States.TryGetValue(enemy, out var state)) return;
            States.Remove(enemy);
            if (!state.Sleeping) return;

            var body = state.HadBody ? enemy.GetComponent<Rigidbody>() : null;
            if (body != null)
            {
                body.useGravity = state.BodyUsedGravity;
                body.isKinematic = state.BodyWasKinematic;
                if (!state.BodyWasKinematic)
                {
                    body.linearVelocity = state.LinearVelocity;
                    body.angularVelocity = state.AngularVelocity;
                }
            }

            var ai = GetHostileAgent(enemy);
            if (ai != null) ai.enabled = state.AgentWasEnabled;
        }
    }
}
