// Assets/Scripts/VoxelEngine/UI/UITheme.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          INDUSTRIAL WORLD — UNIFIED UI DESIGN SYSTEM           ║
// ║   Premium dark-steel OS dashboard aesthetic with amber/cyan    ║
// ║   accent language, micro-interaction helpers, and easing.      ║
// ╚══════════════════════════════════════════════════════════════════╝
//
// Every UI builder routes through this class. Never hard-code colours,
// sizes, or border widths outside of UITheme.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;

namespace VoxelEngine.UI
{
    public static class UITheme
    {
        // ── Core Palette ──────────────────────────────────────────────────
        // Steel-dark backgrounds — layered depth system
        public static readonly Color BgBase       = new(0.04f, 0.045f, 0.06f, 1.00f);   // deepest layer
        public static readonly Color BgDark       = new(0.06f, 0.065f, 0.09f, 0.98f);   // panel backdrop
        public static readonly Color BgPanel      = new(0.08f, 0.088f, 0.12f, 0.97f);   // panel face
        public static readonly Color BgCard       = new(0.065f, 0.07f, 0.10f, 0.98f);   // card / slot cluster
        public static readonly Color BgSlot       = new(0.09f, 0.10f, 0.14f, 0.96f);    // individual slot
        public static readonly Color BgHover      = new(0.13f, 0.15f, 0.21f, 0.95f);    // hover state
        public static readonly Color BgActive     = new(0.10f, 0.18f, 0.24f, 0.98f);    // pressed / selected

        // Borders — 3-tier hierarchy
        public static readonly Color BorderSubtle = new(0.13f, 0.15f, 0.20f, 0.70f);   // hairline separators
        public static readonly Color BorderDim    = new(0.18f, 0.21f, 0.28f, 0.85f);   // default border
        public static readonly Color BorderBright = new(0.22f, 0.50f, 0.72f, 0.55f);   // teal accent border
        public static readonly Color BorderGold   = new(0.82f, 0.66f, 0.20f, 0.55f);   // amber accent border
        public static readonly Color BorderRed    = new(0.75f, 0.22f, 0.18f, 0.55f);   // danger border

        // Accent palette — muted luxury tones
        public static readonly Color AccentCyan   = new(0.18f, 0.72f, 0.88f);
        public static readonly Color AccentTeal   = new(0.12f, 0.60f, 0.68f);
        public static readonly Color AccentGold   = new(0.88f, 0.72f, 0.22f);
        public static readonly Color AccentAmber  = new(0.92f, 0.60f, 0.12f);
        public static readonly Color AccentRed    = new(0.82f, 0.22f, 0.18f);
        public static readonly Color AccentGreen  = new(0.22f, 0.78f, 0.42f);
        public static readonly Color AccentOrange = new(0.88f, 0.52f, 0.12f);
        public static readonly Color AccentPurple = new(0.58f, 0.30f, 0.84f);
        public static readonly Color AccentBlue   = new(0.20f, 0.50f, 0.92f);
        /// <summary>Low-emphasis "informational / inactive" accent used by toast HUDs
        /// when reporting non-actionable status (e.g. "No connections"). Reads as a
        /// soft, desaturated steel-grey so it never competes with success/danger pills.</summary>
        public static readonly Color AccentDim    = new(0.46f, 0.50f, 0.58f);

        // Typography hierarchy
        public static readonly Color TextPrimary   = new(0.92f, 0.94f, 0.97f);
        public static readonly Color TextSecondary = new(0.62f, 0.67f, 0.76f);
        public static readonly Color TextMuted     = new(0.40f, 0.44f, 0.52f);
        public static readonly Color TextAccent    = AccentCyan;
        public static readonly Color TextDanger    = new(0.90f, 0.45f, 0.40f);

        // ── Spacing & Sizing Constants ────────────────────────────────────
        public const float PanelPaddingH   = 22f;
        public const float PanelPaddingV   = 20f;
        public const float PanelRadius     = 12f;
        public const float CardRadius      = 8f;
        public const float ButtonRadius    = 6f;
        public const float PillRadius      = 12f;

        // ── Panel Builders ────────────────────────────────────────────────

        /// <summary>Primary content panel with theme-reactive glow border and inset shadow feel. Uses ThemedPanel tokens.</summary>
        public static VisualElement Panel()
        {
            var v = new VisualElement();
            v.style.paddingTop    = PanelPaddingV;
            v.style.paddingBottom = PanelPaddingV;
            v.style.paddingLeft   = PanelPaddingH;
            v.style.paddingRight  = PanelPaddingH;
            v.style.backgroundColor = new StyleColor(UIThemeManager.PanelColor);
            Radius(v, UIThemeManager.CornerRadius);
            Color accent = UIThemeManager.Accent;
            float glow = UIThemeManager.AccentGlow;
            Border(v, 1, new Color(accent.r, accent.g, accent.b, 0.35f + glow * 0.4f));
            LcdHudTheme.AddBezelAccents(v, new Color(accent.r, accent.g, accent.b, 0.42f));
            // Every themed panel plays the shared LCD boot + phosphor wipe so the
            // whole game — chests, containers, machines, menus — speaks one language.
            LcdHudTheme.AnimateScreenBoot(v);
            LcdHudTheme.AnimateBootSweep(v);
            LcdHudTheme.ApplyPanelDepth(v);
            v.AddToClassList("themed-panel");
            return v;
        }

        /// <summary>Right-side machine panel — safe-screen responsive dock.</summary>
        public static VisualElement MachinePanel()
        {
            var p = Panel();
            p.style.position = Position.Absolute;
            // Clear the bottom hotbar/vitals strip so a machine panel never covers
            // the hotbar or overlaps the bottom-right vitals cluster.
            p.style.top      = 12;
            p.style.bottom   = 92;
            p.style.right    = 14;
            p.style.width    = new StyleLength(new Length(34f, LengthUnit.Percent));
            p.style.minWidth = 280;
            p.style.maxWidth = new StyleLength(new Length(44f, LengthUnit.Percent));
            p.style.overflow = Overflow.Hidden;
            AddScanlines(p, 6, 25f, 45f);
            return p;
        }

        /// <summary>Card surface — lighter than panel, used for subsections.</summary>
        public static VisualElement Card()
        {
            var v = new VisualElement();
            v.style.paddingTop    = 12;
            v.style.paddingBottom = 12;
            v.style.paddingLeft   = 14;
            v.style.paddingRight  = 14;
            v.style.backgroundColor = new StyleColor(BgCard);
            Radius(v, CardRadius);
            Border(v, 1, BorderDim);
            return v;
        }

        // ── Typography ────────────────────────────────────────────────────

        /// <summary>Panel title — 18px bold, theme-reactive.</summary>
        public static Label Title(string text)
        {
            var l = new Label(text);
            l.style.color                       = new StyleColor(UIThemeManager.TextColor);
            l.style.fontSize                    = 17;
            l.style.unityFontStyleAndWeight     = FontStyle.Bold;
            l.style.letterSpacing               = 1.5f;
            l.style.minHeight                   = 26;
            l.pickingMode = PickingMode.Ignore;
            l.AddToClassList("themed-title");
            return l;
        }

        /// <summary>Section subtitle — themed accent, spaced caps feel.</summary>
        public static Label Subtitle(string text)
        {
            var l = new Label(text.ToUpper());
            l.style.color                   = new StyleColor(UIThemeManager.Accent);
            l.style.fontSize                = 10;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.letterSpacing           = 1.8f;
            l.style.minHeight               = 20;
            l.style.marginTop               = 4;
            l.pickingMode = PickingMode.Ignore;
            l.AddToClassList("themed-subtitle");
            return l;
        }

        /// <summary>Body text — secondary colour, wraps normally.</summary>
        public static Label Body(string text)
        {
            var l = new Label(text);
            l.style.color       = new StyleColor(TextSecondary);
            l.style.fontSize    = 12;
            l.style.whiteSpace  = WhiteSpace.Normal;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        /// <summary>
        /// Compact inline label used inside dense form rows (e.g. "Direction:",
        /// "Network Type:" inside the PortConfig widget). Uses TextSecondary at the
        /// supplied size so it sits visually below Body/Subtitle but above Muted.
        /// </summary>
        public static Label Label(string text, int fontSize = 11)
        {
            var l = new Label(text);
            l.style.color    = new StyleColor(TextSecondary);
            l.style.fontSize = fontSize;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginRight = 4;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        /// <summary>Muted fine-print label — 10px, dim colour.</summary>
        public static Label Muted(string text)
        {
            var l = new Label(text);
            l.style.color      = new StyleColor(TextMuted);
            l.style.fontSize   = 10;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop  = 4;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        /// <summary>Stat value label — bold, optional colour override.</summary>
        public static Label StatLabel(string text, Color? color = null)
        {
            var l = new Label(text);
            l.style.color                   = new StyleColor(color ?? TextSecondary);
            l.style.fontSize                = 11;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        // ── Status Pill ───────────────────────────────────────────────────

        /// <summary>
        /// Compact rounded status badge with pulsing indicator dot.
        /// Returns (pill container, text label) for live-update access.
        /// </summary>
        public static (VisualElement pill, Label label) StatusPill(string text, Color bg)
        {
            var pill = new VisualElement();
            pill.style.flexDirection  = FlexDirection.Row;
            pill.style.alignItems     = Align.Center;
            pill.style.paddingLeft    = 9;
            pill.style.paddingRight   = 11;
            pill.style.paddingTop     = 3;
            pill.style.paddingBottom  = 3;
            pill.style.height         = 22;
            pill.style.backgroundColor = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.22f));
            Radius(pill, PillRadius);
            Border(pill, 1, new Color(bg.r, bg.g, bg.b, 0.55f));
            pill.pickingMode = PickingMode.Ignore;

            // Accent dot
            var dot = new VisualElement();
            dot.style.width           = 6;
            dot.style.height          = 6;
            dot.style.backgroundColor = new StyleColor(bg);
            Radius(dot, 3f);
            dot.style.marginRight     = 6;
            dot.pickingMode           = PickingMode.Ignore;
            pill.Add(dot);

            var l = new Label(text);
            l.style.color                   = new StyleColor(bg);
            l.style.fontSize                = 10;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.letterSpacing           = 1.2f;
            // Centre the text vertically inside the pill — without this the
            // label defaults to TopLeft, which made "RUNNING" sit at the very
            // top of the pill (visible bug in the user's screenshot where the
            // text was clipped above the rounded border).
            l.style.unityTextAlign          = TextAnchor.MiddleCenter;
            l.style.height                  = new StyleLength(new Length(100f, LengthUnit.Percent));
            l.style.flexShrink              = 0;
            l.pickingMode = PickingMode.Ignore;
            pill.Add(l);

            pill.schedule.Execute(() =>
            {
                float alpha = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 4.2f));
                dot.style.backgroundColor = new StyleColor(new Color(bg.r, bg.g, bg.b, alpha));
            }).Every(75);

            return (pill, l);
        }

        /// <summary>
        /// Triggers a retro-futuristic phosphor LCD boot-up animation on a panel or screen.
        /// </summary>
        public static void AnimatePanelBoot(VisualElement panel, float delaySeconds = 0f, System.Action onComplete = null)
        {
            LcdHudTheme.AnimateScreenBoot(panel, delaySeconds, onComplete);
        }

        /// <summary>
        /// Attaches subtle animated scanlines to any themed panel or display area.
        /// </summary>
        public static void AddScanlines(VisualElement panel, int count = 4, float top = 10f, float spacing = 15f)
        {
            LcdHudTheme.AddAnimatedScanlines(panel, count, top, spacing);
        }

        /// <summary>
        /// Compact ON/OFF toggle pill — pairs with <see cref="StatusPill"/>.
        /// Hook <paramref name="onToggle"/> for click handling. The returned
        /// pill reflects <paramref name="initial"/> state on first render.
        /// </summary>
        public static (VisualElement pill, Label label) MachineToggle(
            bool initial, System.Action<bool> onToggle, string onText = "ENABLED", string offText = "DISABLED")
        {
            bool state = initial;
            Color onColor  = AccentGreen;
            Color offColor = AccentRed;

            var pill = new VisualElement();
            pill.style.flexDirection = FlexDirection.Row;
            pill.style.alignItems    = Align.Center;
            pill.style.paddingLeft   = 9;
            pill.style.paddingRight  = 11;
            pill.style.paddingTop    = 3;
            pill.style.paddingBottom = 3;
            pill.style.height        = 22;
            pill.style.marginRight   = 8; // gap before the status pill on the right
            Radius(pill, PillRadius);

            var dot = new VisualElement();
            dot.style.width  = 6; dot.style.height = 6;
            Radius(dot, 3f);
            dot.style.marginRight = 6;
            dot.pickingMode = PickingMode.Ignore;
            pill.Add(dot);

            var label = new Label();
            label.style.fontSize                = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.letterSpacing           = 1.2f;
            label.style.unityTextAlign          = TextAnchor.MiddleCenter;
            label.style.height                  = new StyleLength(new Length(100f, LengthUnit.Percent));
            label.style.flexShrink              = 0;
            label.pickingMode = PickingMode.Ignore;
            pill.Add(label);

            void Apply()
            {
                Color c = state ? onColor : offColor;
                pill.style.backgroundColor = new StyleColor(new Color(c.r, c.g, c.b, 0.22f));
                Border(pill, 1, new Color(c.r, c.g, c.b, 0.70f));
                dot.style.backgroundColor = new StyleColor(c);
                label.style.color = new StyleColor(c);
                label.text = state ? onText : offText;
            }
            Apply();

            // Hover sheen + click toggle.
            pill.RegisterCallback<PointerEnterEvent>(_ =>
            {
                Color c = state ? onColor : offColor;
                pill.style.backgroundColor = new StyleColor(new Color(c.r, c.g, c.b, 0.35f));
            });
            pill.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                Color c = state ? onColor : offColor;
                pill.style.backgroundColor = new StyleColor(new Color(c.r, c.g, c.b, 0.22f));
            });
            pill.RegisterCallback<ClickEvent>(_ =>
            {
                state = !state;
                Apply();
                onToggle?.Invoke(state);
            });

            return (pill, label);
        }

        // ── Progress Bar ──────────────────────────────────────────────────

        /// <summary>
        /// Slim progress bar with gradient fill and rounded caps.
        /// Returns (track, fill) — caller can animate fill.width to update.
        /// </summary>
        /// <summary>
        /// Fixed-width progress bar (240px) for machine panels.
        /// Pass flexGrow=true only for crafting progress bars inside recipe rows.
        /// </summary>
        public static (VisualElement bar, VisualElement fill) ProgressBar(
            float t, Color fillColor, float height = 8, bool flexGrow = false)
        {
            // Hard cap: machine UIs should never get a bar taller than 8px regardless
            // of what the caller asked for. Crafting/recipe rows pass flexGrow=true
            // and explicitly want their 8px bar to span the row width — that still
            // works, the cap only applies to height.
            const float MAX_BAR_HEIGHT = 8f;
            float h = Mathf.Min(Mathf.Max(2f, height), MAX_BAR_HEIGHT);

            // Wrapper row gives us a stable parent that the inner bar can't escape.
            // The wrapper either takes a fixed width (240px) or fills its parent row
            // horizontally; in BOTH cases it stays exactly h pixels tall.
            var wrapper = new VisualElement();
            wrapper.style.height        = h;
            wrapper.style.minHeight     = h;
            wrapper.style.maxHeight     = h;
            wrapper.style.flexShrink    = 0;
            wrapper.style.flexGrow      = flexGrow ? 1 : 0;
            if (!flexGrow) wrapper.style.width = 240;
            else
            {
                // Belt-and-braces: when flexGrow is on, also set width=100% so the
                // bar fills its parent even when the parent is a COLUMN container
                // (where flexGrow alone only stretches vertically). Without this,
                // the bar collapsed to 0px width inside row/column wrappers — the
                // Coal Generator's fuel bar was the visible victim.
                wrapper.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            }
            wrapper.style.alignSelf     = Align.Center;
            wrapper.pickingMode         = PickingMode.Ignore;

            // Track styling lives on the wrapper itself — it IS the bar visually.
            wrapper.style.backgroundColor = new StyleColor(new Color(0.05f, 0.055f, 0.075f));
            Radius(wrapper, h * 0.5f);
            Border(wrapper, 1, BorderSubtle);
            wrapper.style.overflow      = Overflow.Hidden;
            wrapper.style.flexDirection = FlexDirection.Row;

            // Fill: width-percent only. Height matches wrapper exactly (100%).
            var fill = new VisualElement();
            fill.style.height          = new StyleLength(new Length(100f, LengthUnit.Percent));
            fill.style.width           = new StyleLength(new Length(Mathf.Clamp01(t) * 100f, LengthUnit.Percent));
            fill.style.backgroundColor = new StyleColor(fillColor);
            fill.pickingMode           = PickingMode.Ignore;
            wrapper.Add(fill);

            return (wrapper, fill);
        }

        // ── Tank Gauge ────────────────────────────────────────────────────

        /// <summary>
        /// Vertical fill gauge for fluid/gas levels.
        /// Premium liquid-tube look with label above and value below.
        /// </summary>
        public static VisualElement TankGauge(
            string label, float fill01, Color fillColor,
            string valueText, float width = 54, float height = 84)
        {
            var col = new VisualElement();
            col.style.alignItems = Align.Center;
            col.style.width      = width;
            col.pickingMode      = PickingMode.Ignore;

            var lbl = new Label(label.ToUpper());
            lbl.style.color                   = new StyleColor(TextMuted);
            lbl.style.fontSize                = 8;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.letterSpacing           = 1.5f;
            lbl.style.marginBottom            = 4;
            lbl.pickingMode = PickingMode.Ignore;
            col.Add(lbl);

            // Outer tube
            var tube = new VisualElement();
            tube.style.width           = width - 10;
            tube.style.height          = height;
            tube.style.backgroundColor = new StyleColor(new Color(0.04f, 0.045f, 0.065f));
            Radius(tube, 5);
            Border(tube, 1, BorderDim);
            tube.style.overflow  = Overflow.Hidden;
            tube.pickingMode     = PickingMode.Ignore;

            // Fill element (bottom-anchored)
            var fillEl = new VisualElement();
            fillEl.style.position  = Position.Absolute;
            fillEl.style.bottom    = 0;
            fillEl.style.left      = 0;
            fillEl.style.right     = 0;
            float clampedFill = Mathf.Clamp01(fill01);
            fillEl.style.height    = new StyleLength(new Length(clampedFill * 100f, LengthUnit.Percent));
            fillEl.style.backgroundColor = new StyleColor(new Color(fillColor.r, fillColor.g, fillColor.b, 0.65f));
            fillEl.pickingMode     = PickingMode.Ignore;
            tube.Add(fillEl);

            // Top sheen line
            var sheen = new VisualElement();
            sheen.style.position  = Position.Absolute;
            sheen.style.top       = 1;
            sheen.style.left      = 2;
            sheen.style.right     = 2;
            sheen.style.height    = 2;
            sheen.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.06f));
            Radius(sheen, 1);
            sheen.pickingMode = PickingMode.Ignore;
            tube.Add(sheen);

            col.Add(tube);

            // Value label
            var val = new Label(valueText);
            val.style.color                   = new StyleColor(TextPrimary);
            val.style.fontSize                = 9;
            val.style.unityFontStyleAndWeight = FontStyle.Bold;
            val.style.marginTop               = 4;
            val.style.unityTextAlign          = TextAnchor.MiddleCenter;
            val.pickingMode = PickingMode.Ignore;
            col.Add(val);

            return col;
        }

        // ── Labeled Slot Card ─────────────────────────────────────────────

        /// <summary>Wraps an inventory slot visual in a labeled card container.</summary>
        public static VisualElement SlotCard(string label, VisualElement slot)
        {
            var card = new VisualElement();
            card.style.alignItems = Align.Center;

            var l = new Label(label.ToUpper());
            l.style.color                   = new StyleColor(TextMuted);
            l.style.fontSize                = 8;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.letterSpacing           = 1.2f;
            l.style.marginBottom            = 4;
            l.pickingMode = PickingMode.Ignore;
            card.Add(l);

            var bg = new VisualElement();
            bg.style.paddingTop    = 5;
            bg.style.paddingBottom = 5;
            bg.style.paddingLeft   = 5;
            bg.style.paddingRight  = 5;
            bg.style.backgroundColor = new StyleColor(BgCard);
            Radius(bg, CardRadius);
            Border(bg, 1, BorderDim);
            bg.Add(slot);
            card.Add(bg);

            return card;
        }

        // ── Stat Row ──────────────────────────────────────────────────────

        /// <summary>
        /// Icon + label + right-aligned value row.
        /// Clean horizontal data display used throughout machine panels.
        /// </summary>
        public static VisualElement StatRow(string icon, string label, string value,
            Color? valueColor = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 5;
            row.style.minHeight     = 20;
            row.pickingMode = PickingMode.Ignore;

            if (!string.IsNullOrEmpty(icon))
            {
                var ico = new Label(icon);
                ico.style.fontSize    = 12;
                ico.style.color       = new StyleColor(AccentCyan);
                ico.style.marginRight = 7;
                ico.style.minWidth    = 18;
                ico.style.unityTextAlign = TextAnchor.MiddleCenter;
                ico.pickingMode = PickingMode.Ignore;
                row.Add(ico);
            }

            var lbl = new Label(label);
            lbl.style.color    = new StyleColor(TextSecondary);
            lbl.style.fontSize = 11;
            lbl.style.flexGrow = 1;
            lbl.pickingMode    = PickingMode.Ignore;
            row.Add(lbl);

            var val = new Label(value);
            val.style.color                   = new StyleColor(valueColor ?? TextPrimary);
            val.style.fontSize                = 11;
            val.style.unityFontStyleAndWeight = FontStyle.Bold;
            val.pickingMode = PickingMode.Ignore;
            row.Add(val);

            return row;
        }

        // ── Header Row ────────────────────────────────────────────────────

        /// <summary>
        /// Title + status pill on one row — standard panel header layout.
        /// Returns all elements for live-update access.
        /// </summary>
        public static (VisualElement row, Label title, VisualElement pill, Label pillLabel)
            HeaderRow(string titleText, string statusText, Color statusColor)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 10;
            row.pickingMode = PickingMode.Ignore;

            var title = Title(titleText);
            title.style.flexGrow = 1;
            row.Add(title);

            var (pill, pillLabel) = StatusPill(statusText, statusColor);
            row.Add(pill);

            return (row, title, pill, pillLabel);
        }

        // ── Slot Grid ─────────────────────────────────────────────────────

        /// <summary>Inventory slot grid — dark card background, wrapped rows.</summary>
        public static VisualElement SlotGrid(int cols = 6)
        {
            var grid = new VisualElement();
            grid.style.flexDirection  = FlexDirection.Row;
            grid.style.flexWrap       = Wrap.Wrap;
            grid.style.paddingTop     = 5;
            grid.style.paddingBottom  = 5;
            grid.style.paddingLeft    = 5;
            grid.style.paddingRight   = 5;
            grid.style.backgroundColor = new StyleColor(BgCard);
            Radius(grid, CardRadius);
            Border(grid, 1, BorderDim);
            return grid;
        }

        // ── Dividers ──────────────────────────────────────────────────────

        /// <summary>Subtle hairline separator with generous margin.</summary>
        public static VisualElement Divider()
        {
            var d = new VisualElement();
            d.style.height           = 1;
            d.style.marginTop        = 10;
            d.style.marginBottom     = 10;
            d.style.backgroundColor  = new StyleColor(BorderSubtle);
            d.pickingMode = PickingMode.Ignore;
            return d;
        }

        /// <summary>Accent-coloured divider — uses theme glow token and reactive USS var.</summary>
        public static VisualElement AccentDivider(Color? color = null)
        {
            var d = new VisualElement();
            d.style.height          = 1;
            d.style.marginTop       = 6;
            d.style.marginBottom    = 10;
            d.pickingMode = PickingMode.Ignore;

            Color c = color ?? UIThemeManager.Accent;
            float glow = UIThemeManager.AccentGlow;
            d.style.backgroundColor = new StyleColor(new Color(c.r, c.g, c.b, 0.20f + glow * 0.25f));
            Radius(d, 1);
            d.AddToClassList("themed-accent-divider");
            return d;
        }

        /// <summary>Vertical spacer with fixed height.</summary>
        public static VisualElement Spacer(float h)
        {
            var s = new VisualElement();
            s.style.height    = h;
            s.style.flexShrink = 0;
            s.pickingMode     = PickingMode.Ignore;
            return s;
        }

        // ── Icon Badge ────────────────────────────────────────────────────

        /// <summary>Small square icon badge — used for machine type icons in headers.</summary>
        public static VisualElement IconBadge(string emoji, Color bg)
        {
            var badge = new VisualElement();
            badge.style.width            = 32;
            badge.style.height           = 32;
            badge.style.backgroundColor  = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.18f));
            badge.style.alignItems       = Align.Center;
            badge.style.justifyContent   = Justify.Center;
            badge.style.marginRight      = 10;
            Radius(badge, 8);
            Border(badge, 1, new Color(bg.r, bg.g, bg.b, 0.35f));
            badge.pickingMode = PickingMode.Ignore;

            var ico = new Label(emoji);
            ico.style.fontSize        = 16;
            ico.style.unityTextAlign  = TextAnchor.MiddleCenter;
            ico.pickingMode = PickingMode.Ignore;
            badge.Add(ico);
            return badge;
        }

        // ── Utility Button ────────────────────────────────────────────────

        /// <summary>
        /// Styled action button with accent colour fill.
        /// Follows the full Normal/Hover/Pressed visual-state convention.
        /// </summary>
        public static Button ActionButton(string text, System.Action onClick, Color? bg = null)
        {
            var btn = new Button(onClick) { text = text };
            Color bgCol = bg ?? AccentTeal;
            btn.style.minHeight                 = 32;
            btn.style.paddingLeft               = 16;
            btn.style.paddingRight              = 16;
            btn.style.fontSize                  = 11;
            btn.style.unityFontStyleAndWeight   = FontStyle.Bold;
            btn.style.letterSpacing             = 0.8f;
            btn.style.color                     = Color.white;
            btn.style.backgroundColor           = new StyleColor(new Color(bgCol.r, bgCol.g, bgCol.b, 0.85f));
            Radius(btn, ButtonRadius);
            Border(btn, 0, Color.clear);
            return btn;
        }

        /// <summary>
        /// Small inline utility button (sort, random, etc.).
        /// </summary>
        public static Button SmallButton(string text, System.Action onClick, Color? bg = null)
        {
            var btn = new Button(onClick) { text = text };
            Color bgCol = bg ?? AccentTeal;
            btn.style.minHeight                 = 22;
            btn.style.paddingLeft               = 10;
            btn.style.paddingRight              = 10;
            btn.style.fontSize                  = 9;
            btn.style.unityFontStyleAndWeight   = FontStyle.Bold;
            btn.style.letterSpacing             = 0.5f;
            btn.style.color                     = Color.white;
            btn.style.backgroundColor           = new StyleColor(new Color(bgCol.r, bgCol.g, bgCol.b, 0.80f));
            Radius(btn, 4);
            Border(btn, 0, Color.clear);
            return btn;
        }

        // ── Utility Helpers ───────────────────────────────────────────────

        public static void Radius(VisualElement v, float r)
        {
            v.style.borderTopLeftRadius     = r;
            v.style.borderTopRightRadius    = r;
            v.style.borderBottomLeftRadius  = r;
            v.style.borderBottomRightRadius = r;
        }

        public static void Border(VisualElement v, float width, Color color)
        {
            v.style.borderTopWidth    = v.style.borderBottomWidth =
            v.style.borderLeftWidth   = v.style.borderRightWidth  = width;
            var sc = new StyleColor(color);
            v.style.borderTopColor    = v.style.borderBottomColor =
            v.style.borderLeftColor   = v.style.borderRightColor  = sc;
        }

        /// <summary>Lerps a fill element's width percentage — call from Tick() for smooth updates.</summary>
        public static void SetFillPercent(VisualElement fill, float t01)
        {
            fill.style.width = new StyleLength(new Length(Mathf.Clamp01(t01) * 100f, LengthUnit.Percent));
        }

        /// <summary>Sets a fill element's height percentage (for vertical tank gauges).</summary>
        public static void SetFillHeightPercent(VisualElement fill, float t01)
        {
            fill.style.height = new StyleLength(new Length(Mathf.Clamp01(t01) * 100f, LengthUnit.Percent));
        }

        // ── Themed ScrollView ─────────────────────────────────────────────

        /// <summary>
        /// Restyles a ScrollView's scrollbars to match the design system: the
        /// default grey Unity scrollbar becomes a slim 6px steel bar in a
        /// near-invisible inset track, with hover brighten + accent-on-drag
        /// micro-interactions. Handles vertical AND horizontal scrollers.
        /// Call after constructing any ScrollView inside a themed panel.
        /// Optional accent overrides the default steel-blue thumb colour.
        /// </summary>
        public static void StyleScroller(ScrollView scroll, Color? accent = null)
        {
            if (scroll == null) return;
            Color thumbCol = accent ?? new Color(0.30f, 0.42f, 0.56f);   // muted steel-blue
            StyleOneScroller(scroll.verticalScroller,   thumbCol, vertical: true);
            StyleOneScroller(scroll.horizontalScroller, thumbCol, vertical: false);
        }

        private static void StyleOneScroller(Scroller scroller, Color thumbCol, bool vertical)
        {
            if (scroller == null) return;

            // Rail: 10px thin, transparent background.
            if (vertical) { scroller.style.width  = 10; scroller.style.marginLeft = 2; }
            else          { scroller.style.height = 10; scroller.style.marginTop  = 2; }
            Border(scroller, 0, Color.clear);
            scroller.style.backgroundColor = new StyleColor(Color.clear);

            // Hide the tiny arrow buttons — modern scrollbars don't need them.
            scroller.lowButton.style.display  = DisplayStyle.None;
            scroller.highButton.style.display = DisplayStyle.None;
            // Reclaim the space the buttons occupied.
            scroller.slider.style.marginTop    = 0;
            scroller.slider.style.marginBottom = 0;
            scroller.slider.style.marginLeft   = 0;
            scroller.slider.style.marginRight  = 0;
            scroller.slider.style.flexGrow     = 1;

            // The default runtime theme paints the slider (and its drag container)
            // with a grey plate that is wider than our 6px bar — strip EVERY
            // background layer so only the slim track + thumb remain visible.
            scroller.slider.style.backgroundColor = new StyleColor(Color.clear);
            Border(scroller.slider, 0, Color.clear);
            var dragContainer = scroller.slider.Q("unity-drag-container");
            if (dragContainer != null)
            {
                dragContainer.style.backgroundColor = new StyleColor(Color.clear);
                Border(dragContainer, 0, Color.clear);
            }

            // NOTE: tracker & dragger are absolutely-positioned inside the slider,
            // so width/alignSelf alone anchors them to the left/top edge — they
            // must be centred with explicit cross-axis insets:
            // (rail 10px − bar 6px) / 2 = 2px on each side.

            // Track — near-invisible inset channel.
            var tracker = scroller.slider.Q("unity-tracker");
            if (tracker != null)
            {
                tracker.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.04f));
                Radius(tracker, 3f);
                Border(tracker, 0, Color.clear);
                if (vertical)
                {
                    tracker.style.left  = 2; tracker.style.right  = 2;
                    tracker.style.width = StyleKeyword.Auto;
                }
                else
                {
                    tracker.style.top    = 2; tracker.style.bottom = 2;
                    tracker.style.height = StyleKeyword.Auto;
                }
            }

            // Some runtime themes add a border overlay around the thumb — hide it.
            var draggerBorder = scroller.slider.Q("unity-dragger-border");
            if (draggerBorder != null)
            {
                draggerBorder.style.backgroundColor = new StyleColor(Color.clear);
                Border(draggerBorder, 0, Color.clear);
                draggerBorder.style.display = DisplayStyle.None;
            }

            // Thumb — rounded steel bar, brightens on hover, accent when dragged.
            var dragger = scroller.slider.Q("unity-dragger");
            if (dragger != null)
            {
                dragger.style.backgroundColor = new StyleColor(thumbCol);
                Radius(dragger, 3f);
                Border(dragger, 0, Color.clear);
                if (vertical)
                {
                    dragger.style.left = 2;
                    dragger.style.right = 2;
                    dragger.style.width = StyleKeyword.Auto;
                    dragger.style.minHeight = 24;
                    dragger.style.maxWidth = 6;
                }
                else
                {
                    dragger.style.top = 2;
                    dragger.style.bottom = 2;
                    dragger.style.height = StyleKeyword.Auto;
                    dragger.style.minWidth = 24;
                    dragger.style.maxHeight = 6;
                }

                Color hover  = new(thumbCol.r * 1.35f, thumbCol.g * 1.35f, thumbCol.b * 1.35f);
                Color active = AccentCyan;
                dragger.RegisterCallback<PointerEnterEvent>(_ =>
                    dragger.style.backgroundColor = new StyleColor(hover));
                dragger.RegisterCallback<PointerLeaveEvent>(_ =>
                    dragger.style.backgroundColor = new StyleColor(thumbCol));
                dragger.RegisterCallback<PointerDownEvent>(_ =>
                    dragger.style.backgroundColor = new StyleColor(active), TrickleDown.TrickleDown);
                dragger.RegisterCallback<PointerUpEvent>(_ =>
                    dragger.style.backgroundColor = new StyleColor(thumbCol), TrickleDown.TrickleDown);
            }
        }
    }
}
