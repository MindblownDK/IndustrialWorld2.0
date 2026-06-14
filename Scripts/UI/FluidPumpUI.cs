// Assets/Scripts/VoxelEngine/UI/FluidPumpUI.cs
// Sleek UI Toolkit panel for the voxel liquid pump.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Fluids;
using VoxelEngine.Items;

namespace VoxelEngine.UI
{
    public static class FluidPumpUI
    {
        public static VisualElement BuildPanel(WaterPump pump)
        {
            var p = UITheme.Panel();
            p.style.position = Position.Absolute;
            p.style.top = 28;
            p.style.bottom = 100;
            p.style.right = 28;
            p.style.width = 484;

            if (pump == null)
            {
                p.Add(UITheme.Title("Liquid Pump"));
                p.Add(UITheme.Muted("Pump unavailable."));
                return p;
            }

            var accent = pump.liquidType == LiquidType.CrudeOil ? new Color(0.72f, 0.48f, 0.18f) : UITheme.AccentCyan;
            var (header, _, _, _) = UITheme.HeaderRow(
                pump.liquidType == LiquidType.CrudeOil ? "🛢 Crude Oil Pump" : "💧 Water Pump",
                pump.IsPowered ? "ONLINE" : "NO POWER",
                pump.IsPowered ? UITheme.AccentGreen : UITheme.AccentRed);
            p.Add(header);
            p.Add(UITheme.AccentDivider(accent));

            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.alignItems = Align.Center;
            modeRow.Add(UITheme.StatLabel("LIQUID"));
            var gap = new VisualElement(); gap.style.width = 8;
            modeRow.Add(gap);
            modeRow.Add(UITheme.SmallButton("Water", () => { if (pump.internalLitres <= 0.01f) pump.liquidType = LiquidType.Water; },
                pump.liquidType == LiquidType.Water ? UITheme.AccentCyan : UITheme.BgSlot));
            modeRow.Add(UITheme.SmallButton("Crude Oil", () => { if (pump.internalLitres <= 0.01f) pump.liquidType = LiquidType.CrudeOil; },
                pump.liquidType == LiquidType.CrudeOil ? accent : UITheme.BgSlot));
            p.Add(modeRow);
            p.Add(UITheme.Muted("Liquid type can be changed while the internal tank is empty."));
            p.Add(UITheme.Spacer(10));

            p.Add(UITheme.StatRow("◎", "Source", pump.SourceStatus, pump.SourceInfinite ? UITheme.AccentGreen : accent));
            p.Add(UITheme.StatRow("↯", "Pump Rate", $"{pump.pumpLps:0} L/s intake  •  {pump.outputLps:0} L/s output", accent));
            p.Add(UITheme.StatRow("⇄", "Connected Network", pump.network != null ? $"{pump.network.nodes.Count} nodes" : "No pipe network", pump.network != null ? UITheme.AccentGreen : UITheme.TextMuted));

            p.Add(UITheme.Divider());
            p.Add(UITheme.Subtitle("Internal Tank"));
            var tankColor = pump.liquidType.Color();
            var (bar, fill) = UITheme.ProgressBar(pump.InternalFill01, tankColor, 16, true);
            p.Add(bar);
            var litres = UITheme.Muted($"{pump.internalLitres:0} / {pump.internalCapacityLitres:0} L {pump.liquidType.DisplayName()}");
            litres.style.unityTextAlign = TextAnchor.MiddleCenter;
            p.Add(litres);

            p.Add(UITheme.Divider());
            p.Add(UITheme.Muted(
                "Place the pump above or beside a connected pool. Large oceans/pools become infinite, " +
                "while smaller pools are physically drained voxel-by-voxel. Connect water pipes to tanks to transport the liquid."));

            return p;
        }
    }
}
