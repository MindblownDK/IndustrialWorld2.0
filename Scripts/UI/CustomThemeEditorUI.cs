// Assets/Scripts/VoxelEngine/UI/CustomThemeEditorUI.cs
// Full custom theme editor panel — live preview with duplication, editing, export/import.
// Premium OS-dashboard aesthetic: sliders, color fields, curve preview.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    public static class CustomThemeEditorUI
    {
        public static VisualElement BuildPanel(System.Action rebuild = null)
        {
            var panel = UITheme.Panel();
            panel.style.position = Position.Absolute;
            panel.style.left = new StyleLength(new Length(34f, LengthUnit.Percent));
            panel.style.right = 12;
            panel.style.top = 12;
            panel.style.bottom = 72;
            panel.style.width = new StyleLength(new Length(54f, LengthUnit.Percent));
            panel.style.maxWidth = new StyleLength(new Length(62f, LengthUnit.Percent));

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8;
            header.Add(UITheme.IconBadge("◐", UIThemeManager.Accent));
            var title = UITheme.Title("Custom Theme Editor");
            title.style.flexGrow = 1;
            header.Add(title);
            header.Add(UITheme.SmallButton($"Theme: {ProductionPanelThemeState.Label}", () =>
            {
                ProductionPanelThemeState.Next();
                rebuild?.Invoke();
            }, ProductionPanelThemeState.Accent));
            var (pill, _) = UITheme.StatusPill("LIVE", UIThemeManager.Accent);
            header.Add(pill);
            panel.Add(header);
            panel.Add(UITheme.AccentDivider(UIThemeManager.Accent));

            var body = new ScrollView(ScrollViewMode.Vertical);
            body.style.flexGrow = 1;
            UITheme.StyleScroller(body);
            panel.Add(body);

            // Preview card
            body.Add(BuildPreviewCard());

            body.Add(UITheme.Divider());
            body.Add(UITheme.Subtitle("Built-in Theme Selection"));
            body.Add(BuildBuiltInSelector(rebuild));

            body.Add(UITheme.Divider());
            body.Add(UITheme.Subtitle("Custom Accent Override"));
            body.Add(BuildAccentEditor(rebuild));

            body.Add(UITheme.Divider());
            body.Add(UITheme.Subtitle("Panel Shape & Opacity"));
            body.Add(BuildShapeEditor(rebuild));

            body.Add(UITheme.Divider());
            body.Add(UITheme.Subtitle("Effects & Animation"));
            body.Add(BuildEffectsEditor(rebuild));

            body.Add(UITheme.Divider());
            body.Add(UITheme.Subtitle("Import / Export / Reset"));
            body.Add(BuildImportExportRow(rebuild));

            body.Add(UITheme.Spacer(12));
            body.Add(UITheme.Muted("Tip: Use Copy Theme Code to share your palette with teammates. Importing a code instantly applies it and triggers reactive theme updates across all open panels — no reload needed."));

            return panel;
        }

        private static VisualElement BuildPreviewCard()
        {
            var card = UITheme.Card();
            card.style.borderLeftWidth = 4;
            card.style.borderLeftColor = new StyleColor(UIThemeManager.Accent);
            card.style.marginBottom = 8;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 6;

            var badge = UITheme.IconBadge("◑", UIThemeManager.Accent);
            row.Add(badge);

            var col = new VisualElement();
            col.style.flexGrow = 1;
            var title = new Label(UIThemeManager.CurrentLabel);
            title.style.color = new StyleColor(UIThemeManager.TextColor);
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            col.Add(title);
            var desc = new Label(UIThemeManager.DescriptionFor(UIThemeManager.Current));
            desc.style.color = new StyleColor(UITheme.TextSecondary);
            desc.style.fontSize = 10;
            desc.style.whiteSpace = WhiteSpace.Normal;
            col.Add(desc);
            row.Add(col);

            var accentDot = new VisualElement();
            accentDot.style.width = 18; accentDot.style.height = 18;
            accentDot.style.backgroundColor = new StyleColor(UIThemeManager.Accent);
            UITheme.Radius(accentDot, 9);
            accentDot.style.marginLeft = 8;
            row.Add(accentDot);

            card.Add(row);

            var sampleRow = new VisualElement();
            sampleRow.style.flexDirection = FlexDirection.Row;
            sampleRow.style.marginTop = 8;
            sampleRow.Add(UITheme.SmallButton("Primary Action", () => { }, UIThemeManager.Accent));
            sampleRow.Add(UITheme.Spacer(6));
            sampleRow.Add(UITheme.SmallButton("Secondary", () => { }, UITheme.TextMuted));
            sampleRow.Add(UITheme.Spacer(6));
            var (p, _) = UITheme.StatusPill("PREVIEW", UIThemeManager.Accent);
            sampleRow.Add(p);
            card.Add(sampleRow);

            var info = new VisualElement();
            info.style.flexDirection = FlexDirection.Row;
            info.style.marginTop = 8;
            info.Add(UITheme.StatRow("⬤", "Accent", $"#{ColorUtility.ToHtmlStringRGB(UIThemeManager.Accent)}", UIThemeManager.Accent));
            info.Add(UITheme.Spacer(12));
            info.Add(UITheme.StatRow("⬚", "Panel", $"{UIThemeManager.PanelColor.a:0.00} α", UITheme.TextSecondary));
            info.Add(UITheme.Spacer(12));
            info.Add(UITheme.StatRow("◍", "Radius", $"{UIThemeManager.CornerRadius:0}px", UITheme.TextSecondary));
            card.Add(info);

            return card;
        }

        private static VisualElement BuildBuiltInSelector(System.Action rebuild)
        {
            var wrap = new VisualElement();
            var themes = (BuiltInUITheme[])System.Enum.GetValues(typeof(BuiltInUITheme));
            var labels = new List<string>();
            int selected = 0;
            for (int i = 0; i < themes.Length; i++)
            {
                labels.Add(UIThemeManager.Label(themes[i]));
                if (themes[i] == UIThemeManager.Current) selected = i;
            }
            var seg = SettingsUI.Segmented(labels, selected, idx =>
            {
                UIThemeManager.Current = themes[idx];
                rebuild?.Invoke();
            });
            wrap.Add(seg);
            wrap.Add(UITheme.Muted(UIThemeManager.DescriptionFor(UIThemeManager.Current)));
            return wrap;
        }

        private static VisualElement BuildAccentEditor(System.Action rebuild)
        {
            var wrap = new VisualElement();
            var toggleRow = SettingsUI.ToggleRow("Enable Custom Accent", "Override the built-in theme accent with your own RGB color.", UIThemeManager.CustomAccentEnabled, on =>
            {
                UIThemeManager.CustomAccentEnabled = on;
                rebuild?.Invoke();
            });
            wrap.Add(toggleRow);

            if (UIThemeManager.CustomAccentEnabled)
            {
                var c = UIThemeManager.CustomAccent;
                wrap.Add(SliderRow("Accent Red", 0f, 1f, c.r, "0.00", "", v => { var col = UIThemeManager.CustomAccent; col.r = v; UIThemeManager.CustomAccent = col; }));
                wrap.Add(SliderRow("Accent Green", 0f, 1f, c.g, "0.00", "", v => { var col = UIThemeManager.CustomAccent; col.g = v; UIThemeManager.CustomAccent = col; }));
                wrap.Add(SliderRow("Accent Blue", 0f, 1f, c.b, "0.00", "", v => { var col = UIThemeManager.CustomAccent; col.b = v; UIThemeManager.CustomAccent = col; }));

                // Preset accent chips
                var chipRow = new VisualElement();
                chipRow.style.flexDirection = FlexDirection.Row;
                chipRow.style.flexWrap = Wrap.Wrap;
                chipRow.style.marginTop = 8;
                Color[] presets = new[]
                {
                    UITheme.AccentCyan, UITheme.AccentTeal, UITheme.AccentGold, UITheme.AccentAmber,
                    UITheme.AccentGreen, UITheme.AccentOrange, UITheme.AccentPurple, UITheme.AccentBlue,
                    new Color(0.95f,0.24f,0.85f), new Color(0.40f,0.52f,0.92f), new Color(0.58f,0.68f,0.36f)
                };
                foreach (var pc in presets)
                {
                    var chip = new VisualElement();
                    chip.style.width = 28; chip.style.height = 28;
                    chip.style.marginRight = 6; chip.style.marginBottom = 6;
                    chip.style.backgroundColor = new StyleColor(pc);
                    UITheme.Radius(chip, 6);
                    UITheme.Border(chip, 1, new Color(1,1,1,0.15f));
                    Color captured = pc;
                    chip.RegisterCallback<ClickEvent>(_ => { UIThemeManager.CustomAccent = captured; rebuild?.Invoke(); });
                    chipRow.Add(chip);
                }
                wrap.Add(chipRow);
            }
            return wrap;
        }

        private static VisualElement BuildShapeEditor(System.Action rebuild)
        {
            var wrap = new VisualElement();
            wrap.Add(SliderRow($"Panel Opacity — {UIThemeManager.PanelOpacity:0.00}", 0.45f, 1f, UIThemeManager.PanelOpacity, "0.00", "", v => UIThemeManager.PanelOpacity = v));
            wrap.Add(SliderRow($"Corner Radius — {UIThemeManager.CornerRadius:0}px", 2f, 24f, UIThemeManager.CornerRadius, "0", " px", v => UIThemeManager.CornerRadius = v));
            return wrap;
        }

        private static VisualElement BuildEffectsEditor(System.Action rebuild)
        {
            var wrap = new VisualElement();
            wrap.Add(SliderRow($"Accent Glow — {UIThemeManager.AccentGlow:0.00}", 0f, 1f, UIThemeManager.AccentGlow, "0.00", "", v => UIThemeManager.AccentGlow = v));
            wrap.Add(SliderRow($"Animation Speed — {UIThemeManager.AnimationSpeed:0.00}x", 0.2f, 3f, UIThemeManager.AnimationSpeed, "0.00", " x", v => UIThemeManager.AnimationSpeed = v));
            wrap.Add(UITheme.Muted("Glow controls accent border translucency and emissive intensity. Animation speed scales UI transition durations."));
            return wrap;
        }

        private static VisualElement BuildImportExportRow(System.Action rebuild)
        {
            var wrap = new VisualElement();
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 8;

            row.Add(UITheme.SmallButton("Copy Theme Code", () =>
            {
                GUIUtility.systemCopyBuffer = UIThemeManager.ExportThemeCode();
            }, UITheme.AccentGreen));

            row.Add(UITheme.SmallButton("Import Clipboard", () =>
            {
                if (UIThemeManager.TryImportThemeCode(GUIUtility.systemCopyBuffer))
                    rebuild?.Invoke();
            }, UITheme.AccentCyan));

            row.Add(UITheme.SmallButton("Duplicate as Custom", () =>
            {
                // Enable custom accent using current accent so duplication is explicit
                UIThemeManager.CustomAccentEnabled = true;
                UIThemeManager.CustomAccent = UIThemeManager.Accent;
                rebuild?.Invoke();
            }, UITheme.AccentGold));

            row.Add(UITheme.SmallButton("Reset to Default", () =>
            {
                UIThemeManager.ResetToDefault();
                rebuild?.Invoke();
            }, UITheme.AccentRed));

            wrap.Add(row);

            var codeLabel = new Label($"Current Code: {UIThemeManager.ExportThemeCode()}");
            codeLabel.style.color = new StyleColor(UITheme.TextMuted);
            codeLabel.style.fontSize = 9;
            codeLabel.style.whiteSpace = WhiteSpace.Normal;
            codeLabel.style.marginTop = 8;
            wrap.Add(codeLabel);

            return wrap;
        }

        private static VisualElement SliderRow(string label, float min, float max, float value, string fmt, string suffix, System.Action<float> onChange)
        {
            var wrap = new VisualElement();
            var readout = new Label($"{label}");
            readout.style.color = new StyleColor(UITheme.TextSecondary);
            readout.style.fontSize = 11;
            readout.style.unityFontStyleAndWeight = FontStyle.Bold;
            readout.style.marginBottom = 2;
            wrap.Add(readout);

            var s = new Slider(min, max) { value = value, showInputField = true };
            s.style.marginBottom = 6;
            s.RegisterValueChangedCallback(e =>
            {
                readout.text = $"{label.Split('—')[0].Trim()} — {e.newValue.ToString(fmt)}{suffix}";
                onChange?.Invoke(e.newValue);
            });
            SettingsUI.StyleInner(s);
            wrap.Add(s);
            return wrap;
        }
    }
}
