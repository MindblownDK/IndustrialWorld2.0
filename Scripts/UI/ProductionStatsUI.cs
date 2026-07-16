// Assets/Scripts/VoxelEngine/UI/ProductionStatsUI.cs

using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.Simulation;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class ProductionStatsUI
    {
        private const string PrefHintsHidden = "IndustrialWorld.ProductionStats.HintsHidden";
        private const string PrefHiddenItems = "IndustrialWorld.ProductionStats.HiddenItems";
        private static bool _loadedPrefs;
        private static bool _hintsHidden;
        private static readonly HashSet<string> HiddenHintItems = new();
        private static void LoadPrefs()
        {
            if (_loadedPrefs) return;
            _loadedPrefs = true;
            _hintsHidden = PlayerPrefs.GetInt(PrefHintsHidden, 0) != 0;
            HiddenHintItems.Clear();
            string hidden = PlayerPrefs.GetString(PrefHiddenItems, string.Empty);
            if (!string.IsNullOrWhiteSpace(hidden))
                foreach (var item in hidden.Split('|'))
                    if (!string.IsNullOrWhiteSpace(item)) HiddenHintItems.Add(item);
        }

        private static void SavePrefs()
        {
            PlayerPrefs.SetInt(PrefHintsHidden, _hintsHidden ? 1 : 0);
            PlayerPrefs.SetString(PrefHiddenItems, string.Join("|", HiddenHintItems));
            PlayerPrefs.Save();
        }

        private static string BuildStatsText(IReadOnlyList<ProductionStatsTracker.ItemStats> snapshot)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Production Statistics");
            foreach (var stat in snapshot)
            {
                string name = stat.Item != null ? stat.Item.displayName : "Unknown";
                builder.AppendLine($"- {name}: +{stat.ProducedPerMinute:0}/min, -{stat.ConsumedPerMinute:0}/min, net {stat.NetPerMinute:+0;-0;0}/min, total {stat.NetTotal:+0;-0;0}");
            }
            return builder.ToString();
        }

        public static VisualElement BuildPanel()
        {
            LoadPrefs();
            var panel = T.MachinePanel();
            panel.style.left = new StyleLength(new Length(34f, LengthUnit.Percent));
            panel.style.right = 12;
            panel.style.width = new StyleLength(new Length(54f, LengthUnit.Percent));
            panel.style.maxWidth = new StyleLength(new Length(62f, LengthUnit.Percent));

            var snapshot = ProductionStatsTracker.Instance.GetSnapshot();
            panel.Add(Header(snapshot));
            panel.Add(T.AccentDivider(ProductionPanelThemeState.Accent));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.marginTop = 8;
            T.StyleScroller(scroll);
            panel.Add(scroll);

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

        private static VisualElement Header(IReadOnlyList<ProductionStatsTracker.ItemStats> snapshot)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 8;
            row.Add(T.IconBadge("P", ProductionPanelThemeState.Accent));
            var title = T.Title("Production Statistics");
            title.style.flexGrow = 1;
            row.Add(title);
            row.Add(T.SmallButton($"Theme: {ProductionPanelThemeState.Label}", () =>
            {
                ProductionPanelThemeState.Next();
                GameUIController.Instance?.RequestRefresh();
            }, ProductionPanelThemeState.Accent));
            row.Add(T.SmallButton("Copy Stats", () => GUIUtility.systemCopyBuffer = BuildStatsText(snapshot), T.AccentGreen));
            row.Add(T.SmallButton("Reset", () =>
            {
                ProductionStatsTracker.Instance.Clear();
                GameUIController.Instance?.RequestRefresh();
            }, T.AccentRed));
            var (pill, _) = T.StatusPill("LIVE", T.AccentGreen);
            row.Add(pill);
            return row;
        }

        private static VisualElement BuildBottleneckHints(IReadOnlyList<ProductionStatsTracker.ItemStats> snapshot)
        {
            var card = T.Card();
            card.style.marginBottom = 6;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = new StyleColor(_hintsHidden ? T.TextMuted : T.AccentCyan);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            var title = new Label(_hintsHidden ? "Bottleneck Hints Hidden" : "Bottleneck Hints");
            title.style.color = new StyleColor(_hintsHidden ? T.TextMuted : T.AccentCyan);
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1;
            header.Add(title);
            header.Add(T.SmallButton(_hintsHidden ? "Show" : "Hide All", () => { _hintsHidden = !_hintsHidden; SavePrefs(); GameUIController.Instance?.RequestRefresh(); }, _hintsHidden ? T.AccentGreen : T.TextMuted));
            card.Add(header);

            if (_hintsHidden)
            {
                var hidden = new Label("Hints are hidden for this session. Production rows still update below.");
                hidden.style.color = new StyleColor(T.TextSecondary);
                hidden.style.fontSize = 10;
                hidden.style.whiteSpace = WhiteSpace.Normal;
                hidden.style.marginTop = 5;
                card.Add(hidden);
                return card;
            }

            bool IsHidden(ProductionStatsTracker.ItemStats stat)
                => stat.Item != null && HiddenHintItems.Contains(ItemKey(stat.Item));

            var shortages = snapshot
                .Where(stat => !IsHidden(stat) && stat.ConsumedPerMinute > 0.01f && stat.NetPerMinute < -0.01f)
                .OrderBy(stat => stat.NetPerMinute)
                .Take(5)
                .ToList();

            var idleSurplus = snapshot
                .Where(stat => !IsHidden(stat) && stat.ProducedPerMinute > 0.01f && stat.ConsumedPerMinute <= 0.01f)
                .OrderByDescending(stat => stat.ProducedPerMinute)
                .Take(3)
                .ToList();

            bool hasShortage = shortages.Count > 0;
            card.style.borderLeftColor = new StyleColor(hasShortage ? T.AccentRed : T.AccentGreen);

            if (!hasShortage)
            {
                var ok = new Label("No visible consumed item is currently outrunning its production over the last minute.");
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

            if (HiddenHintItems.Count > 0)
            {
                var restore = new VisualElement();
                restore.style.flexDirection = FlexDirection.Row;
                restore.style.flexWrap = Wrap.Wrap;
                restore.style.marginTop = 8;
                restore.Add(T.SmallButton("Unhide All Items", () => { HiddenHintItems.Clear(); SavePrefs(); GameUIController.Instance?.RequestRefresh(); }, T.AccentGreen));
                card.Add(restore);
            }

            return card;
        }

        private static string ItemName(ProductionStatsTracker.ItemStats stat)
        {
            return stat.Item != null ? stat.Item.displayName : "Unknown Item";
        }

        private static string ItemKey(ItemDefinition item)
        {
            if (item == null) return string.Empty;
            return !string.IsNullOrWhiteSpace(item.itemId) ? item.itemId : item.name;
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

            if (stat.Item != null)
            {
                row.Add(T.SmallButton("View", () => GameUIController.Instance?.OpenRecipeBrowserFor(stat.Item), T.AccentCyan));
                row.Add(T.SmallButton("Hide", () => { HiddenHintItems.Add(ItemKey(stat.Item)); SavePrefs(); GameUIController.Instance?.RequestRefresh(); }, T.TextMuted));
            }
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
