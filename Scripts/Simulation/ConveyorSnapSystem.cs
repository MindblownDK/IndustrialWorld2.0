// Assets/Scripts/VoxelEngine/Simulation/ConveyorSnapSystem.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — CONVEYOR SNAP SYSTEM                        ║
// ║  When holding a conveyor belt, snap placement to nearby belts    ║
// ║  BEFORE falling back to grid snap. Toggle belt-snap with X key. ║
// ║  Shows a HUD indicator when belt-snap is active.                ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.UI;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Manages conveyor belt placement snapping. When the player holds
    /// a conveyor belt item:
    ///   1. Belt-snap mode (default ON): ghost snaps to the nearest
    ///      conveyor belt's exit point, auto-orienting to continue the line.
    ///   2. Grid-snap fallback: if no belt is nearby, snaps to the build grid.
    ///   3. X key toggles belt-snap on/off (shows HUD indicator).
    ///
    /// Attach to the player GameObject. References the camera for raycasts
    /// and the ConveyorNetwork for finding nearby belts.
    /// </summary>
    public class ConveyorSnapSystem : MonoBehaviour
    {
        [Header("References")]
        public Camera playerCamera;

        [Header("Snap Settings")]
        [Tooltip("Maximum distance to detect a nearby belt for snapping.")]\
        public float snapDetectRadius = 2.5f;

        [Tooltip("Grid cell size for fallback grid snapping.")]\
        public float gridSize = 1f;

        [Header("Key Bindings")]
        public KeyCode toggleSnapKey = KeyCode.X;

        // ── State ─────────────────────────────────────────────────────

        /// <summary>Whether belt-to-belt snapping is currently enabled.</summary>
        public bool BeltSnapEnabled { get; private set; } = true;

        /// <summary>The belt we're currently snapped to (null if none).</summary>
        public ConveyorBelt SnappedBelt { get; private set; }

        /// <summary>The suggested placement position (snapped or grid).</summary>
        public Vector3 SuggestedPosition { get; private set; }

        /// <summary>The suggested rotation (aligned to snapped belt or player facing).</summary>
        public Quaternion SuggestedRotation { get; private set; }

        /// <summary>True when the player is holding a conveyor belt item.</summary>
        public bool IsHoldingConveyor { get; set; }

        // HUD elements.
        private VisualElement _hudRoot;
        private Label _snapLabel;
        private bool _hudBuilt;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Update()
        {
            // Toggle belt-snap with X.
            if (Input.GetKeyDown(toggleSnapKey) && IsHoldingConveyor)
            {
                BeltSnapEnabled = !BeltSnapEnabled;
                UpdateHUD();
            }

            // Calculate snap position when holding a conveyor.
            if (IsHoldingConveyor)
            {
                CalculateSnap();
                EnsureHUD();
            }
            else
            {
                SnappedBelt = null;
                if (_hudRoot != null) _hudRoot.style.display = DisplayStyle.None;
            }
        }

        // ── Snap Calculation ──────────────────────────────────────────

        private void CalculateSnap()
        {
            if (playerCamera == null) return;

            // Raycast from camera to find the point the player is looking at.
            Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f));
            if (!Physics.Raycast(ray, out RaycastHit hit, 20f))
            {
                SuggestedPosition = ray.GetPoint(5f);
                SuggestedRotation = Quaternion.LookRotation(
                    Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized);
                SnappedBelt = null;
                return;
            }

            Vector3 hitPoint = hit.point;
            SuggestedRotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized);

            // Try belt-snap first.
            if (BeltSnapEnabled)
            {
                var nearestBelt = FindNearestBelt(hitPoint);
                if (nearestBelt != null)
                {
                    SnappedBelt = nearestBelt;
                    SnapToBelt(nearestBelt, hitPoint);
                    UpdateHUD();
                    return;
                }
            }

            // Fallback: grid snap.
            SnappedBelt = null;
            SuggestedPosition = SnapToGrid(hitPoint);
            UpdateHUD();
        }

        private ConveyorBelt FindNearestBelt(Vector3 worldPos)
        {
            var hits = Physics.OverlapSphere(worldPos, snapDetectRadius);
            ConveyorBelt best = null;
            float bestDist = snapDetectRadius;

            foreach (var col in hits)
            {
                var belt = col.GetComponentInParent<ConveyorBelt>();
                if (belt == null) continue;

                float dist = Vector3.Distance(belt.transform.position, worldPos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = belt;
                }
            }
            return best;
        }

        private void SnapToBelt(ConveyorBelt belt, Vector3 hitPoint)
        {
            // Place at the exit end of the snapped belt, continuing its direction.
            Vector3 exitDir = belt.GetExitDirection();
            Vector3 exitPos = belt.transform.position + exitDir * gridSize;

            // Snap to grid cell nearest to the exit position.
            SuggestedPosition = SnapToGrid(exitPos);

            // Align rotation to continue the belt line.
            if (belt.shape == ConveyorShape.Corner)
            {
                // After a corner, continue in the corner's exit direction.
                SuggestedRotation = Quaternion.LookRotation(exitDir);
            }
            else
            {
                SuggestedRotation = belt.transform.rotation;
            }
        }

        private Vector3 SnapToGrid(Vector3 worldPos)
        {
            float g = Mathf.Max(0.1f, gridSize);
            return new Vector3(
                Mathf.Round(worldPos.x / g) * g,
                Mathf.Round(worldPos.y / g) * g,
                Mathf.Round(worldPos.z / g) * g
            );
        }

        // ── HUD ───────────────────────────────────────────────────────

        private void EnsureHUD()
        {
            if (_hudBuilt) { _hudRoot.style.display = DisplayStyle.Flex; return; }

            // Find or create a UIDocument for the HUD.
            var doc = GetComponentInChildren<UIDocument>();
            if (doc == null) return;

            _hudRoot = new VisualElement();
            _hudRoot.style.position = Position.Absolute;
            _hudRoot.style.bottom = 140;
            _hudRoot.style.left = 0;
            _hudRoot.style.right = 0;
            _hudRoot.style.alignItems = Align.Center;
            _hudRoot.style.justifyContent = Justify.Center;
            doc.rootVisualElement.Add(_hudRoot);

            var pill = new VisualElement();
            pill.style.flexDirection = FlexDirection.Row;
            pill.style.alignItems = Align.Center;
            pill.style.paddingLeft = 12;
            pill.style.paddingRight = 14;
            pill.style.paddingTop = 5;
            pill.style.paddingBottom = 5;
            pill.style.backgroundColor = new StyleColor(new Color(0.04f, 0.045f, 0.06f, 0.90f));
            UITheme.Radius(pill, 12);
            UITheme.Border(pill, 1, UITheme.BorderDim);
            _hudRoot.Add(pill);

            // Key hint.
            var keyLabel = new Label("[X]");
            keyLabel.style.color = new StyleColor(UITheme.AccentCyan);
            keyLabel.style.fontSize = 11;
            keyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            keyLabel.style.marginRight = 6;
            keyLabel.pickingMode = PickingMode.Ignore;
            pill.Add(keyLabel);

            _snapLabel = new Label("Belt Snap: ON");
            _snapLabel.style.fontSize = 11;
            _snapLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _snapLabel.pickingMode = PickingMode.Ignore;
            pill.Add(_snapLabel);

            _hudBuilt = true;
            UpdateHUD();
        }

        private void UpdateHUD()
        {
            if (_snapLabel == null) return;

            if (BeltSnapEnabled)
            {
                _snapLabel.text = SnappedBelt != null
                    ? "Belt Snap: LOCKED"
                    : "Belt Snap: ON";
                _snapLabel.style.color = new StyleColor(
                    SnappedBelt != null ? UITheme.AccentGreen : UITheme.AccentCyan);
            }
            else
            {
                _snapLabel.text = "Belt Snap: OFF";
                _snapLabel.style.color = new StyleColor(UITheme.AccentRed);
            }
        }
    }
}
