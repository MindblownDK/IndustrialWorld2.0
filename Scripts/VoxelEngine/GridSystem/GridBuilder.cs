// Assets/Scripts/VoxelEngine/GridSystem/GridBuilder.cs
//
// Handles placing grid blocks. Ghost preview, snap to existing grids.
// All input uses Input System (no UnityEngine.Input).

using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Player;
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

        private GameObject _ghost;
        private MeshRenderer _ghostRenderer;
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
            float cs = gbi.gridSize.CellSize();

            if (targetGrid != null && targetGrid.gridSize == gbi.gridSize)
            {
                gridPos = targetGrid.WorldToGrid(hit.point + hit.normal * cs * 0.5f);
                worldPos = targetGrid.GridToWorld(gridPos);
                if (!targetGrid.CanPlace(gridPos)) { HideGhost(); return; }
            }
            else
            {
                worldPos = new Vector3(
                    Mathf.Round(hit.point.x / cs + hit.normal.x * 0.5f) * cs,
                    Mathf.Round(hit.point.y / cs + hit.normal.y * 0.5f) * cs,
                    Mathf.Round(hit.point.z / cs + hit.normal.z * 0.5f) * cs);
                gridPos = Vector3Int.zero;
                targetGrid = null;
            }

            ShowGhost(worldPos, cs);

            // Place on RMB (Build action).
            if (GameSettings.WasPressed(InputAction.Build))
            {
                PlaceBlock(gbi, targetGrid, gridPos, worldPos);
                inventory.container.Remove(gbi, 1);
            }
        }

        private void PlaceBlock(GridBlockItem item, GridEntity grid, Vector3Int gridPos, Vector3 worldPos)
        {
            if (grid == null)
            {
                grid = GridEntity.Create(worldPos, item.gridSize);
                gridPos = Vector3Int.zero;
            }

            GridBlock block;
            if (item.blockPrefab != null)
            {
                var go = Instantiate(item.blockPrefab);
                block = go.GetComponent<GridBlock>();
                if (block == null) block = go.AddComponent<GridBlock>();
            }
            else
            {
                block = GridBlock.CreateBlock<GridBlock>("Block", item.gridSize,
                    item.iconTint != default ? item.iconTint : new Color(0.5f, 0.5f, 0.55f));
            }

            block.blockName = item.displayName;
            block.BlockMass = item.blockMass;
            block.maxHP = item.blockHP;
            grid.AddBlock(gridPos, block);

            VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(item.displayName, item, 1);
        }

        private void ShowGhost(Vector3 pos, float cellSize)
        {
            if (_ghost == null)
            {
                _ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _ghost.name = "GridGhost";
                var col = _ghost.GetComponent<BoxCollider>();
                if (col != null) Destroy(col);

                _ghostRenderer = _ghost.GetComponent<MeshRenderer>();
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _ghostMat = new Material(shader);
                _ghostMat.color = ghostColor;
                if (_ghostMat.HasProperty("_BaseColor")) _ghostMat.SetColor("_BaseColor", ghostColor);
                if (_ghostMat.HasProperty("_Surface")) _ghostMat.SetFloat("_Surface", 1f);
                _ghostMat.SetOverrideTag("RenderType", "Transparent");
                _ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _ghostMat.SetInt("_ZWrite", 0);
                _ghostMat.renderQueue = 3100;
                _ghostRenderer.material = _ghostMat;
                _ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            _ghost.SetActive(true);
            _ghost.transform.position = pos;
            _ghost.transform.localScale = Vector3.one * cellSize * 0.95f;
        }

        private void HideGhost()
        {
            if (_ghost != null) _ghost.SetActive(false);
        }
    }
}
