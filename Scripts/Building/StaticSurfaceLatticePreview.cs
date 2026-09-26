// Assets/Scripts/VoxelEngine/Building/StaticSurfaceLatticePreview.cs
//
// Reusable world-space surface lattice for small utility pieces mounted to
// ordinary static blocks. It deliberately has no collider and no persistence:
// BuildSystem owns its lifetime as a placement-only guide.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelEngine.Building
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class StaticSurfaceLatticePreview : MonoBehaviour
    {
        private readonly List<Vector3> _vertices = new(64);
        private readonly List<int> _indices = new(64);
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private Material _material;

        private void Awake() => EnsureObjects();

        /// <summary>Draws a small cyan placement lattice directly above the selected
        /// static collider face. The supplied basis is world-space and orthonormal.</summary>
        public void Show(Vector3 center, Vector3 axisU, Vector3 axisV,
            float halfU, float halfV, float spacing)
        {
            if (halfU <= 0.001f || halfV <= 0.001f || spacing <= 0.001f)
            {
                Hide();
                return;
            }

            EnsureObjects();
            transform.SetParent(null, true);
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;

            axisU = axisU.sqrMagnitude > 0.0001f ? axisU.normalized : Vector3.right;
            axisV = Vector3.ProjectOnPlane(axisV, axisU);
            if (axisV.sqrMagnitude <= 0.0001f) axisV = Vector3.Cross(axisU, Vector3.up);
            if (axisV.sqrMagnitude <= 0.0001f) axisV = Vector3.Cross(axisU, Vector3.forward);
            axisV.Normalize();

            // A very large authored face should remain readable and inexpensive.
            int divisionsU = Mathf.Clamp(Mathf.RoundToInt((halfU * 2f) / spacing), 1, 64);
            int divisionsV = Mathf.Clamp(Mathf.RoundToInt((halfV * 2f) / spacing), 1, 64);
            float stepU = (halfU * 2f) / divisionsU;
            float stepV = (halfV * 2f) / divisionsV;

            _vertices.Clear();
            _indices.Clear();
            for (int i = 0; i <= divisionsU; i++)
            {
                float offset = -halfU + i * stepU;
                AddLine(center + axisU * offset - axisV * halfV,
                    center + axisU * offset + axisV * halfV);
            }
            for (int i = 0; i <= divisionsV; i++)
            {
                float offset = -halfV + i * stepV;
                AddLine(center + axisV * offset - axisU * halfU,
                    center + axisV * offset + axisU * halfU);
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetIndices(_indices, MeshTopology.Lines, 0, true);
            _mesh.RecalculateBounds();
            _filter.sharedMesh = _mesh;
            _renderer.enabled = true;
        }

        public void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        private void AddLine(Vector3 a, Vector3 b)
        {
            int first = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _indices.Add(first);
            _indices.Add(first + 1);
        }

        private void EnsureObjects()
        {
            if (!_filter)
            {
                _filter = GetComponent<MeshFilter>();
                if (!_filter) _filter = gameObject.AddComponent<MeshFilter>();
            }
            if (!_renderer)
            {
                _renderer = GetComponent<MeshRenderer>();
                if (!_renderer) _renderer = gameObject.AddComponent<MeshRenderer>();
            }
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "StaticSurfaceLatticeMesh", hideFlags = HideFlags.HideAndDontSave };
                _filter.sharedMesh = _mesh;
            }
            if (_material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard");
                _material = new Material(shader)
                {
                    name = "StaticSurfaceLatticeMaterial",
                    color = new Color(0.12f, 0.78f, 1f, 0.94f),
                    hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = 3100
                };
                if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", _material.color);
                if (_material.HasProperty("_EmissionColor"))
                {
                    _material.EnableKeyword("_EMISSION");
                    _material.SetColor("_EmissionColor", new Color(0.05f, 0.48f, 0.90f) * 1.45f);
                }
                _renderer.sharedMaterial = _material;
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }
    }
}
