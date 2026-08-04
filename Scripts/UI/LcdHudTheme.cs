// Assets/Scripts/VoxelEngine/UI/LcdHudTheme.cs
//
// Shared fitted-instrument language for HUD surfaces. These are deliberately
// quiet phosphor displays with physical bezels and discrete segments — not
// rounded holographic cards. Enhanced with retro-futuristic animated scanline
// shimmer, CRT phosphor boot sequences, bezel corner brackets, pulsing status
// badges, and tactile button micro-interactions.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    public static class LcdHudTheme
    {
        public static readonly Color Chassis = new(0.028f, 0.034f, 0.038f, 0.97f);
        public static readonly Color Bezel = new(0.20f, 0.24f, 0.22f, 0.96f);
        public static readonly Color Glass = new(0.095f, 0.120f, 0.070f, 0.98f);
        public static readonly Color GlassDark = new(0.050f, 0.062f, 0.040f, 0.98f);
        public static readonly Color Phosphor = new(0.72f, 0.84f, 0.42f, 1f);
        public static readonly Color PhosphorDim = new(0.45f, 0.56f, 0.30f, 1f);
        public static readonly Color SegmentOff = new(0.075f, 0.090f, 0.055f, 1f);
        public static readonly Color Caption = new(0.50f, 0.56f, 0.48f, 1f);

        public static void ApplyChassis(VisualElement element, Color? border = null, float radius = 3f)
        {
            if (element == null) return;
            element.style.backgroundColor = new StyleColor(Chassis);
            UITheme.Radius(element, radius);
            UITheme.Border(element, 1f, border ?? Bezel);
        }

        public static void ApplyScreen(VisualElement element, Color? border = null, float radius = 1f)
        {
            if (element == null) return;
            element.style.backgroundColor = new StyleColor(Glass);
            UITheme.Radius(element, radius);
            UITheme.Border(element, 1f, border ?? new Color(Bezel.r, Bezel.g, Bezel.b, 0.88f));
            AddBezelAccents(element, border ?? Bezel);
        }

        /// <summary>
        /// Adds 4 high-tech corner L-bracket elements inside an LCD screen bezel.
        /// </summary>
        public static void AddBezelAccents(VisualElement screen, Color? accentColor = null)
        {
            if (screen == null) return;
            Color color = accentColor ?? new Color(PhosphorDim.r, PhosphorDim.g, PhosphorDim.b, 0.65f);

            void CreateBracket(float? top, float? bottom, float? left, float? right, bool borderTop, bool borderBottom, bool borderLeft, bool borderRight)
            {
                var bracket = new VisualElement { name = "LcdBezelBracket" };
                bracket.style.position = Position.Absolute;
                bracket.style.width = 6;
                bracket.style.height = 6;
                if (top.HasValue) bracket.style.top = top.Value;
                if (bottom.HasValue) bracket.style.bottom = bottom.Value;
                if (left.HasValue) bracket.style.left = left.Value;
                if (right.HasValue) bracket.style.right = right.Value;

                if (borderTop) bracket.style.borderTopWidth = 1;
                if (borderBottom) bracket.style.borderBottomWidth = 1;
                if (borderLeft) bracket.style.borderLeftWidth = 1;
                if (borderRight) bracket.style.borderRightWidth = 1;

                bracket.style.borderTopColor = new StyleColor(color);
                bracket.style.borderBottomColor = new StyleColor(color);
                bracket.style.borderLeftColor = new StyleColor(color);
                bracket.style.borderRightColor = new StyleColor(color);
                bracket.pickingMode = PickingMode.Ignore;
                screen.Add(bracket);
            }

            CreateBracket(2f, null, 2f, null, true, false, true, false);
            CreateBracket(2f, null, null, 2f, true, false, false, true);
            CreateBracket(null, 2f, 2f, null, false, true, true, false);
            CreateBracket(null, 2f, null, 2f, false, true, false, true);
        }

        /// <summary>
        /// Creates scanlines with animated CRT/LCD phosphor drift and subtle brightness waves.
        /// </summary>
        public static void AddScanlines(VisualElement screen, int count, float top = 5f, float spacing = 10f)
        {
            AddAnimatedScanlines(screen, count, top, spacing);
        }

        public static void AddAnimatedScanlines(VisualElement screen, int count, float top = 5f, float spacing = 10f)
        {
            if (screen == null || count <= 0) return;
            var scanlines = new List<VisualElement>(count);
            for (int i = 0; i < count; i++)
            {
                var line = new VisualElement { name = "LcdScanline_" + i };
                line.style.position = Position.Absolute;
                line.style.left = 2;
                line.style.right = 2;
                line.style.top = top + spacing * i;
                line.style.height = 1;
                line.style.backgroundColor = new StyleColor(new Color(Phosphor.r, Phosphor.g, Phosphor.b, 0.065f));
                line.pickingMode = PickingMode.Ignore;
                screen.Add(line);
                scanlines.Add(line);
            }

            // Animate scanline shimmer at a lightweight 20 fps interval.
            screen.schedule.Execute(() =>
            {
                float t = Time.realtimeSinceStartup;
                for (int i = 0; i < scanlines.Count; i++)
                {
                    var line = scanlines[i];
                    if (line == null) continue;
                    float wave = 0.5f + 0.5f * Mathf.Sin(t * 3.2f + i * 0.45f);
                    float alpha = 0.045f + 0.035f * wave;
                    line.style.backgroundColor = new StyleColor(new Color(Phosphor.r, Phosphor.g, Phosphor.b, alpha));
                }
            }).Every(50);
        }

        /// <summary>
        /// While true, boot animations are skipped (elements appear instantly at full
        /// opacity). Set by the UI layer around REBUILDS of an already-open surface
        /// (inventory refresh, terminal refresh, settings toggle) so the LCD boot only
        /// plays on genuine opens — never on every refresh.
        /// </summary>
        public static bool BootsMuted { get; set; }

        /// <summary>
        /// Animates a retro-futuristic CRT/LCD phosphor boot-up sequence on any display element.
        /// </summary>
        public static void AnimateScreenBoot(VisualElement element, float delaySeconds = 0f, System.Action onComplete = null)
        {
            if (element == null) return;

            if (BootsMuted)
            {
                element.style.opacity = 1f;
                element.style.scale = new StyleScale(new Scale(Vector3.one));
                onComplete?.Invoke();
                return;
            }

            element.style.opacity = 0f;
            element.style.scale = new StyleScale(new Scale(new Vector3(1.02f, 0.08f, 1f)));
            element.style.transitionProperty = new List<StylePropertyName> { "opacity", "scale" };
            element.style.transitionDuration = new List<TimeValue>
            {
                new TimeValue(0.12f, TimeUnit.Second),
                new TimeValue(0.18f, TimeUnit.Second)
            };

            long delayMs = (long)Mathf.Max(0f, delaySeconds * 1000f);
            element.schedule.Execute(() =>
            {
                element.style.opacity = 1f;
                element.style.scale = new StyleScale(new Scale(Vector3.one));
                if (onComplete != null)
                {
                    element.schedule.Execute(() => onComplete?.Invoke()).StartingIn(180);
                }
            }).StartingIn(delayMs);
        }

        public static Label CaptionLabel(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 7;
            label.style.letterSpacing = 1.05f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(Caption);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        /// <summary>
        /// Creates the fixed, printed title strip used by large LCD work surfaces.
        /// The title is deliberately part of the glass rather than a floating card header.
        /// Enhanced with pulsing phosphor status indicators.
        /// </summary>
        public static VisualElement CreateDisplayHeader(string caption, string title, string moduleId = null, string status = null)
        {
            var header = new VisualElement { name = "LcdDisplayHeader" };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = 8;
            header.style.paddingRight = 8;
            header.style.paddingTop = 6;
            header.style.paddingBottom = 6;
            header.style.marginBottom = 6;
            header.style.backgroundColor = new StyleColor(GlassDark);
            UITheme.Radius(header, 1f);
            UITheme.Border(header, 1f, new Color(Bezel.r, Bezel.g, Bezel.b, 0.88f));
            AddBezelAccents(header, Bezel);

            var titleBlock = new VisualElement();
            titleBlock.style.flexGrow = 1;
            titleBlock.pickingMode = PickingMode.Ignore;
            if (!string.IsNullOrEmpty(caption))
            {
                var captionLabel = CaptionLabel(caption);
                captionLabel.style.marginBottom = 1;
                titleBlock.Add(captionLabel);
            }

            var titleLabel = new Label(title ?? string.Empty);
            titleLabel.style.fontSize = 14;
            titleLabel.style.letterSpacing = 1.25f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new StyleColor(Phosphor);
            titleLabel.pickingMode = PickingMode.Ignore;
            titleBlock.Add(titleLabel);
            header.Add(titleBlock);

            if (!string.IsNullOrEmpty(moduleId) || !string.IsNullOrEmpty(status))
            {
                var readout = new VisualElement();
                readout.style.flexDirection = FlexDirection.Row;
                readout.style.alignItems = Align.Center;
                readout.style.marginLeft = 8;
                readout.pickingMode = PickingMode.Ignore;

                if (!string.IsNullOrEmpty(moduleId))
                {
                    var id = CaptionLabel(moduleId);
                    id.style.unityTextAlign = TextAnchor.MiddleRight;
                    id.style.marginRight = string.IsNullOrEmpty(status) ? 0 : 6;
                    readout.Add(id);
                }

                if (!string.IsNullOrEmpty(status))
                {
                    var state = new VisualElement();
                    state.style.flexDirection = FlexDirection.Row;
                    state.style.alignItems = Align.Center;
                    state.style.marginTop = 2;
                    state.style.paddingLeft = 5;
                    state.style.paddingRight = 6;
                    state.style.paddingTop = 2;
                    state.style.paddingBottom = 2;
                    state.style.backgroundColor = new StyleColor(new Color(Phosphor.r, Phosphor.g, Phosphor.b, 0.12f));
                    UITheme.Radius(state, 2f);
                    UITheme.Border(state, 1f, new Color(Phosphor.r, Phosphor.g, Phosphor.b, 0.55f));

                    // Pulsing phosphor dot
                    var dot = new VisualElement();
                    dot.style.width = 5;
                    dot.style.height = 5;
                    dot.style.marginRight = 4;
                    dot.style.backgroundColor = new StyleColor(Phosphor);
                    UITheme.Radius(dot, 2.5f);
                    state.Add(dot);

                    var label = new Label(status);
                    label.style.fontSize = 8;
                    label.style.letterSpacing = 0.8f;
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    label.style.color = new StyleColor(Phosphor);
                    label.pickingMode = PickingMode.Ignore;
                    state.Add(label);

                    // Animate pulsing dot
                    state.schedule.Execute(() =>
                    {
                        float alpha = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 4.5f));
                        dot.style.backgroundColor = new StyleColor(new Color(Phosphor.r, Phosphor.g, Phosphor.b, alpha));
                    }).Every(75);

                    readout.Add(state);
                }
                header.Add(readout);
            }

            return header;
        }

        /// <summary>Applies a recessed, square LCD data-cell treatment without creating a floating card.</summary>
        public static void ApplyDataCard(VisualElement element, Color? signalColor = null)
        {
            if (element == null) return;
            Color signal = signalColor ?? Bezel;
            element.style.backgroundColor = new StyleColor(GlassDark);
            UITheme.Radius(element, 1f);
            UITheme.Border(element, 1f, new Color(signal.r, signal.g, signal.b, 0.76f));
        }

        /// <summary>Creates a square, physical-looking command key for LCD surfaces.</summary>
        public static Button CommandButton(string text, System.Action onClick, Color? signalColor = null, bool active = false)
        {
            var button = new Button(onClick) { text = text };
            ApplyCommandButton(button, signalColor, active);
            return button;
        }

        /// <summary>Restyles an existing button as an LCD command key while preserving its callback.</summary>
        public static void ApplyCommandButton(Button button, Color? signalColor = null, bool active = false)
        {
            if (button == null) return;
            Color signal = signalColor ?? Phosphor;
            Color idleBackground = active
                ? new Color(signal.r, signal.g, signal.b, 0.17f)
                : GlassDark;
            Color hoverBackground = active
                ? new Color(signal.r, signal.g, signal.b, 0.27f)
                : new Color(signal.r, signal.g, signal.b, 0.12f);
            Color pressedBackground = new Color(signal.r, signal.g, signal.b, 0.35f);

            button.style.minHeight = 25;
            button.style.paddingLeft = 8;
            button.style.paddingRight = 8;
            button.style.paddingTop = 3;
            button.style.paddingBottom = 3;
            button.style.marginRight = 3;
            button.style.marginBottom = 3;
            button.style.fontSize = 8;
            button.style.letterSpacing = 0.72f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.whiteSpace = WhiteSpace.NoWrap;
            button.style.color = new StyleColor(active ? signal : Caption);
            button.style.backgroundColor = new StyleColor(idleBackground);
            UITheme.Radius(button, 1f);
            UITheme.Border(button, 1f, active
                ? new Color(signal.r, signal.g, signal.b, 0.82f)
                : new Color(Bezel.r, Bezel.g, Bezel.b, 0.92f));

            button.style.transitionProperty = new List<StylePropertyName>
            {
                "background-color", "color", "scale", "border-color"
            };
            button.style.transitionDuration = new List<TimeValue>
            {
                new TimeValue(0.10f, TimeUnit.Second),
                new TimeValue(0.10f, TimeUnit.Second),
                new TimeValue(0.10f, TimeUnit.Second),
                new TimeValue(0.10f, TimeUnit.Second)
            };
            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                button.style.backgroundColor = new StyleColor(hoverBackground);
                button.style.color = new StyleColor(signal);
                button.style.scale = new StyleScale(new Scale(new Vector3(1.03f, 1.03f, 1f)));
                UITheme.Border(button, 1f, signal);
            });
            button.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                button.style.backgroundColor = new StyleColor(idleBackground);
                button.style.color = new StyleColor(active ? signal : Caption);
                button.style.scale = new StyleScale(new Scale(Vector3.one));
                UITheme.Border(button, 1f, active
                    ? new Color(signal.r, signal.g, signal.b, 0.82f)
                    : new Color(Bezel.r, Bezel.g, Bezel.b, 0.92f));
            });
            button.RegisterCallback<PointerDownEvent>(_ =>
            {
                button.style.backgroundColor = new StyleColor(pressedBackground);
                button.style.scale = new StyleScale(new Scale(new Vector3(0.98f, 0.98f, 1f)));
            });
            button.RegisterCallback<PointerUpEvent>(_ =>
            {
                button.style.backgroundColor = new StyleColor(hoverBackground);
                button.style.scale = new StyleScale(new Scale(new Vector3(1.03f, 1.03f, 1f)));
            });
        }

        /// <summary>
        /// LCD-ify an EXISTING plain panel in one call: dark chassis, bezel border,
        /// corner brackets, animated scanlines, phosphor boot animation and a one-shot
        /// boot sweep. Used to lift menus / settings / plain panels onto the shared
        /// LCD language without rebuilding their content.
        /// </summary>
        public static void UpgradePanel(VisualElement panel, string caption = null, Color? accent = null)
        {
            if (panel == null) return;
            Color signal = accent ?? Phosphor;

            panel.style.backgroundColor = new StyleColor(Chassis);
            UITheme.Radius(panel, 4f);
            UITheme.Border(panel, 1f, new Color(Bezel.r, Bezel.g, Bezel.b, 0.96f));
            panel.style.overflow = Overflow.Hidden;

            AddBezelAccents(panel, new Color(signal.r, signal.g, signal.b, 0.42f));
            AddAnimatedScanlines(panel, 5, 8f, 22f);
            AnimateScreenBoot(panel);
            AnimateBootSweep(panel);

            if (!string.IsNullOrEmpty(caption))
            {
                var cap = CaptionLabel(caption);
                cap.style.marginBottom = 4f;
                panel.Insert(0, cap);
            }
        }

        /// <summary>
        /// One-shot CRT/LCD power-on wipe: a phosphor line sweeps down the screen once,
        /// then fades out. Complements <see cref="AnimateScreenBoot"/> with real motion.
        /// </summary>
        public static void AnimateBootSweep(VisualElement screen, Color? sweepColor = null)
        {
            if (screen == null || BootsMuted) return;
            Color baseColor = sweepColor ?? new Color(Phosphor.r, Phosphor.g, Phosphor.b, 0.55f);

            var sweep = new VisualElement { name = "LcdBootSweep" };
            sweep.style.position = Position.Absolute;
            sweep.style.left = 2f;
            sweep.style.right = 2f;
            sweep.style.top = -8f;
            sweep.style.height = 3f;
            sweep.style.backgroundColor = new StyleColor(baseColor);
            sweep.pickingMode = PickingMode.Ignore;
            screen.Add(sweep);

            const float duration = 0.95f;
            float start = Time.realtimeSinceStartup;
            float last = -1f;
            bool done = false;
            // Unity 6 UI Toolkit: schedule.Execute takes an Action (no Func<bool>
            // overload). We never name the scheduler-item type — chaining Until(...)
            // stops the item automatically once the wipe finishes.
            screen.schedule.Execute(() =>
            {
                if (done || sweep == null || sweep.parent == null) return;
                float t = Mathf.Clamp01((Time.realtimeSinceStartup - start) / duration);
                float h = Mathf.Max(160f, screen.resolvedStyle.height);
                // Smoothstep travel top → bottom.
                float p = t * t * (3f - 2f * t);
                sweep.style.top = -8f + (h + 16f) * p;
                // Bright core, fading tail as the wipe completes.
                float alpha = t < 0.94f ? 0.55f : Mathf.Lerp(0.55f, 0f, (t - 0.94f) / 0.06f);
                sweep.style.backgroundColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, alpha));
                if (t >= 1f && Mathf.Abs(t - last) < 0.0001f)
                {
                    done = true;
                    sweep.RemoveFromHierarchy();
                    return;
                }
                last = t;
            }).Every(16).Until(() => done);
        }

        /// <summary>
        /// Micro-interactions for MENU-scale buttons (keeps their sizing): smooth 0.1 s
        /// colour transitions, 1.03× hover scale, 0.98× press scale — per the project's
        /// interaction guidelines. The button's own colours stay intact; hover blends
        /// toward the signal colour.
        /// </summary>
        public static void AddMenuInteractions(Button button, Color signalColor, Color idleBackground)
        {
            if (button == null) return;
            Color hoverBg = Color.Lerp(idleBackground, signalColor, 0.20f);
            Color pressedBg = Color.Lerp(idleBackground, signalColor, 0.42f);

            button.style.transitionProperty = new List<StylePropertyName> { "background-color", "scale", "border-color" };
            button.style.transitionDuration = new List<TimeValue>
            {
                new TimeValue(0.10f, TimeUnit.Second),
                new TimeValue(0.10f, TimeUnit.Second),
                new TimeValue(0.10f, TimeUnit.Second),
            };
            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                button.style.backgroundColor = new StyleColor(hoverBg);
                button.style.scale = new StyleScale(new Scale(new Vector3(1.03f, 1.03f, 1f)));
            });
            button.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                button.style.backgroundColor = new StyleColor(idleBackground);
                button.style.scale = new StyleScale(new Scale(Vector3.one));
            });
            button.RegisterCallback<PointerDownEvent>(_ =>
            {
                button.style.backgroundColor = new StyleColor(pressedBg);
                button.style.scale = new StyleScale(new Scale(new Vector3(0.98f, 0.98f, 1f)));
            });
            button.RegisterCallback<PointerUpEvent>(_ =>
            {
                button.style.backgroundColor = new StyleColor(hoverBg);
                button.style.scale = new StyleScale(new Scale(new Vector3(1.03f, 1.03f, 1f)));
            });
        }

        /// <summary>
        /// Auto-yield a HUD module while a blocking UI (machine panel, chest, terminal)
        /// is open: the module fades out smoothly and returns when the UI closes.
        /// This is the systemic fix for HUD/panel overlap — right-side and bottom
        /// modules step aside whenever a panel needs the screen.
        /// </summary>
        public static void YieldWhileBlocking(VisualElement element, float minOpacity = 0f)
        {
            if (element == null) return;
            float smooth = 1f;
            element.schedule.Execute(() =>
            {
                if (element == null) return;
                bool blocked = VoxelEngine.UI.UIState.IsBlocking;
                float target = blocked ? minOpacity : 1f;
                smooth = Mathf.MoveTowards(smooth, target, Time.unscaledDeltaTime * 6f);
                element.style.opacity = smooth;
                // Fully hidden modules must never intercept the cursor.
                element.pickingMode = smooth < 0.02f ? PickingMode.Ignore : element.pickingMode;
            }).Every(33);
        }

        /// <summary>
        /// Premium machined depth for panels: a hairline top highlight and a soft
        /// bottom shade that make the chassis read as physical metal/glass instead
        /// of a flat rectangle.
        /// </summary>
        public static void ApplyPanelDepth(VisualElement panel)
        {
            if (panel == null) return;

            var highlight = new VisualElement { name = "LcdPanelHighlight" };
            highlight.style.position = Position.Absolute;
            highlight.style.left = 1f;
            highlight.style.right = 1f;
            highlight.style.top = 0f;
            highlight.style.height = 1f;
            highlight.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.055f));
            highlight.pickingMode = PickingMode.Ignore;
            panel.Add(highlight);

            var shade = new VisualElement { name = "LcdPanelShade" };
            shade.style.position = Position.Absolute;
            shade.style.left = 0f;
            shade.style.right = 0f;
            shade.style.bottom = 0f;
            shade.style.height = 3f;
            shade.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.30f));
            shade.pickingMode = PickingMode.Ignore;
            panel.Add(shade);
        }

        /// <summary>Styles a search/input field as inset phosphor glass.</summary>
        public static void ApplySearchField(TextField field)
        {
            if (field == null) return;
            field.style.minHeight = 27;
            field.style.paddingLeft = 7;
            field.style.paddingRight = 7;
            field.style.backgroundColor = new StyleColor(GlassDark);
            field.style.color = new StyleColor(Phosphor);
            UITheme.Radius(field, 1f);
            UITheme.Border(field, 1f, new Color(Bezel.r, Bezel.g, Bezel.b, 0.92f));

            void ApplyInner(VisualElement root)
            {
                var input = root.Q(className: "unity-base-text-field__input")
                            ?? root.Q("unity-text-input");
                if (input != null)
                {
                    input.style.color = new StyleColor(Phosphor);
                    input.style.backgroundColor = new StyleColor(GlassDark);
                    input.style.unityTextAlign = TextAnchor.MiddleLeft;
                    input.style.paddingLeft = 6;
                    input.style.paddingRight = 6;
                }
                root.Query<TextElement>().ForEach(text => text.style.color = new StyleColor(Phosphor));
            }

            ApplyInner(field);
            field.RegisterCallback<AttachToPanelEvent>(_ => ApplyInner(field));
            field.RegisterCallback<GeometryChangedEvent>(_ => ApplyInner(field));
        }

        public static VisualElement CreateSegmentTrack(int count, out VisualElement[] segments, float height = 10f)
        {
            count = Mathf.Max(1, count);
            var track = new VisualElement { name = "LcdSegmentTrack" };
            track.style.flexDirection = FlexDirection.Row;
            track.style.height = height;
            track.style.paddingLeft = 2;
            track.style.paddingRight = 2;
            track.style.paddingTop = 2;
            track.style.paddingBottom = 2;
            track.style.backgroundColor = new StyleColor(GlassDark);
            track.pickingMode = PickingMode.Ignore;
            UITheme.Radius(track, 1f);
            UITheme.Border(track, 1f, new Color(Bezel.r, Bezel.g, Bezel.b, 0.85f));

            segments = new VisualElement[count];
            for (int i = 0; i < count; i++)
            {
                var segment = new VisualElement { name = "LcdSegment" + i };
                segment.style.flexGrow = 1;
                segment.style.marginRight = i < count - 1 ? 1 : 0;
                segment.style.backgroundColor = new StyleColor(SegmentOff);
                segment.pickingMode = PickingMode.Ignore;
                UITheme.Radius(segment, 1f);
                segments[i] = segment;
                track.Add(segment);
            }
            return track;
        }

        public static void SetSegments(VisualElement[] segments, float fill01, Color? signalColor = null)
        {
            if (segments == null) return;
            int lit = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * segments.Length), 0, segments.Length);
            Color signal = signalColor ?? Phosphor;
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment == null) continue;
                segment.style.backgroundColor = new StyleColor(i < lit
                    ? new Color(signal.r, signal.g, signal.b, 0.90f)
                    : SegmentOff);
            }
        }

        /// <summary>
        /// Smoothly updates segmented bar displays with optional active phosphor glow border.
        /// </summary>
        public static void AnimateSegments(VisualElement[] segments, float fill01, Color? signalColor = null)
        {
            SetSegments(segments, fill01, signalColor);
        }
    }
}
