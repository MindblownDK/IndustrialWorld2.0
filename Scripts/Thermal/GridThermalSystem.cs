// Assets/Scripts/VoxelEngine/Thermal/GridThermalSystem.cs
//
// Per-grid block thermal simulation. One component per GridEntity tracks a
// temperature for every block, driven by three sources:
//
//   • ambient      — the planet's air (or the cold of space)
//   • entry heat   — ploughing through atmosphere at speed, applied to the
//                    leading face and attenuated by heatshields
//   • engine heat  — running thrusters cook themselves and their neighbours
//
// Blocks slew toward their target temperature rather than snapping, so thermal
// mass is real: committing to a steep re-entry cannot be undone by throttling up.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Thermal
{
    [DisallowMultipleComponent]
    public class GridThermalSystem : MonoBehaviour
    {
        /// <summary>How often the (relatively expensive) target pass runs, in seconds.</summary>
        private const float ResolveInterval = 0.25f;

        /// <summary>Blocks colder than this are dropped from the table to keep it small.</summary>
        private const float TrackingFloorC = 60f;

        private GridEntity _grid;
        private float _resolveTimer;

        private readonly Dictionary<GridBlock, float> _temperatures = new();
        private readonly List<GridBlock> _scratch = new();

        /// <summary>Hottest block temperature on the grid this tick, in °C.</summary>
        public float PeakTemperatureC { get; private set; }

        /// <summary>Entry-heating stagnation temperature the grid is currently seeing.</summary>
        public float EntryHeatingC { get; private set; }

        /// <summary>True while any block is hot enough to be taking damage.</summary>
        public bool IsBurning => PeakTemperatureC >= ThermalRules.BlockDamageThresholdC;

        public ThermalBand Band => ThermalRules.Band(PeakTemperatureC);

        private void Awake() => _grid = GetComponent<GridEntity>();

        private void OnEnable() => ThermalService.Register(this);
        private void OnDisable() => ThermalService.Unregister(this);

        /// <summary>Attach (or fetch) the thermal service for a grid.</summary>
        public static GridThermalSystem For(GridEntity grid)
        {
            if (grid == null) return null;
            var svc = grid.GetComponent<GridThermalSystem>();
            if (svc == null) svc = grid.gameObject.AddComponent<GridThermalSystem>();
            return svc;
        }

        /// <summary>Temperature of a specific block in °C (ambient when untracked).</summary>
        public float TemperatureOf(GridBlock block)
        {
            if (block == null) return ThermalRules.FallbackAmbientC;
            return _temperatures.TryGetValue(block, out float t)
                ? t
                : ThermalRules.AmbientTemperatureC(transform.position);
        }

        private void FixedUpdate()
        {
            if (_grid == null) { _grid = GetComponent<GridEntity>(); if (_grid == null) return; }

            float dt = Time.fixedDeltaTime;
            _resolveTimer -= dt;
            if (_resolveTimer > 0f) return;

            float step = ResolveInterval - _resolveTimer;   // real elapsed time
            _resolveTimer = ResolveInterval;
            Tick(step);
        }

        private void Tick(float dt)
        {
            Vector3 origin = transform.position;
            float ambient = ThermalRules.AmbientTemperatureC(origin);

            Vector3 velocity = _grid.Body != null ? _grid.Body.linearVelocity : Vector3.zero;
            float speed = velocity.magnitude;
            EntryHeatingC = ThermalRules.EntryHeatingC(origin, speed);
            Vector3 travel = speed > 0.01f ? velocity / speed : Vector3.zero;

            float peak = ambient;
            _scratch.Clear();

            foreach (var block in _grid.AllBlocks)
            {
                if (block == null) continue;

                float target = TargetTemperature(block, ambient, travel);

                // Slew toward the target. Cooling is faster than heating so a hull that
                // survives entry actually recovers instead of staying red forever.
                float current = _temperatures.TryGetValue(block, out float t) ? t : ambient;
                float rate = ThermalRules.ThermalResponsePerSecond
                           * (target < current ? ThermalRules.CoolingRateMultiplier : 1f);
                float next = Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

                float burn = ThermalRules.BlockDamagePerSecond(next);
                if (burn > 0f)
                {
                    // Ablate shields first: that is precisely what they are for.
                    if (block is IHeatshieldBlock shield && shield.ShieldIntact)
                        (block as GridHeatshield)?.Ablate(burn * dt);
                    else
                        block.Damage(burn * dt);
                }

                if (next > peak) peak = next;

                if (next <= TrackingFloorC && target <= TrackingFloorC) _scratch.Add(block);
                else _temperatures[block] = next;
            }

            // Drop cold blocks so the table only carries what matters.
            for (int i = 0; i < _scratch.Count; i++) _temperatures.Remove(_scratch[i]);
            _scratch.Clear();

            PeakTemperatureC = peak;
        }

        /// <summary>
        /// Steady-state temperature this block is being driven toward right now.
        /// </summary>
        private float TargetTemperature(GridBlock block, float ambient, Vector3 travel)
        {
            float target = ambient;

            // ── Entry heating, weighted by how much this block faces the airflow ──
            if (EntryHeatingC > 0.01f && travel.sqrMagnitude > 0.01f)
            {
                float exposure = LeadingFaceExposure(block, travel);
                float transmission = Mathf.Lerp(ThermalRules.ShelteredTransmission, 1f, exposure);

                // A heatshield on the block itself, or shielding immediately ahead of it,
                // is what keeps a crew compartment survivable.
                if (block is IHeatshieldBlock own && own.ShieldIntact)
                    transmission *= own.HeatTransmission;
                else if (IsShieldedAhead(block, travel))
                    transmission *= ThermalRules.HeatshieldTransmission;

                target += EntryHeatingC * transmission;
            }

            // ── Engine heat ───────────────────────────────────────────────────────
            if (block is GridThruster thruster)
            {
                float throttle = ThrusterLoad01(thruster);
                if (throttle > 0.001f) target += ThermalRules.ThrusterPeakSelfHeatC * throttle;
            }
            else
            {
                float neighbourHeat = AdjacentThrusterHeat(block);
                if (neighbourHeat > 0f) target += neighbourHeat;
            }

            // ── Pressurised cabins are climate controlled ─────────────────────────
            // A sealed, powered room holds shirt-sleeve conditions, so interior blocks
            // never freeze to deep-space temperatures just because they are in orbit.
            if (target < ThermalRules.CabinTemperatureC
                && VoxelEngine.Pressure.PressureRules.Seals(block) && _grid.HasPower)
                target = Mathf.Max(target, ThermalRules.CabinTemperatureC);

            return target;
        }

        /// <summary>0..1 — how squarely this block faces the direction of travel.</summary>
        private float LeadingFaceExposure(GridBlock block, Vector3 travel)
        {
            Vector3 toBlock = block.transform.position - transform.position;
            if (toBlock.sqrMagnitude < 0.0001f) return 0.5f;
            return Mathf.Clamp01(Vector3.Dot(toBlock.normalized, travel) * 0.5f + 0.5f);
        }

        /// <summary>True when an intact heatshield sits in the cell directly upstream.</summary>
        private bool IsShieldedAhead(GridBlock block, Vector3 travel)
        {
            if (_grid == null) return false;

            // Convert the travel direction into the grid's local axes and step one cell
            // into the airflow. That cell is the one taking the heat on this block's behalf.
            Vector3 local = _grid.transform.InverseTransformDirection(travel);
            Vector3Int step = new(
                Mathf.RoundToInt(Mathf.Clamp(local.x, -1f, 1f)),
                Mathf.RoundToInt(Mathf.Clamp(local.y, -1f, 1f)),
                Mathf.RoundToInt(Mathf.Clamp(local.z, -1f, 1f)));
            if (step == Vector3Int.zero) return false;

            return _grid.Blocks.TryGetValue(block.GridPos + step, out var ahead)
                   && ahead is IHeatshieldBlock shield && shield.ShieldIntact;
        }

        /// <summary>Heat bleeding in from any running thruster in an adjacent cell.</summary>
        private float AdjacentThrusterHeat(GridBlock block)
        {
            if (_grid == null) return 0f;

            float hottest = 0f;
            for (int i = 0; i < Neighbours.Length; i++)
            {
                if (!_grid.Blocks.TryGetValue(block.GridPos + Neighbours[i], out var other)) continue;
                if (other is not GridThruster thruster) continue;

                float load = ThrusterLoad01(thruster);
                if (load > 0f) hottest = Mathf.Max(hottest, ThermalRules.ThrusterNeighbourHeatC * load);
            }
            return hottest;
        }

        /// <summary>0..1 load of a thruster, used as its heat driver.</summary>
        private static float ThrusterLoad01(GridThruster thruster)
        {
            if (thruster == null || !thruster.Enabled || !thruster.IsOperational) return 0f;
            return Mathf.Clamp01(thruster.ThrustFraction);
        }

        private static readonly Vector3Int[] Neighbours =
        {
            new(0, 1, 0), new(0, -1, 0),
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 0, 1), new(0, 0, -1),
        };
    }
}
