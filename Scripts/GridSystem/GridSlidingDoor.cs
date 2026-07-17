// Assets/Scripts/VoxelEngine/GridSystem/GridSlidingDoor.cs
//
// Premium sliding door for grid ships/bases. Supports manual and motion activation.
// v5.61.0-dev — Grid door foundation with motion activation.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridSlidingDoor : GridBlock, IGridDataProvider
    {
        [Header("Door")]
        public Transform leftPanel;
        public Transform rightPanel;
        public float slideDistance = 0.42f;
        public float slideSpeed = 8f;
        public bool startsOpen;

        [Header("Motion Activation")]
        public bool motionActivated = true;
        public float motionRadius = 4.5f;
        public float motionGraceSeconds = 1.5f;

        [Header("Power")]
        public float idleWatts = 2f;
        public float movingWatts = 18f;

        private Vector3 _leftClosed;
        private Vector3 _rightClosed;
        private bool _targetOpen;
        private bool _cached;
        private float _motionCheckTimer;
        private float _lastMotionTime = -999f;

        public bool IsOpen => _targetOpen;
        public bool IsMoving { get; private set; }
        public bool HasPower => Enabled && Grid != null && Grid.HasPower;
        public override float PowerDraw => Enabled ? (IsMoving ? movingWatts : idleWatts) : 0f;

        public string SourceName => string.IsNullOrWhiteSpace(blockName) || blockName == "Armor Block" ? "Grid Sliding Door" : blockName;
        public string DataCategory => "Door";

        public string GetDisplayData()
        {
            string state = !Enabled ? "DISABLED" : !HasPower ? "NO POWER" : IsOpen ? "OPEN" : "CLOSED";
            return "DOOR\n" + state + "\n" +
                   "Motion " + (motionActivated ? "ON" : "OFF") + "\n" +
                   "Radius " + motionRadius.ToString("0.#") + "m\n" +
                   "Draw " + FormatWatts(PowerDraw);
        }

        private void Awake()
        {
            CachePanels();
            _targetOpen = startsOpen;
            ApplyImmediate(_targetOpen);
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrWhiteSpace(blockName) || blockName == "Armor Block")
                blockName = "Grid Sliding Door";
            CachePanels();
            _targetOpen = startsOpen;
            ApplyImmediate(_targetOpen);
        }

        private void Update()
        {
            CachePanels();
            TickMotionSensor();

            bool wantsOpen = _targetOpen || (motionActivated && Time.time - _lastMotionTime <= Mathf.Max(0.1f, motionGraceSeconds));
            if (!HasPower) wantsOpen = false;

            AnimatePanels(wantsOpen);
        }

        private void CachePanels()
        {
            if (_cached && leftPanel != null && rightPanel != null) return;
            if (leftPanel == null) leftPanel = transform.Find("Generated_LeftPanel");
            if (rightPanel == null) rightPanel = transform.Find("Generated_RightPanel");
            if (leftPanel != null) _leftClosed = leftPanel.localPosition;
            if (rightPanel != null) _rightClosed = rightPanel.localPosition;
            _cached = leftPanel != null || rightPanel != null;
        }

        private void TickMotionSensor()
        {
            if (!motionActivated || !Enabled || Grid == null) return;
            _motionCheckTimer -= Time.deltaTime;
            if (_motionCheckTimer > 0f) return;
            _motionCheckTimer = 0.20f;

            var players = Object.FindObjectsByType<VoxelEngine.Player.PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            float radiusSqr = Mathf.Max(0.1f, motionRadius) * Mathf.Max(0.1f, motionRadius);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                if ((players[i].transform.position - transform.position).sqrMagnitude <= radiusSqr)
                {
                    _lastMotionTime = Time.time;
                    return;
                }
            }
        }

        private void AnimatePanels(bool open)
        {
            Vector3 leftTarget = _leftClosed + Vector3.left * Mathf.Max(0f, slideDistance);
            Vector3 rightTarget = _rightClosed + Vector3.right * Mathf.Max(0f, slideDistance);
            if (!open)
            {
                leftTarget = _leftClosed;
                rightTarget = _rightClosed;
            }

            IsMoving = false;
            if (leftPanel != null)
            {
                leftPanel.localPosition = Vector3.Lerp(leftPanel.localPosition, leftTarget, 1f - Mathf.Exp(-slideSpeed * Time.deltaTime));
                IsMoving |= (leftPanel.localPosition - leftTarget).sqrMagnitude > 0.0001f;
            }
            if (rightPanel != null)
            {
                rightPanel.localPosition = Vector3.Lerp(rightPanel.localPosition, rightTarget, 1f - Mathf.Exp(-slideSpeed * Time.deltaTime));
                IsMoving |= (rightPanel.localPosition - rightTarget).sqrMagnitude > 0.0001f;
            }
        }

        private void ApplyImmediate(bool open)
        {
            Vector3 leftTarget = _leftClosed + (open ? Vector3.left * Mathf.Max(0f, slideDistance) : Vector3.zero);
            Vector3 rightTarget = _rightClosed + (open ? Vector3.right * Mathf.Max(0f, slideDistance) : Vector3.zero);
            if (leftPanel != null) leftPanel.localPosition = leftTarget;
            if (rightPanel != null) rightPanel.localPosition = rightTarget;
        }

        public void Toggle()
        {
            _targetOpen = !_targetOpen;
        }

        public void SetOpen(bool open)
        {
            _targetOpen = open;
        }

        public void SetMotionActivated(bool active)
        {
            motionActivated = active;
            if (!active) _lastMotionTime = -999f;
        }

        private static string FormatWatts(float watts)
        {
            float abs = Mathf.Abs(watts);
            if (abs >= 1000000f) return (watts / 1000000f).ToString("0.##") + " MW";
            if (abs >= 1000f) return (watts / 1000f).ToString("0.#") + " kW";
            return watts.ToString("0") + " W";
        }
    }
}
