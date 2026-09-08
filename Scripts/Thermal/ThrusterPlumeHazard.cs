// Assets/Scripts/VoxelEngine/Thermal/ThrusterPlumeHazard.cs
//
// Makes thruster exhaust (and, since 9.31.0, maritime exhaust stacks) a real hazard
// for everything that is NOT the firing grid (the grid's own hull is solved inside
// GridThermalSystem):
//
//   • other grids          — heat is injected into the target grid's thermal system,
//                            so a parked ship behind your nozzles heats, glows and
//                            eventually burns through the normal thermal damage path
//   • static placed blocks — landing pads, hangar walls, machines: a per-block heat
//                            state (PlacedBlockHeat) slews, glows, cracks and loses HP
//   • tiered build pieces  — same as static blocks
//   • creatures            — Fire damage through IDamageable
//   • the player           — plume temperature is published to PlayerSuitThermal;
//                            the suit heats slowly and the crew burns from there
//
// One global ticker (0.2 s) walks every active plume and overlaps a capsule along
// the exhaust axis. Idle engines cost nothing.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Building.Tiered;
using VoxelEngine.Combat;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Thermal
{
    [DefaultExecutionOrder(50)]
    public class ThrusterPlumeHazard : MonoBehaviour
    {
        private const float TickInterval = 0.2f;
        private const int MaxHits = 96;

        private static ThrusterPlumeHazard s_instance;
        private static readonly Collider[] s_hits = new Collider[MaxHits];

        private float _timer;
        private readonly HashSet<Component> _seen = new();
        private readonly Dictionary<Component, float> _playerPlume = new();

        /// <summary>Plume temperature (°C above ambient) currently felt by the player, if any.</summary>
        public static float PlayerPlumeTemperatureC { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (s_instance != null) return;
            var go = new GameObject("_ThrusterPlumeHazard");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<ThrusterPlumeHazard>();
        }

        private void FixedUpdate()
        {
            _timer -= Time.fixedDeltaTime;
            if (_timer > 0f) return;
            float dt = TickInterval - _timer;
            _timer = TickInterval;
            Tick(dt);
        }

        private void Tick(float dt)
        {
            float playerPlume = 0f;
            var player = Player.PlayerStats.Instance;
            Vector3 playerPos = player != null ? player.transform.position + Vector3.up * 0.9f : Vector3.zero;

            var systems = ThermalService.All;
            for (int s = 0; s < systems.Count; s++)
            {
                var system = systems[s];
                if (system == null) continue;
                var plumes = system.Plumes;
                if (plumes.Count == 0) continue;

                var owner = system.GetComponent<GridEntity>();

                for (int p = 0; p < plumes.Count; p++)
                {
                    var plume = plumes[p];
                    float reach = plume.Reach;
                    if (reach <= 0.01f) continue;

                    // Player: pure geometry, no physics needed.
                    if (player != null)
                    {
                        float t = plume.TemperatureAt(playerPos);
                        if (t > playerPlume) playerPlume = t;
                    }

                    // Everything else: capsule along the axis, radius = cone at the tip.
                    float tipRadius = plume.CellSize * 0.35f + reach * Mathf.Tan(ThermalRules.PlumeHalfAngleDeg * Mathf.Deg2Rad);
                    Vector3 a = plume.Nozzle;
                    Vector3 b = plume.Nozzle + plume.ExhaustDir * reach;
                    int count = Physics.OverlapCapsuleNonAlloc(a, b, tipRadius, s_hits, ~0, QueryTriggerInteraction.Ignore);

                    _seen.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        var col = s_hits[i];
                        if (col == null) continue;
                        ApplyToCollider(col, plume, owner, dt);
                    }
                }
            }

            PlayerPlumeTemperatureC = playerPlume;
            System.Array.Clear(s_hits, 0, s_hits.Length);
        }

        private void ApplyToCollider(Collider col, GridThermalSystem.PlumeSource plume, GridEntity owner, float dt)
        {
            // ── Grid blocks on OTHER grids ───────────────────────────────────
            var gridBlock = col.GetComponentInParent<GridBlock>();
            if (gridBlock != null)
            {
                if (gridBlock.Grid == null || gridBlock.Grid == owner) return;   // own hull is solved locally
                if (!_seen.Add(gridBlock)) return;
                float t = plume.TemperatureAt(gridBlock.transform.position);
                if (t <= 1f) return;
                GridThermalSystem.For(gridBlock.Grid)?.AddExternalHeat(gridBlock, t);
                return;
            }

            // ── Static placed blocks / tiered build pieces ───────────────────
            var placed = col.GetComponentInParent<PlacedBlock>();
            if (placed != null)
            {
                if (!_seen.Add(placed)) return;
                float t = plume.TemperatureAt(col.bounds.center) * ThermalRules.PlumeStaticBlockTransmission;
                if (t > 1f) PlacedBlockHeat.For(placed).AddHeat(t);
                return;
            }

            var tiered = col.GetComponentInParent<PlacedTieredBlock>();
            if (tiered != null)
            {
                if (!_seen.Add(tiered)) return;
                float t = plume.TemperatureAt(col.bounds.center) * ThermalRules.PlumeStaticBlockTransmission;
                if (t > 1f) PlacedBlockHeat.For(tiered).AddHeat(t);
                return;
            }

            // ── Creatures ────────────────────────────────────────────────────
            var damageable = col.GetComponentInParent<IDamageable>();
            if (damageable is Component dc && damageable.IsAlive)
            {
                if (!_seen.Add(dc)) return;
                float t = plume.TemperatureAt(col.bounds.center);
                if (t <= 100f) return;
                float severity = Mathf.Clamp01(t / ThermalRules.PlumeCoreTemperatureC);
                damageable.TakeDamage(new DamageEvent
                {
                    amount = ThermalRules.PlumeCreatureDamagePerSecond * severity * dt,
                    type = DamageType.Fire,
                    point = col.bounds.center,
                    direction = plume.ExhaustDir,
                    source = plume.Source != null ? plume.Source.gameObject : null,
                });
            }
        }
    }
}
