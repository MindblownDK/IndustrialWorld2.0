// Assets/Scripts/VoxelEngine/GridSystem/UI/BlockRotationHud.cs
//
// Top-right helper box (Space-Engineers style) shown while holding a grid block:
// explains the rotation controls. Hides the minimap while visible.

using UnityEngine;
using UnityEngine.UIElements;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.GridSystem.UI
{
    public static class BlockRotationHud
    {
        private static VisualElement _root, _box;
        private static bool _visible;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _box != null && _box.parent == uiRoot) return;
            _root = uiRoot;
            if (_box != null) _box.RemoveFromHierarchy();

            _box = new VisualElement { name = "BlockRotationHud" };
            _box.style.position = Position.Absolute;
            _box.style.top = 16; _box.style.right = 16;
            _box.style.width = 210;
            _box.style.backgroundColor = new StyleColor(new Color(0.07f, 0.08f, 0.11f, 0.95f));
            T.Border(_box, 1, T.AccentCyan); T.Radius(_box, 8);
            _box.style.paddingTop = 10; _box.style.paddingBottom = 10;
            _box.style.paddingLeft = 12; _box.style.paddingRight = 12;
            _box.pickingMode = PickingMode.Ignore;
            _box.style.display = DisplayStyle.None;

            var title = new Label("⟲  ROTATE BLOCK");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 12; title.style.color = new StyleColor(T.AccentCyan);
            title.style.marginBottom = 6;
            _box.Add(title);

            _box.Add(Row("Ctrl + Scroll", "Yaw (turn)", T.AccentGreen));
            _box.Add(Row("Shift + Scroll", "Pitch (tilt)", T.AccentGold));
            _box.Add(Row("Ctrl+Shift+Scroll", "Roll", T.AccentTeal));
            _box.Add(Row("Plain Scroll", "Switch hotbar", new Color(0.6f,0.64f,0.7f)));

            uiRoot.Add(_box);
        }

        private static VisualElement Row(string keys, string action, Color c)
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.justifyContent = Justify.SpaceBetween;
            r.style.marginBottom = 3;
            var k = new Label(keys); k.style.fontSize = 10; k.style.color = new StyleColor(c);
            k.style.unityFontStyleAndWeight = FontStyle.Bold;
            var a = new Label(action); a.style.fontSize = 10; a.style.color = new StyleColor(new Color(0.75f,0.8f,0.86f));
            r.Add(k); r.Add(a);
            return r;
        }

        public static void Tick()
        {
            if (_box == null) return;
            bool show = GridBuilder.HoldingGridBlock && !VoxelEngine.UI.UIState.IsBlocking;
            if (show != _visible)
            {
                _visible = show;
                _box.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                VoxelEngine.UI.Minimap.SetVisible(!show); // hide minimap while this is up
            }
        }
    }
}
