// Assets/Scripts/VoxelEngine/Building/Tiered/BuildSystemV2.cs
//
// Tiered construction build system:
//   * Holding a "BuildToken" item (one per family) shows a Wood-tier ghost preview.
//   * Ghost snaps to the nearest BuildSocket within range; falls back to grid snap.
//   * RMB places at Wood tier, consuming definition.placeCost from the player inventory.
//   * Toggle grid-vs-free with the BuildToggleGrid keybind (default G).
//   * The Hammer tool (separate) handles upgrade / rotate / destroy.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.Building.Tiered
{
    public class BuildSystemV2 : MonoBehaviour
    {
        public static BuildSystemV2 Instance { get; private set; }

        [Header("Refs")]
        public Camera shootCamera;
        public Inventory inventory;
        public TieredBlockRegistry registry;

        [Header("Tuning")]
        public float reach = 8f;
        public float socketSnapRadius = 2.6f;     // metres around aim point to search for sockets
        public bool  gridSnap = true;
        public float gridSize = 3.0f;
        public float ghostAlpha = 0.55f;
        public float yawStep = 90f;

        // Runtime
        private GameObject _ghost;
        private TieredBlockDefinition _ghostDef;
        private float _ghostYaw;
        private Material _matValid, _matInvalid;
        private bool _ghostValid;

        private Vector3 _ghostPos;
        private Quaternion _ghostRot = Quaternion.identity;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _matValid   = MakeGhostMaterial(new Color(0.40f, 0.90f, 0.45f, ghostAlpha));
            _matInvalid = MakeGhostMaterial(new Color(0.95f, 0.30f, 0.25f, ghostAlpha));
        }

        private void Update()
        {
            if (VoxelEngine.UI.UIState.IsBlocking) { HideGhost(); return; }

            // Toggle grid mode.
            if (GameSettings.WasPressed(InputAction.BuildToggleGrid))
                gridSnap = !gridSnap;

            // Rotate ghost only with Ctrl+wheel (matches GameUIController).
            bool ctrl = false;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            ctrl = UnityEngine.InputSystem.Keyboard.current != null
                   && UnityEngine.InputSystem.Keyboard.current.leftCtrlKey.isPressed;
            float wheel = UnityEngine.InputSystem.Mouse.current != null
                ? UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y : 0f;
#else
            ctrl = Input.GetKey(KeyCode.LeftControl);
            float wheel = Input.mouseScrollDelta.y;
#endif
            if (ctrl && Mathf.Abs(wheel) > 0.01f) _ghostYaw += Mathf.Sign(wheel) * yawStep;
            if (GameSettings.WasPressed(InputAction.BuildRotate)) _ghostYaw += yawStep;

            UpdateGhost();
        }

        // ---------- Ghost / placement ----------
        private void UpdateGhost()
        {
            if (inventory == null || registry == null) { HideGhost(); return; }
            // Build mode now requires holding the Hammer AND having picked a family in the wheel.
            var stack = inventory.ActiveStack;
            bool holdingHammer = !stack.IsEmpty && stack.item is VoxelEngine.Items.ToolItem t && t.toolType == VoxelEngine.Items.ToolType.Other && stack.item.GetType().Name == "Hammer";

            BuildFamily? wheelFam = HammerBuildWheel.Instance != null ? HammerBuildWheel.Instance.ActiveFamily : null;
            // Legacy BuildToken support: if a token is held, it overrides the wheel selection.
            BuildFamily? activeFam = null;
            if (!stack.IsEmpty && stack.item is BuildToken tok) activeFam = tok.family;
            else if (holdingHammer && wheelFam.HasValue) activeFam = wheelFam.Value;

            if (activeFam == null)
            {
                HideGhost();
                return;
            }
            var def = registry.Get(activeFam.Value);
            if (def == null || def.GetPrefab(BuildTier.Wood) == null) { HideGhost(); return; }

            if (_ghost == null || _ghostDef != def)
            {
                if (_ghost != null) Destroy(_ghost);
                _ghostDef = def;
                _ghost = Instantiate(def.GetPrefab(BuildTier.Wood));
                _ghost.name = "BuildGhost";
                StripGhost(_ghost);
            }

            var ray = shootCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (!Physics.Raycast(ray, out var hit, reach))
            {
                _ghost.SetActive(false);
                return;
            }
            _ghost.SetActive(true);

            ComputeGhostTransform(hit, def);
            _ghost.transform.SetPositionAndRotation(_ghostPos, _ghostRot);
            ApplyGhostMaterial(_ghost, _ghostValid ? _matValid : _matInvalid);

            // Place on the standard build action (RMB by default).
            if (_ghostValid && GameSettings.WasPressed(InputAction.Build))
            {
                if (CanAfford(def.placeCost))
                {
                    PayCost(def.placeCost);
                    Place(def, _ghostPos, _ghostRot);
                    // placement feedback.
                    var sb = new System.Text.StringBuilder();
                    if (def.placeCost?.items != null)
                        foreach (var ing in def.placeCost.items)
                            if (ing.item != null && ing.count > 0)
                                sb.Append($"-{ing.count} {ing.item.displayName}  ");
                    VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(
                        def.displayName, def.placeCost?.items?.Length > 0 ? def.placeCost.items[0].item : null,
                        def.placeCost?.items?.Length > 0 ? def.placeCost.items[0].count : 0);
                }
            }
        }

        private void HideGhost()
        {
            if (_ghost != null) { Destroy(_ghost); _ghost = null; _ghostDef = null; }
        }

        // ---------- Snap / placement math ----------
        private void ComputeGhostTransform(RaycastHit hit, TieredBlockDefinition def)
        {
            // 1) Try socket snap: look for the nearest BuildSocket within socketSnapRadius
            //    around the hit point that accepts this family.
            BuildSocket bestSocket = null;
            float bestSqr = socketSnapRadius * socketSnapRadius;

            var hits = Physics.OverlapSphere(hit.point, socketSnapRadius);
            var visitedHosts = new HashSet<PlacedTieredBlock>();
            foreach (var col in hits)
            {
                var host = col != null ? col.GetComponentInParent<PlacedTieredBlock>() : null;
                if (host == null || host.definition == null || !visitedHosts.Add(host)) continue;

                var sockets = host.GetComponentsInChildren<BuildSocket>(true);
                foreach (var sock in sockets)
                {
                    if (sock == null || !BuildSocketCompat.AreCompatible(host.definition.family, sock.side, def.family))
                        continue;
                    float d = (sock.transform.position - hit.point).sqrMagnitude;
                    if (d < bestSqr) { bestSqr = d; bestSocket = sock; }
                }
            }

            if (bestSocket != null)
            {
                _ghostPos = bestSocket.transform.position;
                // Preserve the host/socket basis exactly. Reconstructing from world
                // Euler yaw introduced small rotational drift on spherical surfaces.
                Vector3 socketUp = bestSocket.transform.up;
                _ghostRot = Quaternion.AngleAxis(_ghostYaw, socketUp) * bestSocket.transform.rotation;
                _ghostValid = ValidateOverlap(_ghostPos, def.family);
                return;
            }

            // 2) Fall back to grid snap or free placement on the hit surface.
            // Construction roots represent the bottom/hinge plane, not the center
            // of a 2.5 m cube, so snap to grid intersections instead of cell centers.
            float surfaceOffset = def.family == BuildFamily.Foundation ? 0.40f : 0.02f;
            Vector3 raw = hit.point + hit.normal * surfaceOffset;
            _ghostPos = gridSnap
                ? new Vector3(
                    Mathf.Round(raw.x / gridSize) * gridSize,
                    Mathf.Round(raw.y / gridSize) * gridSize,
                    Mathf.Round(raw.z / gridSize) * gridSize)
                : raw;
            _ghostRot = GravityProvider.GetSurfaceRotation(_ghostPos, _ghostYaw);
            _ghostValid = ValidateOverlap(_ghostPos, def.family);
        }

        private bool ValidateOverlap(Vector3 pos, BuildFamily family)
        {
            // Don't overlap the player.
            if (Vector3.Distance(pos, transform.position) < 0.6f) return false;

            // Don't overlap dynamic rigidbodies.
            var overlaps = Physics.OverlapBox(pos, Vector3.one * 0.40f, Quaternion.identity);
            foreach (var col in overlaps)
            {
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) return false;
                // Allow stacking onto other tiered blocks (snap will handle exact alignment).
            }
            return true;
        }

        // ---------- Resource handling ----------
        private bool CanAfford(TierCost cost)
        {
            if (cost == null || cost.items == null) return true;
            foreach (var ing in cost.items)
            {
                if (ing.item == null || ing.count <= 0) continue;
                if (inventory.container.CountOf(ing.item) < ing.count) return false;
            }
            return true;
        }

        private void PayCost(TierCost cost)
        {
            if (cost == null || cost.items == null) return;
            foreach (var ing in cost.items)
            {
                if (ing.item == null || ing.count <= 0) continue;
                inventory.container.Remove(ing.item, ing.count);
            }
        }

        // ---------- Place ----------
        private void Place(TieredBlockDefinition def, Vector3 pos, Quaternion rot)
        {
            var go = Instantiate(def.GetPrefab(BuildTier.Wood), pos, rot);
            go.name = $"{def.displayName} (Wood)";
            var pb = go.GetComponent<PlacedTieredBlock>();
            if (pb == null) pb = go.AddComponent<PlacedTieredBlock>();
            pb.Initialize(def, BuildTier.Wood);
            // Satisfying placement thunk at the build location.
            VoxelEngine.FX.AudioManager.PlayAt(
                VoxelEngine.FX.SfxLibrary.Get(VoxelEngine.FX.Sfx.Place), pos,
                volume: 0.6f, pitch: UnityEngine.Random.Range(0.95f, 1.05f), maxDistance: 20f);
        }

        // ============================================================
        //                   PUBLIC API for Hammer
        // ============================================================
        public bool TryUpgrade(PlacedTieredBlock target)
        {
            if (target == null || target.definition == null) return false;
            if (target.tier == BuildTier.Steel) return false;

            BuildTier next = TieredBlockDefinition.NextTier(target.tier);
            var cost = target.definition.GetUpgradeCost(target.tier);
            if (!CanAfford(cost)) return false;
            PayCost(cost);

            // Replace the prefab in place: spawn the new tier at the same transform, copy state.
            Vector3 pos = target.transform.position;
            Quaternion rot = target.transform.rotation;
            var def = target.definition;
            Destroy(target.gameObject);

            var go = Instantiate(def.GetPrefab(next), pos, rot);
            go.name = $"{def.displayName} ({next})";
            var pb = go.GetComponent<PlacedTieredBlock>();
            if (pb == null) pb = go.AddComponent<PlacedTieredBlock>();
            pb.Initialize(def, next);
            return true;
        }

        public void Rotate(PlacedTieredBlock target, float delta)
        {
            if (target == null) return;
            Vector3 planetUp = GravityProvider.GetUp(target.transform.position);
            target.transform.rotation = Quaternion.AngleAxis(delta, planetUp) * target.transform.rotation;
        }

        // ============================================================
        //                      Ghost material
        // ============================================================
        private static Material MakeGhostMaterial(Color color)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Surface"))   m.SetFloat("_Surface", 1f); // transparent
            if (m.HasProperty("_Blend"))     m.SetFloat("_Blend",   0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = 3000;
            return m;
        }
        private static void StripGhost(GameObject root)
        {
            foreach (var col in root.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            foreach (var rb  in root.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            // Hide socket gizmos in the ghost.
            foreach (var sock in root.GetComponentsInChildren<BuildSocket>(true)) sock.enabled = false;
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
