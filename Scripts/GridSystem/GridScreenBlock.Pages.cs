// Assets/Scripts/VoxelEngine/GridSystem/GridScreenBlock.Pages.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║           THE SCREEN PAGES (17.35.0 / 17.36.0-dev)               ║
// ║                                                                  ║
// ║  A separate half of GridScreenBlock on purpose. The original     ║
// ║  file is about how a screen is BUILT (frame, glass, glow,        ║
// ║  TextMesh); this one is about what it SAYS.                      ║
// ║                                                                  ║
// ║  17.36.0 rebuilt every page against ScreenCanvas, because the    ║
// ║  first pass was written as strings and strings cannot draw:      ║
// ║  a battery bank came out as twelve numbers in a column, a power  ║
// ║  history came out as two solid bars, and a storage monitor came  ║
// ║  out as a word list. The reference for this round was pictures,  ║
// ║  and the honest way to get there on a TextMesh is to treat the   ║
// ║  panel as a character grid and draw on it.                       ║
// ║                                                                  ║
// ║  Pages that show items ask for their pictures through the        ║
// ║  canvas; GridScreenBlock.Icons.cs turns those into quads.        ║
// ║                                                                  ║
// ║  Everything here is read-only: a page never mutates the ship,    ║
// ║  which is what keeps it honest in multiplayer.                   ║
// ║                                                                  ║
// ║  Cost: snapshots walk Grid.AllBlocks, so none of them run per    ║
// ║  frame. Each page's text is cached and rebuilt on a timer or     ║
// ║  when the page changes; the farm scan is slower still because    ║
// ║  it touches the scene.                                           ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Farming;
using VoxelEngine.GridSystem.UI;
using VoxelEngine.Items;
using VoxelEngine.Pressure;
using VoxelEngine.Thermal;

namespace VoxelEngine.GridSystem
{
    public partial class GridScreenBlock
    {
        /// <summary>How often a page's text is rebuilt. A screen is read by a human walking
        /// past it, not by a physics solver - twice a second is generous and costs nothing.</summary>
        private const float ScreenPageRefreshSeconds = 0.5f;
        /// <summary>The farm scan touches the scene, so it runs far less often than the rest.</summary>
        private const float FarmScanRefreshSeconds = 3f;
        /// <summary>A farm page only lists plots this close to the panel.</summary>
        private const float FarmScanRadiusMetres = 24f;
        /// <summary>How many heaviest items a manifest or mining page names.</summary>
        private const int HeaviestItemCount = 6;
        /// <summary>Power history: 90 samples at this cadence is the last 3 minutes.</summary>
        private const float PowerSampleSeconds = 2f;
        private const int PowerHistoryLength = 90;

        // ── Cached page state ────────────────────────────────────────────────────
        private string _cachedPageText;
        private int _cachedPageSignature = int.MinValue;

        // Where a page is being drawn while a split panel composes its panes. Null during
        // a normal single-page build, which is every screen that is not split.
        private ScreenCanvas _splitTarget;
        private RectInt _splitRect;
        private float _pageBuiltAt = -999f;
        private float _farmScannedAt = -999f;
        // 17.36.0-dev - beds are found through IFarmBed, so soil, seed planters and
        // sapling beds all land in the same list. Before this the page asked only for
        // FarmPlot and reported "no plots" on a base full of planters.
        private readonly List<FarmBedEntry> _farmFound = new(16);

        private struct FarmBedEntry
        {
            public IFarmBed bed;
            public Vector3 position;
            public float distanceSq;
        }

        // Power history (per screen; a bank of screens each keeps its own trace, which is
        // cheap - 90 floats - and means a panel shows what IT has seen, like a real gauge).
        private readonly List<float> _powerGainHistory = new(PowerHistoryLength);
        private readonly List<float> _powerLossHistory = new(PowerHistoryLength);
        private float _powerSampledAt = -999f;

        private static readonly ItemMassEntry[] s_massScratch = new ItemMassEntry[256];

        private struct ItemMassEntry
        {
            public ItemDefinition item;
            public int count;
            public float massKg;
        }

        // ── Entry point used by GridScreenBlock.FormattedDisplay ────────────────

        /// <summary>
        /// The text for one of the pages this half owns, cached. Returns null when
        /// <paramref name="mode"/> is not one of them, so the caller can fall through
        /// to its own formatters without the two ever disagreeing.
        /// </summary>
        private string RenderManagedPage(ScreenDataMode mode)
        {
            int signature = PageSignature(mode);
            if (signature == 0) return null;

            // Time-stamped rather than a countdown: FormattedDisplay is a property, and a
            // screen that happens to be read twice in one frame must not age the cache twice.
            bool stale = signature != _cachedPageSignature
                      || _cachedPageText == null
                      || Time.unscaledTime - _pageBuiltAt >= ScreenPageRefreshSeconds;
            if (!stale) return _cachedPageText;

            _cachedPageSignature = signature;
            _pageBuiltAt = Time.unscaledTime;
            _cachedPageText = IsSplit ? BuildSplitPage() : BuildPage(mode);
            return _cachedPageText;
        }

        /// <summary>
        /// Zero when this half of the class has nothing to draw, otherwise a hash of
        /// everything that decides what the glass shows. The sidebar and the pane pages are
        /// part of it: changing which page sits in which pane has to rebuild the panel in the
        /// same frame, not on a timer.
        /// </summary>
        private int PageSignature(ScreenDataMode mode)
        {
            // The camera feed takes the glass whole, split or not.
            if (mode == ScreenDataMode.Camera) return 0;
            if (!IsSplit && !IsManagedPage(mode)) return 0;

            int signature = 17;
            signature = signature * 31 + (int)mode;
            signature = signature * 31 + SplitLayout;
            signature = signature * 31 + (int)PanePage(0);
            signature = signature * 31 + (int)PanePage(1);
            signature = signature * 31 + (int)PanePage(2);
            signature = signature * 31 + (int)PanePage(3);
            return signature == 0 ? 1 : signature;
        }

        // ── SPLIT PANELS ────────────────────────────────────────────────────────
        //
        // 17.36.0-dev — one glass, two or four pages.
        //
        // The screen still has one body mesh, so a split is composed rather than layered:
        // every pane draws into its own canvas, the canvases are blitted into one full-size
        // canvas at the pane's corner, and rules are drawn over the joins. That means a page
        // written for a whole panel needs no knowledge of panes at all - it simply gets a
        // smaller canvas - and it cannot spill into its neighbour.

        /// <summary>Panes in the current layout: two side by side, two stacked, or four.</summary>
        private int SplitLayout => Mathf.Clamp(splitMode, 0, 3);

        /// <summary>True when this screen shows more than one page.</summary>
        private bool IsSplit => SplitLayout > 0;

        private int SplitPaneCount => SplitLayout == 3 ? 4 : 2;

        /// <summary>The page in a pane. Used by the screen and by the editor panel.</summary>
        public ScreenDataMode PanePage(int index) => index switch
        {
            0 => splitPageA,
            1 => splitPageB,
            2 => splitPageC,
            _ => splitPageD,
        };

        /// <summary>Put a page in a pane. Only pages that draw themselves belong here, but
        /// an impossible choice is reported by the pane rather than silently replaced.</summary>
        public void SetPanePage(int index, ScreenDataMode mode)
        {
            switch (index)
            {
                case 0: splitPageA = mode; break;
                case 1: splitPageB = mode; break;
                case 2: splitPageC = mode; break;
                case 3: splitPageD = mode; break;
            }
        }

        /// <summary>True for a page that draws its own canvas, and can therefore be given a
        /// pane instead of the whole glass.</summary>
        public static bool DrawsItsOwnPage(ScreenDataMode mode) => IsManagedPage(mode);

        private string BuildSplitPage()
        {
            var (columns, rows) = PageGrid();
            var main = new ScreenCanvas(columns, rows);
            int panes = SplitPaneCount;

            for (int i = 0; i < panes; i++)
                DrawPane(main, SplitPaneRect(i, columns, rows), PanePage(i));

            DrawSplitRules(main, columns, rows);
            return Finish(main);
        }

        /// <summary>Draw one pane: its title on the first row, its page under it.</summary>
        private void DrawPane(ScreenCanvas main, RectInt rect, ScreenDataMode mode)
        {
            if (rect.width < 3 || rect.height < 1) return;

            // A pane names itself. There is one title mesh on the whole screen, so with four
            // panes the only place a page's name can live is inside its own corner.
            main.Text(rect.x, rect.y, Trim(ScreenPageCatalog.For(mode).Label.ToUpperInvariant(), rect.width));

            // A one-row pane is a label and nothing else; drawing into it would put the
            // page's first line on the neighbour's title row.
            if (rect.height <= 1) return;

            // A provider page has no canvas layout to shrink into, so the pane says so
            // instead of printing a column of source dumps clipped at the divider.
            if (!IsManagedPage(mode))
            {
                main.TextClipped(rect.x, rect.y + 1, "NOT AVAILABLE IN A PANE", rect.width);
                return;
            }

            var content = new RectInt(rect.x, rect.y + 1, rect.width, rect.height - 1);
            _splitTarget = main;
            _splitRect = content;
            try { BuildPage(mode); }
            finally { _splitTarget = null; }
        }

        private RectInt SplitPaneRect(int index, int columns, int rows)
        {
            int midColumn = columns / 2;
            int midRow = rows / 2;

            switch (SplitLayout)
            {
                case 1: // two side by side
                    return index == 0
                        ? new RectInt(0, 0, midColumn, rows)
                        : new RectInt(midColumn + 1, 0, columns - midColumn - 1, rows);

                case 2: // two stacked
                    return index == 0
                        ? new RectInt(0, 0, columns, midRow)
                        : new RectInt(0, midRow + 1, columns, rows - midRow - 1);

                default: // four quarters, clockwise from the top left
                {
                    bool right = (index & 1) == 1;
                    bool bottom = index >= 2;
                    return new RectInt(
                        right ? midColumn + 1 : 0,
                        bottom ? midRow + 1 : 0,
                        right ? columns - midColumn - 1 : midColumn,
                        bottom ? rows - midRow - 1 : midRow);
                }
            }
        }

        private void DrawSplitRules(ScreenCanvas main, int columns, int rows)
        {
            int midColumn = columns / 2;
            int midRow = rows / 2;

            if (SplitLayout == 1 || SplitLayout == 3)
                for (int y = 0; y < rows; y++) main.Set(midColumn, y, '\u2502');

            if (SplitLayout == 2 || SplitLayout == 3)
                for (int x = 0; x < columns; x++) main.Set(x, midRow, '\u2500');

            if (SplitLayout == 3) main.Set(midColumn, midRow, '\u253C');
        }

        /// <summary>True for the pages this half of the class renders.</summary>
        private static bool IsManagedPage(ScreenDataMode mode) => mode switch
        {
            ScreenDataMode.Environment or ScreenDataMode.BatteryBank or ScreenDataMode.PowerGraph
                or ScreenDataMode.Manifest or ScreenDataMode.FillBars or ScreenDataMode.Farm
                or ScreenDataMode.Mining or ScreenDataMode.InventoryGrid
                or ScreenDataMode.PlanetMap or ScreenDataMode.Inventory
                or ScreenDataMode.MiningCargo or ScreenDataMode.Speed => true,
            _ => false,
        };

        private string BuildPage(ScreenDataMode mode)
        {
            if (!Enabled) return "DISABLED";
            if (!IsPowered) return "OFFLINE";
            if (Grid == null) return "NO CONSTRUCT";

            return mode switch
            {
                ScreenDataMode.Environment => BuildEnvironmentPage(),
                ScreenDataMode.BatteryBank => BuildBatteryBankPage(),
                ScreenDataMode.PowerGraph => BuildPowerGraphPage(),
                ScreenDataMode.Manifest => BuildManifestPage(),
                ScreenDataMode.FillBars => BuildFillBarsPage(),
                ScreenDataMode.Farm => BuildFarmPage(),
                ScreenDataMode.Mining => BuildMiningPage(),
                ScreenDataMode.MiningCargo => BuildMiningCargoPage(),
                ScreenDataMode.InventoryGrid => BuildInventoryGridPage(),
                ScreenDataMode.Inventory => BuildStorageMonitorPage(),
                ScreenDataMode.PlanetMap => BuildPlanetMapPage(),
                ScreenDataMode.Speed => BuildSpeedPage(),
                _ => "NO PAGE",
            };
        }

        // ── Layout ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The character grid a page draws on: as many columns as the glass holds at the
        /// line height the fit pass will give it, and exactly as many rows as the panel can
        /// print. A page written against this grid is the same page on a Small panel and an
        /// ExtraLarge one - it just has fewer cells to work with.
        /// </summary>
        private (int columns, int rows) PageGrid()
        {
            var (width, height) = GlassSize();
            int rows = Mathf.Max(1, MaxDisplayLines);
            float lineHeight = (height * 0.62f) / rows;
            float charWidth = Mathf.Max(0.001f, lineHeight * 0.52f);
            int columns = Mathf.Clamp(Mathf.RoundToInt((width * 0.94f) / charWidth), 8, 72);
            return (columns, rows);
        }

        private ScreenCanvas NewCanvas()
        {
            // While a split panel is composing, a page gets the pane's own grid instead of
            // the whole glass. No page builder knows this happened.
            if (_splitTarget != null) return new ScreenCanvas(_splitRect.width, _splitRect.height);

            var grid = PageGrid();
            return new ScreenCanvas(grid.columns, grid.rows);
        }

        private string Finish(ScreenCanvas canvas)
        {
            if (_splitTarget != null)
            {
                _splitTarget.Blit(canvas.Build(), _splitRect.x, _splitRect.y, _splitRect.width, _splitRect.height);
                _splitTarget.AddIcons(canvas.Icons, _splitRect.x, _splitRect.y);
                return string.Empty;
            }

            string text = canvas.Build();
            // Icons are mapped onto the rows that were actually printed, which is what the
            // text mesh will be fitted to.
            int rows = Mathf.Max(canvas.PrintedRows, 1);
            SetPageIcons(canvas.Icons, canvas.Columns, rows);
            return text;
        }

        /// <summary>Pages that say nothing with pictures clear any left over from a previous
        /// page - a stale icon floating over a graph is worse than no icon at all.</summary>
        private void ClearPageIcons() => SetPageIcons(null, 1, 1);

        // ── SPEED ───────────────────────────────────────────────────────────────
        /// <summary>
        /// The flight page, cut down to what a pilot reads: how fast, how fast vertically,
        /// where the thrust is going, and what the frame is doing. The old page was a
        /// provider dump with section rules, which on a Small panel filled the glass with
        /// labels and left no room for the number.
        /// </summary>
        private string BuildSpeedPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();

            Vector3 velocity = Grid != null && Grid.Body != null
                ? Grid.Body.linearVelocity
                : Vector3.zero;
            float speed = velocity.magnitude;
            float vertical = velocity.y;

            string main = speed.ToString("0.0") + " m/s";
            canvas.Text(canvas.Columns - 1, 0, main, rightAlign: true);
            if (canvas.Columns >= main.Length + 8) canvas.Text(0, 0, "SPEED");

            if (canvas.Rows > 1)
                canvas.Text(0, 1, "VERT " + (vertical >= 0f ? "+" : "-") + Mathf.Abs(vertical).ToString("0.0") + " m/s");

            // Thrust split: forward, back, up, down as three bars. This is the number that
            // tells a pilot why a loaded ship is climbing slowly.
            if (canvas.Rows > 2 && Grid != null)
            {
                var (fwd, _, right, left, up, _) = Grid.GetThrustByDirection();
                float thrustMax = Mathf.Max(1f, Mathf.Max(fwd + back, Mathf.Max(up + down, right + left)));

                int row = 2;
                DrawThrustBar(canvas, row, "FWD", fwd, thrustMax);
                if (canvas.Rows > row + 1) DrawThrustBar(canvas, row + 1, "UP ", up, thrustMax);
                if (canvas.Rows > row + 2) DrawThrustBar(canvas, row + 2, "SIDE", right + left, thrustMax);
            }

            return Finish(canvas);
        }

        private static void DrawThrustBar(ScreenCanvas canvas, int row, string label, float value, float max)
        {
            canvas.Text(0, row, label);
            int width = Mathf.Max(3, canvas.Columns - 5);
            canvas.Bar(4, row, width, max > 0.001f ? value / max : 0f);
        }

        // ── ENVIRONMENT ─────────────────────────────────────────────────────────
        /// <summary>
        /// Four readings, two columns, no prose. The old page printed a bar under every
        /// number and a sentence beside it, which on a Small panel meant three numbers were
        /// all that fitted and the priority was wrong. Air first: pressure and whether it is
        /// breathable, then gravity, temperature and altitude.
        /// </summary>
        private string BuildEnvironmentPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();

            Vector3 here = transform.position;

            GridRoom room = null;
            var pressureSystem = GridPressureSystem.For(Grid);
            if (pressureSystem != null) room = pressureSystem.RoomAtWorld(here);

            float pressureAtm = room != null
                ? room.PressureAtm
                : PressureRules.SampleAmbient(here).PressureAtm;
            bool sealedRoom = room != null;
            bool breathable = pressureAtm >= PressureRules.BreathablePressureAtm;

            float gravity = GravityProvider.GetGravity(here).magnitude;
            float temperature = room != null ? room.AirTemperatureC : ExteriorTemperatureC();

            // One reading per row, label left and value against the right edge. Two columns
            // looked tidier on paper and overlapped the moment a value grew a digit, which
            // on a panel this size is most of the time.
            int row = 0;
            TextRow(canvas, ref row, "ATM", pressureAtm.ToString("0.00") + " atm");
            TextRow(canvas, ref row, "GRAV", gravity.ToString("0.00") + " m/s2");
            TextRow(canvas, ref row, "TEMP", temperature.ToString("0.0") + " C");
            TextRow(canvas, ref row, "ALT", AltitudeText());

            // The two words that say whether a player can open the door, on any row spare.
            if (row < canvas.Rows)
            {
                canvas.Text(0, row, sealedRoom ? "SEALED ROOM" : "AMBIENT AIR");
                canvas.Text(canvas.Columns - 1, row, breathable ? "BREATHABLE" : "VACUUM", rightAlign: true);
                row++;
            }

            if (row < canvas.Rows && canvas.Columns >= 12)
            {
                canvas.Text(0, row, "ATM ");
                canvas.Bar(4, row, canvas.Columns - 4,
                    Mathf.Clamp01(pressureAtm / Mathf.Max(0.01f, PressureRules.NominalPressureAtm)));
            }

            return Finish(canvas);
        }

        /// <summary>Label on the left, value against the right edge, next row down.</summary>
        private static void TextRow(ScreenCanvas canvas, ref int row, string label, string value)
        {
            if (row >= canvas.Rows) return;
            canvas.Text(0, row, label);
            canvas.Text(canvas.Columns - 1, row, value, rightAlign: true);
            row++;
        }

        private float ExteriorTemperatureC()
        {
            var weather = VoxelEngine.Weather.WeatherManager.Instance;
            if (weather != null && weather.IsWeatherActive) return weather.CurrentTemperature;
            return ThermalRules.FallbackAmbientC;
        }

        private string AltitudeText()
        {
            var body = GravityProvider.ActiveBody;
            if (body == null) return "SPACE";
            float altitude = Vector3.Distance(transform.position, body.transform.position) - body.SurfaceRadius;
            return FormatDistance(altitude);
        }

        // ── BATTERY BANK ────────────────────────────────────────────────────────
        /// <summary>
        /// The bank as batteries. Every battery on the construct is drawn as a case with a
        /// fill, because a row of pictures says "twenty batteries, all healthy" in the time
        /// it takes to walk past, and a row of twelve numbers does not. The header keeps the
        /// true count and bank total, so a hold of a hundred cells cannot lie about itself.
        /// </summary>
        private string BuildBatteryBankPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();

            int count = 0;
            float stored = 0f;
            float capacity = 0f;
            var fills = new List<float>(32);

            foreach (var block in Grid.AllBlocks)
            {
                if (block is not GridBattery battery) continue;
                count++;
                stored += Mathf.Max(0f, battery.storedWh);
                capacity += Mathf.Max(0f, battery.capacityWh);
                fills.Add(Mathf.Clamp01(battery.Fill01));
            }

            if (count == 0)
            {
                canvas.Text(0, 0, "BATTERY STATUS");
                if (canvas.Rows > 1) canvas.Text(0, 1, "NO BATTERIES ON THIS CONSTRUCT");
                return Finish(canvas);
            }

            float percent = capacity > 0f ? stored / capacity : 0f;

            // Header: a cell, the count, and the bank total - [cell] x N | 60 MWh - because
            // that is the line a player already knows how to read. On a panel too narrow for
            // the picture and the count together, the numbers alone still tell the truth.
            string countWord = "x" + count;
            string total = FormatWattHours(stored);
            bool roomForGlyph = canvas.Columns >= 6 + countWord.Length + 3 + total.Length;

            if (roomForGlyph)
            {
                canvas.BatterySegment(0, 0, 4, percent);
                canvas.Text(5, 0, countWord);
                canvas.Text(canvas.Columns - total.Length - 2, 0, "|");
                canvas.Text(canvas.Columns - 1, 0, total, rightAlign: true);
            }
            else
            {
                canvas.Text(canvas.Columns - 1, 0, countWord + "  " + total, rightAlign: true);
            }

            const int cellWidth = 5;
            const int batteryWidth = 4;
            int perRow = Mathf.Max(1, canvas.Columns / cellWidth);
            int rowsOfCells = Mathf.Max(0, Mathf.Min(3, (canvas.Rows - 1) / 4));
            int capacityCells = perRow * rowsOfCells;

            for (int i = 0; i < fills.Count && i < capacityCells; i++)
            {
                int row = i / perRow;
                int column = i % perRow;
                canvas.Battery(column * cellWidth, 1 + row * 4, batteryWidth, fills[i]);
            }

            if (rowsOfCells == 0)
            {
                // A panel too short for even one row of cells: the bank as one bar and a
                // count. Better a truthful summary than a page with a header and nothing.
                if (canvas.Rows > 1)
                {
                    canvas.Text(0, 1, "BANK");
                    canvas.Bar(5, 1, Mathf.Max(2, canvas.Columns - 12), percent);
                    canvas.Text(canvas.Columns - 1, 1, (percent * 100f).ToString("0") + "%", rightAlign: true);
                }
                return Finish(canvas);
            }

            if (fills.Count > capacityCells)
            {
                // Say how many were left off rather than silently showing a sample.
                int lastRow = 1 + (rowsOfCells - 1) * 4 + 3;
                canvas.Text(canvas.Columns - 1, lastRow,
                    "+" + (fills.Count - capacityCells), rightAlign: true);
            }

            return Finish(canvas);
        }

        // ── POWER GRAPH ─────────────────────────────────────────────────────────
        /// <summary>
        /// The last three minutes as a chart, not as two bars. Generation and draw share one
        /// plot inside a frame: the shape of the day lives in how the two traces separate,
        /// and a player reading a wall wants to see the deficit coming, not just the number
        /// it is at. Sampled every two seconds and kept per panel.
        /// </summary>
        private string BuildPowerGraphPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            SamplePowerHistory();

            float gainNow = Last(_powerGainHistory);
            float lossNow = Last(_powerLossHistory);
            float gainPeak = Peak(_powerGainHistory, gainNow);
            float lossPeak = Peak(_powerLossHistory, lossNow);
            float scale = Mathf.Max(1f, Mathf.Max(gainPeak, lossPeak));

            // One line of legend, values only - the plot carries the history.
            string head = "GEN " + FormatWatts(gainNow) + "   USE " + FormatWatts(lossNow);
            if (canvas.Columns >= head.Length + 12) canvas.Text(0, 0, "POWER");
            canvas.Text(canvas.Columns - 1, 0, head, rightAlign: true);

            int plotTop = 1;
            int plotHeight = canvas.Rows - 3;

            if (plotHeight < 3)
            {
                // Too short for a chart: the two numbers are the honest answer.
                if (canvas.Rows > 1) canvas.Text(0, 1, "NET " + SignedWatts(gainNow - lossNow));
                if (canvas.Rows > 2) canvas.Text(0, 2, PowerStateWord());
                return Finish(canvas);
            }

            int plotLeft = 1;
            int plotWidth = Mathf.Max(2, canvas.Columns - 2);
            canvas.Box(0, plotTop, canvas.Columns, plotHeight);

            canvas.Plot(plotLeft, plotTop + 1, plotWidth, plotHeight - 2, _powerGainHistory, scale, '\u2588');
            canvas.Plot(plotLeft, plotTop + 1, plotWidth, plotHeight - 2, _powerLossHistory, scale, '\u2592');

            // Axis labels outside the frame: what the scale is, and where "now" is.
            int axisRow = canvas.Rows - 1;
            canvas.Text(0, axisRow, "PEAK " + FormatWatts(scale));
            canvas.Text(canvas.Columns - 1, axisRow, "3m \u2500\u2500 now", rightAlign: true);

            return Finish(canvas);
        }

        private void SamplePowerHistory()
        {
            if (_powerGainHistory.Count > 0
                && Time.unscaledTime - _powerSampledAt < PowerSampleSeconds) return;
            _powerSampledAt = Time.unscaledTime;

            Push(_powerGainHistory, Mathf.Max(0f, Grid.PowerGenerated));
            Push(_powerLossHistory, Mathf.Max(0f, Grid.PowerConsumed));

            static void Push(List<float> list, float value)
            {
                list.Add(value);
                if (list.Count > PowerHistoryLength) list.RemoveAt(0);
            }
        }

        private static float Last(List<float> values)
            => values.Count > 0 ? values[values.Count - 1] : 0f;

        private static float Peak(List<float> values, float atLeast)
        {
            float peak = atLeast;
            for (int i = 0; i < values.Count; i++) if (values[i] > peak) peak = values[i];
            return peak;
        }

        private string PowerStateWord()
            => Grid.HasPower ? "STABLE" : "DEFICIT";

        // ── MANIFEST ────────────────────────────────────────────────────────────
        /// <summary>
        /// What is in the hold, heaviest first, with the item's own picture beside it and the
        /// mass right-aligned. This is the page a freighter captain reads: not a list of
        /// everything, but the order of "what is weighing me down".
        /// </summary>
        private string BuildManifestPage()
        {
            var canvas = NewCanvas();
            int entries = CollectGridCargo(_ => true, out int usedSlots, out int capacitySlots, out float totalKg);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;

            if (entries == 0)
            {
                ClearPageIcons();
                canvas.Text(0, 0, "MANIFEST");
                if (canvas.Rows > 1) canvas.Text(0, 1, "HOLD EMPTY");
                return Finish(canvas);
            }

            // Header: how full, and with how much.
            string total = FormatMass(totalKg);
            if (canvas.Columns >= 8 + total.Length + 2) canvas.Text(0, 0, "MANIFEST");
            canvas.Text(canvas.Columns - 1, 0, total, rightAlign: true);
            if (canvas.Rows > 1)
                canvas.Bar(0, 1, canvas.Columns, fill);

            int startRow = 2;
            int rowsLeft = Mathf.Max(0, canvas.Rows - startRow);
            int shown = Mathf.Min(Mathf.Min(HeaviestItemCount, entries), rowsLeft);

            for (int i = 0; i < shown; i++)
            {
                var entry = s_massScratch[i];
                if (entry.item == null) continue;

                int row = startRow + i;
                // Picture in the two cells on the left, name after it, mass on the edge.
                canvas.Icon(0, row, 2, 1, entry.item);
                canvas.Text(2, row, " ");
                canvas.TextClipped(3, row, entry.item.displayName,
                    Mathf.Max(4, canvas.Columns - 3 - 10));
                canvas.Text(canvas.Columns - 1, row, FormatMass(entry.massKg), rightAlign: true);
            }

            return Finish(canvas);
        }

        // ── MINING CARGO ────────────────────────────────────────────────────────
        /// <summary>
        /// The mining overlay's second half: what the hold is actually carrying, heaviest
        /// first. A pilot watching lift margin needs to know which ore is eating it, and that
        /// is a different question from "what is in the hold" - so it is a different page,
        /// meant to be paired with the gauge across a split panel.
        /// </summary>
        private string BuildMiningCargoPage()
        {
            var canvas = NewCanvas();
            int entries = CollectGridCargo(_ => true, out int usedSlots, out int capacitySlots, out float totalKg);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;

            string percentWord = (fill * 100f).ToString("0") + "%";
            if (canvas.Columns >= 5 + percentWord.Length + 2) canvas.Text(0, 0, "CARGO");
            canvas.Text(canvas.Columns - 1, 0, percentWord, rightAlign: true);
            if (canvas.Rows > 1) canvas.Bar(0, 1, canvas.Columns, fill);

            if (entries == 0)
            {
                ClearPageIcons();
                if (canvas.Rows > 2) canvas.Text(0, 2, "HOLD EMPTY");
                return Finish(canvas);
            }

            int startRow = 2;
            int rowsLeft = Mathf.Max(0, canvas.Rows - startRow - 1);
            int shown = Mathf.Min(Mathf.Min(HeaviestItemCount, entries), rowsLeft);

            for (int i = 0; i < shown; i++)
            {
                var entry = s_massScratch[i];
                if (entry.item == null) continue;

                int row = startRow + i;
                canvas.Icon(0, row, 2, 1, entry.item);
                canvas.TextClipped(3, row, entry.item.displayName, Mathf.Max(4, canvas.Columns - 13));
                canvas.Text(canvas.Columns - 1, row, FormatMass(entry.massKg), rightAlign: true);
            }

            canvas.Text(0, canvas.Rows - 1, FormatMass(totalKg) + " on board");
            return Finish(canvas);
        }

        // ── FILL BARS ───────────────────────────────────────────────────────────
        /// <summary>
        /// One bar per LINKED container or gas tank, in link order - the long panel's page.
        /// It deliberately reads the sources the player picked rather than the whole
        /// construct: the point of a fill strip is "these tanks", not "everything".
        /// </summary>
        private string BuildFillBarsPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();

            var providers = ResolveAllProviders();
            int written = 0;

            for (int i = 0; i < providers.Count && written < canvas.Rows; i++)
            {
                float fill;
                string name;
                if (providers[i] is GridCargoContainer cargo) { fill = cargo.Fill01; name = cargo.SourceName; }
                else if (providers[i] is GridGasTank tank) { fill = tank.Fill01; name = tank.SourceName; }
                else continue;

                fill = Mathf.Clamp01(fill);
                int row = written;
                int nameWidth = Mathf.Clamp(canvas.Columns / 3, 5, 18);
                int barWidth = Mathf.Max(3, canvas.Columns - nameWidth - 5);

                canvas.TextClipped(0, row, name, nameWidth);
                canvas.Bar(nameWidth, row, barWidth, fill);
                canvas.Text(canvas.Columns - 1, row, (fill * 100f).ToString("0") + "%", rightAlign: true);
                written++;
            }

            if (written == 0)
            {
                canvas.Text(0, 0, "FILL BARS");
                if (canvas.Rows > 1) canvas.Text(0, 1, "NO CONTAINER LINKED");
            }

            return Finish(canvas);
        }

        // ── FARM ────────────────────────────────────────────────────────────────
        /// <summary>
        /// Every farm plot near the screen, with its crop, growth and water state. A plot
        /// that is ready says so in words, because that is the only thing the player is
        /// looking for when they walk up to it.
        /// </summary>
        private string BuildFarmPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            RefreshFarmScan();

            if (_farmFound.Count == 0)
            {
                canvas.Text(0, 0, "FARM");
                if (canvas.Rows > 1)
                    canvas.Text(0, 1, "NO BEDS WITHIN " + FarmScanRadiusMetres.ToString("0") + " m");
                if (canvas.Rows > 2)
                    canvas.Text(0, 2, "SOIL, PLANTERS AND TREE BEDS");
                return Finish(canvas);
            }

            int ready = 0, thirsty = 0, empty = 0;
            for (int i = 0; i < _farmFound.Count; i++)
            {
                var bed = _farmFound[i].bed;
                if (bed == null) continue;
                if (bed.IsEmpty) empty++;
                else if (bed.IsReady) ready++;
                else if (bed.NeedsWater) thirsty++;
            }

            // Two numbers, in the order a player acts on them: what can be picked now,
            // and what needs carrying water to.
            string tail = ready + " READY";
            if (canvas.Columns > tail.Length + 10) tail = thirsty + " DRY   " + tail;
            canvas.Text(canvas.Columns - 1, 0, tail, rightAlign: true);

            string head = "FARM x" + _farmFound.Count;
            if (canvas.Columns >= head.Length + tail.Length + 2) canvas.Text(0, 0, head);

            int rows = Mathf.Max(0, canvas.Rows - 1);
            // The overflow count needs a row of its own, so one bed fewer is listed when
            // there are more beds than rows.
            bool more = _farmFound.Count > rows;
            int shown = Mathf.Min(_farmFound.Count, more ? Mathf.Max(0, rows - 1) : rows);
            for (int i = 0; i < shown; i++)
            {
                var entry = _farmFound[i];
                var bed = entry.bed;
                if (bed == null) continue;

                int row = 1 + i;
                string state = bed.IsEmpty ? "EMPTY" : bed.IsReady ? "READY"
                    : bed.NeedsWater ? "DRY"
                    : (bed.Growth01 * 100f).ToString("0") + "%";

                // Names are clipped against the state, never the other way round: the
                // state is four characters and the crop name is whatever the pack says.
                int nameWidth = Mathf.Max(3, canvas.Columns - state.Length - 1);
                string name = bed.IsEmpty ? bed.BedName : bed.CropName;
                if (string.IsNullOrEmpty(name)) name = bed.BedName;

                canvas.TextClipped(0, row, name, nameWidth);
                canvas.Text(canvas.Columns - 1, row, state, rightAlign: true);
            }

            // Anything that did not fit is counted, not silently dropped.
            if (more && canvas.Rows > 1)
                canvas.Text(canvas.Columns - 1, canvas.Rows - 1,
                    "+" + (_farmFound.Count - shown), rightAlign: true);

            return Finish(canvas);
        }

        private void RefreshFarmScan()
        {
            if (Time.unscaledTime - _farmScannedAt < FarmScanRefreshSeconds) return;
            _farmScannedAt = Time.unscaledTime;

            _farmFound.Clear();
            Vector3 here = transform.position;
            float radiusSq = FarmScanRadiusMetres * FarmScanRadiusMetres;

            // Three searches rather than one: a bed type that does not exist in this save
            // costs one empty array, and a bed type that has been added since never has to
            // be remembered here again.
            var soil = UnityEngine.Object.FindObjectsByType<FarmPlot>(FindObjectsSortMode.None);
            for (int i = 0; i < soil.Length; i++) AddFarmBed(soil[i], here, radiusSq);

            var planters = UnityEngine.Object.FindObjectsByType<CropPlanter>(FindObjectsSortMode.None);
            for (int i = 0; i < planters.Length; i++) AddFarmBed(planters[i], here, radiusSq);

            var trees = UnityEngine.Object.FindObjectsByType<TreeFarmPlot>(FindObjectsSortMode.None);
            for (int i = 0; i < trees.Length; i++) AddFarmBed(trees[i], here, radiusSq);

            _farmFound.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));
        }

        private void AddFarmBed(Object candidate, Vector3 here, float radiusSq)
        {
            if (candidate is not IFarmBed bed) return;
            var behaviour = candidate as MonoBehaviour;
            if (behaviour == null || !behaviour.isActiveAndEnabled) return;

            Vector3 position = behaviour.transform.position;
            float distanceSq = (position - here).sqrMagnitude;
            if (distanceSq > radiusSq) return;

            _farmFound.Add(new FarmBedEntry { bed = bed, position = position, distanceSq = distanceSq });
        }

        // ── MINING ──────────────────────────────────────────────────────────────
        /// <summary>
        /// The overlay a pilot wants while digging: lift against weight, and hold fill,
        /// stacked as two big readings in one frame. Nothing else - when the shaft is deep
        /// nobody is reading a paragraph.
        /// </summary>
        private string BuildMiningPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();

            Vector3 here = transform.position;
            float g = Mathf.Max(0.01f, GravityProvider.GetGravity(here).magnitude);
            float massKg = Mathf.Max(0f, Grid.TotalMass);
            float weightN = massKg * g;
            float liftN = Grid.GetThrustByDirection().up;
            float lift01 = weightN > 0.001f ? liftN / weightN : (liftN > 0f ? 1f : 0f);

            int entries = CollectGridCargo(_ => true,
                out int usedSlots, out int capacitySlots, out _);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;
            _ = entries;

            bool tight = canvas.Rows < 6 || canvas.Columns < 14;

            if (tight)
            {
                canvas.Text(0, 0, "LIFT " + (lift01 * 100f).ToString("0.00") + "%");
                if (canvas.Rows > 1) canvas.Text(0, 1, "CARGO " + (fill * 100f).ToString("0.00") + "%");
                if (canvas.Rows > 2)
                    canvas.Bar(0, 2, canvas.Columns, lift01);
                return Finish(canvas);
            }

            // The frame is the instrument: two halves, each with a caption above and a big
            // number in the middle, exactly the reading order of the reference gauge.
            canvas.Box(0, 0, canvas.Columns, canvas.Rows);

            int half = canvas.Rows / 2;
            if (canvas.Rows % 2 == 1)
                canvas.HLine(1, half, canvas.Columns - 2, '\u2500');

            DrawGauge(canvas, 1, 1, canvas.Columns - 2, half - 1, "LIFT THRUST", lift01);
            DrawGauge(canvas, 1, half + (canvas.Rows % 2 == 1 ? 1 : 0), canvas.Columns - 2,
                canvas.Rows - half - 1, "CARGO", fill);

            return Finish(canvas);
        }

        /// <summary>Caption, a big percentage, and a bar to the edges of its half.</summary>
        private static void DrawGauge(ScreenCanvas canvas, int left, int top, int width, int height,
            string caption, float value01)
        {
            if (width < 6 || height < 2) return;

            canvas.Text(left + width / 2, top, caption, rightAlign: true);

            string number = (Mathf.Clamp01(value01) * 100f).ToString("0.00") + "%";
            int numberRow = height >= 3 ? top + 1 + (height - 3) / 2 : top + 1;
            canvas.Text(left + width / 2, numberRow, number, rightAlign: true);

            if (height >= 3)
                canvas.Bar(left, top + height - 1, width, value01);
        }

        // ── INVENTORY GRID ──────────────────────────────────────────────────────
        /// <summary>
        /// The hold as a dense grid of pictures and counts: a picture, its count, the next
        /// picture. No names, no header - this is the page for a busy wall, where the
        /// question is "what have I got" and the answer is a shape and a number.
        /// </summary>
        private string BuildInventoryGridPage()
        {
            var canvas = NewCanvas();
            int entries = CollectGridCargo(_ => true,
                out int usedSlots, out int capacitySlots, out float totalKg);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;

            if (entries == 0)
            {
                ClearPageIcons();
                canvas.Text(0, 0, "INVENTORY GRID");
                if (canvas.Rows > 1) canvas.Text(0, 1, "EMPTY");
                return Finish(canvas);
            }

            // Header is one compact line: how full, how heavy.
            string fillWord = "HOLD " + (fill * 100f).ToString("0") + "%";
            string massWord = FormatMass(totalKg);
            if (canvas.Columns >= fillWord.Length + massWord.Length + 2)
                canvas.Text(0, 0, fillWord);
            canvas.Text(canvas.Columns - 1, 0, massWord, rightAlign: true);

            // Each cell: picture on the left of two rows, count on the second row.
            const int cellWidth = 8;
            const int cellHeight = 2;
            int columns = Mathf.Max(1, canvas.Columns / cellWidth);
            int rows = Mathf.Max(1, (canvas.Rows - 1) / cellHeight);
            int capacity = columns * rows;

            for (int i = 0; i < entries && i < capacity; i++)
            {
                var entry = s_massScratch[i];
                if (entry.item == null) continue;

                int column = (i % columns) * cellWidth;
                int row = 1 + (i / columns) * cellHeight;

                canvas.Icon(column, row, 2, cellHeight, entry.item);
                canvas.Text(column + 2, row + 1, FormatCount(entry.count));
            }

            return Finish(canvas);
        }

        // ── STORAGE MONITOR ─────────────────────────────────────────────────────
        /// <summary>
        /// The page the reference calls a storage monitor: a grid of item pictures, each with
        /// its count underneath, the container's own name in the corner, and nothing else.
        /// It exists because the single most common question at a chest is "what is in here",
        /// and the answer should be a picture, not a word.
        /// </summary>
        private string BuildStorageMonitorPage()
        {
            var canvas = NewCanvas();
            int entries = CollectGridCargo(_ => true,
                out int usedSlots, out int capacitySlots, out float totalKg);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;

            // Name of the monitor: the first linked container if one is linked, otherwise
            // the construct. It sits bottom-right, where a real monitor puts its label.
            string owner = GridEntityName();
            var providers = ResolveAllProviders();
            for (int i = 0; i < providers.Count; i++)
            {
                if (providers[i] is GridCargoContainer cargo && !string.IsNullOrEmpty(cargo.SourceName))
                {
                    owner = cargo.SourceName;
                    break;
                }
            }

            if (entries == 0)
            {
                ClearPageIcons();
                canvas.Text(0, 0, "STORAGE MONITOR");
                if (canvas.Rows > 1) canvas.Text(0, 1, "EMPTY");
                canvas.Text(canvas.Columns - 1, canvas.Rows - 1, Trim(owner, canvas.Columns), rightAlign: true);
                return Finish(canvas);
            }

            // Cells: three rows tall (picture, picture, count), five wide.
            const int cellWidth = 5;
            const int cellHeight = 3;
            int columns = Mathf.Max(1, canvas.Columns / cellWidth);
            int rows = Mathf.Max(1, (canvas.Rows - 1) / cellHeight);
            int capacity = columns * rows;

            for (int i = 0; i < entries && i < capacity; i++)
            {
                var entry = s_massScratch[i];
                if (entry.item == null) continue;

                int column = (i % columns) * cellWidth;
                int row = (i / columns) * cellHeight;

                canvas.Icon(column, row, cellWidth - 1, 2, entry.item);
                canvas.Text(column + (cellWidth - 2) / 2, row + 2, FormatCount(entry.count));
            }

            // Footer: how full on the left, whose monitor this is on the right.
            int footer = canvas.Rows - 1;
            string left = (fill * 100f).ToString("0") + "%";
            string label = Trim(owner, Mathf.Max(4, canvas.Columns - left.Length - 2));
            canvas.Text(0, footer, left);
            canvas.Text(canvas.Columns - 1, footer, label, rightAlign: true);

            return Finish(canvas);
        }

        private string GridEntityName()
        {
            if (Grid == null) return "CONSTRUCT";
            // GridEntity is a MonoBehaviour, so its own name is the construct's name - the
            // same string the inspector and the fleet list show.
            string name = Grid.name;
            return string.IsNullOrEmpty(name) ? "CONSTRUCT" : name;
        }

        // ── PLANET MAP ──────────────────────────────────────────────────────────
        /// <summary>
        /// The body below as a framed chart: a dot lattice, the disc drawn from real
        /// geometry, the body's name on the disc, the marker where the construct actually is,
        /// and the numbers along the bottom. A map page that does not show position is a
        /// decoration; this one moves when the ship moves.
        /// </summary>
        private string BuildPlanetMapPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();

            var body = GravityProvider.ActiveBody;
            if (body == null)
            {
                canvas.Text(0, 0, "PLANET MAP");
                if (canvas.Rows > 1) canvas.Text(0, 1, "NO BODY IN RANGE");
                return Finish(canvas);
            }

            Vector3 here = transform.position;
            Vector3 centre = body.transform.position;
            Vector3 arm = here - centre;
            float radius = arm.magnitude;
            float altitude = radius - body.SurfaceRadius;

            Vector3 pole = body.transform.up;
            float up = Vector3.Dot(arm.normalized, pole);
            float latitude = Mathf.Asin(Mathf.Clamp(up, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 flat = Vector3.ProjectOnPlane(arm, pole);
            float longitude = flat.sqrMagnitude > 1f
                ? Mathf.Atan2(Vector3.Dot(flat.normalized, body.transform.forward),
                              Vector3.Dot(flat.normalized, body.transform.right)) * Mathf.Rad2Deg
                : 0f;
            longitude = NormaliseLongitude(longitude);

            bool roomy = canvas.Columns >= 20 && canvas.Rows >= 6;

            if (!roomy)
            {
                canvas.Text(0, 0, Trim(body.DisplayName.ToUpperInvariant(), canvas.Columns));
                if (canvas.Rows > 1)
                    canvas.Text(0, 1, "LAT " + latitude.ToString("0") + "\u00b0  LON " + longitude.ToString("0") + "\u00b0");
                if (canvas.Rows > 2) canvas.Text(0, 2, "ALT " + FormatDistance(altitude));
                return Finish(canvas);
            }

            canvas.Box(0, 0, canvas.Columns, canvas.Rows);

            // Header inside the frame: body on the left, altitude on the right.
            canvas.TextClipped(1, 0, body.DisplayName.ToUpperInvariant(), canvas.Columns - 14);
            canvas.Text(canvas.Columns - 2, 0, FormatDistance(altitude), rightAlign: true);

            // The disc occupies the frame between the header and footer rows.
            int discTop = 1;
            int discHeight = canvas.Rows - 2;
            int discWidth = canvas.Columns - 2;
            int rows = Mathf.Max(2, discHeight);
            int columns = Mathf.Max(4, Mathf.Min(discWidth, rows * 2));
            int offsetX = 1 + (discWidth - columns) / 2;

            float markU = Mathf.Clamp(longitude / 180f, -1f, 1f);
            float markV = Mathf.Clamp(latitude / 90f, -1f, 1f);
            int markColumn = Mathf.RoundToInt((markU + 1f) * 0.5f * (columns - 1));
            int markRow = Mathf.RoundToInt((1f - markV) * 0.5f * (rows - 1));

            for (int row = 0; row < rows; row++)
            {
                float v = 1f - (row + 0.5f) * 2f / rows;
                for (int column = 0; column < columns; column++)
                {
                    float u = (column + 0.5f) * 2f / columns - 1f;
                    int x = offsetX + column;
                    int y = discTop + row;

                    if (u * u + v * v > 1f)
                    {
                        // Outside the disc: a faint lattice, the chart's own paper.
                        canvas.Set(x, y, (column % 3 == 0 && row % 2 == 0) ? '\u00b7' : ' ');
                        continue;
                    }

                    if (row == markRow && column == markColumn) { canvas.Set(x, y, 'X'); continue; }

                    bool meridian = Mathf.Abs(u - Mathf.Round(u * 3f) / 3f) < 0.05f;
                    bool parallel = Mathf.Abs(v - Mathf.Round(v * 2f) / 2f) < 0.06f;
                    canvas.Set(x, y, meridian || parallel ? '\u00b7' : '\u2591');
                }
            }

            // The body's name sits on the disc, low and centred, like a chart label.
            string name = Trim(body.DisplayName.ToUpperInvariant(), Mathf.Max(4, columns - 4));
            if (rows >= 4)
                canvas.Text(offsetX + columns / 2, discTop + rows / 2 + 1, name, rightAlign: true);

            // Footer: where we are, in the two numbers that actually describe it.
            string footer = "LAT " + latitude.ToString("0") + "\u00b0  LON " + longitude.ToString("0") + "\u00b0";
            canvas.Text(1, canvas.Rows - 1, footer);
            canvas.Text(canvas.Columns - 2, canvas.Rows - 1, "R " + FormatDistance(radius), rightAlign: true);

            return Finish(canvas);
        }

        private static float NormaliseLongitude(float degrees)
        {
            while (degrees > 180f) degrees -= 360f;
            while (degrees < -180f) degrees += 360f;
            return degrees;
        }

        // ── Shared snapshot helpers ─────────────────────────────────────────────

        /// <summary>
        /// Aggregates every item held by every container on the construct into
        /// <see cref="s_massScratch"/>, sorted heaviest first, and returns how many distinct
        /// types there are (capped at the scratch length).
        /// </summary>
        private int CollectGridCargo(System.Func<ItemDefinition, bool> filter,
            out int usedSlots, out int capacitySlots, out float totalKg)
        {
            int found = 0;
            usedSlots = 0;
            capacitySlots = 0;
            totalKg = 0f;

            foreach (var block in Grid.AllBlocks)
            {
                if (block is not GridCargoContainer cargo || cargo.container == null) continue;
                capacitySlots += cargo.container.Size;
                for (int i = 0; i < cargo.container.Size; i++)
                {
                    var stack = cargo.container.GetSlot(i);
                    if (stack == null || stack.IsEmpty) continue;
                    if (filter != null && !filter(stack.item)) continue;

                    usedSlots++;
                    float stackKg = MassUtil.StackMass(stack.item, stack.count);
                    totalKg += stackKg;

                    int existing = IndexOfItem(stack.item, found);
                    if (existing >= 0)
                    {
                        var e = s_massScratch[existing];
                        e.count += stack.count;
                        e.massKg += stackKg;
                        s_massScratch[existing] = e;
                    }
                    else if (found < s_massScratch.Length)
                    {
                        s_massScratch[found++] = new ItemMassEntry
                        {
                            item = stack.item, count = stack.count, massKg = stackKg,
                        };
                    }
                }
            }

            SortEntriesByMass(found);
            return found;
        }

        private static int IndexOfItem(ItemDefinition item, int count)
        {
            for (int i = 0; i < count; i++)
                if (ReferenceEquals(s_massScratch[i].item, item)) return i;
            return -1;
        }

        private static void SortEntriesByMass(int count)
        {
            // Insertion sort: the manifest is small and this allocates nothing, which is
            // what matters for a page that refreshes twice a second on every screen.
            for (int i = 1; i < count; i++)
            {
                var key = s_massScratch[i];
                int j = i - 1;
                while (j >= 0 && s_massScratch[j].massKg < key.massKg)
                {
                    s_massScratch[j + 1] = s_massScratch[j];
                    j--;
                }
                s_massScratch[j + 1] = key;
            }
        }

        // ── Shared formatting ───────────────────────────────────────────────────

        private static string FormatCount(int count)
        {
            if (count >= 1_000_000) return (count / 1_000_000f).ToString("0.##") + "M";
            if (count >= 1000) return (count / 1000f).ToString("0.##") + "k";
            return count.ToString();
        }

        private static string FormatMass(float kg)
        {
            if (kg >= 1_000_000f) return (kg / 1_000_000f).ToString("0.##") + " kt";
            if (kg >= 1000f) return (kg / 1000f).ToString("0.##") + " t";
            if (kg >= 1f) return kg.ToString("0.#") + " kg";
            return (kg * 1000f).ToString("0") + " g";
        }

        private static string FormatWattHours(float wh)
        {
            if (wh >= 1_000_000f) return (wh / 1_000_000f).ToString("0.##") + " MWh";
            if (wh >= 1000f) return (wh / 1000f).ToString("0.#") + " kWh";
            return wh.ToString("0") + " Wh";
        }

        private static string FormatDistance(float metres)
        {
            float abs = Mathf.Abs(metres);
            if (abs >= 1_000_000f) return (metres / 1_000_000f).ToString("0.##") + " Mm";
            if (abs >= 1000f) return (metres / 1000f).ToString("0.##") + " km";
            return metres.ToString("0") + " m";
        }

        private static string SignedWatts(float watts)
            => (watts >= 0f ? "+" : "-") + FormatWatts(Mathf.Abs(watts));

        private static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (max <= 1) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max - 1) + "\u2026";
        }
    }
}
