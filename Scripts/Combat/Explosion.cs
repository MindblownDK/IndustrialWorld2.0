// Assets/Scripts/VoxelEngine/Combat/Explosion.cs
//
// Centralized explosion: applies Explosive damage to creatures + the player + placed
// blocks, CARVES A CRATER in the voxel terrain (spherical-world safe via IVoxelWorld),
// fires a distance-based camera shake (respects GameSettings.ScreenShake), and plays a
// REAL particle-based VFX. The VFX is scale-driven — a grenade (scale ~1) is a quick
// blast; a big bomb (scale ~3-5) rises into a billowing mushroom cloud.

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
            var ps = PlayerStats.Instance;   // the player isn't an IDamageable
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

            // ── 3. Distance-based camera shake. ──
            if (ps != null)
            {
                float dist = Vector3.Distance(pos, ps.transform.position);
                float shake = Mathf.Clamp01(1f - dist / Mathf.Max(1f, radius * 3f)) * 0.85f;
                CameraFeedback.AddShake(shake);
            }

            // ── 4. Particle VFX (scale grows with blast radius → mushroom clouds on big bombs). ──
            float scale = Mathf.Clamp(radius / 5f, 0.6f, 10f);
            ExplosionFX.Spawn(pos, up, scale, baseMat);
        }
    }

    // Real particle explosion: bright core, fireball, embers, a rising/billowing smoke
    // column (mushroom at large scale), a shockwave ring, a light flash, and debris.
    public class ExplosionFX : MonoBehaviour
    {
        private Light _light;
        private float _t, _maxLife, _lightDur, _scale;
        private readonly List<Rigidbody> _debris = new List<Rigidbody>();

        public static void Spawn(Vector3 pos, Vector3 up, float scale, Material baseMat)
        {
            var go = new GameObject("ExplosionFX");
            go.transform.position = pos;
            var fx = go.AddComponent<ExplosionFX>();
            fx._scale = scale;
            fx._maxLife = 2.2f * scale + 1.5f;
            fx._lightDur = 0.35f * Mathf.Sqrt(scale);

            Vector3 g = VoxelEngine.Cosmos.GravityProvider.GetGravity(pos);   // radial gravity acceleration

            Burst(go.transform, "Core",   count: R(10, 14, scale), life: 0.16f, speed: R(1, 3, scale), size: 0.5f * scale, color: new Color(1f, 0.97f, 0.8f), gravity: Vector3.zero, shapeR: 0.1f, fadeStart: 0.5f);
            Burst(go.transform, "Fire",   count: R(26, 34, scale), life: 0.45f * scale, speed: R(3, 7, scale), size: 0.42f * scale, color: new Color(1f, 0.45f, 0.12f), gravity: Vector3.zero, shapeR: 0.2f, fadeStart: 0.3f, grow: 1.8f);
            Burst(go.transform, "Embers", count: R(40, 60, scale), life: 0.9f * scale, speed: R(7, 14, scale), size: 0.07f * scale, color: new Color(1f, 0.75f, 0.25f), gravity: g, shapeR: 0.15f, fadeStart: 0.4f);
            Shockwave(go.transform, up, scale);
            Smoke(go.transform, up, g, scale);
            for (int i = 0; i < Mathf.RoundToInt(6 * scale); i++) fx.AddDebris(g, scale, baseMat);

            var lg = new GameObject("BlastLight"); lg.transform.SetParent(go.transform, false);
            fx._light = lg.AddComponent<Light>();
            fx._light.type = LightType.Point;
            fx._light.color = new Color(1f, 0.62f, 0.32f);
            fx._light.range = 8f * scale;
            fx._light.intensity = 12f * Mathf.Sqrt(scale);
        }

        // One radial burst of billboarding particles.
        private static void Burst(Transform parent, string name, int count, float life, float speed, float size,
                                  Color color, Vector3 gravity, float shapeR, float fadeStart, float grow = 1f)
        {
            var ps = AddPS(parent, name);
            var m = ps.main;
            m.startLifetime = Mathf.Max(0.05f, life);
            m.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            m.startSize = Mathf.Max(0.02f, size);
            m.startColor = color;
            m.gravityModifier = 0f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = Mathf.Max(0.02f, shapeR);
            ColorOverLife(ps, color, fadeStart, grow);
            if (gravity.sqrMagnitude > 0.0001f) AddForce(ps, gravity);
            ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, count)) });
        }

        // Flat expanding ring of particles lying on the surface.
        private static void Shockwave(Transform parent, Vector3 up, float scale)
        {
            var ps = AddPS(parent, "Shockwave");
            var m = ps.main;
            m.startLifetime = 0.32f;
            m.startSpeed = new ParticleSystem.MinMaxCurve(10f * scale, 16f * scale);
            m.startSize = 0.16f * scale;
            m.startColor = new Color(1f, 0.7f, 0.35f, 0.85f);
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.2f * scale;
            sh.alignToDirection = false;
            ps.transform.rotation = Quaternion.LookRotation(up);   // lie flat on the surface
            ColorOverLife(ps, new Color(1f, 0.7f, 0.35f), 0.2f, 1f);
            ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(40 * scale)) });
        }

        // Rising, billowing smoke — the mushroom-cloud column. Buoyant (rises against gravity).
        private static void Smoke(Transform parent, Vector3 up, Vector3 g, float scale)
        {
            var ps = AddPS(parent, "Smoke");
            var m = ps.main;
            m.startLifetime = Mathf.Max(0.6f, 1.8f * scale);
            m.startSpeed = new ParticleSystem.MinMaxCurve(1f * scale, 4f * scale);
            m.startSize = new ParticleSystem.MinMaxCurve(0.4f * scale, 0.9f * scale);
            m.startColor = new Color(0.24f, 0.22f, 0.21f, 0.85f);
            m.gravityModifier = 0f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.4f * scale;
            // Buoyancy: rise against gravity (stronger & longer on big bombs → mushroom).
            AddForce(ps, -g * 0.6f);
            // Billow.
            var n = ps.noise; n.enabled = true; n.strength = 1.2f * scale; n.frequency = 0.4f; n.scrollSpeed = 1f;
            // Grow as it rises + fade late.
            var so = ps.sizeOverLifetime; so.enabled = true;
            so.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new[] { new Keyframe(0f, 0.4f), new Keyframe(1f, 1.6f) }));
            ColorOverLife(ps, new Color(0.24f, 0.22f, 0.21f), 0.55f, 1f);
            ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(30 * scale)) });
        }

        private void AddDebris(Vector3 g, float scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * Random.Range(0.08f, 0.18f) * scale;
            var ren = go.GetComponent<Renderer>(); if (mat != null) ren.sharedMaterial = mat;
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;                         // radial gravity applied manually in Update (sphere-correct)
            rb.linearVelocity = (Random.onUnitSphere + transform.up * 0.4f) * Random.Range(5f, 11f) * scale;
            _debris.Add(rb);
        }

        // ---- particle helpers ----
        private static ParticleSystem AddPS(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            main.loop = false;
            main.maxParticles = 800;
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;   // burst-only (no continuous fountain)
            // Default particle material is transparent in URP — leave it so alpha fade works.
            return ps;
        }

        private static void AddForce(ParticleSystem ps, Vector3 worldAccel)
        {
            var f = ps.forceOverLifetime;
            f.enabled = true;
            f.space = ParticleSystemSimulationSpace.World;
            f.x = new ParticleSystem.MinMaxCurve(worldAccel.x);
            f.y = new ParticleSystem.MinMaxCurve(worldAccel.y);
            f.z = new ParticleSystem.MinMaxCurve(worldAccel.z);
        }

        private static void ColorOverLife(ParticleSystem ps, Color color, float fadeStart, float grow)
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, Mathf.Clamp01(fadeStart)),
                        new GradientAlphaKey(0f, 1f) });
            c.color = grad;
            if (grow != 1f)
            {
                var s = ps.sizeOverLifetime; s.enabled = true;
                s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new[] { new Keyframe(0f, 1f / grow), new Keyframe(1f, 1f) }));
            }
        }

        private static int R(int min, int max, float scale) => Mathf.Clamp(Mathf.RoundToInt(Random.Range(min, max) * scale), 1, 300);
        private static float R(float min, float max, float scale) => Random.Range(min, max) * scale;

        private void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            if (_light != null) _light.intensity = Mathf.Lerp(12f * Mathf.Sqrt(_scale), 0f, Mathf.Clamp01(_t / Mathf.Max(0.05f, _lightDur)));
            // Radial gravity on debris chunks (sphere-correct).
            Vector3 g = VoxelEngine.Cosmos.GravityProvider.GetGravity(transform.position);
            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                var rb = _debris[i];
                if (rb == null) { _debris.RemoveAt(i); continue; }
                rb.linearVelocity += g * dt;
            }
            if (_t >= _maxLife) Destroy(gameObject);
        }
    }
}
