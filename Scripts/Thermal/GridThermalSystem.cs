// Assets/Scripts/VoxelEngine/Thermal/GridThermalSystem.cs
//
// Per-grid block thermal simulation. One component per GridEntity tracks a
// temperature for every block, driven by four sources:
//
//   • ambient      — the planet's air (or the cold of space)
//   • entry heat   — ploughing through atmosphere at speed, applied to the
//                    leading face and attenuated by heatshields
//   • engine heat  — running thrusters cook themselves and conduct a little
//                    heat into the hull they are buried in
//   • exhaust plume — the directed flame leaving each nozzle: it impinges on
//                    the first block in its path (fast response, real erosion),
//                    splashes sideways around the impact, and reaches anything
//                    beyond the grid too — blocks on OTHER grids, placed base
//                    blocks (eroded directly) and the player (cooked via the
//                    open-air cells registered along the plume's path)
//
// Blocks slew toward their target temperature rather than snapping, so thermal
// mass is real: committing to a steep re-entry cannot be undone by throttling up.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Building.Tiered;
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

        /// <summary>External plume injections older than this are considered stale.</summary>
        private const float ExternalPlumeExpiry = 1f;

        private GridEntity _grid;
        private float _resolveTimer;
        private GridHullFx _fx;

        private readonly Dictionary<GridBlock, float> _temperatures = new();
        private readonly List<GridBlock> _scratch = new();

        // Snapshot of the block set each tick: destroying a burned block mutates
        // the grid's dictionary mid-iteration otherwise.
        private readonly List<GridBlock> _tickBlocks = new();

        // ── Exhaust plume maps (rebuilt every tick) ───────────────────────────
        private readonly Dictionary<GridBlock, PlumeLoad> _plume = new();     // direct impingement on this grid
        private readonly Dictionary<GridBlock, float> _wash = new();          // nozzle side wash + conduction
        private readonly Dictionary<GridBlock, ExternalPlume> _external = new(); // plumes arriving from other grids
        private readonly List<WorldPlumeCell> _worldCells = new();            // open-air plume cells (player exposure)

        private struct PlumeLoad
        {
            public float HeatC;            // °C added to the block's target
            public float ErosionPerSecond; // HP/s of direct mechanical erosion
        }

        private struct ExternalPlume
        {
            public float HeatC;
            public float ErosionPerSecond;
            public float Time;
        }

        /// <summary>An open-air cell of a live exhaust plume, in world space.
        /// Used to cook the player when they stand in the flame.</summary>
        public readonly struct WorldPlumeCell
        {
            public readonly Vector3 Pos;
            public readonly float HeatC;
            public readonly float Radius;

            public WorldPlumeCell(Vector3 pos, float heatC, float radius)
            {
                Pos = pos;
                HeatC = heatC;
                Radius = radius;
            }
        }

        /// <summary>Hottest block temperature on the grid this tick, in °C.</summary>
        public float PeakTemperatureC { get; private set; }

        /// <summary>Entry-heating stagnation temperature the grid is currently seeing.</summary>
        public float EntryHeatingC { get; private set; }

        /// <summary>Peak exhaust-plume heat applied this tick (HUD marker).</summary>
        public float PlumeHeatingC { get; private set; }

        /// <summary>Open-air cells occupied by live exhaust plumes this tick.
        /// The player hazard model uses these to cook anyone standing in a flame.</summary>
        public IReadOnlyList<WorldPlumeCell> WorldPlumeCells => _worldCells;

        /// <summary>True while any block is hot enough to be taking damage.</summary>
        public bool IsBurning => PeakTemperatureC >= ThermalRules.BlockDamageThresholdC;

        public ThermalBand Band => ThermalRules.Band(PeakTemperatureC);

        /// <summary>The grid this system simulates.</summary>
        public GridEntity Grid => _grid != null ? _grid : (_grid = GetComponent<GridEntity>());

        private void Awake()
        {
            _grid = GetComponent<GridEntity>();
        }

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

        /// <summary>
        /// Another grid's thruster plume hit one of our blocks. Refreshed every
        /// tick while the flame keeps hitting; expires shortly after it stops.
        /// </summary>
        public void InjectPlume(GridBlock block, float heatC, float erosionPerSecond)
        {
            if (block == null || heatC <= 0f) return;

            if (_external.TryGetValue(block, out var cur))
            {
                cur.HeatC = Mathf.Max(cur.HeatC, heatC);
                cur.ErosionPerSecond = Mathf.Max(cur.ErosionPerSecond, erosionPerSecond);
                cur.Time = Time.time;
                _external[block] = cur;
            }
            else
            {
                _external[block] = new ExternalPlume
                {
                    HeatC = heatC,
                    ErosionPerSecond = erosionPerSecond,
                    Time = Time.time,
                };
            }
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

            BuildPlumeMaps(dt);

            // Peak plume load this tick — drives the HUD's EXHAUST cause marker.
            float plumePeak = 0f;
            foreach (var kv in _plume) plumePeak = Mathf.Max(plumePeak, kv.Value.HeatC);
            foreach (var kv in _external) plumePeak = Mathf.Max(plumePeak, kv.Value.HeatC);
            PlumeHeatingC = plumePeak;

            float peak = ambient;
            _tickBlocks.Clear();
            foreach (var block in _grid.AllBlocks)
                if (block != null) _tickBlocks.Add(block);

            for (int i = 0; i < _tickBlocks.Count; i++)
            {
                var block = _tickBlocks[i];
                if (block == null) continue;

                float target = TargetTemperature(block, ambient, travel, out float response, out float plumeHeat);

                // Slew toward the target. Cooling is deliberately slower than
                // heating now — a hull that survives entry stays hot for a while,
                // which is exactly what makes the glow and the HUD strip readable.
                float current = _temperatures.TryGetValue(block, out float t) ? t : ambient;
                bool cooling = target < current;
                float rate = cooling
                    ? ThermalRules.ThermalResponsePerSecond * ThermalRules.CoolingRateMultiplier
                    : response;
                float next = Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

                // ── Thermal burn damage ────────────────────────────────────────
                bool destroyed = false;
                float burn = ThermalRules.BlockDamagePerSecond(next);
                if (burn > 0f)
                {
                    // Ablate shields first: that is precisely what they are for.
                    if (block is IHeatshieldBlock shield && shield.ShieldIntact)
                        (block as GridHeatshield)?.Ablate(burn * dt);
                    else
                        destroyed = block.Damage(burn * dt, impactFx: false);
                }

                // ── Exhaust plume erosion (direct flame sandblasting) ──────────
                if (!destroyed)
                {
                    float erosion = plumeHeat > 0f ? PlumeErosionOf(block) : 0f;
                    if (erosion > 0f)
                    {
                        if (block is IHeatshieldBlock shield && shield.ShieldIntact)
                            (block as GridHeatshield)?.Ablate(erosion * dt);
                        else
                            destroyed = block.Damage(erosion * dt, impactFx: false);
                    }
                }

                // A destroyed block leaves the grid this frame — drop it from the
                // tracking table so the entry can't linger behind a dead reference.
                if (destroyed)
                {
                    _temperatures.Remove(block);
                    continue;
                }

                if (next > peak) peak = next;

                if (next <= TrackingFloorC && target <= TrackingFloorC) _scratch.Add(block);
                else _temperatures[block] = next;

                // Feed the heat glow visuals (hot blocks shine; cooled ones fade).
                if (_fx == null && next >= ThermalRules.GlowVisibleC) _fx = GridHullFx.For(_grid);
                if (_fx != null)
                {
                    if (next >= ThermalRules.GlowVisibleC || _fx.HasState(block))
                        _fx.ReportTemperature(block, next);
                }
            }

            // Drop cold blocks so the table only carries what matters.
            for (int i = 0; i < _scratch.Count; i++) _temperatures.Remove(_scratch[i]);
            _scratch.Clear();

            PeakTemperatureC = peak;
        }

        /// <summary>
        /// Steady-state temperature this block is being driven toward right now,
        /// plus the slew rate that applies (direct plume impingement is fast).
        /// </summary>
        private float TargetTemperature(GridBlock block, float ambient, Vector3 travel,
            out float responsePerSecond, out float plumeHeat)
        {
            float target = ambient;
            responsePerSecond = ThermalRules.ThermalResponsePerSecond;
            plumeHeat = 0f;

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

            // ── Exhaust plume: directed, fast, erosive ────────────────────────────
            if (_plume.TryGetValue(block, out var ownPlume))
            {
                target += ownPlume.HeatC;
                plumeHeat = ownPlume.HeatC;
                if (ownPlume.HeatC >= 80f)
                    responsePerSecond = ThermalRules.PlumeResponsePerSecond;
            }
            else if (_wash.TryGetValue(block, out float wash))
            {
                target += wash;
            }

            // ── Plume arriving from another grid's thrusters ──────────────────────
            if (_external.TryGetValue(block, out var ext)
                && Time.time - ext.Time <= ExternalPlumeExpiry)
            {
                target += ext.HeatC;
                plumeHeat = Mathf.Max(plumeHeat, ext.HeatC);
                if (ext.HeatC >= 80f)
                    responsePerSecond = ThermalRules.PlumeResponsePerSecond;
            }

            // ── Pressurised cabins are climate controlled ─────────────────────────
            // A sealed, powered room holds shirt-sleeve conditions, so interior blocks
            // never freeze to deep-space temperatures just because they are in orbit.
            if (target < ThermalRules.CabinTemperatureC
                && VoxelEngine.Pressure.PressureRules.Seals(block) && _grid.HasPower)
                target = Mathf.Max(target, ThermalRules.CabinTemperatureC);

            return target;
        }

        /// <summary>Erosion rate currently applied to a block by any plume source.</summary>
        private float PlumeErosionOf(GridBlock block)
        {
            float erosion = 0f;
            if (_plume.TryGetValue(block, out var own)) erosion = own.ErosionPerSecond;
            if (_external.TryGetValue(block, out var ext)
                && Time.time - ext.Time <= ExternalPlumeExpiry)
                erosion = Mathf.Max(erosion, ext.ErosionPerSecond);
            return erosion;
        }

        // ── Plume construction ────────────────────────────────────────────────

        /// <summary>
        /// Walk every running thruster's exhaust cone and record what the flame
        /// hits: direct impingement on the first block in the path (with splash
        /// around the impact), side wash beside the nozzle, and mild conduction
        /// into every touching block. Plumes that exit the grid keep travelling
        /// and can hit blocks on other grids, placed base blocks — and the
        /// player, via the open-air cells registered along the way.
        /// </summary>
        private void BuildPlumeMaps(float dt)
        {
            _plume.Clear();
            _wash.Clear();
            _worldCells.Clear();
            PruneExternal();

            foreach (var block in _grid.AllBlocks)
            {
                if (block is not GridThruster thruster) continue;

                float load = ThrusterLoad01(thruster);
                if (load <= 0.01f) continue;

                // Detail-lattice engines are sub-cell machinery; their exhaust is
                // not a structural-scale flame, so they only conduct.
                float conduction = ThermalRules.ThrusterConductionHeatC * load;
                foreach (var n in Neighbours)
                {
                    if (_grid.Blocks.TryGetValue(thruster.GridPos + n, out var touched) && touched != null)
                        MaxAssign(_wash, touched, conduction);
                }
                if (thruster.IsPrecisionAttachment) continue;

                Vector3Int step = GridStepFor(-thruster.transform.forward);
                if (step == Vector3Int.zero) continue;

                float heat = ThermalRules.ThrusterPlumePeakC
                           * ThermalRules.PlumeHeatMultiplier(thruster.thrusterType) * load;
                float erosion = ThermalRules.ThrusterPlumeErosionPerSecond
                              * ThermalRules.PlumeErosionMultiplier(thruster.thrusterType) * load;

                // Nozzle side wash: the cells flanking the housing run warm.
                float sideWash = Mathf.Max(ThermalRules.ThrusterSideWashHeatC * load, conduction);
                foreach (var lateral in LateralsOf(step))
                {
                    if (_grid.Blocks.TryGetValue(thruster.GridPos + lateral, out var flank) && flank != null)
                        MaxAssign(_wash, flank, sideWash);
                }

                // The plume itself: step out along the exhaust axis.
                float cs = _grid.gridSize.CellSize();
                for (int i = 1; i <= ThermalRules.ThrusterPlumeLength; i++)
                {
                    int fi = Mathf.Clamp(i - 1, 0, ThermalRules.ThrusterPlumeFalloff.Length - 1);
                    float falloff = ThermalRules.ThrusterPlumeFalloff[fi];
                    Vector3Int cell = thruster.GridPos + step * i;

                    if (_grid.Blocks.TryGetValue(cell, out var struck) && struck != null)
                    {
                        // Direct impingement — the flame splashes around the struck cell.
                        MaxAssignPlume(_plume, struck, heat * falloff, erosion * falloff);
                        foreach (var lateral in LateralsOf(step))
                        {
                            if (_grid.Blocks.TryGetValue(cell + lateral, out var splashed) && splashed != null)
                                MaxAssignPlume(_plume, splashed, heat * falloff * 0.4f, erosion * falloff * 0.35f);
                        }
                        break;   // the struck block absorbs the plume
                    }

                    // Empty on this grid — the plume continues into open space.
                    // Register the open cell so the player hazard model can cook
                    // anyone standing in the flame, then see what it strikes.
                    if (ThermalRules.CrossGridPlumeDamage)
                    {
                        _worldCells.Add(new WorldPlumeCell(
                            _grid.GridToWorld(cell), heat * falloff, cs * 0.75f));

                        if (TryStrikePlumeTarget(cell, heat * falloff, erosion * falloff, dt))
                            break;
                    }
                }
            }
        }

        private void PruneExternal()
        {
            if (_external.Count == 0) return;
            _scratchExternal.Clear();
            foreach (var kv in _external)
                if (Time.time - kv.Value.Time > ExternalPlumeExpiry || kv.Key == null)
                    _scratchExternal.Add(kv.Key);
            for (int i = 0; i < _scratchExternal.Count; i++) _external.Remove(_scratchExternal[i]);
            _scratchExternal.Clear();
        }

        private readonly List<GridBlock> _scratchExternal = new();

        /// <summary>Project a plume cell into world space and resolve what the
        /// flame strikes beyond this grid: a block on another grid (its thermal
        /// system takes the load) or a placed base block (eroded directly).
        /// Returns true when the plume has been absorbed by a target.</summary>
        private bool TryStrikePlumeTarget(Vector3Int ownCell, float heatC, float erosion, float dt)
        {
            if (heatC <= 0f) return false;
            Vector3 world = _grid.GridToWorld(ownCell);

            var systems = ThermalService.All;
            for (int i = 0; i < systems.Count; i++)
            {
                var other = systems[i];
                if (other == null || other == this) continue;

                var otherGrid = other.Grid;
                if (otherGrid == null) continue;

                var cell = otherGrid.WorldToGrid(world);
                if (!otherGrid.Blocks.TryGetValue(cell, out var victim) || victim == null) continue;

                other.InjectPlume(victim, heatC, erosion);
                return true;
            }

            return StrikePlacedBlocks(world, heatC, erosion, dt);
        }

        /// <summary>Scratch buffer for the plume → placed-block overlap probe.</summary>
        private static readonly Collider[] PlumeOverlapHits = new Collider[24];

        /// <summary>
        /// Erode any placed base blocks (static PlacedBlock / tiered building
        /// pieces) occupying the plume cell. On-grid statics are skipped — they
        /// carry a GridBlock and already take the plume through the grid path.
        /// </summary>
        private bool StrikePlacedBlocks(Vector3 world, float heatC, float erosion, float dt)
        {
            float half = _grid.gridSize.CellSize() * 0.45f;
            int n = Physics.OverlapBoxNonAlloc(world, Vector3.one * half, PlumeOverlapHits,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var col = PlumeOverlapHits[i];
                if (col == null) continue;

                var placed = col.GetComponentInParent<PlacedBlock>();
                if (placed != null)
                {
                    if (!placed.onGrid)
                    {
                        ApplyPlumeToPlaced(placed, heatC, erosion, dt);
                        any = true;
                    }
                    continue;
                }

                var tiered = col.GetComponentInParent<PlacedTieredBlock>();
                if (tiered != null)
                {
                    ApplyPlumeToTiered(tiered, heatC, erosion, dt);
                    any = true;
                }
            }
            return any;
        }

        private static void ApplyPlumeToPlaced(PlacedBlock placed, float heatC, float erosion, float dt)
        {
            int dmg = Mathf.RoundToInt(erosion * dt);
            if (dmg < 1 && erosion > 0.25f) dmg = 1;
            if (dmg >= 1) placed.Damage(dmg, null, impactFx: false);

            // No thermal simulation behind static blocks — report the plume heat
            // directly so the block glows while it is being blasted, then decays.
            GridHullFx.ReportPlacedTemperature(placed, heatC);
        }

        private static void ApplyPlumeToTiered(PlacedTieredBlock tiered, float heatC, float erosion, float dt)
        {
            int dmg = Mathf.RoundToInt(erosion * dt);
            if (dmg < 1 && erosion > 0.25f) dmg = 1;
            if (dmg >= 1) tiered.Damage(dmg, 99, null, impactFx: false);

            GridHullFx.ReportPlacedTemperature(tiered, heatC);
        }

        private static void MaxAssign(Dictionary<GridBlock, float> map, GridBlock block, float value)
        {
            map.TryGetValue(block, out float cur);
            if (value > cur) map[block] = value;
        }

        private static void MaxAssignPlume(Dictionary<GridBlock, PlumeLoad> map, GridBlock block,
            float heat, float erosion)
        {
            map.TryGetValue(block, out var cur);
            if (heat > cur.HeatC || erosion > cur.ErosionPerSecond)
            {
                cur.HeatC = Mathf.Max(cur.HeatC, heat);
                cur.ErosionPerSecond = Mathf.Max(cur.ErosionPerSecond, erosion);
                map[block] = cur;
            }
        }

        /// <summary>Map a world direction onto the grid's dominant cell axis.</summary>
        private Vector3Int GridStepFor(Vector3 worldDir)
        {
            Vector3 local = _grid.transform.InverseTransformDirection(worldDir);
            Vector3 a = new(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));

            if (a.x >= a.y && a.x >= a.z)
                return new Vector3Int(local.x >= 0f ? 1 : -1, 0, 0);
            if (a.y >= a.z)
                return new Vector3Int(0, local.y >= 0f ? 1 : -1, 0);
            return new Vector3Int(0, 0, local.z >= 0f ? 1 : -1);
        }

        /// <summary>The four cell steps perpendicular to an axis-aligned step.</summary>
        private static Vector3Int[] LateralsOf(Vector3Int step)
        {
            if (step.x != 0) return LateralsX;
            if (step.y != 0) return LateralsY;
            return LateralsZ;
        }

        private static readonly Vector3Int[] LateralsX =
            { new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
        private static readonly Vector3Int[] LateralsY =
            { new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1) };
        private static readonly Vector3Int[] LateralsZ =
            { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0) };

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
