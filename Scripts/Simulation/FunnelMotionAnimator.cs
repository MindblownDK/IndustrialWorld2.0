// Assets/Scripts/VoxelEngine/Simulation/FunnelMotionAnimator.cs

using UnityEngine;

namespace VoxelEngine.Simulation
{
    public sealed class FunnelMotionAnimator : MonoBehaviour
        {
            [Header("Generated Child Names")]
            public string flapChildName = "Generated_Flap";
            public string itemChildName = "Generated_ItemPacket";
            public string beltMouthChildName = "Generated_BeltMouth";
            public string inventoryMouthChildName = "Generated_InventoryMouth";
    
            [Header("Motion")]
            public float flapDegrees = 28f;
            public float pulseFrequency = 3.6f;
            public float itemTravelDistance = 0.62f;
    
            private Funnel _funnel;
            private Transform _flap;
            private Transform _itemPacket;
            private Transform _beltMouth;
            private Transform _inventoryMouth;
            private Vector3 _itemBaseLocal;
            private Vector3 _beltMouthBaseScale;
            private Vector3 _inventoryMouthBaseScale;
            private Quaternion _flapBaseLocal;
            private Renderer _itemRenderer;
    
            private void Awake()
            {
                _funnel = GetComponent<Funnel>();
                _flap = FindChild(flapChildName);
                _itemPacket = FindChild(itemChildName);
                _beltMouth = FindChild(beltMouthChildName);
                _inventoryMouth = FindChild(inventoryMouthChildName);
                if (_flap != null) _flapBaseLocal = _flap.localRotation;
                if (_beltMouth != null) _beltMouthBaseScale = _beltMouth.localScale;
                if (_inventoryMouth != null) _inventoryMouthBaseScale = _inventoryMouth.localScale;
                if (_itemPacket != null)
                {
                    _itemBaseLocal = _itemPacket.localPosition;
                    _itemRenderer = _itemPacket.GetComponent<Renderer>();
                }
            }
    
            private void Update()
            {
                if (_funnel == null) _funnel = GetComponent<Funnel>();
                float buffered = _funnel != null ? Mathf.Clamp01(_funnel.BufferedCount / (float)Mathf.Max(1, _funnel.bufferSize)) : 0f;
                bool transferring = buffered > 0.01f || (_funnel != null && (_funnel.inputSource != null || _funnel.outputTarget != null));
                float wave = transferring ? Mathf.Abs(Mathf.Sin(Time.time * pulseFrequency)) : 0f;
    
                if (_flap != null)
                {
                    float direction = _funnel != null && _funnel.Mode == FunnelMode.Export ? -1f : 1f;
                    _flap.localRotation = _flapBaseLocal * Quaternion.Euler(direction * flapDegrees * wave, 0f, 0f);
                }
    
                if (_itemPacket != null)
                {
                    float t = Mathf.Repeat(Time.time * Mathf.Max(0.1f, pulseFrequency * 0.22f), 1f);
                    float direction = _funnel != null && _funnel.Mode == FunnelMode.Export ? 1f : -1f;
                    Vector3 travel = Vector3.forward * (direction * Mathf.Lerp(-itemTravelDistance * 0.5f, itemTravelDistance * 0.5f, t));
                    _itemPacket.localPosition = _itemBaseLocal + travel + Vector3.up * (0.025f * Mathf.Sin(t * Mathf.PI));
                    _itemPacket.localScale = Vector3.one * Mathf.Lerp(0.08f, 0.13f, buffered > 0f ? 1f : wave);
                    if (_itemRenderer != null) _itemRenderer.enabled = transferring;
                }
    
                Pulse(_beltMouth, _beltMouthBaseScale, wave, buffered);
                Pulse(_inventoryMouth, _inventoryMouthBaseScale, wave, buffered);
            }
    
            private static void Pulse(Transform target, Vector3 baseScale, float wave, float buffered)
            {
                if (target == null) return;
                float scale = 1f + wave * Mathf.Lerp(0.01f, 0.035f, buffered);
                target.localScale = new Vector3(baseScale.x, baseScale.y, Mathf.Max(0.02f, baseScale.z) * scale);
            }
    
            private Transform FindChild(string childName)
            {
                return string.IsNullOrEmpty(childName) ? null : transform.Find(childName);
            }
        }
}
