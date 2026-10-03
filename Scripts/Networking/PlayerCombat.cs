// Assets/Scripts/VoxelEngine/Networking/PlayerCombat.cs
//
// 14.34.0-dev - Multiplayer: players can finally fight each other.
//
// The avatar bodies deliberately carry NO colliders (display only - they
// must never block rays, mining or physics), so player-versus-player hits
// are resolved analytically: a weapon swing or shot sweeps a capsule around
// every live avatar, picks the nearest, and sends a HIT INTENT - never an
// outcome - to the host.
//
// Authority follows the locked rule. The host stamps the attacker from its
// connection table, checks the target is present and standing, checks the
// world's friendly-fire rule against the team registry, sanity-checks range
// and amount, and only then routes the damage to the machine that owns the
// victim's PlayerStats. Health flows back to everyone through the avatar's
// existing replicated health bar - this file adds no second channel.
//
// Friendly fire is a WORLD setting (fair ground for every player), not a
// team setting: the host's WorldSession decides, clients merely predict it
// for instant feedback and the host re-checks every intent regardless.

using UnityEngine;

namespace VoxelEngine.Networking
{
    public static class PlayerCombat
    {
        // The avatar's hit capsule, expressed against its own up axis
        // (planets are spheres - world up means nothing away from the pole).
        private const float CapsuleFootM = 0.25f;   // start above the ankles
        private const float CapsuleHeadM = 1.70f;   // up to the helmet
        private const float CapsuleRadiusM = 0.45f;

        /// <summary>Hard ceiling the host holds every intent under - no
        /// weapon in the game hits harder, so anything above is a bad actor.</summary>
        public const float MaxDamagePerHit = 300f;

        private static float _nextHitNotice;      // victim-side toast throttle
        private static float _nextFriendlyNotice; // attacker-side toast throttle

        // ── attacker side ─────────────────────────────────────────────────

        /// <summary>Sweep every live avatar against this aim ray. Returns the
        /// nearest standing avatar whose hit capsule the ray passes through,
        /// with the impact point and its distance along the ray. The local
        /// player's own body and downed players are never candidates.</summary>
        public static bool TryFindAvatarHit(Ray ray, float range, float radiusPad,
            out PlayerAvatar target, out Vector3 point, out float distance)
        {
            target = null;
            point = ray.origin + ray.direction * range;
            distance = float.MaxValue;
            if (NetworkSession.Mode == SessionMode.Offline) return false;

            string me = NetworkSession.LocalPlayerId;
            float radius = CapsuleRadiusM + Mathf.Max(0f, radiusPad);

            foreach (var avatar in PlayerAvatar.All)
            {
                if (avatar == null) continue;
                string id = avatar.PlayerId;
                if (string.IsNullOrEmpty(id) || id == me) continue;
                if (avatar.HealthPercent <= 0) continue;

                Vector3 up = avatar.transform.up;
                Vector3 a = avatar.transform.position + up * CapsuleFootM;
                Vector3 b = avatar.transform.position + up * CapsuleHeadM;

                if (!RayHitsCapsule(ray, range, a, b, radius, out float t)) continue;
                if (t >= distance) continue;

                distance = t;
                target = avatar;
                point = ray.origin + ray.direction * t;
            }
            return target != null;
        }

        /// <summary>One weapon hit against another player: predict the
        /// friendly-fire verdict for instant feedback, then send the intent.
        /// The host re-validates everything - this call promises nothing.</summary>
        public static void RequestDamage(PlayerAvatar target, float amount,
            VoxelEngine.Combat.DamageType type, Vector3 point, Vector3 direction, float maxRange)
        {
            if (target == null || amount <= 0f) return;
            if (NetworkSession.Mode == SessionMode.Offline) return;
            var bootstrap = NetworkBootstrap.Instance;
            if (bootstrap == null) return;

            string me = NetworkSession.LocalPlayerId;
            string targetId = target.PlayerId;
            if (string.IsNullOrEmpty(me) || string.IsNullOrEmpty(targetId) || me == targetId) return;

            // Predicted refusal: the mirror of the host's roster is right
            // here, so a blocked swing can say WHY without a round trip.
            if (!FriendlyFireAllowed(me, targetId))
            {
                if (Time.unscaledTime >= _nextFriendlyNotice)
                {
                    _nextFriendlyNotice = Time.unscaledTime + 2.5f;
                    VoxelEngine.UI.BuildFeedbackHud.Show("Teams",
                        "Friendly fire is off in this world.", null, new Color(0.92f, 0.60f, 0.12f));
                }
                return;
            }

            var intent = new PlayerHitBroadcast
            {
                TargetId = targetId,
                Amount = Mathf.Clamp(amount, 0f, MaxDamagePerHit),
                DamageType = (byte)type,
                Point = point,
                Direction = direction,
                MaxRange = Mathf.Max(0.5f, maxRange)
            };

            if (NetworkSession.Mode == SessionMode.Host)
                bootstrap.HostApplyPlayerHit(me, intent);
            else
                bootstrap.SendPlayerHit(intent);
        }

        /// <summary>Thrown-bomb splash against players, attacker-side: the
        /// machine that owns the projectile sweeps avatars in the blast and
        /// files one intent per victim, damage falling off to the edge.</summary>
        public static void RequestExplosionDamage(Vector3 center, float radius, float damage)
        {
            if (NetworkSession.Mode == SessionMode.Offline || damage <= 0f || radius <= 0f) return;
            string me = NetworkSession.LocalPlayerId;
            foreach (var avatar in PlayerAvatar.All)
            {
                if (avatar == null) continue;
                string id = avatar.PlayerId;
                if (string.IsNullOrEmpty(id) || id == me) continue;
                if (avatar.HealthPercent <= 0) continue;

                Vector3 chest = avatar.transform.position + avatar.transform.up * 1.0f;
                float dist = Vector3.Distance(center, chest);
                if (dist > radius + CapsuleRadiusM) continue;

                float falloff = Mathf.Clamp01(1f - dist / Mathf.Max(0.1f, radius));
                float amount = damage * Mathf.Lerp(0.25f, 1f, falloff);
                RequestDamage(avatar, amount, VoxelEngine.Combat.DamageType.Explosive,
                    chest, (chest - center).normalized, radius + 4f);
            }
        }

        /// <summary>The world rule: teammates only hurt each other when the
        /// world says so. Everyone else is always fair game.</summary>
        public static bool FriendlyFireAllowed(string attackerId, string targetId)
        {
            if (!TeamRegistry.SameTeam(attackerId, targetId)) return true;
            var session = VoxelEngine.Menu.WorldSession.Instance;
            return session != null && session.friendlyFire;
        }

        // ── victim side ───────────────────────────────────────────────────

        /// <summary>Host-approved damage lands on the machine that owns this
        /// player: armor applies through the normal TakeDamage path, the
        /// camera flinches, and a fatal hit names its killer on the death
        /// screen. Replicated health does the rest for every other machine.</summary>
        public static void ApplyIncoming(string attackerName, float amount, byte damageType)
        {
            var stats = VoxelEngine.Player.PlayerStats.Instance;
            if (stats == null || stats.IsDead || amount <= 0f) return;

            string name = string.IsNullOrEmpty(attackerName) ? "ANOTHER CRUSADER" : attackerName;

            // Cause first: TakeDamage may kill, and Die() must already know.
            VoxelEngine.Player.PlayerStats.SetDeathCause("SLAIN BY " + name.ToUpperInvariant());
            stats.TakeDamage(Mathf.Clamp(amount, 0f, MaxDamagePerHit));
            if (stats.Health > 0f)
                VoxelEngine.Player.PlayerStats.ClearDeathCause();   // survived - keep later causes honest

            // Juice: a flinch scaled to the blow, and a throttled notice so
            // auto-fire cannot wallpaper the HUD.
            VoxelEngine.Player.CameraFeedback.AddShake(Mathf.Clamp(amount / 40f, 0.15f, 1.2f));
            if (Time.unscaledTime >= _nextHitNotice)
            {
                _nextHitNotice = Time.unscaledTime + 2f;
                VoxelEngine.UI.BuildFeedbackHud.Show("Combat",
                    $"{name} is attacking you!", null, new Color(0.82f, 0.22f, 0.18f));
            }
        }

        // ── geometry ──────────────────────────────────────────────────────

        /// <summary>Finite ray against a capsule, cheap and allocation-free:
        /// closest approach between the aim segment and the body segment,
        /// compared against the capsule radius.</summary>
        private static bool RayHitsCapsule(Ray ray, float range, Vector3 capA, Vector3 capB,
            float radius, out float tAlongRay)
        {
            Vector3 p1 = ray.origin;
            Vector3 d1 = ray.direction * range;   // aim segment
            Vector3 d2 = capB - capA;             // body segment
            Vector3 r = p1 - capA;

            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);
            float c = Vector3.Dot(d1, r);
            float b = Vector3.Dot(d1, d2);
            float denom = a * e - b * b;

            float s = denom > 1e-6f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
            float t = e > 1e-6f ? Mathf.Clamp01((b * s + f) / e) : 0f;
            // One re-clamp pass keeps the pair consistent at the segment ends.
            s = Mathf.Clamp01((b * t - c) / Mathf.Max(a, 1e-6f));

            Vector3 onRay = p1 + d1 * s;
            Vector3 onBody = capA + d2 * t;
            tAlongRay = s * range;
            return (onRay - onBody).sqrMagnitude <= radius * radius;
        }
    }
}
