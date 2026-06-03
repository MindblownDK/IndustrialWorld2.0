// Assets/Scripts/VoxelEngine/UI/PortConfigHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          PORT CONFIGURATION WIDGET — Inline machine panel      ║
// ║   6-face cycle buttons: None → Input → Output → None          ║
// ║   Network type dropdown per face for advanced configuration    ║
// ║   Premium pill-button grid with clear colour language.         ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
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

        // Network type options for dropdown
        private static readonly string[] NetworkTypeOptions = { "Any", "Power", "Data", "Fluid", "Gas" };

        /// <summary>
        /// Builds a compact 6-face port configuration grid with network type dropdowns.
        /// Click cycles: None → Input → Output → None.
        /// Network type dropdown appears below each face button when enabled.
        /// </summary>
        public static VisualElement Build(PortConfig config, Action onChanged = null, bool showNetworkTypeDropdown = true)
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
            titleRow.Add(T.Spacer(8));
            titleRow.Add(MakeLegendDot(new Color(0.85f, 0.45f, 0.20f), "PWR"));
            titleRow.Add(T.Spacer(4));
            titleRow.Add(MakeLegendDot(new Color(0.30f, 0.85f, 0.40f), "DAT"));
            root.Add(titleRow);

            var hint = T.Muted("Click a face to cycle: None → In → Out");
            hint.style.marginBottom = 8;
            hint.style.marginTop    = 0;
            root.Add(hint);

            // 3×2 button grid with optional network type dropdowns
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
                var   netType = config.GetNetworkType(face);
                var   enabled = config.IsFaceEnabled(face);
                Color bg   = DirectionColor(dir);

                // Container for this face's controls
                var faceContainer = new VisualElement();
                faceContainer.style.marginRight = 6;
                faceContainer.style.marginBottom = 6;
                faceContainer.style.width = 74;

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
                    
                    // If setting to None, also disable the face
                    if (next == PortDirection.None)
                        config.SetFaceEnabled((CubeFace)idx, false);
                    else if (!config.IsFaceEnabled((CubeFace)idx))
                        config.SetFaceEnabled((CubeFace)idx, true);
                    
                    config.SetDirection((CubeFace)idx, next);
                    onChanged?.Invoke();
                });

                btn.style.width                   = Length.Percent(100);
                btn.style.height                  = 44;
                btn.style.color                   = Color.white;
                btn.style.unityFontStyleAndWeight = FontStyle.Bold;
                btn.style.fontSize                = 9;
                btn.style.letterSpacing           = 0.5f;
                btn.style.backgroundColor         = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.85f));
                T.Radius(btn, 5f);
                T.Border(btn, 1, new Color(bg.r, bg.g, bg.b, 0.35f));

                // Two-line label: face name + direction
                string enabledMark = enabled ? "●" : "○";
                btn.text = $"{FaceLabels[i]}\n<size=8>{enabledMark} {dir}</size>";
                btn.enableRichText = true;

                faceContainer.Add(btn);

                // Network type dropdown (only show when face is enabled)
                if (showNetworkTypeDropdown)
                {
                    var dropdown = MakeNetworkTypeDropdown(config, idx, onChanged);
                    faceContainer.Add(dropdown);
                }

                grid.Add(faceContainer);
            }

            return root;
        }

        /// <summary>
        /// Creates a compact network type dropdown for a specific face.
        /// </summary>
        private static VisualElement MakeNetworkTypeDropdown(PortConfig config, int faceIndex, Action onChanged)
        {
            var container = new VisualElement();
            container.style.height = 20;

            var dropdown = new ToolbarMenu { text = "Any" };
            dropdown.style.width = Length.Percent(100);
            dropdown.style.height = 20;
            dropdown.style.fontSize = 8;
            dropdown.style.unityFontStyleAndWeight = FontStyle.Medium;

            // Build menu items
            var menu = new GenericMenu();
            for (int i = 0; i < NetworkTypeOptions.Length; i++)
            {
                int optionIndex = i;
                menu.AddItem(new GUIContent(NetworkTypeOptions[i]), false, () =>
                {
                    config.SetNetworkType((CubeFace)faceIndex, (PortNetworkType)optionIndex);
                    dropdown.text = NetworkTypeOptions[optionIndex];
                    onChanged?.Invoke();
                });
            }
            dropdown.menu = menu;

            // Update text to match current selection
            var currentType = config.GetNetworkType((CubeFace)faceIndex);
            dropdown.text = NetworkTypeOptions[(int)currentType];

            container.Add(dropdown);
            return container;
        }

        /// <summary>
        /// Builds a simplified port config with a dropdown to select which face to configure.
        /// Useful for complex machines like reactors with many port types.
        /// </summary>
        public static VisualElement BuildWithDropdown(PortConfig config, Action onChanged = null)
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

            // Section header with dropdown
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems    = Align.Center;
            headerRow.style.marginBottom  = 8;
            headerRow.pickingMode = PickingMode.Ignore;

            var titleLbl = T.Subtitle("Port Config");
            titleLbl.style.flexShrink = 0;
            titleLbl.style.marginRight = 8;
            headerRow.Add(titleLbl);

            // Face selector dropdown
            var faceDropdown = new ToolbarMenu { text = "+X" };
            faceDropdown.style.flexGrow = 1;
            var faceMenu = new GenericMenu();
            
            // Track selected face
            int selectedFace = 0;
            Action<int> SelectFace = (idx) =>
            {
                selectedFace = idx;
                UpdateFaceDisplay();
            };

            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                faceMenu.AddItem(new GUIContent(FaceLabels[i]), false, () => SelectFace(idx));
            }
            faceDropdown.menu = faceMenu;
            headerRow.Add(faceDropdown);
            root.Add(headerRow);

            // Current face configuration panel
            VisualElement configPanel = null;
            Action UpdateFaceDisplay = () =>
            {
                if (configPanel != null && configPanel.parent != null)
                    root.Remove(configPanel);

                configPanel = BuildSingleFaceConfig(config, selectedFace, () =>
                {
                    onChanged?.Invoke();
                    UpdateFaceDisplay(); // Refresh display
                });
                root.Add(configPanel);
            };

            UpdateFaceDisplay();
            return root;
        }

        /// <summary>
        /// Builds configuration controls for a single face with all options visible.
        /// </summary>
        private static VisualElement BuildSingleFaceConfig(PortConfig config, int faceIndex, Action onChanged)
        {
            var container = new VisualElement();
            container.style.backgroundColor = new StyleColor(T.BgCard);
            T.Radius(container, 6);
            container.style.paddingTop = 8;
            container.style.paddingBottom = 8;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            var face = (CubeFace)faceIndex;
            var dir = config.GetDirection(face);
            var netType = config.GetNetworkType(face);
            var enabled = config.IsFaceEnabled(face);

            // Direction buttons row
            var dirRow = new VisualElement();
            dirRow.style.flexDirection = FlexDirection.Row;
            dirRow.style.marginBottom = 8;
            dirRow.pickingMode = PickingMode.Ignore;
            container.Add(dirRow);

            dirRow.Add(T.Label("Direction:", 9));
            dirRow.Add(T.Spacer(8));

            // Direction: None
            var btnNone = MakeDirButton("None", dir == PortDirection.None, () =>
            {
                config.SetDirection(face, PortDirection.None);
                config.SetFaceEnabled(face, false);
                onChanged?.Invoke();
            }, ColNone);
            dirRow.Add(btnNone);

            // Direction: Input
            var btnIn = MakeDirButton("Input", dir == PortDirection.Input, () =>
            {
                config.SetDirection(face, PortDirection.Input);
                config.SetFaceEnabled(face, true);
                onChanged?.Invoke();
            }, ColInput);
            dirRow.Add(btnIn);

            // Direction: Output
            var btnOut = MakeDirButton("Output", dir == PortDirection.Output, () =>
            {
                config.SetDirection(face, PortDirection.Output);
                config.SetFaceEnabled(face, true);
                onChanged?.Invoke();
            }, ColOutput);
            dirRow.Add(btnOut);

            // Network type row
            var netRow = new VisualElement();
            netRow.style.flexDirection = FlexDirection.Row;
            netRow.style.alignItems = Align.Center;
            netRow.pickingMode = PickingMode.Ignore;
            container.Add(netRow);

            netRow.Add(T.Label("Network Type:", 9));
            netRow.Add(T.Spacer(8));

            var netDropdown = new ToolbarMenu { text = NetworkTypeOptions[(int)netType] };
            netDropdown.style.flexGrow = 1;
            var netMenu = new GenericMenu();
            for (int i = 0; i < NetworkTypeOptions.Length; i++)
            {
                int idx = i;
                netMenu.AddItem(new GUIContent(NetworkTypeOptions[i]), netType == (PortNetworkType)idx, () =>
                {
                    config.SetNetworkType(face, (PortNetworkType)idx);
                    onChanged?.Invoke();
                });
            }
            netDropdown.menu = netMenu;
            netRow.Add(netDropdown);

            return container;
        }

        private static Button MakeDirButton(string label, bool selected, Action onClick, Color baseColor)
        {
            var btn = new Button(() => onClick());
            btn.text = label;
            btn.style.fontSize = 8;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.paddingTop = 4;
            btn.style.paddingBottom = 4;
            btn.style.paddingLeft = 8;
            btn.style.paddingRight = 8;
            btn.style.marginLeft = 2;
            btn.style.marginRight = 2;

            if (selected)
            {
                btn.style.backgroundColor = new StyleColor(baseColor);
                btn.style.color = Color.white;
            }
            else
            {
                btn.style.backgroundColor = new StyleColor(ColNone);
                btn.style.color = new StyleColor(Color.gray);
            }

            T.Radius(btn, 4);
            return btn;
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
