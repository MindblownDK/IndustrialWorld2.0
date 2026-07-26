// Assets/Scripts/VoxelEngine/Simulation/CrusherMotionAnimator.cs

using UnityEngine;

namespace VoxelEngine.Simulation
{
    public sealed class CrusherMotionAnimator : MonoBehaviour
        {
            public string leftJawChildName = "Generated_LeftJaw";
            public string rightJawChildName = "Generated_RightJaw";
            public string fallingItemChildName = "Generated_FallingItem";
            public string crushedItemChildName = "Generated_CrushedItem";
            public float jawTravel = 0.16f;
            public float shakeDegrees = 2.5f;
    
            private Crusher _crusher;
            private Transform _leftJaw;
            private Transform _rightJaw;
            private Transform _fallingItem;
            private Transform _crushedItem;
            private Vector3 _leftBase;
            private Vector3 _rightBase;
            private Vector3 _fallingBase;
            private Vector3 _crushedBase;
            private Renderer _fallingRenderer;
            private Renderer _crushedRenderer;
    
            private void Awake()
            {
                _crusher = GetComponent<Crusher>();
                _leftJaw = transform.Find(leftJawChildName);
                _rightJaw = transform.Find(rightJawChildName);
                _fallingItem = transform.Find(fallingItemChildName);
                _crushedItem = transform.Find(crushedItemChildName);
                if (_leftJaw != null) _leftBase = _leftJaw.localPosition;
                if (_rightJaw != null) _rightBase = _rightJaw.localPosition;
                if (_fallingItem != null) { _fallingBase = _fallingItem.localPosition; _fallingRenderer = _fallingItem.GetComponent<Renderer>(); }
                if (_crushedItem != null) { _crushedBase = _crushedItem.localPosition; _crushedRenderer = _crushedItem.GetComponent<Renderer>(); }
            }
    
            private void Update()
            {
                if (_crusher == null) _crusher = GetComponent<Crusher>();
                float progress = _crusher != null ? _crusher.Progress01 : 0f;
                bool active = _crusher != null && _crusher.IsActive;
                float crush = active ? Mathf.Abs(Mathf.Sin(Time.time * 14f)) : 0f;
    
                if (_leftJaw != null) _leftJaw.localPosition = _leftBase + Vector3.right * (jawTravel * crush);
                if (_rightJaw != null) _rightJaw.localPosition = _rightBase - Vector3.right * (jawTravel * crush);
                transform.localRotation = Quaternion.Euler(0f, 0f, active ? Mathf.Sin(Time.time * 18f) * shakeDegrees * 0.12f : 0f);
    
                if (_fallingItem != null)
                {
                    bool falling = active && progress < 0.55f;
                    if (_fallingRenderer != null) _fallingRenderer.enabled = falling;
                    float t = Mathf.Clamp01(progress / 0.55f);
                    _fallingItem.localPosition = Vector3.Lerp(_fallingBase + Vector3.up * 0.55f, _fallingBase - Vector3.up * 0.20f, t);
                    _fallingItem.localRotation = Quaternion.Euler(Time.time * 120f, Time.time * 70f, Time.time * 95f);
                }
    
                if (_crushedItem != null)
                {
                    bool crushed = active && progress >= 0.45f;
                    if (_crushedRenderer != null) _crushedRenderer.enabled = crushed;
                    float t = Mathf.Clamp01((progress - 0.45f) / 0.55f);
                    _crushedItem.localPosition = _crushedBase + Vector3.forward * Mathf.Lerp(-0.12f, 0.28f, t);
                    _crushedItem.localScale = new Vector3(0.22f, Mathf.Lerp(0.12f, 0.035f, t), 0.22f);
                }
            }
        }
}
