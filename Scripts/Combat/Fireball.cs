// Assets/Scripts/VoxelEngine/Combat/Fireball.cs
//
// A hurled ball of fire cast by the Ifrit Djinn. A lightweight, non-physical projectile
// (raycast continuous-collision, clean on spherical worlds) that deals fire damage and
// applies an armor-escalating BURN to the player. Passes through the caster.

using UnityEngine;
using VoxelEngine.Player;

namespace VoxelEngine.Combat
{
    public class Fireball : MonoBehaviour
    {
        public float speed          = 18f;
        public float damage         = 14f;
        public float burnDps        = 6f;
        public float burnDuration   = 3f;
        public float maxLife        = 2.5f;
        /// <summary>14.55.0 - guest-side replay of a host cast: fly and look
        /// identical, but never apply damage, burns or ignition.</summary>
        public bool visualOnly;

        private Vector3 _vel;
        private float _life;
        private GameObject _owner;

        public static Fireball Spawn(Vector3 pos, Vector3 dir, GameObject owner, Material mat,
                                     float dmg, float bdps, float bdur)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Fireball";
            go.transform.localScale = Vector3.one * 0.3f;
            go.transform.position = pos;
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            var ren = go.GetComponent<Renderer>(); if (mat != null) ren.sharedMaterial = mat;
            var fb = go.AddComponent<Fireball>();
            fb._vel = dir.normalized * fb.speed;
            fb._owner = owner;
            fb.damage = dmg; fb.burnDps = bdps; fb.burnDuration = bdur;
            return fb;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _life += dt;
            if (_life >= maxLife) { Destroy(gameObject); return; }

            Vector3 step = _vel * dt;
            float dist = step.magnitude;
            if (dist > 0.0001f && Physics.Raycast(transform.position, step, out var hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (_owner != null && (hit.collider.transform == _owner.transform || hit.collider.transform.IsChildOf(_owner.transform)))
                {
                    transform.position += step;
                    return;
                }

                if (visualOnly) { Destroy(gameObject); return; }   // 14.55.0 - replayed cast, host owns the damage

                var ps = hit.collider.GetComponentInParent<PlayerStats>();
                if (ps != null)
                {
                    // Local player hit - victim-side funnel (death cause + burn + feedback).
                    VoxelEngine.Networking.HostileSync.ApplyStrikeLocal(damage, "Ifrit",
                        VoxelEngine.Networking.HostileSync.EffectBurn, burnDps, burnDuration);
                }
                else
                {
                    // 14.55.0 - a remote player's avatar: route the strike to its machine.
                    var av = hit.collider.GetComponentInParent<VoxelEngine.Networking.PlayerAvatar>();
                    if (av != null)
                    {
                        VoxelEngine.Networking.HostileSync.StrikeAvatar(av, damage, "Ifrit",
                            VoxelEngine.Networking.HostileSync.EffectBurn, burnDps, burnDuration);
                    }
                    else
                    {
                        var d = hit.collider.GetComponentInParent<IDamageable>();
                        if (d != null && d.IsAlive)
                            d.TakeDamage(new DamageEvent { amount = damage, type = DamageType.Fire,
                                point = hit.point, direction = step.normalized, source = _owner });
                    }
                }
                // 9.16.0 fire system — a fireball splashing into a flammable pool sets it alight.
                var aw = VoxelEngine.Core.ActiveWorld.Current;
                if (aw != null) VoxelEngine.Fire.FireManager.TryIgniteAt(aw.WorldToVoxel(hit.point));
                Destroy(gameObject);
                return;
            }
            transform.position += step;
        }
    }
}
