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
        private bool _previousLooking;

        // Drag-drop state
        private DragSource _dragSource;
        private VisualElement _dragGhost;
        private VisualElement _dropVoidOverlay;

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
            // Wire premium click/hover audio for the whole in-game UI in one place.
            VoxelEngine.FX.UiAudio.Attach(_root);

            // Keep input polling even if Unity isn't the foreground app — fixes "game
            // not focused" feeling on some Windows setups where the Editor steals focus.
            Application.runInBackground = true;
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

            // Live-update the open furnace panel in-place every frame (no rebuild needed).
            TickFurnaceLiveUI();
            PlayerHud.Tick();
            RustStyleHud.Tick();
            BuildFeedbackHud.Tick();
            VoxelEngine.Weather.WeatherHud.Tick();
            InteractionHud.Tick();
            WorldInspectionHud.Tick();
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
            if (_machineRefreshAccum >= 0.25f && !PortConfigHud.IsAnyDropdownOpen && liveMachineOpen
                && !_dragSource.active && !PointerOverInteractiveUI())
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
            bool typing = _searchHasFocus || RecipeBrowserUI.IsSearchFocused || PortConfigHud.IsAnyDropdownOpen;

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
            _openReprocessor= null; _openElectrolyser= null;
            _openHydroEngine= null; _openGasTank     = null; _openWaterPump = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
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
            _openElectrolyser = null; _openHydroEngine = null; _openGasTank = null; _openWaterPump = null; _openWindTurbine = null;
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
            _openReprocessor= null; _openElectrolyser= null;
            _openHydroEngine= null; _openGasTank     = null; _openWaterPump = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _openStation    = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null; _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
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
            _openReprocessor= null; _openElectrolyser= null;
            _openHydroEngine= null; _openGasTank     = null; _openWaterPump = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _rightContainer = null; _openChest = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null; _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
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
            _openReprocessor= null; _openElectrolyser= null;
            _openHydroEngine= null; _openGasTank     = null; _openWaterPump = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _rightContainer = null; _openChest = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null; _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
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
            _openReprocessor= null; _openElectrolyser= null;
            _openHydroEngine= null; _openGasTank     = null; _openWaterPump = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _rightContainer = null; _openChest = null; _openStation = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null; _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
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
            _openProcessor = null; _openReprocessor = null; _openElectrolyser = null;
            _openHydroEngine = null; _openGasTank = null; _openWaterPump = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _openStorageTerminal = null; _openServerRack = null;
            _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null;
            _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openVoltageStation = null;
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
                case VoxelEngine.Gas.GasTank gt: _openGasTank = gt; break;
                case VoxelEngine.Fluids.WaterPump wp: _openWaterPump = wp; wp.ScanSource(); break;
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
                    else if (gb is VoxelEngine.GridSystem.GridWeapon gw) { if (gw.ammo == null) gw.OnPlaced(); WatchContainer(gw.ammo); }
                    else if (gb is VoxelEngine.GridSystem.GridDockingPort gdp) { if (gdp.container == null) gdp.OnPlaced(); WatchContainer(gdp.container); }
                    else if (gb is VoxelEngine.GridSystem.GridPortableReactor gpr) { if (gpr.fuelC == null) gpr.OnPlaced(); WatchContainer(gpr.fuelC); WatchContainer(gpr.iceC); WatchContainer(gpr.wasteC); }
                    else if (gb is VoxelEngine.GridSystem.GridDrill gdr) { if (gdr.buffer == null) gdr.OnPlaced(); WatchContainer(gdr.buffer); }
                    else if (gb is VoxelEngine.GridSystem.GridElectricFurnace gef) { if (gef.inputC == null) gef.OnPlaced(); WatchContainer(gef.inputC); WatchContainer(gef.outputC); }
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
            _openProcessor = null; _openReprocessor = null; _openElectrolyser = null;
            _openHydroEngine = null; _openGasTank = null; _openWaterPump = null; _openWindTurbine = null; _openGridBlock = null;
            _openOilRefinery = null; _openChemPlant = null;
            _openStorageTerminal = null; _openServerRack = null;
            _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null;
            _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
            _openGridTerminal = grid; _terminalTab = -1;
            _inventoryOpen = true;
            UnwatchAllContainers();
            UnlockCursor();
            Refresh();
        }

        public void OpenStation(CraftingStation st)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            _openStation    = st;
            _rightContainer = null; _openChest = null;
            _openFurnace    = null;
            _openElectric   = null;
            _openCoalGen    = null;
            _openQuarry     = null;
            _openReactor    = null; _openTurbine     = null;
            _openPortReactor= null; _openProcessor   = null;
            _openReprocessor= null; _openElectrolyser= null;
            _openHydroEngine= null; _openGasTank     = null; _openWaterPump = null; _openWindTurbine = null; _openGridBlock = null; _openOilRefinery = null; _openChemPlant = null; _openGridTerminal = null;
            _openStorageTerminal = null; _openServerRack = null; _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null; _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
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
            if (_inventoryOpen) UIState.PopBlock();
            _inventoryOpen  = false;
            _rightContainer = null; _openChest = null;
            _openFurnace    = null;
            _openElectric   = null;
            _openCoalGen    = null;
            _openStation    = null;
            _openQuarry     = null;
            _openReactor    = null; _openTurbine      = null;
            _openPortReactor= null; _openProcessor    = null;
            _openReprocessor= null; _openElectrolyser = null;
            _openHydroEngine= null; _openGasTank      = null; _openWaterPump = null; _openWindTurbine = null;
            _openGridBlock  = null; _openGridTerminal = null;
            _openOilRefinery = null; _openChemPlant = null;
            _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter   = null; _openExporter     = null;
            _openDiskManipulator = null; _openNAS     = null;
            _openPowerstation= null;
            _openStorageDrawer = null; _openDrawerController = null; _openItemDisplay = null;
            _openStorageTerminal = null; _openServerRack = null;
            _openCrusher = null; _openAssembler = null; _openFunnel = null; _openSplitter = null;
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

        private void Refresh()
        {
            // Container changes can arrive while logistics is moving items. Keep the
            // modal tree mounted instead of clearing it underneath the player; the
            // latest container state is rebuilt immediately when the overlay closes.
            if (_itemPortsOverlay != null && _itemPortsOverlay.parent != null) return;
            if (_dropVoidOverlay != null && _dropVoidOverlay.parent != null) return;
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

            _root.Clear();
            if (inventory == null) return;

            // (Re)mount the tooltip overlay; it lives at the root and is invisible until hovered.
            Tooltip.EnsureMounted(_root);
            PlayerHud.EnsureMounted(_root);
            RecipePinHud.EnsureMounted(_root);
            ResearchHud.EnsureMounted(_root);
            UpgradePromptHud.EnsureMounted(_root);

            VoxelEngine.GridSystem.UI.BlockRotationHud.EnsureMounted(_root);
            VoxelEngine.GridSystem.UI.ShipToolHud.EnsureMounted(_root);
            RustStyleHud.EnsureMounted(_root);
            InteractionHud.EnsureMounted(_root);
            WorldInspectionHud.EnsureMounted(_root);
            BuildFeedbackHud.EnsureMounted(_root);
            VoxelEngine.Weather.WeatherHud.EnsureMounted(_root);
            VoxelEngine.GridSystem.GridPilotHud.EnsureMounted(_root);
            GrinderHud.EnsureMounted(_root);
            BuildCostHud.EnsureMounted(_root);

            // (We poll mouse buttons in Update() — much more reliable than RegisterCallback.)

            // Hotbar (always on)
            BuildHotbar(_root);

            if (_inventoryOpen)
            {
                _root.pickingMode = PickingMode.Position;
                _root.style.backgroundColor = new StyleColor(new Color(0,0,0,0.55f));

                // Left panel — player inventory + crafting toggle
                BuildLeftPanel(_root);

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

                    _root.Add(overlay);
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
                    _openGasTank != null || _openWaterPump != null || _openWindTurbine != null || _openStorageTerminal != null || _openServerRack != null ||
                    _openPatternTerminal != null || _openCraftTerminal != null || _openImporter != null ||
                    _openExporter != null || _openDiskManipulator != null || _openNAS != null ||
                    _openPowerstation != null || _openStorageDrawer != null ||
                    _openDrawerController != null || _openItemDisplay != null ||
                    _openCrusher != null || _openAssembler != null || _openFunnel != null || _openSplitter != null;
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
                    _openGasTank != null || _openWaterPump != null || _openWindTurbine != null || _openStorageTerminal != null || _openServerRack != null ||
                    _openPatternTerminal != null || _openCraftTerminal != null || _openImporter != null ||
                    _openExporter != null || _openDiskManipulator != null || _openNAS != null ||
                    _openPowerstation != null || _openStorageDrawer != null ||
                    _openDrawerController != null || _openItemDisplay != null ||
                    _openCrusher != null || _openAssembler != null || _openFunnel != null || _openSplitter != null;
                // The station pane (_openStation) renders its OWN crafting list on
                // the right, so we suppress the center panel only in that case.
                // For every other right panel (chest / furnace / storage terminal)
                // we keep crafting available — the panel simply shrinks to sit in
                // the gap between the inventory and the right panel.
                if (CraftingScreen.Visible && _openStation == null)
                {
                    BuildCenterCrafting(_root, aRightPanelIsOpen);
                    _craftPanelWasVisible = true;
                }
                else _craftPanelWasVisible = false;

                // Right panel — container or station
                if (_productionStatsOpen) _root.Add(ProductionStatsUI.BuildPanel());
                else if (_recipeBrowserOpen) _root.Add(RecipeBrowserUI.BuildPanel(recipeRegistry, inventory));
                else if (_rightContainer != null) BuildRightContainer(_root, _rightContainer);
                else if (_openFurnace  != null) BuildRightFurnace(_root, _openFurnace);
                else if (_openElectric != null) BuildRightElectricFurnace(_root, _openElectric);
                else if (_openCoalGen  != null) BuildRightCoalGenerator(_root, _openCoalGen);
                else if (_openQuarry   != null) { var mp = MachineUIs.QuarryPanel(_openQuarry, BuildSlot); _root.Add(mp); AppendItemPorts(mp, _openQuarry); }
                else if (_openReactor  != null) { var mp = MachineUIs.ReactorCorePanel(_openReactor, BuildSlot); _root.Add(mp); AppendItemPorts(mp, _openReactor); }
                else if (_openTurbine  != null) _root.Add(MachineUIs.SteamTurbinePanel(_openTurbine));
                else if (_openPortReactor != null) { var mp = MachineUIs.PortableReactorPanel(_openPortReactor, BuildSlot); _root.Add(mp); AppendItemPorts(mp, _openPortReactor); }
                else if (_openProcessor != null) { var mp = MachineUIs.UraniumProcessorPanel(_openProcessor, BuildSlot); _root.Add(mp); AppendItemPorts(mp, _openProcessor); }
                else if (_openReprocessor != null) { var mp = MachineUIs.WasteReprocessorPanel(_openReprocessor, BuildSlot); _root.Add(mp); AppendItemPorts(mp, _openReprocessor); }
                else if (_openElectrolyser != null) { var mp = MachineUIs.ElectrolyserPanel(_openElectrolyser, BuildSlot); _root.Add(mp); AppendItemPorts(mp, _openElectrolyser); }
                else if (_openHydroEngine != null) _root.Add(MachineUIs.HydrogenEnginePanel(_openHydroEngine));
                else if (_openGasTank != null) _root.Add(MachineUIs.GasTankPanel(_openGasTank));
                else if (_openWaterPump != null) _root.Add(VoxelEngine.UI.FluidPumpUI.BuildPanel(_openWaterPump));
                else if (_openWindTurbine != null) _root.Add(VoxelEngine.Power.Wind.WindTurbineUI.BuildPanel(_openWindTurbine, inventory));
                else if (_openStorageTerminal  != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildTerminalPanel(_openStorageTerminal, BuildSlot, inventory));
                else if (_openServerRack       != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildServerPanel(_openServerRack, BuildSlot));
                else if (_openPatternTerminal  != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildPatternTerminalPanel(_openPatternTerminal, recipeRegistry, inventory));
                else if (_openCraftTerminal    != null) _root.Add(VoxelEngine.Storage.StorageUI.CreateCraftingTerminalPanel(_openCraftTerminal, inventory));
                else if (_openImporter         != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildImporterPanel(_openImporter, BuildSlot));
                else if (_openExporter         != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildExporterPanel(_openExporter, BuildSlot));
                else if (_openDiskManipulator  != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildDiskManipulatorPanel(_openDiskManipulator, BuildSlot));
                else if (_openNAS              != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildNASPanel(_openNAS, BuildSlot));
                else if (_openPowerstation     != null) _root.Add(BuildPowerstationPanel(_openPowerstation));
                else if (_openStorageDrawer   != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildDrawerPanel(_openStorageDrawer, BuildSlot));
                else if (_openDrawerController!= null) { var mp = VoxelEngine.Storage.StorageUI.BuildDrawerControllerPanel(_openDrawerController); _root.Add(mp); AppendItemPorts(mp, _openDrawerController); }
                else if (_openItemDisplay     != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildItemDisplayPanel(_openItemDisplay, BuildSlot));
                else if (_openGridBlock        != null) { var mp = VoxelEngine.GridSystem.UI.GridBlockUI.BuildPanel(_openGridBlock, BuildSlot); _root.Add(mp); if (_openGridBlock is VoxelEngine.Transport.IItemPortHost) AppendItemPorts(mp, _openGridBlock); }
                else if (_openOilRefinery      != null) { var mp = VoxelEngine.Crafting.ProcessorUI.OilRefineryPanel(_openOilRefinery, BuildSlot); _root.Add(mp); AppendItemPorts(mp, _openOilRefinery); }
                else if (_openChemPlant        != null) { var mp = VoxelEngine.Crafting.ProcessorUI.ChemicalPlantPanel(_openChemPlant, BuildSlot); _root.Add(mp); AppendItemPorts(mp, _openChemPlant); }
                else if (_openCrusher          != null) { var mp = MachineUIs.CrusherPanel(_openCrusher, BuildSlot); _root.Add(mp); AppendItemPorts(mp, _openCrusher); }
                else if (_openAssembler        != null) { var mp = MachineUIs.AssemblerPanel(_openAssembler, BuildSlot); _root.Add(mp); AppendItemPorts(mp, _openAssembler); }
                else if (_openFunnel           != null) _root.Add(MachineUIs.FunnelPanel(_openFunnel));
                else if (_openSplitter         != null) _root.Add(MachineUIs.SplitterPanel(_openSplitter, BuildSlot));
                else if (_openVoltageStation   != null) _root.Add(VoxelEngine.Simulation.VoltageStationUI.BuildPanel(_openVoltageStation));
                else if (_openStation  != null) BuildRightStationCrafting(_root, _openStation);
            }
            else
            {
                _root.pickingMode = PickingMode.Ignore;
                _root.style.backgroundColor = new StyleColor(new Color(0,0,0,0));
            }
        }


        /// <summary>Creates a sort button + slot grid for any ItemContainer.</summary>
        private VisualElement BuildSortableSlotGrid(IItemContainer container, int startIdx = 0, int endIdx = -1)
        {
            var wrapper = new VisualElement();

            // Sort button row
            var sortRow = new VisualElement();
            sortRow.style.flexDirection = FlexDirection.Row;
            sortRow.style.justifyContent = Justify.FlexEnd;
            sortRow.style.marginBottom = 4;

            if (container is ItemContainer ic)
            {
                var sortBtn = new Button(() => { if (startIdx > 0) ic.SortRange(startIdx, endIdx < 0 ? ic.Size : endIdx); else ic.Sort(); Refresh(); }) { text = "⇅ Sort" };
                sortBtn.style.minHeight = 22; sortBtn.style.minWidth = 60;
                sortBtn.style.fontSize = 10;
                sortBtn.style.color = Color.white;
                sortBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                sortBtn.style.backgroundColor = new StyleColor(new Color(0.15f, 0.40f, 0.65f));
                SetBorderRadius(sortBtn, 4);
                ZeroBorder(sortBtn);
                sortRow.Add(sortBtn);
            }
            wrapper.Add(sortRow);

            // Slot grid
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;

            int end = endIdx < 0 ? container.Slots.Count : endIdx;
            for (int i = startIdx; i < end; i++)
                grid.Add(BuildSlot(container, i, container.GetSlot(i), false));
            wrapper.Add(grid);

            return wrapper;
        }

        private VisualElement BuildInventoryWeightReadout()
        {
            var box = new VisualElement();
            box.style.marginTop = 2;
            box.style.marginBottom = 8;
            box.style.paddingTop = 7;
            box.style.paddingBottom = 7;
            box.style.paddingLeft = 9;
            box.style.paddingRight = 9;
            box.style.backgroundColor = new StyleColor(new Color(0.08f, 0.10f, 0.14f, 0.82f));
            SetBorderRadius(box, 5);

            float current = inventory != null ? inventory.CurrentWeightKg : 0f;
            float max = inventory != null ? inventory.MaxWeightKg : VoxelEngine.Menu.WorldSession.DefaultPlayerInventoryWeightKg;
            float fill = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            Color accent = fill >= 0.95f ? UITheme.AccentRed : fill >= 0.80f ? UITheme.AccentAmber : UITheme.AccentCyan;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            var label = new Label("MATTER WEIGHT");
            label.style.flexGrow = 1;
            label.style.fontSize = 9;
            label.style.letterSpacing = 1f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(UITheme.TextMuted);
            row.Add(label);
            var value = new Label($"{MassFormat.Format(current)} / {MassFormat.Format(max)}");
            value.style.fontSize = 10;
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            value.style.color = new StyleColor(accent);
            row.Add(value);
            box.Add(row);

            var bar = new VisualElement();
            bar.style.height = 5;
            bar.style.marginTop = 5;
            bar.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.35f));
            SetBorderRadius(bar, 3);
            var fillBar = new VisualElement();
            fillBar.style.height = 5;
            fillBar.style.width = new StyleLength(new Length(fill * 100f, LengthUnit.Percent));
            fillBar.style.backgroundColor = new StyleColor(accent);
            SetBorderRadius(fillBar, 3);
            bar.Add(fillBar);
            box.Add(bar);
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
            bar.style.bottom = 12;
            bar.style.left = 0; bar.style.right = 0;
            bar.style.height = 64;
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.justifyContent = Justify.Center;
            bar.style.alignItems = Align.Center;
            bar.pickingMode = _inventoryOpen ? PickingMode.Position : PickingMode.Ignore;
            root.Add(bar);

            for (int i = 0; i < Inventory.HOTBAR_SIZE; i++)
            {
                var slot = inventory.container.GetSlot(i);
                bar.Add(BuildSlot(inventory.container, i, slot,
                    hotbarHighlight: i == inventory.activeHotbarIndex,
                    interactive: _inventoryOpen));
            }
        }

        // ----- LEFT panel -----
        private void BuildLeftPanel(VisualElement root)
        {
            var panel = MakePanel();
            panel.style.position = Position.Absolute;
            panel.style.top = 12; panel.style.bottom = 72;
            panel.style.left = 12;
            panel.style.width = new StyleLength(new Length(32f, LengthUnit.Percent));
            panel.style.minWidth = 240;
            panel.style.maxWidth = new StyleLength(new Length(42f, LengthUnit.Percent));
            root.Add(panel);

            panel.Add(MakeTitle("Inventory"));
            panel.Add(BuildInventoryWeightReadout());

            var equipment = inventory != null ? inventory.GetComponent<VoxelEngine.Player.PlayerEquipment>() : null;
            if (equipment != null)
            {
                panel.Add(MakeSubtitle("Jetpack Slots"));
                panel.Add(BuildSortableSlotGrid(equipment.JetpackSlots, 0, VoxelEngine.Player.PlayerEquipment.JetpackSlotCount));
            }

            // Backpack grid with sort button
            panel.Add(BuildSortableSlotGrid(inventory.container, Inventory.HOTBAR_SIZE, Inventory.TOTAL_SIZE));

            // ── Wireless transmitter selector ────────────────────────
            // Drop-down appears once any transmitter is online so the player can
            // pick which network to route shift-clicks / drag-drops / crafting
            // ingredients through. Selection is remembered across sessions.
            BuildWirelessTransmitterSelector(panel);

            // ── Crafting screen toggle (crafting show / hide) ──────
            // The full crafting surface lives in its own center panel (built in
            // Refresh()). Here we only render the toggle pill; its open/closed
            // state persists across sessions via CraftingScreen.Visible.
            panel.Add(CraftingScreen.ToggleButton(Refresh));

            var statsBtn = new Button(() =>
            {
                _productionStatsOpen = !_productionStatsOpen;
                if (_productionStatsOpen) _recipeBrowserOpen = false;
                Refresh();
            })
            { text = _productionStatsOpen ? "📈 Hide Production Stats" : "📈 Production Stats" };
            statsBtn.style.minHeight = 28;
            statsBtn.style.fontSize = 11;
            statsBtn.style.color = Color.white;
            statsBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            statsBtn.style.backgroundColor = new StyleColor(_productionStatsOpen ? UITheme.AccentCyan : new Color(0.12f, 0.18f, 0.24f));
            SetBorderRadius(statsBtn, 4); ZeroBorder(statsBtn);
            panel.Add(statsBtn);

            var recipeBrowserBtn = new Button(() =>
            {
                _recipeBrowserOpen = !_recipeBrowserOpen;
                if (_recipeBrowserOpen) _productionStatsOpen = false;
                Refresh();
            })
            { text = _recipeBrowserOpen ? "Hide Recipe Browser" : "Recipe Browser" };
            recipeBrowserBtn.style.minHeight = 28;
            recipeBrowserBtn.style.fontSize = 11;
            recipeBrowserBtn.style.color = Color.white;
            recipeBrowserBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            recipeBrowserBtn.style.backgroundColor = new StyleColor(_recipeBrowserOpen ? UITheme.AccentGold : new Color(0.14f, 0.14f, 0.20f));
            SetBorderRadius(recipeBrowserBtn, 4); ZeroBorder(recipeBrowserBtn);
            panel.Add(recipeBrowserBtn);

            // ── Wireless Storage Network (if unlocked) ──
            var transmitters = VoxelEngine.Storage.WirelessTransmitter.GetAllOnline();
            if (transmitters.Length > 0)
            {
                panel.Add(Spacer(8));
                panel.Add(MakeDivider());

                var wirelessBtn = new Button(() => { _showWirelessStorage = !_showWirelessStorage; Refresh(); })
                { text = _showWirelessStorage ? "▼ Hide Storage Network" : "▶ Show Storage Network" };
                wirelessBtn.style.minHeight = 26; wirelessBtn.style.fontSize = 11;
                wirelessBtn.style.color = Color.white;
                wirelessBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                wirelessBtn.style.backgroundColor = new StyleColor(new Color(0.15f, 0.40f, 0.65f));
                SetBorderRadius(wirelessBtn, 4); ZeroBorder(wirelessBtn);
                panel.Add(wirelessBtn);

                if (_showWirelessStorage)
                {
                    foreach (var tx in transmitters)
                    {
                        if (tx.ConnectedRack == null) continue;
                        var rack = tx.ConnectedRack;
                        panel.Add(Spacer(4));

                        var netTitle = MakeSubtitle($"📡 {tx.transmitterName}");
                        panel.Add(netTitle);

                        // Categorized items.
                        var items = rack.GetAllItems();
                        if (items.Count == 0)
                        {
                            panel.Add(MakeMutedLabel("  (empty)"));
                            continue;
                        }

                        // Group by category.
                        var cats = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<VoxelEngine.Storage.StoredItemEntry>>();
                        foreach (var entry in items)
                        {
                            // Try to find the item's category.
                            string cat = "Misc";
                            var allItemDefs = Resources.FindObjectsOfTypeAll<VoxelEngine.Items.ItemDefinition>();
                            foreach (var def in allItemDefs)
                            {
                                if (def.itemId == entry.itemId)
                                { cat = string.IsNullOrEmpty(def.category) ? "Misc" : def.category; break; }
                            }
                            if (!cats.ContainsKey(cat)) cats[cat] = new();
                            cats[cat].Add(entry);
                        }

                        foreach (var kv in cats)
                        {
                            var catLabel = new Label($"  {kv.Key}");
                            catLabel.style.color = new StyleColor(new Color(0.65f, 0.70f, 0.78f));
                            catLabel.style.fontSize = 10;
                            catLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                            catLabel.style.marginTop = 2;
                            panel.Add(catLabel);

                            foreach (var entry in kv.Value)
                            {
                                var row = new VisualElement();
                                row.style.flexDirection = FlexDirection.Row;
                                row.style.marginLeft = 12; row.style.marginBottom = 1;

                                var n = new Label(entry.displayName);
                                n.style.color = new StyleColor(new Color(0.85f, 0.87f, 0.92f));
                                n.style.fontSize = 10; n.style.flexGrow = 1;
                                row.Add(n);

                                var cnt = new Label($"×{entry.count:N0}");
                                cnt.style.color = new StyleColor(new Color(0.30f, 0.75f, 0.90f));
                                cnt.style.fontSize = 10; cnt.style.minWidth = 50;
                                cnt.style.unityTextAlign = TextAnchor.MiddleRight;
                                row.Add(cnt);

                                panel.Add(row);
                            }
                        }
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

            parent.Add(Spacer(10));
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.alignItems     = Align.Center;
            row.style.marginBottom   = 4;
            row.style.paddingTop     = 6;
            row.style.paddingBottom  = 6;
            row.style.paddingLeft    = 10;
            row.style.paddingRight   = 10;
            row.style.backgroundColor = new StyleColor(UITheme.BgCard);
            SetBorderRadius(row, UITheme.CardRadius);
            UITheme.Border(row, 1, UITheme.BorderDim);
            parent.Add(row);

            var lbl = new Label("\ud83d\udce1 Network:");
            lbl.style.color    = new StyleColor(UITheme.TextSecondary);
            lbl.style.fontSize = 11;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.marginRight = 8;
            lbl.pickingMode = PickingMode.Ignore;
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
            dd.style.flexGrow  = 1;
            dd.style.minHeight = 26;

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
            status.style.color    = new StyleColor(rack != null && rack.IsOnline ? UITheme.AccentGreen : UITheme.AccentRed);
            status.style.fontSize = 10;
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

        // ── Progress bar / pill / divider — all routed through UITheme ───────
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

            var recipes = Crafter.AvailableRecipes(recipeRegistry, st.tier);
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
            var slot = new VisualElement();
            slot.style.width = 56; slot.style.height = 56;
            slot.style.marginRight = 4; slot.style.marginBottom = 4;
            slot.style.backgroundColor = new StyleColor(new Color(0.13f, 0.15f, 0.19f, 0.95f));
            SetBorderRadius(slot, 4);
            slot.style.borderTopWidth = slot.style.borderBottomWidth =
            slot.style.borderLeftWidth = slot.style.borderRightWidth = 2;
            var bColor = hotbarHighlight
                ? new StyleColor(new Color(0.95f, 0.85f, 0.20f))
                : new StyleColor(new Color(0.25f, 0.27f, 0.32f));
            slot.style.borderTopColor = slot.style.borderBottomColor =
            slot.style.borderLeftColor = slot.style.borderRightColor = bColor;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;

            if (!stack.IsEmpty)
            {
                // Icon
                if (stack.item.icon != null)
                {
                    var img = new Image { sprite = stack.item.icon };
                    img.style.width = 44; img.style.height = 44;
                    img.pickingMode = PickingMode.Ignore;   // children must not steal events
                    slot.Add(img);
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
                    count.style.color = Color.white;
                    count.style.fontSize = 12;
                    count.style.unityFontStyleAndWeight = FontStyle.Bold;
                    // Subtle dark backdrop so the number is readable on bright icons.
                    count.style.paddingLeft = 3; count.style.paddingRight = 3;
                    count.style.backgroundColor = new StyleColor(new Color(0,0,0,0.55f));
                    SetBorderRadius(count, 3);
                    count.pickingMode = PickingMode.Ignore;
                    slot.Add(count);
                }
                // Tool durability bar
                if (stack.item is ToolItem tool && tool.maxDurability > 0)
                {
                    float frac = stack.durability / (float)tool.maxDurability;
                    var bar = new VisualElement();
                    bar.style.position = Position.Absolute;
                    bar.style.left = 4; bar.style.right = 4; bar.style.bottom = 2;
                    bar.style.height = 3;
                    bar.style.backgroundColor = new StyleColor(new Color(0.2f,0.2f,0.2f,0.7f));
                    bar.pickingMode = PickingMode.Ignore;
                    var fill = new VisualElement();
                    fill.style.height = 3;
                    fill.style.width = new StyleLength(new Length(Mathf.Clamp01(frac) * 100, LengthUnit.Percent));
                    fill.style.backgroundColor = new StyleColor(Color.Lerp(Color.red, Color.green, frac));
                    bar.Add(fill);
                    slot.Add(bar);
                }
                // Tooltip on hover (only when the panel is interactive — otherwise hotbar
                // slots in the corner of the screen would pop tooltips constantly).
                if (interactive) Tooltip.Bind(slot, stack);
            }

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
                    ? new ItemStack { item = stack.item, count = remaining, durability = stack.durability, payload = stack.payload }
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
                    var clone1 = new ItemStack { item = srcStack.item, count = srcStack.count, durability = srcStack.durability, payload = srcStack.payload };
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

                    var clone2 = new ItemStack { item = srcStack.item, count = srcStack.count, durability = srcStack.durability, payload = srcStack.payload };
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

            var clone = new ItemStack { item = srcStack.item, count = srcStack.count, durability = srcStack.durability, payload = srcStack.payload };
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
