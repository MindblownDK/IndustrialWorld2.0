// Assets/Scripts/VoxelEngine/UI/BombHud.cs
//
// Bomb timer UI:
//   • A FUSE SLIDER (number + slider) shown while the player holds an explosive, writing
//     ExplosiveBlock.NextFuse so the next placed bomb uses that fuse.
//   • A floating COUNTDOWN NUMBER over each placed bomb in view (its remaining fuse, green→red).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class BombHud
    {
        private static VisualElement _root;
        private static VisualElement _sliderPanel;
        private static Slider _slider;
        private static Label _valueLabel;
        private static VisualElement _timerLayer;
        private static readonly List<Label> _labels = new();
        private const int MAX_LABELS = 8;

        private static float _scanAcc;
        private static readonly List<MonoBehaviour> _bombs = new();

        public static void EnsureMounted(VisualElement uiRoot)
        {
            // Re-add if the root was cleared underneath us.
            if (_sliderPanel != null && _sliderPanel.parent == null && _root == uiRoot) { uiRoot.Add(_sliderPanel); }
            if (_timerLayer != null && _timerLayer.parent == null && _root == uiRoot) { uiRoot.Add(_timerLayer); }
            if (_root == uiRoot && _sliderPanel != null && _sliderPanel.parent == uiRoot) return;

            _root = uiRoot;
            if (_sliderPanel != null) _sliderPanel.RemoveFromHierarchy();
            if (_timerLayer != null) _timerLayer.RemoveFromHierarchy();

            // ── Fuse slider panel (bottom-centre, only while holding a bomb). ──
            _sliderPanel = new VisualElement { name = "BombFusePanel" };
            _sliderPanel.style.position = Position.Absolute;
            _sliderPanel.style.left = Length.Percent(30); _sliderPanel.style.right = Length.Percent(30);
            _sliderPanel.style.bottom = 84;
            _sliderPanel.style.flexDirection = FlexDirection.Row;
            _sliderPanel.style.alignItems = Align.Center;
            _sliderPanel.style.backgroundColor = new StyleColor(new Color(0.05f, 0.06f, 0.09f, 0.88f));
            _sliderPanel.style.paddingTop = 5; _sliderPanel.style.paddingBottom = 5;
            _sliderPanel.style.paddingLeft = 10; _sliderPanel.style.paddingRight = 10;
            T.Radius(_sliderPanel, 6); T.Border(_sliderPanel, 1, new Color(1f, 0.55f, 0.15f, 0.55f));
            _sliderPanel.style.display = DisplayStyle.None;

            var title = new Label("BOMB FUSE");
            title.style.color = new Color(1f, 0.62f, 0.22f);
            title.style.fontSize = 11; title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginRight = 8;
            _sliderPanel.Add(title);

            _slider = new Slider(1, 30) { value = 5f };
            _slider.style.flexGrow = 1; _slider.style.minWidth = 80;
            _slider.RegisterValueChangedCallback(e => VoxelEngine.Combat.ExplosiveBlock.NextFuse = Mathf.Clamp(e.newValue, 1f, 30f));
            _sliderPanel.Add(_slider);

            _valueLabel = new Label("5.0s");
            _valueLabel.style.color = Color.white;
            _valueLabel.style.fontSize = 13; _valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _valueLabel.style.minWidth = 40; _valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _valueLabel.style.marginLeft = 8;
            _sliderPanel.Add(_valueLabel);
            uiRoot.Add(_sliderPanel);

            // ── Countdown-number layer (full-screen overlay, non-interactive). ──
            _timerLayer = new VisualElement { name = "BombTimerLayer" };
            _timerLayer.style.position = Position.Absolute;
            _timerLayer.style.left = 0; _timerLayer.style.top = 0; _timerLayer.style.right = 0; _timerLayer.style.bottom = 0;
            _timerLayer.pickingMode = PickingMode.Ignore;
            _labels.Clear();
            for (int i = 0; i < MAX_LABELS; i++)
            {
                var l = new Label("");
                l.style.position = Position.Absolute;
                l.style.fontSize = 16; l.style.unityFontStyleAndWeight = FontStyle.Bold;
                l.style.color = Color.white;
                l.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.55f));
                l.style.paddingLeft = 5; l.style.paddingRight = 5; l.style.paddingTop = 1; l.style.paddingBottom = 1;
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                T.Radius(l, 4);
                l.style.display = DisplayStyle.None;
                _timerLayer.Add(l);
                _labels.Add(l);
            }
            uiRoot.Add(_timerLayer);
        }

        public static void Tick(VoxelEngine.Items.Inventory inventory)
        {
            if (_sliderPanel == null) return;

            // ── Slider panel: show only while holding a bomb; sync to NextFuse. ──
            bool holdingBomb = false;
            if (inventory != null && inventory.container != null)
            {
                var st = inventory.ActiveStack;
                if (st != null && !st.IsEmpty && st.item is VoxelEngine.Items.BlockItem bi && bi.placedPrefab != null)
                {
                    if (bi.placedPrefab.GetComponent<VoxelEngine.Combat.ExplosiveBlock>() != null
                        || bi.placedPrefab.GetComponent<VoxelEngine.Combat.AntimatterBomb>() != null)
                        holdingBomb = true;
                }
            }
            if (holdingBomb)
            {
                if (_sliderPanel.style.display == DisplayStyle.None) _sliderPanel.style.display = DisplayStyle.Flex;
                float v = VoxelEngine.Combat.ExplosiveBlock.NextFuse > 0 ? VoxelEngine.Combat.ExplosiveBlock.NextFuse : 5f;
                if (Mathf.Abs(_slider.value - v) > 0.01f) _slider.SetValueWithoutNotify(v);
                _valueLabel.text = v.ToString("0.0") + "s";
            }
            else if (_sliderPanel.style.display != DisplayStyle.None)
            {
                _sliderPanel.style.display = DisplayStyle.None;
            }

            // ── Floating countdown numbers over placed bombs in view (throttled scan). ──
            _scanAcc += Time.unscaledDeltaTime;
            bool scan = _scanAcc >= 0.1f;
            if (scan) _scanAcc = 0f;

            Camera cam = Camera.main;
            int li = 0;
            if (cam != null)
            {
                if (scan)
                {
                    _bombs.Clear();
                    foreach (var k in Object.FindObjectsByType<VoxelEngine.Combat.ExplosiveBlock>())
                        if (k != null && k.isActiveAndEnabled) _bombs.Add(k);
                    foreach (var a in Object.FindObjectsByType<VoxelEngine.Combat.AntimatterBomb>())
                        if (a != null && a.isActiveAndEnabled) _bombs.Add(a);
                }

                Vector3 camPos = cam.transform.position;
                foreach (var b in _bombs)
                {
                    if (b == null) continue;
                    Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(b.transform.position);
                    Vector3 bp = b.transform.position + up * 2.2f;
                    if (Vector3.Distance(camPos, bp) > 60f) continue;
                    Vector3 sp = cam.WorldToScreenPoint(bp);
                    if (sp.z <= 0f || sp.x < 0 || sp.x > Screen.width || sp.y < 0 || sp.y > Screen.height) continue;
                    if (li >= MAX_LABELS) break;

                    float remaining = (b is VoxelEngine.Combat.AntimatterBomb ab) ? ab.fuse : ((VoxelEngine.Combat.ExplosiveBlock)b).fuse;
                    var lbl = _labels[li++];
                    lbl.text = Mathf.Max(0, Mathf.CeilToInt(remaining)) + "s";
                    lbl.style.left = sp.x;
                    lbl.style.top = Screen.height - sp.y;
                    float denom = (b is VoxelEngine.Combat.AntimatterBomb) ? 8f : 5f;
                    float frac = Mathf.Clamp01(remaining / Mathf.Max(1f, denom));
                    lbl.style.color = Color.Lerp(new Color(1f, 0.2f, 0.1f), new Color(0.4f, 0.95f, 0.4f), frac);
                    lbl.style.display = DisplayStyle.Flex;
                }
            }
            for (int i = li; i < MAX_LABELS; i++) _labels[i].style.display = DisplayStyle.None;
        }
    }
}
