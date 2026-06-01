// Assets/Scripts/VoxelEngine/Menu/InGamePauseMenu.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║             IN-GAME PAUSE MENU — Premium overlay               ║
// ║   Dark frosted backdrop, centred card, 3 action buttons.       ║
// ║   Settings sub-page with Display / Camera / Audio / Keybinds. ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;
using Cursor      = UnityEngine.Cursor;
using T           = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Menu
{
    [RequireComponent(typeof(UIDocument))]
    public class InGamePauseMenu : MonoBehaviour
    {
        [Tooltip("Scene name to return to on 'Save & Quit'.")]
        public string mainMenuScene = "MainMenu";

        // ── State ──────────────────────────────────────────────────
        private UIDocument    _doc;
        private VisualElement _root;
        private bool          _open;
        private float         _savedTS;
        private CursorLockMode _savedLock;
        private bool          _savedVis;

        private enum Page  { Pause, Settings }
        private enum STab  { Display, Camera, Audio, Keybinds }
        private Page _page = Page.Pause;
        private STab _tab  = STab.Camera;

        // ── Unity Lifecycle ────────────────────────────────────────
        private void Awake()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc.panelSettings == null)
                _doc.panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
            _root = _doc.rootVisualElement;
            _root.style.flexGrow = 1;
            HideUI();
        }

        private void Update()
        {
            if (VoxelEngine.UI.UIState.PauseConsumedThisFrame) return;
            if (!GameSettings.WasPressed(InputAction.Pause)) return;
            if (_open) { Close(); return; }
            if (VoxelEngine.UI.UIState.IsBlocking) return;
            Open();
        }

        // ── Open / Close ───────────────────────────────────────────
        private void Open()
        {
            _open = true;
            VoxelEngine.UI.UIState.PushBlock();
            _savedTS   = Time.timeScale;
            _savedLock = Cursor.lockState;
            _savedVis  = Cursor.visible;
            Time.timeScale      = 0f;
            Cursor.lockState    = CursorLockMode.None;
            Cursor.visible      = true;
            _page = Page.Pause;
            _tab  = STab.Camera;
            BuildUI();
        }

        private void Close()
        {
            _open = false;
            VoxelEngine.UI.UIState.PopBlock();
            Time.timeScale   = _savedTS;
            Cursor.lockState = _savedLock;
            Cursor.visible   = _savedVis;
            HideUI();
        }

        private void HideUI()
        {
            _root.Clear();
            _root.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
            _root.pickingMode = PickingMode.Ignore;
        }

        // ── UI Root ────────────────────────────────────────────────
        private void BuildUI()
        {
            _root.Clear();
            // Frosted dark backdrop.
            _root.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.62f));
            _root.style.alignItems      = Align.Center;
            _root.style.justifyContent  = Justify.Center;
            _root.pickingMode           = PickingMode.Position;

            if (_page == Page.Pause)  BuildPause();
            else                      BuildSettings();
        }

        // ── Pause Page ─────────────────────────────────────────────
        private void BuildPause()
        {
            var panel = MakePanel(360, 0);
            _root.Add(panel);

            // Logo / title section.
            var logoRow = new VisualElement();
            logoRow.style.flexDirection  = FlexDirection.Row;
            logoRow.style.alignItems     = Align.Center;
            logoRow.style.justifyContent = Justify.Center;
            logoRow.style.marginBottom   = 4;
            logoRow.pickingMode = PickingMode.Ignore;

            var ico = new Label("⏸");
            ico.style.fontSize  = 20;
            ico.style.marginRight = 10;
            ico.pickingMode = PickingMode.Ignore;
            logoRow.Add(ico);

            var title = T.Title("PAUSED");
            title.style.fontSize    = 22;
            title.style.letterSpacing = 4;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            logoRow.Add(title);
            panel.Add(logoRow);

            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(10));

            panel.Add(PrimaryBtn("▶   RESUME",      Close,                                T.AccentCyan));
            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("⚙   SETTINGS",    () => { _page = Page.Settings; BuildUI(); }, T.BgSlot));
            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("⬅   SAVE & QUIT", QuitToMenu,                           T.AccentRed));
        }

        // ── Settings Page ──────────────────────────────────────────
        private void BuildSettings()
        {
            var panel = MakePanel(700, 560);
            _root.Add(panel);

            // Header.
            var hdr = new VisualElement();
            hdr.style.flexDirection = FlexDirection.Row;
            hdr.style.alignItems    = Align.Center;
            hdr.style.marginBottom  = 6;

            var title = T.Title("SETTINGS");
            title.style.flexGrow = 1;
            hdr.Add(title);

            var backBtn = PrimaryBtn("← BACK", () => { _page = Page.Pause; BuildUI(); }, T.BgSlot);
            backBtn.style.minWidth  = 90;
            backBtn.style.minHeight = 30;
            backBtn.style.fontSize  = 11;
            hdr.Add(backBtn);
            panel.Add(hdr);
            panel.Add(T.AccentDivider());

            // Tab bar.
            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginBottom  = 12;
            tabs.Add(TabBtn("Display",  STab.Display));
            tabs.Add(TabBtn("Camera",   STab.Camera));
            tabs.Add(TabBtn("Audio",    STab.Audio));
            tabs.Add(TabBtn("Keybinds", STab.Keybinds));
            panel.Add(tabs);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            panel.Add(scroll);

            switch (_tab)
            {
                case STab.Display:  DisplayTab(scroll);  break;
                case STab.Camera:   CameraTab(scroll);   break;
                case STab.Audio:    AudioTab(scroll);     break;
                case STab.Keybinds: KeybindTab(scroll);   break;
            }

            panel.Add(T.Spacer(8));
            var resetBtn = PrimaryBtn("RESET DEFAULTS", () => { GameSettings.ResetToDefaults(); BuildUI(); }, T.AccentRed);
            resetBtn.style.alignSelf = Align.FlexEnd;
            resetBtn.style.minWidth  = 150;
            resetBtn.style.minHeight = 28;
            resetBtn.style.fontSize  = 10;
            panel.Add(resetBtn);
        }

        // ── Settings Tab Content ───────────────────────────────────
        private void DisplayTab(VisualElement p)
        {
            p.Add(SettingRow("View Distance (chunks)"));
            p.Add(IntSl(2, 16, GameSettings.ViewDistance, v => GameSettings.ViewDistance = v));
            p.Add(T.Spacer(10));
            p.Add(SettingRow("VSync"));
            p.Add(IntSl(0, 2, GameSettings.VSync, v => GameSettings.VSync = v));
        }

        private void CameraTab(VisualElement p)
        {
            p.Add(SettingRow("Field of View"));
            p.Add(FloatSl(40f, 120f, GameSettings.Fov, v => GameSettings.Fov = v));
            p.Add(T.Spacer(10));
            p.Add(SettingRow("Mouse Sensitivity"));
            p.Add(FloatSl(0.02f, 1.5f, GameSettings.MouseSensitivity, v => GameSettings.MouseSensitivity = v));
            p.Add(T.Spacer(10));
            p.Add(SettingRow("Invert Y-Axis"));
            var toggle = new Toggle { value = GameSettings.InvertY };
            toggle.style.marginBottom = 4;
            toggle.RegisterValueChangedCallback(e => GameSettings.InvertY = e.newValue);
            p.Add(toggle);
        }

        private void AudioTab(VisualElement p)
        {
            float vol = GameSettings.MasterVolume;
            p.Add(SettingRow($"Master Volume  —  {Mathf.Round(vol * 100f):0}%"));
            var s = new Slider(0f, 1f) { value = vol, showInputField = true };
            s.style.marginBottom = 4;
            s.RegisterValueChangedCallback(e => { GameSettings.MasterVolume = e.newValue; BuildUI(); });
            p.Add(s);
        }

        private void KeybindTab(VisualElement p)
        {
            foreach (InputAction a in Enum.GetValues(typeof(InputAction)))
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems    = Align.Center;
                row.style.marginBottom  = 5;
                row.style.paddingTop    = 4;
                row.style.paddingBottom = 4;
                row.style.paddingLeft   = 8;
                row.style.paddingRight  = 8;
                row.style.backgroundColor = new StyleColor(T.BgCard);
                T.Radius(row, 5f);

                var lbl = new Label(a.ToString());
                lbl.style.color    = new StyleColor(T.TextSecondary);
                lbl.style.fontSize = 12;
                lbl.style.flexGrow = 1;
                lbl.style.minHeight = 24;
                row.Add(lbl);

                var btn = T.SmallButton(GameSettings.GetKey(a), null, T.AccentTeal);
                btn.style.minWidth = 120;
                btn.clickable.clicked += () =>
                {
                    btn.text = "Press key…";
                    btn.style.backgroundColor = new StyleColor(new Color(T.AccentGold.r, T.AccentGold.g, T.AccentGold.b, 0.80f));
                    var cap = gameObject.AddComponent<KeyRebindCapture>();
                    cap.onCaptured = code => { GameSettings.SetKey(a, code); BuildUI(); };
                };
                row.Add(btn);
                p.Add(row);
            }
        }

        // ── Helpers ────────────────────────────────────────────────
        private static VisualElement MakePanel(int w, int h)
        {
            var v = new VisualElement();
            if (w > 0) v.style.width  = w;
            if (h > 0) v.style.height = h;
            v.style.paddingTop    = T.PanelPaddingV + 4;
            v.style.paddingBottom = T.PanelPaddingV + 4;
            v.style.paddingLeft   = T.PanelPaddingH;
            v.style.paddingRight  = T.PanelPaddingH;
            v.style.backgroundColor = new StyleColor(T.BgPanel);
            T.Radius(v, T.PanelRadius);
            T.Border(v, 1, T.BorderBright);
            return v;
        }

        private Button PrimaryBtn(string text, Action onClick, Color bg)
        {
            var b = new Button(onClick) { text = text };
            b.style.minHeight                 = 42;
            b.style.fontSize                  = 13;
            b.style.unityFontStyleAndWeight   = FontStyle.Bold;
            b.style.letterSpacing             = 0.8f;
            b.style.color                     = Color.white;
            b.style.backgroundColor           = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.85f));
            T.Radius(b, T.ButtonRadius);
            T.Border(b, 0, Color.clear);
            return b;
        }

        private Button TabBtn(string text, STab tab)
        {
            bool active = _tab == tab;
            var b = new Button(() => { _tab = tab; BuildUI(); }) { text = text };
            b.style.minHeight               = 30;
            b.style.minWidth                = 90;
            b.style.fontSize                = 11;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.color                   = active ? Color.white : new StyleColor(T.TextSecondary).value;
            b.style.backgroundColor = new StyleColor(active
                ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.85f)
                : new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.85f));
            T.Radius(b, T.ButtonRadius);
            T.Border(b, 0, Color.clear);
            b.style.marginRight = 5;
            return b;
        }

        private static Label SettingRow(string text)
        {
            var l = new Label(text);
            l.style.color    = new StyleColor(T.TextSecondary);
            l.style.fontSize = 12;
            l.style.minHeight = 20;
            l.style.marginTop = 2;
            return l;
        }

        private static VisualElement IntSl(int min, int max, int v, Action<int> cb)
        {
            var s = new SliderInt(min, max) { value = v, showInputField = true };
            s.style.marginBottom = 4;
            s.RegisterValueChangedCallback(e => cb(e.newValue));
            return s;
        }

        private static VisualElement FloatSl(float min, float max, float v, Action<float> cb)
        {
            var s = new Slider(min, max) { value = v, showInputField = true };
            s.style.marginBottom = 4;
            s.RegisterValueChangedCallback(e => cb(e.newValue));
            return s;
        }

        private void QuitToMenu()
        {
            Time.timeScale = 1f;
            VoxelEngine.Persistence.WorldStatePersistence.Instance?.SaveAll();
            VoxelEngine.Research.ResearchManager.Instance?.SaveToDisk();
            try { SceneManager.LoadScene(mainMenuScene); }
            catch (Exception ex) { Debug.LogError("[PauseMenu] Scene load failed: " + ex.Message); }
        }
    }
}
