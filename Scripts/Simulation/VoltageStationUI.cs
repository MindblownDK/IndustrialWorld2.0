using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.UI;
using VoxelEngine.Items; // Use existing PowerFormat
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Simulation
{
    public static class VoltageStationUI
    {
        public static VisualElement BuildPanel(IVoltageStation station)
        {
            var p = T.MachinePanel();
            string title = station.IsHighVoltage ? "⚡ High Voltage Grid" : "🔌 Low Voltage Grid";
            var (hdr, _, _, _) = T.HeaderRow(title, "CONNECTED", T.AccentCyan);
            p.Add(hdr);
            p.Add(T.AccentDivider(station.IsHighVoltage ? T.AccentGold : T.AccentCyan));

            // Stats
            p.Add(T.StatRow("⚡", "Total Produced", PowerFormat.Watts(station.TotalProduced), T.AccentGreen));
            p.Add(T.StatRow("🔌", "Total Consumed", PowerFormat.Watts(station.TotalConsumed), T.AccentRed));
            
            float available = Mathf.Max(0, station.TotalProduced - station.TotalConsumed);
            p.Add(T.StatRow("📊", "Available Power", PowerFormat.Watts(available), T.AccentCyan));
            
            p.Add(T.StatRow("⚠️", "Max Capacity", PowerFormat.Watts(station.MaxCapacity), T.AccentAmber));

            p.Add(T.Spacer(10));

            // Progress bar for load
            float load01 = float.IsInfinity(station.MaxCapacity) ? 0f : (station.MaxCapacity > 0 ? station.TotalConsumed / station.MaxCapacity : 0f);
            p.Add(T.Muted("System Load"));
            var (bar, _) = T.ProgressBar(load01, load01 > 0.9f ? T.AccentRed : T.AccentGreen, 12, true);
            p.Add(bar);

            p.Add(T.Spacer(10));
            p.Add(T.Muted("This panel shows live statistics from the connected power grid network."));

            return p;
        }

        private static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.alignItems = Align.Center;
            return r;
        }
    }
}

