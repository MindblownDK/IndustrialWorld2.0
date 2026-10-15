// Assets/Scripts/VoxelEngine/Simulation/MetallurgicalFoundryAnimator.cs

using UnityEngine;

namespace VoxelEngine.Simulation
{
    public sealed class MetallurgicalFoundryAnimator : MonoBehaviour
    {
        public string damperCapChildName = "Generated_DamperCap";
        public string electrodeAChildName = "Generated_ElectrodeA";
        public string electrodeBChildName = "Generated_ElectrodeB";
        public string electrodeCChildName = "Generated_ElectrodeC";
        public string moltenGlowChildName = "Generated_MoltenGlow";
        public string arcGlowChildName = "Generated_ArcGlow";

        private MetallurgicalFoundry _foundry;
        private Transform _damper;
        private Transform _elA;
        private Transform _elB;
        private Transform _elC;
        private Renderer _moltenRenderer;
        private Renderer _arcRenderer;
        private Vector3 _damperBase;
        private Vector3 _elABase;
        private Vector3 _elBBase;
        private Vector3 _elCBase;

        private void Awake()
        {
            _foundry = GetComponent<MetallurgicalFoundry>();
            _damper = transform.Find(damperCapChildName);
            _elA = transform.Find(electrodeAChildName);
            _elB = transform.Find(electrodeBChildName);
            _elC = transform.Find(electrodeCChildName);

            if (_damper != null) _damperBase = _damper.localPosition;
            if (_elA != null) _elABase = _elA.localPosition;
            if (_elB != null) _elBBase = _elB.localPosition;
            if (_elC != null) _elCBase = _elC.localPosition;

            var molten = transform.Find(moltenGlowChildName);
            if (molten != null) _moltenRenderer = molten.GetComponent<Renderer>();
            var arc = transform.Find(arcGlowChildName);
            if (arc != null) _arcRenderer = arc.GetComponent<Renderer>();
        }

        private void Update()
        {
            if (_foundry == null) _foundry = GetComponent<MetallurgicalFoundry>();
            bool active = _foundry != null && _foundry.IsActive;

            if (_damper != null)
            {
                float lift = active ? 0.06f + Mathf.Sin(Time.time * 5f) * 0.015f : 0f;
                _damper.localPosition = _damperBase + Vector3.up * lift;
            }

            if (_elA != null)
            {
                float plunge = active ? -0.10f + Mathf.Sin(Time.time * 14f) * 0.025f : 0f;
                _elA.localPosition = _elABase + Vector3.up * plunge;
            }
            if (_elB != null)
            {
                float plunge = active ? -0.10f + Mathf.Sin(Time.time * 14f + 2.1f) * 0.025f : 0f;
                _elB.localPosition = _elBBase + Vector3.up * plunge;
            }
            if (_elC != null)
            {
                float plunge = active ? -0.10f + Mathf.Sin(Time.time * 14f + 4.2f) * 0.025f : 0f;
                _elC.localPosition = _elCBase + Vector3.up * plunge;
            }

            if (_moltenRenderer != null)
                _moltenRenderer.enabled = active || (_foundry != null && _foundry.IsBurning);

            if (_arcRenderer != null)
                _arcRenderer.enabled = active;
        }
    }
}
