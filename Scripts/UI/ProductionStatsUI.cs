// Assets/Scripts/VoxelEngine/UI/ProductionStatsUI.cs
// Final polished production statistics - theme-aware, responsive, micro-interactions.

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

        private static string BuildCsvText(IReadOnlyList<ProductionStatsTracker.ItemStats> snapshot)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Item,ProducedPerMin,ConsumedPerMin,NetPerMin,ProducedTotal,ConsumedTotal,NetTotal");
            foreach (var stat in snapshot)
            {
                string name = stat.Item != null ? stat.Item.displayName : "Unknown";
                builder.AppendLine($"\"{name}\",{stat.ProducedPerMinute:0},{stat.ConsumedPerMinute:0},{stat.NetPerMinute:0},{stat.ProducedTotal},{stat.ConsumedTotal},{stat.NetTotal}");
            }
            return builder.ToString();
        }

        public static VisualElement BuildPanel()
        {
            LoadPrefs();
            var panel = T.MachinePanel();
            LcdHudTheme.ApplyChassis(panel, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.98f), 2f);
            panel.style.left = new StyleLength(new Length(34f, LengthUnit.Percent));
            panel.style.right = 12;
            panel.style.width = new StyleLength(new Length(54f, LengthUnit.Percent));
            panel.style.maxWidth = new StyleLength(new Length(62f, LengthUnit.Percent));
            panel.style.minWidth = 300;
            panel.style.paddingTop = 6;
            panel.style.paddingBottom = 6;
            panel.style.paddingLeft = 6;
            panel.style.paddingRight = 6;

            var display = new VisualElement { name = "ProductionLcdDisplay" };
            display.style.flexDirection = FlexDirection.Column;
            display.style.flexGrow = 1;
            display.style.minHeight = 0;
            display.style.paddingTop = 6;
            display.style.paddingBottom = 6;
            display.style.paddingLeft = 6;
            display.style.paddingRight = 6;
            display.style.overflow = Overflow.Hidden;
            LcdHudTheme.ApplyScreen(display, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.92f), 1f);
            panel.Add(display);
            LcdHudTheme.AddAnimatedScanlines(display, 8, 25f, 35f);
            LcdHudTheme.AnimateScreenBoot(display);

            var snapshot = ProductionStatsTracker.Instance.GetSnapshot();
            display.Add(Header(snapshot));

            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "ProductionDataScroll" };
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            scroll.style.marginTop = 2;
            T.StyleScroller(scroll, LcdHudTheme.Phosphor);
            display.Add(scroll);

            if (snapshot.Count == 0)
            {
                var empty = LcdCard(LcdHudTheme.Bezel);
                empty.style.marginTop = 4;
                empty.Add(LcdHudTheme.CaptionLabel("NO TELEMETRY"));
                var message = new Label("NO PRODUCTION RECORDED");
                message.style.fontSize = 11;
                message.style.letterSpacing = 0.9f;
                message.style.unityFontStyleAndWeight = FontStyle.Bold;
                message.style.color = new StyleColor(LcdHudTheme.Phosphor);
                empty.Add(message);
                var hint = LcdHudTheme.CaptionLabel("RUN A PROCESSOR OR ASSEMBLER TO POPULATE THIS MONITOR.");
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.marginTop = 5;
                empty.Add(hint);
                scroll.Add(empty);
                LcdHudTheme.AddScanlines(display, 9, 48f, 57f);
                return panel;
            }

            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems = Align.Center;
            topRow.style.marginBottom = 6;
            topRow.style.paddingTop = 5;
            topRow.style.paddingBottom = 5;
            topRow.style.paddingLeft = 7;
            topRow.style.paddingRight = 7;
            LcdHudTheme.ApplyDataCard(topRow, LcdHudTheme.Bezel);
            var tracked = LcdHudTheme.CaptionLabel("TRACKED MATERIAL FLOWS");
            tracked.style.flexGrow = 1;
            topRow.Add(tracked);
            var trackedValue = new Label(snapshot.Count.ToString("00"));
            trackedValue.style.fontSize = 12;
            trackedValue.style.letterSpacing = 0.8f;
            trackedValue.style.unityFontStyleAndWeight = FontStyle.Bold;
            trackedValue.style.color = new StyleColor(LcdHudTheme.Phosphor);
            topRow.Add(trackedValue);
            scroll.Add(topRow);

            scroll.Add(BuildBottleneckHints(snapshot));
            scroll.Add(T.Spacer(6));

            foreach (var stat in snapshot.OrderByDescending(s => Mathf.Abs(s.NetPerMinute)).ThenBy(s => s.Item != null ? s.Item.displayName : ""))
                scroll.Add(Row(stat));

            scroll.Add(T.Spacer(5));
            var footnote = LcdHudTheme.CaptionLabel("RATE WINDOW: 60 SECONDS  //  TOTALS RESET PER SESSION");
            footnote.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(footnote);
            LcdHudTheme.AddScanlines(display, 9, 48f, 57f);
            return panel;
        }


        private static VisualElement Header(IReadOnlyList<ProductionStatsTracker.ItemStats> snapshot)
        {
            var host = new VisualElement { name = "ProductionDisplayHeader" };
            host.style.marginBottom = 5;
            host.Add(LcdHudTheme.CreateDisplayHeader("OPERATIONS MONITOR", "PRODUCTION", "PRD-01", "LIVE"));

            var commands = new VisualElement();
            commands.style.flexDirection = FlexDirection.Row;
            commands.style.flexWrap = Wrap.Wrap;
            commands.style.paddingTop = 4;
            commands.style.paddingBottom = 2;
            commands.style.paddingLeft = 4;
            commands.style.paddingRight = 4;
            LcdHudTheme.ApplyDataCard(commands, LcdHudTheme.Bezel);

            commands.Add(LcdButton($"THEME {ProductionPanelThemeState.Label.ToUpperInvariant()}", () =>
            {
                ProductionPanelThemeState.Next();
                GameUIController.Instance?.RequestRefresh();
            }, LcdHudTheme.Phosphor));
            commands.Add(LcdButton("COPY STATS", () => GUIUtility.systemCopyBuffer = BuildStatsText(snapshot), LcdHudTheme.Phosphor));
            commands.Add(LcdButton("COPY CSV", () => GUIUtility.systemCopyBuffer = BuildCsvText(snapshot), LcdHudTheme.Phosphor));
            commands.Add(LcdButton("RESET", () =>
            {
                ProductionStatsTracker.Instance.Clear();
                GameUIController.Instance?.RequestRefresh();
            }, UITheme.AccentRed));
            host.Add(commands);
            return host;
        }


        private static VisualElement BuildBottleneckHints(IReadOnlyList<ProductionStatsTracker.ItemStats> snapshot)
        {
            var card = LcdCard();
            card.style.marginBottom = 8;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = new StyleColor(_hintsHidden ? T.TextMuted : ProductionPanelThemeState.Accent);
            card.AddToClassList("themed-panel");
            card.style.transitionProperty = new List<StylePropertyName> { "scale", "background-color" };
            card.style.transitionDuration = new List<TimeValue> { new TimeValue(0.10f, TimeUnit.Second), new TimeValue(0.10f, TimeUnit.Second) };
            card.RegisterCallback<PointerEnterEvent>(_ => card.style.scale = new StyleScale(new Scale(new Vector3(1.01f, 1.01f, 1f))));
            card.RegisterCallback<PointerLeaveEvent>(_ => card.style.scale = new StyleScale(new Scale(Vector3.one)));

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.flexWrap = Wrap.Wrap;
            var title = new Label(_hintsHidden ? "BOTTLENECK HINTS HIDDEN" : "BOTTLENECK HINTS");
            title.style.color = new StyleColor(_hintsHidden ? LcdHudTheme.PhosphorDim : LcdHudTheme.Phosphor);
            title.style.fontSize = 11;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.2f;
            title.style.flexGrow = 1;
            header.Add(title);
            header.Add(LcdButton(_hintsHidden ? "Show" : "Hide All", () => { _hintsHidden = !_hintsHidden; SavePrefs(); GameUIController.Instance?.RequestRefresh(); }, _hintsHidden ? T.AccentGreen : T.TextMuted));
            card.Add(header);

            if (_hintsHidden)
            {
                var hidden = new Label("Hints hidden. Production rows still update below.");
                hidden.style.color = new StyleColor(T.TextSecondary);
                hidden.style.fontSize = 10;
                hidden.style.whiteSpace = WhiteSpace.Normal;
                hidden.style.marginTop = 5;
                card.Add(hidden);
                return card;
            }

            bool IsHidden(ProductionStatsTracker.ItemStats stat) => stat.Item != null && HiddenHintItems.Contains(ItemKey(stat.Item));

            var shortages = snapshot.Where(stat => !IsHidden(stat) && stat.ConsumedPerMinute > 0.01f && stat.NetPerMinute < -0.01f).OrderBy(stat => stat.NetPerMinute).Take(5).ToList();
            var idleSurplus = snapshot.Where(stat => !IsHidden(stat) && stat.ProducedPerMinute > 0.01f && stat.ConsumedPerMinute <= 0.01f).OrderByDescending(stat => stat.ProducedPerMinute).Take(3).ToList();

            card.style.borderLeftColor = new StyleColor(shortages.Count > 0 ? T.AccentRed : T.AccentGreen);

            if (shortages.Count == 0)
            {
                var ok = new Label("✓ Production stable — no consumed item is outrunning production in last minute.");
                ok.style.color = new StyleColor(T.AccentGreen);
                ok.style.fontSize = 10;
                ok.style.whiteSpace = WhiteSpace.Normal;
                ok.style.marginTop = 4;
                ok.style.unityFontStyleAndWeight = FontStyle.Bold;
                card.Add(ok);
            }
            else
            {
                foreach (var stat in shortages)
                    card.Add(HintLine("SHORTAGE", stat, T.AccentRed, $"Need {Mathf.Abs(stat.NetPerMinute):0}/min more {ItemName(stat)}"));
            }

            if (idleSurplus.Count > 0)
            {
                var surplusTitle = new Label("IDLE SURPLUS");
                surplusTitle.style.color = new StyleColor(T.AccentGold);
                surplusTitle.style.fontSize = 9;
                surplusTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                surplusTitle.style.marginTop = 8;
                card.Add(surplusTitle);
                foreach (var stat in idleSurplus)
                    card.Add(HintLine("SURPLUS", stat, T.AccentGold, $"Producing {stat.ProducedPerMinute:0}/min with no consumer"));
            }

            if (HiddenHintItems.Count > 0)
            {
                var restore = new VisualElement();
                restore.style.flexDirection = FlexDirection.Row;
                restore.style.flexWrap = Wrap.Wrap;
                restore.style.marginTop = 8;
                restore.Add(LcdButton("Unhide All Items", () => { HiddenHintItems.Clear(); SavePrefs(); GameUIController.Instance?.RequestRefresh(); }, T.AccentGreen));
                card.Add(restore);
            }

            return card;
        }

        private static string ItemName(ProductionStatsTracker.ItemStats stat) => stat.Item != null ? stat.Item.displayName : "Unknown Item";
        private static string ItemKey(ItemDefinition item) => item == null ? string.Empty : (!string.IsNullOrWhiteSpace(item.itemId) ? item.itemId : item.name);

        private static VisualElement HintLine(string label, ProductionStatsTracker.ItemStats stat, Color accent, string text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 5;
            row.style.paddingTop = 4;
            row.style.paddingBottom = 4;
            row.style.paddingLeft = 6;
            row.style.paddingRight = 6;
            row.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.06f));
            LcdHudTheme.ApplyDataCard(row, accent);
            row.style.transitionProperty = new List<StylePropertyName> { "background-color", "scale" };
            row.style.transitionDuration = new List<TimeValue> { new TimeValue(0.10f, TimeUnit.Second), new TimeValue(0.10f, TimeUnit.Second) };
            row.RegisterCallback<PointerEnterEvent>(_ => { row.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.10f)); row.style.scale = new StyleScale(new Scale(new Vector3(1.01f, 1.01f, 1f))); });
            row.RegisterCallback<PointerLeaveEvent>(_ => { row.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.06f)); row.style.scale = new StyleScale(new Scale(Vector3.one)); });

            var tag = new Label(label);
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
                row.Add(LcdButton("VIEW", () => GameUIController.Instance?.OpenRecipeBrowserFor(stat.Item), LcdHudTheme.Phosphor));
                row.Add(LcdButton("Hide", () => { HiddenHintItems.Add(ItemKey(stat.Item)); SavePrefs(); GameUIController.Instance?.RequestRefresh(); }, T.TextMuted));
            }
            return row;
        }

        private static VisualElement Row(ProductionStatsTracker.ItemStats stat)
        {
            var card = LcdCard();
            card.style.marginBottom = 6;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;
            card.style.flexWrap = Wrap.Wrap;
            card.AddToClassList("themed-panel");
            // Micro-interaction: hover scale + highlight
            card.style.transitionProperty = new List<StylePropertyName> { "scale", "background-color" };
            card.style.transitionDuration = new List<TimeValue> { new TimeValue(0.10f, TimeUnit.Second), new TimeValue(0.10f, TimeUnit.Second) };
            Color baseBg = LcdHudTheme.GlassDark;
            card.RegisterCallback<PointerEnterEvent>(_ => { card.style.backgroundColor = new StyleColor(new Color(LcdHudTheme.Phosphor.r, LcdHudTheme.Phosphor.g, LcdHudTheme.Phosphor.b, 0.10f)); card.style.scale = new StyleScale(new Scale(new Vector3(1.02f, 1.02f, 1f))); });
            card.RegisterCallback<PointerLeaveEvent>(_ => { card.style.backgroundColor = new StyleColor(baseBg); card.style.scale = new StyleScale(new Scale(Vector3.one)); });

            var tint = stat.Item != null ? stat.Item.iconTint : T.TextMuted;
            var swatch = new VisualElement();
            swatch.style.width = 4;
            swatch.style.height = 34;
            swatch.style.marginRight = 10;
            swatch.style.backgroundColor = new StyleColor(tint);
            T.Radius(swatch, 2);
            card.Add(swatch);

            var nameCol = new VisualElement();
            nameCol.style.flexGrow = 1;
            nameCol.style.minWidth = 120;
            var name = new Label(stat.Item != null ? stat.Item.displayName : "Unknown Item");
            name.style.color = new StyleColor(LcdHudTheme.Phosphor);
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
            col.style.width = 88;
            col.style.minWidth = 80;
            col.style.alignItems = Align.FlexEnd;
            col.style.marginLeft = 6;
            col.style.paddingTop = 3;
            col.style.paddingBottom = 3;
            col.style.paddingLeft = 5;
            col.style.paddingRight = 5;
            LcdHudTheme.ApplyDataCard(col, accent);
            var value = new Label($"{perMinute:0}/min");
            value.style.color = new StyleColor(accent);
            value.style.fontSize = 11;
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            col.Add(value);
            var sub = new Label($"{label}: {total}");
            sub.style.color = new StyleColor(T.TextMuted);
            sub.style.fontSize = 8;
            col.Add(sub);
            return col;
        }

        private static VisualElement LcdCard(Color? signalColor = null)
        {
            var card = T.Card();
            LcdHudTheme.ApplyDataCard(card, signalColor ?? LcdHudTheme.Bezel);
            return card;
        }

        private static Button LcdButton(string text, System.Action onClick, Color? signalColor = null)
        {
            return LcdHudTheme.CommandButton(text, onClick, signalColor ?? LcdHudTheme.Phosphor);
        }
    }
}
