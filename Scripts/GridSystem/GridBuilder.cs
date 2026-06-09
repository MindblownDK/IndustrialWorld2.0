// Assets/Scripts/VoxelEngine/GridSystem/GridBuilder.cs
//
// GridBuilder with grid size selection when creating a new ship.

using UnityEngine;
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

        private void Start()
        {
            if (buildCamera == null) buildCamera = Camera.main;
            if (inventory == null) inventory = GetComponentInParent<Inventory>();
        }

        private void Update()
        {
            if (VoxelEngine.UI.UIState.IsBlocking) { HideGhost(); return; }
            if (inventory == null) return;

            var stack = inventory.ActiveStack;
            if (stack.IsEmpty || !(stack.item is GridBlockItem gbi))
            {
                HideGhost();
                return;
            }

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
            float cs = gbi.gridSize.CellSize();

            if (targetGrid != null && targetGrid.gridSize == gbi.gridSize)
            {
                gridPos = targetGrid.WorldToGrid(hit.point + hit.normal * cs * 0.5f);
                worldPos = targetGrid.GridToWorld(gridPos);
                rotation = targetGrid.transform.rotation;
                if (!targetGrid.CanPlace(gridPos)) { HideGhost(); return; }
            }
            else
            {
                // New grid - use selected size
                gbi.gridSize = defaultGridSize;
                cs = gbi.gridSize.CellSize();

                worldPos = new Vector3(
                    Mathf.Round(hit.point.x / cs + hit.normal.x * 0.5f) * cs,
                    Mathf.Round(hit.point.y / cs + hit.normal.y * 0.5f) * cs,
                    Mathf.Round(hit.point.z / cs + hit.normal.z * 0.5f) * cs);
                gridPos = Vector3Int.zero;
                targetGrid = null;
            }

            ShowGhost(gbi, worldPos, rotation);

            if (GameSettings.WasPressed(InputAction.Build))
            {
                PlaceBlock(gbi, targetGrid, gridPos, worldPos, rotation);
                inventory.container.Remove(gbi, 1);
            }
        }

        private void PlaceBlock(GridBlockItem item, GridEntity grid, Vector3Int gridPos, Vector3 worldPos, Quaternion rotation)
        {
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