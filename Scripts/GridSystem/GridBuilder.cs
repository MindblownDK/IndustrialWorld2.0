// Assets/Scripts/VoxelEngine/GridSystem/GridBuilder.cs
//
// GridBuilder with grid size selection when creating a new ship.
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

        private Vector3Int _rotSteps;

        public static bool HoldingGridBlock { get; private set; }
        public static string HeldBlockName { get; private set; } = "";
        public static Vector3Int RotationSteps { get; private set; }

        /// <summary>Whether the held item qualifies for shape variants (armour/structural blocks).</summary>
        private static bool IsShapeVariantItem(GridBlockItem item)
        {
            if (item == null) return false;
            string name = (item.displayName ?? "").ToLowerInvariant();
            return name.Contains("armor") || name.Contains("plate") || name.Contains("block") || name.Contains("wall");
        }

        private void Start()
        {
            if (buildCamera == null) buildCamera = Camera.main;
            if (inventory == null) inventory = GetComponentInParent<Inventory>();
        }

        private void Update()
        {
            if (VoxelEngine.UI.UIState.IsBlocking) { HoldingGridBlock = false; HideGhost(); return; }
            if (inventory == null) { HoldingGridBlock = false; return; }

            var stack = inventory.ActiveStack;
            if (stack.IsEmpty || !(stack.item is GridBlockItem gbi))
            {
                HoldingGridBlock = false;
                HideGhost();
                return;
            }

            HoldingGridBlock = true;
            HeldBlockName = gbi.displayName;
            HandleRotationInput();

            var ray = buildCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (!Physics.Raycast(ray, out var hit, reach))
            {
                HideGhost();
                return;
            }

            GridEntity targetGrid = hit.collider.GetComponentInParent<GridEntity>();
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
                Vector3 planetUp = GravityProvider.GetUp(hit.point);
                Vector3 raw = hit.point + planetUp * (cs * 0.5f);
                worldPos = new Vector3(
                    Mathf.Round(raw.x / cs) * cs,
                    Mathf.Round(raw.y / cs) * cs,
                    Mathf.Round(raw.z / cs) * cs);
                gridPos = Vector3Int.zero;
                targetGrid = null;
                rotation = GravityProvider.GetSurfaceRotation(worldPos);
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
            else
            {
                rotation *= Quaternion.Euler(_rotSteps.x * 90f, _rotSteps.y * 90f, _rotSteps.z * 90f);
            }

            ShowGhost(gbi, worldPos, rotation);

            if (GameSettings.WasPressed(InputAction.Build) && TryPlaceBlock(gbi, targetGrid, gridPos, worldPos, rotation))
            {
                inventory.container.Remove(gbi, 1);
            }
        }

        private bool TryPlaceBlock(GridBlockItem item, GridEntity grid, Vector3Int gridPos, Vector3 worldPos, Quaternion rotation)
        {
            if (IsTurbochargerItem(item, out var turboTier))
            {
                if (!TryFindTurboAttachment(grid, gridPos, turboTier, out var engine))
                    return false;
                rotation = GetTurboAttachmentRotation(grid, gridPos, engine);
            }

            if (grid == null)
            {
                grid = GridEntity.Create(worldPos, item.gridSize);
                grid.transform.rotation = rotation;
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
            grid.AddBlock(gridPos, block);

            VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(item.displayName, item, 1);
            return true;
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
            float cs = size.CellSize();
            var ghost = new GameObject("GridGhostShape");

            // Helper to add a cube primitive
            void AddPrim(Vector3 pos, Vector3 scale)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(ghost.transform, false);
                cube.transform.localPosition = pos;
                cube.transform.localScale = scale;
                var col = cube.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }

            switch (shape)
            {
                case VoxelEngine.UI.GridShapeVariant.Cube:
                    AddPrim(Vector3.zero, Vector3.one * cs);
                    break;

                case VoxelEngine.UI.GridShapeVariant.Slope:
                {
                    // Stair-step ascending along Z
                    float stepH = cs * 0.25f;
                    float stepD = cs * 0.25f;
                    for (int i = 0; i < 4; i++)
                    {
                        float zPos = -cs * 0.375f + i * stepD;
                        float yPos = i * stepH + stepH * 0.5f;
                        AddPrim(new Vector3(0f, yPos, zPos),
                            new Vector3(cs, stepH, stepD));
                    }
                    break;
                }

                case VoxelEngine.UI.GridShapeVariant.HalfBlock:
                {
                    float halfH = cs * 0.5f;
                    AddPrim(new Vector3(0f, halfH * 0.5f, 0f),
                        new Vector3(cs, halfH, cs));
                    break;
                }

                case VoxelEngine.UI.GridShapeVariant.HalfSlope:
                {
                    // Half-height stair-step
                    float stepH = cs * 0.125f;
                    float stepD = cs * 0.25f;
                    for (int i = 0; i < 4; i++)
                    {
                        float zPos = -cs * 0.375f + i * stepD;
                        float yPos = i * stepH + stepH * 0.5f;
                        AddPrim(new Vector3(0f, yPos, zPos),
                            new Vector3(cs, stepH, stepD));
                    }
                    break;
                }

                case VoxelEngine.UI.GridShapeVariant.Corner:
                {
                    // Two opposite quadrants forming a corner check pattern
                    // Block A: right-rear quadrant
                    AddPrim(new Vector3(cs * 0.25f, 0f, -cs * 0.25f),
                        new Vector3(cs * 0.48f, cs, cs * 0.48f));
                    // Block B: left-front quadrant
                    AddPrim(new Vector3(-cs * 0.25f, 0f, cs * 0.25f),
                        new Vector3(cs * 0.48f, cs, cs * 0.48f));
                    break;
                }

                case VoxelEngine.UI.GridShapeVariant.InvertedSlope:
                {
                    // Stair-step descending along Z
                    float stepH = cs * 0.25f;
                    float stepD = cs * 0.25f;
                    for (int i = 0; i < 4; i++)
                    {
                        float zPos = -cs * 0.375f + i * stepD;
                        float yPos = (3 - i) * stepH + stepH * 0.5f;
                        AddPrim(new Vector3(0f, yPos, zPos),
                            new Vector3(cs, stepH, stepD));
                    }
                    break;
                }
            }

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

        private void HideGhost()
        {
            if (_ghost != null) _ghost.SetActive(false);
        }
    }
}
