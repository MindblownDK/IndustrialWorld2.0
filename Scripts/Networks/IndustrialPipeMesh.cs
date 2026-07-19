// Assets/Scripts/VoxelEngine/Networks/IndustrialPipeMesh.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║     INDUSTRIAL PIPE MESH — realistic brass/copper pipe segments ║
// ║                                                                  ║
// ║   Replaces the old stretched-cube renderer with proper round    ║
// ║   shafts, spherical joints and flanged connector collars so the  ║
// ║   pipes look like real plumbing (inspired by Modular Pipes Vol 2 ║
// ║   reference).                                                    ║
// ║                                                                  ║
// ║   Three preset profiles share this builder so every conduit in   ║
// ║   the game speaks the same visual language:                      ║
// ║                                                                  ║
// ║     • PipeStyle.Brass    — gas pipes (slim, polished brass)     ║
// ║     • PipeStyle.Copper   — water/fluid pipes (fatter, copper)   ║
// ║     • PipeStyle.Sleeve   — sleeved industrial item pipes (sleeve  ║
// ║                            + chunky terminal end-blocks)        ║
// ║     • PipeStyle.WireArm  — cables (thinner shaft, joint domes)  ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Networks
{
    public enum PipeStyle
    {
        Brass,   // gas pipes — slim polished brass
        Copper,  // water/fluid pipes — fatter copper with thick joints
        Sleeve,  // item pipes — striped sleeve + bulky end terminals
        WireArm  // power/data cables — thin round shaft with hub dome
    }

    /// <summary>
    /// Stateless renderer. Builds a central hub (sphere or block depending on
    /// style) plus up to 6 cylindrical arms reaching toward each connected
    /// neighbour, with a flanged collar at the boundary between hub and arm
    /// so the joint reads like a real fitting.
    /// </summary>
    public static class IndustrialPipeMesh
    {
        public struct StyleProfile
        {
            public float hubRadius;        // sphere/disc radius at centre
            public float armRadius;        // cylinder radius along the run
            public float collarRadius;     // flange radius at joint
            public float collarLength;     // flange length along axis
            public float capInset;         // shorten arm to leave room for collar
            public int   shaftSegments;    // cylinder side count (cosmetic)
            public bool  useSphereHub;     // true = sphere centre, false = chunky cube
            public bool  drawCollar;       // flange ring at each joint
            public bool  drawEndCaps;      // small flat cap if arm dead-ends
            public float endCapInset;      // distance from arm tip for the cap
            // ── Sleeve / box-arm extras (sleeved industrial look) ──
            public bool  useBoxArms;       // true = box arms instead of cylinders (BC)
            public bool  drawSleeveBand;   // long terminal box covering most of the arm
            public float terminalLength;   // length of the terminal end-block (sleeve only)
            public float terminalRadius;   // half-width of the terminal end-block
            public float armSquareScale;   // arm width relative to armRadius when boxArms
            public bool  squareEndCaps;    // force SQUARE end-caps even on sphere-hub
                                           //   styles (BC look — bright bolted nubs)
            public float endCapRadiusMul;  // multiplier for end-cap cross-section
                                           //   (1 = match armRadius, >1 = bulge out)
            public int   boltCount;        // rivets distributed around the collar
                                           //   ring (0 = no bolts; 6 = hexagonal)
            public float boltRadius;       // bolt sphere radius (world units)
            public float boltProtrusion;   // how far the bolt sits OUTSIDE the
                                           //   collar radius (negative = inset)
            public bool  twinShaft;        // render TWO (or three, if tripleShaft)
                                           //   parallel shafts per arm
            public float twinSeparation;   // gap between OUTER shafts (centre-
                                           //   to-centre, world units)
            public bool  tripleShaft;      // when true AND twinShaft is true,
                                           //   render a THIRD conductor in the
                                           //   middle — industrial 3-wire cable look
        }

        public static StyleProfile ProfileFor(PipeStyle style)
        {
            switch (style)
            {
                case PipeStyle.Brass:
                    return new StyleProfile
                    {
                        hubRadius      = 0.13f,
                        armRadius      = 0.085f,
                        collarRadius   = 0.115f,
                        collarLength   = 0.045f,
                        capInset       = 0.05f,
                        shaftSegments  = 12,
                        useSphereHub   = true,
                        drawCollar     = true,
                        drawEndCaps    = true,
                        endCapInset    = 0.02f,
                        useBoxArms     = false,
                        drawSleeveBand = false,
                        terminalLength = 0f,
                        terminalRadius = 0f,
                        armSquareScale = 1f,
                        squareEndCaps  = false,
                        endCapRadiusMul= 1.0f,
                        boltCount      = 0,    boltRadius     = 0f,   boltProtrusion = 0f,
                        twinShaft      = false, twinSeparation = 0f,
                    };
                case PipeStyle.Copper:
                    // FLUID / WATER pipes — chunky burnished-copper plumbing.
                    // Significantly fatter than the brass GAS pipes so the
                    // player can read the size difference at a glance, even
                    // from across a room. Bolts on every collar sell the
                    // industrial-plumbing look (reference: Modular Pipes
                    // Vol 02 brass-copper kit).
                    return new StyleProfile
                    {
                        hubRadius      = 0.30f,   // was 0.20 — sphere joint is now visibly thicker
                        armRadius      = 0.22f,   // was 0.135 — shafts are clearly larger than gas
                        collarRadius   = 0.30f,   // matches hub so the joint flange reads as ONE chunky fitting
                        collarLength   = 0.11f,   // longer collar = beefier mechanical look
                        capInset       = 0.06f,
                        shaftSegments  = 18,      // smoother round shaft at the bigger radius
                        useSphereHub   = true,
                        drawCollar     = true,
                        drawEndCaps    = true,
                        endCapInset    = 0.03f,
                        useBoxArms     = false,
                        drawSleeveBand = false,
                        terminalLength = 0f,
                        terminalRadius = 0f,
                        armSquareScale = 1f,
                        squareEndCaps  = false,
                        endCapRadiusMul= 1.0f,
                        boltCount      = 8,       // was 6 — more bolts to match the larger flange
                        boltRadius     = 0.035f,  // bigger bolt heads to match the bigger pipe
                        boltProtrusion = 0.008f,
                        twinShaft      = false, twinSeparation = 0f,
                    };
                case PipeStyle.Sleeve:
                    // Sleeved industrial pipe style:
                    //   • ROUND dark sleeve shaft along the run
                    //   • Bright wide BRASS COLLAR clad around every joint
                    //   • Bright square TERMINAL end-block on un-connected faces
                    //
                    // User feedback: no dark cube hub. The centre is now a small
                    // sphere fully hidden inside the bright collars, so the only
                    // visible silhouette is "round shaft → bright wrapped joint
                    // → round shaft", with bright square caps at the dead-ends
                    // (the iconic BC bolted-end-block look).
                    return new StyleProfile
                    {
                        hubRadius      = 0.16f,   // small sphere, fully hidden by collars
                        armRadius      = 0.16f,   // matches hub so shaft passes through
                        collarRadius   = 0.26f,   // bright brass collar at every face
                        collarLength   = 0.20f,   // long enough to fully clad the joint
                        capInset       = 0.0f,
                        shaftSegments  = 16,      // smooth round shaft
                        useSphereHub   = true,    // sphere, NOT a cube — no dark square
                        drawCollar     = true,
                        drawEndCaps    = true,    // bright SQUARE caps on dead-end faces
                        endCapInset    = 0.02f,
                        useBoxArms     = false,   // round shafts
                        drawSleeveBand = false,
                        terminalLength = 0f,
                        terminalRadius = 0f,
                        armSquareScale = 1.0f,
                        squareEndCaps  = true,
                        endCapRadiusMul= 1.7f,
                        boltCount      = 0,    boltRadius     = 0f,   boltProtrusion = 0f,
                        twinShaft      = false, twinSeparation = 0f,
                    };
                case PipeStyle.WireArm:
                default:
                    // CABLE / WIRE — industrial three-conductor bundle.
                    //
                    //   ════╤════                                  ════╤════
                    //   ════╪════  ╔═══════════════════════╗      ════╪════
                    //   ════╧════  ║   TIER-COLOURED CLAMP ║      ════╧════
                    //                ╚═══════════════════════╝
                    //
                    // The three slim shafts share the SLEEVE colour (a neutral
                    // dark rubber-jacket tint baked in PowerCable.RebuildVisuals)
                    // while the wide terminal CLAMP at every junction shows the
                    // wire's TIER tint — so the player can read the tier at a
                    // glance from across the factory without losing the premium
                    // "real industrial cable" silhouette.
                    return new StyleProfile
                    {
                        hubRadius      = 0.10f,   // small sphere, hidden inside the clamp
                        armRadius      = 0.045f,  // each individual conductor
                        collarRadius   = 0.26f,   // wide bright tier-coloured clamp
                        collarLength   = 0.18f,   // chunky terminal block at every junction
                        capInset       = 0.04f,
                        shaftSegments  = 12,
                        useSphereHub   = true,
                        drawCollar     = true,
                        drawEndCaps    = true,
                        endCapInset    = 0.02f,
                        useBoxArms     = false,
                        drawSleeveBand = false,
                        terminalLength = 0f,
                        terminalRadius = 0f,
                        armSquareScale = 1f,
                        squareEndCaps  = false,
                        endCapRadiusMul= 1.0f,
                        // Two cosmetic bolts on every terminal — riveted clamp look.
                        boltCount      = 4,    boltRadius     = 0.018f, boltProtrusion = 0.006f,
                        // THREE parallel conductors (twinShaft semantics extended
                        // to "multiShaft" via tripleShaft below). Separation
                        // 0.13 m so all three fit comfortably inside the 0.26 m
                        // terminal collar at every junction.
                        twinShaft      = true,  twinSeparation = 0.13f,
                        tripleShaft    = true,
                    };
            }
        }

        // ── Material factories ──────────────────────────────────

        /// <summary>Polished brass / copper / steel — colour decides the family.</summary>
        public static Material CreateMetalMaterial(Color tint, string debugName, float metallic = 0.95f, float smoothness = 0.78f)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = debugName };
            tint.a = 1f;
            m.color = tint;
            if (m.HasProperty("_BaseColor"))     m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", tint * 0.04f);
            if (m.HasProperty("_Smoothness"))    m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic"))      m.SetFloat("_Metallic",   metallic);
            return m;
        }

        /// <summary>Glassy translucent shell (used by glass pipe variants).</summary>
        public static Material CreateGlassMaterial(Color tint, string debugName)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = debugName };
            tint.a = 0.32f;
            if (m.HasProperty("_Surface"))   m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend"))     m.SetFloat("_Blend",   0f);
            if (m.HasProperty("_ZWrite"))    m.SetFloat("_ZWrite",  0f);
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.color = tint;
            if (m.HasProperty("_BaseColor"))     m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", tint * 0.05f);
            if (m.HasProperty("_Smoothness"))    m.SetFloat("_Smoothness", 0.95f);
            if (m.HasProperty("_Metallic"))      m.SetFloat("_Metallic",   0.05f);
            return m;
        }

        /// <summary>Slightly-emissive inner core showing through glass shells.</summary>
        public static Material CreateInnerCoreMaterial(Color tint, string debugName)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = debugName };
            tint.a = 1f;
            m.color = tint;
            if (m.HasProperty("_BaseColor"))     m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", tint * 0.40f);
            if (m.HasProperty("_Smoothness"))    m.SetFloat("_Smoothness", 0.30f);
            if (m.HasProperty("_Metallic"))      m.SetFloat("_Metallic",   0.0f);
            m.EnableKeyword("_EMISSION");
            return m;
        }

        // ── The actual rebuild ──────────────────────────────────

        /// <summary>
        /// Rebuild the conduit's child meshes. Caller supplies the world
        /// positions of every connected neighbour; we snap each to the
        /// nearest cardinal axis and grow a properly-fitted arm.
        /// </summary>
        public static void Rebuild(
            Transform visualRoot,
            Vector3 selfWorld,
            IReadOnlyList<Vector3> neighbourWorldPositions,
            float gridSize,
            PipeStyle style,
            Material shellMat,
            Material innerMat,   // only used by glass shells; pass null for solid
            Material accentMat)  // collars / end terminals; pass null to reuse shell
        {
            if (visualRoot == null) return;

            // Tear down previous meshes.
            for (int i = visualRoot.childCount - 1; i >= 0; i--)
                Object.Destroy(visualRoot.GetChild(i).gameObject);

            var p = ProfileFor(style);
            float gs = gridSize > 0 ? gridSize : 1f;
            var collarMat = accentMat != null ? accentMat : shellMat;

            // ── Central hub ────────────────────────────────────
            if (p.useSphereHub)
                BuildSphere(visualRoot, "Hub", Vector3.zero,
                    Vector3.one * (p.hubRadius * 2f), shellMat);
            else
                BuildCube(visualRoot, "Hub", Vector3.zero,
                    Vector3.one * (p.hubRadius * 2f), shellMat);

            // Inner core (glass only) — a small bright ball that shows
            // through the translucent shell, sells the "flowing medium".
            if (innerMat != null)
            {
                BuildSphere(visualRoot, "HubCore", Vector3.zero,
                    Vector3.one * (p.hubRadius * 1.25f), innerMat);
            }

            // ── Arms — one per cardinal neighbour ─────────────
            bool[] axisUsed = new bool[6];
            if (neighbourWorldPositions != null)
            {
                for (int j = 0; j < neighbourWorldPositions.Count; j++)
                {
                    Vector3 d = neighbourWorldPositions[j] - selfWorld;
                    if (d.sqrMagnitude < 1e-4f) continue;

                    Vector3 dir = NearestCardinalAxis(d);
                    int axisIdx = AxisIndex(dir);
                    if (axisIdx < 0 || axisUsed[axisIdx]) continue;
                    axisUsed[axisIdx] = true;

                    float projected = Mathf.Abs(Vector3.Dot(d, dir));
                    // Long pipe links may bridge up to five grid cells. Clamp to the
                    // same gameplay limit so visuals never imply a longer connection.
                    float armEnd    = Mathf.Min(projected, gs * 5f);
                    float startOff  = p.hubRadius - p.capInset * 0.5f;
                    float armLen    = Mathf.Max(0.05f, armEnd - startOff);
                    Vector3 armCentre = dir * (startOff + armLen * 0.5f);

                    // ── ARM SHAFT — cylinder OR square box depending on style ──
                    if (p.useBoxArms)
                    {
                        // Square sleeve arm (sleeved industrial pipes).
                        // Cross-section is 2*armRadius wide so the silhouette
                        // matches the cube hub it springs out of.
                        Vector3 size = AxisAlignedBoxSize(dir, armLen,
                                                          p.armRadius * 2f * p.armSquareScale);
                        BuildCube(visualRoot, $"Arm_{axisIdx}", armCentre, size, shellMat);

                        // Inner core (glass only) — slightly inset box.
                        if (innerMat != null)
                        {
                            Vector3 inSize = AxisAlignedBoxSize(dir, armLen,
                                                                p.armRadius * 1.2f);
                            BuildCube(visualRoot, $"ArmCore_{axisIdx}", armCentre, inSize, innerMat);
                        }
                    }
                    else if (p.twinShaft && p.twinSeparation > 0f)
                    {
                        // MULTI-CONDUCTOR PARALLEL ROUND SHAFTS — power-cable look.
                        //   • twinShaft only           → 2 wires (outer pair)
                        //   • twinShaft + tripleShaft  → 3 wires (outer pair + centre)
                        // We need a stable axis perpendicular to `dir` so wires
                        // offset on a consistent side. Picking world UP for
                        // horizontal runs and world RIGHT for vertical runs is
                        // visually consistent regardless of player camera angle.
                        Vector3 perp = Mathf.Abs(dir.y) > 0.5f
                            ? Vector3.right
                            : Vector3.up;
                        Vector3 offset = perp * (p.twinSeparation * 0.5f);

                        // Outer pair.
                        BuildCylinder(visualRoot, $"Arm_{axisIdx}_A", armCentre + offset,
                                      dir, armLen, p.armRadius, shellMat);
                        BuildCylinder(visualRoot, $"Arm_{axisIdx}_B", armCentre - offset,
                                      dir, armLen, p.armRadius, shellMat);

                        // Centre conductor (industrial 3-wire cable).
                        if (p.tripleShaft)
                        {
                            BuildCylinder(visualRoot, $"Arm_{axisIdx}_C", armCentre,
                                          dir, armLen, p.armRadius, shellMat);
                        }

                        if (innerMat != null)
                        {
                            BuildCylinder(visualRoot, $"ArmCore_{axisIdx}_A", armCentre + offset,
                                          dir, armLen, p.armRadius * 0.55f, innerMat);
                            BuildCylinder(visualRoot, $"ArmCore_{axisIdx}_B", armCentre - offset,
                                          dir, armLen, p.armRadius * 0.55f, innerMat);
                            if (p.tripleShaft)
                            {
                                BuildCylinder(visualRoot, $"ArmCore_{axisIdx}_C", armCentre,
                                              dir, armLen, p.armRadius * 0.55f, innerMat);
                            }
                        }
                    }
                    else
                    {
                        // Round shaft (brass, copper, single-wire).
                        BuildCylinder(visualRoot, $"Arm_{axisIdx}", armCentre,
                                      dir, armLen, p.armRadius, shellMat);

                        if (innerMat != null)
                        {
                            BuildCylinder(visualRoot, $"ArmCore_{axisIdx}", armCentre,
                                          dir, armLen, p.armRadius * 0.55f, innerMat);
                        }
                    }

                    // ── FLANGE COLLAR / TERMINAL END-BLOCK ──
                    // Sleeve style: ONE long square terminal block covering the
                    // outer half of the arm — bright accent colour — this is the
                    // boxed end-cap silhouette. Other styles get a small
                    // cylindrical flange ring at the hub end of the arm.
                    if (p.drawSleeveBand)
                    {
                        // Long bright square terminal at the far end of the arm.
                        float termLen = Mathf.Min(p.terminalLength, armLen * 0.9f);
                        float termHalf = p.terminalRadius;
                        Vector3 termCentre = dir * (startOff + armLen - termLen * 0.5f);
                        Vector3 termSize   = AxisAlignedBoxSize(dir, termLen, termHalf * 2f);
                        BuildCube(visualRoot, $"Terminal_{axisIdx}", termCentre, termSize, collarMat);
                    }
                    else if (p.drawCollar)
                    {
                        // Cylindrical flange ring near the hub.
                        Vector3 collarCentre = dir * (startOff + p.collarLength * 0.5f);
                        BuildCylinder(visualRoot, $"Collar_{axisIdx}", collarCentre,
                                      dir, p.collarLength, p.collarRadius, collarMat);

                        // Rivet/bolt detail around the collar (copper plumbing
                        // look). Skipped when boltCount == 0. Bolts are small
                        // spheres distributed evenly around the collar's outer
                        // ring, protruding slightly so they catch a highlight.
                        if (p.boltCount > 0 && p.boltRadius > 0f)
                        {
                            BuildBoltRing(visualRoot, $"Bolts_{axisIdx}",
                                          collarCentre, dir,
                                          p.collarRadius + p.boltProtrusion,
                                          p.boltRadius, p.boltCount,
                                          collarMat);
                        }
                    }
                }
            }

            // ── End caps on unused faces ──────────────────────
            // Two flavours, picked per style:
            //   • squareEndCaps = true  → bright SQUARE bolted nub (BC look),
            //                             works on any hub style.
            //   • squareEndCaps = false → round cap when hub is a sphere, OR
            //                             a small square block when hub is a
            //                             cube (so the silhouette stays coherent).
            if (p.drawEndCaps)
            {
                for (int i = 0; i < CardinalAxes.Length; i++)
                {
                    if (axisUsed[i]) continue;
                    Vector3 dir = CardinalAxes[i];
                    float capLen = p.collarLength * 0.6f;
                    Vector3 capCentre = dir * (p.hubRadius - p.endCapInset + capLen * 0.5f);
                    float crossSize = p.armRadius * 2f * Mathf.Max(0.1f, p.endCapRadiusMul);

                    if (p.squareEndCaps)
                    {
                        // Bright bolted SQUARE terminal nub — the iconic BC end-cap.
                        Vector3 size = AxisAlignedBoxSize(dir, capLen, crossSize);
                        BuildCube(visualRoot, $"Cap_{i}", capCentre, size, collarMat);
                    }
                    else if (p.useSphereHub)
                    {
                        BuildCylinder(visualRoot, $"Cap_{i}", capCentre,
                                      dir, capLen, crossSize * 0.5f, collarMat);
                    }
                    else
                    {
                        Vector3 size = AxisAlignedBoxSize(dir, capLen, crossSize);
                        BuildCube(visualRoot, $"Cap_{i}", capCentre, size, collarMat);
                    }
                }
            }
        }

        /// <summary>
        /// Returns a Vector3 sized so that the box's long axis aligns with
        /// <paramref name="axis"/> (length = <paramref name="along"/>) and the
        /// other two axes share the cross-section width.
        /// </summary>
        private static Vector3 AxisAlignedBoxSize(Vector3 axis, float along, float across)
        {
            if (Mathf.Abs(axis.x) > 0.5f) return new Vector3(along, across, across);
            if (Mathf.Abs(axis.y) > 0.5f) return new Vector3(across, along, across);
            return new Vector3(across, across, along);
        }

        // ────────────────────────────────────────────────────────────
        // Primitive helpers
        // ────────────────────────────────────────────────────────────

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

        /// <summary>
        /// Cylinder aligned along <paramref name="axis"/>, centred at <paramref name="centre"/>.
        /// Unity's PrimitiveType.Cylinder is Y-up and 2m tall — we scale + rotate accordingly.
        /// </summary>
        /// <summary>
        /// Distribute <paramref name="count"/> small sphere "bolts" evenly
        /// around a ring of radius <paramref name="ringRadius"/> centred at
        /// <paramref name="centre"/>, lying in the plane PERPENDICULAR to
        /// <paramref name="axis"/>. Used by the Copper pipe style to add the
        /// classic bolted-plumbing flange detail.
        /// </summary>
        private static void BuildBoltRing(Transform parent, string baseName,
                                          Vector3 centre, Vector3 axis,
                                          float ringRadius, float boltRadius,
                                          int count, Material mat)
        {
            if (count <= 0 || boltRadius <= 0f) return;
            // Build two perpendicular basis vectors in the plane of the ring.
            // The axis is one of ±X/±Y/±Z so picking a fixed orthogonal seed
            // and Gram-Schmidt-ing is bulletproof and allocation-free.
            Vector3 seed = Mathf.Abs(axis.y) > 0.5f ? Vector3.right : Vector3.up;
            Vector3 u = Vector3.Normalize(Vector3.Cross(axis, seed));
            Vector3 v = Vector3.Normalize(Vector3.Cross(axis, u));

            for (int i = 0; i < count; i++)
            {
                float theta = (i / (float)count) * Mathf.PI * 2f;
                Vector3 offset = (u * Mathf.Cos(theta) + v * Mathf.Sin(theta)) * ringRadius;
                BuildSphere(parent, $"{baseName}_{i}", centre + offset,
                            Vector3.one * (boltRadius * 2f), mat);
            }
        }

        private static void BuildCylinder(Transform parent, string name, Vector3 centre,
                                          Vector3 axis, float length, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var t = go.transform;
            t.SetParent(parent, worldPositionStays: false);
            t.localPosition = centre;
            // Unity cylinders default to Y-axis. Rotate Y → axis.
            t.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            // Scale: cylinder primitive is 2 units tall, 1 unit diameter.
            t.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
        }

        private static void BuildSphere(Transform parent, string name, Vector3 centre,
                                        Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var t = go.transform;
            t.SetParent(parent, worldPositionStays: false);
            t.localPosition = centre;
            t.localRotation = Quaternion.identity;
            t.localScale    = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
        }

        private static void BuildCube(Transform parent, string name, Vector3 centre,
                                      Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var t = go.transform;
            t.SetParent(parent, worldPositionStays: false);
            t.localPosition = centre;
            t.localRotation = Quaternion.identity;
            t.localScale    = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
        }
    }
}
