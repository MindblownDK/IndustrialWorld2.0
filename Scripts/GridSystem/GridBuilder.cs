// Assets/Scripts/VoxelEngine/GridSystem/GridBuilder.cs
//
// GridBuilder with grid size selection when creating a new ship.
// v5.62.3-dev — Door top-face edge placement stands upright instead of lying flat.
// v5.63.0-dev — FIX: maritime variable ports now work in grid builder:
//   • Liquid/Gas/Item pipes aiming at HFO/MGO engines get color-coded variable
//     port preview (ghost ring) that follows the pipe ghost and is grid-bound.
//   • Port ghost is anchored to the engine chassis (block transform), not the
//     Rigidbody root, fixing the "port at end of rigidbody far from chassis" bug.
//   • Ghost port ring visibility 100%: preview shows BEFORE click via
//     MaritimePortPlanner, seat is detail-lattice snapped, placement commits port.
//   • GridBuilder now handles PipeFamily detection and per-engine caps with
//     user feedback.

using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using VoxelEngine.Maritime;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.GridSystem
{
    public class GridBuilder : MonoBehaviour
    {
        [Header("Refs")]
        public Camera buildCamera;
        public Inventory inventory;
        public float reach = 16f;
        public const float BuildReach = 16f;

        [Header("Ghost")]
        public Color ghostColor = new Color(0.3f, 0.8f, 1f, 0.3f);

        [Header("Grid Size Selection")]
        public GridSize defaultGridSize = GridSize.Large;

        private GameObject _ghost;
        private GridBlockItem _ghostItem;
        private Material _ghostMat;
        private bool _ghostIsShapeVariant;
        private GridPrecisionLatticePreview _precisionLattice;

        // ── Ghost port ring (variable service port preview) ─────────
        private Transform _ghostPortRing;
        private Renderer _ghostPortRingRenderer;
        private Material _ghostPortRingMat;
        private static bool s_portCapBlocked;
        private static string s_portCapReason;
        private static string s_portCapPipeFamily;
        private static int s_previewPortService;
        private float _portCapFeedbackAt;
        private const float PortCapFeedbackInterval = 1.2f;

        private bool _ledStretchArmed;
        private GridEntity _ledStretchGrid;
        private Vector3Int _ledStretchStart;
        private Vector3Int _ledStretchMountAxis;
        private Vector3 _ledStretchStartSurfaceOffset;
        private int _lastLedStripCost = 1;
        private GridBlockItem _ledStretchItem;
        private GameObject _ledStretchGhost;
        private Material _ledStretchGhostMat;

        private Vector3Int _rotSteps;

        public static bool HoldingGridBlock { get; private set; }
        public static string HeldBlockName { get; private set; } = "";
        public static Vector3Int RotationSteps { get; private set; }

        private static bool IsShapeVariantItem(GridBlockItem item)
        {
            return item != null && item.SupportsShapeVariants;
        }

        private void Start()
        {
            if (buildCamera == null) buildCamera = Camera.main;
            if (inventory == null) inventory = GetComponentInParent<Inventory>();
            if (reach < BuildReach) reach = BuildReach;
        }

        private void OnDestroy()
        {
            if (_ghostPortRing != null) Destroy(_ghostPortRing.gameObject);
        }

        private void Update()
        {
            if (VoxelEngine.UI.UIState.IsBlocking) { HoldingGridBlock = false; HideGhost(); HidePrecisionLattice(); HideGhostPortRing(); return; }
            if (inventory == null) { HoldingGridBlock = false; HidePrecisionLattice(); HideGhostPortRing(); return; }

            var stack = inventory.ActiveStack;
            if (stack.IsEmpty || !(stack.item is GridBlockItem gbi))
            {
                HoldingGridBlock = false;
                HideGhost();
                HidePrecisionLattice();
                HideGhostPortRing();
                CancelLedStretch(false);
                return;
            }

            HoldingGridBlock = true;
            HeldBlockName = gbi.displayName;
            HandleRotationInput();

            if (_ledStretchArmed && IsLedStripItem(gbi) && (GameSettings.WasPressed(InputAction.Mine) || GameSettings.WasPressed(InputAction.Pause)))
            {
                CancelLedStretch(true);
                return;
            }

            var ray = buildCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            bool hasHit = TryRaycastIgnoringSelf(ray, out var hit, reach);
            // Belt placement surfaces are trigger-only so they never influence ship
            // physics or ordinary interaction. When holding a shaft/housing, include
            // that dedicated trigger raycast and let the player aim at the belt itself.
            if (MechanicalBeltNetwork.IsBeltTakeoffItem(gbi)
                && TryRaycastMechanicalBeltSurface(ray, reach, out var beltHit)
                && (!hasHit || beltHit.distance <= hit.distance + 0.015f))
            {
                hit = beltHit;
                hasHit = true;
            }
            if (!hasHit)
            {
                HideGhost();
                HideLedStretchGhost();
                HidePrecisionLattice();
                HideGhostPortRing();
                return;
            }

            GridEntity targetGrid = hit.collider.GetComponentInParent<GridEntity>();
            var beltPlacementSurface = hit.collider != null
                ? hit.collider.GetComponent<MechanicalBeltPlacementSurface>()
                : null;
            bool aimingBeltTakeoff = beltPlacementSurface != null && MechanicalBeltNetwork.IsBeltTakeoffItem(gbi);
            // Reset per-frame cap flag
            s_portCapBlocked = false;
            s_portCapReason = null;
            s_portCapPipeFamily = null;

            // ── Precision path (small blocks onto large grid) ─────
            if (!aimingBeltTakeoff
                && targetGrid != null
                && targetGrid.gridSize == GridSize.Large
                && gbi.gridSize == GridSize.Small)
            {
                HandlePrecisionAttachment(gbi, targetGrid, hit, ray);
                return;
            }
            HidePrecisionLattice();
            HideGhostPortRing();

            Vector3Int gridPos;
            Vector3 worldPos;
            Quaternion rotation = Quaternion.identity;

            bool blockedBySizeRule = targetGrid != null
                && targetGrid.gridSize == GridSize.Small
                && gbi.gridSize == GridSize.Large;
            if (blockedBySizeRule) { HideGhost(); HideGhostPortRing(); return; }

            var targetedBlock = hit.collider != null ? hit.collider.GetComponentInParent<GridBlock>() : null;
            bool placingTurbo = IsTurbochargerItem(gbi, out var turboTier);
            bool portSnapped = false;

            GridEntity attachGrid;
            if (aimingBeltTakeoff)
            {
                if (!beltPlacementSurface.TryGetShaftPlacement(gbi, hit.point,
                        out attachGrid, out gridPos, out worldPos, out rotation, out string beltFailure))
                {
                    HideGhost();
                    HideGhostPortRing();
                    if (GameSettings.WasPressed(InputAction.Build))
                        VoxelEngine.UI.BuildFeedbackHud.Show("Belt Take-off Blocked", beltFailure ?? "No usable shaft cell on this belt.", gbi.icon, Color.red);
                    return;
                }

                targetGrid = attachGrid;
                // This is an exact belt-axis snap: retain the authoritative pulley
                // rotation and skip ordinary neighbour / player-rotation placement.
                portSnapped = true;
            }
            else
            {
                attachGrid = (targetGrid != null && targetGrid.gridSize == gbi.gridSize)
                    ? targetGrid : FindNearbyGrid(hit.point, gbi.gridSize);

                if (attachGrid != null)
                {
                    float cs = gbi.gridSize.CellSize();
                    Vector3 probe = hit.point + hit.normal * (cs * 0.5f);
                    gridPos = attachGrid.WorldToGrid(probe);
                    if (!attachGrid.CanPlace(gridPos))
                    {
                        gridPos = attachGrid.WorldToGrid(hit.point + hit.normal * (cs * 1.0f));
                        if (!attachGrid.CanPlace(gridPos)) { HideGhost(); HideGhostPortRing(); return; }
                    }
                    targetGrid = attachGrid;
                    worldPos = attachGrid.GridToWorld(gridPos);
                    rotation = attachGrid.transform.rotation;

                    if (!placingTurbo && targetedBlock != null)
                        portSnapped = TryApplyMaritimePortSnap(gbi, attachGrid, targetedBlock, hit, ref gridPos, ref worldPos, ref rotation);
                    if (!portSnapped && !attachGrid.HasNeighbor(gridPos)) { HideGhost(); HideGhostPortRing(); return; }
                }
                else
                {
                    float cs = gbi.gridSize.CellSize();
                    Vector3 planetCenter = GravityProvider.ActiveBody.transform.position;
                    Vector3 toPoint = hit.point - planetCenter;
                    float altitude = toPoint.magnitude;
                    Vector3 up = toPoint.normalized;
                    float snappedAlt = Mathf.Ceil(altitude / cs) * cs + cs * 0.5f;
                    Vector3 forward = Vector3.Cross(up, Vector3.right);
                    if (forward.sqrMagnitude < 0.001f)
                        forward = Vector3.Cross(up, Vector3.forward);
                    forward = forward.normalized;
                    Vector3 right = Vector3.Cross(forward, up).normalized;
                    Vector3 tangentOffset = hit.point - planetCenter - up * Vector3.Dot(toPoint, up);
                    float localX = Vector3.Dot(tangentOffset, right);
                    float localZ = Vector3.Dot(tangentOffset, forward);
                    localX = Mathf.Round(localX / cs) * cs;
                    localZ = Mathf.Round(localZ / cs) * cs;
                    worldPos = planetCenter + up * snappedAlt + right * localX + forward * localZ;
                    gridPos = Vector3Int.zero;
                    targetGrid = null;
                    rotation = BuildSurfacePlacementRotation(worldPos, hit.normal);
                }
            }

            if (gbi.gridSize == GridSize.Large
                && targetGrid != null
                && targetedBlock != null
                && targetedBlock.IsPrecisionAttachment
                && !portSnapped)
            {
                var precision = targetGrid.GetComponent<GridPrecisionAttachmentLayer>();
                if (precision == null || !precision.CanPlaceStructuralBlock(gridPos))
                {
                    HideGhost();
                    HideGhostPortRing();
                    if (GameSettings.WasPressed(InputAction.Build))
                    {
                        VoxelEngine.UI.BuildFeedbackHud.Show(
                            "Needs Structural Support",
                            "Extend detail blocks to the structural block face and clear its volume",
                            gbi.icon,
                            Color.red);
                    }
                    return;
                }
            }

            // Structural blocks may not engulf a detail attachment that happens to
            // sit in the target large cell. This catches grid pipes even when the
            // player aims at a neighbouring hull block rather than directly at the pipe.
            if (gbi.gridSize == GridSize.Large && targetGrid != null && !portSnapped && !placingTurbo)
            {
                var precision = targetGrid.PrecisionAttachments;
                if (precision != null && precision.HasStructuralVolumeConflict(gridPos))
                {
                    HideGhost();
                    HideGhostPortRing();
                    if (GameSettings.WasPressed(InputAction.Build))
                    {
                        VoxelEngine.UI.BuildFeedbackHud.Show(
                            "Detail Volume Occupied",
                            "Remove or reroute the pipe/detail block before placing structure here",
                            gbi.icon,
                            Color.red);
                    }
                    return;
                }
            }

            if (placingTurbo)
            {
                if (!TryFindTurboAttachment(targetGrid, gridPos, turboTier, out var engine))
                {
                    if (targetGrid != null && targetedBlock != null && TrySnapTurboToEngine(gbi, targetGrid, hit, out var snappedPos, out engine))
                    {
                        gridPos = snappedPos;
                        worldPos = targetGrid.GridToWorld(gridPos);
                    }
                    else
                    {
                        HideGhost();
                        HideGhostPortRing();
                        return;
                    }
                }
                rotation = GetTurboAttachmentRotation(targetGrid, gridPos, engine);
            }
            else if (IsDoorItem(gbi) && targetGrid != null)
            {
                if (!TryAdjustDoorPlacementToEdge(gbi, targetGrid, hit, ref gridPos, ref worldPos, ref rotation))
                {
                    HideGhost();
                    HideGhostPortRing();
                    return;
                }
            }
            else if (!portSnapped)
            {
                rotation *= Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
            }

            if (IsLedStripItem(gbi))
            {
                if (HandleLedStripStretch(gbi, targetGrid, gridPos, worldPos, rotation, hit))
                    inventory.container.Remove(gbi, Mathf.Max(1, _lastLedStripCost));
                return;
            }
            CancelLedStretch(false);

            // Ground-clearance is only a ROOT-grid operation. Once a block joins an
            // existing structural grid, its position must remain the exact lattice cell;
            // lifting it against a tall landing-gear/visual collider makes that first block
            // sit a few centimetres above every later neighbour.
            bool keepExactAttachmentPose = portSnapped || placingTurbo;
            if (targetGrid == null)
            {
                LiftPoseOutOfGround(gbi, hit, ref worldPos, rotation);
            }
            else if (!keepExactAttachmentPose)
            {
                worldPos = targetGrid.GridToWorld(gridPos);
            }

            bool placementObstructed = PlacementObstructed(gbi, targetGrid, worldPos, rotation);
            if (placementObstructed && GameSettings.WasPressed(InputAction.Build))
            {
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    "Placement Blocked",
                    "Blocked by terrain, a construct or yourself",
                    gbi.icon,
                    Color.red);
            }

            // If port cap blocked, tint red
            if (s_portCapBlocked)
            {
                ShowGhost(gbi, worldPos, rotation, valid: false);
                if (Time.unscaledTime - _portCapFeedbackAt >= PortCapFeedbackInterval)
                {
                    _portCapFeedbackAt = Time.unscaledTime;
                    VoxelEngine.UI.BuildFeedbackHud.Show(
                        $"{s_portCapPipeFamily} pipe reached",
                        s_portCapReason ?? "Port already connected",
                        gbi.icon,
                        new Color(0.90f, 0.30f, 0.20f));
                }
                return;
            }

            ShowGhost(gbi, worldPos, rotation, valid: !placementObstructed);

            if (GameSettings.WasPressed(InputAction.Build) && !placementObstructed
                && TryPlaceBlock(gbi, targetGrid, gridPos, worldPos, rotation, keepExactAttachmentPose))
            {
                inventory.container.Remove(gbi, 1);
            }
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

        /// <summary>
        /// Belt take-off surfaces are trigger colliders so they do not participate in
        /// vehicle physics or ordinary interactions. Query them only while a held
        /// shaft/housing needs an exact in-belt placement target.
        /// </summary>
        private bool TryRaycastMechanicalBeltSurface(Ray ray, float maxDistance, out RaycastHit hit)
        {
            var hits = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Transform selfRoot = transform.root;
            for (int i = 0; i < hits.Length; i++)
            {
                var candidate = hits[i];
                if (candidate.collider == null) continue;
                if (selfRoot != null && candidate.collider.transform.IsChildOf(selfRoot)) continue;
                if (VoxelEngine.Player.PlayerRaycastFilter.IsOwnPlayerCollider(candidate.collider, transform)) continue;
                if (candidate.collider.GetComponent<MechanicalBeltPlacementSurface>() == null) continue;
                hit = candidate;
                return true;
            }
            hit = default;
            return false;
        }

        // ── Ground-clearance lift ─────────────────────────────────────
        private static readonly System.Collections.Generic.Dictionary<GridBlockItem, Bounds> s_prefabLocalBounds = new();
        private static readonly Collider[] s_obstructionProbe = new Collider[16];

        private bool PlacementObstructed(GridBlockItem item, GridEntity grid, Vector3 worldPos, Quaternion rotation)
        {
            if (item == null) return false;
            Bounds local = GetPrefabLocalBounds(item);
            Vector3 center = worldPos + rotation * local.center;
            Vector3 halfExtents = local.extents * 0.88f;
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, s_obstructionProbe,
                rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var col = s_obstructionProbe[i];
                if (col == null || !col.enabled) continue;
                if (grid != null)
                {
                    var ownerGrid = col.GetComponentInParent<GridEntity>();
                    if (ownerGrid == grid) continue;
                }
                return true;
            }
            return false;
        }

        private static void LiftPoseOutOfGround(GridBlockItem item, RaycastHit hit, ref Vector3 worldPos, Quaternion rotation)
        {
            if (item == null) return;
            Vector3 up = hit.normal.sqrMagnitude > 0.0001f
                ? hit.normal.normalized
                : GravityProvider.GetUp(hit.point);

            Bounds local = GetPrefabLocalBounds(item);
            Vector3 center = local.center;
            Vector3 ext = local.extents;
            float lowest = float.MaxValue;
            for (int xi = -1; xi <= 1; xi += 2)
            for (int yi = -1; yi <= 1; yi += 2)
            for (int zi = -1; zi <= 1; zi += 2)
            {
                Vector3 cornerWorld = worldPos + rotation * (center + new Vector3(ext.x * xi, ext.y * yi, ext.z * zi));
                float heightAlongUp = Vector3.Dot(cornerWorld - hit.point, up);
                if (heightAlongUp < lowest) lowest = heightAlongUp;
            }
            if (lowest < 0.02f) worldPos += up * (0.02f - lowest);
        }

        private static Bounds GetPrefabLocalBounds(GridBlockItem item)
        {
            if (s_prefabLocalBounds.TryGetValue(item, out var cached)) return cached;
            Bounds b;
            var prefab = item.blockPrefab;
            if (prefab != null)
            {
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                    if (prefab.transform.position.sqrMagnitude > 0.0001f)
                        b.center -= prefab.transform.position;
                    s_prefabLocalBounds[item] = b;
                    return b;
                }
            }
            float half = item.gridSize.CellSize() * 0.5f;
            b = new Bounds(Vector3.zero, new Vector3(half * 2f, half * 2f, half * 2f));
            s_prefabLocalBounds[item] = b;
            return b;
        }

        // ─────────────────────────────────────────────────────────────
        //  PRECISION ATTACHMENT (including variable maritime ports)
        // ─────────────────────────────────────────────────────────────
        private void HandlePrecisionAttachment(GridBlockItem item, GridEntity grid, RaycastHit hit, Ray ray)
        {
            if (item == null || grid == null) return;

            // ── Variable port path for pipes on engines ─────────────
            if (IsPipeItem(item, out var family))
            {
                var hitBlock = hit.collider != null ? hit.collider.GetComponentInParent<GridBlock>() : null;
                if (TryGetGridTankVariablePortSnap(grid, hitBlock, family, hit, false,
                        out string tankFeedback,
                        out Vector3Int tankPrecisionPos, out Vector3Int tankHostStructuralPos, out Vector3Int tankFaceAxis,
                        out Vector3 tankAnchorLocal, out Vector3 tankPortLocalPos, out Vector3 tankPortOutLocal,
                        out GridTankPortFamily tankPortFamily))
                {
                    ShowPrecisionLattice(grid, tankHostStructuralPos, tankFaceAxis);
                    Vector3 worldPos = grid.transform.TransformPoint(tankAnchorLocal);
                    Quaternion rotation = grid.transform.rotation * Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
                    ShowGhost(item, worldPos, rotation, valid: true);
                    ShowGhostPortRingForBlockColor(hitBlock, tankPortLocalPos, tankPortOutLocal, GridTankVariablePorts.ColorFor(tankPortFamily));

                    if (GameSettings.WasPressed(InputAction.Build))
                    {
                        bool commitSnapped = TryGetGridTankVariablePortSnap(grid, hitBlock, family, hit, true,
                            out string commitFeedback,
                            out Vector3Int cPrec, out Vector3Int cHost, out Vector3Int cFace,
                            out Vector3 cAnchor, out Vector3 cPortLocal, out Vector3 cPortOut, out GridTankPortFamily cFamily);
                        if (!commitSnapped) return;
                        var layer = grid.GetComponent<GridPrecisionAttachmentLayer>();
                        if (layer == null) layer = grid.gameObject.AddComponent<GridPrecisionAttachmentLayer>();
                        if (!layer.CanPlace(cPrec))
                        {
                            VoxelEngine.UI.BuildFeedbackHud.Show("Placement Blocked", "Detail cell occupied", item.icon, Color.red);
                            return;
                        }
                        var block = CreatePrecisionBlock(item);
                        Quaternion localRot = Quaternion.Inverse(grid.transform.rotation) * rotation;
                        if (!layer.AddBlock(cPrec, cHost, block, localRot))
                        {
                            Destroy(block.gameObject);
                            return;
                        }
                        block.transform.localPosition = cAnchor;
                        var pv = block.GetComponentInChildren<VoxelEngine.Networks.PipeVisualBuilder>(true);
                        if (pv != null) { pv.gridSize = GridSize.Small.CellSize(); pv.ForceRebuild(); }
                        GridLiquidNetwork.Instance?.SetDirty();
                        GridGasNetwork.Instance?.SetDirty();
                        VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
                        inventory.container.Remove(item, 1);
                        VoxelEngine.UI.BuildFeedbackHud.Show("Pipe Attached", commitFeedback ?? $"{item.displayName} · {GridTankVariablePorts.LabelFor(cFamily)}", item.icon, item.iconTint);
                    }
                    return;
                }
                if (IsMatchingTankBlockForPipe(hitBlock, family))
                {
                    HideGhost();
                    HideGhostPortRing();
                    if (GameSettings.WasPressed(InputAction.Build))
                        VoxelEngine.UI.BuildFeedbackHud.Show("Port Occupied", "That variable tank port already has a pipe", item.icon, Color.red);
                    return;
                }

                if (hitBlock is GridMaritimeEngine)
                {
                    bool snapped = TryGetGridVariablePortSnap(grid, hitBlock, family, hit, false,
                        out string feedback,
                        out Vector3Int precisionPos, out Vector3Int hostStructuralPos, out Vector3Int faceAxis,
                        out Vector3 anchorLocal,
                        out Vector3 portLocalPos, out Vector3 portOutLocal, out bool portIsNew, out int portService);

                    // Over-cap case: show red ghost + feedback, don't fall through to free placement
                    if (s_portCapBlocked)
                    {
                        ShowPrecisionLattice(grid, hostStructuralPos != default ? hostStructuralPos : grid.WorldToGrid(hit.point), faceAxis != default ? faceAxis : SnapMountAxis(grid, hit.normal));
                        float cs = GridSize.Small.CellSize();
                        Vector3 local = grid.transform.InverseTransformPoint(hit.point);
                        Vector3Int fallbackPrec = new(
                            Mathf.RoundToInt(local.x / cs),
                            Mathf.RoundToInt(local.y / cs),
                            Mathf.RoundToInt(local.z / cs));
                        Vector3 worldPosFallback = grid.transform.TransformPoint((Vector3)fallbackPrec * cs);
                        Quaternion rotFallback = grid.transform.rotation * Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
                        ShowGhost(item, worldPosFallback, rotFallback, valid: false);
                        ShowGhostPortRingForBlock(hitBlock, portLocalPos, portOutLocal, isBlocked: true);
                        if (GameSettings.WasPressed(InputAction.Build) && Time.unscaledTime - _portCapFeedbackAt >= PortCapFeedbackInterval)
                        {
                            _portCapFeedbackAt = Time.unscaledTime;
                            VoxelEngine.UI.BuildFeedbackHud.Show(
                                $"{s_portCapPipeFamily} pipe reached",
                                s_portCapReason ?? "Port already connected",
                                item.icon,
                                new Color(0.90f, 0.30f, 0.20f));
                        }
                        return;
                    }

                    if (snapped)
                    {
                        ShowPrecisionLattice(grid, hostStructuralPos, faceAxis);
                        Vector3 worldPos = grid.transform.TransformPoint(anchorLocal);
                        Quaternion rotation = grid.transform.rotation * Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
                        ShowGhost(item, worldPos, rotation, valid: true);
                        // Port ghost follows pipe ghost - anchored to engine chassis, not rigidbody root
                        ShowGhostPortRingForBlock(hitBlock, portLocalPos, portOutLocal, isBlocked: false, service: portService);

                        if (GameSettings.WasPressed(InputAction.Build))
                        {
                            // Commit path
                            bool commitSnapped = TryGetGridVariablePortSnap(grid, hitBlock, family, hit, true,
                                out string commitFeedback,
                                out Vector3Int cPrec, out Vector3Int cHost, out Vector3Int cFace,
                                out Vector3 cAnchor,
                                out Vector3 cPortLocal, out Vector3 cPortOut, out bool cIsNew, out int cService);
                            if (!commitSnapped) return;
                            var layer = grid.GetComponent<GridPrecisionAttachmentLayer>();
                            if (layer == null) layer = grid.gameObject.AddComponent<GridPrecisionAttachmentLayer>();
                            if (!layer.CanPlace(cPrec))
                            {
                                VoxelEngine.UI.BuildFeedbackHud.Show("Placement Blocked", "Detail cell occupied", item.icon, Color.red);
                                return;
                            }
                            var block = CreatePrecisionBlock(item);
                            Quaternion localRot = Quaternion.Inverse(grid.transform.rotation) * rotation;
                            if (!layer.AddBlock(cPrec, cHost, block, localRot))
                            {
                                Destroy(block.gameObject);
                                return;
                            }
                            // Seat exactly on port anchor (grid-bound)
                            block.transform.localPosition = cAnchor;
                            // Refresh pipe visuals + networks
                            var pv = block.GetComponentInChildren<VoxelEngine.Networks.PipeVisualBuilder>(true);
                            if (pv != null) { pv.gridSize = GridSize.Small.CellSize(); pv.ForceRebuild(); }
                            GridLiquidNetwork.Instance?.SetDirty();
                            GridGasNetwork.Instance?.SetDirty();
                            VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
                            inventory.container.Remove(item, 1);
                            VoxelEngine.UI.BuildFeedbackHud.Show("Pipe Attached", commitFeedback ?? $"{item.displayName} · {MaritimeVariablePorts.LabelFor((PortService)cService)}", item.icon, item.iconTint);
                        }
                        return;
                    }
                }
            }

            // ── Standard precision placement ────────────────────────
            Vector3Int faceAxisStd = SnapMountAxis(grid, hit.normal);
            Vector3 localNormal = ((Vector3)faceAxisStd).normalized;
            var hitBlockStd = hit.collider != null ? hit.collider.GetComponentInParent<GridBlock>() : null;
            bool attachingToPrecisionBlock = hitBlockStd != null
                && hitBlockStd.Grid == grid
                && hitBlockStd.IsPrecisionAttachment;

            Vector3Int precisionPosStd;
            if (attachingToPrecisionBlock)
            {
                precisionPosStd = hitBlockStd.PrecisionGridPos + faceAxisStd;
            }
            else
            {
                float smallCellSize = GridSize.Small.CellSize();
                Vector3 localCenter = grid.transform.InverseTransformPoint(hit.point)
                    + localNormal * (smallCellSize * 0.5f);
                precisionPosStd = new Vector3Int(
                    Mathf.RoundToInt(localCenter.x / smallCellSize),
                    Mathf.RoundToInt(localCenter.y / smallCellSize),
                    Mathf.RoundToInt(localCenter.z / smallCellSize));
            }

            Vector3Int largeCell = hitBlockStd != null && hitBlockStd.Grid == grid
                ? (hitBlockStd.IsPrecisionAttachment ? hitBlockStd.PrecisionHostGridPos : hitBlockStd.GridPos)
                : grid.WorldToGrid(hit.point - hit.normal * 0.02f);
            ShowPrecisionLattice(grid, largeCell, faceAxisStd);

            var layerStd = grid.GetComponent<GridPrecisionAttachmentLayer>();
            if (layerStd == null) layerStd = grid.gameObject.AddComponent<GridPrecisionAttachmentLayer>();

            Vector3 localPosition = (Vector3)precisionPosStd * GridSize.Small.CellSize();
            Vector3Int occupiedLargeCell = new(
                Mathf.RoundToInt(localPosition.x / GridSize.Large.CellSize()),
                Mathf.RoundToInt(localPosition.y / GridSize.Large.CellSize()),
                Mathf.RoundToInt(localPosition.z / GridSize.Large.CellSize()));

            bool supported = attachingToPrecisionBlock
                ? layerStd.HasNeighbor(precisionPosStd)
                : hitBlockStd != null && hitBlockStd.Grid == grid;
            bool overlapsLargeCell = attachingToPrecisionBlock
                && grid.GetBlock(occupiedLargeCell) != null;
            bool valid = supported
                && layerStd.CanPlace(precisionPosStd)
                && !overlapsLargeCell;

            Quaternion rotationStd = grid.transform.rotation
                * Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
            Vector3 worldPosition = grid.transform.TransformPoint(localPosition);

            if (valid) ShowGhost(item, worldPosition, rotationStd, valid: true);
            else HideGhost();

            if (!GameSettings.WasPressed(InputAction.Build)) return;
            if (!valid)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    "Precision Placement Blocked",
                    supported ? "That Detail lattice cell is occupied" : "Attach to a Structural face or another Detail block",
                    item.icon,
                    Color.red);
                return;
            }

            GridBlock blockStd;
            if (item.blockPrefab != null)
            {
                var instance = Instantiate(item.blockPrefab);
                blockStd = instance.GetComponent<GridBlock>();
                if (blockStd == null) blockStd = instance.AddComponent<GridBlock>();
            }
            else
            {
                blockStd = GridBlock.CreateBlock<GridBlock>("Precision Block", GridSize.Small, item.iconTint);
            }

            blockStd.blockName = item.displayName;
            blockStd.BlockMass = item.blockMass;
            blockStd.maxHP = item.blockHP;
            blockStd.SourceItem = item;
            if (IsShapeVariantItem(item))
            {
                var shapeVisual = blockStd.GetComponent<GridShapeVariantBlock>();
                if (shapeVisual == null) shapeVisual = blockStd.gameObject.AddComponent<GridShapeVariantBlock>();
                shapeVisual.Configure(VoxelEngine.UI.GridShapeWheel.CurrentShape, GridSize.Small);
            }

            Quaternion localRotation = Quaternion.Inverse(grid.transform.rotation) * rotationStd;
            if (!layerStd.AddBlock(precisionPosStd, largeCell, blockStd, localRotation))
            {
                Destroy(blockStd.gameObject);
                return;
            }

            inventory.container.Remove(item, 1);
            VoxelEngine.UI.BuildFeedbackHud.Show(
                "Precision Attached",
                $"{item.displayName} · 5×5 large-face lattice",
                item.icon,
                item.iconTint);
        }

        private GridBlock CreatePrecisionBlock(GridBlockItem item)
        {
            GridBlock block;
            if (item.blockPrefab != null)
            {
                var instance = Instantiate(item.blockPrefab);
                block = instance.GetComponent<GridBlock>();
                if (block == null) block = instance.AddComponent<GridBlock>();
            }
            else
            {
                block = GridBlock.CreateBlock<GridBlock>("Precision Block", GridSize.Small, item.iconTint);
            }
            block.blockName = item.displayName;
            block.BlockMass = item.blockMass;
            block.maxHP = item.blockHP;
            block.SourceItem = item;
            if (IsShapeVariantItem(item))
            {
                var shapeVisual = block.GetComponent<GridShapeVariantBlock>();
                if (shapeVisual == null) shapeVisual = block.gameObject.AddComponent<GridShapeVariantBlock>();
                shapeVisual.Configure(VoxelEngine.UI.GridShapeWheel.CurrentShape, GridSize.Small);
            }
            return block;
        }

        private void ShowPrecisionLattice(GridEntity grid, Vector3Int largeCell, Vector3Int faceAxis)
        {
            if (_precisionLattice == null)
            {
                var preview = new GameObject("GridPrecisionLatticePreview");
                _precisionLattice = preview.AddComponent<GridPrecisionLatticePreview>();
            }
            _precisionLattice.Show(grid, largeCell, faceAxis);
        }

        private void HidePrecisionLattice()
        {
            if (_precisionLattice != null) _precisionLattice.Hide();
        }

        // ── Variable port helpers ──────────────────────────────────
        private bool IsPipeItem(GridBlockItem item, out PipeFamily family)
        {
            family = PipeFamily.Liquid;
            if (item == null || item.blockPrefab == null) return false;
            if (item.blockPrefab.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null) { family = PipeFamily.Liquid; return true; }
            if (item.blockPrefab.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null) { family = PipeFamily.Gas; return true; }
            if (item.blockPrefab.GetComponentInChildren<VoxelEngine.Transport.ItemPipe>(true) != null) { family = PipeFamily.Item; return true; }
            string id = (item.itemId ?? "").ToLowerInvariant();
            string dn = (item.displayName ?? "").ToLowerInvariant();
            if (id.Contains("liquid") || id.Contains("water") || dn.Contains("liquid") || dn.Contains("water")) { family = PipeFamily.Liquid; return true; }
            if (id.Contains("gas") || dn.Contains("gas")) { family = PipeFamily.Gas; return true; }
            if (id.Contains("item") || dn.Contains("item pipe")) { family = PipeFamily.Item; return true; }
            return false;
        }

        private static Vector3Int RoundHalfUp(Vector3 v) => new(
            Mathf.FloorToInt(v.x + 0.5f),
            Mathf.FloorToInt(v.y + 0.5f),
            Mathf.FloorToInt(v.z + 0.5f));

        private bool TryGetGridVariablePortSnap(GridEntity grid, GridBlock targetBlock,
            PipeFamily family, RaycastHit hit, bool commit, out string feedback,
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

            var engine = targetBlock as GridMaritimeEngine;
            if (engine == null || engine.Grid != grid) return false;

            float small = GridSize.Small.CellSize();
            var plan = MaritimePortPlanner.PlanPipe(grid, engine, family, hit.point, hit.normal, small);

            if (plan.atCap)
            {
                int max = MaritimeVariablePorts.MaxFor(plan.service);
                feedback = $"{MaritimeVariablePorts.LabelFor(plan.service)} already connected (max {max})";
                s_portCapBlocked = true;
                s_portCapReason = feedback;
                s_portCapPipeFamily = family == PipeFamily.Gas ? "Gas" : family == PipeFamily.Liquid ? "Liquid" : "Item";
                s_previewPortService = (int)plan.service;
                // Provide dummy port preview so ring appears red at engine surface
                portLocalPos = plan.portLocal;
                portOutLocal = plan.outLocal;
                portService = (int)plan.service;
                hostStructuralPos = engine.GridPos;
                faceAxis = plan.faceAxis;
                return false;
            }
            if (!plan.ok) return false;

            portLocalPos = plan.portLocal;
            portOutLocal = plan.outLocal;
            portIsNew = !plan.reusesExisting;
            portService = (int)plan.service;

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
                ? $"Connected to {MaritimeVariablePorts.LabelFor(plan.service)}"
                : $"{MaritimeVariablePorts.LabelFor(plan.service)} installed";
            s_previewPortService = (int)plan.service;
            return true;
        }

        private static bool IsMatchingTankBlockForPipe(GridBlock block, PipeFamily family)
        {
            return (family == PipeFamily.Liquid && (block is GridLiquidTank || block is GridBiofarm || block is GridH2O2Generator))
                || (family == PipeFamily.Gas && (block is GridGasTank || block is GridCryobed || block is GridBiofarm || block is GridH2O2Generator));
        }

        private bool TryGetGridTankVariablePortSnap(GridEntity grid, GridBlock targetBlock,
            PipeFamily pipeFamily, RaycastHit hit, bool commit, out string feedback,
            out Vector3Int precisionPos, out Vector3Int hostStructuralPos, out Vector3Int faceAxis,
            out Vector3 anchorLocal, out Vector3 portLocalPos, out Vector3 portOutLocal,
            out GridTankPortFamily tankFamily)
        {
            feedback = null;
            precisionPos = default;
            hostStructuralPos = default;
            faceAxis = default;
            anchorLocal = default;
            portLocalPos = default;
            portOutLocal = default;
            tankFamily = GridTankPortFamily.Liquid;

            if (grid == null || targetBlock == null || targetBlock.Grid != grid) return false;
            if (pipeFamily == PipeFamily.Liquid)
            {
                if (targetBlock is not GridLiquidTank
                    && targetBlock is not GridBiofarm
                    && targetBlock is not GridH2O2Generator) return false;
                tankFamily = GridTankPortFamily.Liquid;
            }
            else if (pipeFamily == PipeFamily.Gas)
            {
                if (targetBlock is not GridGasTank
                    && targetBlock is not GridCryobed
                    && targetBlock is not GridBiofarm
                    && targetBlock is not GridH2O2Generator) return false;
                tankFamily = GridTankPortFamily.Gas;
            }
            else return false;

            float small = GridSize.Small.CellSize();
            faceAxis = SnapMountAxis(grid, hit.normal);
            if (faceAxis == Vector3Int.zero) faceAxis = Vector3Int.up;
            Vector3 outGridLocal = new Vector3(faceAxis.x, faceAxis.y, faceAxis.z).normalized;
            Vector3 outWorld = grid.transform.TransformDirection(outGridLocal).normalized;

            Vector3 rawSeatGridLocal = grid.transform.InverseTransformPoint(hit.point + outWorld * (small * 0.55f));
            precisionPos = new Vector3Int(
                Mathf.FloorToInt(rawSeatGridLocal.x / small + 0.5f),
                Mathf.FloorToInt(rawSeatGridLocal.y / small + 0.5f),
                Mathf.FloorToInt(rawSeatGridLocal.z / small + 0.5f));
            anchorLocal = (Vector3)precisionPos * small;
            hostStructuralPos = targetBlock.IsPrecisionAttachment
                ? targetBlock.PrecisionHostGridPos
                : targetBlock.GridPos;

            var layer = grid.GetComponent<GridPrecisionAttachmentLayer>();
            if (layer != null && !layer.CanPlace(precisionPos)) return false;

            Vector3 seatWorld = grid.transform.TransformPoint(anchorLocal);
            Vector3 portWorld = seatWorld - outWorld * (small * 0.55f + 0.02f);
            portLocalPos = targetBlock.transform.InverseTransformPoint(portWorld);
            portOutLocal = targetBlock.transform.InverseTransformDirection(outWorld).normalized;
            if (portOutLocal.sqrMagnitude < 0.0001f) portOutLocal = Vector3.up;

            if (commit)
            {
                var ports = targetBlock.GetComponent<GridTankVariablePorts>();
                if (ports == null) ports = targetBlock.gameObject.AddComponent<GridTankVariablePorts>();
                ports.AddPort(tankFamily, portLocalPos, portOutLocal);
            }

            feedback = GridTankVariablePorts.LabelFor(tankFamily) + " installed";
            return true;
        }

        private void ShowGhostPortRingForBlockColor(GridBlock block, Vector3 portLocalPos, Vector3 portOutLocal, Color color)
        {
            if (block == null) return;
            Vector3 worldPos = block.transform.TransformPoint(portLocalPos);
            Vector3 outWorld = block.transform.TransformDirection(portOutLocal).normalized;
            if (outWorld.sqrMagnitude < 0.0001f) outWorld = block.transform.up;
            ShowGhostPortRing(worldPos, outWorld, color);
        }

        private void ShowGhostPortRingForBlock(GridBlock block, Vector3 portLocalPos, Vector3 portOutLocal, bool isBlocked, int service = 0)
        {
            if (block == null) return;
            Vector3 worldPos = block.transform.TransformPoint(portLocalPos);
            Vector3 outWorld = block.transform.TransformDirection(portOutLocal).normalized;
            if (outWorld.sqrMagnitude < 0.0001f) outWorld = block.transform.up;
            Color col = isBlocked
                ? new Color(0.95f, 0.25f, 0.20f)
                : MaritimeVariablePorts.ColorFor((PortService)service);
            ShowGhostPortRing(worldPos, outWorld, col);
        }

        private void ShowGhostPortRing(Vector3 worldPos, Vector3 outWorld, Color color)
        {
            if (_ghostPortRing == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _ghostPortRingMat = new Material(sh);
                _ghostPortRingMat.name = "GridBuilder_GhostPortRing";
                if (_ghostPortRingMat.HasProperty("_BaseColor")) _ghostPortRingMat.SetColor("_BaseColor", color);
                _ghostPortRingMat.color = color;
                if (_ghostPortRingMat.HasProperty("_Metallic")) _ghostPortRingMat.SetFloat("_Metallic", 0.35f);
                if (_ghostPortRingMat.HasProperty("_Smoothness")) _ghostPortRingMat.SetFloat("_Smoothness", 0.6f);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "GridGhostPortRing";
                var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
                _ghostPortRingRenderer = go.GetComponent<Renderer>();
                _ghostPortRingRenderer.sharedMaterial = _ghostPortRingMat;
                _ghostPortRing = go.transform;
                _ghostPortRing.localScale = new Vector3(0.32f, 0.04f, 0.32f);
            }
            _ghostPortRing.gameObject.SetActive(true);
            // Place slightly outside hull so it reads as bolted, anchored to chassis (block transform), not Rigidbody root
            _ghostPortRing.position = worldPos + outWorld * 0.02f;
            Vector3 guide = Vector3.Cross(outWorld, Vector3.up);
            if (guide.sqrMagnitude < 0.0001f) guide = Vector3.Cross(outWorld, Vector3.forward);
            if (guide.sqrMagnitude < 0.0001f) guide = Vector3.right;
            _ghostPortRing.rotation = Quaternion.LookRotation(outWorld, guide) * Quaternion.Euler(90f, 0f, 0f);
            if (_ghostPortRingMat != null)
            {
                _ghostPortRingMat.color = color;
                if (_ghostPortRingMat.HasProperty("_BaseColor")) _ghostPortRingMat.SetColor("_BaseColor", color);
                if (_ghostPortRingMat.HasProperty("_EmissionColor"))
                {
                    Color em = color * 0.6f;
                    em.a = 1f;
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

        // ── Remaining helpers (unchanged) ──────────────────────────
        private bool TryPlaceBlock(GridBlockItem item, GridEntity grid, Vector3Int gridPos, Vector3 worldPos,
            Quaternion rotation, bool keepExactAttachmentPose)
        {
            if (grid != null && item != null && item.gridSize == GridSize.Large && !keepExactAttachmentPose
                && grid.PrecisionAttachments != null && grid.PrecisionAttachments.HasStructuralVolumeConflict(gridPos))
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Detail Volume Occupied", "Clear the pipe/detail block before placing structure here", item.icon, Color.red);
                return false;
            }

            if (grid != null && IsCellReservedByLedStrip(grid, gridPos))
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Placement Blocked", "LED strip path reserves this cell", item != null ? item.icon : null, Color.red);
                return false;
            }

            if (IsTurbochargerItem(item, out var turboTier))
            {
                if (!TryFindTurboAttachment(grid, gridPos, turboTier, out var engine))
                    return false;
                rotation = GetTurboAttachmentRotation(grid, gridPos, engine);
            }

            bool creatingUnifiedDetailRoot = grid == null && item.gridSize == GridSize.Small;
            bool createdNewGrid = false;
            if (grid == null)
            {
                grid = GridEntity.Create(worldPos, GridSize.Large);
                grid.transform.rotation = rotation;
                if (grid.Body != null) grid.Body.isKinematic = true;
                createdNewGrid = true;
                gridPos = Vector3Int.zero;
            }

            GridBlock block;
            if (item.blockPrefab != null)
            {
                var go = Instantiate(item.blockPrefab);
                go.transform.rotation = rotation;
                block = go.GetComponent<GridBlock>();
                if (block == null) block = go.AddComponent<GridBlock>();
            }
            else
            {
                block = GridBlock.CreateBlock<GridBlock>("Block", item.gridSize, item.iconTint);
                block.transform.rotation = rotation;
            }

            block.blockName = item.displayName;
            block.BlockMass = item.blockMass;
            block.maxHP = item.blockHP;
            block.SourceItem = item;

            if (IsShapeVariantItem(item))
            {
                var shapeVisual = block.GetComponent<GridShapeVariantBlock>();
                if (shapeVisual == null) shapeVisual = block.gameObject.AddComponent<GridShapeVariantBlock>();
                shapeVisual.Configure(VoxelEngine.UI.GridShapeWheel.CurrentShape, item.gridSize);
            }

            if (creatingUnifiedDetailRoot)
            {
                var precisionLayer = grid.GetComponent<GridPrecisionAttachmentLayer>();
                if (precisionLayer == null) precisionLayer = grid.gameObject.AddComponent<GridPrecisionAttachmentLayer>();
                Quaternion localRotation = Quaternion.Inverse(grid.transform.rotation) * rotation;
                if (!precisionLayer.AddBlock(Vector3Int.zero, Vector3Int.zero, block, localRotation))
                {
                    Destroy(block.gameObject);
                    Destroy(grid.gameObject);
                    return false;
                }
            }
            else
            {
                grid.AddBlock(gridPos, block);
                // Normal structural placement is always cell-centred. Only explicitly
                // port-snapped machinery/turbo attachments may retain a non-lattice root
                // pose, because their authored port offsets are authoritative.
                block.transform.position = keepExactAttachmentPose
                    ? worldPos
                    : grid.GridToWorld(gridPos);
                block.transform.rotation = rotation;
            }

            VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(item.displayName, item, 1);
            if (createdNewGrid && grid.Body != null)
                StartCoroutine(ReenableGridPhysicsNextFixed(grid));
            return true;
        }

        private System.Collections.IEnumerator ReenableGridPhysicsNextFixed(GridEntity grid)
        {
            yield return new WaitForFixedUpdate();
            if (grid != null && grid.Body != null)
                grid.Body.isKinematic = false;
        }

        private static Quaternion BuildSurfacePlacementRotation(Vector3 position, Vector3 hitNormal)
        {
            Vector3 radialUp = GravityProvider.GetUp(position);
            Vector3 preferredUp = hitNormal.sqrMagnitude > 0.0001f ? hitNormal.normalized : radialUp;
            if (Vector3.Dot(preferredUp, radialUp) < 0.35f)
                preferredUp = radialUp;
            Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, preferredUp);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(Vector3.right, preferredUp);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.Cross(preferredUp, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.Cross(preferredUp, Vector3.forward);
            forward.Normalize();
            return Quaternion.LookRotation(forward, preferredUp);
        }

        private bool IsLedStripItem(GridBlockItem item)
        {
            if (item == null) return false;
            if (item.blockPrefab != null && item.blockPrefab.GetComponentInChildren<VoxelEngine.Simulation.LEDStrip>(true) != null)
                return true;
            string id = (item.itemId ?? string.Empty).ToLowerInvariant();
            string name = (item.displayName ?? string.Empty).ToLowerInvariant();
            return id.Contains("ledstrip") || id.Contains("led_strip") || name.Contains("led strip");
        }

        private bool IsDoorItem(GridBlockItem item)
        {
            if (item == null) return false;
            if (item.blockPrefab != null && item.blockPrefab.GetComponentInChildren<GridSlidingDoor>(true) != null)
                return true;
            string id = (item.itemId ?? string.Empty).ToLowerInvariant();
            string name = (item.displayName ?? string.Empty).ToLowerInvariant();
            return id.Contains("slidingdoor") || id.Contains("vaultdoor") || name.Contains("sliding door") || name.Contains("vault door");
        }

        private bool TryAdjustDoorPlacementToEdge(GridBlockItem item, GridEntity grid, RaycastHit hit, ref Vector3Int gridPos, ref Vector3 worldPos, ref Quaternion rotation)
        {
            if (item == null || grid == null) return false;
            Vector3Int mountAxis = SnapMountAxis(grid, hit.normal);
            var hostBlock = hit.collider != null ? hit.collider.GetComponentInParent<GridBlock>() : null;
            bool edgeMountedFromTopOrBottom = false;
            if (Mathf.Abs(mountAxis.y) > 0 && hostBlock != null && hostBlock.Grid == grid)
            {
                int verticalSign = mountAxis.y >= 0 ? 1 : -1;
                float cellSize = grid.gridSize.CellSize();
                Vector3 localHit = grid.transform.InverseTransformPoint(hit.point)
                                 - new Vector3(hostBlock.GridPos.x, hostBlock.GridPos.y, hostBlock.GridPos.z) * cellSize;
                if (Mathf.Abs(localHit.x) >= Mathf.Abs(localHit.z))
                    mountAxis = new Vector3Int(localHit.x >= 0f ? 1 : -1, 0, 0);
                else
                    mountAxis = new Vector3Int(0, 0, localHit.z >= 0f ? 1 : -1);
                gridPos = hostBlock.GridPos + mountAxis + new Vector3Int(0, verticalSign, 0);
                worldPos = grid.GridToWorld(gridPos);
                edgeMountedFromTopOrBottom = true;
            }
            if (!grid.CanPlace(gridPos)) return false;
            if (!edgeMountedFromTopOrBottom && !grid.HasNeighbor(gridPos)) return false;
            Vector3 outward = grid.transform.TransformDirection(new Vector3(mountAxis.x, mountAxis.y, mountAxis.z));
            if (outward.sqrMagnitude < 0.0001f) outward = hit.normal;
            rotation = BuildDoorSurfaceRotation(grid, outward);
            return true;
        }

        private static Quaternion BuildDoorSurfaceRotation(GridEntity grid, Vector3 worldNormal)
        {
            if (grid == null) return Quaternion.identity;
            Vector3 normal = worldNormal.sqrMagnitude > 0.0001f ? worldNormal.normalized : grid.transform.forward;
            Vector3 up = Vector3.ProjectOnPlane(grid.transform.up, normal);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(grid.transform.forward, normal);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            return Quaternion.LookRotation(normal, up.normalized);
        }

        private bool HandleLedStripStretch(GridBlockItem item, GridEntity grid, Vector3Int gridPos, Vector3 worldPos, Quaternion rotation, RaycastHit hit)
        {
            if (_ledStretchArmed && (_ledStretchItem != item || _ledStretchGrid == null))
                CancelLedStretch(false);

            if (!_ledStretchArmed)
            {
                HideGhost();
                if (grid != null)
                {
                    float previewCellSize = item.gridSize.CellSize();
                    Vector3Int previewMountAxis = SnapMountAxis(grid, hit.normal);
                    Vector3 previewSurfaceOffset = ComputeSurfaceHitOffset(grid, gridPos, previewMountAxis, hit);
                    Vector3 previewDir = DefaultLedAxis(previewMountAxis);
                    Quaternion previewRotation = BuildLedSurfaceRotation(grid, previewDir, previewMountAxis);
                    float previewSurfaceOffsetY = -previewCellSize * 0.5f + Mathf.Max(0.0025f, previewCellSize * 0.004f);
                    float previewLateral = ComputeLedLateralOffset(previewDir, previewMountAxis, previewSurfaceOffset, previewCellSize, item.gridSize == GridSize.Large ? 0.18f : 0.045f);
                    ShowLedStretchGhost(grid.GridToWorld(gridPos), previewCellSize, previewRotation, item.gridSize, new Vector3(0f, previewSurfaceOffsetY, previewLateral));
                }
                else
                {
                    HideLedStretchGhost();
                }

                if (!GameSettings.WasPressed(InputAction.Build)) return false;
                if (grid == null)
                {
                    VoxelEngine.UI.BuildFeedbackHud.Show("LED Strip", "Aim at an existing grid to select first corner.", item.icon, item.iconTint);
                    return false;
                }

                _ledStretchArmed = true;
                _ledStretchGrid = grid;
                _ledStretchStart = gridPos;
                _ledStretchMountAxis = SnapMountAxis(grid, hit.normal);
                _ledStretchStartSurfaceOffset = ComputeSurfaceHitOffset(grid, gridPos, _ledStretchMountAxis, hit);
                _ledStretchItem = item;
                VoxelEngine.UI.BuildFeedbackHud.Show("LED Strip", "First corner set. Aim second corner and right-click.", item.icon, item.iconTint);
                return false;
            }

            if (grid != _ledStretchGrid)
            {
                HideGhost();
                HideLedStretchGhost();
                if (GameSettings.WasPressed(InputAction.Build))
                    VoxelEngine.UI.BuildFeedbackHud.Show("LED Strip", "Second corner must be on the same grid.", item.icon, Color.red);
                return false;
            }

            Vector3Int end = SnapLedEnd(_ledStretchStart, gridPos, _ledStretchMountAxis);
            Vector3Int delta = end - _ledStretchStart;
            Vector3 localDir = new Vector3(delta.x, delta.y, delta.z);
            if (localDir.sqrMagnitude < 0.0001f)
                localDir = DefaultLedAxis(_ledStretchMountAxis);
            localDir = Vector3.ProjectOnPlane(localDir, new Vector3(_ledStretchMountAxis.x, _ledStretchMountAxis.y, _ledStretchMountAxis.z));
            if (localDir.sqrMagnitude < 0.0001f)
                localDir = DefaultLedAxis(_ledStretchMountAxis);
            localDir.Normalize();

            float cs = item.gridSize.CellSize();
            float centerDistance = Vector3.Distance(_ledStretchGrid.GridToWorld(_ledStretchStart), _ledStretchGrid.GridToWorld(end));
            float length = Mathf.Max(cs, centerDistance + cs);
            Quaternion stripRotation = BuildLedSurfaceRotation(_ledStretchGrid, localDir, _ledStretchMountAxis);
            Vector3 startWorld = _ledStretchGrid.GridToWorld(_ledStretchStart);
            float surfaceOffset = -cs * 0.5f + Mathf.Max(0.0025f, cs * 0.004f);
            float lateralOffset = ComputeLedLateralOffset(localDir, _ledStretchMountAxis, _ledStretchStartSurfaceOffset, cs, item.gridSize == GridSize.Large ? 0.18f : 0.045f);
            Vector3 ledLocalOffset = new Vector3(centerDistance * 0.5f, surfaceOffset, lateralOffset);
            int cellsNeeded = LedCellsNeeded(_ledStretchStart, end);

            HideGhost();
            ShowLedStretchGhost(startWorld, length, stripRotation, item.gridSize, ledLocalOffset);

            if (!GameSettings.WasPressed(InputAction.Build)) return false;

            if (inventory == null || inventory.container == null || inventory.container.CountOf(item) < cellsNeeded)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Need More LED Strips", $"Requires {cellsNeeded} items for this length", item.icon, Color.red);
                return false;
            }
            if (!IsLedStripPathClear(_ledStretchGrid, _ledStretchStart, end))
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("LED Path Blocked", "Clear the cells along the strip path first", item.icon, Color.red);
                return false;
            }

            _lastLedStripCost = cellsNeeded;
            if (!TryPlaceLedStrip(item, _ledStretchGrid, _ledStretchStart, stripRotation, length, ledLocalOffset))
                return false;

            CancelLedStretch(false);
            return true;
        }

        private static Vector3Int SnapLedEnd(Vector3Int start, Vector3Int rawEnd, Vector3Int mountAxis)
        {
            Vector3Int d = rawEnd - start;
            int ax = mountAxis.x != 0 ? -1 : Mathf.Abs(d.x);
            int ay = mountAxis.y != 0 ? -1 : Mathf.Abs(d.y);
            int az = mountAxis.z != 0 ? -1 : Mathf.Abs(d.z);
            if (ax >= ay && ax >= az) return new Vector3Int(rawEnd.x, start.y, start.z);
            if (ay >= ax && ay >= az) return new Vector3Int(start.x, rawEnd.y, start.z);
            if (az >= ax && az >= ay) return new Vector3Int(start.x, start.y, rawEnd.z);
            Vector3 fallback = DefaultLedAxis(mountAxis);
            return start + new Vector3Int(Mathf.RoundToInt(fallback.x), Mathf.RoundToInt(fallback.y), Mathf.RoundToInt(fallback.z));
        }

        private static Vector3Int SnapMountAxis(GridEntity grid, Vector3 worldNormal)
        {
            if (grid == null || worldNormal.sqrMagnitude < 0.0001f) return Vector3Int.up;
            Vector3 local = grid.transform.InverseTransformDirection(worldNormal.normalized);
            float ax = Mathf.Abs(local.x);
            float ay = Mathf.Abs(local.y);
            float az = Mathf.Abs(local.z);
            if (ax >= ay && ax >= az) return new Vector3Int(local.x >= 0f ? 1 : -1, 0, 0);
            if (ay >= ax && ay >= az) return new Vector3Int(0, local.y >= 0f ? 1 : -1, 0);
            return new Vector3Int(0, 0, local.z >= 0f ? 1 : -1);
        }

        private static Vector3 DefaultLedAxis(Vector3Int mountAxis)
        {
            if (mountAxis.x == 0) return Vector3.right;
            if (mountAxis.z == 0) return Vector3.forward;
            return Vector3.up;
        }

        private static Quaternion BuildLedSurfaceRotation(GridEntity grid, Vector3 localDirection, Vector3Int mountAxis)
        {
            Vector3 localNormal = new Vector3(mountAxis.x, mountAxis.y, mountAxis.z);
            if (localNormal.sqrMagnitude < 0.0001f) localNormal = Vector3.up;
            if (localDirection.sqrMagnitude < 0.0001f) localDirection = DefaultLedAxis(mountAxis);
            localDirection = Vector3.ProjectOnPlane(localDirection.normalized, localNormal.normalized);
            if (localDirection.sqrMagnitude < 0.0001f) localDirection = DefaultLedAxis(mountAxis);

            Vector3 right = grid != null ? grid.transform.TransformDirection(localDirection.normalized) : localDirection.normalized;
            Vector3 up = grid != null ? grid.transform.TransformDirection(localNormal.normalized) : localNormal.normalized;
            Vector3 forward = Vector3.Cross(right, up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            return Quaternion.LookRotation(forward.normalized, up.normalized);
        }

        private static Vector3 ComputeSurfaceHitOffset(GridEntity grid, Vector3Int placementPos, Vector3Int mountAxis, RaycastHit hit)
        {
            if (grid == null) return Vector3.zero;
            float cs = grid.gridSize.CellSize();
            var hitBlock = hit.collider != null ? hit.collider.GetComponentInParent<GridBlock>() : null;
            Vector3Int surfaceCell = hitBlock != null && hitBlock.Grid == grid
                ? hitBlock.GridPos
                : placementPos - mountAxis;
            return grid.transform.InverseTransformPoint(hit.point) - new Vector3(surfaceCell.x, surfaceCell.y, surfaceCell.z) * cs;
        }

        private static float ComputeLedLateralOffset(Vector3 localDirection, Vector3Int mountAxis, Vector3 hitOffset, float cellSize, float stripWidth)
        {
            Vector3 normal = new Vector3(mountAxis.x, mountAxis.y, mountAxis.z).normalized;
            Vector3 lateral = Vector3.Cross(localDirection.normalized, normal);
            if (lateral.sqrMagnitude < 0.0001f) return 0f;
            float raw = Vector3.Dot(hitOffset, lateral.normalized);
            float edgeThreshold = cellSize * 0.24f;
            float edgeOffset = Mathf.Max(0f, cellSize * 0.5f - stripWidth * 0.5f - 0.006f);
            if (Mathf.Abs(raw) < edgeThreshold) return 0f;
            return Mathf.Sign(raw) * edgeOffset;
        }

        private static int LedCellsNeeded(Vector3Int start, Vector3Int end)
        {
            Vector3Int d = end - start;
            return Mathf.Max(1, Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z)) + 1);
        }

        private bool TryPlaceLedStrip(GridBlockItem item, GridEntity grid, Vector3Int startPos, Quaternion rotation, float length, Vector3 localOffset)
        {
            if (item == null || grid == null) return false;
            if (!grid.CanPlace(startPos)) return false;
            if (IsCellReservedByLedStrip(grid, startPos)) return false;

            GridBlock block;
            if (item.blockPrefab != null)
            {
                var go = Instantiate(item.blockPrefab);
                go.transform.rotation = rotation;
                block = go.GetComponent<GridBlock>();
                if (block == null) block = go.AddComponent<GridBlock>();
            }
            else
            {
                block = GridBlock.CreateBlock<GridBlock>("LED Strip", item.gridSize, item.iconTint);
                block.transform.rotation = rotation;
            }

            block.blockName = item.displayName;
            block.BlockMass = item.blockMass;
            block.maxHP = item.blockHP;
            block.SourceItem = item;
            var strip = block.GetComponentInChildren<VoxelEngine.Simulation.LEDStrip>(true);
            if (strip != null)
            {
                strip.SetStretch(length, localOffset);
                strip.wattsDraw = Mathf.Max(strip.wattsDraw, Mathf.Lerp(5f, 20f, Mathf.Clamp01(length / 10f)));
            }

            grid.AddBlock(startPos, block);
            VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(item.displayName, item, Mathf.Max(1, _lastLedStripCost));
            return true;
        }

        private bool IsLedStripPathClear(GridEntity grid, Vector3Int start, Vector3Int end)
        {
            if (grid == null) return false;
            Vector3Int d = end - start;
            int steps = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
            Vector3Int step = steps <= 0 ? Vector3Int.zero : new Vector3Int(
                d.x == 0 ? 0 : d.x / Mathf.Abs(d.x),
                d.y == 0 ? 0 : d.y / Mathf.Abs(d.y),
                d.z == 0 ? 0 : d.z / Mathf.Abs(d.z));

            for (int i = 0; i <= steps; i++)
            {
                Vector3Int p = start + step * i;
                if (!grid.CanPlace(p)) return false;
                if (IsCellReservedByLedStrip(grid, p)) return false;
            }
            return true;
        }

        private bool IsCellReservedByLedStrip(GridEntity grid, Vector3Int gridPos)
        {
            if (grid == null) return false;
            float cellSize = grid.gridSize.CellSize();
            foreach (var kv in grid.Blocks)
            {
                var block = kv.Value;
                if (block == null) continue;
                var strip = block.GetComponent<VoxelEngine.Simulation.LEDStrip>();
                if (strip == null) continue;
                if (strip.CoversGridCell(gridPos, cellSize)) return true;
            }
            return false;
        }

        private enum MaritimePortSnapKind
        {
            None,
            Exhaust,
            ShaftDriven
        }

        private bool IsTurbochargerItem(GridBlockItem item, out TurboTier tier)
        {
            tier = TurboTier.Small;
            if (item == null) return false;
            if (item.blockPrefab != null)
            {
                var turbo = item.blockPrefab.GetComponent<GridTurbocharger>();
                if (turbo == null) turbo = item.blockPrefab.GetComponentInChildren<GridTurbocharger>(true);
                if (turbo != null) { tier = turbo.tier; return true; }
            }
            string id = (item.itemId ?? string.Empty).ToLowerInvariant();
            string display = (item.displayName ?? string.Empty).ToLowerInvariant();
            if (!id.Contains("turbocharger") && !display.Contains("turbocharger")) return false;
            tier = id.Contains("large") || display.Contains("large") ? TurboTier.Large : TurboTier.Small;
            return true;
        }

        private MaritimePortSnapKind GetMaritimePortSnapKind(GridBlockItem item)
        {
            if (item == null || item.blockPrefab == null) return MaritimePortSnapKind.None;
            if (item.blockPrefab.GetComponentInChildren<GridExhaustPipe>(true) != null)
                return MaritimePortSnapKind.Exhaust;
            if (item.blockPrefab.GetComponentInChildren<GridMaritimeEngine>(true) != null
                || item.blockPrefab.GetComponentInChildren<GridDriveShaft>(true) != null
                || item.blockPrefab.GetComponentInChildren<GridShaftHousing>(true) != null
                || item.blockPrefab.GetComponentInChildren<GridRotationTransfer>(true) != null
                || item.blockPrefab.GetComponentInChildren<GridGearbox>(true) != null
                || item.blockPrefab.GetComponentInChildren<GridMaritimeGenerator>(true) != null
                || item.blockPrefab.GetComponentInChildren<GridPropeller>(true) != null)
                return MaritimePortSnapKind.ShaftDriven;
            return MaritimePortSnapKind.None;
        }

        /// <summary>
        /// Aligns a held shaft/gearbox/generator/propeller by mating its own
        /// mechanical port directly to the clicked target port. Root placement is
        /// derived from both port offsets, so visual shafts form one clean centreline
        /// instead of sitting a cell-length outside an engine or raised above it.
        /// </summary>
        private bool TryApplyMechanicalPortSnap(GridBlockItem item, GridEntity grid, GridBlock hitBlock, RaycastHit hit,
            ref Vector3Int gridPos, ref Vector3 worldPos, ref Quaternion rotation)
        {
            if (!MaritimeMechanicalPorts.TryFindNearestPort(hitBlock, hit.point, out var targetPort)) return false;
            if (!MaritimeMechanicalPorts.TryGetPlacementPort(item, targetPort.Role,
                    out Vector3 placedPortLocalPosition, out Vector3 placedPortLocalOutward, out _))
                return false;

            Vector3 desiredPlacedOutward = -targetPort.WorldOutward;
            rotation = MaritimeMechanicalPorts.BuildAttachmentRotation(
                placedPortLocalOutward,
                desiredPlacedOutward,
                grid.transform.up);

            // A mechanical mate fixes the port direction, not the machine's roll.
            // Let every existing build-rotation gesture twist the snapped block around
            // the shared shaft axis so a gearbox/engine can be turned right-side-up
            // without breaking the port-to-port seal.
            int twistSteps = (_rotSteps.x + _rotSteps.y + _rotSteps.z) & 3;
            if (twistSteps != 0)
                rotation = Quaternion.AngleAxis(twistSteps * 90f, desiredPlacedOutward) * rotation;
            worldPos = targetPort.WorldPosition - rotation * placedPortLocalPosition;

            Vector3Int outwardAxis = MaritimeMechanicalPorts.SnapToCardinalAxis(
                grid.transform.InverseTransformDirection(targetPort.WorldOutward));
            Vector3Int snappedCell = grid.WorldToGrid(worldPos);
            if (snappedCell == hitBlock.GridPos && outwardAxis != Vector3Int.zero)
                snappedCell = hitBlock.GridPos + outwardAxis;
            if (!grid.CanPlace(snappedCell))
            {
                if (outwardAxis == Vector3Int.zero) return false;
                snappedCell = hitBlock.GridPos + outwardAxis;
                if (!grid.CanPlace(snappedCell)) return false;
            }

            gridPos = snappedCell;
            return true;
        }

        private bool TryApplyMaritimePortSnap(GridBlockItem item, GridEntity grid, GridBlock hitBlock, RaycastHit hit,
            ref Vector3Int gridPos, ref Vector3 worldPos, ref Quaternion rotation)
        {
            if (item == null || grid == null || hitBlock == null) return false;
            MaritimePortSnapKind kind = GetMaritimePortSnapKind(item);
            if (kind == MaritimePortSnapKind.None) return false;

            if (kind == MaritimePortSnapKind.ShaftDriven)
                return TryApplyMechanicalPortSnap(item, grid, hitBlock, hit, ref gridPos, ref worldPos, ref rotation);

            // Exhaust pipes retain their existing single-port snap path.
            if (!TryFindNearestNamedPort(hitBlock.transform, MaritimePorts.ExhaustOutputPrefixes, hit.point, out var port))
            {
                var gasPipe = hitBlock.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true);
                if (gasPipe == null) gasPipe = hitBlock.GetComponentInParent<VoxelEngine.Gas.GasPipe>();
                if (gasPipe == null) return false;
                port = gasPipe.transform;
            }

            float cs = grid.gridSize.CellSize();
            Vector3 fallbackAxisWorld = hitBlock.transform.TransformDirection(
                (Vector3)SnapToCardinalAxis(hitBlock.transform.InverseTransformPoint(port.position)));
            Vector3 outWorld = MaritimePorts.PortOutwardWorld(port, fallbackAxisWorld);
            Vector3 gridLocalPort = grid.transform.InverseTransformPoint(port.position);
            Vector3 outGridLocal = grid.transform.InverseTransformDirection(outWorld);
            Vector3Int outAxis = SnapToCardinalAxis(outGridLocal);
            Vector3Int snappedCell = RoundHalfUp((gridLocalPort + (Vector3)outAxis * (cs * 0.55f)) / cs);
            if (outAxis != Vector3Int.zero && snappedCell == hitBlock.GridPos)
                snappedCell = hitBlock.GridPos + outAxis;
            if (!grid.CanPlace(snappedCell)) return false;

            gridPos = snappedCell;
            worldPos = port.position;
            Vector3 upAxis = Mathf.Abs(Vector3.Dot(outWorld, grid.transform.up)) > 0.95f
                ? grid.transform.forward
                : grid.transform.up;
            rotation = Quaternion.LookRotation(outWorld, upAxis.normalized);
            return true;
        }

        private bool TryFindNearestNamedPort(Transform root, string[] names, Vector3 hitPoint, out Transform port)
        {
            port = null;
            if (root == null || names == null || names.Length == 0) return false;
            float best = float.MaxValue;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == root) continue;
                string childName = child.name;
                for (int i = 0; i < names.Length; i++)
                {
                    if (!childName.StartsWith(names[i], System.StringComparison.Ordinal)) continue;
                    float d = (child.position - hitPoint).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        port = child;
                    }
                    break;
                }
            }
            return port != null;
        }

        private static Vector3Int SnapToCardinalAxis(Vector3 localOffset)
        {
            if (localOffset.sqrMagnitude < 0.0001f) return Vector3Int.zero;
            float ax = Mathf.Abs(localOffset.x);
            float ay = Mathf.Abs(localOffset.y);
            float az = Mathf.Abs(localOffset.z);
            if (ax >= ay && ax >= az) return localOffset.x >= 0f ? Vector3Int.right : Vector3Int.left;
            if (ay >= ax && ay >= az) return localOffset.y >= 0f ? Vector3Int.up : Vector3Int.down;
            return localOffset.z >= 0f ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
        }

        private bool TryFindTurboAttachment(GridEntity grid, Vector3Int gridPos,
            TurboTier turboTier, out GridMaritimeEngine engine)
        {
            engine = null;
            if (grid == null || !grid.CanPlace(gridPos)) return false;
            foreach (var kv in grid.Blocks)
                if (kv.Value is GridMaritimeEngine candidate && candidate.CanAttachTurboAt(gridPos, turboTier))
                { engine = candidate; return true; }
            return false;
        }

        private bool TrySnapTurboToEngine(GridBlockItem item, GridEntity grid, RaycastHit hit,
            out Vector3Int turboGridPos, out GridMaritimeEngine engine)
        {
            turboGridPos = default;
            engine = null;
            if (item == null || grid == null || hit.collider == null) return false;
            if (!IsTurbochargerItem(item, out var turboTier)) return false;
            var hitBlock = hit.collider.GetComponentInParent<GridBlock>();
            if (hitBlock == null || hitBlock.Grid != grid) return false;

            GridMaritimeEngine targetEngine = null;
            if (hitBlock is GridMaritimeEngine eng)
                targetEngine = eng;
            else
            {
                foreach (var off in Neighbours6)
                {
                    var nb = grid.GetBlock(hitBlock.GridPos + off);
                    if (nb is GridMaritimeEngine candidate)
                    { targetEngine = candidate; break; }
                }
            }

            if (targetEngine == null) return false;
            if (!GridMaritimeEngine.IsTurboTierCompatible(targetEngine.tier, turboTier))
                return false;

            int maxSlots = targetEngine.MaxTurboSlots;
            for (int i = 0; i < maxSlots; i++)
            {
                var localOffset = targetEngine.GetTurboAttachmentLocalOffset(i);
                var worldOffset = targetEngine.TransformLocalSlotOffsetToGrid(localOffset);
                var slotPos = targetEngine.GridPos + worldOffset;
                if (grid.CanPlace(slotPos))
                {
                    turboGridPos = slotPos;
                    engine = targetEngine;
                    return true;
                }
            }
            return false;
        }

        private static readonly Vector3Int[] Neighbours6 =
        {
            new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
            new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
        };

        private Quaternion GetTurboAttachmentRotation(GridEntity grid, Vector3Int turboGridPos,
            GridMaritimeEngine engine)
        {
            if (grid == null || engine == null) return Quaternion.identity;
            Vector3 engineWorld = grid.GridToWorld(engine.GridPos);
            Vector3 turboWorld = grid.GridToWorld(turboGridPos);
            Vector3 outward = turboWorld - engineWorld;
            if (outward.sqrMagnitude < 0.0001f)
                outward = grid.transform.TransformDirection(new Vector3(
                    turboGridPos.x - engine.GridPos.x, turboGridPos.y - engine.GridPos.y, turboGridPos.z - engine.GridPos.z));
            outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : grid.transform.up;
            Vector3 forward = Vector3.ProjectOnPlane(engine.transform.forward, outward);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(grid.transform.forward, outward);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.Cross(outward, grid.transform.right);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            return Quaternion.LookRotation(forward, outward);
        }

        private void HandleRotationInput()
        {
            float scroll = GridInput.Scroll;
            bool ctrl  = GridInput.Ctrl;
            bool shift = GridInput.Shift;
            if (Mathf.Abs(scroll) < 0.01f) return;
            if (!ctrl && !shift) return;
            int dir = scroll > 0 ? 1 : -1;
            if (ctrl && shift)      _rotSteps.z = (_rotSteps.z + dir + 4) % 4;
            else if (ctrl)          _rotSteps.y = (_rotSteps.y + dir + 4) % 4;
            else if (shift)         _rotSteps.x = (_rotSteps.x + dir + 4) % 4;
            RotationSteps = _rotSteps;
        }

        private GridEntity FindNearbyGrid(Vector3 worldPoint, GridSize size)
        {
            float cs = size.CellSize();
            GridEntity best = null;
            float bestDist = cs * 0.9f;
            foreach (var ge in GameObject.FindObjectsByType<GridEntity>(FindObjectsInactive.Exclude))
            {
                if (ge.gridSize != size) continue;
                var gp = ge.WorldToGrid(worldPoint);
                if (!ge.HasNeighbor(gp) && !ge.Blocks.ContainsKey(gp)) continue;
                float d = Vector3.Distance(worldPoint, ge.GridToWorld(gp));
                if (d < bestDist) { bestDist = d; best = ge; }
            }
            return best;
        }

        private void ShowGhost(GridBlockItem item, Vector3 pos, Quaternion rotation, bool valid = true)
        {
            bool isShapeItem = IsShapeVariantItem(item);
            var currentShape = VoxelEngine.UI.GridShapeWheel.CurrentShape;
            bool useShapeGhost = isShapeItem && currentShape != VoxelEngine.UI.GridShapeVariant.Cube;

            bool needsRebuild = _ghost == null || _ghostItem != item;
            if (!needsRebuild && useShapeGhost && _ghostIsShapeVariant)
            {
                string expectedName = "GridGhost_" + currentShape.ToString();
                if (_ghost == null || _ghost.name != expectedName)
                    needsRebuild = true;
            }
            if (!needsRebuild && _ghost != null && _ghostIsShapeVariant != useShapeGhost)
                needsRebuild = true;

            if (needsRebuild)
            {
                if (_ghost != null) Destroy(_ghost);
                _ghost = null;
                _ghostItem = item;
                _ghostIsShapeVariant = useShapeGhost;

                if (useShapeGhost)
                {
                    _ghost = BuildShapeGhost(currentShape, item.gridSize);
                    _ghost.name = "GridGhost_" + currentShape.ToString();
                }
                else
                {
                    if (item.blockPrefab != null)
                    {
                        _ghost = Instantiate(item.blockPrefab);
                    }
                    else
                    {
                        _ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        _ghost.transform.localScale = Vector3.one * item.gridSize.CellSize();
                    }
                    _ghost.name = "GridGhost";
                    foreach (var c in _ghost.GetComponentsInChildren<Collider>()) Destroy(c);
                    foreach (var b in _ghost.GetComponentsInChildren<GridBlock>()) Destroy(b);
                }
                BuildGhostMaterial();
            }

            if (_ghost == null) return;
            BuildGhostMaterial();
            ApplyGhostMaterialToRenderers(valid);

            _ghost.SetActive(true);
            _ghost.transform.position = pos;
            _ghost.transform.rotation = rotation;
        }

        private GameObject BuildShapeGhost(VoxelEngine.UI.GridShapeVariant shape, GridSize size)
        {
            var ghost = new GameObject("GridGhostShape");
            var shapeVisual = ghost.AddComponent<GridShapeVariantBlock>();
            shapeVisual.Configure(shape, size, createCollider: false);
            return ghost;
        }

        private void BuildGhostMaterial()
        {
            if (_ghostMat != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _ghostMat = new Material(shader) { color = ghostColor };
            if (_ghostMat.HasProperty("_BaseColor")) _ghostMat.SetColor("_BaseColor", ghostColor);
            _ghostMat.SetOverrideTag("RenderType", "Transparent");
            if (_ghostMat.HasProperty("_SrcBlend")) _ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_ghostMat.HasProperty("_DstBlend")) _ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (_ghostMat.HasProperty("_ZWrite"))  _ghostMat.SetInt("_ZWrite", 0);
            if (_ghostMat.HasProperty("_Surface")) _ghostMat.SetFloat("_Surface", 1);
            _ghostMat.renderQueue = 3100;
        }

        private void ApplyGhostMaterialToRenderers(bool valid = true)
        {
            if (_ghost == null || _ghostMat == null) return;
            Color c = valid ? ghostColor : new Color(1f, 0.3f, 0.3f, 0.35f);
            if (_ghostMat.HasProperty("_BaseColor")) _ghostMat.SetColor("_BaseColor", c);
            _ghostMat.color = c;
            foreach (var r in _ghost.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterial = _ghostMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void ShowLedStretchGhost(Vector3 startWorld, float length, Quaternion rotation, GridSize size, Vector3 localOffset)
        {
            if (_ledStretchGhost == null)
            {
                _ledStretchGhost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _ledStretchGhost.name = "LEDStripStretchGhost";
                var col = _ledStretchGhost.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            if (_ledStretchGhostMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _ledStretchGhostMat = new Material(shader) { name = "LEDStripStretchGhost_Mat", color = new Color(0.18f, 0.72f, 0.88f, 0.38f) };
                if (_ledStretchGhostMat.HasProperty("_BaseColor")) _ledStretchGhostMat.SetColor("_BaseColor", new Color(0.18f, 0.72f, 0.88f, 0.38f));
                if (_ledStretchGhostMat.HasProperty("_EmissionColor")) _ledStretchGhostMat.SetColor("_EmissionColor", new Color(0.18f, 0.72f, 0.88f) * 0.9f);
                _ledStretchGhostMat.EnableKeyword("_EMISSION");
                if (_ledStretchGhostMat.HasProperty("_Surface")) _ledStretchGhostMat.SetFloat("_Surface", 1f);
                if (_ledStretchGhostMat.HasProperty("_ZWrite")) _ledStretchGhostMat.SetInt("_ZWrite", 0);
                _ledStretchGhostMat.renderQueue = 3100;
            }

            float width = size == GridSize.Large ? 0.18f : 0.045f;
            _ledStretchGhost.SetActive(true);
            _ledStretchGhost.transform.position = startWorld + rotation * (localOffset + new Vector3(0f, width * 0.5f, 0f));
            _ledStretchGhost.transform.rotation = rotation;
            _ledStretchGhost.transform.localScale = new Vector3(length, width, width);
            var renderer = _ledStretchGhost.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = _ledStretchGhostMat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void HideLedStretchGhost()
        {
            if (_ledStretchGhost != null) _ledStretchGhost.SetActive(false);
        }

        private void CancelLedStretch(bool showFeedback)
        {
            if (showFeedback && _ledStretchArmed)
                VoxelEngine.UI.BuildFeedbackHud.Show("LED Strip", "Corner placement cancelled", null, Color.gray);
            _ledStretchArmed = false;
            _ledStretchGrid = null;
            _ledStretchItem = null;
            HideLedStretchGhost();
        }

        private void HideGhost()
        {
            if (_ghost != null) _ghost.SetActive(false);
        }
    }
}
