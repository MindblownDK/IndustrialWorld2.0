// Assets/Scripts/VoxelEngine/UI/Minimap.cs
//
// Top-right circular minimap. Re-renders every 0.5s to limit cost. Displays player marker
// in the centre + each waypoint as a coloured dot (with off-screen arrow if outside view).

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Core;

namespace VoxelEngine.UI
{
    public static class Minimap
    {
        private const int SIZE = 96;
        private const int RADIUS = 48;          // half the visible voxel diameter

        private static VisualElement _root;
        private static VisualElement _box;
        private static Image _img;
        private static Texture2D _tex;
        private static VisualElement _markersLayer;
        private static float _lastRender;

        /// <summary>Show/hide the minimap (hidden while the block-rotation HUD is up).</summary>
        public static void SetVisible(bool visible)
        {
            if (_box != null) _box.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _box != null && _box.parent == uiRoot) return;
            _root = uiRoot;
            if (_box != null) _box.RemoveFromHierarchy();

            _tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
            _tex.filterMode = FilterMode.Point;

            _box = new VisualElement { name = "Minimap" };
            _box.style.position = Position.Absolute;
            _box.style.top = 16; _box.style.right = 16;
            _box.style.width = SIZE; _box.style.height = SIZE;
            _box.style.borderTopWidth = _box.style.borderBottomWidth =
            _box.style.borderLeftWidth = _box.style.borderRightWidth = 2;
            var bc = new StyleColor(new Color(0.25f, 0.27f, 0.32f));
            _box.style.borderTopColor = _box.style.borderBottomColor =
            _box.style.borderLeftColor = _box.style.borderRightColor = bc;
            _box.style.borderTopLeftRadius = _box.style.borderTopRightRadius =
            _box.style.borderBottomLeftRadius = _box.style.borderBottomRightRadius = 6;
            _box.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_box);

            _img = new Image { image = _tex };
            _img.style.width = SIZE; _img.style.height = SIZE;
            _img.pickingMode = PickingMode.Ignore;
            _box.Add(_img);

            _markersLayer = new VisualElement();
            _markersLayer.style.position = Position.Absolute;
            _markersLayer.style.left = 0; _markersLayer.style.right = 0;
            _markersLayer.style.top = 0; _markersLayer.style.bottom = 0;
            _markersLayer.pickingMode = PickingMode.Ignore;
            _box.Add(_markersLayer);
        }

        public static void Tick(Vector3 playerWorld)
        {
            if (_box == null) return;
            // Re-render every 0.5s.
            var pt = VoxelEngine.Performance.PerformanceThrottle.Instance;
            float mapInterval = pt != null ? pt.minimapInterval : 2.0f;
            if (Time.unscaledTime - _lastRender > mapInterval)
            {
                _lastRender = Time.unscaledTime;
                MapData.RenderMap(_tex, playerWorld, RADIUS, SIZE);
            }
            // Refresh markers.
            _markersLayer.Clear();

            // Player dot (centre).
            _markersLayer.Add(MakeDot(SIZE * 0.5f, SIZE * 0.5f, 8, new Color(0.95f, 0.85f, 0.20f), "▲"));

            // Waypoints — convert world to pixel.
            foreach (var w in MapData.Waypoints)
            {
                Vector2 rel = new Vector2(w.worldPos.x - playerWorld.x, w.worldPos.z - playerWorld.z);
                float pxX = SIZE * 0.5f + rel.x * (SIZE * 0.5f / RADIUS);
                float pxY = SIZE * 0.5f - rel.y * (SIZE * 0.5f / RADIUS);
                if (pxX < 0 || pxX > SIZE || pxY < 0 || pxY > SIZE)
                {
                    // Off-screen — draw an arrow at the rim pointing toward it.
                    Vector2 dir = rel.normalized;
                    float r = SIZE * 0.45f;
                    pxX = SIZE * 0.5f + dir.x * r;
                    pxY = SIZE * 0.5f - dir.y * r;
                    _markersLayer.Add(MakeDot(pxX, pxY, 6, w.color, "→"));
                }
                else
                {
                    _markersLayer.Add(MakeDot(pxX, pxY, 8, w.color, ""));
                }
            }
        }

        private static VisualElement MakeDot(float x, float y, float size, Color color, string label)
        {
            var dot = new VisualElement();
            dot.style.position = Position.Absolute;
            dot.style.left = x - size * 0.5f;
            dot.style.top  = y - size * 0.5f;
            dot.style.width = size; dot.style.height = size;
            dot.style.backgroundColor = new StyleColor(color);
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius =
            dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = size * 0.5f;
            dot.pickingMode = PickingMode.Ignore;
            if (!string.IsNullOrEmpty(label))
            {
                var l = new Label(label);
                l.style.color = Color.black;
                l.style.fontSize = 9;
                l.style.unityFontStyleAndWeight = FontStyle.Bold;
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                l.style.position = Position.Absolute;
                l.style.left = 0; l.style.right = 0; l.style.top = -1; l.style.bottom = 0;
                l.pickingMode = PickingMode.Ignore;
                dot.Add(l);
            }
            return dot;
        }
    }
}
