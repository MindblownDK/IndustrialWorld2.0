// Assets/Scripts/VoxelEngine/Building/Tiered/TieredRailing.cs
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>
    /// Shears an instance of the authored level railing along its length for a stair edge.
    /// A shear raises each station without rotating the posts, so posts remain vertical
    /// while both rails follow the flight. Shared mesh assets are never modified.
    /// </summary>
    public sealed class TieredRailing : MonoBehaviour
    {
        private readonly List<MeshFilter> _filters = new();
        private readonly List<Vector3[]> _levelVertices = new();
        private float _appliedRise = float.NaN;

        public void Configure(float riseAcrossLength)
        {
            if (Mathf.Approximately(_appliedRise, riseAcrossLength)) return;
            EnsureInstances();
            _appliedRise = riseAcrossLength;

            for (int i = 0; i < _filters.Count; i++)
            {
                Mesh mesh = _filters[i] != null ? _filters[i].mesh : null;
                if (mesh == null) continue;
                Vector3[] source = _levelVertices[i];
                var vertices = new Vector3[source.Length];
                for (int v = 0; v < source.Length; v++)
                {
                    Vector3 p = source[v];
                    p.y += Mathf.InverseLerp(-3.75f, 3.75f, p.x) * riseAcrossLength;
                    vertices[v] = p;
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
            }
        }

        private void EnsureInstances()
        {
            if (_filters.Count > 0) return;
            GetComponentsInChildren(true, _filters);
            for (int i = 0; i < _filters.Count; i++)
            {
                MeshFilter filter = _filters[i];
                if (filter == null || filter.sharedMesh == null)
                {
                    _levelVertices.Add(System.Array.Empty<Vector3>());
                    continue;
                }
                Mesh instance = Instantiate(filter.sharedMesh);
                instance.name = filter.sharedMesh.name + "_RailingInstance";
                filter.sharedMesh = instance;
                _levelVertices.Add(instance.vertices);
            }
        }
    }
}
