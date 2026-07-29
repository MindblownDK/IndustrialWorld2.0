// Assets/Scripts/VoxelEngine/Combat/Explosion.cs
//
// Centralized explosion: applies Explosive damage to creatures + the player + placed
// blocks, CARVES A CRATER in the voxel terrain (works on spherical worlds via the
// IVoxelWorld interface), fires a distance-based camera shake (respects the ScreenShake
// setting), and plays a multi-layer "pretty" VFX (flash + fireball + shockwave ring +
// smoke + point light + debris). Used by grenades and any future explosive.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Player;

namespace VoxelEngine.Combat
{
    public static class Explosion
    {
        public static void Detonate(Vector3 pos, float radius, float damage, GameObject owner,
                                    float voxelDamageRadius, Material baseMat)
        {
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(pos);

            // ── 1. Damage creatures (IDamageable), the player, and placed blocks. ──
            var cols = Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Ignore);
            var damaged = new HashSet<IDamageable>();
            var brokenBlocks = new HashSet<VoxelEngine.Building.PlacedBlock>();
            foreach (var c in cols)
            {
                var d = c.GetComponentInParent<IDamageable>();
                if (d != null && d.IsAlive) damaged.Add(d);

                var pb = c.GetComponentInParent<VoxelEngine.Building.PlacedBlock>();
                if (pb != null) brokenBlocks.Add(pb);
            }
            var de = new DamageEvent { amount = damage, type = DamageType.Explosive, point = pos, direction = up, source = owner };
            foreach (var d in damaged) d.TakeDamage(de);
            foreach (var pb in brokenBlocks)
            {
                try { pb.Damage(Mathf.RoundToInt(damage), null); } catch { /* best-effort */ }
            }

            var ps = PlayerStats.Instance;   // the player isn't an IDamageable — handle directly
            if (ps != null && Vector3.Distance(pos, ps.transform.position) <= radius)
                ps.TakeDamage(damage);

            // ── 2. Voxel terrain crater (spherical-world safe via IVoxelWorld). ──
            var world = ActiveWorld.Current;
            if (world != null && voxelDamageRadius > 0f)
            {
                try
                {
                    Vector3Int center = world.WorldToVoxel(pos);
                    int vr = Mathf.Clamp(Mathf.RoundToInt(voxelDamageRadius / VoxelConstants.VOXEL_SIZE), 0, 4);
                    int vr2 = vr * vr;
                    for (int dx = -vr; dx <= vr; dx++)
                    for (int dy = -vr; dy <= vr; dy++)
                    for (int dz = -vr; dz <= vr; dz++)
                    {
                        if (dx * dx + dy * dy + dz * dz > vr2) continue;
                        var v = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                        if (world.GetVoxelWorld(v).IsSolid) world.SetVoxelWorld(v, Voxel.Empty, remesh: true);
                    }
                }
                catch { /* never let a terrain edit crash the explosion */ }
            }

            // ── 3. Distance-based camera shake (respects the ScreenShake setting). ──
            if (ps != null)
            {
                float dist = Vector3.Distance(pos, ps.transform.position);
                float shake = Mathf.Clamp01(1f - dist / Mathf.Max(1f, radius * 3f)) * 0.85f;
                CameraFeedback.AddShake(shake);
            }

            // ── 4. Pretty multi-layer VFX. ──
            if (baseMat != null) ExplosionFX.Spawn(pos, up, baseMat, radius);
        }
    }

    // Multi-layer explosion visual: bright flash → fireball → shockwave ring → smoke,
    // a brief point light, and flung debris. Scale/destroy animated (no alpha needed).
    public class ExplosionFX : MonoBehaviour
    {
        private float _t, _dur;
        private float _radius;
        private Transform _flash, _fire, _smoke, _ring;
        private Light _light;
        private Material[] _mats;
        private List<Rigidbody> _debris = new List<Rigidbody>();

        public static void Spawn(Vector3 pos, Vector3 up, Material baseMat, float radius, float dur = 0.9f)
        {
            var go = new GameObject("ExplosionFX");
            go.transform.position = pos;
            var fx = go.AddComponent<ExplosionFX>();
            fx._dur = dur; fx._radius = Mathf.Max(1f, radius);

            var flashMat  = new Material(baseMat); flashMat.color  = new Color(1.0f, 0.95f, 0.75f);
            var fireMat   = new Material(baseMat); fireMat.color   = new Color(1.0f, 0.45f, 0.10f);
            var smokeMat  = new Material(baseMat); smokeMat.color  = new Color(0.22f, 0.20f, 0.19f);
            var debrisMat = new Material(baseMat); debrisMat.color = new Color(0.35f, 0.30f, 0.26f);
            fx._mats = new[] { flashMat, fireMat, smokeMat, debrisMat };

            fx._flash = MakeSphere("Flash", go.transform, flashMat);
            fx._fire  = MakeSphere("Fire",  go.transform, fireMat);
            fx._smoke = MakeSphere("Smoke", go.transform, smokeMat);

            var ringGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(ringGo.GetComponent<Collider>());
            ringGo.transform.SetParent(go.transform, false);
            ringGo.transform.localPosition = Vector3.zero;
            ringGo.transform.localRotation = Quaternion.FromToRotation(Vector3.up, up);
            ringGo.GetComponent<Renderer>().sharedMaterial = fireMat;
            fx._ring = ringGo.transform;

            var lightGo = new GameObject("BlastLight"); lightGo.transform.SetParent(go.transform, false);
            fx._light = lightGo.AddComponent<Light>();
            fx._light.type = LightType.Point;
            fx._light.color = new Color(1f, 0.6f, 0.3f);
            fx._light.range = radius * 2.5f;
            fx._light.intensity = 10f;

            for (int i = 0; i < 8; i++) fx.SpawnDebris(go.transform, up, debrisMat);
        }

        private static Transform MakeSphere(string n, Transform parent, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = n; go.transform.SetParent(parent, false); go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.zero;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        private void SpawnDebris(Transform parent, Vector3 up, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * Random.Range(0.08f, 0.16f);
            var ren = go.GetComponent<Renderer>(); ren.sharedMaterial = m;
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            Vector3 dir = (Random.onUnitSphere + up * 0.4f).normalized;
            rb.linearVelocity = dir * Random.Range(4f, 9f);
            _debris.Add(rb);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            float r = _radius;

            // Flash: instant bright pop, gone by 0.12s.
            if (_flash != null)
            {
                float fk = Mathf.Clamp01(_t / 0.10f);
                _flash.localScale = Vector3.one * (r * (0.5f + fk * 1.1f) * 2f);
                if (_t > 0.12f) { Destroy(_flash.gameObject); _flash = null; }
            }
            // Fireball: grow then collapse by ~0.5s.
            if (_fire != null)
            {
                float fk = Mathf.Clamp01(_t / 0.45f);
                float s = (fk < 0.4f ? fk / 0.4f : 1f - (fk - 0.4f) / 0.6f);
                _fire.localScale = Vector3.one * (r * 0.9f * Mathf.Clamp01(s) * 2f);
                if (_t > 0.5f) { Destroy(_fire.gameObject); _fire = null; }
            }
            // Shockwave ring: expand outward fast, gone by 0.3s.
            if (_ring != null)
            {
                float fk = Mathf.Clamp01(_t / 0.25f);
                _ring.localScale = new Vector3(r * (0.3f + fk * 1.4f), 0.12f, r * (0.3f + fk * 1.4f));
                if (_t > 0.3f) { Destroy(_ring.gameObject); _ring = null; }
            }
            // Point light: intensity fades.
            if (_light != null) _light.intensity = Mathf.Lerp(10f, 0f, Mathf.Clamp01(_t / 0.4f));

            // Debris: integrate radial gravity so it arcs on spheres, despawn after life.
            Vector3 g = VoxelEngine.Cosmos.GravityProvider.GetGravity(transform.position);
            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                var rb = _debris[i];
                if (rb == null) { _debris.RemoveAt(i); continue; }
                rb.linearVelocity += g * dt;
                if (_t > _dur) Destroy(rb.gameObject);
            }
            // Smoke lingers, slow growth.
            if (_smoke != null)
            {
                float fk = Mathf.Clamp01(_t / 0.6f);
                _smoke.localScale = Vector3.one * (r * (0.4f + fk * 0.7f) * 2f);
            }

            if (_t >= _dur) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_mats != null) foreach (var m in _mats) if (m != null) Destroy(m);
        }
    }
}
