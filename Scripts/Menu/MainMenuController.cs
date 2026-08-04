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

        private enum Page { Main, Saves, NewWorld, EditWorld, Settings }
        private Page _page = Page.Main;

        // New-world form values.
        private string _newName            = "MyWorld";
        private int    _newSeed            = 0;
        private int    _newMaxDroppedItems = WorldSession.DefaultMaxDroppedItems;
        private int    _newInventoryWeightPercent = WorldSession.DefaultInventoryWeightPercent;
        private int    _newContainerWeightPercent = WorldSession.DefaultContainerWeightPercent;
        private bool   _newShowDropVoidWarning = true;
        private bool   _newAllowRuinLootRespawn = WorldSession.DefaultAllowRuinLootRespawn;

        // Edit-world form values. Only non-generation settings are editable here.
        private string _editOriginalName = string.Empty;
        private string _editName = string.Empty;
        private int    _editMaxDroppedItems = WorldSession.DefaultMaxDroppedItems;
        private int    _editInventoryWeightPercent = WorldSession.DefaultInventoryWeightPercent;
        private int    _editContainerWeightPercent = WorldSession.DefaultContainerWeightPercent;
        private bool   _editShowDropVoidWarning = true;
        private bool   _editAllowRuinLootRespawn = WorldSession.DefaultAllowRuinLootRespawn;
        private string _menuStatus = string.Empty;
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
            _root.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            _root.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
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
                case Page.EditWorld: BuildEditWorldPage(); break;
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
        private void AudioTab(VisualElement p)    => SettingsUI.AudioTab(p, BuildUI);
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

            if (!_session.SaveWorldSettingsFor(finalName, _editMaxDroppedItems, _editInventoryWeightPercent, _editContainerWeightPercent, _editShowDropVoidWarning, _editAllowRuinLootRespawn))
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
