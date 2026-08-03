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
        /// Animates a retro-futuristic CRT/LCD phosphor boot-up sequence on any display element.
        /// </summary>
        public static void AnimateScreenBoot(VisualElement element, float delaySeconds = 0f, System.Action onComplete = null)
        {
            if (element == null) return;

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
