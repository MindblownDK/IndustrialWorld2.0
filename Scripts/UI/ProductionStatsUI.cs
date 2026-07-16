// Assets/Scripts/VoxelEngine/UI/ProductionStatsUI.cs

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Simulation;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class ProductionStatsUI
    {
        public static VisualElement BuildPanel()
        {
            var panel = T.MachinePanel();
            panel.style.left = new StyleLength(new Length(34f, LengthUnit.Percent));
            panel.style.right = 12;
            panel.style.width = new StyleLength(new Length(54f, LengthUnit.Percent));
            panel.style.maxWidth = new StyleLength(new Length(62f, LengthUnit.Percent));

            panel.Add(Header());
            panel.Add(T.AccentDivider(T.AccentCyan));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.marginTop = 8;
            T.StyleScroller(scroll);
            panel.Add(scroll);

            var snapshot = ProductionStatsTracker.Instance.GetSnapshot();
            if (snapshot.Count == 0)
            {
                scroll.Add(T.Muted("No production has been recorded yet. Run a Crusher, Assembler, or Electric Furnace batch to populate this panel."));
                return panel;
            }

            scroll.Add(T.StatRow("↕", "Tracked Items", snapshot.Count.ToString(), T.AccentCyan));
            scroll.Add(T.Spacer(6));
            scroll.Add(BuildBottleneckHints(snapshot));
            scroll.Add(T.Spacer(8));

            foreach (var stat in snapshot)
                scroll.Add(Row(stat));

            scroll.Add(T.Spacer(8));
            scroll.Add(T.Muted("Per-minute values use the last 60 seconds. Totals reset when the session restarts and do not touch save data."));
            return panel;
        }

        private static VisualElement Header()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 8;
            row.Add(T.IconBadge("📈", T.AccentCyan));
            var title = T.Title("Production Statistics");
            title.style.flexGrow = 1;
            row.Add(title);
            var (pill, _) = T.StatusPill("LIVE", T.AccentGreen);
            row.Add(pill);
            return row;
        }

        private static VisualElement BuildBottleneckHints(IReadOnlyList<ProductionStatsTracker.ItemStats> snapshot)
        {
            var card = T.Card();
            card.style.marginBottom = 6;
            card.style.borderLeftWidth = 3;

            var shortages = snapshot
                .Where(stat => stat.ConsumedPerMinute > 0.01f && stat.NetPerMinute < -0.01f)
                .OrderBy(stat => stat.NetPerMinute)
                .Take(5)
                .ToList();

            var idleSurplus = snapshot
                .Where(stat => stat.ProducedPerMinute > 0.01f && stat.ConsumedPerMinute <= 0.01f)
                .OrderByDescending(stat => stat.ProducedPerMinute)
                .Take(3)
                .ToList();

            bool hasShortage = shortages.Count > 0;
            card.style.borderLeftColor = new StyleColor(hasShortage ? T.AccentRed : T.AccentGreen);

            var title = new Label(hasShortage ? "⚠ Bottleneck Hints" : "✓ Production Stable");
            title.style.color = new StyleColor(hasShortage ? T.AccentRed : T.AccentGreen);
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(title);

            if (!hasShortage)
            {
                var ok = new Label("No consumed item is currently outrunning its production over the last minute.");
                ok.style.color = new StyleColor(T.TextSecondary);
                ok.style.fontSize = 10;
                ok.style.whiteSpace = WhiteSpace.Normal;
                ok.style.marginTop = 4;
                card.Add(ok);
            }
            else
            {
                foreach (var stat in shortages)
                    card.Add(HintLine("Shortage", stat, T.AccentRed,
                        $"Produce {Mathf.Abs(stat.NetPerMinute):0}/min more {ItemName(stat)}"));
            }

            if (idleSurplus.Count > 0)
            {
                var surplusTitle = new Label("Idle Surplus");
                surplusTitle.style.color = new StyleColor(T.AccentGold);
                surplusTitle.style.fontSize = 10;
                surplusTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                surplusTitle.style.marginTop = 8;
                card.Add(surplusTitle);
                foreach (var stat in idleSurplus)
                    card.Add(HintLine("Surplus", stat, T.AccentGold,
                        $"Producing {stat.ProducedPerMinute:0}/min with no recent consumer"));
            }

            return card;
        }

        private static string ItemName(ProductionStatsTracker.ItemStats stat)
        {
            return stat.Item != null ? stat.Item.displayName : "Unknown Item";
        }

        private static VisualElement HintLine(string label, ProductionStatsTracker.ItemStats stat, Color accent, string text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 5;

            var tag = new Label(label.ToUpperInvariant());
            tag.style.width = 62;
            tag.style.fontSize = 8;
            tag.style.unityFontStyleAndWeight = FontStyle.Bold;
            tag.style.color = new StyleColor(accent);
            row.Add(tag);

            var body = new Label(text);
            body.style.flexGrow = 1;
            body.style.fontSize = 10;
            body.style.color = new StyleColor(T.TextSecondary);
            body.style.whiteSpace = WhiteSpace.Normal;
            row.Add(body);
            return row;
        }

        private static VisualElement Row(ProductionStatsTracker.ItemStats stat)
        {
            var card = T.Card();
            card.style.marginBottom = 6;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;

            var tint = stat.Item != null ? stat.Item.iconTint : T.TextMuted;
            var swatch = new VisualElement();
            swatch.style.width = 12;
            swatch.style.height = 34;
            swatch.style.marginRight = 10;
            swatch.style.backgroundColor = new StyleColor(tint);
            T.Radius(swatch, 4);
            card.Add(swatch);

            var nameCol = new VisualElement();
            nameCol.style.flexGrow = 1;
            var name = new Label(stat.Item != null ? stat.Item.displayName : "Unknown Item");
            name.style.color = new StyleColor(T.TextPrimary);
            name.style.fontSize = 12;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameCol.Add(name);
            var net = new Label($"Net: {stat.NetPerMinute:+0;-0;0}/min · Total {stat.NetTotal:+0;-0;0}");
            net.style.color = new StyleColor(stat.NetPerMinute >= 0 ? T.AccentGreen : T.AccentRed);
            net.style.fontSize = 10;
            nameCol.Add(net);
            card.Add(nameCol);

            card.Add(Metric("Produced", stat.ProducedPerMinute, stat.ProducedTotal, T.AccentGreen));
            card.Add(Metric("Consumed", stat.ConsumedPerMinute, stat.ConsumedTotal, T.AccentOrange));
            return card;
        }

        private static VisualElement Metric(string label, float perMinute, int total, Color accent)
        {
            var col = new VisualElement();
            col.style.width = 96;
            col.style.alignItems = Align.FlexEnd;
            var value = new Label($"{perMinute:0}/min");
            value.style.color = new StyleColor(accent);
            value.style.fontSize = 12;
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            col.Add(value);
            var sub = new Label($"{label}: {total}");
            sub.style.color = new StyleColor(T.TextMuted);
            sub.style.fontSize = 9;
            col.Add(sub);
            return col;
        }
    }
}
