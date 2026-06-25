// Assets/Scripts/VoxelEngine/UI/FluidPumpUI.cs
//
// Sleek UI Toolkit panel for the voxel liquid pump. Rebuilt with prominent
// pool status display (infinite/finite + progress bar to infinite threshold),
// clear internal tank gauge, and connected network info.

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

            bool isOil = pump.liquidType == LiquidType.CrudeOil;
            var accent = isOil ? new Color(0.72f, 0.48f, 0.18f) : UITheme.AccentCyan;

            // ── Header ──────────────────────────────────────────────────────
            var (header, _, _, _) = UITheme.HeaderRow(
                isOil ? "🛢 Crude Oil Pump" : "💧 Water Pump",
                pump.IsPowered ? "ONLINE" : "NO POWER",
                pump.IsPowered ? UITheme.AccentGreen : UITheme.AccentRed);
            p.Add(header);
            p.Add(UITheme.AccentDivider(accent));

            // ── Liquid type selector ────────────────────────────────────────
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

            // ── Source / Pool Status ────────────────────────────────────────
            p.Add(UITheme.Subtitle("SOURCE POOL"));
            p.Add(UITheme.Spacer(4));

            // Source status card with prominent infinite/finite indicator
            var sourceCard = UITheme.Card();

            // Status pill: infinite = green, finite = amber, no source = red
            Color sourceColor;
            string sourceLabel;
            if (!pump.HasSource)
            {
                sourceColor = UITheme.AccentRed;
                sourceLabel = "NO SOURCE";
            }
            else if (pump.SourceInfinite)
            {
                sourceColor = UITheme.AccentGreen;
                sourceLabel = "∞ INFINITE";
            }
            else
            {
                sourceColor = accent;
                sourceLabel = "FINITE";
            }

            var srcRow = new VisualElement();
            srcRow.style.flexDirection = FlexDirection.Row;
            srcRow.style.alignItems = Align.Center;
            srcRow.style.justifyContent = Justify.SpaceBetween;

            var srcLabel = UITheme.StatLabel("POOL STATUS");
            srcRow.Add(srcLabel);

            // Status badge
            var badge = new VisualElement();
            badge.style.flexDirection = FlexDirection.Row;
            badge.style.alignItems = Align.Center;
            badge.style.paddingLeft = 10;
            badge.style.paddingRight = 12;
            badge.style.paddingTop = 4;
            badge.style.paddingBottom = 4;
            badge.style.backgroundColor = new StyleColor(new Color(sourceColor.r, sourceColor.g, sourceColor.b, 0.18f));
            UITheme.Radius(badge, 10);
            UITheme.Border(badge, 1, new Color(sourceColor.r, sourceColor.g, sourceColor.b, 0.5f));

            // Pulsing dot
            var dot = new VisualElement();
            dot.style.width = 7; dot.style.height = 7;
            dot.style.backgroundColor = new StyleColor(sourceColor);
            dot.style.marginRight = 7;
            UITheme.Radius(dot, 4);
            badge.Add(dot);

            var badgeText = new Label(sourceLabel);
            badgeText.style.color = new StyleColor(sourceColor);
            badgeText.style.fontSize = 10;
            badgeText.style.unityFontStyleAndWeight = FontStyle.Bold;
            badgeText.style.letterSpacing = 1.2f;
            badge.Add(badgeText);
            srcRow.Add(badge);
            sourceCard.Add(srcRow);

            // Pool volume details
            if (pump.HasSource)
            {
                sourceCard.Add(UITheme.Spacer(6));
                sourceCard.Add(UITheme.StatRow("◎", "Pool Volume", $"{pump.SourceLitres:0} L", accent));
                sourceCard.Add(UITheme.StatRow("▦", "Pool Voxels", $"{pump.SourceVoxels}", accent));

                // Progress bar to infinite threshold
                if (!pump.SourceInfinite)
                {
                    sourceCard.Add(UITheme.Spacer(4));
                    var thresholdLabel = UITheme.Muted($"Progress to infinite threshold ({pump.infiniteVoxelThreshold} voxels)");
                    sourceCard.Add(thresholdLabel);
                    var (poolBar, poolFill) = UITheme.ProgressBar(pump.PoolInfiniteProgress, accent, 10, true);
                    sourceCard.Add(poolBar);
                }
            }
            p.Add(sourceCard);

            p.Add(UITheme.Spacer(8));

            // ── Pump rates ─────────────────────────────────────────────────
            p.Add(UITheme.StatRow("↯", "Intake Rate", $"{pump.pumpLps:0} L/s", accent));
            p.Add(UITheme.StatRow("⇒", "Output Rate", $"{pump.outputLps:0} L/s", accent));
            p.Add(UITheme.StatRow("⇄", "Pipe Network", pump.network != null ? $"{pump.network.nodes.Count} nodes" : "No network",
                pump.network != null ? UITheme.AccentGreen : UITheme.TextMuted));

            // ── Internal tank ──────────────────────────────────────────────
            p.Add(UITheme.Divider());
            p.Add(UITheme.Subtitle("INTERNAL TANK"));
            p.Add(UITheme.Spacer(4));

            var tankColor = pump.liquidType.Color();
            var (bar, fill) = UITheme.ProgressBar(pump.InternalFill01, tankColor, 20, true);
            p.Add(bar);
            var litres = UITheme.Muted($"{pump.internalLitres:0} / {pump.internalCapacityLitres:0} L {pump.liquidType.DisplayName()}");
            litres.style.unityTextAlign = TextAnchor.MiddleCenter;
            p.Add(litres);

            // ── Help text ──────────────────────────────────────────────────
            p.Add(UITheme.Divider());
            p.Add(UITheme.Muted(
                "Place the pump above or beside a connected pool. Large oceans automatically become ∞ infinite — " +
                "the pump spawns new liquid without draining the source. Finite pools are drained voxel-by-voxel. " +
                "Connect water pipes to tanks to transport the liquid."));

            return p;
        }
    }
}
