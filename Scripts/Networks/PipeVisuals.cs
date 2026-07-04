// Assets/Scripts/VoxelEngine/Networks/PipeVisuals.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║      PIPE VISUALS — wire-style chunky core + arms for pipes     ║
// ║                                                                  ║
// ║  Identical topology to GridCableVisuals (central hub cube + up   ║
// ║  to 6 cardinal arms that grow toward connected neighbours), but  ║
// ║  authored specifically for "industrial pipe" aesthetics:         ║
// ║                                                                  ║
// ║    • SOLID pipes  → opaque metallic shell, slightly fatter arms  ║
// ║    • GLASS pipes  → translucent outer shell + inner coloured     ║
// ║                     core that previews the carried medium        ║
// ║                                                                  ║
// ║  Used by GasPipe, ItemPipe and WaterPipe so every conduit in     ║
// ║  the game presents a single coherent "snap-together" visual      ║
// ║  language with the power / data cables.                          ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Networks
{
    /// <summary>
    /// Stateless helper that (re)builds a pipe's wire-style visual. Mirrors
    /// <see cref="GridCableVisuals"/> but supports glass / translucent pipes
    /// that reveal an inner "medium" colour (water, gas, item-stream).
    /// </summary>
    public static class PipeVisuals
    {
        /// <summary>Rebuild a pipe's child meshes under <paramref name="visualRoot"/>.</summary>
        /// <param name="visualRoot">Owned child transform.</param>
        /// <param name="pipeWorldPos">Pipe centre in world space.</param>
        /// <param name="neighbourWorldPositions">Connected neighbour positions.</param>
        /// <param name="gridSize">Grid cell size (typically 1 m).</param>
        /// <param name="coreSize">Edge length of the central hub.</param>
        /// <param name="armThickness">Cross-section of arm cubes.</param>
        /// <param name="shellMaterial">Outer shell material (opaque OR translucent).</param>
        /// <param name="innerCoreMaterial">If non-null AND glass, renders a thin inner
        /// coloured cube/arm sequence to preview the carried medium.</param>
        /// <param name="isGlass">Use the glass-pipe construction (thinner shell, inner core).</param>
        /// <param name="showUnusedFaceCaps">Render terminator nubs on un-connected faces.</param>
        public static void Rebuild(
            Transform visualRoot,
            Vector3 pipeWorldPos,
            IReadOnlyList<Vector3> neighbourWorldPositions,
            float gridSize,
            float coreSize,
            float armThickness,
            Material shellMaterial,
            Material innerCoreMaterial,
            bool isGlass,
            bool showUnusedFaceCaps)
        {
            if (visualRoot == null) return;

            // Tear down previous meshes.
            for (int i = visualRoot.childCount - 1; i >= 0; i--)
                Object.Destroy(visualRoot.GetChild(i).gameObject);

            float gs = gridSize > 0 ? gridSize : 1f;

            // Glass pipes get a slightly larger but thinner-walled shell — but since
            // we're using cube primitives, "thinner" is faked by adding an inner
            // coloured core that peeks through the translucent shell.
            float innerScale     = 0.55f;
            float armInnerScale  = 0.50f;

            // 1) Central hub cube (outer shell).
            BuildCube(visualRoot, "Core", Vector3.zero,
                new Vector3(coreSize, coreSize, coreSize), shellMaterial);

            // 1b) Inner core (glass only) — sits inside the shell so light passes
            //     through the translucent outer mat onto a clearly-tinted inner.
            if (isGlass && innerCoreMaterial != null)
            {
                BuildCube(visualRoot, "CoreInner", Vector3.zero,
                    new Vector3(coreSize * innerScale, coreSize * innerScale, coreSize * innerScale),
                    innerCoreMaterial);
            }

            // 2) Arms — one per neighbour, snapped to nearest cardinal axis.
            bool[] axisUsed = new bool[6];
            if (neighbourWorldPositions != null)
            {
                for (int j = 0; j < neighbourWorldPositions.Count; j++)
                {
                    Vector3 d = neighbourWorldPositions[j] - pipeWorldPos;
                    if (d.sqrMagnitude < 1e-4f) continue;

                    Vector3 dir = NearestCardinalAxis(d);
                    int axisIdx = AxisIndex(dir);
                    if (axisIdx < 0 || axisUsed[axisIdx]) continue;
                    axisUsed[axisIdx] = true;

                    float projected = Mathf.Abs(Vector3.Dot(d, dir));
                    float armEnd    = Mathf.Min(projected, gs * 1.5f);
                    float armLen    = Mathf.Max(0.05f, armEnd - coreSize * 0.5f);

                    Vector3 size = AxisAlignedSize(dir, armLen, armThickness);
                    Vector3 pos  = dir * (coreSize * 0.5f + armLen * 0.5f);
                    BuildCube(visualRoot, $"Arm_{axisIdx}", pos, size, shellMaterial);

                    // Inner core arm (glass only).
                    if (isGlass && innerCoreMaterial != null)
                    {
                        Vector3 innerSize = AxisAlignedSize(dir, armLen, armThickness * armInnerScale);
                        BuildCube(visualRoot, $"ArmInner_{axisIdx}", pos, innerSize, innerCoreMaterial);
                    }
                }
            }

            // 3) Optional terminator caps on unused faces (capped ends).
            if (showUnusedFaceCaps)
            {
                for (int i = 0; i < CardinalAxes.Length; i++)
                {
                    if (axisUsed[i]) continue;
                    Vector3 dir  = CardinalAxes[i];
                    Vector3 size = AxisAlignedSize(dir, armThickness * 0.35f, armThickness * 1.05f);
                    Vector3 pos  = dir * (coreSize * 0.5f + armThickness * 0.17f);
                    BuildCube(visualRoot, $"Cap_{i}", pos, size, shellMaterial);
                }
            }
        }

        // ── Material factories ──────────────────────────────────

        /// <summary>
        /// Opaque metallic pipe material — bright tint, high metallic, medium smoothness.
        /// </summary>
        public static Material CreateSolidPipeMaterial(Color tint, string debugName)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = debugName };
            tint.a = 1f;
            m.color = tint;
            if (m.HasProperty("_BaseColor"))     m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", tint * 0.08f);
            if (m.HasProperty("_Smoothness"))    m.SetFloat("_Smoothness", 0.65f);
            if (m.HasProperty("_Metallic"))      m.SetFloat("_Metallic",   0.85f);
            return m;
        }

        /// <summary>
        /// Translucent glass-shell material. Renders as a tinted glassy surface; the
        /// inner core mesh provides the visible "fluid level" colour underneath.
        /// </summary>
        public static Material CreateGlassPipeMaterial(Color tint, string debugName)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = debugName };
            tint.a = 0.35f;
            // URP transparency setup.
            if (m.HasProperty("_Surface"))   m.SetFloat("_Surface", 1f);   // Transparent
            if (m.HasProperty("_Blend"))     m.SetFloat("_Blend",   0f);   // Alpha
            if (m.HasProperty("_ZWrite"))    m.SetFloat("_ZWrite",  0f);
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            m.color = tint;
            if (m.HasProperty("_BaseColor"))     m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", tint * 0.05f);
            if (m.HasProperty("_Smoothness"))    m.SetFloat("_Smoothness", 0.92f);
            if (m.HasProperty("_Metallic"))      m.SetFloat("_Metallic",   0.0f);
            return m;
        }

        /// <summary>
        /// Inner-core "medium preview" material — slightly emissive so it reads
        /// through the translucent glass shell even in shadow.
        /// </summary>
        public static Material CreateInnerCoreMaterial(Color mediumTint, string debugName)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = debugName };
            mediumTint.a = 1f;
            m.color = mediumTint;
            if (m.HasProperty("_BaseColor"))     m.SetColor("_BaseColor", mediumTint);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", mediumTint * 0.40f);
            if (m.HasProperty("_Smoothness"))    m.SetFloat("_Smoothness", 0.30f);
            if (m.HasProperty("_Metallic"))      m.SetFloat("_Metallic",   0.0f);
            m.EnableKeyword("_EMISSION");
            return m;
        }

        // ── Geometry helpers (mirror GridCableVisuals) ──────────

        public static readonly Vector3[] CardinalAxes =
        {
            new( 1, 0, 0), new(-1, 0, 0),
            new( 0, 1, 0), new( 0,-1, 0),
            new( 0, 0, 1), new( 0, 0,-1),
        };

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
    }
}
