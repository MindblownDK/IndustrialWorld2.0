// Assets/Scripts/VoxelEngine/Combat/ManticoreSpike.cs
//
// Venomous tail-spike fired by the Manticore. A lightweight, non-physical projectile
// that flies straight, raycasts each step for clean continuous collision on spherical
// worlds, and applies kinetic damage + an armor-bypassing poison DoT to the player
// (or kinetic damage to other Damageables). Ignores the firing Manticore.

using UnityEngine;
using VoxelEngine.Player;

namespace VoxelEngine.Combat
{
    public class ManticoreSpike : MonoBehaviour
    {
        public float speed           = 24f;
        public float damage          = 12f;
        public float poisonDps       = 4f;
        public float poisonDuration  = 3f;
        public float maxLife         = 2.5f;
        /// <summary>14.55.0 - guest-side replay of a host cast: fly and look
        /// identical, but never apply damage or poison.</summary>
        public bool visualOnly;

        private Vector3 _vel;
        private float _life;
        private GameObject _owner;

        public static ManticoreSpike Spawn(Vector3 pos, Vector3 dir, GameObject owner, Material mat,
                                           float dmg, float pdps, float pdur)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ManticoreSpike";
            go.transform.localScale = new Vector3(0.14f, 0.14f, 0.55f);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            var ren = go.GetComponent<Renderer>(); if (mat != null) ren.sharedMaterial = mat;
            var spike = go.AddComponent<ManticoreSpike>();
            spike._vel = dir.normalized * spike.speed;
            spike._owner = owner;
            spike.damage = dmg; spike.poisonDps = pdps; spike.poisonDuration = pdur;
            return spike;
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
                // Pass through the firing Manticore.
                if (_owner != null && (hit.collider.transform == _owner.transform || hit.collider.transform.IsChildOf(_owner.transform)))
                {
                    transform.position += step;
                    return;
                }

                if (visualOnly) { Destroy(gameObject); return; }   // 14.55.0 - replayed cast, host owns the damage

                var ps = hit.collider.GetComponentInParent<PlayerStats>();
                if (ps != null)
                {
                    // Local player hit - victim-side funnel (death cause + poison + feedback).
                    VoxelEngine.Networking.HostileSync.ApplyStrikeLocal(damage, "Manticore",
                        VoxelEngine.Networking.HostileSync.EffectPoison, poisonDps, poisonDuration);
                }
                else
                {
                    // 14.55.0 - a remote player's avatar: route the strike to its machine.
                    var av = hit.collider.GetComponentInParent<VoxelEngine.Networking.PlayerAvatar>();
                    if (av != null)
                    {
                        VoxelEngine.Networking.HostileSync.StrikeAvatar(av, damage, "Manticore",
                            VoxelEngine.Networking.HostileSync.EffectPoison, poisonDps, poisonDuration);
                    }
                    else
                    {
                        var d = hit.collider.GetComponentInParent<IDamageable>();
                        if (d != null && d.IsAlive)
                            d.TakeDamage(new DamageEvent { amount = damage, type = DamageType.Kinetic,
                                point = hit.point, direction = step.normalized, source = _owner });
                    }
                }
                Destroy(gameObject);
                return;
            }
            transform.position += step;
        }
    }
}
