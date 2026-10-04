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
using VoxelEngine.Cosmos;
using VoxelEngine.Items;
using VoxelEngine.Networking;
using VoxelEngine.Settings;
using VoxelEngine.UI;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Menu
{
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        [Tooltip("Scene name (without .unity) containing the gameplay scene. " +
                 "Ensure it is added to File > Build Profiles > Scene List.")]
        public string gameSceneName = "Game";

        // ── State ──────────────────────────────────────────────────
        private UIDocument    _doc;
        private VisualElement _root;
        private WorldSession  _session;

        private enum Page { Main, Saves, NewWorld, EditWorld, Multiplayer, EditPlayer, Settings }
        private Page _page = Page.Main;
        private Page _lastBuiltPage = (Page)(-1);

        // New-world form values.
        private string _newName            = "MyWorld";
        private int    _newSeed            = 0;
        private int    _newMaxDroppedItems = WorldSession.DefaultMaxDroppedItems;
        private int    _newInventoryWeightPercent = WorldSession.DefaultInventoryWeightPercent;
        private int    _newContainerWeightPercent = WorldSession.DefaultContainerWeightPercent;
        private bool   _newShowDropVoidWarning = true;
        private bool   _newAllowRuinLootRespawn = WorldSession.DefaultAllowRuinLootRespawn;
        private bool   _newFriendlyFire = WorldSession.DefaultFriendlyFire;
        private bool   _newAllowBannerPainting = true;
        private int    _newOrbitPace = WorldSession.OrbitPaceRealistic;

        // Edit-world form values. Only non-generation settings are editable here.
        private string _editOriginalName = string.Empty;
        private string _editName = string.Empty;
        private int    _editMaxDroppedItems = WorldSession.DefaultMaxDroppedItems;
        private int    _editInventoryWeightPercent = WorldSession.DefaultInventoryWeightPercent;
        private int    _editContainerWeightPercent = WorldSession.DefaultContainerWeightPercent;
        private bool   _editShowDropVoidWarning = true;
        private bool   _editAllowRuinLootRespawn = WorldSession.DefaultAllowRuinLootRespawn;
        private bool   _editFriendlyFire = WorldSession.DefaultFriendlyFire;
        private bool   _editAllowBannerPainting = true;
        private string _menuStatus = string.Empty;

        // Multiplayer page: remembered between sessions so a friend's address
        // is typed once, not every evening. Read in Awake, NEVER here - a field
        // initializer runs inside the MonoBehaviour constructor, where Unity
        // forbids PlayerPrefs, and the throw there kills every initializer
        // BELOW it (which is how this one nulled the planet-seed lists).
        private string _joinAddress = "";

        // ── 14.48.0 server browser state ──────────────────────────────
        // The tab survives rebuilds (star/remove clicks rebuild the page);
        // the LAN results live here so a tab switch does not forget a scan.
        private enum MpTab { Servers, Favorites, Recent, Lan }
        private MpTab _mpTab = MpTab.Servers;
        private string _addServerName = "";
        private string _addServerAddress = "";
        private string _addServerPassword = "";
        private readonly List<VoxelEngine.Networking.LanDiscovery.Found> _lanFound = new();
        private bool _lanScanShown;

        // ── 14.49.0 edit-player drafts ────────────────────────────────
        // Seeded once from the local store, kept across rebuilds, written
        // back only by SAVE - the same draft discipline as the banner editor.
        private bool _editPlayerSeeded;
        private string _chestTextDraft = "";
        private Texture2D _iconDraft;
        private Color32[] _iconDraftPixels;
        private bool _iconDraftHasImage;
        private bool _iconPainting;
        private Color32 _iconBrush = new Color32(168, 24, 28, 255);
        private bool _iconErasing;   // 14.52.0 - eraser paints the blank canvas color
        private int _iconBrushRadius = 6;
        private readonly Dictionary<string, Texture2D> _iconGalleryCache = new();
        private string _expandedAutosaveWorld = string.Empty;

        // ── Cosmos: solar-system picker + per-planet editable seeds ──
        private List<SolarSystemTemplate> _systemChoices;
        private int   _selectedSystemIndex = 0;
        // Per-planet editable seeds aligned with the selected system's planet order.
        private List<string> _planetNames = new List<string>();
        private List<int>    _planetSeeds = new List<int>();
        private int _selectedSpawnPlanet = 0;  // which planet the player will spawn on

        // Settings tabs.
        private enum STab { Display, Camera, Interface, Audio, Saving, Keybinds }
        private STab _settingsTab = STab.Display;

        // ── Cached fonts (loaded once per scene-load) ──────────────
        private static Font _cachedTextFont;
        private static Font _cachedIconFont;

        // Scroll preservation for settings to avoid jump-to-top on toggle/slider
        private float _savedScrollY = 0f;
        private bool _hasSavedScroll = false;

        // ── Unity Lifecycle ────────────────────────────────────────
        private void Awake()
        {
            UIState.ClearSceneBlocks();
            Time.timeScale = 1f;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
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
            // 14.51.0 - menus scale with the screen (and grew 1.25x at 1080p).
            T.ApplyMenuScale(_doc.panelSettings);
            if (_newSeed == 0)
                _newSeed = UnityEngine.Random.Range(1, int.MaxValue);

            _session = WorldSession.Instance;
            if (_session == null)
            {
                var go = new GameObject("WorldSession");
                _session = go.AddComponent<WorldSession>();
            }

            // Standing in the menu means no join is in flight. Clearing here
            // is what guarantees the boot gate can never be left raised by an
            // abandoned attempt and stall the next single-player world.
            // Safe here, unsafe as a field initializer: see _joinAddress.
            _joinAddress = VoxelEngine.Settings.GameSettings.LastHostAddress;

            _session.ClearRemoteJoin();

            // Joined worlds are caches, not saves. The menu is the one place
            // where nothing is streaming, so it is the safe place to sweep
            // them off disk.
            _session.PurgeJoinedCaches();

            // Player records belong to the world that was open. Standing here
            // means none is, so none may be carried into the next one.
            VoxelEngine.Persistence.PlayerRecords.Clear();
            // Teams obey the same rule: the next world inherits nothing, not
            // even by accident.
            TeamRegistry.ClearAll();

            // Milestone 12, part 1 (14.45.0): a dedicated server never shows
            // this menu. It resolves its world from server_config.json and
            // the command line, installs the headless runner and goes
            // straight into the game scene - the same two launch paths a
            // player would have clicked, just without the clicking.
            if (DedicatedServer.IsActive)
            {
                _dedicatedBoot = true;
                LaunchDedicated();
            }
        }

        private bool _dedicatedBoot;

        /// <summary>Boot the configured world headless. An existing save
        /// folder loads exactly like LoadWorld; a missing one is created
        /// exactly like CreateAndLoadWorld, with the config's seed (0 =
        /// random) and default world settings an admin can later edit from
        /// any client... once that exists. For now: world_settings.json.</summary>
        private void LaunchDedicated()
        {
            var cfg = DedicatedServer.Config;
            DedicatedServer.InstallRunner();

            bool exists = Directory.Exists(_session.WorldFolderPath(cfg.worldName));
            if (exists)
            {
                Debug.Log($"[Server] Loading existing world '{cfg.worldName}'.");
                _session.worldName = cfg.worldName;
                _session.isNewWorld = false;
                _session.LoadWorldSettings();
                _session.LoadCosmosSidecar();
            }
            else
            {
                _session.worldName = cfg.worldName;
                _session.seed = cfg.newWorldSeed != 0
                    ? cfg.newWorldSeed
                    : UnityEngine.Random.Range(1, int.MaxValue);
                _session.isNewWorld = true;
                _session.SaveWorldSettings();
                ApplyCosmosSelectionToSession();   // system 0, fresh planet seeds
                _session.SaveCosmosSidecar();
                Debug.Log($"[Server] Creating new world '{cfg.worldName}' (seed {_session.seed}).");
            }

            UIState.ClearSceneBlocks();
            Time.timeScale = 1f;
            try { SceneManager.LoadScene(gameSceneName); }
            catch (Exception ex) { Debug.LogError("[Server] Could not load game scene: " + ex.Message); }
        }

        private void OnEnable()
        {
            if (_dedicatedBoot) return;   // headless: no menu UI to build
            BuildUI();
        }

        // ── UI Root ────────────────────────────────────────────────
        private void BuildUI()
        {
            // Rebuilding the SAME page (settings toggle, tab refresh) must not replay
            // the LCD boot — only real page changes get the full boot animation.
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
            // Preserve scroll Y if we are rebuilding settings and a ScrollView exists
            if (_root != null)
            {
                var existingScroll = _root.Q<ScrollView>();
                if (existingScroll != null)
                {
                    _savedScrollY = existingScroll.scrollOffset.y;
                    _hasSavedScroll = true;
                }
            }

            _root = _doc.rootVisualElement;
            VoxelEngine.FX.UiAudio.Attach(_root);   // click/hover audio (idempotent)
            _root.Clear();
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.top = 0;
            _root.style.right = 0;
            _root.style.bottom = 0;
            _root.style.flexGrow        = 1;
            _root.style.backgroundColor = new StyleColor(T.BgBase);
            // 14.50.0 - menus live LEFT: panels anchor to the left edge with
            // a fixed gutter, vertically centered. One rule for every page,
            // so main menu and pause menu agree on where a menu IS.
            _root.style.alignItems      = Align.FlexStart;
            _root.style.justifyContent  = Justify.Center;
            _root.style.paddingLeft     = 64;

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
                case Page.EditWorld: BuildEditWorldPage(); break;
                case Page.Multiplayer: BuildMultiplayerPage(); break;
                case Page.EditPlayer: BuildEditPlayerPage(); break;
                case Page.Settings: BuildSettingsPage(); break;
            }

            // 14.51.0 - the trailer only plays on the front page; sub-pages
            // pause it so nothing decodes video behind a settings screen.
            if (_page != Page.Main) PauseTrailer();
        }

        // ════════════════════════════════════════════════════════════
        //                      MAIN PAGE
        // ════════════════════════════════════════════════════════════
        private void BuildMainPage()
        {
            // 14.51.0 - the front page dresses the whole screen: trailer on
            // the right, latest changes top-left, menu on the left. The
            // dressing exists ONLY here - every other page rebuilds the root
            // without it, so tabs stay clean.
            BuildMenuDressing();

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
            panel.Add(PrimaryBtn("MULTIPLAYER", () => { _menuStatus = string.Empty; _page = Page.Multiplayer; BuildUI(); }, T.AccentGreen, LucideIcons.Globe));
            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("SETTINGS",  () => { _page = Page.Settings; BuildUI(); }, T.BgSlot,    LucideIcons.Settings));
            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("QUIT",      QuitGame,                                    T.AccentRed, LucideIcons.X));

            panel.Add(T.Spacer(20));
            var ver = T.Muted($"Build {VoxelEngine.Core.GameVersion.Display}");
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

            panel.Add(PageHeader("SAVES", "BACK", () => { _menuStatus = string.Empty; _page = Page.Main; BuildUI(); }));
            panel.Add(T.AccentDivider());
            if (!string.IsNullOrEmpty(_menuStatus))
            {
                var status = T.Muted(_menuStatus);
                status.style.marginTop = 4;
                status.style.marginBottom = 6;
                status.style.color = new StyleColor(_menuStatus.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ? T.AccentRed : T.AccentTeal);
                panel.Add(status);
            }
            panel.Add(T.Spacer(4));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(scroll);   // themed slim scrollbar
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
            var card = new VisualElement();
            card.style.flexDirection   = FlexDirection.Column;
            card.style.paddingTop      = 12;
            card.style.paddingBottom   = 12;
            card.style.paddingLeft     = 14;
            card.style.paddingRight    = 14;
            card.style.marginBottom    = 8;
            card.style.backgroundColor = new StyleColor(T.BgCard);
            T.Radius(card, T.CardRadius);
            T.Border(card, 1, T.BorderDim);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            card.Add(row);

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
            info.style.flexGrow = 1;
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
            var meta = T.Muted($"{w.lastWrite:dd-MM-yyyy  HH:mm}  ·  {size}{seed}  ·  drops {Mathf.Max(1, w.maxDroppedItems)}  ·  inv {Mathf.Max(25, w.inventoryWeightPercent)}% / containers {Mathf.Max(25, w.containerWeightPercent)}%");
            meta.style.marginTop = 2;
            info.Add(meta);
            row.Add(info);

            // Main action: PLAY remains the primary target.
            var playBtn = BuildIconSmallButton(LucideIcons.Play, "PLAY", () => LoadWorld(w.name), T.AccentCyan);
            playBtn.style.minHeight = 40;
            playBtn.style.minWidth = 92;
            playBtn.style.marginRight = 8;
            row.Add(playBtn);

            // Edit + Saves grouped together as the world-management cluster.
            var manage = new VisualElement();
            manage.style.flexDirection = FlexDirection.Column;
            manage.style.marginRight = 8;
            var editBtn = BuildIconSmallButton(LucideIcons.Settings, "EDIT", () => StartEditWorld(w), T.BgSlot);
            editBtn.style.minWidth = 86;
            editBtn.style.marginBottom = 4;
            manage.Add(editBtn);
            bool savesExpanded = _expandedAutosaveWorld == w.name;
            var savesBtn = BuildIconSmallButton(LucideIcons.Save, savesExpanded ? "HIDE" : "SAVES", () =>
            {
                _expandedAutosaveWorld = savesExpanded ? string.Empty : w.name;
                _menuStatus = string.Empty;
                BuildUI();
            }, T.BgSlot);
            savesBtn.style.minWidth = 86;
            manage.Add(savesBtn);
            row.Add(manage);

            // Clone + smaller Delete stacked beside management.
            var side = new VisualElement();
            side.style.flexDirection = FlexDirection.Column;
            var cloneBtn = BuildIconSmallButton(LucideIcons.Globe, "CLONE", () => CloneWorldAction(w.name), T.AccentTeal);
            cloneBtn.style.minWidth = 82;
            cloneBtn.style.marginBottom = 4;
            side.Add(cloneBtn);
            var delBtn = BuildIconSmallButton(LucideIcons.Trash, "DEL", () =>
            {
                _session.DeleteWorld(w.name);
                _menuStatus = $"Deleted world '{w.name}'.";
                if (_expandedAutosaveWorld == w.name) _expandedAutosaveWorld = string.Empty;
                BuildUI();
            }, T.AccentRed);
            delBtn.style.minWidth = 82;
            delBtn.style.minHeight = 26;
            delBtn.style.fontSize = 9;
            side.Add(delBtn);
            row.Add(side);

            if (savesExpanded)
                card.Add(BuildAutosaveSlots(w.name, true));
            return card;
        }

        private VisualElement BuildAutosaveSlots(string worldName, bool expanded)
        {
            var box = new VisualElement();
            box.style.marginTop = 10;
            box.style.paddingTop = 8;
            box.style.paddingBottom = 8;
            box.style.paddingLeft = 10;
            box.style.paddingRight = 10;
            box.style.backgroundColor = new StyleColor(new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.55f));
            T.Radius(box, T.CardRadius * 0.75f);
            T.Border(box, 1, T.BorderDim);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            var title = T.Muted("AUTOSAVE SLOTS");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1;
            header.Add(title);
            if (expanded)
            {
                var hint = T.Muted("Restore copies the slot to the current save and backs up the previous current save.");
                hint.style.unityTextAlign = TextAnchor.MiddleRight;
                header.Add(hint);
            }
            box.Add(header);

            var slots = new VisualElement();
            slots.style.flexDirection = FlexDirection.Row;
            slots.style.flexWrap = Wrap.Wrap;
            slots.style.marginTop = 6;

            foreach (var slot in _session.GetAutosaveSlots(worldName))
                slots.Add(BuildAutosaveSlotCard(slot));
            box.Add(slots);
            return box;
        }

        private VisualElement BuildAutosaveSlotCard(AutosaveSlotSummary slot)
        {
            var card = new VisualElement();
            card.style.minWidth = 180;
            card.style.flexGrow = 1;
            card.style.marginRight = 6;
            card.style.marginBottom = 6;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 8;
            card.style.paddingRight = 8;
            card.style.backgroundColor = new StyleColor(new Color(T.BgCard.r, T.BgCard.g, T.BgCard.b, 0.82f));
            T.Radius(card, 6f);
            T.Border(card, 1, slot.exists ? T.AccentTeal : T.BorderDim);

            var label = new Label($"SLOT {slot.slotIndex}");
            label.style.color = new StyleColor(slot.exists ? T.TextPrimary : T.TextSecondary);
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(label);

            string metaText = slot.exists
                ? $"{slot.lastWrite:dd-MM HH:mm} · {FormatBytes(slot.sizeBytes)}"
                : "Empty — waiting for autosave";
            var meta = T.Muted(metaText);
            meta.style.marginTop = 2;
            meta.style.marginBottom = 6;
            card.Add(meta);

            var restore = BuildIconSmallButton(LucideIcons.Save, slot.exists ? "RESTORE" : "EMPTY", () =>
            {
                if (!slot.exists) return;
                bool ok = _session.RestoreAutosaveSlot(slot.worldName, slot.slotIndex, out var message);
                _menuStatus = ok ? message : "Error: " + message;
                BuildUI();
            }, slot.exists ? T.AccentCyan : T.BgSlot);
            restore.SetEnabled(slot.exists);
            restore.style.minHeight = 26;
            restore.style.fontSize = 9;
            card.Add(restore);
            return card;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes / (1024.0 * 1024.0):0.00} MB";
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
            VoxelEngine.UI.UITheme.StyleScroller(scroll);   // themed slim scrollbar
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
            // TextField with integer parsing — chosen over IntegerField because
            // IntegerField was editor-only in Unity ≤ 2022 and only became a
            // runtime UIElement in Unity 6. Using TextField + int.TryParse here
            // keeps the menu portable across every supported Unity version.
            var seedField = new TextField { value = _newSeed.ToString() };
            StyleField(seedField);
            seedField.style.flexGrow = 1;
            seedField.RegisterValueChangedCallback(e =>
            {
                if (int.TryParse(e.newValue, out var parsed)) _newSeed = parsed;
            });
            seedRow.Add(seedField);
            var rndBtn = BuildIconSmallButton(LucideIcons.Dice5, "RANDOM", () =>
            {
                _newSeed = UnityEngine.Random.Range(1, int.MaxValue);
                seedField.SetValueWithoutNotify(_newSeed.ToString());
            }, T.AccentTeal);
            rndBtn.style.marginLeft = 8;
            seedRow.Add(rndBtn);
            scroll.Add(seedRow);
            scroll.Add(T.Spacer(12));

            scroll.Add(FormLabel("Maximum Dropped Items"));
            var maxDropsField = new TextField { value = _newMaxDroppedItems.ToString() };
            StyleField(maxDropsField);
            maxDropsField.RegisterValueChangedCallback(e =>
            {
                if (int.TryParse(e.newValue, out var parsed))
                    _newMaxDroppedItems = Mathf.Clamp(parsed, 1, 10000);
            });
            scroll.Add(maxDropsField);
            var maxDropsHelp = T.Muted("Default 1000 · applies only to physical world drops. Conveyor packets are protected separately.");
            maxDropsHelp.style.marginTop = 3;
            scroll.Add(maxDropsHelp);
            scroll.Add(T.Spacer(12));

            scroll.Add(FormLabel("Inventory Weight Limit %"));
            var invWeightField = new TextField { value = _newInventoryWeightPercent.ToString() };
            StyleField(invWeightField);
            invWeightField.RegisterValueChangedCallback(e =>
            {
                if (int.TryParse(e.newValue, out var parsed))
                    _newInventoryWeightPercent = Mathf.Clamp(parsed, 25, 1000);
            });
            scroll.Add(invWeightField);
            scroll.Add(T.Muted($"100% = {MassFormat.Format(WorldSession.DefaultPlayerInventoryWeightKg)} player matter capacity."));
            scroll.Add(T.Spacer(10));

            scroll.Add(FormLabel("Container / Machine Weight Limit %"));
            var containerWeightField = new TextField { value = _newContainerWeightPercent.ToString() };
            StyleField(containerWeightField);
            containerWeightField.RegisterValueChangedCallback(e =>
            {
                if (int.TryParse(e.newValue, out var parsed))
                    _newContainerWeightPercent = Mathf.Clamp(parsed, 25, 1000);
            });
            scroll.Add(containerWeightField);
            scroll.Add(T.Muted($"100% = {MassFormat.Format(WorldSession.DefaultContainerWeightKg)} per chest/machine matter buffer."));
            var dropWarnToggle = new Toggle("Warn before voiding drops above the physical drop limit");
            dropWarnToggle.SetValueWithoutNotify(_newShowDropVoidWarning);
            dropWarnToggle.style.marginTop = 8;
            dropWarnToggle.style.color = new StyleColor(T.TextSecondary);
            dropWarnToggle.RegisterValueChangedCallback(e => _newShowDropVoidWarning = e.newValue);
            scroll.Add(dropWarnToggle);

            var ruinRespawnToggle = new Toggle("Allow Ruin Loot to Respawn (uncheck to disable respawning loot)");
            ruinRespawnToggle.SetValueWithoutNotify(_newAllowRuinLootRespawn);
            ruinRespawnToggle.style.marginTop = 8;
            ruinRespawnToggle.style.color = new StyleColor(T.TextSecondary);
            ruinRespawnToggle.RegisterValueChangedCallback(e => _newAllowRuinLootRespawn = e.newValue);
            scroll.Add(ruinRespawnToggle);

            var friendlyFireToggle = new Toggle("Friendly Fire (teammates can damage each other in multiplayer)");
            friendlyFireToggle.SetValueWithoutNotify(_newFriendlyFire);
            friendlyFireToggle.style.marginTop = 8;
            friendlyFireToggle.style.color = new StyleColor(T.TextSecondary);
            friendlyFireToggle.RegisterValueChangedCallback(e => _newFriendlyFire = e.newValue);
            scroll.Add(friendlyFireToggle);
            var friendlyFireHelp = T.Muted("A world rule, the same for every team - never a per-team choice.");
            friendlyFireHelp.style.marginTop = 2;
            scroll.Add(friendlyFireHelp);

            var bannerPaintToggle = new Toggle("Banner Painting (teams may hand-paint their banner cloth)");
            bannerPaintToggle.SetValueWithoutNotify(_newAllowBannerPainting);
            bannerPaintToggle.style.marginTop = 8;
            bannerPaintToggle.style.color = new StyleColor(T.TextSecondary);
            bannerPaintToggle.RegisterValueChangedCallback(e => _newAllowBannerPainting = e.newValue);
            scroll.Add(bannerPaintToggle);
            var bannerPaintHelp = T.Muted("Off = teams still pick gallery images, texts and the default emblem.");
            bannerPaintHelp.style.marginTop = 2;
            scroll.Add(bannerPaintHelp);
            scroll.Add(T.Spacer(16));

            // ── Cosmos: solar-system picker + per-planet custom seeds ──
            scroll.Add(BuildCosmosSection());
            scroll.Add(T.Spacer(20));

            panel.Add(T.Spacer(8));
            panel.Add(PrimaryBtn("CREATE & PLAY", CreateAndLoadWorld, T.AccentCyan, LucideIcons.Play));
        }

        // ════════════════════════════════════════════════════════════
        //                     EDIT WORLD PAGE
        // ════════════════════════════════════════════════════════════
        private void BuildEditWorldPage()
        {
            var panel = MakePanel(560, 0);
            _root.Add(panel);

            panel.Add(PageHeader("EDIT WORLD", "BACK", () => { _menuStatus = string.Empty; _page = Page.Saves; BuildUI(); }));
            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(4));

            var warning = T.Muted("Non-generation settings only. Seeds, planets, terrain, chunks, and saved builds are never regenerated here.");
            warning.style.marginBottom = 12;
            warning.style.color = new StyleColor(T.AccentTeal);
            panel.Add(warning);

            if (!string.IsNullOrEmpty(_menuStatus))
            {
                var status = T.Muted(_menuStatus);
                status.style.marginBottom = 10;
                status.style.color = new StyleColor(_menuStatus.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ? T.AccentRed : T.AccentTeal);
                panel.Add(status);
            }

            panel.Add(FormLabel("World Name"));
            var nameField = new TextField { value = _editName };
            StyleField(nameField);
            nameField.RegisterValueChangedCallback(e => _editName = SanitizeName(e.newValue));
            panel.Add(nameField);
            panel.Add(T.Spacer(12));

            panel.Add(FormLabel("Maximum Dropped Items"));
            var maxDropsField = new TextField { value = _editMaxDroppedItems.ToString() };
            StyleField(maxDropsField);
            maxDropsField.RegisterValueChangedCallback(e =>
            {
                if (int.TryParse(e.newValue, out var parsed))
                    _editMaxDroppedItems = Mathf.Clamp(parsed, 1, 10000);
            });
            panel.Add(maxDropsField);
            var maxDropsHelp = T.Muted("Default 1000 · applies only to physical world drops. Conveyor packets and belt visuals are protected separately.");
            maxDropsHelp.style.marginTop = 3;
            panel.Add(maxDropsHelp);
            panel.Add(T.Spacer(12));

            panel.Add(FormLabel("Inventory Weight Limit %"));
            var invWeightField = new TextField { value = _editInventoryWeightPercent.ToString() };
            StyleField(invWeightField);
            invWeightField.RegisterValueChangedCallback(e =>
            {
                if (int.TryParse(e.newValue, out var parsed))
                    _editInventoryWeightPercent = Mathf.Clamp(parsed, 25, 1000);
            });
            panel.Add(invWeightField);
            panel.Add(T.Muted($"100% = {MassFormat.Format(WorldSession.DefaultPlayerInventoryWeightKg)} player matter capacity."));
            panel.Add(T.Spacer(10));

            panel.Add(FormLabel("Container / Machine Weight Limit %"));
            var containerWeightField = new TextField { value = _editContainerWeightPercent.ToString() };
            StyleField(containerWeightField);
            containerWeightField.RegisterValueChangedCallback(e =>
            {
                if (int.TryParse(e.newValue, out var parsed))
                    _editContainerWeightPercent = Mathf.Clamp(parsed, 25, 1000);
            });
            panel.Add(containerWeightField);
            panel.Add(T.Muted($"100% = {MassFormat.Format(WorldSession.DefaultContainerWeightKg)} per chest/machine matter buffer."));
            var dropWarnToggle = new Toggle("Warn before voiding drops above the physical drop limit");
            dropWarnToggle.SetValueWithoutNotify(_editShowDropVoidWarning);
            dropWarnToggle.style.marginTop = 8;
            dropWarnToggle.style.color = new StyleColor(T.TextSecondary);
            dropWarnToggle.RegisterValueChangedCallback(e => _editShowDropVoidWarning = e.newValue);
            panel.Add(dropWarnToggle);

            var ruinRespawnToggleEdit = new Toggle("Allow Ruin Loot to Respawn (uncheck to disable respawning loot)");
            ruinRespawnToggleEdit.SetValueWithoutNotify(_editAllowRuinLootRespawn);
            ruinRespawnToggleEdit.style.marginTop = 8;
            ruinRespawnToggleEdit.style.color = new StyleColor(T.TextSecondary);
            ruinRespawnToggleEdit.RegisterValueChangedCallback(e => _editAllowRuinLootRespawn = e.newValue);
            panel.Add(ruinRespawnToggleEdit);

            var friendlyFireToggleEdit = new Toggle("Friendly Fire (teammates can damage each other in multiplayer)");
            friendlyFireToggleEdit.SetValueWithoutNotify(_editFriendlyFire);
            friendlyFireToggleEdit.style.marginTop = 8;
            friendlyFireToggleEdit.style.color = new StyleColor(T.TextSecondary);
            friendlyFireToggleEdit.RegisterValueChangedCallback(e => _editFriendlyFire = e.newValue);
            panel.Add(friendlyFireToggleEdit);
            var friendlyFireHelpEdit = T.Muted("A world rule, the same for every team - never a per-team choice.");
            friendlyFireHelpEdit.style.marginTop = 2;
            panel.Add(friendlyFireHelpEdit);

            var bannerPaintToggleEdit = new Toggle("Banner Painting (teams may hand-paint their banner cloth)");
            bannerPaintToggleEdit.SetValueWithoutNotify(_editAllowBannerPainting);
            bannerPaintToggleEdit.style.marginTop = 8;
            bannerPaintToggleEdit.style.color = new StyleColor(T.TextSecondary);
            bannerPaintToggleEdit.RegisterValueChangedCallback(e => _editAllowBannerPainting = e.newValue);
            panel.Add(bannerPaintToggleEdit);
            var bannerPaintHelpEdit = T.Muted("Off = teams still pick gallery images, texts and the default emblem.");
            bannerPaintHelpEdit.style.marginTop = 2;
            panel.Add(bannerPaintHelpEdit);

            panel.Add(T.Spacer(18));
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            var cancel = BuildIconSmallButton(LucideIcons.ArrowLeft, "CANCEL", () => { _menuStatus = string.Empty; _page = Page.Saves; BuildUI(); }, T.BgSlot);
            cancel.style.marginRight = 8;
            row.Add(cancel);
            row.Add(BuildIconSmallButton(LucideIcons.Save, "SAVE", ApplyWorldEdit, T.AccentCyan));
            panel.Add(row);
        }

        // ════════════════════════════════════════════════════════════
        //                   MULTIPLAYER PAGE  (14.23.0)
        // ════════════════════════════════════════════════════════════
        //
        // Joining used to live in the in-game pause menu, which meant the only
        // way to reach a friend's world was to load a world of your own first
        // - and it had to be built from the same seed or nothing lined up.
        // From here you type an address and arrive in THEIR world: the host
        // sends its seed, its per-planet seed table and its world rules in the
        // handshake, and the client builds from that.
        private void BuildMultiplayerPage()
        {
            var panel = MakePanel(640, 0);
            _root.Add(panel);

            panel.Add(PageHeader("MULTIPLAYER", "BACK", () => { _menuStatus = string.Empty; _page = Page.Main; BuildUI(); }));
            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(10));

            var blurb = T.Muted("Join a friend's world directly. You do not need their save - " +
                                "the world is sent to you when you connect.");
            blurb.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(blurb);
            panel.Add(T.Spacer(10));

            // 14.49.0 - who you ARE in multiplayer: name, chest text, icon.
            panel.Add(PrimaryBtn("EDIT PLAYER", () =>
            {
                _menuStatus = string.Empty;
                _page = Page.EditPlayer;
                BuildUI();
            }, T.AccentTeal, LucideIcons.UserPlus));
            panel.Add(T.Spacer(12));

            // ── 14.48.0 server browser: tabs, list, add form ──────────
            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.Add(MpTabBtn("SERVERS", MpTab.Servers));
            tabs.Add(MpTabBtn("FAVORITES", MpTab.Favorites));
            tabs.Add(MpTabBtn("RECENT", MpTab.Recent));
            tabs.Add(MpTabBtn("LAN SCAN", MpTab.Lan));
            panel.Add(tabs);
            panel.Add(T.Spacer(8));

            var listHost = new VisualElement();
            panel.Add(listHost);
            RebuildBrowserList(listHost);

            // LAN results arrive on a background thread; this drain runs on
            // the UI's clock and dies with the panel, so nothing leaks. Only
            // the list is rebuilt - never the whole page - so typing in the
            // fields below is never interrupted by a reply landing.
            panel.schedule.Execute(() =>
            {
                bool changed = false;
                while (VoxelEngine.Networking.LanDiscovery.TryTake(out var f))
                {
                    bool known = false;
                    for (int i = 0; i < _lanFound.Count; i++)
                        if (_lanFound[i].address == f.address && _lanFound[i].port == f.port)
                        { _lanFound[i] = f; known = true; break; }
                    if (!known) _lanFound.Add(f);
                    changed = true;
                }
                bool scanning = VoxelEngine.Networking.LanDiscovery.Scanning;
                if (scanning != _lanScanShown) { _lanScanShown = scanning; changed = true; }
                if (changed && _mpTab == MpTab.Lan) RebuildBrowserList(listHost);
            }).Every(250);

            panel.Add(T.Spacer(10));
            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(8));

            panel.Add(FormLabel("Add Server"));
            var addRow = new VisualElement();
            addRow.style.flexDirection = FlexDirection.Row;
            addRow.style.alignItems = Align.FlexEnd;

            var nameCol = new VisualElement();
            nameCol.style.flexGrow = 1; nameCol.style.flexBasis = 0; nameCol.style.marginRight = 6;
            nameCol.Add(FormLabel("Name"));
            var addNameField = new TextField { value = _addServerName, maxLength = 32 };
            StyleField(addNameField);
            addNameField.RegisterValueChangedCallback(e => _addServerName = e.newValue);
            nameCol.Add(addNameField);
            addRow.Add(nameCol);

            var addrCol = new VisualElement();
            addrCol.style.flexGrow = 1; addrCol.style.flexBasis = 0; addrCol.style.marginRight = 6;
            addrCol.Add(FormLabel("Address"));
            var addAddrField = new TextField { value = _addServerAddress, maxLength = 64 };
            StyleField(addAddrField);
            addAddrField.RegisterValueChangedCallback(e => _addServerAddress = e.newValue);
            addrCol.Add(addAddrField);
            addRow.Add(addrCol);

            // 14.48.1 - a saved server can carry its own password, autofilled
            // the moment JOIN is pressed on its row. Optional; blank = open.
            var pwCol = new VisualElement();
            pwCol.style.flexGrow = 1; pwCol.style.flexBasis = 0; pwCol.style.marginRight = 6;
            pwCol.Add(FormLabel("Password (optional)"));
            var addPwField = new TextField { value = _addServerPassword, maxLength = 64 };
            addPwField.isPasswordField = true;
            StyleField(addPwField);
            addPwField.RegisterValueChangedCallback(e => _addServerPassword = e.newValue);
            pwCol.Add(addPwField);
            addRow.Add(pwCol);

            var addBtn = MiniBtn("ADD", () =>
            {
                string addr = (_addServerAddress ?? "").Trim();
                if (string.IsNullOrEmpty(addr)) { _menuStatus = "Enter an address to add."; BuildUI(); return; }
                // Blank password = "leave as is", so re-adding to rename an
                // entry never wipes a stored password. A wrong one is fixed
                // by typing the right one here, or simply by joining once
                // via direct connect - the accepted password writes back.
                ServerBrowserStore.AddOrUpdate(_addServerName, addr,
                    string.IsNullOrEmpty(_addServerPassword) ? null : _addServerPassword);
                _addServerName = ""; _addServerAddress = ""; _addServerPassword = "";
                _menuStatus = string.Empty;
                _mpTab = MpTab.Servers;
                BuildUI();
            }, T.AccentTeal, true);
            addBtn.style.minHeight = 30;
            addBtn.style.marginBottom = 4;
            addRow.Add(addBtn);
            panel.Add(addRow);

            panel.Add(T.Spacer(10));
            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(8));

            panel.Add(FormLabel("Direct Connect"));
            var addrField = new TextField { value = _joinAddress };
            StyleField(addrField);
            addrField.RegisterValueChangedCallback(e => _joinAddress = e.newValue);
            panel.Add(addrField);
            panel.Add(T.Spacer(4));

            var hint = T.Muted("IP address or hostname, with an optional :port. " +
                               "Use localhost to join a game on this computer.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(hint);
            panel.Add(T.Spacer(10));

            // 14.47.0 - passworded servers. Blank is correct for open ones;
            // a wrong password comes back as a named refusal. The same
            // password is sent for browser-row joins too.
            panel.Add(FormLabel("Server Password (if any - used for every join)"));
            var pwField = new TextField { value = VoxelEngine.Networking.NetworkBootstrap.JoinPassword };
            pwField.isPasswordField = true;
            StyleField(pwField);
            pwField.RegisterValueChangedCallback(e =>
                VoxelEngine.Networking.NetworkBootstrap.JoinPassword = e.newValue);
            panel.Add(pwField);
            panel.Add(T.Spacer(16));

            panel.Add(PrimaryBtn("JOIN GAME", JoinHost, T.AccentCyan, LucideIcons.Play));
            panel.Add(T.Spacer(16));

            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(8));
            var hostNote = T.Muted("To host: load one of your own worlds, then open the pause menu " +
                                   "and choose HOST THIS WORLD.");
            hostNote.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(hostNote);

            if (!string.IsNullOrEmpty(_menuStatus))
            {
                panel.Add(T.Spacer(10));
                var status = T.Body(_menuStatus);
                status.style.color = new StyleColor(T.AccentRed);
                status.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(status);
            }
            else if (!string.IsNullOrEmpty(VoxelEngine.Networking.NetworkBootstrap.LastSessionNotice))
            {
                // 14.47.0 - a kicked/banned/refused player lands here; the
                // reason the server gave is better than a silent menu.
                panel.Add(T.Spacer(10));
                var notice = T.Body(VoxelEngine.Networking.NetworkBootstrap.LastSessionNotice);
                notice.style.color = new StyleColor(T.AccentRed);
                notice.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(notice);
            }
        }

        // ════════════════════════════════════════════════════════════
        //            14.51.0 FRONT-PAGE DRESSING (trailer + changelog)
        // ════════════════════════════════════════════════════════════

        private UnityEngine.Video.VideoPlayer _trailerPlayer;
        private RenderTexture _trailerRT;
        private static List<(string title, string body)> _recentChanges;
        private static int _changesExpanded = -1;   // which entry is folded open

        /// <summary>The trailer theater (right side) and the latest-changes
        /// card (top-left). Both are absolute overlays added BEFORE the menu
        /// panel, so the menu always draws above them.</summary>
        private void BuildMenuDressing()
        {
            // ── right side: the theater ───────────────────────────────
            var theater = new VisualElement();
            theater.style.position = Position.Absolute;
            theater.style.left = Length.Percent(46f);
            theater.style.right = 0;
            theater.style.top = 0;
            theater.style.bottom = 0;
            theater.style.backgroundColor = new StyleColor(Color.black);
            theater.style.justifyContent = Justify.Center;
            theater.style.alignItems = Align.Center;
            theater.pickingMode = PickingMode.Ignore;
            _root.Add(theater);

            if (EnsureTrailer())
            {
                var screen = new Image { image = _trailerRT, scaleMode = ScaleMode.ScaleToFit };
                screen.style.width = Length.Percent(100f);
                screen.style.height = Length.Percent(100f);
                screen.pickingMode = PickingMode.Ignore;
                theater.Add(screen);
            }
            else
            {
                var hint = T.Muted("Drop a Trailer.mp4 into StreamingAssets and the trailer plays here.");
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.maxWidth = 360;
                hint.pickingMode = PickingMode.Ignore;
                theater.Add(hint);
            }

            // ── top-right: the latest five changes, expandable (14.52.0) ──
            // Each title is a button; clicking it folds the full entry text
            // out underneath, so a player can actually READ what was added.
            var log = new VisualElement();
            log.style.position = Position.Absolute;
            log.style.right = 24;
            log.style.top = 20;
            log.style.width = 440;
            log.style.backgroundColor = new StyleColor(new Color(T.BgCard.r, T.BgCard.g, T.BgCard.b, 0.90f));
            log.style.paddingLeft = 12;
            log.style.paddingRight = 12;
            log.style.paddingTop = 8;
            log.style.paddingBottom = 8;
            T.Radius(log, 6f);
            T.Border(log, 1, T.BorderDim);

            var logTitle = T.Muted("LATEST CHANGES");
            logTitle.style.marginBottom = 4;
            log.Add(logTitle);

            var changes = RecentChanges();
            if (changes.Count == 0)
            {
                log.Add(T.Muted("Changelog.md not found."));
            }
            else for (int i = 0; i < changes.Count; i++)
            {
                int idx = i;
                bool open = _changesExpanded == idx;

                var head = new Button(() =>
                {
                    _changesExpanded = _changesExpanded == idx ? -1 : idx;
                    BuildUI();
                }) { text = (open ? "v  " : ">  ") + changes[idx].title };
                head.style.backgroundColor = new StyleColor(open
                    ? new Color(1f, 1f, 1f, 0.07f) : Color.clear);
                head.style.color = new StyleColor(open ? T.AccentCyan : T.TextPrimary);
                head.style.fontSize = 11;
                head.style.unityTextAlign = TextAnchor.MiddleLeft;
                head.style.marginBottom = 1;
                head.style.paddingTop = 3;
                head.style.paddingBottom = 3;
                T.Radius(head, 4f);
                T.Border(head, 0, Color.clear);
                log.Add(head);

                if (!open) continue;
                var bodyScroll = new ScrollView();
                T.StyleScroller(bodyScroll);
                bodyScroll.style.maxHeight = 280;
                bodyScroll.style.marginBottom = 6;
                var body = T.Body(changes[idx].body);
                body.style.fontSize = 11;
                body.style.whiteSpace = WhiteSpace.Normal;
                body.style.color = new StyleColor(T.TextSecondary);
                bodyScroll.Add(body);
                log.Add(bodyScroll);
            }
            _root.Add(log);
        }

        /// <summary>Find and run the trailer; false = no file, show the hint.
        /// The player and its render texture live on this GameObject and are
        /// reused across page visits - leaving the page merely pauses it.</summary>
        private bool EnsureTrailer()
        {
            if (_trailerPlayer != null)
            {
                if (!_trailerPlayer.isPlaying) _trailerPlayer.Play();
                return true;
            }

            string path = null;
            try
            {
                foreach (var name in new[] { "Trailer.mp4", "trailer.mp4", "Trailer.webm", "trailer.webm", "Trailer.mov" })
                {
                    string candidate = Path.Combine(Application.streamingAssetsPath, name);
                    if (File.Exists(candidate)) { path = candidate; break; }
                }
            }
            catch { }
            if (path == null) return false;

            _trailerRT = new RenderTexture(1280, 720, 0) { name = "TrailerRT" };
            var vp = gameObject.AddComponent<UnityEngine.Video.VideoPlayer>();
            vp.playOnAwake = false;
            vp.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
            vp.targetTexture = _trailerRT;
            vp.url = path;
            vp.isLooping = true;
            // Silent on purpose: menu music territory, not a cinema.
            vp.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
            _trailerPlayer = vp;
            _trailerPlayer.Play();
            return true;
        }

        private void PauseTrailer()
        {
            if (_trailerPlayer != null && _trailerPlayer.isPlaying) _trailerPlayer.Pause();
        }

        /// <summary>The newest five entries out of Changelog.md - title AND
        /// body, so the card can fold the full notes open. Read from Assets
        /// in the editor, from StreamingAssets in a build (copy it there
        /// when packaging). Markdown bold markers are stripped for the UI.</summary>
        private static List<(string title, string body)> RecentChanges()
        {
            if (_recentChanges != null) return _recentChanges;
            _recentChanges = new List<(string, string)>();
            try
            {
                string[] candidates =
                {
                    Path.Combine(Application.dataPath, "Changelog.md"),
                    Path.Combine(Application.streamingAssetsPath, "Changelog.md")
                };
                foreach (var candidate in candidates)
                {
                    if (!File.Exists(candidate)) continue;
                    string title = null;
                    var body = new System.Text.StringBuilder();
                    foreach (var line in File.ReadLines(candidate))
                    {
                        if (line.StartsWith("### ", StringComparison.Ordinal))
                        {
                            if (title != null)
                            {
                                _recentChanges.Add((title, body.ToString().Trim()));
                                if (_recentChanges.Count >= 5) { title = null; break; }
                            }
                            title = line.Substring(4).Trim();
                            body.Length = 0;
                            continue;
                        }
                        if (title == null) continue;
                        string clean = line.Replace("**", "").TrimEnd();
                        if (clean.Length == 0 && body.Length > 0
                            && body[body.Length - 1] == '\n') continue;   // collapse blank runs
                        body.Append(clean).Append('\n');
                    }
                    if (title != null && _recentChanges.Count < 5)
                        _recentChanges.Add((title, body.ToString().Trim()));
                    break;
                }
            }
            catch { }
            return _recentChanges;
        }

        // ════════════════════════════════════════════════════════════
        //                14.48.0 SERVER BROWSER HELPERS
        // ════════════════════════════════════════════════════════════

        private Button MpTabBtn(string text, MpTab tab)
        {
            bool active = _mpTab == tab;
            var b = new Button(() =>
            {
                _mpTab = tab;
                // Opening the LAN tab is itself the question - scan at once
                // instead of making the player find the button first.
                if (tab == MpTab.Lan && !VoxelEngine.Networking.LanDiscovery.Scanning)
                {
                    _lanFound.Clear();
                    VoxelEngine.Networking.LanDiscovery.StartScan(3f);
                    _lanScanShown = true;
                }
                BuildUI();
            }) { text = text };
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
            LcdHudTheme.AddMenuInteractions(b, T.AccentCyan,
                active ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.85f)
                       : new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.85f));
            return b;
        }

        /// <summary>Compact row action button for browser rows.</summary>
        private static Button MiniBtn(string text, Action onClick, Color accent, bool filled = false)
        {
            var b = new Button(onClick) { text = text };
            b.style.minHeight               = 24;
            b.style.fontSize                = 10;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.paddingLeft             = 9;
            b.style.paddingRight            = 9;
            b.style.marginLeft              = 4;
            Color bg = filled
                ? new Color(accent.r, accent.g, accent.b, 0.85f)
                : new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.85f);
            b.style.color = filled ? Color.white : new StyleColor(accent).value;
            b.style.backgroundColor = new StyleColor(bg);
            T.Radius(b, T.ButtonRadius);
            T.Border(b, 0, Color.clear);
            LcdHudTheme.AddMenuInteractions(b, accent, bg);
            return b;
        }

        private static readonly Color FavGold = new Color(0.95f, 0.78f, 0.25f);

        /// <summary>Fills the browser list for the active tab. Rebuilds only
        /// this container, so the form fields below keep their focus.</summary>
        private void RebuildBrowserList(VisualElement host)
        {
            host.Clear();

            var scroll = new ScrollView();
            T.StyleScroller(scroll);
            scroll.style.maxHeight = 236;

            if (_mpTab == MpTab.Lan)
            {
                var headRow = new VisualElement();
                headRow.style.flexDirection = FlexDirection.Row;
                headRow.style.alignItems    = Align.Center;
                headRow.style.marginBottom  = 6;

                var scanBtn = MiniBtn("SCAN AGAIN", () =>
                {
                    _lanFound.Clear();
                    VoxelEngine.Networking.LanDiscovery.StartScan(3f);
                    _lanScanShown = true;
                    RebuildBrowserList(host);
                }, T.AccentCyan, true);
                scanBtn.style.marginLeft = 0;
                headRow.Add(scanBtn);

                var scanState = T.Muted(_lanScanShown
                    ? "Scanning the local network..."
                    : _lanFound.Count == 0
                        ? "No servers answered. The host must be on this network and in their world."
                        : $"{_lanFound.Count} server{(_lanFound.Count == 1 ? "" : "s")} found.");
                scanState.style.marginLeft = 8;
                scanState.style.whiteSpace = WhiteSpace.Normal;
                scanState.style.flexShrink = 1;
                headRow.Add(scanState);
                host.Add(headRow);

                foreach (var f in _lanFound) scroll.Add(LanRow(f, host));
                host.Add(scroll);
                return;
            }

            IReadOnlyList<ServerBrowserEntry> entries =
                _mpTab == MpTab.Favorites ? ServerBrowserStore.Favorites() :
                _mpTab == MpTab.Recent    ? ServerBrowserStore.Recents() :
                                            ServerBrowserStore.All;

            if (entries.Count == 0)
            {
                var empty = T.Muted(
                    _mpTab == MpTab.Favorites
                        ? "No favorites yet. Press FAV on any server to pin it here."
                    : _mpTab == MpTab.Recent
                        ? "Nothing joined yet. Servers you successfully join appear here on their own."
                        : "No saved servers yet. Add one below, star a LAN find, or just join - " +
                          "every successful join is remembered under RECENT.");
                empty.style.whiteSpace = WhiteSpace.Normal;
                host.Add(empty);
                return;
            }

            foreach (var e in entries) scroll.Add(BrowserEntryRow(e, host));
            host.Add(scroll);
        }

        private VisualElement BrowserRowShell()
        {
            var row = new VisualElement();
            row.style.flexDirection   = FlexDirection.Row;
            row.style.alignItems      = Align.Center;
            row.style.backgroundColor = new StyleColor(T.BgCard);
            row.style.paddingLeft     = 8;
            row.style.paddingRight    = 8;
            row.style.paddingTop      = 6;
            row.style.paddingBottom   = 6;
            row.style.marginBottom    = 4;
            T.Radius(row, 5f);
            T.Border(row, 1, T.BorderDim);
            return row;
        }

        private VisualElement BrowserRowInfo(string title, string sub)
        {
            var col = new VisualElement();
            col.style.flexGrow   = 1;
            col.style.flexShrink = 1;
            col.style.overflow   = Overflow.Hidden;

            var name = T.Body(string.IsNullOrEmpty(title) ? sub : title);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            name.style.overflow   = Overflow.Hidden;
            col.Add(name);

            var addr = T.Muted(sub);
            addr.style.fontSize   = 10;
            addr.style.whiteSpace = WhiteSpace.NoWrap;
            addr.style.overflow   = Overflow.Hidden;
            col.Add(addr);
            return col;
        }

        private VisualElement BrowserEntryRow(ServerBrowserEntry e, VisualElement host)
        {
            var row = BrowserRowShell();

            var fav = MiniBtn("FAV", () =>
            {
                ServerBrowserStore.ToggleFavorite(e.address);
                RebuildBrowserList(host);
            }, e.favorite ? FavGold : T.TextSecondary, e.favorite);
            fav.style.marginLeft  = 0;
            fav.style.marginRight = 8;
            row.Add(fav);

            string sub = e.address;
            if (e.lastJoinedTicks > 0)
            {
                var when = new DateTime(e.lastJoinedTicks, DateTimeKind.Utc).ToLocalTime();
                sub += "   last joined " + when.ToString("dd MMM HH:mm");
            }
            if (!string.IsNullOrEmpty(e.password)) sub += "   password saved";
            row.Add(BrowserRowInfo(e.name, sub));

            row.Add(MiniBtn("JOIN", () =>
            {
                // 14.48.1 - the saved entry speaks for itself: its stored
                // password is applied whole (empty included - a saved OPEN
                // server must not inherit whatever was typed below).
                VoxelEngine.Networking.NetworkBootstrap.JoinPassword = e.password ?? "";
                JoinHostTo(e.address);
            }, T.AccentCyan, true));
            row.Add(MiniBtn("DEL", () =>
            {
                ServerBrowserStore.Remove(e.address);
                RebuildBrowserList(host);
            }, T.AccentRed));
            return row;
        }

        private VisualElement LanRow(VoxelEngine.Networking.LanDiscovery.Found f, VisualElement host)
        {
            var row = BrowserRowShell();
            string address = f.port > 0 ? f.address + ":" + f.port : f.address;
            var saved = ServerBrowserStore.Find(address);

            var fav = MiniBtn("FAV", () =>
            {
                // Starring a LAN find saves it under its broadcast name, so
                // it is still there when the server is offline.
                if (ServerBrowserStore.Find(address) == null)
                    ServerBrowserStore.AddOrUpdate(f.serverName, address);
                ServerBrowserStore.ToggleFavorite(address);
                RebuildBrowserList(host);
            }, saved != null && saved.favorite ? FavGold : T.TextSecondary, saved != null && saved.favorite);
            fav.style.marginLeft  = 0;
            fav.style.marginRight = 8;
            row.Add(fav);

            string world = string.IsNullOrEmpty(f.worldName) ? "unknown world" : f.worldName;
            string lanSub = $"{world}   {f.players}/{f.maxPlayers} players   {address}";
            if (saved != null && !string.IsNullOrEmpty(saved.password)) lanSub += "   password saved";
            row.Add(BrowserRowInfo(f.serverName, lanSub));
            row.Add(MiniBtn("JOIN", () =>
            {
                // 14.48.1 - a LAN find whose address is already in the book
                // joins with the book's password; an unknown one keeps
                // whatever is typed in the field below.
                var known = ServerBrowserStore.Find(address);
                if (known != null)
                    VoxelEngine.Networking.NetworkBootstrap.JoinPassword = known.password ?? "";
                JoinHostTo(address);
            }, T.AccentCyan, true));
            return row;
        }

        // ════════════════════════════════════════════════════════════
        //                14.49.0 EDIT PLAYER PAGE
        // ════════════════════════════════════════════════════════════

        private void BuildEditPlayerPage()
        {
            var panel = MakePanel(560, 0);
            _root.Add(panel);

            panel.Add(PageHeader("EDIT PLAYER", "BACK",
                () => { _menuStatus = string.Empty; _page = Page.Multiplayer; BuildUI(); }));
            panel.Add(T.AccentDivider());
            panel.Add(T.Spacer(10));

            EnsurePlayerDrafts();

            var scroll = new ScrollView();
            T.StyleScroller(scroll);
            scroll.style.maxHeight = 640;
            panel.Add(scroll);

            scroll.Add(FormLabel("Player Name"));
            var nameField = new TextField { value = VoxelEngine.Networking.PlayerIdentity.LocalName, maxLength = 20 };
            StyleField(nameField);
            nameField.RegisterValueChangedCallback(e => VoxelEngine.Networking.PlayerIdentity.LocalName = e.newValue);
            scroll.Add(nameField);
            scroll.Add(T.Spacer(8));

            scroll.Add(FormLabel("Chest Text (worn on your chest - blank for none)"));
            var chestField = new TextField
            { value = _chestTextDraft, maxLength = PlayerCosmeticsRegistry.MaxChestTextLength };
            StyleField(chestField);
            chestField.RegisterValueChangedCallback(e => _chestTextDraft = e.newValue);
            scroll.Add(chestField);
            scroll.Add(T.Spacer(10));

            // ── icon preview, which doubles as the painting board ─────
            scroll.Add(FormLabel("Icon"));
            int previewSize = _iconPainting ? 280 : 140;
            var preview = new Image { image = _iconDraft, scaleMode = ScaleMode.StretchToFill };
            preview.style.width = previewSize;
            preview.style.height = previewSize;
            preview.style.alignSelf = Align.Center;
            preview.style.marginBottom = 6;
            T.Border(preview, 2, new Color(0.85f, 0.68f, 0.21f, 0.8f));
            scroll.Add(preview);

            preview.RegisterCallback<PointerDownEvent>(e =>
            {
                if (!_iconPainting) return;
                preview.CapturePointer(e.pointerId);
                PaintIconAt(preview, e.localPosition);
                e.StopPropagation();
            });
            preview.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_iconPainting || !preview.HasPointerCapture(e.pointerId)) return;
                PaintIconAt(preview, e.localPosition);
            });
            preview.RegisterCallback<PointerUpEvent>(e =>
            {
                if (preview.HasPointerCapture(e.pointerId)) preview.ReleasePointer(e.pointerId);
            });

            if (!_iconDraftHasImage)
            {
                var none = T.Muted("No icon right now - paint one, or pick an image below.");
                none.style.alignSelf = Align.Center;
                scroll.Add(none);
            }

            // ── sources ───────────────────────────────────────────────
            var srcRow = new VisualElement();
            srcRow.style.flexDirection = FlexDirection.Row;
            srcRow.style.flexWrap = Wrap.Wrap;
            srcRow.Add(MiniBtn("BLANK", () =>
            {
                FillIconDraft(IconBlank);
                _iconDraftHasImage = true;
                preview.MarkDirtyRepaint();
                BuildUI();
            }, T.TextPrimary));
            srcRow.Add(MiniBtn("NO ICON", () =>
            {
                FillIconDraft(IconBlank);
                _iconDraftHasImage = false;
                BuildUI();
            }, T.TextPrimary));
            srcRow.Add(MiniBtn("OPEN FOLDER", () =>
            {
                string dir = PlayerCosmeticsRegistry.IconsFolder;
                Application.OpenURL("file:///" + dir.Replace('\\', '/'));
            }, T.TextPrimary));
            srcRow.Add(MiniBtn("RESCAN", () =>
            {
                foreach (var tex in _iconGalleryCache.Values)
                    if (tex != null) Destroy(tex);
                _iconGalleryCache.Clear();
                BuildUI();
            }, T.TextPrimary));
            scroll.Add(srcRow);
            var dropHint = T.Muted("Drop PNG or JPG images into the PlayerIcons folder and RESCAN - square works best.");
            dropHint.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(dropHint);

            // ── gallery ───────────────────────────────────────────────
            var gallery = new VisualElement();
            gallery.style.flexDirection = FlexDirection.Row;
            gallery.style.flexWrap = Wrap.Wrap;
            gallery.style.marginTop = 4;
            int shown = 0;
            foreach (var path in PlayerIconFiles())
            {
                if (shown >= 24) break;
                var thumbTex = LoadPlayerIconTexture(path);
                if (thumbTex == null) continue;
                shown++;
                var thumb = new Image { image = thumbTex, scaleMode = ScaleMode.StretchToFill };
                thumb.style.width = 44;
                thumb.style.height = 44;
                thumb.style.marginRight = 4;
                thumb.style.marginBottom = 4;
                T.Border(thumb, 1, T.BorderDim);
                var captured = thumbTex;
                thumb.RegisterCallback<ClickEvent>(_ =>
                {
                    LoadIconDraftFrom(captured);
                    _iconDraftHasImage = true;
                    preview.MarkDirtyRepaint();
                    BuildUI();
                });
                gallery.Add(thumb);
            }
            if (shown > 0) scroll.Add(gallery);

            // ── painting board ────────────────────────────────────────
            scroll.Add(T.Spacer(6));
            var paintRow = new VisualElement();
            paintRow.style.flexDirection = FlexDirection.Row;
            paintRow.Add(MiniBtn(_iconPainting ? "PAINTING: ON" : "PAINTING: OFF", () =>
            {
                _iconPainting = !_iconPainting;
                BuildUI();
            }, _iconPainting ? T.AccentGreen : T.TextSecondary, _iconPainting));
            scroll.Add(paintRow);

            if (_iconPainting)
            {
                var paintHint = T.Muted("Click and drag on the icon above to paint.");
                scroll.Add(paintHint);
                var swatches = new VisualElement();
                swatches.style.flexDirection = FlexDirection.Row;
                swatches.style.flexWrap = Wrap.Wrap;
                foreach (var swatch in IconBrushPalette())
                {
                    var c = swatch;
                    var b = new Button(() => { _iconBrush = c; _iconErasing = false; BuildUI(); }) { text = "" };
                    b.style.width = 24; b.style.height = 24;
                    b.style.marginRight = 4; b.style.marginBottom = 4;
                    b.style.backgroundColor = new StyleColor((Color)c);
                    T.Radius(b, 4);
                    bool selected = !_iconErasing && c.r == _iconBrush.r && c.g == _iconBrush.g
                        && c.b == _iconBrush.b && c.a == _iconBrush.a;
                    T.Border(b, selected ? 2 : 1, selected ? Color.white : T.BorderDim);
                    swatches.Add(b);
                }
                scroll.Add(swatches);
                var sizeRow = new VisualElement();
                sizeRow.style.flexDirection = FlexDirection.Row;
                sizeRow.style.alignItems = Align.Center;
                sizeRow.Add(T.Muted("BRUSH "));
                foreach (var (label, radius) in new[] { ("S", 3), ("M", 6), ("L", 12) })
                {
                    int r = radius;
                    sizeRow.Add(MiniBtn(label, () => { _iconBrushRadius = r; BuildUI(); },
                        _iconBrushRadius == r ? T.AccentCyan : T.TextSecondary, _iconBrushRadius == r));
                }
                // 14.52.0 - the eraser: paints the blank canvas color, so a
                // slip is undone with the same drag that caused it.
                sizeRow.Add(T.Muted("  "));
                sizeRow.Add(MiniBtn("ERASER", () => { _iconErasing = !_iconErasing; BuildUI(); },
                    _iconErasing ? T.AccentCyan : T.TextSecondary, _iconErasing));
                scroll.Add(sizeRow);
            }

            // ── save ──────────────────────────────────────────────────
            scroll.Add(T.Spacer(12));
            scroll.Add(PrimaryBtn("SAVE PLAYER", () =>
            {
                byte[] png = _iconDraftHasImage && _iconDraft != null ? _iconDraft.EncodeToPNG() : null;
                PlayerCosmeticsRegistry.SaveLocal(png, _chestTextDraft);
                _menuStatus = "Saved. Your crest is worn the next time you join or host.";
                BuildUI();
            }, T.AccentGreen, LucideIcons.Save));

            if (!string.IsNullOrEmpty(_menuStatus))
            {
                scroll.Add(T.Spacer(8));
                var status = T.Body(_menuStatus);
                status.style.color = new StyleColor(T.AccentTeal);
                status.style.whiteSpace = WhiteSpace.Normal;
                scroll.Add(status);
            }
        }

        /// <summary>Seed the drafts from the local store, once per menu life.</summary>
        private void EnsurePlayerDrafts()
        {
            if (_editPlayerSeeded && _iconDraft != null) return;
            _editPlayerSeeded = true;
            _chestTextDraft = PlayerCosmeticsRegistry.LocalChestText;
            if (_iconDraft == null)
            {
                _iconDraft = new Texture2D(PlayerCosmeticsRegistry.IconSize,
                    PlayerCosmeticsRegistry.IconSize, TextureFormat.RGBA32, false)
                { name = "PlayerIconDraft", wrapMode = TextureWrapMode.Clamp };
            }
            var png = PlayerCosmeticsRegistry.LoadLocalIconPng();
            if (png != null)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(png)) { LoadIconDraftFrom(tex); _iconDraftHasImage = true; }
                else _iconDraftHasImage = false;
                Destroy(tex);
            }
            else
            {
                FillIconDraft(IconBlank);
                _iconDraftHasImage = false;
            }
        }

        /// <summary>The blank canvas color - what the eraser paints with.</summary>
        private static readonly Color32 IconBlank = new Color32(242, 238, 228, 255);

        private void FillIconDraft(Color32 color)
        {
            if (_iconDraft == null) return;
            int w = _iconDraft.width, h = _iconDraft.height;
            if (_iconDraftPixels == null || _iconDraftPixels.Length != w * h)
                _iconDraftPixels = new Color32[w * h];
            for (int i = 0; i < _iconDraftPixels.Length; i++) _iconDraftPixels[i] = color;
            _iconDraft.SetPixels32(_iconDraftPixels);
            _iconDraft.Apply(false, false);
        }

        /// <summary>Nearest-neighbor resample of any readable texture into
        /// the canonical square draft.</summary>
        private void LoadIconDraftFrom(Texture2D source)
        {
            if (_iconDraft == null || source == null) return;
            int w = _iconDraft.width, h = _iconDraft.height;
            var src = source.GetPixels32();
            int sw = source.width, sh = source.height;
            _iconDraftPixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int sy = Mathf.Clamp(y * sh / h, 0, sh - 1);
                for (int x = 0; x < w; x++)
                {
                    int sx = Mathf.Clamp(x * sw / w, 0, sw - 1);
                    var px = src[sy * sw + sx];
                    px.a = 255;   // the crest quad is opaque
                    _iconDraftPixels[y * w + x] = px;
                }
            }
            _iconDraft.SetPixels32(_iconDraftPixels);
            _iconDraft.Apply(false, false);
        }

        /// <summary>Stamp one brush circle in icon pixel space.</summary>
        private void PaintIconAt(Image preview, Vector2 local)
        {
            if (_iconDraft == null || _iconDraftPixels == null) return;
            float uiW = preview.resolvedStyle.width, uiH = preview.resolvedStyle.height;
            if (uiW <= 1f || uiH <= 1f) return;
            int w = _iconDraft.width, h = _iconDraft.height;
            int cx = Mathf.RoundToInt(local.x / uiW * w);
            int cy = Mathf.RoundToInt((1f - local.y / uiH) * h);
            int r = Mathf.Max(1, _iconBrushRadius);
            int r2 = r * r;
            for (int y = Mathf.Max(0, cy - r); y <= Mathf.Min(h - 1, cy + r); y++)
            {
                int dy = y - cy;
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(w - 1, cx + r); x++)
                {
                    int dx = x - cx;
                    if (dx * dx + dy * dy > r2) continue;
                    _iconDraftPixels[y * w + x] = _iconErasing ? IconBlank : _iconBrush;
                }
            }
            _iconDraft.SetPixels32(_iconDraftPixels);
            _iconDraft.Apply(false, false);
            _iconDraftHasImage = true;
            preview.MarkDirtyRepaint();
        }

        private static IEnumerable<string> PlayerIconFiles()
        {
            string dir = PlayerCosmeticsRegistry.IconsFolder;
            List<string> files = new();
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") files.Add(f);
                }
                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return files;
        }

        private Texture2D LoadPlayerIconTexture(string path)
        {
            if (_iconGalleryCache.TryGetValue(path, out var cached) && cached != null) return cached;
            try
            {
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                { name = "IconGallery_" + Path.GetFileName(path), wrapMode = TextureWrapMode.Clamp };
                if (!tex.LoadImage(bytes)) { Destroy(tex); return null; }
                _iconGalleryCache[path] = tex;
                return tex;
            }
            catch { return null; }
        }

        private static IEnumerable<Color32> IconBrushPalette() => new Color32[]
        {
            new(168, 24, 28, 255),  new(222, 158, 28, 255), new(32, 90, 167, 255),
            new(34, 120, 54, 255),  new(94, 56, 29, 255),   new(104, 36, 128, 255),
            new(220, 220, 214, 255), new(242, 238, 228, 255), new(24, 24, 26, 255),
            new(120, 124, 130, 255), new(206, 96, 44, 255),  new(64, 160, 164, 255)
        };

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
            tabs.style.flexWrap      = Wrap.Wrap;   // 14.50.0 - deep tabs wrap, never overflow
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
            scroll.style.flexGrow  = 1;
            scroll.style.maxHeight = 420;
            VoxelEngine.UI.SettingsUI.ApplyLcdScreen(scroll);
            panel.Add(scroll);

            switch (_settingsTab)
            {
                case STab.Display:  DisplayTab(scroll);  break;
                case STab.Camera:   CameraTab(scroll);   break;
                case STab.Interface: SettingsUI.InterfaceTab(scroll, BuildUI); break;
                case STab.Audio:    AudioTab(scroll);     break;
                case STab.Saving:   SavingTab(scroll);    break;
                case STab.Keybinds: KeybindTab(scroll);   break;
            }

            // Restore preserved scroll offset (prevents jump-to-top on toggle/slider rebuild)
            if (_hasSavedScroll && _settingsTab == STab.Interface)
            {
                float y = _savedScrollY;
                scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0, y)).ExecuteLater(20);
            }
            _hasSavedScroll = false;

            panel.Add(T.Spacer(8));
            var resetBtn = PrimaryBtn("RESET DEFAULTS", () => { GameSettings.ResetToDefaults(); BuildUI(); }, T.AccentRed);
            resetBtn.style.alignSelf = Align.FlexEnd;
            resetBtn.style.minWidth  = 160;
            resetBtn.style.minHeight = 30;
            resetBtn.style.fontSize  = 10;
            panel.Add(resetBtn);
        }

        // ── Settings Tab Implementations ───────────────────────────
        // All four tabs now delegate to the shared SettingsUI builder so the
        // main menu and the in-game pause menu can never drift apart.
        private void DisplayTab(VisualElement p)  => SettingsUI.DisplayTab(p, BuildUI);
        private void CameraTab(VisualElement p)   => SettingsUI.CameraTab(p, BuildUI);
        private void AudioTab(VisualElement p)    => SettingsUI.AudioTab(p, BuildUI, this);
        private void SavingTab(VisualElement p)   => SettingsUI.SavingTab(p, BuildUI);
        private void KeybindTab(VisualElement p)  => SettingsUI.KeybindTab(p, this, BuildUI);

        // ── Page Actions ───────────────────────────────────────────
        private void StartEditWorld(WorldSummary world)
        {
            _editOriginalName = world.name;
            _editName = world.name;
            _editMaxDroppedItems = Mathf.Max(1, world.maxDroppedItems);
            _editInventoryWeightPercent = Mathf.Clamp(world.inventoryWeightPercent <= 0 ? WorldSession.DefaultInventoryWeightPercent : world.inventoryWeightPercent, 25, 1000);
            _editContainerWeightPercent = Mathf.Clamp(world.containerWeightPercent <= 0 ? WorldSession.DefaultContainerWeightPercent : world.containerWeightPercent, 25, 1000);
            _editShowDropVoidWarning = world.showDropVoidWarning;
            _editAllowRuinLootRespawn = world.allowRuinLootRespawn;
            _editFriendlyFire = world.friendlyFire;
            _editAllowBannerPainting = world.allowBannerPainting;
            _menuStatus = string.Empty;
            _page = Page.EditWorld;
            BuildUI();
        }

        private void ApplyWorldEdit()
        {
            string requestedName = SanitizeName(_editName);
            if (string.IsNullOrWhiteSpace(requestedName)) requestedName = _editOriginalName;
            string finalName = _editOriginalName;

            if (!string.Equals(requestedName, _editOriginalName, StringComparison.Ordinal))
            {
                if (!_session.RenameWorld(_editOriginalName, requestedName, out var renameMessage))
                {
                    _menuStatus = "Error: " + renameMessage;
                    BuildUI();
                    return;
                }
                finalName = requestedName;
            }

            if (!_session.SaveWorldSettingsFor(finalName, _editMaxDroppedItems, _editInventoryWeightPercent, _editContainerWeightPercent, _editShowDropVoidWarning, _editAllowRuinLootRespawn, _editFriendlyFire, _editAllowBannerPainting))
            {
                _menuStatus = "Error: Could not save world settings.";
                BuildUI();
                return;
            }

            _menuStatus = $"Saved world settings for '{finalName}'.";
            _editOriginalName = finalName;
            _page = Page.Saves;
            BuildUI();
        }

        private void LoadWorld(string worldName)
        {
            _session.worldName  = worldName;
            _session.isNewWorld = false;
            _session.LoadWorldSettings();
            // Restore this world's per-planet seeds / chosen system into the session so the
            // game-scene bootstrap can apply them to the celestial bodies.
            _session.LoadCosmosSidecar();
            UIState.ClearSceneBlocks();
            Time.timeScale = 1f;
            try { SceneManager.LoadScene(gameSceneName); }
            catch (Exception ex) { Debug.LogError("[MainMenu] Could not load scene: " + ex.Message); }
        }

        /// <summary>Load the game scene as a guest in somebody else's world.
        /// No world is generated here and no save is touched: the scene comes
        /// up with world generation held, NetworkBootstrap connects, and the
        /// host's world card releases the gate.</summary>
        private void JoinHost() => JoinHostTo(_joinAddress);

        /// <summary>14.48.0 - one join path for the direct-connect field,
        /// browser rows and LAN finds. The address may carry a ":port".</summary>
        private void JoinHostTo(string rawAddress)
        {
            string address = (rawAddress ?? "").Trim();
            if (string.IsNullOrEmpty(address))
            {
                _menuStatus = "Enter the host's address first.";
                BuildUI();
                return;
            }

            VoxelEngine.Settings.GameSettings.LastHostAddress = address;
            _menuStatus = string.Empty;
            VoxelEngine.Networking.NetworkBootstrap.LastSessionNotice = "";   // fresh attempt, fresh verdict

            // A placeholder world identity, replaced the moment the host's
            // card arrives. It exists only so nothing downstream reads a null
            // world name before the handshake lands.
            _session.worldName  = WorldSession.JoinedCacheFolderName(address);
            _session.isNewWorld = false;
            _session.BeginRemoteJoin(address);

            UIState.ClearSceneBlocks();
            Time.timeScale = 1f;
            try { SceneManager.LoadScene(gameSceneName); }
            catch (Exception ex)
            {
                _session.ClearRemoteJoin();
                Debug.LogError("[MainMenu] Could not load scene: " + ex.Message);
            }
        }

        private void CreateAndLoadWorld()
        {
            if (string.IsNullOrWhiteSpace(_newName)) _newName = "MyWorld";
            _session.worldName         = _newName;
            _session.seed              = _newSeed;
            _session.isNewWorld        = true;
            _session.maxDroppedItems   = Mathf.Clamp(_newMaxDroppedItems, 1, 10000);
            _session.inventoryWeightPercent = Mathf.Clamp(_newInventoryWeightPercent, 25, 1000);
            _session.containerWeightPercent = Mathf.Clamp(_newContainerWeightPercent, 25, 1000);
            _session.showDropVoidWarning = _newShowDropVoidWarning;
            _session.allowRuinLootRespawn = _newAllowRuinLootRespawn;
            _session.friendlyFire = _newFriendlyFire;
            _session.allowBannerPainting = _newAllowBannerPainting;
            _session.SaveWorldSettings();

            // Persist the cosmos choice (system + per-planet seeds) so the same seeds
            // regenerate the identical world on every subsequent load.
            ApplyCosmosSelectionToSession();
            _session.SaveCosmosSidecar();

            UIState.ClearSceneBlocks();
            Time.timeScale = 1f;
            try { SceneManager.LoadScene(gameSceneName); }
            catch (Exception ex) { Debug.LogError("[MainMenu] Could not load scene: " + ex.Message); }
        }

        // ── Cosmos helpers ────────────────────────────────────────
        /// <summary>Populate the cached list of available solar systems (once per menu session).</summary>
        private void EnsureSystemChoicesLoaded()
        {
            if (_systemChoices != null) return;
            _systemChoices = new List<SolarSystemTemplate>();
            var library = CosmosTemplateLibrary.Load();
            if (library != null && library.systems != null)
            {
                foreach (var s in library.systems)
                    if (s != null) _systemChoices.Add(s);
            }
            // No library yet → seed an empty synthetic entry so the UI still renders.
            if (_systemChoices.Count == 0)
                _systemChoices.Add(null);

            RebuildPlanetSeedsForSystem(_selectedSystemIndex);
        }

        /// <summary>(Re)build the per-planet editable seed list for the selected system.</summary>
        private void RebuildPlanetSeedsForSystem(int systemIndex)
        {
            _planetNames.Clear();
            _planetSeeds.Clear();

            var sys = (systemIndex >= 0 && systemIndex < _systemChoices.Count) ? _systemChoices[systemIndex] : null;
            if (sys == null || sys.planets == null || sys.planets.Length == 0)
            {
                _planetNames.Add("Earth");
                _planetSeeds.Add(SystemSeedState.RandomSeed());
                return;
            }
            for (int i = 0; i < sys.planets.Length; i++)
            {
                var p = sys.planets[i];
                _planetNames.Add(p != null && p.body != null ? p.body.bodyName : ("Planet " + (i + 1)));
                _planetSeeds.Add(SystemSeedState.RandomSeed());
            }
        }

        /// <summary>
        /// Push the menu's cosmos selection into the session as a SystemSeedState (the structure
        /// the world bootstrap consumes). Seeds are the player-edited values, never re-randomised.
        /// </summary>
        private void ApplyCosmosSelectionToSession()
        {
            EnsureSystemChoicesLoaded();
            var sys = (_selectedSystemIndex >= 0 && _selectedSystemIndex < _systemChoices.Count)
                        ? _systemChoices[_selectedSystemIndex] : null;

            var state = new SystemSeedState { systemName = sys != null ? sys.systemName : "Unknown" };
            for (int i = 0; i < _planetNames.Count; i++)
            {
                state.planets.Add(new SystemSeedState.PlanetSeed
                {
                    planetName = _planetNames[i],
                    seed       = _planetSeeds[i],
                });
            }
            _session.chosenSystemName = state.systemName;
            _session.seedState        = state;
            _session.spawnPlanetIndex = _selectedSpawnPlanet;
            _session.orbitPace        = _newOrbitPace;
        }

        /// <summary>
        /// CLONE action: true save clone. Copies the selected world folder byte-for-byte
        /// into the next available "copy" name so the clone boots identically.
        /// </summary>
        private void CloneWorldAction(string sourceName)
        {
            string cloneName = NextCloneName(sourceName);
            string clonedPath = _session.CloneWorld(sourceName, cloneName);
            if (string.IsNullOrEmpty(clonedPath))
                _menuStatus = $"Error: Could not clone '{sourceName}'.";
            else
                _menuStatus = $"Cloned '{sourceName}' → '{cloneName}'.";
            BuildUI();
        }

        private string NextCloneName(string sourceName)
        {
            string baseName = SanitizeName(sourceName + " copy");
            if (!Directory.Exists(_session.WorldFolderPath(baseName))) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                string candidate = SanitizeName(sourceName + " copy " + i);
                if (!Directory.Exists(_session.WorldFolderPath(candidate))) return candidate;
            }
            return SanitizeName(sourceName + " copy " + DateTime.Now.ToString("yyyyMMddHHmmss"));
        }

        /// <summary>Builds the solar-system picker + per-planet seed editor block.</summary>
        private VisualElement BuildCosmosSection()
        {
            EnsureSystemChoicesLoaded();

            var box = new VisualElement();
            box.style.backgroundColor = new StyleColor(T.BgCard);
            T.Radius(box, T.CardRadius);
            T.Border(box, 1, T.BorderDim);
            box.style.paddingTop = 10; box.style.paddingBottom = 10;
            box.style.paddingLeft = 12; box.style.paddingRight = 12;

            var hdr = new Label("SOLAR SYSTEM");
            hdr.style.color = new StyleColor(T.TextPrimary);
            hdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            hdr.style.fontSize = 11;
            hdr.style.letterSpacing = 1f;
            hdr.style.marginBottom = 8;
            box.Add(hdr);

            // System picker — horizontal button row (on-brand, no version risk).
            var sysRow = new VisualElement();
            sysRow.style.flexDirection = FlexDirection.Row;
            sysRow.style.flexWrap = Wrap.Wrap;
            sysRow.style.marginBottom = 10;
            for (int i = 0; i < _systemChoices.Count; i++)
            {
                var sys = _systemChoices[i];
                string label = sys != null ? sys.systemName : "(none)";
                int captured = i;
                bool active = i == _selectedSystemIndex;
                var b = new Button(() =>
                {
                    if (_selectedSystemIndex == captured) return;
                    _selectedSystemIndex = captured;
                    RebuildPlanetSeedsForSystem(captured);
                    BuildUI();
                }) { text = label };
                b.style.minHeight = 28;
                b.style.minWidth = 80;
                b.style.marginRight = 5;
                b.style.marginBottom = 4;
                b.style.fontSize = 10;
                b.style.unityFontStyleAndWeight = FontStyle.Bold;
                b.style.color = Color.white;
                b.style.backgroundColor = new StyleColor(active
                    ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.85f)
                    : new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.85f));
                T.Radius(b, T.ButtonRadius);
                T.Border(b, 0, Color.clear);
                sysRow.Add(b);
            }
            box.Add(sysRow);

            // Per-planet seed editor.
            var plHdr = T.Muted("PER-PLANET SEEDS");
            plHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            plHdr.style.marginBottom = 6;
            box.Add(plHdr);

            for (int i = 0; i < _planetNames.Count; i++)
            {
                int captured = i;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 6;

                var name = new Label(_planetNames[i]);
                name.style.color = new StyleColor(T.TextSecondary);
                name.style.fontSize = 11;
                name.style.minWidth = 90;
                name.pickingMode = PickingMode.Ignore;
                row.Add(name);

                var field = new TextField { value = _planetSeeds[i].ToString() };
                StyleField(field);
                field.style.flexGrow = 1;
                field.RegisterValueChangedCallback(e =>
                {
                    if (int.TryParse(e.newValue, out var parsed)) _planetSeeds[captured] = parsed;
                });
                row.Add(field);

                var dice = BuildIconSmallButton(LucideIcons.Dice5, "", () =>
                {
                    _planetSeeds[captured] = SystemSeedState.RandomSeed();
                    field.SetValueWithoutNotify(_planetSeeds[captured].ToString());
                }, T.AccentTeal);
                dice.style.marginLeft = 6;
                row.Add(dice);

                box.Add(row);
            }

            // ── Spawn planet picker ──
            var spawnHdr = T.Muted("SPAWN PLANET");
            spawnHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            spawnHdr.style.marginTop = 10;
            spawnHdr.style.marginBottom = 6;
            box.Add(spawnHdr);

            if (_planetNames.Count > 1)
            {
                var spawnRow = new VisualElement();
                spawnRow.style.flexDirection = FlexDirection.Row;
                spawnRow.style.flexWrap = Wrap.Wrap;
                spawnRow.style.marginBottom = 8;
                for (int i = 0; i < _planetNames.Count; i++)
                {
                    int captured = i;
                    bool spawnActive = i == _selectedSpawnPlanet;
                    var pb = new Button(() => { _selectedSpawnPlanet = captured; BuildUI(); })
                        { text = _planetNames[i] };
                    pb.style.minHeight = 28;
                    pb.style.minWidth = 70;
                    pb.style.marginRight = 5;
                    pb.style.marginBottom = 4;
                    pb.style.fontSize = 10;
                    pb.style.unityFontStyleAndWeight = FontStyle.Bold;
                    pb.style.color = Color.white;
                    pb.style.backgroundColor = new StyleColor(spawnActive
                        ? new Color(T.AccentTeal.r, T.AccentTeal.g, T.AccentTeal.b, 0.85f)
                        : new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.85f));
                    T.Radius(pb, T.ButtonRadius);
                    T.Border(pb, 0, Color.clear);
                    spawnRow.Add(pb);
                }
                box.Add(spawnRow);
            }
            else
            {
                var onlyOne = T.Muted("Only one planet in this system.");
                box.Add(onlyOne);
            }

            // "Randomize all planets" button.
            var allBtn = BuildIconSmallButton(LucideIcons.Dice5, "RANDOMIZE ALL", () =>
            {
                for (int i = 0; i < _planetSeeds.Count; i++) _planetSeeds[i] = SystemSeedState.RandomSeed();
                BuildUI();
            }, T.AccentCyan);
            allBtn.style.marginTop = 4;
            allBtn.style.alignSelf = Align.FlexEnd;
            box.Add(allBtn);

            // ── Orbit pace: realistic periods vs fast arcade sweep ──
            var paceHdr = T.Muted("ORBIT PACE");
            paceHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            paceHdr.style.marginTop = 10;
            paceHdr.style.marginBottom = 6;
            box.Add(paceHdr);

            var paceRow = new VisualElement();
            paceRow.style.flexDirection = FlexDirection.Row;
            paceRow.style.marginBottom = 4;
            string[] paceNames = { "REALISTIC", "ARCADE" };
            for (int pi = 0; pi < paceNames.Length; pi++)
            {
                int captured = pi;
                bool paceActive = _newOrbitPace == captured;
                var paceBtn = new Button(() => { _newOrbitPace = captured; BuildUI(); })
                    { text = paceNames[captured] };
                paceBtn.style.minHeight = 28;
                paceBtn.style.minWidth = 110;
                paceBtn.style.marginRight = 5;
                paceBtn.style.fontSize = 10;
                paceBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                paceBtn.style.color = Color.white;
                paceBtn.style.backgroundColor = new StyleColor(paceActive
                    ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.85f)
                    : new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.85f));
                T.Radius(paceBtn, T.ButtonRadius);
                T.Border(paceBtn, 0, Color.clear);
                paceRow.Add(paceBtn);
            }
            box.Add(paceRow);
            var paceHelp = T.Muted("Realistic: true Keplerian periods — a year takes days. Arcade: planets sweep visibly (x120 orbital speed). Moons, craft and seasons keep normal time. Set at creation; stored per world.");
            paceHelp.style.marginTop = 2;
            box.Add(paceHelp);

            return box;
        }

        private void QuitGame()
        {
            Application.Quit();
        }

        // ── UI Helpers ─────────────────────────────────────────────
        private static VisualElement MakePanel(int w, int h)
        {
            var v = new VisualElement();
            if (w > 0)
            {
                v.style.width = new StyleLength(new Length(92f, LengthUnit.Percent));
                v.style.maxWidth = w;
                v.style.minWidth = Mathf.Min(320, w);
            }
            if (h > 0)
            {
                v.style.height = new StyleLength(new Length(88f, LengthUnit.Percent));
                v.style.maxHeight = h;
            }
            v.style.maxHeight = new StyleLength(new Length(92f, LengthUnit.Percent));
            v.style.overflow = Overflow.Hidden;
            v.style.paddingTop    = T.PanelPaddingV + 6;
            v.style.paddingBottom = T.PanelPaddingV + 6;
            v.style.paddingLeft   = T.PanelPaddingH + 4;
            v.style.paddingRight  = T.PanelPaddingH + 4;
            v.style.backgroundColor = new StyleColor(T.BgPanel);
            T.Radius(v, T.PanelRadius);
            T.Border(v, 1, T.BorderBright);
            // LCD chassis treatment: bezel, corner brackets, animated scanlines,
            // phosphor boot + wipe — every main-menu page inherits the same look.
            LcdHudTheme.UpgradePanel(v);
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

            // Micro-interactions: 1.03x hover / 0.98x press with 0.1s colour transitions.
            LcdHudTheme.AddMenuInteractions(b, bg, new Color(bg.r, bg.g, bg.b, 0.85f));
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

            LcdHudTheme.AddMenuInteractions(b, bg, new Color(bg.r, bg.g, bg.b, 0.85f));
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
            LcdHudTheme.AddMenuInteractions(b, T.AccentCyan,
                active ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.85f)
                       : new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.85f));
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

        // (Removed) IntegerField overload — seed input is now a TextField with
        // int parsing, so the typed overload above handles every styling caller.

        /// <summary>
        /// Forces every text-rendering descendant of a TextField / Slider
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
            ps.name = "MainMenu_RuntimePanelSettings";
            VoxelEngine.Settings.GameSettings.ApplyUiScaleAndFit(ps);

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
