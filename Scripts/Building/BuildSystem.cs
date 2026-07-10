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

        public static bool HoldingBlock { get; private set; }
        public static string HeldBlockName { get; private set; } = string.Empty;

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
            if (VoxelEngine.UI.UIState.IsBlocking) { HoldingBlock = false; HeldBlockName = string.Empty; HideGhost(); return; }

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
                return;
            }
            _ghost.SetActive(true);

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
            if (_ghost != null) { Destroy(_ghost); _ghost = null; _ghostItem = null; } Quarry.HidePlacementPreview();
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
        }

        public bool TryPlace(BlockItem block, RaycastHit hit, Vector3 viewDir)
        {
            ComputePlacementPose(hit, block, out Vector3 pos, out Quaternion rot);
            if (!IsPlacementValid(pos, block)) return false;

            var go = Instantiate(block.placedPrefab, pos, rot);
            go.name = block.displayName;

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

            bool placingBelt = block.placedPrefab.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true) != null;
            bool placingChute = block.placedPrefab.GetComponentInChildren<VoxelEngine.Simulation.ConveyorChute>(true) != null;
            bool placingPowerPipe = block.placedPrefab.GetComponentInChildren<VoxelEngine.Power.PowerCable>(true) != null;
            if (!placingBelt && !placingChute && !placingPowerPipe) return false;

            var targetBelt = hit.collider.GetComponentInParent<VoxelEngine.Simulation.ConveyorBelt>();
            if (placingBelt && targetBelt != null)
            {
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

            var targetChute = hit.collider.GetComponentInParent<VoxelEngine.Simulation.ConveyorChute>();
            if (placingChute && targetChute != null)
            {
                pos = targetChute.transform.position + targetChute.transform.up * Mathf.Max(gridSize, 1f);
                rot = targetChute.transform.rotation;
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
