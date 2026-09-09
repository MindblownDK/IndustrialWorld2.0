// Assets/Scripts/VoxelEngine/Thermal/GridThermalSystem.cs
//
// Per-grid block thermal simulation. One component per GridEntity tracks a
// temperature for every block, driven by four sources:
//
//   • ambient       — the planet's air (or the cold of space)
//   • entry heat    — ploughing through atmosphere at speed, applied to the
//                     leading face and attenuated by heatshields
//   • engine heat   — running thrusters cook themselves and conduct into neighbours
//   • machine heat  — any IHeatSourceBlock (hydrogen engines, maritime diesels and
//                     generators, reactors, furnaces, exhaust stacks) heats itself and
//                     its face neighbours while it works (9.31.0)
//   • room air      — a block sealed inside a compartment is standing in that
//                     compartment's air, and it cannot radiate into a sky it cannot
//                     see. Engine rooms therefore hold their heat long after the
//                     machinery has stopped (9.32.0, roadmap item 14)
//   • plume heat    — the exhaust column itself: any block of THIS grid standing in
//                     a nozzle's or exhaust stack's blast is heated by the hot gas
//                     (9.30.0 / 9.31.0). Blocks of other grids, static base blocks and
//                     the player are handled by ThrusterPlumeHazard, which reads the
//                     same plume model.
//
// Damage honours the block's heat tolerance family (ThermalRules.ToleranceC): glass
// and electronics fail first, hull plate at 800 °C, machinery is built hot, intact
// heat shields ablate instead of breaking.
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
        private VoxelEngine.Pressure.GridPressureSystem _pressure;

        private readonly Dictionary<GridBlock, float> _temperatures = new();
        private readonly List<GridBlock> _scratch = new();
        private readonly List<GridBlock> _blockSnapshot = new();
        private readonly List<PlumeSource> _plumes = new();

        /// <summary>A running nozzle or venting exhaust stack on this grid, cached per tick for plume queries.</summary>
        public readonly struct PlumeSource
        {
            /// <summary>Block emitting the plume: a GridThruster or an IExhaustPlumeSource block.</summary>
            public readonly GridBlock Source;
            public readonly Vector3 Nozzle;
            public readonly Vector3 ExhaustDir;
            public readonly float CellSize;
            public readonly float Load01;
            public readonly float Scale;

            /// <summary>The emitting thruster, or null when the plume comes from an exhaust stack.</summary>
            public GridThruster Thruster => Source as GridThruster;

            public PlumeSource(GridBlock source, Vector3 nozzle, Vector3 exhaustDir, float cellSize, float load01, float scale)
            {
                Source = source; Nozzle = nozzle; ExhaustDir = exhaustDir;
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

        /// <summary>True while any block is hot enough to be taking damage (per its own tolerance).</summary>
        public bool IsBurning => WorstBand == ThermalBand.Critical;

        /// <summary>Worst thermal band on the grid, judged per block against each block's tolerance.</summary>
        public ThermalBand Band => WorstBand;

        /// <summary>Worst band solved on the last tick (see <see cref="Band"/>).</summary>
        public ThermalBand WorstBand { get; private set; } = ThermalBand.Nominal;

        /// <summary>The block in the worst thermal state on the last tick, or null when everything is nominal.</summary>
        public GridBlock WorstBlock { get; private set; }

        /// <summary>Temperature of <see cref="WorstBlock"/> on the last tick.</summary>
        public float WorstBlockTemperatureC { get; private set; } = ThermalRules.FallbackAmbientC;

        // ── Concealed spaces (roadmap 5.1 item 14) ──────────────────────────────
        // A hull can read nominal while the volume welded around an engine is slowly
        // baking, so the worst COMPARTMENT is published apart from the worst block: the
        // HUD and the panels need to say "the engine room is the problem".

        /// <summary>Band of the worst sealed compartment on this grid this tick.</summary>
        public ThermalBand WorstRoomBand { get; private set; } = ThermalBand.Nominal;

        /// <summary>Temperature rise above outside air in that compartment, °C.</summary>
        public float WorstRoomRiseC { get; private set; }

        /// <summary>Air temperature of that compartment, °C.</summary>
        public float WorstRoomAirC { get; private set; } = ThermalRules.FallbackAmbientC;

        /// <summary>0..1 how foul the air in that compartment has become.</summary>
        public float WorstRoomExhaust01 { get; private set; }

        /// <summary>True while a sealed volume on this grid is hot enough to damage what is inside it.</summary>
        public bool RoomOverheating => WorstRoomBand == ThermalBand.Critical;

        /// <summary>Compartment cell → its own atmosphere, rebuilt once per thermal tick.</summary>
        private readonly Dictionary<Vector3Int, RoomAir> _roomAir = new();

        /// <summary>Block → the sealed volume it is standing in, rebuilt with the air table.</summary>
        private readonly Dictionary<GridBlock, VoxelEngine.Pressure.GridRoom> _blockRooms = new();

        /// <summary>A tracked compartment's air, as the blocks inside it experience it.</summary>
        private readonly struct RoomAir
        {
            /// <summary>°C above exterior ambient that interior blocks are pulled toward.</summary>
            public readonly float RiseC;
            /// <summary>0..1 cooling suppression: a wall of still air does not let heat escape.</summary>
            public readonly float CoolingPenalty;

            public RoomAir(float riseC, float coolingPenalty)
            {
                RiseC = riseC;
                CoolingPenalty = coolingPenalty;
            }
        }

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

        /// <summary>Temperature of whatever occupies a grid cell (ambient when untracked).</summary>
        public float TemperatureAt(Vector3Int cell)
            => _grid != null && _grid.Blocks.TryGetValue(cell, out var block) ? TemperatureOf(block) : AmbientC;

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
            RefreshRoomAir(ambient);

            float peak = ambient;
            var worstBand = ThermalBand.Nominal;
            GridBlock worstBlock = null;
            float worstTemperature = ambient;
            float worstSeverity = float.NegativeInfinity;
            _scratch.Clear();

            // Snapshot first: burning through a block removes it from the grid's block
            // dictionary, which must not happen while that dictionary is being enumerated.
            _blockSnapshot.Clear();
            foreach (var block in _grid.AllBlocks) _blockSnapshot.Add(block);

            for (int b = 0; b < _blockSnapshot.Count; b++)
            {
                var block = _blockSnapshot[b];
                if (block == null) continue;

                _roomAir.TryGetValue(block.GridPos, out var air);
                float localAmbient = ambient + air.RiseC;
                float target = TargetTemperature(block, localAmbient, travel);

                // Slew toward the target. Heating is comparatively quick; cooling is slow
                // (steel radiates poorly) with a boost in the last warm band so a hull
                // eventually settles to ambient instead of hovering lukewarm forever.
                float current = _temperatures.TryGetValue(block, out float t) ? t : localAmbient;
                float rate = ThermalRules.SlewRate(current, target, localAmbient);
                // A block boxed in by a compartment cools through still air rather than
                // open sky, so it holds its heat considerably longer.
                if (air.CoolingPenalty > 0f && target < current)
                    rate *= Mathf.Lerp(1f, 0.4f, air.CoolingPenalty);
                float next = Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

                // Damage honours the block family: glass at 520 °C, electronics at 600 °C,
                // hull plate at 800 °C, machinery at 1100 °C. An intact heat shield burns
                // ablator instead of structure; the shield's own tolerance is far higher.
                float tolerance = ThermalRules.ToleranceC(block);
                bool destroyed = false;
                if (block is GridHeatshield shield && shield.ShieldIntact)
                {
                    float shieldBurn = ThermalRules.BlockDamagePerSecond(next, ThermalRules.BlockDamageThresholdC);
                    if (shieldBurn > 0f) shield.Ablate(shieldBurn * dt);
                }
                else
                {
                    float burn = ThermalRules.BlockDamagePerSecond(next, tolerance);
                    if (burn > 0f) destroyed = block.Damage(burn * dt);
                }

                if (destroyed) { _scratch.Add(block); continue; }

                if (next > peak) peak = next;
                // The HUD asks "is anything on this hull failing?", which for a glass
                // canopy happens well below the steel threshold. Track the worst band.
                var blockBand = ThermalRules.Band(next, tolerance);
                float severity = next - tolerance;   // how far past (or short of) failure
                if (blockBand > worstBand || (blockBand == worstBand && blockBand != ThermalBand.Nominal && severity > worstSeverity))
                {
                    worstBand = blockBand; worstBlock = block; worstTemperature = next; worstSeverity = severity;
                }

                bool nearAmbient = Mathf.Abs(next - localAmbient) <= TrackingBandC
                                   && Mathf.Abs(target - localAmbient) <= TrackingBandC;
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
            WorstBand = worstBand;
            WorstBlock = worstBlock;
            WorstBlockTemperatureC = worstTemperature;
        }

        /// <summary>
        /// Rebuilds the compartment air table for this tick. Rooms are only asked for
        /// their atmosphere through the pressure service that is already attached, so a
        /// grid with no sealed volumes costs a single dictionary clear.
        /// </summary>
        private void RefreshRoomAir(float ambient)
        {
            _roomAir.Clear();
            _blockRooms.Clear();
            if (_grid == null) return;
            _pressure ??= _grid.GetComponent<VoxelEngine.Pressure.GridPressureSystem>();
            if (_pressure == null) return;

            var rooms = _pressure.Rooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room == null || !room.IsSealed) continue;

                float rise = Mathf.Max(0f, room.AirTemperatureC - ambient);
                foreach (var cell in room.Cells)
                {
                    if (rise >= 8f)   // below this there is nothing worth coupling a block to
                    {
                        float penalty = Mathf.Clamp01(rise / ThermalRules.RoomDamageHeatC);
                        _roomAir[cell] = new RoomAir(rise * ThermalRules.RoomAirTransmission, penalty);
                    }
                    if (_grid.Blocks.TryGetValue(cell, out var member) && member != null)
                        _blockRooms[member] = room;
                }
            }
        }

        /// <summary>
        /// The sealed compartment a block occupies, or null when it can see the sky.
        /// Sources ask this to decide whether their waste heat has anywhere to go.
        /// </summary>
        public VoxelEngine.Pressure.GridRoom ConcealedSpaceOf(GridBlock block)
        {
            if (block == null) return null;
            if (_blockRooms.TryGetValue(block, out var room)) return room;
            // The table is rebuilt once per thermal tick; before the first one lands
            // (placement, restore) resolve the same room lazily so a machine's very
            // first frame in a closed space is not silently counted as outdoors.
            _pressure ??= _grid != null ? _grid.GetComponent<VoxelEngine.Pressure.GridPressureSystem>() : null;
            return _pressure != null ? _pressure.RoomAtCell(block.GridPos) : null;
        }

        /// <summary>
        /// Called by the pressure service after it has solved its compartments, so the
        /// HUD never has to walk a grid to answer "is a room cooking?".
        /// </summary>
        public void PublishRoomWorst(IReadOnlyList<VoxelEngine.Pressure.GridRoom> rooms)
        {
            var band = ThermalBand.Nominal;
            float rise = 0f, air = AmbientC, exhaust = 0f;
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room == null || !room.IsSealed) continue;
                var b = room.Band;
                if (b == ThermalBand.Nominal) continue;
                float severity = room.RoomRiseC - ThermalRules.RoomSuitWarmRiseC;
                if (b > band || (b == band && severity > rise))
                {
                    band = b;
                    rise = severity;
                    air = room.AirTemperatureC;
                    exhaust = room.ExhaustLoad01;
                }
            }

            WorstRoomBand = band;
            WorstRoomRiseC = Mathf.Max(0f, rise);
            WorstRoomAirC = air;
            WorstRoomExhaust01 = exhaust;
        }

        /// <summary>Cache nozzle poses for every running thruster and venting exhaust stack on this grid.</summary>
        private void CollectPlumes()
        {
            _plumes.Clear();
            foreach (var block in _grid.AllBlocks)
            {
                if (block is GridThruster thruster)
                {
                    float load = ThrusterLoad01(thruster);
                    if (load <= 0.001f) continue;

                    float cs = thruster.EffectiveCellSize;
                    // The flame exits the block's local -forward (see GridThruster.PushDirection).
                    Vector3 exhaustDir = -thruster.transform.forward;
                    Vector3 nozzle = thruster.transform.position + exhaustDir * (cs * 0.5f);
                    _plumes.Add(new PlumeSource(thruster, nozzle, exhaustDir, cs, load, ThermalRules.PlumeScale(thruster.thrusterType)));
                }
                else if (block is IExhaustPlumeSource stack)
                {
                    float load = Mathf.Clamp01(stack.PlumeLoad01);
                    if (load <= 0.001f) continue;
                    Vector3 dir = stack.PlumeDirection;
                    if (dir.sqrMagnitude < 0.0001f) continue;
                    _plumes.Add(new PlumeSource(block, stack.PlumeOrigin, dir.normalized, block.EffectiveCellSize, load, stack.PlumeScale));
                }
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

            // ── Engine and machine heat ───────────────────────────────────────────
            // A working block heats itself; everything in a face-adjacent cell receives
            // the conducted share. Thrusters keep their dedicated load model; every other
            // machine reports through IHeatSourceBlock.
            float engineHeat = 0f;
            if (block is GridThruster thruster)
            {
                float throttle = ThrusterLoad01(thruster);
                if (throttle > 0.001f) engineHeat = ThermalRules.ThrusterPeakSelfHeatC * throttle;
            }
            else if (block is IHeatSourceBlock source)
            {
                engineHeat = Mathf.Max(0f, source.SelfHeatC);
            }
            engineHeat = Mathf.Max(engineHeat, AdjacentSourceHeat(block));

            // ── Plume heat: standing in a nozzle's or stack's exhaust ─────────────
            // A source is not heated by its own plume (the gas is leaving it), but it
            // is heated by a neighbour blowing into it.
            float plumeHeat = 0f;
            for (int i = 0; i < _plumes.Count; i++)
            {
                var plume = _plumes[i];
                if (ReferenceEquals(plume.Source, block)) continue;
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

        /// <summary>Heat conducted in from any running thruster or working machine in an adjacent cell.</summary>
        private float AdjacentSourceHeat(GridBlock block)
        {
            if (_grid == null || block.IsPrecisionAttachment) return 0f;

            float hottest = 0f;
            for (int i = 0; i < Neighbours.Length; i++)
            {
                if (!_grid.Blocks.TryGetValue(block.GridPos + Neighbours[i], out var other)) continue;
                if (ReferenceEquals(other, block)) continue;

                if (other is GridThruster thruster)
                {
                    float load = ThrusterLoad01(thruster);
                    if (load > 0f) hottest = Mathf.Max(hottest, ThermalRules.ThrusterNeighbourHeatC * load);
                }
                else if (other is IHeatSourceBlock source)
                {
                    hottest = Mathf.Max(hottest, Mathf.Max(0f, source.NeighbourHeatC));
                }
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
