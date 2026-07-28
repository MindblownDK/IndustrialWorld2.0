// Assets/Scripts/VoxelEngine/GridSystem/GridShapeVariantBlock.cs
//
// Runtime visual and collision representation for structural grid shape variants.
// The source block item, recipe, mass, health, and authored prefab tuning remain intact.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.UI;

namespace VoxelEngine.GridSystem
{
    [DisallowMultipleComponent]
    public sealed class GridShapeVariantBlock : MonoBehaviour
    {
        private const string GeneratedRootName = "Generated_ShapeVariantMesh";

        [SerializeField] private GridShapeVariant variant = GridShapeVariant.Cube;
        [SerializeField] private GridSize gridSize = GridSize.Large;

        private readonly List<Renderer> _authoredRenderers = new();
        private readonly List<Collider> _authoredColliders = new();
        private GameObject _generatedRoot;
        private Mesh _generatedMesh;

        public GridShapeVariant Variant => variant;
        public float VolumeScale => GetVolumeScale(variant);

        /// <summary>
        /// Applies a selected shape without changing the source prefab or gameplay balance.
        /// Repeated calls are safe and rebuild only this component's generated child.
        /// </summary>
        public void Configure(GridShapeVariant selectedVariant, GridSize selectedGridSize, bool createCollider = true)
        {
            variant = selectedVariant;
            gridSize = selectedGridSize;
            CacheAuthoredParts();
            ClearGenerated();

            bool useGeneratedShape = variant != GridShapeVariant.Cube;
            SetAuthoredPartsEnabled(!useGeneratedShape);
            if (!useGeneratedShape) return;

            _generatedMesh = CreateMesh(variant, Mathf.Max(0.01f, gridSize.CellSize()));
            _generatedMesh.name = $"GridShape_{variant}_{gridSize}";

            _generatedRoot = new GameObject(GeneratedRootName);
            _generatedRoot.transform.SetParent(transform, false);
            _generatedRoot.layer = gameObject.layer;

            var filter = _generatedRoot.AddComponent<MeshFilter>();
            filter.sharedMesh = _generatedMesh;

            var renderer = _generatedRoot.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ResolveAuthoredMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            if (createCollider)
            {
                var collider = _generatedRoot.AddComponent<MeshCollider>();
                collider.sharedMesh = _generatedMesh;
                collider.convex = true;
            }
        }

        private void CacheAuthoredParts()
        {
            _authoredRenderers.Clear();
            _authoredColliders.Clear();

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer != null && renderer.transform.name != GeneratedRootName)
                    _authoredRenderers.Add(renderer);

            foreach (var collider in GetComponentsInChildren<Collider>(true))
                if (collider != null && collider.transform.name != GeneratedRootName)
                    _authoredColliders.Add(collider);
        }

        private Material ResolveAuthoredMaterial()
        {
            for (int i = 0; i < _authoredRenderers.Count; i++)
            {
                var renderer = _authoredRenderers[i];
                if (renderer != null && renderer.sharedMaterial != null)
                    return renderer.sharedMaterial;
            }

            // Ghosts intentionally start without a material; GridBuilder applies its
            // shared translucent preview material immediately after construction.
            return null;
        }

        private void SetAuthoredPartsEnabled(bool enabled)
        {
            for (int i = 0; i < _authoredRenderers.Count; i++)
                if (_authoredRenderers[i] != null) _authoredRenderers[i].enabled = enabled;

            for (int i = 0; i < _authoredColliders.Count; i++)
                if (_authoredColliders[i] != null) _authoredColliders[i].enabled = enabled;
        }

        private void ClearGenerated()
        {
            Transform existing = transform.Find(GeneratedRootName);
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }
            _generatedRoot = null;

            if (_generatedMesh != null)
            {
                if (Application.isPlaying) Destroy(_generatedMesh);
                else DestroyImmediate(_generatedMesh);
                _generatedMesh = null;
            }
        }

        private void OnDestroy()
        {
            if (_generatedMesh == null) return;
            if (Application.isPlaying) Destroy(_generatedMesh);
            else DestroyImmediate(_generatedMesh);
            _generatedMesh = null;
        }

        public static float GetVolumeScale(GridShapeVariant shape)
        {
            return shape switch
            {
                GridShapeVariant.HalfBlock => 0.5f,
                GridShapeVariant.HalfSlope => 0.25f,
                GridShapeVariant.Slope => 0.5f,
                GridShapeVariant.InvertedSlope => 0.5f,
                GridShapeVariant.Corner => 1f / 3f,
                _ => 1f
            };
        }

        /// <summary>Creates a closed, convex, cell-aligned mesh suitable for rendering and collision.</summary>
        public static Mesh CreateMesh(GridShapeVariant shape, float cellSize)
        {
            float h = cellSize * 0.5f;
            var vertices = new List<Vector3>(12);
            var triangles = new List<int>(36);

            switch (shape)
            {
                case GridShapeVariant.HalfBlock:
                    AddBox(vertices, triangles, new Vector3(-h, -h, -h), new Vector3(h, 0f, h));
                    break;
                case GridShapeVariant.HalfSlope:
                    AddWedge(vertices, triangles, h, -h, 0f, false);
                    break;
                case GridShapeVariant.InvertedSlope:
                    AddWedge(vertices, triangles, h, -h, h, true);
                    break;
                case GridShapeVariant.Corner:
                    AddCorner(vertices, triangles, h);
                    break;
                case GridShapeVariant.Slope:
                    AddWedge(vertices, triangles, h, -h, h, false);
                    break;
                default:
                    AddBox(vertices, triangles, new Vector3(-h, -h, -h), new Vector3(h, h, h));
                    break;
            }

            if (shape == GridShapeVariant.Slope
                || shape == GridShapeVariant.HalfSlope
                || shape == GridShapeVariant.InvertedSlope
                || shape == GridShapeVariant.Corner)
            {
                ReverseWinding(triangles);
            }

            ExpandForFlatShading(vertices, triangles, cellSize,
                out var renderVertices, out var renderTriangles, out var renderUvs);

            var mesh = new Mesh();
            mesh.SetVertices(renderVertices);
            mesh.SetTriangles(renderTriangles, 0, true);
            mesh.SetUVs(0, renderUvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void ReverseWinding(List<int> triangles)
        {
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int swap = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = swap;
            }
        }

        private static void ExpandForFlatShading(
            List<Vector3> sourceVertices,
            List<int> sourceTriangles,
            float cellSize,
            out List<Vector3> vertices,
            out List<int> triangles,
            out List<Vector2> uvs)
        {
            vertices = new List<Vector3>(sourceTriangles.Count);
            triangles = new List<int>(sourceTriangles.Count);
            uvs = new List<Vector2>(sourceTriangles.Count);
            float inverseCellSize = 1f / Mathf.Max(0.01f, cellSize);

            for (int i = 0; i + 2 < sourceTriangles.Count; i += 3)
            {
                Vector3 a = sourceVertices[sourceTriangles[i]];
                Vector3 b = sourceVertices[sourceTriangles[i + 1]];
                Vector3 c = sourceVertices[sourceTriangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                int first = vertices.Count;

                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
                uvs.Add(ProjectUv(a, normal, inverseCellSize));
                uvs.Add(ProjectUv(b, normal, inverseCellSize));
                uvs.Add(ProjectUv(c, normal, inverseCellSize));
            }
        }

        private static Vector2 ProjectUv(Vector3 point, Vector3 normal, float inverseCellSize)
        {
            Vector3 absolute = new(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            if (absolute.y >= absolute.x && absolute.y >= absolute.z)
                return new Vector2(point.x, point.z) * inverseCellSize + Vector2.one * 0.5f;
            if (absolute.x >= absolute.z)
                return new Vector2(point.z, point.y) * inverseCellSize + Vector2.one * 0.5f;
            return new Vector2(point.x, point.y) * inverseCellSize + Vector2.one * 0.5f;
        }

        private static void AddBox(List<Vector3> v, List<int> t, Vector3 min, Vector3 max)
        {
            int s = v.Count;
            v.Add(new Vector3(min.x, min.y, min.z));
            v.Add(new Vector3(max.x, min.y, min.z));
            v.Add(new Vector3(max.x, max.y, min.z));
            v.Add(new Vector3(min.x, max.y, min.z));
            v.Add(new Vector3(min.x, min.y, max.z));
            v.Add(new Vector3(max.x, min.y, max.z));
            v.Add(new Vector3(max.x, max.y, max.z));
            v.Add(new Vector3(min.x, max.y, max.z));
            AddQuad(t, s + 0, s + 3, s + 2, s + 1);
            AddQuad(t, s + 4, s + 5, s + 6, s + 7);
            AddQuad(t, s + 0, s + 1, s + 5, s + 4);
            AddQuad(t, s + 3, s + 7, s + 6, s + 2);
            AddQuad(t, s + 0, s + 4, s + 7, s + 3);
            AddQuad(t, s + 1, s + 2, s + 6, s + 5);
        }

        private static void AddWedge(List<Vector3> v, List<int> t, float h, float bottom, float top, bool inverted)
        {
            int s = v.Count;
            v.Add(new Vector3(-h, bottom, -h));
            v.Add(new Vector3( h, bottom, -h));
            v.Add(new Vector3(-h, bottom,  h));
            v.Add(new Vector3( h, bottom,  h));

            if (inverted)
            {
                v.Add(new Vector3(-h, top, -h));
                v.Add(new Vector3( h, top, -h));
                AddQuad(t, s + 0, s + 2, s + 3, s + 1);
                AddQuad(t, s + 0, s + 1, s + 5, s + 4);
                AddTri(t, s + 0, s + 4, s + 2);
                AddTri(t, s + 1, s + 3, s + 5);
                AddQuad(t, s + 2, s + 4, s + 5, s + 3);
            }
            else
            {
                v.Add(new Vector3(-h, top, h));
                v.Add(new Vector3( h, top, h));
                AddQuad(t, s + 0, s + 2, s + 3, s + 1);
                AddQuad(t, s + 2, s + 4, s + 5, s + 3);
                AddTri(t, s + 0, s + 4, s + 2);
                AddTri(t, s + 1, s + 3, s + 5);
                AddQuad(t, s + 0, s + 1, s + 5, s + 4);
            }
        }

        private static void AddCorner(List<Vector3> v, List<int> t, float h)
        {
            int s = v.Count;
            v.Add(new Vector3(-h, -h, -h));
            v.Add(new Vector3( h, -h, -h));
            v.Add(new Vector3( h, -h,  h));
            v.Add(new Vector3(-h, -h,  h));
            v.Add(new Vector3( h,  h,  h));

            AddQuad(t, s + 0, s + 3, s + 2, s + 1);
            AddTri(t, s + 0, s + 1, s + 4);
            AddTri(t, s + 1, s + 2, s + 4);
            AddTri(t, s + 2, s + 3, s + 4);
            AddTri(t, s + 3, s + 0, s + 4);
        }

        private static void AddQuad(List<int> t, int a, int b, int c, int d)
        {
            AddTri(t, a, b, c);
            AddTri(t, a, c, d);
        }

        private static void AddTri(List<int> t, int a, int b, int c)
        {
            t.Add(a);
            t.Add(b);
            t.Add(c);
        }
    }
}
