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
using VoxelEngine.UI;
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

        // ── Cached fonts (loaded once per scene-load) ──────────────
        private static Font _cachedTextFont;
        private static Font _cachedIconFont;

        // ── Unity Lifecycle ────────────────────────────────────────
        private void Awake()
        {
            _doc = GetComponent<UIDocument>();

            // 1) Prefer a project-authored PanelSettings (drag-assigned in inspector
            //    OR placed under any Resources/ folder as "MenuPanelSettings").
            // 2) Otherwise synthesise a complete one at runtime — theme + font included.
            if (_doc.panelSettings == null)
            {
                var preset = Resources.Load<PanelSettings>("MenuPanelSettings");
                _doc.panelSettings = preset != null ? preset : CreateDefaultPanelSettings();
            }
            else if (_doc.panelSettings.themeStyleSheet == null)
            {
                // Existing PanelSettings but missing theme → patch it so the
                // "No Theme Style Sheet set" warning disappears.
                _doc.panelSettings.themeStyleSheet = LoadOrCreateDefaultTheme();
            }

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

            // GUARANTEED FONT — without this, a missing TSS theme means every
            // Label/Button renders only its background colour (no glyphs).
            // We force-cascade a font from the root so all children inherit it.
            var fallbackFont = LoadFallbackFont();
            if (fallbackFont != null)
                _root.style.unityFontDefinition = new StyleFontDefinition(fallbackFont);

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

            var logoIco = MakeIcon(LucideIcons.Factory, 40, T.AccentCyan);
            logoIco.style.marginBottom    = 6;
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
            panel.Add(PrimaryBtn("PLAY",      () => { _page = Page.Saves;    BuildUI(); }, T.AccentCyan, LucideIcons.Play));
            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("NEW WORLD", () => { _page = Page.NewWorld; BuildUI(); }, T.AccentTeal, LucideIcons.Plus));
            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("SETTINGS",  () => { _page = Page.Settings; BuildUI(); }, T.BgSlot,    LucideIcons.Settings));
            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("QUIT",      QuitGame,                                    T.AccentRed, LucideIcons.X));

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

            panel.Add(PageHeader("SAVES", "BACK", () => { _page = Page.Main; BuildUI(); }));
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

                empty.Add(MakeIcon(LucideIcons.Globe, 36, T.TextSecondary));

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

            panel.Add(PrimaryBtn("NEW WORLD", () => { _page = Page.NewWorld; BuildUI(); }, T.AccentTeal, LucideIcons.Plus));
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

            // Action buttons — built locally so we can mix the icon font in.
            var playBtn = BuildIconSmallButton(LucideIcons.Play, "PLAY",
                () => LoadWorld(w.name), T.AccentCyan);
            playBtn.style.marginRight = 6;
            row.Add(playBtn);

            var delBtn = BuildIconSmallButton(LucideIcons.Trash, "DELETE",
                () => { _session.DeleteWorld(w.name); BuildUI(); }, T.AccentRed);
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

            panel.Add(PageHeader("NEW WORLD", "BACK", () => { _page = Page.Main; BuildUI(); }));
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
            var rndBtn = BuildIconSmallButton(LucideIcons.Dice5, "RANDOM", () =>
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
            panel.Add(PrimaryBtn("CREATE & PLAY", CreateAndLoadWorld, T.AccentCyan, LucideIcons.Play));
        }

        // ════════════════════════════════════════════════════════════
        //                     SETTINGS PAGE
        // ════════════════════════════════════════════════════════════
        private void BuildSettingsPage()
        {
            var panel = MakePanel(720, 0);
            _root.Add(panel);

            panel.Add(PageHeader("SETTINGS", "BACK", () => { _page = Page.Main; BuildUI(); }));
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
            StyleInnerInput(s);
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

        private Button PrimaryBtn(string text, Action onClick, Color bg, string icon = null)
        {
            var b = new Button(onClick);
            b.text = string.Empty; // we build label content ourselves
            b.style.minHeight               = 44;
            b.style.fontSize                = 13;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.letterSpacing           = 0.8f;
            b.style.color                   = Color.white;
            b.style.backgroundColor         = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.85f));
            b.style.flexDirection           = FlexDirection.Row;
            b.style.alignItems              = Align.Center;
            b.style.justifyContent          = Justify.Center;
            b.style.paddingLeft             = 16;
            b.style.paddingRight            = 16;
            T.Radius(b, T.ButtonRadius);
            T.Border(b, 0, Color.clear);

            if (!string.IsNullOrEmpty(icon))
            {
                var ic = MakeIcon(icon, 16, Color.white);
                ic.style.marginRight = 10;
                b.Add(ic);
            }

            var lbl = new Label(text) { pickingMode = PickingMode.Ignore };
            lbl.style.color                   = Color.white;
            lbl.style.fontSize                = 13;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.letterSpacing           = 0.8f;
            lbl.style.unityTextAlign          = TextAnchor.MiddleCenter;
            b.Add(lbl);

            return b;
        }

        /// <summary>
        /// Compact icon+label button, sized like UITheme.SmallButton but composed
        /// from two child labels so the Lucide font can be used for the glyph
        /// while keeping the regular text font for the label.
        /// </summary>
        private static Button BuildIconSmallButton(string iconGlyph, string text, Action onClick, Color bg)
        {
            var b = new Button(onClick);
            b.text = string.Empty;
            b.style.minHeight               = 30;
            b.style.color                   = Color.white;
            b.style.backgroundColor         = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.85f));
            b.style.flexDirection           = FlexDirection.Row;
            b.style.alignItems              = Align.Center;
            b.style.justifyContent          = Justify.Center;
            b.style.paddingLeft             = 10;
            b.style.paddingRight            = 12;
            T.Radius(b, T.ButtonRadius);
            T.Border(b, 0, Color.clear);

            var ic = MakeIcon(iconGlyph, 12, Color.white);
            ic.style.marginRight = 6;
            b.Add(ic);

            var lbl = new Label(text) { pickingMode = PickingMode.Ignore };
            lbl.style.color                   = Color.white;
            lbl.style.fontSize                = 11;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.unityTextAlign          = TextAnchor.MiddleCenter;
            b.Add(lbl);

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

            var back = new Button(backAction);
            back.text = string.Empty;
            back.style.minHeight        = 28;
            back.style.minWidth         = 90;
            back.style.color            = Color.white;
            back.style.backgroundColor  = new StyleColor(new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.90f));
            back.style.flexDirection    = FlexDirection.Row;
            back.style.alignItems       = Align.Center;
            back.style.justifyContent   = Justify.Center;
            back.style.paddingLeft      = 10;
            back.style.paddingRight     = 12;
            T.Radius(back, T.ButtonRadius);
            T.Border(back, 0, Color.clear);

            var ic = MakeIcon(LucideIcons.ArrowLeft, 13, Color.white);
            ic.style.marginRight = 6;
            back.Add(ic);

            var lbl = new Label(backText) { pickingMode = PickingMode.Ignore };
            lbl.style.color                   = Color.white;
            lbl.style.fontSize                = 10;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.unityTextAlign          = TextAnchor.MiddleCenter;
            back.Add(lbl);

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
            StyleInnerInput(f);
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
            StyleInnerInput(f);
        }

        /// <summary>
        /// Forces every text-rendering descendant of a TextField / IntegerField / Slider
        /// input to use our theme colour. Unity Toolkit's input control is a deep tree
        /// (Field → TextInputBase → TextElement) and `color` does NOT cascade reliably
        /// onto the inner TextElement that actually draws typed glyphs. Without this
        /// fix, the caret + characters render in the default white, which is invisible
        /// against our dark BgCard backgrounds.
        /// </summary>
        private static void StyleInnerInput(VisualElement field)
        {
            if (field == null) return;

            void Apply(VisualElement root)
            {
                // 1) Style the input box wrapper (the visible "well" inside the field).
                var input = root.Q(className: "unity-base-text-field__input")
                            ?? root.Q("unity-text-input");
                if (input != null)
                {
                    input.style.color           = new StyleColor(T.TextPrimary);
                    input.style.backgroundColor = new StyleColor(T.BgCard);
                    input.style.unityTextAlign  = TextAnchor.MiddleLeft;
                    input.style.paddingLeft     = 6;
                    input.style.paddingRight    = 6;
                }

                // 2) Walk every descendant and force colour on real text renderers.
                //    This catches the inner TextElement that draws the actual glyphs,
                //    plus any Label that Unity adds for the field's display value.
                root.Query<TextElement>().ForEach(te =>
                {
                    te.style.color = new StyleColor(T.TextPrimary);
                });
            }

            Apply(field);
            // Re-apply once the panel has had a chance to materialise lazy children.
            field.RegisterCallback<AttachToPanelEvent>(_ => Apply(field));
            field.RegisterCallback<GeometryChangedEvent>(_ => Apply(field));
            // And again whenever the value changes — covers the SliderInt input field
            // which Unity sometimes rebuilds when its value crosses an integer step.
            field.RegisterCallback<ChangeEvent<string>>(_ => Apply(field));
            field.RegisterCallback<ChangeEvent<int>>(_ => Apply(field));
            field.RegisterCallback<ChangeEvent<float>>(_ => Apply(field));
        }

        private static VisualElement BuildIntSlider(int min, int max, int value, Action<int> onChange)
        {
            var s = new SliderInt(min, max) { value = value, showInputField = true };
            s.style.marginBottom = 4;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            StyleInnerInput(s);
            return s;
        }

        private static VisualElement BuildFloatSlider(float min, float max, float value, Action<float> onChange)
        {
            var s = new Slider(min, max) { value = value, showInputField = true };
            s.style.marginBottom = 4;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            StyleInnerInput(s);
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
            ps.name                = "MainMenu_RuntimePanelSettings";
            ps.scaleMode           = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.match               = 0.5f;

            // CRITICAL — without a ThemeStyleSheet, UI Toolkit logs the warning
            // "No Theme Style Sheet set to PanelSettings, UI will not render properly"
            // and falls back to *no* styling (no fonts, no default rules).
            ps.themeStyleSheet = LoadOrCreateDefaultTheme();
            return ps;
        }

        /// <summary>
        /// Loads the default Unity runtime theme. Tries (in order):
        /// 1) A user-authored theme placed at  Resources/MenuTheme.tss
        /// 2) Resources.Load on the default theme name
        /// 3) A freshly-instantiated empty ThemeStyleSheet (last-resort, suppresses
        ///    the warning but provides no styling).
        /// Never returns null.
        /// </summary>
        private static ThemeStyleSheet LoadOrCreateDefaultTheme()
        {
            var theme = Resources.Load<ThemeStyleSheet>("MenuTheme");
            if (theme != null) return theme;

            theme = Resources.Load<ThemeStyleSheet>("UnityDefaultRuntimeTheme");
            if (theme != null) return theme;

            // Final safety net — empty sheet still satisfies the validator.
            var empty = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            empty.name = "MainMenu_RuntimeEmptyTheme";
            return empty;
        }

        /// <summary>
        /// Returns a usable Font for UI Toolkit. Order:
        /// 1) Resources/Fonts/MenuFont (project-shipped)
        /// 2) Built-in "LegacyRuntime" (Unity 6 default UI font, always present)
        /// 3) Built-in "Arial" (older fallback)
        /// Never throws; may return null only if no fonts exist on the platform.
        /// </summary>
        private static Font LoadFallbackFont()
        {
            if (_cachedTextFont != null) return _cachedTextFont;

            _cachedTextFont = Resources.Load<Font>("Fonts/MenuFont");
            if (_cachedTextFont != null) return _cachedTextFont;

            // Unity 6 ships LegacyRuntime.ttf as the universal built-in UI font.
            _cachedTextFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_cachedTextFont != null) return _cachedTextFont;

            _cachedTextFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _cachedTextFont;
        }

        /// <summary>
        /// Loads the Lucide icon font (Resources/Fonts/Lucide.ttf).
        /// Returns null gracefully if the font is missing — callers must handle.
        /// </summary>
        private static Font LoadIconFont()
        {
            if (_cachedIconFont != null) return _cachedIconFont;
            _cachedIconFont = Resources.Load<Font>(LucideIcons.ResourcePath);
            return _cachedIconFont;
        }

        /// <summary>
        /// Builds a single-glyph Lucide-font Label sized to fit a button row.
        /// Falls back to an empty (zero-width) element if the icon font isn't loaded,
        /// so layout never collapses.
        /// </summary>
        private static Label MakeIcon(string glyph, int sizePx, Color color)
        {
            var icon = new Label(glyph)
            {
                pickingMode = PickingMode.Ignore
            };
            icon.style.fontSize       = sizePx;
            icon.style.color          = new StyleColor(color);
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            icon.style.marginRight    = 0;

            var font = LoadIconFont();
            if (font != null)
                icon.style.unityFontDefinition = new StyleFontDefinition(font);

            return icon;
        }
    }
}
