// Assets/Scripts/VoxelEngine/Networks/GridCableVisuals.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║      GRID CABLE VISUALS — shared chunky-core + arms renderer    ║
// ║   Used by PowerCable, DataCable, and any future grid-aligned    ║
// ║   conduit. One central cube + up to 6 arms that extend toward   ║
// ║   connected cardinal neighbours, producing automatic visual     ║
// ║   coupling between adjacent placed cables.                      ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Networks
{
    /// <summary>
    /// Stateless helper that (re)builds a cable's chunky visual: a central core cube
    /// plus directional arm cubes for every connected neighbour. Cables call
    /// <see cref="Rebuild"/> whenever their connection set changes.
    /// </summary>
    public static class GridCableVisuals
    {
        public static readonly Vector3[] CardinalAxes =
        {
            new( 1, 0, 0), new(-1, 0, 0),
            new( 0, 1, 0), new( 0,-1, 0),
            new( 0, 0, 1), new( 0, 0,-1),
        };

        /// <summary>
        /// Rebuilds the cable's visual children under <paramref name="visualRoot"/>.
        /// Caller supplies the world-space positions of neighbours that should grow arms
        /// (the helper does the alignment match itself).
        /// </summary>
        /// <param name="visualRoot">A child transform of the cable owning the meshes.</param>
        /// <param name="cableWorldPos">World position of the cable's centre.</param>
        /// <param name="neighbourWorldPositions">Positions of connected neighbours.</param>
        /// <param name="gridSize">Grid cell size (typically 1m).</param>
        /// <param name="coreSize">Edge length of the central hub cube.</param>
        /// <param name="armThickness">Cross-section of arm cubes.</param>
        /// <param name="material">Material applied to every generated mesh.</param>
        /// <param name="showUnusedFaceCaps">Render terminator nubs on un-connected faces.</param>
        public static void Rebuild(
            Transform visualRoot,
            Vector3 cableWorldPos,
            IReadOnlyList<Vector3> neighbourWorldPositions,
            float gridSize,
            float coreSize,
            float armThickness,
            Material material,
            bool showUnusedFaceCaps)
        {
            if (visualRoot == null) return;

            // Tear down previous meshes.
            for (int i = visualRoot.childCount - 1; i >= 0; i--)
                Object.Destroy(visualRoot.GetChild(i).gameObject);

            float gs = gridSize > 0 ? gridSize : 1f;

            // 1) Central hub cube.
            BuildCube(visualRoot, "Core", Vector3.zero,
                new Vector3(coreSize, coreSize, coreSize), material);

            // 2) Arms — one per neighbour, snapped to nearest cardinal axis.
            //    Arm length stretches all the way to the neighbour's *projected* face,
            //    so a cable sitting beside a 1m gap from a multi-voxel generator
            //    still gets a visible arm that touches the generator's collider edge.
            bool[] axisUsed = new bool[6];
            if (neighbourWorldPositions != null)
            {
                for (int j = 0; j < neighbourWorldPositions.Count; j++)
                {
                    Vector3 d = neighbourWorldPositions[j] - cableWorldPos;
                    if (d.sqrMagnitude < 1e-4f) continue;

                    Vector3 dir = NearestCardinalAxis(d);
                    int axisIdx = AxisIndex(dir);
                    if (axisIdx < 0 || axisUsed[axisIdx]) continue; // first neighbour on this axis wins
                    axisUsed[axisIdx] = true;

                    // Project the neighbour's offset onto the chosen axis. This is the
                    // distance the arm needs to span from the cable's centre to the
                    // neighbour's centre (or near-face for big blocks). We cap it at
                    // 1.5 grid units so a wildly far neighbour can't grow a goofy arm.
                    float projected = Mathf.Abs(Vector3.Dot(d, dir));
                    float armEnd    = Mathf.Min(projected, gs * 1.5f);
                    float armLen    = Mathf.Max(0.05f, armEnd - coreSize * 0.5f);

                    Vector3 size = AxisAlignedSize(dir, armLen, armThickness);
                    Vector3 pos  = dir * (coreSize * 0.5f + armLen * 0.5f);
                    BuildCube(visualRoot, $"Arm_{axisIdx}", pos, size, material);
                }
            }

            // 3) Optional terminator nubs on unused faces.
            if (showUnusedFaceCaps)
            {
                for (int i = 0; i < CardinalAxes.Length; i++)
                {
                    if (axisUsed[i]) continue;
                    Vector3 dir  = CardinalAxes[i];
                    Vector3 size = AxisAlignedSize(dir, armThickness * 0.4f, armThickness * 0.7f);
                    Vector3 pos  = dir * (coreSize * 0.5f + armThickness * 0.2f);
                    BuildCube(visualRoot, $"Cap_{i}", pos, size, material);
                }
            }
        }

        private static Vector3 NearestCardinalAxis(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0, 0);
            if (ay >= ax && ay >= az) return new Vector3(0, Mathf.Sign(v.y), 0);
            return new Vector3(0, 0, Mathf.Sign(v.z));
        }

        private static int AxisIndex(Vector3 axis)
        {
            if (Mathf.Abs(axis.x) > 0.5f) return axis.x > 0 ? 0 : 1;
            if (Mathf.Abs(axis.y) > 0.5f) return axis.y > 0 ? 2 : 3;
            if (Mathf.Abs(axis.z) > 0.5f) return axis.z > 0 ? 4 : 5;
            return -1;
        }

        private static Vector3 AxisAlignedSize(Vector3 axis, float along, float across)
        {
            if (Mathf.Abs(axis.x) > 0.5f) return new Vector3(along, across, across);
            if (Mathf.Abs(axis.y) > 0.5f) return new Vector3(across, along, across);
            return new Vector3(across, across, along);
        }

        private static void BuildCube(Transform parent, string name, Vector3 localPos,
                                       Vector3 localSize, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var t = go.transform;
            t.SetParent(parent, worldPositionStays: false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.identity;
            t.localScale    = localSize;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
        }

        /// <summary>
        /// Builds a URP-friendly metallic material tinted with <paramref name="tint"/>.
        /// Caller owns the resulting material instance.
        /// </summary>
        public static Material CreateTintedMaterial(Color tint, string debugName = "CableMat")
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = debugName };
            tint.a = 1f;
            m.color = tint;
            if (m.HasProperty("_BaseColor"))     m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", tint * 0.15f);
            if (m.HasProperty("_Smoothness"))    m.SetFloat("_Smoothness", 0.55f);
            if (m.HasProperty("_Metallic"))      m.SetFloat("_Metallic", 0.70f);
            return m;
        }
    }
}
