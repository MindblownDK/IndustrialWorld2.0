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
// ║  Nothing is imported: no art dependency, no atlas to keep in     ║
// ║  sync when a family is added — add a case below and it draws.    ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.UI;

namespace VoxelEngine.Building.Tiered
{
    public static class BuildPieceIcons
    {
        private const int   Resolution = 128;
        private const float StrokeWidth = 4.2f;
        private const float Padding = 9f;

        private static readonly Dictionary<BuildFamily, Texture2D> _cache = new();

        /// <summary>
        /// Icon mask for a family. Built on first request, then cached — the
        /// wheel opens hundreds of times a session and must never re-rasterise.
        /// </summary>
        public static Texture2D Get(BuildFamily family)
        {
            if (_cache.TryGetValue(family, out var cached) && cached != null) return cached;

            var builder = new LineArtBuilder();
            Draw(builder, family);
            var texture = builder.Rasterize(Resolution, StrokeWidth, Padding, $"BuildIcon_{family}");
            _cache[family] = texture;
            return texture;
        }

        /// <summary>Drops every cached mask and releases its texture.</summary>
        public static void Clear()
        {
            foreach (var pair in _cache)
            {
                if (pair.Value == null) continue;
                if (Application.isPlaying) Object.Destroy(pair.Value);
                else Object.DestroyImmediate(pair.Value);
            }
            _cache.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad() => _cache.Clear();

        // ── Isometric helpers ─────────────────────────────────────────────
        // Ground plane is X/Z, height is Y and projects straight up. This is the
        // builder's-eye view: you always see the top face and the two near walls.

        private static Vector2 Iso(float x, float y, float z)
            => new((x - z) * 0.866f, y + (x + z) * 0.5f);

        /// <summary>The nine visible edges of an axis-aligned box.</summary>
        private static void Box(LineArtBuilder b, float ox, float oy, float oz, float sx, float sy, float sz)
        {
            Vector2 bb = Iso(ox + sx, oy, oz);
            Vector2 cc = Iso(ox + sx, oy, oz + sz);
            Vector2 dd = Iso(ox, oy, oz + sz);
            Vector2 ee = Iso(ox, oy + sy, oz);
            Vector2 ff = Iso(ox + sx, oy + sy, oz);
            Vector2 gg = Iso(ox + sx, oy + sy, oz + sz);
            Vector2 hh = Iso(ox, oy + sy, oz + sz);

            b.Closed(ee, ff, gg, hh);   // top face
            b.Line(ff, bb);             // near-right vertical
            b.Line(bb, cc);             // right ground edge
            b.Line(cc, gg);             // near vertical
            b.Line(hh, dd);             // near-left vertical
            b.Line(dd, cc);             // left ground edge
        }

        /// <summary>A rectangle painted on the +Z face of a box, in 0..1 face space.</summary>
        private static void FacePanel(LineArtBuilder b, float sx, float sy, float sz,
                                      float u0, float v0, float u1, float v1)
        {
            b.Closed(
                Iso(u0 * sx, v0 * sy, sz),
                Iso(u1 * sx, v0 * sy, sz),
                Iso(u1 * sx, v1 * sy, sz),
                Iso(u0 * sx, v1 * sy, sz));
        }

        private static Vector2[] Hexagon(Vector2 c, float r)
        {
            var pts = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float a = (90f + i * 60f) * Mathf.Deg2Rad;
                pts[i] = new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
            }
            return pts;
        }

        // ── Family artwork ────────────────────────────────────────────────

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

                default: Box(b, 0f, 0f, 0f, 1f, 1f, 1f); break;
            }
        }

        private static void Foundation(LineArtBuilder b)
        {
            Box(b, 0f, 0f, 0f, 1f, 0.40f, 1f);
            // Inset line on the top face: reads as a poured slab with a form edge.
            b.Closed(
                Iso(0.14f, 0.40f, 0.14f), Iso(0.86f, 0.40f, 0.14f),
                Iso(0.86f, 0.40f, 0.86f), Iso(0.14f, 0.40f, 0.86f));
        }

        private static void WallPiece(LineArtBuilder b)
        {
            const float sx = 1f, sy = 0.95f, sz = 0.14f;
            Box(b, 0f, 0f, 0f, sx, sy, sz);
            // Two stud lines so a wall never reads as a blank slab.
            b.Line(Iso(0.34f * sx, 0.06f * sy, sz), Iso(0.34f * sx, 0.94f * sy, sz));
            b.Line(Iso(0.66f * sx, 0.06f * sy, sz), Iso(0.66f * sx, 0.94f * sy, sz));
        }

        private static void FloorPiece(LineArtBuilder b)
        {
            Box(b, 0f, 0f, 0f, 1f, 0.12f, 1f);
            // Plank seams across the top face.
            for (int i = 1; i <= 2; i++)
            {
                float t = i / 3f;
                b.Line(Iso(0f, 0.12f, t), Iso(1f, 0.12f, t));
            }
        }

        private static void Doorway(LineArtBuilder b)
        {
            const float sx = 1f, sy = 0.95f, sz = 0.14f;
            Box(b, 0f, 0f, 0f, sx, sy, sz);
            FacePanel(b, sx, sy, sz, 0.26f, 0f, 0.74f, 0.76f);
            // Head reveal on the far face, so the opening reads as a hole
            // punched through the wall rather than a panel stuck onto it.
            b.Line(Iso(0.26f * sx, 0.76f * sy, sz), Iso(0.26f * sx, 0.76f * sy, 0f));
            b.Line(Iso(0.26f * sx, 0.76f * sy, 0f), Iso(0.74f * sx, 0.76f * sy, 0f));
        }

        private static void DoorLeaf(LineArtBuilder b)
        {
            const float sx = 0.52f, sy = 0.95f, sz = 0.1f;
            Box(b, 0f, 0f, 0f, sx, sy, sz);
            FacePanel(b, sx, sy, sz, 0.14f, 0.08f, 0.86f, 0.5f);
            FacePanel(b, sx, sy, sz, 0.14f, 0.58f, 0.86f, 0.92f);
            // Lever handle on the swing edge.
            b.Line(Iso(0.9f * sx, 0.52f * sy, sz), Iso(1.18f * sx, 0.52f * sy, sz));
        }

        private static void WindowPiece(LineArtBuilder b)
        {
            const float sx = 1f, sy = 0.95f, sz = 0.14f;
            Box(b, 0f, 0f, 0f, sx, sy, sz);
            FacePanel(b, sx, sy, sz, 0.24f, 0.3f, 0.76f, 0.78f);
            b.Line(Iso(0.5f * sx, 0.3f * sy, sz), Iso(0.5f * sx, 0.78f * sy, sz));
            b.Line(Iso(0.24f * sx, 0.54f * sy, sz), Iso(0.76f * sx, 0.54f * sy, sz));
        }

        private static void Stairs(LineArtBuilder b)
        {
            const int steps = 4;
            const float depth = 0.62f;
            var front = new List<Vector2>();
            front.Add(Iso(0f, 0f, depth));
            for (int i = 0; i < steps; i++)
            {
                float x = i / (float)steps;
                float yTop = (i + 1) / (float)steps;
                front.Add(Iso(x, yTop, depth));
                front.Add(Iso(x + 1f / steps, yTop, depth));
            }
            front.Add(Iso(1f, 0f, depth));
            b.Closed(front.ToArray());

            // Treads run back into the piece; the back stringer closes the form.
            for (int i = 0; i < steps; i++)
            {
                float x = i / (float)steps;
                float yTop = (i + 1) / (float)steps;
                b.Line(Iso(x, yTop, depth), Iso(x, yTop, 0f));
                b.Line(Iso(x, yTop, 0f), Iso(x + 1f / steps, yTop, 0f));
                if (i < steps - 1)
                    b.Line(Iso(x + 1f / steps, yTop, 0f), Iso(x + 1f / steps, (i + 2f) / steps, 0f));
            }
            b.Line(Iso(1f, 1f, 0f), Iso(1f, 1f, depth));
            b.Line(Iso(1f, 0f, 0f), Iso(1f, 0f, depth));
            b.Line(Iso(1f, 0f, 0f), Iso(1f, 1f, 0f));
        }

        private static void Roof(LineArtBuilder b)
        {
            // Hipped roof: an eave rectangle, a ridge set above it and four hips
            // running down to the corners. Unmistakable at icon size.
            const float h = 0.58f;
            Vector2 a = Iso(0f, 0f, 0f), bb = Iso(1f, 0f, 0f);
            Vector2 c = Iso(1f, 0f, 1f), d = Iso(0f, 0f, 1f);
            Vector2 r0 = Iso(0.27f, h, 0.5f), r1 = Iso(0.73f, h, 0.5f);

            b.Closed(a, bb, c, d);
            b.Line(r0, r1);
            b.Line(r0, a); b.Line(r0, d);
            b.Line(r1, bb); b.Line(r1, c);
        }

        private static void Pillar(LineArtBuilder b)
        {
            Box(b, 0.1f, 0f, 0.1f, 0.44f, 0.12f, 0.44f);   // footing
            Box(b, 0.19f, 0.12f, 0.19f, 0.26f, 0.7f, 0.26f); // shaft
            Box(b, 0.1f, 0.82f, 0.1f, 0.44f, 0.12f, 0.44f);  // capital
        }

        private static void HalfWall(LineArtBuilder b)
        {
            const float sx = 1f, sy = 0.46f, sz = 0.16f;
            Box(b, 0f, 0f, 0f, sx, sy, sz);
            b.Line(Iso(0.5f * sx, 0.06f * sy, sz), Iso(0.5f * sx, 0.94f * sy, sz));
        }

        private static void Hull(LineArtBuilder b)
        {
            Vector2 c = Vector2.zero;
            b.Closed(Hexagon(c, 0.52f));
            b.Closed(Hexagon(c, 0.34f));
            for (int i = 0; i < 6; i++)
            {
                float a = (120f + i * 60f) * Mathf.Deg2Rad;
                b.Circle(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.44f, 0.045f, 12);
            }
        }

        private static void Deck(LineArtBuilder b)
        {
            Box(b, 0f, 0f, 0f, 1f, 0.1f, 1f);
            for (int i = 1; i <= 3; i++)
            {
                float t = i / 4f;
                b.Line(Iso(t, 0.1f, 0f), Iso(t, 0.1f, 1f));
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
