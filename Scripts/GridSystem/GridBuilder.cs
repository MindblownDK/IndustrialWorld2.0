// Assets/Scripts/VoxelEngine/GridSystem/GridBuilder.cs
//
// GridBuilder with grid size selection when creating a new ship.

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
        public GridSize defaultGridSize = GridSize.Large;   // Player can change this

        private GameObject _ghost;
        private GridBlockItem _ghostItem;   // which item the current ghost was built from
        private Material _ghostMat;

        // Local-space rotation the player has dialled in for the next block.
        private Vector3Int _rotSteps; // 90° steps around x,y,z

        /// <summary>True while the player is holding a grid block (build mode). Player
        /// movement uses this to suppress Ctrl-fly-down so Ctrl+Scroll can rotate
        /// blocks without the player sinking.</summary>
        public static bool HoldingGridBlock { get; private set; }

        /// <summary>Display name of the grid block currently in hand (for the rotation HUD).</summary>
        public static string HeldBlockName { get; private set; } = "";

        /// <summary>Current 90-degree rotation steps (pitch/yaw/roll) shown by the rotation HUD.</summary>
        public static Vector3Int RotationSteps { get; private set; }

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

            // SIZE RULE: a small block may attach to a large grid (detail building),
            // but a large block may NOT be placed onto a small grid.
            bool blockedBySizeRule = targetGrid != null
                && targetGrid.gridSize == GridSize.Small
                && gbi.gridSize == GridSize.Large;
            if (blockedBySizeRule) { HideGhost(); return; }

            // Only attach to a grid of the SAME size (a true sub-grid). Aiming at a
            // large grid with a small block starts a NEW small grid latched to it.
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
                // CONNECTION RULE: the target cell must touch an existing block, so
                // you can't latch a block floating near (but not joined to) the grid.
                if (!attachGrid.HasNeighbor(gridPos)) { HideGhost(); return; }

                targetGrid = attachGrid;
                worldPos = attachGrid.GridToWorld(gridPos);
                rotation = attachGrid.transform.rotation;
            }
            else
            {
                // Brand-new grid — orient to the planet surface so the first block sits
                // flush with the local ground and the grid's +Y points away from the core.
                // IMPORTANT: respect the item's OWN grid size — never mutate the shared
                // ScriptableObject (that corrupted the asset and caused size mismatches).
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
                // Apply the player's dialled-in rotation on top of the grid alignment.
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
                if (turbo != null)
                {
                    tier = turbo.tier;
                    return true;
                }
            }

            string id = (item.itemId ?? string.Empty).ToLowerInvariant();
            string display = (item.displayName ?? string.Empty).ToLowerInvariant();
            if (!id.Contains("turbocharger") && !display.Contains("turbocharger")) return false;

            tier = id.Contains("large") || display.Contains("large")
                ? VoxelEngine.Maritime.TurboTier.Large
                : VoxelEngine.Maritime.TurboTier.Small;
            return true;
        }

        private bool TryFindTurboAttachment(GridEntity grid, Vector3Int gridPos,
            VoxelEngine.Maritime.TurboTier turboTier, out VoxelEngine.Maritime.GridMaritimeEngine engine)
        {
            engine = null;
            if (grid == null || !grid.CanPlace(gridPos)) return false;

            foreach (var kv in grid.Blocks)
            {
                if (kv.Value is VoxelEngine.Maritime.GridMaritimeEngine candidate &&
                    candidate.CanAttachTurboAt(gridPos, turboTier))
                {
                    engine = candidate;
                    return true;
                }
            }

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
                    turboGridPos.x - engine.GridPos.x,
                    turboGridPos.y - engine.GridPos.y,
                    turboGridPos.z - engine.GridPos.z));
            outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : grid.transform.up;

            Vector3 forward = Vector3.ProjectOnPlane(engine.transform.forward, outward);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(grid.transform.forward, outward);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.Cross(outward, grid.transform.right);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

            // Turbo local +Y points away from the engine, so its local bottom (-Y)
            // is always pressed against the engine's attachment cube.
            return Quaternion.LookRotation(forward, outward);
        }

        // Ctrl+Scroll = yaw (Y), Shift+Scroll = pitch (X), Ctrl+Shift+Scroll = roll (Z).
        private void HandleRotationInput()
        {
            float scroll = GridInput.Scroll;
            bool ctrl  = GridInput.Ctrl;
            bool shift = GridInput.Shift;
            if (Mathf.Abs(scroll) < 0.01f) return;
            if (!ctrl && !shift) return;   // plain scroll = hotbar, leave it alone

            int dir = scroll > 0 ? 1 : -1;
            if (ctrl && shift)      _rotSteps.z = (_rotSteps.z + dir + 4) % 4; // roll
            else if (ctrl)          _rotSteps.y = (_rotSteps.y + dir + 4) % 4; // yaw
            else if (shift)         _rotSteps.x = (_rotSteps.x + dir + 4) % 4; // pitch
            RotationSteps = _rotSteps;
        }

        // Find a grid of the given size whose nearest cell is within ~1 cell of the
        // aim point, so blocks attach to an existing ship even when aiming just past it.
        private GridEntity FindNearbyGrid(Vector3 worldPoint, GridSize size)
        {
            float cs = size.CellSize();
            GridEntity best = null;
            float bestDist = cs * 0.9f;   // must be genuinely close to a real block
            foreach (var ge in GameObject.FindObjectsByType<GridEntity>(FindObjectsInactive.Exclude))
            {
                if (ge.gridSize != size) continue;
                var gp = ge.WorldToGrid(worldPoint);
                // Only latch if the aimed cell touches an ACTUAL placed block — the
                // grid's math is infinite, so distance alone isn't enough (that made
                // every new placement snap to a far grid and never start a new ship).
                if (!ge.HasNeighbor(gp) && !ge.Blocks.ContainsKey(gp)) continue;
                float d = Vector3.Distance(worldPoint, ge.GridToWorld(gp));
                if (d < bestDist) { bestDist = d; best = ge; }
            }
            return best;
        }

        private void ShowGhost(GridBlockItem item, Vector3 pos, Quaternion rotation)
        {
            // Rebuild the ghost from the held item's prefab so the preview LOOKS like
            // the real block (correct shape + cell-fitting size), instead of a cube.
            if (_ghost == null || _ghostItem != item)
            {
                if (_ghost != null) Destroy(_ghost);
                _ghostItem = item;

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

                // FUTURE: respect VoxelEngine.UI.GridShapeWheel.CurrentShape here
                // and swap the visual mesh / scale children when shape variants are
                // authored via Voxel Engine Setup (Step 18). CurrentShape is already
                // exposed for this purpose.
            }

                // Strip colliders + any block behaviour so the ghost is purely visual.
                foreach (var c in _ghost.GetComponentsInChildren<Collider>()) Destroy(c);
                foreach (var b in _ghost.GetComponentsInChildren<GridBlock>()) Destroy(b);

                BuildGhostMaterial();
                foreach (var r in _ghost.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial = _ghostMat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            _ghost.SetActive(true);
            _ghost.transform.position = pos;
            _ghost.transform.rotation = rotation;
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
            if (_ghostMat.HasProperty("_Surface")) _ghostMat.SetFloat("_Surface", 1); // URP transparent
            _ghostMat.renderQueue = 3100;
        }

        private void HideGhost()
        {
            if (_ghost != null) _ghost.SetActive(false);
        }
    }
}