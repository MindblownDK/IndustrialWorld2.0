// Assets/Scripts/VoxelEngine/GridSystem/UI/ScreenCanvas.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║            A DRAWING SURFACE MADE OF CHARACTERS (17.36.0)        ║
// ║                                                                  ║
// ║  A grid screen is a TextMesh. There is no second render target,  ║
// ║  no canvas, no shader that draws a chart - and there should not  ║
// ║  be one, because a page that cannot be read at a glance in the   ║
// ║  dark is a page that failed. Everything the pages draw is        ║
// ║  therefore characters: box-drawing for frames, block elements    ║
// ║  for fills, and half blocks for line charts.                     ║
// ║                                                                  ║
// ║  This class is the one place that knows how to put a character   ║
// ║  at a column and a row without running off the panel. Pages are  ║
// ║  written against it instead of building strings by hand, which   ║
// ║  is what made the old pages drift out of alignment the moment    ║
// ║  a name was one letter longer than expected.                     ║
// ║                                                                  ║
// ║  It also carries the page's ICON requests: an item drawn in a    ║
// ║  cell is recorded here, and the block turns those into small     ║
// ║  textured quads laid over the glass. Text alone cannot show an   ║
// ║  item, and a storage monitor without item pictures is a list of  ║
// ║  words pretending to be a container.                            ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem.UI
{
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

        private readonly char[] _cells;
        private readonly List<ScreenIconRequest> _icons = new(32);

        /// <summary>The item pictures this page wants drawn over the glass.</summary>
        public IReadOnlyList<ScreenIconRequest> Icons => _icons;

        public ScreenCanvas(int columns, int rows)
        {
            Columns = Mathf.Max(1, columns);
            Rows = Mathf.Max(1, rows);
            _cells = new char[Columns * Rows];
            for (int i = 0; i < _cells.Length; i++) _cells[i] = ' ';
        }

        // ── Primitives ──────────────────────────────────────────────────────────

        public void Set(int column, int row, char glyph)
        {
            if (column < 0 || column >= Columns || row < 0 || row >= Rows) return;
            _cells[row * Columns + column] = glyph;
        }

        public char Get(int column, int row)
        {
            if (column < 0 || column >= Columns || row < 0 || row >= Rows) return ' ';
            return _cells[row * Columns + column];
        }

        public void Fill(char glyph)
        {
            for (int i = 0; i < _cells.Length; i++) _cells[i] = glyph;
        }

        /// <summary>Write a string, clipped to the canvas. Newlines start a new row.</summary>
        public void Text(int column, int row, string text, bool rightAlign = false)
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
                Set(x++, row, c);
            }
        }

        /// <summary>Write a string, shortened to fit the given width. The ellipsis is the
        /// page's promise that the name continues, not a silent truncation.</summary>
        public void TextClipped(int column, int row, string text, int width)
        {
            if (width <= 0) return;
            if (string.IsNullOrEmpty(text)) return;
            if (text.Length > width) text = text.Substring(0, Mathf.Max(1, width - 1)) + "\u2026";
            Text(column, row, text);
        }

        /// <summary>A single-line frame: the house style for a grouped reading.</summary>
        public void Box(int column, int row, int width, int height)
        {
            if (width < 2 || height < 2) return;
            int right = column + width - 1;
            int bottom = row + height - 1;

            Set(column, row, '\u250C');            // ┌
            Set(right, row, '\u2510');             // ┐
            Set(column, bottom, '\u2514');         // └
            Set(right, bottom, '\u2518');          // ┘
            for (int x = column + 1; x < right; x++)
            {
                Set(x, row, '\u2500');             // ─
                Set(x, bottom, '\u2500');
            }
            for (int y = row + 1; y < bottom; y++)
            {
                Set(column, y, '\u2502');          // │
                Set(right, y, '\u2502');
            }
        }

        /// <summary>A box drawn in the heavy weight, for the one reading that matters most
        /// on the page.</summary>
        public void HeavyBox(int column, int row, int width, int height)
        {
            if (width < 2 || height < 2) return;
            int right = column + width - 1;
            int bottom = row + height - 1;

            Set(column, row, '\u250F');
            Set(right, row, '\u2513');
            Set(column, bottom, '\u2517');
            Set(right, bottom, '\u251B');
            for (int x = column + 1; x < right; x++)
            {
                Set(x, row, '\u2501');
                Set(x, bottom, '\u2501');
            }
            for (int y = row + 1; y < bottom; y++)
            {
                Set(column, y, '\u2503');
                Set(right, y, '\u2503');
            }
        }

        public void HLine(int column, int row, int width, char glyph = '\u2500')
        {
            for (int x = 0; x < width; x++) Set(column + x, row, glyph);
        }

        /// <summary>A percentage drawn as a bar. Ten steps is enough for a glance, and a
        /// step is a character, so a panel never draws a "half" it cannot show.</summary>
        public void Bar(int column, int row, int width, float fill01, char full = '\u2588', char empty = '\u2591')
        {
            int steps = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * width), 0, width);
            for (int x = 0; x < width; x++) Set(column + x, row, x < steps ? full : empty);
        }

        /// <summary>An "icon cell": a quiet frame the item picture is drawn inside, with its
        /// count underneath. The text side reserves the space; the quad side fills it.</summary>
        public void Icon(int column, int row, int width, int height, ItemDefinition item)
        {
            if (item == null || width <= 0 || height <= 0) return;
            _icons.Add(new ScreenIconRequest(column, row, width, height, item));
        }

        // ── Charts ──────────────────────────────────────────────────────────────

        /// <summary>
        /// A line chart over block characters. Each column is one sample window; the trace
        /// marks the row its value reaches and joins to the previous column, so a jump reads
        /// as a cliff instead of a dotted trail. This is what a power history has to look
        /// like to be worth a panel: the shape of the last three minutes, not a number.
        /// </summary>
        public void Plot(int column, int row, int width, int height,
            IReadOnlyList<float> values, float scale, char glyph)
        {
            if (width < 2 || height < 2 || values == null || values.Count == 0) return;
            if (scale <= 0.0001f) scale = 1f;

            int previousRow = -1;
            for (int x = 0; x < width; x++)
            {
                int index = Mathf.Clamp(
                    Mathf.FloorToInt(x * (values.Count - 1f) / Mathf.Max(1, width - 1)),
                    0, values.Count - 1);
                float v = Mathf.Clamp01(values[index] / scale);
                int y = row + height - 1 - Mathf.Clamp(Mathf.RoundToInt(v * (height - 1)), 0, height - 1);

                Set(column + x, y, glyph);

                if (previousRow >= 0 && previousRow != y)
                {
                    int from = Mathf.Min(previousRow, y);
                    int to = Mathf.Max(previousRow, y);
                    for (int between = from + 1; between < to; between++)
                        Set(column + x, between, '\u2502');
                }
                previousRow = y;
            }
        }

        /// <summary>
        /// A battery, drawn as the thing every player already recognises: a case with a
        /// rounded top and bottom and a fill that rises from the base. At three characters
        /// wide and four tall it is a picture, which is the entire point - a row of them
        /// shows a bank's health at a glance in a way twelve numbers never will.
        /// </summary>
        public void Battery(int column, int row, int width, float fill01)
        {
            if (width < 3) width = 3;
            const int height = 4;
            int right = column + width - 1;
            int bottom = row + height - 1;

            HLine(column, row, width, '\u2584');       // ▄▄▄ top of the case
            HLine(column, bottom, width, '\u2580');    // ▀▀▀ base

            for (int y = row + 1; y < bottom; y++)
            {
                Set(column, y, '\u2590');              // ▐
                Set(right, y, '\u258C');               // ▌
                for (int x = column + 1; x < right; x++) Set(x, y, ' ');
            }

            int innerRows = bottom - row - 1;
            int filled = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * innerRows), 0, innerRows);
            for (int i = 0; i < innerRows; i++)
            {
                int y = bottom - 1 - i;
                bool full = i < filled;
                for (int x = column + 1; x < right; x++) Set(x, y, full ? '\u2588' : '\u2591');
            }
        }

        /// <summary>
        /// A battery reduced to one row, for a header line: a cap on each side and the fill
        /// between them. Four cells wide it reads as a cell rather than as punctuation,
        /// which is what the count and the total beside it need.
        /// </summary>
        public void BatterySegment(int column, int row, int width, float fill01)
        {
            if (width < 3) width = 3;
            int right = column + width - 1;
            int steps = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * (width - 2)), 0, width - 2);

            Set(column, row, '\u2590');                // ▐
            Set(right, row, '\u258C');                 // ▌
            for (int x = column + 1; x < right; x++)
                Set(x, row, x - column <= steps ? '\u2588' : '\u2591');
        }

        // ── Panes ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Copy a page, already built, into a rectangle of this canvas. Text stops at the
        /// rectangle's right edge and at its last row, which is what makes a split panel
        /// safe: a page written for a whole glass cannot spill into its neighbour.
        /// </summary>
        public void Blit(string text, int column, int row, int width, int height)
        {
            if (string.IsNullOrEmpty(text) || width <= 0 || height <= 0) return;

            int line = 0;
            int i = 0;
            while (i < text.Length && line < height)
            {
                int end = text.IndexOf('\n', i);
                if (end < 0) end = text.Length;
                int length = Mathf.Min(end - i, width);
                for (int x = 0; x < length; x++) Set(column + x, row + line, text[i + x]);
                line++;
                i = end + 1;
            }
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

        /// <summary>The page as the block's TextMesh wants it: one string, trailing spaces
        /// trimmed so the block's own centring decides where the page sits.</summary>
        /// <summary>How many rows the last <see cref="Build"/> actually printed. The block
        /// needs this to map icon cells onto the text it fitted: a page that only used four
        /// of its twelve rows produced four rows of mesh, and the pictures have to follow
        /// the mesh, not the canvas.</summary>
        public int PrintedRows { get; private set; }

        public string Build()
        {
            var sb = new StringBuilder((Columns + 1) * Rows);
            PrintedRows = 0;
            for (int y = 0; y < Rows; y++)
            {
                int end = Columns - 1;
                while (end >= 0 && _cells[y * Columns + end] == ' ') end--;
                for (int x = 0; x <= end; x++) sb.Append(_cells[y * Columns + x]);
                if (end >= 0) PrintedRows = y + 1;
                if (y < Rows - 1) sb.Append('\n');
            }
            return sb.ToString().TrimEnd();
        }
    }
}
