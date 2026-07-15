// Assets/Scripts/VoxelEngine/UI/ProductionStatsUI.cs

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
