// Assets/Scripts/VoxelEngine/Simulation/ConveyorSnapSystem.cs
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.UI;

namespace VoxelEngine.Simulation
{
    public class ConveyorSnapSystem : MonoBehaviour
    {
        [Header("References")]
        public Camera playerCamera;

        [Header("Snap Settings")]
        public float snapDetectRadius = 2.5f;
        public float gridSize = 1f;

        [Header("Key Bindings")]
        public KeyCode toggleSnapKey = KeyCode.X;

        public bool BeltSnapEnabled { get; private set; } = true;
        public ConveyorBelt SnappedBelt { get; private set; }
        public Vector3 SuggestedPosition { get; private set; }
        public Quaternion SuggestedRotation { get; private set; }
        public bool IsHoldingConveyor { get; set; }

        private VisualElement _hudRoot;
        private Label _snapLabel;
        private bool _hudBuilt;

        private void Update()
        {
            if (Input.GetKeyDown(toggleSnapKey) && IsHoldingConveyor)
            {
                BeltSnapEnabled = !BeltSnapEnabled;
                UpdateHUD();
                VoxelEngine.UI.BuildFeedbackHud.Show("Conveyor Snap", BeltSnapEnabled ? "ENABLED" : "DISABLED", null, BeltSnapEnabled ? Color.cyan : Color.red);
            }

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

        private void CalculateSnap()
        {
            if (playerCamera == null) return;

            Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (!Physics.Raycast(ray, out RaycastHit hit, 20f))
            {
                SuggestedPosition = ray.GetPoint(5f);
                SuggestedRotation = GetPlayerFacingRotation();
                SnappedBelt = null;
                return;
            }

            Vector3 hitPoint = hit.point;
            SuggestedRotation = GetPlayerFacingRotation();

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

            SnappedBelt = null;
            SuggestedPosition = SnapToGrid(hitPoint);
            UpdateHUD();
        }

        private Quaternion GetPlayerFacingRotation()
        {
            Vector3 forward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            
            // Snap to 90 degree increments
            float angle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            angle = Mathf.Round(angle / 90f) * 90f;
            return Quaternion.Euler(0, angle, 0);
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
            // Smart Snap: check if we are looking more at the exit or the sides
            Vector3 exitDir = belt.GetExitDirection();
            Vector3 sideDir = Vector3.Cross(Vector3.up, exitDir);
            
            Vector3 toHit = (hitPoint - belt.transform.position).normalized;
            float dotExit = Vector3.Dot(toHit, exitDir);
            float dotSide = Vector3.Dot(toHit, sideDir);

            Vector3 snapDir = exitDir;
            if (Mathf.Abs(dotSide) > 0.6f && dotExit < 0.4f)
            {
                snapDir = sideDir * Mathf.Sign(dotSide);
            }

            SuggestedPosition = SnapToGrid(belt.transform.position + snapDir * gridSize);
            // Ensure same level
            SuggestedPosition = new Vector3(SuggestedPosition.x, belt.transform.position.y, SuggestedPosition.z);
            
            // Orientation: if snapping to exit, continue direction. If side, face away from belt.
            if (snapDir == exitDir)
                SuggestedRotation = belt.transform.rotation;
            else
                SuggestedRotation = Quaternion.LookRotation(snapDir);
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

        private void EnsureHUD()
        {
            if (_hudBuilt) { _hudRoot.style.display = DisplayStyle.Flex; return; }

            var doc = GetComponentInChildren<UIDocument>();
            if (doc == null) return;

            _hudRoot = new VisualElement();
            _hudRoot.style.position = Position.Absolute;
            _hudRoot.style.bottom = 100; // Above hotbar
            _hudRoot.style.left = 0;
            _hudRoot.style.right = 0;
            _hudRoot.style.alignItems = Align.Center;
            _hudRoot.style.justifyContent = Justify.Center;
            _hudRoot.pickingMode = PickingMode.Ignore;
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

            var keyLabel = new Label("[X]");
            keyLabel.style.color = new StyleColor(UITheme.AccentCyan);
            keyLabel.style.fontSize = 11;
            keyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            keyLabel.style.marginRight = 6;
            keyLabel.pickingMode = PickingMode.Ignore;
            pill.Add(keyLabel);

            _snapLabel = new Label("Conveyor Snap: ENABLED");
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
                _snapLabel.text = "Conveyor Snap: ENABLED";
                _snapLabel.style.color = new StyleColor(UITheme.AccentCyan);
            }
            else
            {
                _snapLabel.text = "Conveyor Snap: DISABLED";
                _snapLabel.style.color = new StyleColor(UITheme.AccentRed);
            }
        }
    }
}
