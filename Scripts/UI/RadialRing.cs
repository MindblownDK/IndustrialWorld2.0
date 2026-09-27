// Assets/Scripts/VoxelEngine/UI/RadialRing.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║            INDUSTRIAL WORLD — VECTOR RADIAL RING                  ║
// ║                                                                  ║
// ║  A donut of angular wedges drawn with Painter2D. Vector, not a   ║
// ║  baked texture: the wedge boundaries are exact at any resolution,║
// ║  a hover repaint costs one mesh rebuild instead of a 65k-pixel   ║
// ║  CPU loop, and the hovered wedge can physically grow out of the  ║
// ║  ring without smearing a bitmap.                                 ║
// ║                                                                  ║
// ║  The owner supplies a per-wedge style through WedgeProvider, so  ║
// ║  this element holds no game state at all.                        ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    /// <summary>Per-wedge paint description handed back by <see cref="RadialRing.WedgeProvider"/>.</summary>
    public struct RadialWedge
    {
        public Color Fill;
        public Color Rim;
        public float RimWidth;
        /// <summary>Pops the wedge out of the ring — the hover tell.</summary>
        public bool Expanded;
        /// <summary>Soft halo behind an expanded wedge.</summary>
        public Color Glow;
    }

    /// <summary>
    /// Draws <see cref="SegmentCount"/> wedges around the element centre, wedge 0
    /// centred at 12 o'clock and running clockwise.
    /// </summary>
    public sealed class RadialRing : VisualElement
    {
        public int   SegmentCount = 8;
        public float InnerRadius  = 206f;
        public float OuterRadius  = 286f;
        /// <summary>Gap between wedges, measured in pixels at the ring's mid radius.</summary>
        public float GapPixels    = 6f;
        public float ExpandOuter  = 14f;
        public float ExpandInner  = 8f;

        public float HairlineOuterRadius = 302f;
        public float HairlineInnerRadius = 192f;
        public float HairlineWidth       = 1.6f;
        public Color HairlineColor       = new(0.90f, 0.88f, 0.84f, 0.26f);

        /// <summary>Filled behind the wedges so gaps read as inlay, not as holes.</summary>
        public Color BackdropColor = new(0.02f, 0.023f, 0.03f, 0.55f);

        public Func<int, RadialWedge> WedgeProvider;

        public RadialRing()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Repaint() => MarkDirtyRepaint();

        private void Draw(MeshGenerationContext ctx)
        {
            if (SegmentCount <= 0 || WedgeProvider == null) return;

            var rect = contentRect;
            if (rect.width <= 1f || rect.height <= 1f) return;

            var painter = ctx.painter2D;
            Vector2 centre = rect.center;

            // Backdrop disc first — everything else lands on top of it. A disc
            // rather than a donut: the opaque hub covers the middle anyway, and
            // the thin annulus left showing reads as an inset seat for the dial.
            if (BackdropColor.a > 0.001f)
            {
                painter.fillColor = BackdropColor;
                painter.BeginPath();
                painter.Arc(centre, HairlineOuterRadius, Angle.Degrees(0f), Angle.Degrees(360f));
                painter.ClosePath();
                painter.Fill(FillRule.NonZero);
            }

            float step = 360f / SegmentCount;
            float midRadius = (InnerRadius + OuterRadius) * 0.5f;
            float gapDegrees = Mathf.Clamp(GapPixels * Mathf.Rad2Deg / Mathf.Max(1f, midRadius), 0f, step * 0.45f);

            // Pass 1 — haloes, so a neighbouring wedge never paints over them.
            for (int i = 0; i < SegmentCount; i++)
            {
                var wedge = WedgeProvider(i);
                if (!wedge.Expanded || wedge.Glow.a <= 0.001f) continue;
                painter.fillColor = wedge.Glow;
                painter.BeginPath();
                WedgePath(painter, centre, i, step, gapDegrees * 0.55f,
                          InnerRadius - ExpandInner - 7f, OuterRadius + ExpandOuter + 9f);
                painter.Fill(FillRule.NonZero);
            }

            // Pass 2 — the wedges themselves.
            for (int i = 0; i < SegmentCount; i++)
            {
                var wedge = WedgeProvider(i);
                float inner = wedge.Expanded ? InnerRadius - ExpandInner : InnerRadius;
                float outer = wedge.Expanded ? OuterRadius + ExpandOuter : OuterRadius;

                painter.fillColor = wedge.Fill;
                painter.BeginPath();
                WedgePath(painter, centre, i, step, gapDegrees, inner, outer);
                painter.Fill(FillRule.NonZero);

                if (wedge.RimWidth > 0.01f && wedge.Rim.a > 0.001f)
                {
                    painter.strokeColor = wedge.Rim;
                    painter.lineWidth = wedge.RimWidth;
                    painter.lineJoin = LineJoin.Round;
                    painter.BeginPath();
                    WedgePath(painter, centre, i, step, gapDegrees, inner, outer);
                    painter.Stroke();
                }
            }

            // Pass 3 — the two hairline circles that frame the dial.
            if (HairlineColor.a > 0.001f && HairlineWidth > 0.01f)
            {
                painter.strokeColor = HairlineColor;
                painter.lineWidth = HairlineWidth;
                StrokeCircle(painter, centre, HairlineOuterRadius);
                StrokeCircle(painter, centre, HairlineInnerRadius);
            }
        }

        /// <summary>Builds one wedge as an outer arc, a radial step and an inner arc back.</summary>
        private static void WedgePath(Painter2D painter, Vector2 centre, int index,
                                      float step, float gapDegrees, float inner, float outer)
        {
            // Painter angles are measured from +X and sweep clockwise on screen,
            // so a wedge centred at 12 o'clock starts at -90 degrees.
            float mid = index * step - 90f;
            float from = mid - step * 0.5f + gapDegrees * 0.5f;
            float to   = mid + step * 0.5f - gapDegrees * 0.5f;

            painter.Arc(centre, outer, Angle.Degrees(from), Angle.Degrees(to), ArcDirection.Clockwise);
            painter.Arc(centre, inner, Angle.Degrees(to), Angle.Degrees(from), ArcDirection.CounterClockwise);
            painter.ClosePath();
        }

        private static void StrokeCircle(Painter2D painter, Vector2 centre, float radius)
        {
            if (radius <= 0.5f) return;
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.ClosePath();
            painter.Stroke();
        }
    }
}
