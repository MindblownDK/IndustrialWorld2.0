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

        [Header("Refs")]
        public Camera shootCamera;
        public Inventory inventory;

        [Header("Tuning")]
        public float reach = 8f;
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
                _ghost = Instantiate(block.placedPrefab);
                _ghost.name = "BuildGhost";
                _ghostItem = block;
                StripGhost(_ghost, _ghostMaterialValid);
            }

            var ray = shootCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (!Physics.Raycast(ray, out var hit, reach))
            {
                _ghost.SetActive(false);
                HidePrecisionLattice();
                return;
            }
            _ghost.SetActive(true);

            var targetGrid = hit.collider != null ? hit.collider.GetComponentInParent<GridEntity>() : null;
            if (targetGrid != null && IsUnifiedPipe(block))
            {
                bool validPrecision = UnifiedGridTopology.TryGetDetailPlacement(
                    targetGrid, hit, out var precisionPos, out var hostStructuralPos, out var faceAxis);
                ShowPrecisionLattice(targetGrid, hostStructuralPos, faceAxis);

                var pipeVisual = _ghost.GetComponentInChildren<VoxelEngine.Networks.PipeVisualBuilder>(true);
                if (pipeVisual != null && !Mathf.Approximately(pipeVisual.gridSize, GridSize.Small.CellSize()))
                {
                    pipeVisual.gridSize = GridSize.Small.CellSize();
                    pipeVisual.ForceRebuild();
                    ApplyGhostMaterial(_ghost, validPrecision ? _ghostMaterialValid : _ghostMaterialInvalid);
                }

                Vector3 localPosition = (Vector3)precisionPos * GridSize.Small.CellSize();
                Vector3 worldPosition = targetGrid.transform.TransformPoint(localPosition);
                Quaternion worldRotation = targetGrid.transform.rotation
                    * Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
                _ghost.transform.SetPositionAndRotation(worldPosition, worldRotation);
                ApplyGhostMaterial(_ghost, validPrecision ? _ghostMaterialValid : _ghostMaterialInvalid);
                return;
            }
            HidePrecisionLattice();

            var ghostBelt = _ghost.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true);
            if (ghostBelt != null)
                ghostBelt.SetBuildShape(ResolveConveyorBuildShape(ghostBelt, hit));

            ComputePlacementPose(hit, block, out Vector3 pos, out Quaternion rot);
            _ghost.transform.SetPositionAndRotation(pos, rot);

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
            Quarry.HidePlacementPreview();
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

        public bool TryPlace(BlockItem block, RaycastHit hit, Vector3 viewDir)
        {
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
            bool placingPowerPipe = block.placedPrefab.GetComponentInChildren<VoxelEngine.Power.PowerCable>(true) != null;
            bool placingCompactPower = block.placedPrefab.GetComponentInChildren<VoxelEngine.Simulation.CompactVoltageStation>(true) != null;
            if (!placingBelt && !placingChute && !placingPowerPipe && !placingCompactPower) return false;
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

        private bool IsPlacementValid(Vector3 pos, BlockItem block)
        {
            // Don't allow placing inside the player.
            if (Vector3.Distance(pos, transform.position) < 0.5f) return false;

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
