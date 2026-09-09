// Assets/Scripts/VoxelEngine/Maritime/GridExhaustPipe.cs
//
// Exhaust Pipe — venting / cooling. Every engine MUST have at least one
// adjacent exhaust pipe or it chokes (zero torque). The pipe vents exhaust
// gas from adjacent engines and emits visible smoke particles while venting.
//
// Mechanics:
//   • Finds engines by face-neighbours AND by world-space proximity — pipes snapped
//     to an engine's exhaust port can sit several lattice cells from the engine's
//     origin cell on the big modern engine models, so proximity is the truth test.
//   • While any adjacent engine is running + producing exhaust gas, the pipe
//     emits smoke particles styled after the engine's tier:
//       Tier 1 Crude   — pulsating dark blackish-grey puffs tuned to RPM.
//       Tier 2 HFO V8  — steady, thick dark-grey column.
//       Tier 3 MGO V12 — clean, lightly visible blueish-white fast stream.
//   • Engine upgrade modules reshape the plume: High-Flow Turbochargers raise
//     exhaust velocity, Overclocked Fuel Injectors dirty the smoke, and a
//     critical-overheat engine belches heavy black smoke at an increased rate.
//   • The actual vent RATE is handled inside GridMaritimeEngine.RefreshMaritimeNode
//     (it checks HasExhaust = adjacent pipe exists → reduces ExhaustGas).

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Thermal;

namespace VoxelEngine.Maritime
{
    public class GridExhaustPipe : MaritimeBlockBase, VoxelEngine.Thermal.IExhaustPlumeSource
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.ExhaustPipe;

        [Header("Exhaust VFX")]
        [Tooltip("Max smoke particles per second while venting.")]
        public float smokeRate = 40f;
        [Tooltip("Smoke colour when venting from a Giant Diesel (heavy black smoke).")]
        public Color heavySmoke = new Color(0.08f, 0.07f, 0.06f, 0.7f);
        [Tooltip("Smoke colour when venting from a Small Engine (light grey sputter).")]
        public Color lightSmoke = new Color(0.35f, 0.33f, 0.30f, 0.5f);

        [Header("Tiered Smoke Profiles")]
        [Tooltip("Tier 1 Crude Inline-4: pulsating dark blackish-grey puffs.")]
        public Color crudePuffSmoke = new Color(0.16f, 0.15f, 0.14f, 0.65f);
        [Tooltip("Tier 2 HFO V8: steady thick dark-grey column.")]
        public Color hfoColumnSmoke = new Color(0.22f, 0.21f, 0.20f, 0.7f);
        [Tooltip("Tier 3 MGO V12: clean light-grey / blueish-white high-velocity stream.")]
        public Color mgoStreamSmoke = new Color(0.82f, 0.85f, 0.88f, 0.22f);
        [Tooltip("Critical-overheat smoke: heavy oily black regardless of tier.")]
        public Color criticalSmoke = new Color(0.03f, 0.03f, 0.03f, 0.9f);
        [Tooltip("Rate multiplier applied while a neighbouring engine is in critical heat.")]
        public float criticalRateMultiplier = 1.6f;
        [Tooltip("How strongly dirty exhaust (fuel injectors) darkens the plume, 0-1.")]
        [Range(0f, 1f)] public float dirtyDarkenAmount = 0.55f;

        [Header("Exhaust-Gas Tap")]
        [Tooltip("Exhaust gas units/sec pushed into a connected gas network via the Port_ExhaustGasIO tap while venting. Captured exhaust produces visibly less smoke.")]
        public float gasTapFeedRate = 8f;

        private ParticleSystem _smokeFX;
        private bool _venting;
        private float _puffPhase;
        // Gas-tap capture state (rescan at 2 Hz; the captured share thins the plume).
        private VoxelEngine.Gas.GasTank _gasTapTank;          // classic world tank
        private GridGasTank _gasTapGridTank;                   // shipboard vessel
        private readonly System.Collections.Generic.List<GridBlock> _gasTapSeeds = new(6);
        private float _gasTapScanTimer;
        private Transform _gasTapPort;
        /// <summary>
        /// True when the player has already put a gas run on this stack (a port installed
        /// with the pipe tool, or a gas pipe snapped directly onto the tap flange). The
        /// authored flange itself never counts — otherwise a stack could never be hooked.
        /// </summary>
        public bool IsGasRunAttached
        {
            get
            {
                var tankPorts = GetComponent<GridTankVariablePorts>();
                if (tankPorts != null && tankPorts.CountPorts(GridTankPortFamily.Gas) > 0) return true;
                if (Grid == null) return false;
                foreach (var block in Grid.AllBlocks)
                {
                    if (block == null || block == this) continue;
                    if (block.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) == null) continue;
                    if (!IsTapAnchoredBlock(block)) continue;
                    return true;
                }
                return false;
            }
        }

        /// <summary>True while the tap has a run to pour into — a shipboard vessel or,
        /// on a world build, a classic gas tank. The plume thins out either way.</summary>
        public bool IsCapturingGas => _gasTapTank != null || _gasTapGridTank != null;

        /// <summary>0..1 share of the stream the connected gas network is taking away this frame.
        /// Captured gas is gas the compartment never has to swallow.</summary>
        public float CapturedShare01 { get; private set; }

        private static readonly Vector3Int[] Faces =
        {
            new( 1,0,0), new(-1,0,0),
            new( 0,1,0), new( 0,-1,0),
            new( 0,0,1), new( 0,0,-1),
        };
        private static readonly Collider[] s_engineProbe = new Collider[16];
        private readonly System.Collections.Generic.HashSet<GridMaritimeEngine> _foundEngines = new();

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Exhaust Pipe";
            CreateSmokeEffect();
            _needsOrient = true;
            _orientRetries = 0;
        }

        private void OnEnable()
        {
            // Freshly placed AND restored-from-save pipes both re-aim their intake
            // flange at the served engine's exhaust port on the first frame.
            _needsOrient = true;
            _orientRetries = 0;
        }

        /// <summary>True once the pipe has aimed its intake at the engine this spawn.</summary>
        private bool _needsOrient = true;
        private int _orientRetries;

        // ══════════════════════════════════════════════════════════════
        //  AUTO-ORIENT — point the intake flange (local −Z, Port_ExhaustInput)
        //  at the nearest engine exhaust-output port so the pipe always mounts
        //  the right way round: flange DOWN onto a top collector, outlet (+Z,
        //  Socket_StackTop) pointing away. Fixes pipes that were placed with a
        //  generic player rotation and ended up backwards on the engine.
        // ══════════════════════════════════════════════════════════════
        private void AutoOrientToEngine()
        {
            if (Grid == null)
            {
                // Grid not wired yet — retry for a short window, then give up.
                _needsOrient = ++_orientRetries < 30;
                return;
            }
            _needsOrient = false;

            float cs = Grid.gridSize.CellSize();
            GridMaritimeEngine engine = null;
            float bestSq = float.MaxValue;
            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position, cs * 1.8f, s_engineProbe, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitCount; i++)
            {
                var col = s_engineProbe[i];
                if (col == null) continue;
                var e = col.GetComponentInParent<GridMaritimeEngine>();
                if (e == null || e.Grid != Grid) continue;
                float d = (e.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; engine = e; }
            }
            if (engine == null) return; // no engine to serve — keep placement rotation

            // Aim at the nearest exhaust-output port (authored Port_ExhaustOutput*,
            // or a player-installed variable Port_ExhaustOutput_V).
            var port = MaritimePorts.FindNearest(
                engine.transform, MaritimePorts.ExhaustOutputPrefixes, transform.position, cs * 3.0f);
            Vector3 away = port != null
                ? transform.position - port.position
                : transform.position - engine.transform.position;
            if (away.sqrMagnitude < 0.0001f) away = Grid.transform.up;
            away = away.normalized;

            // +Z (outlet) points AWAY from the port ⇒ −Z (intake flange) faces it.
            Vector3 upHint = Mathf.Abs(Vector3.Dot(away, Grid.transform.up)) > 0.95f
                ? Grid.transform.forward
                : Grid.transform.up;
            transform.rotation = Quaternion.LookRotation(away, upHint.normalized);
        }

        /// <summary>True if any adjacent engine is currently venting exhaust gas.</summary>
        public bool IsVenting => _venting;

        // ── Heat source + exhaust plume (9.31.0) ─────────────────────────────
        // The stack casing runs hot while gas moves through it, and the gas leaving
        // the +Z outlet is a real (if comparatively cool) plume: it heats the hull
        // plates it blows across, a parked ship above a funnel, or a player on deck.
        // A seized engine belching black smoke pushes considerably more heat.

        /// <summary>0..1 vent intensity resolved on the last frame (exhaust backlog against the 50-unit reference).</summary>
        public float VentLoad01 { get; private set; }

        /// <summary>0..1 how much of this stack's gas is trapped by the compartment around it.
        /// Venting into a sealed volume means the stream has nowhere to go, so the plume
        /// collapses into the room instead of rising off the stack (roadmap 5.1 item 14).</summary>
        public float ConcealedVent01 { get; private set; }

        /// <summary>Temperature rise the trapped stream is holding the room's air at, °C above ambient.</summary>
        public float TrappedRiseC { get; private set; }

        /// <summary>True while this stack is venting into a volume that cannot clear it.</summary>
        public bool IsVentingIntoConcealedSpace => ConcealedVent01 > 0.15f;

        /// <summary>The sealed volume this stack is dumping into, if any.</summary>
        public VoxelEngine.Pressure.GridRoom ServedRoom => _servedRoom;
        private VoxelEngine.Pressure.GridRoom _servedRoom;

        // NOTE: the casing's own climb is expressed directly on SelfHeatC/NeighbourHeatC
        // below (up to +45 percent while the stream is blocked in) — the gas is still
        // arriving and it has nowhere to go, so there is no flow left to cool it.

        public float SelfHeatC => VentLoad01 > 0.001f
            ? ThermalRules.ExhaustPipeSelfHeatC * VentLoad01 * (_anyCriticalLastFrame ? 1.35f : 1f)
              * (1f + 0.45f * ConcealedVent01)
            : 0f;

        public float NeighbourHeatC => VentLoad01 > 0.001f
            ? ThermalRules.ExhaustPipeNeighbourHeatC * VentLoad01 * (_anyCriticalLastFrame ? 1.35f : 1f)
              * (1f + 0.45f * ConcealedVent01)
            : 0f;

        // A trapped stream does not blow a plume — the gas stops at the nearest bulkhead.
        public float PlumeLoad01 => VentLoad01 * (1f - ConcealedVent01);

        public Vector3 PlumeOrigin
        {
            get
            {
                float cs = Grid != null ? Grid.gridSize.CellSize() : 2.5f;
                return transform.position + transform.forward * (cs * 0.52f);
            }
        }

        public Vector3 PlumeDirection => transform.forward;

        public float PlumeScale => VoxelEngine.Thermal.ThermalRules.ExhaustPipePlumeScale * (_anyCriticalLastFrame ? 1.4f : 1f);

        private bool _anyCriticalLastFrame;

        private void Update()
        {
            if (_needsOrient) AutoOrientToEngine();
            if (_smokeFX == null) { VentLoad01 = 0f; return; }

            // Scan adjacent engines, keeping the strongest exhaust source as the
            // profile anchor while aggregating module modifiers across all of them.
            _venting = false;
            GridMaritimeEngine anchor = null;
            float maxExhaust = 0f;
            float smokeSpeedMul = 1f;
            bool anyDirty = false;
            bool anyCritical = false;

            // Collect every engine this pipe can vent for: face-neighbours (classic
            // 1-cell builds) PLUS world-space proximity, which is what actually matters
            // for port-snapped pipes on the big modern engine models — their lattice
            // cell may sit one or two cells away from the engine's origin cell.
            _foundEngines.Clear();
            if (Grid != null)
            {
                foreach (var off in Faces)
                {
                    if (Grid.GetBlock(GridPos + off) is GridMaritimeEngine eng)
                        _foundEngines.Add(eng);
                }
                float cs = Grid.gridSize.CellSize();
                int hitCount = Physics.OverlapSphereNonAlloc(
                    transform.position, cs * 1.35f, s_engineProbe, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hitCount; i++)
                {
                    var col = s_engineProbe[i];
                    if (col == null) continue;
                    var eng = col.GetComponentInParent<GridMaritimeEngine>();
                    if (eng != null && eng.Grid == Grid) _foundEngines.Add(eng);
                }
            }

            // The bellows stub that seals flange → port (rescan at 2 Hz).
            TickFlexCoupling();

            foreach (var eng in _foundEngines)
            {
                if (!eng.IsRunning || eng.ExhaustGas <= 0.5f) continue;

                _venting = true;
                if (eng.ExhaustGas > maxExhaust) { maxExhaust = eng.ExhaustGas; anchor = eng; }
                smokeSpeedMul = Mathf.Max(smokeSpeedMul, eng.SmokeSpeedMultiplier);
                anyDirty |= eng.SmokeDirty;
                anyCritical |= eng.IsCriticalHeat;
            }

            var emission = _smokeFX.emission;
            _anyCriticalLastFrame = anyCritical;
            if (!_venting || anchor == null)
            {
                emission.rateOverTime = 0f;
                VentLoad01 = 0f;
                // A stopped stack must still say so: compartments only hold what
                // their sources keep restating.
                UpdateConcealedSpace(0f, false, 0f);
                // The run stays scannable even while nothing vents, so a line built
                // onto a cold stack is recognised the moment it is finished.
                ScanGasTap();
                return;
            }

            var main = _smokeFX.main;
            float intensity = Mathf.Clamp01(maxExhaust / 50f);
            VentLoad01 = intensity;

            // ── Tier profile ────────────────────────────────────────
            Color baseColor;
            float rate = smokeRate * intensity;
            switch (anchor.tier)
            {
                case EngineTier.Medium:
                    // HFO V8 — steady, thick dark-grey column.
                    baseColor = hfoColumnSmoke;
                    rate *= 1.15f;
                    main.startLifetime = 3.4f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
                    break;

                case EngineTier.Giant:
                    // MGO V12 — clean, lightly visible blueish-white fast stream.
                    baseColor = mgoStreamSmoke;
                    rate *= 1.35f;
                    main.startLifetime = 1.4f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.5f);
                    break;

                default:
                    // Crude Inline-4 — pulsating puffs synced to engine RPM.
                    // A 4-stroke inline-4 fires twice per revolution; phase the
                    // emission pulse off that so the puffs visibly track throttle.
                    baseColor = crudePuffSmoke;
                    float puffsPerSecond = Mathf.Max(0.5f, anchor.CurrentRPM / 30f);
                    _puffPhase = (_puffPhase + Time.deltaTime * puffsPerSecond) % 1f;
                    float pulse = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Sin(_puffPhase * Mathf.PI * 2f) * 2.2f));
                    rate *= Mathf.Lerp(0.15f, 1.6f, pulse);
                    main.startLifetime = 2.2f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
                    break;
            }

            // ── Module / fault modifiers ────────────────────────────
            float speed = smokeSpeedMul;
            if (anyDirty)
            {
                // Overclocked Fuel Injectors — dark, sooty exhaust.
                baseColor = Color.Lerp(baseColor, new Color(0.05f, 0.045f, 0.04f, Mathf.Min(0.85f, baseColor.a + 0.25f)), dirtyDarkenAmount);
            }
            if (anyCritical)
            {
                // Critical heat — mechanical failure belches heavy black smoke.
                baseColor = criticalSmoke;
                rate *= criticalRateMultiplier;
                speed = Mathf.Max(speed * 0.6f, 0.7f); // sluggish, oily roll-off
            }

            // ── Gas-tap capture: a connected gas network swallows part of
            //    the stream as storable ExhaustGas and the plume thins out. ──
            TickGasTap(anchor);

            // ── Where does the gas actually go? (roadmap 5.1 item 14) ──────────
            // Run last so it reads this frame's intensity and this frame's capture
            // rate rather than the previous one's.
            UpdateConcealedSpace(VentLoad01, anyCritical, CapturedShare01);

            // High-Flow Turbochargers — higher exhaust velocity.
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * speed, 3f * speed);
            var velOverLife = _smokeFX.velocityOverLifetime;
            velOverLife.y = new ParticleSystem.MinMaxCurve(1f * speed, 2.5f * speed);

            emission.rateOverTime = IsCapturingGas ? rate * 0.45f : rate;
            main.startColor = baseColor;
        }

        // ── Concealed space (roadmap 5.1 item 14) ─────────────────────
        /// <summary>
        /// Decides whether the gas leaving this stack can escape. A funnel under an open
        /// sky is free; the same funnel welded into an engine room is a heater. Whatever
        /// the room swallows, the plume loses — the casing runs hotter, the room fills
        /// with foul gas, and the engines this stack serves feel back-pressure.
        /// </summary>
        /// <param name="load01">This frame's vent intensity (0 when the stack is idle).</param>
        /// <param name="anyCritical">True while a served engine is in critical heat.</param>
        /// <param name="captured01">Share of the stream a gas network is taking away.</param>
        private void UpdateConcealedSpace(float load01, bool anyCritical, float captured01)
        {
            _servedRoom = VoxelEngine.Pressure.GridPressureSystem.ConcealedRoom(this);

            // Whatever a pipe network is carrying away is by definition not trapped in
            // here, so capture relieves the room as surely as an open hatch does.
            float escape = load01 * Mathf.Clamp01(captured01);
            float trapped = Mathf.Clamp01(load01 - escape);

            float trappedScale = 0f;
            if (_servedRoom != null && trapped > 0.001f)
            {
                float size = Mathf.Max(1f, _servedRoom.Cells.Count);
                float openness = Mathf.Clamp01(size / 220f);
                trappedScale = Mathf.Lerp(0.15f, 1f, 1f - openness) * trapped;
            }
            ConcealedVent01 = trappedScale;
            TrappedRiseC = ThermalRules.TrappedExhaustRiseC(trapped * (anyCritical ? 1.2f : 1f));

            if (_servedRoom != null)
            {
                var pressure = Grid != null ? Grid.GetComponent<VoxelEngine.Pressure.GridPressureSystem>() : null;
                if (pressure != null)
                {
                    // Restated every frame, including with nothing to report: the room's
                    // air is the average of what its stacks are saying right now.
                    pressure.InjectExhaust(transform.position, trapped > 0.001f ? TrappedRiseC : 0f);
                    // The share of the stream's heat that the air keeps, on top of what
                    // the casing already conducts into its neighbours.
                    pressure.InjectWasteHeat(transform.position,
                        ThermalRules.RoomExhaustRisePerLoadC * trapped * 0.35f);
                }
            }

            // Hand the back-pressure and the room's ambient floor to every engine served.
            float exposure = _servedRoom != null
                ? _servedRoom.RoomRiseC * ThermalRules.RoomAirTransmission
                : 0f;
            foreach (var eng in _foundEngines)
            {
                if (eng == null || eng.Grid != Grid) continue;
                eng.SetThermalExposure(trappedScale, load01 > 0.001f ? exposure : 0f);
            }
        }

        // ── Exhaust-gas tap ───────────────────────────────────────────
        // The tap used to live inside the venting branch, which meant a line snapped
        // onto a cold stack was invisible: no pipes, no tank, and no way to build the
        // disposal run before starting the engine. It now scans on its own clock,
        // whether or not anything is venting, and it feeds whenever a sink answers.
        private void TickGasTap(GridMaritimeEngine anchor)
        {
            ScanGasTap();
            if (!IsCapturingGas || anchor == null) { CapturedShare01 = 0f; return; }

            float feed = gasTapFeedRate * Time.deltaTime;
            if (feed <= 0f) { CapturedShare01 = 0f; return; }

            // Expose the share the line is taking so the concealed-space pass can
            // subtract it instead of charging the room for gas already piped away.
            float accepted = 0f;
            if (_gasTapGridTank != null)
                accepted = _gasTapGridTank.AddTyped(VoxelEngine.Gas.GasType.ExhaustGas, feed);
            if (_gasTapTank != null && accepted < feed - 0.0001f)
                accepted += _gasTapTank.TryAdd(VoxelEngine.Gas.GasType.ExhaustGas, feed - accepted);

            // Nothing a shipboard tank could hold? Hand it to the vent at the far end
            // of this same run. Storage first, disposal second — a tank is worth more
            // than thin air, and a line that ends in a vent never fills one anyway.
            if (Grid != null && accepted < feed - 0.0001f
                && Grid.TryGetComponent(out GridGasNetwork dumpNetwork))
                accepted += Mathf.Min(feed - accepted, dumpNetwork.DumpGas(
                    this, VoxelEngine.Gas.GasType.ExhaustGas, feed - accepted));

            CapturedShare01 = feed > 0.0001f ? Mathf.Clamp01(accepted / feed) : 0f;
            if (accepted <= 0.0001f)
            {
                _gasTapTank = null;
                _gasTapGridTank = null;   // full / wrong gas / unplugged — rescan next window
                CapturedShare01 = 0f;
            }
        }

        /// <summary>Re-locate the sink behind the tap twice a second. Never returns early
        /// for want of a network: a grid run lives in GridGasNetwork, and the classic
        /// world tank network is consulted only as a fallback for shipless builds.</summary>
        private void ScanGasTap()
        {
            _gasTapScanTimer -= Time.deltaTime;
            if (_gasTapScanTimer > 0f) return;
            _gasTapScanTimer = 0.5f;

            if (HasTapSink()) return;
            _gasTapTank = null;
            _gasTapGridTank = null;

            if (_gasTapPort == null)
                _gasTapPort = MaritimePorts.FindNearest(transform, s_gasTapPortPrefix, transform.position);
            Vector3 origin = _gasTapPort != null ? _gasTapPort.position : transform.position;
            float cs = Grid != null ? Grid.gridSize.CellSize() : 2.5f;

            // Shipboard: any pipe of the run anchored to THIS stack. The anchor rule is
            // what keeps a passing oxygen line from being flooded with exhaust.
            if (Grid != null && Grid.TryGetComponent(out GridGasNetwork network))
            {
                CollectTapAnchoredPipes();
                if (_gasTapSeeds.Count > 0)
                {
                    var pipes = network.CollectGasPipesFrom(Grid, this, _gasTapSeeds);
                    for (int i = 0; i < pipes.Count; i++)
                    {
                        foreach (var block in UnifiedGridTopology.AdjacentBlocks(Grid, pipes[i]))
                        {
                            if (block is not GridGasTank tank || !tank.Enabled) continue;
                            if (!tank.CanAccept(VoxelEngine.Gas.GasType.ExhaustGas)) continue;   // typed: never hijacks an oxygen vessel
                            _gasTapGridTank = tank;
                            return;
                        }
                    }
                }
            }

            // Classic world tanks (planet builds with no grid gas service).
            var legacy = VoxelEngine.Gas.GasNetwork.Instance;
            if (legacy != null)
                _gasTapTank = legacy.FindTankNear(origin, VoxelEngine.Gas.GasType.ExhaustGas, forOutput: false,
                    searchDist: cs * 2.0f, corridorStep: cs, seedFilter: IsTapAnchoredPipe);
        }

        /// <summary>True while a previously found sink is still worth pouring into.</summary>
        private bool HasTapSink()
        {
            if (_gasTapGridTank != null && _gasTapGridTank.Enabled
                && _gasTapGridTank.CanAccept(VoxelEngine.Gas.GasType.ExhaustGas)) return true;
            if (_gasTapTank != null && _gasTapTank.capacity - _gasTapTank.storedAmount > 0.01f) return true;
            // A vent-only run stores nothing, so it must be re-probed rather than cached:
            // keeping the seeds lets the feed pass reach the vent every frame.
            return _gasTapSeeds.Count > 0 && Grid != null
                && Grid.TryGetComponent(out GridGasNetwork n) && n.HasVentFor(this, VoxelEngine.Gas.GasType.ExhaustGas);
        }

        /// <summary>Pipes snapped straight onto this stack — the legal seeds for the tap.</summary>
        private void CollectTapAnchoredPipes()
        {
            _gasTapSeeds.Clear();
            if (Grid == null) return;
            foreach (var block in Grid.AllBlocks)
            {
                if (block == null || block == this) continue;
                if (block.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) == null) continue;
                if (!IsTapAnchoredPipe(block.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true))) continue;
                _gasTapSeeds.Add(block);
            }
        }

        // The stack's own tap flange plus any player-added gas port, so a line built
        // onto a port installed with the pipe tool is recognised too.
        private static readonly string[] s_gasTapPortPrefix =
        {
            "Port_ExhaustGasIO",
            GridTankVariablePorts.PrefixFor(GridTankPortFamily.Gas),
        };

        /// <summary>Exhaust capture only flows through pipes ANCHORED TO THIS exhaust
        /// pipe (the dedicated capture run): an oxygen supply line that merely runs
        /// nearby must never get flooded with exhaust gas. From the anchored seeds
        /// the walk may continue down the run's own neighbours.</summary>
        private bool IsTapAnchoredPipe(VoxelEngine.Gas.GasPipe pipe)
        {
            if (pipe == null) return false;
            return IsTapAnchoredBlock(pipe.GetComponentInParent<GridBlock>());
        }

        /// <summary>Same rule for a block found by topology rather than by component.</summary>
        private bool IsTapAnchoredBlock(GridBlock block)
        {
            if (block == null) return false;
            if (block == null) return false;
            return block.IsPrecisionAttachment
                ? block.PrecisionHostGridPos == GridPos
                : block.GridPos == GridPos;
        }

        // ══════════════════════════════════════════════════════════════
        //  FLEX COUPLING — a bellows stub that seals this pipe's intake
        //  flange to the served engine's REAL exhaust-output port. Ports
        //  overhang the machine's own lattice cell (the MGO collectors sit
        //  two cells out), so the pipe body can't always kiss the port on
        //  its own — the coupling spans the residual gap and every exhaust
        //  hookup looks welded shut.
        // ══════════════════════════════════════════════════════════════
        private GameObject _flexCoupling;
        private Transform _intakePort;
        private float _couplingTimer;
        private static readonly string[] s_intakePortPrefix = { "Port_ExhaustInput" };

        private void TickFlexCoupling()
        {
            _couplingTimer -= Time.deltaTime;
            if (_couplingTimer > 0f) return;
            _couplingTimer = 0.5f;
            UpdateFlexCoupling();
        }

        private void UpdateFlexCoupling()
        {
            if (_intakePort == null)
                _intakePort = MaritimePorts.FindNearest(transform, s_intakePortPrefix, transform.position);
            if (_intakePort == null) return;

            float reach = EffectiveCellSize * 1.35f;
            Vector3 from = _intakePort.position;
            Transform target = null;
            float bestSq = reach * reach;
            foreach (var eng in _foundEngines)
            {
                if (eng == null) continue;
                var port = MaritimePorts.FindNearest(eng.transform, MaritimePorts.ExhaustOutputPrefixes, from, reach);
                if (port == null) continue;
                float d = (port.position - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; target = port; }
            }

            if (target == null || bestSq < 0.0004f)
            {
                if (_flexCoupling != null) { Destroy(_flexCoupling); _flexCoupling = null; }
                return;
            }

            if (_flexCoupling == null)
            {
                _flexCoupling = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _flexCoupling.name = "FlexCoupling";
                _flexCoupling.transform.SetParent(transform, true);
                var col = _flexCoupling.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var portRenderer = _intakePort.GetComponentInChildren<Renderer>();
                if (portRenderer != null)
                    _flexCoupling.GetComponent<Renderer>().sharedMaterial = portRenderer.sharedMaterial;
            }

            Vector3 to = target.position - from;
            float distance = to.magnitude;
            float tube = EffectiveCellSize * 0.085f;
            _flexCoupling.transform.position = from + to * 0.5f;
            _flexCoupling.transform.rotation = Quaternion.FromToRotation(Vector3.up, to.normalized);
            _flexCoupling.transform.localScale = new Vector3(tube, distance * 0.5f, tube);
        }

        private void CreateSmokeEffect()
        {
            var go = new GameObject("ExhaustSmoke");
            go.transform.SetParent(transform, false);
            float cs = Grid != null ? Grid.gridSize.CellSize() : 2.5f;
            // v18 straight pipe: smoke exits the +Z outlet (Socket_StackTop).
            go.transform.localPosition = new Vector3(0, 0, cs * 0.52f);

            _smokeFX = go.AddComponent<ParticleSystem>();
            var main = _smokeFX.main;
            main.loop = true;
            main.startLifetime = 2.5f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.maxParticles = 150;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.3f; // smoke rises
            main.startColor = lightSmoke;

            var shape = _smokeFX.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25;
            shape.radius = cs * 0.15f;

            var emission = _smokeFX.emission;
            emission.rateOverTime = 0;

            var velOverLife = _smokeFX.velocityOverLifetime;
            velOverLife.enabled = true;
            velOverLife.y = new ParticleSystem.MinMaxCurve(1f, 2.5f);

            var sizeOverLife = _smokeFX.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.3f);
            sizeCurve.AddKey(1f, 2.5f);
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var colorOverLife = _smokeFX.colorOverLifetime;
            colorOverLife.enabled = true;
            var colorGradient = new Gradient();
            colorGradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0.3f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(colorGradient);

            var rend = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            rend.material = new Material(sh) { color = lightSmoke };
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
