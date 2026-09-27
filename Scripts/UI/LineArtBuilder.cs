// Assets/Scripts/VoxelEngine/UI/LineArtBuilder.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║            INDUSTRIAL WORLD — PROCEDURAL LINE-ART ICONS           ║
// ║                                                                  ║
// ║  A tiny vector scratchpad that rasterises stroked polylines into ║
// ║  a white alpha mask. UI code tints the mask at draw time, so one ║
// ║  texture serves every state (idle / hot / disabled) with no      ║
// ║  imported art and no per-state atlas.                            ║
// ║                                                                  ║
// ║  Coordinates are authored in any convenient unit with +Y up —    ║
// ║  Rasterize fits the drawing to the texture and keeps the aspect. ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.UI
{
    /// <summary>
    /// Collects 2D line segments and bakes them into an anti-aliased alpha mask.
    /// Allocation-light: one float buffer and one colour buffer per bake, both
    /// released as soon as the texture is uploaded.
    /// </summary>
    public sealed class LineArtBuilder
    {
        // x,y = start, z,w = end. A flat list beats a segment class here: the
        // rasteriser walks it once and never mutates it.
        private readonly List<Vector4> _segments = new(64);
        private Vector2 _min = new(float.MaxValue, float.MaxValue);
        private Vector2 _max = new(float.MinValue, float.MinValue);

        public int SegmentCount => _segments.Count;

        public void Line(Vector2 a, Vector2 b)
        {
            if ((a - b).sqrMagnitude < 1e-10f) return;
            _segments.Add(new Vector4(a.x, a.y, b.x, b.y));
            Encapsulate(a);
            Encapsulate(b);
        }

        public void Polyline(params Vector2[] points)
        {
            if (points == null || points.Length < 2) return;
            for (int i = 1; i < points.Length; i++) Line(points[i - 1], points[i]);
        }

        public void Closed(params Vector2[] points)
        {
            if (points == null || points.Length < 3) return;
            Polyline(points);
            Line(points[^1], points[0]);
        }

        public void Rect(float x0, float y0, float x1, float y1)
            => Closed(new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1));

        /// <summary>Elliptical arc, angles in degrees measured CCW from +X.</summary>
        public void Arc(Vector2 center, float rx, float ry, float startDeg, float endDeg, int steps = 24)
        {
            steps = Mathf.Max(2, steps);
            Vector2 prev = OnEllipse(center, rx, ry, startDeg);
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 next = OnEllipse(center, rx, ry, Mathf.Lerp(startDeg, endDeg, t));
                Line(prev, next);
                prev = next;
            }
        }

        public void Ellipse(Vector2 center, float rx, float ry, int steps = 40)
            => Arc(center, rx, ry, 0f, 360f, steps);

        public void Circle(Vector2 center, float r, int steps = 40) => Ellipse(center, r, r, steps);

        // ── Isometric solids ──────────────────────────────────────────────
        // Ground plane is X/Z, height is Y and projects straight up. The camera
        // sits at (-X, +Y, -Z), so the TOP face and the two NEAR faces (x = 0 and
        // z = 0) are the visible ones: a box reads as a hexagonal silhouette with
        // a three-spoke Y meeting at the near-top corner. Getting this backwards
        // is what leaves an isometric icon looking like an open carton with its
        // front edges missing.

        public static Vector2 Iso(float x, float y, float z)
            => new((x - z) * 0.866f, y + (x + z) * 0.5f);

        /// <summary>The nine visible edges of an axis-aligned box.</summary>
        public void IsoBox(float ox, float oy, float oz, float sx, float sy, float sz)
        {
            Vector2 a = Iso(ox,      oy,      oz);        // near-bottom corner
            Vector2 b = Iso(ox + sx, oy,      oz);
            Vector2 d = Iso(ox,      oy,      oz + sz);
            Vector2 e = Iso(ox,      oy + sy, oz);        // near-top corner
            Vector2 f = Iso(ox + sx, oy + sy, oz);
            Vector2 g = Iso(ox + sx, oy + sy, oz + sz);
            Vector2 h = Iso(ox,      oy + sy, oz + sz);

            Closed(a, b, f, g, h, d);   // silhouette
            Line(e, a);                 // near vertical
            Line(e, f);                 // top edge running right
            Line(e, h);                 // top edge running left
        }

        /// <summary>A rectangle drawn on the near (z = 0) face of a box, in 0..1 face space.</summary>
        public void IsoFace(float sx, float sy, float u0, float v0, float u1, float v1)
        {
            Closed(
                Iso(u0 * sx, v0 * sy, 0f), Iso(u1 * sx, v0 * sy, 0f),
                Iso(u1 * sx, v1 * sy, 0f), Iso(u0 * sx, v1 * sy, 0f));
        }

        /// <summary>A straight line on the near (z = 0) face of a box, in 0..1 face space.</summary>
        public void IsoFaceLine(float sx, float sy, float u0, float v0, float u1, float v1)
            => Line(Iso(u0 * sx, v0 * sy, 0f), Iso(u1 * sx, v1 * sy, 0f));

        /// <summary>A regular polygon, first vertex at the top.</summary>
        public Vector2[] Polygon(Vector2 centre, float radius, int sides)
        {
            var pts = new Vector2[Mathf.Max(3, sides)];
            for (int i = 0; i < pts.Length; i++)
            {
                float a = (90f + i * (360f / pts.Length)) * Mathf.Deg2Rad;
                pts[i] = new Vector2(centre.x + Mathf.Cos(a) * radius, centre.y + Mathf.Sin(a) * radius);
            }
            return pts;
        }

        private static Vector2 OnEllipse(Vector2 c, float rx, float ry, float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(c.x + Mathf.Cos(rad) * rx, c.y + Mathf.Sin(rad) * ry);
        }

        private void Encapsulate(Vector2 p)
        {
            if (p.x < _min.x) _min.x = p.x;
            if (p.y < _min.y) _min.y = p.y;
            if (p.x > _max.x) _max.x = p.x;
            if (p.y > _max.y) _max.y = p.y;
        }

        /// <summary>
        /// Bakes the collected strokes into a square white mask. Only the pixels
        /// inside each segment's padded bounding box are touched, so a 128 px icon
        /// with thirty strokes costs a few thousand distance evaluations, not a
        /// full-texture pass per stroke.
        /// </summary>
        public Texture2D Rasterize(int size, float strokePx, float padPx, string textureName)
        {
            size = Mathf.Max(8, size);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = string.IsNullOrEmpty(textureName) ? "LineArt" : textureName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            if (_segments.Count == 0)
            {
                tex.SetPixels32(pixels);
                tex.Apply(false, false);
                return tex;
            }

            float spanX = Mathf.Max(1e-4f, _max.x - _min.x);
            float spanY = Mathf.Max(1e-4f, _max.y - _min.y);
            float scale = (size - 2f * padPx) / Mathf.Max(spanX, spanY);
            Vector2 mid = (_min + _max) * 0.5f;
            Vector2 origin = new(size * 0.5f, size * 0.5f);

            const float aa = 0.9f;
            float half = Mathf.Max(0.5f, strokePx * 0.5f);
            float reach = half + aa + 1f;
            var coverage = new float[size * size];

            foreach (var seg in _segments)
            {
                Vector2 a = (new Vector2(seg.x, seg.y) - mid) * scale + origin;
                Vector2 b = (new Vector2(seg.z, seg.w) - mid) * scale + origin;

                int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) - reach), 0, size - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x) + reach), 0, size - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y) - reach), 0, size - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y) + reach), 0, size - 1);

                Vector2 ab = b - a;
                float lenSq = Mathf.Max(1e-6f, ab.sqrMagnitude);

                for (int y = y0; y <= y1; y++)
                {
                    int row = y * size;
                    for (int x = x0; x <= x1; x++)
                    {
                        Vector2 p = new(x + 0.5f, y + 0.5f);
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
                        float dist = (p - (a + ab * t)).magnitude;
                        float v = Mathf.Clamp01((half + aa - dist) / (2f * aa));
                        int i = row + x;
                        if (v > coverage[i]) coverage[i] = v;
                    }
                }
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                byte alpha = (byte)Mathf.RoundToInt(coverage[i] * 255f);
                pixels[i] = new Color32(255, 255, 255, alpha);
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}
