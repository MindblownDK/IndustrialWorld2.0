// Assets/Scripts/VoxelEngine/UI/GameUIController.cs
//
// Single in-game HUD/UI controller. Built entirely in code (UI Toolkit).
//
//   • Bottom hotbar (always on)
//   • Tab opens player inventory on the LEFT
//   • Looking at a chest/furnace/crafting bench and pressing RMB opens that container on the RIGHT
//   • Crafting recipes appear on the LEFT pane when no container is open OR when station is opened
//
// Layout (rough):
//   ┌──────────────────────────────────────────────────────┐
//   │ ┌─Inventory + Crafting─┐         ┌─Container/Recipes─┐│
//   │ │ [crafting] [items]   │         │  Recipe list       ││
//   │ │ slot grid            │         │  inputs / fuel /   ││
//   │ │                      │         │  output            ││
//   │ └──────────────────────┘         └────────────────────┘│
//   │                  HOTBAR (10 slots)                     │
//   └──────────────────────────────────────────────────────┘

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using VoxelEngine.Simulation;
using InputAction = VoxelEngine.Settings.InputAction;
using Cursor      = UnityEngine.Cursor;

namespace VoxelEngine.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class GameUIController : MonoBehaviour
    {
        public static GameUIController Instance { get; private set; }

        [Header("Refs")]
        public Inventory       inventory;
        public RecipeRegistry  recipeRegistry;
        [Tooltip("How far away the player can use a station to craft.")]
        public float stationRadius = 4f;

        // UI state
        private UIDocument _doc;
        private VisualElement _root;
        // Layered UI — created once in Awake, never cleared by Refresh():
        //   _contentLayer : hotbar + panels — rebuilt on every Refresh().
        //   _hudLayer     : vitals / interaction / feedback HUDs — PERSISTENT so
        //                   scrolling the hotbar or any container change no longer
        //                   destroys & recreates them (kills the visible HUD flash).
        //   _topLayer     : tooltip overlay — always rendered above both.
        private VisualElement _contentLayer;
        private VisualElement _hudLayer;
        private VisualElement _topLayer;
        private VisualElement _itemPortsOverlay;
        private bool _inventoryOpen;
        public bool IsInventoryOpen => _inventoryOpen;
        private IItemContainer _rightContainer; // chest contents OR furnace etc.
        private VoxelEngine.Building.Chest _openChest; // set when the right container is a Chest (drives Item Ports UI)
        private Furnace        _openFurnace;
        private ElectricFurnace _openElectric;
        private CraftQueue _activeQueue;
        private VoxelEngine.Power.CoalGeneratorFuel _openCoalGen;
        private VoxelEngine.Transport.Quarry _openQuarry;
        private VoxelEngine.Nuclear.ReactorCore _openReactor;
        private VoxelEngine.Nuclear.SteamTurbine _openTurbine;
        private VoxelEngine.Nuclear.PortableReactor _openPortReactor;
        private VoxelEngine.Nuclear.UraniumProcessor _openProcessor;
        private VoxelEngine.Nuclear.WasteReprocessor _openReprocessor;
        private VoxelEngine.Gas.Electrolyser _openElectrolyser;
        private VoxelEngine.Gas.HydrogenEngine _openHydroEngine;
        private VoxelEngine.Gas.GasTank _openGasTank;
        private VoxelEngine.Fluids.WaterPump _openWaterPump;
        private VoxelEngine.Building.Biofarm _openBiofarm;
        private VoxelEngine.Power.Wind.WindTurbineController _openWindTurbine;
        private VoxelEngine.GridSystem.GridBlock _openGridBlock;
        private VoxelEngine.GridSystem.GridEntity _openGridTerminal;
        private int _terminalTab; // -1 = All Storage, >=0 = index into the station list
        private VoxelEngine.Crafting.OilRefinery _openOilRefinery;
        private VoxelEngine.Industrial.StationaryChemicalPlant _openChemPlant;
        private VoxelEngine.Storage.StorageTerminal    _openStorageTerminal;
        private VoxelEngine.Storage.ServerRack         _openServerRack;
        private VoxelEngine.Storage.PatternTerminal    _openPatternTerminal;
        private VoxelEngine.Storage.CraftingTerminal   _openCraftTerminal;
        private VoxelEngine.Storage.StorageImporter    _openImporter;
        private VoxelEngine.Storage.StorageExporter    _openExporter;
        private VoxelEngine.Storage.DiskManipulator    _openDiskManipulator;
        private VoxelEngine.Storage.NASBlock           _openNAS;
        private VoxelEngine.Storage.Powerstation       _openPowerstation;
        private VoxelEngine.Storage.StorageDrawer      _openStorageDrawer;
        private VoxelEngine.Storage.StorageDrawerController _openDrawerController;
        private VoxelEngine.Storage.StorageItemDisplayBlock _openItemDisplay;
        private VoxelEngine.Simulation.Crusher _openCrusher;
        private VoxelEngine.Simulation.Assembler _openAssembler;
        private VoxelEngine.Simulation.Funnel _openFunnel;
        private VoxelEngine.Simulation.ConveyorSplitter _openSplitter;
        private IVoltageStation _openVoltageStation;
        private bool _productionStatsOpen;
        private bool _recipeBrowserOpen;

        // Containers whose OnChanged should call Refresh; cleared on each panel switch.
        private System.Collections.Generic.List<ItemContainer> _watchedContainers = new();

        // Crafting filter state — keyed by panel ID so left + right panels are independent.
        // Survives UI rebuilds (Refresh re-creates VisualElements but this dict persists).
        private readonly System.Collections.Generic.Dictionary<string, (string search, string category)> _browserState = new();
        // Persists scroll offsets across full Refresh() rebuilds (keyed by panelId).
        private readonly System.Collections.Generic.Dictionary<string, float> _browserScrollY = new();

        private (string search, string category) GetBrowserState(string panelId)
        {
            if (_browserState.TryGetValue(panelId, out var v)) return v;
            return ("", "All");
        }
        private void SetBrowserState(string panelId, string search, string category)
        {
            _browserState[panelId] = (search, category);
        }
        private CraftingStation _openStation;
        private VoxelEngine.Combat.ArmorUpgradeStation _openArmorUpgradeStation;
        private bool _previousLooking;

        // Drag-drop state
        private DragSource _dragSource;
        private VisualElement _dragGhost;
        private VisualElement _dropVoidOverlay;
        private VisualElement _tankTypeVoidOverlay;

        private struct DragSource
        {
            public IItemContainer container;
            public int            slotIndex;
            public ItemStack      stack;
            public bool active;
        }

        // ============================================================
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            UIState.Reset();
            Time.timeScale = 1f;
            _doc = GetComponent<UIDocument>();
            _doc.sortingOrder = 500;
            if (_doc.panelSettings == null)
                _doc.panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
            if (_doc.panelSettings != null)
            {
                _doc.panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
                _doc.panelSettings.scale = 1f;
                _doc.panelSettings.referenceDpi = 96f;
                _doc.panelSettings.fallbackDpi = 96f;
            }
            _root = _doc.rootVisualElement;
            _root.style.flexGrow = 1;
            // Pin the root to a DEFINITE full-screen size so absolutely-positioned children
            // (the ship terminal overlay, modals) resolve their bottom/right insets + 100%
            // heights correctly. flexGrow alone left the root height "auto" under some
            // PanelScaler configs, which collapsed the terminal to a single line.
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.top = 0; _root.style.right = 0; _root.style.bottom = 0;
            _root.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            _root.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            _root.pickingMode = PickingMode.Ignore;
            EnsureUiLayers();

            // Wire premium click/hover audio for the whole in-game UI in one place.
            VoxelEngine.FX.UiAudio.Attach(_root);

            // Keep input polling even if Unity isn't the foreground app — fixes "game
            // not focused" feeling on some Windows setups where the Editor steals focus.
            Application.runInBackground = true;
        }

        /// <summary>Creates the three persistent UI layers exactly once (idempotent).</summary>
        private void EnsureUiLayers()
        {
            if (_root == null) _root = _doc != null ? _doc.rootVisualElement : GetComponent<UIDocument>().rootVisualElement;
            if (_contentLayer != null && _contentLayer.parent == _root) return;
            _root.Clear();

            _contentLayer = MakeFullscreenLayer("ContentLayer");
            _hudLayer     = MakeFullscreenLayer("HudLayer");
            _topLayer     = MakeFullscreenLayer("TopLayer");
            _root.Add(_contentLayer);
            _root.Add(_hudLayer);
            _root.Add(_topLayer);

            static VisualElement MakeFullscreenLayer(string layerName)
            {
                var v = new VisualElement { name = layerName };
                v.style.position = Position.Absolute;
                v.style.left = 0; v.style.top = 0; v.style.right = 0; v.style.bottom = 0;
                v.pickingMode = PickingMode.Ignore;
                return v;
            }
        }

        private void Start()
        {
            if (inventory == null) inventory = FindAnyObjectByType<Inventory>();
            if (inventory != null)
            {
                if (inventory.container != null) inventory.container.OnChanged += Refresh;
                inventory.OnActiveSlotChanged += Refresh;
            }

            // Auto-find a RecipeRegistry if the inspector field was left blank
            // (e.g. the wizard spawned the player before you ran step 4 - Build Crafting Content).
            if (recipeRegistry == null)
            {
                recipeRegistry = Resources.Load<Crafting.RecipeRegistry>("RecipeRegistry");
            }
            if (recipeRegistry == null)
                Debug.LogWarning("[GameUI] No RecipeRegistry assigned — crafting will be empty. " +
                                 "Run Tools > Voxel Engine > Setup Wizard > Step 4 to create one, " +
                                 "then drag it into GameUI.recipeRegistry.");

            Refresh();
        }

        private void OnDestroy()
        {
            if (inventory != null)
            {
                if (inventory.container != null) inventory.container.OnChanged -= Refresh;
                inventory.OnActiveSlotChanged -= Refresh;
            }
            if (Instance == this) Instance = null;
        }

        // Live-updating references to furnace UI elements (set by BuildRightFurnace / BuildRightElectricFurnace).
        // We poke these from Update() so the flame pulses + bars tick without rebuilding the panel.
        private VisualElement _liveFlame;
        private VisualElement _liveSmeltFill;
        private VisualElement _liveFuelFill;
        private Label         _liveSmeltLabel;
        private Label         _liveFuelStat;
        private VisualElement _liveStatusPill;
        private Label         _liveStatusLabel;
        private Label         _liveWattLabel;
        // Jetpack bay live refs (set by BuildJetpackSlotsPanel; poked every frame so
        // the H₂ / power bars track flight drain WITHOUT a destructive rebuild).
        private VisualElement _jbStatusPill;
        private Label         _jbStatus;
        private VisualElement _jbH2Row, _jbH2Fill;
        private Label         _jbH2Label;
        private VisualElement _jbPRow, _jbPFill;
        private Label         _jbPLabel;
        // Per-pack live chips: H₂ ml + charge % for each equipped jetpack, kept in
        // sync every frame (hybrids show both pools). Rows sized to SlotCount.
        private VisualElement[] _jbPackRows;
        private Label[]         _jbPackH2;
        private Label[]         _jbPackPwr;
        // Battery panel live refs (set by BuildRightPowerBattery).
        private VisualElement[] _battSegments;
        private bool            _batterySweepPending;   // one-shot: play the power-on sweep on fresh open
        private Label         _battPct;
        private Label         _battChargeRow;
        private Label         _battInRow;
        private Label         _battOutRow;
        private Label         _battStatus;
        private Label         _battDockRow;
        private float         _battSegSmooth;    // eased segment fill (0..1)
        private float         _battDockIconAccum; // throttled dock-icon refresh while charging
        private float         _furnaceTickAccum;
        private float         _recipeRefreshAccum;
        private float         _machineRefreshAccum;
        private bool HasLivePanel()
        {
            if (_doc == null) _doc = GetComponent<UIDocument>();
            if (_doc == null) return false;
            if (_root == null || _root != _doc.rootVisualElement) _root = _doc.rootVisualElement;
            return _root != null && _root.panel != null;
        }

        private void Update()
        {
            if (!HasLivePanel()) return;

            // Keyboard capture state — suppress player movement/jetpack keys while typing.
            VoxelEngine.UI.UIState.TextInputActive = _searchHasFocus || RecipeBrowserUI.IsSearchFocused
                || VoxelEngine.Research.ResearchUI.IsSearchFocused
                || VoxelEngine.Maritime.MaritimeBlockUI.IsNumericInputFocused;

            // Live-update the open furnace panel in-place every frame (no rebuild needed).
            TickFurnaceLiveUI();
            // Live-update the jetpack bay bars + battery panel HUD (no rebuilds → no flash).
            TickJetpackBayLiveUI();
            TickBatteryLiveUI();
            PlayerHud.Tick();
            BombHud.Tick(inventory);
            PaintHud.Tick(inventory);
            VitalsHud.Tick();
            BuildFeedbackHud.Tick();
            VoxelEngine.Weather.WeatherHud.Tick();
            CryobedConfigHud.Tick();
            InteractionHud.Tick();
            WorldInspectionHud.Tick();
            GravityPullHud.Tick();
            VoxelEngine.GridSystem.GridPilotHud.Tick();
            GrinderHud.Tick();
            BuildCostHud.Tick();
            if (_openQuarry != null) QuarryHud.Tick(_openQuarry);
            // Periodic refresh for machine panels that need live updates (1 Hz).
            // SUSPENDED while a PortConfig dropdown is open — otherwise the
            // dropdown gets destroyed mid-click as the panel rebuilds.
            _machineRefreshAccum += Time.unscaledDeltaTime;
            bool liveMachineOpen =
                _openCoalGen != null || _openReactor != null || _openTurbine != null ||
                _openPortReactor != null || _openProcessor != null || _openReprocessor != null ||
                _openElectrolyser != null || _openHydroEngine != null || _openGasTank != null || _openWaterPump != null || _openWindTurbine != null ||
                _openOilRefinery != null || _openChemPlant != null ||
                _openGridBlock != null || _openGridTerminal != null;
            // 4 Hz so tank fills, wattage, charge %, recipe progress, etc. update smoothly.
            // BUT a full rebuild destroys the element the pointer is hovering / about to click,
            // which caused the terminal buttons to flash and "eat" the first click. So while the
            // cursor is over an interactive control (Button), defer the destructive refresh until
            // the pointer moves off it — hover + clicks then work first time.
            // GridBattery owns an in-place live panel now; rebuilding it on every power tick
            // was the source of the Auto / Recharge / Discharge button flashing report.
            bool liveGridBatteryPanel = _openGridBlock is VoxelEngine.GridSystem.GridBattery;
            // Master terminal controls are also interactive and must not be rebuilt under
            // battery mode buttons. Explicit terminal actions already refresh immediately.
            bool gridTerminalControlOpen = _openGridTerminal != null;
            if (liveGridBatteryPanel || gridTerminalControlOpen) _machineRefreshAccum = 0f;
            if (_machineRefreshAccum >= 0.25f && !liveGridBatteryPanel && !gridTerminalControlOpen
                && !PortConfigHud.IsAnyDropdownOpen && liveMachineOpen
                && !_dragSource.active && !PointerOverInteractiveUI()
                && !VoxelEngine.Maritime.MaritimeBlockUI.IsNumericInputFocused)
            { _machineRefreshAccum = 0f; Refresh(); }
            ResearchHud.Tick();
            TickUpgradePrompt();

            VoxelEngine.GridSystem.UI.BlockRotationHud.Tick();
            VoxelEngine.GridSystem.UI.ShipToolHud.Tick();

            // 4 Hz refresh while ANY craft queue near the player has work — drives recipe-row progress bars.
            if (_inventoryOpen && inventory != null)
            {
                bool anyWork = (_activeQueue != null && _activeQueue.HasWork);
                if (!anyWork)
                {
                    var nearest = FindNearestQueueForTier(Crafting.StationTier.CraftingBench, inventory.transform.position);
                    if (nearest != null && nearest.HasWork) anyWork = true;
                    if (!anyWork)
                    {
                        nearest = FindNearestQueueForTier(Crafting.StationTier.Assembler, inventory.transform.position);
                        if (nearest != null && nearest.HasWork) anyWork = true;
                    }
                }
                if (anyWork)
                {
                    _recipeRefreshAccum += Time.unscaledDeltaTime;
                    if (_recipeRefreshAccum >= 1.0f) { _recipeRefreshAccum = 0f; Refresh(); }
                }
            }

            // ---------- DRAG/DROP via direct mouse-button polling ----------
            UpdateDragDrop();

            // Hotbar number keys (1..9 then 0 = slot 9). Maps each digit to its action explicitly.
            CheckHotbarKey(InputAction.Hotbar1, 0);
            CheckHotbarKey(InputAction.Hotbar2, 1);
            CheckHotbarKey(InputAction.Hotbar3, 2);
            CheckHotbarKey(InputAction.Hotbar4, 3);
            CheckHotbarKey(InputAction.Hotbar5, 4);
            CheckHotbarKey(InputAction.Hotbar6, 5);
            CheckHotbarKey(InputAction.Hotbar7, 6);
            CheckHotbarKey(InputAction.Hotbar8, 7);
            CheckHotbarKey(InputAction.Hotbar9, 8);
            CheckHotbarKey(InputAction.Hotbar0, 9);
            CheckDropKey();
            // Hotbar wheel — only when no UI is open and no modifier is held (Ctrl/Shift
            // + wheel rotate the grid build ghost, so they must not also cycle the hotbar).
            bool ctrl = false, shift = false;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var kbWheel = UnityEngine.InputSystem.Keyboard.current;
            ctrl  = kbWheel != null && (kbWheel.leftCtrlKey.isPressed  || kbWheel.rightCtrlKey.isPressed);
            shift = kbWheel != null && (kbWheel.leftShiftKey.isPressed || kbWheel.rightShiftKey.isPressed);
            float wheel = UnityEngine.InputSystem.Mouse.current != null
                ? UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y : 0f;
#else
            ctrl  = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            shift = Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift);
            float wheel = Input.mouseScrollDelta.y;
#endif
            // Block hotbar cycling while a grid block is held + a modifier is down (rotation).
            bool rotatingBlock = VoxelEngine.GridSystem.GridBuilder.HoldingGridBlock && (ctrl || shift);
            bool piloting = VoxelEngine.GridSystem.GridCockpit.AnyPilotSeatActive; // control seats own scroll
            // Throttle: at most one slot change per Update, regardless of scroll-unit magnitude.
            if (!ctrl && !shift && !rotatingBlock && !piloting && !UIState.IsBlocking
                && !_inventoryOpen && _rightContainer == null && inventory != null && Mathf.Abs(wheel) > 0.01f)
            {
                int dir = wheel > 0 ? -1 : 1; // wheel up = previous slot, wheel down = next
                int next = inventory.activeHotbarIndex + dir;
                if (next < 0) next = Inventory.HOTBAR_SIZE - 1;
                else if (next >= Inventory.HOTBAR_SIZE) next = 0;
                inventory.SetActiveHotbar(next);
            }
            // Tick after both hotbar keys and wheel selection so the readout responds
            // on the same input frame rather than one frame later.
            HotbarItemNameHud.Tick(inventory);

            // Item Ports owns Escape before the underlying inventory and pause menu.
            // This remains available even while a port dropdown/filter has focus.
            bool itemPortsClosedThisFrame = false;
            bool pausePressed = GameSettings.WasPressed(InputAction.Pause);
            if (pausePressed && ItemFilterDialog.CloseActive())
            {
                UIState.PauseConsumedFrame = Time.frameCount;
                itemPortsClosedThisFrame = true;
            }
            else if (pausePressed && _itemPortsOverlay != null && _itemPortsOverlay.parent != null)
            {
                CloseItemPortsOverlay(refreshAfterClose: true);
                UIState.PauseConsumedFrame = Time.frameCount;
                itemPortsClosedThisFrame = true;
            }

            // While the search field has keyboard focus, don't react to hotkey-style keys
            // — the player is typing into the search box.
            bool typing = _searchHasFocus || RecipeBrowserUI.IsSearchFocused
                || VoxelEngine.Research.ResearchUI.IsSearchFocused
                || VoxelEngine.Maritime.MaritimeBlockUI.IsNumericInputFocused
                || PortConfigHud.IsAnyDropdownOpen;

            // Toggle inventory / ship terminal — but NOT while typing in a search/name field.
            bool weAreOpen = _inventoryOpen || _rightContainer != null || _openGridTerminal != null;
            if (!typing && GameSettings.WasPressed(InputAction.Inventory))
            {
                if (weAreOpen)
                {
                    // I closes the currently open inventory/container/ship terminal.
                    CloseAll();
                    UIState.PauseConsumedFrame = Time.frameCount;
                    _justClosedThisFrame = true;
                }
                else if (!UIState.IsBlocking && !_justClosedThisFrame)
                {
                    // While piloting a cockpit, "I" opens the master ship terminal
                    // instead of the player inventory (grid systems style).
                    var controlledGrid = VoxelEngine.GridSystem.GridCockpit.ActiveControlGrid;
                    if (controlledGrid != null) OpenGridTerminal(controlledGrid);
                    else OpenInventory();
                }
            }
            // Y also closes our UI when it is already open, but must not open research on
            // that same frame. ResearchUI checks PauseConsumedThisFrame before opening.
            else if (!typing && GameSettings.WasPressed(InputAction.Research) && weAreOpen)
            {
                CloseAll();
                UIState.PauseConsumedFrame = Time.frameCount;
                _justClosedThisFrame = true;
            }
            // Reset per-frame close guard each frame.
            else
            {
                _justClosedThisFrame = false;
            }
            // Esc closes our panels — and tells the pause menu we already handled Esc this frame.
            if (!itemPortsClosedThisFrame && !typing && pausePressed && weAreOpen)
            {
                CloseAll();
                UIState.PauseConsumedFrame = Time.frameCount;
            }

            // Tick custom tooltip overlay
            #if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            Vector2 mp = UnityEngine.InputSystem.Mouse.current != null
                ? UnityEngine.InputSystem.Mouse.current.position.ReadValue() : Vector2.zero;
            #else
            Vector2 mp = Input.mousePosition;
            #endif
            if (_inventoryOpen) Tooltip.Tick(mp, Screen.height, ProbeStackAt);
            else                Tooltip.Hide();

            // Drag follow — convert screen-pixel cursor to panel coordinates.
            if (_dragSource.active && _dragGhost != null && HasLivePanel())
            {
                Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(mp.x, Screen.height - mp.y));
                _dragGhost.style.left = panelPos.x - 24;
                _dragGhost.style.top  = panelPos.y - 24;
            }
        }

        // ============================================================
        //                       PUBLIC API
        // ============================================================
        public void OpenInventory()
        {
            if (!_inventoryOpen) UIState.PushBlock();
            // Replay the crafting screen's entrance pop each time the inventory opens.
            if (!_inventoryOpen) _craftPanelWasVisible = false;
            _inventoryOpen  = true;
            _openFurnace    = null;
            _openElectric   = null;
            _openCoalGen    = null;
            _openQuarry     = null;
            _openReactor    = null; _openTurbine     = null;
            _openPortReactor= null; _openProcessor   = null;
            _openReprocessor= null; _openElectrolyser= null; _openBiofarm = null;
            _openHydroEngine= null; _openGasTank = null; _openWaterPump = null; _openBiofarm = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _rightContainer = null; _openChest = null;
            _openStation    = null;
            _activeQueue    = null;

            // QoL: if the player has at least one online wireless transmitter,
            // pressing Inventory auto-opens the wireless storage panel beside the
            // inventory — exactly as if they had clicked a Storage Terminal —
            // so they don't have to walk back to a rack just to deposit items.
            // We clear all OTHER open-X first (above) so this never hijacks an
            // already-open machine UI; it only kicks in when the player pressed
            // I from the world.
            _openStorageTerminal = ResolveWirelessTerminal();
            _openServerRack      = null;
            _openPatternTerminal = null; _openCraftTerminal   = null;
            _openImporter        = null; _openExporter        = null;
            _openDiskManipulator = null; _openNAS             = null;
            _openPowerstation = null; _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;

            // Normalize + auto-refuel equipped jetpacks BEFORE building the UI:
            // pulling H₂/charge from portable containers mutates the inventory and
            // must never happen mid-build (re-entering Refresh mangled slot grids).
            inventory?.GetComponent<VoxelEngine.Player.PlayerEquipment>()?.EnsureAllJetpackFuelInitialized();

            UnlockCursor();
            Refresh();
        }

        public void OpenRecipeBrowserFor(ItemDefinition item)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            RecipeBrowserUI.FocusItem(item);
            _inventoryOpen = true;
            _recipeBrowserOpen = true;
            _productionStatsOpen = false;
            CraftingScreen.Visible = false;
            _rightContainer = null; _openChest = null;
            _openFurnace = null; _openElectric = null; _openCoalGen = null; _openStation = null; _openQuarry = null;
            _openReactor = null; _openTurbine = null; _openPortReactor = null; _openProcessor = null; _openReprocessor = null;
            _openElectrolyser = null; _openHydroEngine = null; _openGasTank = null; _openWaterPump = null; _openBiofarm = null; _openWindTurbine = null;
            _openGridBlock = null; _openGridTerminal = null; _openOilRefinery = null; _openChemPlant = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null; _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null; _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            UnlockCursor();
            Refresh();
        }

        // Cached synthetic terminal so we don't allocate one each frame.
        private VoxelEngine.Storage.StorageTerminal _wirelessTerminalProxy;

        // ── Wireless transmitter selection ─────────────────────────
        // Player can pick which online transmitter to route through (a dropdown
        // appears in the inventory panel whenever more than one is online).
        // Persisted across sessions via PlayerPrefs by the transmitter's name.
        private const string _wirelessTxPrefKey = "iw.wireless.selectedTx";
        private string _selectedTransmitterName;   // null = "Auto" (nearest)

        /// <summary>Returns the rack the player currently wants to use, honouring
        /// their dropdown selection. Falls back to the nearest online transmitter
        /// when "Auto" is selected (the default) or the named one went offline.</summary>
        public VoxelEngine.Storage.ServerRack GetActiveWirelessRack()
        {
            var tx = GetActiveWirelessTransmitter();
            return tx != null ? tx.ConnectedRack : null;
        }

        public VoxelEngine.Storage.WirelessTransmitter GetActiveWirelessTransmitter()
        {
            var all = VoxelEngine.Storage.WirelessTransmitter.GetAllOnline();
            if (all == null || all.Length == 0) return null;

            if (_selectedTransmitterName == null)
                _selectedTransmitterName = PlayerPrefs.GetString(_wirelessTxPrefKey, "");

            // Match by name when one was chosen. Empty string = "Auto" (nearest).
            if (!string.IsNullOrEmpty(_selectedTransmitterName))
            {
                foreach (var t in all)
                    if (t != null && t.transmitterName == _selectedTransmitterName && t.ConnectedRack != null)
                        return t;
                // Selected one went offline → silently fall through to Auto.
            }

            // Auto: pick the closest online transmitter that has a rack.
            VoxelEngine.Storage.WirelessTransmitter best = null;
            float bestSqr = float.MaxValue;
            Vector3 origin = inventory != null ? inventory.transform.position : Vector3.zero;
            foreach (var t in all)
            {
                if (t == null || t.ConnectedRack == null) continue;
                float d = (t.transform.position - origin).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = t; }
            }
            return best;
        }

        public void SetSelectedTransmitter(string nameOrEmpty)
        {
            _selectedTransmitterName = nameOrEmpty ?? "";
            PlayerPrefs.SetString(_wirelessTxPrefKey, _selectedTransmitterName);
            PlayerPrefs.Save();
            Refresh();
        }

        /// <summary>
        /// Returns a "virtual" StorageTerminal pointed at the nearest online
        /// wireless transmitter's connected ServerRack, or null if no transmitter
        /// is online. Used by OpenInventory() so plain I auto-opens the storage
        /// network panel for players carrying a powered wireless transmitter.
        /// </summary>
        private VoxelEngine.Storage.StorageTerminal ResolveWirelessTerminal()
        {
            // Honour the player's dropdown selection (or Auto = nearest).
            var best = GetActiveWirelessTransmitter();
            if (best == null) return null;

            // Reuse a single hidden proxy so the StorageUI thinks it's looking
            // at a real wired terminal — but pre-wired to the wireless rack.
            if (_wirelessTerminalProxy == null)
            {
                var go = new GameObject("WirelessTerminalProxy");
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.SetParent(transform, false);
                // StorageTerminal requires a PlacedBlock — add a stub.
                go.AddComponent<VoxelEngine.Building.PlacedBlock>();
                _wirelessTerminalProxy = go.AddComponent<VoxelEngine.Storage.StorageTerminal>();
                _wirelessTerminalProxy.isWireless = true;
            }
            // Force-set the connected rack via reflection-free path: the proxy's
            // own Update() would re-search by distance; instead we drop it next
            // to the transmitter so its built-in search picks the right rack.
            _wirelessTerminalProxy.transform.position = best.transform.position;
            return _wirelessTerminalProxy;
        }
        public void OpenContainer(IItemContainer c) => OpenContainer(c, null);

        /// <summary>
        /// Open a generic container on the right. When <paramref name="owningChest"/>
        /// is supplied, the panel also renders the chest's advanced Item-Port
        /// configuration (per-face direction + item filters).
        /// </summary>
        public void OpenContainer(IItemContainer c, VoxelEngine.Building.Chest owningChest)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            _rightContainer = c;
            _openChest      = owningChest;
            _inventoryOpen  = true;
            _openFurnace    = null;
            _openElectric   = null;
            _openCoalGen    = null;
            _openQuarry     = null;
            _openReactor    = null; _openTurbine     = null;
            _openPortReactor= null; _openProcessor   = null;
            _openReprocessor= null; _openElectrolyser= null; _openBiofarm = null;
            _openHydroEngine= null; _openGasTank = null; _openWaterPump = null; _openBiofarm = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _openStation    = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null; _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openDefense = null;
            _openPowerBattery = null;
            UnwatchAllContainers();
            if (c is ItemContainer ic) WatchContainer(ic);
            UnlockCursor();
            Refresh();
        }
        public void OpenFurnace(Furnace f)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            _openFurnace    = f;
            _openElectric   = null;
            _openQuarry     = null;
            _openReactor    = null; _openTurbine     = null;
            _openPortReactor= null; _openProcessor   = null;
            _openReprocessor= null; _openElectrolyser= null; _openBiofarm = null;
            _openHydroEngine= null; _openGasTank = null; _openWaterPump = null; _openBiofarm = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _rightContainer = null; _openChest = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null; _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openDefense = null;
            _openPowerBattery = null;
            _openStation    = f.GetComponent<CraftingStation>();
            _inventoryOpen  = true;
            UnwatchAllContainers();
            if (f != null) { f.EnsureContainers(); WatchContainer(f.inputC); WatchContainer(f.fuelC); WatchContainer(f.outputC); }
            UnlockCursor();
            Refresh();
        }

        public void OpenElectricFurnace(ElectricFurnace ef)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            _openElectric   = ef;
            _openFurnace    = null;
            _openQuarry     = null;
            _openReactor    = null; _openTurbine     = null;
            _openPortReactor= null; _openProcessor   = null;
            _openReprocessor= null; _openElectrolyser= null; _openBiofarm = null;
            _openHydroEngine= null; _openGasTank = null; _openWaterPump = null; _openBiofarm = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _rightContainer = null; _openChest = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null; _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openDefense = null;
            _openPowerBattery = null;
            _openStation    = ef.GetComponent<CraftingStation>();
            _inventoryOpen  = true;
            UnwatchAllContainers();
            if (ef != null) { ef.EnsureContainers(); WatchContainer(ef.inputC); WatchContainer(ef.outputC); WatchContainer(ef.upgradeC); }
            UnlockCursor();
            Refresh();
        }
        public void OpenCoalGenerator(VoxelEngine.Power.CoalGeneratorFuel fuel)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            _openCoalGen    = fuel;
            _openFurnace    = null; _openElectric = null;
            _openQuarry     = null;
            _openReactor    = null; _openTurbine     = null;
            _openPortReactor= null; _openProcessor   = null;
            _openReprocessor= null; _openElectrolyser= null; _openBiofarm = null;
            _openHydroEngine= null; _openGasTank = null; _openWaterPump = null; _openBiofarm = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _rightContainer = null; _openChest = null; _openStation = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null; _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openDefense = null;
            _openPowerBattery = null;
            _inventoryOpen  = true;
            UnwatchAllContainers();
            if (fuel != null) { fuel.EnsureContainers(); WatchContainer(fuel.fuelC); }
            UnlockCursor();
            Refresh();
        }

        public void OpenQuarry(VoxelEngine.Transport.Quarry quarry)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            _openQuarry     = quarry;
            _openFurnace    = null; _openElectric = null;
            _openCoalGen    = null;
            _rightContainer = null; _openChest = null; _openStation = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null; _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openDefense = null;
            _openPowerBattery = null;
            _inventoryOpen  = true;
            UnwatchAllContainers();
            if (quarry != null) { quarry.EnsureOutputPublic(); quarry.EnsureUpgrades(); WatchContainer(quarry.Output); WatchContainer(quarry.upgradeC); }
            UnlockCursor();
            Refresh();
        }

        /// <summary>Generic opener for all new machine types.</summary>
        public void OpenMachine(MonoBehaviour machine)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            _openFurnace = null; _openElectric = null; _openCoalGen = null;
            _rightContainer = null; _openChest = null; _openStation = null; _openQuarry = null;
            _openReactor = null; _openTurbine = null; _openPortReactor = null;
            _openProcessor = null; _openReprocessor = null; _openElectrolyser = null; _openBiofarm = null;
            _openHydroEngine = null; _openGasTank = null; _openWaterPump = null; _openBiofarm = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _openStorageTerminal = null; _openServerRack = null;
            _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null;
            _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openVoltageStation = null;
            _openDefense = null;
            _openPowerBattery = null;
            _inventoryOpen = true;
            UnwatchAllContainers();

            if (machine is IVoltageStation vs)
            {
                _openVoltageStation = vs;
            }

            switch (machine)
            {
                case VoxelEngine.Nuclear.ReactorCore r:
                    _openReactor = r; r.EnsureContainers();
                    WatchContainer(r.fuelC); WatchContainer(r.spentC); break;
                case VoxelEngine.Nuclear.SteamTurbine t: _openTurbine = t; break;
                case VoxelEngine.Nuclear.PortableReactor pr:
                    _openPortReactor = pr; pr.EnsureContainers();
                    WatchContainer(pr.fuelC); WatchContainer(pr.iceC); WatchContainer(pr.wasteC); break;
                case VoxelEngine.Nuclear.UraniumProcessor up:
                    _openProcessor = up; up.EnsureContainers();
                    WatchContainer(up.inputC); WatchContainer(up.enrichedOutputC); WatchContainer(up.wasteOutputC); break;
                case VoxelEngine.Nuclear.WasteReprocessor wr:
                    _openReprocessor = wr; wr.EnsureContainers();
                    WatchContainer(wr.inputC); WatchContainer(wr.outputC); WatchContainer(wr.wasteOutputC); break;
                case VoxelEngine.Gas.Electrolyser el:
                    _openElectrolyser = el; el.EnsureContainers();
                    WatchContainer(el.iceInputC); break;
                case VoxelEngine.Gas.HydrogenEngine he: _openHydroEngine = he; break;
                case VoxelEngine.Gas.GasTank gt: _openGasTank = gt; gt.EnsureContainers(); WatchContainer(gt.PortableSlot); break;
                case VoxelEngine.Power.PowerBattery pb:
                    if (_openPowerBattery != pb) _batterySweepPending = true;   // fresh open → power-on sweep
                    _openPowerBattery = pb;
                    pb.EnsureContainers();
                    WatchContainer(pb.ChargeSlot);
                    break;
                case VoxelEngine.Fluids.WaterPump wp: _openWaterPump = wp; wp.ScanSource(); break;
                case VoxelEngine.Building.Biofarm bf:
                    _openBiofarm = bf; bf.EnsureContainers();
                    WatchContainer(bf.biomassInput); break;
                case VoxelEngine.Power.Wind.WindTurbineController wt: _openWindTurbine = wt; break;
                case VoxelEngine.Simulation.Crusher crusher:
                    _openCrusher = crusher; crusher.EnsureContainers();
                    WatchContainer(crusher.inputC); WatchContainer(crusher.outputC); WatchContainer(crusher.upgradeC); break;
                case VoxelEngine.Simulation.Assembler assembler:
                    _openAssembler = assembler; assembler.EnsureContainers();
                    WatchContainer(assembler.inputC); WatchContainer(assembler.outputC); WatchContainer(assembler.upgradeC); break;
                case VoxelEngine.Simulation.Funnel funnel:
                    _openFunnel = funnel;
                    break;
                case VoxelEngine.Simulation.ConveyorSplitter splitter:
                    _openSplitter = splitter;
                    break;
                case VoxelEngine.Crafting.OilRefinery orf:
                    _openOilRefinery = orf; orf.EnsureContainers();
                    WatchContainer(orf.inputC); WatchContainer(orf.outputC); WatchContainer(orf.upgradeC); break;
                case VoxelEngine.Industrial.StationaryChemicalPlant scp:
                    _openChemPlant = scp; scp.EnsureContainers();
                    WatchContainer(scp.inputC); WatchContainer(scp.outputC); break;
                case VoxelEngine.GridSystem.GridBlock gb:
                    _openGridBlock = gb;
                    // Watch the container(s) the block exposes so the panel auto-refreshes.
                    if (gb is VoxelEngine.GridSystem.GridCargoContainer gcc) { if (gcc.container == null) gcc.OnPlaced(); WatchContainer(gcc.container); }
                    else if (gb is VoxelEngine.GridSystem.GridH2O2Generator gh2) { if (gh2.iceInput == null) gh2.OnPlaced(); WatchContainer(gh2.iceInput); }
                    else if (gb is VoxelEngine.GridSystem.GridBiofarm gbf) { if (gbf.biomassInput == null) gbf.OnPlaced(); WatchContainer(gbf.biomassInput); }
                    else if (gb is VoxelEngine.GridSystem.GridWeapon gw) { if (gw.ammo == null) gw.OnPlaced(); WatchContainer(gw.ammo); }
                    else if (gb is VoxelEngine.GridSystem.GridDockingPort gdp) { if (gdp.container == null) gdp.OnPlaced(); WatchContainer(gdp.container); }
                    else if (gb is VoxelEngine.GridSystem.GridPortableReactor gpr) { if (gpr.fuelC == null) gpr.OnPlaced(); WatchContainer(gpr.fuelC); WatchContainer(gpr.iceC); WatchContainer(gpr.wasteC); }
                    else if (gb is VoxelEngine.GridSystem.GridDrill gdr) { if (gdr.buffer == null) gdr.OnPlaced(); WatchContainer(gdr.buffer); }
                    else if (gb is VoxelEngine.GridSystem.GridElectricFurnace gef) { if (gef.inputC == null) gef.OnPlaced(); WatchContainer(gef.inputC); WatchContainer(gef.outputC); }
                    else if (gb is VoxelEngine.GridSystem.GridGasTank ggt) { ggt.EnsureContainers(); WatchContainer(ggt.PortableSlot); }
                    else if (gb is VoxelEngine.GridSystem.GridBattery gridBattery) { gridBattery.EnsureContainers(); WatchContainer(gridBattery.ChargeSlot); }
                    break;
                case VoxelEngine.Storage.StorageTerminal st2: _openStorageTerminal = st2; break;
                case VoxelEngine.Storage.PatternTerminal pt2: _openPatternTerminal = pt2; break;
                case VoxelEngine.Storage.CraftingTerminal ct2: _openCraftTerminal = ct2; break;
                case VoxelEngine.Storage.StorageExporter se:
                    _openExporter = se; se.EnsureContainers();
                    WatchContainer(se.upgradeSlots); break;
                case VoxelEngine.Storage.StorageImporter si:
                    _openImporter = si; si.EnsureContainers();
                    WatchContainer(si.upgradeSlots); break;
                case VoxelEngine.Storage.DiskManipulator dm:
                    _openDiskManipulator = dm; dm.EnsureContainers();
                    WatchContainer(dm.sourceSlot); WatchContainer(dm.destSlot); break;
                case VoxelEngine.Storage.NASBlock nb:
                    _openNAS = nb;
                    WatchContainer(nb.diskSlots); break;
                case VoxelEngine.Storage.Powerstation ps:
                    _openPowerstation = ps; ps.EnsureContainers();
                    WatchContainer(ps.psuSlots); break;
                case VoxelEngine.Storage.StorageDrawer sd:
                    _openStorageDrawer = sd; sd.EnsureContainers();
                    WatchContainer(sd.upgradeSlots); break;
                case VoxelEngine.Storage.StorageDrawerController dc:
                    _openDrawerController = dc; dc.RefreshLinks(); break;
                case VoxelEngine.Storage.StorageItemDisplayBlock dib:
                    _openItemDisplay = dib; break;
                case VoxelEngine.Storage.ServerRack sr:
                    _openServerRack = sr; sr.EnsureContainers();
                    WatchContainer(sr.diskSlots); WatchContainer(sr.ramSlots);
                    WatchContainer(sr.cpuSlot); WatchContainer(sr.psuSlot); break;
            }
            UnlockCursor();
            Refresh();
        }

        /// <summary>Open the grid-terminal master terminal for a whole grid:
        /// a tabbed view of every station + an All-Storage tab spanning all containers.</summary>
        public void OpenGridTerminal(VoxelEngine.GridSystem.GridEntity grid)
        {
            if (grid == null) return;
            if (!_inventoryOpen) UIState.PushBlock();
            // Clear all other open targets.
            _openFurnace = null; _openElectric = null; _openCoalGen = null;
            _rightContainer = null; _openChest = null; _openStation = null; _openQuarry = null;
            _openReactor = null; _openTurbine = null; _openPortReactor = null;
            _openProcessor = null; _openReprocessor = null; _openElectrolyser = null; _openBiofarm = null;
            _openHydroEngine = null; _openGasTank = null; _openWaterPump = null; _openBiofarm = null; _openWindTurbine = null; _openGridBlock = null;
            _openOilRefinery = null; _openChemPlant = null;
            _openStorageTerminal = null; _openServerRack = null;
            _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null;
            _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openDefense = null;
            _openPowerBattery = null;
            _openGridTerminal = grid; _terminalTab = -1;
            _inventoryOpen = true;
            UnwatchAllContainers();
            foreach (var block in grid.AllBlocks)
            {
                if (block is VoxelEngine.GridSystem.GridBattery gridBattery)
                {
                    gridBattery.EnsureContainers();
                    WatchContainer(gridBattery.ChargeSlot);
                }
            }
            UnlockCursor();
            Refresh();
        }

        private void ClearArmorUpgradeStationBinding()
        {
            if (_openArmorUpgradeStation != null)
                _openArmorUpgradeStation.OnStateChanged -= Refresh;
            _openArmorUpgradeStation = null;
        }

        public void OpenArmorUpgradeStation(VoxelEngine.Combat.ArmorUpgradeStation station)
        {
            if (station == null) return;
            if (!_inventoryOpen) UIState.PushBlock();

            ClearArmorUpgradeStationBinding();
            _openArmorUpgradeStation = station;
            _openArmorUpgradeStation.OnStateChanged += Refresh;
            _openStation = null;
            _activeQueue = null;
            _rightContainer = null; _openChest = null;
            _openFurnace = null; _openElectric = null; _openCoalGen = null;
            _openQuarry = null; _openReactor = null; _openTurbine = null;
            _openPortReactor = null; _openProcessor = null; _openReprocessor = null;
            _openElectrolyser = null; _openBiofarm = null; _openHydroEngine = null;
            _openGasTank = null; _openWaterPump = null; _openWindTurbine = null;
            _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null;
            _openGridTerminal = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null;
            _openCraftTerminal = null; _openImporter = null; _openExporter = null;
            _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openDefense = null; _openPowerBattery = null; _openVoltageStation = null;
            _inventoryOpen = true;
            UnwatchAllContainers();
            WatchContainer(station.ArmorSlot);
            WatchContainer(station.ModuleSlot);
            WatchContainer(station.OutputSlot);
            UnlockCursor();
            Refresh();
        }

        public void OpenStation(CraftingStation st)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            ClearArmorUpgradeStationBinding();
            _openStation    = st;
            _rightContainer = null; _openChest = null;
            _openFurnace    = null;
            _openElectric   = null;
            _openCoalGen    = null;
            _openQuarry     = null;
            _openReactor    = null; _openTurbine     = null;
            _openPortReactor= null; _openProcessor   = null;
            _openReprocessor= null; _openElectrolyser= null; _openBiofarm = null;
            _openHydroEngine= null; _openGasTank = null; _openWaterPump = null; _openBiofarm = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null; _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openDefense = null;
            _openPowerBattery = null;
            _inventoryOpen  = true;
            // Lazy-create a queue on the station so progress survives panel closure/reopen.
            _activeQueue    = st.GetComponent<CraftQueue>();
            if (_activeQueue == null) _activeQueue = st.gameObject.AddComponent<CraftQueue>();
            _activeQueue.OnChanged -= Refresh; _activeQueue.OnChanged += Refresh;
            UnlockCursor();
            Refresh();
        }
        public void CloseAll()
        {
            CloseItemPortsOverlay();
            CloseDropVoidOverlay();
            CloseTankTypeVoidConfirmation();
            if (_inventoryOpen) UIState.PopBlock();
            _inventoryOpen  = false;
            _rightContainer = null; _openChest = null;
            _openFurnace    = null;
            _openElectric   = null;
            _openCoalGen    = null;
            _openStation    = null;
            ClearArmorUpgradeStationBinding();
            _openQuarry     = null;
            _openReactor    = null; _openTurbine      = null;
            _openPortReactor= null; _openProcessor    = null;
            _openReprocessor= null; _openElectrolyser = null;
            _openHydroEngine= null; _openGasTank = null; _openWaterPump = null; _openBiofarm = null; _openWindTurbine = null;
            _openGridBlock  = null; _openGridTerminal = null;
            _openOilRefinery = null; _openChemPlant = null;
            _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter   = null; _openExporter     = null;
            _openDiskManipulator = null; _openNAS     = null;
            _openPowerstation= null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openStorageTerminal = null; _openServerRack = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openDefense = null;
            _openPowerBattery = null;
            _openVoltageStation = null;
            _productionStatsOpen = false;
            _recipeBrowserOpen = false;
            _activeQueue    = null;
            _openCoalGen    = null;
            UnwatchAllContainers();
            CancelDrag();   // drop the held item back into source slot if user closes mid-drag
            CancelDrag();
            RelockCursor();
            Refresh();
        }

        // ============================================================
        //                          BUILD UI
        // ============================================================

        private void WatchContainer(ItemContainer c)
        {
            if (c == null) return;
            c.OnChanged -= Refresh;
            c.OnChanged += Refresh;
            _watchedContainers.Add(c);
        }
        private void UnwatchAllContainers()
        {
            foreach (var c in _watchedContainers) { if (c != null) c.OnChanged -= Refresh; }
            _watchedContainers.Clear();
        }

        public void RequestRefresh()
        {
            Refresh();
        }

        // ── Refresh re-entrancy guard ──────────────────────────────────────────
        // Building the panel can MUTATE containers (jetpack auto-refuel pulls gas
        // from portable tanks; docking bumps raise OnChanged). If those mutations
        // re-entered Refresh mid-build, the old tree was cleared UNDER the still-
        // running build → half-built panels (missing slots). Any nested Refresh is
        // deferred to the next frame instead — always converging, never corrupting.
        private bool _refreshing;
        private bool _refreshQueued;

        private void Refresh()
        {
            // Container changes can arrive while logistics is moving items. Keep the
            // modal tree mounted instead of clearing it underneath the player; the
            // latest container state is rebuilt immediately when the overlay closes.
            if (_itemPortsOverlay != null && _itemPortsOverlay.parent != null) return;
            if (_dropVoidOverlay != null && _dropVoidOverlay.parent != null) return;
            if (_refreshing)
            {
                _refreshQueued = true;
                if (_root != null && _root.panel != null)
                    _root.schedule.Execute(() =>
                    {
                        if (!_refreshQueued) return;
                        _refreshQueued = false;
                        Refresh();
                    });
                else _refreshQueued = false;
                return;
            }
            _refreshing = true;
            try
            {
                RefreshInternal();
            }
            finally { _refreshing = false; }
        }

        private void RefreshInternal()
        {
            if (_itemPortsOverlay != null)
            {
                _itemPortsOverlay = null;
                PortConfigHud.IsAnyDropdownOpen = false;
            }
            if (_dropVoidOverlay != null) _dropVoidOverlay = null;
            if (_searchHasFocus) return;
            WorldInspectionHud.ClearInventoryHover();

            // Clear stale references — the elements they point to are about to be destroyed.
            _liveFlame = null; _liveSmeltFill = null; _liveFuelFill = null;
            _liveSmeltLabel = null; _liveFuelStat = null;
            _liveStatusPill = null; _liveStatusLabel = null; _liveWattLabel = null;
            _jbStatus = null; _jbStatusPill = null;
            _jbH2Row = null; _jbH2Fill = null; _jbH2Label = null;
            _jbPRow = null; _jbPFill = null; _jbPLabel = null;
            _battPct = null; _battChargeRow = null; _battInRow = null; _battOutRow = null;
            _battStatus = null; _battDockRow = null; _battSegments = null;

            EnsureUiLayers();

            // ONLY the content layer is rebuilt — the HUD + tooltip layers persist
            // across refreshes so vitals/HUD never flash on scroll or container ticks.
            _contentLayer.Clear();
            _contentLayer.pickingMode = PickingMode.Ignore;
            _hudLayer.pickingMode = PickingMode.Ignore;
            if (inventory == null) return;

            // (Re)mount the tooltip + every HUD — they no-op when already mounted,
            // which is exactly what keeps them flash-free.
            Tooltip.EnsureMounted(_topLayer);
            PlayerHud.EnsureMounted(_hudLayer);
            RecipePinHud.EnsureMounted(_hudLayer);
            ResearchHud.EnsureMounted(_hudLayer);
            UpgradePromptHud.EnsureMounted(_hudLayer);

            VoxelEngine.GridSystem.UI.BlockRotationHud.EnsureMounted(_hudLayer);
            VoxelEngine.GridSystem.UI.ShipToolHud.EnsureMounted(_hudLayer);
            VitalsHud.EnsureMounted(_hudLayer);
            InteractionHud.EnsureMounted(_hudLayer);
            HotbarItemNameHud.EnsureMounted(_hudLayer);
            WorldInspectionHud.EnsureMounted(_hudLayer);
            BuildFeedbackHud.EnsureMounted(_hudLayer);
            VoxelEngine.Weather.WeatherHud.EnsureMounted(_hudLayer);
            GravityPullHud.EnsureMounted(_hudLayer);
            VoxelEngine.GridSystem.GridPilotHud.EnsureMounted(_hudLayer);
            GrinderHud.EnsureMounted(_hudLayer);
            BuildCostHud.EnsureMounted(_hudLayer);
            DeathScreenHud.EnsureMounted(_hudLayer);
            CryobedConfigHud.EnsureMounted(_hudLayer);
            BombHud.EnsureMounted(_hudLayer);
            PaintHud.EnsureMounted(_hudLayer);

            // (We poll mouse buttons in Update() — much more reliable than RegisterCallback.)

            // Hotbar (always on)
            BuildHotbar(_contentLayer);

            if (_inventoryOpen)
            {
                _root.pickingMode = PickingMode.Position;
                _root.style.backgroundColor = new StyleColor(new Color(0,0,0,0.55f));

                // Left area — player inventory, with the ARMOR equipment panel docking
                // to its right when no crafting/container/stats panel occupies the space.
                BuildLeftArea(_contentLayer);

                // ── MASTER SHIP TERMINAL — rendered FIRST (lowest z-order) so the
                // inventory stays on the left and, crucially, the crafting screen
                // (built afterwards) draws ON TOP of it when toggled. ──
                if (_openGridTerminal != null)
                {
                    // Full-screen overlay (same pattern as the working Item-Ports modal): pin all
                    // four edges to 0 so it ALWAYS has a definite size, then the terminal card
                    // grows inside it via flexGrow. This is immune to PanelScaler inset quirks
                    // that were collapsing the terminal to a single line.
                    var overlay = new VisualElement();
                    overlay.style.position = Position.Absolute;
                    overlay.style.left = 0; overlay.style.top = 0;
                    overlay.style.right = 0; overlay.style.bottom = 0;
                    overlay.style.flexDirection = FlexDirection.Row;
                    overlay.pickingMode = PickingMode.Ignore;

                    // Left gutter reserves room for the inventory panel; the terminal fills the rest.
                    var gutter = new VisualElement();
                    gutter.style.width = new StyleLength(new Length(34f, LengthUnit.Percent)); gutter.style.maxWidth = 492; gutter.style.flexShrink = 1;
                    gutter.pickingMode = PickingMode.Ignore;
                    overlay.Add(gutter);

                    var spacer = new VisualElement();
                    spacer.style.flexGrow = 1;
                    spacer.pickingMode = PickingMode.Ignore;
                    overlay.Add(spacer);

                    var card = VoxelEngine.GridSystem.UI.GridMasterTerminal.Build(
                        _openGridTerminal, _terminalTab,
                        t => { _terminalTab = t; Refresh(); }, BuildSlot,
                        () => CloseAll());
                    // Right-side desktop-app layout: fixed/readable width, pushed to the
                    // screen's right edge instead of stretching across big monitors.
                    card.style.flexGrow = 0;
                    card.style.flexShrink = 0;
                    card.style.width = new StyleLength(new Length(64f, LengthUnit.Percent));
                    card.style.maxWidth = 1180;
                    card.style.minWidth = 420;
                    // Explicit height too (root is now a definite full-screen size) so the
                    // terminal never collapses to its title bar.
                    card.style.height = new StyleLength(new Length(96, LengthUnit.Percent));
                    card.style.marginTop = 12; card.style.marginBottom = 12; card.style.marginRight = 12;
                    overlay.Add(card);

                    _contentLayer.Add(overlay);
                }

                // Center panel — crafting screen (toggle-driven,
                // state persisted). Opening any real machine/container/crafting
                // surface owns the right side and automatically closes the live
                // production stats dashboard so stale dashboards never cover the
                // requested machine UI.
                bool anyRightTargetOpen =
                    _rightContainer != null || _openFurnace != null || _openElectric != null ||
                    _openCoalGen != null || _openQuarry != null || _openReactor != null ||
                    _openTurbine != null || _openPortReactor != null || _openProcessor != null ||
                    _openReprocessor != null || _openElectrolyser != null || _openHydroEngine != null ||
                    _openGasTank != null || _openWaterPump != null || _openBiofarm != null || _openWindTurbine != null || _openStorageTerminal != null || _openServerRack != null ||
                    _openPatternTerminal != null || _openCraftTerminal != null || _openImporter != null ||
                    _openExporter != null || _openDiskManipulator != null || _openNAS != null ||
                    _openPowerstation != null || _openStorageDrawer != null ||
                    _openDrawerController != null || _openItemDisplay != null ||
                    _openCrusher != null || _openAssembler != null || _openFunnel != null || _openSplitter != null ||
                    _openDefense != null || _openArmorUpgradeStation != null;
                if ((anyRightTargetOpen || CraftingScreen.Visible) && (_productionStatsOpen || _recipeBrowserOpen))
                {
                    _productionStatsOpen = false;
                    _recipeBrowserOpen = false;
                }

                bool aRightPanelIsOpen =
                    _productionStatsOpen || _recipeBrowserOpen || _rightContainer != null || _openFurnace != null || _openElectric != null ||
                    _openCoalGen != null || _openQuarry != null || _openReactor != null ||
                    _openTurbine != null || _openPortReactor != null || _openProcessor != null ||
                    _openReprocessor != null || _openElectrolyser != null || _openHydroEngine != null ||
                    _openGasTank != null || _openWaterPump != null || _openBiofarm != null || _openWindTurbine != null || _openStorageTerminal != null || _openServerRack != null ||
                    _openPatternTerminal != null || _openCraftTerminal != null || _openImporter != null ||
                    _openExporter != null || _openDiskManipulator != null || _openNAS != null ||
                    _openPowerstation != null || _openStorageDrawer != null ||
                    _openDrawerController != null || _openItemDisplay != null ||
                    _openCrusher != null || _openAssembler != null || _openFunnel != null || _openSplitter != null ||
                    _openDefense != null || _openArmorUpgradeStation != null;
                // The station pane (_openStation) renders its OWN crafting list on
                // the right, so we suppress the center panel only in that case.
                // For every other right panel (chest / furnace / storage terminal)
                // we keep crafting available — the panel simply shrinks to sit in
                // the gap between the inventory and the right panel.
                if (CraftingScreen.Visible && _openStation == null && _openArmorUpgradeStation == null)
                {
                    BuildCenterCrafting(_contentLayer, aRightPanelIsOpen);
                    _craftPanelWasVisible = true;
                }
                else _craftPanelWasVisible = false;

                // Right panel — container or station
                if (_productionStatsOpen) _contentLayer.Add(ProductionStatsUI.BuildPanel());
                else if (_recipeBrowserOpen) _contentLayer.Add(RecipeBrowserUI.BuildPanel(recipeRegistry, inventory));
                else if (_rightContainer != null) BuildRightContainer(_contentLayer, _rightContainer);
                else if (_openFurnace  != null) BuildRightFurnace(_contentLayer, _openFurnace);
                else if (_openElectric != null) BuildRightElectricFurnace(_contentLayer, _openElectric);
                else if (_openCoalGen  != null) BuildRightCoalGenerator(_contentLayer, _openCoalGen);
                else if (_openQuarry   != null) { var mp = MachineUIs.QuarryPanel(_openQuarry, BuildSlot); _contentLayer.Add(mp); AppendItemPorts(mp, _openQuarry); }
                else if (_openReactor  != null) { var mp = MachineUIs.ReactorCorePanel(_openReactor, BuildSlot); _contentLayer.Add(mp); AppendItemPorts(mp, _openReactor); }
                else if (_openTurbine  != null) _contentLayer.Add(MachineUIs.SteamTurbinePanel(_openTurbine));
                else if (_openPortReactor != null) { var mp = MachineUIs.PortableReactorPanel(_openPortReactor, BuildSlot); _contentLayer.Add(mp); AppendItemPorts(mp, _openPortReactor); }
                else if (_openProcessor != null) { var mp = MachineUIs.UraniumProcessorPanel(_openProcessor, BuildSlot); _contentLayer.Add(mp); AppendItemPorts(mp, _openProcessor); }
                else if (_openReprocessor != null) { var mp = MachineUIs.WasteReprocessorPanel(_openReprocessor, BuildSlot); _contentLayer.Add(mp); AppendItemPorts(mp, _openReprocessor); }
                else if (_openElectrolyser != null) { var mp = MachineUIs.ElectrolyserPanel(_openElectrolyser, BuildSlot); _contentLayer.Add(mp); AppendItemPorts(mp, _openElectrolyser); }
                else if (_openHydroEngine != null) _contentLayer.Add(MachineUIs.HydrogenEnginePanel(_openHydroEngine));
                else if (_openGasTank != null) { _openGasTank.EnsureContainers(); _contentLayer.Add(MachineUIs.GasTankPanel(_openGasTank, BuildSlot)); }
                else if (_openWaterPump != null) _contentLayer.Add(VoxelEngine.UI.FluidPumpUI.BuildPanel(_openWaterPump));
                else if (_openBiofarm != null) { var mp = MachineUIs.BiofarmPanel(_openBiofarm, BuildSlot); _contentLayer.Add(mp); AppendItemPorts(mp, _openBiofarm); }
                else if (_openWindTurbine != null) _contentLayer.Add(VoxelEngine.Power.Wind.WindTurbineUI.BuildPanel(_openWindTurbine, inventory));
                else if (_openStorageTerminal  != null) _contentLayer.Add(VoxelEngine.Storage.StorageUI.BuildTerminalPanel(_openStorageTerminal, BuildSlot, inventory));
                else if (_openServerRack       != null) _contentLayer.Add(VoxelEngine.Storage.StorageUI.BuildServerPanel(_openServerRack, BuildSlot));
                else if (_openPatternTerminal  != null) _contentLayer.Add(VoxelEngine.Storage.StorageUI.BuildPatternTerminalPanel(_openPatternTerminal, recipeRegistry, inventory));
                else if (_openCraftTerminal    != null) _contentLayer.Add(VoxelEngine.Storage.StorageUI.CreateCraftingTerminalPanel(_openCraftTerminal, inventory));
                else if (_openImporter         != null) _contentLayer.Add(VoxelEngine.Storage.StorageUI.BuildImporterPanel(_openImporter, BuildSlot));
                else if (_openExporter         != null) _contentLayer.Add(VoxelEngine.Storage.StorageUI.BuildExporterPanel(_openExporter, BuildSlot));
                else if (_openDiskManipulator  != null) _contentLayer.Add(VoxelEngine.Storage.StorageUI.BuildDiskManipulatorPanel(_openDiskManipulator, BuildSlot));
                else if (_openNAS              != null) _contentLayer.Add(VoxelEngine.Storage.StorageUI.BuildNASPanel(_openNAS, BuildSlot));
                else if (_openPowerstation     != null) _contentLayer.Add(BuildPowerstationPanel(_openPowerstation));
                else if (_openStorageDrawer   != null) _contentLayer.Add(VoxelEngine.Storage.StorageUI.BuildDrawerPanel(_openStorageDrawer, BuildSlot));
                else if (_openDrawerController!= null) { var mp = VoxelEngine.Storage.StorageUI.BuildDrawerControllerPanel(_openDrawerController); _contentLayer.Add(mp); AppendItemPorts(mp, _openDrawerController); }
                else if (_openItemDisplay     != null) _contentLayer.Add(VoxelEngine.Storage.StorageUI.BuildItemDisplayPanel(_openItemDisplay, BuildSlot));
                else if (_openGridBlock        != null) { var mp = VoxelEngine.GridSystem.UI.GridBlockUI.BuildPanel(_openGridBlock, BuildSlot); _contentLayer.Add(mp); if (_openGridBlock is VoxelEngine.Transport.IItemPortHost) AppendItemPorts(mp, _openGridBlock); }
                else if (_openOilRefinery      != null) { var mp = VoxelEngine.Crafting.ProcessorUI.OilRefineryPanel(_openOilRefinery, BuildSlot); _contentLayer.Add(mp); AppendItemPorts(mp, _openOilRefinery); }
                else if (_openChemPlant        != null) { var mp = VoxelEngine.Crafting.ProcessorUI.ChemicalPlantPanel(_openChemPlant, BuildSlot); _contentLayer.Add(mp); AppendItemPorts(mp, _openChemPlant); }
                else if (_openCrusher          != null) { var mp = MachineUIs.CrusherPanel(_openCrusher, BuildSlot); _contentLayer.Add(mp); AppendItemPorts(mp, _openCrusher); }
                else if (_openAssembler        != null) { var mp = MachineUIs.AssemblerPanel(_openAssembler, BuildSlot); _contentLayer.Add(mp); AppendItemPorts(mp, _openAssembler); }
                else if (_openFunnel           != null) _contentLayer.Add(MachineUIs.FunnelPanel(_openFunnel));
                else if (_openSplitter         != null) _contentLayer.Add(MachineUIs.SplitterPanel(_openSplitter, BuildSlot));
                else if (_openVoltageStation   != null) _contentLayer.Add(VoxelEngine.Simulation.VoltageStationUI.BuildPanel(_openVoltageStation));
                else if (_openDefense != null) BuildDefensePanel(_contentLayer, _openDefense);
                else if (_openPowerBattery != null) _contentLayer.Add(BuildRightPowerBattery(_openPowerBattery));
                else if (_openArmorUpgradeStation != null) BuildRightArmorUpgradeStation(_contentLayer, _openArmorUpgradeStation);
                else if (_openStation  != null) BuildRightStationCrafting(_contentLayer, _openStation);
            }
            else
            {
                _root.pickingMode = PickingMode.Ignore;
                _root.style.backgroundColor = new StyleColor(new Color(0,0,0,0));
            }
        }


        /// <summary>Creates a sort button + slot grid for any ItemContainer.</summary>
        private VisualElement BuildSortableSlotGrid(IItemContainer container, int startIdx = 0, int endIdx = -1, bool showSort = true)
        {
            bool cargoMatrix = IsPlayerCargoContainer(container, startIdx);
            var wrapper = new VisualElement { name = cargoMatrix ? "InventoryCargoMatrix" : "LcdSlotMatrix" };
            wrapper.style.marginBottom = 5;

            if (showSort)
            {
                var controls = new VisualElement();
                controls.style.flexDirection = FlexDirection.Row;
                controls.style.alignItems = Align.Center;
                controls.style.marginBottom = 4;

                if (cargoMatrix)
                {
                    var caption = LcdHudTheme.CaptionLabel("CARGO MATRIX");
                    caption.style.flexGrow = 1;
                    controls.Add(caption);

                    int end = endIdx < 0 ? container.Slots.Count : endIdx;
                    var cells = LcdHudTheme.CaptionLabel($"{Mathf.Max(0, end - startIdx):00} CELLS");
                    cells.style.marginRight = 5;
                    controls.Add(cells);
                }
                else
                {
                    var caption = LcdHudTheme.CaptionLabel("SLOT MATRIX");
                    caption.style.flexGrow = 1;
                    controls.Add(caption);
                }

                if (container is ItemContainer itemContainer)
                {
                    var sortButton = LcdHudTheme.CommandButton("SORT", () =>
                    {
                        if (startIdx > 0) itemContainer.SortRange(startIdx, endIdx < 0 ? itemContainer.Size : endIdx);
                        else itemContainer.Sort();
                        Refresh();
                    }, LcdHudTheme.Phosphor);
                    sortButton.style.minWidth = 54;
                    controls.Add(sortButton);
                }
                wrapper.Add(controls);
            }

            var grid = new VisualElement { name = cargoMatrix ? "InventoryLcdSlotGrid" : "LcdSlotGrid" };
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.paddingLeft = 5;
            grid.style.paddingRight = 5;
            grid.style.paddingTop = 5;
            grid.style.paddingBottom = 3;
            LcdHudTheme.ApplyScreen(grid, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.86f), 1f);

            int slotEnd = endIdx < 0 ? container.Slots.Count : endIdx;
            for (int i = startIdx; i < slotEnd; i++)
                grid.Add(BuildSlot(container, i, container.GetSlot(i), false));

            if (cargoMatrix)
                LcdHudTheme.AddScanlines(grid, 7, 11f, 53f);

            wrapper.Add(grid);
            return wrapper;
        }

        private bool IsPlayerCargoContainer(IItemContainer container, int startIndex)
        {
            return inventory != null && container == inventory.container && startIndex >= Inventory.HOTBAR_SIZE;
        }

        private bool IsEquipmentContainer(IItemContainer container)
        {
            if (inventory == null || container == null) return false;
            var equipment = inventory.GetComponent<VoxelEngine.Player.PlayerEquipment>();
            if (equipment == null) return false;
            return object.ReferenceEquals(container, equipment.ArmorSlots)
                || object.ReferenceEquals(container, equipment.JetpackSlots)
                || object.ReferenceEquals(container, equipment.HelmetSlots)
                || object.ReferenceEquals(container, equipment.OxygenTankSlots);
        }


        private VisualElement BuildJetpackSlotsPanel(VoxelEngine.Player.PlayerEquipment equipment)
        {
            var box = new VisualElement();
            box.name = "jetpack-bay";   // lets TickJetpackBayLiveUI find the live badges
            box.style.marginTop = 6;
            box.style.marginBottom = 10;
            box.style.paddingTop = 2;
            box.style.paddingBottom = 4;
            box.style.paddingLeft = 6;
            box.style.paddingRight = 6;
            LcdHudTheme.ApplyScreen(box, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.84f), 1f);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.FlexStart;
            header.style.marginBottom = 6;

            var title = new Label("JETPACK BAY");
            title.style.fontSize = 10;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.2f;
            title.style.color = LcdHudTheme.Phosphor;
            header.Add(title);

            // NOTE: ensure/auto-refuel is done by OpenInventory BEFORE the build —
            // doing it here mutated containers mid-layout (broken slot grids).
            var summary = equipment.GetJetpackSummary();

            _jbStatusPill = new VisualElement();
            _jbStatusPill.style.marginLeft = 8;
            _jbStatusPill.style.paddingLeft = 7;
            _jbStatusPill.style.paddingRight = 7;
            _jbStatusPill.style.paddingTop = 2;
            _jbStatusPill.style.paddingBottom = 2;
            SetBorderRadius(_jbStatusPill, 1);
            _jbStatusPill.style.backgroundColor = new StyleColor(LcdHudTheme.GlassDark);
            UITheme.Border(_jbStatusPill, 1f, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.82f));
            _jbStatus = new Label("");
            _jbStatus.style.fontSize = 9;
            _jbStatus.style.unityFontStyleAndWeight = FontStyle.Bold;
            _jbStatusPill.Add(_jbStatus);
            header.Add(_jbStatusPill);
            box.Add(header);

            var slots = BuildSortableSlotGrid(equipment.JetpackSlots, 0,
                VoxelEngine.Player.PlayerEquipment.JetpackSlotCount, showSort: false);
            slots.style.marginLeft = 2;
            box.Add(slots);

            // ── Dual fuel bars: Hydrogen (ml) + Power (Wh) — shown whenever a pack
            // with that pool TYPE is equipped, even at 0 (an empty Atmospheric cell
            // still renders its bar instead of disappearing). Twin packs sum, so the
            // displayed capacity doubles exactly like the real combined tank does.
            (_jbH2Row, _jbH2Fill, _jbH2Label) = BuildJetpackFuelBar(
                summary.h2Cap > 0, "H₂ Fuel", summary.h2, summary.h2Cap, "ml", new Color(0.35f, 0.85f, 1f));
            if (_jbH2Row != null) box.Add(_jbH2Row);

            (_jbPRow, _jbPFill, _jbPLabel) = BuildJetpackFuelBar(
                summary.powerCap > 0, "Power", summary.power, summary.powerCap, "Wh", new Color(0.45f, 0.9f, 0.6f));
            if (_jbPRow != null) box.Add(_jbPRow);

            // ── Per-pack chips: exact H₂ ml + charge % for each equipped pack ──
            int slotCount = VoxelEngine.Player.PlayerEquipment.JetpackSlotCount;
            _jbPackRows = new VisualElement[slotCount];
            _jbPackH2   = new Label[slotCount];
            _jbPackPwr  = new Label[slotCount];
            var chipsCol = new VisualElement();
            chipsCol.style.marginTop = 5;
            chipsCol.style.marginLeft = 2;
            chipsCol.style.marginRight = 2;
            for (int i = 0; i < slotCount; i++)
            {
                var s = equipment.JetpackSlots.GetSlot(i);
                if (s == null || s.IsEmpty || s.item is not VoxelEngine.Items.JetpackItem jp) continue;

                var chip = new VisualElement();
                chip.style.flexDirection = FlexDirection.Row;
                chip.style.alignItems = Align.Center;
                chip.style.marginBottom = 3;
                chip.style.paddingLeft = 6; chip.style.paddingRight = 6;
                chip.style.paddingTop = 3;  chip.style.paddingBottom = 3;
                LcdHudTheme.ApplyDataCard(chip, GetJetpackFamilyColor(jp.family));
                chip.pickingMode = PickingMode.Ignore;

                var name = new Label(jp.displayName);
                name.style.fontSize = 9;
                name.style.color = new StyleColor(LcdHudTheme.Caption);
                name.style.flexGrow = 1;
                name.style.overflow = Overflow.Hidden;
                name.style.whiteSpace = WhiteSpace.NoWrap;
                name.style.textOverflow = TextOverflow.Ellipsis;
                name.pickingMode = PickingMode.Ignore;
                chip.Add(name);

                if (jp.HydrogenCapacityMl > 0)
                {
                    var h2 = new Label();
                    h2.style.fontSize = 9;
                    h2.style.unityFontStyleAndWeight = FontStyle.Bold;
                    h2.style.color = new StyleColor(new Color(0.35f, 0.85f, 1f));
                    h2.style.marginLeft = 8;
                    h2.pickingMode = PickingMode.Ignore;
                    chip.Add(h2);
                    _jbPackH2[i] = h2;
                }
                if (jp.PowerCapacityMl > 0)
                {
                    var pwr = new Label();
                    pwr.style.fontSize = 9;
                    pwr.style.unityFontStyleAndWeight = FontStyle.Bold;
                    pwr.style.color = new StyleColor(new Color(0.45f, 0.9f, 0.6f));
                    pwr.style.marginLeft = 8;
                    pwr.pickingMode = PickingMode.Ignore;
                    chip.Add(pwr);
                    _jbPackPwr[i] = pwr;
                }
                chipsCol.Add(chip);
                _jbPackRows[i] = chip;
            }
            if (_jbPackRows != null)
                box.Add(chipsCol);

            // Push real values into the fresh elements immediately.
            TickJetpackBayLiveUI();
            return box;
        }

        /// <summary>Icon/accent color per jetpack family (matches the bay slot icon).</summary>
        private static Color GetJetpackFamilyColor(VoxelEngine.Items.JetpackFamily family) => family switch
        {
            VoxelEngine.Items.JetpackFamily.HydrogenBoost => new Color(0.35f, 0.85f, 1f),
            VoxelEngine.Items.JetpackFamily.Atmospheric   => new Color(0.45f, 0.9f, 0.6f),
            VoxelEngine.Items.JetpackFamily.Hybrid        => new Color(0.72f, 0.55f, 1f),
            _                                             => UITheme.TextSecondary,
        };

        /// <summary>One labelled mini bar for the jetpack bay. Returns (row, fill, label);
        /// row is null when the pool type isn't equipped.</summary>
        private static (VisualElement row, VisualElement fill, Label label) BuildJetpackFuelBar(
            bool visible, string name, int cur, int cap, string unit, Color accent)
        {
            if (!visible) return (null, null, null);
            var row = new VisualElement();
            row.style.marginTop = 4; row.style.marginLeft = 2; row.style.marginRight = 2;
            row.style.paddingLeft = 5; row.style.paddingRight = 5;
            row.style.paddingTop = 3; row.style.paddingBottom = 3;
            LcdHudTheme.ApplyScreen(row, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.78f), 1f);

            var label = new Label($"{name}  {cur} / {cap} {unit}");
            label.style.fontSize = 9;
            label.style.color = LcdHudTheme.Phosphor;
            label.style.marginBottom = 3;
            row.Add(label);

            var track = new VisualElement();
            track.style.height = 6;
            track.style.backgroundColor = new StyleColor(LcdHudTheme.GlassDark);
            SetBorderRadius(track, 1);
            UITheme.Border(track, 1f, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.75f));
            track.style.overflow = Overflow.Hidden;

            var fill = new VisualElement();
            fill.style.height = 6;
            fill.style.width = new StyleLength(new Length(cap > 0 ? Mathf.Clamp01(cur / (float)cap) * 100f : 0f, LengthUnit.Percent));
            fill.style.backgroundColor = new StyleColor(accent);
            SetBorderRadius(fill, 1);
            track.Add(fill);
            row.Add(track);
            return (row, fill, label);
        }

        /// <summary>Frame-cheap in-place update of the jetpack bay status + dual bars.
        /// Runs every Update so flight drain shows live WITHOUT rebuilding the panel.</summary>
        private void TickJetpackBayLiveUI()
        {
            if (!_inventoryOpen || inventory == null) return;
            var equipment = inventory.GetComponent<VoxelEngine.Player.PlayerEquipment>();
            if (equipment == null) return;
            var summary = equipment.GetJetpackSummary();

            if (_jbStatus != null)
            {
                string statusText;
                Color statusCol;
                if (!summary.anyPack)            { statusText = "EMPTY"; statusCol = new Color(0.95f, 0.45f, 0.25f); }
                else if (!summary.canFly)
                {
                    bool envBlocked = summary.offlineReason != null && summary.offlineReason.Contains("atmosphere");
                    statusText = envBlocked ? "NO ATMOS" : "DRY";
                    statusCol = envBlocked ? new Color(1f, 0.62f, 0.25f) : new Color(0.95f, 0.45f, 0.25f);
                }
                else if (summary.twinActive)     { statusText = "TWIN ×2"; statusCol = new Color(0.72f, 0.55f, 1f); }
                else
                {
                    float frac = 1f;
                    int totCap = summary.h2Cap + summary.powerCap;
                    if (totCap > 0) frac = (summary.h2 + summary.power) / (float)totCap;
                    statusText = frac <= 0.05f ? "DRY" : frac < 0.25f ? "LOW" : "ONLINE";
                    statusCol = statusText == "ONLINE" ? new Color(0.30f, 0.95f, 0.55f)
                              : statusText == "LOW" ? new Color(1f, 0.78f, 0.25f)
                              : new Color(0.95f, 0.45f, 0.25f);
                }
                _jbStatus.text = statusText;
                _jbStatus.style.color = statusCol;
            }

            // Hide a bar entirely if its pool type left the bay (pack swapped out).
            if (_jbH2Row != null) _jbH2Row.style.display = summary.h2Cap > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_jbPRow != null) _jbPRow.style.display = summary.powerCap > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (_jbH2Fill != null)
            {
                _jbH2Label.text = $"H₂ Fuel  {summary.h2} / {summary.h2Cap} ml";
                _jbH2Fill.style.width = new StyleLength(new Length(
                    summary.h2Cap > 0 ? Mathf.Clamp01(summary.h2 / (float)summary.h2Cap) * 100f : 0f, LengthUnit.Percent));
            }
            if (_jbPFill != null)
            {
                _jbPLabel.text = $"Power  {summary.power} / {summary.powerCap} Wh";
                _jbPFill.style.width = new StyleLength(new Length(
                    summary.powerCap > 0 ? Mathf.Clamp01(summary.power / (float)summary.powerCap) * 100f : 0f, LengthUnit.Percent));
            }

            // ── Per-pack chips + on-item badges: re-stamped EVERY FRAME so a
            // draining pack visibly drains everywhere (bars, chips AND the item
            // icon's own ml / % badges) without rebuilding anything.
            if (_jbPackRows != null)
            {
                var slots = equipment.JetpackSlots;
                for (int i = 0; i < _jbPackRows.Length && i < slots.Size; i++)
                {
                    var chip = _jbPackRows[i];
                    if (chip == null) continue;
                    var s = slots.GetSlot(i);
                    if (s == null || s.IsEmpty || s.item is not VoxelEngine.Items.JetpackItem jp)
                    { chip.style.display = DisplayStyle.None; continue; }
                    chip.style.display = DisplayStyle.Flex;

                    var h2 = _jbPackH2 != null && i < _jbPackH2.Length ? _jbPackH2[i] : null;
                    if (h2 != null)
                    {
                        int h2Cap = jp.HydrogenCapacityMl;
                        h2.text = $"H₂ {VoxelEngine.Items.JetpackItem.GetH2Ml(s)} / {h2Cap} ml";
                        h2.style.display = h2Cap > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                    var pwr = _jbPackPwr != null && i < _jbPackPwr.Length ? _jbPackPwr[i] : null;
                    if (pwr != null)
                    {
                        int pCap = jp.PowerCapacityMl;
                        float pct = pCap > 0
                            ? Mathf.Clamp01(VoxelEngine.Items.JetpackItem.GetPowerMl(s) / (float)pCap) * 100f : 0f;
                        pwr.text = $"PWR {pct:0}%";
                        pwr.style.display = pCap > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                }
            }

            var bay = _contentLayer != null ? _contentLayer.Q("jetpack-bay") : null;
            if (bay != null)
            {
                var slotsC = equipment.JetpackSlots;
                for (int i = 0; i < slotsC.Size; i++)
                {
                    var s = slotsC.GetSlot(i);
                    var jp = (s != null && !s.IsEmpty) ? s.item as VoxelEngine.Items.JetpackItem : null;
                    var h2Badge = bay.Q<Label>($"jp-h2-{i}");
                    if (h2Badge != null)
                    {
                        int ml = jp != null ? VoxelEngine.Items.JetpackItem.GetH2Ml(s) : 0;
                        h2Badge.text = jp == null ? "" : (ml >= 1000 ? $"{ml / 1000f:0.0}L" : $"{ml}ml");
                    }
                    var pwrBadge = bay.Q<Label>($"jp-pwr-{i}");
                    if (pwrBadge != null)
                    {
                        int pCap = jp != null ? jp.PowerCapacityMl : 0;
                        float pct = pCap > 0
                            ? Mathf.Clamp01(VoxelEngine.Items.JetpackItem.GetPowerMl(s) / (float)pCap) * 100f : 0f;
                        pwrBadge.text = pCap > 0 ? $"{pct:0}%" : "";
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //                  BATTERY PANEL (6.77 rework)
        // ════════════════════════════════════════════════════════════
        // Segmented, eased charge gauge + live in/out flow stats + the device
        // charger dock. Everything below updates in place via TickBatteryLiveUI —
        // no destructive 4 Hz rebuild, so the panel never flashes or eats clicks.

        private const int BatterySegmentCount = 12;
        private static readonly Color BatteryGreen = new(0.30f, 0.95f, 0.55f);
        private static readonly Color BatteryAmber = new(1.00f, 0.78f, 0.25f);
        private static readonly Color BatteryRed   = new(0.95f, 0.35f, 0.25f);
        private static readonly Color BatteryOff   = new(0.10f, 0.11f, 0.15f, 0.95f);

        private VisualElement BuildRightPowerBattery(VoxelEngine.Power.PowerBattery pb)
        {
            pb.EnsureContainers();
            var p = UITheme.MachinePanel();

            // ── Header: badge + title + live status pill ──────────────
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            head.style.marginBottom = 8;
            head.Add(UITheme.IconBadge("🔋", new Color(0.45f, 0.75f, 0.95f)));
            var title = UITheme.Title("Battery");
            title.style.flexGrow = 1;
            head.Add(title);
            _battStatus = new Label("IDLE");
            _battStatus.style.fontSize = 10;
            _battStatus.style.unityFontStyleAndWeight = FontStyle.Bold;
            _battStatus.style.letterSpacing = 1.2f;
            _battStatus.style.color = UITheme.TextSecondary;
            _battStatus.style.paddingLeft = 9; _battStatus.style.paddingRight = 9;
            _battStatus.style.paddingTop = 3;  _battStatus.style.paddingBottom = 3;
            _battStatus.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.30f));
            SetBorderRadius(_battStatus, 12);
            head.Add(_battStatus);
            p.Add(head);
            p.Add(UITheme.AccentDivider(new Color(0.45f, 0.75f, 0.95f)));

            // ── Segmented charge gauge + big % ────────────────────────
            var gaugeRow = new VisualElement();
                gaugeRow.style.flexDirection = FlexDirection.Row;
                gaugeRow.style.alignItems = Align.Center;
                gaugeRow.style.marginTop = 4;
                gaugeRow.style.marginBottom = 2;
                gaugeRow.pickingMode = PickingMode.Ignore;

            var segTrack = new VisualElement();
                segTrack.style.flexDirection = FlexDirection.Row;
                segTrack.style.flexGrow = 1;
                segTrack.style.height = 26;
                segTrack.style.paddingTop = 3; segTrack.style.paddingBottom = 3;
                segTrack.style.paddingLeft = 3; segTrack.style.paddingRight = 3;
                segTrack.style.backgroundColor = new StyleColor(new Color(0.04f, 0.045f, 0.065f, 0.98f));
                SetBorderRadius(segTrack, 7);
                segTrack.pickingMode = PickingMode.Ignore;

            _battSegments = new VisualElement[BatterySegmentCount];
            for (int i = 0; i < BatterySegmentCount; i++)
            {
                var seg = new VisualElement();
                seg.style.flexGrow = 1;
                seg.style.marginRight = i < BatterySegmentCount - 1 ? 2 : 0;
                seg.style.backgroundColor = new StyleColor(BatteryOff);
                SetBorderRadius(seg, 2);
                seg.pickingMode = PickingMode.Ignore;
                _battSegments[i] = seg;
                segTrack.Add(seg);
            }
            gaugeRow.Add(segTrack);

            _battPct = new Label("0%");
            _battPct.style.width = 58;
            _battPct.style.fontSize = 18;
            _battPct.style.unityFontStyleAndWeight = FontStyle.Bold;
            _battPct.style.unityTextAlign = TextAnchor.MiddleRight;
            _battPct.style.color = UITheme.TextPrimary;
            _battPct.style.marginLeft = 10;
            _battPct.pickingMode = PickingMode.Ignore;
            gaugeRow.Add(_battPct);
            p.Add(gaugeRow);

            // ── Live flow stats ───────────────────────────────────────
            p.Add(UITheme.Spacer(6));
            _battChargeRow = BuildBatteryStatRow(p, "🔋", "Stored", UITheme.TextPrimary);
            _battInRow  = BuildBatteryStatRow(p, "⬇", "Power In", BatteryGreen);
            _battOutRow = BuildBatteryStatRow(p, "⬆", "Power Out", BatteryAmber);

            // ── Device charger dock ───────────────────────────────────
            p.Add(UITheme.Divider());
            p.Add(UITheme.Subtitle("Device Charger"));
            var dockGrid = UITheme.SlotGrid(1);
            dockGrid.Add(BuildSlot(pb.ChargeSlot, 0, pb.ChargeSlot.GetSlot(0), false));
            p.Add(dockGrid);
            _battDockRow = new Label("No device docked");
            _battDockRow.style.fontSize = 10;
            _battDockRow.style.color = UITheme.TextMuted;
            _battDockRow.style.marginTop = 5;
            _battDockRow.pickingMode = PickingMode.Ignore;
            p.Add(_battDockRow);

            p.Add(UITheme.Spacer(8));
            p.Add(UITheme.Muted(
                "Dock a Portable Battery or a power-fed jetpack (Atmospheric / Hybrid) to charge it from stored energy. " +
                "You can also hold the device and RMB the battery — Shift tops it to 100%."));

            // Power-on sweep: only on a FRESH open. Rebuilds while the panel stays
            // open (dock insert/remove, telemetry) must keep the smoothed value —
            // otherwise the gauge visibly resets to 0, which looked broken while
            // a device was charging (the dock used to rebuild the panel every tick).
            if (_batterySweepPending) { _battSegSmooth = 0f; _batterySweepPending = false; }
            TickBatteryLiveUI();
            return p;
        }

        /// <summary>Icon + label + right-aligned stat value row; returns the value label.</summary>
        private static Label BuildBatteryStatRow(VisualElement parent, string icon, string label, Color valueColor)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 5;
            row.style.minHeight = 20;
            row.pickingMode = PickingMode.Ignore;

            var ico = new Label(icon);
            ico.style.fontSize = 12;
            ico.style.color = new StyleColor(valueColor);
            ico.style.marginRight = 7;
            ico.style.minWidth = 18;
            ico.style.unityTextAlign = TextAnchor.MiddleCenter;
            ico.pickingMode = PickingMode.Ignore;
            row.Add(ico);

            var lbl = new Label(label);
            lbl.style.color = new StyleColor(UITheme.TextSecondary);
            lbl.style.fontSize = 11;
            lbl.style.flexGrow = 1;
            lbl.pickingMode = PickingMode.Ignore;
            row.Add(lbl);

            var val = new Label("—");
            val.style.color = new StyleColor(valueColor);
            val.style.fontSize = 11;
            val.style.unityFontStyleAndWeight = FontStyle.Bold;
            val.pickingMode = PickingMode.Ignore;
            row.Add(val);

            parent.Add(row);
            return val;
        }

        /// <summary>Frame-cheap in-place battery panel update — segments, %, flows, dock.</summary>
        private void TickBatteryLiveUI()
        {
            var pb = _openPowerBattery;
            if (pb == null || _battSegments == null) return;

            // The dock slot's fill bar / % badge should climb while a device charges.
            // The dock no longer raises OnChanged per-tick (that rebuilt the whole
            // panel every frame and reset the gauge — the "flashes to 0" bug), so we
            // raise a gentle ~3 Hz refresh instead, never mid-drag.
            if (pb.IsChargingItem && !_dragSource.active)
            {
                _battDockIconAccum += Time.unscaledDeltaTime;
                if (_battDockIconAccum >= 0.33f)
                {
                    _battDockIconAccum = 0f;
                    pb.ChargeSlot.RaiseChanged();
                    return;   // Refresh() rebuilds + re-ticks us fresh
                }
            }
            else _battDockIconAccum = 0f;

            float fill = pb.Fill01;
            _battSegSmooth = Mathf.MoveTowards(_battSegSmooth, fill, Time.unscaledDeltaTime * 1.2f);

            Color col = fill > 0.5f ? BatteryGreen : fill > 0.25f ? BatteryAmber : BatteryRed;
            int lit = Mathf.RoundToInt(_battSegSmooth * _battSegments.Length);
            for (int i = 0; i < _battSegments.Length; i++)
            {
                var seg = _battSegments[i];
                if (seg == null) continue;
                seg.style.backgroundColor = new StyleColor(
                    i < lit ? new Color(col.r, col.g, col.b, 0.88f) : BatteryOff);
            }

            if (_battPct != null)
            {
                _battPct.text = $"{fill * 100f:0}%";
                _battPct.style.color = new StyleColor(col);
            }
            if (_battChargeRow != null)
                _battChargeRow.text = $"{pb.charge:0} / {pb.capacityWattHours:0} Wh";
            if (_battInRow != null)
                _battInRow.text = $"{pb.lastChargeInW:0} / {pb.ioRate:0} W";
            if (_battOutRow != null)
                _battOutRow.text = $"{pb.lastDischargeOutW:0} / {pb.ioRate:0} W";

            if (_battStatus != null)
            {
                string text; Color statusCol;
                if (pb.lastChargeInW > 0.5f)       { text = "CHARGING";     statusCol = BatteryGreen; }
                else if (pb.lastDischargeOutW > 0.5f) { text = "DISCHARGING"; statusCol = BatteryAmber; }
                else if (pb.IsChargingItem)        { text = "DOCK +";       statusCol = new Color(0.45f, 0.75f, 0.95f); }
                else if (fill >= 0.999f)           { text = "FULL";         statusCol = BatteryGreen; }
                else                               { text = "IDLE";         statusCol = UITheme.TextSecondary; }
                _battStatus.text = text;
                _battStatus.style.color = new StyleColor(statusCol);
            }

            if (_battDockRow != null)
            {
                pb.GetDockedItemCharge(out int stored, out int capacity);
                var docked = pb.ChargeSlot.GetSlot(0);
                if (docked == null || docked.IsEmpty || capacity <= 0)
                {
                    _battDockRow.text = "No device docked — drop a Portable Battery or power jetpack.";
                    _battDockRow.style.color = new StyleColor(UITheme.TextMuted);
                }
                else
                {
                    float f01 = Mathf.Clamp01(stored / (float)capacity);
                    _battDockRow.text = $"{docked.item.displayName}  ·  {stored} / {capacity} Wh  ({f01 * 100f:0}%)";
                    _battDockRow.style.color = new StyleColor(f01 >= 0.999f ? BatteryGreen : new Color(0.45f, 0.75f, 0.95f));
                }
            }
        }

        private VisualElement BuildLifeSupportSlotsPanel(VoxelEngine.Player.PlayerEquipment equipment)
        {
            var box = new VisualElement();
            box.style.marginTop = 2;
            box.style.marginBottom = 10;
            box.style.paddingTop = 6;
            box.style.paddingBottom = 6;
            box.style.paddingLeft = 6;
            box.style.paddingRight = 6;
            LcdHudTheme.ApplyDataCard(box, LcdHudTheme.Phosphor);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.FlexStart;
            header.style.marginBottom = 6;

            var title = new Label("LIFE SUPPORT");
            title.style.fontSize = 10;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.2f;
            title.style.color = LcdHudTheme.Phosphor;
            header.Add(title);

            bool online = equipment.HasBreathingKit;
            var playerStats = VoxelEngine.Player.PlayerStats.Instance;
            string supportState = playerStats != null ? playerStats.LifeSupportStatus : (online ? "SEALED" : "OPEN");
            bool hazard = playerStats != null && playerStats.RequiresLifeSupport;
            var status = new Label(hazard ? supportState : (online ? "SEALED" : "OPEN"));
            status.style.marginLeft = 8;
            status.style.fontSize = 9;
            status.style.unityFontStyleAndWeight = FontStyle.Bold;
            status.style.color = hazard
                ? (online ? new Color(0.30f, 0.95f, 0.55f) : UITheme.AccentRed)
                : online ? new Color(0.30f, 0.95f, 0.55f) : new Color(0.95f, 0.62f, 0.18f);
            status.style.backgroundColor = new StyleColor(LcdHudTheme.GlassDark);
            status.style.paddingLeft = 7;
            status.style.paddingRight = 7;
            status.style.paddingTop = 2;
            status.style.paddingBottom = 2;
            SetBorderRadius(status, 1);
            UITheme.Border(status, 1f, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.82f));
            header.Add(status);
            box.Add(header);

            var slotRow = new VisualElement();
            slotRow.style.flexDirection = FlexDirection.Row;
            slotRow.style.flexWrap = Wrap.Wrap;
            slotRow.Add(BuildSlot(equipment.HelmetSlots, 0, equipment.HelmetSlots.GetSlot(0), false));
            slotRow.Add(BuildSlot(equipment.OxygenTankSlots, 0, equipment.OxygenTankSlots.GetSlot(0), false));
            box.Add(slotRow);

            return box;
        }

        private VisualElement BuildInventoryWeightReadout()
        {
            var box = new VisualElement { name = "InventoryCargoLoadReadout" };
            box.style.marginTop = 1;
            box.style.marginBottom = 7;
            box.style.paddingTop = 6;
            box.style.paddingBottom = 6;
            box.style.paddingLeft = 8;
            box.style.paddingRight = 8;

            float current = inventory != null ? inventory.CurrentWeightKg : 0f;
            float max = inventory != null ? inventory.MaxWeightKg : VoxelEngine.Menu.WorldSession.DefaultPlayerInventoryWeightKg;
            float fill = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            Color signal = fill >= 0.95f ? UITheme.AccentRed : fill >= 0.80f ? UITheme.AccentAmber : LcdHudTheme.Phosphor;
            LcdHudTheme.ApplyDataCard(box, signal);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var label = LcdHudTheme.CaptionLabel("CARGO LOAD");
            label.style.flexGrow = 1;
            row.Add(label);

            var value = new Label($"{MassFormat.Format(current)} / {MassFormat.Format(max)}");
            value.style.fontSize = 10;
            value.style.letterSpacing = 0.45f;
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            value.style.color = new StyleColor(signal);
            value.pickingMode = PickingMode.Ignore;
            row.Add(value);
            box.Add(row);

            var track = LcdHudTheme.CreateSegmentTrack(10, out var segments, 9f);
            track.style.marginTop = 5;
            LcdHudTheme.SetSegments(segments, fill, signal);
            box.Add(track);
            return box;
        }


        // ----- HOTBAR -----
        private void BuildHotbar(VisualElement root)
        {
            // While seated in a cockpit, the player flies the ship — the on-foot hotbar is
            // replaced by the ShipToolHud (drill/weapon selector), so hide it entirely.
            if (VoxelEngine.GridSystem.GridCockpit.AnyPilotSeatActive) return;

            var bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.bottom = 10;
            bar.style.left = 0; bar.style.right = 0;
            bar.style.height = 70;
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.justifyContent = Justify.Center;
            bar.style.alignItems = Align.Center;
            bar.pickingMode = _inventoryOpen ? PickingMode.Position : PickingMode.Ignore;
            root.Add(bar);

            var rack = new VisualElement { name = "HotbarInstrumentRack" };
            rack.style.flexDirection = FlexDirection.Row;
            rack.style.alignItems = Align.Center;
            rack.style.paddingLeft = 5;
            rack.style.paddingRight = 5;
            rack.style.paddingTop = 5;
            rack.style.paddingBottom = 5;
            rack.pickingMode = bar.pickingMode;
            LcdHudTheme.ApplyChassis(rack, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.94f), 2f);
            bar.Add(rack);

            for (int i = 0; i < Inventory.HOTBAR_SIZE; i++)
            {
                var slot = inventory.container.GetSlot(i);
                rack.Add(BuildSlot(inventory.container, i, slot,
                    hotbarHighlight: i == inventory.activeHotbarIndex,
                    interactive: _inventoryOpen));
            }
        }

        // ── LEFT DOCK ──────────────────────────────────────────────
        // Inventory panel + (when nothing else needs the space) the slim ARMOR
        // equipment panel docked to its right. Both are flex children of a single
        // absolutely-positioned row, so the armor panel always hugs the inventory's
        // right edge regardless of screen size / window scale.

        private Component _openDefense;
        private VoxelEngine.Power.PowerBattery _openPowerBattery;

        public void OpenDefense(Component d)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            _rightContainer = null; _openFurnace = null; _openElectric = null; _openCoalGen = null;
            _openQuarry = null; _openReactor = null; _openStation = null; _recipeBrowserOpen = false;
            _productionStatsOpen = false; _openChest = null; _openGridTerminal = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openDefense = d;
            _inventoryOpen = true;
            UnwatchAllContainers();
            // Watch magazines so drag-drop refreshes live.
            if (d is VoxelEngine.Combat.Artillery artWatch) WatchContainer(artWatch.ShellMagazine);
            else if (d is VoxelEngine.Combat.FlamethrowerTurret flameWatch) WatchContainer(flameWatch.FuelMagazine);
            else if (d is VoxelEngine.Combat.MortarTurret mortarWatch) WatchContainer(mortarWatch.ShellMagazine);
            else if (d is VoxelEngine.Combat.GiantShellTurret giantWatch) WatchContainer(giantWatch.ShellMagazine);
            else if (d is VoxelEngine.Combat.AntiAirTurret aaWatch) WatchContainer(aaWatch.AmmoMagazine);
            else if (d is VoxelEngine.Combat.EnergyRelicTurret energyWatch) WatchContainer(energyWatch.CellMagazine);
            UnlockCursor();
            Refresh();
        }

        private void BuildDefensePanel(VisualElement root, Component defense)
        {
            var panel = MakePanel();
            panel.style.position = Position.Absolute;
            panel.style.top = 24; panel.style.bottom = 92;
            panel.style.right = 18;
            panel.style.width = new StyleLength(new Length(28f, LengthUnit.Percent));
            panel.style.minWidth = 280; panel.style.maxWidth = 400;
            root.Add(panel);

            var art    = defense as VoxelEngine.Combat.Artillery;
            var tur    = defense as VoxelEngine.Combat.Turret;
            var flame  = defense as VoxelEngine.Combat.FlamethrowerTurret;
            var mortar = defense as VoxelEngine.Combat.MortarTurret;
            var giant  = defense as VoxelEngine.Combat.GiantShellTurret;
            var aa     = defense as VoxelEngine.Combat.AntiAirTurret;
            var energy = defense as VoxelEngine.Combat.EnergyRelicTurret;

            string name = art != null ? art.variant.ToString()
                        : flame != null ? "Flamethrower Turret"
                        : mortar != null ? "Mortar Turret"
                        : giant != null ? "Giant Shell Turret"
                        : aa != null ? "Anti-Air Turret"
                        : energy != null ? "Energy / Relic Turret"
                        : "Auto Turret";
            panel.Add(MakeTitle(name));

            // Live stock strip (EMPTY / LOW / OK) from shared DefenseStatus.
            if (VoxelEngine.Combat.DefenseStatus.TryDescribe(defense, out var stockInfo))
            {
                string stockLabel = stockInfo.isEmpty ? "EMPTY" : (stockInfo.isLow ? "LOW" : "STOCKED");
                Color stockCol = stockInfo.isEmpty ? new Color(1f, 0.35f, 0.3f)
                                : stockInfo.isLow ? new Color(1f, 0.75f, 0.25f)
                                : new Color(0.45f, 0.9f, 0.55f);
                var stock = new Label($"{stockLabel}  ·  {stockInfo.status}");
                stock.style.color = stockCol;
                stock.style.fontSize = 11;
                stock.style.unityFontStyleAndWeight = FontStyle.Bold;
                stock.style.marginBottom = 8;
                stock.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(stock);
            }

            if (art != null)
            {
                panel.Add(MakeSubtitle("Shells (drag in)"));
                WatchContainer(art.ShellMagazine);
                panel.Add(BuildSortableSlotGrid(art.ShellMagazine, showSort: false));
            }
            else if (energy != null)
            {
                panel.Add(MakeSubtitle("Cells (Charged / Relic Capacitor)"));
                WatchContainer(energy.CellMagazine);
                panel.Add(BuildSortableSlotGrid(energy.CellMagazine, showSort: false));
                var hint = new Label("Electrical beam. Relic Capacitors = heavier charged shot.");
                hint.style.color = new Color(0.7f, 0.55f, 1f);
                hint.style.fontSize = 10; hint.style.marginBottom = 6; hint.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(hint);
            }
            else if (aa != null)
            {
                panel.Add(MakeSubtitle("AA Rounds (or Bullets)"));
                WatchContainer(aa.AmmoMagazine);
                panel.Add(BuildSortableSlotGrid(aa.AmmoMagazine, showSort: false));
                var hint = new Label("Fast flak — prefers aerial targets (Griffin / Roc). Proximity burst.");
                hint.style.color = new Color(0.55f, 0.85f, 1f);
                hint.style.fontSize = 10; hint.style.marginBottom = 4; hint.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(hint);
                var airOnly = new Toggle("Aerial Only") { value = aa.preferAerialOnly };
                airOnly.style.color = Color.white; airOnly.style.marginBottom = 6;
                airOnly.RegisterValueChangedCallback(e => { aa.preferAerialOnly = e.newValue; Refresh(); });
                panel.Add(airOnly);
            }
            else if (giant != null)
            {
                panel.Add(MakeSubtitle("Giant Shells (one per shot)"));
                WatchContainer(giant.ShellMagazine);
                panel.Add(BuildSortableSlotGrid(giant.ShellMagazine, showSort: false));
                var hint = new Label("Siege gun — prefers bosses/high-HP. Slow aim, huge blast.");
                hint.style.color = new Color(0.85f, 0.7f, 0.45f);
                hint.style.fontSize = 10; hint.style.marginBottom = 6; hint.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(hint);
            }
            else if (mortar != null)
            {
                panel.Add(MakeSubtitle("Mortar Shells (Explosive / Smoke / Illum)"));
                WatchContainer(mortar.ShellMagazine);
                panel.Add(BuildSortableSlotGrid(mortar.ShellMagazine, showSort: false));
                var hint = new Label("Indirect fire — no LOS needed. Min range ~8 m.");
                hint.style.color = new Color(0.75f, 0.78f, 0.82f);
                hint.style.fontSize = 10; hint.style.marginBottom = 6; hint.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(hint);
            }
            else if (flame != null)
            {
                float fuel = flame.FuelSeconds;
                float maxF = Mathf.Max(0.01f, flame.MaxFuelDisplay);
                var fuelInfo = new Label($"Fuel buffer: {fuel:0.0}s  ({Mathf.Clamp01(fuel / maxF) * 100f:0}%)");
                fuelInfo.style.color = fuel > 0.5f ? new Color(1f, 0.7f, 0.3f) : new Color(1f, 0.4f, 0.3f);
                fuelInfo.style.fontSize = 12; fuelInfo.style.marginBottom = 4;
                panel.Add(fuelInfo);
                panel.Add(MakeSubtitle("Fuel (Flame Canister / Coal)"));
                WatchContainer(flame.FuelMagazine);
                panel.Add(BuildSortableSlotGrid(flame.FuelMagazine, showSort: false));
            }
            else if (tur != null)
            {
                var info = new Label($"Ammo: {tur.ammo} / {tur.maxAmmo}");
                info.style.color = Color.white; info.style.fontSize = 12; info.style.marginBottom = 6;
                panel.Add(info);

                var reloadBtn = new Button(() =>
                {
                    VoxelEngine.Items.ItemDefinition bullets = null;
                    for (int i = 0; i < inventory.container.Slots.Count; i++)
                    {
                        var sl = inventory.container.GetSlot(i);
                        if (sl != null && !sl.IsEmpty && sl.item != null && sl.item.itemId == "item_bullets") { bullets = sl.item; break; }
                    }
                    if (bullets == null) { VoxelEngine.UI.BuildFeedbackHud.Show("No Bullets", "Craft Bullets at the Assembler", null, Color.yellow); return; }
                    int want = tur.maxAmmo - tur.ammo;
                    if (want <= 0) return;
                    int got = inventory.container.Remove(bullets, want);
                    tur.ammo += got;
                    inventory.container.RaiseChanged();
                    Refresh();
                }) { text = "Reload from Inventory" };
                StyleBtn(reloadBtn);
                panel.Add(reloadBtn);
            }

            // Targeting (all defense types) — resolve filter/auto via helpers so new
            // turret kinds don't grow the toggle signature forever.
            panel.Add(MakeSubtitle("Targeting"));
            var curFilter = GetDefenseFilter(defense);
            panel.Add(MakeDefenseToggle("Target Enemies", VoxelEngine.Combat.TargetFilter.Enemies, curFilter, defense));
            panel.Add(MakeDefenseToggle("Target Players", VoxelEngine.Combat.TargetFilter.Players, curFilter, defense));
            panel.Add(MakeDefenseToggle("Target Passive", VoxelEngine.Combat.TargetFilter.Passive, curFilter, defense));

            bool autoF = GetDefenseAuto(defense);
            var autoT = new Toggle("Auto-Fire") { value = autoF };
            autoT.style.color = Color.white; autoT.style.marginBottom = 6;
            autoT.RegisterValueChangedCallback(e => { SetDefenseAuto(defense, e.newValue); Refresh(); });
            panel.Add(autoT);

            // Conserve-ammo / reserve stock (all IDefenseFirePolicy defenses).
            if (defense is VoxelEngine.Combat.IDefenseFirePolicy policy)
            {
                panel.Add(MakeSubtitle("Ammo Policy"));
                var cons = new Toggle("Conserve Ammo") { value = policy.ConserveAmmo };
                cons.style.color = Color.white; cons.style.marginBottom = 4;
                cons.RegisterValueChangedCallback(e => { policy.ConserveAmmo = e.newValue; Refresh(); });
                panel.Add(cons);

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 6;
                var rLab = new Label($"Reserve: {policy.ReserveStock}");
                rLab.style.color = Color.white; rLab.style.fontSize = 11;
                rLab.style.flexGrow = 1; rLab.style.minWidth = 90;
                row.Add(rLab);

                void bump(int delta)
                {
                    policy.ReserveStock = policy.ReserveStock + delta;
                    Refresh();
                }
                var minus = new Button(() => bump(-1)) { text = "−" };
                var plus  = new Button(() => bump(+1)) { text = "+" };
                StyleBtn(minus); StyleBtn(plus);
                minus.style.width = 34; plus.style.width = 34;
                minus.style.marginBottom = 0; plus.style.marginBottom = 0;
                minus.style.marginRight = 4;
                row.Add(minus); row.Add(plus);
                panel.Add(row);

                var hint = new Label(policy.ConserveAmmo
                    ? "Auto-fire stops at the reserve. Manual artillery cockpit still fires."
                    : "Reserve ignored while Conserve Ammo is off.");
                hint.style.color = new Color(0.7f, 0.75f, 0.8f);
                hint.style.fontSize = 10;
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.marginBottom = 4;
                panel.Add(hint);
            }

            // Engagement range + firing arc.
            if (defense is VoxelEngine.Combat.IDefenseEngagement eng)
            {
                panel.Add(MakeSubtitle("Engagement"));
                float maxR = Mathf.Max(2f, eng.MaxRange);
                var rangeLab = new Label($"Range: {eng.EngagementRange:0} m  (max {maxR:0})");
                rangeLab.style.color = Color.white; rangeLab.style.fontSize = 11; rangeLab.style.marginBottom = 2;
                panel.Add(rangeLab);
                var rangeSlider = new Slider(2f, maxR) { value = eng.EngagementRange };
                rangeSlider.style.marginBottom = 8;
                rangeSlider.RegisterValueChangedCallback(e =>
                {
                    eng.EngagementRange = e.newValue;
                    rangeLab.text = $"Range: {eng.EngagementRange:0} m  (max {maxR:0})";
                });
                panel.Add(rangeSlider);

                var arcLab = new Label(eng.FiringArcDegrees >= 359.5f
                    ? "Arc: 360° (omni)"
                    : $"Arc: {eng.FiringArcDegrees:0}°");
                arcLab.style.color = Color.white; arcLab.style.fontSize = 11; arcLab.style.marginBottom = 2;
                panel.Add(arcLab);
                var arcSlider = new Slider(15f, 360f) { value = eng.FiringArcDegrees };
                arcSlider.style.marginBottom = 6;
                arcSlider.RegisterValueChangedCallback(e =>
                {
                    eng.FiringArcDegrees = e.newValue;
                    arcLab.text = eng.FiringArcDegrees >= 359.5f
                        ? "Arc: 360° (omni)"
                        : $"Arc: {eng.FiringArcDegrees:0}°";
                });
                panel.Add(arcSlider);

                var eHint = new Label("Arc is centred on the turret's placed facing. 360° = all directions.");
                eHint.style.color = new Color(0.7f, 0.75f, 0.8f);
                eHint.style.fontSize = 10;
                eHint.style.whiteSpace = WhiteSpace.Normal;
                eHint.style.marginBottom = 4;
                panel.Add(eHint);
            }
        }

        private static VoxelEngine.Combat.TargetFilter GetDefenseFilter(Component d)
        {
            if (d is VoxelEngine.Combat.Artillery a) return a.filter;
            if (d is VoxelEngine.Combat.FlamethrowerTurret f) return f.filter;
            if (d is VoxelEngine.Combat.MortarTurret m) return m.filter;
            if (d is VoxelEngine.Combat.GiantShellTurret g) return g.filter;
            if (d is VoxelEngine.Combat.AntiAirTurret aa) return aa.filter;
            if (d is VoxelEngine.Combat.EnergyRelicTurret e) return e.filter;
            if (d is VoxelEngine.Combat.Turret t) return t.filter;
            return VoxelEngine.Combat.TargetFilter.Enemies;
        }

        private static void SetDefenseFilter(Component d, VoxelEngine.Combat.TargetFilter f)
        {
            if (d is VoxelEngine.Combat.Artillery a) a.filter = f;
            else if (d is VoxelEngine.Combat.FlamethrowerTurret fl) fl.filter = f;
            else if (d is VoxelEngine.Combat.MortarTurret m) m.filter = f;
            else if (d is VoxelEngine.Combat.GiantShellTurret g) g.filter = f;
            else if (d is VoxelEngine.Combat.AntiAirTurret aa) aa.filter = f;
            else if (d is VoxelEngine.Combat.EnergyRelicTurret e) e.filter = f;
            else if (d is VoxelEngine.Combat.Turret t) t.filter = f;
        }

        private static bool GetDefenseAuto(Component d)
        {
            if (d is VoxelEngine.Combat.Artillery a) return a.autoMode;
            if (d is VoxelEngine.Combat.FlamethrowerTurret f) return f.autoMode;
            if (d is VoxelEngine.Combat.MortarTurret m) return m.autoMode;
            if (d is VoxelEngine.Combat.GiantShellTurret g) return g.autoMode;
            if (d is VoxelEngine.Combat.AntiAirTurret aa) return aa.autoMode;
            if (d is VoxelEngine.Combat.EnergyRelicTurret e) return e.autoMode;
            if (d is VoxelEngine.Combat.Turret t) return t.autoMode;
            return true;
        }

        private static void SetDefenseAuto(Component d, bool v)
        {
            if (d is VoxelEngine.Combat.Artillery a) a.autoMode = v;
            else if (d is VoxelEngine.Combat.FlamethrowerTurret f) f.autoMode = v;
            else if (d is VoxelEngine.Combat.MortarTurret m) m.autoMode = v;
            else if (d is VoxelEngine.Combat.GiantShellTurret g) g.autoMode = v;
            else if (d is VoxelEngine.Combat.AntiAirTurret aa) aa.autoMode = v;
            else if (d is VoxelEngine.Combat.EnergyRelicTurret e) e.autoMode = v;
            else if (d is VoxelEngine.Combat.Turret t) t.autoMode = v;
        }

        private Toggle MakeDefenseToggle(string label, VoxelEngine.Combat.TargetFilter flag,
                                         VoxelEngine.Combat.TargetFilter cur, Component defense)
        {
            var t = new Toggle(label) { value = (cur & flag) != 0 };
            t.style.color = Color.white; t.style.marginBottom = 2;
            t.RegisterValueChangedCallback(e =>
            {
                var f = GetDefenseFilter(defense);
                if (e.newValue) f |= flag; else f &= ~flag;
                SetDefenseFilter(defense, f);
                Refresh();
            });
            return t;
        }

        private void StyleBtn(Button b)
        {
            b.style.minHeight = 26; b.style.fontSize = 11; b.style.color = Color.white;
            b.style.unityFontStyleAndWeight = FontStyle.Bold; b.style.marginBottom = 4;
            SetBorderRadius(b, 4); ZeroBorder(b);
        }

        private void BuildLeftArea(VisualElement root)
        {
            var row = new VisualElement();
            row.style.position = Position.Absolute;
            row.style.top = 12; row.style.bottom = 72;
            row.style.left = 12;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.pickingMode = PickingMode.Ignore;   // empty gaps fall through to the dimmed backdrop
            root.Add(row);

            BuildLeftPanel(row);   // inventory (flex child — fills the row height)

            // The selected equipment module remains a reachable drop target, but now
            // reads as a mechanically coupled extension of the inventory terminal.
            row.Add(BuildInventoryAddonCoupler());
            row.Add(BuildEquipmentPanel());
        }

        // True when another inventory-backed panel is open. Equipment bays remain
        // visible, but this flag stops Shift-click from auto-equipping instead of
        // routing an item into the active chest, station, or machine.
        private bool AnyCenterOrRightPanelOpen()
        {
            if (CraftingScreen.Visible) return true;
            if (_productionStatsOpen || _recipeBrowserOpen) return true;
            if (_openGridTerminal != null) return true;
            return _rightContainer != null || _openFurnace != null || _openElectric != null ||
                   _openCoalGen != null || _openQuarry != null || _openReactor != null ||
                   _openTurbine != null || _openPortReactor != null || _openProcessor != null ||
                   _openReprocessor != null || _openElectrolyser != null || _openHydroEngine != null ||
                   _openGasTank != null || _openWaterPump != null || _openBiofarm != null ||
                   _openWindTurbine != null || _openStorageTerminal != null || _openServerRack != null ||
                   _openPatternTerminal != null || _openCraftTerminal != null || _openImporter != null ||
                   _openExporter != null || _openDiskManipulator != null || _openNAS != null ||
                   _openPowerstation != null || _openStorageDrawer != null || _openDrawerController != null ||
                   _openItemDisplay != null || _openCrusher != null || _openAssembler != null ||
                   _openFunnel != null || _openSplitter != null || _openGridBlock != null ||
                   _openOilRefinery != null || _openChemPlant != null || _openStation != null ||
                   _openArmorUpgradeStation != null || _openVoltageStation != null || _openDefense != null;
        }

        // The equipment console is a physical extension of the inventory terminal.
        // All three modules stay open together in one scroll-safe add-on, so every
        // equipment slot remains visible and reachable without a tab swap.
        private VisualElement BuildEquipmentPanel()
        {
            _jbStatusPill = null;
            _jbStatus = null;
            _jbH2Row = _jbH2Fill = null;
            _jbH2Label = null;
            _jbPRow = _jbPFill = null;
            _jbPLabel = null;
            _jbPackRows = null;
            _jbPackH2 = null;
            _jbPackPwr = null;

            var addon = new VisualElement { name = "InventoryEquipmentAddon" };
            addon.style.width = 230;
            addon.style.height = new StyleLength(new Length(100f, LengthUnit.Percent));
            addon.style.flexShrink = 0;
            addon.style.paddingTop = 6;
            addon.style.paddingBottom = 6;
            addon.style.paddingLeft = 6;
            addon.style.paddingRight = 6;
            addon.style.overflow = Overflow.Hidden;
            LcdHudTheme.ApplyChassis(addon, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.98f), 2f);

            var screen = new VisualElement { name = "EquipmentAddonLcdScreen" };
            screen.style.flexDirection = FlexDirection.Column;
            screen.style.flexGrow = 1;
            screen.style.minHeight = 0;
            screen.style.paddingTop = 6;
            screen.style.paddingBottom = 6;
            screen.style.paddingLeft = 6;
            screen.style.paddingRight = 6;
            screen.style.overflow = Overflow.Hidden;
            LcdHudTheme.ApplyScreen(screen, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.90f), 1f);
            addon.Add(screen);

            screen.Add(LcdHudTheme.CreateDisplayHeader("PERSONAL SYSTEMS", "EQUIPMENT", "AUX-01", "3 MODULES"));

            var allModules = LcdHudTheme.CaptionLabel("ARMOR  //  JETPACK  //  LIFE SUPPORT");
            allModules.style.marginLeft = 3;
            allModules.style.marginBottom = 4;
            screen.Add(allModules);

            // Thomas asked for the add-on to stay open as one connected console.
            // A scroll-safe stack keeps every real equipment slot visible without
            // forcing a tab swap or hiding a drag/drop destination.
            var content = new ScrollView(ScrollViewMode.Vertical) { name = "EquipmentAddonContent" };
            content.style.flexGrow = 1;
            content.style.minHeight = 0;
            content.style.paddingRight = 2;
            UITheme.StyleScroller(content, LcdHudTheme.Phosphor);
            screen.Add(content);

            var equipment = inventory != null ? inventory.GetComponent<VoxelEngine.Player.PlayerEquipment>() : null;
            if (equipment == null)
            {
                var unavailable = new VisualElement();
                unavailable.style.paddingTop = 10;
                unavailable.style.paddingBottom = 10;
                unavailable.style.paddingLeft = 8;
                unavailable.style.paddingRight = 8;
                LcdHudTheme.ApplyDataCard(unavailable, LcdHudTheme.Bezel);
                unavailable.Add(LcdHudTheme.CaptionLabel("SYSTEM STATUS"));
                var message = new Label("EQUIPMENT BUS OFFLINE");
                message.style.fontSize = 10;
                message.style.letterSpacing = 0.8f;
                message.style.unityFontStyleAndWeight = FontStyle.Bold;
                message.style.color = new StyleColor(UITheme.AccentAmber);
                unavailable.Add(message);
                content.Add(unavailable);
            }
            else
            {
                content.Add(BuildArmorAddon(equipment));
                content.Add(BuildJetpackSlotsPanel(equipment));
                content.Add(BuildLifeSupportSlotsPanel(equipment));
            }

            LcdHudTheme.AddScanlines(screen, 8, 48f, 58f);
            return addon;
        }




        private VisualElement BuildArmorAddon(VoxelEngine.Player.PlayerEquipment equipment)
        {
            var module = new VisualElement { name = "ArmorAddonModule" };
            module.style.paddingTop = 7;
            module.style.paddingBottom = 7;
            module.style.paddingLeft = 7;
            module.style.paddingRight = 7;
            LcdHudTheme.ApplyDataCard(module, LcdHudTheme.Phosphor);

            var moduleTitle = LcdHudTheme.CaptionLabel("ARMOR SHELL / SLOT 01");
            moduleTitle.style.marginBottom = 5;
            module.Add(moduleTitle);

            var slotHost = new VisualElement();
            slotHost.style.flexDirection = FlexDirection.Row;
            slotHost.style.justifyContent = Justify.Center;
            slotHost.style.marginBottom = 7;
            slotHost.Add(BuildSlot(equipment.ArmorSlots, 0, equipment.ArmorSlots.GetSlot(0), false));
            module.Add(slotHost);

            var armor = equipment.EquippedArmor;
            if (armor == null)
            {
                var empty = new Label("NO ARMOR MODULE INSTALLED");
                empty.style.fontSize = 9;
                empty.style.letterSpacing = 0.8f;
                empty.style.unityFontStyleAndWeight = FontStyle.Bold;
                empty.style.color = new StyleColor(LcdHudTheme.PhosphorDim);
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.marginTop = 4;
                module.Add(empty);
                return module;
            }

            var status = new VisualElement();
            status.style.paddingTop = 5;
            status.style.paddingBottom = 5;
            status.style.paddingLeft = 6;
            status.style.paddingRight = 6;
            status.style.marginBottom = 6;
            LcdHudTheme.ApplyScreen(status, new Color(LcdHudTheme.Phosphor.r, LcdHudTheme.Phosphor.g, LcdHudTheme.Phosphor.b, 0.62f), 1f);

            var shell = new Label($"TIER {armor.tier}  //  {armor.damageReduction * 100f:0}% DAMAGE REDUCTION");
            shell.style.fontSize = 9;
            shell.style.letterSpacing = 0.45f;
            shell.style.unityFontStyleAndWeight = FontStyle.Bold;
            shell.style.color = new StyleColor(LcdHudTheme.Phosphor);
            shell.style.whiteSpace = WhiteSpace.Normal;
            status.Add(shell);
            module.Add(status);

            var upgrades = new VisualElement();
            upgrades.style.paddingTop = 5;
            upgrades.style.paddingBottom = 5;
            upgrades.style.paddingLeft = 6;
            upgrades.style.paddingRight = 6;
            LcdHudTheme.ApplyScreen(upgrades, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.82f), 1f);
            upgrades.Add(BuildAddonReadoutLine("HEAT", $"T{equipment.GetArmorUpgradeTier(VoxelEngine.Combat.ArmorUpgradeKind.HeatTolerance)}"));
            upgrades.Add(BuildAddonReadoutLine("RADIATION", $"T{equipment.GetArmorUpgradeTier(VoxelEngine.Combat.ArmorUpgradeKind.RadiationShielding)}"));
            upgrades.Add(BuildAddonReadoutLine("OXYGEN", $"T{equipment.GetArmorUpgradeTier(VoxelEngine.Combat.ArmorUpgradeKind.OxygenEfficiency)}"));
            upgrades.Add(BuildAddonReadoutLine("IMPACT", $"T{equipment.GetArmorUpgradeTier(VoxelEngine.Combat.ArmorUpgradeKind.FallImpact)}"));
            upgrades.Add(BuildAddonReadoutLine("MOBILITY", $"T{equipment.GetArmorUpgradeTier(VoxelEngine.Combat.ArmorUpgradeKind.Mobility)}"));
            if (equipment.HasHazmatProtection)
                upgrades.Add(BuildAddonReadoutLine("SEAL", "HAZMAT"));
            module.Add(upgrades);
            return module;
        }

        private static VisualElement BuildAddonReadoutLine(string labelText, string valueText)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;

            var label = LcdHudTheme.CaptionLabel(labelText);
            label.style.flexGrow = 1;
            row.Add(label);

            var value = new Label(valueText);
            value.style.fontSize = 8;
            value.style.letterSpacing = 0.75f;
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            value.style.color = new StyleColor(LcdHudTheme.Phosphor);
            value.pickingMode = PickingMode.Ignore;
            row.Add(value);
            return row;
        }

        private static VisualElement BuildInventoryAddonCoupler()
        {
            var coupler = new VisualElement { name = "InventoryAddonCoupler" };
            coupler.style.width = 12;
            coupler.style.height = new StyleLength(new Length(100f, LengthUnit.Percent));
            coupler.style.flexShrink = 0;
            coupler.pickingMode = PickingMode.Ignore;

            for (int i = 0; i < 2; i++)
            {
                var bridge = new VisualElement();
                bridge.style.position = Position.Absolute;
                bridge.style.left = 0;
                bridge.style.right = 0;
                bridge.style.top = new StyleLength(new Length(i == 0 ? 43f : 55f, LengthUnit.Percent));
                bridge.style.height = 4;
                bridge.style.backgroundColor = new StyleColor(LcdHudTheme.Bezel);
                UITheme.Radius(bridge, 1f);
                coupler.Add(bridge);
            }
            return coupler;
        }

        private void BuildLeftPanel(VisualElement parent)
        {
            var panel = MakePanel();
            panel.name = "InventoryConsoleChassis";
            LcdHudTheme.ApplyChassis(panel, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.98f), 2f);
            // This is the primary screen half of an inventory terminal. The equipment
            // module attaches through a short physical coupler in BuildLeftArea.
            panel.style.flexShrink = 1;
            panel.style.flexGrow = 0;
            panel.style.width = new StyleLength(new Length(32f, LengthUnit.Percent));
            panel.style.minWidth = 252;
            panel.style.maxWidth = new StyleLength(new Length(42f, LengthUnit.Percent));
            panel.style.height = new StyleLength(new Length(100f, LengthUnit.Percent));
            panel.style.paddingTop = 6;
            panel.style.paddingBottom = 6;
            panel.style.paddingLeft = 6;
            panel.style.paddingRight = 6;
            panel.style.overflow = Overflow.Hidden;
            parent.Add(panel);

            var display = new VisualElement { name = "InventoryLcdDisplay" };
            display.style.flexDirection = FlexDirection.Column;
            display.style.flexGrow = 1;
            display.style.minHeight = 0;
            display.style.paddingTop = 6;
            display.style.paddingBottom = 6;
            display.style.paddingLeft = 6;
            display.style.paddingRight = 6;
            display.style.overflow = Overflow.Hidden;
            LcdHudTheme.ApplyScreen(display, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.92f), 1f);
            panel.Add(display);

            // The word INVENTORY lives on the display itself; no decorative heraldry
            // or detached title treatment remains around the console.
            display.Add(LcdHudTheme.CreateDisplayHeader("PERSONAL STORAGE", "INVENTORY", "INV-01", "ONLINE"));

            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "InventoryLcdScroll" };
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            scroll.style.paddingRight = 2;
            UITheme.StyleScroller(scroll, LcdHudTheme.Phosphor);
            display.Add(scroll);

            scroll.Add(BuildInventoryWeightReadout());
            scroll.Add(BuildSortableSlotGrid(inventory.container, Inventory.HOTBAR_SIZE, Inventory.TOTAL_SIZE));

            // Network routing stays inside the same display, immediately below the
            // cargo matrix, rather than forming an unrelated floating card.
            BuildWirelessTransmitterSelector(scroll);
            scroll.Add(BuildInventoryCommandBay());
            BuildWirelessStorageReadout(scroll);

            LcdHudTheme.AddScanlines(display, 9, 44f, 55f);
        }

        private VisualElement BuildInventoryCommandBay()
        {
            var bay = new VisualElement { name = "InventoryCommandBay" };
            bay.style.marginTop = 8;
            bay.style.paddingTop = 6;
            bay.style.paddingBottom = 5;
            bay.style.paddingLeft = 6;
            bay.style.paddingRight = 6;
            LcdHudTheme.ApplyDataCard(bay, LcdHudTheme.Bezel);

            var caption = LcdHudTheme.CaptionLabel("TERMINAL COMMANDS");
            caption.style.marginBottom = 4;
            bay.Add(caption);

            var commands = new VisualElement();
            commands.style.flexDirection = FlexDirection.Row;
            commands.style.flexWrap = Wrap.Wrap;
            bay.Add(commands);

            var crafting = CraftingScreen.ToggleButton(Refresh);
            crafting.style.flexGrow = 1;
            crafting.style.minWidth = 104;
            crafting.style.marginTop = 0;
            crafting.style.marginRight = 3;
            commands.Add(crafting);

            var statsButton = LcdHudTheme.CommandButton(_productionStatsOpen ? "STATS / CLOSE" : "PRODUCTION", () =>
            {
                _productionStatsOpen = !_productionStatsOpen;
                if (_productionStatsOpen) _recipeBrowserOpen = false;
                Refresh();
            }, LcdHudTheme.Phosphor, _productionStatsOpen);
            statsButton.style.flexGrow = 1;
            statsButton.style.minWidth = 104;
            commands.Add(statsButton);

            var recipesButton = LcdHudTheme.CommandButton(_recipeBrowserOpen ? "RECIPES / CLOSE" : "RECIPE ARCHIVE", () =>
            {
                _recipeBrowserOpen = !_recipeBrowserOpen;
                if (_recipeBrowserOpen) _productionStatsOpen = false;
                Refresh();
            }, LcdHudTheme.Phosphor, _recipeBrowserOpen);
            recipesButton.style.flexGrow = 1;
            recipesButton.style.minWidth = 104;
            commands.Add(recipesButton);
            return bay;
        }

        private void BuildWirelessStorageReadout(VisualElement parent)
        {
            var transmitters = VoxelEngine.Storage.WirelessTransmitter.GetAllOnline();
            if (transmitters == null || transmitters.Length == 0) return;

            var module = new VisualElement { name = "InventoryWirelessReadout" };
            module.style.marginTop = 8;
            module.style.paddingTop = 6;
            module.style.paddingBottom = 6;
            module.style.paddingLeft = 6;
            module.style.paddingRight = 6;
            LcdHudTheme.ApplyDataCard(module, LcdHudTheme.Bezel);
            parent.Add(module);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            module.Add(header);
            var label = LcdHudTheme.CaptionLabel("WIRELESS STORAGE BUS");
            label.style.flexGrow = 1;
            header.Add(label);
            var toggle = LcdHudTheme.CommandButton(_showWirelessStorage ? "COLLAPSE" : "EXPAND", () =>
            {
                _showWirelessStorage = !_showWirelessStorage;
                Refresh();
            }, LcdHudTheme.Phosphor, _showWirelessStorage);
            header.Add(toggle);

            if (!_showWirelessStorage) return;

            foreach (var transmitter in transmitters)
            {
                if (transmitter == null || transmitter.ConnectedRack == null) continue;
                var rack = transmitter.ConnectedRack;
                var title = new Label(string.IsNullOrEmpty(transmitter.transmitterName)
                    ? "NETWORK NODE"
                    : transmitter.transmitterName.ToUpperInvariant());
                title.style.fontSize = 9;
                title.style.letterSpacing = 0.8f;
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.color = new StyleColor(LcdHudTheme.Phosphor);
                title.style.marginTop = 6;
                module.Add(title);

                var items = rack.GetAllItems();
                if (items.Count == 0)
                {
                    var empty = LcdHudTheme.CaptionLabel("NO STORED MATERIAL");
                    empty.style.marginTop = 3;
                    module.Add(empty);
                    continue;
                }

                var categories = new Dictionary<string, List<VoxelEngine.Storage.StoredItemEntry>>();
                foreach (var entry in items)
                {
                    string category = "MISC";
                    var definitions = Resources.FindObjectsOfTypeAll<VoxelEngine.Items.ItemDefinition>();
                    foreach (var definition in definitions)
                    {
                        if (definition.itemId == entry.itemId)
                        {
                            category = string.IsNullOrEmpty(definition.category) ? "MISC" : definition.category.ToUpperInvariant();
                            break;
                        }
                    }
                    if (!categories.TryGetValue(category, out var bucket))
                    {
                        bucket = new List<VoxelEngine.Storage.StoredItemEntry>();
                        categories[category] = bucket;
                    }
                    bucket.Add(entry);
                }

                foreach (var category in categories)
                {
                    var categoryTitle = LcdHudTheme.CaptionLabel(category.Key);
                    categoryTitle.style.marginTop = 4;
                    module.Add(categoryTitle);
                    foreach (var entry in category.Value)
                    {
                        var row = new VisualElement();
                        row.style.flexDirection = FlexDirection.Row;
                        row.style.paddingTop = 2;
                        row.style.paddingBottom = 2;
                        row.style.paddingLeft = 4;
                        row.style.paddingRight = 4;
                        LcdHudTheme.ApplyScreen(row, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.72f), 1f);

                        var name = new Label(entry.displayName);
                        name.style.flexGrow = 1;
                        name.style.fontSize = 9;
                        name.style.color = new StyleColor(LcdHudTheme.Caption);
                        name.style.overflow = Overflow.Hidden;
                        name.style.textOverflow = TextOverflow.Ellipsis;
                        name.style.whiteSpace = WhiteSpace.NoWrap;
                        row.Add(name);

                        var count = new Label($"×{entry.count:N0}");
                        count.style.fontSize = 9;
                        count.style.unityFontStyleAndWeight = FontStyle.Bold;
                        count.style.color = new StyleColor(LcdHudTheme.Phosphor);
                        row.Add(count);
                        module.Add(row);
                    }
                }
            }
        }


        // ----------------------------------------------------------------
        //  CENTER CRAFTING PANEL — the crafting surface.
        //  Sits between the left inventory and the right container, shown
        //  only when CraftingScreen.Visible is true. Its open/closed state
        //  persists across sessions; the player toggles it from the
        //  inventory header (CraftingScreen.ToggleButton).
        // ----------------------------------------------------------------
        private void BuildCenterCrafting(VisualElement root, bool rightPanelOpen)
        {
            if (inventory == null) return;

            var (recipes, source, _) = ResolveCraftContext();

            var panel = MakePanel();
            panel.style.position = Position.Absolute;
            // When the ship terminal is open the terminal fills the right portion of the
            // screen, so the crafting screen floats as a CENTERED modal ON TOP of everything
            // (it is added to the root last → highest z-order). Otherwise it docks in the
            // usual center column between the inventory and any right-side panel.
            bool terminalOpen = _openGridTerminal != null;
            if (terminalOpen)
            {
                panel.style.top    = 60;
                panel.style.bottom = 60;
                panel.style.left   = new StyleLength(new Length(22, LengthUnit.Percent));
                panel.style.right  = new StyleLength(new Length(22, LengthUnit.Percent));
                panel.style.maxWidth = 820;
                // Strong opaque backing + shadow-ish border so it reads as the topmost layer.
                panel.style.backgroundColor = new StyleColor(new Color(0.05f, 0.06f, 0.09f, 0.98f));
            }
            else
            {
                panel.style.top      = 12;
                panel.style.bottom   = 72;
                panel.style.left     = new StyleLength(new Length(34f, LengthUnit.Percent));
                // Keep the center panel in the safe gap between the responsive
                // inventory and right-side machine panels.
                panel.style.right    = rightPanelOpen
                    ? new StyleLength(new Length(34f, LengthUnit.Percent))
                    : 12;
                panel.style.minWidth = 0;
                panel.style.maxWidth = new StyleLength(new Length(52f, LengthUnit.Percent));
            }
            panel.style.overflow = Overflow.Hidden;
            root.Add(panel);

            // Subtle entrance pop — only the FIRST time the panel appears (i.e. the
            // player just toggled it on), NOT on every Refresh() rebuild. Without
            // this guard the panel would flash on each craft / queue tick.
            if (!_craftPanelWasVisible)
            {
                panel.style.opacity = 0f;
                panel.style.scale   = new StyleScale(new Scale(new Vector3(0.985f, 0.985f, 1f)));
                panel.schedule.Execute(() =>
                {
                    panel.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "opacity", "scale" };
                    panel.style.transitionDuration = new System.Collections.Generic.List<TimeValue>
                        { new TimeValue(0.14f, TimeUnit.Second), new TimeValue(0.14f, TimeUnit.Second) };
                    panel.style.opacity = 1f;
                    panel.style.scale   = new StyleScale(new Scale(Vector3.one));
                }).ExecuteLater(0);
            }

            CraftingScreen.Populate(
                panel, recipes, source, inventory.container,
                resolveQueue: r =>
                {
                    if (_activeQueue != null) return _activeQueue;
                    if (r != null && r.requiredStation != Crafting.StationTier.None)
                        return FindNearestQueueForTier(r.requiredStation, inventory.transform.position);
                    return null;
                },
                refresh: Refresh,
                setSearchFocus: v => _searchHasFocus = v,
                panelId: "inventory");
        }

        /// <summary>
        /// Computes the recipe set + ingredient source the inventory-side crafting
        /// screen should use, honouring storage-network access and station tiers.
        /// Extracted from the old inline crafting block so both the center panel
        /// and any future caller can reuse the exact same priority rules.
        /// </summary>
        private (System.Collections.Generic.List<Crafting.RecipeDefinition> recipes, IItemContainer source, Crafting.StationTier maxStation) ResolveCraftContext()
        {
            // ── Crafting source priority (per user spec) ─────────────
            //   1) If the player has opened a Storage Terminal (wired OR wireless),
            //      OR is in the inventory with an active wireless transmitter, we
            //      treat the storage network as a tier-Assembler crafting station
            //      AND let crafting pull ingredients from inventory FIRST, then
            //      from the network. Gated by the res_storage_crafting research node.
            //   2) Otherwise, crafting only uses the inventory and respects normal
            //      station-tier rules (Crafting Bench / Furnace / Assembler).
            var rmCheck = VoxelEngine.Research.ResearchManager.Instance;
            bool storageCraftingUnlocked = rmCheck != null
                && rmCheck.IsUnlocked("res_storage_crafting");

            VoxelEngine.Storage.ServerRack craftRack = null;
            if (_openStorageTerminal != null && _openStorageTerminal.ConnectedRack != null
                && _openStorageTerminal.ConnectedRack.IsOnline)
                craftRack = _openStorageTerminal.ConnectedRack;

            VoxelEngine.Storage.ServerRack passiveWirelessRack = GetActiveWirelessRack();
            bool wirelessActive = passiveWirelessRack != null && passiveWirelessRack.IsOnline;

            var maxStation = Crafter.MaxAccessibleStation(inventory.transform.position, stationRadius);
            if (storageCraftingUnlocked && (craftRack != null || wirelessActive)
                && (int)maxStation < (int)Crafting.StationTier.Assembler)
                maxStation = Crafting.StationTier.Assembler;

            // Dedicated stations own their own recipe lists. Even while standing
            // beside an Armor Station, the general inventory browser stays capped
            // at Assembler recipes so armor/module production remains intentional.
            if ((int)maxStation > (int)Crafting.StationTier.Assembler)
                maxStation = Crafting.StationTier.Assembler;

            var allRecipes = Crafter.AvailableRecipes(recipeRegistry, maxStation);

            IItemContainer craftSource = inventory.container;
            var craftRackForSource = craftRack ?? (wirelessActive ? passiveWirelessRack : null);
            if (craftRackForSource != null)
                craftSource = new VoxelEngine.Storage.NetworkItemSource(inventory.container, craftRackForSource);

            return (allRecipes, craftSource, maxStation);
        }

        // ----------------------------------------------------------------
        // Reusable recipe browser: search bar + category tabs + recipe list.
        // Used by the player inventory pane AND the workstation right pane.
        // ----------------------------------------------------------------
        // True while a TextField inside the inventory has keyboard focus.
        // Set by the search field. Read by Update() to suppress hotkey/closing handling.
        private bool _searchHasFocus;
        private bool _showWirelessStorage;
        // True while the center crafting panel is currently mounted — lets us play
        // the entrance animation only on first appearance, not on every Refresh().
        private bool _craftPanelWasVisible;
        // Prevents I from re-opening inventory the same frame it closed a machine panel.
        private bool _justClosedThisFrame;

        /// <summary>
        /// Wireless transmitter dropdown — appears in the inventory panel whenever
        /// any transmitter is online. Lets the player route storage actions through
        /// a chosen network (or "Auto" = nearest). Selection persists via PlayerPrefs.
        /// </summary>
        private void BuildWirelessTransmitterSelector(VisualElement parent)
        {
            var all = VoxelEngine.Storage.WirelessTransmitter.GetAllOnline();
            if (all == null || all.Length == 0) return;

            parent.Add(Spacer(8));
            var row = new VisualElement { name = "InventoryNetworkRoute" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;
            row.style.paddingTop = 5;
            row.style.paddingBottom = 5;
            row.style.paddingLeft = 7;
            row.style.paddingRight = 7;
            LcdHudTheme.ApplyDataCard(row, LcdHudTheme.Bezel);
            parent.Add(row);

            var lbl = LcdHudTheme.CaptionLabel("NETWORK ROUTE");
            lbl.style.marginRight = 7;
            row.Add(lbl);

            // Build the list of "Auto" + every online transmitter's name.
            var names = new System.Collections.Generic.List<string> { "Auto (nearest)" };
            foreach (var t in all)
            {
                if (t == null) continue;
                var n = string.IsNullOrEmpty(t.transmitterName) ? "Wireless Network" : t.transmitterName;
                if (!names.Contains(n)) names.Add(n);
            }

            var dd = new DropdownField(names, 0);
            dd.style.flexGrow = 1;
            dd.style.minHeight = 26;
            dd.style.backgroundColor = new StyleColor(LcdHudTheme.GlassDark);
            dd.style.color = new StyleColor(LcdHudTheme.Phosphor);
            UITheme.Radius(dd, 1f);
            UITheme.Border(dd, 1f, LcdHudTheme.Bezel);

            // Reflect current selection.
            string current = string.IsNullOrEmpty(_selectedTransmitterName) ? "Auto (nearest)" : _selectedTransmitterName;
            int idx = names.IndexOf(current);
            dd.SetValueWithoutNotify(idx >= 0 ? current : "Auto (nearest)");

            dd.RegisterValueChangedCallback(e =>
            {
                string chosen = e.newValue == "Auto (nearest)" ? "" : e.newValue;
                SetSelectedTransmitter(chosen);
            });
            row.Add(dd);

            // Status hint — shows which rack the active transmitter is pointed at.
            var rack = GetActiveWirelessRack();
            var statusTxt = rack != null && rack.IsOnline
                ? $"  \u2713 online ({rack.TotalStored:N0}/{rack.TotalCapacity:N0} GB)"
                : "  \u26A0 offline";
            var status = new Label(statusTxt);
            status.style.color = new StyleColor(rack != null && rack.IsOnline ? LcdHudTheme.Phosphor : UITheme.AccentRed);
            status.style.fontSize = 8;
            status.style.letterSpacing = 0.45f;
            status.style.marginLeft = 6;
            status.pickingMode = PickingMode.Ignore;
            row.Add(status);
        }

        private void BuildRecipeBrowser(VisualElement parent,
            System.Collections.Generic.List<Crafting.RecipeDefinition> recipes,
            IItemContainer source, IItemContainer dest, string emptyMessage, string panelId)
        {
            if (recipes == null || recipes.Count == 0)
            {
                parent.Add(MakeMutedLabel(emptyMessage));
                return;
            }

            var st = GetBrowserState(panelId);
            string localSearch   = st.search;
            string localCategory = st.category;

            // Collect unique categories from output items.
            var cats = new System.Collections.Generic.SortedSet<string>();
            cats.Add("All");
            foreach (var r in recipes)
            {
                if (r == null || r.outputItem == null) continue;
                cats.Add(string.IsNullOrEmpty(r.outputItem.category) ? "Misc" : r.outputItem.category);
            }

            // --- Search bar ---
            var searchField = new TextField { value = localSearch };
            searchField.style.minHeight = 28;
            searchField.style.marginTop = 6;
            searchField.style.marginBottom = 4;

            var searchRow = new VisualElement(); searchRow.style.flexDirection = FlexDirection.Row;
            var searchLabel = new Label("\u26B7 Search:");
            searchLabel.style.color = new StyleColor(new Color(0.78f, 0.82f, 0.9f));
            searchLabel.style.fontSize = 12;
            searchLabel.style.alignSelf = Align.Center;
            searchLabel.style.marginRight = 6;
            searchLabel.pickingMode = PickingMode.Ignore;
            searchRow.Add(searchLabel);
            searchField.style.flexGrow = 1;
            searchRow.Add(searchField);
            parent.Add(searchRow);

            // --- Category tabs ---
            var tabRow = new VisualElement();
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.flexWrap = Wrap.Wrap;
            tabRow.style.marginBottom = 6;
            parent.Add(tabRow);

            // --- Recipe list container — persists scroll offset across full Refresh() rebuilds ---
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(scroll);   // themed slim scrollbar
            scroll.style.flexGrow = 1;
            scroll.style.marginTop = 4;
            parent.Add(scroll);
            // Restore saved scroll position immediately after layout.
            if (_browserScrollY.TryGetValue(panelId, out float restoredY) && restoredY > 0f)
                scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0, restoredY)).ExecuteLater(0);

            // ----- helpers that update only the recipe list (not the whole UI) -----
            void RebuildTabs()
            {
                tabRow.Clear();
                foreach (var c in cats)
                {
                    bool active = (c == localCategory);
                    string label = c;
                    var btn = new Button(() => {
                        localCategory = label;
                        SetBrowserState(panelId, localSearch, localCategory);
                        _browserScrollY[panelId] = 0f;  // reset scroll on category change
                        RebuildTabs(); RebuildList();
                    }) { text = label };
                    btn.style.minHeight = 24;
                    btn.style.fontSize  = 11;
                    btn.style.marginRight = 4;
                    btn.style.marginBottom = 4;
                    btn.style.paddingLeft = 8; btn.style.paddingRight = 8;
                    btn.style.color = Color.white;
                    btn.style.backgroundColor = new StyleColor(active
                        ? new Color(0.20f, 0.50f, 0.85f)
                        : new Color(0.18f, 0.20f, 0.26f));
                    SetBorderRadius(btn, 3);
                    ZeroBorder(btn);
                    tabRow.Add(btn);
                }
            }

            void RebuildList()
            {
                // Persist scroll Y before clearing so it survives full Refresh() rebuilds too.
                float curY = scroll.scrollOffset.y;
                if (curY > 0f) _browserScrollY[panelId] = curY;
                scroll.Clear();
                string q = (localSearch ?? "").Trim().ToLowerInvariant();
                int shown = 0;
                foreach (var r in recipes)
                {
                    if (r == null) continue;
                    string cat = (r.outputItem != null && !string.IsNullOrEmpty(r.outputItem.category))
                        ? r.outputItem.category : "Misc";
                    if (localCategory != "All" && cat != localCategory) continue;
                    if (q.Length > 0)
                    {
                        string n = r.GetName().ToLowerInvariant();
                        if (!n.Contains(q)) continue;
                    }
                    scroll.Add(BuildRecipeRow(r, source, dest));
                    shown++;
                }
                if (shown == 0) scroll.Add(MakeMutedLabel("No recipes match your filter."));
                // Restore scroll after DOM rebuild (uses the value we just persisted above).
                if (_browserScrollY.TryGetValue(panelId, out float sy) && sy > 0f)
                    scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0, sy)).ExecuteLater(0);
            }

            // Track when the search field has focus so Update() can suppress hotkeys.
            searchField.RegisterCallback<FocusInEvent>(_  => _searchHasFocus = true);
            searchField.RegisterCallback<FocusOutEvent>(_ => _searchHasFocus = false);

            // Live filter — only rebuilds the LIST so the field keeps focus.
            searchField.RegisterValueChangedCallback(e =>
            {
                localSearch = e.newValue;
                SetBrowserState(panelId, localSearch, localCategory);
                RebuildList();
            });

            // Also: pressing Escape inside the search field unfocuses it (player expectation).
            searchField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    searchField.Blur();
                    e.StopPropagation();
                }
            });

            RebuildTabs();
            RebuildList();
        }

        // ----- RIGHT (chest) -----
        private void BuildRightContainer(VisualElement root, IItemContainer c)
        {
            var panel = MakePanel();
            panel.style.position = Position.Absolute;
            panel.style.top = 24; panel.style.bottom = 92;
            panel.style.right = 18;
            panel.style.width = new StyleLength(new Length(30f, LengthUnit.Percent));
            panel.style.minWidth = 320;
            panel.style.maxWidth = 520;   // room for the 2-column item-port grid
            root.Add(panel);

            panel.Add(MakeTitle(c.Name));

            // Scroll so the slot grid + advanced port config both fit on small panels.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(scroll);   // themed slim scrollbar
            scroll.style.flexGrow = 1;
            // Force the scroll CONTENT container to fill the panel width — otherwise
            // it sizes to content and the 50%-wide port cards collapse into one
            // column. This is the key to the 2-column item-port grid rendering.
            scroll.contentContainer.style.width = Length.Percent(100);
            scroll.contentContainer.style.flexGrow = 1;
            panel.Add(scroll);

            scroll.Add(BuildSortableSlotGrid(c));

            // ── Advanced Item-Port configuration (chests) ───────────────────
            // Uses the same shared collapsible widget every machine uses.
            if (_openChest != null)
                AppendItemPorts(scroll, _openChest);
        }


        /// <summary>
        /// Append the shared collapsible Item-Ports widget to a machine panel when
        /// the machine implements <see cref="VoxelEngine.Transport.IItemPortHost"/>.
        /// Works for ANY machine (furnace, processor, …) — one call, full feature set.
        /// </summary>
        private void AppendItemPorts(VisualElement panel, MonoBehaviour machine)
        {
            if (machine == null) return;
            var host = machine.GetComponent<VoxelEngine.Transport.IItemPortHost>();
            if (host == null) return;
            var routing = machine.GetComponent<VoxelEngine.Transport.ItemPortRouting>();
            if (routing == null) routing = machine.gameObject.AddComponent<VoxelEngine.Transport.ItemPortRouting>();

            var divider = new VisualElement();
            divider.style.height = 1;
            divider.style.marginTop = 12; divider.style.marginBottom = 8;
            divider.style.backgroundColor = new StyleColor(UITheme.BorderSubtle);
            panel.Add(divider);

            // The grid now opens as a full overlay ABOVE the machine UI instead of
            // being crammed inside the (clipped) panel — keeps every machine tidy.
            panel.Add(MakePortsToggle(false, () => OpenItemPortsOverlay(host, routing)));
        }

        private void CloseItemPortsOverlay(bool refreshAfterClose = false)
        {
            ItemFilterDialog.CloseActive();
            PortConfigHud.IsAnyDropdownOpen = false;
            if (_itemPortsOverlay != null && _itemPortsOverlay.parent != null)
                _itemPortsOverlay.RemoveFromHierarchy();
            _itemPortsOverlay = null;
            if (refreshAfterClose) Refresh();
        }

        /// <summary>
        /// Open the Item-Ports editor as a centered modal overlay on the root UI,
        /// with a near-solid dim backdrop, so it never squashes the machine panel
        /// or overflows the screen. Closes on Escape, the close button, or DONE.
        /// </summary>
        private void OpenItemPortsOverlay(VoxelEngine.Transport.IItemPortHost host,
                                          VoxelEngine.Transport.ItemPortRouting routing)
        {
            if (_root == null || host == null || routing == null) return;
            if (_itemPortsOverlay != null && _itemPortsOverlay.parent != null) return;

            var overlay = new VisualElement { name = "ItemPortsOverlay" };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0; overlay.style.top = 0; overlay.style.right = 0; overlay.style.bottom = 0;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            // IMPORTANT: the backdrop is CLICK-THROUGH so the player can still click
            // items in their inventory to add them to the open filter. Only the card
            // itself captures clicks; closing is via the ✕ / DONE button.
            overlay.pickingMode = PickingMode.Ignore;
            // Suspend the periodic Refresh() so the overlay isn't destroyed under us.
            VoxelEngine.UI.PortConfigHud.IsAnyDropdownOpen = true;

            // A dim layer pinned to the RIGHT side only (over the machine panel),
            // leaving the left inventory clear & readable for click-to-add.
            var dim = new VisualElement();
            dim.style.position = Position.Absolute;
            dim.style.left = 0; dim.style.top = 0; dim.style.right = 0; dim.style.bottom = 0;
            dim.style.backgroundColor = new StyleColor(new Color(0.02f, 0.025f, 0.04f, 0.55f));
            dim.pickingMode = PickingMode.Ignore;
            overlay.Add(dim);

            void Close() => CloseItemPortsOverlay(refreshAfterClose: true);

            // Card container.
            var card = MakePanel();
            card.style.position = Position.Absolute;   // float on the right over the machine UI
            card.style.right = 24;
            card.style.top = 24;
            card.style.width = 560;
            card.style.maxWidth = Length.Percent(60);
            card.style.maxHeight = Length.Percent(92);
            card.style.paddingTop = 18; card.style.paddingBottom = 18;
            card.style.paddingLeft = 20; card.style.paddingRight = 20;
            card.pickingMode = PickingMode.Position;    // capture clicks (don't pass through)
            overlay.Add(card);

            // Header row with close button.
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            head.style.marginBottom = 6;
            var title = MakeTitle("Item Ports");
            title.style.flexGrow = 1;
            head.Add(title);
            var close = new Button { text = "✕" };
            close.style.width = 28; close.style.height = 28; close.style.fontSize = 14;
            close.style.color = new StyleColor(UITheme.TextSecondary);
            close.style.backgroundColor = new StyleColor(UITheme.BgCard);
            SetBorderRadius(close, 6);
            close.clicked += Close;
            head.Add(close);
            card.Add(head);

            // Scrollable body so it never overflows the screen.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(scroll);   // themed slim scrollbar
            scroll.style.flexGrow = 1;
            scroll.contentContainer.style.width = Length.Percent(100);
            card.Add(scroll);

            var body = VoxelEngine.UI.PortConfigHud.BuildItemPorts(host, routing, onChanged: () => { });
            body.style.width = Length.Percent(100);
            scroll.Add(body);

            _itemPortsOverlay = overlay;
            _root.Add(overlay);
        }

        /// <summary>Launcher pill that opens the Item-Ports overlay.</summary>
        private VisualElement MakePortsToggle(bool _, System.Action onClick)
        {
            var accent = UITheme.AccentCyan;
            bool overlayOpen = _itemPortsOverlay != null && _itemPortsOverlay.parent != null;
            var btn = new Button();
            btn.SetEnabled(!overlayOpen);
            btn.style.flexDirection = FlexDirection.Row;
            btn.style.alignItems = Align.Center;
            btn.style.height = 34;
            btn.style.paddingLeft = 12; btn.style.paddingRight = 12;
            btn.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.14f));
            UITheme.Radius(btn, 8f);
            UITheme.Border(btn, 1, new Color(accent.r, accent.g, accent.b, 0.40f));

            var icon = new Label("⚙");
            icon.style.color = new StyleColor(accent);
            icon.style.fontSize = 14;
            icon.style.marginRight = 8;
            icon.pickingMode = PickingMode.Ignore;
            btn.Add(icon);

            var lbl = new Label("ITEM PORTS");
            lbl.style.color = new StyleColor(UITheme.TextPrimary);
            lbl.style.fontSize = 11;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.letterSpacing = 1.2f;
            lbl.style.flexGrow = 1;
            lbl.pickingMode = PickingMode.Ignore;
            btn.Add(lbl);

            var hint = new Label("CONFIGURE  ▸");
            hint.style.color = new StyleColor(UITheme.TextMuted);
            hint.style.fontSize = 9;
            hint.style.unityFontStyleAndWeight = FontStyle.Bold;
            hint.style.letterSpacing = 1f;
            hint.pickingMode = PickingMode.Ignore;
            btn.Add(hint);

            btn.RegisterCallback<PointerEnterEvent>(_e =>
                btn.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.22f)));
            btn.RegisterCallback<PointerLeaveEvent>(_e =>
                btn.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.14f)));
            btn.clicked += () =>
            {
                if (_itemPortsOverlay != null && _itemPortsOverlay.parent != null) return;
                onClick?.Invoke();
            };
            return btn;
        }

        // ----- RIGHT (furnace) -----
        // Cached one-shot pulse value for animated flame icon (driven by Time.unscaledTime).
        private static float FlamePulse() => 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 6f);

        private void BuildRightFurnace(VisualElement root, Furnace f)
        {
            f.EnsureContainers();
            var panel = MakePanel();
            DockRightPanel(panel, 480);
            root.Add(panel);

            // ===== Header: title + status pill =====
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 14;
            var title = MakeTitle("Solid Fuel Furnace");
            title.style.flexGrow = 1;
            headerRow.Add(title);
            var (pill, pillLabel) = MakeStatusPillWithLabel(
                f.IsBurning ? "BURNING" : (f.Current != null ? "OUT OF FUEL" : "IDLE"),
                f.IsBurning ? new Color(0.95f, 0.50f, 0.15f)
                            : (f.Current != null ? new Color(0.70f, 0.30f, 0.20f) : new Color(0.30f, 0.30f, 0.35f)));
            headerRow.Add(pill);
            _liveStatusPill = pill; _liveStatusLabel = pillLabel;
            panel.Add(headerRow);

            // ===== Main row: INPUT  ->  [flame + arrow + progress]  ->  OUTPUT =====
            var mainRow = new VisualElement();
            mainRow.style.flexDirection = FlexDirection.Row;
            mainRow.style.alignItems = Align.Center;
            mainRow.style.justifyContent = Justify.Center;
            mainRow.style.marginBottom = 18;

            // Input column.
            mainRow.Add(MakeLabeledSlot("Input", f.inputC, 0));

            // Middle column: flame + arrow + smelt-progress bar.
            var midCol = new VisualElement();
            midCol.style.alignItems = Align.Center;
            midCol.style.marginLeft = 20; midCol.style.marginRight = 20;
            midCol.style.minWidth = 140;

            // Flame "icon" — a colored rounded box (more reliable than emoji rendering).
            var flame = new VisualElement();
            flame.style.width  = 32; flame.style.height = 32;
            flame.style.backgroundColor = new StyleColor(new Color(0.30f, 0.30f, 0.35f));
            SetBorderRadius(flame, 16);
            flame.pickingMode = PickingMode.Ignore;
            midCol.Add(flame);
            _liveFlame = flame;

            // Smelt progress bar.
            var smeltLabel = new Label("");
            smeltLabel.style.color = new StyleColor(new Color(0.75f, 0.78f, 0.85f));
            smeltLabel.style.fontSize = 11;
            smeltLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            smeltLabel.style.marginTop = 6;
            smeltLabel.style.marginBottom = 4;
            smeltLabel.style.minWidth = 130;
            smeltLabel.style.maxWidth = 130;
            smeltLabel.style.whiteSpace = WhiteSpace.NoWrap;
            midCol.Add(smeltLabel);
            _liveSmeltLabel = smeltLabel;

            var (smeltBar, smeltFill) = MakeProgressBarWithFill(f.SmeltProgress01,
                new Color(0.95f, 0.55f, 0.10f), width: 140, height: 10);
            midCol.Add(smeltBar);
            _liveSmeltFill = smeltFill;

            // Arrow underneath
            var arrow = new Label("⇒");
            arrow.style.fontSize = 22;
            arrow.style.color = new StyleColor(f.Current != null
                ? new Color(0.95f, 0.55f, 0.10f) : new Color(0.4f, 0.4f, 0.45f));
            arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            arrow.style.marginTop = 6;
            arrow.pickingMode = PickingMode.Ignore;
            midCol.Add(arrow);

            mainRow.Add(midCol);

            // Output column.
            mainRow.Add(MakeLabeledSlot("Output", f.outputC, 0));

            panel.Add(mainRow);

            // ===== Divider =====
            panel.Add(MakeDivider());

            // ===== Fuel row =====
            panel.Add(Spacer(12));
            var fuelHeader = new VisualElement();
            fuelHeader.style.flexDirection = FlexDirection.Row;
            fuelHeader.style.alignItems = Align.Center;
            fuelHeader.style.marginBottom = 6;
            var fuelLabel = new Label("Fuel");
            fuelLabel.style.color = new StyleColor(new Color(0.95f, 0.85f, 0.30f));
            fuelLabel.style.fontSize = 14;
            fuelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            fuelLabel.style.flexGrow = 1;
            fuelHeader.Add(fuelLabel);
            var fuelStat = new Label("");
            fuelStat.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.90f));
            fuelStat.style.fontSize = 11;
            fuelHeader.Add(fuelStat);
            _liveFuelStat = fuelStat;
            panel.Add(fuelHeader);

            var fuelRow = new VisualElement();
            fuelRow.style.flexDirection = FlexDirection.Row;
            fuelRow.style.alignItems = Align.Center;
            fuelRow.Add(BuildSlot(f.fuelC, 0, f.fuelC.GetSlot(0), false));

            // Fuel-burndown bar to the right of the slot.
            var fuelBarHolder = new VisualElement();
            fuelBarHolder.style.flexGrow = 1;
            fuelBarHolder.style.marginLeft = 12;
            var (fuelBar, fuelFill) = MakeProgressBarWithFill(f.FuelProgress01,
                new Color(0.95f, 0.55f, 0.10f), width: 0, height: 12, fillFlexGrow: true);
            fuelBarHolder.Add(fuelBar);
            _liveFuelFill = fuelFill;
            fuelRow.Add(fuelBarHolder);
            panel.Add(fuelRow);

            // Footer hint.
            panel.Add(Spacer(14));
            var hint = new Label("Tip: place coal in the Fuel slot. " +
                                 "Wood logs (4s) and planks (3s) also burn — slower than coal (8s).");
            hint.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.65f));
            hint.style.fontSize = 10;
            hint.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(hint);

            // Advanced per-face item ports (Input/Fuel/Output routing + filters).
            AppendItemPorts(panel, f);
        }

        // -------- prettier-UI helpers used by furnace + electric furnace --------
        private VisualElement MakeLabeledSlot(string label, IItemContainer c, int idx)
        {
            var col = new VisualElement();
            col.style.alignItems = Align.Center;
            var l = new Label(label);
            l.style.color = new StyleColor(new Color(0.75f, 0.78f, 0.85f));
            l.style.fontSize = 11;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginBottom = 4;
            col.Add(l);
            // The slot is rendered inside a subtle rounded card.
            var card = new VisualElement();
            card.style.paddingTop = 4; card.style.paddingBottom = 4;
            card.style.paddingLeft = 4; card.style.paddingRight = 4;
            card.style.backgroundColor = new StyleColor(new Color(0.08f, 0.09f, 0.12f));
            SetBorderRadius(card, 6);
            card.Add(BuildSlot(c, idx, c.GetSlot(idx), false));
            col.Add(card);
            return col;
        }

        /// <summary>Shared style for the tiny top-left slot badges (ml / % badges on
        /// tanks, jetpacks, portable batteries).</summary>
        private static void StyleSlotBadge(Label lbl, float top, Color accent)
        {
            lbl.style.position = Position.Absolute;
            lbl.style.left = 3; lbl.style.top = top;
            lbl.style.fontSize = 8;
            lbl.style.color = Color.white;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.60f));
            lbl.style.paddingLeft = 2; lbl.style.paddingRight = 2;
            lbl.style.borderLeftWidth = 2;
            lbl.style.borderLeftColor = new StyleColor(accent);
            SetBorderRadius(lbl, 2);
            lbl.pickingMode = PickingMode.Ignore;
        }

        // ── Progress bar / pill / divider — all routed through UITheme ───────
        private VisualElement MakeItemFillBar(float frac, Color fillColor)
        {
            var bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.left = 4; bar.style.right = 4; bar.style.bottom = 2;
            bar.style.height = 4;
            bar.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.18f, 0.85f));
            SetBorderRadius(bar, 2);
            bar.pickingMode = PickingMode.Ignore;
            var fill = new VisualElement();
            fill.style.height = 4;
            fill.style.width = new StyleLength(new Length(Mathf.Clamp01(frac) * 100f, LengthUnit.Percent));
            fill.style.backgroundColor = new StyleColor(fillColor);
            SetBorderRadius(fill, 2);
            fill.pickingMode = PickingMode.Ignore;
            bar.Add(fill);
            return bar;
        }

        private (VisualElement bar, VisualElement fill) MakeProgressBarWithFill(
            float t, Color fillColor, float width, float height, bool fillFlexGrow = false)
        {
            var (bar, fill) = UITheme.ProgressBar(t, fillColor, height, fillFlexGrow);
            if (!fillFlexGrow) bar.style.width = width;
            return (bar, fill);
        }

        private (VisualElement pill, Label label) MakeStatusPillWithLabel(string text, Color bg)
            => UITheme.StatusPill(text, bg);

        private VisualElement MakeProgressBar(
            float t, Color fillColor, float width, float height, bool fillFlexGrow = false)
        {
            var (bar, _) = UITheme.ProgressBar(t, fillColor, height, fillFlexGrow);
            if (!fillFlexGrow) bar.style.width = width;
            return bar;
        }

        private VisualElement MakeStatusPill(string text, Color bg)
        {
            var (pill, _) = UITheme.StatusPill(text, bg);
            return pill;
        }

        private VisualElement MakeDivider()
        {
            return UITheme.Divider();
        }

        // ----- RIGHT (coal generator) -----
        // Logged once per Unity session so the player can confirm the latest
        // build of this controller is actually loaded. If you don't see this
        // line in the console after opening a Coal Generator, Unity is still
        // running a stale assembly cache — close and reopen the project.
        private static bool _coalGenBuildLogged;
        private void BuildRightCoalGenerator(VisualElement root, VoxelEngine.Power.CoalGeneratorFuel f)
        {
            if (!_coalGenBuildLogged)
            {
                _coalGenBuildLogged = true;
                Debug.Log("[IndustrialWorld] CoalGenerator UI v3 loaded (toggle pill + fuel bar + centred status).");
            }
            f.EnsureContainers();
            var panel = MakePanel();
            DockRightPanel(panel, 460);
            root.Add(panel);

            var headerRow = new VisualElement(); headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 14;
            var t = MakeTitle("Coal Generator"); t.style.flexGrow = 1;
            headerRow.Add(t);

            // ENABLED / DISABLED toggle (left of the status pill) — lets the
            // player switch the generator off without removing the fuel.
            var (togglePill, _) = UITheme.MachineToggle(
                f.userEnabled,
                isOn =>
                {
                    f.userEnabled = isOn;
                    // The 1Hz refresh picks up the new status; nudge the pill
                    // label here so the change feels instant.
                });
            headerRow.Add(togglePill);

            var (pill, pillLbl) = MakeStatusPillWithLabel(
                f.IsBurning ? "RUNNING" : "OFFLINE",
                f.IsBurning ? new Color(0.95f, 0.50f, 0.15f) : new Color(0.30f, 0.30f, 0.35f));
            headerRow.Add(pill);

            // Live wattage output — to the RIGHT of the status pill, so the
            // player can see exactly how much power the generator is feeding
            // into the network. Reads off the PowerGenerator component.
            var coalWatt = new Label("");
            coalWatt.style.color    = new StyleColor(new Color(1f, 0.92f, 0.40f));
            coalWatt.style.fontSize = 13;
            coalWatt.style.unityFontStyleAndWeight = FontStyle.Bold;
            coalWatt.style.marginLeft = 10;
            coalWatt.tooltip = "Live wattage being produced and pushed into the connected power network.";
            var coalGen = f.GetComponent<VoxelEngine.Power.PowerGenerator>();
            coalWatt.text = (coalGen != null && coalGen.isOn)
                ? $"{coalGen.wattsPerSecond:0} W"
                : "0 W";
            headerRow.Add(coalWatt);
            panel.Add(headerRow);

            panel.Add(MakeSubtitle("Fuel"));
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center; row.style.marginTop = 8;
            row.Add(MakeLabeledSlot("Fuel", f.fuelC, 0));
            // Explicit-width bar — flexGrow path had layout-collapse issues
            // when the parent row hadn't computed its width yet. A fixed
            // 320px bar always renders, regardless of layout pass timing,
            // and comfortably fits inside the 460px panel beside the 64px
            // slot card.
            var (fuelBar, fuelFill) = MakeProgressBarWithFill(f.FuelProgress01,
                new Color(0.95f, 0.55f, 0.10f), width: 320, height: 8, fillFlexGrow: false);
            fuelBar.style.marginLeft = 12;
            row.Add(fuelBar);
            panel.Add(row);
            _liveFuelFill = fuelFill;

            panel.Add(Spacer(8));
            var status = new Label($"Fuel left: {f.fuelRemaining:0.0}s / {f.fuelMaxDuration:0.0}s");
            status.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.90f));
            status.style.fontSize = 11;
            panel.Add(status);

            string batteryText = f.HasNetworkBattery
                ? (f.IsPausedByFullBattery ? $"🔋 Battery full · fuel paused ({f.BatteryFill01 * 100f:0}%)" : $"🔋 Battery reserve {f.BatteryFill01 * 100f:0}%")
                : "🔋 No battery on this power network";
            var battery = new Label(batteryText);
            battery.style.color = new StyleColor(f.IsPausedByFullBattery ? UITheme.AccentGreen : UITheme.TextSecondary);
            battery.style.fontSize = 11;
            battery.style.unityFontStyleAndWeight = FontStyle.Bold;
            battery.style.marginTop = 4;
            panel.Add(battery);

            panel.Add(MakeDivider());

            var hint = new Label("Tip: place Coal in the fuel slot to start producing power. " +
                                 "Wood logs and planks also work but burn faster.");
            hint.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.65f));
            hint.style.fontSize = 11;
            hint.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(hint);

            // Advanced item ports — auto-feed the fuel slot via pipes.
            AppendItemPorts(panel, f);
        }

        // ----- RIGHT (electric furnace) -----
        private void BuildRightElectricFurnace(VisualElement root, ElectricFurnace ef)
        {
            ef.EnsureContainers();
            var panel = MakePanel();
            DockRightPanel(panel, 480);
            root.Add(panel);

            // ===== Header: title + status pill + wattage =====
            bool online = ef.IsOnline;
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 14;
            var title = MakeTitle("Electric Furnace");
            title.style.flexGrow = 1;
            headerRow.Add(title);

            // ENABLED / DISABLED toggle to the LEFT of the status pill.
            var (efTogglePill, _) = UITheme.MachineToggle(
                ef.userEnabled, isOn => ef.userEnabled = isOn);
            headerRow.Add(efTogglePill);

            var (pillE, pillELabel) = MakeStatusPillWithLabel(online ? "ONLINE" : "OFFLINE",
                online ? new Color(0.20f, 0.60f, 0.30f) : new Color(0.60f, 0.20f, 0.20f));
            pillE.tooltip = $"This furnace draws power from the connected network.\nLive draw shown to the right.";
            headerRow.Add(pillE);
            _liveStatusPill = pillE; _liveStatusLabel = pillELabel;

            var wattLbl = new Label("");
            wattLbl.style.color = new StyleColor(new Color(1f, 0.92f, 0.40f));
            wattLbl.style.fontSize = 13;
            wattLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            wattLbl.tooltip = $"Live power draw.\nIdle: {ef.idleWattsPerSecond:0} W\nSmelting: {ef.baseWattsPerSecond:0} W x efficiency multiplier";
            headerRow.Add(wattLbl);
            _liveWattLabel = wattLbl;
            panel.Add(headerRow);

            // Auto-pull toggle: continuously pull smeltable items from nearby chests.
            {
                var apRow = new VisualElement();
                apRow.style.flexDirection = FlexDirection.Row;
                apRow.style.alignItems = Align.Center;
                apRow.style.marginBottom = 8;
                apRow.Add(UITheme.SmallButton(
                    ef.autoPull ? "⤵  Auto-Pull: ON" : "⤵  Auto-Pull: OFF",
                    () => { ef.autoPull = !ef.autoPull; Refresh(); },
                    ef.autoPull ? UITheme.AccentGreen : UITheme.BgSlot));
                apRow.Add(UITheme.Muted("  Pulls smeltable items from nearby chests."));
                panel.Add(apRow);
            }

            // ===== Main row: INPUT  ->  [bolt + progress]  ->  4 OUTPUTS =====
            var mainRow = new VisualElement();
            mainRow.style.flexDirection = FlexDirection.Row;
            mainRow.style.alignItems = Align.Center;
            mainRow.style.marginBottom = 18;

            mainRow.Add(MakeLabeledSlot("Input", ef.inputC, 0));

            // Middle column: bolt + progress bar.
            var midCol = new VisualElement();
            midCol.style.alignItems = Align.Center;
            midCol.style.marginLeft = 18; midCol.style.marginRight = 18;
            midCol.style.minWidth = 130;

            var bolt = new VisualElement();
            bolt.style.width = 32; bolt.style.height = 32;
            bolt.style.backgroundColor = new StyleColor(new Color(0.30f, 0.30f, 0.35f));
            SetBorderRadius(bolt, 16);
            bolt.pickingMode = PickingMode.Ignore;
            midCol.Add(bolt);
            _liveFlame = bolt;  // re-using for electric furnace too

            var smeltLabel = new Label("");
            smeltLabel.style.color = new StyleColor(new Color(0.75f, 0.78f, 0.85f));
            smeltLabel.style.fontSize = 11;
            smeltLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            smeltLabel.style.marginTop = 6;
            smeltLabel.style.marginBottom = 4;
            smeltLabel.style.minWidth = 130;
            smeltLabel.style.maxWidth = 130;
            smeltLabel.style.whiteSpace = WhiteSpace.NoWrap;
            midCol.Add(smeltLabel);
            _liveSmeltLabel = smeltLabel;

            var (smeltBarE, smeltFillE) = MakeProgressBarWithFill(ef.SmeltProgress01,
                new Color(0.20f, 0.65f, 0.95f), width: 130, height: 10);
            midCol.Add(smeltBarE);
            _liveSmeltFill = smeltFillE;

            var arrow = new Label("⇒");
            arrow.style.fontSize = 22;
            arrow.style.color = new StyleColor(online && ef.Current != null
                ? new Color(0.20f, 0.65f, 0.95f) : new Color(0.4f, 0.4f, 0.45f));
            arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            arrow.style.marginTop = 6;
            arrow.pickingMode = PickingMode.Ignore;
            midCol.Add(arrow);

            mainRow.Add(midCol);

            // OUTPUTS — 4 slots in a 2x2 grid for compactness.
            var outBlock = new VisualElement();
            outBlock.style.alignItems = Align.Center;
            var outLbl = new Label("Outputs");
            outLbl.style.color = new StyleColor(new Color(0.75f, 0.78f, 0.85f));
            outLbl.style.fontSize = 11;
            outLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            outLbl.style.marginBottom = 4;
            outBlock.Add(outLbl);

            var outGrid = new VisualElement();
            outGrid.style.flexDirection = FlexDirection.Row;
            outGrid.style.flexWrap = Wrap.Wrap;
            outGrid.style.width = 120;
            outGrid.style.paddingTop = 4; outGrid.style.paddingBottom = 4;
            outGrid.style.paddingLeft = 4; outGrid.style.paddingRight = 4;
            outGrid.style.backgroundColor = new StyleColor(new Color(0.08f, 0.09f, 0.12f));
            SetBorderRadius(outGrid, 6);
            for (int i = 0; i < ef.outputC.Size; i++)
                outGrid.Add(BuildSlot(ef.outputC, i, ef.outputC.GetSlot(i), false));
            outBlock.Add(outGrid);
            mainRow.Add(outBlock);

            panel.Add(mainRow);

            panel.Add(MakeDivider());

            // ===== Upgrade slots with stat multipliers =====
            panel.Add(Spacer(10));
            var upHeader = new VisualElement();
            upHeader.style.flexDirection = FlexDirection.Row;
            upHeader.style.alignItems = Align.Center;
            upHeader.style.marginBottom = 6;
            var upLabel = new Label("Upgrades");
            upLabel.style.color = new StyleColor(new Color(0.40f, 0.85f, 0.45f));
            upLabel.style.fontSize = 14;
            upLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            upLabel.style.flexGrow = 1;
            upHeader.Add(upLabel);
            var multStat = new Label($"Speed x{ef.SpeedMultiplier:0.00}  |  Eff x{ef.EfficiencyMultiplier:0.00}");
            multStat.style.color = new StyleColor(new Color(0.75f, 0.85f, 0.75f));
            multStat.style.fontSize = 11;
            upHeader.Add(multStat);
            panel.Add(upHeader);

            var upRow = new VisualElement();
            upRow.style.flexDirection = FlexDirection.Row;
            upRow.style.paddingTop = 4; upRow.style.paddingBottom = 4;
            upRow.style.paddingLeft = 4; upRow.style.paddingRight = 4;
            upRow.style.backgroundColor = new StyleColor(new Color(0.08f, 0.09f, 0.12f));
            SetBorderRadius(upRow, 6);
            for (int i = 0; i < ef.upgradeC.Size; i++)
                upRow.Add(BuildSlot(ef.upgradeC, i, ef.upgradeC.GetSlot(i), false));
            panel.Add(upRow);

            // Footer hint.
            panel.Add(Spacer(14));

            var hint = new Label("Tip: connect cables from a generator. Insert Speed/Efficiency modules to tune output vs power use.");
            hint.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.65f));
            hint.style.fontSize = 10;
            hint.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(hint);

            // Advanced per-face ITEM ports (route pipes to Input / Output, with filters).
            AppendItemPorts(panel, ef);
        }

        // ----- RIGHT (anvil-style armor upgrades) -----
        private void BuildRightArmorUpgradeStation(VisualElement root, VoxelEngine.Combat.ArmorUpgradeStation station)
        {
            var panel = ArmorUpgradeStationPanel.Build(
                station,
                (container, index, stack, hotbarHighlight, interactive) =>
                    BuildSlot(container, index, stack, hotbarHighlight, interactive),
                Refresh);
            DockRightPanel(panel, 510);
            root.Add(panel);
        }

        // ----- RIGHT (crafting bench / assembler) -----
        private void BuildRightStationCrafting(VisualElement root, CraftingStation st)
        {
            var panel = MakePanel();
            DockRightPanel(panel, 460);
            root.Add(panel);

            panel.Add(MakeTitle(st.displayName));

            // Active craft queue display.
            if (_activeQueue != null && _activeQueue.HasWork)
            {
                panel.Add(MakeSubtitle("In Progress"));
                int i = 0;
                foreach (var e in _activeQueue.entries)
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 4;

                    var name = new Label(e.recipe.GetName());
                    name.style.color = Color.white; name.style.fontSize = 12; name.style.flexGrow = 1;
                    row.Add(name);

                    float pct = e.recipe.craftSeconds > 0 ? Mathf.Clamp01(e.progressSeconds / e.recipe.craftSeconds) : 1f;
                    float remain = Mathf.Max(0, e.recipe.craftSeconds - e.progressSeconds);
                    var time = new Label($"{remain:0.0}s");
                    time.style.color = new StyleColor(new Color(0.78f, 0.82f, 0.9f));
                    time.style.fontSize = 11; time.style.marginRight = 6;
                    row.Add(time);

                    var (bar, fill) = MakeProgressBarWithFill(pct, new Color(0.20f, 0.65f, 0.95f), width: 80, height: 8);
                    row.Add(bar);

                    int idx = i;
                    var cancel = new Button(() => { _activeQueue.Cancel(idx); Refresh(); }) { text = "X" };
                    cancel.style.minWidth = 24; cancel.style.minHeight = 22;
                    cancel.style.marginLeft = 6;
                    cancel.style.color = Color.white;
                    cancel.style.backgroundColor = new StyleColor(new Color(0.55f, 0.22f, 0.22f));
                    SetBorderRadius(cancel, 4);
                    row.Add(cancel);

                    panel.Add(row);
                    i++;
                }
                panel.Add(MakeDivider());
            }

            var recipes = Crafter.AvailableRecipesForStation(recipeRegistry, st);
            BuildRecipeBrowser(panel, recipes, inventory.container, inventory.container,
                emptyMessage: "No recipes available at this station tier.",
                // GetEntityId — Unity 6+ replacement for the now-deprecated
                // GetInstanceID. Same semantics: a unique, stable int per
                // UnityObject. (Earlier this call was incorrectly "fixed" to
                // GetInstanceID, which the Unity 6.4 compiler immediately
                // flagged as obsolete — reverted to GetEntityId here.)
                panelId: "station_" + st.GetEntityId());
        }

        // ============================================================
        //                       SLOT WIDGET
        // ============================================================
        private VisualElement BuildSlot(IItemContainer container, int index, ItemStack stack, bool hotbarHighlight, bool interactive = true)
        {
            bool isHotbarSlot = inventory != null && container == inventory.container
                && index >= 0 && index < Inventory.HOTBAR_SIZE;
            bool isCargoSlot = inventory != null && container == inventory.container && index >= Inventory.HOTBAR_SIZE;
            bool isEquipmentSlot = IsEquipmentContainer(container);
            bool lcdMatrixSlot = isCargoSlot || isEquipmentSlot;

            var slot = new VisualElement
            {
                name = isCargoSlot ? "InventoryLcdSlot" : isEquipmentSlot ? "EquipmentLcdSlot" : "ItemSlot"
            };
            slot.style.width = isHotbarSlot ? 52 : lcdMatrixSlot ? 48 : 56;
            slot.style.height = isHotbarSlot ? 52 : lcdMatrixSlot ? 48 : 56;
            slot.style.marginRight = isHotbarSlot ? 2 : lcdMatrixSlot ? 3 : 4;
            slot.style.marginBottom = 4;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;

            if (isHotbarSlot)
            {
                slot.style.backgroundColor = new StyleColor(hotbarHighlight ? LcdHudTheme.Glass : LcdHudTheme.GlassDark);
                SetBorderRadius(slot, 1);
                slot.style.borderTopWidth = slot.style.borderBottomWidth =
                slot.style.borderLeftWidth = slot.style.borderRightWidth = hotbarHighlight ? 2 : 1;
                var lcdBorder = hotbarHighlight
                    ? new StyleColor(LcdHudTheme.Phosphor)
                    : new StyleColor(new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.95f));
                slot.style.borderTopColor = slot.style.borderBottomColor =
                slot.style.borderLeftColor = slot.style.borderRightColor = lcdBorder;

                var scan = new VisualElement();
                scan.style.position = Position.Absolute;
                scan.style.left = 2; scan.style.right = 2;
                scan.style.top = 14; scan.style.height = 1;
                scan.style.backgroundColor = new StyleColor(new Color(LcdHudTheme.Phosphor.r, LcdHudTheme.Phosphor.g, LcdHudTheme.Phosphor.b, 0.08f));
                scan.pickingMode = PickingMode.Ignore;
                slot.Add(scan);

                var key = new Label(index == 9 ? "0" : (index + 1).ToString()) { name = "HotbarKey" };
                key.style.position = Position.Absolute;
                key.style.top = 2; key.style.right = 3;
                key.style.fontSize = 7;
                key.style.letterSpacing = 0.4f;
                key.style.unityFontStyleAndWeight = FontStyle.Bold;
                key.style.color = new StyleColor(hotbarHighlight ? LcdHudTheme.Phosphor : LcdHudTheme.PhosphorDim);
                key.pickingMode = PickingMode.Ignore;
                slot.Add(key);
            }
            else if (lcdMatrixSlot)
            {
                slot.style.backgroundColor = new StyleColor(LcdHudTheme.GlassDark);
                SetBorderRadius(slot, 1);
                slot.style.borderTopWidth = slot.style.borderBottomWidth =
                slot.style.borderLeftWidth = slot.style.borderRightWidth = 1;
                var matrixBorder = isEquipmentSlot
                    ? new Color(LcdHudTheme.Phosphor.r, LcdHudTheme.Phosphor.g, LcdHudTheme.Phosphor.b, 0.62f)
                    : new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.96f);
                var border = new StyleColor(matrixBorder);
                slot.style.borderTopColor = slot.style.borderBottomColor =
                slot.style.borderLeftColor = slot.style.borderRightColor = border;

                var cell = new Label(isCargoSlot ? $"{index - Inventory.HOTBAR_SIZE + 1:00}" : "EQ") { name = "LcdSlotIndex" };
                cell.style.position = Position.Absolute;
                cell.style.left = 3;
                cell.style.top = 2;
                cell.style.fontSize = 6;
                cell.style.letterSpacing = 0.55f;
                cell.style.unityFontStyleAndWeight = FontStyle.Bold;
                cell.style.color = new StyleColor(isEquipmentSlot ? LcdHudTheme.PhosphorDim : LcdHudTheme.Caption);
                cell.pickingMode = PickingMode.Ignore;
                slot.Add(cell);

                var divider = new VisualElement();
                divider.style.position = Position.Absolute;
                divider.style.left = 3;
                divider.style.right = 3;
                divider.style.top = 12;
                divider.style.height = 1;
                divider.style.backgroundColor = new StyleColor(new Color(LcdHudTheme.Phosphor.r, LcdHudTheme.Phosphor.g, LcdHudTheme.Phosphor.b, 0.08f));
                divider.pickingMode = PickingMode.Ignore;
                slot.Add(divider);
            }
            else
            {
                slot.style.backgroundColor = new StyleColor(new Color(0.13f, 0.15f, 0.19f, 0.95f));
                SetBorderRadius(slot, 4);
                slot.style.borderTopWidth = slot.style.borderBottomWidth =
                slot.style.borderLeftWidth = slot.style.borderRightWidth = 2;
                var bColor = hotbarHighlight
                    ? new StyleColor(LcdHudTheme.Phosphor)
                    : new StyleColor(new Color(0.25f, 0.27f, 0.32f));
                slot.style.borderTopColor = slot.style.borderBottomColor =
                slot.style.borderLeftColor = slot.style.borderRightColor = bColor;
            }

            if (!stack.IsEmpty)
            {
                // Icon
                if (stack.item.icon != null)
                {
                    var img = new Image { sprite = stack.item.icon };
                    // Fill the slot: the generated icons are tight-cropped, so a
                    // larger image reads instantly even at a glance. ScaleToFit keeps
                    // aspect for any legacy non-square sprite.
                    img.scaleMode = ScaleMode.ScaleToFit;
                    img.style.width = isHotbarSlot ? 46 : lcdMatrixSlot ? 40 : 51;
                    img.style.height = isHotbarSlot ? 46 : lcdMatrixSlot ? 40 : 51;
                    img.pickingMode = PickingMode.Ignore;   // children must not steal events
                    slot.Add(img);
                }
else if (VoxelEngine.Items.HydrogenCanisterItem.IsPortableHydrogenTank(stack.item))
                {
                    // Procedural hydrogen tank bottle icon.
                    var icon = new VisualElement();
                    icon.style.width = 40; icon.style.height = 44;
                    icon.pickingMode = PickingMode.Ignore;

                    var body = new VisualElement();
                    body.style.position = Position.Absolute;
                    body.style.left = 8; body.style.right = 8;
                    body.style.top = 10; body.style.bottom = 4;
                    body.style.backgroundColor = new StyleColor(new Color(0.22f, 0.48f, 0.62f));
                    SetBorderRadius(body, 8);
                    body.pickingMode = PickingMode.Ignore;
                    icon.Add(body);

                    var stripe = new VisualElement();
                    stripe.style.position = Position.Absolute;
                    stripe.style.left = 8; stripe.style.right = 8;
                    stripe.style.top = 22; stripe.style.height = 6;
                    stripe.style.backgroundColor = new StyleColor(new Color(0.45f, 0.9f, 1f, 0.85f));
                    stripe.pickingMode = PickingMode.Ignore;
                    icon.Add(stripe);

                    var neck = new VisualElement();
                    neck.style.position = Position.Absolute;
                    neck.style.left = 15; neck.style.width = 10;
                    neck.style.top = 4; neck.style.height = 10;
                    neck.style.backgroundColor = new StyleColor(new Color(0.55f, 0.58f, 0.62f));
                    SetBorderRadius(neck, 2);
                    neck.pickingMode = PickingMode.Ignore;
                    icon.Add(neck);

                    var valve = new VisualElement();
                    valve.style.position = Position.Absolute;
                    valve.style.left = 12; valve.style.width = 16;
                    valve.style.top = 1; valve.style.height = 5;
                    valve.style.backgroundColor = new StyleColor(new Color(0.75f, 0.78f, 0.82f));
                    SetBorderRadius(valve, 2);
                    valve.pickingMode = PickingMode.Ignore;
                    icon.Add(valve);

                    float fill01 = VoxelEngine.Items.HydrogenCanisterItem.Fill01(stack);
                    var liquid = new VisualElement();
                    liquid.style.position = Position.Absolute;
                    liquid.style.left = 10; liquid.style.right = 10;
                    liquid.style.bottom = 6;
                    liquid.style.height = Mathf.Max(2, fill01 * 28f);
                    liquid.style.backgroundColor = new StyleColor(new Color(0.35f, 0.85f, 1f, 0.55f));
                    SetBorderRadius(liquid, 6);
                    liquid.pickingMode = PickingMode.Ignore;
                    icon.Add(liquid);

                    slot.Add(icon);
                }
                else
                {
                    var box = new VisualElement();
                    box.style.width = 36; box.style.height = 36;
                    box.style.backgroundColor = new StyleColor(stack.item.iconTint);
                    SetBorderRadius(box, 3);
                    box.pickingMode = PickingMode.Ignore;
                    slot.Add(box);
                }
                                // Stack count — show always (even for "1") so the player sees how much is in the stack.
                if (stack.count > 0)
                {
                    var count = new Label(stack.count.ToString());
                    count.style.position = Position.Absolute;
                    count.style.bottom = 2; count.style.right = 4;
                    count.style.color = lcdMatrixSlot ? LcdHudTheme.Phosphor : Color.white;
                    count.style.fontSize = lcdMatrixSlot ? 10 : 12;
                    count.style.unityFontStyleAndWeight = FontStyle.Bold;
                    // Subtle dark backdrop so the number is readable on bright icons.
                    count.style.paddingLeft = 3; count.style.paddingRight = 3;
                    count.style.backgroundColor = new StyleColor(lcdMatrixSlot ? LcdHudTheme.GlassDark : new Color(0,0,0,0.55f));
                    SetBorderRadius(count, lcdMatrixSlot ? 1 : 3);
                    count.pickingMode = PickingMode.Ignore;
                    slot.Add(count);
                }
                // Tool durability bar
                if (stack.item is ToolItem tool && tool.maxDurability > 0)
                {
                    float frac = stack.durability / (float)tool.maxDurability;
                    slot.Add(MakeItemFillBar(frac, Color.Lerp(Color.red, Color.green, frac)));
                }
                // Portable Hydrogen Tank fill (ml)
                else if (VoxelEngine.Items.HydrogenCanisterItem.IsPortableHydrogenTank(stack.item))
                {
                    float frac = VoxelEngine.Items.HydrogenCanisterItem.Fill01(stack);
                    var h2Col = new Color(0.35f, 0.85f, 1f);
                    slot.Add(MakeItemFillBar(frac, Color.Lerp(new Color(0.8f, 0.2f, 0.15f), h2Col, frac)));
                    int ml = VoxelEngine.Items.HydrogenCanisterItem.GetStoredMl(stack);
                    var mlLbl = new Label(ml >= 1000 ? $"{ml / 1000f:0.0}L" : $"{ml}ml");
                    mlLbl.style.position = Position.Absolute;
                    mlLbl.style.left = 3; mlLbl.style.top = 2;
                    mlLbl.style.fontSize = 9;
                    mlLbl.style.color = Color.white;
                    mlLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                    mlLbl.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.55f));
                    mlLbl.style.paddingLeft = 2; mlLbl.style.paddingRight = 2;
                    SetBorderRadius(mlLbl, 2);
                    mlLbl.pickingMode = PickingMode.Ignore;
                    slot.Add(mlLbl);
                }
                // Jetpack combined fuel (H₂ + power, both pools) — bar + per-pool badges.
                else if (stack.item is VoxelEngine.Items.JetpackItem jp)
                {
                    int cap = Mathf.Max(1, jp.HydrogenCapacityMl + jp.PowerCapacityMl);
                    int cur = VoxelEngine.Items.JetpackItem.GetH2Ml(stack) + VoxelEngine.Items.JetpackItem.GetPowerMl(stack);
                    float frac = Mathf.Clamp01(cur / (float)cap);
                    slot.Add(MakeItemFillBar(frac, Color.Lerp(Color.red, new Color(0.4f, 0.9f, 1f), frac)));

                    int h2Cap = jp.HydrogenCapacityMl;
                    int pCap  = jp.PowerCapacityMl;
                    float top = 2f;
                    if (h2Cap > 0)
                    {
                        int ml = VoxelEngine.Items.JetpackItem.GetH2Ml(stack);
                        var h2Lbl = new Label(ml >= 1000 ? $"{ml / 1000f:0.0}L" : $"{ml}ml");
                        h2Lbl.name = $"jp-h2-{index}";   // named for the bay's live tick
                        StyleSlotBadge(h2Lbl, top, new Color(0.4f, 0.9f, 1f));
                        slot.Add(h2Lbl);
                        top += 11f;
                    }
                    if (pCap > 0)
                    {
                        float pct = Mathf.Clamp01(VoxelEngine.Items.JetpackItem.GetPowerMl(stack) / (float)pCap) * 100f;
                        var pwrLbl = new Label($"{pct:0}%");
                        pwrLbl.name = $"jp-pwr-{index}"; // named for the bay's live tick
                        StyleSlotBadge(pwrLbl, top, new Color(0.55f, 1f, 0.7f));
                        slot.Add(pwrLbl);
                    }
                }
                // Portable Battery charge (Wh) — fill bar + readable % badge.
                else if (VoxelEngine.Items.PortableBatteryItem.IsPortableBattery(stack.item))
                {
                    float frac = VoxelEngine.Items.PortableBatteryItem.Fill01(stack);
                    var pwrCol = new Color(0.45f, 0.9f, 0.6f);
                    slot.Add(MakeItemFillBar(frac, Color.Lerp(new Color(0.85f, 0.3f, 0.15f), pwrCol, frac)));
                    var pctLbl = new Label($"{Mathf.RoundToInt(frac * 100f)}%");
                    pctLbl.name = "pbat-pct";
                    pctLbl.style.position = Position.Absolute;
                    pctLbl.style.left = 3; pctLbl.style.top = 2;
                    pctLbl.style.fontSize = 9;
                    pctLbl.style.color = Color.white;
                    pctLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                    pctLbl.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.55f));
                    pctLbl.style.paddingLeft = 2; pctLbl.style.paddingRight = 2;
                    SetBorderRadius(pctLbl, 2);
                    pctLbl.pickingMode = PickingMode.Ignore;
                    slot.Add(pctLbl);
                }
                // Tooltip on hover (only when the panel is interactive — otherwise hotbar
                // slots in the corner of the screen would pop tooltips constantly).
                if (interactive) Tooltip.Bind(slot, stack);
            }

            // Icons are added after the screen decorations; bring the tiny key label
            // back above them so every hotbar slot remains readable at a glance.
            if (isHotbarSlot) slot.Q<Label>("HotbarKey")?.BringToFront();

            if (interactive)
            {
                // Tag the slot with its container/index so the panel-level click handler can find us.
                slot.userData = new SlotRef { container = container, index = index };
                if (stack != null && !stack.IsEmpty)
                    WorldInspectionHud.BindInventoryItem(slot, stack);
            }
            else
            {
                slot.pickingMode = PickingMode.Ignore;   // truly inert when HUD-only
            }

            return slot;
        }

        // Lightweight identifier we attach to every interactive slot's userData.
        private class SlotRef
        {
            public IItemContainer container;
            public int            index;
        }

        // ============================================================
        //              PANEL-WIDE DRAG / DROP HANDLING
        // ============================================================
        // We register mouse events on the persistent _root element and use Pick() to find
        // which slot is under the cursor. This survives UI rebuilds (which happen on every
        // inventory change) where per-slot event registrations would be lost.


        private static readonly System.Collections.Generic.List<VisualElement> _pickBuf = new();

        private SlotRef FindSlotAt(Vector2 panelPos)
        {
            if (_root.panel == null) return null;
            _pickBuf.Clear();
            _root.panel.PickAll(panelPos, _pickBuf);
            for (int i = 0; i < _pickBuf.Count; i++)
            {
                var picked = _pickBuf[i];
                while (picked != null)
                {
                    if (picked.userData is SlotRef sr) return sr;
                    picked = picked.parent;
                }
            }
            return null;
        }

        // Used by Tooltip.Tick — returns whatever stack is currently in the slot under the cursor.
        private ItemStack ProbeStackAt(Vector2 panelPos)
        {
            var sr = FindSlotAt(panelPos);
            if (sr == null) return null;
            if (sr.index < 0 || sr.index >= sr.container.Slots.Count) return null;
            return sr.container.GetSlot(sr.index);
        }

        // ============================================================
        //                     DRAG & DROP
        // ============================================================
        private void BeginDrag(IItemContainer c, int idx)
        {
            var st = c.GetSlot(idx);
            if (st.IsEmpty) return;
            _dragSource = new DragSource { container = c, slotIndex = idx, stack = st.Clone(), active = true };

            _dragGhost = new VisualElement();
            _dragGhost.style.position = Position.Absolute;
            _dragGhost.style.width = 48; _dragGhost.style.height = 48;
            _dragGhost.style.backgroundColor = new StyleColor(new Color(0.13f, 0.15f, 0.19f, 0.85f));
            SetBorderRadius(_dragGhost, 4);
            _dragGhost.pickingMode = PickingMode.Ignore;
            if (st.item.icon != null)
            {
                var img = new Image { sprite = st.item.icon };
                img.scaleMode = ScaleMode.ScaleToFit; // match BuildSlot: tight-cropped generated icons must fit, not crop (fixes blank recipe/crafter icons)
                img.style.width = 40; img.style.height = 40;
                img.style.marginLeft = 4; img.style.marginTop = 4;
                img.pickingMode = PickingMode.Ignore;
                _dragGhost.Add(img);
            }
            else
            {
                var box = new VisualElement();
                box.style.width = 32; box.style.height = 32;
                box.style.marginLeft = 8; box.style.marginTop = 8;
                box.style.backgroundColor = new StyleColor(st.item.iconTint);
                box.pickingMode = PickingMode.Ignore;
                _dragGhost.Add(box);
            }
            if (st.count > 1)
            {
                var l = new Label(st.count.ToString());
                l.style.position = Position.Absolute;
                l.style.bottom = 2; l.style.right = 4;
                l.style.color = Color.white;
                l.pickingMode = PickingMode.Ignore;
                _dragGhost.Add(l);
            }
            _root.Add(_dragGhost);
            _dragGhost.BringToFront();
        }

        private void EndDrag(IItemContainer destC, int destIdx)
        {
            if (!_dragSource.active) return;
            var srcC = _dragSource.container;
            int srcIdx = _dragSource.slotIndex;

            // Take FRESH copies so list-element references can't double-count.
            var srcStack = srcC.GetSlot(srcIdx).Clone();
            var dstStack = destC.GetSlot(destIdx).Clone();

            // Same slot — cancel.
            if (srcC == destC && srcIdx == destIdx) { CancelDrag(); return; }

            // UI-only filter slots record the item type without consuming the dragged stack.
            if (destC is VoxelEngine.Storage.IItemFilterSlot filterSlot)
            {
                if (!srcStack.IsEmpty) filterSlot.ApplyFilter(srcStack.item);
                CancelDrag();
                Refresh();
                return;
            }

            // Virtual drawer storage is capacity-based, not slot-swap based.
            if (destC is VoxelEngine.Storage.StorageDrawer destDrawer)
            {
                int accepted = destDrawer.InsertItems(srcStack.item, srcStack.count);
                if (accepted > 0)
                {
                    if (srcC is VoxelEngine.Storage.StorageDrawer srcDrawer) srcDrawer.Remove(srcStack.item, accepted);
                    else
                    {
                        if (accepted >= srcStack.count) srcC.SetSlot(srcIdx, new ItemStack());
                        else { srcStack.count -= accepted; srcC.SetSlot(srcIdx, srcStack); }
                    }
                }
                CancelDrag();
                Refresh();
                return;
            }
            if (srcC is VoxelEngine.Storage.StorageDrawer sourceDrawer)
            {
                var clone = srcStack.Clone();
                var leftover = destC.Insert(clone);
                int moved = srcStack.count - (leftover?.count ?? 0);
                if (moved > 0) sourceDrawer.Remove(srcStack.item, moved);
                CancelDrag();
                Refresh();
                return;
            }

            bool sameContainer = srcC == destC;

            // Stack merge.
            if (!srcStack.IsEmpty && !dstStack.IsEmpty &&
                dstStack.item == srcStack.item && srcStack.item.IsStackable)
            {
                int space = ItemStack.MaxItemsPerStack(srcStack.item) - dstStack.count;
                int move  = Mathf.Min(space, srcStack.count);
                if (!sameContainer)
                    move = Mathf.Min(move, MaxDirectAdd(destC, srcStack.item, move));
                if (move <= 0)
                {
                    CancelDrag();
                    return;
                }
                dstStack.count += move;
                srcStack.count -= move;
                destC.SetSlot(destIdx, dstStack);
                srcC.SetSlot(srcIdx, srcStack.count > 0 ? srcStack : new ItemStack());
            }
            else
            {
                // Respect destination/source AcceptFilter and mass gates during direct swaps.
                // Insert() already honours filters, but SetSlot()-based swaps used to bypass them.
                if (!sameContainer && (!CanDirectSet(destC, srcStack, dstStack) || !CanDirectSet(srcC, dstStack, srcStack)))
                {
                    CancelDrag();
                    return;
                }

                // Plain swap.
                destC.SetSlot(destIdx, srcStack);
                srcC.SetSlot(srcIdx, dstStack);
            }
            CancelDrag();
            Refresh();
        }

        private static int MaxDirectAdd(IItemContainer container, ItemDefinition item, int wanted)
        {
            if (item == null || wanted <= 0) return 0;
            int allowed = wanted;
            if (container is ItemContainer itemContainer)
            {
                if (itemContainer.AcceptFilter != null)
                    allowed = Mathf.Min(allowed, Mathf.Clamp(itemContainer.AcceptFilter(item, allowed), 0, allowed));
                float freeMass = itemContainer.MaxWeightKg - itemContainer.CurrentWeightKg;
                allowed = Mathf.Min(allowed, Mathf.FloorToInt((freeMass + 0.0001f) / Mathf.Max(0.0001f, item.massPerUnit)));
            }
            return Mathf.Clamp(allowed, 0, wanted);
        }

        private static bool CanDirectSet(IItemContainer container, ItemStack stack, ItemStack replacing)
        {
            if (stack == null || stack.IsEmpty || stack.item == null) return true;
            if (container is ItemContainer itemContainer)
            {
                if (itemContainer.AcceptFilter != null && itemContainer.AcceptFilter(stack.item, stack.count) < stack.count)
                    return false;
                float replacingMass = replacing == null || replacing.IsEmpty || replacing.item == null
                    ? 0f
                    : MassUtil.StackMass(replacing.item, replacing.count);
                float freeAfterReplace = itemContainer.MaxWeightKg - Mathf.Max(0f, itemContainer.CurrentWeightKg - replacingMass);
                return freeAfterReplace + 0.0001f >= MassUtil.StackMass(stack.item, stack.count);
            }
            return true;
        }

        private void CancelDrag()
        {
            _dragSource.active = false;
            if (_dragGhost != null) { _dragGhost.RemoveFromHierarchy(); _dragGhost = null; }
        }

        private void SplitOrMove(IItemContainer c, int idx)
        {
            var st = c.GetSlot(idx);
            if (st.IsEmpty) return;

            // RMB = SPLIT: take half the stack into an empty slot.
            if (st.count > 1)
            {
                int half = st.count / 2;
                // Find an empty slot in the same container.
                int emptyIdx = -1;
                for (int i = 0; i < c.Slots.Count; i++)
                {
                    if (i == idx) continue;
                    if (c.GetSlot(i).IsEmpty) { emptyIdx = i; break; }
                }
                if (emptyIdx >= 0)
                {
                    var splitStack = new ItemStack(st.item, half);
                    splitStack.durability = st.durability;
                    c.SetSlot(emptyIdx, splitStack);
                    st.count -= half;
                    c.SetSlot(idx, st);
                    return;
                }
            }

            // Fallback: quick-move to the other side.
            IItemContainer target = null;
            if (c == inventory.container && _rightContainer != null) target = _rightContainer;
            else if (c != inventory.container) target = inventory.container;
            if (target == null) return;
            var leftover = target.Insert(st.Clone());
            if (leftover == null || leftover.count == 0)
                c.SetSlot(idx, new ItemStack());
            else
                c.SetSlot(idx, leftover);
        }

        /// <summary>
        /// If a storage network is reachable (open storage terminal OR an online
        /// wireless transmitter), push the currently dragged stack into it and
        /// clear the source slot. Returns true if the stack was fully consumed.
        /// Used by the drag-onto-empty handler so users can drop items into
        /// storage without the obscure shift-click ritual.
        /// </summary>
        private bool TryInsertDraggedIntoNetwork()
        {
            if (!_dragSource.active) return false;
            var srcC   = _dragSource.container;
            int srcIdx = _dragSource.slotIndex;
            var stack  = srcC.GetSlot(srcIdx);
            if (stack.IsEmpty) return false;

            VoxelEngine.Storage.ServerRack rack = null;
            if (_openStorageTerminal != null) rack = _openStorageTerminal.ConnectedRack;
            if (rack == null)
            {
                var transmitters = VoxelEngine.Storage.WirelessTransmitter.GetAllOnline();
                if (transmitters.Length > 0) rack = transmitters[0].ConnectedRack;
            }
            if (rack == null) return false;

            int leftover = rack.NetworkInsert(stack.item, stack.count);
            int moved    = stack.count - leftover;
            if (moved <= 0) return false;

            if (leftover <= 0) srcC.SetSlot(srcIdx, new ItemStack());
            else { stack.count = leftover; srcC.SetSlot(srcIdx, stack); }

            BuildFeedbackHud.Show($"Stored {stack.item.displayName}",
                $"+{moved}", stack.item.icon, UITheme.AccentCyan);
            Refresh();
            return leftover <= 0;
        }

        /// <summary>Drop the item from the given container slot into the world.</summary>
        public void DropItemFromSlot(IItemContainer c, int idx)
        {
            if (c == null) return;
            var stack = c.GetSlot(idx);
            if (stack == null || stack.IsEmpty) return;

            int capacity = VoxelEngine.Items.DroppedItem.AvailablePhysicalCapacity;
            bool wouldVoid = stack.count > capacity;
            var session = VoxelEngine.Menu.WorldSession.Instance;
            bool shouldWarn = session == null || session.showDropVoidWarning;

            if (wouldVoid && shouldWarn)
            {
                ShowDropVoidConfirmation(c, idx, stack, Mathf.Max(0, capacity));
                return;
            }

            CompleteDropFromSlot(c, idx, allowVoidOverflow: wouldVoid);
        }

        private void ShowDropVoidConfirmation(IItemContainer container, int slotIndex, ItemStack snapshot, int capacity)
        {
            if (_root == null || snapshot == null || snapshot.IsEmpty) return;
            if (_dropVoidOverlay != null && _dropVoidOverlay.parent != null) return;

            int voidCount = Mathf.Max(0, snapshot.count - capacity);
            var session = VoxelEngine.Menu.WorldSession.Instance;
            bool showNextTime = session == null || session.showDropVoidWarning;

            var overlay = new VisualElement { name = "DropVoidConfirmOverlay" };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.top = 0;
            overlay.style.right = 0;
            overlay.style.bottom = 0;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.pickingMode = PickingMode.Position;

            var dim = new VisualElement();
            dim.style.position = Position.Absolute;
            dim.style.left = 0;
            dim.style.top = 0;
            dim.style.right = 0;
            dim.style.bottom = 0;
            dim.style.backgroundColor = new StyleColor(new Color(0.02f, 0.025f, 0.04f, 0.68f));
            dim.pickingMode = PickingMode.Position;
            overlay.Add(dim);

            var card = MakePanel();
            card.style.width = 520;
            card.style.maxWidth = Length.Percent(92);
            card.style.paddingTop = 20;
            card.style.paddingBottom = 18;
            card.style.paddingLeft = 22;
            card.style.paddingRight = 22;
            card.pickingMode = PickingMode.Position;
            overlay.Add(card);

            var title = MakeTitle("DROP LIMIT WARNING");
            title.style.color = new StyleColor(UITheme.AccentAmber);
            card.Add(title);
            card.Add(UITheme.AccentDivider(UITheme.AccentAmber));

            var msg = new Label($"You are about to drop {snapshot.count:N0} x {snapshot.item.displayName}, but only {capacity:N0} physical item units can exist before the world-drop limit is reached.\n\nConfirming will drop what fits and VOID {voidCount:N0} item unit{(voidCount == 1 ? "" : "s")}. Denying keeps the stack in your inventory.");
            msg.style.whiteSpace = WhiteSpace.Normal;
            msg.style.color = new StyleColor(UITheme.TextSecondary);
            msg.style.fontSize = 12;
            msg.style.marginTop = 8;
            msg.style.marginBottom = 12;
            card.Add(msg);

            var toggle = new Toggle("Show this warning before voiding drops in this world");
            toggle.SetValueWithoutNotify(showNextTime);
            toggle.style.color = new StyleColor(UITheme.TextSecondary);
            toggle.style.marginBottom = 14;
            toggle.RegisterValueChangedCallback(e => showNextTime = e.newValue);
            card.Add(toggle);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.alignItems = Align.Center;

            void CloseOnly()
            {
                SaveDropVoidWarningPreference(showNextTime);
                CloseDropVoidOverlay();
            }

            var deny = new Button(() =>
            {
                CloseOnly();
                BuildFeedbackHud.Show("Drop cancelled", "Stack kept in inventory", snapshot.item.icon, UITheme.AccentCyan);
            }) { text = "DENY" };
            deny.style.minWidth = 100;
            deny.style.minHeight = 34;
            deny.style.marginRight = 8;
            deny.style.color = Color.white;
            deny.style.backgroundColor = new StyleColor(UITheme.BgSlot);
            SetBorderRadius(deny, UITheme.ButtonRadius);
            ZeroBorder(deny);
            row.Add(deny);

            var confirm = new Button(() =>
            {
                CloseOnly();
                CompleteDropFromSlot(container, slotIndex, allowVoidOverflow: true);
            }) { text = "CONFIRM VOID" };
            confirm.style.minWidth = 150;
            confirm.style.minHeight = 34;
            confirm.style.color = Color.white;
            confirm.style.unityFontStyleAndWeight = FontStyle.Bold;
            confirm.style.backgroundColor = new StyleColor(UITheme.AccentRed);
            SetBorderRadius(confirm, UITheme.ButtonRadius);
            ZeroBorder(confirm);
            row.Add(confirm);
            card.Add(row);

            _dropVoidOverlay = overlay;
            _root.Add(overlay);
            overlay.BringToFront();
        }

        private static void SaveDropVoidWarningPreference(bool showWarning)
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session == null) return;
            session.showDropVoidWarning = showWarning;
            session.SaveWorldSettings();
        }

        private void CloseDropVoidOverlay()
        {
            if (_dropVoidOverlay != null && _dropVoidOverlay.parent != null)
                _dropVoidOverlay.RemoveFromHierarchy();
            _dropVoidOverlay = null;
        }

        /// <summary>
        /// Confirms an intentional type change that discards a non-empty tank.
        /// Grid tank panels use this instead of silently blocking type changes or
        /// silently deleting stored material.
        /// </summary>
        public void ShowTankTypeVoidConfirmation(string resourceLabel, string nextType, float storedAmount,
            System.Action confirmVoidAndChange)
        {
            if (_root == null || confirmVoidAndChange == null) return;
            if (_tankTypeVoidOverlay != null && _tankTypeVoidOverlay.parent != null) return;

            var overlay = new VisualElement { name = "TankTypeVoidConfirmOverlay" };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0; overlay.style.top = 0; overlay.style.right = 0; overlay.style.bottom = 0;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.pickingMode = PickingMode.Position;

            var dim = new VisualElement();
            dim.style.position = Position.Absolute;
            dim.style.left = 0; dim.style.top = 0; dim.style.right = 0; dim.style.bottom = 0;
            dim.style.backgroundColor = new StyleColor(new Color(0.02f, 0.025f, 0.04f, 0.70f));
            dim.pickingMode = PickingMode.Position;
            overlay.Add(dim);

            var card = MakePanel();
            card.style.width = 480;
            card.style.maxWidth = Length.Percent(90);
            card.style.paddingTop = 20;
            card.style.paddingBottom = 18;
            card.style.paddingLeft = 22;
            card.style.paddingRight = 22;
            card.pickingMode = PickingMode.Position;
            overlay.Add(card);

            card.Add(MakeTitle($"Void {resourceLabel}?"));
            card.Add(UITheme.AccentDivider(UITheme.AccentRed));
            var message = new Label($"This tank contains {storedAmount:0.##} units of {resourceLabel.ToLowerInvariant()}. " +
                                    $"Changing it to {nextType} will permanently void the stored {resourceLabel.ToLowerInvariant()}.");
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.color = new StyleColor(UITheme.TextSecondary);
            message.style.fontSize = 12;
            message.style.marginBottom = 16;
            card.Add(message);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.alignItems = Align.Center;

            var cancel = UITheme.ActionButton("CANCEL", () =>
            {
                CloseTankTypeVoidConfirmation();
                BuildFeedbackHud.Show("Type change cancelled", $"Stored {resourceLabel.ToLowerInvariant()} was kept.", null, UITheme.AccentCyan);
            }, UITheme.BgHover);
            cancel.style.marginRight = 8;
            row.Add(cancel);

            var confirm = UITheme.ActionButton($"VOID {resourceLabel.ToUpperInvariant()}", () =>
            {
                CloseTankTypeVoidConfirmation();
                confirmVoidAndChange();
                RefreshCurrentPanel();
            }, UITheme.AccentRed);
            row.Add(confirm);
            card.Add(row);

            _tankTypeVoidOverlay = overlay;
            _root.Add(overlay);
            overlay.BringToFront();
        }

        private void CloseTankTypeVoidConfirmation()
        {
            if (_tankTypeVoidOverlay != null && _tankTypeVoidOverlay.parent != null)
                _tankTypeVoidOverlay.RemoveFromHierarchy();
            _tankTypeVoidOverlay = null;
        }

        private void CompleteDropFromSlot(IItemContainer c, int idx, bool allowVoidOverflow)
        {
            if (c == null) return;
            var stack = c.GetSlot(idx);
            if (stack == null || stack.IsEmpty || stack.item == null) return;

            GetDropPose(out Vector3 spawnPos, out Vector3 tossDir);

            int capacity = VoxelEngine.Items.DroppedItem.AvailablePhysicalCapacity;
            int spawnCount = allowVoidOverflow ? Mathf.Min(stack.count, Mathf.Max(0, capacity)) : stack.count;
            int voided = allowVoidOverflow ? Mathf.Max(0, stack.count - spawnCount) : 0;

            DroppedItem dropped = null;
            if (spawnCount > 0)
            {
                var spawnStack = new ItemStack
                {
                    item = stack.item,
                    count = spawnCount,
                    durability = stack.durability,
                    charge = stack.charge,
                    payload = stack.payload
                };
                dropped = VoxelEngine.Items.DroppedItem.Spawn(spawnStack, spawnPos, tossDir);
                if (dropped != null) dropped.SetDropOwner(inventory);
            }

            if (!allowVoidOverflow && dropped == null)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Drop Limit Reached",
                    $"Physical world limit: {VoxelEngine.Items.DroppedItem.MaximumPhysicalItemCount:N0}",
                    stack.item.icon, new Color(0.95f, 0.55f, 0.20f));
                return;
            }

            if (allowVoidOverflow)
            {
                c.SetSlot(idx, new ItemStack());
            }
            else
            {
                int droppedCount = dropped != null ? dropped.stack.count : 0;
                int remaining = Mathf.Max(0, stack.count - droppedCount);
                var retained = remaining > 0
                    ? new ItemStack { item = stack.item, count = remaining, durability = stack.durability, charge = stack.charge, payload = stack.payload }
                    : new ItemStack();
                c.SetSlot(idx, retained);
            }

            if (spawnCount > 0 && voided > 0)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    $"Dropped {stack.item.displayName}",
                    $"-{spawnCount:N0} · {voided:N0} voided",
                    stack.item.icon,
                    UITheme.AccentAmber);
            }
            else if (voided > 0)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    $"Voided {stack.item.displayName}",
                    $"{voided:N0} item unit{(voided == 1 ? "" : "s")} removed",
                    stack.item.icon,
                    UITheme.AccentRed);
            }
            else
            {
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    $"Dropped {stack.item.displayName}",
                    $"-{spawnCount:N0}",
                    stack.item.icon,
                    new Color(0.85f, 0.35f, 0.25f));
            }
        }

        private void GetDropPose(out Vector3 spawnPos, out Vector3 tossDir)
        {
            // Spawn the drop a short distance in front of the player at chest
            // height. Using a fixed offset relative to the player root (not the
            // camera) avoids Camera.main/null and near-clip culling failures.
            if (inventory != null)
            {
                var root = inventory.transform;
                tossDir = root.forward;
                spawnPos = root.position + Vector3.up * 1.0f + root.forward * 1.0f;
            }
            else
            {
                spawnPos = Vector3.up * 2f;
                tossDir  = Vector3.forward;
            }
        }

        // ============================================================
        //                     RECIPE ROW
        // ============================================================

        private CraftQueue FindNearestQueueForTier(Crafting.StationTier tier, Vector3 origin)
        {
            CraftQueue best = null;
            float bestSqr = stationRadius * stationRadius;
            var stations = FindObjectsByType<Crafting.CraftingStation>(FindObjectsInactive.Exclude);
            foreach (var st in stations)
            {
                if (st.tier != tier) continue;
                float d = (st.transform.position - origin).sqrMagnitude;
                if (d > bestSqr) continue;
                var q = st.GetComponent<CraftQueue>();
                if (q == null) q = st.gameObject.AddComponent<CraftQueue>();
                bestSqr = d;
                best = q;
            }
            return best;
        }
        private VisualElement BuildRecipeRow(RecipeDefinition recipe, IItemContainer source, IItemContainer dest)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 6; row.style.paddingBottom = 6;
            row.style.paddingLeft = 8; row.style.paddingRight = 8;
            row.style.marginBottom = 4;
            row.style.backgroundColor = new StyleColor(new Color(0.13f, 0.15f, 0.19f, 0.85f));
            SetBorderRadius(row, 4);

            // Output icon
            var icon = new VisualElement();
            icon.style.width = 36; icon.style.height = 36; icon.style.marginRight = 8;
            icon.style.backgroundColor = new StyleColor(recipe.outputItem != null ? recipe.outputItem.iconTint : Color.gray);
            SetBorderRadius(icon, 3);
            if (recipe.GetIcon() != null)
            {
                var img = new Image { sprite = recipe.GetIcon() };
                img.scaleMode = ScaleMode.ScaleToFit; // match BuildSlot: tight-cropped generated icons must fit, not crop (fixes blank recipe/crafter icons)
                img.style.width = 36; img.style.height = 36;
                icon.Add(img);
            }
            row.Add(icon);

            // Name + ingredient list
            var info = new VisualElement(); info.style.flexGrow = 1;
            var name = new Label($"{recipe.GetName()} x{recipe.outputCount}");
            name.style.color = Color.white; name.style.fontSize = 13;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            info.Add(name);
            // Show craft time under the name (only if it has one).
            if (recipe.craftSeconds > 0f)
            {
                var time = new Label($"⏱  {recipe.craftSeconds:0.0}s craft time");
                time.style.color = new StyleColor(new Color(0.95f, 0.85f, 0.30f));
                time.style.fontSize = 10;
                info.Add(time);
            }
            else
            {
                var time = new Label("⏱  Instant");
                time.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.65f));
                time.style.fontSize = 10;
                info.Add(time);
            }
            var inglist = new System.Text.StringBuilder();
            foreach (var ing in recipe.inputs)
            {
                if (ing.item == null) continue;
                int have = source.CountOf(ing.item);
                inglist.Append(have >= ing.count ? "<color=#9be19b>" : "<color=#e19b9b>");
                inglist.Append($"{have}/{ing.count} {ing.item.displayName}</color>   ");
            }
            var ing2 = new Label(inglist.ToString());
            ing2.enableRichText = true;
            ing2.style.fontSize = 11; ing2.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.75f));
            ing2.style.whiteSpace = WhiteSpace.Normal;
            info.Add(ing2);
            row.Add(info);

            // --- Live progress bar for this recipe (filled if it's in any reachable queue) ---
            CraftQueue queueForThisRecipe = _activeQueue;
            if (queueForThisRecipe == null && recipe.requiredStation != Crafting.StationTier.None && inventory != null)
                queueForThisRecipe = FindNearestQueueForTier(recipe.requiredStation, inventory.transform.position);
            int sameRecipeQueued = 0;
            float headProgress01 = 0f;
            if (queueForThisRecipe != null)
            {
                foreach (var e in queueForThisRecipe.entries)
                    if (e.recipe == recipe) sameRecipeQueued++;
                if (queueForThisRecipe.entries.Count > 0 && queueForThisRecipe.entries[0].recipe == recipe)
                    headProgress01 = recipe.craftSeconds > 0
                        ? Mathf.Clamp01(queueForThisRecipe.entries[0].progressSeconds / recipe.craftSeconds)
                        : 0f;
            }

            // Progress bar (always present; just empty if not queued).
            var progressBox = new VisualElement();
            progressBox.style.width = 90; progressBox.style.alignSelf = Align.Center;
            progressBox.style.marginRight = 6;
            var (bar, fill) = MakeProgressBarWithFill(headProgress01, new Color(0.20f, 0.65f, 0.95f), width: 90, height: 8);
            progressBox.Add(bar);
            var qcount = new Label(sameRecipeQueued > 0 ? $"x{sameRecipeQueued}" : "");
            qcount.style.color = new StyleColor(new Color(0.95f, 0.85f, 0.20f));
            qcount.style.fontSize = 10;
            qcount.style.unityFontStyleAndWeight = FontStyle.Bold;
            qcount.style.unityTextAlign = TextAnchor.MiddleCenter;
            progressBox.Add(qcount);
            row.Add(progressBox);

            // --- Craft button ---
            bool can = Crafter.HasIngredients(source, recipe);
            bool atMaxQueue = sameRecipeQueued >= 10;
            var btn = new Button(() => {
                CraftQueue qNow = _activeQueue;
                if (qNow == null && recipe.requiredStation != Crafting.StationTier.None && inventory != null)
                    qNow = FindNearestQueueForTier(recipe.requiredStation, inventory.transform.position);
                if (Crafter.TryCraft(source, dest, recipe, qNow)) Refresh();
            }) { text = "CRAFT" };
            btn.style.minHeight = 32; btn.style.minWidth = 80; btn.style.color = Color.white;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.backgroundColor = new StyleColor((can && !atMaxQueue)
                ? new Color(0.20f, 0.55f, 0.85f)
                : new Color(0.30f, 0.30f, 0.34f));
            btn.SetEnabled(can && !atMaxQueue);
            SetBorderRadius(btn, 4);
            ZeroBorder(btn);
            row.Add(btn);

            return row;
        }

        private void TickUpgradePrompt()
        {
            if (inventory == null) return;
            var stack = inventory.ActiveStack;
            bool holdingHammer = !stack.IsEmpty && stack.item != null
                                 && stack.item.GetType().Name == "Hammer";
            VoxelEngine.Building.Tiered.PlacedTieredBlock target = null;
            if (holdingHammer)
            {
                // Use the player's main camera.
                var cam = Camera.main;
                if (cam != null)
                {
                    var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
                    if (Physics.Raycast(ray, out var hit, 8f))
                        target = hit.collider.GetComponentInParent<VoxelEngine.Building.Tiered.PlacedTieredBlock>();
                }
            }
            UpgradePromptHud.Tick(target, inventory, holdingHammer);
        }

        private void TickFurnaceLiveUI()
        {
            if (_openFurnace != null)
            {
                var f = _openFurnace;

                if (_liveFlame != null)
                {
                    if (f.IsBurning)
                    {
                        float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
                        _liveFlame.style.backgroundColor = new StyleColor(new Color(1f, 0.55f * pulse, 0.10f));
                    }
                    else
                    {
                        _liveFlame.style.backgroundColor = new StyleColor(new Color(0.30f, 0.30f, 0.35f));
                    }
                }
                if (_liveSmeltFill != null)
                    _liveSmeltFill.style.width = new StyleLength(new Length(Mathf.Clamp01(f.SmeltProgress01) * 100, LengthUnit.Percent));
                if (_liveSmeltLabel != null)
                    _liveSmeltLabel.text = f.Current != null ? $"{f.SmeltProgress01 * 100f:0}% smelted" : "No input";
                if (_liveFuelFill != null)
                    _liveFuelFill.style.width = new StyleLength(new Length(Mathf.Clamp01(f.FuelProgress01) * 100, LengthUnit.Percent));
                if (_liveFuelStat != null)
                    _liveFuelStat.text = f.FuelMaxDuration > 0 ? $"{f.FuelRemaining:0.0}s / {f.FuelMaxDuration:0.0}s" : "No fuel";
                if (_liveStatusPill != null && _liveStatusLabel != null)
                {
                    string txt = f.IsBurning ? "BURNING" : (f.Current != null ? "OUT OF FUEL" : "IDLE");
                    Color bg = f.IsBurning ? new Color(0.95f, 0.50f, 0.15f)
                              : (f.Current != null ? new Color(0.70f, 0.30f, 0.20f) : new Color(0.30f, 0.30f, 0.35f));
                    _liveStatusLabel.text = txt;
                    _liveStatusPill.style.backgroundColor = new StyleColor(bg);
                }
            }
            else if (_openElectric != null)
            {
                var ef = _openElectric;

                if (_liveFlame != null)
                {
                    bool active = ef.IsOnline && ef.Current != null;
                    if (active)
                    {
                        float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
                        _liveFlame.style.backgroundColor = new StyleColor(new Color(0.30f, 0.75f * pulse, 1f));
                    }
                    else
                    {
                        _liveFlame.style.backgroundColor = new StyleColor(new Color(0.30f, 0.30f, 0.35f));
                    }
                }
                if (_liveSmeltFill != null)
                    _liveSmeltFill.style.width = new StyleLength(new Length(Mathf.Clamp01(ef.SmeltProgress01) * 100, LengthUnit.Percent));
                if (_liveSmeltLabel != null)
                    _liveSmeltLabel.text = ef.Current != null ? $"{ef.SmeltProgress01 * 100f:0}% smelted"
                                                              : (ef.IsOnline ? "No input" : "No power");
                if (_liveStatusPill != null && _liveStatusLabel != null)
                {
                    _liveStatusLabel.text = ef.IsOnline ? "ONLINE" : "OFFLINE";
                    _liveStatusPill.style.backgroundColor = new StyleColor(
                        ef.IsOnline ? new Color(0.20f, 0.60f, 0.30f) : new Color(0.60f, 0.20f, 0.20f));
                }
                if (_liveWattLabel != null) _liveWattLabel.text = $"  {ef.CurrentWattage:0} W";
            }
        }

        // ============================================================
        //          UPDATE-LOOP DRAG/DROP & HOTKEY-ON-HOVER
        // ============================================================
        /// <summary>True if the cursor is currently over an interactive UI control (a Button or
        /// inside a ScrollView). Used to DEFER the destructive 4 Hz panel rebuild so hovering /
        /// clicking the ship terminal works first time instead of flashing + eating clicks.</summary>
        private bool PointerOverInteractiveUI()
        {
            if (_root?.panel == null) return false;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return false;
            Vector2 sp = mouse.position.ReadValue();
#else
            Vector2 sp = Input.mousePosition;
#endif
            if (!HasLivePanel()) return false;
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(_root.panel,
                new Vector2(sp.x, Screen.height - sp.y));
            var picked = _root.panel.Pick(panelPos);
            // Walk up the hierarchy: if the hovered element (or an ancestor) is a Button, pause.
            for (var e = picked; e != null; e = e.parent)
                if (e is Button) return true;
            return false;
        }

        private void UpdateDragDrop()
        {
            if (!_inventoryOpen) return;
            if (_dropVoidOverlay != null && _dropVoidOverlay.parent != null) return;

            // --- Read mouse state directly from the device ---
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;
            Vector2 screenPos    = mouse.position.ReadValue();
            bool    lmbPressed   = mouse.leftButton.wasPressedThisFrame;
            bool    rmbPressed   = mouse.rightButton.wasPressedThisFrame;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool    shiftHeld    = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
#else
            Vector2 screenPos    = Input.mousePosition;
            bool    lmbPressed   = Input.GetMouseButtonDown(0);
            bool    rmbPressed   = Input.GetMouseButtonDown(1);
            bool    shiftHeld    = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            // Convert screen pixels -> panel coords (UI uses Y-down origin).
            if (!HasLivePanel()) return;
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(_root.panel,
                new Vector2(screenPos.x, Screen.height - screenPos.y));

            var slotRef = FindSlotAt(panelPos);

            if (lmbPressed)
            {
                if (slotRef == null)
                {
                    if (_dragSource.active)
                    {
                        // QoL: dragging an inventory item onto the empty area inside
                        // a storage terminal (or with a wireless transmitter online)
                        // INSERTS it into the network instead of tossing it to the
                        // ground. Dragging onto truly empty UI still drops to world,
                        // because some players use that as a quick discard gesture
                        // when no storage panel is open.
                        if (_dragSource.container == inventory?.container &&
                            TryInsertDraggedIntoNetwork())
                        {
                            CancelDrag();
                        }
                        else
                        {
                            DropItemFromSlot(_dragSource.container, _dragSource.slotIndex);
                            CancelDrag();
                        }
                    }
                    return;
                }
                // If a filter dialog is open and capturing, shift-clicking (or even
                // plain clicking) an item routes it INTO the filter instead of the
                // normal quick-transfer / pick-up.
                if (VoxelEngine.UI.ItemFilterDialog.IsCapturing)
                {
                    var capStack = slotRef.container.GetSlot(slotRef.index);
                    if (!capStack.IsEmpty &&
                        VoxelEngine.UI.ItemFilterDialog.TryCaptureItem(capStack.item))
                        return;
                }

                // Shift+LMB = quick-transfer to the OTHER side (player inventory <-> open container).
                if (shiftHeld)
                {
                    QuickTransfer(slotRef.container, slotRef.index);
                    return;
                }
                if (_dragSource.active)
                {
                    EndDrag(slotRef.container, slotRef.index);
                }
                else
                {
                    var stack = slotRef.container.GetSlot(slotRef.index);
                    if (!stack.IsEmpty) BeginDrag(slotRef.container, slotRef.index);
                }
            }
            else if (rmbPressed && slotRef != null)
            {
                SplitOrMove(slotRef.container, slotRef.index);
            }
        }

        // Hotkey-on-hover: pressing 1..9/0 while hovering an inventory slot SWAPS that
        // slot with the corresponding hotbar slot. Implements the "press 1 to swap with
        // your active hotbar slot" quick-access inventory pattern.

        /// <summary>Shift-click: send the whole stack at (sourceC, sourceIdx) to the "other side".
        /// Smart routing: from player inventory, fuel items go to fuel slot, anything else goes to input.
        /// From any furnace slot back to player.
        ///
        /// Order matters here: an *explicit* container (chest / furnace / server rack / disk
        /// manipulator) ALWAYS wins over the wireless-storage-terminal proxy that
        /// OpenInventory() creates automatically. Without that ordering, shift-clicking coal
        /// while a furnace was open would route it into the wireless network instead of the
        /// furnace's fuel slot.</summary>
        private void QuickTransfer(IItemContainer sourceC, int sourceIdx)
        {
            if (inventory == null) return;
            var srcStack = sourceC.GetSlot(sourceIdx);
            if (srcStack.IsEmpty) return;

            var equipment = inventory.GetComponent<VoxelEngine.Player.PlayerEquipment>();
            var jetpackSlots = equipment != null ? equipment.JetpackSlots : null;
            var helmetSlots = equipment != null ? equipment.HelmetSlots : null;
            var oxygenSlots = equipment != null ? equipment.OxygenTankSlots : null;
            var armorSlots  = equipment != null ? equipment.ArmorSlots      : null;
            // Shift-click auto-equips only from a plain inventory view. When a
            // station, chest, or other external panel is open, the active panel
            // owns the transfer destination instead.
            bool allowQuickEquip = !AnyCenterOrRightPanelOpen();

            if (sourceC == inventory.container && allowQuickEquip && srcStack.item is SpaceHelmetItem && helmetSlots != null)
            {
                var cloneHelmet = new ItemStack { item = srcStack.item, count = 1, durability = srcStack.durability, charge = srcStack.charge, payload = srcStack.payload };
                var leftoverHelmet = helmetSlots.Insert(cloneHelmet);
                if (leftoverHelmet == null || leftoverHelmet.count <= 0)
                {
                    srcStack.count -= 1;
                    sourceC.SetSlot(sourceIdx, srcStack.count <= 0 ? new ItemStack() : srcStack);
                    BuildFeedbackHud.Show("Helmet Equipped", srcStack.item.displayName, srcStack.item.icon, srcStack.item.iconTint);
                    Refresh();
                }
                else BuildFeedbackHud.Show("Helmet Slot Full", "Remove the current helmet first", srcStack.item.icon, Color.yellow);
                return;
            }

            if (sourceC == inventory.container && allowQuickEquip && srcStack.item is OxygenTankItem && oxygenSlots != null)
            {
                var cloneTank = new ItemStack { item = srcStack.item, count = 1, durability = srcStack.durability, charge = srcStack.charge, payload = srcStack.payload };
                var leftoverTank = oxygenSlots.Insert(cloneTank);
                if (leftoverTank == null || leftoverTank.count <= 0)
                {
                    srcStack.count -= 1;
                    sourceC.SetSlot(sourceIdx, srcStack.count <= 0 ? new ItemStack() : srcStack);
                    BuildFeedbackHud.Show("Oxygen Tank Equipped", srcStack.item.displayName, srcStack.item.icon, srcStack.item.iconTint);
                    Refresh();
                }
                else BuildFeedbackHud.Show("Oxygen Slot Full", "Remove the current tank first", srcStack.item.icon, Color.yellow);
                return;
            }

            if (sourceC == inventory.container && allowQuickEquip && srcStack.item is VoxelEngine.Combat.ArmorItem && armorSlots != null)
            {
                var cloneArmor = new ItemStack { item = srcStack.item, count = 1, durability = srcStack.durability, charge = srcStack.charge, payload = srcStack.payload };
                var leftoverArmor = armorSlots.Insert(cloneArmor);
                if (leftoverArmor == null || leftoverArmor.count <= 0)
                {
                    srcStack.count -= 1;
                    sourceC.SetSlot(sourceIdx, srcStack.count <= 0 ? new ItemStack() : srcStack);
                    BuildFeedbackHud.Show("Armor Equipped", srcStack.item.displayName, srcStack.item.icon, srcStack.item.iconTint);
                    Refresh();
                }
                else BuildFeedbackHud.Show("Armor Slot Full", "Remove the current armor first", srcStack.item.icon, Color.yellow);
                return;
            }

            if ((helmetSlots != null && sourceC == helmetSlots) || (oxygenSlots != null && sourceC == oxygenSlots) ||
                (armorSlots != null && sourceC == armorSlots))
            {
                var cloneGear = new ItemStack { item = srcStack.item, count = srcStack.count, durability = srcStack.durability, charge = srcStack.charge, payload = srcStack.payload };
                var leftoverGear = inventory.container.Insert(cloneGear);
                int movedGear = leftoverGear == null ? srcStack.count : (srcStack.count - leftoverGear.count);
                if (movedGear > 0)
                {
                    if (movedGear >= srcStack.count) sourceC.SetSlot(sourceIdx, new ItemStack());
                    else { srcStack.count -= movedGear; sourceC.SetSlot(sourceIdx, srcStack); }
                    Refresh();
                }
                return;
            }

            // Gas dock QoL: with a hydrogen Gas Tank open (world or grid), shift-clicking
            // a portable H₂ tank or a hydrogen jetpack docks it for filling (before the
            // jetpack auto-equip routing below can claim the pack).
            IItemContainer openGasDock = null;
            if (_openGasTank != null && _openGasTank.IsHydrogenMode)
            {
                _openGasTank.EnsureContainers();
                openGasDock = _openGasTank.PortableSlot;
            }
            else if (_openGridBlock is VoxelEngine.GridSystem.GridGasTank openGridGas && openGridGas.IsHydrogenMode)
            {
                openGridGas.EnsureContainers();
                openGasDock = openGridGas.PortableSlot;
            }
            if (sourceC == inventory.container && openGasDock != null)
            {
                bool dockable = VoxelEngine.Items.HydrogenCanisterItem.IsPortableHydrogenTank(srcStack.item)
                    || (srcStack.item is JetpackItem jpg && jpg.UsesHydrogenEffective);
                if (dockable)
                {
                    var cloneDock = new ItemStack { item = srcStack.item, count = 1, durability = srcStack.durability, charge = srcStack.charge, payload = srcStack.payload };
                    var leftoverDock = openGasDock.Insert(cloneDock);
                    if (leftoverDock == null || leftoverDock.count <= 0)
                    {
                        srcStack.count -= 1;
                        sourceC.SetSlot(sourceIdx, srcStack.count <= 0 ? new ItemStack() : srcStack);
                        BuildFeedbackHud.Show("Docked — Filling H₂", srcStack.item.displayName, srcStack.item.icon, srcStack.item.iconTint);
                        Refresh();
                    }
                    else BuildFeedbackHud.Show("Dock Occupied", "Remove the docked item first", srcStack.item.icon, Color.yellow);
                    return;
                }
            }

            // Battery dock QoL: with a Battery open, shift-clicking chargeable
            // devices (Portable Batteries / power jetpacks) docks them for charging
            // before the jetpack auto-equip routing below can claim the pack.
            if (sourceC == inventory.container && _openPowerBattery != null)
            {
                bool chargeable = VoxelEngine.Items.PortableBatteryItem.IsPortableBattery(srcStack.item)
                    || (srcStack.item is JetpackItem jpq && jpq.UsesPowerEffective);
                if (chargeable)
                {
                    _openPowerBattery.EnsureContainers();
                    var cloneCharge = new ItemStack { item = srcStack.item, count = 1, durability = srcStack.durability, charge = srcStack.charge, payload = srcStack.payload };
                    var leftoverCharge = _openPowerBattery.ChargeSlot.Insert(cloneCharge);
                    if (leftoverCharge == null || leftoverCharge.count <= 0)
                    {
                        srcStack.count -= 1;
                        sourceC.SetSlot(sourceIdx, srcStack.count <= 0 ? new ItemStack() : srcStack);
                        BuildFeedbackHud.Show("Docked for Charging", srcStack.item.displayName, srcStack.item.icon, srcStack.item.iconTint);
                        Refresh();
                    }
                    else BuildFeedbackHud.Show("Charger Occupied", "Remove the docked device first", srcStack.item.icon, Color.yellow);
                    return;
                }
            }

            // Jetpack QoL: shift-click from either hotbar or backpack equips into the
            // dedicated jetpack slots before any external machine/storage routing.
            if (sourceC == inventory.container && allowQuickEquip && srcStack.item is JetpackItem && jetpackSlots != null)
            {
                var cloneJet = new ItemStack { item = srcStack.item, count = 1, durability = srcStack.durability, charge = srcStack.charge, payload = srcStack.payload };
                var leftoverJet = jetpackSlots.Insert(cloneJet);
                if (leftoverJet == null || leftoverJet.count <= 0)
                {
                    srcStack.count -= 1;
                    sourceC.SetSlot(sourceIdx, srcStack.count <= 0 ? new ItemStack() : srcStack);
                    BuildFeedbackHud.Show("Jetpack Equipped", srcStack.item.displayName, srcStack.item.icon, srcStack.item.iconTint);
                    Refresh();
                    return;
                }
                BuildFeedbackHud.Show("Jetpack Slots Full", "Remove a pack before equipping another", srcStack.item.icon, Color.yellow);
                return;
            }

            // Shift-clicking a jetpack slot sends it back to normal inventory.
            if (jetpackSlots != null && sourceC == jetpackSlots)
            {
                var cloneBack = new ItemStack { item = srcStack.item, count = srcStack.count, durability = srcStack.durability, charge = srcStack.charge, payload = srcStack.payload };
                var leftoverBack = inventory.container.Insert(cloneBack);
                int movedBack = leftoverBack == null ? srcStack.count : (srcStack.count - leftoverBack.count);
                if (movedBack > 0)
                {
                    if (movedBack >= srcStack.count) sourceC.SetSlot(sourceIdx, new ItemStack());
                    else { srcStack.count -= movedBack; sourceC.SetSlot(sourceIdx, srcStack); }
                    Refresh();
                }
                return;
            }

            // 1) Inventory → an EXPLICIT open machine takes priority. This block must come
            // before any wireless / storage-terminal routing below, otherwise coal-into-furnace
            // breaks the moment a transmitter is online.
            if (sourceC == inventory.container)
            {
                IItemContainer explicitDest = ResolveQuickTransferDestination(sourceC, srcStack.item);
                if (explicitDest != null)
                {
                    if (explicitDest is VoxelEngine.Storage.IItemFilterSlot filterSlot)
                    {
                        filterSlot.ApplyFilter(srcStack.item);
                        Refresh();
                        return;
                    }
                    var clone1 = new ItemStack { item = srcStack.item, count = srcStack.count, durability = srcStack.durability, charge = srcStack.charge, payload = srcStack.payload };
                    var leftover1 = explicitDest.Insert(clone1);
                    int moved1 = leftover1 == null ? srcStack.count : (srcStack.count - leftover1.count);
                    if (moved1 > 0)
                    {
                        if (moved1 >= srcStack.count) sourceC.SetSlot(sourceIdx, new ItemStack());
                        else                          { srcStack.count -= moved1; sourceC.SetSlot(sourceIdx, srcStack); }
                        return;
                    }
                    // If the explicit destination refused (full, wrong type), fall through to
                    // network/inventory routing so the player isn't left holding a "stuck" stack.
                }
                // ── HOTBAR → BACKPACK quick-transfer ─────────────────────────
                // When NO machine/network destination accepted the stack AND the
                // click came from a HOTBAR slot, push the items into the first
                // free BACKPACK slot. Inversely, a click on a BACKPACK slot with
                // no machine open promotes the items down to the first free
                // HOTBAR slot. This mirrors the quick-access inventory transfer
                // convention: shift-click always sends items "to the other half"
                // of the inventory when there's no external container.
                if (sourceC is ItemContainer ic)
                {
                    bool fromHotbar = sourceIdx < Inventory.HOTBAR_SIZE;
                    int destStart = fromHotbar ? Inventory.HOTBAR_SIZE : 0;
                    int destCount = fromHotbar
                        ? (Inventory.TOTAL_SIZE - Inventory.HOTBAR_SIZE)
                        : Inventory.HOTBAR_SIZE;

                    var clone2 = new ItemStack { item = srcStack.item, count = srcStack.count, durability = srcStack.durability, charge = srcStack.charge, payload = srcStack.payload };
                    var leftover2 = ic.InsertRange(clone2, destStart, destCount);
                    int moved2 = leftover2 == null ? srcStack.count : (srcStack.count - leftover2.count);
                    if (moved2 > 0)
                    {
                        if (moved2 >= srcStack.count) sourceC.SetSlot(sourceIdx, new ItemStack());
                        else                          { srcStack.count -= moved2; sourceC.SetSlot(sourceIdx, srcStack); }
                        return;
                    }
                }
            }

            // 2) Inventory → storage terminal that the PLAYER explicitly opened.
            //    The auto-spawned wireless proxy is excluded here so it doesn't override
            //    machine routing above. The explicit terminal path is handled by the
            //    real opener (StorageTerminal interact → OpenMachine → _openStorageTerminal).
            if (sourceC == inventory.container && _openStorageTerminal != null
                && _openStorageTerminal != _wirelessTerminalProxy
                && _openStorageTerminal.ConnectedRack != null)
            {
                var rack = _openStorageTerminal.ConnectedRack;
                int netLeftover = rack.NetworkInsert(srcStack.item, srcStack.count);
                int netMoved = srcStack.count - netLeftover;
                if (netMoved > 0)
                {
                    if (netLeftover <= 0) sourceC.SetSlot(sourceIdx, new ItemStack());
                    else { srcStack.count = netLeftover; sourceC.SetSlot(sourceIdx, srcStack); }
                    BuildFeedbackHud.Show($"Stored {srcStack.item.displayName}", $"+{netMoved}", srcStack.item.icon, UITheme.AccentCyan);
                    Refresh();
                }
                return;
            }

            // 3) Plain inventory ↔ wireless transmitter (selected one) — shift-click stores
            //    into the network. This is the case where the player pressed I with a
            //    transmitter online but is NOT looking at any machine.
            if (sourceC == inventory.container)
            {
                var rack = GetActiveWirelessRack();
                if (rack != null)
                {
                    int netLeftover = rack.NetworkInsert(srcStack.item, srcStack.count);
                    int netMoved = srcStack.count - netLeftover;
                    if (netMoved > 0)
                    {
                        if (netLeftover <= 0) sourceC.SetSlot(sourceIdx, new ItemStack());
                        else { srcStack.count = netLeftover; sourceC.SetSlot(sourceIdx, srcStack); }
                        BuildFeedbackHud.Show($"Stored {srcStack.item.displayName}", $"+{netMoved}", srcStack.item.icon, UITheme.AccentCyan);
                        Refresh();
                    }
                    return;
                }
            }

            // 4) Fallback (machine → inventory etc).
            IItemContainer dest = ResolveQuickTransferDestination(sourceC, srcStack.item);
            if (dest == null) return;

            var clone = new ItemStack { item = srcStack.item, count = srcStack.count, durability = srcStack.durability, charge = srcStack.charge, payload = srcStack.payload };
            var leftover = dest.Insert(clone);
            int moved = leftover == null ? srcStack.count : (srcStack.count - leftover.count);
            if (moved <= 0) return;
            if (moved >= srcStack.count) sourceC.SetSlot(sourceIdx, new ItemStack());
            else                          { srcStack.count -= moved; sourceC.SetSlot(sourceIdx, srcStack); }
        }

        private IItemContainer ResolveQuickTransferDestination(IItemContainer sourceC, ItemDefinition item)
        {
            // If source IS the player inventory: pick the right side based on item type.
            if (inventory != null && sourceC == inventory.container)
            {
                if (_openArmorUpgradeStation != null)
                {
                    if (item is VoxelEngine.Combat.ArmorItem) return _openArmorUpgradeStation.ArmorSlot;
                    if (item is VoxelEngine.Combat.ArmorUpgradeItem) return _openArmorUpgradeStation.ModuleSlot;
                    return null;
                }
                if (_rightContainer != null) return _rightContainer;
                if (_openFurnace != null)
                {
                    bool isFuel = item is ResourceItem ri && ri.fuelSeconds > 0f;
                    return isFuel ? _openFurnace.fuelC : _openFurnace.inputC;
                }
                if (_openElectric != null)
                {
                    bool isUpgrade = item is FurnaceUpgradeItem;
                    return isUpgrade ? _openElectric.upgradeC : _openElectric.inputC;
                }
                // Coal Generator: only fuel items go in.
                if (_openCoalGen != null)
                {
                    bool isFuel = item is ResourceItem rg && rg.fuelSeconds > 0f;
                    return isFuel ? _openCoalGen.fuelC : null;
                }
                if (_openQuarry != null)
                {
                    bool isUpgrade = item is QuarryUpgradeItem;
                    return isUpgrade ? _openQuarry.upgradeC : _openQuarry.Output;
                }
                if (_openGridBlock != null)
                {
                    var gridDest = ResolveGridBlockQuickTransferDestination(_openGridBlock, item);
                    if (gridDest != null) return gridDest;
                }
                if (_openGridTerminal != null)
                {
                    var gridDest = FirstGridInventoryDestination(_openGridTerminal, item);
                    if (gridDest != null) return gridDest;
                }
                if (_openDiskManipulator != null) return _openDiskManipulator.sourceSlot;
                if (_openNAS != null)             return _openNAS.diskSlots;
                if (_openImporter != null)        return _openImporter.upgradeSlots;
                if (_openExporter != null)        return _openExporter.upgradeSlots;
                if (_openPowerstation != null)    return _openPowerstation.psuSlots;
                if (_openStorageDrawer != null)
                {
                    if (item is VoxelEngine.Storage.StorageDrawerUpgradeItem) return _openStorageDrawer.upgradeSlots;
                    return _openStorageDrawer;
                }
                if (_openItemDisplay != null) return _openItemDisplay.FilterSlot;

                // Server Rack: hardware items go to their dedicated slots — never wrong-typed.
                if (_openServerRack != null)
                {
                    if (item is VoxelEngine.Storage.StorageDisk) return _openServerRack.diskSlots;
                    if (item is VoxelEngine.Storage.ServerComponent sc)
                    {
                        switch (sc.componentType)
                        {
                            case VoxelEngine.Storage.ComponentType.CPU: return _openServerRack.cpuSlot;
                            case VoxelEngine.Storage.ComponentType.RAM: return _openServerRack.ramSlots;
                            case VoxelEngine.Storage.ComponentType.PSU: return _openServerRack.psuSlot;
                        }
                    }
                    // Any other item type is not a valid rack component — refuse the transfer
                    // so the player doesn't accidentally lose a coal stack in the disk slots.
                    return null;
                }
                return null;
            }
            // Source is ANY non-player container: send to the player inventory.
            return inventory.container;
        }

        private static IItemContainer ResolveGridBlockQuickTransferDestination(VoxelEngine.GridSystem.GridBlock block, ItemDefinition item)
        {
            if (block == null || item == null) return null;
            switch (block)
            {
                case VoxelEngine.GridSystem.GridCargoContainer cargo:
                    return cargo.container;
                case VoxelEngine.GridSystem.GridDockingPort dock:
                    return dock.container;
                case VoxelEngine.GridSystem.GridDrill drill:
                    return drill.buffer;
                // Grid gas tank: portable H₂ tanks + hydrogen jetpacks dock here
                // (the slot's own AcceptFilter guards the types).
                case VoxelEngine.GridSystem.GridGasTank gridGas:
                    gridGas.EnsureContainers();
                    return gridGas.PortableSlot;
                case VoxelEngine.GridSystem.GridWeapon weapon:
                    return weapon.ammo;
                case VoxelEngine.GridSystem.GridH2O2Generator h2:
                    return IsItemId(item, "ice") ? h2.iceInput : null;
                case VoxelEngine.GridSystem.GridElectricFurnace furnace:
                    return furnace.inputC;
                case VoxelEngine.GridSystem.GridPortableReactor reactor:
                    if (reactor.leuPelletItem != null && item == reactor.leuPelletItem) return reactor.fuelC;
                    if (reactor.iceItem != null && item == reactor.iceItem) return reactor.iceC;
                    return reactor.fuelC;
                case VoxelEngine.Maritime.GridMaritimeEngine engine:
                {
                    // Upgrade modules route to the engine module slots (tier-filtered).
                    if (item is VoxelEngine.Items.EngineModuleItem && engine.CanSocketModule(item))
                        return engine.GetModuleSlots();
                    // Burnable items (wood logs, planks, coal) route to the fuel hopper.
                    // The hopper's own AcceptFilter keeps anything else out.
                    if (engine.SolidFuelInput != null && item is VoxelEngine.Items.ResourceItem res && res.fuelSeconds > 0f)
                        return engine.SolidFuelInput;
                    return null;
                }
                case VoxelEngine.Maritime.GridMaritimeGenerator generator:
                    if (item is VoxelEngine.Items.EngineModuleItem && generator.CanSocketModule(item))
                        return generator.GetModuleSlots();
                    return null;
                default:
                    return null;
            }
        }

        private static IItemContainer FirstGridInventoryDestination(VoxelEngine.GridSystem.GridEntity grid, ItemDefinition item)
        {
            if (grid == null || item == null) return null;
            foreach (var kv in grid.Blocks)
            {
                var dest = ResolveGridBlockQuickTransferDestination(kv.Value, item);
                if (dest == null) continue;
                if (ContainerHasSpaceFor(dest, item, 1)) return dest;
            }
            return null;
        }

        private static bool ContainerHasSpaceFor(IItemContainer container, ItemDefinition item, int count)
        {
            if (container == null || item == null || count <= 0) return false;

            if (container is ItemContainer itemContainer && itemContainer.AcceptFilter != null
                && itemContainer.AcceptFilter(item, count) <= 0)
                return false;

            foreach (var slot in container.Slots)
            {
                if (slot == null || slot.IsEmpty) return true;
                if (slot.item == item && item.IsStackable && slot.count < ItemStack.MaxItemsPerStack(item)) return true;
            }

            return false;
        }

        private static bool IsItemId(ItemDefinition item, string id)
        {
            return item != null && item.itemId != null
                && item.itemId.Equals(id, System.StringComparison.OrdinalIgnoreCase);
        }
        private void SwapHoveredWithHotbar(int hotbarIdx)
        {
            if (!_inventoryOpen) return;
            if (inventory == null || inventory.container == null) return;
            if (hotbarIdx < 0 || hotbarIdx >= Inventory.HOTBAR_SIZE) return;

#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;
            Vector2 screenPos = mouse.position.ReadValue();
#else
            Vector2 screenPos = Input.mousePosition;
#endif
            if (!HasLivePanel()) return;
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(_root.panel,
                new Vector2(screenPos.x, Screen.height - screenPos.y));

            var hovered = FindSlotAt(panelPos);
            if (hovered == null) return;
            // Don't swap with itself.
            if (hovered.container == inventory.container && hovered.index == hotbarIdx) return;

            var hoveredStack = hovered.container.GetSlot(hovered.index).Clone();
            var hotbarStack  = inventory.container.GetSlot(hotbarIdx).Clone();
            hovered.container.SetSlot(hovered.index, hotbarStack);
            inventory.container.SetSlot(hotbarIdx, hoveredStack);
        }

        // ============================================================
        //                       UI HELPERS
        // ============================================================
        // ── Internal UI Helpers (delegate to UITheme for full consistency) ──────
        private static VisualElement MakePanel()           => UITheme.Panel();
        private static void DockRightPanel(VisualElement panel, float maxWidth = 500f)
        {
            panel.style.position = Position.Absolute;
            panel.style.top = 24;
            panel.style.bottom = 92;
            panel.style.right = 18;
            panel.style.width = new StyleLength(new Length(30f, LengthUnit.Percent));
            panel.style.minWidth = 320;
            panel.style.maxWidth = maxWidth;
            panel.style.overflow = Overflow.Hidden;
        }
        private static Label         MakeTitle(string t)   => UITheme.Title(t);
        private static Label         MakeSubtitle(string t) => UITheme.Subtitle(t);
        private static Label         MakeMutedLabel(string t) => UITheme.Muted(t);
        private static VisualElement Spacer(float h)        => UITheme.Spacer(h);
        private static VisualElement MakeColumn(string label, VisualElement child)
        {
            var col = new VisualElement();
            col.style.alignItems = Align.Center;
            var l = UITheme.StatLabel(label);
            l.style.marginBottom = 3;
            col.Add(l);
            col.Add(child);
            return col;
        }
        private static void SetBorderRadius(VisualElement v, float r) => UITheme.Radius(v, r);
        private static void ZeroBorder(VisualElement v)                => UITheme.Border(v, 0, Color.clear);

        // Press DropItem (default 'O') while hovering ANY slot in an open UI to drop
        // that slot's stack into the world.
        private void CheckDropKey()
        {
            if (_searchHasFocus || !_inventoryOpen) return;
            if (_dropVoidOverlay != null && _dropVoidOverlay.parent != null) return;
            if (!GameSettings.WasPressed(InputAction.DropItem)) return;

#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;
            Vector2 screenPos = mouse.position.ReadValue();
#else
            Vector2 screenPos = Input.mousePosition;
#endif
            if (!HasLivePanel()) return;
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(_root.panel,
                new Vector2(screenPos.x, Screen.height - screenPos.y));
            var hovered = FindSlotAt(panelPos);
            if (hovered == null) return;
            var stack = hovered.container.GetSlot(hovered.index);
            if (stack == null || stack.IsEmpty) return;
            DropItemFromSlot(hovered.container, hovered.index);
        }

        private void CheckHotbarKey(InputAction act, int slotIdx)
        {
            if (_searchHasFocus) return; // typing in search bar — don't intercept digits
            if (UIState.IsBlocking && !_inventoryOpen) return; // modal text fields (cryobed/death/etc.) own number keys
            if (!GameSettings.WasPressed(act) || inventory == null) return;
            if (_inventoryOpen)
            {
                SwapHoveredWithHotbar(slotIdx);
            }
            else
            {
                inventory.SetActiveHotbar(slotIdx);
            }
        }

        private void UnlockCursor() { /* UIState owns cursor state now */ }
        private void RelockCursor() { /* UIState owns cursor state now */ }

        /// <summary>
        /// Called by StorageUI sub-panels when they need an immediate re-render
        /// (e.g. pattern added/removed).
        /// </summary>
        public void RefreshCurrentPanel() => Refresh();

        // ── Powerstation inline panel ────────────────────────────────
        private VisualElement BuildPowerstationPanel(VoxelEngine.Storage.Powerstation ps)
        {
            ps.EnsureContainers();
            var p = MakePanel();
            DockRightPanel(p, 484);

            var (hdr, _, _, _) = UITheme.HeaderRow("🔌 Powerstation",
                ps.TotalWatts > 0 ? "ACTIVE" : "EMPTY",
                ps.TotalWatts > 0 ? UITheme.AccentGold : UITheme.TextMuted);
            p.Add(hdr);
            p.Add(UITheme.AccentDivider(UITheme.AccentGold));
            p.Add(UITheme.StatRow("⚡", "Total Output", $"{ps.TotalWatts:0} W", UITheme.AccentGold));
            p.Add(UITheme.Divider());
            p.Add(UITheme.Subtitle("PSU Slots (4)"));
            var grid = UITheme.SlotGrid();
            for (int i = 0; i < ps.psuSlots.Size; i++)
                grid.Add(BuildSlot(ps.psuSlots, i, ps.psuSlots.GetSlot(i), false, true));
            p.Add(grid);
            p.Add(UITheme.Spacer(8));
            p.Add(UITheme.Muted("Each PSU module adds to the power capacity of the nearest Server Rack. " +
                                "Only PSU items may be inserted."));
            return p;
        }
    }
}
