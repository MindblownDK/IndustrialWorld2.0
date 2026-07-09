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

        // Fields expected by Setup Wizard
        public float maxThroughputWatts = 50000f;
        public float conversionLoss = 0.02f;

        protected List<IVoltageStation> _connectedStations = new();
        protected Dictionary<IVoltageStation, LineRenderer> _wireRenderers = new();

        public Vector3 ConnectionPoint => transform.position + transform.TransformDirection(connectionPointOffset);
        public Transform StationTransform => transform;
        public bool CanConnectMore => _connectedStations.Count < maxConnections;
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

        public virtual void AddConnection(IVoltageStation other)
        {
            if (other == null || other == (IVoltageStation)this) return;
            if (_connectedStations.Contains(other)) return;
            if (!CanConnectMore) return;

            _connectedStations.Add(other);
            
            var myNode = GetComponent<PowerNode>();
            if (myNode != null && other.StationTransform != null)
            {
                var otherNode = other.StationTransform.GetComponent<PowerNode>();
                if (otherNode != null)
                {
                    if (!myNode.manualLinks.Contains(otherNode)) myNode.manualLinks.Add(otherNode);
                    if (!otherNode.manualLinks.Contains(myNode)) otherNode.manualLinks.Add(myNode);
                    PowerNetworkManager.Instance?.SetDirty();
                }
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
                        otherNode.manualLinks.Remove(myNode);
                        PowerNetworkManager.Instance?.SetDirty();
                    }
                }

                if (_wireRenderers.TryGetValue(other, out var lr))
                {
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
}
