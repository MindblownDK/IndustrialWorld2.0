// Assets/Scripts/VoxelEngine/Building/Tiered/BuildPieceIcons.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          INDUSTRIAL WORLD — BUILD PIECE ICON LIBRARY              ║
// ║                                                                  ║
// ║  Every construction family gets a hand-authored isometric line   ║
// ║  drawing, rasterised once into a white alpha mask and cached for ║
// ║  the session. The build wheel tints the mask per state, so an    ║
// ║  idle, hovered and unaffordable wedge all share one texture.     ║
// ║                                                                  ║
// ║  The camera sits at (-X, +Y, -Z): the top face and the two near  ║
// ║  faces are visible, and every surface detail belongs on the      ║
// ║  z = 0 face. Drawing detail on the far face is what leaves a     ║
// ║  piece looking like an open carton.                              ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.UI;

namespace VoxelEngine.Building.Tiered
{
    public static class BuildPieceIcons
    {
        /// <summary>Mask for a family. Rasterised on first request, cached for the session.</summary>
        public static Texture2D Get(BuildFamily family)
            => IconAtlas.Get("build:" + family, b => Draw(b, family));

        private static void Draw(LineArtBuilder b, BuildFamily family)
        {
            switch (family)
            {
                case BuildFamily.Foundation: Foundation(b); break;
                case BuildFamily.Wall:       WallPiece(b);  break;
                case BuildFamily.Floor:      FloorPiece(b); break;
                case BuildFamily.Doorway:    Doorway(b);    break;
                case BuildFamily.Door:       DoorLeaf(b);   break;
                case BuildFamily.Window:     WindowPiece(b);break;
                case BuildFamily.Stairs:     Stairs(b);     break;
                case BuildFamily.Roof:       Roof(b);       break;
                case BuildFamily.Pillar:     Pillar(b);     break;
                case BuildFamily.HalfWall:   HalfWall(b);   break;

                case BuildFamily.StationHull:     Hull(b);      break;
                case BuildFamily.StationFloor:    Deck(b);      break;
                case BuildFamily.StationCorridor: Corridor(b);  break;
                case BuildFamily.StationJunction: Junction(b);  break;
                case BuildFamily.StationWindow:   Viewport(b);  break;
                case BuildFamily.StationAirlock:  Airlock(b);   break;
                case BuildFamily.StationDock:     Dock(b);      break;
                case BuildFamily.StationDome:     Dome(b);      break;

                case BuildFamily.WallFrame:  WallFrame(b);  break;
                case BuildFamily.GarageDoor: GarageDoor(b); break;
                case BuildFamily.FloorHatch: FloorHatch(b); break;
                case BuildFamily.WindowPane: WindowPane(b); break;
                case BuildFamily.HatchLid:   HatchLid(b);   break;
                case BuildFamily.Railing:    Railing(b);    break;

                // ── Roofing and gates (13.15.0-dev) ──
                case BuildFamily.TriangularWall:            TriWall(b, false); break;
                case BuildFamily.TriangularWallInverted:    TriWall(b, true);  break;
                case BuildFamily.SlantedRoof:               SlantedRoof(b);    break;
                case BuildFamily.TriangularRoof:            TriRoof(b);        break;
                case BuildFamily.SlantedTriangularRoof:     SlantedTriRoof(b); break;
                case BuildFamily.CornerRoof:                CornerRoof(b);     break;
                case BuildFamily.SlantedCornerRoofInverted: ValleyCorner(b);   break;
                case BuildFamily.PyramidRoof:               PyramidRoof(b);    break;
                case BuildFamily.GateFrame:                 GatePortal(b, false); break;
                case BuildFamily.Gate:                      GateLeaf(b, false);   break;
                case BuildFamily.BigGateFrame:              GatePortal(b, true);  break;
                case BuildFamily.BigGate:                   GateLeaf(b, true);    break;
                case BuildFamily.CompoundWall:              CompoundWall(b);      break;

                default: b.IsoBox(0f, 0f, 0f, 1f, 1f, 1f); break;
            }
        }

        private static Vector2 P(float x, float y, float z) => LineArtBuilder.Iso(x, y, z);

        // ── Structural ────────────────────────────────────────────────────

        private static void Foundation(LineArtBuilder b)
        {
            b.IsoBox(0f, 0f, 0f, 1f, 0.40f, 1f);
            // Inset line on the top face: a poured slab with a form edge.
            b.Closed(P(0.14f, 0.40f, 0.14f), P(0.86f, 0.40f, 0.14f),
                     P(0.86f, 0.40f, 0.86f), P(0.14f, 0.40f, 0.86f));
        }

        private static void WallPiece(LineArtBuilder b)
        {
            const float sx = 1f, sy = 0.95f, sz = 0.14f;
            b.IsoBox(0f, 0f, 0f, sx, sy, sz);
            b.IsoFaceLine(sx, sy, 0.34f, 0.06f, 0.34f, 0.94f);
            b.IsoFaceLine(sx, sy, 0.66f, 0.06f, 0.66f, 0.94f);
        }

        private static void FloorPiece(LineArtBuilder b)
        {
            b.IsoBox(0f, 0f, 0f, 1f, 0.12f, 1f);
            for (int i = 1; i <= 2; i++)
            {
                float t = i / 3f;
                b.Line(P(0f, 0.12f, t), P(1f, 0.12f, t));
            }
        }

        private static void Doorway(LineArtBuilder b)
        {
            const float sx = 1f, sy = 0.95f, sz = 0.14f;
            b.IsoBox(0f, 0f, 0f, sx, sy, sz);
            b.IsoFace(sx, sy, 0.26f, 0f, 0.74f, 0.76f);
            // Head reveal running back into the wall, so the opening reads as a
            // hole punched through rather than a panel stuck on.
            b.Line(P(0.26f * sx, 0.76f * sy, 0f), P(0.26f * sx, 0.76f * sy, sz));
            b.Line(P(0.26f * sx, 0.76f * sy, sz), P(0.74f * sx, 0.76f * sy, sz));
        }

        private static void DoorLeaf(LineArtBuilder b)
        {
            const float sx = 0.52f, sy = 0.95f, sz = 0.1f;
            b.IsoBox(0f, 0f, 0f, sx, sy, sz);
            b.IsoFace(sx, sy, 0.16f, 0.08f, 0.84f, 0.48f);
            b.IsoFace(sx, sy, 0.16f, 0.58f, 0.84f, 0.92f);
            b.Line(P(0.88f * sx, 0.52f * sy, 0f), P(1.16f * sx, 0.52f * sy, 0f));
        }

        private static void WindowPiece(LineArtBuilder b)
        {
            const float sx = 1f, sy = 0.95f, sz = 0.14f;
            b.IsoBox(0f, 0f, 0f, sx, sy, sz);
            b.IsoFace(sx, sy, 0.24f, 0.3f, 0.76f, 0.78f);
            b.IsoFaceLine(sx, sy, 0.5f, 0.3f, 0.5f, 0.78f);
            b.IsoFaceLine(sx, sy, 0.24f, 0.54f, 0.76f, 0.54f);
        }

        private static void Stairs(LineArtBuilder b)
        {
            const int steps = 4;
            const float width = 0.62f;

            // Near stringer profile on the z = 0 face.
            var profile = new List<Vector2> { P(0f, 0f, 0f) };
            for (int i = 0; i < steps; i++)
            {
                float x = i / (float)steps;
                float top = (i + 1) / (float)steps;
                profile.Add(P(x, top, 0f));
                profile.Add(P(x + 1f / steps, top, 0f));
            }
            profile.Add(P(1f, 0f, 0f));
            b.Closed(profile.ToArray());

            // Treads running back into the piece, then the far stringer.
            for (int i = 0; i < steps; i++)
            {
                float x = i / (float)steps;
                float top = (i + 1) / (float)steps;
                b.Line(P(x, top, 0f), P(x, top, width));
                b.Line(P(x, top, width), P(x + 1f / steps, top, width));
                if (i < steps - 1)
                    b.Line(P(x + 1f / steps, top, width), P(x + 1f / steps, (i + 2f) / steps, width));
            }
            b.Line(P(1f, 1f, width), P(1f, 1f, 0f));
            b.Line(P(1f, 0f, width), P(1f, 0f, 0f));
            b.Line(P(1f, 0f, width), P(1f, 1f, width));
        }

        private static void Roof(LineArtBuilder b)
        {
            // Hipped roof: eave rectangle, a ridge above it, four hips to the corners.
            const float h = 0.58f;
            Vector2 a = P(0f, 0f, 0f), bb = P(1f, 0f, 0f);
            Vector2 c = P(1f, 0f, 1f), d = P(0f, 0f, 1f);
            Vector2 r0 = P(0.27f, h, 0.5f), r1 = P(0.73f, h, 0.5f);

            b.Closed(a, bb, c, d);
            b.Line(r0, r1);
            b.Line(r0, a); b.Line(r0, d);
            b.Line(r1, bb); b.Line(r1, c);
        }

        private static void Pillar(LineArtBuilder b)
        {
            b.IsoBox(0.10f, 0f,     0.10f, 0.44f, 0.12f, 0.44f);  // footing
            b.IsoBox(0.19f, 0.12f,  0.19f, 0.26f, 0.70f, 0.26f);  // shaft
            b.IsoBox(0.10f, 0.82f,  0.10f, 0.44f, 0.12f, 0.44f);  // capital
        }

        private static void HalfWall(LineArtBuilder b)
        {
            const float sx = 1f, sy = 0.46f, sz = 0.16f;
            b.IsoBox(0f, 0f, 0f, sx, sy, sz);
            b.IsoFaceLine(sx, sy, 0.5f, 0.08f, 0.5f, 0.92f);
        }

        private static void WallFrame(LineArtBuilder b)
        {
            const float sx = 1f, sy = 0.95f, sz = 0.14f;
            b.IsoBox(0f, 0f, 0f, sx, sy, sz);
            b.IsoFace(sx, sy, 0.12f, 0f, 0.88f, 0.72f);
            // Head reveal, so the wide opening reads as punched through the wall.
            b.Line(P(0.12f * sx, 0.72f * sy, 0f), P(0.12f * sx, 0.72f * sy, sz));
            b.Line(P(0.12f * sx, 0.72f * sy, sz), P(0.88f * sx, 0.72f * sy, sz));
        }

        private static void GarageDoor(LineArtBuilder b)
        {
            const float sx = 1f, sy = 0.86f, sz = 0.12f;
            b.IsoBox(0f, 0f, 0f, sx, sy, sz);
            // Shutter slats plus the drum the door rolls into.
            for (int i = 1; i <= 4; i++)
            {
                float v = i / 5f;
                b.IsoFaceLine(sx, sy, 0.03f, v, 0.97f, v);
            }
            b.Line(P(0f, sy + 0.16f, 0f), P(sx, sy + 0.16f, 0f));
            b.Line(P(0f, sy + 0.16f, 0f), P(0f, sy, 0f));
            b.Line(P(sx, sy + 0.16f, 0f), P(sx, sy, 0f));
        }

        private static void FloorHatch(LineArtBuilder b)
        {
            b.IsoBox(0f, 0f, 0f, 1f, 0.12f, 1f);
            // Square opening on the deck with the lid folded up behind it.
            b.Closed(P(0.28f, 0.12f, 0.28f), P(0.72f, 0.12f, 0.28f),
                     P(0.72f, 0.12f, 0.72f), P(0.28f, 0.12f, 0.72f));
            b.Closed(P(0.28f, 0.12f, 0.28f), P(0.72f, 0.12f, 0.28f),
                     P(0.72f, 0.56f, 0.22f), P(0.28f, 0.56f, 0.22f));
            for (int i = 1; i <= 2; i++)
            {
                float t = i / 3f;
                b.Line(P(0.34f, 0.12f - t * 0.34f, 0.34f + t * 0.1f),
                       P(0.66f, 0.12f - t * 0.34f, 0.34f + t * 0.1f));
            }
        }

        private static void WindowPane(LineArtBuilder b)
        {
            // A glazed pane on its own, with a corner highlight so it reads as glass.
            const float sx = 1f, sy = 0.66f, sz = 0.07f;
            b.IsoBox(0f, 0f, 0f, sx, sy, sz);
            b.IsoFaceLine(sx, sy, 0.5f, 0.04f, 0.5f, 0.96f);
            b.IsoFaceLine(sx, sy, 0.04f, 0.5f, 0.96f, 0.5f);
            b.Line(P(0.12f * sx, 0.2f * sy, 0f), P(0.38f * sx, 0.46f * sy, 0f));
            b.Line(P(0.12f * sx, 0.42f * sy, 0f), P(0.26f * sx, 0.56f * sy, 0f));
        }

        // ── Roofing and gates (13.15.0-dev) ──

        private static void TriWall(LineArtBuilder b, bool inverted)
        {
            const float h = 0.85f;
            if (!inverted)
            {
                // Rises to full height on the right.
                b.Closed(P(0f, 0f, 0f), P(1f, 0f, 0f), P(1f, h, 0f));
                b.Line(P(0.55f, 0f, 0f), P(0.55f, h * 0.55f, 0f));
            }
            else
            {
                // Hangs from a top point on the left, full on the right.
                b.Closed(P(0f, h, 0f), P(1f, h, 0f), P(1f, 0f, 0f));
                b.Line(P(0.55f, h, 0f), P(0.55f, h * 0.45f, 0f));
            }
        }

        private static void SlantedRoof(LineArtBuilder b)
        {
            const float h = 0.8f;
            // A pitched panel seen in iso: low eave near, high ridge far.
            b.Closed(P(0f, 0f, 0f), P(1f, 0f, 0f), P(1f, h, 1f), P(0f, h, 1f));
            b.Line(P(0f, 0f, 0f), P(0f, h * 0.35f, 0.35f));
            b.Line(P(1f, 0f, 0f), P(1f, h * 0.35f, 0.35f));
        }

        private static void TriRoof(LineArtBuilder b)
        {
            b.Closed(P(0f, 0f, 0f), P(1f, 0f, 0f), P(0f, 0f, 1f));
            b.Line(P(0.5f, 0f, 0f), P(0f, 0f, 0.5f));
        }

        private static void SlantedTriRoof(LineArtBuilder b)
        {
            const float h = 0.8f;
            b.Closed(P(0f, 0f, 0f), P(1f, 0f, 0f), P(0f, h, 1f));
            b.Line(P(0.5f, 0f, 0f), P(0f, h * 0.5f, 0.5f));
        }

        private static void CornerRoof(LineArtBuilder b)
        {
            const float h = 0.7f;
            Vector2 a = P(0f, 0f, 0f), c = P(1f, 0f, 0f), d = P(0f, 0f, 1f);
            Vector2 apex = P(1f, h, 1f);
            b.Closed(a, c, apex, d);
            b.Line(a, apex);   // the diagonal hip ridge
        }

        private static void ValleyCorner(LineArtBuilder b)
        {
            const float h = 0.7f;
            Vector2 a = P(0f, h, 0f), c = P(1f, h, 0f), d = P(0f, h, 1f);
            Vector2 drop = P(1f, 0f, 1f);
            b.Closed(a, c, drop, d);
            b.Line(a, drop);   // the valley line
        }

        private static void PyramidRoof(LineArtBuilder b)
        {
            const float h = 0.75f;
            Vector2 a = P(0f, 0f, 0f), c = P(1f, 0f, 0f);
            Vector2 d = P(1f, 0f, 1f), e = P(0f, 0f, 1f);
            Vector2 apex = P(0.5f, h, 0.5f);
            b.Closed(a, c, d, e);
            b.Line(a, apex); b.Line(c, apex);
            b.Line(d, apex); b.Line(e, apex);
        }

        private static void GatePortal(LineArtBuilder b, bool big)
        {
            float w = big ? 1f : 0.82f, h = big ? 0.95f : 0.8f;
            float x0 = (1f - w) * 0.5f;
            // Outer frame with a gate-sized opening.
            b.Closed(P(x0, 0f, 0f), P(x0 + w, 0f, 0f), P(x0 + w, h, 0f), P(x0, h, 0f));
            float iw = w * 0.62f, ih = h * 0.78f;
            float ix = (1f - iw) * 0.5f;
            b.Line(P(ix, 0f, 0f), P(ix, ih, 0f));
            b.Line(P(ix + iw, 0f, 0f), P(ix + iw, ih, 0f));
            b.Line(P(ix, ih, 0f), P(ix + iw, ih, 0f));
            if (big) b.Line(P(x0, h * 0.9f, 0f), P(x0 + w, h * 0.9f, 0f));
        }

        private static void CompoundWall(LineArtBuilder b)
        {
            const float h = 0.85f;
            b.Closed(P(0f, 0f, 0f), P(1f, 0f, 0f), P(1f, h, 0f), P(0f, h, 0f));
            b.Line(P(0f, h * 0.92f, 0f), P(1f, h * 0.92f, 0f));   // parapet cap
            b.Line(P(0.12f, 0f, 0f), P(0.12f, h, 0f));            // end posts
            b.Line(P(0.88f, 0f, 0f), P(0.88f, h, 0f));
        }

        private static void GateLeaf(LineArtBuilder b, bool big)
        {
            float w = big ? 0.9f : 0.7f, h = big ? 0.95f : 0.8f;
            float x0 = (1f - w) * 0.5f;
            b.Closed(P(x0, 0f, 0f), P(x0 + w, 0f, 0f), P(x0 + w, h, 0f), P(x0, h, 0f));
            b.Line(P(x0, h * 0.5f, 0f), P(x0 + w, h * 0.5f, 0f));
            b.Line(P(x0, 0f, 0f), P(x0 + w, h, 0f));   // the diagonal brace
        }

        private static void Railing(LineArtBuilder b)
        {
            const float h = 0.68f;
            b.Line(P(0f, 0f, 0f), P(0f, h, 0f));
            b.Line(P(0.33f, 0f, 0f), P(0.33f, h, 0f));
            b.Line(P(0.66f, 0f, 0f), P(0.66f, h, 0f));
            b.Line(P(1f, 0f, 0f), P(1f, h, 0f));
            b.Line(P(0f, h, 0f), P(1f, h, 0f));
            b.Line(P(0f, h * 0.52f, 0f), P(1f, h * 0.52f, 0f));
        }

        private static void HatchLid(LineArtBuilder b)
        {
            // The lid swung up on its hinge with the ladder unrolled beneath it.
            b.Closed(P(0f, 0f, 0f), P(1f, 0f, 0f), P(1f, 0f, 1f), P(0f, 0f, 1f));
            b.Closed(P(0f, 0f, 0f), P(1f, 0f, 0f), P(1f, 0.78f, -0.18f), P(0f, 0.78f, -0.18f));
            b.Line(P(0.18f, 0f, 0.5f), P(0.18f, -0.95f, 0.5f));
            b.Line(P(0.82f, 0f, 0.5f), P(0.82f, -0.95f, 0.5f));
            for (int i = 1; i <= 3; i++)
            {
                float t = i / 4f;
                b.Line(P(0.18f, -0.95f * t, 0.5f), P(0.82f, -0.95f * t, 0.5f));
            }
        }

        // ── Orbital station ───────────────────────────────────────────────

        private static void Hull(LineArtBuilder b)
        {
            b.Closed(b.Polygon(Vector2.zero, 0.52f, 6));
            b.Closed(b.Polygon(Vector2.zero, 0.34f, 6));
            for (int i = 0; i < 6; i++)
            {
                float a = (120f + i * 60f) * Mathf.Deg2Rad;
                b.Circle(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.44f, 0.045f, 12);
            }
        }

        private static void Deck(LineArtBuilder b)
        {
            b.IsoBox(0f, 0f, 0f, 1f, 0.10f, 1f);
            for (int i = 1; i <= 3; i++)
            {
                float t = i / 4f;
                b.Line(P(t, 0.10f, 0f), P(t, 0.10f, 1f));
            }
        }

        private static void Corridor(LineArtBuilder b)
        {
            b.Ellipse(new Vector2(-0.42f, 0f), 0.14f, 0.36f, 28);
            b.Ellipse(new Vector2(0.42f, 0f), 0.14f, 0.36f, 28);
            b.Line(new Vector2(-0.42f, 0.36f), new Vector2(0.42f, 0.36f));
            b.Line(new Vector2(-0.42f, -0.36f), new Vector2(0.42f, -0.36f));
            b.Line(new Vector2(0f, 0.36f), new Vector2(0f, -0.36f));
        }

        private static void Junction(LineArtBuilder b)
        {
            const float arm = 0.52f, half = 0.19f;
            b.Closed(
                new Vector2(-half, -arm), new Vector2(half, -arm), new Vector2(half, -half),
                new Vector2(arm, -half), new Vector2(arm, half), new Vector2(half, half),
                new Vector2(half, arm), new Vector2(-half, arm), new Vector2(-half, half),
                new Vector2(-arm, half), new Vector2(-arm, -half), new Vector2(-half, -half));
            b.Circle(Vector2.zero, 0.13f, 20);
        }

        private static void Viewport(LineArtBuilder b)
        {
            b.Rect(-0.5f, -0.4f, 0.5f, 0.4f);
            b.Circle(Vector2.zero, 0.27f, 32);
            b.Arc(Vector2.zero, 0.18f, 0.18f, 115f, 175f, 10);
        }

        private static void Airlock(LineArtBuilder b)
        {
            b.Rect(-0.48f, -0.5f, 0.48f, 0.5f);
            b.Circle(Vector2.zero, 0.3f, 32);
            b.Circle(Vector2.zero, 0.1f, 16);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
                b.Line(dir * 0.12f, dir * 0.28f);
            }
        }

        private static void Dock(LineArtBuilder b)
        {
            b.Circle(Vector2.zero, 0.5f, 40);
            b.Circle(Vector2.zero, 0.31f, 32);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 perp = new(-dir.y, dir.x);
                b.Closed(dir * 0.31f + perp * 0.1f, dir * 0.5f + perp * 0.1f,
                         dir * 0.5f - perp * 0.1f, dir * 0.31f - perp * 0.1f);
            }
        }

        private static void Dome(LineArtBuilder b)
        {
            b.Arc(new Vector2(0f, -0.18f), 0.52f, 0.52f, 0f, 180f, 30);
            b.Arc(new Vector2(0f, -0.18f), 0.52f, 0.17f, 0f, -180f, 24);
            b.Arc(new Vector2(0f, -0.18f), 0.52f, 0.17f, 0f, 180f, 24);
            b.Arc(new Vector2(0f, -0.18f), 0.26f, 0.52f, 0f, 180f, 24);
            b.Line(new Vector2(-0.62f, -0.18f), new Vector2(0.62f, -0.18f));
        }
    }
}
