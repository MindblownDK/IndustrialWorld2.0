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
using VoxelEngine.UI;
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

        private enum Page  { Pause, Settings, Multiplayer, Teams, Admin }
        private enum STab  { Display, Camera, Interface, Audio, Saving, Keybinds }
        private Page _page = Page.Pause;
        private Page _lastBuiltPage = (Page)(-1);
        private STab _tab  = STab.Camera;
        private float _savedScrollY = 0f;
        private bool _hasSavedScroll = false;
        private bool _frozeTime = false;          // time only freezes offline
        private string _mpAddress = "localhost";  // last join address typed

        // ── Unity Lifecycle ────────────────────────────────────────
        private void Awake()
        {
            _doc = GetComponent<UIDocument>();
            _doc.sortingOrder = 1000;
            if (_doc.panelSettings == null)
                _doc.panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
            _root = _doc.rootVisualElement;
            _root.style.flexGrow = 1;
            VoxelEngine.FX.UiAudio.Attach(_root);   // click/hover audio (idempotent)
            HideUI();
        }

        private void Update()
        {
            if (VoxelEngine.UI.UIState.PauseConsumedThisFrame) return;
            if (!GameSettings.WasPressed(InputAction.Pause)) return;

            var hammerWheel = VoxelEngine.Building.Tiered.HammerBuildWheel.Instance;
            if (hammerWheel != null && (hammerWheel.IsOpen || hammerWheel.ActiveFamily.HasValue))
            {
                hammerWheel.ExitBuildMode();
                VoxelEngine.UI.UIState.PauseConsumedFrame = Time.frameCount;
                return;
            }

            if (_open) { Close(); return; }
            if (VoxelEngine.UI.UIState.IsBlocking) return;
            Open();
        }

        // ── Open / Close ───────────────────────────────────────────
        private void Open()
        {
            _open = true;
            VoxelEngine.UI.UIState.PushBlock();
            // Freezing time is a single-player luxury: in a session the world
            // keeps running on the server, so the menu must not stall the tick.
            _frozeTime = VoxelEngine.Networking.NetworkSession.Mode
                         == VoxelEngine.Networking.SessionMode.Offline;
            _savedTS   = Time.timeScale;
            _savedLock = Cursor.lockState;
            _savedVis  = Cursor.visible;
            if (_frozeTime)
            {
                VoxelEngine.UI.UIState.PushHardPause();   // pause menu = the ONLY time-freezing UI
                Time.timeScale = 0f;
            }
            Cursor.lockState    = CursorLockMode.None;
            Cursor.visible      = true;
            _page = Page.Pause;
            _tab  = STab.Camera;
            BuildUI();
        }

        private void Close()
        {
            _open = false;
            if (_frozeTime) { VoxelEngine.UI.UIState.PopHardPause(); Time.timeScale = _savedTS; _frozeTime = false; }
            VoxelEngine.UI.UIState.PopBlock();
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
            // Rebuilding the same page must not replay the LCD boot.
            bool samePage = _lastBuiltPage == _page;
            _lastBuiltPage = _page;
            if (samePage) LcdHudTheme.BootsMuted = true;
            try
            {
                BuildUIBody();
            }
            finally
            {
                LcdHudTheme.BootsMuted = false;
            }
        }

        private void BuildUIBody()
        {
            // Preserve scroll for settings tab
            if (_root != null)
            {
                var existingScroll = _root.Q<ScrollView>();
                if (existingScroll != null)
                {
                    _savedScrollY = existingScroll.scrollOffset.y;
                    _hasSavedScroll = true;
                }
            }

            _root.Clear();
            // Frosted dark backdrop.
            _root.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.62f));
            _root.style.alignItems      = Align.Center;
            _root.style.justifyContent  = Justify.Center;
            _root.pickingMode           = PickingMode.Position;

            if      (_page == Page.Pause)       BuildPause();
            else if (_page == Page.Multiplayer) BuildMultiplayer();
            else if (_page == Page.Teams)       BuildTeams();
            else if (_page == Page.Admin)       BuildAdmin();
            else                                BuildSettings();
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
            panel.Add(PrimaryBtn("◉   MULTIPLAYER", () => { _page = Page.Multiplayer; BuildUI(); }, T.BgSlot));
            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("TEAMS",            () => { _page = Page.Teams; BuildUI(); },       T.BgSlot, VoxelEngine.UI.LucideIcons.Flag));
            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("⬅   SAVE & QUIT", QuitToMenu,                           T.AccentRed));
        }

        // ── Multiplayer Page ───────────────────────────────────────
        private void BuildMultiplayer()
        {
            var panel = MakePanel(420, 0);
            _root.Add(panel);

            var hdr = new VisualElement();
            hdr.style.flexDirection = FlexDirection.Row;
            hdr.style.alignItems    = Align.Center;
            hdr.style.marginBottom  = 6;
            var title = T.Title("MULTIPLAYER");
            title.style.flexGrow = 1;
            hdr.Add(title);
            var backBtn = PrimaryBtn("← BACK", () => { _page = Page.Pause; BuildUI(); }, T.BgSlot);
            backBtn.style.minWidth  = 90;
            backBtn.style.minHeight = 30;
            backBtn.style.fontSize  = 11;
            hdr.Add(backBtn);
            panel.Add(hdr);
            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(8));

            var bootstrap = VoxelEngine.Networking.NetworkBootstrap.Instance;
            if (bootstrap == null)
            {
                var missing = T.Muted(
                    "This scene has no network bootstrap.\n" +
                    "Run Setup Step 105 (Tools → Voxel Engine → Voxel Engine Setup)\n" +
                    "in the game scene, then save it.");
                missing.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(missing);
                return;
            }

            bool online = bootstrap.IsOnline;
            var status = T.StatLabel(bootstrap.StatusLine, online ? T.AccentGreen : T.TextSecondary);
            panel.Add(status);
            panel.Add(T.Spacer(10));

            bool mismatchShown = bootstrap.WorldMismatch;
            if (mismatchShown)
            {
                var warn = T.StatLabel(
                    "WORLD MISMATCH - terrain and buildings will not line up.\n"
                    + bootstrap.HostWorldLine + "\n"
                    + "Create or load a world with that seed to truly share ground.",
                    T.AccentRed);
                warn.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(warn);
                panel.Add(T.Spacer(10));
            }

            // Renames apply live: locally at once, to everyone via the server.
            panel.Add(T.Muted("YOUR NAME"));
            var nameField = MpField(VoxelEngine.Networking.PlayerIdentity.LocalName);
            nameField.isDelayed = true;   // commit on Enter/blur, not every keystroke
            nameField.RegisterValueChangedCallback(evt =>
                VoxelEngine.Networking.PlayerIdentity.LocalName = evt.newValue);
            panel.Add(nameField);
            panel.Add(T.Spacer(10));

            // Skin tone (14.15.0): applies live - the avatar's pose mirror picks
            // the change up within a tick, locally saved like the name.
            panel.Add(T.Muted("SKIN"));
            var skinRow = new VisualElement();
            skinRow.style.flexDirection = FlexDirection.Row;
            skinRow.style.marginTop = 2;
            int currentTone = VoxelEngine.Networking.PlayerIdentity.LocalSkinTone;
            for (int i = 0; i < VoxelEngine.Networking.CrusaderModel.SkinToneCount; i++)
            {
                int tone = i;
                var swatch = new Button(() =>
                {
                    VoxelEngine.Networking.PlayerIdentity.LocalSkinTone = tone;
                    BuildUI();
                }) { text = "" };
                swatch.style.width = 36;
                swatch.style.height = 26;
                swatch.style.marginRight = 6;
                swatch.style.backgroundColor =
                    new StyleColor(VoxelEngine.Networking.CrusaderModel.SkinToneColor(tone));
                bool selected = tone == currentTone;
                var borderColor = selected ? Color.white : new Color(0f, 0f, 0f, 0.55f);
                float borderWidth = selected ? 2f : 1f;
                swatch.style.borderTopColor = borderColor;
                swatch.style.borderBottomColor = borderColor;
                swatch.style.borderLeftColor = borderColor;
                swatch.style.borderRightColor = borderColor;
                swatch.style.borderTopWidth = borderWidth;
                swatch.style.borderBottomWidth = borderWidth;
                swatch.style.borderLeftWidth = borderWidth;
                swatch.style.borderRightWidth = borderWidth;
                T.Radius(swatch, 5);
                skinRow.Add(swatch);
            }
            panel.Add(skinRow);
            panel.Add(T.Spacer(14));

            Label playersLabel = null;
            if (!online)
            {
                panel.Add(PrimaryBtn("◈   HOST THIS WORLD", () =>
                {
                    ReleaseFreezeForSession();
                    bootstrap.StartHost();
                    BuildUI();
                }, T.AccentGreen));
                panel.Add(T.Spacer(14));

                panel.Add(T.Muted("HOST ADDRESS"));
                var addrField = MpField(_mpAddress);
                addrField.RegisterValueChangedCallback(evt => _mpAddress = evt.newValue);
                panel.Add(addrField);
                panel.Add(T.Spacer(6));

                // 14.47.0 - passworded servers. Blank is correct for open
                // ones; a wrong password comes back as a named refusal.
                panel.Add(T.Muted("SERVER PASSWORD (IF ANY)"));
                var pwField = MpField(VoxelEngine.Networking.NetworkBootstrap.JoinPassword);
                pwField.isPasswordField = true;
                pwField.RegisterValueChangedCallback(evt =>
                    VoxelEngine.Networking.NetworkBootstrap.JoinPassword = evt.newValue);
                panel.Add(pwField);
                panel.Add(T.Spacer(6));

                panel.Add(PrimaryBtn("→   JOIN GAME", () =>
                {
                    ReleaseFreezeForSession();
                    bootstrap.StartClient(_mpAddress);
                    BuildUI();
                }, T.AccentCyan));
            }
            else
            {
                panel.Add(T.Muted("PLAYERS"));
                playersLabel = T.Body(PlayerListText());
                playersLabel.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(playersLabel);
                panel.Add(T.Spacer(14));

                // 14.47.0 - the server's door, for those allowed to hold the
                // keys. Always visible while online: the rank-less see the
                // claim box, so a fresh owner can find the way in.
                panel.Add(PrimaryBtn("◆   SERVER ADMINISTRATION", () =>
                {
                    _page = Page.Admin; BuildUI();
                }, T.BgSlot));
                panel.Add(T.Spacer(6));
                panel.Add(PrimaryBtn("✕   DISCONNECT", () =>
                {
                    bootstrap.StopSession();
                    BuildUI();
                }, T.AccentRed));
            }

            // Live refresh: connecting is asynchronous, players come and go.
            panel.schedule.Execute(() =>
            {
                if (!_open || _page != Page.Multiplayer) return;
                if (bootstrap.IsOnline != online) { BuildUI(); return; }
                if (bootstrap.WorldMismatch != mismatchShown) { BuildUI(); return; }
                status.text = bootstrap.StatusLine;
                if (playersLabel != null) playersLabel.text = PlayerListText();
            }).Every(400);
        }

        /// <summary>Going online from a frozen pause menu: the world must run
        /// again the moment a session starts, menu still open or not.</summary>
        private void ReleaseFreezeForSession()
        {
            if (!_frozeTime) return;
            VoxelEngine.UI.UIState.PopHardPause();
            Time.timeScale = _savedTS;
            _frozeTime = false;
        }

        private TextField MpField(string value)
        {
            var f = new TextField { value = value };
            f.style.minHeight = 30;
            f.style.fontSize  = 13;
            f.style.marginTop = 4;
            var input = f.Q("unity-text-input");
            if (input != null)
            {
                input.style.backgroundColor = new StyleColor(new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.95f));
                input.style.color = new StyleColor(T.TextPrimary);
                T.Radius(input, T.ButtonRadius);
                T.Border(input, 1, T.BorderDim);
                input.style.paddingLeft = 8;
                input.style.paddingRight = 8;
            }
            return f;
        }

        private static string PlayerListText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in VoxelEngine.Networking.NetworkSession.Players)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("•  ").Append(string.IsNullOrEmpty(p.displayName) ? p.playerId : p.displayName);
                if (p.playerId == VoxelEngine.Networking.NetworkSession.LocalPlayerId) sb.Append("   (you)");
            }
            return sb.Length > 0 ? sb.ToString() : "—";
        }

        // ── Teams Page (14.33.0, milestone 11; 14.34.0 widened so long
        // team names never push the answer buttons off the card) ───────
        private void BuildTeams()
        {
            var panel = MakePanel(580, 0);
            _root.Add(panel);

            var hdr = new VisualElement();
            hdr.style.flexDirection = FlexDirection.Row;
            hdr.style.alignItems    = Align.Center;
            hdr.style.marginBottom  = 6;
            var title = T.Title("TEAMS");
            title.style.flexGrow = 1;
            hdr.Add(title);
            var backBtn = PrimaryBtn("← BACK", () => { _page = Page.Pause; BuildUI(); }, T.BgSlot);
            backBtn.style.minWidth  = 90;
            backBtn.style.minHeight = 30;
            backBtn.style.fontSize  = 11;
            hdr.Add(backBtn);
            panel.Add(hdr);
            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(8));

            // Content (invitations, my team / founding, overview, host limits)
            // comes from TeamsPanel; this menu keeps only the chrome and the
            // live refresh, same division of labour as the settings tabs.
            panel.Add(VoxelEngine.UI.TeamsPanel.Build(BuildUI));

            // Live refresh: the roster moves when ANY player touches it -
            // host-applied intents locally, roster broadcasts on guests. The
            // version number is the cheap tell; the draft team name survives
            // the rebuild because TeamsPanel keeps it.
            int builtVersion = VoxelEngine.Networking.TeamRegistry.Version;
            panel.schedule.Execute(() =>
            {
                if (!_open || _page != Page.Teams) return;
                if (VoxelEngine.Networking.TeamRegistry.Version != builtVersion) BuildUI();
            }).Every(500);
        }

        // ── Server Administration Page (14.47.0) ───────────────────
        private void BuildAdmin()
        {
            var panel = MakePanel(560, 0);
            _root.Add(panel);

            var hdr = new VisualElement();
            hdr.style.flexDirection = FlexDirection.Row;
            hdr.style.alignItems    = Align.Center;
            hdr.style.marginBottom  = 6;
            var title = T.Title("SERVER ADMINISTRATION");
            title.style.flexGrow = 1;
            hdr.Add(title);
            var backBtn = PrimaryBtn("← BACK", () => { _page = Page.Multiplayer; BuildUI(); }, T.BgSlot);
            backBtn.style.minWidth  = 90;
            backBtn.style.minHeight = 30;
            backBtn.style.fontSize  = 11;
            hdr.Add(backBtn);
            panel.Add(hdr);
            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(8));

            // Content comes from ServerAdminPanel; this menu keeps only the
            // chrome and the live refresh - the TeamsPanel division of labour.
            panel.Add(VoxelEngine.UI.ServerAdminPanel.Build(BuildUI));

            // Live refresh: the state moves when ANY admin touches it, and a
            // session that drops must not leave a dead admin page open.
            int builtVersion = VoxelEngine.Networking.ServerAdminRegistry.Version;
            bool wasOnline = VoxelEngine.Networking.NetworkBootstrap.Instance != null
                && VoxelEngine.Networking.NetworkBootstrap.Instance.IsOnline;
            panel.schedule.Execute(() =>
            {
                if (!_open || _page != Page.Admin) return;
                var bootstrap = VoxelEngine.Networking.NetworkBootstrap.Instance;
                bool online = bootstrap != null && bootstrap.IsOnline;
                if (online != wasOnline) { _page = Page.Multiplayer; BuildUI(); return; }
                if (VoxelEngine.Networking.ServerAdminRegistry.Version != builtVersion) BuildUI();
            }).Every(500);
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
            tabs.Add(TabBtn("Interface", STab.Interface));
            tabs.Add(TabBtn("Audio",    STab.Audio));
            tabs.Add(TabBtn("Saving",   STab.Saving));
            tabs.Add(TabBtn("Keybinds", STab.Keybinds));
            panel.Add(tabs);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(scroll);
            scroll.style.flexGrow = 1;
            SettingsUI.ApplyLcdScreen(scroll);
            panel.Add(scroll);

            switch (_tab)
            {
                case STab.Display:  DisplayTab(scroll);  break;
                case STab.Camera:   CameraTab(scroll);   break;
                case STab.Interface: SettingsUI.InterfaceTab(scroll, BuildUI); break;
                case STab.Audio:    AudioTab(scroll);     break;
                case STab.Saving:   SavingTab(scroll);    break;
                case STab.Keybinds: KeybindTab(scroll);   break;
            }

            if (_hasSavedScroll && _tab == STab.Interface)
            {
                float y = _savedScrollY;
                scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0, y)).ExecuteLater(20);
            }
            _hasSavedScroll = false;

            panel.Add(T.Spacer(8));
            var resetBtn = PrimaryBtn("RESET DEFAULTS", () => { GameSettings.ResetToDefaults(); BuildUI(); }, T.AccentRed);
            resetBtn.style.alignSelf = Align.FlexEnd;
            resetBtn.style.minWidth  = 150;
            resetBtn.style.minHeight = 28;
            resetBtn.style.fontSize  = 10;
            panel.Add(resetBtn);
        }

        // ── Settings Tab Content ───────────────────────────────────
        // Delegates to the shared SettingsUI builder (same surface as the main
        // menu) so polish & new options stay in lock-step across both menus.
        private void DisplayTab(VisualElement p)  => SettingsUI.DisplayTab(p, BuildUI);
        private void CameraTab(VisualElement p)   => SettingsUI.CameraTab(p, BuildUI);
        private void AudioTab(VisualElement p)    => SettingsUI.AudioTab(p, BuildUI, this);
        private void SavingTab(VisualElement p)   => SettingsUI.SavingTab(p, BuildUI);
        private void KeybindTab(VisualElement p)  => SettingsUI.KeybindTab(p, this, BuildUI);

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
            // LCD chassis treatment matching the main menu: bezel, corner brackets,
            // animated scanlines, phosphor boot + wipe.
            LcdHudTheme.UpgradePanel(v);
            return v;
        }

        private Button PrimaryBtn(string text, Action onClick, Color bg, string lucideIcon = null)
        {
            var b = new Button(onClick) { text = lucideIcon == null ? text : string.Empty };
            b.style.minHeight                 = 42;
            b.style.fontSize                  = 13;
            b.style.unityFontStyleAndWeight   = FontStyle.Bold;
            b.style.letterSpacing             = 0.8f;
            b.style.color                     = Color.white;
            b.style.backgroundColor           = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.85f));
            T.Radius(b, T.ButtonRadius);
            T.Border(b, 0, Color.clear);

            // Icon buttons compose their own content row: a Lucide glyph can
            // never render as a missing-glyph box the way a raw unicode flag
            // did in the menu font (14.34.0).
            if (lucideIcon != null)
            {
                b.style.flexDirection  = FlexDirection.Row;
                b.style.alignItems     = Align.Center;
                b.style.justifyContent = Justify.Center;

                var ic = VoxelEngine.UI.LucideIcons.Make(lucideIcon, 15, Color.white);
                ic.style.marginRight = 10;
                b.Add(ic);

                var lbl = new Label(text) { pickingMode = PickingMode.Ignore };
                lbl.style.color                   = Color.white;
                lbl.style.fontSize                = 13;
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                lbl.style.letterSpacing           = 0.8f;
                lbl.style.unityTextAlign          = TextAnchor.MiddleCenter;
                b.Add(lbl);
            }

            LcdHudTheme.AddMenuInteractions(b, bg, new Color(bg.r, bg.g, bg.b, 0.85f));
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
            LcdHudTheme.AddMenuInteractions(b, T.AccentCyan,
                active ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.85f)
                       : new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.85f));
            return b;
        }

        private void QuitToMenu()
        {
            // Leave the session cleanly before tearing the world down.
            VoxelEngine.Networking.NetworkBootstrap.Instance?.StopSession();
            Time.timeScale = 1f;
            VoxelEngine.UI.UIState.ClearSceneBlocks();
            VoxelEngine.Persistence.WorldStatePersistence.Instance?.SaveAll();
            VoxelEngine.Research.ResearchManager.Instance?.SaveToDisk();
            try { SceneManager.LoadScene(mainMenuScene); }
            catch (Exception ex) { Debug.LogError("[PauseMenu] Scene load failed: " + ex.Message); }
        }
    }
}
