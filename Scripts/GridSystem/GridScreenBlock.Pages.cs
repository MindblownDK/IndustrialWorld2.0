// Assets/Scripts/VoxelEngine/GridSystem/GridScreenBlock.Pages.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║           THE SCREEN PAGES (17.42.0-dev)                         ║
// ║                                                                  ║
// ║  A separate half of GridScreenBlock on purpose. The original     ║
// ║  file is about how a screen is BUILT (frame, glass, glow,        ║
// ║  TextMesh); this one is about what it SAYS.                      ║
// ║                                                                  ║
// ║  17.42.0-dev rebuilt the page suite as instruments: every page   ║
// ║  draws on the character grid with its own ink palette, values    ║
// ║  end where the glass ends, bars are the length their labels      ║
// ║  need, and every reading has one home on the panel. The old      ║
// ║  pages grew organically and read like a dump of numbers - these  ║
// ║  were drawn against reference layouts and answer one question   ║
// ║  each, at a glance, in the dark.                                 ║
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
using System.Globalization;
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
        private const int HeaviestItemCount = 8;
        /// <summary>Graph history depth. A full house decimates (every second sample
        /// goes) instead of forgetting, so the record stretches instead of ending.</summary>
        private const int PowerHistoryMax = 2400;

        /// <summary>The chart's time spans, by the config's index; 0 = all recorded.</summary>
        private static readonly float[] GraphSpanSeconds =
            { 300f, 900f, 1800f, 3600f, 43200f, 86400f, 0f };

        // ── Cached page state ────────────────────────────────────────────────────
        private string _cachedPageText;
        private readonly string[] _cachedInkTexts = new string[5];
        private int _cachedPageSignature = int.MinValue;

        // Where a page is being drawn while a split panel composes its panes. Null during
        // a normal single-page build, which is every screen that is not split.
        private ScreenCanvas _splitTarget;
        private RectInt _splitRect;
        private float _pageBuiltAt = -999f;
        private float _farmScannedAt = -999f;
        private readonly List<FarmBedEntry> _farmFound = new(16);

        private struct FarmBedEntry
        {
            public IFarmBed bed;
            public Vector3 position;
            public float distanceSq;
            public bool vertical;
        }

        // Graph history, one pair of buffers per series (main trace + paired trace),
        // with the moment each sample was taken. Per screen: a bank of screens each
        // keeps its own trace, and a panel shows what IT has seen, like a real gauge.
        private readonly List<float>[] _seriesMain = CreateSeriesBuffers();
        private readonly List<float>[] _seriesPaired = CreateSeriesBuffers();
        private readonly List<float> _seriesTimes = new(1024);
        private readonly List<float> _windowMain = new(128);
        private readonly List<float> _windowPaired = new(128);
        private float _graphSampledAt = -999f;

        private static List<float>[] CreateSeriesBuffers()
        {
            var buffers = new List<float>[12];
            for (int i = 0; i < buffers.Length; i++) buffers[i] = new List<float>(1024);
            return buffers;
        }

        /// <summary>Quadrant block glyphs by coverage bits (TL=1, TR=2, BL=4, BR=8) -
        /// the smooth rim of every disc the pages draw.</summary>
        private static readonly char[] QuadrantGlyphs =
        {
            ' ',       // 0
            '\u2598',  // 1  TL
            '\u259d',  // 2  TR
            '\u2580',  // 3  TL+TR
            '\u2596',  // 4  BL
            '\u258c',  // 5  TL+BL
            '\u259e',  // 6  TR+BL
            '\u259b',  // 7  TL+TR+BL
            '\u2597',  // 8  BR
            '\u259a',  // 9  TL+BR
            '\u2590',  // 10 TR+BR
            '\u259c',  // 11 TL+TR+BR
            '\u2584',  // 12 BL+BR
            '\u2599',  // 13 TL+BL+BR
            '\u259f',  // 14 TR+BL+BR
            '\u2588',  // 15 full
        };

        private readonly List<GridBattery> _batteryFound = new(32);
        /// <summary>The holds picked as this screen's sources, and the list being walked
        /// right now - both reused, because a cargo page refreshes twice a second.</summary>
        private readonly List<GridCargoContainer> _cargoScope = new(8);
        private readonly List<GridCargoContainer> _cargoWalk = new(8);
        private static readonly ItemMassEntry[] s_massScratch = new ItemMassEntry[256];

        private struct ItemMassEntry
        {
            public ItemDefinition item;
            public int count;
            public float massKg;
        }

        /// <summary>One instant of every series value, taken from a single walk of the
        /// construct so a panel can never show two different seconds as "now".summary>
        private struct GraphSnapshot
        {
            public float loadW, ratedW, genW;
            public float battInW, battOutW, battStoredWh, battCapWh, battRatedInW, battRatedOutW;
            public float solarOutW, solarUsedW, solarCapW;
            public float reactorOutW, reactorRatedW;
            public float h2OutW, h2RatedW;
            public float o2Stored, o2Cap, h2Stored, h2Cap;
        }

        // ── Page options, read back out of their saved strings ───────────────────

        private ScreenGraphSeries GraphSeriesA
            => ScreenGraphSeriesCatalog.Parse(graphSeriesA, ScreenGraphSeries.usage);
        private ScreenGraphSeries GraphSeriesB
            => ScreenGraphSeriesCatalog.Parse(graphSeriesB, ScreenGraphSeries.solar);
        private ScreenGraphSeries GraphSeriesC
            => ScreenGraphSeriesCatalog.Parse(graphSeriesC, ScreenGraphSeries.batteryInOut);

        // ── Entry point used by GridScreenBlock.FormattedDisplay ────────────────

        /// <summary>
        /// The text for one of the pages this half owns, cached - and with every ink layer
        /// published for the block to stack. Returns null when <paramref name="mode"/> is
        /// not one of them, so the caller can fall through to its own formatters without
        /// the two ever disagreeing.
        /// </summary>
        private string RenderManagedPage(ScreenDataMode mode)
        {
            int signature = PageSignature(mode);
            if (signature == 0)
            {
                // A page this half does not own speaks in one colour only - and the
                // one colour rides the caller's text, not a stale ink layer.
                _pageInksPublished = false;
                for (int i = 0; i < _inkTexts.Length; i++) _inkTexts[i] = string.Empty;
                return null;
            }

            // Time-stamped rather than a countdown: FormattedDisplay is a property, and a
            // screen that happens to be read twice in one frame must not age the cache twice.
            bool stale = signature != _cachedPageSignature
                      || _cachedPageText == null
                      || Time.unscaledTime - _pageBuiltAt >= ScreenPageRefreshSeconds;
            if (!stale)
            {
                for (int i = 0; i < _inkTexts.Length; i++) _inkTexts[i] = _cachedInkTexts[i];
                return _cachedPageText;
            }

            _cachedPageSignature = signature;
            _pageBuiltAt = Time.unscaledTime;
            _pageInksPublished = false;
            _cachedPageText = IsSplit ? BuildSplitPage() : BuildPage(mode);
            if (!_pageInksPublished)
            {
                // A status line ("OFFLINE", "NO CONSTRUCT") printed without a canvas:
                // the body layer takes it whole and the inks go quiet.
                _inkTexts[0] = _cachedPageText ?? string.Empty;
                for (int i = 1; i < _inkTexts.Length; i++) _inkTexts[i] = string.Empty;
            }
            for (int i = 0; i < _inkTexts.Length; i++)
                _cachedInkTexts[i] = _inkTexts[i] ?? string.Empty;
            return _cachedPageText;
        }

        /// <summary>Set by Finish() so the cache knows the ink layers are already published.</summary>
        private bool _pageInksPublished;

        /// <summary>
        /// Zero when this half of the class has nothing to draw, otherwise a hash of
        /// everything that decides what the glass shows.
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
            signature = signature * 31 + (storageNote ?? string.Empty).GetHashCode();
            signature = signature * 31 + Mathf.Clamp(powerGraphPanels, 1, 3);
            signature = signature * 31 + Mathf.Clamp(powerGraphSpan, 0, 6);
            signature = signature * 31 + (showBatteryPowerRates ? 1 : 0);
            signature = signature * 31 + (showInventoryFill ? 1 : 0);
            signature = signature * 31 + (showStorageWeight ? 1 : 0);
            signature = signature * 31 + (int)GraphSeriesA;
            signature = signature * 31 + (int)GraphSeriesB;
            signature = signature * 31 + (int)GraphSeriesC;
            return signature == 0 ? 1 : signature;
        }

        // ── SPLIT PANELS ────────────────────────────────────────────────────────
        //
        // One glass, two or four pages. The screen still has one body mesh, so a split is
        // composed rather than layered: every pane draws into its own canvas, the canvases
        // are blitted into one full-size canvas at the pane's corner, and each pane is
        // outlined so the joins read as instruments side by side, not as one page that
        // got cut.

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

        /// <summary>Draw one pane: content first, then its outline on top, so a page can
        /// never paint over its own border. The outline is the divider - a player splitting
        /// a glass between two readings must see where one instrument ends.</summary>
        private void DrawPane(ScreenCanvas main, RectInt rect, ScreenDataMode mode)
        {
            if (rect.width < 4 || rect.height < 3) return;

            int innerWidth = rect.width - 2;

            if (!IsManagedPage(mode))
            {
                main.Box(rect.x, rect.y, rect.width, rect.height, ScreenInk.Dim);
                string label = Trim(ScreenPageCatalog.For(mode).Label.ToUpperInvariant(), innerWidth);
                main.Text(rect.x + 1, rect.y + 1, label, false, ScreenInk.Accent);
                if (rect.height > 3)
                    main.TextClipped(rect.x + 1, rect.y + 2, "NOT AVAILABLE IN A PANE", innerWidth, ScreenInk.Dim);
                return;
            }

            var content = new RectInt(rect.x + 1, rect.y + 1, innerWidth, rect.height - 2);
            if (content.height <= 0) return;
            _splitTarget = main;
            _splitRect = content;
            try { BuildPage(mode); }
            finally { _splitTarget = null; }

            // The outline is drawn LAST and slightly brighter than page furniture.
            main.Box(rect.x, rect.y, rect.width, rect.height, ScreenInk.Accent);
            string paneLabel = Trim(ScreenPageCatalog.For(mode).Label.ToUpperInvariant(), innerWidth);
            main.Text(rect.x + 1, rect.y, paneLabel, false, ScreenInk.Accent);
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
                        : new RectInt(midColumn, 0, columns - midColumn, rows);

                case 2: // two stacked
                    return index == 0
                        ? new RectInt(0, 0, columns, midRow)
                        : new RectInt(0, midRow, columns, rows - midRow);

                default: // four quarters
                {
                    bool right = (index & 1) == 1;
                    bool bottom = index >= 2;
                    return new RectInt(
                        right ? midColumn : 0,
                        bottom ? midRow : 0,
                        right ? columns - midColumn : midColumn,
                        bottom ? rows - midRow : midRow);
                }
            }
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

        // ═════════════════════════════════════════════════════════════════════════
        // MIXED (the reference's categorized terminal board)
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Every linked source, filed under its category like a station board: a quiet
        /// dashed heading, then the source's own lines with their percentages drawn as
        /// bars. The page a player leaves on a wall in the workshop.
        /// </summary>
        private string BuildMixedPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            var providers = ResolveAllProviders();
            if (providers.Count == 0)
            {
                canvas.SectionHeader(0, 0, canvas.Columns, "MIXED");
                canvas.Text(0, 1, "NO SOURCES LINKED", false, ScreenInk.Dim);
                if (canvas.Rows > 2) canvas.Text(0, 2, "OPEN THE SCREEN EDITOR", false, ScreenInk.Dim);
                return Finish(canvas);
            }

            // File the sources by category, in the order they were linked.
            var order = new List<string>(6);
            var groups = new Dictionary<string, List<IGridDataProvider>>(6);
            for (int i = 0; i < providers.Count; i++)
            {
                string category = (providers[i].DataCategory ?? "GENERAL").Trim().ToUpperInvariant();
                if (category.Length == 0) category = "GENERAL";
                if (!groups.TryGetValue(category, out var list))
                {
                    list = new List<IGridDataProvider>(4);
                    groups[category] = list;
                    order.Add(category);
                }
                list.Add(providers[i]);
            }

            int row = 0;
            bool first = true;
            for (int g = 0; g < order.Count && row < canvas.Rows; g++)
            {
                if (!first && row < canvas.Rows) row++;
                first = false;
                canvas.SectionHeader(0, row++, canvas.Columns, order[g], ScreenInk.Dim);
                var list = groups[order[g]];
                for (int i = 0; i < list.Count && row < canvas.Rows; i++)
                    row = DrawProviderLines(canvas, row, list[i]);
            }
            return Finish(canvas);
        }

        /// <summary>
        /// One source's readings, filed into the page: name first, then its lines with
        /// any percentage drawn as a short bar and the number kept where the bar ends.
        /// Returns the next free row.
        /// </summary>
        private static int DrawProviderLines(ScreenCanvas canvas, int row, IGridDataProvider provider)
        {
            string name = (provider.SourceName ?? "SOURCE").ToUpperInvariant();
            if (row < canvas.Rows) canvas.TextClipped(1, row++, name, canvas.Columns - 1, ScreenInk.Accent);

            string raw = provider.GetDisplayData() ?? string.Empty;
            var lines = raw.Split('\n');
            for (int i = 0; i < lines.Length && row < canvas.Rows; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                float pct = ExtractPercentage(line);
                if (pct >= 0f)
                {
                    DrawCompactBarRow(canvas, row++, line, pct);
                }
                else
                {
                    canvas.TextClipped(2, row++, line, canvas.Columns - 2, ScreenInk.Primary);
                }
            }
            return row;
        }

        /// <summary>Label left, a bounded bar, the percentage right - the one bar row
        /// every page shares. Bars never run past mid-panel unless the panel is tiny.</summary>
        private static void DrawCompactBarRow(ScreenCanvas canvas, int row, string label, float value01)
        {
            int columns = canvas.Columns;
            string percent = (Mathf.Clamp01(value01) * 100f).ToString("0") + "%";
            int barWidth = Mathf.Clamp(columns / 3, 3, 12);
            int labelWidth = Mathf.Max(2, columns - barWidth - percent.Length - 2);

            string labelText = label;
            int percentAt = labelText.IndexOf('%');
            if (percentAt >= 0) labelText = labelText.Substring(0, percentAt);
            labelText = labelText.Trim(' ', ':', '-', '\u2588', '\u2591');

            canvas.TextClipped(2, row, labelText, labelWidth - 2, ScreenInk.Primary);
            canvas.Bar(2 + labelWidth - 2, row, barWidth, value01, '\u2588', '\u2591', ScreenInk.Accent);
            canvas.TextClipped(labelWidth + barWidth + 1, row, percent,
                Mathf.Max(2, canvas.Columns - labelWidth - barWidth - 1), ScreenInk.Ok);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // SYSTEMS
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The state of every linked device, one line each: a lamp, the device, and what
        /// it says it is doing. Grouped by category so a grid with twenty machines reads
        /// as a board, not a scroll.
        /// </summary>
        private string BuildSystemsPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            var providers = ResolveAllProviders();
            if (providers.Count == 0)
            {
                canvas.SectionHeader(0, 0, canvas.Columns, "SYSTEMS");
                canvas.Text(0, 1, "NO SYSTEMS LINKED", false, ScreenInk.Dim);
                return Finish(canvas);
            }

            var order = new List<string>(6);
            var groups = new Dictionary<string, List<IGridDataProvider>>(6);
            for (int i = 0; i < providers.Count; i++)
            {
                string category = (providers[i].DataCategory ?? "DEVICE").Trim().ToUpperInvariant();
                if (category.Length == 0) category = "DEVICE";
                if (!groups.TryGetValue(category, out var list))
                {
                    list = new List<IGridDataProvider>(4);
                    groups[category] = list;
                    order.Add(category);
                }
                list.Add(providers[i]);
            }

            int row = 0;
            bool first = true;
            for (int g = 0; g < order.Count && row < canvas.Rows; g++)
            {
                if (!first && row < canvas.Rows) row++;
                first = false;
                canvas.SectionHeader(0, row++, canvas.Columns, order[g], ScreenInk.Dim);
                var list = groups[order[g]];
                for (int i = 0; i < list.Count && row < canvas.Rows; i++)
                {
                    var provider = list[i];
                    string state = DeviceStateWord(provider.GetDisplayData());
                    ScreenInk lamp = state switch
                    {
                        "OFFLINE" or "FAULT" or "EMPTY" => ScreenInk.Warn,
                        "RUNNING" or "ONLINE" or "READY" or "CHARGING" => ScreenInk.Ok,
                        _ => ScreenInk.Accent,
                    };
                    canvas.Text(1, row, "\u25CF", false, lamp); // ● lamp
                    string name = ((provider.SourceName ?? "DEVICE") + "  " + state).ToUpperInvariant();
                    canvas.TextClipped(3, row, name, canvas.Columns - 3, ScreenInk.Primary);
                    row++;
                }
            }
            return Finish(canvas);
        }

        /// <summary>The one word a device leads with, or its second line, or its state.</summary>
        private static string DeviceStateWord(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "IDLE";
            var lines = raw.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim().ToUpperInvariant();
                if (t.Length == 0) continue;
                if (i == 0 && t.Length <= 14) continue; // the name line
                if (t.Length <= 14) return t;
                return t.Substring(0, 14);
            }
            return "IDLE";
        }

        // ═════════════════════════════════════════════════════════════════════════
        // BARS
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Every percentage the linked sources report, as label / bar / value rows. The
        /// bars are the length their labels need, and every row starts at the left edge:
        /// a bar that runs off to the right of the panel is a bar nobody can read.
        /// </summary>
        private string BuildBarsPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            var providers = ResolveAllProviders();
            if (providers.Count == 0)
            {
                canvas.Text(0, 0, "NO SOURCES LINKED", false, ScreenInk.Dim);
                if (canvas.Rows > 1) canvas.Text(0, 1, "LINK A DEVICE TO SHOW BARS", false, ScreenInk.Dim);
                return Finish(canvas);
            }

            // The reference board: [SOURCE NAME], then the source's OWN readout line by
            // line, with a bar drawn on the line that reports a percentage. The numbers
            // stay - they are the reading; the bar is where it is measured.
            int row = 0;
            for (int i = 0; i < providers.Count && row < canvas.Rows; i++)
            {
                var provider = providers[i];
                string name = (provider.SourceName ?? "SOURCE").ToUpperInvariant();
                if (i > 0) row++;
                if (row < canvas.Rows)
                    canvas.TextClipped(0, row++, "[" + name + "]", canvas.Columns, ScreenInk.Accent);

                var lines = (provider.GetDisplayData() ?? string.Empty).Split('\n');
                for (int j = 0; j < lines.Length && row < canvas.Rows; j++)
                {
                    string line = lines[j].Trim();
                    if (line.Length == 0) continue;
                    float value = ExtractPercentage(line);
                    if (value >= 0f)
                    {
                        int barWidth = Mathf.Clamp(canvas.Columns / 3, 3, 14);
                        canvas.Bar(0, row, barWidth, value, '\u2588', '\u2591', ScreenInk.Accent);
                        canvas.TextClipped(barWidth + 1, row, line,
                            Mathf.Max(2, canvas.Columns - barWidth - 1), ScreenInk.Primary);
                    }
                    else
                    {
                        canvas.TextClipped(0, row, line, canvas.Columns, ScreenInk.Primary);
                    }
                    row++;
                }
            }
            return Finish(canvas);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // POWER (the ledger)
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The truth about the bus: what the generators make, what the load takes, which
        /// way the batteries lean, and what is left. A battery is NOT a generator - the
        /// old page mixed the two and a bank discharging into a load read as "GEN 255 W /
        /// USE 255 W", which is a lie. These four rows keep the roles apart.
        /// </summary>
        private string BuildPowerPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            if (Grid == null)
            {
                canvas.Text(0, 0, "NO CONSTRUCT", false, ScreenInk.Dim);
                return Finish(canvas);
            }

            // No picked sources, no board: this page reports on what the player chose to
            // watch, never on everything the construct happens to contain.
            if (!HasPickedSources)
            {
                NoSources(canvas, "POWER");
                return Finish(canvas);
            }

            // The bank owns the bottom band, so its rows are reserved up front: a flow
            // line, the bank header, the rule, a cell and the lines under it. The ledger
            // above is bounded by that - a generator list that runs into the bank pushes
            // the cells off the bottom of the panel. A tall panel can carry the full
            // three-line cell caption; a short one keeps the two that matter and spends
            // the rows on the battery itself.
            int bankRows = canvas.Rows >= 18 ? 11 : 8;
            int bankTop = Mathf.Max(4, canvas.Rows - bankRows);
            int row = 0;

            // ── GEN: what is making power, how many of each, and how hard they work.
            CollectGeneratorGroups();
            if (_genGroups.Count > 0)
            {
                canvas.Text(0, row++, "GEN", false, ScreenInk.Accent);
                for (int i = 0; i < _genGroups.Count && row < bankTop; i++)
                {
                    var g = _genGroups[i];
                    float eff = g.rated > 0.01f ? Mathf.Clamp01(g.current / g.rated) : 0f;
                    string head = g.count > 1 ? g.count + "x " + g.label : g.label;
                    string tail = FormatWatts(g.current) + " / " + FormatWatts(g.rated)
                        + "  " + (eff * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
                    canvas.TextClipped(0, row, head, Mathf.Max(2, canvas.Columns - tail.Length - 2),
                        ScreenInk.Primary);
                    canvas.TextClipped(canvas.Columns - tail.Length, row, tail,
                        tail.Length + 1, eff >= 0.6f ? ScreenInk.Ok : ScreenInk.Primary);
                    row++;
                }
                if (row < bankTop)
                {
                    // The total of what the board shows - the selected generators when
                    // the screen is pointed at some, the whole bus otherwise.
                    float shown = 0f;
                    for (int i = 0; i < _genGroups.Count; i++) shown += _genGroups[i].current;
                    string total = FormatWatts(shown);
                    canvas.TextClipped(0, row, "TOTAL", 6, ScreenInk.Dim);
                    canvas.TextClipped(7, row, total, canvas.Columns - 7, ScreenInk.Ok);
                    row++;
                }
            }

            // ── USE: the load, against what the grid could ask for.
            if (row < bankTop)
            {
                // The load is read off the picked consumers, not the whole bus: a
                // screen watching two machines reports those two machines' draw.
                float usedW = 0f, ratedW = 0f;
                var picked = PickedBlocks();
                for (int i = 0; i < picked.Count; i++)
                {
                    if (picked[i] == null) continue;
                    usedW += Mathf.Max(0f, picked[i].PowerDraw);
                    ratedW += Mathf.Max(0f, picked[i].RatedPowerDraw);
                }
                string load = FormatWatts(usedW) + " / " + FormatWatts(ratedW);
                canvas.TextClipped(0, row, "USE", 6, ScreenInk.Dim);
                canvas.TextClipped(7, row, load, canvas.Columns - 7, ScreenInk.Primary);
                row++;
            }

            // ── BATTERIES: the bank block, at the bottom, as the reference wants it.
            DrawBatteryBlock(canvas, bankTop, includeHeader: true);
            return Finish(canvas);
        }

        /// <summary>
        /// The battery bank block - the bottom of the Power page and the whole of the
        /// Battery Status page, drawn the way the reference board draws it: full-width
        /// rules, the live flow line and the bank header centred between them, then the
        /// cells as a composed group - a picture per battery with its charge under it
        /// and its mode letter under that.
        /// </summary>
        private int DrawBatteryBlock(ScreenCanvas canvas, int row, bool includeHeader)
        {
            RefreshBatteryScan();

            if (includeHeader && row < canvas.Rows)
            {
                float inW = Grid != null ? Grid.BatteryChargeWatts : 0f;
                float outW = Grid != null ? Grid.BatteryDischargeWatts : 0f;
                // Net follows the signs on the row: flowing out is negative.
                float net = inW - outW;
                string signedNet = (net >= 0f ? "+" : "-") + FormatWatts(Mathf.Abs(net));
                string flow = "IN +" + FormatWatts(inW) + "  OUT -" + FormatWatts(outW)
                    + "  NET " + signedNet;
                // A narrow panel gets the readings without their padding instead of a
                // sentence clipped in the middle - every number stays visible.
                if (flow.Length > canvas.Columns)
                    flow = "IN " + FormatWatts(inW) + " OUT " + FormatWatts(outW) + " NET " + signedNet;
                if (flow.Length > canvas.Columns)
                    flow = "IN " + FormatWatts(inW) + " NET " + signedNet;
                canvas.TextCentered(0, row, canvas.Columns, flow,
                    Mathf.Abs(net) > 0.01f ? ScreenInk.Accent : ScreenInk.Dim);
                row++;
            }

            if (_batteryFound.Count == 0)
            {
                if (row < canvas.Rows)
                    canvas.TextCentered(0, row, canvas.Columns, "NO BATTERIES SELECTED", ScreenInk.Dim);
                return row + 1;
            }

            float stored = 0f, capacity = 0f;
            for (int i = 0; i < _batteryFound.Count; i++)
            {
                stored += _batteryFound[i].storedWh;
                capacity += _batteryFound[i].capacityWh;
            }
            float percent = capacity > 0f ? Mathf.Clamp01(stored / capacity) : 0f;

            if (includeHeader && row < canvas.Rows)
            {
                string pct = (percent * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
                string head = "BATTERIES (x" + _batteryFound.Count + ", " + pct
                    + ", " + FormatWattHours(stored) + ")";
                if (head.Length > canvas.Columns)
                    head = "BATTERIES (x" + _batteryFound.Count + ", " + pct + ")";
                if (head.Length > canvas.Columns)
                    head = "BATT x" + _batteryFound.Count + " " + pct;
                canvas.TextCentered(0, row, canvas.Columns, head, ScreenInk.Accent);
                row++;
            }

            // The rule under the header, the reference board's second full-width line.
            if (row < canvas.Rows)
            {
                for (int x = 0; x < canvas.Columns; x++)
                    canvas.Set(x, row, '\u2500', ScreenInk.Dim);
                row++;
            }

            // The cells take whatever rows are left, so the bank owns the bottom of the
            // glass instead of floating in it. Each cell is a picture plus three lines -
            // charge, stored energy, mode - and the group is composed in the middle of
            // the panel, never stranded against the left edge.
            int rowsLeft = Mathf.Max(0, canvas.Rows - row);
            // Charge, stored energy and mode when the panel can carry three lines; on a
            // short panel the energy line goes and the battery itself grows instead.
            int textRows = rowsLeft >= 10 ? 3 : 2;

            // HOW MANY CELLS FIT. A bank is read as a wall of cells, so the page packs as
            // many across as it can while every cell still shows a case tall enough to read
            // a charge level from: the minimum cell is five rows, which sets how many rows
            // of cells the panel needs and therefore how many columns each row can take.
            int minimumCellHeight = 5;
            int targetPerRow = Mathf.Max(1, _batteryFound.Count);
            for (int attempt = targetPerRow; attempt >= 1; attempt--)
            {
                int cellRowsNeeded = Mathf.CeilToInt(_batteryFound.Count / (float)attempt);
                int needed = cellRowsNeeded * (minimumCellHeight + textRows);
                if (needed <= rowsLeft) { targetPerRow = attempt; break; }
                targetPerRow = attempt;
            }

            int perRow = Mathf.Max(1, Mathf.Min(_batteryFound.Count, targetPerRow));
            int colWidth = Mathf.Max(7, canvas.Columns / perRow);
            perRow = Mathf.Max(1, canvas.Columns / colWidth);
            int cellRows = Mathf.CeilToInt(_batteryFound.Count / (float)perRow);
            int cellHeight = Mathf.Clamp((rowsLeft - cellRows * textRows) / Mathf.Max(1, cellRows), 4, 26);
            int cellW = Mathf.Clamp(colWidth - 2, 4, 12);
            if (cellW % 2 == 0) cellW--;   // an odd width keeps the case symmetric

            int r = row;
            int placed = 0;
            while (placed < _batteryFound.Count && r + cellHeight + textRows <= canvas.Rows)
            {
                int inThisRow = Mathf.Min(perRow, _batteryFound.Count - placed);
                int blockWidth = inThisRow * colWidth;
                int startX = Mathf.Max(0, (canvas.Columns - blockWidth) / 2);

                for (int c = 0; c < inThisRow; c++, placed++)
                {
                    var battery = _batteryFound[placed];
                    int x = startX + c * colWidth + (colWidth - cellW) / 2;
                    canvas.Battery(x, r, cellW, battery.Fill01, ScreenInk.Ok, ScreenInk.Accent, cellHeight);

                    int textColumn = startX + c * colWidth;
                    canvas.TextCentered(textColumn, r + cellHeight, colWidth,
                        (battery.Fill01 * 100f).ToString("0", CultureInfo.InvariantCulture) + "%",
                        ScreenInk.Primary);
                    if (textRows >= 3)
                        canvas.TextCentered(textColumn, r + cellHeight + 1, colWidth,
                            FormatWattHours(battery.storedWh), ScreenInk.Dim);

                    string mode = battery.mode == GridBatteryMode.Recharge ? "R"
                        : battery.mode == GridBatteryMode.Discharge ? "D" : "A";
                    ScreenInk modeInk = battery.IsCharging ? ScreenInk.Ok
                        : battery.IsDischarging ? ScreenInk.Accent : ScreenInk.Dim;
                    canvas.TextCentered(textColumn, r + cellHeight + textRows - 1, colWidth, mode, modeInk);
                }
                r += cellHeight + textRows;
            }

            if (placed < _batteryFound.Count && r < canvas.Rows)
                canvas.TextCentered(0, canvas.Rows - 1, canvas.Columns,
                    "+" + (_batteryFound.Count - placed) + " NOT SHOWN", ScreenInk.Dim);
            return r;
        }

        /// <summary>The generator kinds on this bus, counted and summed by their rating.</summary>
        private readonly List<(string label, int count, float current, float rated)> _genGroups = new();

        /// <summary>The block behind a selected source, whether the provider is the
        /// block itself or a behaviour riding on it.</summary>
        private static GridBlock BlockOf(IGridDataProvider provider)
        {
            if (provider is GridBlock block) return block;
            // A block that only has a plain readout still names the block it reads.
            if (provider is BlockReadout readout) return readout.Block;
            return provider is Component component
                ? component.GetComponentInParent<GridBlock>() : null;
        }

        /// <summary>
        /// The blocks this screen was pointed at - the DATA SOURCES the player ticked in
        /// the screen editor - and nothing else.
        ///
        /// A board that reports on the whole construct because its owner has not picked
        /// anything yet is a board that lies about what it is showing: a Power page with
        /// no generators selected listed the sun's output, a battery page counted every
        /// battery in the base, and a graph labelled "SOLAR" drew solar whether or not a
        /// single panel was chosen. Nothing is inferred. The pages that are about the
        /// world rather than the construct (the planet map, the banner, the camera) do
        /// not ask, and they are the only ones that do not.
        /// </summary>
        private List<GridBlock> PickedBlocks()
        {
            _pickedBlocks.Clear();
            var providers = ResolveAllProviders();
            for (int i = 0; i < providers.Count; i++)
            {
                var block = BlockOf(providers[i]);
                if (block != null && !_pickedBlocks.Contains(block)) _pickedBlocks.Add(block);
            }
            return _pickedBlocks;
        }

        /// <summary>True when at least one source is picked.</summary>
        private bool HasPickedSources => ResolveAllProviders().Count > 0;

        /// <summary>Picked blocks for a board, or the whole construct when the caller has
        /// already established there is nothing picked (graph history only).</summary>
        private List<GridBlock> BlocksForBoards()
        {
            _kindBlocks.Clear();
            var picked = PickedBlocks();
            if (picked.Count > 0)
            {
                _kindBlocks.AddRange(picked);
                return _kindBlocks;
            }
            foreach (var block in Grid.AllBlocks) _kindBlocks.Add(block);
            return _kindBlocks;
        }

        private readonly List<GridBlock> _kindBlocks = new(64);

        private readonly List<GridBlock> _pickedBlocks = new(16);

        /// <summary>The line a construct board prints when nothing is picked: the page is
        /// not empty, it is waiting to be told what to watch.</summary>
        private static void NoSources(ScreenCanvas canvas, string title)
            => EmptyState(canvas, title, "NO DATA SOURCES");

        /// <summary>
        /// The ONE way a page says it has nothing to show: its name, what is missing, and
        /// what to do about it, on three centred rows in the middle of the panel.
        ///
        /// Every page that could arrive empty used to invent its own wording on its own
        /// row, so a farm with no beds printed three left-aligned lines in the corner while
        /// a storage monitor printed a centred block - the same state, drawn two ways. A page
        /// that has nothing to show now looks like every other page that has nothing to
        /// show, and the fit has one shape to place instead of several.
        /// </summary>
        private static void EmptyState(ScreenCanvas canvas, string title, string headline,
            string hint = "PICK THEM IN THE SCREEN EDITOR")
        {
            int middle = Mathf.Max(1, canvas.Rows / 2);
            canvas.TextCentered(0, Mathf.Max(0, middle - 1), canvas.Columns,
                title.ToUpperInvariant(), ScreenInk.Dim);
            canvas.TextCentered(0, middle, canvas.Columns, headline, ScreenInk.Primary);
            if (middle + 1 < canvas.Rows)
                canvas.TextCentered(0, middle + 1, canvas.Columns, hint, ScreenInk.Dim);
        }

        private void CollectGeneratorGroups()
        {
            _genGroups.Clear();
            if (Grid == null) return;

            void AddGenerator(GridBlock block)
            {
                if (block == null || block is GridBattery) return;
                float current = block.PowerOutput;
                float rated = RatedOutputFor(block);
                if (current <= 0.01f && rated <= 0.01f) return;

                string label = (string.IsNullOrEmpty(block.blockName) ? "GENERATOR" : block.blockName)
                    .ToUpperInvariant();
                for (int i = 0; i < _genGroups.Count; i++)
                {
                    var g = _genGroups[i];
                    if (g.label != label) continue;
                    _genGroups[i] = (g.label, g.count + 1, g.current + current, g.rated + rated);
                    return;
                }
                _genGroups.Add((label, 1, current, rated));
            }

            // Selected sources only. A board with nothing picked reports nothing - it
            // does not borrow the construct's generators to look busy.
            var picked = PickedBlocks();
            for (int i = 0; i < picked.Count; i++) AddGenerator(picked[i]);

            _genGroups.Sort((a, b) => b.current.CompareTo(a.current));
        }

        /// <summary>A generator's own ceiling: its rated field where it has one, its
        /// current output otherwise (steam and friends do not publish a rating).</summary>
        private static float RatedOutputFor(GridBlock block)
        {
            switch (block)
            {
                case GridSolarPanel solar: return solar.maxOutput;
                case GridHydrogenEngine h2: return h2.wattsOutput;
                case GridPortableReactor reactor: return reactor.wattsOutput;
                default: return block.PowerOutput;
            }
        }

        /// <summary>What batteries sit on this bus, remembered between the Power page and
        /// the Battery Status page so both see the same bank in the same order.</summary>
        private void RefreshBatteryScan()
        {
            _batteryFound.Clear();
            if (Grid == null) return;

            // Selected batteries only, the same rule as the generators: the bank is the
            // batteries this screen was pointed at. Nothing is picked, nothing is shown.
            var picked = PickedBlocks();
            for (int i = 0; i < picked.Count; i++)
                if (picked[i] is GridBattery selected && !_batteryFound.Contains(selected))
                    _batteryFound.Add(selected);
        }

        /// <summary>The reference's battery page: the board's own big title over its
        /// rule, then the bank block the Power page carries at its bottom - flow line,
        /// bank header, and the cells with their charge and mode.</summary>
        private string BuildBatteryBankPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            if (Grid == null)
            {
                canvas.Text(0, 0, "NO CONSTRUCT", false, ScreenInk.Dim);
                return Finish(canvas);
            }
            // The bank is the batteries this screen was pointed at - nothing is borrowed
            // from the rest of the construct.
            if (!HasPickedSources)
            {
                NoSources(canvas, "BATTERY STATUS");
                return Finish(canvas);
            }
            int row = 0;
            if (row < canvas.Rows)
            {
                canvas.Text(0, row, "BATTERY STATUS", false, ScreenInk.Accent);
                row++;
            }
            if (row < canvas.Rows)
            {
                for (int x = 0; x < canvas.Columns; x++)
                    canvas.Set(x, row, '\u2500', ScreenInk.Dim);
                row++;
            }
            DrawBatteryBlock(canvas, row, includeHeader: true);
            return Finish(canvas);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // ENVIRONMENT
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The air board, as the reference draws it: a quiet block of left-adjacent
        /// readings - pressure, gravity, temperature, altitude - then the air's verdict
        /// in one line, then the pressure bar with its segment ticks under an ATM caption.
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

            // The reference lets the block breathe in the middle of the glass.
            int row = Mathf.Max(0, (canvas.Rows - 8) / 2);

            row = EnvRow(canvas, row, "ATM",
                pressureAtm.ToString("0.00", CultureInfo.InvariantCulture) + " atm", ScreenInk.Accent);
            row = EnvRow(canvas, row, "GRAV",
                gravity.ToString("0.00", CultureInfo.InvariantCulture) + " m/s2", ScreenInk.Accent);
            row = EnvRow(canvas, row, "TEMP",
                temperature.ToString("0.0", CultureInfo.InvariantCulture) + " C", ScreenInk.Accent);
            row = EnvRow(canvas, row, "ALT", AltitudeText(), ScreenInk.Accent);

            string air = room != null ? "SEALED AIR " : "AMBIENT AIR ";
            air += breathable ? "BREATHABLE" : "NOT BREATHABLE";
            row = EnvRow(canvas, row, "", air, ScreenInk.Accent);

            if (row < canvas.Rows)
            {
                row = EnvRow(canvas, row, "ATM", "", ScreenInk.Accent);
                float fill = Mathf.Clamp01(pressureAtm / Mathf.Max(0.01f, PressureRules.NominalPressureAtm));
                int barRows = canvas.Rows - row >= 2 ? 2 : 1;
                for (int b = 0; b < barRows && row < canvas.Rows; b++, row++)
                {
                    canvas.Bar(0, row, canvas.Columns, fill, '\u2588', '\u2591', ScreenInk.Accent);
                    // Segment ticks, so the bar reads as a scale and not a slab.
                    for (int x = 5; x < canvas.Columns - 1; x += 5)
                        canvas.Set(x, row, '\u2502', ScreenInk.Dim);
                }
            }
            return Finish(canvas);
        }

        /// <summary>Label and value share a left-adjacent row - one space apart, exactly
        /// as the reference board prints them. Names never drift to the far edge when a
        /// number changes length.</summary>
        private int EnvRow(ScreenCanvas canvas, int row, string label, string value,
            ScreenInk ink = ScreenInk.Primary)
        {
            if (row >= canvas.Rows) return row;
            string text = string.IsNullOrEmpty(label) ? value : label + " " + value;
            canvas.TextClipped(0, row, text, canvas.Columns, ink);
            return row + 1;
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

        // ═════════════════════════════════════════════════════════════════════════
        // POWER GRAPH (one to three series panels, each with its own legend)
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The recorded past as charts, over the span the screen is set to (five
        /// minutes to a day, or everything recorded). Each panel asks its own question - usage,
        /// solar, batteries, stores - plots its traces over a quiet lattice, and keeps a
        /// legend of what the numbers are RIGHT NOW beside the scale maximum. Multiple
        /// panels stack; each is scaled on its own terms (peak or rated), so a battery
        /// graph never flattens a usage graph beside it.
        /// </summary>
        private string BuildPowerGraphPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            if (Grid == null)
            {
                canvas.Text(0, 0, "NO CONSTRUCT", false, ScreenInk.Dim);
                return Finish(canvas);
            }
            // A chart of what? With no sources picked there is no series to draw, and
            // drawing the whole construct's would label a solar trace the player never
            // asked for.
            if (!HasPickedSources)
            {
                NoSources(canvas, "POWER GRAPH");
                return Finish(canvas);
            }
            SampleGraphHistory();

            var seriesList = new List<ScreenGraphSeries>(3) { GraphSeriesA, GraphSeriesB, GraphSeriesC };
            int wantPanels = Mathf.Clamp(powerGraphPanels, 1, 3);
            // A panel needs a legend, a plot and an axis; below that the page shows fewer
            // panels rather than unreadable ones.
            int fitPanels = Mathf.Clamp(canvas.Rows / 4, 1, 3);
            int panels = Mathf.Min(wantPanels, fitPanels);
            if (panels < 1) panels = 1;

            // The reference board lays its panels out SIDE BY SIDE across a wide glass.
            // A tall panel gets them stacked instead - the same charts, scrolled down -
            // because three columns on a portrait panel would be three unreadable slivers.
            bool across = panels > 1 && canvas.Columns >= canvas.Rows * 2;
            int gap = 1;

            if (across)
            {
                int available = canvas.Columns - gap * (panels - 1);
                int baseWidth = available / panels;
                int extra = available % panels;
                int column = 0;
                for (int panel = 0; panel < panels; panel++)
                {
                    int width = baseWidth + (panel < extra ? 1 : 0);
                    DrawGraphPanel(canvas, column, 0, width, canvas.Rows, seriesList[panel]);
                    column += width + gap;
                }
            }
            else
            {
                int baseHeight = canvas.Rows / panels;
                int extraRows = canvas.Rows % panels;
                int top = 0;
                for (int panel = 0; panel < panels; panel++)
                {
                    int height = baseHeight + (panel < extraRows ? 1 : 0);
                    DrawGraphPanel(canvas, 0, top, canvas.Columns, height, seriesList[panel]);
                    top += height;
                }
            }
            return Finish(canvas);
        }

        private void DrawGraphPanel(ScreenCanvas canvas, int left, int top, int width, int height,
            ScreenGraphSeries series)
        {
            var info = ScreenGraphSeriesCatalog.For(series);
            int index = (int)series;
            float span = GraphSpan();

            float mainNow = Last(_seriesMain[index]);
            float pairedNow = Last(_seriesPaired[index]);

            // The reference's panel: a boxed plot, the legend floating inside its top-left
            // corner with a colour chip per trace, and the time axis written along the
            // bottom rule - "1h37m ago" on the left, "now" on the right. A chart without a
            // frame and an axis is a squiggle; the frame is what reads as an instrument.
            bool boxed = height >= 5 && width >= 12;
            int right = left + width - 1;
            int plotTop = top + (boxed ? 1 : 0);
            int plotBottom = top + height - 1 - (boxed ? 1 : 0);
            int plotColumn = boxed ? left + 1 : left;
            int plotWidth = Mathf.Max(1, right - (boxed ? 1 : 0) - plotColumn + 1);
            int plotHeight = Mathf.Max(1, plotBottom - plotTop + 1);

            // The visible window, resampled BY TIME onto the plot's columns: the history's
            // samples are not evenly spaced once old data has thinned, and an index-linear
            // plot would squash the old hours into the new minutes.
            BuildWindow(_seriesMain[index], span, plotWidth, _windowMain);
            BuildWindow(_seriesPaired[index], span, plotWidth, _windowPaired);
            float scale = SeriesScale(series, info.Scale, _windowMain, _windowPaired, mainNow, pairedNow);

            if (boxed)
            {
                canvas.Box(left, top, width, height, ScreenInk.Dim);
                canvas.ChartLattice(plotColumn, plotTop, plotWidth, plotHeight, ScreenInk.Dim);
            }

            if (_windowMain.Count > 0 && plotHeight >= 2)
                canvas.Plot(plotColumn, plotTop, plotWidth, plotHeight, _windowMain, scale,
                    '\u2588', ScreenInk.Accent);
            if (info.HasPairedTrace && _windowPaired.Count > 0 && plotHeight >= 2)
                canvas.Plot(plotColumn, plotTop, plotWidth, plotHeight, _windowPaired, scale,
                    '\u2584', ScreenInk.Warn);

            // The legend goes on last so it is never buried by a trace running under it -
            // which is what happened when it was drawn first and then overpainted.
            DrawGraphLegend(canvas, info, plotColumn + 1, plotTop, plotWidth - 2, mainNow, pairedNow, scale);

            // The time axis: the oldest moment of the window at the left, "now" at the
            // right, written along the bottom rule the way the reference writes them.
            float shown = span > 0f ? span
                : _seriesTimes.Count > 0 ? Time.unscaledTime - _seriesTimes[0] : 0f;
            string age = FormatDurationShort(shown);
            int axisRow = top + height - 1;
            if (axisRow >= 0 && axisRow < canvas.Rows)
            {
                int ageColumn = boxed ? left + 2 : left;
                int nowColumn = boxed ? right - 2 : right;
                canvas.TextClipped(ageColumn, axisRow, age, Mathf.Max(2, plotWidth / 2), ScreenInk.Dim);
                canvas.TextRightClipped(nowColumn, axisRow, "now", 6, ScreenInk.Dim);
            }
            if (!boxed)
            {
                // No room for a frame, but one line of legend still names the panel.
                canvas.TextClipped(left, top, info.Label.ToUpperInvariant(), width, ScreenInk.Dim);
            }
        }

        /// <summary>
        /// The panel's legend, in the reference's shape: a colour chip in the trace's own
        /// ink, then the reading, one line per trace, then the panel's ceiling. It is
        /// drawn over the plot corner, which is where every instrument of this kind writes
        /// it - and where there is no room for all of it, the least important line goes.
        /// </summary>
        private void DrawGraphLegend(ScreenCanvas canvas, ScreenGraphSeriesInfo info,
            int column, int row, int width, float mainNow, float pairedNow, float scale)
        {
            if (width < 6) return;
            var (mainName, mainValue) = LegendMain(info, mainNow);
            var (pairedName, pairedValue) = LegendPaired(info, pairedNow);
            string ceiling = "Max: " + LegendValue(info, scale);

            bool roomForPaired = info.HasPairedTrace && width >= 16;
            bool roomForCeiling = width >= 12;

            int line = 0;
            DrawLegendLine(canvas, column, row + line++, width, mainName, mainValue, ScreenInk.Accent);
            if (roomForPaired)
                DrawLegendLine(canvas, column, row + line++, width, pairedName, pairedValue, ScreenInk.Warn);
            if (roomForCeiling && row + line < canvas.Rows)
                canvas.TextClipped(column, row + line, ceiling, width, ScreenInk.Dim);
        }

        /// <summary>One legend line: the chip, the trace's name, its current reading.</summary>
        private static void DrawLegendLine(ScreenCanvas canvas, int column, int row, int width,
            string name, string value, ScreenInk chipInk)
        {
            if (row < 0 || row >= canvas.Rows || width < 4) return;
            canvas.Set(column, row, '\u2588', chipInk);
            string label = name.Length > 0 ? name + ": " + value : value;
            canvas.TextClipped(column + 2, row, label, Mathf.Max(2, width - 2), ScreenInk.Primary);
        }

        private (string name, string value) LegendMain(ScreenGraphSeriesInfo info, float now)
        {
            // The catalogue's own name for the trace - the same word the picker shows, so
            // the legend and the picker never disagree - with a reading under it.
            string name = !string.IsNullOrEmpty(info.MainName) ? info.MainName : "Current";
            return (name, LegendValue(info, now));
        }

        private (string name, string value) LegendPaired(ScreenGraphSeriesInfo info, float now)
        {
            string name = !string.IsNullOrEmpty(info.PairedName) ? info.PairedName : string.Empty;
            return (name, LegendValue(info, now));
        }

        /// <summary>A reading in the series' own unit - watts, watt-hours or litres.</summary>
        private static string LegendValue(ScreenGraphSeriesInfo info, float value)
        {
            bool stored = info.Series == ScreenGraphSeries.battery;
            bool gas = info.Series == ScreenGraphSeries.storedOxygen
                       || info.Series == ScreenGraphSeries.storedHydrogen;
            if (gas) return FormatVolume(value);
            if (stored) return FormatWattHours(value);
            return FormatWatts(value);
        }

        /// <summary>Resample a history onto the plot's columns by time: each column
        /// reads the last sample at or before its moment, so a thinned old stretch
        /// still spans the hours it should.</summary>
        private void BuildWindow(IReadOnlyList<float> source, float span, int columns, List<float> target)
        {
            target.Clear();
            int n = Mathf.Min(source.Count, _seriesTimes.Count);
            if (n == 0 || columns < 1) return;

            float now = Time.unscaledTime;
            float start = span > 0f ? now - span : _seriesTimes[0];
            if (now - start < 1f) start = now - 1f;

            int idx = 0;
            while (idx + 1 < n && _seriesTimes[idx + 1] <= start) idx++;
            for (int x = 0; x < columns; x++)
            {
                float t = start + (now - start) * x / Mathf.Max(1, columns - 1);
                while (idx + 1 < n && _seriesTimes[idx + 1] <= t) idx++;
                target.Add(source[idx]);
            }
        }

        /// <summary>The panel's vertical scale, on the series' own terms.</summary>
        private float SeriesScale(ScreenGraphSeries series, ScreenGraphScale scaleMode,
            IReadOnlyList<float> main, IReadOnlyList<float> paired, float mainNow, float pairedNow)
        {
            if (scaleMode == ScreenGraphScale.RatedDemand)
            {
                float rated = Grid != null ? Mathf.Max(1f, Grid.MaxRequiredWatts) : 1f;
                float peak = Mathf.Max(Peak(main, mainNow), 0.001f);
                // Rated is the ceiling, not the wallpaper: a quiet construct scaled
                // to its nameplate draws its whole story as one bar on the floor.
                // Scale to what the window actually did, floored at a tenth of rated.
                return Mathf.Clamp(peak * 1.2f, rated * 0.1f, Mathf.Max(rated, peak * 1.2f));
            }
            if (scaleMode == ScreenGraphScale.Rated)
            {
                float rated = RatedScale(series);
                if (rated > 0.001f) return rated;
            }
            return Mathf.Max(0.001f, Mathf.Max(Peak(main, mainNow), Peak(paired, pairedNow)));
        }

        /// <summary>The rated ceiling a series is scaled against when it has one.</summary>
        private float RatedScale(ScreenGraphSeries series)
        {
            if (Grid == null) return 0f;
            var snap = TakeGraphSnapshot();
            switch (series)
            {
                case ScreenGraphSeries.battery: return Mathf.Max(1f, snap.battCapWh);
                case ScreenGraphSeries.batteryIn: return Mathf.Max(1f, snap.battRatedInW);
                case ScreenGraphSeries.batteryOut: return Mathf.Max(1f, snap.battRatedOutW);
                case ScreenGraphSeries.batteryInOut:
                    return Mathf.Max(1f, snap.battRatedInW + snap.battRatedOutW);
                case ScreenGraphSeries.solar: return snap.solarCapW;
                case ScreenGraphSeries.reactor: return snap.reactorRatedW;
                case ScreenGraphSeries.h2engine: return snap.h2RatedW;
                case ScreenGraphSeries.storedOxygen: return snap.o2Cap;
                case ScreenGraphSeries.storedHydrogen: return snap.h2Cap;
                default: return 0f;
            }
        }

        // ── Graph sampling ───────────────────────────────────────────────────────

        /// <summary>The span this screen's chart is set to, in seconds; 0 = all recorded.</summary>
        private float GraphSpan()
            => GraphSpanSeconds[Mathf.Clamp(powerGraphSpan, 0, GraphSpanSeconds.Length - 1)];

        /// <summary>Sampling slows as the window widens: a day-long chart does not
        /// need two-second fidelity, and the buffer would be memory for nothing.</summary>
        private float GraphSampleInterval()
        {
            float span = GraphSpan();
            return span <= 0f ? 5f : Mathf.Clamp(span / 600f, 1f, 30f);
        }

        private void SampleGraphHistory()
        {
            if (Grid == null) return;
            // Nothing picked, nothing recorded: a screen that is not watching anything
            // must not quietly keep the whole construct's history for later.
            if (!HasPickedSources) return;
            if (_seriesTimes.Count > 0
                && Time.unscaledTime - _graphSampledAt < GraphSampleInterval()) return;
            _graphSampledAt = Time.unscaledTime;

            var snap = TakeGraphSnapshot();
            Push(_seriesMain[(int)ScreenGraphSeries.usage], snap.loadW);
            Push(_seriesPaired[(int)ScreenGraphSeries.usage], snap.ratedW);
            Push(_seriesMain[(int)ScreenGraphSeries.usageScaled], snap.loadW);
            Push(_seriesPaired[(int)ScreenGraphSeries.usageScaled], snap.ratedW);
            Push(_seriesMain[(int)ScreenGraphSeries.battery], snap.battStoredWh);
            Push(_seriesPaired[(int)ScreenGraphSeries.battery], 0f);
            Push(_seriesMain[(int)ScreenGraphSeries.batteryIn], snap.battInW);
            Push(_seriesPaired[(int)ScreenGraphSeries.batteryIn], 0f);
            Push(_seriesMain[(int)ScreenGraphSeries.batteryOut], snap.battOutW);
            Push(_seriesPaired[(int)ScreenGraphSeries.batteryOut], 0f);
            float throughput = snap.battInW + snap.battOutW;
            Push(_seriesMain[(int)ScreenGraphSeries.batteryInOut], throughput);
            Push(_seriesPaired[(int)ScreenGraphSeries.batteryInOut], 0f);
            Push(_seriesMain[(int)ScreenGraphSeries.batteryInOutAuto], throughput);
            Push(_seriesPaired[(int)ScreenGraphSeries.batteryInOutAuto], 0f);
            Push(_seriesMain[(int)ScreenGraphSeries.solar], snap.solarOutW);
            Push(_seriesPaired[(int)ScreenGraphSeries.solar], snap.solarUsedW);
            Push(_seriesMain[(int)ScreenGraphSeries.reactor], snap.reactorOutW);
            Push(_seriesPaired[(int)ScreenGraphSeries.reactor], 0f);
            Push(_seriesMain[(int)ScreenGraphSeries.h2engine], snap.h2OutW);
            Push(_seriesPaired[(int)ScreenGraphSeries.h2engine], 0f);
            Push(_seriesMain[(int)ScreenGraphSeries.storedOxygen], snap.o2Stored);
            Push(_seriesPaired[(int)ScreenGraphSeries.storedOxygen], 0f);
            Push(_seriesMain[(int)ScreenGraphSeries.storedHydrogen], snap.h2Stored);
            Push(_seriesPaired[(int)ScreenGraphSeries.storedHydrogen], 0f);
            _seriesTimes.Add(_graphSampledAt);

            // A full house halves its resolution instead of forgetting its past:
            // every second sample goes, the record stretches twice as far, and the
            // time-based windowing in the panel draw keeps the hours honest.
            if (_seriesTimes.Count > PowerHistoryMax)
            {
                Decimate(_seriesTimes);
                for (int i = 0; i < _seriesMain.Length; i++)
                {
                    Decimate(_seriesMain[i]);
                    Decimate(_seriesPaired[i]);
                }
            }

            static void Push(List<float> list, float value) => list.Add(value);

            static void Decimate(List<float> list)
            {
                int w = 0;
                for (int r = 0; r < list.Count; r += 2) list[w++] = list[r];
                if (w < list.Count) list.RemoveRange(w, list.Count - w);
            }
        }

        /// <summary>One walk of the construct, every number the graph family can ask for.</summary>
        private GraphSnapshot TakeGraphSnapshot()
        {
            var snap = new GraphSnapshot();
            if (Grid == null) return snap;

            // Scoped to the picked sources. The grid's own totals are only used for gas,
            // which is stored per construct and has no per-block reading.
            var picked = PickedBlocks();
            bool scoped = picked.Count > 0;
            if (scoped)
            {
                for (int i = 0; i < picked.Count; i++)
                {
                    var block = picked[i];
                    if (block == null || !block.Enabled) continue;
                    snap.loadW += Mathf.Max(0f, block.PowerDraw);
                    snap.ratedW += Mathf.Max(0f, block.RatedPowerDraw);
                    snap.genW += Mathf.Max(0f, block.PowerOutput);
                    if (block is GridBattery battery)
                    {
                        snap.battInW += Mathf.Max(0f, battery.CurrentChargeWatts);
                        snap.battOutW += Mathf.Max(0f, battery.CurrentDischargeWatts);
                    }
                }
            }
            else
            {
                snap.loadW = Mathf.Max(0f, Grid.LoadWatts);
                snap.ratedW = Mathf.Max(0f, Grid.MaxRequiredWatts);
                snap.genW = Mathf.Max(0f, Grid.GeneratorWatts);
                snap.battInW = Mathf.Max(0f, Grid.BatteryChargeWatts);
                snap.battOutW = Mathf.Max(0f, Grid.BatteryDischargeWatts);
            }
            snap.o2Stored = Mathf.Max(0f, Grid.OxygenStored);
            snap.o2Cap = Mathf.Max(0f, Grid.OxygenCapacity);
            snap.h2Stored = Mathf.Max(0f, Grid.HydrogenStored);
            snap.h2Cap = Mathf.Max(0f, Grid.HydrogenCapacity);

            // The per-kind sums walk the same scope as the totals above.
            var kindSource = BlocksForBoards();
            for (int k = 0; k < kindSource.Count; k++)
            {
                var block = kindSource[k];
                if (block == null || !block.Enabled) continue;
                switch (block)
                {
                    case GridBattery battery:
                        snap.battStoredWh += Mathf.Max(0f, battery.storedWh);
                        snap.battCapWh += Mathf.Max(0f, battery.capacityWh);
                        snap.battRatedInW += Mathf.Max(0f, battery.maxChargeRate);
                        snap.battRatedOutW += Mathf.Max(0f, battery.maxDischargeRate);
                        break;
                    case GridSolarPanel solar:
                        snap.solarOutW += Mathf.Max(0f, solar.CurrentOutput);
                        snap.solarCapW += Mathf.Max(0f, solar.maxOutput);
                        break;
                    case GridPortableReactor reactor:
                        snap.reactorOutW += Mathf.Max(0f, reactor.PowerOutput);
                        snap.reactorRatedW += Mathf.Max(0f, reactor.wattsOutput);
                        break;
                    case GridHydrogenEngine h2:
                        snap.h2OutW += Mathf.Max(0f, h2.PowerOutput);
                        snap.h2RatedW += Mathf.Max(0f, h2.wattsOutput);
                        break;
                }
            }

            // Used solar is what the load and the chargers actually eat of what arrives.
            float appetite = snap.loadW + snap.battInW;
            snap.solarUsedW = Mathf.Min(snap.solarOutW, appetite);
            return snap;
        }

        private static float Last(IReadOnlyList<float> values)
            => values != null && values.Count > 0 ? values[values.Count - 1] : 0f;

        private static float Peak(IReadOnlyList<float> values, float atLeast)
        {
            float peak = atLeast;
            if (values == null) return peak;
            for (int i = 0; i < values.Count; i++) if (values[i] > peak) peak = values[i];
            return peak;
        }

        private static string FormatDurationShort(float seconds)
        {
            if (seconds >= 3600f) return (seconds / 3600f).ToString("0") + "h ago";
            if (seconds >= 60f) return (seconds / 60f).ToString("0") + "m ago";
            return seconds.ToString("0") + "s ago";
        }

        // ═════════════════════════════════════════════════════════════════════════
        // MANIFEST (the reference's categorized cargo terminal)
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// What is in the hold, filed by kind: raw materials, ingots, components, blocks,
        /// then the rest. The name at the top is the player's custom heading if they set
        /// one, else the single container's name, else the grid's. Armor blocks land under
        /// BLOCKS because the categories are read off the items themselves - a manifest
        /// that files a hull plate under MISC is worse than no manifest.
        /// </summary>
        private string BuildManifestPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            // Which holds? The ones this screen was pointed at. A cargo board that lists
            // the whole base because nothing was picked is showing data the player never
            // asked for, on every wall at once.
            if (!HasPickedSources)
            {
                NoSources(canvas, "MANIFEST");
                return Finish(canvas);
            }
            var selectedContainer = SingleSelectedCargoContainer();
            int entries = CollectGridCargo(_ => true, out int usedSlots, out int capacitySlots,
                out float totalKg, selectedContainer);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;

            int containerCount = CountLinkedCargoContainers();
            string title = !string.IsNullOrWhiteSpace(manifestTitle) ? manifestTitle.Trim()
                : selectedContainer != null ? selectedContainer.SourceName
                : containerCount > 1 ? GridEntityName() + "  x" + containerCount + " CARGOS"
                : GridEntityName();

            int row = 0;
            canvas.TextCentered(0, row++, canvas.Columns, "~ " + title.ToUpperInvariant() + " ~", ScreenInk.Accent);
            if (row < canvas.Rows)
            {
                // The hold's fill, quietly, under the name.
                int barWidth = Mathf.Min(canvas.Columns - 10, 18);
                canvas.Bar(0, row, barWidth, fill, '\u2588', '\u2591', ScreenInk.Dim);
                canvas.TextClipped(barWidth + 1, row,
                    (fill * 100f).ToString("0") + "%  " + FormatMass(totalKg),
                    Mathf.Max(2, canvas.Columns - barWidth - 1), ScreenInk.Dim);
                row++;
            }

            if (entries == 0)
            {
                if (row < canvas.Rows) canvas.Text(0, row, "HOLD EMPTY", false, ScreenInk.Dim);
                ClearPageIcons();
                return Finish(canvas);
            }

            // File every type under its category, in house order.
            var order = new List<string>(8);
            var groups = new Dictionary<string, List<ItemMassEntry>>(8);
            for (int i = 0; i < entries; i++)
            {
                var entry = s_massScratch[i];
                if (entry.item == null) continue;
                string category = ManifestCategory(entry.item);
                if (!groups.TryGetValue(category, out var list))
                {
                    list = new List<ItemMassEntry>(8);
                    groups[category] = list;
                    order.Add(category);
                }
                list.Add(entry);
            }
            order.Sort(CompareManifestCategories);

            ClearPageIcons();
            for (int g = 0; g < order.Count && row < canvas.Rows; g++)
            {
                row++;
                if (row >= canvas.Rows) break;
                canvas.SectionHeader(0, row++, canvas.Columns, order[g], ScreenInk.Dim);
                var list = groups[order[g]];
                for (int i = 0; i < list.Count && row < canvas.Rows; i++)
                {
                    var entry = list[i];
                    string name = entry.item.displayName.ToUpperInvariant();
                    string count = "x" + FormatCount(entry.count);
                    canvas.TextClipped(1, row, name,
                        Mathf.Max(2, canvas.Columns - count.Length - 3), ScreenInk.Primary);
                    canvas.TextRightClipped(canvas.Columns - 1, row, count, count.Length + 2, ScreenInk.Ok);
                    row++;
                }
            }
            return Finish(canvas);
        }

        /// <summary>House order: what came out of the ground first, what the furnaces made
        /// second, what the assemblers made third, and the rest in plain alphabetical.</summary>
        private static int CompareManifestCategories(string a, string b)
        {
            int ra = ManifestCategoryRank(a);
            int rb = ManifestCategoryRank(b);
            if (ra != rb) return ra.CompareTo(rb);
            return string.CompareOrdinal(a, b);
        }

        private static int ManifestCategoryRank(string category) => category switch
        {
            "RAW MATERIALS" => 0,
            "INGOTS" => 1,
            "COMPONENTS" => 2,
            "BLOCKS" => 3,
            "FUEL" => 4,
            "AMMO" => 5,
            "TOOLS" => 6,
            "MISC" => 8,
            "OTHER" => 9,
            _ => 7,
        };

        // ═════════════════════════════════════════════════════════════════════════
        // FILL BARS (the reference's named LCD strip)
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// One named block per linked container or tank: the name at the top (the custom
        /// heading when the player set one), the percentage beside it, and the fill bar
        /// under it. This is the long-panel page - a strip of them down a wall.
        /// </summary>
        private string BuildFillBarsPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();

            var providers = ResolveAllProviders();
            var fills = new List<(string name, float fill)>(8);
            for (int i = 0; i < providers.Count; i++)
            {
                if (providers[i] is GridCargoContainer cargo && cargo.container != null)
                    fills.Add((cargo.SourceName, Mathf.Clamp01(cargo.Fill01)));
                else if (providers[i] is GridGasTank tank)
                    fills.Add((tank.SourceName, Mathf.Clamp01(tank.Fill01)));
            }

            if (fills.Count == 0)
            {
                canvas.Text(0, 0, "FILL BARS", false, ScreenInk.Accent);
                if (canvas.Rows > 1) canvas.Text(0, 1, "NO CONTAINER LINKED", false, ScreenInk.Dim);
                return Finish(canvas);
            }

            string custom = manifestTitle != null ? manifestTitle.Trim() : string.Empty;
            int row = 0;
            if (custom.Length > 0 && canvas.Rows > 1)
            {
                canvas.TextClipped(0, row++, custom.ToUpperInvariant(), canvas.Columns, ScreenInk.Dim);
            }

            // Room per block: name line, bar line, one blank between blocks.
            int blockHeight = 3;
            int blocks = Mathf.Max(1, (canvas.Rows - row + 1) / blockHeight);
            int shown = Mathf.Min(fills.Count, blocks);

            for (int i = 0; i < shown && row < canvas.Rows; i++)
            {
                var (name, fill) = fills[i];
                string label = custom.Length > 0 && fills.Count == 1
                    ? custom.ToUpperInvariant()
                    : (name ?? "CONTAINER").ToUpperInvariant();
                string percent = (fill * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%";

                // The reference row: "Large Cargo Components (4.5%)" - the number is part
                // of the name line, not a distant column at the far side of the glass.
                canvas.TextClipped(0, row, label + " (" + percent + ")", canvas.Columns,
                    ScreenInk.Accent);
                row++;
                if (row < canvas.Rows)
                {
                    canvas.Bar(0, row, canvas.Columns, fill, '\u2588', '\u2591', ScreenInk.Ok);
                    row++;
                }
                row++; // the blank line between strips
            }

            if (shown < fills.Count && row <= canvas.Rows)
                canvas.TextClipped(0, canvas.Rows - 1, "+" + (fills.Count - shown) + " MORE",
                    canvas.Columns, ScreenInk.Dim);
            return Finish(canvas);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // FARM (the reference's plot board) + FARM PLOTS (the row detail)
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The plot board: one card per bed with its plant type (the harvest it will give,
        /// not the seed), a thin water level, and a growth column that fills upward -
        /// green while growing, bright when ready, red when the crop is failing, and a
        /// quiet purple outline when the plot is empty. The top says FARMS, then what the
        /// beds are doing in one line.
        /// </summary>
        private string BuildFarmPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            RefreshFarmScan();

            if (_farmFound.Count == 0)
            {
                // The same empty state every other page uses. The hint names the beds this
                // screen is waiting for, because on this page that IS the instruction.
                EmptyState(canvas, "FARMS", "NO BEDS SELECTED", "SOIL / PLANTER / TREE BED");
                return Finish(canvas);
            }

            int ready = 0, dry = 0, oxygenShort = 0, empty = 0, dead = 0;
            for (int i = 0; i < _farmFound.Count; i++)
            {
                var bed = _farmFound[i].bed;
                if (bed == null) continue;
                if (bed.IsDead) dead++;
                if (bed.IsEmpty) empty++;
                else if (bed.IsReady) ready++;
                if (!bed.IsEmpty && bed.NeedsWater) dry++;
                if (!bed.IsEmpty && bed.NeedsOxygen) oxygenShort++;
            }

            canvas.TextClipped(0, 0, "FARMS x" + _farmFound.Count, canvas.Columns, ScreenInk.Accent);
            if (canvas.Rows > 1)
            {
                int row = 1;
                canvas.Text(0, row, "READY " + ready, false, ScreenInk.Ok);
                int x = 8 + ready.ToString(CultureInfo.InvariantCulture).Length;
                x = AppendFarmCount(canvas, x, row, "DRY", dry, dry > 0 ? ScreenInk.Warn : ScreenInk.Dim);
                x = AppendFarmCount(canvas, x, row, "NO O2", oxygenShort,
                    oxygenShort > 0 ? ScreenInk.Warn : ScreenInk.Dim);
                x = AppendFarmCount(canvas, x, row, "DEAD", dead, dead > 0 ? ScreenInk.Warn : ScreenInk.Dim);
                AppendFarmCount(canvas, x, row, "EMPTY", empty, ScreenInk.Dim);
            }

            // Plot cards below the summary: each card is a little picture of its bed.
            int cardTop = 2;
            int cardWidth = 6;
            int cardHeight = 4;
            int perRow = Mathf.Max(1, canvas.Columns / cardWidth);
            int cardRows = Mathf.Max(0, (canvas.Rows - cardTop) / cardHeight);
            int capacity = perRow * cardRows;

            for (int i = 0; i < _farmFound.Count && i < capacity; i++)
            {
                var entry = _farmFound[i];
                var bed = entry.bed;
                if (bed == null) continue;

                int cx = (i % perRow) * cardWidth;
                int cy = cardTop + (i / perRow) * cardHeight;
                DrawFarmCard(canvas, cx, cy, entry);
            }

            if (_farmFound.Count > capacity && capacity > 0)
                canvas.TextClipped(0, canvas.Rows - 1,
                    "+" + (_farmFound.Count - capacity) + " MORE PLOTS", canvas.Columns, ScreenInk.Dim);
            return Finish(canvas);
        }

        /// <summary>
        /// One bed as a card: the product it will yield (as a picture), the water level
        /// as a thin column, and the growth column beside it. The growth column is the
        /// card's voice: growing = green fill, ready = full bright, failing = red, empty
        /// = quiet purple frame with nothing in it.
        /// </summary>
        private void DrawFarmCard(ScreenCanvas canvas, int x, int y, FarmBedEntry entry)
        {
            var bed = entry.bed;
            bool empty = bed == null || bed.IsEmpty;
            bool dead = !empty && bed.IsDead;
            bool ready = !empty && !dead && bed.IsReady;
            bool failing = !empty && !dead && !ready && (bed.NeedsOxygen || bed.NeedsWater);

            ScreenInk growthInk = empty ? ScreenInk.Dim
                : dead ? ScreenInk.Warn
                : ready ? ScreenInk.Accent
                : failing ? ScreenInk.Warn
                : ScreenInk.Ok;

            // Plant type: the harvest's own picture, so a corn plot shows corn.
            var product = FarmProductItem(bed);
            if (product != null) canvas.Icon(x, y, 2, 2, product);

            // Water level: one thin column, drawn from the bed's own hydration reading
            // instead of a dry/full guess.
            float water = empty ? 0f : Mathf.Clamp01(bed.Water01);
            canvas.VBarInk(x + 2, y, 3, water, ScreenInk.Ok, ScreenInk.Dim);

            // Growth: the wide column, filling upward from the base.
            float growth = empty || dead ? 0f : ready ? 1f : Mathf.Clamp01(bed.Growth01);
            canvas.VBarInk(x + 3, y, 3, growth, growthInk, empty ? ScreenInk.Dim : ScreenInk.Dim);

            // The state word under the card.
            string word = empty ? "EMPTY" : dead ? "DEAD" : ready ? "READY" : failing ? "FAIL" : "GROW";
            canvas.TextCentered(x, y + 3, Mathf.Min(6, canvas.Columns - x), word, growthInk);
        }

        /// <summary>
        /// The row detail: every bed as a line - product picture, water level, growth bar
        /// filling left to right, and the state at the right edge. This is the page that
        /// answers "is anything ready", one bed at a time.
        /// </summary>
        private string BuildFarmPlotsPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            RefreshFarmScan();

            if (_farmFound.Count == 0)
            {
                EmptyState(canvas, "FARMS \u00b7 PLOT STATUS", "NO BEDS SELECTED",
                    "SOIL / PLANTER / TREE BED");
                return Finish(canvas);
            }

            int row = 0;
            canvas.TextClipped(0, row++, "PLOT STATUS x" + _farmFound.Count, canvas.Columns, ScreenInk.Accent);

            // Rows of two lines each: the bars on the first, the words on the second.
            int rowHeight = 2;
            int dataRows = Mathf.Max(1, (canvas.Rows - row) / rowHeight);
            bool more = _farmFound.Count > dataRows;
            int shown = Mathf.Min(_farmFound.Count, more ? dataRows - 1 : dataRows);

            for (int i = 0; i < shown && row + 1 < canvas.Rows; i++)
            {
                var entry = _farmFound[i];
                var bed = entry.bed;
                if (bed == null) continue;

                bool empty = bed.IsEmpty;
                bool dead = !empty && bed.IsDead;
                bool ready = !empty && !dead && bed.IsReady;
                bool failing = !empty && !dead && !ready && (bed.NeedsOxygen || bed.NeedsWater);
                ScreenInk growthInk = empty ? ScreenInk.Dim : dead ? ScreenInk.Warn
                    : ready ? ScreenInk.Accent : failing ? ScreenInk.Warn : ScreenInk.Ok;

                var product = FarmProductItem(bed);
                int nameX = 0;
                if (product != null)
                {
                    canvas.Icon(0, row, 2, 2, product);
                    nameX = 3;
                }

                // Water: a short tick column drawn from the bed's own hydration reading;
                // growth: the wide bar beside it.
                float water = empty ? 0f : Mathf.Clamp01(bed.Water01);
                int growthWidth = Mathf.Max(3, canvas.Columns - nameX - 11);
                float fill = empty || dead ? 0f : ready ? 1f : Mathf.Clamp01(bed.Growth01);
                canvas.VBarInk(nameX, row, 2, water, ScreenInk.Ok, ScreenInk.Dim);
                canvas.Bar(nameX + 2, row, growthWidth, fill,
                    '\u2588', '\u2591', growthInk);
                canvas.Bar(nameX + 2, row + 1, growthWidth, fill,
                    '\u2588', '\u2591', growthInk);

                string state = empty ? "EMPTY" : dead ? "DEAD - CLEAR"
                    : ready ? "READY TO PICK"
                    : failing ? (bed.NeedsOxygen ? "NO O2" : "DRY")
                    : (Mathf.Clamp01(bed.Growth01) * 100f).ToString("0") + "%";
                canvas.TextRightClipped(canvas.Columns - 1, row, state, 12, growthInk);

                string crop = empty ? bed.BedName : (string.IsNullOrEmpty(bed.CropName) ? bed.BedName : bed.CropName);
                if (bed is CropPlanter ported && ported.autoDumpWhenRipe) crop += " \u00b7 PORT";
                canvas.TextClipped(nameX, row + 1,
                    (entry.vertical ? "VERT \u00b7 " : "FLAT \u00b7 ") + crop,
                    canvas.Columns - nameX, ScreenInk.Dim);

                row += rowHeight;
            }

            if (more && row < canvas.Rows)
                canvas.TextClipped(0, canvas.Rows - 1,
                    "+" + (_farmFound.Count - shown) + " MORE PLOTS", canvas.Columns, ScreenInk.Dim);
            return Finish(canvas);
        }

        /// <summary>One "LABEL n" pill on the FARMS summary line. Returns the column the next
        /// one starts at, so the line never overlaps itself on a narrow screen.</summary>
        private static int AppendFarmCount(ScreenCanvas canvas, int x, int row, string label, int value, ScreenInk ink)
        {
            if (x >= canvas.Columns) return x;
            string text = label + " " + value.ToString(CultureInfo.InvariantCulture);
            canvas.TextClipped(x, row, text, canvas.Columns - x, ink);
            return x + text.Length + 2;
        }

        private static ItemDefinition FarmProductItem(IFarmBed bed)
        {
            if (bed is FarmPlot soil && soil.plantedCrop != null) return soil.plantedCrop.harvestItem;
            if (bed is CropPlanter planter && planter.plantedCrop != null) return planter.plantedCrop.harvestItem;
            if (bed is TreeFarmPlot tree) return tree.woodLogItem;
            return null;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // MINING (the reference's amber dial) + MINING CARGO (ores only)
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The digging dial: two semicircle gauges in deep amber - lift thrust over the
        /// top arc, cargo fill over the bottom arc - with the percentages big in the
        /// middle. When the shaft is deep nobody is reading a paragraph.
        /// </summary>
        /// <summary>One cell's width over its height, as fitted - the number that turns
        /// cell counts into round shapes. Falls back to the design ratio before the
        /// first fit has measured anything.</summary>
        private float CellAspect()
        {
            if (_fittedLineHeight > 0.0001f && _fittedCharWidth > 0.0001f)
            {
                float measured = Mathf.Clamp(_fittedCharWidth / _fittedLineHeight, 0.3f, 1.2f);
                // The ratio is a property of the face, but each page measures it a little
                // differently - a page printing two rows has not printed two whole lines.
                // Blending keeps the column count from jumping when the player flips from
                // one page to the next, which would move every row on the glass.
                if (_cellAspectFit <= 0f || Mathf.Abs(measured - _cellAspectFit) > 0.06f)
                    _cellAspectFit = measured;                       // a new page's own reading
                else
                    _cellAspectFit = Mathf.Lerp(_cellAspectFit, measured, 0.2f);
                return _cellAspectFit;
            }
            return _cellAspectFit > 0f ? _cellAspectFit : 0.52f * StyleStretch(fontStyle);
        }

        /// <summary>The measured cell shape, smoothed across pages.</summary>
        private float _cellAspectFit;

        private string BuildMiningPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();

            Vector3 here = transform.position;
            float g = Mathf.Max(0.01f, GravityProvider.GetGravity(here).magnitude);
            float massKg = Mathf.Max(0f, Grid.TotalMass);
            float weightN = massKg * g;
            float liftN = Grid.GetThrustByDirection().up;
            float lift01 = weightN > 0.001f ? Mathf.Clamp01(liftN / weightN) : (liftN > 0f ? 1f : 0f);

            CollectGridCargo(_ => true, out int usedSlots, out int capacitySlots, out _);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;

            // Both branches print the same pair of readings; they are written once here so
            // there is one rounding rule on the page and no shadowed names to trip over.
            string liftWord = (lift01 * 100f).ToString("0.00", CultureInfo.InvariantCulture) + "%";
            string cargoWord = (fill * 100f).ToString("0.00", CultureInfo.InvariantCulture) + "%";

            bool tight = canvas.Rows < 7 || canvas.Columns < 16;
            if (tight)
            {
                // A small pane cannot hold a dial; it holds the two readings as bars. The
                // value goes on its own row unless the pane is wide enough for the label
                // and the number to share one without touching - which is what used to
                // print "LIFT THRUS0.00%".
                bool sameRow = canvas.Columns >= 26;
                int row = 0;
                canvas.Text(0, row, "LIFT THRUST", false, ScreenInk.Dim);
                if (sameRow) canvas.TextRight(canvas.Columns - 1, row, liftWord, ScreenInk.Accent);
                row++;
                if (!sameRow && row < canvas.Rows)
                {
                    canvas.Text(0, row, liftWord, false, ScreenInk.Accent);
                    row++;
                }
                if (row < canvas.Rows)
                {
                    canvas.Bar(0, row, canvas.Columns, lift01, '\u2588', '\u2591', ScreenInk.Accent);
                    row++;
                }
                if (row < canvas.Rows)
                {
                    canvas.Text(0, row, "CARGO", false, ScreenInk.Dim);
                    if (sameRow) canvas.TextRight(canvas.Columns - 1, row, cargoWord, ScreenInk.Accent);
                    row++;
                }
                if (!sameRow && row < canvas.Rows)
                {
                    canvas.Text(0, row, cargoWord, false, ScreenInk.Accent);
                    row++;
                }
                if (row < canvas.Rows)
                    canvas.Bar(0, row, canvas.Columns, fill, '\u2588', '\u2591', ScreenInk.Accent);
                return Finish(canvas);
            }

            // The dial fills the glass: a ring of tick marks around the centre with the
            // two readings stacked inside it. The radii are corrected by the fitted
            // cell shape, so the ring is a circle on the panel and not an oval that
            // leaves half the screen empty.
            int cx = canvas.Columns / 2;
            int cy = Mathf.Max(2, (canvas.Rows - 1) / 2);
            float aspect = Mathf.Max(0.2f, CellAspect());
            float radiusRows = Mathf.Min(canvas.Rows * 0.46f, (canvas.Columns * 0.46f) * aspect);
            radiusRows = Mathf.Max(3f, radiusRows);
            float radiusColumns = Mathf.Max(4f, radiusRows / aspect);

            // Tick count follows the dial's size: a small dial with forty lamps is one
            // solid ring, a big one with twelve is a spoked wheel.
            // Enough lamps that the ring reads as a graduated dial, few enough that each
            // one is a separate dash: roughly one per cell of circumference.
            int tickCount = Mathf.Clamp(Mathf.RoundToInt(radiusColumns * 1.6f), 12, 34);
            canvas.RingDial(cx, cy, radiusColumns, radiusRows, lift01, fill,
                ScreenInk.Accent, ScreenInk.Dim, tickCount);

            // The reference's stack: small caption over the big reading, a rule, then the
            // second reading under its caption.
            int liftCaptionRow = cy - Mathf.Max(2, Mathf.RoundToInt(radiusRows * 0.46f));
            int liftValueRow = liftCaptionRow + 1;
            int cargoValueRow = cy + Mathf.Max(1, Mathf.RoundToInt(radiusRows * 0.24f));
            int cargoCaptionRow = cargoValueRow + 1;

            if (liftCaptionRow > 0)
                canvas.TextCentered(0, liftCaptionRow, canvas.Columns, "LIFT THRUST", ScreenInk.Dim);
            canvas.TextCentered(0, Mathf.Max(1, liftValueRow), canvas.Columns, liftWord, ScreenInk.Accent);
            if (cargoValueRow < canvas.Rows)
                canvas.TextCentered(0, cargoValueRow, canvas.Columns, cargoWord, ScreenInk.Accent);
            if (cargoCaptionRow < canvas.Rows)
                canvas.TextCentered(0, cargoCaptionRow, canvas.Columns, "CARGO", ScreenInk.Dim);
            return Finish(canvas);
        }

        /// <summary>
        /// The hold, as ore: what came out of the ground and how much of it weighs on the
        /// lift. Ores only - components belong to the manifest, and a mining pilot is
        /// watching weight, not inventory. The amber family, with the mass at the right
        /// edge of every row.
        /// </summary>
        private string BuildMiningCargoPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            // Which holds? The ones this screen was pointed at. A cargo board that lists
            // the whole base because nothing was picked is showing data the player never
            // asked for, on every wall at once.
            if (!HasPickedSources)
            {
                NoSources(canvas, "MINING CARGO");
                return Finish(canvas);
            }
            int entries = CollectGridCargo(IsOre, out int usedSlots, out int capacitySlots, out float totalKg);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;

            int row = 0;
            canvas.TextClipped(0, row, "MINING CARGO", canvas.Columns, ScreenInk.Dim);
            row++;
            if (row < canvas.Rows)
            {
                canvas.TextClipped(0, row, "HOLD " + (fill * 100f).ToString("0") + "%", 12, ScreenInk.Primary);
                canvas.TextClipped(13, row, FormatMass(totalKg),
                    Mathf.Max(2, canvas.Columns - 13), ScreenInk.Accent);
                row++;
            }

            if (entries == 0)
            {
                ClearPageIcons();
                if (row < canvas.Rows) canvas.Text(0, row, "NO ORE ON BOARD", false, ScreenInk.Dim);
                return Finish(canvas);
            }

            int rowsLeft = Mathf.Max(0, canvas.Rows - row - 1);
            int shown = Mathf.Min(Mathf.Min(HeaviestItemCount, entries), rowsLeft);

            for (int i = 0; i < shown && row < canvas.Rows; i++)
            {
                var entry = s_massScratch[i];
                if (entry.item == null) continue;

                canvas.Icon(0, row, 2, 1, entry.item);
                string name = entry.item.displayName.ToUpperInvariant();
                string mass = FormatMass(entry.massKg);
                canvas.TextClipped(3, row, name, Mathf.Max(2, canvas.Columns - mass.Length - 5), ScreenInk.Primary);
                canvas.TextRightClipped(canvas.Columns - 1, row, mass, mass.Length + 2, ScreenInk.Accent);
                row++;
                if (row < canvas.Rows && i + 1 < shown)
                {
                    canvas.HLine(0, row, canvas.Columns, '\u2500', ScreenInk.Dim);
                    row++;
                }
            }

            if (entries > shown && row < canvas.Rows)
                canvas.TextClipped(0, row, "+" + (entries - shown) + " MORE ORES", canvas.Columns, ScreenInk.Dim);
            return Finish(canvas);
        }

        private static bool IsOre(ItemDefinition item)
            => item is ResourceItem resource && resource.subcategory == ResourceCategory.Raw;

        // ═════════════════════════════════════════════════════════════════════════
        // INVENTORY GRID (the reference's name / picture / count tiles)
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The hold as tiles: the item's name on top, its picture under the name, and how
        /// many there are under that. The fill bar across the top is optional - on a wall
        /// it is the "how full am I" glance; on a small panel it is one line too many.
        /// </summary>
        private string BuildInventoryGridPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            // Which holds? The ones this screen was pointed at. A cargo board that lists
            // the whole base because nothing was picked is showing data the player never
            // asked for, on every wall at once.
            if (!HasPickedSources)
            {
                NoSources(canvas, "INVENTORY GRID");
                return Finish(canvas);
            }
            int entries = CollectGridCargo(_ => true, out int usedSlots, out int capacitySlots, out float totalKg);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;

            if (entries == 0)
            {
                ClearPageIcons();
                canvas.Text(0, 0, "INVENTORY GRID", false, ScreenInk.Accent);
                if (canvas.Rows > 1) canvas.Text(0, 1, "HOLD EMPTY", false, ScreenInk.Dim);
                return Finish(canvas);
            }

            int row = 0;
            if (showInventoryFill)
            {
                // The top line: how full the hold is, as a number and a bar.
                string percent = (fill * 100f).ToString("0.0", CultureInfo.InvariantCulture);
                canvas.TextClipped(0, row, percent, percent.Length + 2, ScreenInk.Warn);
                int barX = percent.Length + 2;
                int barWidth = Mathf.Max(3, canvas.Columns - barX - 2);
                canvas.Bar(barX, row, barWidth, fill, '\u2588', '\u2591', ScreenInk.Warn);
                row++;
            }

            // Tiles: name over picture over count.
            const int cellWidth = 9;
            const int cellHeight = 4;
            int columns = Mathf.Max(1, canvas.Columns / cellWidth);
            int rowsAvailable = Mathf.Max(1, (canvas.Rows - row) / cellHeight);
            int capacity = columns * rowsAvailable;

            for (int i = 0; i < entries && i < capacity; i++)
            {
                var entry = s_massScratch[i];
                if (entry.item == null) continue;

                int cellColumn = i % columns;
                int cellRow = i / columns;
                int x = cellColumn * cellWidth;
                int y = row + cellRow * cellHeight;

                canvas.TextClipped(x, y, entry.item.displayName.ToUpperInvariant(), cellWidth - 1, ScreenInk.Primary);
                canvas.Icon(x, y + 1, 2, 2, entry.item);
                canvas.TextClipped(x, y + 3, FormatCount(entry.count), cellWidth - 1, ScreenInk.Ok);
            }

            if (entries > capacity && capacity > 0)
                canvas.TextClipped(0, canvas.Rows - 1,
                    "+" + (entries - capacity) + " MORE TYPES", canvas.Columns, ScreenInk.Dim);
            return Finish(canvas);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // STORAGE MONITOR (the reference's icon board)
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// What is in here, as pictures: every type with its icon, its name and its total,
        /// and - when the player wants it - its mass. The custom note sits bottom-left,
        /// the grid's own name bottom-right, and the name at the top is the custom
        /// heading or the container's. It is the "what have I got" board for a busy wall.
        /// </summary>
        private string BuildStorageMonitorPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            // Which holds? The ones this screen was pointed at. A cargo board that lists
            // the whole base because nothing was picked is showing data the player never
            // asked for, on every wall at once.
            if (!HasPickedSources)
            {
                NoSources(canvas, "STORAGE MONITOR");
                return Finish(canvas);
            }
            var selectedContainer = SingleSelectedCargoContainer();
            // The board is a picture board; the hold's fill belongs to the manifest and the
            // inventory grid, which is where the reference puts it too.
            int entries = CollectGridCargo(_ => true, out _, out _, out _, selectedContainer);

            // THE REFERENCE BOARD: a heading over a full-width rule, then a grid of tiles
            // that are a picture and a count - no names competing with the numbers, which
            // is what the reference does and what keeps every tile the same size. The
            // footer carries the player's own note and the construct's name.
            int row = 0;
            if (row < canvas.Rows)
            {
                string title = "STORAGE MONITOR";
                if (!string.IsNullOrWhiteSpace(manifestTitle))
                    title = manifestTitle.Trim().ToUpperInvariant();
                canvas.TextCentered(0, row++, canvas.Columns, title, ScreenInk.Accent);
            }
            if (row < canvas.Rows)
            {
                for (int x = 0; x < canvas.Columns; x++) canvas.Set(x, row, '\u2500', ScreenInk.Dim);
                row++;
            }

            if (entries == 0)
            {
                if (row < canvas.Rows) canvas.TextCentered(0, row, canvas.Columns, "EMPTY", ScreenInk.Dim);
                DrawStorageFooter(canvas);
                return Finish(canvas);
            }

            // TILES. One picture and its total per tile, laid out over the whole panel: the
            // tile size is the panel divided by however many columns and rows fit, so the
            // board fills the glass instead of stacking in one narrow column.
            int iconsPerTile = 2;
            int captionRows = showStorageWeight ? 2 : 1;
            int tileRows = iconsPerTile + captionRows;
            int bottom = canvas.Rows - 1;              // the footer

            int tileColumns = Mathf.Clamp(canvas.Columns / 7, 2, 8);
            int tileWidth = Mathf.Max(5, canvas.Columns / tileColumns);
            tileColumns = Mathf.Max(1, canvas.Columns / tileWidth);
            int gridRows = Mathf.Max(1, (bottom - row) / Mathf.Max(2, tileRows + 1));
            int capacity = tileColumns * gridRows;

            // If the panel has rows to spare, grow the tiles rather than leaving a band of
            // empty glass under the board - the reference's tiles are big.
            int spareRows = (bottom - row) - gridRows * (tileRows + 1);
            int extraIcon = Mathf.Clamp(spareRows / Mathf.Max(1, gridRows), 0, 2);

            for (int i = 0; i < entries && i < capacity; i++)
            {
                var entry = s_massScratch[i];
                if (entry.item == null) continue;

                int x = (i % tileColumns) * tileWidth;
                int y = row + (i / tileColumns) * (tileRows + 1 + extraIcon);

                canvas.Icon(x + Mathf.Max(0, (tileWidth - iconsPerTile) / 2), y, iconsPerTile,
                    iconsPerTile + extraIcon, entry.item);

                string count = FormatCount(entry.count);
                int captionRow = y + iconsPerTile + extraIcon;
                canvas.TextCentered(x, captionRow, tileWidth, count, ScreenInk.Primary);
                if (showStorageWeight && captionRow + 1 < canvas.Rows)
                    canvas.TextCentered(x, captionRow + 1, tileWidth, FormatMass(entry.massKg),
                        ScreenInk.Dim);
            }

            if (entries > capacity && capacity > 0 && bottom > row)
                canvas.TextCentered(0, bottom, canvas.Columns,
                    "+" + (entries - capacity) + " MORE TYPES", ScreenInk.Dim);

            DrawStorageFooter(canvas);
            return Finish(canvas);
        }

        /// <summary>The footer's two corners: the player's own note left, the construct's
        /// name right - the reference's exact furniture.</summary>
        private void DrawStorageFooter(ScreenCanvas canvas)
        {
            if (canvas.Rows < 1) return;
            int row = canvas.Rows - 1;
            string note = storageNote != null ? storageNote.Trim() : string.Empty;
            if (note.Length > 0)
                canvas.TextClipped(0, row, note, Mathf.Max(2, canvas.Columns / 2), ScreenInk.Dim);
            canvas.TextRightClipped(canvas.Columns - 1, row, GridEntityName(),
                Mathf.Max(2, canvas.Columns / 2), ScreenInk.Dim);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // PLANET MAP (the reference's chart-paper globe)
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The body below as a chart: green graph paper, a shaded globe with the body's
        /// name across it, the construct's own marker on the surface, the position along
        /// the top and the flying numbers along the bottom. A map page that does not move
        /// when the ship moves is a decoration; this one does.
        /// </summary>
        private string BuildPlanetMapPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();

            var body = GravityProvider.ActiveBody;
            if (body == null)
            {
                canvas.Text(0, 0, "PLANET MAP", false, ScreenInk.Accent);
                if (canvas.Rows > 1) canvas.Text(0, 1, "NO BODY IN RANGE", false, ScreenInk.Dim);
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

            bool roomy = canvas.Columns >= 20 && canvas.Rows >= 8;
            if (!roomy)
            {
                canvas.TextClipped(0, 0, body.DisplayName.ToUpperInvariant(), canvas.Columns, ScreenInk.Warn);
                if (canvas.Rows > 1)
                    canvas.TextClipped(0, 1, "LAT " + latitude.ToString("0", CultureInfo.InvariantCulture)
                        + "\u00b0  LON " + longitude.ToString("0", CultureInfo.InvariantCulture) + "\u00b0",
                        canvas.Columns, ScreenInk.Primary);
                if (canvas.Rows > 2)
                    canvas.TextClipped(0, 2, "ALT " + FormatDistance(altitude), canvas.Columns, ScreenInk.Primary);
                return Finish(canvas);
            }

            // The chart paper under everything: the reference draws its globe on bright
            // graph squares, and the grid is what makes the picture read as a map.
            for (int y = 0; y < canvas.Rows; y++)
            {
                for (int x = 0; x < canvas.Columns; x++)
                {
                    bool vertical = x % 3 == 0;
                    bool horizontal = y % 2 == 0;
                    if (vertical && horizontal) canvas.Set(x, y, '\u253c', ScreenInk.Ok);  // \u253c
                    else if (vertical) canvas.Set(x, y, '\u2502', ScreenInk.Ok);           // \u2502
                    else if (horizontal) canvas.Set(x, y, '\u2500', ScreenInk.Ok);         // \u2500
                }
            }

            // The reference's header line: the mode word, then the position in brackets.
            string coords = FormatCompact(arm.x) + ", " + FormatCompact(arm.y) + ", " + FormatCompact(arm.z);
            canvas.Text(0, 0, "FREE", false, ScreenInk.Primary);
            canvas.TextClipped(5, 0, "[" + coords + "]", canvas.Columns - 5, ScreenInk.Primary);

            // The globe fills the middle band: a flat cyan disc with a curved horizon
            // and a scatter of surface marks - the reference's picture, not a shaded
            // ball. The cell counts come from the fitted cell shape so the disc is a
            // circle on the glass, and its rim is supersampled in quadrants (each
            // cell tests its four quarters against the circle) so the edge is smooth
            // instead of a staircase.
            int discTop = 2;
            float aspect = CellAspect();
            // The disc takes the tallest circle that fits BOTH dimensions - the row count
            // on its own used to overflow into the header on a tall, narrow panel.
            int rows = Mathf.Max(3, canvas.Rows - 4);
            int widest = Mathf.Max(5, Mathf.RoundToInt((canvas.Columns - 4) * aspect));
            rows = Mathf.Min(rows, widest);
            int columns = Mathf.Clamp(Mathf.RoundToInt(rows / Mathf.Max(0.2f, aspect)), 5,
                canvas.Columns - 2);
            rows = Mathf.Max(3, Mathf.RoundToInt(columns * aspect));
            int offsetX = (canvas.Columns - columns) / 2;

            float rx = (columns - 1) * 0.5f;
            float ry = (rows - 1) * 0.5f;
            float cx = offsetX + rx;
            float cy = discTop + ry;
            for (int y = discTop; y < discTop + rows; y++)
            {
                for (int x = offsetX; x < offsetX + columns; x++)
                {
                    float u = (x - cx) / Mathf.Max(1f, rx);
                    float v = (y - cy) / Mathf.Max(1f, ry);
                    float du = 0.25f / Mathf.Max(1f, rx);
                    float dv = 0.25f / Mathf.Max(1f, ry);

                    int bits = 0;
                    if (Inside(u - du, v - dv)) bits |= 1;   // top-left
                    if (Inside(u + du, v - dv)) bits |= 2;   // top-right
                    if (Inside(u - du, v + dv)) bits |= 4;   // bottom-left
                    if (Inside(u + du, v + dv)) bits |= 8;   // bottom-right
                    if (bits == 0) continue;

                    if (bits != 15)
                    {
                        canvas.Set(x, y, QuadrantGlyphs[bits], ScreenInk.Accent);
                        continue;
                    }

                    // A full cell of the body: surface marks above the horizon curve,
                    // a change of texture below it.
                    bool belowHorizon = v > 0.34f - 0.22f * u * u;
                    bool speck = !belowHorizon && ((x * 7 + y * 13) % 17 == 0);
                    if (speck) canvas.Set(x, y, '\u2592', ScreenInk.Dim);
                    else if (belowHorizon) canvas.Set(x, y, '\u2593', ScreenInk.Accent);
                    else canvas.Set(x, y, '\u2588', ScreenInk.Accent);
                }
            }

            static bool Inside(float u, float v) => u * u + v * v <= 1f;

            // The body's name, across the upper globe, in the hot colour.
            string name = Trim(body.DisplayName.ToUpperInvariant(), Mathf.Max(4, columns - 4));
            int nameRow = discTop + Mathf.RoundToInt(rows * 0.36f);
            canvas.TextCentered(offsetX, nameRow, columns, name, ScreenInk.Warn);

            // The construct's marker on the disc, kept off the name.
            float markU = Mathf.Clamp(longitude / 180f, -1f, 1f);
            float markV = Mathf.Clamp(latitude / 90f, -1f, 1f);
            int markColumn = offsetX + Mathf.RoundToInt((markU + 1f) * 0.5f * (columns - 1));
            int markRow = discTop + Mathf.RoundToInt((1f - markV) * 0.5f * (rows - 1));
            int nameStart = offsetX + (columns - name.Length) / 2;
            if (markRow == nameRow && markColumn >= nameStart - 1
                && markColumn <= nameStart + name.Length
                && markRow + 1 < discTop + rows)
                markRow++;
            canvas.Set(markColumn, markRow, '\u25b2', ScreenInk.Primary);

            // The reference's footer: compact flight numbers, one line.
            float az = NormaliseLongitude(Vector3.SignedAngle(body.transform.forward,
                Vector3.ProjectOnPlane(here - centre, pole), pole));
            string footer = "FL:" + FormatCompact(altitude)
                + "  Alt:" + latitude.ToString("0", CultureInfo.InvariantCulture) + "\u00b0"
                + "  Az:" + az.ToString("0", CultureInfo.InvariantCulture) + "\u00b0"
                + "  R:" + FormatCompact(radius);
            canvas.Text(0, canvas.Rows - 1, footer, false, ScreenInk.Primary);
            return Finish(canvas);
        }

        /// <summary>Instrument shorthand for far distances: 4.1k, 262.1k, 5.8M - the way
        /// the reference board writes a flight line.</summary>
        private static string FormatCompact(float value)
        {
            float a = Mathf.Abs(value);
            string sign = value < 0f ? "-" : "";
            if (a >= 1_000_000f)
                return sign + (a / 1_000_000f).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (a >= 1000f)
                return sign + (a / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return sign + a.ToString("0", CultureInfo.InvariantCulture);
        }

        private static float NormaliseLongitude(float degrees)
        {
            while (degrees > 180f) degrees -= 360f;
            while (degrees < -180f) degrees += 360f;
            return degrees;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // SPEED (the flight page)
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// What a pilot reads while flying: how fast, how fast vertically, where the
        /// velocity is going, and where the thrust is being spent. Values end at the right
        /// edge; the thrust bars are the length their labels need, not the length of the
        /// glass.
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

            int row = 0;
            row = EnvRow(canvas, row, "SPEED", speed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s");
            row = EnvRow(canvas, row, "VERT", (vertical >= 0f ? "+" : "-")
                + Mathf.Abs(vertical).ToString("0.0", CultureInfo.InvariantCulture) + " m/s");
            row = EnvRow(canvas, row, "FWD", (forward >= 0f ? "+" : "-")
                + Mathf.Abs(forward).ToString("0.0", CultureInfo.InvariantCulture) + " m/s");

            if (row < canvas.Rows && Grid != null)
            {
                row++;
                if (row < canvas.Rows)
                {
                    canvas.TextClipped(0, row, "THRUST", canvas.Columns, ScreenInk.Dim);
                    row++;
                }
                var (fwd, back, right, left, up, down) = Grid.GetThrustByDirection();
                float thrustMax = Mathf.Max(1f, Mathf.Max(fwd + back, Mathf.Max(up + down, right + left)));
                row = DrawThrustBar(canvas, row, "FWD", fwd, thrustMax);
                row = DrawThrustBar(canvas, row, "UP", up, thrustMax);
                row = DrawThrustBar(canvas, row, "SIDE", right + left, thrustMax);
            }
            return Finish(canvas);
        }

        private static int DrawThrustBar(ScreenCanvas canvas, int row, string label, float value, float max)
        {
            if (row >= canvas.Rows) return row;
            int barWidth = Mathf.Clamp(canvas.Columns / 3, 3, 12);
            float value01 = Mathf.Clamp01(value / Mathf.Max(1f, max));
            string percent = (value01 * 100f).ToString("0") + "%";

            canvas.TextClipped(0, row, label, 6, ScreenInk.Dim);
            canvas.Bar(6, row, barWidth, value01, '\u2588', '\u2591', ScreenInk.Accent);
            canvas.TextClipped(7 + barWidth, row, percent,
                Mathf.Max(2, canvas.Columns - 7 - barWidth), ScreenInk.Primary);
            return row + 1;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // AUTO-RUN PILOT + ROUTE RECORDER
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The pilot's own page: what run it is on, how far is left of it, when it lands,
        /// and whether the batteries and the tank can pay for it. The numbers a captain
        /// wants before going to bed mid-voyage.
        /// </summary>
        private string BuildAutoRunPilotPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            var pilot = FindAutoRunPilot();
            if (pilot == null)
            {
                canvas.Text(0, 0, "AUTO-RUN PILOT", false, ScreenInk.Accent);
                if (canvas.Rows > 1) canvas.TextClipped(0, 1, "NO PILOT ON THIS CONSTRUCT", canvas.Columns, ScreenInk.Dim);
                return Finish(canvas);
            }

            int row = 0;
            string status = AutoRunStatus();
            canvas.TextClipped(0, row, "STATUS", 8, ScreenInk.Dim);
            bool active = status != "STANDBY" && !status.StartsWith("Select");
            canvas.TextClipped(9, row++, status, canvas.Columns - 9,
                active ? ScreenInk.Ok : ScreenInk.Dim);

            var book = Grid.GetComponent<VoxelEngine.Navigation.RouteBook>();
            if (book == null)
            {
                canvas.TextClipped(0, row, "NO ROUTE BOOK", canvas.Columns, ScreenInk.Warn);
                return Finish(canvas);
            }
            if (book.IsRecording)
            {
                canvas.TextClipped(0, row, "RECORDING  " + (book.Draft != null ? book.Draft.routeName : "NEW ROUTE"),
                    canvas.Columns, ScreenInk.Warn);
                return Finish(canvas);
            }

            var route = book.Find(pilot.selectedRouteName);
            if (route == null && book.Count > 0) route = book.Routes[0];
            if (route == null)
            {
                canvas.TextClipped(0, row, "NO SAVED ROUTES", canvas.Columns, ScreenInk.Dim);
                return Finish(canvas);
            }

            var plan = pilot.Plan;
            row = DrawRouteRows(canvas, row, route, plan);
            return Finish(canvas);
        }

        /// <summary>
        /// The recorder's own page: what it is doing, the run it has open, and the same
        /// cost table - because a route that cannot pay for itself is a plan, not a run.
        /// </summary>
        private string BuildRouteRecorderPage()
        {
            var canvas = NewCanvas();
            ClearPageIcons();
            var recorder = FindRouteRecorder();
            if (recorder == null)
            {
                canvas.Text(0, 0, "ROUTE RECORDER", false, ScreenInk.Accent);
                if (canvas.Rows > 1) canvas.TextClipped(0, 1, "NO RECORDER ON THIS CONSTRUCT", canvas.Columns, ScreenInk.Dim);
                return Finish(canvas);
            }

            var book = Grid.GetComponent<VoxelEngine.Navigation.RouteBook>();
            int row = 0;
            if (book == null)
            {
                canvas.TextClipped(0, row, "NO ROUTE BOOK", canvas.Columns, ScreenInk.Warn);
                return Finish(canvas);
            }

            canvas.TextClipped(0, row, "STATE", 8, ScreenInk.Dim);
            canvas.TextClipped(9, row++,
                book.IsRecording ? "RECORDING" : "READY  x" + book.Count,
                canvas.Columns - 9, book.IsRecording ? ScreenInk.Warn : ScreenInk.Ok);

            if (book.IsRecording && book.Draft != null)
            {
                canvas.TextClipped(0, row++, "DRAFT  " + book.Draft.routeName, canvas.Columns, ScreenInk.Primary);
                canvas.TextClipped(0, row++, "MODE  " + RouteModeLabel(book.Draft.travelMode)
                    + "  x " + book.Draft.waypoints.Count, canvas.Columns, ScreenInk.Dim);
                return Finish(canvas);
            }

            var route = book.Find(recorder.selectedRouteName);
            if (route == null && book.Count > 0) route = book.Routes[0];
            if (route == null)
            {
                canvas.TextClipped(0, row, "NO SAVED ROUTES", canvas.Columns, ScreenInk.Dim);
                return Finish(canvas);
            }

            row = DrawRouteRows(canvas, row, route, recorder.Plan);
            return Finish(canvas);
        }

        /// <summary>
        /// The shared route table: the run, how far is left of it, when it lands, where it
        /// is happening, and the two stores it spends - watts and hydrogen - each against
        /// what the construct has.
        /// </summary>
        private int DrawRouteRows(ScreenCanvas canvas, int row, VoxelEngine.Navigation.ShipRoute route,
            VoxelEngine.Navigation.RoutePlan plan)
        {
            canvas.TextClipped(0, row, "ROUTE", 8, ScreenInk.Dim);
            canvas.TextClipped(9, row++, Trim(route.routeName, canvas.Columns - 9),
                canvas.Columns - 9, ScreenInk.Accent);

            // Distance: how far is left of the whole, when a run is live.
            float totalKm = plan.IsValid ? plan.TotalDistanceKm : 0f;
            float leftKm = EstimateRemainingKm(route);
            if (totalKm > 0.01f && leftKm >= 0f)
            {
                canvas.TextClipped(0, row, "DIST", 8, ScreenInk.Dim);
                canvas.TextClipped(9, row++,
                    leftKm.ToString("0.#", CultureInfo.InvariantCulture) + " / "
                    + totalKm.ToString("0.#", CultureInfo.InvariantCulture) + " km",
                    canvas.Columns - 9, ScreenInk.Primary);
            }
            else if (totalKm > 0.01f)
            {
                row = EnvRow(canvas, row, "DIST", totalKm.ToString("0.#", CultureInfo.InvariantCulture) + " km");
            }

            if (plan.IsValid && row < canvas.Rows)
            {
                float etaSeconds = totalKm > 0.01f && leftKm >= 0f
                    ? plan.TotalSeconds * Mathf.Clamp01(leftKm / Mathf.Max(0.01f, totalKm))
                    : plan.TotalSeconds;
                row = EnvRow(canvas, row, "ETA", VoxelEngine.Navigation.GridRouteRecorder.FormatDuration(etaSeconds));
            }

            if (row < canvas.Rows)
            {
                string place = GravityProvider.ActiveBody != null
                    ? GravityProvider.ActiveBody.DisplayName.ToUpperInvariant() : "SPACE";
                float speed = Grid != null && Grid.Body != null ? Grid.Body.linearVelocity.magnitude : 0f;
                canvas.TextClipped(0, row, "SPEED", 8, ScreenInk.Dim);
                string speedText = speed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s";
                canvas.TextClipped(9, row, speedText,
                    Mathf.Max(4, (canvas.Columns - 9) / 2), ScreenInk.Primary);
                canvas.TextClipped(9 + speedText.Length + 2, row, place,
                    Mathf.Max(2, canvas.Columns - 11 - speedText.Length), ScreenInk.Ok);
                row++;
            }

            if (plan.IsValid)
            {
                // The two stores: watts and hydrogen, needed against held.
                row = ResourceRow(canvas, row, "POWER",
                    plan.TotalWattHours, plan.StoredEnergyWh, FormatWattHours);
                if (row < canvas.Rows && plan.HydrogenLitres > 0.5f)
                    row = ResourceRow(canvas, row, "H2",
                        plan.HydrogenLitres, Grid != null ? Grid.HydrogenStored : 0f, FormatVolume);
                if (row < canvas.Rows)
                {
                    int marginPercent = Mathf.RoundToInt(plan.ReserveMargin01 * 100f);
                    canvas.TextClipped(0, row, "RESERVE", 8, ScreenInk.Dim);
                    canvas.TextClipped(9, row++,
                        marginPercent + "%", canvas.Columns - 9,
                        plan.ReserveMargin01 >= VoxelEngine.Navigation.RouteRules.MinimumReserve01
                            ? ScreenInk.Ok : ScreenInk.Warn);
                }
                if (row < canvas.Rows)
                {
                    bool warn = plan.Warnings != null && plan.Warnings.Count > 0;
                    canvas.TextClipped(0, row, warn ? "WARNINGS " + plan.Warnings.Count : "ROUTE CLEAR",
                        canvas.Columns, warn ? ScreenInk.Warn : ScreenInk.Dim);
                }
            }
            else if (row < canvas.Rows)
            {
                canvas.TextClipped(0, row, "ROUTE CANNOT BE COSTED", canvas.Columns, ScreenInk.Warn);
            }
            return row;
        }

        /// <summary>NEED x / HELD y with a bar of the held against the need.</summary>
        private static int ResourceRow(ScreenCanvas canvas, int row, string label,
            float needed, float held, System.Func<float, string> format)
        {
            if (row >= canvas.Rows) return row;
            canvas.TextClipped(0, row, label, 8, ScreenInk.Dim);
            int barWidth = Mathf.Clamp(canvas.Columns / 4, 3, 10);
            float ratio = needed > 0.01f ? Mathf.Clamp01(held / needed) : 1f;
            canvas.Bar(8, row, barWidth, ratio, '\u2588', '\u2591', ratio >= 1f ? ScreenInk.Ok : ScreenInk.Warn);
            canvas.TextClipped(9 + barWidth, row,
                format(needed) + " / " + format(held),
                Mathf.Max(2, canvas.Columns - 9 - barWidth), ScreenInk.Primary);
            return row + 1;
        }

        /// <summary>
        /// How far is left of the run: the legs from the nearest waypoint to the end, or
        /// straight-line distance to the end when the run is not live. Returns -1 when the
        /// route has no measurable geometry.
        /// </summary>
        private float EstimateRemainingKm(VoxelEngine.Navigation.ShipRoute route)
        {
            if (route == null || route.waypoints == null || route.waypoints.Count == 0) return -1f;
            var registry = CosmicRegistry.Instance;
            var origin = SpaceOrigin.Instance;
            if (registry == null || origin == null || !registry.IsReady) return -1f;

            var hereKm = origin.GetCosmicKm(transform.position);

            // The nearest waypoint is where the pilot last was; everything after it is
            // still to fly.
            int nearest = 0;
            double nearestSq = double.MaxValue;
            for (int i = 0; i < route.waypoints.Count; i++)
            {
                var resolved = route.waypoints[i].ResolvedPositionKm(registry);
                double dx = resolved.x - hereKm.x;
                double dy = resolved.y - hereKm.y;
                double dz = resolved.z - hereKm.z;
                double sq = dx * dx + dy * dy + dz * dz;
                if (sq < nearestSq) { nearestSq = sq; nearest = i; }
            }

            double remaining = 0.0;
            for (int i = nearest; i + 1 < route.waypoints.Count; i++)
            {
                var a = route.waypoints[i].ResolvedPositionKm(registry);
                var b = route.waypoints[i + 1].ResolvedPositionKm(registry);
                double dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z;
                remaining += System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            return (float)remaining;
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
                if (block is VoxelEngine.Navigation.GridRouteRecorder recorder
                    && block is not IndustrialWorld.Navigation.AutoRunPilot) return recorder;
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

        // ═════════════════════════════════════════════════════════════════════════
        // Shared snapshot helpers
        // ═════════════════════════════════════════════════════════════════════════

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

        private int CountLinkedCargoContainers()
        {
            int count = 0;
            var providers = ResolveAllProviders();
            for (int i = 0; i < providers.Count; i++)
                if (providers[i] is GridCargoContainer) count++;
            return count;
        }

        /// <summary>
        /// The manifest's filing: raw material out of the ground, what the furnaces and
        /// assemblers made, blocks and tools and ammo as their own categories, and the
        /// rest filed under MISC or OTHER. Armor blocks are blocks because the item's own
        /// category says so - never because a name guess said so.
        /// </summary>
        private static string ManifestCategory(ItemDefinition item)
        {
            if (item is ResourceItem resource)
            {
                switch (resource.subcategory)
                {
                    case ResourceCategory.Raw: return "RAW MATERIALS";
                    case ResourceCategory.Ingot: return "INGOTS";
                    case ResourceCategory.Component: return "COMPONENTS";
                    case ResourceCategory.Fuel: return "FUEL";
                    case ResourceCategory.Misc: return "MISC";
                }
            }

            string category = item != null ? item.category : null;
            if (string.IsNullOrWhiteSpace(category)) return "OTHER";
            return category.Trim().ToUpperInvariant();
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
            if (Grid == null) return 0;

            // WHICH HOLDS THIS PAGE IS ABOUT.
            //
            // A manifest, a storage monitor or an inventory board is a board about
            // something: the holds the player picked as this screen's data sources. The
            // old rule fell back to every hold on the construct, so a screen set up for
            // one chest listed the whole base and no number on it was about the chest.
            // An explicit container wins, then the picked ones, and only a screen with
            // no hold among its sources reports the whole construct.
            _cargoScope.Clear();
            _cargoWalk.Clear();
            if (onlyContainer != null)
            {
                _cargoWalk.Add(onlyContainer);
            }
            else
            {
                var providers = ResolveAllProviders();
                for (int i = 0; i < providers.Count; i++)
                {
                    if (BlockOf(providers[i]) is not GridCargoContainer picked) continue;
                    if (!_cargoScope.Contains(picked)) _cargoScope.Add(picked);
                }
                if (_cargoScope.Count > 0)
                {
                    for (int i = 0; i < _cargoScope.Count; i++) _cargoWalk.Add(_cargoScope[i]);
                }
                else
                {
                    foreach (var block in Grid.AllBlocks)
                        if (block is GridCargoContainer all) _cargoWalk.Add(all);
                }
            }

            for (int walk = 0; walk < _cargoWalk.Count; walk++)
            {
                var cargo = _cargoWalk[walk];
                if (cargo == null || cargo.container == null) continue;
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

        // ── Farm scan ───────────────────────────────────────────────────────────

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

        // ── Layout ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The character grid a page draws on: the panel's line count from its size class,
        /// and as many columns as share those cells at the font's own proportions. A page
        /// written against this grid is the same page on a Small panel and an ExtraLarge
        /// one - it just has fewer cells to work with.
        /// </summary>
        private (int columns, int rows) PageGrid()
        {
            var (width, height) = GlassSize();
            int rows = MaxDisplayLines;

            // The grid's SHAPE has to be the glass's shape, or the fit has to throw
            // cells away: a grid wider than the panel is scaled until its width fits and
            // the page comes out short, a grid taller than the panel goes the other way.
            // The cell's own width over height is measured from the rendered glyphs
            // (CellAspect), so the columns are however many of THOSE cells the panel is
            // wide - which is the number that makes a full page fill the glass.
            float aspect = CellAspect();
            float boxAspect = (width * 0.96f) / Mathf.Max(0.001f, height * 0.90f);
            int columns = Mathf.Clamp(Mathf.RoundToInt(rows * boxAspect / Mathf.Max(0.2f, aspect)),
                12, 96);
            return (columns, rows);
        }

        /// <summary>The rows a page gets while split between panes.</summary>
        private int PageGridRows() => MaxDisplayLines;

        private ScreenCanvas NewCanvas()
        {
            // While a split panel is composing, a page gets the pane's own grid instead of
            // the whole glass. No page builder knows this happened.
            if (_splitTarget != null) return new ScreenCanvas(_splitRect.width, _splitRect.height);

            var grid = PageGrid();
            return new ScreenCanvas(grid.columns, grid.rows);
        }

        /// <summary>
        /// Publish a page: every ink layer goes to the block for stacking, the merged text
        /// goes back to the caller (preview and legacy readers), and the pictures are
        /// handed over with the rows they were drawn on.
        /// </summary>
        private string Finish(ScreenCanvas canvas)
        {
            if (_splitTarget != null)
            {
                _splitTarget.Blit(canvas, _splitRect.x, _splitRect.y, _splitRect.width, _splitRect.height);
                _splitTarget.AddIcons(canvas.Icons, _splitRect.x, _splitRect.y);
                return string.Empty;
            }

            for (int i = 0; i < _inkTexts.Length; i++)
                _inkTexts[i] = canvas.BuildLayer((ScreenInk)i);
            _pageInksPublished = true;

            string text = canvas.Build();
            // Icons are mapped onto the block that was actually printed - its own rows and
            // its own columns - because that is the rectangle the fit scales onto the glass.
            // Mapping them onto the whole canvas put every picture off its cell whenever a
            // page did not print all the way to the last row or column.
            int rows = Mathf.Max(canvas.MeasurePrintedRows(), 1);
            int columns = Mathf.Max(canvas.MeasurePrintedColumns(), 1);
            SetPageIcons(canvas.Icons, columns, rows);
            return text;
        }

        /// <summary>Pages that say nothing with pictures clear any left over from a previous
        /// page - a stale icon floating over a graph is worse than no icon at all.</summary>
        private void ClearPageIcons() => SetPageIcons(null, 1, 1);

        // ── Shared formatting ───────────────────────────────────────────────────
        //
        // Every number on every page goes through these, and every one of them speaks
        // Invariant: a kilogram is "935.96 kg" with a dot on every machine in the world,
        // because instruments do not change their decimals when the ship sails.

        private static string FormatCount(int count)
        {
            if (count >= 1_000_000) return (count / 1_000_000f).ToString("0.##", CultureInfo.InvariantCulture) + "M";
            if (count >= 1000) return (count / 1000f).ToString("0.##", CultureInfo.InvariantCulture) + "k";
            return count.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatMass(float kg)
        {
            if (kg >= 1_000_000f) return (kg / 1_000_000f).ToString("0.##", CultureInfo.InvariantCulture) + " kt";
            if (kg >= 1000f) return (kg / 1000f).ToString("0.##", CultureInfo.InvariantCulture) + " t";
            if (kg >= 1f) return kg.ToString("0.#", CultureInfo.InvariantCulture) + " kg";
            return (kg * 1000f).ToString("0", CultureInfo.InvariantCulture) + " g";
        }

        private static string FormatWatts(float watts)
        {
            float abs = Mathf.Abs(watts);
            if (abs >= 1000000f) return (watts / 1000000f).ToString("0.##", CultureInfo.InvariantCulture) + " MW";
            if (abs >= 1000f) return (watts / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + " kW";
            return watts.ToString("0", CultureInfo.InvariantCulture) + " W";
        }

        private static string FormatWattHours(float wh)
        {
            if (wh >= 1_000_000f) return (wh / 1_000_000f).ToString("0.##", CultureInfo.InvariantCulture) + " MWh";
            if (wh >= 1000f) return (wh / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + " kWh";
            return wh.ToString("0", CultureInfo.InvariantCulture) + " Wh";
        }

        private static string FormatVolume(float litres)
        {
            if (litres >= 1_000_000f) return (litres / 1_000_000f).ToString("0.##", CultureInfo.InvariantCulture) + " Ml";
            if (litres >= 1000f) return (litres / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + " kl";
            return litres.ToString("0", CultureInfo.InvariantCulture) + " L";
        }

        private static string FormatDistance(float metres)
        {
            float abs = Mathf.Abs(metres);
            if (abs >= 1_000_000f) return (metres / 1_000_000f).ToString("0.##", CultureInfo.InvariantCulture) + " Mm";
            if (abs >= 1000f) return (metres / 1000f).ToString("0.##", CultureInfo.InvariantCulture) + " km";
            return metres.ToString("0", CultureInfo.InvariantCulture) + " m";
        }

        private static string SignedWatts(float watts)
            => (watts >= 0f ? "+" : "-") + FormatWatts(Mathf.Abs(watts));

        private static float ExtractPercentage(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1f;
            int idx = text.IndexOf('%');
            if (idx < 1) return -1f;
            int start = idx - 1;
            while (start >= 0 && (char.IsDigit(text[start]) || text[start] == '.' || text[start] == ',')) start--;
            start++;
            if (start >= idx) return -1f;
            string number = text.Substring(start, idx - start).Replace(',', '.');
            if (float.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out float val))
                return Mathf.Clamp01(val / 100f);
            return -1f;
        }

        private static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (max <= 1) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max - 1) + "\u2026";
        }

        private string GridEntityName()
        {
            if (Grid == null) return "CONSTRUCT";
            // GridEntity is a MonoBehaviour, so its own name is the construct's name - the
            // same string the inspector and the fleet list show.
            string name = Grid.name;
            return string.IsNullOrEmpty(name) ? "CONSTRUCT" : name;
        }
    }
}
