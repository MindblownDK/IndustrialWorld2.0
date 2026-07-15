// Assets/Scripts/VoxelEngine/Simulation/MachineVisualAnimators.cs
//
// Lightweight procedural visual motion for factory blocks. These components only
// animate generated child transforms/materials; they never participate in save data
// or gameplay inventory logic.

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

    public sealed class AssemblerMotionAnimator : MonoBehaviour
    {
        public string gantryChildName = "Generated_Gantry";
        public string pressHeadChildName = "Generated_PressHead";
        public string workPieceChildName = "Generated_WorkPiece";
        public float gantryTravel = 0.7f;
        public float pressTravel = 0.18f;

        private Assembler _assembler;
        private Transform _gantry;
        private Transform _pressHead;
        private Transform _workPiece;
        private Vector3 _gantryBase;
        private Vector3 _pressBase;
        private Vector3 _workBase;
        private Renderer _workRenderer;

        private void Awake()
        {
            _assembler = GetComponent<Assembler>();
            _gantry = transform.Find(gantryChildName);
            _pressHead = transform.Find(pressHeadChildName);
            _workPiece = transform.Find(workPieceChildName);
            if (_gantry != null) _gantryBase = _gantry.localPosition;
            if (_pressHead != null) _pressBase = _pressHead.localPosition;
            if (_workPiece != null) { _workBase = _workPiece.localPosition; _workRenderer = _workPiece.GetComponent<Renderer>(); }
        }

        private void Update()
        {
            if (_assembler == null) _assembler = GetComponent<Assembler>();
            bool active = _assembler != null && _assembler.IsActive;
            float progress = _assembler != null ? _assembler.Progress01 : 0f;
            float wave = active ? Mathf.SmoothStep(0f, 1f, Mathf.PingPong(Time.time * 1.8f, 1f)) : 0f;

            if (_gantry != null)
                _gantry.localPosition = _gantryBase + Vector3.right * Mathf.Lerp(-gantryTravel * 0.5f, gantryTravel * 0.5f, wave);
            if (_pressHead != null)
                _pressHead.localPosition = _pressBase - Vector3.up * (pressTravel * Mathf.Abs(Mathf.Sin(Time.time * 9f)) * (active ? 1f : 0f));
            if (_workPiece != null)
            {
                if (_workRenderer != null) _workRenderer.enabled = active;
                _workPiece.localPosition = _workBase + Vector3.forward * Mathf.Lerp(-0.28f, 0.28f, progress);
                _workPiece.localRotation = Quaternion.Euler(0f, Time.time * (active ? 80f : 0f), 0f);
            }
        }
    }
}
