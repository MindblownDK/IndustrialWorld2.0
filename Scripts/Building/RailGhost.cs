// Assets/Scripts/VoxelEngine/Building/RailGhost.cs
//
// The live preview for the rail layer.
//
// WHY THIS IS NEEDED AND NOT OPTIONAL
// Between the two clicks of a drag the player is committing to a route they cannot see.
// Without a preview the tool is "click twice and find out", and every refusal - too
// steep, corner too tight, off the terrain - arrives only after the second click, with
// no way to adjust before spending materials.
//
// The ghost is built from THE SAME PLAN the commit will use, not from a separate
// approximation. That is the important property: if the ghost shows a route, the commit
// lays that route. A preview that is computed differently from the thing it previews is
// worse than none, because it teaches the player to trust something that can lie.
//
// It also colours itself from the plan's own verdict, so an unlayable run is visibly red
// while the player is still aiming rather than a message after the fact.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    /// <summary>
    /// Draws the pending rail run. One hidden mesh reused every frame - a ghost that
    /// allocated per frame would be a garbage source on a long drag.
    /// </summary>
    public static class RailGhost
    {
        private static GameObject _ghost;
        private static MeshFilter _filter;
        private static MeshRenderer _renderer;
        private static Material _material;
        private static Mesh _mesh;

        private static readonly List<Vector3> _positions = new(2048);
        private static readonly List<int> _triangles = new(3072);
        private static readonly List<Color> _colors = new(2048);

        private static readonly Color Good = new(0.30f, 0.85f, 1.00f, 0.55f);
        private static readonly Color Bad = new(1.00f, 0.35f, 0.25f, 0.55f);

        /// <summary>Half-width of the drawn sleeper, in metres.</summary>
        private const float HalfWidth = 0.62f;

        /// <summary>Lift above the planned cell so the ghost is not buried in terrain.</summary>
        private const float Lift = 0.28f;

        /// <summary>
        /// Shows the run described by <paramref name="plan"/>.
        ///
        /// A refused plan still draws, in red, using the cells it managed to solve. Hiding
        /// it would leave the player aiming blind at exactly the moment they need to see
        /// what is wrong.
        /// </summary>
        public static void Show(RailPlan plan)
        {
            if (plan == null || plan.cells.Count == 0) { Hide(); return; }

            EnsureGhost();
            if (_ghost == null) return;

            _positions.Clear();
            _triangles.Clear();
            _colors.Clear();

            Color tint = plan.IsPlaceable ? Good : Bad;

            for (int i = 0; i < plan.cells.Count; i++)
            {
                var cell = plan.cells[i];
                Vector3 right = cell.rotation * Vector3.right * HalfWidth;
                Vector3 forward = cell.rotation * Vector3.forward * 0.42f;
                Vector3 up = cell.rotation * Vector3.up * Lift;
                Vector3 centre = cell.position + up;

                int b = _positions.Count;
                _positions.Add(centre - right - forward);
                _positions.Add(centre + right - forward);
                _positions.Add(centre + right + forward);
                _positions.Add(centre - right + forward);
                for (int v = 0; v < 4; v++) _colors.Add(tint);

                _triangles.Add(b); _triangles.Add(b + 1); _triangles.Add(b + 2);
                _triangles.Add(b); _triangles.Add(b + 2); _triangles.Add(b + 3);
            }

            _mesh.Clear();
            // A long multi-lane run exceeds the 16-bit index limit, and a silently truncated
            // preview would show a shorter route than the commit lays.
            _mesh.indexFormat = _positions.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(_positions);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.SetColors(_colors);
            _mesh.RecalculateBounds();

            _ghost.SetActive(true);
        }

        public static void Hide()
        {
            if (_ghost != null) _ghost.SetActive(false);
        }

        private static void EnsureGhost()
        {
            if (_ghost != null) return;

            _ghost = new GameObject("RailGhost") { hideFlags = HideFlags.HideAndDontSave };
            _filter = _ghost.AddComponent<MeshFilter>();
            _renderer = _ghost.AddComponent<MeshRenderer>();

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                // Never cull: the preview must survive being viewed from below a cutting.
                if (_material.HasProperty("_Cull")) _material.SetFloat("_Cull", 0f);
                // Transparent, so the ghost reads as a projection rather than as built track.
                if (_material.HasProperty("_Surface")) _material.SetFloat("_Surface", 1f);
                _renderer.material = _material;
            }

            _mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "RailGhost" };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;

            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _ghost.SetActive(false);
        }
    }
}
