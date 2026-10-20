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
            public bool vertical;
        }

        // Power history (per screen; a bank of screens each keeps its own trace, which is
        // cheap - 90 floats - and means a panel shows what IT has seen, like a real gauge).
        private readonly List<float> _powerGainHistory = new(PowerHistoryLength);
        private readonly List<float> _powerLossHistory = new(PowerHistoryLength);
        private readonly List<float> _solarOutputHistory = new(PowerHistoryLength);
        private readonly List<float> _solarCapacityHistory = new(PowerHistoryLength);
        private readonly List<float> _batteryChargeHistory = new(PowerHistoryLength);
        private readonly List<float> _batteryDischargeHistory = new(PowerHistoryLength);
        private readonly List<GridBattery> _batteryFound = new(32);
        private readonly List<string> _manifestCategories = new(8);
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
            signature = signature * 31 + (manifestTitle ?? string.Empty).GetHashCode();
            signature = signature * 31 + Mathf.Clamp(powerGraphPanels, 1, 3);
            signature = signature * 31 + (showBatteryPowerRates ? 1 : 0);
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

            return Finish(main);
        }

        /// <summary>Draw one pane inside its own border, with a short page label and a
        /// clipped content area below it.</summary>
        private void DrawPane(ScreenCanvas main, RectInt rect, ScreenDataMode mode)
        {
            if (rect.width < 4 || rect.height < 3) return;

            main.Box(rect.x, rect.y, rect.width, rect.height);
            int innerWidth = rect.width - 2;
            string label = Trim(ScreenPageCatalog.For(mode).Label.ToUpperInvariant(), innerWidth);
            main.Text(rect.x + 1, rect.y + 1, label);

            if (!IsManagedPage(mode))
            {
                if (rect.height > 3)
                    main.TextClipped(rect.x + 1, rect.y + 2, "NOT AVAILABLE IN A PANE", innerWidth);
                return;
            }

            var content = new RectInt(rect.x + 1, rect.y + 2, innerWidth, rect.height - 3);
            if (content.height <= 0) return;
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
            ScreenDataMode.Summary or ScreenDataMode.System or ScreenDataMode.Bars
                or ScreenDataMode.Power or ScreenDataMode.Environment or ScreenDataMode.BatteryBank
                or ScreenDataMode.PowerGraph or ScreenDataMode.Manifest or ScreenDataMode.FillBars
                or ScreenDataMode.Farm or ScreenDataMode.Mining or ScreenDataMode.InventoryGrid
                or ScreenDataMode.PlanetMap or ScreenDataMode.Inventory or ScreenDataMode.MiningCargo
                or ScreenDataMode.Speed or ScreenDataMode.AutoRunPilotInfo
                or ScreenDataMode.RouteRecorderInfo or ScreenDataMode.FarmPlots => true,
            _ => false,
        };

        private string BuildPage(ScreenDataMode mode)
        {
            if (!Enabled) return "DISABLED";
            if (!IsPowered) return "OFFLINE";
            if (Grid == null) return "NO CONSTRUCT";

            return mode switch
            {
                ScreenDataMode.Summary => BuildMixedPage(),
                ScreenDataMode.System => BuildSystemsPage(),
                ScreenDataMode.Bars => BuildBarsPage(),
                ScreenDataMode.Power => BuildPowerPage(),
                ScreenDataMode.Environment => BuildEnvironmentPage(),
                ScreenDataMode.BatteryBank => BuildBatteryBankPage(),
                ScreenDataMode.PowerGraph => BuildPowerGraphPage(),
                ScreenDataMode.Manifest => BuildManifestPage(),
                ScreenDataMode.FillBars => BuildFillBarsPage(),
                ScreenDataMode.Farm => BuildFarmPage(),
                ScreenDataMode.FarmPlots => BuildFarmPlotsPage(),
                ScreenDataMode.Mining => BuildMiningPage(),
                ScreenDataMode.MiningCargo => BuildMiningCargoPage(),
                ScreenDataMode.InventoryGrid => BuildInventoryGridPage(),
                ScreenDataMode.Inventory => BuildStorageMonitorPage(),
                ScreenDataMode.PlanetMap => BuildPlanetMapPage(),
                ScreenDataMode.Speed => BuildSpeedPage(),
                ScreenDataMode.AutoRunPilotInfo => BuildAutoRunPilotPage(),
                ScreenDataMode.RouteRecorderInfo => BuildRouteRecorderPage(),
                _ => "NO PAGE",
            };
        }

        // ── Mixed / Systems / Bars ─────────────────────────────────────────────

        private string BuildMixedPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            var providers = ResolveAllProviders();
            if (providers.Count == 0)
            {
                canvas.Text(0, 0, "NO SOURCES LINKED");
                if (canvas.Rows > 1) canvas.Text(0, 1, "OPEN THE SCREEN EDITOR");
                return Finish(canvas);
            }

            bool wide = canvas.Columns >= 24;
            int providerSlots = wide ? canvas.Rows : canvas.Rows / 2;
            int shown = Mathf.Min(providers.Count, providerSlots);
            if (providers.Count > shown)
                shown = wide ? Mathf.Max(0, canvas.Rows - 1) : providerSlots;

            int row = 0;
            int nameWidth = Mathf.Clamp(canvas.Columns / 3, 5, 16);
            for (int i = 0; i < shown; i++)
            {
                var provider = providers[i];
                string value = FirstUsefulLine(provider.GetDisplayData());
                string name = (provider.SourceName ?? "SOURCE").ToUpperInvariant();

                if (wide)
                {
                    canvas.TextClipped(0, row, name, nameWidth);
                    canvas.TextClipped(nameWidth + 1, row, value,
                        Mathf.Max(1, canvas.Columns - nameWidth - 1));
                    row++;
                }
                else
                {
                    canvas.TextClipped(0, row++, name, canvas.Columns);
                    canvas.TextClipped(0, row++, value, canvas.Columns);
                }
            }

            if (shown < providers.Count && row < canvas.Rows)
                canvas.TextClipped(0, row, "+" + (providers.Count - shown) + " MORE SOURCES", canvas.Columns);
            return Finish(canvas);
        }

        private string BuildSystemsPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            var providers = ResolveAllProviders();
            if (providers.Count == 0)
            {
                canvas.Text(0, 0, "NO SYSTEMS LINKED");
                if (canvas.Rows > 1) canvas.Text(0, 1, "LINK A GRID DEVICE");
                return Finish(canvas);
            }

            int row = 0;
            for (int i = 0; i < providers.Count && row < canvas.Rows; i++)
            {
                var provider = providers[i];
                string[] lines = (provider.GetDisplayData() ?? string.Empty).Split('\n');
                string status = lines.Length > 1 ? lines[1].Trim() : FirstUsefulLine(provider.GetDisplayData());
                string label = (provider.DataCategory ?? "SYSTEM").ToUpperInvariant()
                    + "  " + (provider.SourceName ?? "DEVICE").ToUpperInvariant();
                canvas.TextClipped(0, row, label, canvas.Columns);
                row++;
                if (row >= canvas.Rows) break;
                int valueWidth = Mathf.Min(18, Mathf.Max(4, canvas.Columns - 2));
                canvas.TextClipped(1, row, status, valueWidth);
                row++;
                if (row < canvas.Rows && i + 1 < providers.Count) canvas.HLine(0, row++, canvas.Columns, '\u2500');
            }
            return Finish(canvas);
        }

        private string BuildBarsPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            var providers = ResolveAllProviders();
            if (providers.Count == 0)
            {
                canvas.Text(0, 0, "NO SOURCES LINKED");
                if (canvas.Rows > 1) canvas.Text(0, 1, "LINK A DEVICE TO SHOW BARS");
                return Finish(canvas);
            }

            int row = 0;
            int barWidth = Mathf.Clamp(canvas.Columns / 3, 5, 14);
            for (int i = 0; i < providers.Count && row < canvas.Rows; i++)
            {
                var provider = providers[i];
                string[] lines = (provider.GetDisplayData() ?? string.Empty).Split('\n');
                for (int j = 0; j < lines.Length && row < canvas.Rows; j++)
                {
                    string line = lines[j].Trim();
                    float value = ExtractPercentage(line);
                    if (value < 0f) continue;
                    int percentAt = line.IndexOf('%');
                    string label = percentAt > 0 ? line.Substring(0, percentAt).Trim() : provider.SourceName;
                    int labelWidth = Mathf.Max(3, canvas.Columns - barWidth - 5);
                    canvas.TextClipped(0, row, label.ToUpperInvariant(), labelWidth);
                    int barX = Mathf.Min(labelWidth + 1, canvas.Columns - barWidth - 4);
                    canvas.Bar(barX, row, barWidth, value);
                    canvas.TextClipped(barX + barWidth + 1, row, (value * 100f).ToString("0") + "%",
                        Mathf.Max(1, canvas.Columns - barX - barWidth - 1));
                    row++;
                }
            }
            if (row == 0) canvas.Text(0, 0, "NO PERCENTAGES REPORTED");
            return Finish(canvas);
        }

        private string BuildPowerPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            if (Grid == null)
            {
                canvas.Text(0, 0, "NO CONSTRUCT");
                return Finish(canvas);
            }

            float generated = Mathf.Max(0f, Grid.PowerGenerated);
            float consumed = Mathf.Max(0f, Grid.PowerConsumed);
            float net = generated - consumed;
            string state = net >= -0.1f ? "STABLE" : "DEFICIT";
            bool bars = canvas.Rows >= 4 && canvas.Columns >= 22;
            int barWidth = bars ? Mathf.Clamp(canvas.Columns / 3, 8, 14) : 0;
            int row = 0;

            canvas.Text(0, row++, "POWER " + state);
            if (row < canvas.Rows)
            {
                canvas.Text(0, row, "GEN " + FormatWatts(generated));
                if (bars) canvas.Bar(Mathf.Min(canvas.Columns - barWidth, 13), row, barWidth,
                    generated / Mathf.Max(1f, Mathf.Max(generated, consumed)));
                row++;
            }
            if (row < canvas.Rows)
            {
                canvas.Text(0, row, "USE " + FormatWatts(consumed));
                if (bars) canvas.Bar(Mathf.Min(canvas.Columns - barWidth, 13), row, barWidth,
                    consumed / Mathf.Max(1f, Mathf.Max(generated, consumed)));
                row++;
            }
            if (row < canvas.Rows) canvas.Text(0, row, "NET " + SignedWatts(net));
            return Finish(canvas);
        }

        private static string FirstUsefulLine(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "NO DATA";
            var lines = raw.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length > 0) return line;
            }
            return "NO DATA";
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
            Vector3 upAxis = GravityProvider.GetUp(transform.position);
            if (upAxis.sqrMagnitude < 0.0001f) upAxis = Vector3.up;
            else upAxis.Normalize();
            float speed = velocity.magnitude;
            float vertical = Vector3.Dot(velocity, upAxis);
            float forward = Grid != null ? Vector3.Dot(velocity, transform.forward) : 0f;

            canvas.Text(0, 0, "SPD  " + speed.ToString("0.0") + " m/s");
            if (canvas.Rows > 1)
                canvas.Text(0, 1, "FWD  " + (forward >= 0f ? "+" : "-") + Mathf.Abs(forward).ToString("0.0") + " m/s");
            if (canvas.Rows > 2)
                canvas.Text(0, 2, "VERT " + (vertical >= 0f ? "+" : "-") + Mathf.Abs(vertical).ToString("0.0") + " m/s");

            // Short, capped thrust bars leave room for the direction labels and do not turn
            // an otherwise compact flight page into three edge-to-edge stripes.
            if (canvas.Rows > 3 && Grid != null)
            {
                var (fwd, back, right, left, up, down) = Grid.GetThrustByDirection();
                float thrustMax = Mathf.Max(1f, Mathf.Max(fwd + back, Mathf.Max(up + down, right + left)));
                int row = 3;
                DrawThrustBar(canvas, row++, "FWD", fwd, thrustMax);
                if (row < canvas.Rows) DrawThrustBar(canvas, row++, "UP", up, thrustMax);
                if (row < canvas.Rows) DrawThrustBar(canvas, row, "SIDE", right + left, thrustMax);
            }

            return Finish(canvas);
        }

        private static void DrawThrustBar(ScreenCanvas canvas, int row, string label, float value, float max)
        {
            if (row < 0 || row >= canvas.Rows) return;
            int barWidth = Mathf.Clamp(canvas.Columns / 3, 5, 14);
            int barX = Mathf.Min(label.Length + 1, Mathf.Max(0, canvas.Columns - barWidth - 4));
            canvas.TextClipped(0, row, label, barX);
            canvas.Bar(barX, row, barWidth, max > 0.001f ? value / max : 0f);
            int percentX = barX + barWidth + 1;
            if (percentX < canvas.Columns)
                canvas.TextClipped(percentX, row, (Mathf.Clamp01(value / Mathf.Max(1f, max)) * 100f).ToString("0") + "%",
                    canvas.Columns - percentX);
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
            bool breathable = pressureAtm >= PressureRules.BreathablePressureAtm;
            float gravity = GravityProvider.GetGravity(here).magnitude;
            float temperature = room != null ? room.AirTemperatureC : ExteriorTemperatureC();

            int row = 0;
            if (row < canvas.Rows)
                canvas.Text(0, row++, "AIR  " + (breathable ? "BREATHABLE" : "NOT BREATHABLE")
                    + (room != null ? " · SEALED" : " · AMBIENT"));

            if (row < canvas.Rows)
            {
                TextRow(canvas, ref row, "PRESS", pressureAtm.ToString("0.00") + " atm");
                int barWidth = Mathf.Min(12, Mathf.Max(0, canvas.Columns - 18));
                if (barWidth >= 4 && row - 1 < canvas.Rows)
                    canvas.Bar(Mathf.Min(18, canvas.Columns - barWidth), row - 1, barWidth,
                        Mathf.Clamp01(pressureAtm / Mathf.Max(0.01f, PressureRules.NominalPressureAtm)));
            }
            TextRow(canvas, ref row, "GRAV", gravity.ToString("0.00") + " m/s2");
            TextRow(canvas, ref row, "TEMP", temperature.ToString("0.0") + " C");
            TextRow(canvas, ref row, "ALT", AltitudeText());
            return Finish(canvas);
        }

        /// <summary>Label and value share a left-anchored row; names never drift to the far
        /// edge when a number changes length.</summary>
        private static void TextRow(ScreenCanvas canvas, ref int row, string label, string value)
        {
            if (row >= canvas.Rows) return;
            string text = label + "  " + value;
            canvas.TextClipped(0, row, text, canvas.Columns);
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
            _batteryFound.Clear();

            float stored = 0f;
            float capacity = 0f;
            foreach (var block in Grid.AllBlocks)
            {
                if (block is not GridBattery battery) continue;
                _batteryFound.Add(battery);
                stored += Mathf.Max(0f, battery.storedWh);
                capacity += Mathf.Max(0f, battery.capacityWh);
            }

            if (_batteryFound.Count == 0)
            {
                canvas.Text(0, 0, "BATTERY STATUS");
                if (canvas.Rows > 1) canvas.Text(0, 1, "NO BATTERIES ON THIS CONSTRUCT");
                return Finish(canvas);
            }

            float percent = capacity > 0f ? stored / capacity : 0f;
            if (showBatteryPowerRates)
            {
                canvas.TextClipped(0, 0, "BATTERIES x" + _batteryFound.Count + "  "
                    + FormatWattHours(stored) + " / " + FormatWattHours(capacity), canvas.Columns);
                int availableRows = Mathf.Max(0, canvas.Rows - 1);
                bool hasOverflow = _batteryFound.Count > availableRows;
                int shown = hasOverflow ? Mathf.Max(0, availableRows - 1) : availableRows;
                for (int i = 0; i < shown; i++)
                {
                    var battery = _batteryFound[i];
                    float current = battery.IsCharging ? battery.CurrentChargeWatts
                        : battery.IsDischarging ? battery.CurrentDischargeWatts : 0f;
                    float maximum = battery.IsCharging ? battery.maxChargeRate
                        : battery.IsDischarging ? battery.maxDischargeRate
                        : Mathf.Max(battery.maxChargeRate, battery.maxDischargeRate);
                    string state = battery.IsCharging ? "CHG" : battery.IsDischarging ? "DIS" : "IDLE";
                    string line = "B" + (i + 1).ToString("00") + " " + state + " "
                        + FormatWatts(current) + " / " + FormatWatts(maximum);
                    canvas.TextClipped(0, i + 1, line, canvas.Columns);
                }
                if (_batteryFound.Count > shown && canvas.Rows > 1)
                    canvas.TextClipped(0, canvas.Rows - 1,
                        "+" + (_batteryFound.Count - shown) + " BATTERIES", canvas.Columns);
                return Finish(canvas);
            }

            canvas.TextClipped(0, 0, "x" + _batteryFound.Count + "  " + FormatWattHours(stored)
                + " / " + FormatWattHours(capacity) + "  " + (percent * 100f).ToString("0") + "%",
                canvas.Columns);

            const int cellWidth = 5;
            const int batteryWidth = 4;
            int perRow = Mathf.Max(1, canvas.Columns / cellWidth);
            int rowsOfCells = Mathf.Max(0, Mathf.Min(3, (canvas.Rows - 1) / 4));
            int capacityCells = perRow * rowsOfCells;

            for (int i = 0; i < _batteryFound.Count && i < capacityCells; i++)
            {
                int row = i / perRow;
                int column = i % perRow;
                canvas.Battery(column * cellWidth, 1 + row * 4, batteryWidth, _batteryFound[i].Fill01);
            }

            if (rowsOfCells == 0 && canvas.Rows > 1)
            {
                int width = Mathf.Min(16, Mathf.Max(2, canvas.Columns - 8));
                canvas.Text(0, 1, "BANK");
                canvas.Bar(Mathf.Min(5, canvas.Columns - width), 1, width, percent);
                return Finish(canvas);
            }

            if (_batteryFound.Count > capacityCells && canvas.Rows > 1)
                canvas.TextClipped(0, canvas.Rows - 1,
                    "+" + (_batteryFound.Count - capacityCells) + " NOT SHOWN", canvas.Columns);

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

            int panels = Mathf.Clamp(powerGraphPanels, 1, Mathf.Clamp(canvas.Rows / 3, 1, 3));
            int baseHeight = canvas.Rows / panels;
            int extraRows = canvas.Rows % panels;
            int top = 0;

            for (int panel = 0; panel < panels; panel++)
            {
                int height = baseHeight + (panel < extraRows ? 1 : 0);
                IReadOnlyList<float> first;
                IReadOnlyList<float> second;
                float firstNow, secondNow, scale;
                string label;

                if (panel == 0)
                {
                    first = _powerGainHistory; second = _powerLossHistory;
                    firstNow = Last(first); secondNow = Last(second);
                    scale = Mathf.Max(1f, Mathf.Max(Peak(first, firstNow), Peak(second, secondNow)));
                    label = "GRID  GEN " + FormatWatts(firstNow) + "  USE " + FormatWatts(secondNow);
                }
                else if (panel == 1)
                {
                    first = _solarOutputHistory; second = _solarCapacityHistory;
                    firstNow = Last(first); secondNow = Last(second);
                    scale = Mathf.Max(1f, Peak(second, secondNow));
                    label = "SOLAR  " + FormatWatts(firstNow) + " / " + FormatWatts(secondNow);
                }
                else
                {
                    first = _batteryChargeHistory; second = _batteryDischargeHistory;
                    firstNow = Last(first); secondNow = Last(second);
                    scale = Mathf.Max(1f, Mathf.Max(Peak(first, firstNow), Peak(second, secondNow)));
                    label = "BATTERY  CHG " + FormatWatts(firstNow) + "  DIS " + FormatWatts(secondNow);
                }

                canvas.TextClipped(0, top, label, canvas.Columns);
                int plotHeight = height - 1;
                if (plotHeight >= 2 && canvas.Columns >= 4)
                {
                    canvas.Plot(0, top + 1, canvas.Columns, plotHeight, first, scale, '\u2588');
                    canvas.Plot(0, top + 1, canvas.Columns, plotHeight, second, scale, '\u2592');
                }
                top += height;
            }

            return Finish(canvas);
        }

        private void SamplePowerHistory()
        {
            if (_powerGainHistory.Count > 0
                && Time.unscaledTime - _powerSampledAt < PowerSampleSeconds) return;
            _powerSampledAt = Time.unscaledTime;

            float solar = 0f, solarCapacity = 0f, batteryCharge = 0f, batteryDischarge = 0f;
            foreach (var block in Grid.AllBlocks)
            {
                if (block is GridSolarPanel panel)
                {
                    solar += Mathf.Max(0f, panel.CurrentOutput);
                    solarCapacity += Mathf.Max(0f, panel.maxOutput);
                }
                else if (block is GridBattery battery)
                {
                    batteryCharge += Mathf.Max(0f, battery.CurrentChargeWatts);
                    batteryDischarge += Mathf.Max(0f, battery.CurrentDischargeWatts);
                }
            }

            Push(_powerGainHistory, Mathf.Max(0f, Grid.PowerGenerated));
            Push(_powerLossHistory, Mathf.Max(0f, Grid.PowerConsumed));
            Push(_solarOutputHistory, solar);
            Push(_solarCapacityHistory, solarCapacity);
            Push(_batteryChargeHistory, batteryCharge);
            Push(_batteryDischargeHistory, batteryDischarge);

            static void Push(List<float> list, float value)
            {
                list.Add(value);
                if (list.Count > PowerHistoryLength) list.RemoveAt(0);
            }
        }

        private static float Last(IReadOnlyList<float> values)
            => values != null && values.Count > 0 ? values[values.Count - 1] : 0f;

        private static float Peak(IReadOnlyList<float> values, float atLeast)
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
            var selectedContainer = SingleSelectedCargoContainer();
            int entries = CollectGridCargo(_ => true, out int usedSlots, out int capacitySlots,
                out float totalKg, selectedContainer);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;
            string title = !string.IsNullOrWhiteSpace(manifestTitle) ? manifestTitle.Trim()
                : selectedContainer != null ? selectedContainer.SourceName : GridEntityName() + " MANIFEST";

            if (entries == 0)
            {
                ClearPageIcons();
                canvas.TextClipped(0, 0, title.ToUpperInvariant(), canvas.Columns);
                if (canvas.Rows > 1) canvas.Text(0, 1, "HOLD EMPTY");
                return Finish(canvas);
            }

            canvas.TextClipped(0, 0, title.ToUpperInvariant() + "  " + FormatMass(totalKg), canvas.Columns);
            if (canvas.Rows > 1) canvas.Bar(0, 1, Mathf.Min(16, canvas.Columns), fill);

            _manifestCategories.Clear();
            for (int i = 0; i < entries; i++)
            {
                string category = ManifestCategory(s_massScratch[i].item);
                if (!_manifestCategories.Contains(category)) _manifestCategories.Add(category);
            }

            int startRow = 2;
            int rowCursor = startRow;
            int shown = 0;
            int maxEntries = Mathf.Min(HeaviestItemCount, entries);
            for (int categoryIndex = 0; categoryIndex < _manifestCategories.Count
                 && shown < maxEntries && rowCursor < canvas.Rows - 1; categoryIndex++)
            {
                string category = _manifestCategories[categoryIndex];
                canvas.TextClipped(0, rowCursor++, category, canvas.Columns);
                for (int i = 0; i < entries && shown < maxEntries && rowCursor < canvas.Rows - 1; i++)
                {
                    var entry = s_massScratch[i];
                    if (entry.item == null || ManifestCategory(entry.item) != category) continue;
                    string line = entry.item.displayName + "  x" + FormatCount(entry.count)
                        + "  " + FormatMass(entry.massKg);
                    canvas.TextClipped(1, rowCursor++, line, Mathf.Max(1, canvas.Columns - 1));
                    shown++;
                }
            }

            int notShown = Mathf.Max(0, entries - shown);
            if (notShown > 0 && rowCursor < canvas.Rows)
                canvas.TextClipped(0, rowCursor, "+" + notShown + " ITEMS", canvas.Columns);
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
            canvas.TextClipped(0, 0, "CARGO  " + percentWord + "  " + FormatMass(totalKg), canvas.Columns);
            if (canvas.Rows > 1) canvas.Bar(0, 1, Mathf.Min(16, canvas.Columns), fill);

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
                string line = entry.item.displayName + "  " + FormatMass(entry.massKg);
                canvas.TextClipped(3, row, line, Mathf.Max(1, canvas.Columns - 3));
            }

            canvas.TextClipped(0, canvas.Rows - 1, "ON BOARD  " + FormatMass(totalKg), canvas.Columns);
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
                int percentX = nameWidth + barWidth + 1;
                if (percentX < canvas.Columns)
                    canvas.TextClipped(percentX, row, (fill * 100f).ToString("0") + "%",
                        canvas.Columns - percentX);
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
                canvas.Text(0, 0, "FARMS");
                if (canvas.Rows > 1) canvas.TextClipped(0, 1, "NO BEDS SELECTED", canvas.Columns);
                if (canvas.Rows > 2) canvas.TextClipped(0, 2, "AVAILABLE: SOIL / PLANTER / TREE", canvas.Columns);
                return Finish(canvas);
            }

            int ready = 0, dry = 0, oxygenShort = 0, empty = 0;
            for (int i = 0; i < _farmFound.Count; i++)
            {
                var bed = _farmFound[i].bed;
                if (bed == null) continue;
                if (bed.IsEmpty) empty++;
                else if (bed.IsReady) ready++;
                if (!bed.IsEmpty && bed.NeedsWater) dry++;
                if (!bed.IsEmpty && bed.NeedsOxygen) oxygenShort++;
            }

            string summary = "FARMS x" + _farmFound.Count + "  READY " + ready
                + "  DRY " + dry + "  NO O2 " + oxygenShort + "  EMPTY " + empty;
            canvas.TextClipped(0, 0, summary, canvas.Columns);

            int dataRows = Mathf.Max(0, canvas.Rows - 1);
            bool more = _farmFound.Count > dataRows;
            int shown = Mathf.Min(_farmFound.Count, more ? Mathf.Max(0, dataRows - 1) : dataRows);
            for (int i = 0; i < shown; i++)
            {
                var entry = _farmFound[i];
                var bed = entry.bed;
                if (bed == null) continue;

                string crop = bed.IsEmpty ? "EMPTY" : bed.CropName;
                if (string.IsNullOrEmpty(crop)) crop = bed.BedName;
                string status = FarmBedState(bed, detailed: false);
                string orientation = entry.vertical ? "VERT" : "FLAT";
                string line = orientation + "  " + bed.BedName + "  " + crop + "  " + status;
                canvas.TextClipped(0, i + 1, line, canvas.Columns);
            }

            if (more && canvas.Rows > 1)
                canvas.TextClipped(0, canvas.Rows - 1,
                    "+" + (_farmFound.Count - shown) + " MORE PLOTS", canvas.Columns);
            return Finish(canvas);
        }

        private string BuildFarmPlotsPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            RefreshFarmScan();

            if (_farmFound.Count == 0)
            {
                canvas.Text(0, 0, "FARMS · PLOT STATUS");
                if (canvas.Rows > 1) canvas.TextClipped(0, 1, "NO BEDS SELECTED", canvas.Columns);
                if (canvas.Rows > 2) canvas.TextClipped(0, 2, "AVAILABLE: SOIL / PLANTER / TREE", canvas.Columns);
                return Finish(canvas);
            }

            int ready = 0, dry = 0, oxygenShort = 0;
            for (int i = 0; i < _farmFound.Count; i++)
            {
                var bed = _farmFound[i].bed;
                if (bed == null || bed.IsEmpty) continue;
                if (bed.IsReady) ready++;
                if (bed.NeedsWater) dry++;
                if (bed.NeedsOxygen) oxygenShort++;
            }

            canvas.TextClipped(0, 0, "FARMS · PLOT STATUS x" + _farmFound.Count, canvas.Columns);
            if (canvas.Rows > 1)
                canvas.TextClipped(0, 1, "READY " + ready + "  DRY " + dry + "  NO O2 " + oxygenShort, canvas.Columns);

            int dataStart = 2;
            int row = dataStart;
            bool overflow = _farmFound.Count > Mathf.Max(0, canvas.Rows - dataStart);
            int rowLimit = overflow ? canvas.Rows - 1 : canvas.Rows;
            for (int i = 0; i < _farmFound.Count && row < rowLimit; i++)
            {
                var entry = _farmFound[i];
                var bed = entry.bed;
                if (bed == null) continue;

                string state = FarmBedState(bed, detailed: true);

                string product = FarmProductName(bed);
                string line = (entry.vertical ? "VERT" : "FLAT") + "  " + bed.BedName + "  "
                    + (bed.IsEmpty ? "EMPTY" : bed.CropName) + "  " + state;
                if (!string.IsNullOrEmpty(product) && !bed.IsEmpty)
                    line += "  -> " + product;

                var iconItem = FarmProductItem(bed);
                if (iconItem != null) canvas.Icon(0, row, 2, 1, iconItem);
                canvas.TextClipped(iconItem != null ? 3 : 0, row, line,
                    canvas.Columns - (iconItem != null ? 3 : 0));
                row++;
            }

            if (row < canvas.Rows && rowLimit < canvas.Rows)
                canvas.TextClipped(0, canvas.Rows - 1,
                    "+" + (_farmFound.Count - (row - dataStart)) + " MORE PLOTS", canvas.Columns);
            return Finish(canvas);
        }

        private static string FarmBedState(IFarmBed bed, bool detailed)
        {
            if (bed == null) return "OFFLINE";
            if (bed.IsEmpty) return "EMPTY";
            if (bed.IsReady) return detailed ? "READY TO PICK" : "READY";
            if (bed.NeedsOxygen && bed.NeedsWater) return "NO O2 + DRY";
            if (bed.NeedsOxygen) return "NO O2";
            if (bed.NeedsWater) return "DRY";
            string growth = (bed.Growth01 * 100f).ToString("0") + "%";
            return detailed ? "GROW " + growth : growth;
        }

        private static ItemDefinition FarmProductItem(IFarmBed bed)
        {
            if (bed is FarmPlot soil && soil.plantedCrop != null) return soil.plantedCrop.harvestItem;
            if (bed is CropPlanter planter && planter.plantedCrop != null) return planter.plantedCrop.harvestItem;
            if (bed is TreeFarmPlot tree) return tree.woodLogItem;
            return null;
        }

        private static string FarmProductName(IFarmBed bed)
        {
            var product = FarmProductItem(bed);
            return product != null ? product.displayName : string.Empty;
        }

        private string BuildAutoRunPilotPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            var pilot = FindAutoRunPilot();
            if (pilot == null)
            {
                canvas.TextClipped(0, 0, "AUTO-RUN PILOT", canvas.Columns);
                if (canvas.Rows > 1) canvas.TextClipped(0, 1, "NO PILOT ON THIS CONSTRUCT", canvas.Columns);
                return Finish(canvas);
            }

            var book = Grid.GetComponent<VoxelEngine.Navigation.RouteBook>();
            int row = 0;
            WritePageLine(canvas, ref row, "AUTO-RUN PILOT");
            WritePageLine(canvas, ref row, "STATUS  " + AutoRunStatus());
            WritePageLine(canvas, ref row, "CONTROL  " + FormatWatts(pilot.PowerDraw));
            if (book == null)
            {
                WritePageLine(canvas, ref row, "NO ROUTE BOOK");
                return Finish(canvas);
            }

            if (book.IsRecording)
            {
                WritePageLine(canvas, ref row, "RECORDING  "
                    + (book.Draft != null ? book.Draft.routeName : "NEW ROUTE"));
                WritePageLine(canvas, ref row, "POINTS  " + (book.Draft != null ? book.Draft.waypoints.Count : 0));
                return Finish(canvas);
            }

            var route = book.Find(pilot.selectedRouteName);
            if (route == null && book.Count > 0) route = book.Routes[0];
            if (route == null)
            {
                WritePageLine(canvas, ref row, "NO SAVED ROUTES");
                return Finish(canvas);
            }

            WritePageLine(canvas, ref row, "ROUTE  " + route.routeName);
            WritePageLine(canvas, ref row, "MODE  " + RouteModeLabel(route.travelMode)
                + "  ·  POINTS " + route.waypoints.Count);
            var plan = pilot.Plan;
            if (plan.IsValid)
            {
                WritePageLine(canvas, ref row, "DIST  " + plan.TotalDistanceKm.ToString("0") + " km"
                    + "  ·  ETA " + VoxelEngine.Navigation.GridRouteRecorder.FormatDuration(plan.TotalSeconds));
                WritePageLine(canvas, ref row, "ENERGY  " + FormatWattHours(plan.TotalWattHours)
                    + "  ·  MARGIN " + (plan.ReserveMargin01 * 100f).ToString("0") + "%");
                WritePageLine(canvas, ref row, plan.HasWarnings
                    ? "WARNINGS  " + plan.Warnings.Count : "ROUTE CLEAR");
            }
            else WritePageLine(canvas, ref row, "ROUTE CANNOT BE COSTED");
            return Finish(canvas);
        }

        private string BuildRouteRecorderPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            var recorder = FindRouteRecorder();
            if (recorder == null)
            {
                canvas.TextClipped(0, 0, "ROUTE RECORDER", canvas.Columns);
                if (canvas.Rows > 1) canvas.TextClipped(0, 1, "NO RECORDER ON THIS CONSTRUCT", canvas.Columns);
                return Finish(canvas);
            }

            var book = Grid.GetComponent<VoxelEngine.Navigation.RouteBook>();
            int row = 0;
            WritePageLine(canvas, ref row, "ROUTE RECORDER");
            if (book == null)
            {
                WritePageLine(canvas, ref row, "NO ROUTE BOOK");
                return Finish(canvas);
            }

            WritePageLine(canvas, ref row, book.IsRecording ? "STATE  RECORDING" : "STATE  READY");
            WritePageLine(canvas, ref row, "SAVED ROUTES  " + book.Count);
            if (book.IsRecording && book.Draft != null)
            {
                WritePageLine(canvas, ref row, "DRAFT  " + book.Draft.routeName);
                WritePageLine(canvas, ref row, "MODE  " + RouteModeLabel(book.Draft.travelMode)
                    + "  ·  POINTS " + book.Draft.waypoints.Count);
                return Finish(canvas);
            }

            var route = book.Find(recorder.selectedRouteName);
            if (route == null && book.Count > 0) route = book.Routes[0];
            if (route == null)
            {
                WritePageLine(canvas, ref row, "NO SAVED ROUTES");
                return Finish(canvas);
            }

            WritePageLine(canvas, ref row, "SELECTED  " + route.routeName);
            WritePageLine(canvas, ref row, "MODE  " + RouteModeLabel(route.travelMode)
                + "  ·  POINTS " + route.waypoints.Count);
            var plan = recorder.Plan;
            if (plan.IsValid)
            {
                WritePageLine(canvas, ref row, "DIST  " + plan.TotalDistanceKm.ToString("0") + " km"
                    + "  ·  ETA " + VoxelEngine.Navigation.GridRouteRecorder.FormatDuration(plan.TotalSeconds));
                WritePageLine(canvas, ref row, "ENERGY  " + FormatWattHours(plan.TotalWattHours)
                    + "  ·  MARGIN " + (plan.ReserveMargin01 * 100f).ToString("0") + "%");
            }
            else WritePageLine(canvas, ref row, "PLAN  CANNOT BE COSTED");
            return Finish(canvas);
        }

        private IndustrialWorld.Navigation.AutoRunPilot FindAutoRunPilot()
        {
            if (Grid == null) return null;
            foreach (var block in Grid.AllBlocks)
                if (block is IndustrialWorld.Navigation.AutoRunPilot pilot) return pilot;
            return null;
        }

        private VoxelEngine.Navigation.GridRouteRecorder FindRouteRecorder()
        {
            if (Grid == null) return null;
            foreach (var block in Grid.AllBlocks)
                if (block is VoxelEngine.Navigation.GridRouteRecorder recorder) return recorder;
            return null;
        }

        private string AutoRunStatus()
        {
            if (Grid.WheelAutopilot != null && Grid.WheelAutopilot.IsDriving)
                return Grid.WheelAutopilot.Status;

            var local = Grid.GetComponent<IndustrialWorld.Navigation.LocalRoutePilot>();
            if (local != null && local.IsActive) return local.Status;

            var legacy = Grid.GetComponent<VoxelEngine.Navigation.GridRouteAutopilot>();
            if (legacy != null && legacy.IsArmed) return legacy.StatusLine;

            var session = Grid.GetComponent<IndustrialWorld.Navigation.RouteRunSession>();
            return session != null ? session.Status : "STANDBY";
        }

        private static string RouteModeLabel(VoxelEngine.Navigation.RouteTravelMode mode)
            => mode.ToString().ToUpperInvariant().Replace("NETWORK", " NETWORK");

        private static void WritePageLine(ScreenCanvas canvas, ref int row, string text)
        {
            if (row >= canvas.Rows) return;
            canvas.TextClipped(0, row++, text, canvas.Columns);
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
            var soil = UnityEngine.Object.FindObjectsByType<FarmPlot>(FindObjectsInactive.Exclude);
            for (int i = 0; i < soil.Length; i++) AddFarmBed(soil[i], here, radiusSq);

            var planters = UnityEngine.Object.FindObjectsByType<CropPlanter>(FindObjectsInactive.Exclude);
            for (int i = 0; i < planters.Length; i++) AddFarmBed(planters[i], here, radiusSq);

            var trees = UnityEngine.Object.FindObjectsByType<TreeFarmPlot>(FindObjectsInactive.Exclude);
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

            Vector3 gravityUp = GravityProvider.GetUp(position);
            if (gravityUp.sqrMagnitude < 0.0001f) gravityUp = Vector3.up;
            else gravityUp.Normalize();
            bool vertical = Mathf.Abs(Vector3.Dot(behaviour.transform.up, gravityUp)) < 0.5f;
            _farmFound.Add(new FarmBedEntry
            {
                bed = bed, position = position, distanceSq = distanceSq, vertical = vertical,
            });
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

            int captionX = left + Mathf.Max(0, (width - caption.Length) / 2);
            canvas.Text(captionX, top, caption);

            string number = (Mathf.Clamp01(value01) * 100f).ToString("0.00") + "%";
            int numberRow = height >= 3 ? top + 1 + (height - 3) / 2 : top + 1;
            int numberX = left + Mathf.Max(0, (width - number.Length) / 2);
            canvas.Text(numberX, numberRow, number);

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
            canvas.TextClipped(0, 0, fillWord + "  " + massWord, canvas.Columns);

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
            var selectedContainer = SingleSelectedCargoContainer();
            int entries = CollectGridCargo(_ => true,
                out int usedSlots, out int capacitySlots, out float totalKg, selectedContainer);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;

            string owner = selectedContainer != null && !string.IsNullOrEmpty(selectedContainer.SourceName)
                ? selectedContainer.SourceName : GridEntityName();

            if (entries == 0)
            {
                ClearPageIcons();
                canvas.TextClipped(0, 0, "STORAGE MONITOR", canvas.Columns);
                if (canvas.Rows > 1) canvas.Text(0, 1, "EMPTY");
                canvas.TextClipped(0, canvas.Rows - 1, Trim(owner, canvas.Columns), canvas.Columns);
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

            int footer = canvas.Rows - 1;
            string footerLine = Trim(owner, Mathf.Max(4, canvas.Columns / 2)) + "  "
                + (fill * 100f).ToString("0") + "%  " + FormatMass(totalKg);
            canvas.TextClipped(0, footer, footerLine, canvas.Columns);

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

            // Keep the frame intact; the heading and footer sit on its inside rows.
            canvas.TextClipped(1, 1, body.DisplayName.ToUpperInvariant() + "  ALT "
                + FormatDistance(altitude), canvas.Columns - 2);

            int discTop = 2;
            int discHeight = canvas.Rows - 4;
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

            // The body's name sits on the disc, centred by its left edge rather than
            // right-aligned, so it remains readable when the label length changes.
            string name = Trim(body.DisplayName.ToUpperInvariant(), Mathf.Max(4, columns - 4));
            if (rows >= 3)
            {
                int nameX = offsetX + Mathf.Max(0, (columns - name.Length) / 2);
                canvas.Text(nameX, discTop + rows / 2, name);
            }

            string footer = "LAT " + latitude.ToString("0") + "\u00b0  LON "
                + longitude.ToString("0") + "\u00b0  R " + FormatDistance(radius);
            canvas.TextClipped(1, canvas.Rows - 2, footer, canvas.Columns - 2);

            return Finish(canvas);
        }

        private static float NormaliseLongitude(float degrees)
        {
            while (degrees > 180f) degrees -= 360f;
            while (degrees < -180f) degrees += 360f;
            return degrees;
        }

        // ── Shared snapshot helpers ─────────────────────────────────────────────

        private GridCargoContainer SingleSelectedCargoContainer()
        {
            GridCargoContainer selected = null;
            var providers = ResolveAllProviders();
            for (int i = 0; i < providers.Count; i++)
            {
                if (providers[i] is not GridCargoContainer cargo) continue;
                if (selected != null) return null;
                selected = cargo;
            }
            return selected;
        }

        private static string ManifestCategory(ItemDefinition item)
        {
            if (item is ResourceItem resource)
            {
                switch (resource.subcategory.ToString().ToUpperInvariant())
                {
                    case "RAW": return "RAW MATERIALS";
                    case "INGOT": return "INGOTS";
                    case "COMPONENT": return "COMPONENTS";
                    case "FUEL": return "FUEL";
                    case "MISC": return "MISC";
                }
            }

            string category = item != null ? item.category : null;
            return string.IsNullOrWhiteSpace(category) ? "OTHER" : category.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// Aggregates every item held by each chosen container into <see cref="s_massScratch"/>,
        /// sorted heaviest first, and returns the number of distinct types (capped at scratch
        /// capacity). A single linked container scopes the monitor to that container; otherwise
        /// the page shows the whole construct.
        /// </summary>
        private int CollectGridCargo(System.Func<ItemDefinition, bool> filter,
            out int usedSlots, out int capacitySlots, out float totalKg,
            GridCargoContainer onlyContainer = null)
        {
            int found = 0;
            usedSlots = 0;
            capacitySlots = 0;
            totalKg = 0f;

            foreach (var block in Grid.AllBlocks)
            {
                if (block is not GridCargoContainer cargo || cargo.container == null
                    || (onlyContainer != null && cargo != onlyContainer)) continue;
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
