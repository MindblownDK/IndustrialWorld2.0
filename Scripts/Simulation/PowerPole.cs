using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    public class PowerPole : VoltageStationBase, IVoltageStation
    {
        [Header("Pole Configuration")]
        // ... (rest of the fields)

        // IVoltageStation implementation
        public override float TotalProduced => _powerNode != null && _powerNode.network != null ? _powerNode.network.producedThisTick : 0f;
        public override float TotalConsumed => _powerNode != null && _powerNode.network != null ? _powerNode.network.consumedThisTick : 0f;
        public override float MaxCapacity => _powerNode != null && _powerNode.network != null ? _powerNode.network.bottleneckWatts : 0f;

        private PowerNode _powerNode;
        private LineRenderer[] _poleWireRenderers; // Renamed to avoid conflict

        protected override void Awake()
        {
            base.Awake();
            _powerNode = GetComponent<PowerNode>();
            if (_powerNode == null) _powerNode = gameObject.AddComponent<PowerCable>();
            
            isHighVoltage = false;
            connectionPointOffset = new Vector3(0, poleHeight, 0);
            wireReach = 15f;

            _poleWireRenderers = new LineRenderer[maxConnections];
        }

        public bool TryConnect(PowerPole target)
        {
            if (target == null || target == this) return false;
            if (!CanConnectMore || !target.CanConnectMore) return false;

            float dist = Vector3.Distance(transform.position, target.transform.position);
            if (dist > wireReach) return false;

            foreach (var c in _connections) if (c.target == target) return false;

            _connections.Add(new PowerPoleConnection { target = target, distance = dist, isActive = true });
            target._connections.Add(new PowerPoleConnection { target = this, distance = dist, isActive = true });

            UpdateWireVisuals();
            target.UpdateWireVisuals();
            return true;
        }

        public void Disconnect(PowerPole target)
        {
            _connections.RemoveAll(c => c.target == target);
            target._connections.RemoveAll(c => c.target == this);
            UpdateWireVisuals();
            target.UpdateWireVisuals();
        }

        private void UpdateWireVisuals()
        {
            for (int i = 0; i < maxConnections; i++)
            {
                if (i < _connections.Count && _connections[i].target != null)
                {
                    if (_wireRenderers[i] == null)
                    {
                        var wireGo = new GameObject($"Wire_{i}");
                        wireGo.transform.SetParent(transform, false);
                        _wireRenderers[i] = wireGo.AddComponent<LineRenderer>();
                        _wireRenderers[i].positionCount = 12;
                        _wireRenderers[i].startWidth = 0.03f;
                        _wireRenderers[i].endWidth = 0.03f;
                        _wireRenderers[i].useWorldSpace = true;
                        
                        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                        var mat = new Material(shader);
                        mat.color = new Color(0.15f, 0.15f, 0.15f);
                        _wireRenderers[i].material = mat;
                    }
                    DrawCatenary(_wireRenderers[i], ConnectionPoint, _connections[i].target.ConnectionPoint);
                    _wireRenderers[i].gameObject.SetActive(true);
                }
                else if (_wireRenderers[i] != null)
                {
                    _wireRenderers[i].gameObject.SetActive(false);
                }
            }
        }

        private static void DrawCatenary(LineRenderer lr, Vector3 a, Vector3 b)
        {
            int segments = lr.positionCount;
            float dist = Vector3.Distance(a, b);
            float sag = dist * 0.08f;
            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / (segments - 1);
                Vector3 pos = Vector3.Lerp(a, b, t);
                pos.y -= sag * 4f * t * (1f - t);
                lr.SetPosition(i, pos);
            }
        }
    }

    [System.Serializable]
    public struct PowerPoleConnection
    {
        public PowerPole target;
        public float distance;
        public bool isActive;
    }
}
