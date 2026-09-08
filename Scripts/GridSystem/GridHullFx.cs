// Assets/Scripts/VoxelEngine/GridSystem/GridHullFx.cs
//
// Hull feedback for damaged and heated blocks — grid blocks AND placed base
// blocks. One component per grid (attached lazily) plus one world host for
// static PlacedBlock/PlacedTieredBlock pieces, wiring five signals into one
// quiet, physical presentation:
//
//   • cracks  — procedural crack overlays grow through 5 stages as HP drops,
//               from ANY damage source (plumes, entry heat, weapons, tools)
//   • scorch  — blocks char darker as their HP drops
//   • glow    — hot blocks shine with a blackbody-tinted shell, so heat damage
//               is visible long before the block fails
//   • smoke   — badly damaged blocks smoke; burning blocks spit embers
//   • bursts  — destroyed blocks break apart with debris, a flash and a bang
//
// Visual state is derived, never stored: cracks/scorch follow HP and glow
// follows the last reported temperature, so a restored save shows its scars
// without touching the save format at all.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Building.Tiered;
using VoxelEngine.FX;
using VoxelEngine.Thermal;

namespace VoxelEngine.GridSystem
{
    [DisallowMultipleComponent]
    public class GridHullFx : MonoBehaviour
    {
        // ── Tuning ────────────────────────────────────────────────────────────
        private const float RefreshInterval = 0.15f;     // visual pass cadence
        private const float GridAuditInterval = 5f;      // grid damage sweep
        private const float WorldAuditInterval = 15f;    // placed-block sweep
        private const int MaxSmokeEmitters = 24;         // hard cap on live smoke
        private const float SmokeDamageThreshold = 0.30f;
        private const float ClankGateSeconds = 0.07f;    // impact sound throttle
        private const float ScorchStrength = 0.85f;      // how dark a dead block chars

        // ── Crack tuning ──────────────────────────────────────────────────────
        private const int CrackStageCount = 5;           // hairline → shattered
        private const float CrackShowDamage01 = 0.15f;   // first hairline appears
        private const int CrackTexSize = 128;

        private GridEntity _grid;
        private bool _isWorld;                 // true for the static world host
        private float _refreshTimer;
        private float _auditTimer;
        private float _lastClankAt = -999f;

        private readonly Dictionary<Component, State> _states = new();
        private readonly List<State> _scratch = new();
        private readonly List<State> _drop = new();
        private readonly List<State> _smoking = new();

        // ── Access & routing ──────────────────────────────────────────────────

        /// <summary>Attach (or fetch) the hull FX service for a grid.</summary>
        public static GridHullFx For(GridEntity grid)
        {
            if (grid == null) return null;
            var fx = grid.GetComponent<GridHullFx>();
            if (fx == null) fx = grid.gameObject.AddComponent<GridHullFx>();
            return fx;
        }

        private static GridHullFx _world;

        /// <summary>The world host servicing static placed blocks (lazy).</summary>
        private static GridHullFx World()
        {
            if (_world != null) return _world;
            var go = new GameObject("GridHullFx (World)");
            _world = go.AddComponent<GridHullFx>();
            _world._isWorld = true;
            return _world;
        }

        /// <summary>A grid block just took a hit. impactFx is false for
        /// continuous sources (thermal burn, plume erosion) so per-tick damage
        /// doesn't clank.</summary>
        public static void NotifyDamaged(GridBlock block, float amount, bool impactFx)
        {
            if (block == null) return;
            var host = block.Grid != null ? For(block.Grid) : World();
            host?.Notify(block, amount, impactFx);
        }

        /// <summary>A static placed block took a hit. Blocks that live on a grid
        /// (they carry both components) route to that grid's service so their
        /// visual state stays in one place.</summary>
        public static void NotifyDamaged(PlacedBlock block, float amount, bool impactFx)
        {
            if (block == null) return;
            var sibling = block.GetComponent<GridBlock>();
            if (sibling != null && sibling.Grid != null)
            {
                For(sibling.Grid)?.Notify(sibling, amount, impactFx);
                return;
            }
            World().Notify(block, amount, impactFx);
        }

        /// <summary>A tiered building piece took a hit.</summary>
        public static void NotifyDamaged(PlacedTieredBlock block, float amount, bool impactFx)
        {
            if (block == null) return;
            World().Notify(block, amount, impactFx);
        }

        /// <summary>
        /// Temperature report for a static placed block (plume heat while it is
        /// being blasted). Unlike grid blocks there is no thermal simulation
        /// behind it, so the value decays once reports stop arriving.
        /// </summary>
        public static void ReportPlacedTemperature(Component block, float temperatureC)
        {
            if (block == null) return;
            var host = World();
            if (host._states.TryGetValue(block, out var s))
            {
                s.TemperatureC = temperatureC;
                s.LastTempReport = Time.time;
            }
            else if (temperatureC >= ThermalRules.GlowVisibleC)
            {
                s = host.EnsureState(block);
                s.TemperatureC = temperatureC;
                s.LastTempReport = Time.time;
            }
        }

        /// <summary>
        /// A grid block ran out of HP and is about to be removed. Spawn the
        /// break-apart burst while its transform is still valid.
        /// </summary>
        public static void SpawnDestructionBurst(GridBlock block)
        {
            if (block == null) return;
            SpawnDestructionBurst(block.transform.position,
                Mathf.Max(0.25f, block.EffectiveCellSize), ReadTint(block), IsContinuousSource(block));
        }

        /// <summary>A placed base block ran out of HP and is about to be removed.</summary>
        public static void SpawnDestructionBurst(PlacedBlock block) => BurstFromComponent(block);

        /// <summary>A tiered building piece ran out of HP and is about to be removed.</summary>
        public static void SpawnDestructionBurst(PlacedTieredBlock block) => BurstFromComponent(block);

        /// <summary>World-position variant used by anything that clears a cell outright.</summary>
        public static void SpawnDestructionBurst(Vector3 worldPos, float cellSize, Color tint, bool thermal)
        {
            var go = new GameObject("HullBurst");
            go.transform.position = worldPos;
            BuildDebrisBurst(go.transform, cellSize, tint, thermal);
            BuildFlashBurst(go.transform, cellSize, thermal);
            AudioManager.PlayAt(SfxLibrary.GetVariant(Sfx.BlockBreak, 3), worldPos,
                0.95f, Random.Range(0.92f, 1.08f), 70f);
            Destroy(go, 4f);
        }

        /// <summary>
        /// Temperature report from the grid's thermal system. Hot blocks gain a
        /// glow; cold reports fade an existing glow out.
        /// </summary>
        public void ReportTemperature(GridBlock block, float temperatureC)
        {
            if (block == null) return;
            if (_states.TryGetValue(block, out var s))
            {
                s.TemperatureC = temperatureC;
                s.LastTempReport = Time.time;
            }
            else if (temperatureC >= ThermalRules.GlowVisibleC)
            {
                s = EnsureState(block);
                s.TemperatureC = temperatureC;
                s.LastTempReport = Time.time;
            }
        }

        /// <summary>True while this block already carries visual state (glow/scorch/cracks).</summary>
        public bool HasState(GridBlock block) => block != null && _states.ContainsKey(block);

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake() => _grid = GetComponent<GridEntity>();

        private void Update()
        {
            if (!_isWorld && _grid == null) _grid = GetComponent<GridEntity>();

            float dt = Time.deltaTime;
            _refreshTimer -= dt;
            _auditTimer -= dt;

            bool refresh = _refreshTimer <= 0f;
            bool audit = _auditTimer <= 0f;
            if (refresh) _refreshTimer = RefreshInterval;
            if (audit) _auditTimer = _isWorld ? WorldAuditInterval : GridAuditInterval;

            if (audit) { if (_isWorld) AuditWorldBlocks(); else AuditGridBlocks(); }
            if (refresh) RefreshAll();
        }

        private void Notify(Component block, float amount, bool impactFx)
        {
            var s = EnsureState(block);

            if (impactFx && amount >= 2f && Time.time - _lastClankAt >= ClankGateSeconds)
            {
                _lastClankAt = Time.time;
                s.ContinuousSource = false;
                AudioManager.PlayAt(SfxLibrary.GetVariant(Sfx.BlockHit, 3), s.Transform.position,
                    Mathf.Clamp01(0.35f + amount / 90f), Random.Range(0.9f, 1.12f), 45f);
            }
            else if (!impactFx)
            {
                s.ContinuousSource = true;
            }
        }

        // ── Refresh pass ──────────────────────────────────────────────────────

        private void RefreshAll()
        {
            _scratch.Clear();
            _scratch.AddRange(_states.Values);
            for (int i = 0; i < _scratch.Count; i++) RefreshState(_scratch[i]);
        }

        private void RefreshState(State s)
        {
            var owner = s.Owner;
            if (owner == null) { RemoveState(s); return; }

            float damage01 = Damage01Of(owner);

            // World-hosted blocks (no thermal simulation behind them) cool their
            // reported plume temperature once the reports stop arriving.
            if (s.WorldHosted && Time.time - s.LastTempReport > 0.6f && s.TemperatureC > 0f)
                s.TemperatureC = Mathf.Lerp(s.TemperatureC, 0f, 0.03f);

            bool burning = s.TemperatureC >= ThermalRules.BlockDamageThresholdC;
            float glow = ThermalRules.GlowIntensity01(s.TemperatureC);

            // Scorch tint — applied only when the ratio actually moved.
            if (Mathf.Abs(damage01 - s.AppliedDamage) > 0.004f)
                ApplyScorch(s, damage01);

            // Heat glow shell, with a subtle flicker while actively burning.
            if (glow > 0.004f)
            {
                EnsureGlowShell(s);
                if (s.GlowRenderer != null)
                {
                    float flicker = burning
                        ? 0.86f + 0.14f * Mathf.Sin(Time.time * 11f + s.Phase)
                        : 1f;
                    Color c = ThermalRules.GlowColor(s.TemperatureC);
                    c.a = glow * flicker;

                    if (!s.GlowRenderer.enabled) s.GlowRenderer.enabled = true;
                    if (burning || Mathf.Abs(c.a - s.AppliedGlow) > 0.004f)
                    {
                        s.GlowRenderer.GetPropertyBlock(s.Mp);
                        s.Mp.SetColor("_BaseColor", c);
                        s.Mp.SetColor("_Color", c);
                        s.GlowRenderer.SetPropertyBlock(s.Mp);
                        s.AppliedGlow = c.a;
                    }
                }
            }
            else if (s.GlowRenderer != null && s.GlowRenderer.enabled)
            {
                s.GlowRenderer.enabled = false;
                s.AppliedGlow = -1f;
            }

            // Procedural cracks — a new overlay stage each time damage crosses a band.
            int crackStage = CrackStageFor(damage01);
            if (crackStage != s.AppliedCrackStage)
                ApplyCracks(s, crackStage);

            ManageEmitters(s, damage01, burning);

            // Retire states that have fully recovered (or were heat-only and cooled).
            if (damage01 <= 0.002f && glow <= 0.004f && !s.SmokeLive && !s.EmbersLive)
                RemoveState(s);
        }

        private void ApplyScorch(State s, float damage01)
        {
            s.AppliedDamage = damage01;
            float k = Mathf.Pow(damage01, 0.8f) * ScorchStrength;

            for (int i = 0; i < s.Renderers.Length; i++)
            {
                var r = s.Renderers[i];
                if (r == null) continue;

                Color baseColor = s.BaseColors[i];
                Color charred = Color.Lerp(baseColor * 0.28f, new Color(0.06f, 0.055f, 0.05f), 0.5f);
                charred.a = baseColor.a;
                Color c = Color.Lerp(baseColor, charred, k);

                r.GetPropertyBlock(s.Mp);
                s.Mp.SetColor("_BaseColor", c);
                s.Mp.SetColor("_Color", c);
                r.SetPropertyBlock(s.Mp);
            }
        }

        private void RestoreColors(State s)
        {
            for (int i = 0; i < s.Renderers.Length; i++)
            {
                var r = s.Renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(s.Mp);
                s.Mp.SetColor("_BaseColor", s.BaseColors[i]);
                s.Mp.SetColor("_Color", s.BaseColors[i]);
                r.SetPropertyBlock(s.Mp);
            }
        }

        // ── Cracks ────────────────────────────────────────────────────────────

        /// <summary>Crack overlay stage for a damage ratio: -1 = pristine,
        /// 0..4 = hairline through shattered.</summary>
        private static int CrackStageFor(float damage01)
        {
            if (damage01 < CrackShowDamage01) return -1;
            return Mathf.Clamp(Mathf.FloorToInt(damage01 * CrackStageCount) - 1, 0, CrackStageCount - 1);
        }

        private void ApplyCracks(State s, int stage)
        {
            if (stage < 0)
            {
                if (s.CrackRenderer != null && s.CrackRenderer.enabled)
                    s.CrackRenderer.enabled = false;
                s.AppliedCrackStage = -1;
                return;
            }

            if (s.CrackShell == null)
            {
                s.CrackShell = BuildShell(s, "HullCracks", CrackMaterial, inflate: 1.002f, pad: 0.006f);
                s.CrackRenderer = s.CrackShell != null ? s.CrackShell.GetComponent<MeshRenderer>() : null;
            }

            if (s.CrackRenderer != null)
            {
                if (!s.CrackRenderer.enabled) s.CrackRenderer.enabled = true;

                var tex = CrackTexture(stage);
                s.CrackRenderer.GetPropertyBlock(s.Mp);
                s.Mp.SetTexture("_BaseMap", tex);
                s.Mp.SetTexture("_MainTex", tex);
                s.Mp.SetColor("_BaseColor", Color.white);
                s.Mp.SetColor("_Color", Color.white);
                s.CrackRenderer.SetPropertyBlock(s.Mp);
            }

            s.AppliedCrackStage = stage;
        }

        /// <summary>
        /// Procedurally generated crack overlay for one damage stage. Deterministic
        /// per stage (fixed seed): impact points with radiating, forking fissures
        /// that widen and multiply as the damage stage climbs, plus chipped patches
        /// on the two worst stages. Generated once, cached for the session.
        /// </summary>
        private static Texture2D[] _crackTextures;

        private static Texture2D CrackTexture(int stage)
        {
            _crackTextures ??= new Texture2D[CrackStageCount];
            if (_crackTextures[stage] != null) return _crackTextures[stage];

            const int S = CrackTexSize;
            var px = new Color32[S * S];                    // fully transparent
            var rng = new System.Random(0x00C0FFEE + stage * 7919);

            int impacts = 1 + stage;
            for (int imp = 0; imp < impacts; imp++)
            {
                float ox = S * (0.5f + (float)(rng.NextDouble() - 0.5) * 0.55f);
                float oy = S * (0.5f + (float)(rng.NextDouble() - 0.5) * 0.55f);

                int branches = 3 + stage * 2;
                for (int b = 0; b < branches; b++)
                {
                    float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                    float len = (12f + stage * 11f) * (0.65f + (float)rng.NextDouble() * 0.7f);
                    float width = 0.9f + stage * 0.45f;
                    WalkCrack(px, S, ox, oy, ang, len, width, rng, depth: 0,
                        maxDepth: stage >= 2 ? 1 : 0);
                }
            }

            // Heavy stages also chip material away around the fractures.
            if (stage >= 3)
            {
                int chips = (stage - 2) * 6;
                for (int i = 0; i < chips; i++)
                {
                    float cx = (float)rng.NextDouble() * (S - 1);
                    float cy = (float)rng.NextDouble() * (S - 1);
                    float r = 1.5f + (float)rng.NextDouble() * 2.5f;
                    StampBrush(px, S, cx, cy, r, new Color32(28, 26, 24, 110));
                }
            }

            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels32(px);
            tex.Apply(false, makeNoLongerReadable: true);
            _crackTextures[stage] = tex;
            return tex;
        }

        /// <summary>Random-walk a single fissure across the overlay, forking
        /// occasionally on the rougher stages.</summary>
        private static void WalkCrack(Color32[] px, int s, float x, float y, float ang,
            float len, float width, System.Random rng, int depth, int maxDepth)
        {
            float half = width * 0.5f;
            var line = new Color32(34, 31, 29, 235);

            for (int step = 0; step < (int)len; step++)
            {
                ang += ((float)rng.NextDouble() - 0.5f) * 0.45f;
                x += Mathf.Cos(ang);
                y += Mathf.Sin(ang);
                StampBrush(px, s, x, y, half, line);

                if (depth < maxDepth && rng.NextDouble() < 0.035)
                    WalkCrack(px, s, x, y,
                        ang + (rng.NextDouble() < 0.5 ? 0.9f : -0.9f),
                        len * 0.4f, width * 0.7f, rng, depth + 1, maxDepth);

                if (x < 2f || y < 2f || x > s - 3f || y > s - 3f) break;
            }
        }

        /// <summary>Stamp a soft round brush of colour onto the overlay buffer.</summary>
        private static void StampBrush(Color32[] px, int s, float x, float y, float radius, Color32 color)
        {
            int x0 = Mathf.Max(0, (int)(x - radius));
            int x1 = Mathf.Min(s - 1, (int)(x + radius));
            int y0 = Mathf.Max(0, (int)(y - radius));
            int y1 = Mathf.Min(s - 1, (int)(y + radius));
            if (x1 < x0 || y1 < y0) return;

            for (int py = y0; py <= y1; py++)
            {
                for (int pxx = x0; pxx <= x1; pxx++)
                {
                    int i = py * s + pxx;
                    if (px[i].a < color.a) px[i] = color;
                }
            }
        }

        // ── Emitters ──────────────────────────────────────────────────────────

        private void ManageEmitters(State s, float damage01, bool burning)
        {
            bool wantSmoke = burning || damage01 >= SmokeDamageThreshold;
            bool wantEmbers = burning;

            if (wantSmoke)
            {
                if (s.Smoke == null) s.Smoke = CreateSmoke(s);
                if (s.Smoke != null)
                {
                    AcquireSmokeSlot(s);
                    if (!s.Smoke.isEmitting) s.Smoke.Play();
                    float intensity = Mathf.Max(damage01, burning ? 0.75f : 0f);
                    var em = s.Smoke.emission;
                    em.rateOverTime = Mathf.Lerp(2.5f, 10f, intensity);
                    s.SmokeLive = true;
                }
            }
            else if (s.Smoke != null && s.Smoke.isEmitting)
            {
                var em = s.Smoke.emission;
                em.rateOverTime = 0f;
                s.Smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                ReleaseSmokeSlot(s);
                s.SmokeLive = false;
            }
            else
            {
                s.SmokeLive = false;
            }

            if (wantEmbers)
            {
                if (s.Embers == null) s.Embers = CreateEmbers(s);
                if (s.Embers != null)
                {
                    if (!s.Embers.isEmitting) s.Embers.Play();
                    var em = s.Embers.emission;
                    em.rateOverTime = Mathf.Lerp(8f, 18f, damage01);
                    s.EmbersLive = true;
                }
            }
            else if (s.Embers != null && s.Embers.isEmitting)
            {
                s.Embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                s.EmbersLive = false;
            }
            else
            {
                s.EmbersLive = false;
            }
        }

        /// <summary>Keep the live-smoker list under the cap by recycling the
        /// least-deserving emitter (least damaged, not burning).</summary>
        private void AcquireSmokeSlot(State s)
        {
            if (_smoking.Contains(s)) return;

            if (_smoking.Count >= MaxSmokeEmitters)
            {
                int evictIdx = -1;
                float evictScore = float.MaxValue;
                for (int i = 0; i < _smoking.Count; i++)
                {
                    var c = _smoking[i];
                    if (c.Owner == null) { evictIdx = i; break; }
                    float score = SmokerScore(c);
                    if (score < evictScore) { evictScore = score; evictIdx = i; }
                }
                if (evictIdx >= 0)
                {
                    var evicted = _smoking[evictIdx];
                    _smoking.RemoveAt(evictIdx);
                    if (evicted.Smoke != null)
                        evicted.Smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    evicted.SmokeLive = false;
                }
            }
            _smoking.Add(s);
        }

        private static float SmokerScore(State s)
        {
            float damage = Damage01Of(s.Owner);
            bool burning = s.TemperatureC >= ThermalRules.BlockDamageThresholdC;
            return damage + (burning ? 2f : 0f);
        }

        private void ReleaseSmokeSlot(State s)
        {
            _smoking.Remove(s);
        }

        // ── Audits (restored saves show their scars) ──────────────────────────

        private void AuditGridBlocks()
        {
            if (_grid == null) return;

            foreach (var b in _grid.AllBlocks)
            {
                if (b == null || b.maxHP <= 0f) continue;
                float d = 1f - Mathf.Clamp01(b.currentHP / b.maxHP);
                if (d >= 0.05f && !_states.ContainsKey(b)) EnsureState(b);
            }

            DropDeadStates();
        }

        private void AuditWorldBlocks()
        {
            // On-grid statics carry a GridBlock sibling and are audited by their
            // grid's service — skip them here so they never get two states.
            foreach (var b in FindObjectsByType<PlacedBlock>(FindObjectsInactive.Exclude))
            {
                if (b == null || b.onGrid || b.GetComponent<GridBlock>() != null) continue;
                if (Damage01Of(b) >= 0.05f && !_states.ContainsKey(b)) EnsureState(b);
            }

            foreach (var b in FindObjectsByType<PlacedTieredBlock>(FindObjectsInactive.Exclude))
            {
                if (b == null) continue;
                if (Damage01Of(b) >= 0.05f && !_states.ContainsKey(b)) EnsureState(b);
            }

            DropDeadStates();
        }

        private void DropDeadStates()
        {
            _drop.Clear();
            foreach (var s in _states.Values)
                if (s.Owner == null) _drop.Add(s);
            for (int i = 0; i < _drop.Count; i++) RemoveState(_drop[i]);
            _drop.Clear();
        }

        // ── State management ──────────────────────────────────────────────────

        private State EnsureState(Component owner)
        {
            if (_states.TryGetValue(owner, out var s)) return s;

            s = new State
            {
                Owner = owner,
                Transform = owner.transform,
                Phase = Random.value * 10f,
                WorldHosted = _isWorld,
            };
            CacheRenderers(owner, s);
            _states[owner] = s;
            return s;
        }

        /// <summary>Live damage ratio 0..1 for any hull-like component.</summary>
        private static float Damage01Of(Component owner)
        {
            switch (owner)
            {
                case GridBlock gb:
                    return gb.maxHP > 0f ? 1f - Mathf.Clamp01(gb.currentHP / gb.maxHP) : 0f;

                case PlacedBlock pb:
                {
                    float max = pb.Item != null && pb.Item.blockHealth > 0 ? pb.Item.blockHealth : 100f;
                    return 1f - Mathf.Clamp01(pb.Hp / max);
                }

                case PlacedTieredBlock tb:
                {
                    float max = tb.definition != null ? tb.definition.GetStats(tb.tier).hp : 100f;
                    return 1f - Mathf.Clamp01(tb.hp / max);
                }

                default:
                    return 0f;
            }
        }

        private static void CacheRenderers(Component owner, State s)
        {
            var found = owner.GetComponentsInChildren<Renderer>(true);

            int n = 0;
            for (int i = 0; i < found.Length; i++)
                if (IsHullRenderer(found[i])) n++;

            s.Renderers = new Renderer[n];
            s.BaseColors = new Color[n];

            int k = 0;
            for (int i = 0; i < found.Length; i++)
            {
                var r = found[i];
                if (!IsHullRenderer(r)) continue;
                s.Renderers[k] = r;
                s.BaseColors[k] = ReadBaseColor(r);
                k++;
            }
        }

        /// <summary>Only the block's own solid geometry: never particles and
        /// never our own glow/crack shells.</summary>
        private static bool IsHullRenderer(Renderer r)
            => r != null
               && r is not ParticleSystemRenderer
               && r.name != "HeatGlowShell"
               && r.name != "HullCracks";

        private static Color ReadBaseColor(Renderer r)
        {
            var m = r.sharedMaterial;
            if (m != null)
            {
                if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
                if (m.HasProperty("_Color")) return m.GetColor("_Color");
            }
            return Color.white;
        }

        private static Color ReadTint(Component owner)
        {
            var renderers = owner.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (!IsHullRenderer(r)) continue;
                Color c = ReadBaseColor(r);
                if (c.maxColorComponent > 0.02f) return c;
            }
            return new Color(0.5f, 0.5f, 0.55f);
        }

        private static bool IsContinuousSource(Component owner)
        {
            var host = HostOf(owner, out var key);
            return host != null && key != null
                && host._states.TryGetValue(key, out var st)
                && st.ContinuousSource;
        }

        /// <summary>Which service owns the visuals for a component, and the key
        /// its state is filed under (on-grid statics are filed under their
        /// GridBlock sibling).</summary>
        private static GridHullFx HostOf(Component owner, out Component key)
        {
            key = owner;

            if (owner is GridBlock gb && gb.Grid != null)
                return For(gb.Grid);

            if (owner is PlacedBlock pb)
            {
                var sibling = pb.GetComponent<GridBlock>();
                if (sibling != null && sibling.Grid != null)
                {
                    key = sibling;
                    return For(sibling.Grid);
                }
            }

            return _world;
        }

        private static void BurstFromComponent(Component owner)
        {
            if (owner == null) return;

            Bounds b = default;
            bool any = false;
            Color tint = new(0.5f, 0.5f, 0.55f);

            var renderers = owner.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (!IsHullRenderer(r)) continue;
                if (!any) { b = r.bounds; any = true; tint = ReadBaseColor(r); }
                else b.Encapsulate(r.bounds);
            }

            float size = any ? Mathf.Clamp(b.size.magnitude * 0.35f, 0.3f, 4f) : 1f;
            SpawnDestructionBurst(owner.transform.position, size, tint, IsContinuousSource(owner));
        }

        private void RemoveState(State s)
        {
            _states.Remove(s.Owner);
            ReleaseSmokeSlot(s);

            if (s.Shell != null) Destroy(s.Shell);
            if (s.CrackShell != null) Destroy(s.CrackShell);
            if (s.Smoke != null) { Destroy(s.Smoke.gameObject); s.SmokeLive = false; }
            if (s.Embers != null) { Destroy(s.Embers.gameObject); s.EmbersLive = false; }

            // If we ever darkened this block, put the authored tint back.
            if (s.AppliedDamage > 0.002f) RestoreColors(s);
        }

        // ── Shells ────────────────────────────────────────────────────────────

        /// <summary>Build a bounds-wrapped overlay cube on the block (cracks or
        /// glow). Collider is destroyed so the grid collision shape is untouched.</summary>
        private static GameObject BuildShell(State s, string name, Material mat, float inflate, float pad)
        {
            if (s.Transform == null) return null;

            var shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shell.name = name;

            var col = shell.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var mr = shell.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;

            // Wrap the block's combined renderer bounds so the shell covers every
            // protrusion (nozzles, wheels, tanks) and not just the cell cube.
            Bounds b = ComputeBounds(s);
            var inv = s.Transform.worldToLocalMatrix;
            Vector3 a = inv.MultiplyPoint3x4(b.min);
            Vector3 c = inv.MultiplyPoint3x4(b.max);
            Vector3 lmin = Vector3.Min(a, c);
            Vector3 lmax = Vector3.Max(a, c);

            shell.transform.SetParent(s.Transform, false);
            shell.transform.localPosition = (lmin + lmax) * 0.5f;
            shell.transform.localRotation = Quaternion.identity;
            shell.transform.localScale = (lmax - lmin) * inflate + Vector3.one * pad;

            return shell;
        }

        private void EnsureGlowShell(State s)
        {
            if (s.Shell != null) return;
            s.Shell = BuildShell(s, "HeatGlowShell", GlowMaterial, inflate: 1.035f, pad: 0.015f);
            s.GlowRenderer = s.Shell != null ? s.Shell.GetComponent<MeshRenderer>() : null;
        }

        private static Bounds ComputeBounds(State s)
        {
            bool any = false;
            Bounds b = default;
            for (int i = 0; i < s.Renderers.Length; i++)
            {
                var r = s.Renderers[i];
                if (r == null) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!any && s.Transform != null)
                b = new Bounds(s.Transform.position, Vector3.one);
            return b;
        }

        // ── Particle factories ────────────────────────────────────────────────

        private static ParticleSystem CreateSmoke(State s)
        {
            if (s.Transform == null) return null;
            float cs = Mathf.Max(0.3f, s.Transform.lossyScale.magnitude * 0.4f);

            var go = new GameObject("HullSmoke");
            go.transform.SetParent(s.Transform, false);
            go.transform.localPosition = new Vector3(0f, cs * 0.32f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(cs * 0.12f, cs * 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(cs * 0.16f, cs * 0.30f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.13f, 0.13f, 0.14f, 0.55f),
                new Color(0.24f, 0.24f, 0.26f, 0.45f));
            main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.012f;   // buoyant
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = cs * 0.16f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient grad = new();
            grad.SetKeys(
                new GradientColorKey[] { new(new Color(0.5f, 0.5f, 0.52f), 0f), new(new Color(0.35f, 0.35f, 0.37f), 1f) },
                new GradientAlphaKey[] { new(0.6f, 0f), new(0.45f, 0.5f), new(0f, 1f) });
            colorOverLifetime.color = grad;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.6f,
                new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1.7f)));

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = SmokeMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.sortingFudge = 5f;
            return ps;
        }

        private static ParticleSystem CreateEmbers(State s)
        {
            if (s.Transform == null) return null;
            float cs = Mathf.Max(0.3f, s.Transform.lossyScale.magnitude * 0.4f);

            var go = new GameObject("HullEmbers");
            go.transform.SetParent(s.Transform, false);
            go.transform.localPosition = new Vector3(0f, cs * 0.18f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(cs * 0.5f, cs * 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(cs * 0.02f, cs * 0.05f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.42f, 0.10f),
                new Color(1f, 0.72f, 0.28f));
            main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.06f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = cs * 0.22f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient grad = new();
            grad.SetKeys(
                new GradientColorKey[] { new(new Color(1f, 0.85f, 0.45f), 0f), new(new Color(1f, 0.3f, 0.05f), 1f) },
                new GradientAlphaKey[] { new(1f, 0f), new(0.8f, 0.6f), new(0f, 1f) });
            colorOverLifetime.color = grad;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = EmberMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.sortingFudge = 5f;
            return ps;
        }

        private static void BuildDebrisBurst(Transform parent, float cs, Color tint, bool thermal)
        {
            var go = new GameObject("Debris");
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(cs * 1.2f, cs * 3.2f);
            main.startSize3D = false;
            main.startSize = new ParticleSystem.MinMaxCurve(cs * 0.07f, cs * 0.15f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.55f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, Mathf.RoundToInt(Random.Range(14f, 20f))) });

            Color chunk = Color.Lerp(tint * 0.7f, new Color(0.16f, 0.15f, 0.15f), 0.45f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                chunk,
                Color.Lerp(chunk, Color.white, 0.12f));

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = cs * 0.3f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 0.9f), new Keyframe(1f, 0.35f)));

            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.dampen = 0.45f;
            collision.bounce = 0.35f;
            collision.lifetimeLoss = 0.10f;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Mesh;
            rend.mesh = DebrisMesh;
            rend.sharedMaterial = DebrisMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.sortingFudge = 5f;
        }

        private static void BuildFlashBurst(Transform parent, float cs, bool thermal)
        {
            var go = new GameObject("Flash");
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(cs * 0.25f, cs * 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(cs * 0.22f, cs * 0.45f);
            main.maxParticles = 16;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            Color core = thermal ? new Color(1f, 0.62f, 0.22f) : new Color(0.85f, 0.85f, 0.8f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                core,
                new Color(core.r, core.g, core.b, 0.6f));

            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, Mathf.RoundToInt(Random.Range(8f, 12f))) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = cs * 0.2f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.6f,
                new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.1f)));

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient grad = new();
            grad.SetKeys(
                new GradientColorKey[] { new(Color.white, 0f), new(core, 0.35f) },
                new GradientAlphaKey[] { new(0.95f, 0f), new(0f, 1f) });
            colorOverLifetime.color = grad;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = FlashMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.sortingFudge = 5f;
        }

        // ── Shared resources ──────────────────────────────────────────────────
        // One material per effect, created once and shared by every emitter, so
        // bursts and smokers never leak material instances.

        private static Shader ParticleShader =>
            Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Particles/Standard Unlit");

        private static Shader UnlitShader =>
            Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Sprites/Default");

        private static Material _smokeMat, _emberMat, _debrisMat, _flashMat, _glowMat, _crackMat;

        private static Material SmokeMaterial
        {
            get
            {
                if (_smokeMat == null) _smokeMat = new Material(ParticleShader) { color = Color.white };
                return _smokeMat;
            }
        }

        private static Material EmberMaterial
        {
            get
            {
                if (_emberMat == null)
                {
                    _emberMat = new Material(ParticleShader) { color = Color.white };
                    MakeAdditive(_emberMat);
                }
                return _emberMat;
            }
        }

        private static Material DebrisMaterial
        {
            get
            {
                if (_debrisMat == null) _debrisMat = new Material(ParticleShader) { color = Color.white };
                return _debrisMat;
            }
        }

        private static Material FlashMaterial
        {
            get
            {
                if (_flashMat == null)
                {
                    _flashMat = new Material(ParticleShader) { color = Color.white };
                    MakeAdditive(_flashMat);
                }
                return _flashMat;
            }
        }

        private static Material GlowMaterial
        {
            get
            {
                if (_glowMat != null) return _glowMat;

                var m = new Material(UnlitShader);

                // Transparent additive so overlapping glow shells brighten each
                // other and the shell never occludes the hull beneath it.
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

                _glowMat = m;
                return m;
            }
        }

        /// <summary>Crack overlay: alpha-blended unlit cube shell. The crack
        /// stage texture is selected per renderer via a MaterialPropertyBlock so
        /// every damaged block shares this one material.</summary>
        private static Material CrackMaterial
        {
            get
            {
                if (_crackMat != null) return _crackMat;

                var m = new Material(UnlitShader);

                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 15; // under the glow

                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);

                _crackMat = m;
                return m;
            }
        }

        private static void MakeAdditive(Material m)
        {
            if (m == null) return;
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static Mesh _debrisMesh;
        private static Mesh DebrisMesh
        {
            get
            {
                if (_debrisMesh != null) return _debrisMesh;
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _debrisMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
                return _debrisMesh;
            }
        }

        // ── Per-block visual state ────────────────────────────────────────────

        private sealed class State
        {
            public Component Owner;                 // GridBlock / PlacedBlock / PlacedTieredBlock
            public Transform Transform;
            public Renderer[] Renderers = System.Array.Empty<Renderer>();
            public Color[] BaseColors = System.Array.Empty<Color>();
            public readonly MaterialPropertyBlock Mp = new();

            public float AppliedDamage = -1f;       // scorch ratio last applied
            public float AppliedGlow = -1f;         // shell alpha last applied
            public int AppliedCrackStage = -1;      // crack stage last applied
            public float TemperatureC;              // last reported temperature
            public float LastTempReport = -999f;    // Time.time of the last report
            public float Phase;                     // flicker offset

            public bool ContinuousSource;           // last damage was thermal/plume
            public bool SmokeLive;
            public bool EmbersLive;
            public bool WorldHosted;                // static placed block (temp decays)

            public ParticleSystem Smoke;
            public ParticleSystem Embers;
            public GameObject Shell;                // heat glow overlay
            public Renderer GlowRenderer;
            public GameObject CrackShell;           // crack overlay
            public Renderer CrackRenderer;
        }
    }
}
