// Assets/Scripts/VoxelEngine/GridSystem/GridBuilder.cs
//
// GridBuilder with grid size selection when creating a new ship.
// v5.62.3-dev — Door top-face edge placement stands upright instead of lying flat.
// v5.40.0-dev — Ghost properly shows shape variants as primitive shapes
// (stair-step slopes, L-corner, half blocks) only for armour/structural
// blocks when the wheel variant is non-Cube. All other grid blocks show
// their normal prefab ghost. No prefab children are ever modified.

using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.GridSystem
{
    public class GridBuilder : MonoBehaviour
    {
        [Header("Refs")]
        public Camera buildCamera;
        public Inventory inventory;
        public float reach = 8f;

        [Header("Ghost")]
        public Color ghostColor = new Color(0.3f, 0.8f, 1f, 0.3f);

        [Header("Grid Size Selection")]
        public GridSize defaultGridSize = GridSize.Large;

        private GameObject _ghost;
        private GridBlockItem _ghostItem;
        private Material _ghostMat;
        private bool _ghostIsShapeVariant;
        private GridPrecisionLatticePreview _precisionLattice;

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

        /// <summary>Whether the held item qualifies for shape variants (armour/structural blocks).</summary>
        private static bool IsShapeVariantItem(GridBlockItem item)
        {
            return item != null && item.SupportsShapeVariants;
        }

        private void Start()
        {
            if (buildCamera == null) buildCamera = Camera.main;
            if (inventory == null) inventory = GetComponentInParent<Inventory>();
        }

        private void Update()
        {
            if (VoxelEngine.UI.UIState.IsBlocking) { HoldingGridBlock = false; HideGhost(); HidePrecisionLattice(); return; }
            if (inventory == null) { HoldingGridBlock = false; HidePrecisionLattice(); return; }

            var stack = inventory.ActiveStack;
            if (stack.IsEmpty || !(stack.item is GridBlockItem gbi))
            {
                HoldingGridBlock = false;
                HideGhost();
                HidePrecisionLattice();
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
            if (!Physics.Raycast(ray, out var hit, reach))
            {
                HideGhost();
                HideLedStretchGhost();
                HidePrecisionLattice();
                return;
            }

            GridEntity targetGrid = hit.collider.GetComponentInParent<GridEntity>();
            if (targetGrid != null
                && targetGrid.gridSize == GridSize.Large
                && gbi.gridSize == GridSize.Small)
            {
                HandlePrecisionAttachment(gbi, targetGrid, hit);
                return;
            }
            HidePrecisionLattice();
            Vector3Int gridPos;
            Vector3 worldPos;
            Quaternion rotation = Quaternion.identity;

            bool blockedBySizeRule = targetGrid != null
                && targetGrid.gridSize == GridSize.Small
                && gbi.gridSize == GridSize.Large;
            if (blockedBySizeRule) { HideGhost(); return; }

            GridEntity attachGrid = (targetGrid != null && targetGrid.gridSize == gbi.gridSize)
                ? targetGrid : FindNearbyGrid(hit.point, gbi.gridSize);

            if (attachGrid != null)
            {
                float cs = gbi.gridSize.CellSize();
                Vector3 probe = hit.point + hit.normal * (cs * 0.5f);
                gridPos = attachGrid.WorldToGrid(probe);
                if (!attachGrid.CanPlace(gridPos))
                {
                    gridPos = attachGrid.WorldToGrid(hit.point + hit.normal * (cs * 1.0f));
                    if (!attachGrid.CanPlace(gridPos)) { HideGhost(); return; }
                }
                if (!attachGrid.HasNeighbor(gridPos)) { HideGhost(); return; }

                targetGrid = attachGrid;
                worldPos = attachGrid.GridToWorld(gridPos);
                rotation = attachGrid.transform.rotation;
            }
            else
            {
                float cs = gbi.gridSize.CellSize();

                // Spherical planet: snap position along the planet's tangent plane
                // so the grid follows the curvature instead of snapping to world axes.
                Vector3 planetCenter = GravityProvider.ActiveBody.transform.position;
                Vector3 toPoint = hit.point - planetCenter;
                float altitude = toPoint.magnitude;
                Vector3 up = toPoint.normalized;

                // Use Ceil to always round AWAY from the planet center, preventing
                // blocks from being placed inside terrain voxels. Full cell offset
                // ensures the block sits cleanly on the surface.
                float snappedAlt = Mathf.Ceil(altitude / cs) * cs + cs * 0.5f;

                // Build a tangent-plane frame at this surface point.
                Vector3 forward = Vector3.Cross(up, Vector3.right);
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.Cross(up, Vector3.forward);
                forward = forward.normalized;
                Vector3 right = Vector3.Cross(forward, up).normalized;

                // Project the hit point onto the tangent plane (relative to planet center).
                Vector3 tangentOffset = hit.point - planetCenter - up * Vector3.Dot(toPoint, up);
                float localX = Vector3.Dot(tangentOffset, right);
                float localZ = Vector3.Dot(tangentOffset, forward);

                // Snap along the tangent plane.
                localX = Mathf.Round(localX / cs) * cs;
                localZ = Mathf.Round(localZ / cs) * cs;

                worldPos = planetCenter + up * snappedAlt + right * localX + forward * localZ;

                gridPos = Vector3Int.zero;
                targetGrid = null;
                rotation = BuildSurfacePlacementRotation(worldPos, hit.normal);
            }

            var targetedBlock = hit.collider != null ? hit.collider.GetComponentInParent<GridBlock>() : null;
            if (gbi.gridSize == GridSize.Large
                && targetGrid != null
                && targetedBlock != null
                && targetedBlock.IsPrecisionAttachment)
            {
                var precision = targetGrid.GetComponent<GridPrecisionAttachmentLayer>();
                if (precision == null || !precision.CanPlaceStructuralBlock(gridPos))
                {
                    HideGhost();
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

            bool placingTurbo = IsTurbochargerItem(gbi, out var turboTier);
            if (placingTurbo)
            {
                if (!TryFindTurboAttachment(targetGrid, gridPos, turboTier, out var engine))
                {
                    HideGhost();
                    return;
                }
                rotation = GetTurboAttachmentRotation(targetGrid, gridPos, engine);
            }
            else if (IsDoorItem(gbi) && targetGrid != null)
            {
                if (!TryAdjustDoorPlacementToEdge(gbi, targetGrid, hit, ref gridPos, ref worldPos, ref rotation))
                {
                    HideGhost();
                    return;
                }
            }
            else
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

            ShowGhost(gbi, worldPos, rotation);

            if (GameSettings.WasPressed(InputAction.Build) && TryPlaceBlock(gbi, targetGrid, gridPos, worldPos, rotation))
            {
                inventory.container.Remove(gbi, 1);
            }
        }

        private void HandlePrecisionAttachment(GridBlockItem item, GridEntity grid, RaycastHit hit)
        {
            if (item == null || grid == null) return;

            Vector3Int faceAxis = SnapMountAxis(grid, hit.normal);
            Vector3 localNormal = ((Vector3)faceAxis).normalized;
            var hitBlock = hit.collider != null ? hit.collider.GetComponentInParent<GridBlock>() : null;
            bool attachingToPrecisionBlock = hitBlock != null
                && hitBlock.Grid == grid
                && hitBlock.IsPrecisionAttachment;

            Vector3Int precisionPos;
            if (attachingToPrecisionBlock)
            {
                precisionPos = hitBlock.PrecisionGridPos + faceAxis;
            }
            else
            {
                float smallCellSize = GridSize.Small.CellSize();
                Vector3 localCenter = grid.transform.InverseTransformPoint(hit.point)
                    + localNormal * (smallCellSize * 0.5f);
                precisionPos = new Vector3Int(
                    Mathf.RoundToInt(localCenter.x / smallCellSize),
                    Mathf.RoundToInt(localCenter.y / smallCellSize),
                    Mathf.RoundToInt(localCenter.z / smallCellSize));
            }

            Vector3Int largeCell = hitBlock != null && hitBlock.Grid == grid
                ? (hitBlock.IsPrecisionAttachment ? hitBlock.PrecisionHostGridPos : hitBlock.GridPos)
                : grid.WorldToGrid(hit.point - hit.normal * 0.02f);
            ShowPrecisionLattice(grid, largeCell, faceAxis);

            var layer = grid.GetComponent<GridPrecisionAttachmentLayer>();
            if (layer == null) layer = grid.gameObject.AddComponent<GridPrecisionAttachmentLayer>();

            Vector3 localPosition = (Vector3)precisionPos * GridSize.Small.CellSize();
            Vector3Int occupiedLargeCell = new(
                Mathf.RoundToInt(localPosition.x / GridSize.Large.CellSize()),
                Mathf.RoundToInt(localPosition.y / GridSize.Large.CellSize()),
                Mathf.RoundToInt(localPosition.z / GridSize.Large.CellSize()));

            bool supported = attachingToPrecisionBlock
                ? layer.HasNeighbor(precisionPos)
                : hitBlock != null && hitBlock.Grid == grid;
            // A direct face hit is already guaranteed to be on the exposed surface of
            // the clicked structural block. Only chained detail placement needs the
            // macro-cell overlap test; applying it to direct hits incorrectly rejected
            // every valid face cell and hid the ghost.
            bool overlapsLargeCell = attachingToPrecisionBlock
                && grid.GetBlock(occupiedLargeCell) != null;
            bool valid = supported
                && layer.CanPlace(precisionPos)
                && !overlapsLargeCell;

            Quaternion rotation = grid.transform.rotation
                * Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
            Vector3 worldPosition = grid.transform.TransformPoint(localPosition);

            if (valid) ShowGhost(item, worldPosition, rotation);
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

            Quaternion localRotation = Quaternion.Inverse(grid.transform.rotation) * rotation;
            if (!layer.AddBlock(precisionPos, largeCell, block, localRotation))
            {
                Destroy(block.gameObject);
                return;
            }

            inventory.container.Remove(item, 1);
            VoxelEngine.UI.BuildFeedbackHud.Show(
                "Precision Attached",
                $"{item.displayName} · 5×5 large-face lattice",
                item.icon,
                item.iconTint);
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

        private bool TryPlaceBlock(GridBlockItem item, GridEntity grid, Vector3Int gridPos, Vector3 worldPos, Quaternion rotation)
        {
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
                // Every newly created construct uses one universal host grid. Block scale
                // remains an item property, but the player never creates a separate grid type.
                grid = GridEntity.Create(worldPos, GridSize.Large);
                grid.transform.rotation = rotation;
                // Start kinematic to prevent terrain collision from tilting the grid
                // during the first physics frame. We enable physics after AddBlock.
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
            }

            VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(item.displayName, item, 1);

            // Re-enable physics after the next fixed step so terrain contacts and newly
            // added colliders are fully settled before the grid starts simulating.
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

            // Trust terrain/surface normals when they're broadly aligned with planetary up;
            // otherwise fall back to radial up so odd collider normals can't flip the grid.
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

            // If the player clicks a floor/ceiling face, choose the nearest horizontal edge
            // and place the door standing upright on that edge instead of lying flat.
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

                // Top-face clicks mount the door upright on the nearest top edge.
                // This cell is diagonal from the host cube, so it intentionally bypasses
                // normal face-neighbour attachment validation below.
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

            // Doors must stand upright. Keep their up vector aligned to grid up unless the
            // clicked normal is almost parallel to it, in which case use grid forward as fallback.
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
            Vector3 endWorld = _ledStretchGrid.GridToWorld(end);
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

        private bool IsTurbochargerItem(GridBlockItem item, out VoxelEngine.Maritime.TurboTier tier)
        {
            tier = VoxelEngine.Maritime.TurboTier.Small;
            if (item == null) return false;
            if (item.blockPrefab != null)
            {
                var turbo = item.blockPrefab.GetComponent<VoxelEngine.Maritime.GridTurbocharger>();
                if (turbo == null) turbo = item.blockPrefab.GetComponentInChildren<VoxelEngine.Maritime.GridTurbocharger>(true);
                if (turbo != null) { tier = turbo.tier; return true; }
            }
            string id = (item.itemId ?? string.Empty).ToLowerInvariant();
            string display = (item.displayName ?? string.Empty).ToLowerInvariant();
            if (!id.Contains("turbocharger") && !display.Contains("turbocharger")) return false;
            tier = id.Contains("large") || display.Contains("large") ? VoxelEngine.Maritime.TurboTier.Large : VoxelEngine.Maritime.TurboTier.Small;
            return true;
        }

        private bool TryFindTurboAttachment(GridEntity grid, Vector3Int gridPos,
            VoxelEngine.Maritime.TurboTier turboTier, out VoxelEngine.Maritime.GridMaritimeEngine engine)
        {
            engine = null;
            if (grid == null || !grid.CanPlace(gridPos)) return false;
            foreach (var kv in grid.Blocks)
                if (kv.Value is VoxelEngine.Maritime.GridMaritimeEngine candidate && candidate.CanAttachTurboAt(gridPos, turboTier))
                { engine = candidate; return true; }
            return false;
        }

        private Quaternion GetTurboAttachmentRotation(GridEntity grid, Vector3Int turboGridPos,
            VoxelEngine.Maritime.GridMaritimeEngine engine)
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

        // ── Ghost System ───────────────────────────────────────────

        /// <summary>
        /// Shows the appropriate ghost preview. For armour/structural blocks with a
        /// non-Cube shape variant selected, draws a primitive shape representation
        /// (stair-step slope, half block, L-corner, etc.). For all other blocks or
        /// when the variant is Cube, shows the normal prefab ghost.
        /// </summary>
        private void ShowGhost(GridBlockItem item, Vector3 pos, Quaternion rotation)
        {
            bool isShapeItem = IsShapeVariantItem(item);
            var currentShape = VoxelEngine.UI.GridShapeWheel.CurrentShape;
            bool useShapeGhost = isShapeItem && currentShape != VoxelEngine.UI.GridShapeVariant.Cube;

            bool needsRebuild = _ghost == null || _ghostItem != item;

            // Only rebuild shape ghost if variant changed (and we're using shape ghost mode)
            if (!needsRebuild && useShapeGhost && _ghostIsShapeVariant)
            {
                // variant stays the same? fine. variant changed? rebuild.
                // We check by seeing if the ghost is still valid for this variant.
                // Simplest: track the shape variant on the ghost name
                string expectedName = "GridGhost_" + currentShape.ToString();
                if (_ghost == null || _ghost.name != expectedName)
                    needsRebuild = true;
            }

            // If we were showing shape ghost but now should show prefab (or vice versa)
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
                    // Normal prefab ghost
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

                    // Strip colliders + block scripts
                    foreach (var c in _ghost.GetComponentsInChildren<Collider>()) Destroy(c);
                    foreach (var b in _ghost.GetComponentsInChildren<GridBlock>()) Destroy(b);
                }

                BuildGhostMaterial();
            }

            if (_ghost == null) return;

            BuildGhostMaterial();
            ApplyGhostMaterialToRenderers();

            _ghost.SetActive(true);
            _ghost.transform.position = pos;
            _ghost.transform.rotation = rotation;
        }

        /// <summary>
        /// Builds a multi-cube shape that clearly represents each variant:
        /// • Cube: single full cube
        /// • Slope: stair-step of 4 blocks ascending diagonally
        /// • HalfBlock: half-height cube
        /// • HalfSlope: half-height stair-step
        /// • Corner: L-shaped arrangement
        /// • InvertedSlope: stair-step descending diagonally
        /// All primitives, no prefab children touched.
        /// </summary>
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

        private void ApplyGhostMaterialToRenderers()
        {
            if (_ghost == null || _ghostMat == null) return;
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
