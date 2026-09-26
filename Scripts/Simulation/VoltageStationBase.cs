using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    public abstract class VoltageStationBase : MonoBehaviour, IVoltageStation
    {
        [Header("Station Settings")]
        public int maxConnections = 4;
        public float wireReach = 100f;
        public Vector3 connectionPointOffset = new Vector3(0, 5, 0);
        public bool isHighVoltage = true;

        [Header("Visuals")]
        public float wireWidth = 0.05f;
        public Material wireMaterial;

        public float maxThroughputWatts = 50000f;
        public float conversionLoss = 0.02f;

        protected List<IVoltageStation> _connectedStations = new();
        protected Dictionary<IVoltageStation, LineRenderer> _wireRenderers = new();

        public Vector3 ConnectionPoint => transform.position + transform.TransformDirection(connectionPointOffset);
        public Transform StationTransform => transform;
        public virtual bool CanConnectMore => _connectedStations.Count < maxConnections;
        public bool IsHighVoltage => isHighVoltage;

        public abstract float TotalProduced { get; }
        public abstract float TotalConsumed { get; }
        public abstract float MaxCapacity { get; }
        public float CurrentPower => TotalProduced;

        protected virtual void Awake()
        {
            if (wireMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                wireMaterial = new Material(shader);
                wireMaterial.color = Color.black;
            }
        }

        public virtual void AddConnection(IVoltageStation other) => AddConnection(other, 1000000000f);

        public virtual void AddConnection(IVoltageStation other, float capacity)
        {
            if (other == null || other == (IVoltageStation)this) return;
            if (_connectedStations.Contains(other)) return;

            var myNode = GetComponent<PowerNode>();
            var otherNode = other.StationTransform != null
                ? other.StationTransform.GetComponent<PowerNode>()
                : null;
            bool graphEdgeAlreadyCreated = myNode != null && otherNode != null
                && myNode.manualLinks.Contains(otherNode);

            // The wire tool preflights both endpoints before it calls either side.
            // When this is the SECOND reciprocal call, the first side has already
            // inserted the shared PowerNode manual edge; do not reject that normal
            // bookkeeping step merely because the first connector is now full.
            if (!CanConnectMore || (!graphEdgeAlreadyCreated && !other.CanConnectMore)) return;

            _connectedStations.Add(other);

            if (myNode != null && otherNode != null)
            {
                if (!myNode.manualLinks.Contains(otherNode)) myNode.manualLinks.Add(otherNode);
                myNode.manualLinkCapacities[otherNode] = capacity;

                if (!otherNode.manualLinks.Contains(myNode)) otherNode.manualLinks.Add(myNode);
                otherNode.manualLinkCapacities[myNode] = capacity;

                PowerNetworkManager.Instance?.SetDirty();
            }

            UpdateWireVisuals();
        }

        public virtual void RemoveConnection(IVoltageStation other)
        {
            if (_connectedStations.Remove(other))
            {
                var myNode = GetComponent<PowerNode>();
                if (myNode != null && other.StationTransform != null)
                {
                    var otherNode = other.StationTransform.GetComponent<PowerNode>();
                    if (otherNode != null)
                    {
                        myNode.manualLinks.Remove(otherNode);
                        myNode.manualLinkCapacities.Remove(otherNode);
                        otherNode.manualLinks.Remove(myNode);
                        otherNode.manualLinkCapacities.Remove(myNode);
                        PowerNetworkManager.Instance?.SetDirty();
                    }
                }

                if (_wireRenderers.TryGetValue(other, out var lr))
                {
                    if (lr != null && lr.GetComponent<OverheatedManualWire>() == null)
                        Destroy(lr.gameObject);
                    _wireRenderers.Remove(other);
                }
            }
        }

        protected virtual void Update()
        {
            foreach (var kvp in _wireRenderers)
            {
                if (kvp.Key != null)
                {
                    DrawCatenary(kvp.Value, ConnectionPoint, kvp.Key.ConnectionPoint);
                }
            }
        }

        /// <summary>
        /// Detaches the owned manual wire visual into world space and turns it
        /// red-hot. This lets the short overload warning survive even when the
        /// connector at one end is destroyed immediately afterwards.
        /// </summary>
        internal void BeginManualWireOverload(IVoltageStation other, float seconds)
        {
            if (other == null) return;
            LineRenderer line = null;
            if (_wireRenderers.TryGetValue(other, out var ownedLine) && ownedLine != null)
            {
                line = ownedLine;
                _wireRenderers.Remove(other);
            }
            else if (other is VoltageStationBase otherBase && otherBase._wireRenderers.TryGetValue(this, out var otherLine) && otherLine != null)
            {
                line = otherLine;
                otherBase._wireRenderers.Remove(this);
            }
            if (line == null) return;

            line.transform.SetParent(null, true);
            var heat = line.GetComponent<OverheatedManualWire>();
            if (heat == null) heat = line.gameObject.AddComponent<OverheatedManualWire>();
            heat.Begin(seconds);
        }

        protected void UpdateWireVisuals()
        {
            foreach (var other in _connectedStations)
            {
                if (other == null) continue;
                if (!_wireRenderers.ContainsKey(other))
                {
                    if (other is VoltageStationBase otherBase && otherBase._wireRenderers.ContainsKey(this))
                        continue;

                    var wireGo = new GameObject("Wire_" + other.StationTransform.name);
                    wireGo.transform.SetParent(transform);
                    var lr = wireGo.AddComponent<LineRenderer>();
                    lr.startWidth = wireWidth;
                    lr.endWidth = wireWidth;
                    lr.material = wireMaterial;
                    lr.positionCount = 20;
                    lr.useWorldSpace = true;
                    _wireRenderers[other] = lr;
                }
            }
        }

        private static void DrawCatenary(LineRenderer lr, Vector3 a, Vector3 b)
        {
            int segments = lr.positionCount;
            float dist = Vector3.Distance(a, b);
            float sag = dist * 0.05f;

            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / (segments - 1);
                Vector3 pos = Vector3.Lerp(a, b, t);
                pos.y -= sag * 4f * t * (1f - t);
                lr.SetPosition(i, pos);
            }
        }

        private void OnDestroy()
        {
            foreach (var other in _connectedStations)
            {
                if (other != null) other.RemoveConnection(this);
            }
        }
    }

    [DisallowMultipleComponent]
    internal sealed class OverheatedManualWire : MonoBehaviour
    {
        private LineRenderer _line;
        private Material _heatMaterial;
        private float _destroyAt;

        public void Begin(float seconds)
        {
            _destroyAt = Mathf.Max(_destroyAt, Time.time + Mathf.Max(0.1f, seconds));
            if (_line == null) _line = GetComponent<LineRenderer>();
            if (_line == null) return;

            if (_heatMaterial == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _heatMaterial = new Material(sh);
                _heatMaterial.color = new Color(1f, 0.12f, 0.02f, 1f);
                if (_heatMaterial.HasProperty("_BaseColor"))
                    _heatMaterial.SetColor("_BaseColor", new Color(1f, 0.12f, 0.02f, 1f));
                _heatMaterial.EnableKeyword("_EMISSION");
                _heatMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                _heatMaterial.SetColor("_EmissionColor", new Color(4.5f, 0.35f, 0.02f, 1f));
                _line.material = _heatMaterial;
            }
            _line.startColor = new Color(1f, 0.25f, 0.05f, 1f);
            _line.endColor = new Color(1f, 0.25f, 0.05f, 1f);
        }

        private void Update()
        {
            if (_line != null && _heatMaterial != null)
            {
                float pulse = 3.0f + Mathf.PingPong(Time.time * 8f, 3.5f);
                if (_heatMaterial.HasProperty("_EmissionColor"))
                    _heatMaterial.SetColor("_EmissionColor", new Color(pulse, pulse * 0.18f, 0.02f, 1f));
            }
            if (Time.time >= _destroyAt) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_heatMaterial != null) Destroy(_heatMaterial);
        }
    }
}
