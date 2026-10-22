// Assets/Scripts/VoxelEngine/GridSystem/UI/ScreenGraphSeries.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        THE POWER GRAPH'S SERIES MENU (17.42.0-dev)               ║
// ║                                                                  ║
// ║  A graph panel is one question asked of the construct: how much  ║
// ║  power is flowing, which way, and is the tank going to last.     ║
// ║  This file is the list of questions a player can ask, and the    ║
// ║  rules each one draws by - how its trace is scaled and what its  ║
// ║  legend must say.                                                ║
// ║                                                                  ║
// ║  The enum values are APPEND-ONLY: a screen persists its series   ║
// ║  as the enum NAME, so adding to the end is save-compatible and   ║
// ║  renumbering never is.                                           ║
// ╚══════════════════════════════════════════════════════════════════╝

namespace VoxelEngine.GridSystem.UI
{
    /// <summary>
    /// What one graph panel plots. Every series is "value now / its natural maximum",
    /// with the plot scaled either by its own peak, by a rated capacity, or by the
    /// construct's maximum required power.
    /// </summary>
    public enum ScreenGraphSeries
    {
        /// <summary>Total power usage against the construct's required (rated) power.</summary>
        usage,
        /// <summary>The same usage trace, but scaled to the total max required power.</summary>
        usageScaled,
        /// <summary>Battery charge state.</summary>
        battery,
        /// <summary>Battery input (charging) power.</summary>
        batteryIn,
        /// <summary>Battery output (discharging) power.</summary>
        batteryOut,
        /// <summary>Combined battery input and output.</summary>
        batteryInOut,
        /// <summary>Combined battery input and output, scaled by the peak value.</summary>
        batteryInOutAuto,
        /// <summary>Solar power output against used solar power.</summary>
        solar,
        /// <summary>Reactor power output.</summary>
        reactor,
        /// <summary>Hydrogen engine power output.</summary>
        h2engine,
        /// <summary>Stored oxygen.</summary>
        storedOxygen,
        /// <summary>Stored hydrogen.</summary>
        storedHydrogen,
    }

    /// <summary>How a panel decides its vertical scale.</summary>
    public enum ScreenGraphScale
    {
        /// <summary>Grow the scale to whatever the window's peak was.</summary>
        Peak,
        /// <summary>Hold the scale at a rated capacity, so overnight runs stay comparable.</summary>
        Rated,
        /// <summary>Hold the scale at the construct's maximum required power.</summary>
        RatedDemand,
    }

    /// <summary>Everything the picker and the panel need to know about one series.</summary>
    public readonly struct ScreenGraphSeriesInfo
    {
        public readonly ScreenGraphSeries Series;
        /// <summary>Short picker label.</summary>
        public readonly string Label;
        /// <summary>What the trace is, in the player's words.</summary>
        public readonly string Blurb;
        public readonly ScreenGraphScale Scale;
        /// <summary>True when the panel draws a second, paired trace (required power,
        /// used solar) under the main one.</summary>
        public readonly bool HasPairedTrace;
        /// <summary>Legend names for the main trace, the paired trace and the scale.</summary>
        public readonly string MainName;
        public readonly string PairedName;

        public ScreenGraphSeriesInfo(ScreenGraphSeries series, string label, string blurb,
            ScreenGraphScale scale, string mainName, string pairedName = "")
        {
            Series = series;
            Label = label;
            Blurb = blurb;
            Scale = scale;
            MainName = mainName;
            PairedName = pairedName;
            HasPairedTrace = !string.IsNullOrEmpty(pairedName);
        }
    }

    /// <summary>The one list the picker and the panels share.</summary>
    public static class ScreenGraphSeriesCatalog
    {
        public static readonly ScreenGraphSeriesInfo[] All =
        {
            new(ScreenGraphSeries.usage, "Usage",
                "Total power usage against the required power of the construct.",
                ScreenGraphScale.Peak, "Current", "Required"),
            new(ScreenGraphSeries.usageScaled, "Usage (scaled)",
                "The usage trace scaled to the total max required power.",
                ScreenGraphScale.RatedDemand, "Current", "Required"),
            new(ScreenGraphSeries.battery, "Battery charge",
                "Stored energy in the battery bank.",
                ScreenGraphScale.Rated, "Stored"),
            new(ScreenGraphSeries.batteryIn, "Battery input",
                "Power flowing into the batteries.",
                ScreenGraphScale.Rated, "Input"),
            new(ScreenGraphSeries.batteryOut, "Battery output",
                "Power flowing out of the batteries.",
                ScreenGraphScale.Rated, "Output"),
            new(ScreenGraphSeries.batteryInOut, "Battery in + out",
                "Combined battery input and output against the bank's rated transfer.",
                ScreenGraphScale.Rated, "Transfer"),
            new(ScreenGraphSeries.batteryInOutAuto, "Battery in + out (auto)",
                "Combined battery input and output, scaled by the peak value in the window.",
                ScreenGraphScale.Peak, "Transfer"),
            new(ScreenGraphSeries.solar, "Solar",
                "Solar power output against the used solar power.",
                ScreenGraphScale.Rated, "Generating", "Used"),
            new(ScreenGraphSeries.reactor, "Reactor",
                "Reactor power output against the reactors' rated output.",
                ScreenGraphScale.Rated, "Output"),
            new(ScreenGraphSeries.h2engine, "Hydrogen engine",
                "Hydrogen engine power output against the engines' rated output.",
                ScreenGraphScale.Rated, "Output"),
            new(ScreenGraphSeries.storedOxygen, "Stored oxygen",
                "Oxygen held in the construct's stores.",
                ScreenGraphScale.Rated, "Stored"),
            new(ScreenGraphSeries.storedHydrogen, "Stored hydrogen",
                "Hydrogen held in the construct's stores.",
                ScreenGraphScale.Rated, "Stored"),
        };

        public static ScreenGraphSeriesInfo For(ScreenGraphSeries series)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Series == series) return All[i];
            return All[0];
        }

        /// <summary>Parse a persisted name; unknown names fall back to the first series.</summary>
        public static ScreenGraphSeries Parse(string name, ScreenGraphSeries fallback)
        {
            if (string.IsNullOrWhiteSpace(name)) return fallback;
            return System.Enum.TryParse(name.Trim(), true, out ScreenGraphSeries parsed)
                ? parsed
                : fallback;
        }
    }
}
