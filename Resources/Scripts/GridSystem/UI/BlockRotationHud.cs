// Assets/Scripts/VoxelEngine/GridSystem/UI/BlockRotationHud.cs
//
// Universal block-rotation helper shown while holding any placeable block.
// It explains the Shift / Ctrl / Shift+Ctrl scroll controls and includes a
// small cube diagram so the player understands the rotation axes.

using UnityEngine;
using UnityEngine.UIElements;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.GridSystem.UI
{
    public static class BlockRotationHud
    {
        private static VisualElement _root, _box;
        private static Label _nameLabel;
        private static VisualElement _cube;
        private static Label _stepLabel;
        private static bool _visible;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _box != null && _box.parent == uiRoot) return;
            _root = uiRoot;
            if (_box != null) _box.RemoveFromHierarchy();

            _box = new VisualElement { name = "BlockRotationHud" };
            _box.style.position = Position.Absolute;
            _box.style.top = 18;
            _box.style.right = 18;
            _box.style.width = 250;
            _box.style.backgroundColor = new StyleColor(new Color(0.035f, 0.045f, 0.060f, 0.94f));
            T.Border(_box, 1, T.AccentCyan);
            T.Radius(_box, 12);
            _box.style.paddingTop = 12;
            _box.style.paddingBottom = 12;
            _box.style.paddingLeft = 12;
            _box.style.paddingRight = 12;
            _box.pickingMode = PickingMode.Ignore;
            _box.style.display = DisplayStyle.None;

            _nameLabel = new Label("");
            _nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _nameLabel.style.fontSize = 13;
            _nameLabel.style.color = new StyleColor(Color.white);
            _nameLabel.style.marginBottom = 6;
            _box.Add(_nameLabel);

            var title = new Label("ROTATE PLACEMENT");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 11;
            title.style.letterSpacing = 1.4f;
            title.style.color = new StyleColor(T.AccentCyan);
            title.style.marginBottom = 8;
            _box.Add(title);

            _box.Add(BuildCubeGuide());
            _box.Add(Row("Ctrl + Scroll", "Yaw / turn", T.AccentGreen));
            _box.Add(Row("Shift + Scroll", "Pitch / tilt", T.AccentGold));
            _box.Add(Row("Ctrl + Shift + Scroll", "Roll", T.AccentTeal));
            _box.Add(Row("Plain Scroll", "Switch hotbar", new Color(0.62f, 0.66f, 0.72f)));

            uiRoot.Add(_box);
        }

        private static VisualElement BuildCubeGuide()
        {
            var wrap = new VisualElement();
            wrap.style.height = 116;
            wrap.style.marginBottom = 8;
            wrap.style.alignItems = Align.Center;
            wrap.style.justifyContent = Justify.Center;
            wrap.pickingMode = PickingMode.Ignore;

            _cube = new VisualElement();
            _cube.style.position = Position.Relative;
            _cube.style.width = 84;
            _cube.style.height = 84;
            _cube.style.backgroundColor = new StyleColor(new Color(0.12f, 0.27f, 0.36f, 0.58f));
            T.Border(_cube, 2, new Color(0.35f, 0.75f, 0.95f, 0.65f));
            T.Radius(_cube, 6);
            _cube.style.rotate = new StyleRotate(new Rotate(new Angle(0f, AngleUnit.Degree)));
            _cube.pickingMode = PickingMode.Ignore;
            wrap.Add(_cube);

            _cube.Add(Arrow("Yaw", "↔", T.AccentGreen, 4, 28, 76, 24));
            _cube.Add(Arrow("Pitch", "↕", T.AccentGold, 28, 2, 30, 24));
            _cube.Add(Arrow("Roll", "⟳", T.AccentTeal, 28, 56, 34, 24));

            _stepLabel = new Label("Pitch 0° · Yaw 0° · Roll 0°");
            _stepLabel.style.position = Position.Absolute;
            _stepLabel.style.bottom = 0;
            _stepLabel.style.left = 0;
            _stepLabel.style.right = 0;
            _stepLabel.style.fontSize = 9;
            _stepLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _stepLabel.style.color = new StyleColor(new Color(0.78f, 0.84f, 0.90f));
            _stepLabel.pickingMode = PickingMode.Ignore;
            wrap.Add(_stepLabel);

            return wrap;
        }

        private static Label Arrow(string tooltip, string symbol, Color color, float left, float top, float width, float height)
        {
            var label = new Label(symbol);
            label.tooltip = tooltip;
            label.style.position = Position.Absolute;
            label.style.left = left;
            label.style.top = top;
            label.style.width = width;
            label.style.height = height;
            label.style.fontSize = 22;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(color);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private static void UpdateCube(Vector3Int steps)
        {
            if (_cube != null)
            {
                float yaw = steps.y * 90f;
                float roll = steps.z * 90f;
                float pitch = steps.x * 90f;
                _cube.style.rotate = new StyleRotate(new Rotate(new Angle(yaw + roll, AngleUnit.Degree)));
                float pitchScale = Mathf.Lerp(1f, 0.72f, Mathf.Abs(Mathf.Sin(pitch * Mathf.Deg2Rad)));
                _cube.style.scale = new StyleScale(new Scale(new Vector3(1f, pitchScale, 1f)));
                _cube.style.borderTopColor = new StyleColor(steps.y != 0 ? T.AccentGreen : new Color(0.35f, 0.75f, 0.95f, 0.65f));
                _cube.style.borderRightColor = new StyleColor(steps.z != 0 ? T.AccentTeal : new Color(0.35f, 0.75f, 0.95f, 0.65f));
                _cube.style.borderBottomColor = new StyleColor(steps.x != 0 ? T.AccentGold : new Color(0.35f, 0.75f, 0.95f, 0.65f));
            }

            if (_stepLabel != null)
            {
                _stepLabel.text = $"Pitch {steps.x * 90}° · Yaw {steps.y * 90}° · Roll {steps.z * 90}°";
            }
        }

        private static VisualElement Row(string keys, string action, Color color)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginBottom = 4;

            var key = new Label(keys);
            key.style.fontSize = 10;
            key.style.color = new StyleColor(color);
            key.style.unityFontStyleAndWeight = FontStyle.Bold;
            key.pickingMode = PickingMode.Ignore;

            var value = new Label(action);
            value.style.fontSize = 10;
            value.style.color = new StyleColor(new Color(0.78f, 0.82f, 0.88f));
            value.pickingMode = PickingMode.Ignore;

            row.Add(key);
            row.Add(value);
            return row;
        }

        public static void Tick()
        {
            if (_box == null) return;

            bool gridBlock = GridBuilder.HoldingGridBlock;
            bool staticBlock = VoxelEngine.Building.BuildSystem.HoldingBlock;
            bool show = (gridBlock || staticBlock) && !VoxelEngine.UI.UIState.IsBlocking;

            _box.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
            {
                string heldName = gridBlock ? GridBuilder.HeldBlockName : VoxelEngine.Building.BuildSystem.HeldBlockName;
                Vector3Int steps = gridBlock ? GridBuilder.RotationSteps : VoxelEngine.Building.BuildSystem.RotationSteps;
                if (_nameLabel != null) _nameLabel.text = "▣ " + heldName;
                UpdateCube(steps);
            }

            if (show != _visible)
                _visible = show;
        }
    }
}
