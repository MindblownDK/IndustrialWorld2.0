// Assets/Scripts/VoxelEngine/UI/MachineShapeIcons.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        INDUSTRIAL WORLD — SHAPE ICON LIBRARY (NON-BUILDING)       ║
// ║                                                                  ║
// ║  Drawn icons for every non-building radial wheel: conveyor build ║
// ║  modes, grid armour shapes, energy pipe fittings and road        ║
// ║  surfaces. Same isometric convention as the build pieces, same   ║
// ║  white-mask-plus-tint contract, so a wedge looks identical no    ║
// ║  matter which wheel it belongs to.                               ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.UI
{
    public static class MachineShapeIcons
    {
        private static Vector2 P(float x, float y, float z) => LineArtBuilder.Iso(x, y, z);

        // ══════════════════════════════════════════════════════════════════
        //  CONVEYORS — a belt deck with a direction chevron riding on it
        // ══════════════════════════════════════════════════════════════════

        /// <summary>0 = straight, 1 = ramp, 2 = vertical.</summary>
        public static Texture2D Conveyor(int mode) => IconAtlas.Get("conveyor:" + mode, b =>
        {
            switch (mode)
            {
                case 1: ConveyorRamp(b); break;
                case 2: ConveyorVertical(b); break;
                default: ConveyorStraight(b); break;
            }
        });

        /// <summary>
        /// A travel arrow drawn on a surface. Chevrons shear badly under the
        /// isometric projection and turn into a squiggle at icon size; a single
        /// shaft with a head survives the shear and still reads as direction.
        /// </summary>
        private static void Arrow(LineArtBuilder b, Vector2 tail, Vector2 head, Vector2 barbA, Vector2 barbB)
        {
            b.Line(tail, head);
            b.Line(head, barbA);
            b.Line(head, barbB);
        }

        private static void ConveyorStraight(LineArtBuilder b)
        {
            b.IsoBox(0f, 0f, 0.2f, 1.25f, 0.16f, 0.6f);
            Arrow(b,
                P(0.28f, 0.16f, 0.5f), P(0.98f, 0.16f, 0.5f),
                P(0.78f, 0.16f, 0.34f), P(0.78f, 0.16f, 0.66f));
        }

        private static void ConveyorRamp(LineArtBuilder b)
        {
            // The same wedge the armour slope uses, stretched into a belt run.
            const float sx = 1.3f, h = 0.7f, sz = 0.55f;
            Vector2 a = P(0f, 0f, 0f), bb = P(sx, 0f, 0f), c = P(sx, h, 0f);
            Vector2 d = P(0f, 0f, sz), e = P(sx, 0f, sz), f = P(sx, h, sz);
            b.Closed(a, bb, c);
            b.Line(a, d); b.Line(bb, e); b.Line(c, f);
            b.Polyline(d, e, f);
            b.Line(f, c);
            Arrow(b,
                P(0.22f * sx, 0.22f * h, 0.5f * sz), P(0.88f * sx, 0.88f * h, 0.5f * sz),
                P(0.70f * sx, 0.70f * h, 0.14f * sz), P(0.70f * sx, 0.70f * h, 0.86f * sz));
        }

        private static void ConveyorVertical(LineArtBuilder b)
        {
            const float ox = 0.3f, oz = 0.3f, sx = 0.5f, sy = 1.15f;
            b.IsoBox(ox, 0f, oz, sx, sy, 0.5f);
            float mid = ox + sx * 0.5f;
            Arrow(b,
                P(mid, 0.2f, oz), P(mid, 0.96f, oz),
                P(ox + 0.09f, 0.74f, oz), P(ox + sx - 0.09f, 0.74f, oz));
        }

        // ══════════════════════════════════════════════════════════════════
        //  GRID ARMOUR SHAPES
        // ══════════════════════════════════════════════════════════════════

        /// <summary>0 Cube, 1 Slope, 2 HalfBlock, 3 HalfSlope, 4 Corner, 5 InvertedSlope.</summary>
        public static Texture2D GridShape(int variant) => IconAtlas.Get("gridshape:" + variant, b =>
        {
            switch (variant)
            {
                case 1: Slope(b, 1f); break;
                case 2: b.IsoBox(0f, 0f, 0f, 1f, 0.5f, 1f); break;
                case 3: Slope(b, 0.5f); break;
                case 4: CornerWedge(b); break;
                case 5: InvertedSlope(b); break;
                default: b.IsoBox(0f, 0f, 0f, 1f, 1f, 1f); break;
            }
        });

        private static void Slope(LineArtBuilder b, float height)
        {
            // Rises from the near edge (x = 0) to full height at x = 1.
            Vector2 a = P(0f, 0f, 0f), bb = P(1f, 0f, 0f), c = P(1f, height, 0f);
            Vector2 d = P(0f, 0f, 1f), e = P(1f, 0f, 1f), f = P(1f, height, 1f);
            b.Closed(a, bb, c);
            b.Line(a, d); b.Line(bb, e); b.Line(c, f);
            b.Polyline(d, e, f);
            b.Line(f, c);
        }

        private static void CornerWedge(LineArtBuilder b)
        {
            // A tetrahedral corner: full height on one vertical edge only.
            Vector2 a = P(0f, 0f, 0f), bb = P(1f, 0f, 0f), d = P(0f, 0f, 1f);
            Vector2 top = P(1f, 1f, 0f);
            b.Closed(a, bb, d);
            b.Line(a, top); b.Line(bb, top); b.Line(d, top);
        }

        private static void InvertedSlope(LineArtBuilder b)
        {
            // A full block with the underside cut away — the ceiling ramp. Drawn as
            // the solid that is actually left, so it cannot be read as a marked cube.
            Vector2 t0 = P(0f, 1f, 0f), t1 = P(1f, 1f, 0f), t2 = P(1f, 0f, 0f);
            Vector2 s0 = P(0f, 1f, 1f), s1 = P(1f, 1f, 1f), s2 = P(1f, 0f, 1f);
            b.Closed(t0, t1, t2);      // near face
            b.Line(t0, s0);
            b.Line(t1, s1);
            b.Line(t2, s2);
            b.Line(s0, s1);            // far top edge
        }

        // ══════════════════════════════════════════════════════════════════
        //  ENERGY PIPE FITTINGS — a conduit centreline with end collars
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Keyed by the variant name so the pipe enum can grow freely.</summary>
        public static Texture2D Pipe(string variantName) => IconAtlas.Get("pipe:" + variantName, b =>
        {
            switch (variantName)
            {
                case "BendRight":     PipeElbow(b, false); break;
                case "BendUp":        PipeRiser(b);        break;
                case "StepUp":        PipeStep(b, true);   break;
                case "StepRight":     PipeStep(b, false);  break;
                case "BendLeftToUp":  PipeCompound(b, -1); break;
                case "BendRightToUp": PipeCompound(b, 1);  break;
                case "Junction4Way":  PipeCross(b);        break;
                case "Junction6Way":  PipeHub(b);          break;
                default:              PipeStraight(b);     break;
            }
        });

        private static void Collar(LineArtBuilder b, Vector2 at, Vector2 axis)
        {
            Vector2 n = new Vector2(-axis.y, axis.x).normalized * 0.13f;
            b.Line(at - n, at + n);
        }

        /// <summary>
        /// One conduit run. A single centreline rather than two offset rails: at
        /// icon size, parallel rails leave open, unmitred corners that read as a
        /// broken pipe, while the rasteriser's round joins make a centreline turn
        /// corners cleanly.
        /// </summary>
        private static void Duct(LineArtBuilder b, Vector2 from, Vector2 to) => b.Line(from, to);

        /// <summary>A coupling ring dropped on a bend so the turn reads as a fitting.</summary>
        private static void Coupling(LineArtBuilder b, Vector2 at) => b.Circle(at, 0.075f, 12);

        private static void PipeStraight(LineArtBuilder b)
        {
            Vector2 a = new(-0.5f, 0f), c = new(0.5f, 0f);
            Duct(b, a, c);
            Coupling(b, Vector2.zero);
            Collar(b, a, Vector2.right);
            Collar(b, c, Vector2.right);
        }

        private static void PipeElbow(LineArtBuilder b, bool mirrored)
        {
            float s = mirrored ? -1f : 1f;
            Vector2 a = new(-0.5f * s, 0.25f), k = new(0.2f * s, 0.25f), c = new(0.2f * s, -0.5f);
            Duct(b, a, k);
            Duct(b, k, c);
            Coupling(b, k);
            Collar(b, a, Vector2.right);
            Collar(b, c, Vector2.up);
        }

        private static void PipeRiser(LineArtBuilder b)
        {
            Vector2 a = new(-0.45f, -0.35f), k = new(0.15f, -0.35f), c = new(0.15f, 0.5f);
            Duct(b, a, k);
            Duct(b, k, c);
            Coupling(b, k);
            Collar(b, a, Vector2.right);
            Collar(b, c, Vector2.up);
            b.Arc(new Vector2(0.15f, -0.35f), 0.2f, 0.2f, 90f, 180f, 8);
        }

        private static void PipeStep(LineArtBuilder b, bool vertical)
        {
            Vector2 a, k1, k2, c;
            if (vertical)
            {
                a = new Vector2(-0.5f, -0.3f); k1 = new Vector2(-0.05f, -0.3f);
                k2 = new Vector2(-0.05f, 0.3f); c = new Vector2(0.5f, 0.3f);
            }
            else
            {
                a = new Vector2(-0.5f, 0.28f); k1 = new Vector2(0f, 0.28f);
                k2 = new Vector2(0f, -0.28f); c = new Vector2(0.5f, -0.28f);
            }
            Duct(b, a, k1); Duct(b, k1, k2); Duct(b, k2, c);
            Coupling(b, k1); Coupling(b, k2);
            Collar(b, a, Vector2.right);
            Collar(b, c, Vector2.right);
        }

        private static void PipeCompound(LineArtBuilder b, float side)
        {
            Vector2 a = new(0.5f * side, -0.42f), k = new(0f, -0.42f), c = new(0f, 0.5f);
            Duct(b, a, k);
            Duct(b, k, c);
            Coupling(b, k);
            Collar(b, a, Vector2.right);
            Collar(b, c, Vector2.up);
        }

        private static void PipeCross(LineArtBuilder b)
        {
            Duct(b, new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f));
            Duct(b, new Vector2(0f, -0.5f), new Vector2(0f, 0.5f));
            b.Circle(Vector2.zero, 0.16f, 20);
        }

        private static void PipeHub(LineArtBuilder b)
        {
            Duct(b, new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f));
            Duct(b, new Vector2(0f, -0.5f), new Vector2(0f, 0.5f));
            Duct(b, new Vector2(-0.34f, -0.34f), new Vector2(0.34f, 0.34f));
            b.Circle(Vector2.zero, 0.2f, 24);
        }

        // ══════════════════════════════════════════════════════════════════
        //  ROAD SURFACES — a paved strip with a surface signature on it
        // ══════════════════════════════════════════════════════════════════

        public static Texture2D RoadSurface(string kindName) => IconAtlas.Get("road:" + kindName, b =>
        {
            switch (kindName)
            {
                case "Pathway":
                    // Cobble: a thin deck with set stones laid across it.
                    b.IsoBox(0f, 0f, 0f, 1.25f, 0.08f, 0.85f);
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 2; j++)
                        {
                            float x = 0.16f + i * 0.4f, z = 0.2f + j * 0.34f;
                            b.Closed(P(x, 0.08f, z), P(x + 0.26f, 0.08f, z),
                                     P(x + 0.26f, 0.08f, z + 0.22f), P(x, 0.08f, z + 0.22f));
                        }
                    break;

                case "Bridge":
                    // A deck with one leaf lifted off its hinge: the crossing opens.
                    b.IsoBox(0f, 0f, 0f, 0.62f, 0.1f, 0.85f);
                    b.Closed(P(0.70f, 0.1f, 0f), P(1.22f, 0.44f, 0f),
                             P(1.22f, 0.53f, 0f), P(0.70f, 0.19f, 0f));
                    b.Line(P(0.70f, 0.1f, 0f), P(0.70f, 0.1f, 0.85f));
                    b.Line(P(1.22f, 0.44f, 0f), P(1.22f, 0.44f, 0.85f));
                    b.Line(P(0.70f, 0.1f, 0.85f), P(1.22f, 0.44f, 0.85f));
                    b.Circle(P(0.66f, 0.14f, 0.42f), 0.07f, 12);
                    break;

                default:
                    // Asphalt: hot mix with a centre line.
                    b.IsoBox(0f, 0f, 0f, 1.25f, 0.1f, 0.85f);
                    for (int i = 0; i < 3; i++)
                    {
                        float x = 0.18f + i * 0.4f;
                        b.Line(P(x, 0.1f, 0.42f), P(x + 0.2f, 0.1f, 0.42f));
                    }
                    break;
            }
        });

        // ══════════════════════════════════════════════════════════════════
        //  SHARED CENTRE GLYPHS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>A cross inside a ring — the universal "cancel" hub icon.</summary>
        public static Texture2D Cancel => IconAtlas.Get("ui:cancel", b =>
        {
            b.Circle(Vector2.zero, 0.44f, 36);
            b.Line(new Vector2(-0.2f, -0.2f), new Vector2(0.2f, 0.2f));
            b.Line(new Vector2(-0.2f, 0.2f), new Vector2(0.2f, -0.2f));
        });

        /// <summary>A chevron rising out of a bracket — the "upgrade" hub icon.</summary>
        public static Texture2D Upgrade => IconAtlas.Get("ui:upgrade", b =>
        {
            b.Polyline(new Vector2(-0.34f, 0.02f), new Vector2(0f, 0.36f), new Vector2(0.34f, 0.02f));
            b.Polyline(new Vector2(-0.34f, -0.26f), new Vector2(0f, 0.08f), new Vector2(0.34f, -0.26f));
            b.Line(new Vector2(-0.42f, -0.46f), new Vector2(0.42f, -0.46f));
        });
    }
}
