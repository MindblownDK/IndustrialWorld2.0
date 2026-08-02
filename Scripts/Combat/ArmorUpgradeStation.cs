// Assets/Scripts/VoxelEngine/Combat/ArmorUpgradeStation.cs
//
// A dedicated anvil workstation that installs one crafted armor module onto one
// armor piece at a time. Installation is intentionally physical and timed: tier 1
// takes the 30-second base duration, while higher-grade modules take proportionally
// longer. Inputs and progress are additive world-save state, so a process resumes
// after loading instead of losing a player's armor or module.

using System;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    [DisallowMultipleComponent]
    public sealed class ArmorUpgradeStation : MonoBehaviour
    {
        public const float DefaultBaseUpgradeSeconds = 30f;

        [Header("Upgrade Timing")]
        [Tooltip("Tier 1 installation duration in seconds. Higher module tiers multiply this base duration.")]
        [Min(0.1f)] public float baseUpgradeSeconds = DefaultBaseUpgradeSeconds;

        [Header("Runtime Visuals")]
        [SerializeField] private Transform _hammerPivot;
        [SerializeField] private Light _forgeLight;

        [SerializeField] private ItemContainer _armorSlot;
        [SerializeField] private ItemContainer _moduleSlot;
        [SerializeField] private ItemContainer _outputSlot;
        [SerializeField] private bool _isUpgrading;
        [SerializeField] private float _elapsedSeconds;
        [SerializeField] private float _totalSeconds;

        private Vector3 _hammerRestPosition;
        private Quaternion _hammerRestRotation;
        private bool _hammerRestCached;
        private float _nextStateNotifyAt;

        public event Action OnStateChanged;

        public ItemContainer ArmorSlot
        {
            get { EnsureContainers(); return _armorSlot; }
        }

        public ItemContainer ModuleSlot
        {
            get { EnsureContainers(); return _moduleSlot; }
        }

        public ItemContainer OutputSlot
        {
            get { EnsureContainers(); return _outputSlot; }
        }

        public bool IsUpgrading => _isUpgrading;
        public float ElapsedSeconds => Mathf.Max(0f, _elapsedSeconds);
        public float TotalSeconds => Mathf.Max(0f, _totalSeconds);
        public float Progress01 => _totalSeconds <= 0f ? 0f : Mathf.Clamp01(_elapsedSeconds / _totalSeconds);
        public float RemainingSeconds => Mathf.Max(0f, _totalSeconds - _elapsedSeconds);

        private void Awake()
        {
            EnsureContainers();
            CacheVisualState();
        }

        private void OnValidate()
        {
            if (baseUpgradeSeconds <= 0f) baseUpgradeSeconds = DefaultBaseUpgradeSeconds;
            EnsureContainers();
        }

        private void Update()
        {
            EnsureContainers();
            UpdateVisuals();
            if (!_isUpgrading) return;

            if (!CanContinueCurrentUpgrade(out _))
            {
                StopUpgrade();
                return;
            }

            _elapsedSeconds = Mathf.Min(_totalSeconds, _elapsedSeconds + Time.deltaTime);
            if (_elapsedSeconds >= _totalSeconds)
                CompleteUpgrade();
            else
                NotifyStateChanged(force: false);
        }

        /// <summary>Called by setup authoring to wire only generated visual children.</summary>
        public void ConfigureVisuals(Transform hammerPivot, Light forgeLight)
        {
            _hammerPivot = hammerPivot;
            _forgeLight = forgeLight;
            _hammerRestCached = false;
            CacheVisualState();
        }

        public float GetUpgradeDuration(ArmorUpgradeItem module)
        {
            if (module == null) return 0f;
            float baseSeconds = Mathf.Max(0.1f, baseUpgradeSeconds);
            return baseSeconds * module.InstallationTier;
        }

        public bool CanStartUpgrade(out string reason)
        {
            EnsureContainers();
            if (_isUpgrading)
            {
                reason = "An upgrade is already in progress.";
                return false;
            }

            var output = _outputSlot.GetSlot(0);
            if (output != null && !output.IsEmpty)
            {
                reason = "Collect the finished armor before starting another upgrade.";
                return false;
            }

            var armor = _armorSlot.GetSlot(0);
            var module = _moduleSlot.GetSlot(0)?.item as ArmorUpgradeItem;
            return ArmorUpgrades.CanApply(armor, module, out reason);
        }

        public bool TryStartUpgrade(out string reason)
        {
            if (!CanStartUpgrade(out reason)) return false;

            var module = _moduleSlot.GetSlot(0).item as ArmorUpgradeItem;
            _totalSeconds = GetUpgradeDuration(module);
            _elapsedSeconds = 0f;
            _isUpgrading = _totalSeconds > 0f;
            if (!_isUpgrading)
            {
                reason = "The upgrade duration is invalid.";
                return false;
            }

            NotifyStateChanged(force: true);
            return true;
        }

        /// <summary>
        /// Cancelling is safe: the armor and module remain in their input slots and
        /// can be removed by the player. No resource is consumed until completion.
        /// </summary>
        public void CancelUpgrade()
        {
            if (!_isUpgrading) return;
            StopUpgrade();
        }

        /// <summary>Restores only additive progress data after the station containers restore.</summary>
        public void RestoreProgress(bool wasUpgrading, float elapsedSeconds, float totalSeconds)
        {
            EnsureContainers();
            if (!wasUpgrading || !CanContinueCurrentUpgrade(out _))
            {
                StopUpgrade();
                return;
            }

            var module = _moduleSlot.GetSlot(0).item as ArmorUpgradeItem;
            _totalSeconds = totalSeconds > 0.1f ? totalSeconds : GetUpgradeDuration(module);
            _elapsedSeconds = Mathf.Clamp(elapsedSeconds, 0f, _totalSeconds);
            _isUpgrading = _totalSeconds > 0f;
            NotifyStateChanged(force: true);
        }

        private bool CanContinueCurrentUpgrade(out string reason)
        {
            var output = _outputSlot.GetSlot(0);
            if (output != null && !output.IsEmpty)
            {
                reason = "Output slot is occupied.";
                return false;
            }

            var armor = _armorSlot.GetSlot(0);
            var module = _moduleSlot.GetSlot(0)?.item as ArmorUpgradeItem;
            return ArmorUpgrades.CanApply(armor, module, out reason);
        }

        private void CompleteUpgrade()
        {
            if (!CanContinueCurrentUpgrade(out string reason))
            {
                StopUpgrade();
                VoxelEngine.UI.BuildFeedbackHud.Show("Upgrade Stopped", reason, null, Color.yellow);
                return;
            }

            var armor = _armorSlot.GetSlot(0);
            var moduleStack = _moduleSlot.GetSlot(0);
            var module = moduleStack.item as ArmorUpgradeItem;
            if (!ArmorUpgrades.TryApply(armor, module, out reason))
            {
                StopUpgrade();
                VoxelEngine.UI.BuildFeedbackHud.Show("Upgrade Stopped", reason, module != null ? module.icon : null, Color.yellow);
                return;
            }

            _armorSlot.SetSlot(0, new ItemStack());
            _moduleSlot.SetSlot(0, new ItemStack());
            _outputSlot.SetSlot(0, armor);
            StopUpgrade();

            string result = module.isHazmat
                ? $"Hazmat seal installed on {armor.item.displayName}."
                : $"{ArmorUpgradeKindInfo.DisplayName(module.kind)} upgraded to T{ArmorUpgrades.GetTier(armor, module.kind)}.";
            VoxelEngine.UI.BuildFeedbackHud.Show("Armor Upgrade Complete", result, armor.item.icon, new Color(0.88f, 0.72f, 0.22f));
        }

        private void StopUpgrade()
        {
            _isUpgrading = false;
            _elapsedSeconds = 0f;
            _totalSeconds = 0f;
            NotifyStateChanged(force: true);
        }

        private void EnsureContainers()
        {
            if (_armorSlot == null) _armorSlot = new ItemContainer("Armor Upgrade Input", 1);
            else _armorSlot.Resize(1);
            _armorSlot.AcceptFilter = (item, wanted) => item is ArmorItem ? Mathf.Min(1, wanted) : 0;

            if (_moduleSlot == null) _moduleSlot = new ItemContainer("Armor Upgrade Module", 1);
            else _moduleSlot.Resize(1);
            _moduleSlot.AcceptFilter = (item, wanted) => item is ArmorUpgradeItem ? Mathf.Min(1, wanted) : 0;

            if (_outputSlot == null) _outputSlot = new ItemContainer("Upgraded Armor Output", 1);
            else _outputSlot.Resize(1);
            _outputSlot.AcceptFilter = (item, wanted) => item is ArmorItem ? Mathf.Min(1, wanted) : 0;
        }

        private void CacheVisualState()
        {
            if (_hammerPivot == null || _hammerRestCached) return;
            _hammerRestPosition = _hammerPivot.localPosition;
            _hammerRestRotation = _hammerPivot.localRotation;
            _hammerRestCached = true;
        }

        private void UpdateVisuals()
        {
            CacheVisualState();
            if (_hammerPivot != null)
            {
                float strike = 0f;
                if (_isUpgrading)
                {
                    float phase = Mathf.Repeat(Time.time * 2.4f, 1f);
                    strike = Mathf.Sin(phase * Mathf.PI);
                    strike *= strike;
                }
                // The hammer travels vertically onto the anvil face instead of
                // rotating through its body. A small tilt gives the impact a tactile
                // feel while keeping the head visibly above the work surface at rest.
                _hammerPivot.localPosition = _hammerRestPosition + Vector3.down * (0.15f * strike);
                _hammerPivot.localRotation = _hammerRestRotation * Quaternion.Euler(5f * strike, 0f, 0f);
            }

            if (_forgeLight != null)
            {
                float pulse = _isUpgrading ? 0.85f + Mathf.Sin(Time.time * 7f) * 0.15f : 0.16f;
                _forgeLight.intensity = _isUpgrading ? 3.2f * pulse : 0.45f;
                _forgeLight.color = _isUpgrading
                    ? new Color(1f, 0.36f, 0.08f)
                    : new Color(0.95f, 0.55f, 0.18f);
            }
        }

        private void NotifyStateChanged(bool force)
        {
            if (!force && Time.unscaledTime < _nextStateNotifyAt) return;
            _nextStateNotifyAt = Time.unscaledTime + 0.25f;
            OnStateChanged?.Invoke();
        }
    }
}
