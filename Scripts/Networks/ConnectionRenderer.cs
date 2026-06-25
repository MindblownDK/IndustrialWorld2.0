// Assets/Scripts/VoxelEngine/Networks/ConnectionRenderer.cs
//
// Draws visual lines between connected anchors. Updates when connections change.
// Uses LineRenderer for each connection.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Networks
{
    [RequireComponent(typeof(ConnectionAnchor))]
    public class ConnectionRenderer : MonoBehaviour
    {
        [Header("Visual")]
        public Color lineColor = new Color(0.7f, 0.4f, 0.2f);
        public float lineWidth = 0.06f;

        private ConnectionAnchor _anchor;
        private readonly List<LineRenderer> _lines = new();
        private int _lastCount = -1;

        private void Awake() { _anchor = GetComponent<ConnectionAnchor>(); }

        private void LateUpdate()
        {
            if (_anchor == null) return;
            int count = _anchor.connections.Count;
            if (count == _lastCount) return;
            _lastCount = count;
            Rebuild();
        }

        private void Rebuild()
        {
            foreach (var lr in _lines) if (lr != null) Destroy(lr.gameObject);
            _lines.Clear();

            foreach (var other in _anchor.connections)
            {
                if (other == null) continue;
                // Only draw from lower instance ID to avoid duplicates.
                if (other.GetHashCode() < _anchor.GetHashCode()) continue;

                var go = new GameObject("Wire");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.SetPosition(0, transform.position);
                lr.SetPosition(1, other.transform.position);
                lr.startWidth = lineWidth; lr.endWidth = lineWidth;

                Color c = _anchor.networkType switch
                {
                    NetworkType.Power => _anchor.powerTier switch
                    {
                        PowerTier.Low    => new Color(0.8f, 0.5f, 0.2f),
                        PowerTier.Medium => new Color(0.9f, 0.8f, 0.2f),
                        PowerTier.High   => new Color(0.3f, 0.7f, 1.0f),
                        _ => lineColor
                    },
                    NetworkType.Fluid => new Color(0.2f, 0.5f, 0.9f),
                    NetworkType.Gas   => new Color(0.7f, 0.7f, 0.75f),
                    NetworkType.Data  => new Color(0.3f, 0.9f, 0.4f),
                    _ => lineColor
                };

                lr.startColor = c; lr.endColor = c;
                lr.useWorldSpace = true;
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Sprites/Default");
                lr.material = new Material(sh) { color = c };
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                _lines.Add(lr);
            }
        }

        private void OnDestroy()
        {
            foreach (var lr in _lines) if (lr != null) Destroy(lr.gameObject);
        }
    }
}
