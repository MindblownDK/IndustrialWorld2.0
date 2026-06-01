// Assets/Scripts/VoxelEngine/Building/BuildSystem.cs
//
// Handles ghost preview + placement. Singleton attached to the player.
// Toggle grid-snap with the BuildToggleGrid keybind (default G).

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Settings;
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
        private float _ghostYaw = 0f;

        // Runtime
        private GameObject _ghost;
        private BlockItem  _ghostItem;
        private Material   _ghostMaterialValid;
        private Material   _ghostMaterialInvalid;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            // Create translucent ghost materials.
            _ghostMaterialValid   = MakeGhostMaterial(new Color(0.4f, 0.9f, 0.5f, ghostAlpha));
            _ghostMaterialInvalid = MakeGhostMaterial(new Color(0.95f, 0.35f, 0.3f, ghostAlpha));
        }

        private void Update()
        {
            if (VoxelEngine.UI.UIState.IsBlocking) { HideGhost(); return; }

            // Toggle grid mode.
            if (GameSettings.WasPressed(InputAction.BuildToggleGrid))
                gridSnap = !gridSnap;

            // Rotate ghost only while holding LeftCtrl — otherwise the wheel scrolls the hotbar.
            bool ctrlHeld = false;
#if ENABLE_INPUT_SYSTEM
            ctrlHeld = UnityEngine.InputSystem.Keyboard.current != null
                       && UnityEngine.InputSystem.Keyboard.current.leftCtrlKey.isPressed;
            float wheel = UnityEngine.InputSystem.Mouse.current != null
                ? UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y : 0f;
#else
            ctrlHeld = Input.GetKey(KeyCode.LeftControl);
            float wheel = Input.mouseScrollDelta.y;
#endif
            if (ctrlHeld && Mathf.Abs(wheel) > 0.01f && _ghost != null)
                _ghostYaw += Mathf.Sign(wheel) * yawStep;

            UpdateGhost();
        }

        // ---------- Ghost ----------
        private void UpdateGhost()
        {
            if (inventory == null) { HideGhost(); return; }
            var stack = inventory.ActiveStack;
            if (stack.IsEmpty || !(stack.item is BlockItem block) || block.placedPrefab == null)
            {
                HideGhost();
                return;
            }
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
                return;
            }
            _ghost.SetActive(true);

            Vector3 pos = ComputePlacementPosition(hit, block);
            Quaternion rot = Quaternion.Euler(0, _ghostYaw, 0);
            _ghost.transform.SetPositionAndRotation(pos, rot);

            bool valid = IsPlacementValid(pos, block);
            ApplyGhostMaterial(_ghost, valid ? _ghostMaterialValid : _ghostMaterialInvalid);
        }

        private void HideGhost()
        {
            if (_ghost != null) { Destroy(_ghost); _ghost = null; _ghostItem = null; }
        }

        public bool TryPlace(BlockItem block, RaycastHit hit, Vector3 viewDir)
        {
            Vector3 pos = ComputePlacementPosition(hit, block);
            if (!IsPlacementValid(pos, block)) return false;

            var go = Instantiate(block.placedPrefab, pos, Quaternion.Euler(0, _ghostYaw, 0));
            go.name = block.displayName;

            // Make sure it has a collider for future raycasts.
            if (go.GetComponentInChildren<Collider>() == null)
                go.AddComponent<BoxCollider>();

            var pb = go.AddComponent<PlacedBlock>();
            pb.Item   = block;
            pb.Hp     = block.blockHealth;
            pb.onGrid = gridSnap;

            // Apply optional texture/material override at runtime.
            if (block.placedMaterial != null || block.texture != null)
            {
                var tex = go.AddComponent<BlockTexturizer>();
                tex.overrideMaterial = block.placedMaterial;
                tex.overrideTexture  = block.texture;
            }
            return true;
        }

        // ---------- Placement math ----------
        private Vector3 ComputePlacementPosition(RaycastHit hit, BlockItem block)
        {
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
