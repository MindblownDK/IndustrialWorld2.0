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
            float coreSize,       // kept for API compat — IndustrialPipeMesh
            float armThickness,   //   sizes via its own profile constants
            Material material,
            bool showUnusedFaceCaps) // kept for API compat
        {
            if (visualRoot == null) return;

            // ── Premium renderer path ──────────────────────────
            // Delegate to IndustrialPipeMesh with the WireArm profile so
            // cables (PowerCable / DataCable) get the same round-shaft,
            // dome-hub, flange-collar treatment as the new realistic pipes.
            // Every conduit in the game now shares one premium visual language.
            var accentMat = MakeAccentVariant(material);
            IndustrialPipeMesh.Rebuild(
                visualRoot,
                cableWorldPos,
                neighbourWorldPositions,
                gridSize,
                PipeStyle.WireArm,
                material,
                /* innerMat */ null,
                accentMat);
        }

        /// <summary>
        /// Build a slightly brighter / more polished variant of the supplied
        /// material so cable collars catch a little extra light. Cached on the
        /// owning material via name suffix so we don't allocate every rebuild.
        /// </summary>
        private static Material MakeAccentVariant(Material src)
        {
            if (src == null) return null;
            Color baseTint = src.color;
            // Lift each channel ~25% toward white; keep alpha 1.
            Color accent = new Color(
                Mathf.Clamp01(baseTint.r * 0.7f + 0.30f),
                Mathf.Clamp01(baseTint.g * 0.7f + 0.30f),
                Mathf.Clamp01(baseTint.b * 0.7f + 0.30f),
                1f);
            return IndustrialPipeMesh.CreateMetalMaterial(accent,
                $"{src.name}_Accent", metallic: 0.95f, smoothness: 0.88f);
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
