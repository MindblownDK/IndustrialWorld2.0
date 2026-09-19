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
        // Two separate meshes rather than one vertex-coloured mesh.
        //
        // THE BUG (11.38.0): the ghost set vertex colours and used URP/Unlit, which does
        // NOT read them - so every preview rendered flat white and the green/red verdict
        // was invisible. Exactly the same mistake as the asteroid material in 11.28.1.
        //
        // Splitting valid and invalid cells into two meshes with real material colours
        // needs no special shader, so it cannot silently stop working if the render
        // pipeline changes.
        private static GameObject _root;
        private static GhostLayer _validLayer;
        private static GhostLayer _invalidLayer;

        private sealed class GhostLayer
        {
            public MeshFilter filter;
            public MeshRenderer renderer;
            public Mesh mesh;
            public readonly List<Vector3> positions = new(2048);
            public readonly List<int> triangles = new(3072);
        }

        // Opaque, and green rather than cyan. The URP/Unlit fallback is an OPAQUE shader,
        // so an alpha of 0.55 was simply ignored - which is another reason the old ghost
        // read as a solid white slab rather than a translucent hint.
        private static readonly Color Good = new(0.25f, 0.90f, 0.35f);
        private static readonly Color Bad = new(0.95f, 0.25f, 0.20f);

        /// <summary>Half-width of the drawn sleeper when the caller has no template to measure.</summary>
        private const float DefaultHalfWidth = 0.62f;

        /// <summary>Lift above the planned cell so the ghost is not buried in terrain.</summary>
        private const float Lift = 0.28f;

        /// <summary>
        /// Shows the run described by <paramref name="plan"/>.
        ///
        /// A refused plan still draws, in red, using the cells it managed to solve. Hiding
        /// it would leave the player aiming blind at exactly the moment they need to see
        /// what is wrong.
        /// </summary>
        /// <param name="halfWidth">
        /// Half the formation width the commit will lay, measured off the track prefab by the
        /// caller. The preview must be as wide as the thing it previews or the player aims a
        /// ribbon and lays a causeway.
        /// </param>
        public static void Show(RailPlan plan, float halfWidth = DefaultHalfWidth)
        {
            if (plan == null || plan.cells.Count == 0) { Hide(); return; }

            halfWidth = Mathf.Clamp(halfWidth, 0.1f, 8f);

            EnsureGhost();
            if (_root == null) return;

            _validLayer.positions.Clear(); _validLayer.triangles.Clear();
            _invalidLayer.positions.Clear(); _invalidLayer.triangles.Clear();

            for (int i = 0; i < plan.cells.Count; i++)
            {
                var cell = plan.cells[i];

                // Per CELL, not per run. A route that clips one rock shows one red cell the
                // player can nudge around, instead of turning the whole line red and leaving
                // them to guess which end is the problem.
                var layer = cell.valid ? _validLayer : _invalidLayer;

                Vector3 right = cell.rotation * Vector3.right * halfWidth;
                Vector3 forward = cell.rotation * Vector3.forward * 0.42f;
                Vector3 up = cell.rotation * Vector3.up * Lift;
                Vector3 centre = cell.position + up;

                int b = layer.positions.Count;
                layer.positions.Add(centre - right - forward);
                layer.positions.Add(centre + right - forward);
                layer.positions.Add(centre + right + forward);
                layer.positions.Add(centre - right + forward);

                layer.triangles.Add(b); layer.triangles.Add(b + 1); layer.triangles.Add(b + 2);
                layer.triangles.Add(b); layer.triangles.Add(b + 2); layer.triangles.Add(b + 3);
            }

            Upload(_validLayer);
            Upload(_invalidLayer);
            _root.SetActive(true);
        }

        private static void Upload(GhostLayer layer)
        {
            layer.mesh.Clear();

            if (layer.positions.Count == 0)
            {
                layer.renderer.enabled = false;
                return;
            }

            // A long multi-lane run exceeds the 16-bit index limit, and a silently truncated
            // preview would show a shorter route than the commit lays.
            layer.mesh.indexFormat = layer.positions.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            layer.mesh.SetVertices(layer.positions);
            layer.mesh.SetTriangles(layer.triangles, 0);
            layer.mesh.RecalculateBounds();
            layer.renderer.enabled = true;
        }

        public static void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        private static void EnsureGhost()
        {
            if (_root != null) return;

            _root = new GameObject("RailGhost") { hideFlags = HideFlags.HideAndDontSave };
            _validLayer = CreateLayer("Valid", Good);
            _invalidLayer = CreateLayer("Invalid", Bad);
            _root.SetActive(false);
        }

        private static GhostLayer CreateLayer(string name, Color color)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(_root.transform, false);

            var layer = new GhostLayer
            {
                filter = go.AddComponent<MeshFilter>(),
                renderer = go.AddComponent<MeshRenderer>(),
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "RailGhost_" + name },
            };

            layer.mesh.MarkDynamic();
            layer.filter.sharedMesh = layer.mesh;

            // Colour lives on the MATERIAL, not in vertex colours: URP/Unlit ignores vertex
            // colour, which is what made the previous ghost render flat white.
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            material.color = color;
            // Never cull: the preview must survive being viewed from below a cutting.
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);

            layer.renderer.sharedMaterial = material;
            layer.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            layer.renderer.receiveShadows = false;

            return layer;
        }
    }
}
