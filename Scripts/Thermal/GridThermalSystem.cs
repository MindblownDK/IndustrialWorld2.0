// Assets/Scripts/VoxelEngine/Thermal/GridThermalSystem.cs
//
// Per-grid block thermal simulation. One component per GridEntity tracks a
// temperature for every block, driven by four sources:
//
//   • ambient       — the planet's air (or the cold of space)
//   • entry heat    — ploughing through atmosphere at speed, applied to the
//                     leading face and attenuated by heatshields
//   • engine heat   — running thrusters cook themselves and conduct into neighbours
//   • plume heat    — the exhaust column itself: any block of THIS grid standing in
//                     a nozzle's blast is heated by the hot gas (9.30.0). Blocks of
//                     other grids, static base blocks and the player are handled by
//                     ThrusterPlumeHazard, which reads the same plume model.
//
// Blocks slew toward their target temperature rather than snapping, so thermal
// mass is real: committing to a steep re-entry cannot be undone by throttling up,
// and a hull that came in glowing stays too hot to touch for minutes (9.30.0 slowed
// cooling to 0.55x of heating; it used to bleed off in seconds).
//
// Every tracked block also feeds BlockDamageVisual so heat is visible as glow and
// structural loss is visible as cracks, on the ship and from the cockpit.

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

        /// <summary>Blocks within this many °C of ambient are dropped from the table.</summary>
        private const float TrackingBandC = 45f;

        private GridEntity _grid;
        private float _resolveTimer;

        private readonly Dictionary<GridBlock, float> _temperatures = new();
        private readonly List<GridBlock> _scratch = new();
        private readonly List<GridBlock> _blockSnapshot = new();
        private readonly List<PlumeSource> _plumes = new();

        /// <summary>A running nozzle on this grid, cached per tick for plume queries.</summary>
        public readonly struct PlumeSource
        {
            public readonly GridThruster Thruster;
            public readonly Vector3 Nozzle;
            public readonly Vector3 ExhaustDir;
            public readonly float CellSize;
            public readonly float Load01;
            public readonly float Scale;

            public PlumeSource(GridThruster thruster, Vector3 nozzle, Vector3 exhaustDir, float cellSize, float load01, float scale)
            {
                Thruster = thruster; Nozzle = nozzle; ExhaustDir = exhaustDir;
                CellSize = cellSize; Load01 = load01; Scale = scale;
            }

            /// <summary>Plume temperature (°C above ambient) delivered at a world point.</summary>
            public float TemperatureAt(Vector3 point)
                => ThermalRules.PlumeTemperatureAt(Nozzle, ExhaustDir, CellSize, Load01, point) * Scale;

            /// <summary>Farthest world distance from the nozzle the plume can reach.</summary>
            public float Reach => ThermalRules.PlumeLengthCells * CellSize * Mathf.Lerp(0.45f, 1f, Load01);
        }

        /// <summary>Hottest block temperature on the grid this tick, in °C.</summary>
        public float PeakTemperatureC { get; private set; }

        /// <summary>Entry-heating stagnation temperature the grid is currently seeing.</summary>
        public float EntryHeatingC { get; private set; }

        /// <summary>Ambient the grid solved against on the last tick.</summary>
        public float AmbientC { get; private set; } = ThermalRules.FallbackAmbientC;

        /// <summary>Number of blocks currently above ambient enough to be tracked.</summary>
        public int TrackedBlockCount => _temperatures.Count;

        /// <summary>Active exhaust plumes on this grid (empty when every engine is idle).</summary>
        public IReadOnlyList<PlumeSource> Plumes => _plumes;

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
            return _temperatures.TryGetValue(block, out float t) ? t : AmbientC;
        }

        /// <summary>
        /// Hottest tracked block temperature within <paramref name="radius"/> of a point.
        /// Used by the suit model so a player standing on a cold wing is not cooked by a
        /// glowing nose cone forty metres away.
        /// </summary>
        public float HottestTemperatureNear(Vector3 worldPoint, float radius)
        {
            float best = AmbientC;
            float r2 = radius * radius;
            foreach (var kv in _temperatures)
            {
                var block = kv.Key;
                if (block == null) continue;
                if ((block.transform.position - worldPoint).sqrMagnitude > r2) continue;
                if (kv.Value > best) best = kv.Value;
            }
            return best;
        }

        /// <summary>Temperature above ambient any of this grid's plumes deliver at a point.</summary>
        public float PlumeTemperatureAt(Vector3 worldPoint)
        {
            float hottest = 0f;
            for (int i = 0; i < _plumes.Count; i++)
            {
                float t = _plumes[i].TemperatureAt(worldPoint);
                if (t > hottest) hottest = t;
            }
            return hottest;
        }

        /// <summary>
        /// Injects external heat into a block (another ship's plume, a fire, a weapon).
        /// The value is a target temperature above ambient and lasts for this tick only.
        /// </summary>
        public void AddExternalHeat(GridBlock block, float temperatureAboveAmbientC)
        {
            if (block == null || temperatureAboveAmbientC <= 0f) return;
            _externalHeat ??= new Dictionary<GridBlock, float>();
            _externalHeat.TryGetValue(block, out float existing);
            if (temperatureAboveAmbientC > existing) _externalHeat[block] = temperatureAboveAmbientC;
        }
        private Dictionary<GridBlock, float> _externalHeat;

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
            AmbientC = ambient;

            Vector3 velocity = _grid.Body != null ? _grid.Body.linearVelocity : Vector3.zero;
            float speed = velocity.magnitude;
            EntryHeatingC = ThermalRules.EntryHeatingC(origin, speed);
            Vector3 travel = speed > 0.01f ? velocity / speed : Vector3.zero;

            CollectPlumes();

            float peak = ambient;
            _scratch.Clear();

            // Snapshot first: burning through a block removes it from the grid's block
            // dictionary, which must not happen while that dictionary is being enumerated.
            _blockSnapshot.Clear();
            foreach (var block in _grid.AllBlocks) _blockSnapshot.Add(block);

            for (int b = 0; b < _blockSnapshot.Count; b++)
            {
                var block = _blockSnapshot[b];
                if (block == null) continue;

                float target = TargetTemperature(block, ambient, travel);

                // Slew toward the target. Heating is comparatively quick; cooling is slow
                // (steel radiates poorly) with a boost in the last warm band so a hull
                // eventually settles to ambient instead of hovering lukewarm forever.
                float current = _temperatures.TryGetValue(block, out float t) ? t : ambient;
                float rate = ThermalRules.SlewRate(current, target, ambient);
                float next = Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

                float burn = ThermalRules.BlockDamagePerSecond(next);
                bool destroyed = false;
                if (burn > 0f)
                {
                    // Ablate shields first: that is precisely what they are for.
                    if (block is GridHeatshield shield && shield.ShieldIntact)
                        shield.Ablate(burn * dt);
                    else
                        destroyed = block.Damage(burn * dt);
                }

                if (destroyed) { _scratch.Add(block); continue; }

                if (next > peak) peak = next;

                bool nearAmbient = Mathf.Abs(next - ambient) <= TrackingBandC && Mathf.Abs(target - ambient) <= TrackingBandC;
                if (nearAmbient) _scratch.Add(block);
                else _temperatures[block] = next;

                // Visible heat: glow from ~450 °C upward. Reporting a cooled block once
                // more with "no glow" lets its visual fade out and go to sleep.
                BlockDamageVisual.ReportTemperature(block, next);
            }

            // Drop cold (or destroyed) blocks so the table only carries what matters,
            // plus anything dismantled since the last tick (a destroyed Unity object
            // still hashes by instance id, so Remove finds the stale entry).
            foreach (var kv in _temperatures)
                if (kv.Key == null) _scratch.Add(kv.Key);
            for (int i = 0; i < _scratch.Count; i++) _temperatures.Remove(_scratch[i]);
            _scratch.Clear();
            _blockSnapshot.Clear();
            _externalHeat?.Clear();

            PeakTemperatureC = peak;
        }

        /// <summary>Cache nozzle poses for every running thruster on this grid.</summary>
        private void CollectPlumes()
        {
            _plumes.Clear();
            foreach (var block in _grid.AllBlocks)
            {
                if (block is not GridThruster thruster) continue;
                float load = ThrusterLoad01(thruster);
                if (load <= 0.001f) continue;

                float cs = thruster.EffectiveCellSize;
                // The flame exits the block's local -forward (see GridThruster.PushDirection).
                Vector3 exhaustDir = -thruster.transform.forward;
                Vector3 nozzle = thruster.transform.position + exhaustDir * (cs * 0.5f);
                _plumes.Add(new PlumeSource(thruster, nozzle, exhaustDir, cs, load, ThermalRules.PlumeScale(thruster.thrusterType)));
            }
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
            float engineHeat = 0f;
            if (block is GridThruster thruster)
            {
                float throttle = ThrusterLoad01(thruster);
                if (throttle > 0.001f) engineHeat = ThermalRules.ThrusterPeakSelfHeatC * throttle;
            }
            else
            {
                engineHeat = AdjacentThrusterHeat(block);
            }

            // ── Plume heat: standing in another nozzle's exhaust ──────────────────
            // A thruster is not heated by its own plume (the gas is leaving it), but it
            // is heated by a neighbour firing straight into it.
            float plumeHeat = 0f;
            for (int i = 0; i < _plumes.Count; i++)
            {
                var plume = _plumes[i];
                if (ReferenceEquals(plume.Thruster, block)) continue;
                float t = plume.TemperatureAt(block.transform.position);
                if (t > plumeHeat) plumeHeat = t;
            }

            // Heat shields shrug off plume gas the same way they shrug off entry gas.
            if (plumeHeat > 0f && block is IHeatshieldBlock shieldBlock && shieldBlock.ShieldIntact)
                plumeHeat *= shieldBlock.HeatTransmission;

            float external = 0f;
            _externalHeat?.TryGetValue(block, out external);

            // Sources do not simply add; the hottest gas stream dominates and the others
            // top it up a little, which keeps stacked engines from producing silly numbers.
            float hottest = Mathf.Max(engineHeat, Mathf.Max(plumeHeat, external));
            float rest = engineHeat + plumeHeat + external - hottest;
            target += hottest + rest * 0.25f;

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

        /// <summary>Heat conducted in from any running thruster in an adjacent cell.</summary>
        private float AdjacentThrusterHeat(GridBlock block)
        {
            if (_grid == null || block.IsPrecisionAttachment) return 0f;

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
        public static float ThrusterLoad01(GridThruster thruster)
        {
            if (thruster == null || !thruster.Enabled || !thruster.IsOperational) return 0f;
            float load = Mathf.Clamp01(thruster.ThrustFraction);
            // Atmospheric engines lose authority (and exhaust energy) as the air thins.
            if (thruster.thrusterType == ThrusterType.Atmospheric) load *= thruster.AtmosphericEfficiency;
            return load;
        }

        private static readonly Vector3Int[] Neighbours =
        {
            new(0, 1, 0), new(0, -1, 0),
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 0, 1), new(0, 0, -1),
        };
    }
}
