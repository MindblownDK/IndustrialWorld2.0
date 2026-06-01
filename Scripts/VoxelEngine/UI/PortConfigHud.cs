// Assets/Scripts/VoxelEngine/UI/PortConfigHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          PORT CONFIGURATION WIDGET — Inline machine panel      ║
// ║   6-face cycle buttons: None → Input → Output → None          ║
// ║   Premium pill-button grid with clear colour language.         ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Transport;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class PortConfigHud
    {
        // Face labels and per-direction colour palette.
        private static readonly string[] FaceLabels = { "+X", "−X", "+Y", "−Y", "+Z", "−Z" };

        private static readonly Color ColNone   = new(0.16f, 0.18f, 0.23f);
        private static readonly Color ColInput  = new(0.15f, 0.48f, 0.82f);
        private static readonly Color ColOutput = new(0.82f, 0.50f, 0.12f);

        /// <summary>
        /// Builds a compact 6-face port configuration grid.
        /// Click cycles: None → Input → Output → None.
        /// </summary>
        public static VisualElement Build(PortConfig config, Action onChanged = null)
        {
            if (config == null)
            {
                var err = T.Muted("No PortConfig component found.");
                return err;
            }

            config.EnsureAllFaces();

            var root = new VisualElement();
            root.style.marginTop    = 6;
            root.style.marginBottom = 4;

            // Section header.
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems    = Align.Center;
            titleRow.style.marginBottom  = 6;
            titleRow.pickingMode = PickingMode.Ignore;

            var titleLbl = T.Subtitle("Port Configuration");
            titleLbl.style.flexGrow    = 1;
            titleLbl.style.marginTop   = 0;
            titleRow.Add(titleLbl);

            // Legend row.
            titleRow.Add(MakeLegendDot(ColInput,  "IN"));
            titleRow.Add(T.Spacer(4));
            titleRow.Add(MakeLegendDot(ColOutput, "OUT"));
            root.Add(titleRow);

            var hint = T.Muted("Click a face to cycle its direction");
            hint.style.marginBottom = 8;
            hint.style.marginTop    = 0;
            root.Add(hint);

            // 3×2 button grid.
            var grid = new VisualElement();
            grid.style.flexDirection  = FlexDirection.Row;
            grid.style.flexWrap       = Wrap.Wrap;
            grid.style.paddingTop     = 4;
            grid.style.paddingBottom  = 4;
            grid.style.paddingLeft    = 4;
            grid.style.paddingRight   = 4;
            grid.style.backgroundColor = new StyleColor(T.BgCard);
            T.Radius(grid, T.CardRadius);
            T.Border(grid, 1, T.BorderDim);
            root.Add(grid);

            for (int i = 0; i < 6; i++)
            {
                int   idx  = i;
                var   face = (CubeFace)i;
                var   dir  = config.GetDirection(face);
                Color bg   = DirectionColor(dir);

                var btn = new Button(() =>
                {
                    var cur  = config.GetDirection((CubeFace)idx);
                    var next = cur switch
                    {
                        PortDirection.None   => PortDirection.Input,
                        PortDirection.Input  => PortDirection.Output,
                        PortDirection.Output => PortDirection.None,
                        _                   => PortDirection.None
                    };
                    config.SetDirection((CubeFace)idx, next);
                    onChanged?.Invoke();
                });

                btn.style.width                   = 66;
                btn.style.height                  = 44;
                btn.style.marginRight             = 4;
                btn.style.marginBottom            = 4;
                btn.style.color                   = Color.white;
                btn.style.unityFontStyleAndWeight = FontStyle.Bold;
                btn.style.fontSize                = 9;
                btn.style.letterSpacing           = 0.5f;
                btn.style.backgroundColor         = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.85f));
                T.Radius(btn, 5f);
                T.Border(btn, 1, new Color(bg.r, bg.g, bg.b, 0.35f));

                // Two-line label: face name + direction
                btn.text = $"{FaceLabels[i]}\n<size=8>{dir}</size>";
                btn.enableRichText = true;

                grid.Add(btn);
            }

            return root;
        }

        // ── Helpers ─────────────────────────────────────────────────────
        private static Color DirectionColor(PortDirection dir) => dir switch
        {
            PortDirection.Input  => ColInput,
            PortDirection.Output => ColOutput,
            _                   => ColNone
        };

        private static VisualElement MakeLegendDot(Color color, string label)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.pickingMode         = PickingMode.Ignore;

            var dot = new VisualElement();
            dot.style.width           = 7;
            dot.style.height          = 7;
            dot.style.backgroundColor = new StyleColor(color);
            T.Radius(dot, 3.5f);
            dot.style.marginRight = 3;
            dot.pickingMode       = PickingMode.Ignore;
            row.Add(dot);

            var lbl = new Label(label);
            lbl.style.color    = new StyleColor(new Color(color.r, color.g, color.b, 0.80f));
            lbl.style.fontSize = 8;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.pickingMode = PickingMode.Ignore;
            row.Add(lbl);

            return row;
        }
    }
}
