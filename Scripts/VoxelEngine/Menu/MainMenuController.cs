// Assets/Scripts/VoxelEngine/Menu/MainMenuController.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          MAIN MENU — Pure UI Toolkit, zero prefab deps         ║
// ║   Pages: Main · Saves · New World · Settings                   ║
// ║   Premium dark-steel design, consistent with in-game theme.    ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VoxelEngine.Settings;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Menu
{
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        [Tooltip("Scene name (without .unity) containing your VoxelWorld_Manager. " +
                 "Ensure it is added to File > Build Profiles > Scene List.")]
        public string gameSceneName = "Game";

        // ── State ──────────────────────────────────────────────────
        private UIDocument    _doc;
        private VisualElement _root;
        private WorldSession  _session;

        private enum Page { Main, Saves, NewWorld, Settings }
        private Page _page = Page.Main;

        // New-world form values.
        private string _newName           = "MyWorld";
        private int    _newSeed           = 0;
        private int    _newSeaLevel       = 96;
        private int    _newBaseHeight     = 100;
        private float  _newContinentScale = 0.0015f;

        // Settings tabs.
        private enum STab { Display, Camera, Audio, Keybinds }
        private STab _settingsTab = STab.Display;

        // ── Unity Lifecycle ────────────────────────────────────────
        private void Awake()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc.panelSettings == null)
                _doc.panelSettings = CreateDefaultPanelSettings();

            if (_newSeed == 0)
                _newSeed = UnityEngine.Random.Range(1, int.MaxValue);

            _session = WorldSession.Instance;
            if (_session == null)
            {
                var go = new GameObject("WorldSession");
                _session = go.AddComponent<WorldSession>();
            }
        }

        private void OnEnable() => BuildUI();

        // ── UI Root ────────────────────────────────────────────────
        private void BuildUI()
        {
            _root = _doc.rootVisualElement;
            _root.Clear();
            _root.style.flexGrow        = 1;
            _root.style.backgroundColor = new StyleColor(T.BgBase);
            _root.style.alignItems      = Align.Center;
            _root.style.justifyContent  = Justify.Center;

            switch (_page)
            {
                case Page.Main:     BuildMainPage();     break;
                case Page.Saves:    BuildSavesPage();    break;
                case Page.NewWorld: BuildNewWorldPage(); break;
                case Page.Settings: BuildSettingsPage(); break;
            }
        }

        // ════════════════════════════════════════════════════════════
        //                      MAIN PAGE
        // ════════════════════════════════════════════════════════════
        private void BuildMainPage()
        {
            var panel = MakePanel(420, 0);
            _root.Add(panel);

            // Branding block.
            var brand = new VisualElement();
            brand.style.alignItems  = Align.Center;
            brand.style.marginBottom = 8;
            brand.pickingMode = PickingMode.Ignore;

            var logoIco = new Label("🏭");
            logoIco.style.fontSize        = 36;
            logoIco.style.unityTextAlign  = TextAnchor.MiddleCenter;
            logoIco.style.marginBottom    = 6;
            logoIco.pickingMode = PickingMode.Ignore;
            brand.Add(logoIco);

            var gameTitle = new Label("INDUSTRIAL WORLD");
            gameTitle.style.color                   = new StyleColor(T.TextPrimary);
            gameTitle.style.fontSize                = 24;
            gameTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            gameTitle.style.letterSpacing           = 4f;
            gameTitle.style.unityTextAlign          = TextAnchor.MiddleCenter;
            gameTitle.pickingMode = PickingMode.Ignore;
            brand.Add(gameTitle);

            var tagline = T.Muted("Automate. Expand. Conquer.");
            tagline.style.unityTextAlign = TextAnchor.MiddleCenter;
            tagline.style.letterSpacing  = 1.5f;
            brand.Add(tagline);
            panel.Add(brand);

            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(12));

            // Action buttons.
            panel.Add(PrimaryBtn("▶   PLAY",       () => { _page = Page.Saves;    BuildUI(); }, T.AccentCyan));
            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("✦   NEW WORLD",  () => { _page = Page.NewWorld; BuildUI(); }, T.AccentTeal));
            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("⚙   SETTINGS",   () => { _page = Page.Settings; BuildUI(); }, T.BgSlot));
            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("✕   QUIT",       QuitGame,                                    T.AccentRed));

            panel.Add(T.Spacer(20));
            var ver = T.Muted($"Build {Application.version}");
            ver.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(ver);
        }

        // ════════════════════════════════════════════════════════════
        //                      SAVES PAGE
        // ════════════════════════════════════════════════════════════
        private void BuildSavesPage()
        {
            var panel = MakePanel(660, 0);
            _root.Add(panel);

            panel.Add(PageHeader("SAVES", "⬅  BACK", () => { _page = Page.Main; BuildUI(); }));
            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(4));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow   = 1;
            scroll.style.minHeight  = 380;
            scroll.style.maxHeight  = 480;
            scroll.style.marginBottom = 12;
            panel.Add(scroll);

            var worlds = _session.ListWorlds();
            if (worlds.Count == 0)
            {
                var empty = new VisualElement();
                empty.style.alignItems    = Align.Center;
                empty.style.marginTop     = 60;
                empty.style.marginBottom  = 60;
                empty.pickingMode = PickingMode.Ignore;

                var ico = new Label("🌍");
                ico.style.fontSize       = 32;
                ico.style.unityTextAlign = TextAnchor.MiddleCenter;
                ico.pickingMode = PickingMode.Ignore;
                empty.Add(ico);

                var msg = T.Muted("No saved worlds yet.\nCreate your first world below.");
                msg.style.unityTextAlign = TextAnchor.MiddleCenter;
                msg.style.marginTop      = 8;
                empty.Add(msg);
                scroll.Add(empty);
            }
            else
            {
                foreach (var w in worlds)
                    scroll.Add(BuildSaveRow(w));
            }

            panel.Add(PrimaryBtn("✦   NEW WORLD", () => { _page = Page.NewWorld; BuildUI(); }, T.AccentTeal));
        }

        private VisualElement BuildSaveRow(WorldSummary w)
        {
            var row = new VisualElement();
            row.style.flexDirection   = FlexDirection.Row;
            row.style.alignItems      = Align.Center;
            row.style.paddingTop      = 12;
            row.style.paddingBottom   = 12;
            row.style.paddingLeft     = 14;
            row.style.paddingRight    = 14;
            row.style.marginBottom    = 6;
            row.style.backgroundColor = new StyleColor(T.BgCard);
            T.Radius(row, T.CardRadius);
            T.Border(row, 1, T.BorderDim);

            // Left accent stripe — colour based on save size.
            var stripe = new VisualElement();
            stripe.style.width            = 3;
            stripe.style.alignSelf        = Align.Stretch;
            stripe.style.backgroundColor  = new StyleColor(T.AccentTeal);
            stripe.style.marginRight      = 12;
            stripe.style.borderTopLeftRadius   = 3;
            stripe.style.borderBottomLeftRadius = 3;
            stripe.pickingMode = PickingMode.Ignore;
            row.Add(stripe);

            // Info column.
            var info = new VisualElement();
            info.style.flexGrow  = 1;
            info.pickingMode = PickingMode.Ignore;

            var name = new Label(w.name);
            name.style.color                   = new StyleColor(T.TextPrimary);
            name.style.fontSize                = 15;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.pickingMode = PickingMode.Ignore;
            info.Add(name);

            string size  = w.sizeBytes < 1024 * 1024
                ? $"{w.sizeBytes / 1024.0:0.0} KB"
                : $"{w.sizeBytes / (1024.0 * 1024.0):0.00} MB";
            string seed  = w.savedSeed.HasValue ? $"  ·  seed {w.savedSeed.Value}" : "";
            var meta = T.Muted($"{w.lastWrite:yyyy-MM-dd  HH:mm}  ·  {size}{seed}");
            meta.style.marginTop = 2;
            info.Add(meta);
            row.Add(info);

            // Action buttons.
            var playBtn = T.SmallButton("▶  PLAY",    () => LoadWorld(w.name), T.AccentCyan);
            playBtn.style.marginRight = 6;
            row.Add(playBtn);

            var delBtn  = T.SmallButton("✕  DELETE",  () => { _session.DeleteWorld(w.name); BuildUI(); }, T.AccentRed);
            row.Add(delBtn);

            return row;
        }

        // ════════════════════════════════════════════════════════════
        //                     NEW WORLD PAGE
        // ════════════════════════════════════════════════════════════
        private void BuildNewWorldPage()
        {
            var panel = MakePanel(560, 0);
            _root.Add(panel);

            panel.Add(PageHeader("NEW WORLD", "⬅  BACK", () => { _page = Page.Main; BuildUI(); }));
            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(4));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow   = 1;
            scroll.style.maxHeight  = 460;
            panel.Add(scroll);

            // World Name
            scroll.Add(FormLabel("World Name"));
            var nameField = new TextField { value = _newName };
            StyleField(nameField);
            nameField.RegisterValueChangedCallback(e => _newName = SanitizeName(e.newValue));
            scroll.Add(nameField);
            scroll.Add(T.Spacer(12));

            // Seed row
            scroll.Add(FormLabel("World Seed"));
            var seedRow = new VisualElement();
            seedRow.style.flexDirection = FlexDirection.Row;
            var seedField = new IntegerField { value = _newSeed };
            StyleField(seedField);
            seedField.style.flexGrow = 1;
            seedField.RegisterValueChangedCallback(e => _newSeed = e.newValue);
            seedRow.Add(seedField);
            var rndBtn = T.SmallButton("RANDOM", () =>
            {
                _newSeed = UnityEngine.Random.Range(1, int.MaxValue);
                seedField.SetValueWithoutNotify(_newSeed);
            }, T.AccentTeal);
            rndBtn.style.marginLeft = 8;
            seedRow.Add(rndBtn);
            scroll.Add(seedRow);
            scroll.Add(T.Spacer(12));

            scroll.Add(FormLabel($"Sea Level  —  {_newSeaLevel} voxels"));
            scroll.Add(BuildIntSlider(40, 200, _newSeaLevel, v => { _newSeaLevel = v; BuildUI(); }));
            scroll.Add(T.Spacer(10));

            scroll.Add(FormLabel($"Base Height  —  {_newBaseHeight} voxels"));
            scroll.Add(BuildIntSlider(60, 220, _newBaseHeight, v => { _newBaseHeight = v; BuildUI(); }));
            scroll.Add(T.Spacer(10));

            scroll.Add(FormLabel($"Continent Scale  —  {_newContinentScale:0.0000} (lower = larger)"));
            scroll.Add(BuildFloatSlider(0.0005f, 0.005f, _newContinentScale, v => { _newContinentScale = v; BuildUI(); }));
            scroll.Add(T.Spacer(20));

            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("✦   CREATE & PLAY", CreateAndLoadWorld, T.AccentCyan));
        }

        // ════════════════════════════════════════════════════════════
        //                     SETTINGS PAGE
        // ════════════════════════════════════════════════════════════
        private void BuildSettingsPage()
        {
            var panel = MakePanel(720, 0);
            _root.Add(panel);

            panel.Add(PageHeader("SETTINGS", "⬅  BACK", () => { _page = Page.Main; BuildUI(); }));
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
            scroll.style.flexGrow  = 1;
            scroll.style.maxHeight = 420;
            panel.Add(scroll);

            switch (_settingsTab)
            {
                case STab.Display:  DisplayTab(scroll);  break;
                case STab.Camera:   CameraTab(scroll);   break;
                case STab.Audio:    AudioTab(scroll);     break;
                case STab.Keybinds: KeybindTab(scroll);   break;
            }

            panel.Add(T.Spacer(8));
            var resetBtn = PrimaryBtn("RESET DEFAULTS", () => { GameSettings.ResetToDefaults(); BuildUI(); }, T.AccentRed);
            resetBtn.style.alignSelf = Align.FlexEnd;
            resetBtn.style.minWidth  = 160;
            resetBtn.style.minHeight = 30;
            resetBtn.style.fontSize  = 10;
            panel.Add(resetBtn);
        }

        // ── Settings Tab Implementations ───────────────────────────
        private void DisplayTab(VisualElement p)
        {
            p.Add(FormLabel($"View Distance  —  {GameSettings.ViewDistance} chunks"));
            p.Add(BuildIntSlider(2, 16, GameSettings.ViewDistance, v => { GameSettings.ViewDistance = v; BuildUI(); }));
            p.Add(T.Spacer(10));
            p.Add(FormLabel($"VSync  —  {GameSettings.VSync}"));
            p.Add(BuildIntSlider(0, 2, GameSettings.VSync, v => { GameSettings.VSync = v; BuildUI(); }));
        }

        private void CameraTab(VisualElement p)
        {
            p.Add(FormLabel($"Field of View  —  {GameSettings.Fov:0}°"));
            p.Add(BuildFloatSlider(40f, 120f, GameSettings.Fov, v => { GameSettings.Fov = v; BuildUI(); }));
            p.Add(T.Spacer(10));
            p.Add(FormLabel($"Mouse Sensitivity  —  {GameSettings.MouseSensitivity:0.00}"));
            p.Add(BuildFloatSlider(0.02f, 1.5f, GameSettings.MouseSensitivity, v => { GameSettings.MouseSensitivity = v; BuildUI(); }));
            p.Add(T.Spacer(10));
            p.Add(FormLabel("Invert Y-Axis"));
            var t = new Toggle { value = GameSettings.InvertY };
            t.style.marginBottom = 4;
            t.RegisterValueChangedCallback(e => GameSettings.InvertY = e.newValue);
            p.Add(t);
        }

        private void AudioTab(VisualElement p)
        {
            float vol = GameSettings.MasterVolume;
            p.Add(FormLabel($"Master Volume  —  {Mathf.Round(vol * 100f):0}%"));
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
                row.style.flexDirection   = FlexDirection.Row;
                row.style.alignItems      = Align.Center;
                row.style.marginBottom    = 5;
                row.style.paddingTop      = 6;
                row.style.paddingBottom   = 6;
                row.style.paddingLeft     = 10;
                row.style.paddingRight    = 10;
                row.style.backgroundColor = new StyleColor(T.BgCard);
                T.Radius(row, 5f);

                var lbl = new Label(a.ToString());
                lbl.style.color    = new StyleColor(T.TextSecondary);
                lbl.style.fontSize = 12;
                lbl.style.flexGrow = 1;
                lbl.style.minHeight = 22;
                row.Add(lbl);

                var btn = T.SmallButton(GameSettings.GetKey(a), null, T.AccentTeal);
                btn.style.minWidth = 120;
                btn.clickable.clicked += () =>
                {
                    btn.text = "Press key…";
                    btn.style.backgroundColor = new StyleColor(
                        new Color(T.AccentGold.r, T.AccentGold.g, T.AccentGold.b, 0.80f));
                    var cap = gameObject.AddComponent<KeyRebindCapture>();
                    cap.onCaptured = code => { GameSettings.SetKey(a, code); BuildUI(); };
                };
                row.Add(btn);
                p.Add(row);
            }
        }

        // ── Page Actions ───────────────────────────────────────────
        private void LoadWorld(string worldName)
        {
            _session.worldName  = worldName;
            _session.isNewWorld = false;
            try { SceneManager.LoadScene(gameSceneName); }
            catch (Exception ex) { Debug.LogError("[MainMenu] Could not load scene: " + ex.Message); }
        }

        private void CreateAndLoadWorld()
        {
            if (string.IsNullOrWhiteSpace(_newName)) _newName = "MyWorld";
            _session.worldName         = _newName;
            _session.seed              = _newSeed;
            _session.newSeaLevel       = _newSeaLevel;
            _session.newBaseHeight     = _newBaseHeight;
            _session.newContinentScale = _newContinentScale;
            _session.isNewWorld        = true;
            try { SceneManager.LoadScene(gameSceneName); }
            catch (Exception ex) { Debug.LogError("[MainMenu] Could not load scene: " + ex.Message); }
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ── UI Helpers ─────────────────────────────────────────────
        private static VisualElement MakePanel(int w, int h)
        {
            var v = new VisualElement();
            if (w > 0) v.style.width  = w;
            if (h > 0) v.style.height = h;
            v.style.paddingTop    = T.PanelPaddingV + 6;
            v.style.paddingBottom = T.PanelPaddingV + 6;
            v.style.paddingLeft   = T.PanelPaddingH + 4;
            v.style.paddingRight  = T.PanelPaddingH + 4;
            v.style.backgroundColor = new StyleColor(T.BgPanel);
            T.Radius(v, T.PanelRadius);
            T.Border(v, 1, T.BorderBright);
            return v;
        }

        private Button PrimaryBtn(string text, Action onClick, Color bg)
        {
            var b = new Button(onClick) { text = text };
            b.style.minHeight               = 44;
            b.style.fontSize                = 13;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.letterSpacing           = 0.8f;
            b.style.color                   = Color.white;
            b.style.backgroundColor         = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.85f));
            T.Radius(b, T.ButtonRadius);
            T.Border(b, 0, Color.clear);
            return b;
        }

        private Button TabBtn(string text, STab tab)
        {
            bool active = _settingsTab == tab;
            var b = new Button(() => { _settingsTab = tab; BuildUI(); }) { text = text };
            b.style.minHeight               = 30;
            b.style.minWidth                = 100;
            b.style.fontSize                = 11;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.color = active ? Color.white : new StyleColor(T.TextSecondary).value;
            b.style.backgroundColor = new StyleColor(active
                ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.85f)
                : new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.85f));
            T.Radius(b, T.ButtonRadius);
            T.Border(b, 0, Color.clear);
            b.style.marginRight = 5;
            return b;
        }

        private static VisualElement PageHeader(string title, string backText, Action backAction)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 4;
            row.pickingMode = PickingMode.Ignore;

            var t = T.Title(title);
            t.style.flexGrow = 1;
            row.Add(t);

            var back = new Button(backAction) { text = backText };
            back.style.minHeight               = 28;
            back.style.minWidth                = 90;
            back.style.fontSize                = 10;
            back.style.unityFontStyleAndWeight = FontStyle.Bold;
            back.style.color                   = Color.white;
            back.style.backgroundColor         = new StyleColor(new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.90f));
            T.Radius(back, T.ButtonRadius);
            T.Border(back, 0, Color.clear);
            row.Add(back);

            return row;
        }

        private static Label FormLabel(string text)
        {
            var l = new Label(text);
            l.style.color    = new StyleColor(T.TextSecondary);
            l.style.fontSize = 11;
            l.style.minHeight = 18;
            l.style.marginBottom = 3;
            return l;
        }

        private static void StyleField(TextInputBaseField<string> f)
        {
            f.style.minHeight         = 30;
            f.style.marginBottom      = 4;
            f.style.backgroundColor   = new StyleColor(T.BgCard);
            f.style.color             = new StyleColor(T.TextPrimary);
            f.style.fontSize          = 13;
            T.Radius(f, 5f);
            T.Border(f, 1, T.BorderDim);
        }

        private static void StyleField(IntegerField f)
        {
            f.style.minHeight       = 30;
            f.style.marginBottom    = 4;
            f.style.backgroundColor = new StyleColor(T.BgCard);
            f.style.color           = new StyleColor(T.TextPrimary);
            f.style.fontSize        = 13;
            T.Radius(f, 5f);
            T.Border(f, 1, T.BorderDim);
        }

        private static VisualElement BuildIntSlider(int min, int max, int value, Action<int> onChange)
        {
            var s = new SliderInt(min, max) { value = value, showInputField = true };
            s.style.marginBottom = 4;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }

        private static VisualElement BuildFloatSlider(float min, float max, float value, Action<float> onChange)
        {
            var s = new Slider(min, max) { value = value, showInputField = true };
            s.style.marginBottom = 4;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }

        private static string SanitizeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "MyWorld";
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var sb      = new System.Text.StringBuilder();
            foreach (char c in raw)
                if (!invalid.Contains(c)) sb.Append(c);
            return sb.Length == 0 ? "MyWorld" : sb.ToString();
        }

        private static PanelSettings CreateDefaultPanelSettings()
        {
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            return ps;
        }
    }
}
