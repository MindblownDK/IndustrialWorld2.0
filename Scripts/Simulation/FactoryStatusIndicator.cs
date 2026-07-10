// Assets/Scripts/VoxelEngine/Simulation/FactoryStatusIndicator.cs
//
// Lightweight generated-prefab polish component. It drives a named status strip
// and optional light from nearby factory logic so conveyors, funnels, chutes,
// and machines communicate idle/active/blocked/offline states at a glance.

using UnityEngine;
using VoxelEngine.Crafting;

namespace VoxelEngine.Simulation
{
    public enum FactoryVisualStatus
    {
        Idle,
        Active,
        Blocked,
        Offline,
        Disabled
    }

    public sealed class FactoryStatusIndicator : MonoBehaviour
    {
        [Header("Targets")]
        public string rendererChildName = "Generated_StatusStrip";
        public string lightChildName;

        [Header("Timing")]
        [Min(0.05f)] public float refreshInterval = 0.2f;
        [Range(0f, 1f)] public float pulseAmount = 0.18f;
        [Min(0f)] public float pulseSpeed = 2f;

        [Header("Colors")]
        public Color idleColor = new(0.95f, 0.62f, 0.18f);
        public Color activeColor = new(0.22f, 0.78f, 0.42f);
        public Color blockedColor = new(0.95f, 0.18f, 0.14f);
        public Color offlineColor = new(0.18f, 0.72f, 0.88f);
        public Color disabledColor = new(0.25f, 0.26f, 0.28f);

        private Renderer _renderer;
        private Material _material;
        private Light _light;
        private float _refreshTimer;
        private FactoryVisualStatus _status = FactoryVisualStatus.Idle;
        private IMachine _machine;
        private ElectricFurnace _electricFurnace;
        private ConveyorBelt _belt;
        private ConveyorChute _chute;
        private Funnel _funnel;

        private void Awake()
        {
            CacheLogicTargets();
            CacheVisualTargets();
            ApplyStatus(ResolveStatus(), force: true);
        }

        private void Update()
        {
            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= refreshInterval)
            {
                _refreshTimer = 0f;
                ApplyStatus(ResolveStatus(), force: false);
            }

            if (_light != null && pulseAmount > 0f && _status == FactoryVisualStatus.Active)
            {
                float pulse = 1f + Mathf.Sin(Time.time * Mathf.PI * 2f * pulseSpeed) * pulseAmount;
                _light.intensity = Mathf.Max(0f, pulse);
            }
        }

        private void CacheLogicTargets()
        {
            _machine = GetComponent<IMachine>();
            _electricFurnace = GetComponent<ElectricFurnace>();
            _belt = GetComponent<ConveyorBelt>();
            _chute = GetComponent<ConveyorChute>();
            _funnel = GetComponent<Funnel>();
        }

        private void CacheVisualTargets()
        {
            Transform rendererTransform = FindChild(rendererChildName);
            if (rendererTransform != null) _renderer = rendererTransform.GetComponent<Renderer>();
            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>(true);

            if (_renderer != null)
            {
                _material = new Material(_renderer.sharedMaterial != null
                    ? _renderer.sharedMaterial
                    : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")));
                _renderer.sharedMaterial = _material;
                _material.EnableKeyword("_EMISSION");
            }

            Transform lightTransform = FindChild(lightChildName);
            if (lightTransform != null) _light = lightTransform.GetComponent<Light>();
            if (_light == null) _light = GetComponentInChildren<Light>(true);
        }

        private FactoryVisualStatus ResolveStatus()
        {
            if (_machine != null)
            {
                if (!_machine.UserEnabled) return FactoryVisualStatus.Disabled;
                if (!_machine.IsOnline) return FactoryVisualStatus.Offline;
                if (_machine.IsActive || _machine.Progress01 > 0.001f) return FactoryVisualStatus.Active;
                return FactoryVisualStatus.Idle;
            }

            if (_electricFurnace != null)
            {
                if (!_electricFurnace.userEnabled) return FactoryVisualStatus.Disabled;
                if (!_electricFurnace.IsOnline) return FactoryVisualStatus.Offline;
                if (_electricFurnace.Current != null || _electricFurnace.SmeltProgress01 > 0.001f) return FactoryVisualStatus.Active;
                return FactoryVisualStatus.Idle;
            }

            if (_belt != null)
            {
                int count = _belt.Items != null ? _belt.Items.Count : 0;
                if (count >= Mathf.Max(1, _belt.maxItems)) return FactoryVisualStatus.Blocked;
                if (count > 0) return FactoryVisualStatus.Active;
                return FactoryVisualStatus.Idle;
            }

            if (_chute != null)
            {
                int count = _chute.Items != null ? _chute.Items.Count : 0;
                if (count >= Mathf.Max(1, _chute.maxItems)) return FactoryVisualStatus.Blocked;
                if (count > 0) return FactoryVisualStatus.Active;
                return FactoryVisualStatus.Idle;
            }

            if (_funnel != null)
            {
                if (_funnel.BufferedCount >= Mathf.Max(1, _funnel.bufferSize)) return FactoryVisualStatus.Blocked;
                if (_funnel.BufferedCount > 0) return FactoryVisualStatus.Active;
                return FactoryVisualStatus.Idle;
            }

            return FactoryVisualStatus.Idle;
        }

        private void ApplyStatus(FactoryVisualStatus status, bool force)
        {
            if (!force && status == _status) return;
            _status = status;

            Color color = status switch
            {
                FactoryVisualStatus.Active => activeColor,
                FactoryVisualStatus.Blocked => blockedColor,
                FactoryVisualStatus.Offline => offlineColor,
                FactoryVisualStatus.Disabled => disabledColor,
                _ => idleColor
            };

            if (_material != null)
            {
                _material.color = color;
                if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", color);
                if (_material.HasProperty("_EmissionColor")) _material.SetColor("_EmissionColor", color * 1.45f);
            }

            if (_light != null)
            {
                _light.color = color;
                _light.enabled = status != FactoryVisualStatus.Disabled;
                _light.intensity = status == FactoryVisualStatus.Blocked ? 1.8f : 1f;
            }
        }

        private Transform FindChild(string childName)
        {
            if (string.IsNullOrWhiteSpace(childName)) return null;
            var children = GetComponentsInChildren<Transform>(true);
            foreach (var child in children)
            {
                if (child != null && child.name == childName) return child;
            }
            return null;
        }
    }
}
