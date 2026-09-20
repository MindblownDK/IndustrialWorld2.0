// Assets/Scripts/VoxelEngine/UI/SteampunkTheme.cs
//
// The steampunk instrument library: the UI matches the block, and rail and steam
// hardware wears brass, cream dials and red needles - not just a brass accent
// colour. Real analog gauges with tick rings and needles, riveted dividers and
// machined selector keys. Static styling only (no scheduled anims), so live
// panels that rebuild on the machine cadence can call it safely.

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    public static class SteampunkTheme
    {
        public static readonly Color Cream = new(0.93f, 0.89f, 0.78f, 1f);
        public static readonly Color Ink = new(0.13f, 0.12f, 0.10f, 1f);
        public static readonly Color NeedleRed = new(0.62f, 0.12f, 0.10f, 1f);
        public static readonly Color IronDark = new(0.10f, 0.10f, 0.11f, 1f);

        /// <summary>True while a steampunk-panel text field holds keyboard focus.
        /// GameUIController feeds this into the same guards as the maritime
        /// numeric flag, so a live rebuild never eats a field mid-typing.</summary>
        public static bool IsTextInputFocused { get; private set; }

        /// <summary>Hooks focus tracking onto a text field. Returns the field.</summary>
        public static TextField GuardTextField(TextField field)
        {
            if (field == null) return null;
            field.RegisterCallback<FocusInEvent>(_ => IsTextInputFocused = true);
            field.RegisterCallback<FocusOutEvent>(_ => IsTextInputFocused = false);
            field.RegisterCallback<DetachFromPanelEvent>(_ => IsTextInputFocused = false);
            return field;
        }

        /// <summary>A machined selector key: brass when active, dark slot when idle.</summary>
        public static Button SelectorButton(string text, bool active, System.Action onClick, bool flexGrow = true)
        {
            var b = UITheme.SmallButton(text, onClick, active ? LcdHudTheme.Brass : UITheme.BgSlot);
            if (flexGrow) b.style.flexGrow = 1;
            b.style.minHeight = 24;
            return b;
        }

        /// <summary>A brass divider with three rivets - the steampunk section break.</summary>
        public static VisualElement RivetedDivider()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 2;
            row.style.marginBottom = 8;
            row.pickingMode = PickingMode.Ignore;

            for (int i = 0; i < 3; i++)
            {
                var line = new VisualElement();
                line.style.flexGrow = 1;
                line.style.height = 1;
                line.style.backgroundColor = new StyleColor(new Color(
                    LcdHudTheme.Brass.r, LcdHudTheme.Brass.g, LcdHudTheme.Brass.b, 0.45f));
                line.pickingMode = PickingMode.Ignore;
                row.Add(line);

                var rivet = new VisualElement();
                rivet.style.width = 5;
                rivet.style.height = 5;
                rivet.style.marginLeft = 5;
                rivet.style.marginRight = 5;
                rivet.style.backgroundColor = new StyleColor(LcdHudTheme.Brass);
                UITheme.Radius(rivet, 2.5f);
                rivet.pickingMode = PickingMode.Ignore;
                row.Add(rivet);
            }

            var tail = new VisualElement();
            tail.style.flexGrow = 1;
            tail.style.height = 1;
            tail.style.backgroundColor = new StyleColor(new Color(
                LcdHudTheme.Brass.r, LcdHudTheme.Brass.g, LcdHudTheme.Brass.b, 0.45f));
            tail.pickingMode = PickingMode.Ignore;
            row.Add(tail);

            return row;
        }

        /// <summary>
        /// A true analog dial: brass bezel, cream face, eleven-mark tick ring over
        /// the 270-degree sweep (ends and middle longer), red needle on a brass hub.
        /// Ticks at or above redlineFrom read red - a boiler pressure gauge redlines.
        /// </summary>
        public static VisualElement Dial(string label, float fill01, string valueText,
            float size = 118f, float redlineFrom = 1.01f)
        {
            float dial = Mathf.Max(72f, size);
            float fill = Mathf.Clamp01(fill01);
            // The 3px bezel is a real USS border, so children anchor to the
            // padding box inside it: the face centre sits one bezel-width up
            // and left of the naive half-size origin.
            const float bezel = 3f;

            var col = new VisualElement();
            col.style.alignItems = Align.Center;
            col.style.flexShrink = 0;
            col.pickingMode = PickingMode.Ignore;

            var name = new Label(label.ToUpper());
            name.style.fontSize = 8;
            name.style.letterSpacing = 1.4f;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.color = new StyleColor(LcdHudTheme.BrassBright);
            name.style.marginBottom = 3;
            name.pickingMode = PickingMode.Ignore;
            col.Add(name);

            // Iron body with a machined brass bezel.
            var body = new VisualElement();
            body.style.width = dial;
            body.style.height = dial;
            body.style.position = Position.Relative;
            body.style.backgroundColor = new StyleColor(IronDark);
            UITheme.Radius(body, dial / 2f);
            UITheme.Border(body, 3f, LcdHudTheme.Brass);
            body.pickingMode = PickingMode.Ignore;
            col.Add(body);

            // Cream face.
            float faceD = dial - 14f;
            var face = new VisualElement();
            face.style.position = Position.Absolute;
            face.style.left = 7f - bezel; face.style.top = 7f - bezel;
            face.style.width = faceD; face.style.height = faceD;
            face.style.backgroundColor = new StyleColor(Cream);
            UITheme.Radius(face, faceD / 2f);
            UITheme.Border(face, 1f, new Color(0.25f, 0.22f, 0.16f));
            face.pickingMode = PickingMode.Ignore;
            body.Add(face);

            // Tick ring: eleven ticks from -135 to +135 degrees.
            float cx = dial / 2f - bezel, cy = dial / 2f - bezel;
            float tickR = faceD / 2f - 8f;
            for (int i = 0; i < 11; i++)
            {
                float deg = -135f + 27f * i;
                float rad = deg * Mathf.Deg2Rad;
                bool major = i == 0 || i == 5 || i == 10;
                bool red = (i / 10f) >= redlineFrom;
                var tick = new VisualElement();
                tick.style.position = Position.Absolute;
                tick.style.width = major ? 2.5f : 1.5f;
                tick.style.height = major ? 9f : 6f;
                tick.style.left = cx + tickR * Mathf.Sin(rad) - (major ? 1.25f : 0.75f);
                tick.style.top = cy - tickR * Mathf.Cos(rad) - (major ? 4.5f : 3f);
                tick.style.rotate = new Rotate(new Angle(deg, AngleUnit.Degree));
                tick.style.backgroundColor = new StyleColor(red ? NeedleRed : Ink);
                tick.pickingMode = PickingMode.Ignore;
                body.Add(tick);
            }

            // Needle, rotating about its bottom centre (the hub).
            var needle = new VisualElement();
            needle.style.position = Position.Absolute;
            needle.style.width = 3f;
            float needleLen = faceD / 2f - 13f;
            needle.style.height = needleLen;
            needle.style.left = cx - 1.5f;
            needle.style.top = cy - needleLen;
            needle.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(100f));
            float ang = -135f + 270f * fill;
            needle.style.rotate = new Rotate(new Angle(ang, AngleUnit.Degree));
            needle.style.backgroundColor = new StyleColor(NeedleRed);
            needle.pickingMode = PickingMode.Ignore;
            body.Add(needle);

            // Brass hub with a dark centre.
            var hub = new VisualElement();
            hub.style.position = Position.Absolute;
            hub.style.width = 13; hub.style.height = 13;
            hub.style.left = cx - 6.5f; hub.style.top = cy - 6.5f;
            hub.style.backgroundColor = new StyleColor(LcdHudTheme.Brass);
            UITheme.Radius(hub, 6.5f);
            UITheme.Border(hub, 1.5f, IronDark);
            hub.pickingMode = PickingMode.Ignore;
            body.Add(hub);
            var pin = new VisualElement();
            pin.style.position = Position.Absolute;
            pin.style.width = 5; pin.style.height = 5;
            pin.style.left = cx - 2.5f; pin.style.top = cy - 2.5f;
            pin.style.backgroundColor = new StyleColor(IronDark);
            UITheme.Radius(pin, 2.5f);
            pin.pickingMode = PickingMode.Ignore;
            body.Add(pin);

            var val = new Label(valueText ?? string.Empty);
            val.style.fontSize = 10;
            val.style.unityFontStyleAndWeight = FontStyle.Bold;
            val.style.color = new StyleColor(Cream);
            val.style.marginTop = 3;
            val.style.unityTextAlign = TextAnchor.MiddleCenter;
            val.pickingMode = PickingMode.Ignore;
            col.Add(val);

            return col;
        }

        /// <summary>A centred row of dials with even spacing.</summary>
        public static VisualElement DialRow(params VisualElement[] dials)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceAround;
            row.style.alignItems = Align.FlexStart;
            row.style.marginTop = 4;
            row.style.marginBottom = 6;
            row.pickingMode = PickingMode.Ignore;
            foreach (var d in dials)
            {
                if (d == null) continue;
                d.style.marginLeft = 4;
                d.style.marginRight = 4;
                row.Add(d);
            }
            return row;
        }
    }
}
