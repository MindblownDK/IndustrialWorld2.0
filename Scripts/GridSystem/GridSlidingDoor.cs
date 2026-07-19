// Assets/Scripts/VoxelEngine/GridSystem/GridSlidingDoor.cs
//
// Premium sliding door for grid ships/bases. Supports manual and motion activation.
// v5.62.4-dev — Moves decorative panel parts with door panels and rotates vault handles.

using System.Collections.Generic;
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
        [Tooltip("If true, decorative panel details with matching generated names move with the main panel.")]
        public bool autoBindGeneratedPanelDetails = true;
        [Tooltip("Degrees the vault handle/core rotates while opening.")]
        public float vaultHandleTurnDegrees = 110f;

        [Header("Motion Activation")]
        public bool motionActivated = true;
        public float motionRadius = 4.5f;
        public float motionGraceSeconds = 1.5f;

        [Header("Power")]
        public float idleWatts = 2f;
        public float movingWatts = 18f;

        private readonly Dictionary<Transform, Vector3> _leftClosed = new();
        private readonly Dictionary<Transform, Vector3> _rightClosed = new();
        private readonly Dictionary<Transform, Quaternion> _handleClosed = new();
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
            // Cache authored closed positions once. Re-caching moving panels every frame made
            // single-panel/vault doors slide away forever.
            if (_cached) return;

            if (leftPanel == null) leftPanel = transform.Find("Generated_LeftPanel");
            if (rightPanel == null) rightPanel = transform.Find("Generated_RightPanel");

            _leftClosed.Clear();
            _rightClosed.Clear();
            _handleClosed.Clear();

            AddMovingPart(leftPanel, _leftClosed);
            AddMovingPart(rightPanel, _rightClosed);

            if (autoBindGeneratedPanelDetails)
            {
                foreach (Transform child in transform)
                {
                    if (child == null) continue;
                    string n = child.name;
                    if (IsLeftMovingDetail(n)) AddMovingPart(child, _leftClosed);
                    else if (IsRightMovingDetail(n)) AddMovingPart(child, _rightClosed);
                    if (IsVaultHandlePart(n) && !_handleClosed.ContainsKey(child))
                        _handleClosed.Add(child, child.localRotation);
                }
            }

            _cached = _leftClosed.Count > 0 || _rightClosed.Count > 0;
        }

        private static void AddMovingPart(Transform t, Dictionary<Transform, Vector3> target)
        {
            if (t == null || target.ContainsKey(t)) return;
            if (t.localScale.sqrMagnitude < 0.0001f) return;
            target.Add(t, t.localPosition);
        }

        private static bool IsLeftMovingDetail(string n)
        {
            return n == "Generated_LeftPanel"
                || n.StartsWith("Generated_LeftWindow")
                || n.StartsWith("Generated_DarkDiagonalInset")
                || n.StartsWith("Generated_AccessPanel")
                || n.StartsWith("Generated_AccessGlow")
                || n.StartsWith("Generated_NumberStripe")
                || n.StartsWith("Generated_LeftAccess")
                || n.StartsWith("Generated_Vault")
                || n.StartsWith("Generated_Bolt")
                || n.StartsWith("Generated_VaultBolt");
        }

        private static bool IsRightMovingDetail(string n)
        {
            return n == "Generated_RightPanel"
                || n.StartsWith("Generated_RightWindow")
                || n.StartsWith("Generated_RightRib");
        }

        private static bool IsVaultHandlePart(string n)
        {
            return n.StartsWith("Generated_VaultCore")
                || n.StartsWith("Generated_VaultBar");
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
            IsMoving = false;
            float t = 1f - Mathf.Exp(-slideSpeed * Time.deltaTime);
            Vector3 leftDelta = Vector3.left * Mathf.Max(0f, slideDistance);
            Vector3 rightDelta = Vector3.right * Mathf.Max(0f, slideDistance);

            AnimateGroup(_leftClosed, open ? leftDelta : Vector3.zero, t);
            AnimateGroup(_rightClosed, open ? rightDelta : Vector3.zero, t);
            AnimateVaultHandles(open, t);
        }

        private void AnimateGroup(Dictionary<Transform, Vector3> parts, Vector3 delta, float t)
        {
            foreach (var kv in parts)
            {
                if (kv.Key == null) continue;
                Vector3 target = kv.Value + delta;
                kv.Key.localPosition = Vector3.Lerp(kv.Key.localPosition, target, t);
                IsMoving |= (kv.Key.localPosition - target).sqrMagnitude > 0.0001f;
            }
        }

        private void AnimateVaultHandles(bool open, float t)
        {
            foreach (var kv in _handleClosed)
            {
                if (kv.Key == null) continue;
                Quaternion target = kv.Value * Quaternion.Euler(0f, 0f, open ? vaultHandleTurnDegrees : 0f);
                kv.Key.localRotation = Quaternion.Slerp(kv.Key.localRotation, target, t);
                IsMoving |= Quaternion.Angle(kv.Key.localRotation, target) > 0.1f;
            }
        }

        private void ApplyImmediate(bool open)
        {
            Vector3 leftDelta = open ? Vector3.left * Mathf.Max(0f, slideDistance) : Vector3.zero;
            Vector3 rightDelta = open ? Vector3.right * Mathf.Max(0f, slideDistance) : Vector3.zero;
            foreach (var kv in _leftClosed) if (kv.Key != null) kv.Key.localPosition = kv.Value + leftDelta;
            foreach (var kv in _rightClosed) if (kv.Key != null) kv.Key.localPosition = kv.Value + rightDelta;
            foreach (var kv in _handleClosed) if (kv.Key != null) kv.Key.localRotation = kv.Value * Quaternion.Euler(0f, 0f, open ? vaultHandleTurnDegrees : 0f);
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
