// Assets/Scripts/VoxelEngine/GridSystem/GridPrecisionLatticePreview.cs
//
// Reusable cyan 5x5 placement lattice displayed over a large-grid face while
// placing supported small-grid structural details.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelEngine.GridSystem
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class GridPrecisionLatticePreview : MonoBehaviour
    {
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private Material _material;

        private void Awake()
        {
            EnsureObjects();
        }

        public void Show(GridEntity grid, Vector3Int largeCell, Vector3Int faceAxis)
        {
            if (grid == null || grid.gridSize != GridSize.Large)
            {
                Hide();
                return;
            }

            EnsureObjects();
            transform.SetParent(grid.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            float largeSize = GridSize.Large.CellSize();
            float smallSize = GridSize.Small.CellSize();
            float half = largeSize * 0.5f;
            Vector3 normal = ((Vector3)faceAxis).normalized;
            if (normal.sqrMagnitude < 0.5f) normal = Vector3.up;

            Vector3 axisU;
            Vector3 axisV;
            if (Mathf.Abs(normal.x) > 0.5f)
            {
                axisU = Vector3.up;
                axisV = Vector3.forward;
            }
            else if (Mathf.Abs(normal.y) > 0.5f)
            {
                axisU = Vector3.right;
                axisV = Vector3.forward;
            }
            else
            {
                axisU = Vector3.right;
                axisV = Vector3.up;
            }

            Vector3 center = (Vector3)largeCell * largeSize + normal * (half + 0.008f);
            var vertices = new List<Vector3>(24);
            var indices = new List<int>(24);
            int divisions = Mathf.RoundToInt(largeSize / smallSize);

            for (int i = 0; i <= divisions; i++)
            {
                float offset = -half + i * smallSize;
                AddLine(vertices, indices, center + axisU * offset - axisV * half, center + axisU * offset + axisV * half);
                AddLine(vertices, indices, center + axisV * offset - axisU * half, center + axisV * offset + axisU * half);
            }

            _mesh.Clear();
            _mesh.SetVertices(vertices);
            _mesh.SetIndices(indices, MeshTopology.Lines, 0, true);
            _mesh.RecalculateBounds();
            _filter.sharedMesh = _mesh;
            _renderer.enabled = true;
        }

        public void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        private void EnsureObjects()
        {
            // Do not use ?? with UnityEngine.Object components: destroyed/missing Unity
            // objects use overloaded null semantics and can leave a marshalled null wrapper.
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
                _mesh = new Mesh { name = "PrecisionLatticeMesh", hideFlags = HideFlags.HideAndDontSave };
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
                    name = "PrecisionLatticeMaterial",
                    color = new Color(0.10f, 0.78f, 1f, 0.92f),
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", _material.color);
                if (_material.HasProperty("_EmissionColor"))
                {
                    _material.EnableKeyword("_EMISSION");
                    _material.SetColor("_EmissionColor", new Color(0.04f, 0.50f, 0.90f) * 1.5f);
                }
                _material.renderQueue = 3100;
                _renderer.sharedMaterial = _material;
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
            }
        }

        private static void AddLine(List<Vector3> vertices, List<int> indices, Vector3 a, Vector3 b)
        {
            int first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            indices.Add(first);
            indices.Add(first + 1);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }
    }
}
