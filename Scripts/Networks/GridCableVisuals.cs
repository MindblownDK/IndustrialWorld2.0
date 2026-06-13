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

            // ── Industrial cable visual language ────────────────────
            // The TIER colour (e.g. orange Copper, grey Iron, gold Gold,
            // cyan Superconductor) is what the player needs to identify at a
            // glance. We use it for the WIDE TERMINAL COLLAR at every
            // junction so each tier reads instantly from across a factory.
            //
            // The actual cable SHAFT meanwhile uses a shared "rubber sleeve"
            // material — a near-black neutral so the bright tier collar
            // pops against it AND so adjacent cables of different tiers
            // form visually consistent runs without colour clashing.
            //
            // Net result:
            //   • Shaft       = dark rubber sleeve (every tier)
            //   • Collar/end  = the wire's tier tint
            var sleeveMat = SharedSleeveMaterial();
            var tierMat   = MakeTierAccentVariant(material);

            IndustrialPipeMesh.Rebuild(
                visualRoot,
                cableWorldPos,
                neighbourWorldPositions,
                gridSize,
                PipeStyle.WireArm,
                /* shellMat  */ sleeveMat,  // dark sleeve = visible cable run
                /* innerMat  */ null,
                /* accentMat */ tierMat);   // bright tier-coloured collar
        }

        // ── Shared neutral sleeve material ──────────────────────────
        // One instance shared across every cable in the world — this is the
        // dark rubber jacket the conductors are wrapped in. Cached so we
        // don't allocate a new material per cable.
        private static Material _sleeveMatCache;
        private static Material SharedSleeveMaterial()
        {
            if (_sleeveMatCache != null) return _sleeveMatCache;
            // Near-black charcoal with a hint of warmth — reads as rubber,
            // not plastic. Low metallic, medium-low smoothness so it stays
            // matte against the polished tier-metal collars.
            var rubber = new Color(0.10f, 0.10f, 0.115f, 1f);
            _sleeveMatCache = IndustrialPipeMesh.CreateMetalMaterial(
                rubber, "CableSleeve_Shared",
                metallic: 0.10f, smoothness: 0.35f);
            return _sleeveMatCache;
        }

        /// <summary>
        /// Build a polished metallic variant of the tier colour for use on
        /// the terminal collars at every junction. Lifts brightness ~15%
        /// toward white so the tier reads clearly even on darker tiers
        /// (Iron, Lead), and uses high metallic + high smoothness so the
        /// collar looks like a bolted brass / steel clamp.
        /// </summary>
        private static Material MakeTierAccentVariant(Material src)
        {
            if (src == null) return SharedSleeveMaterial();
            Color baseTint = src.color;
            Color accent = new Color(
                Mathf.Clamp01(baseTint.r * 0.85f + 0.15f),
                Mathf.Clamp01(baseTint.g * 0.85f + 0.15f),
                Mathf.Clamp01(baseTint.b * 0.85f + 0.15f),
                1f);
            return IndustrialPipeMesh.CreateMetalMaterial(
                accent, $"{src.name}_TierCollar",
                metallic: 0.95f, smoothness: 0.90f);
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
