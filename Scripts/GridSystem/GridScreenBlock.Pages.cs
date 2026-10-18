// Assets/Scripts/VoxelEngine/GridSystem/GridScreenBlock.Pages.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║           THE NEW SCREEN PAGES (17.35.0-dev)                     ║
// ║                                                                  ║
// ║  A separate half of GridScreenBlock on purpose. The original     ║
// ║  file is about how a screen is BUILT (frame, glow strip, camera  ║
// ║  quad, TextMesh); this one is about what it SAYS. Seven new      ║
// ║  pages live here, each one a pure formatter over a snapshot of   ║
// ║  the construct it is bolted to:                                  ║
// ║                                                                  ║
// ║    ENVIRONMENT  pressure, gravity, temperature, altitude         ║
// ║    BATTERIES    every battery on the construct, stored/capacity  ║
// ║    POWER GRAPH  the last few minutes as a live trace             ║
// ║    MANIFEST     every item on the construct, heaviest first      ║
// ║    FILL BARS    one percentage bar per linked container          ║
// ║    FARM         every farm plot near the screen                  ║
// ║    MINING       lift against weight, hold fill, heaviest cargo   ║
// ║    INVENTORY    the hold as a grid of names and counts           ║
// ║    PLANET MAP   the body below as a disc, construct marked       ║
// ║                                                                  ║
// ║  Everything here is read-only: a page never mutates the ship or  ║
// ║  the world, which is what keeps it honest in multiplayer (a      ║
// ║  screen is a display, and a display that writes would need an    ║
// ║  authority story it does not have).                              ║
// ║                                                                  ║
// ║  Cost: the snapshots walk Grid.AllBlocks, so none of them run    ║
// ║  per frame. Each page's text is cached and rebuilt on a timer    ║
// ║  (ScreenPageRefreshSeconds) or when the page changes, and the    ║
// ║  farm scan is even slower because it touches the scene.          ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Farming;
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
        /// <summary>A farm page only lists plots this close to the panel; a wall of screens on
        /// one base should not all report the same field three kilometres away.</summary>
        private const float FarmScanRadiusMetres = 24f;
        /// <summary>How many heaviest items a manifest or mining page names.</summary>
        private const int HeaviestItemCount = 6;
        /// <summary>Power history: 90 samples at this cadence is the last 3 minutes.</summary>
        private const float PowerSampleSeconds = 2f;
        private const int PowerHistoryLength = 90;

        // ── Cached page state ────────────────────────────────────────────────────
        private string _cachedPageText;
        private ScreenDataMode _cachedPageMode = (ScreenDataMode)(-1);
        private float _pageBuiltAt = -999f;
        private float _farmScannedAt = -999f;
        private readonly List<FarmPlot> _farmScratch = new(16);
        private readonly List<FarmPlot> _farmFound = new(16);

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
        /// The text for one of the new pages, cached. Returns null when
        /// <paramref name="mode"/> is not a page this half owns, so the caller can fall through
        /// to its own formatters without the two ever disagreeing about who renders what.
        /// </summary>
        private string RenderManagedPage(ScreenDataMode mode)
        {
            if (!IsManagedPage(mode)) return null;

            // Time-stamped rather than a countdown: FormattedDisplay is a property, and a
            // screen that happens to be read twice in one frame must not age the cache twice.
            bool stale = mode != _cachedPageMode
                      || _cachedPageText == null
                      || Time.unscaledTime - _pageBuiltAt >= ScreenPageRefreshSeconds;
            if (!stale) return _cachedPageText;

            _cachedPageMode = mode;
            _pageBuiltAt = Time.unscaledTime;
            _cachedPageText = BuildPage(mode);
            return _cachedPageText;
        }

        /// <summary>True for the pages this half of the class renders.</summary>
        private static bool IsManagedPage(ScreenDataMode mode) => mode switch
        {
            ScreenDataMode.Environment or ScreenDataMode.BatteryBank or ScreenDataMode.PowerGraph
                or ScreenDataMode.Manifest or ScreenDataMode.FillBars or ScreenDataMode.Farm
                or ScreenDataMode.Mining or ScreenDataMode.InventoryGrid
                or ScreenDataMode.PlanetMap => true,
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
                ScreenDataMode.InventoryGrid => BuildInventoryGridPage(),
                ScreenDataMode.PlanetMap => BuildPlanetMapPage(),
                _ => "NO PAGE",
            };
        }

        // ── ENVIRONMENT ─────────────────────────────────────────────────────────
        /// <summary>
        /// Pressure, gravity, temperature, altitude - the four numbers a player needs before
        /// they open a door. Pressure reads the SEALED ROOM this screen is in when there is
        /// one (that is what a room gauge means) and the planet's ambient atmosphere
        /// otherwise, and it says which of the two it is reading.
        /// </summary>
        private string BuildEnvironmentPage()
        {
            var sb = new StringBuilder(160);
            Vector3 here = transform.position;

            // Pressure: the room if the screen is inside one, else the ambient air.
            GridRoom room = null;
            var pressure = GridPressureSystem.For(Grid);
            if (pressure != null) room = pressure.RoomAtWorld(here);

            float pressureAtm = room != null
                ? room.PressureAtm
                : PressureRules.SampleAmbient(here).PressureAtm;
            string where = room != null ? "SEALED ROOM" : "AMBIENT";
            sb.Append("PRESSURE ").Append(pressureAtm.ToString("0.00")).Append(" atm")
              .Append(" (").Append(where).Append(")\n");
            sb.Append("  ").Append(UnitsBar01(Mathf.Clamp01(pressureAtm / Mathf.Max(0.01f, NominalPressureAtm()))))
              .Append('\n');

            // Gravity: what the body is actually pulling with here, in m/s^2 and in g.
            Vector3 gravity = GravityProvider.GetGravity(here);
            float g = gravity.magnitude;
            sb.Append("GRAVITY ").Append(g.ToString("0.00")).Append(" m/s2")
              .Append(" (").Append((g / 9.81f).ToString("0.00")).Append(" g)\n");

            // Temperature: the room's air if sealed, otherwise the exterior the thermal
            // system already reports for this construct.
            float temperature = room != null ? room.AirTemperatureC : ExteriorTemperatureC();
            sb.Append("TEMP ").Append(temperature.ToString("0.0")).Append(" C\n");

            // Altitude above the mean surface of the body below, when there is one.
            sb.Append("ALT ").Append(AltitudeLine());
            return sb.ToString();
        }

        private static float NominalPressureAtm() => PressureRules.NominalPressureAtm;

        private float ExteriorTemperatureC()
        {
            var weather = VoxelEngine.Weather.WeatherManager.Instance;
            if (weather != null && weather.IsWeatherActive) return weather.CurrentTemperature;
            return ThermalRules.FallbackAmbientC;
        }

        private string AltitudeLine()
        {
            var body = GravityProvider.ActiveBody;
            if (body == null) return "DEEP SPACE";
            float altitude = Vector3.Distance(transform.position, body.transform.position) - body.SurfaceRadius;
            return FormatDistance(altitude) + " above surface";
        }

        // ── BATTERY BANK ────────────────────────────────────────────────────────
        /// <summary>
        /// Every battery on the construct, one bar each, plus the bank total. The bar count is
        /// capped so a hundred-battery freighter still fits on a Medium panel: the header
        /// always carries the true count and total, the bars are the readable sample.
        /// </summary>
        private string BuildBatteryBankPage()
        {
            int count = 0;
            float stored = 0f;
            float capacity = 0f;
            var sb = new StringBuilder(256);

            int perRow = Mathf.Clamp(CharsPerLine() / 7, 1, 8);
            int rows = Mathf.Max(1, MaxDisplayLines - 2);
            int maxBars = rows * perRow;
            int drawn = 0;

            foreach (var block in Grid.AllBlocks)
            {
                if (block is not GridBattery battery) continue;
                count++;
                stored += Mathf.Max(0f, battery.storedWh);
                capacity += Mathf.Max(0f, battery.capacityWh);
                if (drawn >= maxBars) continue;

                if (drawn % perRow == 0) { if (drawn > 0) sb.Append('\n'); }
                sb.Append(BatteryCell(battery.Fill01)).Append(' ');
                drawn++;
            }

            if (count == 0) return "BATTERIES\nNONE ON THIS CONSTRUCT";

            float percent = capacity > 0f ? stored / capacity : 0f;
            var header = new StringBuilder(96);
            header.Append("BATTERIES x").Append(count)
                  .Append("  ").Append(FormatWattHours(stored))
                  .Append(" / ").Append(FormatWattHours(capacity)).Append('\n');
            header.Append(UnitsBar01(percent)).Append(' ')
                  .Append((percent * 100f).ToString("0")).Append("%\n");
            if (count > drawn)
                header.Append('[').Append(drawn).Append(" of ").Append(count).Append(" shown]\n");
            return header.ToString() + sb.ToString().TrimEnd();
        }

        /// <summary>A single battery as a five-step cell: full blocks, empty outline.</summary>
        private static string BatteryCell(float fill01)
        {
            int full = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * 4f), 0, 4);
            var sb = new StringBuilder(6);
            sb.Append('[');
            for (int i = 0; i < 4; i++) sb.Append(i < full ? '\u2588' : '\u2591');
            sb.Append(']');
            return sb.ToString();
        }

        // ── POWER GRAPH ─────────────────────────────────────────────────────────
        /// <summary>
        /// Generation and draw over the last three minutes, drawn as two sparklines on the
        /// screen's own block-character grid, with the high-water mark of each. This is the
        /// one page that keeps state: a history has to be accumulated, so it is sampled here
        /// rather than computed on demand.
        /// </summary>
        private string BuildPowerGraphPage()
        {
            SamplePowerHistory();

            float gainNow = _powerGainHistory.Count > 0 ? _powerGainHistory[_powerGainHistory.Count - 1] : 0f;
            float lossNow = _powerLossHistory.Count > 0 ? _powerLossHistory[_powerLossHistory.Count - 1] : 0f;
            float gainPeak = Peak(_powerGainHistory);
            float lossPeak = Peak(_powerLossHistory);
            float scale = Mathf.Max(1f, Mathf.Max(gainPeak, lossPeak));

            int width = Mathf.Clamp(CharsPerLine() - 2, 10, 60);
            var sb = new StringBuilder(320);
            sb.Append("POWER  ").Append(FormatWatts(gainNow - lossNow))
              .Append(Grid.HasPower ? "  STABLE" : "  DEFICIT").Append('\n');
            sb.Append("GEN ").Append(FormatWatts(gainNow))
              .Append("  PEAK ").Append(FormatWatts(gainPeak)).Append('\n');
            sb.Append(Sparkline(_powerGainHistory, scale, width)).Append('\n');
            sb.Append("USE ").Append(FormatWatts(lossNow))
              .Append("  PEAK ").Append(FormatWatts(lossPeak)).Append('\n');
            sb.Append(Sparkline(_powerLossHistory, scale, width));
            return sb.ToString();
        }

        private void SamplePowerHistory()
        {
            if (_powerGainHistory.Count > 0
                && Time.unscaledTime - _powerSampledAt < PowerSampleSeconds) return;
            _powerSampledAt = Time.unscaledTime;

            Push(_powerGainHistory, Mathf.Max(0f, Grid.PowerGenerated));
            Push(_powerLossHistory, Mathf.Max(0f, Grid.PowerConsumed));

            // A page that has been off for an hour should not replay an hour of nothing:
            // if the construct is idle the trace flattens, which is the truth.
            static void Push(List<float> list, float value)
            {
                list.Add(value);
                if (list.Count > PowerHistoryLength) list.RemoveAt(0);
            }
        }

        private static float Peak(List<float> values)
        {
            float peak = 0f;
            for (int i = 0; i < values.Count; i++) if (values[i] > peak) peak = values[i];
            return peak;
        }

        /// <summary>A sparkline over block characters, resampled to <paramref name="width"/>.</summary>
        private static string Sparkline(List<float> values, float scale, int width)
        {
            if (values.Count == 0) return new string('\u2591', Mathf.Max(1, width));
            const string ramp = "\u2581\u2582\u2583\u2584\u2585\u2586\u2587\u2588";
            var sb = new StringBuilder(width);
            for (int x = 0; x < width; x++)
            {
                int index = Mathf.Clamp(Mathf.FloorToInt(x * (values.Count - 1f) / Mathf.Max(1, width - 1)), 0, values.Count - 1);
                float v = Mathf.Clamp01(values[index] / scale);
                int step = Mathf.Clamp(Mathf.FloorToInt(v * (ramp.Length - 1) + 0.5f), 0, ramp.Length - 1);
                sb.Append(ramp[step]);
            }
            return sb.ToString();
        }

        // ── MANIFEST ────────────────────────────────────────────────────────────
        /// <summary>
        /// Every item on the construct, aggregated by type across EVERY container, then the
        /// heaviest few named with their mass. This is the "what is actually in the hold"
        /// page - a freighter captain's answer to "where did my cobalt go".
        /// </summary>
        private string BuildManifestPage()
        {
            int entries = CollectGridCargo(_ => true,
                out int usedSlots, out int capacitySlots, out float totalKg);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;

            var sb = new StringBuilder(320);
            sb.Append("MANIFEST  ").Append(UnitsBar01(fill)).Append(' ')
              .Append((fill * 100f).ToString("0")).Append("%\n");
            sb.Append(FormatMass(totalKg)).Append("  ·  ").Append(entries).Append(" item types\n");

            if (entries == 0) { sb.Append("HOLD EMPTY"); return sb.ToString(); }

            sb.Append('\n');
            int shown = Mathf.Min(HeaviestItemCount, entries);
            for (int i = 0; i < shown; i++)
            {
                var entry = s_massScratch[i];
                if (entry.item == null) continue;
                sb.Append((i + 1)).Append(' ')
                  .Append(PadRight(TrimName(entry.item.displayName, 14), 14))
                  .Append(FormatMass(entry.massKg)).Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        // ── FILL BARS ───────────────────────────────────────────────────────────
        /// <summary>
        /// One bar per LINKED container, in link order - the long wide panel's page. It
        /// deliberately reads the sources the player picked rather than the whole construct:
        /// the point of a fill strip is "these tanks", not "everything".
        /// </summary>
        private string BuildFillBarsPage()
        {
            var providers = ResolveAllProviders();
            var sb = new StringBuilder(320);
            int written = 0;

            for (int i = 0; i < providers.Count && written < MaxDisplayLines; i++)
            {
                // Containers and gas tanks both answer "how full am I", which is the whole
                // question this page asks. Anything else a player linked is simply skipped.
                float fill;
                string name;
                if (providers[i] is GridCargoContainer cargo) { fill = cargo.Fill01; name = cargo.SourceName; }
                else if (providers[i] is GridGasTank tank) { fill = tank.Fill01; name = tank.SourceName; }
                else continue;

                fill = Mathf.Clamp01(fill);
                sb.Append(FillLine(name, fill)).Append('\n');
                written++;
            }

            if (written == 0)
                return "FILL BARS\nNO CONTAINER LINKED\nRight-click to link one";
            return sb.ToString().TrimEnd();
        }

        /// <summary>One "NAME [bar] 87%" line, shortened until it fits the panel: the
        /// percentage goes first, then the bar, then the name. A fill strip squeezed onto a
        /// Small panel becomes a name and a stub of bar, which still reads as full or empty.</summary>
        private string FillLine(string name, float fill01)
        {
            int width = CharsPerLine();
            bool showPercent = width >= 20;
            int nameWidth = Mathf.Clamp(width / 3, 5, 16);
            int segments = Mathf.Clamp(width - nameWidth - 2 - (showPercent ? 5 : 0), 4, 24);

            var sb = new StringBuilder(width + 2);
            sb.Append(PadRight(TrimName(name, nameWidth), nameWidth))
              .Append(UnitsBar01Wide(fill01, segments));
            if (showPercent) sb.Append(' ').Append((fill01 * 100f).ToString("0")).Append('%');
            return sb.ToString();
        }

        // ── FARM ────────────────────────────────────────────────────────────────
        /// <summary>
        /// Every farm plot near the screen, with its crop, growth and water state. The scan is
        /// cached for several seconds because it touches the scene; the growth numbers it
        /// reads are live and move on the next refresh.
        /// </summary>
        private string BuildFarmPage()
        {
            RefreshFarmScan();

            if (_farmFound.Count == 0) return "FARM\nNO PLOTS WITHIN " + FarmScanRadiusMetres.ToString("0") + " m";

            var sb = new StringBuilder(320);
            int ready = 0, thirsty = 0, harvested = 0;
            for (int i = 0; i < _farmFound.Count; i++)
            {
                var plot = _farmFound[i];
                if (plot == null) continue;
                bool isEmpty = plot.plantedCrop == null;
                if (isEmpty) harvested++;
                else if (plot.growthProgress >= 0.999f) ready++;
                else if (plot.plantedCrop.requiresWater && !plot.isIrrigated) thirsty++;
            }

            sb.Append("FARM x").Append(_farmFound.Count)
              .Append("  READY ").Append(ready)
              .Append("  DRY ").Append(thirsty).Append('\n');

            int rows = Mathf.Max(0, MaxDisplayLines - 2);
            for (int i = 0; i < _farmFound.Count && i < rows; i++)
            {
                var plot = _farmFound[i];
                if (plot == null) continue;
                string crop = plot.plantedCrop != null ? TrimName(plot.plantedCrop.cropName, 10) : "-- empty --";
                float growth = plot.plantedCrop != null ? Mathf.Clamp01(plot.growthProgress) : 0f;
                char water = plot.plantedCrop == null ? ' '
                    : plot.isIrrigated ? 'W'
                    : (plot.plantedCrop.requiresWater ? '!' : 'w');
                sb.Append(PadRight(crop, 12)).Append(MiniBar(growth)).Append(' ')
                  .Append((growth * 100f).ToString("0")).Append("% ").Append(water).Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        private void RefreshFarmScan()
        {
            if (Time.unscaledTime - _farmScannedAt < FarmScanRefreshSeconds) return;
            _farmScannedAt = Time.unscaledTime;

            _farmFound.Clear();
            var all = UnityEngine.Object.FindObjectsByType<FarmPlot>(FindObjectsSortMode.None);
            Vector3 here = transform.position;
            float radiusSq = FarmScanRadiusMetres * FarmScanRadiusMetres;
            for (int i = 0; i < all.Length; i++)
            {
                var plot = all[i];
                if (plot == null) continue;
                if ((plot.transform.position - here).sqrMagnitude > radiusSq) continue;
                _farmFound.Add(plot);
            }
            _farmFound.Sort((a, b) =>
                (a.transform.position - here).sqrMagnitude.CompareTo((b.transform.position - here).sqrMagnitude));
        }

        // ── MINING ──────────────────────────────────────────────────────────────
        /// <summary>
        /// The overlay a pilot wants while digging: can this ship still hold itself up, how
        /// full is the hold, and what is weighing it down. Lift is upward thrust against the
        /// weight the construct currently has, so loading the hold visibly eats the margin.
        /// </summary>
        private string BuildMiningPage()
        {
            Vector3 here = transform.position;
            float g = Mathf.Max(0.01f, GravityProvider.GetGravity(here).magnitude);
            float massKg = Mathf.Max(0f, Grid.TotalMass);
            float weightN = massKg * g;
            float liftN = Grid.GetThrustByDirection().up;
            float lift01 = weightN > 0.001f ? liftN / weightN : (liftN > 0f ? 1f : 0f);

            // This call fills the shared scratch with the aggregate, sorted heaviest first;
            // the mining page only needs the first three entries of it.
            int entries = CollectGridCargo(_ => true,
                out int usedSlots, out int capacitySlots, out float totalKg);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;

            var sb = new StringBuilder(320);
            sb.Append("LIFT  ").Append(SemiGauge(lift01)).Append(' ')
              .Append((lift01 * 100f).ToString("0")).Append("%\n");
            sb.Append("HOLD  ").Append(UnitsBar01(fill)).Append(' ')
              .Append((fill * 100f).ToString("0")).Append("%\n");
            sb.Append(FormatMass(totalKg)).Append(" on board\n");

            if (fill > 0.001f)
            {
                sb.Append("HEAVIEST\n");
                int shown = Mathf.Min(3, entries);
                for (int i = 0; i < shown; i++)
                {
                    var entry = s_massScratch[i];
                    if (entry.item == null || entry.massKg <= 0f) continue;
                    sb.Append("  ").Append(PadRight(TrimName(entry.item.displayName, 12), 12))
                      .Append(FormatMass(entry.massKg)).Append('\n');
                }
            }
            return sb.ToString().TrimEnd();
        }

        // ── INVENTORY GRID ──────────────────────────────────────────────────────
        /// <summary>
        /// The hold as a grid rather than a list: several names per line, each with its
        /// count abbreviated the way a player reads a stockpile ("2.74 k"), busiest first.
        /// This is the page for a wall panel in a factory - you walk past it, you see what
        /// you are short of, you do not read down a column of twelve rows.
        /// </summary>
        private string BuildInventoryGridPage()
        {
            int entries = CollectGridCargo(_ => true,
                out int usedSlots, out int capacitySlots, out float totalKg);
            float fill = capacitySlots > 0 ? Mathf.Clamp01(usedSlots / (float)capacitySlots) : 0f;

            var sb = new StringBuilder(400);
            sb.Append("HOLD  ").Append(UnitsBar01(fill)).Append(' ')
              .Append((fill * 100f).ToString("0")).Append("%  ").Append(FormatMass(totalKg)).Append('\n');

            if (entries == 0) { sb.Append("EMPTY"); return sb.ToString(); }

            // Columns come from the panel's width, not from the item count: a Wide panel is
            // 1.4 m across and four lines tall (three columns, two rows of cells), an
            // ExtraLarge square is four columns and many rows, a Medium panel is one column
            // because there is simply no room for a second.
            int width = CharsPerLine();
            int columns = Mathf.Clamp(width / 11, 1, 4);
            int rows = Mathf.Max(1, MaxDisplayLines - 1);
            int capacity = columns * rows;

            // A cell is "name count" padded to a fixed width, so the grid stays aligned
            // whatever the names are - the one thing a monospace panel is good at.
            int cellWidth = Mathf.Max(9, width / columns);

            for (int i = 0; i < entries && i < capacity; i++)
            {
                var entry = s_massScratch[i];
                if (entry.item == null) continue;

                if (i > 0 && i % columns == 0) sb.Append('\n');
                string count = FormatCount(entry.count);
                string name = TrimName(entry.item.displayName, cellWidth - count.Length - 1);
                sb.Append(PadRight(name, cellWidth - count.Length)).Append(count);
                if (i % columns != columns - 1) sb.Append(' ');
            }

            if (entries > capacity)
                sb.Append('\n').Append('+').Append(entries - capacity).Append(" more");
            return sb.ToString().TrimEnd();
        }

        /// <summary>A count the way a stockpile reads: 773, 2.74 k, 5 k, 1.2 M.</summary>
        private static string FormatCount(int count)
        {
            if (count >= 1_000_000) return (count / 1_000_000f).ToString("0.##") + "M";
            if (count >= 1000) return (count / 1000f).ToString("0.##") + "k";
            return count.ToString();
        }

        // ── PLANET MAP ──────────────────────────────────────────────────────────
        /// <summary>
        /// The body below as a disc of block characters with a latitude/longitude grid, the
        /// construct's position marked on it, and the numbers underneath. A screen is text,
        /// so the disc is drawn in text - but it is drawn from the real geometry: the marker
        /// moves across the disc as the construct crosses the planet.
        /// </summary>
        private string BuildPlanetMapPage()
        {
            var body = GravityProvider.ActiveBody;
            Vector3 here = transform.position;

            var sb = new StringBuilder(600);
            if (body == null) return "MAP\nNO BODY IN RANGE";

            Vector3 centre = body.transform.position;
            Vector3 arm = here - centre;
            float radius = arm.magnitude;
            float altitude = radius - body.SurfaceRadius;

            // Position in the body's own frame: the pole is the body's up, so latitude and
            // longitude stay put while the planet turns under the construct.
            Vector3 pole = body.transform.up;
            float up = Vector3.Dot(arm.normalized, pole);
            float latitude = Mathf.Asin(Mathf.Clamp(up, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 flat = Vector3.ProjectOnPlane(arm, pole);
            float longitude = flat.sqrMagnitude > 1f
                ? Mathf.Atan2(Vector3.Dot(flat.normalized, body.transform.forward),
                              Vector3.Dot(flat.normalized, body.transform.right)) * Mathf.Rad2Deg
                : 0f;

            int width = CharsPerLine();
            sb.Append(TrimName(body.DisplayName.ToUpperInvariant(), width)).Append('\n');

            // The disc. Round, or not at all: it is drawn in a character grid whose cells are
            // twice as tall as they are wide, so it only looks like a planet when it gets
            // two columns per row. On a narrow panel that means fewer rows, not a squashed
            // circle - a map that is an ellipse is worse than a small map.
            int rows = Mathf.Clamp(Mathf.Min(MaxDisplayLines - 3, width / 2), 2, 8);
            int columns = rows * 2;

            // Marker in disc space: longitude across, latitude up.
            float markU = Mathf.Clamp(longitude / 180f, -1f, 1f);
            float markV = Mathf.Clamp(latitude / 90f, -1f, 1f);

            int markCol = Mathf.RoundToInt((markU + 1f) * 0.5f * (columns - 1));
            int markRow = Mathf.RoundToInt((1f - markV) * 0.5f * (rows - 1));

            for (int row = 0; row < rows; row++)
            {
                float v = 1f - (row + 0.5f) * 2f / rows;
                for (int column = 0; column < columns; column++)
                {
                    float u = (column + 0.5f) * 2f / columns - 1f;
                    if (u * u + v * v > 1f) { sb.Append(' '); continue; }

                    if (row == markRow && column == markCol) { sb.Append('X'); continue; }

                    bool meridian = Mathf.Abs(u - Mathf.Round(u * 3f) / 3f) < 0.045f;
                    bool parallel = Mathf.Abs(v - Mathf.Round(v * 2f) / 2f) < 0.05f;
                    sb.Append(meridian || parallel ? '\u00b7' : '\u2591');
                }
                sb.Append('\n');
            }

            // The numbers, in the order they matter, and only as many as the panel holds:
            // where am I (lat/lon), how high, how far from the centre. A Medium panel gets
            // the two angles; a Large gets the altitude as well.
            var footer = new StringBuilder(40);
            footer.Append("LAT ").Append(latitude.ToString("0")).Append('\u00b0')
                  .Append(" LON ").Append(NormaliseLongitude(longitude).ToString("0")).Append('\u00b0');
            AppendIfRoom(footer, width, " ALT " + FormatDistance(altitude));
            AppendIfRoom(footer, width, " R " + FormatDistance(radius));
            sb.Append(footer);
            return sb.ToString();
        }

        /// <summary>Longitude in -180..180, so a body that turns the "wrong" way still reads
        /// as a position on a globe rather than a number that runs off the end.</summary>
        private static float NormaliseLongitude(float degrees)
        {
            while (degrees > 180f) degrees -= 360f;
            while (degrees < -180f) degrees += 360f;
            return degrees;
        }

        /// <summary>Append a footer field only when it still fits the panel's line budget.</summary>
        private static void AppendIfRoom(StringBuilder sb, int width, string field)
        {
            if (sb.Length + field.Length > width) return;
            sb.Append(field);
        }

        // ── Shared snapshot helpers ─────────────────────────────────────────────

        /// <summary>
        /// Aggregates every item held by every container on the construct into
        /// <see cref="s_massScratch"/>, sorted heaviest first, and returns how many distinct
        /// types there are (capped at the scratch length). <paramref name="usedSlots"/> is
        /// slots occupied, <paramref name="capacitySlots"/> the summed slot count and
        /// <paramref name="totalKg"/> the summed stack mass.
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
            // Insertion sort: the manifest is small (a handful of types on a typical ship)
            // and this allocates nothing, which is what matters for a page that refreshes
            // twice a second on every screen of a base.
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

        /// <summary>
        /// How many of the screen font's characters fit across this panel before a line runs
        /// off the edge. Derived from the real glass size the block measures off its surface
        /// (17.35.1-dev) and the per-line height the fit pass produces, so a page lays itself
        /// out for the panel it is actually on instead of assuming one size.
        /// </summary>
        private int CharsPerLine()
        {
            var (width, height) = GlassSize();
            // A line of the screen font is a fixed fraction of the panel's height once the
            // page has been fitted to it, and a glyph is a little under half a line wide.
            float lineHeight = (height * 0.62f) / Mathf.Max(1, MaxDisplayLines);
            float charWidth = Mathf.Max(0.001f, lineHeight * 0.42f);
            return Mathf.Clamp(Mathf.FloorToInt(width / charWidth), 8, 64);
        }

        /// <summary>Ten-segment bar with a percentage, the house style of the older pages.</summary>
        private static string UnitsBar01(float value01)
        {
            float v = Mathf.Clamp01(value01);
            int full = Mathf.Clamp(Mathf.RoundToInt(v * 10f), 0, 10);
            var sb = new StringBuilder(12);
            sb.Append('[');
            for (int i = 0; i < 10; i++) sb.Append(i < full ? '\u2588' : '\u2591');
            sb.Append(']');
            return sb.ToString();
        }

        private static string UnitsBar01Wide(float value01, int segments)
        {
            float v = Mathf.Clamp01(value01);
            int count = Mathf.Clamp(segments, 4, 32);
            int full = Mathf.Clamp(Mathf.RoundToInt(v * count), 0, count);
            var sb = new StringBuilder(count + 2);
            sb.Append('[');
            for (int i = 0; i < count; i++) sb.Append(i < full ? '\u2588' : '\u2591');
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>A four-step mini bar for a crowded page.</summary>
        private static string MiniBar(float value01)
        {
            int full = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value01) * 4f), 0, 4);
            var sb = new StringBuilder(4);
            for (int i = 0; i < 4; i++) sb.Append(i < full ? '\u2588' : '\u2591');
            return sb.ToString();
        }

        /// <summary>
        /// The lift gauge: twelve block steps read left to right, so a pilot can see the
        /// margin without reading the number. Built from block characters on purpose - it
        /// renders on the same TextMesh as every other page, so there is no second drawing
        /// path, no render texture and no extra material to keep alive.
        /// </summary>
        private static string SemiGauge(float value01)
        {
            float v = Mathf.Clamp01(value01);
            int full = Mathf.Clamp(Mathf.RoundToInt(v * 12f), 0, 12);
            var sb = new StringBuilder(14);
            sb.Append('[');
            for (int i = 0; i < 12; i++) sb.Append(i < full ? '\u2588' : '\u2591');
            sb.Append(']');
            return sb.ToString();
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

        private static string PadRight(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return new string(' ', width);
            if (text.Length >= width) return text;
            return text + new string(' ', width - text.Length);
        }

        private static string TrimName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return name.Length <= max ? name : name.Substring(0, Mathf.Max(1, max - 1)) + "\u2026";
        }
    }
}
