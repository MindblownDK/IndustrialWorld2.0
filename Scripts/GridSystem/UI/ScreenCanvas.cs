// Assets/Scripts/VoxelEngine/GridSystem/UI/ScreenCanvas.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        A DRAWING SURFACE MADE OF CHARACTERS (17.42.0-dev)        ║
// ║                                                                  ║
// ║  A grid screen is a TextMesh. There is no second render target,  ║
// ║  no canvas, no shader that draws a chart - and there should not  ║
// ║  be one, because a page that cannot be read at a glance in the   ║
// ║  dark is a page that failed. Everything the pages draw is        ║
// ║  characters: box-drawing for frames, block elements for fills,  ║
// ║  and half blocks for line charts.                                ║
// ║                                                                  ║
// ║  17.42.0-dev — INK LAYERS.                                       ║
// ║  A page is still one character grid, but every cell now carries  ║
// ║  an ink class along with its glyph. The block stacks one text    ║
// ║  mesh per ink over the same glass, so a page can have a dim      ║
// ║  label, a bright value, a green cell and a red warning without   ║
// ║  a single quad of guesswork. Layers are built from the SAME      ║
// ║  grid, so they can never drift apart.                            ║
// ║                                                                  ║
// ║  This class is the one place that knows how to put a character   ║
// ║  at a column and a row without running off the panel. Pages are  ║
// ║  written against it instead of building strings by hand.         ║
// ║                                                                  ║
// ║  It also carries the page's ICON requests: an item drawn in a    ║
// ║  cell is recorded here, and the block turns those into small     ║
// ║  textured quads laid over the glass.                             ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem.UI
{
    /// <summary>
    /// Which colour layer a glyph lives on. The block gives every layer its own
    /// colour from the page's palette; the same class reads as the same voice on
    /// every page (Dim speaks quietly, Warn says something is wrong).
    /// </summary>
    public enum ScreenInk : byte
    {
        /// <summary>The page's normal reading text.</summary>
        Primary = 0,
        /// <summary>Highlights, traces, fills, section rules - the page's accent.</summary>
        Accent = 1,
        /// <summary>Labels, units, footnotes - deliberately quiet.</summary>
        Dim = 2,
        /// <summary>Warnings, deficits, dead plants, empty stores.</summary>
        Warn = 3,
        /// <summary>Healthy, charged, ready, breathable.</summary>
        Ok = 4,
    }

    /// <summary>One item picture the page asked for, in character cells.</summary>
    public readonly struct ScreenIconRequest
    {
        public readonly int Column;
        public readonly int Row;
        public readonly int Width;
        public readonly int Height;
        public readonly ItemDefinition Item;

        public ScreenIconRequest(int column, int row, int width, int height, ItemDefinition item)
        {
            Column = column; Row = row; Width = width; Height = height; Item = item;
        }
    }

    /// <summary>A character grid a page draws on. Columns and rows are counted from the
    /// top-left, because that is how a page reads.</summary>
    public sealed class ScreenCanvas
    {
        public readonly int Columns;
        public readonly int Rows;

        private readonly char[] _glyphs;
        private readonly byte[] _inks;
        private readonly List<ScreenIconRequest> _icons = new(32);

        /// <summary>The item pictures this page wants drawn over the glass.</summary>
        public IReadOnlyList<ScreenIconRequest> Icons => _icons;

        public ScreenCanvas(int columns, int rows)
        {
            Columns = Mathf.Max(1, columns);
            Rows = Mathf.Max(1, rows);
            _glyphs = new char[Columns * Rows];
            _inks = new byte[Columns * Rows];
            for (int i = 0; i < _glyphs.Length; i++) _glyphs[i] = ' ';
        }

        // ── Primitives ──────────────────────────────────────────────────────────

        public void Set(int column, int row, char glyph, ScreenInk ink = ScreenInk.Primary)
        {
            if (column < 0 || column >= Columns || row < 0 || row >= Rows) return;
            int index = row * Columns + column;
            _glyphs[index] = glyph;
            _inks[index] = (byte)ink;
        }

        public char Get(int column, int row)
        {
            if (column < 0 || column >= Columns || row < 0 || row >= Rows) return ' ';
            return _glyphs[row * Columns + column];
        }

        public void Fill(char glyph, ScreenInk ink = ScreenInk.Primary)
        {
            for (int i = 0; i < _glyphs.Length; i++) { _glyphs[i] = glyph; _inks[i] = (byte)ink; }
        }

        /// <summary>Fill a rectangle without touching its border cells.</summary>
        public void FillRect(int column, int row, int width, int height,
            char glyph, ScreenInk ink = ScreenInk.Primary)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    Set(column + x, row + y, glyph, ink);
        }

        /// <summary>Write a string, clipped to the canvas. Newlines start a new row.</summary>
        public void Text(int column, int row, string text, bool rightAlign = false,
            ScreenInk ink = ScreenInk.Primary)
        {
            if (string.IsNullOrEmpty(text)) return;
            // Right alignment puts the LAST character on the given column, which is how a
            // page reads "this number ends where the panel ends".
            if (rightAlign) column -= text.Length - 1;

            int startColumn = column;
            int x = column;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n') { row++; x = startColumn; continue; }
                Set(x++, row, c, ink);
            }
        }

        /// <summary>Write a string ending exactly at <paramref name="rightColumn"/>.</summary>
        public void TextRight(int rightColumn, int row, string text,
            ScreenInk ink = ScreenInk.Primary)
        {
            if (string.IsNullOrEmpty(text)) return;
            Text(rightColumn, row, text, rightAlign: true, ink);
        }

        /// <summary>Write a string, shortened to fit the given width. The ellipsis is the
        /// page's promise that the name continues, not a silent truncation.</summary>
        public void TextClipped(int column, int row, string text, int width,
            ScreenInk ink = ScreenInk.Primary)
        {
            if (width <= 0) return;
            if (string.IsNullOrEmpty(text)) return;
            if (text.Length > width) text = text.Substring(0, Mathf.Max(1, width - 1)) + "\u2026";
            Text(column, row, text, false, ink);
        }

        /// <summary>Write a string shortened to fit and right-aligned into a column span.</summary>
        public void TextRightClipped(int rightColumn, int row, string text, int width,
            ScreenInk ink = ScreenInk.Primary)
        {
            if (width <= 0) return;
            if (string.IsNullOrEmpty(text)) return;
            if (text.Length > width) text = text.Substring(0, Mathf.Max(1, width - 1)) + "\u2026";
            TextRight(rightColumn, row, text, ink);
        }

        /// <summary>Centred inside [column, column+width).</summary>
        public void TextCentered(int column, int row, int width, string text,
            ScreenInk ink = ScreenInk.Primary)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return;
            if (text.Length > width) text = text.Substring(0, Mathf.Max(1, width - 1)) + "\u2026";
            int x = column + Mathf.Max(0, (width - text.Length) / 2);
            Text(x, row, text, false, ink);
        }

        /// <summary>A single-line frame: the house style for a grouped reading.</summary>
        public void Box(int column, int row, int width, int height,
            ScreenInk ink = ScreenInk.Primary)
        {
            if (width < 2 || height < 2) return;
            int right = column + width - 1;
            int bottom = row + height - 1;

            Set(column, row, '\u250C', ink);         // ┌
            Set(right, row, '\u2510', ink);          // ┐
            Set(column, bottom, '\u2514', ink);      // └
            Set(right, bottom, '\u2518', ink);       // ┘
            for (int x = column + 1; x < right; x++)
            {
                Set(x, row, '\u2500', ink);          // ─
                Set(x, bottom, '\u2500', ink);
            }
            for (int y = row + 1; y < bottom; y++)
            {
                Set(column, y, '\u2502', ink);       // │
                Set(right, y, '\u2502', ink);
            }
        }

        /// <summary>A box drawn in the heavy weight, for the one reading that matters most
        /// on the page.</summary>
        public void HeavyBox(int column, int row, int width, int height,
            ScreenInk ink = ScreenInk.Primary)
        {
            if (width < 2 || height < 2) return;
            int right = column + width - 1;
            int bottom = row + height - 1;

            Set(column, row, '\u250F', ink);
            Set(right, row, '\u2513', ink);
            Set(column, bottom, '\u2517', ink);
            Set(right, bottom, '\u251B', ink);
            for (int x = column + 1; x < right; x++)
            {
                Set(x, row, '\u2501', ink);
                Set(x, bottom, '\u2501', ink);
            }
            for (int y = row + 1; y < bottom; y++)
            {
                Set(column, y, '\u2503', ink);
                Set(right, y, '\u2503', ink);
            }
        }

        public void HLine(int column, int row, int width,
            char glyph = '\u2500', ScreenInk ink = ScreenInk.Primary)
        {
            for (int x = 0; x < width; x++) Set(column + x, row, glyph, ink);
        }

        public void VLine(int column, int row, int height,
            char glyph = '\u2502', ScreenInk ink = ScreenInk.Primary)
        {
            for (int y = 0; y < height; y++) Set(column, row + y, glyph, ink);
        }

        /// <summary>
        /// A section heading in the terminal style: "---- LABEL ----" or
        /// "---- LABEL ----" filling the width. The page's rhythm comes from these.
        /// </summary>
        public void SectionHeader(int column, int row, int width, string label,
            ScreenInk ink = ScreenInk.Dim)
        {
            if (width <= 2) return;
            string text = string.IsNullOrEmpty(label) ? string.Empty : " " + label + " ";
            int dashLeft = 4;
            int dashRight = Mathf.Max(0, width - text.Length - dashLeft);
            string line = new string('-', dashLeft) + text + new string('-', dashRight);
            TextClipped(column, row, line, width, ink);
        }

        /// <summary>A percentage drawn as a bar. A step is a character, so a panel never
        /// draws a "half" it cannot show.</summary>
        public void Bar(int column, int row, int width, float fill01,
            char full = '\u2588', char empty = '\u2591', ScreenInk ink = ScreenInk.Accent)
        {
            if (width <= 0) return;
            int steps = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * width), 0, width);
            for (int x = 0; x < width; x++) Set(column + x, row, x < steps ? full : empty, ink);
        }

        /// <summary>
        /// A bar with a separate ink for the filled part: the empty tail whispers so the
        /// fill is the only loud thing on the row.
        /// </summary>
        public void BarTwoTone(int column, int row, int width, float fill01,
            ScreenInk fillInk, ScreenInk emptyInk, char full = '\u2588', char empty = '\u2591')
        {
            if (width <= 0) return;
            int steps = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * width), 0, width);
            for (int x = 0; x < width; x++)
                Set(column + x, row, x < steps ? full : empty, x < steps ? fillInk : emptyInk);
        }

        /// <summary>
        /// A small tick gauge: the value's first cells are bright ticks and the rest are
        /// quiet. Reads as a bar without the weight of a solid block fill - used for
        /// water levels and secondary readings where a full bar would shout.
        /// </summary>
        public void Ticks(int column, int row, int width, float fill01,
            ScreenInk fillInk, ScreenInk emptyInk, char glyph = '\u2588')
        {
            if (width <= 0) return;
            int steps = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * width), 0, width);
            for (int x = 0; x < width; x++)
                Set(column + x, row, glyph, x < steps ? fillInk : emptyInk);
        }

        /// <summary>An "icon cell": a quiet frame the item picture is drawn inside, with its
        /// count underneath. The text side reserves the space; the quad side fills it.</summary>
        public void Icon(int column, int row, int width, int height, ItemDefinition item)
        {
            if (item == null || width <= 0 || height <= 0) return;
            _icons.Add(new ScreenIconRequest(column, row, width, height, item));
        }

        /// <summary>
        /// A vertical fill bar: the farm cards' growth and water columns. It fills from
        /// the base upward, which is how a plant grows and how a tank drains in reverse.
        /// </summary>
        public void VBarInk(int column, int topRow, int height, float fill01,
            ScreenInk fillInk, ScreenInk emptyInk, char full = '\u2588', char empty = '\u2591')
        {
            if (height <= 0) return;
            int steps = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * height), 0, height);
            for (int i = 0; i < height; i++)
            {
                bool on = i >= height - steps;
                Set(column, topRow + i, on ? full : empty, on ? fillInk : emptyInk);
            }
        }

        // ── Charts ──────────────────────────────────────────────────────────────

        /// <summary>
        /// A line chart over half-block characters: the trace is placed on a sub-cell
        /// row (two per character) so a chart with a dozen rows has two dozen steps and
        /// reads as a line instead of a staircase of whole blocks. Vertical runs between
        /// two columns are joined, so a jump reads as a cliff and not a dotted trail.
        /// </summary>
        public void Plot(int column, int row, int width, int height,
            IReadOnlyList<float> values, float scale, char glyph,
            ScreenInk ink = ScreenInk.Accent)
        {
            if (width < 2 || height < 2 || values == null || values.Count == 0) return;
            if (scale <= 0.0001f) scale = 1f;

            int subRows = height * 2;
            int previous = -1;
            for (int x = 0; x < width; x++)
            {
                int index = Mathf.Clamp(
                    Mathf.FloorToInt(x * (values.Count - 1f) / Mathf.Max(1, width - 1)),
                    0, values.Count - 1);
                float v = Mathf.Clamp01(values[index] / scale);
                // Sub-row 0 is the top of the plot; the trace sits on its own half-cell.
                int sub = Mathf.Clamp(Mathf.RoundToInt((1f - v) * (subRows - 1)), 0, subRows - 1);

                PlotSub(column + x, row, sub, ink);
                if (previous >= 0 && previous != sub)
                {
                    int from = Mathf.Min(previous, sub);
                    int to = Mathf.Max(previous, sub);
                    for (int between = from + 1; between < to; between++)
                        PlotSub(column + x, row, between, ink, join: true);
                }
                previous = sub;
            }
        }

        /// <summary>
        /// One half-cell of a trace: the upper or lower half of a character cell.
        ///
        /// A chart line should be a LINE. A full block per sample reads as a row of
        /// squares - which is what a panel of them looks like across a room - so the trace
        /// is drawn with the lower half block only, which puts a single thin rule at the
        /// sample's height and lets two traces of different colours sit in one cell if
        /// they cross.
        /// </summary>
        private void PlotSub(int column, int row, int subRow, ScreenInk ink, bool join = false)
        {
            int r = row + subRow / 2;
            if (join) Set(column, r, '\u2502', ink);                    // │ between samples
            else Set(column, r, subRow % 2 == 0 ? '\u2580' : '\u2584', ink);
        }

        /// <summary>
        /// The quiet lattice a chart is drawn over: one faint dot per few cells, so the
        /// plot has paper under it without a grid that competes with the trace.
        /// </summary>
        public void ChartLattice(int column, int row, int width, int height,
            ScreenInk ink = ScreenInk.Dim)
        {
            // Sparse: a chart's grid paper must whisper. Every sixth column, every
            // third row - anything denser reads as static behind the traces.
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (x % 6 == 0 && y % 3 == 0 && Get(column + x, row + y) == ' ')
                        Set(column + x, row + y, '\u00b7', ink);
        }

        /// <summary>
        /// A battery: a clean case with its charge rising from the base in five bands.
        ///
        /// The old drawing put a terminal nub above the case, which on a character grid
        /// reads as two loose squares floating above the picture, and separated the five
        /// bands with blank rows, which at any real cell size looks like three thin
        /// stripes inside an empty box. The case is now a plain rectangle - the way the
        /// reference board draws a cell - and the charge is one continuous fill quantised
        /// into five steps, so it reads as a level gauge from across a room.
        /// </summary>
        public void Battery(int column, int row, int width, float fill01,
            ScreenInk fillInk = ScreenInk.Ok, ScreenInk caseInk = ScreenInk.Accent,
            int height = 6)
        {
            if (width < 3) width = 3;
            if (height < 4) height = 4;
            int right = column + width - 1;
            int bottom = row + height - 1;

            // The case: top rule, both sides, base.
            Set(column, row, '\u250c', caseInk);               // ┌
            Set(right, row, '\u2510', caseInk);                // ┐
            Set(column, bottom, '\u2514', caseInk);            // └
            Set(right, bottom, '\u2518', caseInk);             // ┘
            HLine(column + 1, row, width - 2, '\u2500', caseInk);
            HLine(column + 1, bottom, width - 2, '\u2500', caseInk);

            int innerRows = height - 2;
            if (innerRows <= 0) return;

            // Five bands of charge, filled from the base up - no blank rows between them,
            // so the level is one edge and not five islands.
            int bands = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * 5f), 0, 5);
            int litRows = Mathf.RoundToInt(bands * innerRows / 5f);

            for (int i = 0; i < innerRows; i++)
            {
                int y = row + 1 + i;
                Set(column, y, '\u2502', caseInk);
                Set(right, y, '\u2502', caseInk);
                bool lit = i >= innerRows - litRows;
                for (int x = column + 1; x < right; x++)
                    Set(x, y, lit ? '\u2588' : '\u2591', lit ? fillInk : caseInk);
            }
        }

        /// <summary>
        /// A battery reduced to one row, for a header line: a cap on each side and the fill
        /// between them.
        /// </summary>
        public void BatterySegment(int column, int row, int width, float fill01,
            ScreenInk fillInk = ScreenInk.Ok, ScreenInk caseInk = ScreenInk.Accent)
        {
            if (width < 3) width = 3;
            int right = column + width - 1;
            int steps = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * (width - 2)), 0, width - 2);

            Set(column, row, '\u2590', caseInk);                // ▐
            Set(right, row, '\u258C', caseInk);                 // ▌
            for (int x = column + 1; x < right; x++)
                Set(x, row, x - column <= steps ? '\u2588' : '\u2591', x - column <= steps ? fillInk : ScreenInk.Dim);
        }

        // ── Gauges, globes and fields ───────────────────────────────────────────

        /// <summary>
        /// A ring dial of radial tick marks - the mining gauge. Ticks stand all the way
        /// round the circle, split at the two horizontal seams: the top half reads one
        /// value, the bottom half the other, and each half lights its ticks one lamp at a
        /// time from the left. A cell-wise rasterisation of the true circle (the caller
        /// hands in radii already corrected for the cell's shape) keeps the ring round on
        /// a character grid, where per-column sampling came out as a flattened oval.
        /// </summary>
        public void RingDial(int centerX, int centerY, float radiusColumns, float radiusRows,
            float upperValue01, float lowerValue01, ScreenInk litInk, ScreenInk dimInk,
            int ticksPerHalf = 20)
        {
            if (radiusColumns < 3f || radiusRows < 2f) return;
            ticksPerHalf = Mathf.Clamp(ticksPerHalf, 8, 40);
            float upper = Mathf.Clamp01(upperValue01);
            float lower = Mathf.Clamp01(lowerValue01);

            int reachX = Mathf.CeilToInt(radiusColumns);
            int reachY = Mathf.CeilToInt(radiusRows);
            for (int dy = -reachY; dy <= reachY; dy++)
            {
                for (int dx = -reachX; dx <= reachX; dx++)
                {
                    float u = dx / Mathf.Max(1f, radiusColumns);
                    float v = dy / Mathf.Max(1f, radiusRows);
                    float d = Mathf.Sqrt(u * u + v * v);
                    // The tick band: one lamp thick, hugging the rim. A wide band turns
                    // the dial into a sunburst of wedges; the reference's dial is a ring
                    // of thin radial dashes around an open face.
                    if (d < 0.86f || d > 1.0f) continue;

                    bool upperHalf = v < 0f;
                    float phi = Mathf.Atan2(-v, u);            // radians, y up
                    // A hair of gap at the two seams, where the halves' scales meet.
                    if (Mathf.Abs(phi) < 0.035f * Mathf.PI) continue;
                    if (Mathf.Abs(phi) > 0.965f * Mathf.PI) continue;

                    float progress = upperHalf ? (Mathf.PI - phi) / Mathf.PI
                                               : (phi + Mathf.PI) / Mathf.PI;
                    float scaled = progress * ticksPerHalf;
                    int tick = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, ticksPerHalf - 1);
                    float within = scaled - tick;
                    // The dash: lit through the middle of its slot, blank at the edges,
                    // so the ring is a row of spokes and not a painted band.
                    if (within < 0.30f || within > 0.74f) continue;

                    bool on = (tick + 0.5f) / ticksPerHalf <= (upperHalf ? upper : lower) + 0.0001f;
                    Set(centerX + dx, centerY + dy, '\u2588', on ? litInk : dimInk);
                }
            }

            // The seam between the halves: a short rule across the middle of the dial,
            // stopping well inside the ring, the way the reference divides its readings.
            int rule = Mathf.Max(2, Mathf.RoundToInt(radiusColumns * 0.42f));
            for (int dx = -rule; dx <= rule; dx++)
            {
                if (Get(centerX + dx, centerY) == ' ')
                    Set(centerX + dx, centerY, '\u2500', dimInk);
            }
        }

        /// <summary>
        /// A shaded sphere: the planet every map page and cockpit display draws. The
        /// shading runs from a lit limb to a dark one, the equator is drawn as a lighter
        /// band, and the rim is one glyph brighter than the body - a globe, not a ball.
        /// </summary>
        public void Sphere(int column, int row, int width, int height,
            ScreenInk shadeInk, ScreenInk rimInk, ScreenInk bandInk)
        {
            if (width < 3 || height < 3) return;
            float cx = column + (width - 1) * 0.5f;
            float cy = row + (height - 1) * 0.5f;
            float rx = Mathf.Max(1f, (width - 1) * 0.5f);
            float ry = Mathf.Max(1f, (height - 1) * 0.5f);

            for (int y = row; y < row + height; y++)
            {
                for (int x = column; x < column + width; x++)
                {
                    float u = (x - cx) / rx;
                    float v = (y - cy) / ry;
                    float d = u * u + v * v;
                    if (d > 1.0f) continue;

                    bool rim = d > 0.70f;
                    // Light comes from the upper left, as it does in every reference.
                    float light = 0.55f + 0.45f * (-(u * 0.6f) + (v * 0.55f));
                    char glyph = rim ? '\u2588'
                        : light > 0.72f ? '\u2588'
                        : light > 0.42f ? '\u2593'
                        : '\u2592';

                    // The equator and one meridian, so the globe reads as mapped.
                    bool band = Mathf.Abs(v) < 0.09f && !rim;
                    bool meridian = Mathf.Abs(u) < 0.07f && !rim;
                    ScreenInk ink = rim ? rimInk : band || meridian ? bandInk : shadeInk;
                    if (band || meridian) glyph = '\u2591';
                    Set(x, y, glyph, ink);
                }
            }
        }

        /// <summary>
        /// The chart paper behind a planet map: a faint lattice of dots that never
        /// competes with the globe drawn over it.
        /// </summary>
        public void DotField(int column, int row, int width, int height,
            char glyph = '\u00b7', ScreenInk ink = ScreenInk.Dim, int everyX = 3, int everyY = 2)
        {
            everyX = Mathf.Max(1, everyX);
            everyY = Mathf.Max(1, everyY);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (x % everyX == 0 && y % everyY == 0)
                        Set(column + x, row + y, glyph, ink);
        }

        // ── Panes ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Copy a page, already built, into a rectangle of this canvas. Text stops at the
        /// rectangle's right edge and at its last row, which is what makes a split panel
        /// safe: a page written for a whole glass cannot spill into its neighbour.
        /// </summary>
        public void Blit(ScreenCanvas source, int column, int row, int width, int height)
        {
            if (source == null || width <= 0 || height <= 0) return;
            for (int y = 0; y < height && y < source.Rows; y++)
            {
                for (int x = 0; x < width && x < source.Columns; x++)
                {
                    char glyph = source.Get(x, y);
                    if (glyph == ' ') continue;
                    Set(column + x, row + y, glyph, source.InkAt(x, y));
                }
            }
        }

        /// <summary>The ink a cell was drawn in.</summary>
        public ScreenInk InkAt(int column, int row)
        {
            if (column < 0 || column >= Columns || row < 0 || row >= Rows) return ScreenInk.Primary;
            return (ScreenInk)_inks[row * Columns + column];
        }

        /// <summary>
        /// Move another canvas's icon requests into this one, offset by the pane's corner.
        /// Requests that would fall outside this canvas are dropped rather than clamped:
        /// a picture half over a divider is worse than no picture.
        /// </summary>
        public void AddIcons(IReadOnlyList<ScreenIconRequest> icons, int offsetX, int offsetY)
        {
            if (icons == null) return;
            for (int i = 0; i < icons.Count; i++)
            {
                var request = icons[i];
                if (request.Item == null) continue;

                int column = request.Column + offsetX;
                int row = request.Row + offsetY;
                if (column < 0 || column >= Columns || row < 0 || row >= Rows) continue;

                _icons.Add(new ScreenIconRequest(column, row, request.Width, request.Height, request.Item));
            }
        }

        // ── Output ──────────────────────────────────────────────────────────────

        /// <summary>How many rows the last build actually printed. The block needs this to
        /// map icon cells onto the text it fitted.</summary>
        public int PrintedRows { get; private set; }

        /// <summary>The page as one string, every ink merged. Used by the config preview
        /// and by legacy string readers; the glass itself takes the layer build.</summary>
        public string Build()
        {
            var sb = new StringBuilder((Columns + 1) * Rows);
            PrintedRows = 0;
            for (int y = 0; y < Rows; y++)
            {
                int end = Columns - 1;
                while (end >= 0 && _glyphs[y * Columns + end] == ' ') end--;
                for (int x = 0; x <= end; x++) sb.Append(_glyphs[y * Columns + x]);
                if (end >= 0) PrintedRows = y + 1;
                if (y < Rows - 1) sb.Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// One ink's share of the page: the same grid, spaces wherever another ink spoke.
        /// Every layer has identical geometry, so stacking them on the glass keeps every
        /// glyph in its own cell.
        /// </summary>
        public string BuildLayer(ScreenInk ink)
        {
            byte wanted = (byte)ink;
            var sb = new StringBuilder((Columns + 1) * Rows);
            for (int y = 0; y < Rows; y++)
            {
                int end = Columns - 1;
                while (end >= 0 && (_glyphs[y * Columns + end] == ' ' || _inks[y * Columns + end] != wanted)) end--;
                for (int x = 0; x <= end; x++)
                {
                    int index = y * Columns + x;
                    sb.Append(_glyphs[index] == ' ' || _inks[index] != wanted ? ' ' : _glyphs[index]);
                }
                if (y < Rows - 1) sb.Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>The widest printed column after a build: the block's own width in
        /// cells, which is what the pictures are stretched across.</summary>
        public int MeasurePrintedColumns()
        {
            int widest = 0;
            for (int y = 0; y < Rows; y++)
            {
                for (int x = Columns - 1; x >= widest; x--)
                {
                    if (_glyphs[y * Columns + x] == ' ') continue;
                    if (x + 1 > widest) widest = x + 1;
                    break;
                }
            }
            return widest;
        }

        /// <summary>Every layer's row count after a build: the tallest printed row.</summary>
        public int MeasurePrintedRows()
        {
            for (int y = Rows - 1; y >= 0; y--)
            {
                for (int x = 0; x < Columns; x++)
                    if (_glyphs[y * Columns + x] != ' ') return y + 1;
            }
            return 0;
        }
    }
}
