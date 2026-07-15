// Assets/Scripts/VoxelEngine/Simulation/AssemblerMotionAnimator.cs

using UnityEngine;

namespace VoxelEngine.Simulation
{
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
