// Assets/Scripts/VoxelEngine/Building/BuildSystem.cs
//
// Handles ghost preview + placement. Singleton attached to the player.
// Toggle grid-snap with the BuildToggleGrid keybind (default G).

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using VoxelEngine.Transport;
using VoxelEngine.GridSystem;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.Building
{
    public class BuildSystem : MonoBehaviour
    {
        public static BuildSystem Instance { get; private set; }
        public static bool IsCreatingGhost { get; private set; }

        [Header("Refs")]
        public Camera shootCamera;
        public Inventory inventory;

        [Header("Tuning")]
        /// <summary>Build raycast reach. Older player prefabs serialized the legacy
        /// 8 m value — Awake() raises any stale value to the modern reach.</summary>
        public float reach = 16f;

        /// <summary>The modern build reach (m). Anything shorter is treated as a stale
        /// serialized value from an older player prefab and auto-upgraded in Awake().</summary>
        public const float BuildReach = 16f;
        public bool  gridSnap = true;
        public float gridSize = 1f;
        public float ghostAlpha = 0.5f;

        [Header("Rotation")]
        public float yawStep = 90f;

        // Runtime
        private GameObject _ghost;
        private BlockItem  _ghostItem;
        private Material   _ghostMaterialValid;
        private Material   _ghostMaterialInvalid;
        private Vector3Int _rotSteps;
        private GridPrecisionLatticePreview _precisionLattice;
        private readonly System.Collections.Generic.List<Vector3> _pipeGhostLinks = new(1);
        private VoxelEngine.Networks.PipeVisualBuilder _pipeGhostVisual;
        private EntityId _pipeGhostTargetId;
        private Vector3 _pipeGhostLastPosition = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

        /// <summary>Set while a port snap is rejected because the engine's service
        /// port of that type is already at capacity. The ghost tints red and placement
        /// is blocked with a message instead of silently falling through to a free
        /// detail-lattice pipe (which would bypass the per-engine cap).</summary>
        private static bool s_portCapBlocked;
        private static string s_portCapReason;
        private static string s_portCapPipeFamily;
        private static int s_previewPortService;
        private static bool s_previewTankPort;
        private static VoxelEngine.GridSystem.GridTankPortFamily s_previewTankPortFamily;
        private float _portCapFeedbackAt;       // throttle bottom-right toast while aiming
        private const float PortCapFeedbackInterval = 1.2f;

        // Ghost-port preview ring (shown on the engine hull while the player aims
        // at a surface that will create a variable port) so the port collar is
        // visible BEFORE they click — fixes "ports isn't showing 100% of the time".
        private Transform _ghostPortRing;
        private Renderer _ghostPortRingRenderer;
        private Material _ghostPortRingMat;

        public static bool HoldingBlock { get; private set; }
        public static string HeldBlockName { get; private set; } = string.Empty;
        public static Vector3Int RotationSteps { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            if (GetComponent<VoxelEngine.Simulation.ConveyorShapeWheel>() == null)
                gameObject.AddComponent<VoxelEngine.Simulation.ConveyorShapeWheel>();

            // Create translucent ghost materials.
            _ghostMaterialValid   = MakeGhostMaterial(new Color(0.4f, 0.9f, 0.5f, ghostAlpha));
            _ghostMaterialInvalid = MakeGhostMaterial(new Color(0.95f, 0.35f, 0.3f, ghostAlpha));

            // Older player prefabs serialized the short 8 m build reach — upgrade it.
            if (reach < BuildReach) reach = BuildReach;
        }

        private void Update()
        {
            bool buildWheelHeld = GameSettings.IsHeld(InputAction.BuildWheel);
            if (VoxelEngine.UI.UIState.IsBlocking && !buildWheelHeld)
            {
                HoldingBlock = false;
                HeldBlockName = string.Empty;
                HideGhost();
                return;
            }

            // Toggle grid mode.
            if (GameSettings.WasPressed(InputAction.BuildToggleGrid))
                gridSnap = !gridSnap;

            HandleRotationInput();

            UpdateGhost();

            // Quarry placement preview
            UpdateQuarryPreview();
        }

        // ---------- Ghost ----------
        private void UpdateGhost()
        {
            if (inventory == null) { HoldingBlock = false; HeldBlockName = string.Empty; HideGhost(); return; }
            var stack = inventory.ActiveStack;
            if (stack.IsEmpty || !(stack.item is BlockItem block) || block.placedPrefab == null)
            {
                HoldingBlock = false;
                HeldBlockName = string.Empty;
                HideGhost();
                return;
            }
            HoldingBlock = true;
            HeldBlockName = block.displayName;
            if (_ghost == null || _ghostItem != block)
            {
                if (_ghost != null) Destroy(_ghost);
                try
                {
                    IsCreatingGhost = true;
                    _ghost = Instantiate(block.placedPrefab);
                }
                finally
                {
                    IsCreatingGhost = false;
                }
                _ghost.name = "BuildGhost";
                _ghostItem = block;
                _pipeGhostVisual = null;
                _pipeGhostTargetId = EntityId.None;
                _pipeGhostLastPosition = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                _pipeGhostLinks.Clear();
                StripGhost(_ghost, _ghostMaterialValid);
            }

            var ray = shootCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            HideGhostPortRing();

            if (!TryRaycastIgnoringSelf(ray, out var hit, reach))
            {
                // The ray slipped between the thin pipe visuals (their arms/caps are
                // collider-free — only the small hub box is ray-hittable). Gripping
                // the nearest pipe hub along the aim shows the continuation/branch
                // ghost instead of nothing; clicking places exactly this cell.
                if (IsUnifiedPipe(block) && TryChainAim(block, ray,
                        out GridEntity chainGrid, out Vector3Int chainPos,
                        out Vector3Int chainHost, out Vector3Int chainFace, out bool chainCanPlace))
                {
                    _ghost.SetActive(true);
                    ShowPrecisionLattice(chainGrid, chainHost, chainFace);

                    var chainVisual = _ghost.GetComponentInChildren<VoxelEngine.Networks.PipeVisualBuilder>(true);
                    if (chainVisual != null)
                        chainVisual.gridSize = GridSize.Small.CellSize();

                    Vector3 chainLocal = (Vector3)chainPos * GridSize.Small.CellSize();
                    Vector3 chainWorld = chainGrid.transform.TransformPoint(chainLocal);
                    Quaternion chainRot = chainGrid.transform.rotation
                        * Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
                    _ghost.transform.SetPositionAndRotation(chainWorld, chainRot);
                    ConfigurePipeGhostConnection(block, chainGrid, chainWorld, GridSize.Small.CellSize());
                    ApplyGhostMaterial(_ghost, chainCanPlace ? _ghostMaterialValid : _ghostMaterialInvalid);
                    return;
                }
                _ghost.SetActive(false);
                HidePrecisionLattice();
                return;
            }
            _ghost.SetActive(true);

            var targetGrid = hit.collider != null ? hit.collider.GetComponentInParent<GridEntity>() : null;
            if (targetGrid != null && IsUnifiedPipe(block))
            {
                Vector3Int precisionPos, hostStructuralPos, faceAxis;
                Vector3 ghostPortWorldPos = default; Vector3 ghostPortOutWorld = default;
                bool showGhostPort = false;
                Color ghostPortColor = Color.white;

                bool snappedToPort = TryGetMaritimePortSnap(targetGrid, block, hit, ray, commit: false,
                        out precisionPos, out hostStructuralPos, out faceAxis,
                        out Vector3 anchorLocal, out string portFeedback,
                        out Vector3 previewPortLocal, out Vector3 previewOutLocal, out bool previewIsNew);
                if (previewIsNew)
                {
                    var targetBlock = hit.collider.GetComponentInParent<GridBlock>();
                    if (targetBlock != null)
                    {
                        ghostPortWorldPos = targetBlock.transform.TransformPoint(previewPortLocal);
                        ghostPortOutWorld = targetBlock.transform.TransformDirection(previewOutLocal).normalized;
                        showGhostPort = true;
                        ghostPortColor = s_portCapBlocked
                            ? new Color(0.95f, 0.25f, 0.20f)
                            : s_previewTankPort
                                ? VoxelEngine.GridSystem.GridTankVariablePorts.ColorFor(s_previewTankPortFamily)
                                : VoxelEngine.Maritime.MaritimeVariablePorts.ColorFor(
                                    (VoxelEngine.Maritime.PortService)s_previewPortService);
                    }
                }

                // Over-cap aim: the engine refuses another port of this service. Show
                // the pipe at the aim point in RED and surface a toast instead of
                // silently falling through (which used to let players bypass the cap).
                bool validPrecision;
                if (s_portCapBlocked) validPrecision = false;
                else validPrecision = snappedToPort
                    || UnifiedGridTopology.TryGetDetailPlacement(
                        targetGrid, hit, out precisionPos, out hostStructuralPos, out faceAxis);
                ShowPrecisionLattice(targetGrid, hostStructuralPos, faceAxis);

                var pipeVisual = _ghost.GetComponentInChildren<VoxelEngine.Networks.PipeVisualBuilder>(true);
                if (pipeVisual != null)
                    pipeVisual.gridSize = GridSize.Small.CellSize();

                Vector3 localPosition = snappedToPort
                    ? anchorLocal
                    : s_portCapBlocked
                        ? targetGrid.transform.InverseTransformPoint(hit.point)
                        : (Vector3)precisionPos * GridSize.Small.CellSize();
                Vector3 worldPosition = targetGrid.transform.TransformPoint(localPosition);
                Quaternion worldRotation = targetGrid.transform.rotation
                    * Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
                _ghost.transform.SetPositionAndRotation(worldPosition, worldRotation);
                ConfigurePipeGhostConnection(block, targetGrid, worldPosition, GridSize.Small.CellSize());
                ApplyGhostMaterial(_ghost, validPrecision ? _ghostMaterialValid : _ghostMaterialInvalid);

                if (showGhostPort) ShowGhostPortRing(ghostPortWorldPos, ghostPortOutWorld, ghostPortColor);
                if (s_portCapBlocked && Time.unscaledTime - _portCapFeedbackAt >= PortCapFeedbackInterval)
                {
                    _portCapFeedbackAt = Time.unscaledTime;
                    VoxelEngine.UI.BuildFeedbackHud.Show(
                        $"{s_portCapPipeFamily} pipe reached",
                        s_portCapReason ?? "Port already connected",
                        block.icon, new Color(0.90f, 0.30f, 0.20f));
                }
                return;
            }
            HidePrecisionLattice();

            var ghostBelt = _ghost.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true);
            if (ghostBelt != null)
                ghostBelt.SetBuildShape(ResolveConveyorBuildShape(ghostBelt, hit));

            ComputePlacementPose(hit, block, out Vector3 pos, out Quaternion rot);
            _ghost.transform.SetPositionAndRotation(pos, rot);
            if (IsUnifiedPipe(block))
                ConfigurePipeGhostConnection(block, null, pos, Mathf.Max(0.01f, gridSize));

            bool valid = IsPlacementValid(pos, block);
            ApplyGhostMaterial(_ghost, valid ? _ghostMaterialValid : _ghostMaterialInvalid);
        }

        private void UpdateQuarryPreview()
        {
            if (_ghost == null || _ghostItem == null) { Quarry.HidePlacementPreview(); return; }
            var block = _ghostItem;
            if (block.placedPrefab == null) { Quarry.HidePlacementPreview(); return; }
            var q = block.placedPrefab.GetComponent<Quarry>();
            if (q == null) { Quarry.HidePlacementPreview(); return; }
            Quarry.ShowPlacementPreview(_ghost.transform.position, _ghost.transform.rotation, q.defaultSize, q.forwardOffset);
        }

        private void HideGhost()
        {
            HoldingBlock = false;
            HeldBlockName = string.Empty;
            RotationSteps = _rotSteps;
            if (_ghost != null) { Destroy(_ghost); _ghost = null; _ghostItem = null; }
            HidePrecisionLattice();
            HideGhostPortRing();
            Quarry.HidePlacementPreview();
        }

        private void OnDestroy()
        {
            if (_ghostPortRing != null) Destroy(_ghostPortRing.gameObject);
        }

        private void HandleRotationInput()
        {
            float scroll = GridInput.Scroll;
            bool ctrl = GridInput.Ctrl;
            bool shift = GridInput.Shift;
            if (Mathf.Abs(scroll) < 0.01f) return;
            if (!ctrl && !shift) return;

            int dir = scroll > 0 ? 1 : -1;
            if (ctrl && shift) _rotSteps.z = (_rotSteps.z + dir + 4) % 4;
            else if (ctrl) _rotSteps.y = (_rotSteps.y + dir + 4) % 4;
            else if (shift) _rotSteps.x = (_rotSteps.x + dir + 4) % 4;
            RotationSteps = _rotSteps;
        }

        private static bool IsUnifiedPipe(BlockItem block)
        {
            if (block == null || block.placedPrefab == null) return false;
            return block.placedPrefab.GetComponentInChildren<VoxelEngine.Transport.ItemPipe>(true) != null
                || block.placedPrefab.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null
                || block.placedPrefab.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null;
        }

        // ════════════════════════════════════════════════════════════════
        //  MARITIME PORT SNAP (liquids + gases)
        //  Pipes pulled toward a named service port on the targeted machine snap
        //  onto the exact Detail-lattice cell that hosts the port — regardless of
        //  grid mode and regardless of how far the machine's visual model overhangs
        //  its origin cell. LIQUID pipes snap ONLY to liquid ports (fuel, coolant,
        //  liquid IO — incl. the liquid tank's Port_LiquidIO); GAS pipes snap ONLY
        //  to gas ports (engine oxygen intake, the exhaust pipe's gas tap, generic
        //  gas IO, steam heat). Rotation stays player-controlled + auto-shaped by
        //  the pipe network builder, only the position is magnetised.
        // ════════════════════════════════════════════════════════════════
        private static bool TryGetMaritimePortSnap(GridEntity grid, BlockItem item, RaycastHit hit, Ray aimRay,
            bool commit, out Vector3Int precisionPos, out Vector3Int hostStructuralPos, out Vector3Int faceAxis,
            out Vector3 anchorLocal, out string feedback)
        {
            return TryGetMaritimePortSnap(grid, item, hit, aimRay, commit,
                out precisionPos, out hostStructuralPos, out faceAxis, out anchorLocal, out feedback,
                out _, out _, out _);
        }

        // Full overload — used by the ghost preview so it can draw a port collar
        // on the hull BEFORE the player commits to placement.
        private static bool TryGetMaritimePortSnap(GridEntity grid, BlockItem item, RaycastHit hit, Ray aimRay,
            bool commit, out Vector3Int precisionPos, out Vector3Int hostStructuralPos, out Vector3Int faceAxis,
            out Vector3 anchorLocal, out string feedback,
            out Vector3 newPortLocalPos, out Vector3 newPortOutLocal, out bool portIsNew)
        {
            precisionPos = default;
            hostStructuralPos = default;
            faceAxis = default;
            anchorLocal = default;
            feedback = null;
            newPortLocalPos = default;
            newPortOutLocal = default;
            portIsNew = false;
            s_portCapBlocked = false;
            s_portCapReason = null;
            s_portCapPipeFamily = null;
            s_previewPortService = 0;
            s_previewTankPort = false;
            s_previewTankPortFamily = VoxelEngine.GridSystem.GridTankPortFamily.Liquid;
            if (grid == null || item == null || item.placedPrefab == null || hit.collider == null) return false;

            // Route by the HELD pipe type — liquid pipes to liquid ports, gas pipes to
            // gas ports, item pipes to item ports (the fuel port doesn't take steam hoses).
            string[] prefixes;
            VoxelEngine.Maritime.PipeFamily family;
            string familyLabel;
            if (item.placedPrefab.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null)
            {
                prefixes = VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes;
                family = VoxelEngine.Maritime.PipeFamily.Liquid;
                familyLabel = "Liquid";
            }
            else if (item.placedPrefab.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null)
            {
                prefixes = VoxelEngine.Maritime.MaritimePorts.GasPrefixes;
                family = VoxelEngine.Maritime.PipeFamily.Gas;
                familyLabel = "Gas";
            }
            else if (item.placedPrefab.GetComponentInChildren<VoxelEngine.Transport.ItemPipe>(true) != null)
            {
                prefixes = VoxelEngine.Maritime.MaritimePorts.ItemPrefixes;
                family = VoxelEngine.Maritime.PipeFamily.Item;
                familyLabel = "Item";
            }
            else return false;

            var targetBlock = hit.collider.GetComponentInParent<GridBlock>();
            if (targetBlock == null || targetBlock.Grid != grid) return false;

            float small = GridSize.Small.CellSize();

            // Grid gas/liquid tanks use the same variable-port UX as maritime
            // engines: aim at the tank hull with the matching pipe, preview a colored
            // port ring, then install a dynamic Port_*_V and seat the pipe on the
            // Detail lattice just outside it.
            if (TryGetGridTankVariablePortSnap(grid, targetBlock, family, hit, commit,
                    out feedback, out precisionPos, out hostStructuralPos, out faceAxis,
                    out anchorLocal, out Vector3 tankPortLocal, out Vector3 tankOutLocal,
                    out bool tankPortIsNew, out VoxelEngine.GridSystem.GridTankPortFamily tankFamily))
            {
                newPortLocalPos = tankPortLocal;
                newPortOutLocal = tankOutLocal;
                portIsNew = tankPortIsNew;
                s_previewTankPort = true;
                s_previewTankPortFamily = tankFamily;
                return true;
            }
            // The big machine hitboxes reach far past their ports (an MGO fuel port
            // can sit ~4m inside the collider surface), so the snap radius must span
            // machine internals — not just the port's own overhang.
            float maxSnap = Mathf.Max(small * 3.5f, grid.gridSize.CellSize() * 2.0f);

            // TWO picks, hit-proximity first, aim-ray second. The ray pick covers the
            // case the hit-distance pick cannot: the player aims ACROSS the machine at
            // a port buried deep inside the hull, where the surface hit point sits
            // metres from the port. Buried ports lie past the hull along the aim, so
            // accept ray candidates up to maxSnap beyond the hit distance — far-side
            // ports (the ray would tunnel clean through the machine) stay rejected.
            Transform best = VoxelEngine.Maritime.MaritimePorts.FindNearest(
                targetBlock.transform, prefixes, hit.point, maxSnap);
            if (best == null)
            {
                best = VoxelEngine.Maritime.MaritimePorts.FindNearestToRay(
                    targetBlock.transform, prefixes, aimRay, maxLineDistance: 0.45f,
                    maxRayT: hit.distance + maxSnap);
            }
            if (best != null && IsRuntimeVariablePort(best))
            {
                Vector3 outWorldPreview = VoxelEngine.Maritime.MaritimePorts.PortOutwardWorld(best, hit.normal);
                newPortLocalPos = targetBlock.transform.InverseTransformPoint(best.position);
                newPortOutLocal = targetBlock.transform.InverseTransformDirection(outWorldPreview).normalized;
                portIsNew = true;
                s_portCapBlocked = true;
                s_portCapReason = "Variable port already has a pipe";
                s_portCapPipeFamily = familyLabel;
                return false;
            }
            if (best == null)
            {
                // No authored/dynamic port near the aim. Install (or re-snap to) a
                // color-coded VARIABLE service port right where the player is aiming —
                // "connect from anywhere". The port is born on the hull surface, so the
                // pipe can never end up buried inside the engine body. The planner
                // decides which service the held pipe family maps to and whether this
                // engine tier offers it (fuel/coolant/oxygen on HFO+MGO; oxygen+item on
                // the Crude engine).
                bool ok = TryGetVariablePortSnap(grid, targetBlock, family, hit, commit, out feedback,
                    out precisionPos, out hostStructuralPos, out faceAxis, out anchorLocal,
                    out Vector3 vpLocal, out Vector3 voLocal, out bool vpIsNew, out int vpService);
                if (vpIsNew)
                {
                    newPortLocalPos = vpLocal;
                    newPortOutLocal = voLocal;
                    portIsNew = true;
                    s_previewPortService = vpService;
                }
                if (s_portCapBlocked) s_portCapPipeFamily = familyLabel;
                return ok;
            }

            // Anchor = the seat for the pipe hub. Snug half-cell plug on surface ports;
            // for ports authored metres INSIDE the engine hull (MGO fuel/coolant/O₂)
            // SeatAnchorOutsideMachineShell finds the machine's own collider surface
            // along the port's authored facing (reverse ray) and seats half a cell
            // beyond it — the pipe lands just OUTSIDE the engine like a free-placed
            // pipe beside it.
            Vector3 local = grid.transform.InverseTransformPoint(best.position);

            Vector3 hostLocal = grid.transform.InverseTransformPoint(targetBlock.transform.position);
            Vector3 offsetOut = (local - hostLocal).normalized;
            Vector3 fallbackWorld = offsetOut.sqrMagnitude > 0.0001f
                ? grid.transform.TransformDirection(offsetOut)
                : hit.normal;
            Vector3 outWorld = VoxelEngine.Maritime.MaritimePorts.PortOutwardWorld(best, fallbackWorld);
            Vector3 outLocal = grid.transform.InverseTransformDirection(outWorld).normalized;
            anchorLocal = SeatAnchorOutsideMachineShell(targetBlock, grid, local, outLocal, small);
            // The Detail-lattice cell the pipe CLAIMS follows the hub: on surface ports
            // this is the cell just beyond the port face (same as before); when the seat
            // was pushed out past the hull the occupancy lives where the hub actually is.
            Vector3 cellPos = anchorLocal + outLocal * (small * 0.25f);
            precisionPos = new Vector3Int(
                Mathf.FloorToInt(cellPos.x / small + 0.5f),
                Mathf.FloorToInt(cellPos.y / small + 0.5f),
                Mathf.FloorToInt(cellPos.z / small + 0.5f));
            hostStructuralPos = targetBlock.GridPos;

            // Outward face axis — prefer the port's true authored facing; fallback:
            // dominant grid-space direction from the machine's origin toward the port.
            faceAxis = UnifiedGridTopology.SnapFaceAxis(grid, outWorld);
            if (faceAxis == Vector3Int.zero)
                faceAxis = offsetOut.sqrMagnitude > 0.0001f
                    ? UnifiedGridTopology.SnapFaceAxis(grid, grid.transform.TransformDirection(offsetOut))
                    : UnifiedGridTopology.SnapFaceAxis(grid, hit.normal);

            var layer = grid.GetComponent<GridPrecisionAttachmentLayer>();
            if (layer != null && layer.CanPlace(precisionPos)) return true;
            // Facing cell taken — port snap fails, let placement fall through
            // to normal grid placement. NEVER fall back to the port's own cell
            // (it sits inside the engine body and makes the pipe render inside).
            return false;
        }

        // ════════════════════════════════════════════════════════════════
        //  PIPE HUB SEATING — simple full-cell offset
        //  The pipe hub sits exactly one full Detail cell outward from the
        //  port's own position, along the port's authored facing direction.
        //  This guarantees the pipe body is completely outside the engine
        //  body, even for deep-buried ports (MGO fuel/coolant/O₂).
        //  The widened proximity range (2.5× cell) ensures the pipe is still
        //  found as connected to the engine despite the full-cell gap.
        //  ⚠ Do NOT use collider-dependent reverse raycasting — complex
        //  banded hitboxes (MGO slim/full-width bands) make it unreliable.
        // ════════════════════════════════════════════════════════════════
        /// <summary>
        /// Compute the pipe-hub seat for a port. Always sits one full Detail cell (0.5 m)
        /// outward from the port position along its outward facing. Simple, reliable,
        /// and guaranteed outside the engine body regardless of collider geometry.
        /// Expressed in GRID-LOCAL space.
        /// </summary>
        private static Vector3 SeatAnchorOutsideMachineShell(
            GridBlock machine, GridEntity grid, Vector3 portLocal, Vector3 outLocal, float small)
        {
            // 1.4 Detail cells outward from the authored port — comfortably outside
            // the engine body and visibly proud of the hull so the player can see the
            // hub and chain the next pipe onto it. (Variable service ports seat from
            // the actual hull surface and are the preferred, always-outside path.)
            return portLocal + outLocal * (small * 1.4f);
        }

        // ════════════════════════════════════════════════════════════════
        //  VARIABLE SERVICE PORT SNAP
        //  Player aims at the body of a liquid HFO/MGO engine with no authored
        //  port nearby → a color-coded service port (fuel/coolant/oxygen) is
        //  installed at the surface hit and the pipe snaps onto it. Ghost calls
        //  with commit=false to preview the seat without mutating the engine;
        //  placement calls with commit=true to actually install the port. Both
        //  run identical geometry so ghost ≡ placed.
        // ════════════════════════════════════════════════════════════════
        private static bool IsRuntimeVariablePort(Transform port)
        {
            return port != null && port.name.EndsWith("_V", System.StringComparison.Ordinal);
        }

        private static bool TryGetGridTankVariablePortSnap(GridEntity grid, GridBlock targetBlock,
            VoxelEngine.Maritime.PipeFamily pipeFamily, RaycastHit hit, bool commit, out string feedback,
            out Vector3Int precisionPos, out Vector3Int hostStructuralPos, out Vector3Int faceAxis,
            out Vector3 anchorLocal, out Vector3 portLocalPos, out Vector3 portOutLocal,
            out bool portIsNew, out VoxelEngine.GridSystem.GridTankPortFamily tankFamily)
        {
            feedback = null;
            precisionPos = default;
            hostStructuralPos = default;
            faceAxis = default;
            anchorLocal = default;
            portLocalPos = default;
            portOutLocal = default;
            portIsNew = false;
            tankFamily = VoxelEngine.GridSystem.GridTankPortFamily.Liquid;

            if (grid == null || targetBlock == null || targetBlock.Grid != grid) return false;
            if (pipeFamily == VoxelEngine.Maritime.PipeFamily.Liquid)
            {
                if (targetBlock is not VoxelEngine.GridSystem.GridLiquidTank) return false;
                tankFamily = VoxelEngine.GridSystem.GridTankPortFamily.Liquid;
            }
            else if (pipeFamily == VoxelEngine.Maritime.PipeFamily.Gas)
            {
                if (targetBlock is not VoxelEngine.GridSystem.GridGasTank
                    && targetBlock is not VoxelEngine.GridSystem.GridCryobed) return false;
                tankFamily = VoxelEngine.GridSystem.GridTankPortFamily.Gas;
            }
            else return false;

            float small = GridSize.Small.CellSize();
            faceAxis = UnifiedGridTopology.SnapFaceAxis(grid, hit.normal);
            if (faceAxis == Vector3Int.zero) faceAxis = Vector3Int.up;
            Vector3 outLocal = new Vector3(faceAxis.x, faceAxis.y, faceAxis.z).normalized;
            Vector3 outWorld = grid.transform.TransformDirection(outLocal).normalized;

            Vector3 rawSeatGridLocal = grid.transform.InverseTransformPoint(hit.point + outWorld * (small * 0.55f));
            precisionPos = new Vector3Int(
                Mathf.FloorToInt(rawSeatGridLocal.x / small + 0.5f),
                Mathf.FloorToInt(rawSeatGridLocal.y / small + 0.5f),
                Mathf.FloorToInt(rawSeatGridLocal.z / small + 0.5f));
            anchorLocal = (Vector3)precisionPos * small;
            hostStructuralPos = targetBlock.IsPrecisionAttachment ? targetBlock.PrecisionHostGridPos : targetBlock.GridPos;

            var layer = grid.GetComponent<GridPrecisionAttachmentLayer>();
            if (layer != null && !layer.CanPlace(precisionPos)) return false;

            Vector3 seatWorld = grid.transform.TransformPoint(anchorLocal);
            Vector3 portWorld = seatWorld - outWorld * (small * 0.55f + 0.02f);
            portLocalPos = targetBlock.transform.InverseTransformPoint(portWorld);
            portOutLocal = targetBlock.transform.InverseTransformDirection(outWorld).normalized;
            if (portOutLocal.sqrMagnitude < 0.0001f) portOutLocal = Vector3.up;

            var ports = targetBlock.GetComponent<VoxelEngine.GridSystem.GridTankVariablePorts>();
            portIsNew = true;
            if (commit)
            {
                if (ports == null) ports = targetBlock.gameObject.AddComponent<VoxelEngine.GridSystem.GridTankVariablePorts>();
                ports.AddPort(tankFamily, portLocalPos, portOutLocal);
            }

            feedback = VoxelEngine.GridSystem.GridTankVariablePorts.LabelFor(tankFamily) + " installed";
            return true;
        }

        private static bool TryGetVariablePortSnap(GridEntity grid, GridBlock targetBlock,
            VoxelEngine.Maritime.PipeFamily family, RaycastHit hit, bool commit, out string feedback,
            out Vector3Int precisionPos, out Vector3Int hostStructuralPos, out Vector3Int faceAxis,
            out Vector3 anchorLocal)
        {
            return TryGetVariablePortSnap(grid, targetBlock, family, hit, commit, out feedback,
                out precisionPos, out hostStructuralPos, out faceAxis, out anchorLocal,
                out _, out _, out _, out _);
        }

        private static bool TryGetVariablePortSnap(GridEntity grid, GridBlock targetBlock,
            VoxelEngine.Maritime.PipeFamily family, RaycastHit hit, bool commit, out string feedback,
            out Vector3Int precisionPos, out Vector3Int hostStructuralPos, out Vector3Int faceAxis,
            out Vector3 anchorLocal,
            out Vector3 portLocalPos, out Vector3 portOutLocal, out bool portIsNew, out int portService)
        {
            precisionPos = default;
            hostStructuralPos = default;
            faceAxis = default;
            anchorLocal = default;
            feedback = null;
            portLocalPos = default;
            portOutLocal = default;
            portIsNew = false;
            portService = 0;

            var engine = targetBlock as VoxelEngine.Maritime.GridMaritimeEngine;
            if (engine == null || engine.Grid != grid) return false;

            float small = GridSize.Small.CellSize();
            var plan = VoxelEngine.Maritime.MaritimePortPlanner.PlanPipe(
                grid, engine, family, hit.point, hit.normal, small);

            if (plan.atCap)
            {
                int max = VoxelEngine.Maritime.MaritimeVariablePorts.MaxFor(plan.service);
                feedback = $"{VoxelEngine.Maritime.MaritimeVariablePorts.LabelFor(plan.service)} already connected (max {max})";
                s_portCapBlocked = true;
                s_portCapReason = feedback;
                return false;
            }
            if (!plan.ok) return false;

            // Expose the planned port to the caller so the ghost preview can draw
            // a color-coded collar on the hull before the player clicks.
            portLocalPos = plan.portLocal;
            portOutLocal = plan.outLocal;
            portIsNew = !plan.reusesExisting;
            portService = (int)plan.service;

            // Snap the pipe hub onto the DETAIL lattice cell just outside the surface.
            precisionPos = new Vector3Int(
                Mathf.FloorToInt(plan.seatGridLocal.x / small + 0.5f),
                Mathf.FloorToInt(plan.seatGridLocal.y / small + 0.5f),
                Mathf.FloorToInt(plan.seatGridLocal.z / small + 0.5f));
            anchorLocal = (Vector3)precisionPos * small;
            hostStructuralPos = engine.GridPos;
            faceAxis = plan.faceAxis;

            var layer = grid.GetComponent<GridPrecisionAttachmentLayer>();
            if (layer != null && !layer.CanPlace(precisionPos)) return false;

            if (commit && !plan.reusesExisting)
                engine.VariablePorts.AddPort(plan.service, plan.portLocal, plan.outLocal);

            feedback = plan.reusesExisting
                ? $"Connected to {VoxelEngine.Maritime.MaritimeVariablePorts.LabelFor(plan.service)}"
                : $"{VoxelEngine.Maritime.MaritimeVariablePorts.LabelFor(plan.service)} installed";
            return true;
        }

        private System.Collections.Generic.List<Vector3> ProvidePipeGhostLinks() => _pipeGhostLinks;

        private void ConfigurePipeGhostConnection(BlockItem block, GridEntity grid, Vector3 ghostPosition, float cellSize)
        {
            if (_ghost == null || block == null) return;
            var visual = _ghost.GetComponentInChildren<VoxelEngine.Networks.PipeVisualBuilder>(true);
            if (visual == null) return;

            bool itemPipe = block.placedPrefab.GetComponentInChildren<VoxelEngine.Transport.ItemPipe>(true) != null;
            bool gasPipe = block.placedPrefab.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null;
            bool liquidPipe = block.placedPrefab.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null;
            MonoBehaviour best = null;
            float bestDistance = float.MaxValue;

            void Consider(MonoBehaviour candidate)
            {
                if (candidate == null || candidate.transform.IsChildOf(_ghost.transform)) return;
                Vector3 worldDelta = candidate.transform.position - ghostPosition;
                Vector3 alignmentDelta = grid != null
                    ? grid.transform.InverseTransformVector(worldDelta)
                    : worldDelta;
                if (!VoxelEngine.Networks.PipeAdjacency.IsCardinalLinkDelta(
                        alignmentDelta, cellSize, 5f, cellSize * 0.35f)) return;
                float distance = worldDelta.sqrMagnitude;
                if (distance >= bestDistance) return;
                bestDistance = distance;
                best = candidate;
            }

            if (grid != null)
            {
                foreach (var gridBlock in grid.AllBlocks)
                {
                    if (gridBlock == null) continue;
                    if (itemPipe) Consider(gridBlock.GetComponentInChildren<VoxelEngine.Transport.ItemPipe>(true));
                    else if (gasPipe) Consider(gridBlock.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true));
                    else if (liquidPipe) Consider(gridBlock.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true));
                }
            }
            else
            {
                var hits = Physics.OverlapSphere(ghostPosition, cellSize * 5f + cellSize * 0.4f);
                var seen = new System.Collections.Generic.HashSet<EntityId>();
                foreach (var hit in hits)
                {
                    MonoBehaviour candidate = null;
                    if (itemPipe) candidate = hit.GetComponentInParent<VoxelEngine.Transport.ItemPipe>();
                    else if (gasPipe) candidate = hit.GetComponentInParent<VoxelEngine.Gas.GasPipe>();
                    else if (liquidPipe) candidate = hit.GetComponentInParent<VoxelEngine.Fluids.WaterPipe>();
                    if (candidate != null && seen.Add(candidate.GetEntityId())) Consider(candidate);
                }
            }

            EntityId targetId = best != null ? best.GetEntityId() : EntityId.None;
            Vector3 targetPosition = best != null ? best.transform.position : default;
            bool ghostMovedWithTarget = targetId != EntityId.None
                && (ghostPosition - _pipeGhostLastPosition).sqrMagnitude > 0.0001f;
            bool changed = visual != _pipeGhostVisual || targetId != _pipeGhostTargetId
                || ghostMovedWithTarget
                || (_pipeGhostLinks.Count > 0 && (targetPosition - _pipeGhostLinks[0]).sqrMagnitude > 0.0001f);
            _pipeGhostVisual = visual;
            _pipeGhostTargetId = targetId;
            _pipeGhostLastPosition = ghostPosition;
            _pipeGhostLinks.Clear();
            if (best != null) _pipeGhostLinks.Add(targetPosition);
            visual.gridSize = cellSize;
            visual.neighbourPositionsProvider = ProvidePipeGhostLinks;
            if (changed) visual.ForceRebuild();
        }

        private void ShowPrecisionLattice(GridEntity grid, Vector3Int hostStructuralPos, Vector3Int faceAxis)
        {
            if (_precisionLattice == null)
            {
                var preview = new GameObject("PipePrecisionLatticePreview");
                _precisionLattice = preview.AddComponent<GridPrecisionLatticePreview>();
            }
            _precisionLattice.Show(grid, hostStructuralPos, faceAxis);
        }

        private void HidePrecisionLattice()
        {
            if (_precisionLattice != null) _precisionLattice.Hide();
        }

        // ── Ghost port ring ──────────────────────────────────────────
        // Draws a color-coded disc on the engine hull while the player
        // aims at a surface that will create a new variable port. This
        // makes the port visible BEFORE placement, fixing "ports aren't
        // showing 100% of the time".
        private void ShowGhostPortRing(Vector3 worldPos, Vector3 outWorld, Color color)
        {
            if (_ghostPortRing == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _ghostPortRingMat = new Material(sh)
                {
                    color = color
                };
                if (_ghostPortRingMat.HasProperty("_BaseColor")) _ghostPortRingMat.SetColor("_BaseColor", color);
                if (_ghostPortRingMat.HasProperty("_Metallic")) _ghostPortRingMat.SetFloat("_Metallic", 0.35f);
                if (_ghostPortRingMat.HasProperty("_Smoothness")) _ghostPortRingMat.SetFloat("_Smoothness", 0.6f);

                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(go.GetComponent<Collider>());
                go.transform.localScale = new Vector3(0.24f, 0.03f, 0.24f);
                _ghostPortRingRenderer = go.GetComponent<Renderer>();
                _ghostPortRing = go.transform;
            }
            _ghostPortRing.gameObject.SetActive(true);
            _ghostPortRing.position = worldPos + outWorld * 0.02f;
            Vector3 guide = Mathf.Abs(Vector3.Dot(outWorld, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            _ghostPortRing.rotation = Quaternion.LookRotation(outWorld, guide) * Quaternion.Euler(90f, 0f, 0f);
            if (_ghostPortRingMat != null)
            {
                Color em = color * 0.9f;
                _ghostPortRingMat.color = color;
                if (_ghostPortRingMat.HasProperty("_BaseColor")) _ghostPortRingMat.SetColor("_BaseColor", color);
                if (_ghostPortRingMat.HasProperty("_EmissionColor"))
                {
                    _ghostPortRingMat.EnableKeyword("_EMISSION");
                    _ghostPortRingMat.SetColor("_EmissionColor", em);
                }
                _ghostPortRingRenderer.sharedMaterial = _ghostPortRingMat;
            }
        }

        private void HideGhostPortRing()
        {
            if (_ghostPortRing != null) _ghostPortRing.gameObject.SetActive(false);
        }

        public bool TryPlace(BlockItem block, RaycastHit hit, Vector3 viewDir)
        {
            var targetGrid = hit.collider != null ? hit.collider.GetComponentInParent<GridEntity>() : null;
            if (targetGrid != null && IsUnifiedPipe(block))
            {
                // Rebuild the aim ray from the camera so port selection can match the
                // ray-line the player is actually aiming along (buried engine ports).
                Vector3 rayOrigin = shootCamera != null
                    ? shootCamera.transform.position
                    : hit.point - viewDir.normalized * 0.5f;
                var aimRay = new Ray(rayOrigin, viewDir.normalized);
                if (TryPlaceUnifiedPipe(block, targetGrid, hit, aimRay)) return true;
                // Hit-based math failed (hit in a gap between the machine's colliders,
                // occupied Detail cell, ...) — try the chain continuation on the same
                // aim so a pipe run STILL grows when clicking its open end.
                return TryPlaceUnifiedPipeChain(block, aimRay);
            }

            ComputePlacementPose(hit, block, out Vector3 pos, out Quaternion rot);
            if (!IsPlacementValid(pos, block)) return false;

            var go = Instantiate(block.placedPrefab, pos, rot);
            go.name = block.displayName;

            var placedBelt = go.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true);
            if (placedBelt != null)
                placedBelt.SetBuildShape(ResolveConveyorBuildShape(placedBelt, hit));

            // Make sure it has a collider for future raycasts.
            if (go.GetComponentInChildren<Collider>() == null)
                go.AddComponent<BoxCollider>();

            var pb = go.GetComponent<PlacedBlock>();
            if (pb == null) pb = go.AddComponent<PlacedBlock>();
            pb.Item   = block;
            pb.Hp     = block.blockHealth;
            pb.onGrid = gridSnap;

            var payloadReceiver = go.GetComponentInChildren<IPlacedBlockPayloadReceiver>();
            if (payloadReceiver != null && inventory != null)
                payloadReceiver.ApplyPlacedPayload(inventory.ActiveStack);

            // Power pipes/nodes register during Instantiate, before all placement
            // payload has settled. Force one topology refresh after the final pose.
            if (go.GetComponentInChildren<VoxelEngine.Power.PowerNode>(true) != null)
                VoxelEngine.Power.PowerNetworkManager.Instance?.SetDirty();

            // Apply optional texture/material override at runtime.
            if (block.placedMaterial != null || block.texture != null)
            {
                var tex = go.AddComponent<BlockTexturizer>();
                tex.overrideMaterial = block.placedMaterial;
                tex.overrideTexture  = block.texture;
            }

            // Placement is now final and the collider is registered at its snapped
            // pose, so refresh this belt and its neighbours immediately instead of
            // waiting for the periodic connection scan.
            placedBelt?.RefreshTopologyImmediate();
            return true;
        }

        /// <summary>
        /// Attaches existing pipe items to the unified Detail lattice. The pipe keeps
        /// its normal static-world behavior everywhere else, while this path gives it
        /// a real GridBlock address so physics, networks, and persistence move together.
        /// </summary>
        private bool TryPlaceUnifiedPipe(BlockItem item, GridEntity grid, RaycastHit hit, Ray aimRay)
        {
            if (item == null || item.placedPrefab == null || grid == null) return false;
            Vector3Int precisionPos, hostStructuralPos;
            bool snappedToPort = TryGetMaritimePortSnap(grid, item, hit, aimRay, commit: true,
                out precisionPos, out hostStructuralPos, out _, out Vector3 anchorLocal, out string portFeedback);
            // The engine already has its fill of this service — refuse the placement
            // outright (with a message) instead of dropping a free pipe that would
            // sneak past the per-engine cap.
            if (!snappedToPort && s_portCapBlocked)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    "Port Full", s_portCapReason ?? "Engine service port already connected",
                    item.icon, item.iconTint);
                return false;
            }
            if (!snappedToPort
                && !UnifiedGridTopology.TryGetDetailPlacement(grid, hit,
                    out precisionPos, out hostStructuralPos, out _)) return false;

            var block = PlaceOnDetailLattice(item, grid, precisionPos, hostStructuralPos,
                snappedToPort ? anchorLocal : (Vector3?)null);
            if (block == null) return false;

            // A freshly installed variable service port announces itself; a rejected
            // over-cap attempt never reaches here (placement fails earlier).
            string detail = !string.IsNullOrEmpty(portFeedback)
                ? portFeedback
                : $"{item.displayName} · Detail lattice";
            VoxelEngine.UI.BuildFeedbackHud.Show("Pipe Attached", detail, item.icon, item.iconTint);
            return true;
        }

        /// <summary>
        /// Public entry for the RAY-MISS click path (PlayerInteractionTool): extending a
        /// pipe run by aiming at its open end must work even when the camera ray slips
        /// between the thin arm/cap visuals and hits nothing at all. The ghost preview
        /// computes the same cell via <see cref="TryChainAim"/>, so ghost ≡ placed.
        /// </summary>
        public bool TryPlaceUnifiedPipeChain(BlockItem item, Ray aimRay)
        {
            if (!TryChainAim(item, aimRay,
                    out GridEntity grid, out Vector3Int precisionPos,
                    out Vector3Int hostStructuralPos, out _, out bool canPlace))
                return false;
            if (!canPlace) return false;
            if (!canPlace) return false;

            var block = PlaceOnDetailLattice(item, grid, precisionPos, hostStructuralPos, null);
            if (block == null) return false;

            VoxelEngine.UI.BuildFeedbackHud.Show(
                "Pipe Attached", $"{item.displayName} · Detail lattice", item.icon, item.iconTint);
            return true;
        }

        /// <summary>
        /// The shared tail of every unified-pipe placement: claim the Detail cell, spawn
        /// + register the pipe, optionally re-seat it on a port anchor, refresh visuals
        /// and all pipe networks immediately. Returns the placed block, or null when the
        /// cell was occupied / the layer rejected the add.
        /// </summary>
        private GridBlock PlaceOnDetailLattice(BlockItem item, GridEntity grid,
            Vector3Int precisionPos, Vector3Int hostStructuralPos, Vector3? portAnchorLocal)
        {
            var layer = grid.GetComponent<GridPrecisionAttachmentLayer>()
                ?? grid.gameObject.AddComponent<GridPrecisionAttachmentLayer>();
            if (!layer.CanPlace(precisionPos)) return null;

            var go = Instantiate(item.placedPrefab);
            var block = go.GetComponent<GridBlock>() ?? go.AddComponent<GridBlock>();
            block.SourceItem = item;
            block.blockName = item.displayName;
            block.maxHP = item.blockHealth;
            block.currentHP = item.blockHealth;

            var placed = go.GetComponent<PlacedBlock>() ?? go.AddComponent<PlacedBlock>();
            placed.Item = item;
            placed.Hp = item.blockHealth;
            placed.onGrid = true;

            Quaternion worldRotation = grid.transform.rotation
                * Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
            Quaternion localRotation = Quaternion.Inverse(grid.transform.rotation) * worldRotation;
            if (!layer.AddBlock(precisionPos, hostStructuralPos, block, localRotation))
            {
                Destroy(go);
                return null;
            }

            // Port-snapped pipes sit exactly on the port object — centred like the
            // ghost showed — not merely rounded onto the Detail cell.
            if (portAnchorLocal.HasValue)
                block.transform.localPosition = portAnchorLocal.Value;

            // Grid-mounted pipes link on the Detail lattice step (carried over from
            // the retired static placement fork) and refresh visuals + all pipe
            // networks immediately instead of waiting for their polling cadence.
            var pipeVisual = go.GetComponentInChildren<VoxelEngine.Networks.PipeVisualBuilder>(true);
            if (pipeVisual != null)
            {
                pipeVisual.gridSize = GridSize.Small.CellSize();
                pipeVisual.ForceRebuild();
            }
            VoxelEngine.Transport.ItemPipeNetwork.Instance?.SetDirty();
            VoxelEngine.Gas.GasNetwork.Instance?.SetDirty();
            VoxelEngine.Fluids.FluidNetworkManager.Instance?.SetDirty();
            VoxelEngine.GridSystem.GridLiquidNetwork.Instance?.SetDirty();
            VoxelEngine.GridSystem.GridGasNetwork.Instance?.SetDirty();
            VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
            return block;
        }

        // ════════════════════════════════════════════════════════════════
        //  PIPE CHAIN AIM (ray-miss continuation)
        //  Pipe ARMS and end-caps are collider-free visuals; the only ray-hit
        //  target on a pipe is the small hub box. Aiming at the open end of a
        //  run in open space therefore finds NOTHING — no ghost, no placement.
        //  ChainAim forgives the aim: a fat sphere-cast along the view ray
        //  grips the nearest held-type pipe hub and computes the Detail cell
        //  one step from it — along the run axis when aiming past the tip
        //  (continuation), or sideways when aiming at its side (branch).
        // ════════════════════════════════════════════════════════════════
        private static readonly RaycastHit[] s_chainBuffer = new RaycastHit[16];

        /// <summary>
        /// Find the chain-continuation cell from the pipe hub closest to the aim ray.
        /// Shared by the ghost preview and <see cref="TryPlaceUnifiedPipeChain"/> so the
        /// previewed cell is exactly the placed one. <paramref name="canPlace"/> reports
        /// whether the computed cell is free of other pipes and structural blocks — the
        /// ghost tints red on false instead of hiding.
        /// </summary>
        private bool TryChainAim(BlockItem item, Ray aimRay,
            out GridEntity grid, out Vector3Int precisionPos, out Vector3Int hostStructuralPos,
            out Vector3Int faceAxis, out bool canPlace)
        {
            grid = null;
            precisionPos = hostStructuralPos = faceAxis = default;
            canPlace = false;
            if (item == null || item.placedPrefab == null) return false;

            bool itemPipe = item.placedPrefab.GetComponentInChildren<VoxelEngine.Transport.ItemPipe>(true) != null;
            bool gasPipe = !itemPipe && item.placedPrefab.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null;
            bool liquidPipe = !itemPipe && !gasPipe
                && item.placedPrefab.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null;
            if (!itemPipe && !gasPipe && !liquidPipe) return false;

            int hitCount = Physics.SphereCastNonAlloc(aimRay, 0.45f, s_chainBuffer,
                reach, ~0, QueryTriggerInteraction.Ignore);
            Transform selfRoot = transform.root;
            GridBlock best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                var h = s_chainBuffer[i];
                if (h.collider == null) continue;
                if (selfRoot != null && h.collider.transform.IsChildOf(selfRoot)) continue;
                var gb = h.collider.GetComponentInParent<GridBlock>();
                if (gb == null || !gb.IsPrecisionAttachment || gb.Grid == null) continue;
                // Chain only onto the SAME pipe family — item/gas/liquid never mix.
                bool sameFamily =
                    (itemPipe && gb.GetComponentInChildren<VoxelEngine.Transport.ItemPipe>(true) != null)
                    || (gasPipe && gb.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null)
                    || (liquidPipe && gb.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null);
                if (!sameFamily) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = gb; }
            }
            if (best == null) return false;

            grid = best.Grid;
            Vector3 hubWorld = best.transform.position;

            // Approach vector: from the hub toward where the aim ray passes it.
            // Prominent sideways approach ⇒ branch cell; nearly head-on (aiming past
            // the pipe into space) ⇒ walk the run FORWARD along the view ray's
            // dominant grid axis — the classic "aim at the tip to continue" motion.
            float t = Mathf.Max(0f, Vector3.Dot(hubWorld - aimRay.origin, aimRay.direction));
            Vector3 approach = aimRay.GetPoint(t);
            Vector3 perp = approach - hubWorld;
            Vector3 axisWorld = perp.magnitude < 0.15f ? aimRay.direction : perp;
            faceAxis = UnifiedGridTopology.SnapFaceAxis(grid, axisWorld);
            if (faceAxis == Vector3Int.zero) faceAxis = Vector3Int.up;

            precisionPos = best.PrecisionGridPos + faceAxis;
            hostStructuralPos = best.PrecisionHostGridPos;

            var layer = grid.PrecisionAttachments;
            bool free = layer == null || layer.CanPlace(precisionPos);
            if (free)
            {
                // Chained cells must not dive into a structural block (mirrors the
                // guard in UnifiedGridTopology.TryGetDetailPlacement).
                Vector3 localPosition = (Vector3)precisionPos * GridSize.Small.CellSize();
                float large = GridSize.Large.CellSize();
                Vector3Int structuralCell = new(
                    Mathf.RoundToInt(localPosition.x / large),
                    Mathf.RoundToInt(localPosition.y / large),
                    Mathf.RoundToInt(localPosition.z / large));
                free = grid.GetBlock(structuralCell) == null;
            }
            canPlace = free;
            return true;
        }

        // ---------- Placement math ----------

        /// <summary>
        /// Full placement pose (position + rotation). Wind turbine parts snap to
        /// their exact socket pose — including rotation, so blades arrive in the
        /// correct 120° slot orientation and can never be placed wrong. Everything
        /// else falls back to the classic position + player-controlled yaw.
        /// </summary>
        private void ComputePlacementPose(RaycastHit hit, BlockItem block, out Vector3 pos, out Quaternion rot)
        {
            if (TryGetFactorySnapPose(hit, block, out pos, out rot))
                return;

            if (block != null && block.placedPrefab != null &&
                VoxelEngine.Power.Wind.WindTurbineController.TryGetSnapPoint(block.placedPrefab, hit, out pos, out rot))
                return;

            pos = ComputePlacementPosition(hit, block);
            rot = GravityProvider.GetSurfaceRotation(pos) * Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
        }

        private bool TryGetFactorySnapPose(RaycastHit hit, BlockItem block, out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = default;
            if (block == null || block.placedPrefab == null || hit.collider == null) return false;

            var placingBeltComponent = block.placedPrefab.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true);
            bool placingBelt = placingBeltComponent != null;
            bool placingChute = block.placedPrefab.GetComponentInChildren<VoxelEngine.Simulation.ConveyorChute>(true) != null;
            bool placingFunnel = block.placedPrefab.GetComponentInChildren<VoxelEngine.Simulation.Funnel>(true) != null;
            bool placingPowerPipe = block.placedPrefab.GetComponentInChildren<VoxelEngine.Power.PowerCable>(true) != null;
            bool placingCompactPower = block.placedPrefab.GetComponentInChildren<VoxelEngine.Simulation.CompactVoltageStation>(true) != null;
            if (!placingBelt && !placingChute && !placingFunnel && !placingPowerPipe && !placingCompactPower) return false;
            var selectedConveyorShape = placingBelt
                ? ResolveConveyorBuildShape(placingBeltComponent, hit)
                : VoxelEngine.Simulation.ConveyorShape.Straight;

            float factorySpacing = Mathf.Max(gridSize, 1f);
            var targetBelt = hit.collider.GetComponentInParent<VoxelEngine.Simulation.ConveyorBelt>();
            if (placingBelt && targetBelt != null)
            {
                var placingShape = selectedConveyorShape;
                if (!targetBelt.autoShape)
                {
                    rot = targetBelt.transform.rotation;
                    Vector3 localEntry = ConveyorLocalEntryOffset(placingShape);
                    pos = targetBelt.GetExitSocketPosition() - rot * localEntry;
                    return true;
                }
                if (placingShape == VoxelEngine.Simulation.ConveyorShape.RampUp)
                {
                    pos = targetBelt.transform.position + targetBelt.transform.forward * factorySpacing;
                    rot = targetBelt.transform.rotation;
                    return true;
                }
                if (placingShape == VoxelEngine.Simulation.ConveyorShape.RampDown)
                {
                    pos = targetBelt.transform.position
                        + targetBelt.transform.forward * factorySpacing
                        - targetBelt.transform.up * factorySpacing;
                    rot = targetBelt.transform.rotation;
                    return true;
                }
                if (placingShape == VoxelEngine.Simulation.ConveyorShape.VerticalUp
                    || placingShape == VoxelEngine.Simulation.ConveyorShape.VerticalDown)
                {
                    rot = targetBelt.transform.rotation;
                    Vector3 localEntry = ConveyorLocalEntryOffset(placingShape);
                    pos = targetBelt.GetExitSocketPosition() - rot * localEntry;
                    return true;
                }

                Vector3 localHit = targetBelt.transform.InverseTransformPoint(hit.point);
                Vector3 snapDirection;
                bool sideSnap = Mathf.Abs(localHit.x) > Mathf.Abs(localHit.z);
                if (sideSnap)
                    snapDirection = targetBelt.transform.right * Mathf.Sign(Mathf.Approximately(localHit.x, 0f) ? 1f : localHit.x);
                else
                    snapDirection = targetBelt.transform.forward * Mathf.Sign(Mathf.Approximately(localHit.z, 0f) ? 1f : localHit.z);

                // Keep belts level. Rotation remains player-controlled so side
                // placement can create a parallel lane, feed into the target belt,
                // or make a clean turn with the BuildRotate key.
                pos = targetBelt.transform.position + snapDirection.normalized * Mathf.Max(gridSize, 1f);
                rot = targetBelt.transform.rotation * Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
                return true;
            }

            if (placingChute && targetBelt != null)
            {
                Vector3 beltUp = targetBelt.transform.up;
                float normalHeight = Vector3.Dot(hit.normal.normalized, beltUp);
                float localHeight = Vector3.Dot(hit.point - targetBelt.transform.position, beltUp);
                bool snapAbove = normalHeight > 0.45f
                    || (Mathf.Abs(normalHeight) <= 0.45f && localHeight > 0.30f);
                float verticalSign = snapAbove ? 1f : -1f;
                pos = targetBelt.transform.position + beltUp * verticalSign * factorySpacing;
                rot = targetBelt.transform.rotation;
                return true;
            }

            if (placingFunnel && targetBelt != null)
            {
                pos = targetBelt.transform.position - targetBelt.transform.forward * factorySpacing;
                rot = targetBelt.transform.rotation;
                return true;
            }

            if (placingBelt && (selectedConveyorShape == VoxelEngine.Simulation.ConveyorShape.VerticalUp
                                || selectedConveyorShape == VoxelEngine.Simulation.ConveyorShape.VerticalDown))
            {
                var targetPorts = hit.collider.GetComponentInParent<PortConfig>();
                if (TryGetVerticalItemPortSnap(targetPorts, hit, factorySpacing, out pos, out rot))
                    return true;
            }

            if (placingChute)
            {
                var targetPorts = hit.collider.GetComponentInParent<PortConfig>();
                if (TryGetVerticalItemPortSnap(targetPorts, hit, factorySpacing, out pos, out rot))
                    return true;
            }

            var targetChute = hit.collider.GetComponentInParent<VoxelEngine.Simulation.ConveyorChute>();
            if (placingBelt && targetChute != null)
            {
                Vector3 localHit = targetChute.transform.InverseTransformPoint(hit.point);
                float verticalSign = localHit.y < 0f ? -1f : 1f;
                pos = targetChute.transform.position + targetChute.transform.up * verticalSign * factorySpacing;
                rot = targetChute.transform.rotation;
                return true;
            }

            if (placingChute && targetChute != null)
            {
                Vector3 localHit = targetChute.transform.InverseTransformPoint(hit.point);
                float verticalSign = localHit.y < 0f ? -1f : 1f;
                pos = targetChute.transform.position + targetChute.transform.up * verticalSign * factorySpacing;
                rot = targetChute.transform.rotation;
                return true;
            }

            var targetFunnel = hit.collider.GetComponentInParent<VoxelEngine.Simulation.Funnel>();
            if (placingFunnel && TryGetInventoryLikeAnchor(hit, out var inventoryAnchor))
            {
                Vector3 outward = SnapOutwardNormal(inventoryAnchor.transform, hit.normal);
                pos = inventoryAnchor.transform.position + outward * factorySpacing;
                Vector3 up = GravityProvider.GetUp(pos);
                Vector3 forward = Vector3.ProjectOnPlane(outward, up);
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(inventoryAnchor.transform.forward, up);
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
                rot = Quaternion.LookRotation(forward.normalized, up);
                return true;
            }

            if (!placingFunnel && targetFunnel != null && IsInventoryLikePlacement(block))
            {
                Vector3 inventoryDir = targetFunnel.transform.TransformDirection(targetFunnel.inventoryDirection.normalized);
                pos = targetFunnel.transform.position + inventoryDir * factorySpacing;
                rot = targetFunnel.transform.rotation;
                return true;
            }

            var targetPowerNode = hit.collider.GetComponentInParent<VoxelEngine.Power.PowerNode>();
            if (placingCompactPower && targetPowerNode != null)
            {
                Vector3 normal = hit.normal.sqrMagnitude > 0.001f ? hit.normal.normalized : (hit.point - targetPowerNode.transform.position).normalized;
                if (normal.sqrMagnitude < 0.001f) normal = targetPowerNode.transform.forward;
                float spacing = Mathf.Max(gridSize, 1f);
                pos = targetPowerNode.transform.position + normal * spacing;
                Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(pos);
                Vector3 forward = Vector3.ProjectOnPlane(-normal, up);
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(targetPowerNode.transform.forward, up);
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
                rot = Quaternion.LookRotation(forward.normalized, up) * Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
                return true;
            }

            var targetPipe = hit.collider.GetComponentInParent<VoxelEngine.Power.PowerCable>();
            if (placingPowerPipe && targetPipe != null)
            {
                Vector3 localHit = targetPipe.transform.InverseTransformPoint(hit.point);
                Vector3 localDirection = NearestLocalCardinal(localHit);
                if (localDirection.sqrMagnitude < 0.01f) localDirection = Vector3.forward;

                pos = targetPipe.transform.position + targetPipe.transform.TransformDirection(localDirection) * Mathf.Max(gridSize, 1f);
                rot = targetPipe.transform.rotation;
                return true;
            }

            return false;
        }

        private static VoxelEngine.Simulation.ConveyorShape ResolveConveyorBuildShape(
            VoxelEngine.Simulation.ConveyorBelt belt,
            RaycastHit hit)
        {
            if (belt == null) return VoxelEngine.Simulation.ConveyorShape.Straight;
            var mode = VoxelEngine.Simulation.ConveyorShapeWheel.GetMode(belt.speed);
            if (mode == VoxelEngine.Simulation.ConveyorBuildMode.Straight)
                return VoxelEngine.Simulation.ConveyorShape.Straight;

            Transform reference = null;
            var targetBelt = hit.collider != null ? hit.collider.GetComponentInParent<VoxelEngine.Simulation.ConveyorBelt>() : null;
            if (targetBelt != null) reference = targetBelt.transform;
            if (reference == null)
            {
                var targetChute = hit.collider != null ? hit.collider.GetComponentInParent<VoxelEngine.Simulation.ConveyorChute>() : null;
                if (targetChute != null) reference = targetChute.transform;
            }
            if (reference == null)
            {
                var ports = hit.collider != null ? hit.collider.GetComponentInParent<PortConfig>() : null;
                if (ports != null) reference = ports.transform;
            }

            Vector3 up = reference != null ? reference.up : GravityProvider.GetUp(hit.point);
            Vector3 origin = reference != null ? reference.position : hit.point;
            float normalHeight = Vector3.Dot(hit.normal.normalized, up);
            float localHeight = Vector3.Dot(hit.point - origin, up);
            bool upward = normalHeight > 0.45f
                || (Mathf.Abs(normalHeight) <= 0.45f && localHeight > 0.30f);

            if (mode == VoxelEngine.Simulation.ConveyorBuildMode.Ramp)
                return upward ? VoxelEngine.Simulation.ConveyorShape.RampUp : VoxelEngine.Simulation.ConveyorShape.RampDown;
            return upward ? VoxelEngine.Simulation.ConveyorShape.VerticalUp : VoxelEngine.Simulation.ConveyorShape.VerticalDown;
        }

        private static Vector3 ConveyorLocalEntryOffset(VoxelEngine.Simulation.ConveyorShape shape)
        {
            return shape switch
            {
                VoxelEngine.Simulation.ConveyorShape.RampUp => new Vector3(0f, 0f, -0.5f),
                VoxelEngine.Simulation.ConveyorShape.RampDown => new Vector3(0f, 1f, -0.5f),
                VoxelEngine.Simulation.ConveyorShape.VerticalUp => Vector3.zero,
                VoxelEngine.Simulation.ConveyorShape.VerticalDown => Vector3.up,
                _ => Vector3.back * 0.5f
            };
        }

        private static bool TryGetVerticalItemPortSnap(
            PortConfig ports,
            RaycastHit hit,
            float spacing,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = default;
            if (ports == null) return false;

            bool found = false;
            CubeFace selectedFace = CubeFace.PosY;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < 2; i++)
            {
                CubeFace face = i == 0 ? CubeFace.PosY : CubeFace.NegY;
                if (!ports.IsFaceEnabled(face) || ports.GetDirection(face) == PortDirection.None) continue;
                float distance = (ports.FaceWorldPoint(face) - hit.point).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                selectedFace = face;
                found = true;
            }

            if (!found) return false;
            Vector3 normal = ports.FaceNormal(selectedFace).normalized;
            position = ports.transform.position + normal * spacing;
            rotation = ports.transform.rotation;
            return true;
        }

        private static Vector3 NearestLocalCardinal(Vector3 local)
        {
            float ax = Mathf.Abs(local.x), ay = Mathf.Abs(local.y), az = Mathf.Abs(local.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(Mathf.Approximately(local.x, 0f) ? 1f : local.x), 0f, 0f);
            if (ay >= ax && ay >= az) return new Vector3(0f, Mathf.Sign(Mathf.Approximately(local.y, 0f) ? 1f : local.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(Mathf.Approximately(local.z, 0f) ? 1f : local.z));
        }

        private static bool IsInventoryLikePlacement(BlockItem block)
        {
            if (block == null || block.placedPrefab == null) return false;
            var behaviours = block.placedPrefab.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null) continue;
                if (behaviour is VoxelEngine.Transport.IInventoryInterface
                    || behaviour is VoxelEngine.Transport.IItemPortHost
                    || behaviour is VoxelEngine.Building.Chest)
                    return true;
            }
            return false;
        }

        private static bool TryGetInventoryLikeAnchor(RaycastHit hit, out MonoBehaviour anchor)
        {
            anchor = null;
            if (hit.collider == null) return false;
            var behaviours = hit.collider.GetComponentsInParent<MonoBehaviour>(true);
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null) continue;
                if (behaviour is VoxelEngine.Transport.IInventoryInterface
                    || behaviour is VoxelEngine.Transport.IItemPortHost
                    || behaviour is VoxelEngine.Building.Chest)
                {
                    anchor = behaviour;
                    return true;
                }
            }
            return false;
        }

        private static Vector3 SnapOutwardNormal(Transform target, Vector3 hitNormal)
        {
            if (target == null) return hitNormal.sqrMagnitude > 0.001f ? hitNormal.normalized : Vector3.forward;
            Vector3 local = target.InverseTransformDirection(hitNormal.sqrMagnitude > 0.001f ? hitNormal.normalized : target.forward);
            Vector3 cardinal = NearestLocalCardinal(local);
            return target.TransformDirection(cardinal).normalized;
        }

        private Vector3 ComputePlacementPosition(RaycastHit hit, BlockItem block)
        {

            // ── BUSBAR SNAP ─────────────────────────────────────────────────
            // If the player is placing a Power Busbar AND looking at the END
            // face of an existing busbar, snap the ghost to that busbar's tip
            // so consecutive bars line up perfectly into one long run. This
            // makes "extend the bus" feel like a single intuitive gesture.
            // For any other case we fall through to the normal grid-snap below.
            var existingBus = hit.collider != null
                ? hit.collider.GetComponentInParent<VoxelEngine.Power.PowerBusbar>()
                : null;
            if (existingBus != null && block != null && block.placedPrefab != null &&
                block.placedPrefab.GetComponent<VoxelEngine.Power.PowerBusbar>() != null)
            {
                var snapPos = TrySnapToBusbarEnd(existingBus, hit);
                if (snapPos.HasValue) return snapPos.Value;
            }

            // Free placement = hit point pushed along surface normal by half block size.
            Vector3 free = hit.point + hit.normal * (gridSize * 0.5f);
            if (!gridSnap) return free;

            // Snap to grid. Push from the hit surface along the normal.
            float gs = gridSize;
            Vector3 raw = hit.point + hit.normal * (gs * 0.5f);

            // On spherical planets, world-axis rounding creates visible height drift between
            // neighboring placed machines/containers/pipes because each placement rounds X/Y/Z
            // independently instead of following the local tangent plane. If a nearby static
            // placed block already exists, use it as a local placement anchor so follow-up
            // placements stay on the same tangent-frame lattice.
            if (TryGetNearbyPlacedBlockSnap(hit, raw, gs, out var anchoredSnap))
                return anchoredSnap;

            Vector3 snapped = new Vector3(
                Mathf.Round(raw.x / gs) * gs,
                Mathf.Round(raw.y / gs) * gs,
                Mathf.Round(raw.z / gs) * gs
            );

            // If the snapped position is inside the hit object, push one more grid step.
            float distToHit = Vector3.Dot(snapped - hit.point, hit.normal);
            if (distToHit < gs * 0.1f)
                snapped += hit.normal * gs;

            return snapped;
        }

        /// <summary>
        /// Smart placement target for a new busbar near an existing one.
        ///
        ///   • Click the TIP face → snap to tip-to-tip continuation
        ///     (centres exactly <c>busLength</c> apart so the bars form
        ///     one perfectly continuous run).
        ///   • Click any SIDE face → snap to a parallel bar one grid
        ///     cell out along the clicked face's normal (so two busbars
        ///     side-by-side without overlapping).
        ///
        /// Either way the result is guaranteed to clear the existing
        /// bar's stretched collider, preventing the "busbar inside
        /// busbar" overlap the user reported.
        /// </summary>
        private Vector3? TrySnapToBusbarEnd(VoxelEngine.Power.PowerBusbar existing,
                                            RaycastHit hit)
        {
            if (existing == null) return null;

            // Convert the bus axis to world space (busbars are placed with
            // identity rotation in the current build flow, so transform.right /
            // transform.up / transform.forward all align with world axes).
            Vector3 axisWorld = existing.busAxis switch
            {
                VoxelEngine.Power.PowerBusbar.BusAxis.X => existing.transform.right,
                VoxelEngine.Power.PowerBusbar.BusAxis.Y => existing.transform.up,
                _                                       => existing.transform.forward,
            };

            Vector3 hitNormal = hit.normal.normalized;
            float alignment   = Vector3.Dot(hitNormal, axisWorld);

            // TIP face → tip-to-tip snap.
            if (Mathf.Abs(alignment) >= 0.6f)
            {
                float sign = Mathf.Sign(alignment);
                return existing.transform.position + axisWorld * sign * existing.busLength;
            }

            // SIDE face → parallel snap, one grid cell out along the face normal.
            //
            // We round the face normal to the nearest cardinal axis (in case the
            // physics raycast returned a slightly off normal) and offset by exactly
            // one grid cell so the new bar runs parallel to the existing one with
            // a clean 1 m gap between their centres.
            Vector3 perpAxis = NearestCardinal(hitNormal);
            if (perpAxis.sqrMagnitude < 0.01f) return null;
            return existing.transform.position + perpAxis * Mathf.Max(gridSize, 1f);
        }

        private static Vector3 NearestCardinal(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0, 0);
            if (ay >= ax && ay >= az) return new Vector3(0, Mathf.Sign(v.y), 0);
            return new Vector3(0, 0, Mathf.Sign(v.z));
        }

        private bool TryGetNearbyPlacedBlockSnap(RaycastHit hit, Vector3 raw, float spacing, out Vector3 snapped)
        {
            snapped = default;
            if (!GravityProvider.IsRadial || GravityProvider.ActiveBody == null) return false;

            var anchor = FindStaticPlacementAnchor(hit, spacing);
            if (anchor == null) return false;

            Vector3 up = anchor.up;
            if (up.sqrMagnitude < 0.0001f) up = GravityProvider.GetUp(anchor.position);
            up = up.normalized;

            Vector3 forward = Vector3.ProjectOnPlane(anchor.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(GravityProvider.GetSurfaceRotation(anchor.position) * Vector3.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(Vector3.right, up);
            forward = forward.normalized;
            Vector3 right = Vector3.Cross(forward, up).normalized;
            forward = Vector3.Cross(up, right).normalized;

            Vector3 delta = raw - anchor.position;
            float localX = Mathf.Round(Vector3.Dot(delta, right) / spacing) * spacing;
            float localY = Mathf.Round(Vector3.Dot(delta, up) / spacing) * spacing;
            float localZ = Mathf.Round(Vector3.Dot(delta, forward) / spacing) * spacing;
            snapped = anchor.position + right * localX + up * localY + forward * localZ;
            return true;
        }

        private Transform FindStaticPlacementAnchor(RaycastHit hit, float spacing)
        {
            Transform best = null;
            float bestDistance = float.MaxValue;

            void Consider(PlacedBlock placed)
            {
                if (placed == null) return;
                if (placed.GetComponentInParent<GridEntity>() != null) return;
                float distance = (placed.transform.position - hit.point).sqrMagnitude;
                if (distance >= bestDistance) return;
                bestDistance = distance;
                best = placed.transform;
            }

            Consider(hit.collider != null ? hit.collider.GetComponentInParent<PlacedBlock>() : null);
            if (best != null) return best;

            float searchRadius = Mathf.Max(spacing * 1.75f, 2f);
            var overlaps = Physics.OverlapSphere(hit.point, searchRadius, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlaps.Length; i++)
                Consider(overlaps[i] != null ? overlaps[i].GetComponentInParent<PlacedBlock>() : null);
            return best;
        }

        private bool TryRaycastIgnoringSelf(Ray ray, out RaycastHit hit, float maxDistance)
        {
            var hits = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Transform selfRoot = transform.root;
            for (int i = 0; i < hits.Length; i++)
            {
                var candidate = hits[i];
                if (candidate.collider == null) continue;
                if (selfRoot != null && candidate.collider.transform.IsChildOf(selfRoot)) continue;
                if (VoxelEngine.Player.PlayerRaycastFilter.IsOwnPlayerCollider(candidate.collider, transform)) continue;
                hit = candidate;
                return true;
            }
            hit = default;
            return false;
        }

        private bool IsPlacementValid(Vector3 pos, BlockItem block)
        {
            // Never place inside the player — a proper capsule column test, not a flat
            // distance check: blocks spawning between the player's feet used to launch
            // them upwards. Player pivot ≈ feet, capsule ≈ 1.9 m tall.
            Vector3 feet = transform.position;
            bool insideColumn = new Vector2(feet.x - pos.x, feet.z - pos.z).magnitude < 0.65f;
            bool withinHeight = pos.y > feet.y - 0.4f && pos.y < feet.y + 2.05f;
            if (insideColumn && withinHeight) return false;

            // For cables/pipes use a very tight box (they chain end-to-end).
            // For solid blocks use a half-block check.
            bool isThin = block.allowStacking;
            float checkSize = isThin ? 0.18f : 0.42f;

            var overlaps = Physics.OverlapBox(pos, Vector3.one * checkSize, Quaternion.identity);
            foreach (var col in overlaps)
            {
                if (col.isTrigger) continue; // pickup spheres, etc.
                // Placed blocks: stacking blocks can share space with other placed blocks.
                if (col.GetComponentInParent<PlacedBlock>() != null)
                {
                    if (!block.allowStacking) return false;
                    continue;
                }
                // Block placement on dynamic rigidbodies.
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) return false;
                // Static world geometry (terrain, rocks, trees): never bury a block
                // into it — half-buried placements kicked the whole construct.
                if (col.attachedRigidbody == null) return false;
            }
            return true;
        }

        // ---------- Ghost material helpers ----------
        private static Material MakeGhostMaterial(Color color)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Surface"))   m.SetFloat("_Surface", 1f); // transparent
            if (m.HasProperty("_Blend"))     m.SetFloat("_Blend",   0f); // alpha blend
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = 3000;
            return m;
        }
        private static void StripGhost(GameObject root, Material mat)
        {
            foreach (var col in root.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            foreach (var rb  in root.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;

            // Placement previews must never participate in simulation. Instantiating
            // the real prefab briefly runs OnEnable before colliders are stripped;
            // disabling logistical/powered behaviours here prevents existing belts
            // from caching the ghost as a valid consumer and deleting items into it.
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                if (mb is VoxelEngine.Simulation.IItemConsumer ||
                    mb is VoxelEngine.Simulation.IItemProvider ||
                    mb is VoxelEngine.Simulation.IMachine ||
                    mb is VoxelEngine.Transport.ItemPipe ||
                    mb is VoxelEngine.Gas.GasPipe ||
                    mb is VoxelEngine.Fluids.FluidNode ||
                    mb is VoxelEngine.Networks.PipeVisualBuilder ||
                    mb.GetType().Namespace == "VoxelEngine.Simulation")
                {
                    mb.enabled = false;
                }
            }

            ApplyGhostMaterial(root, mat);
        }
        private static void ApplyGhostMaterial(GameObject root, Material mat)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var arr = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                r.sharedMaterials = arr;
            }
        }
    }
}
