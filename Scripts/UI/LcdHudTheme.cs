// Assets/Scripts/VoxelEngine/UI/LcdHudTheme.cs
//
// Shared fitted-instrument language for HUD surfaces. These are deliberately
// quiet phosphor displays with physical bezels and discrete segments — not
// rounded holographic cards.

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
        }

        public static void AddScanlines(VisualElement screen, int count, float top = 5f, float spacing = 10f)
        {
            if (screen == null) return;
            for (int i = 0; i < count; i++)
            {
                var line = new VisualElement { name = "LcdScanline" };
                line.style.position = Position.Absolute;
                line.style.left = 2;
                line.style.right = 2;
                line.style.top = top + spacing * i;
                line.style.height = 1;
                line.style.backgroundColor = new StyleColor(new Color(Phosphor.r, Phosphor.g, Phosphor.b, 0.055f));
                line.pickingMode = PickingMode.Ignore;
                screen.Add(line);
            }
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
    }
}
