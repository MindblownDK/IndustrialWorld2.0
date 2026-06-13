// Assets/Scripts/VoxelEngine/UI/FullMap.cs
//
// Fast draggable map. Renders asynchronously (coroutine) so it doesn't freeze.
// Caches the rendered texture — dragging just moves the image, no re-render.
// Re-renders only when you stop dragging or zoom changes.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Core;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class FullMap : MonoBehaviour
    {
        public static FullMap Instance { get; private set; }

        private UIDocument _doc;
        private VisualElement _root;
        private bool _open;
        private Transform _player;

        // Map state
        private Vector2 _mapCenter;
        private float _zoom = 1f;
        private const int BASE_RADIUS = 200;
        private const int TEX_SIZE = 256; // smaller = MUCH faster

        // Render
        private Texture2D _tex;
        private Image _mapImage;
        private VisualElement _markersLayer;
        private VisualElement _imgWrap;
        private bool _needsRender;
        private Coroutine _renderCoroutine;

        // Drag
        private bool _dragging;
        private Vector2 _dragStartPx;
        private Vector2 _dragStartCenter;

        // Context menu
        private VisualElement _ctxMenu;
        private Waypoint _ctxWP;
        private Vector2 _ctxWorld;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _doc = GetComponent<UIDocument>();
            if (_doc.panelSettings == null)
                _doc.panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
            _root = _doc.rootVisualElement;
            _root.style.flexGrow = 1;
            _tex = new Texture2D(TEX_SIZE, TEX_SIZE, TextureFormat.RGBA32, false);
            _tex.filterMode = FilterMode.Bilinear;
            Hide();
        }

        private void Start()
        {
            _player = VoxelWorld.Instance?.viewer;
            MapData.Load();
        }

        private void Update()
        {
            if (!GameSettings.WasPressed(InputAction.Map)) return;
            if (_open) { Close(); return; }
            // NEVER open when other UI is active.
            if (UIState.IsBlocking) return;
            var gui = GameUIController.Instance;
            if (gui != null && gui.IsInventoryOpen) return;
            Open();
        }

        public void Open()
        {
            if (_open) return;
            _open = true;
            UIState.PushBlock();
            if (_player != null) _mapCenter = new Vector2(_player.position.x, _player.position.z);
            _zoom = 1f;
            BuildUI();
            // Render synchronously on first open (instant, small texture = fast).
            RenderSync();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            UIState.PopBlock();
            _dragging = false;
            if (_renderCoroutine != null) { StopCoroutine(_renderCoroutine); _renderCoroutine = null; }
            Hide();
        }

        private void Hide()
        {
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
        }

        // ── UI ───────────────────────────────────────────────────

        private void BuildUI()
        {
            _root.Clear();
            _root.pickingMode = PickingMode.Position;
            _root.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.65f));
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;

            var panel = new VisualElement();
            panel.style.width = 540; panel.style.height = 560;
            panel.style.paddingTop = 14; panel.style.paddingBottom = 14;
            panel.style.paddingLeft = 18; panel.style.paddingRight = 18;
            panel.style.backgroundColor = new StyleColor(T.BgPanel);
            T.Radius(panel, 10); T.Border(panel, 1, T.BorderBright);
            _root.Add(panel);

            // Header
            var hdr = new VisualElement(); hdr.style.flexDirection = FlexDirection.Row;
            hdr.style.alignItems = Align.Center; hdr.style.marginBottom = 8;
            var title = T.Title("MAP"); title.style.flexGrow = 1; hdr.Add(title);
            hdr.Add(T.Muted("LMB: Drag  Scroll: Zoom  RMB: Waypoint  "));
            var closeBtn = new Button(Close) { text = "✕" };
            closeBtn.style.minWidth = 28; closeBtn.style.minHeight = 28;
            closeBtn.style.fontSize = 14; closeBtn.style.color = Color.white;
            closeBtn.style.backgroundColor = new StyleColor(new Color(0.55f, 0.2f, 0.2f));
            T.Radius(closeBtn, 14); closeBtn.style.borderTopWidth = closeBtn.style.borderBottomWidth =
            closeBtn.style.borderLeftWidth = closeBtn.style.borderRightWidth = 0;
            hdr.Add(closeBtn);
            panel.Add(hdr);

            // Map image area (500x500 display, texture is 256x256 upscaled)
            _imgWrap = new VisualElement();
            _imgWrap.style.width = 500; _imgWrap.style.height = 500;
            _imgWrap.style.alignSelf = Align.Center;
            _imgWrap.style.overflow = Overflow.Hidden;   // ← prevents dragging outside bounds
            _imgWrap.style.backgroundColor = new StyleColor(new Color(0.05f, 0.06f, 0.08f));
            T.Border(_imgWrap, 1, T.BorderDim);
            panel.Add(_imgWrap);

            _mapImage = new Image { image = _tex };
            _mapImage.style.position = Position.Absolute;
            _mapImage.style.left  = 0; _mapImage.style.top = 0;
            _mapImage.style.width = 500; _mapImage.style.height = 500;
            _mapImage.scaleMode = ScaleMode.StretchToFill;
            _imgWrap.Add(_mapImage);

            _markersLayer = new VisualElement();
            _markersLayer.style.position = Position.Absolute;
            _markersLayer.style.left = 0; _markersLayer.style.right = 0;
            _markersLayer.style.top = 0; _markersLayer.style.bottom = 0;
            _markersLayer.pickingMode = PickingMode.Ignore;
            _imgWrap.Add(_markersLayer);

            // Events
            _imgWrap.RegisterCallback<MouseDownEvent>(OnMouseDown);
            _imgWrap.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            _imgWrap.RegisterCallback<MouseUpEvent>(OnMouseUp);
            _imgWrap.RegisterCallback<WheelEvent>(OnWheel);
        }

        // ── Input ────────────────────────────────────────────────

        private void OnMouseDown(MouseDownEvent e)
        {
            CloseCtx();
            if (e.button == 0) // LMB drag
            {
                _dragging = true;
                _dragStartPx = e.localMousePosition;
                _dragStartCenter = _mapCenter;
            }
            else if (e.button == 1) // RMB waypoint
            {
                Vector2 px = e.localMousePosition;
                _ctxWorld = PxToWorld(px);

                // Find nearest existing waypoint
                _ctxWP = null;
                float best = 20f;
                foreach (var wp in MapData.Waypoints)
                {
                    float d = Vector2.Distance(WorldToPx(new Vector2(wp.worldPos.x, wp.worldPos.z)), px);
                    if (d < best) { best = d; _ctxWP = wp; }
                }
                ShowCtx(px);
            }
            e.StopPropagation();
        }

        private void OnMouseMove(MouseMoveEvent e)
        {
            if (!_dragging) return;
            Vector2 delta = e.localMousePosition - _dragStartPx;
            // Move image AND markers together (instant, zero computation).
            _mapImage.style.left = delta.x;
            _mapImage.style.top = delta.y;
            _markersLayer.style.left = delta.x;
            _markersLayer.style.top = delta.y;
        }

        private void OnMouseUp(MouseUpEvent e)
        {
            if (e.button == 0 && _dragging)
            {
                _dragging = false;
                // Calculate how far we dragged in world units.
                Vector2 delta = e.localMousePosition - _dragStartPx;
                float scale = (BASE_RADIUS * 2f * _zoom) / 500f;
                _mapCenter = _dragStartCenter - new Vector2(delta.x, -delta.y) * scale;
                // Reset visual offset and re-render at new position.
                _mapImage.style.left = 0; _mapImage.style.top = 0;
                _markersLayer.style.left = 0; _markersLayer.style.top = 0;
                RequestRender();
            }
        }

        private void OnWheel(WheelEvent e)
        {
            _zoom *= (e.delta.y > 0) ? 1.2f : 0.83f;
            _zoom = Mathf.Clamp(_zoom, 0.15f, 4f);
            RequestRender();
            e.StopPropagation();
        }

        // ── Context Menu ─────────────────────────────────────────

        private void ShowCtx(Vector2 px)
        {
            _ctxMenu = new VisualElement();
            _ctxMenu.style.position = Position.Absolute;
            _ctxMenu.style.left = Mathf.Min(px.x + 4, 340);
            _ctxMenu.style.top = Mathf.Min(px.y + 4, 420);
            _ctxMenu.style.width = 160;
            _ctxMenu.style.backgroundColor = new StyleColor(T.BgDark);
            T.Radius(_ctxMenu, 6); T.Border(_ctxMenu, 1, T.BorderBright);
            _ctxMenu.style.paddingTop = 6; _ctxMenu.style.paddingBottom = 6;
            _ctxMenu.style.paddingLeft = 8; _ctxMenu.style.paddingRight = 8;

            if (_ctxWP == null)
            {
                var nf = new TextField { value = $"WP {MapData.Waypoints.Count + 1}" };
                nf.style.marginBottom = 4; nf.style.minHeight = 22;
                _ctxMenu.Add(nf);
                Btn("Create Waypoint", T.AccentCyan, () => {
                    MapData.AddWaypoint(nf.value, new Vector3(_ctxWorld.x, 0, _ctxWorld.y));
                    CloseCtx(); RequestRender();
                });
            }
            else
            {
                var nf = new TextField { value = _ctxWP.name };
                nf.style.marginBottom = 4; nf.style.minHeight = 22;
                _ctxMenu.Add(nf);
                Btn("Rename", T.AccentCyan, () => {
                    _ctxWP.name = nf.value; MapData.Save(); CloseCtx(); DrawMarkers();
                });
                Btn("Delete", T.AccentRed, () => {
                    MapData.RemoveWaypoint(_ctxWP); CloseCtx(); DrawMarkers();
                });
            }
            Btn("Cancel", T.TextMuted, CloseCtx);
            _markersLayer.Add(_ctxMenu);
        }

        private void Btn(string text, Color bg, System.Action action)
        {
            var b = new Button(action) { text = text };
            b.style.minHeight = 22; b.style.fontSize = 10; b.style.color = Color.white;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.backgroundColor = new StyleColor(bg);
            b.style.marginTop = 2;
            T.Radius(b, 3); b.style.borderTopWidth = b.style.borderBottomWidth =
            b.style.borderLeftWidth = b.style.borderRightWidth = 0;
            _ctxMenu.Add(b);
        }

        private void CloseCtx()
        {
            if (_ctxMenu != null) { _ctxMenu.RemoveFromHierarchy(); _ctxMenu = null; }
        }

        // ── Coordinate conversion (500px display space) ──────────

        private Vector2 PxToWorld(Vector2 px)
        {
            float s = (BASE_RADIUS * 2f * _zoom) / 500f;
            return new Vector2(_mapCenter.x + (px.x - 250) * s, _mapCenter.y + (250 - px.y) * s);
        }

        private Vector2 WorldToPx(Vector2 w)
        {
            float s = 500f / (BASE_RADIUS * 2f * _zoom);
            return new Vector2(250 + (w.x - _mapCenter.x) * s, 250 - (w.y - _mapCenter.y) * s);
        }

        // ── Render (async — no freeze!) ──────────────────────────

        private void RenderSync()
        {
            var world = VoxelWorld.Instance;
            if (world == null) return;
            int r = Mathf.RoundToInt(BASE_RADIUS * _zoom);
            MapData.RenderMap(_tex, new Vector3(_mapCenter.x, 0, _mapCenter.y), r, TEX_SIZE);
            if (_mapImage != null) _mapImage.image = _tex;
            DrawMarkers();
        }

        private void RequestRender()
        {
            if (_renderCoroutine != null) StopCoroutine(_renderCoroutine);
            _renderCoroutine = StartCoroutine(RenderAsync());
        }

        private IEnumerator RenderAsync()
        {
            var world = VoxelWorld.Instance;
            if (world == null) yield break;

            var pixels = _tex.GetPixels32();
            int cx = Mathf.FloorToInt(_mapCenter.x);
            int cz = Mathf.FloorToInt(_mapCenter.y);
            int r = Mathf.RoundToInt(BASE_RADIUS * _zoom);
            float scale = (r * 2f) / TEX_SIZE;

            int rowsPerFrame = 64;

            for (int py = 0; py < TEX_SIZE; py++)
            {
                int wz = cz + Mathf.RoundToInt((py - TEX_SIZE * 0.5f) * scale);
                int row = py * TEX_SIZE;
                for (int px = 0; px < TEX_SIZE; px++)
                {
                    int wx = cx + Mathf.RoundToInt((px - TEX_SIZE * 0.5f) * scale);
                    pixels[row + px] = MapData.SampleColumn(world, wx, wz);
                }
                if (py % rowsPerFrame == 0) yield return null; // yield to next frame
            }

            _tex.SetPixels32(pixels);
            _tex.Apply(false);
            if (_mapImage != null) _mapImage.image = _tex;
            DrawMarkers();
            _renderCoroutine = null;
        }

        // ── Markers ──────────────────────────────────────────────

        private void DrawMarkers()
        {
            if (_markersLayer == null) return;
            var keep = new List<VisualElement>();
            foreach (var ch in _markersLayer.Children())
                if (ch == _ctxMenu) keep.Add(ch);
            _markersLayer.Clear();
            foreach (var k in keep) _markersLayer.Add(k);

            // Player
            if (_player != null)
            {
                var pp = WorldToPx(new Vector2(_player.position.x, _player.position.z));
                if (pp.x > -5 && pp.x < 505 && pp.y > -5 && pp.y < 505)
                    _markersLayer.Insert(0, Dot(pp.x, pp.y, 10, new Color(0.95f, 0.85f, 0.20f)));
            }

            // Waypoints
            foreach (var w in MapData.Waypoints)
            {
                var px = WorldToPx(new Vector2(w.worldPos.x, w.worldPos.z));
                if (px.x < -10 || px.x > 510 || px.y < -10 || px.y > 510) continue;
                _markersLayer.Insert(0, Dot(px.x, px.y, 8, w.color));
                var lbl = new Label(w.name);
                lbl.style.position = Position.Absolute;
                lbl.style.left = px.x + 6; lbl.style.top = px.y - 7;
                lbl.style.color = Color.white; lbl.style.fontSize = 9;
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                lbl.pickingMode = PickingMode.Ignore;
                _markersLayer.Insert(0, lbl);
            }
        }

        private VisualElement Dot(float x, float y, float sz, Color c)
        {
            var d = new VisualElement();
            d.style.position = Position.Absolute;
            d.style.left = x - sz * 0.5f; d.style.top = y - sz * 0.5f;
            d.style.width = sz; d.style.height = sz;
            d.style.backgroundColor = new StyleColor(c);
            T.Radius(d, sz * 0.5f);
            d.pickingMode = PickingMode.Ignore;
            return d;
        }
    }
}
