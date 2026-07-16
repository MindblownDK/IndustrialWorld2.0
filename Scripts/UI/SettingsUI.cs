// Assets/Scripts/VoxelEngine/UI/SettingsUI.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║            INDUSTRIAL WORLD — SHARED SETTINGS SURFACE          ║
// ║                                                                  ║
// ║  ONE premium settings builder consumed by BOTH the main menu    ║
// ║  and the in-game pause menu, so the two can never drift apart.   ║
// ║                                                                  ║
// ║  Polished widgets:                                               ║
// ║   • Segmented chooser  → graphical Quality (Low/Med/High/Ultra)  ║
// ║                          and Window Mode selectors.             ║
// ║   • Toggle switch      → VSync on/off, Invert-Y.                ║
// ║   • Styled dropdowns   → Monitor / Resolution / Refresh Rate.   ║
// ║   • Value sliders      → View Distance, FOV, Sensitivity,       ║
// ║                          Master Volume (now 0–100, not 0–1).    ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Settings;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    /// <summary>
    /// Stateless builders that populate a settings tab body with our themed,
    /// micro-interactive controls. Both menu controllers route through here.
    /// </summary>
    public static class SettingsUI
    {
        // ════════════════════════════════════════════════════════════
        //  TABS
        // ════════════════════════════════════════════════════════════

        /// <summary>Display / graphics: quality, window mode, resolution, vsync…</summary>
        public static void DisplayTab(VisualElement p, Action rebuild)
        {
            // ── Graphics Quality (graphical chooser) ──────────────────
            p.Add(SectionLabel("Graphics Quality"));
            string[] qNames = QualitySettings.names;
            int curQ = GameSettings.Quality;
            if (curQ < 0 || curQ >= qNames.Length) curQ = QualitySettings.GetQualityLevel();
            p.Add(Segmented(qNames, curQ, i => { GameSettings.Quality = i; rebuild?.Invoke(); }));
            p.Add(Hint("Higher presets enable richer shadows, draw distance and post-processing."));
            p.Add(T.Divider());

            // ── VSync (on/off toggle) ─────────────────────────────────
            p.Add(ToggleRow("VSync", "Caps the framerate to your monitor to remove screen tearing.",
                GameSettings.VSync > 0,
                on => { GameSettings.VSync = on ? 1 : 0; rebuild?.Invoke(); }));
            p.Add(T.Divider());

            // ── Window Mode (graphical chooser) ───────────────────────
            p.Add(SectionLabel("Window Mode"));
            var modes = new[] { "Fullscreen", "Borderless", "Windowed" };
            var modeValues = new[] { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
            int curMode = Array.IndexOf(modeValues, GameSettings.FullscreenMode);
            if (curMode < 0) curMode = 1; // default borderless
            p.Add(Segmented(modes, curMode, i => { GameSettings.FullscreenMode = modeValues[i]; rebuild?.Invoke(); }));
            p.Add(T.Spacer(10));

            // ── Resolution (dropdown) ─────────────────────────────────
            BuildResolutionDropdown(p, rebuild);

            // ── Refresh Rate (dropdown) ───────────────────────────────
            BuildRefreshRateDropdown(p, rebuild);

            // ── Monitor (dropdown, only if more than one is connected) ─
            if (Display.displays != null && Display.displays.Length > 1)
            {
                var monitors = new List<string>();
                for (int i = 0; i < Display.displays.Length; i++) monitors.Add($"Display {i + 1}");
                int curMon = Mathf.Clamp(GameSettings.DisplayIndex, 0, monitors.Count - 1);
                p.Add(DropdownRow("Monitor", monitors, curMon,
                    i => { GameSettings.DisplayIndex = i; rebuild?.Invoke(); }));
            }

            p.Add(T.Divider());

            // ── View Distance (slider) ────────────────────────────────
            p.Add(IntSliderRow("View Distance", 2, 16, GameSettings.ViewDistance, " chunks",
                v => GameSettings.ViewDistance = v));
        }

        /// <summary>Camera / input feel.</summary>
        public static void CameraTab(VisualElement p, Action rebuild)
        {
            p.Add(FloatSliderRow("Field of View", 40f, 120f, GameSettings.Fov, "0", "°",
                v => GameSettings.Fov = v));
            p.Add(T.Spacer(12));
            p.Add(FloatSliderRow("Mouse Sensitivity", 0.02f, 1.5f, GameSettings.MouseSensitivity, "0.00", "",
                v => GameSettings.MouseSensitivity = v));
            p.Add(T.Divider());
            p.Add(ToggleRow("Invert Y-Axis", "Flip vertical look direction.",
                GameSettings.InvertY, on => GameSettings.InvertY = on));
        }

        /// <summary>Audio — Master / Music / SFX, all on a clean 0–100 scale.</summary>
        public static void AudioTab(VisualElement p, Action rebuild)
        {
            p.Add(PercentSliderRow("Master Volume", Mathf.RoundToInt(GameSettings.MasterVolume * 100f),
                v => GameSettings.MasterVolume = v / 100f));
            p.Add(T.Spacer(12));
            p.Add(PercentSliderRow("Music Volume", Mathf.RoundToInt(GameSettings.MusicVolume * 100f),
                v => GameSettings.MusicVolume = v / 100f));
            p.Add(T.Spacer(12));
            p.Add(PercentSliderRow("SFX Volume", Mathf.RoundToInt(GameSettings.SfxVolume * 100f),
                v => GameSettings.SfxVolume = v / 100f));

            if (!VoxelEngine.FX.AudioManager.HasMixer)
            {
                p.Add(T.Spacer(10));
                p.Add(Hint("Music & SFX channels become independent once the GameAudioMixer " +
                           "asset is added. Until then, Master controls overall volume."));
            }
        }

        /// <summary>Interface theming and production-planner presentation.</summary>
        public static void InterfaceTab(VisualElement p, Action rebuild)
        {
            p.Add(SectionLabel("Global UI Theme"));
            var themes = (BuiltInUITheme[])Enum.GetValues(typeof(BuiltInUITheme));
            var labels = new List<string>();
            int selected = 0;
            for (int i = 0; i < themes.Length; i++)
            {
                labels.Add(UIThemeManager.Label(themes[i]));
                if (themes[i] == UIThemeManager.Current) selected = i;
            }
            p.Add(Segmented(labels, selected, i => { UIThemeManager.Current = themes[i]; rebuild?.Invoke(); }));
            p.Add(Hint("This starts the theme pipeline with persistent theme selection. Production planning panels already use themed accent colors."));
            p.Add(ThemePreview());
            p.Add(T.SmallButton("Reset Interface Theme", () => { UIThemeManager.Current = BuiltInUITheme.IndustrialSteel; rebuild?.Invoke(); }, T.AccentRed));
            p.Add(T.Divider());

            p.Add(SectionLabel("Production Panel Accent"));
            var prodLabels = new List<string> { "Steel", "Amber", "Cyan", "Violet" };
            int prodIndex = ProductionPanelThemeState.Current switch
            {
                ProductionPanelTheme.AmberFactory => 1,
                ProductionPanelTheme.CyanLogistics => 2,
                ProductionPanelTheme.VioletResearch => 3,
                _ => 0
            };
            p.Add(Segmented(prodLabels, prodIndex, i =>
            {
                while ((int)ProductionPanelThemeState.Current != i) ProductionPanelThemeState.Next();
                rebuild?.Invoke();
            }));
            p.Add(Hint("Overrides Recipe Browser and Production Statistics accent colors without affecting gameplay."));
        }

        private static VisualElement ThemePreview()
        {
            var preview = T.Card();
            preview.style.marginTop = 8;
            preview.style.marginBottom = 8;
            preview.style.borderLeftWidth = 4;
            preview.style.borderLeftColor = new StyleColor(UIThemeManager.Accent);
            var title = new Label(UIThemeManager.CurrentLabel);
            title.style.color = new StyleColor(UIThemeManager.TextColor);
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            preview.Add(title);
            var body = new Label("Preview · accent, panel border, and text tone update immediately.");
            body.style.color = new StyleColor(UIThemeManager.TextColor);
            body.style.fontSize = 10;
            body.style.whiteSpace = WhiteSpace.Normal;
            preview.Add(body);
            return preview;
        }

        /// <summary>Saving — autosave cadence chooser.</summary>
        public static void SavingTab(VisualElement p, Action rebuild)
        {
            p.Add(SectionLabel("Autosave Interval"));

            var labels = new List<string>();
            int cur = GameSettings.AutosaveSeconds;
            int curIdx = 0;
            for (int i = 0; i < GameSettings.AUTOSAVE_CHOICES.Length; i++)
            {
                int s = GameSettings.AUTOSAVE_CHOICES[i];
                labels.Add(s <= 0 ? "Off" : (s < 60 ? $"{s}s" : $"{s / 60}m"));
                if (s == cur) curIdx = i;
            }

            p.Add(Segmented(labels, curIdx, i =>
            {
                GameSettings.AutosaveSeconds = GameSettings.AUTOSAVE_CHOICES[i];
                rebuild?.Invoke();
            }));
            p.Add(Hint(cur <= 0
                ? "Autosave is OFF — the world only saves on quit / return to menu. Save often!"
                : $"The world autosaves in the background every {(cur < 60 ? cur + " seconds" : cur / 60 + " minute(s)")}."));
        }

        /// <summary>Keybinds — one rebindable row per action.</summary>
        public static void KeybindTab(VisualElement p, MonoBehaviour host, Action rebuild)
        {
            foreach (InputAction a in Enum.GetValues(typeof(InputAction)))
            {
                var row = new VisualElement();
                row.style.flexDirection   = FlexDirection.Row;
                row.style.alignItems      = Align.Center;
                row.style.marginBottom    = 5;
                row.style.paddingTop      = 6;
                row.style.paddingBottom   = 6;
                row.style.paddingLeft     = 10;
                row.style.paddingRight    = 10;
                row.style.backgroundColor = new StyleColor(T.BgCard);
                T.Radius(row, 5f);

                var lbl = new Label(Prettify(a.ToString()));
                lbl.style.color    = new StyleColor(T.TextSecondary);
                lbl.style.fontSize = 12;
                lbl.style.flexGrow = 1;
                lbl.style.minHeight = 22;
                row.Add(lbl);

                var btn = T.SmallButton(GameSettings.GetKey(a), null, T.AccentTeal);
                btn.style.minWidth = 120;
                btn.clickable.clicked += () =>
                {
                    btn.text = "Press key…";
                    btn.style.backgroundColor = new StyleColor(
                        new Color(T.AccentGold.r, T.AccentGold.g, T.AccentGold.b, 0.80f));
                    var cap = host.gameObject.AddComponent<KeyRebindCapture>();
                    cap.onCaptured = code => { GameSettings.SetKey(a, code); rebuild?.Invoke(); };
                };
                row.Add(btn);
                p.Add(row);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  WIDGETS
        // ════════════════════════════════════════════════════════════

        /// <summary>Bold section heading used between control groups.</summary>
        private static Label SectionLabel(string text)
        {
            var l = T.Subtitle(text);
            l.style.marginBottom = 6;
            return l;
        }

        private static Label Hint(string text)
        {
            var l = T.Muted(text);
            l.style.marginTop = 4;
            return l;
        }

        // ── Segmented chooser (graphical Low/Med/High/Ultra style) ────
        /// <summary>
        /// Row of connected pill buttons; the selected one glows with the cyan
        /// accent. Equal-width segments, full hover/press micro-interactions.
        /// </summary>
        public static VisualElement Segmented(IReadOnlyList<string> options, int selected, Action<int> onSelect)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap      = Wrap.Wrap;

            for (int i = 0; i < options.Count; i++)
            {
                bool active = i == selected;
                int idx = i;

                var seg = new VisualElement();
                seg.style.flexGrow        = 1;
                seg.style.flexBasis       = 0;
                seg.style.minWidth        = 70;
                seg.style.height          = 38;
                seg.style.marginRight     = 4;
                seg.style.marginBottom    = 4;
                seg.style.alignItems      = Align.Center;
                seg.style.justifyContent  = Justify.Center;
                Color segBg = active
                    ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.20f)
                    : T.BgSlot;
                seg.style.backgroundColor = new StyleColor(segBg);
                T.Radius(seg, T.ButtonRadius);
                T.Border(seg, active ? 2 : 1, active ? T.AccentCyan : T.BorderDim);

                var l = new Label(Prettify(options[i]));
                l.style.fontSize = 12;
                l.style.unityFontStyleAndWeight = FontStyle.Bold;
                l.style.letterSpacing = 0.4f;
                l.style.color = new StyleColor(active ? T.TextPrimary : T.TextSecondary);
                l.pickingMode = PickingMode.Ignore;
                seg.Add(l);

                // micro-interaction
                seg.style.transitionProperty = new List<StylePropertyName> { "scale", "background-color" };
                seg.style.transitionDuration = new List<TimeValue> { new TimeValue(0.10f, TimeUnit.Second), new TimeValue(0.10f, TimeUnit.Second) };
                seg.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    seg.style.scale = new StyleScale(new Scale(new Vector3(1.03f, 1.03f, 1f)));
                    if (!active) { seg.style.backgroundColor = new StyleColor(T.BgHover); T.Border(seg, 1, T.BorderBright); }
                });
                seg.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    seg.style.scale = new StyleScale(new Scale(Vector3.one));
                    if (!active) { seg.style.backgroundColor = new StyleColor(segBg); T.Border(seg, 1, T.BorderDim); }
                });
                VoxelEngine.FX.UiAudio.MarkClickable(seg);
                seg.RegisterCallback<ClickEvent>(_ => onSelect?.Invoke(idx));

                row.Add(seg);
            }
            return row;
        }

        // ── Toggle row (label + description left, switch right) ────────
        public static VisualElement ToggleRow(string label, string desc, bool value, Action<bool> onToggle)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;

            var col = new VisualElement();
            col.style.flexGrow = 1;
            var l = new Label(label);
            l.style.color = new StyleColor(T.TextPrimary);
            l.style.fontSize = 13;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            col.Add(l);
            if (!string.IsNullOrEmpty(desc))
            {
                var d = T.Muted(desc);
                d.style.marginTop = 1;
                col.Add(d);
            }
            row.Add(col);

            row.Add(Switch(value, onToggle));
            return row;
        }

        /// <summary>Premium pill on/off switch with sliding knob + colour shift.</summary>
        public static VisualElement Switch(bool value, Action<bool> onToggle)
        {
            bool state = value;

            // Geometry (kept as named constants so the knob is always perfectly
            // centred regardless of future size tweaks).
            const float TrackW = 52f, TrackH = 28f, Border = 1f;
            const float Knob   = 22f;
            // Inner padding so the knob never touches the border. Vertically this
            // is what centres the knob: gap top == gap bottom.
            const float Pad    = (TrackH - 2f * Border - Knob) / 2f; // → 2px

            var track = new VisualElement();
            track.style.width      = TrackW;
            track.style.height     = TrackH;
            track.style.flexShrink = 0;
            // Use flex layout to vertically centre the knob — no absolute top math.
            track.style.flexDirection = FlexDirection.Row;
            track.style.alignItems    = Align.Center;
            track.style.paddingLeft   = Pad;
            track.style.paddingRight  = Pad;
            T.Radius(track, TrackH / 2f);

            var knob = new VisualElement();
            knob.style.width  = Knob;
            knob.style.height = Knob;
            T.Radius(knob, Knob / 2f);
            knob.style.backgroundColor = new StyleColor(Color.white);
            knob.pickingMode = PickingMode.Ignore;
            // Slide horizontally via translate (the flexbox keeps it centred Y).
            knob.style.transitionProperty = new List<StylePropertyName> { "translate" };
            knob.style.transitionDuration = new List<TimeValue> { new TimeValue(0.12f, TimeUnit.Second) };
            track.Add(knob);

            // Distance the knob travels between the two states.
            float travel = TrackW - 2f * Border - 2f * Pad - Knob; // → 24px

            void Apply()
            {
                Color on  = T.AccentGreen;
                Color off = new Color(0.30f, 0.32f, 0.38f);
                track.style.backgroundColor = new StyleColor(state
                    ? new Color(on.r, on.g, on.b, 0.40f)
                    : new Color(off.r, off.g, off.b, 0.55f));
                T.Border(track, Border, state ? new Color(on.r, on.g, on.b, 0.80f) : T.BorderDim);
                knob.style.translate = new StyleTranslate(new Translate(
                    new Length(state ? travel : 0f, LengthUnit.Pixel),
                    new Length(0f, LengthUnit.Pixel), 0f));
            }
            Apply();

            VoxelEngine.FX.UiAudio.MarkClickable(track);
            track.RegisterCallback<ClickEvent>(_ => { state = !state; Apply(); onToggle?.Invoke(state); });
            return track;
        }

        // ── Dropdown row ──────────────────────────────────────────────
        public static VisualElement DropdownRow(string label, List<string> choices, int index, Action<int> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 8;

            var l = new Label(label);
            l.style.color = new StyleColor(T.TextSecondary);
            l.style.fontSize = 12;
            l.style.flexGrow = 1;
            row.Add(l);

            var dd = new DropdownField { choices = choices, index = Mathf.Clamp(index, 0, Mathf.Max(0, choices.Count - 1)) };
            dd.style.width = 220; dd.style.height = 30; dd.style.fontSize = 12;
            dd.RegisterValueChangedCallback(e =>
            {
                int ix = choices.IndexOf(e.newValue);
                if (ix >= 0) onChange?.Invoke(ix);
            });
            StyleInner(dd);
            row.Add(dd);
            return row;
        }

        // ── Slider rows (label + live readout above the slider) ───────
        public static VisualElement IntSliderRow(string label, int min, int max, int value, string suffix, Action<int> onChange)
        {
            var wrap = new VisualElement();
            var readout = ReadoutLabel($"{label}  —  {value}{suffix}");
            wrap.Add(readout);

            var s = new SliderInt(min, max) { value = value, showInputField = true };
            s.style.marginBottom = 4;
            s.RegisterValueChangedCallback(e =>
            {
                readout.text = $"{label}  —  {e.newValue}{suffix}";
                onChange?.Invoke(e.newValue);
            });
            StyleInner(s);
            wrap.Add(s);
            return wrap;
        }

        public static VisualElement FloatSliderRow(string label, float min, float max, float value, string fmt, string suffix, Action<float> onChange)
        {
            var wrap = new VisualElement();
            var readout = ReadoutLabel($"{label}  —  {value.ToString(fmt)}{suffix}");
            wrap.Add(readout);

            var s = new Slider(min, max) { value = value, showInputField = true };
            s.style.marginBottom = 4;
            s.RegisterValueChangedCallback(e =>
            {
                readout.text = $"{label}  —  {e.newValue.ToString(fmt)}{suffix}";
                onChange?.Invoke(e.newValue);
            });
            StyleInner(s);
            wrap.Add(s);
            return wrap;
        }

        public static VisualElement PercentSliderRow(string label, int value, Action<int> onChange)
            => IntSliderRow(label, 0, 100, value, "%", onChange);

        private static Label ReadoutLabel(string text)
        {
            var l = new Label(text);
            l.style.color    = new StyleColor(T.TextSecondary);
            l.style.fontSize = 12;
            l.style.minHeight = 20;
            l.style.marginBottom = 3;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        // ════════════════════════════════════════════════════════════
        //  RESOLUTION / REFRESH-RATE HELPERS
        // ════════════════════════════════════════════════════════════
        private static void BuildResolutionDropdown(VisualElement p, Action rebuild)
        {
            var res = Screen.resolutions;
            var seen = new HashSet<long>();
            var dims = new List<Vector2Int>();
            foreach (var r in res)
            {
                long key = ((long)r.width << 32) | (uint)r.height;
                if (seen.Add(key)) dims.Add(new Vector2Int(r.width, r.height));
            }
            // Fallback when the platform reports nothing (e.g. some editor states).
            if (dims.Count == 0)
                dims.Add(new Vector2Int(Screen.width, Screen.height));

            var choices = new List<string>();
            int curIdx = 0;
            for (int i = 0; i < dims.Count; i++)
            {
                choices.Add($"{dims[i].x} × {dims[i].y}");
                if (dims[i].x == GameSettings.ResolutionWidth && dims[i].y == GameSettings.ResolutionHeight)
                    curIdx = i;
            }

            p.Add(DropdownRow("Resolution", choices, curIdx, i =>
            {
                GameSettings.ResolutionWidth  = dims[i].x;
                GameSettings.ResolutionHeight = dims[i].y;
                rebuild?.Invoke();
            }));
        }

        private static void BuildRefreshRateDropdown(VisualElement p, Action rebuild)
        {
            var res = Screen.resolutions;
            var rates = new SortedSet<int>();
            foreach (var r in res)
            {
                int hz = Mathf.RoundToInt((float)r.refreshRateRatio.value);
                if (hz > 0) rates.Add(hz);
            }
            if (rates.Count == 0)
                rates.Add(Mathf.Max(1, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value)));

            var list = new List<int>(rates);
            var choices = new List<string>();
            int curIdx = 0;
            for (int i = 0; i < list.Count; i++)
            {
                choices.Add($"{list[i]} Hz");
                if (list[i] == GameSettings.RefreshRate) curIdx = i;
            }

            p.Add(DropdownRow("Refresh Rate", choices, curIdx, i =>
            {
                GameSettings.RefreshRate = list[i];
                rebuild?.Invoke();
            }));
        }

        // ════════════════════════════════════════════════════════════
        //  TEXT-COLOUR FIX FOR DARK BACKGROUNDS
        // ════════════════════════════════════════════════════════════
        /// <summary>
        /// Forces every text-rendering descendant of a slider / dropdown to use
        /// our theme colour. Unity's input controls are deep trees where `color`
        /// does not cascade onto the inner TextElement that draws glyphs.
        /// </summary>
        public static void StyleInner(VisualElement field)
        {
            if (field == null) return;

            void Apply(VisualElement root)
            {
                var input = root.Q(className: "unity-base-text-field__input")
                            ?? root.Q("unity-text-input");
                if (input != null)
                {
                    input.style.color           = new StyleColor(T.TextPrimary);
                    input.style.backgroundColor = new StyleColor(T.BgCard);
                    input.style.unityTextAlign  = TextAnchor.MiddleLeft;
                }
                root.Query<TextElement>().ForEach(te => te.style.color = new StyleColor(T.TextPrimary));
            }

            Apply(field);
            field.RegisterCallback<AttachToPanelEvent>(_ => Apply(field));
            field.RegisterCallback<GeometryChangedEvent>(_ => Apply(field));
            field.RegisterCallback<ChangeEvent<string>>(_ => Apply(field));
            field.RegisterCallback<ChangeEvent<int>>(_ => Apply(field));
            field.RegisterCallback<ChangeEvent<float>>(_ => Apply(field));
        }

        // ── Pretty-print enum / camelCase names → "Mouse Sensitivity" etc. ──
        private static string Prettify(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var sb = new System.Text.StringBuilder(raw.Length + 4);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(raw[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
