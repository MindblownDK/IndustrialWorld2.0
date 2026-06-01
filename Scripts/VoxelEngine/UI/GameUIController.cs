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
        private bool _inventoryOpen;
        public bool IsInventoryOpen => _inventoryOpen;
        private IItemContainer _rightContainer; // chest contents OR furnace etc.
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
        private VoxelEngine.Storage.StorageTerminal    _openStorageTerminal;
        private VoxelEngine.Storage.ServerRack         _openServerRack;
        private VoxelEngine.Storage.PatternTerminal    _openPatternTerminal;
        private VoxelEngine.Storage.CraftingTerminal   _openCraftTerminal;
        private VoxelEngine.Storage.StorageImporter    _openImporter;
        private VoxelEngine.Storage.StorageExporter    _openExporter;
        private VoxelEngine.Storage.DiskManipulator    _openDiskManipulator;
        private VoxelEngine.Storage.NASBlock           _openNAS;
        private VoxelEngine.Storage.Powerstation       _openPowerstation;
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
            _doc = GetComponent<UIDocument>();
            if (_doc.panelSettings == null)
                _doc.panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
            _root = _doc.rootVisualElement;
            _root.style.flexGrow = 1;
            _root.pickingMode = PickingMode.Ignore;

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
#if UNITY_EDITOR
            if (recipeRegistry == null)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets("t:RecipeRegistry");
                if (guids != null && guids.Length > 0)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    recipeRegistry = UnityEditor.AssetDatabase.LoadAssetAtPath<Crafting.RecipeRegistry>(path);
                    Debug.Log("[GameUI] Auto-found RecipeRegistry at " + path);
                }
            }
#endif
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
        private void Update()
        {
            // Live-update the open furnace panel in-place every frame (no rebuild needed).
            TickFurnaceLiveUI();
            PlayerHud.Tick();
            RustStyleHud.Tick();
            BuildFeedbackHud.Tick();
            VoxelEngine.Weather.WeatherHud.Tick();
            VoxelEngine.GridSystem.GridPilotHud.Tick();
            GrinderHud.Tick();
            BuildCostHud.Tick();
            if (_openQuarry != null) QuarryHud.Tick(_openQuarry);
            // Periodic refresh for machine panels that need live updates (1 Hz).
            _machineRefreshAccum += Time.unscaledDeltaTime;
            if (_machineRefreshAccum >= 1.0f && (_openCoalGen != null || _openReactor != null || _openTurbine != null || _openPortReactor != null || _openProcessor != null || _openReprocessor != null || _openElectrolyser != null || _openHydroEngine != null || _openGasTank != null))
            { _machineRefreshAccum = 0f; Refresh(); }
            ResearchHud.Tick();
            TickUpgradePrompt();
            if (inventory != null) Minimap.Tick(inventory.transform.position);

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
            // Hotbar wheel — only when no UI is open and Ctrl is NOT held (Ctrl+wheel rotates build ghost).
            bool ctrl = false;
#if ENABLE_INPUT_SYSTEM
            ctrl = UnityEngine.InputSystem.Keyboard.current != null
                   && UnityEngine.InputSystem.Keyboard.current.leftCtrlKey.isPressed;
            float wheel = UnityEngine.InputSystem.Mouse.current != null
                ? UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y : 0f;
#else
            ctrl = Input.GetKey(KeyCode.LeftControl);
            float wheel = Input.mouseScrollDelta.y;
#endif
            // Throttle: at most one slot change per Update, regardless of scroll-unit magnitude.
            if (!ctrl && !_inventoryOpen && _rightContainer == null && inventory != null && Mathf.Abs(wheel) > 0.01f)
            {
                int dir = wheel > 0 ? -1 : 1; // wheel up = previous slot, wheel down = next
                int next = inventory.activeHotbarIndex + dir;
                if (next < 0) next = Inventory.HOTBAR_SIZE - 1;
                else if (next >= Inventory.HOTBAR_SIZE) next = 0;
                inventory.SetActiveHotbar(next);
            }

            // While the search field has keyboard focus, don't react to hotkey-style keys
            // — the player is typing into the search box.
            bool typing = _searchHasFocus;

            // Toggle inventory — but NOT while pause menu (or any other UI we don't own) is up,
            // and NOT while typing in the search field.
            bool weAreOpen = _inventoryOpen || _rightContainer != null;
            if (!typing && GameSettings.WasPressed(InputAction.Inventory))
            {
                if (weAreOpen)
                {
                    // Only do a plain close — pressing I on a machine panel just closes it,
                    // it does NOT re-open the plain inventory on the same frame.
                    CloseAll();
                    UIState.PauseConsumedFrame = Time.frameCount;
                    _justClosedThisFrame = true;
                }
                else if (!UIState.IsBlocking && !_justClosedThisFrame)
                {
                    OpenInventory();
                }
            }
            // Reset per-frame close guard each frame.
            else
            {
                _justClosedThisFrame = false;
            }
            // Esc closes our panels — and tells the pause menu we already handled Esc this frame.
            if (!typing && GameSettings.WasPressed(InputAction.Pause) && weAreOpen)
            {
                CloseAll();
                UIState.PauseConsumedFrame = Time.frameCount;
            }

            // Tick custom tooltip overlay
            #if ENABLE_INPUT_SYSTEM
            Vector2 mp = UnityEngine.InputSystem.Mouse.current != null
                ? UnityEngine.InputSystem.Mouse.current.position.ReadValue() : Vector2.zero;
            #else
            Vector2 mp = Input.mousePosition;
            #endif
            if (_inventoryOpen) Tooltip.Tick(mp, Screen.height, ProbeStackAt);
            else                Tooltip.Hide();

            // Drag follow — convert screen-pixel cursor to panel coordinates.
            if (_dragSource.active && _dragGhost != null)
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
            _inventoryOpen  = true;
            _openFurnace    = null;
            _openElectric   = null;
            _openCoalGen    = null;
            _openQuarry     = null;
            _openReactor    = null; _openTurbine     = null;
            _openPortReactor= null; _openProcessor   = null;
            _openReprocessor= null; _openElectrolyser= null;
            _openHydroEngine= null; _openGasTank     = null;
            _rightContainer = null;
            _openStation    = null;
            _activeQueue    = null;
            UnlockCursor();
            Refresh();
        }
        public void OpenContainer(IItemContainer c)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            _rightContainer = c;
            _inventoryOpen  = true;
            _openFurnace    = null;
            _openElectric   = null;
            _openCoalGen    = null;
            _openQuarry     = null;
            _openReactor    = null; _openTurbine     = null;
            _openPortReactor= null; _openProcessor   = null;
            _openReprocessor= null; _openElectrolyser= null;
            _openHydroEngine= null; _openGasTank     = null;
            _openStation    = null;
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
            _openHydroEngine= null; _openGasTank     = null;
            _rightContainer = null;
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
            _openHydroEngine= null; _openGasTank     = null;
            _rightContainer = null;
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
            _openHydroEngine= null; _openGasTank     = null;
            _rightContainer = null; _openStation = null;
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
            _rightContainer = null; _openStation = null;
            _inventoryOpen  = true;
            UnwatchAllContainers();
            if (quarry != null) { quarry.EnsureOutputPublic(); WatchContainer(quarry.Output); }
            UnlockCursor();
            Refresh();
        }

        /// <summary>Generic opener for all new machine types.</summary>
        public void OpenMachine(MonoBehaviour machine)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            _openFurnace = null; _openElectric = null; _openCoalGen = null;
            _rightContainer = null; _openStation = null; _openQuarry = null;
            _openReactor = null; _openTurbine = null; _openPortReactor = null;
            _openProcessor = null; _openReprocessor = null; _openElectrolyser = null;
            _openHydroEngine = null; _openGasTank = null;
            _openStorageTerminal = null; _openServerRack = null;
            _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter = null; _openExporter = null;
            _openDiskManipulator = null; _openNAS = null; _openPowerstation = null;
            _inventoryOpen = true;
            UnwatchAllContainers();
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
                case VoxelEngine.Storage.ServerRack sr:
                    _openServerRack = sr; sr.EnsureContainers();
                    WatchContainer(sr.diskSlots); WatchContainer(sr.ramSlots);
                    WatchContainer(sr.cpuSlot); WatchContainer(sr.psuSlot); break;
            }
            UnlockCursor();
            Refresh();
        }

        public void OpenStation(CraftingStation st)
        {
            if (!_inventoryOpen) UIState.PushBlock();
            _openStation    = st;
            _rightContainer = null;
            _openFurnace    = null;
            _openElectric   = null;
            _openCoalGen    = null;
            _openQuarry     = null;
            _openReactor    = null; _openTurbine     = null;
            _openPortReactor= null; _openProcessor   = null;
            _openReprocessor= null; _openElectrolyser= null;
            _openHydroEngine= null; _openGasTank     = null;
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
            if (_inventoryOpen) UIState.PopBlock();
            _inventoryOpen  = false;
            _rightContainer = null;
            _openFurnace    = null;
            _openElectric   = null;
            _openCoalGen    = null;
            _openStation    = null;
            _openQuarry     = null;
            _openReactor    = null; _openTurbine      = null;
            _openPortReactor= null; _openProcessor    = null;
            _openReprocessor= null; _openElectrolyser = null;
            _openHydroEngine= null; _openGasTank      = null;
            _openPatternTerminal = null; _openCraftTerminal = null;
            _openImporter   = null; _openExporter     = null;
            _openDiskManipulator = null; _openNAS     = null;
            _openPowerstation= null;
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

        private void Refresh()
        {
            if (_searchHasFocus) return;

            // Clear stale references — the elements they point to are about to be destroyed.
            _liveFlame = null; _liveSmeltFill = null; _liveFuelFill = null;
            _liveSmeltLabel = null; _liveFuelStat = null;
            _liveStatusPill = null; _liveStatusLabel = null; _liveWattLabel = null;

            _root.Clear();
            if (inventory == null) return;

            // (Re)mount the tooltip overlay; it lives at the root and is invisible until hovered.
            Tooltip.EnsureMounted(_root);
            PlayerHud.EnsureMounted(_root);
            ResearchHud.EnsureMounted(_root);
            UpgradePromptHud.EnsureMounted(_root);
            Minimap.EnsureMounted(_root);
            RustStyleHud.EnsureMounted(_root);
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

                // Left panel — player inventory + crafting list
                BuildLeftPanel(_root);

                // Right panel — container or station
                if (_rightContainer != null) BuildRightContainer(_root, _rightContainer);
                else if (_openFurnace  != null) BuildRightFurnace(_root, _openFurnace);
                else if (_openElectric != null) BuildRightElectricFurnace(_root, _openElectric);
                else if (_openCoalGen  != null) BuildRightCoalGenerator(_root, _openCoalGen);
                else if (_openQuarry   != null) _root.Add(MachineUIs.QuarryPanel(_openQuarry, BuildSlot));
                else if (_openReactor  != null) _root.Add(MachineUIs.ReactorCorePanel(_openReactor, BuildSlot));
                else if (_openTurbine  != null) _root.Add(MachineUIs.SteamTurbinePanel(_openTurbine));
                else if (_openPortReactor != null) _root.Add(MachineUIs.PortableReactorPanel(_openPortReactor, BuildSlot));
                else if (_openProcessor != null) _root.Add(MachineUIs.UraniumProcessorPanel(_openProcessor, BuildSlot));
                else if (_openReprocessor != null) _root.Add(MachineUIs.WasteReprocessorPanel(_openReprocessor, BuildSlot));
                else if (_openElectrolyser != null) _root.Add(MachineUIs.ElectrolyserPanel(_openElectrolyser, BuildSlot));
                else if (_openHydroEngine != null) _root.Add(MachineUIs.HydrogenEnginePanel(_openHydroEngine));
                else if (_openGasTank != null) _root.Add(MachineUIs.GasTankPanel(_openGasTank));
                else if (_openStorageTerminal  != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildTerminalPanel(_openStorageTerminal, BuildSlot, inventory));
                else if (_openServerRack       != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildServerPanel(_openServerRack, BuildSlot));
                else if (_openPatternTerminal  != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildPatternTerminalPanel(_openPatternTerminal, recipeRegistry, inventory));
                else if (_openCraftTerminal    != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildCraftingTerminalPanel(_openCraftTerminal, inventory));
                else if (_openImporter         != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildImporterPanel(_openImporter, BuildSlot));
                else if (_openExporter         != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildExporterPanel(_openExporter, BuildSlot));
                else if (_openDiskManipulator  != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildDiskManipulatorPanel(_openDiskManipulator, BuildSlot));
                else if (_openNAS              != null) _root.Add(VoxelEngine.Storage.StorageUI.BuildNASPanel(_openNAS, BuildSlot));
                else if (_openPowerstation     != null) _root.Add(BuildPowerstationPanel(_openPowerstation));
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

        // ----- HOTBAR -----
        private void BuildHotbar(VisualElement root)
        {
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
            panel.style.top = 32; panel.style.bottom = 96;
            panel.style.left = 32; panel.style.width = 460;
            root.Add(panel);

            panel.Add(MakeTitle("Inventory"));

            // Backpack grid with sort button
            panel.Add(BuildSortableSlotGrid(inventory.container, Inventory.HOTBAR_SIZE, Inventory.TOTAL_SIZE));

            // Crafting list — filtered by category + search.
            panel.Add(Spacer(12));
            panel.Add(MakeSubtitle("Crafting"));
            var maxStation = Crafter.MaxAccessibleStation(inventory.transform.position, stationRadius);
            var allRecipes = Crafter.AvailableRecipes(recipeRegistry, maxStation);

            // If a wireless transmitter is online, use the combined network+inventory as
            // the crafting source so the player can craft using stored items.
            IItemContainer craftSource = inventory.container;
            var transmittersForCraft = VoxelEngine.Storage.WirelessTransmitter.GetAllOnline();
            if (transmittersForCraft.Length > 0 && transmittersForCraft[0].ConnectedRack != null)
                craftSource = new VoxelEngine.Storage.NetworkItemSource(inventory.container, transmittersForCraft[0].ConnectedRack);

            BuildRecipeBrowser(panel, allRecipes, craftSource, inventory.container,
                emptyMessage: "No recipes available — craft a Crafting Bench first.", panelId: "inventory");

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
        // Reusable recipe browser: search bar + category tabs + recipe list.
        // Used by the player inventory pane AND the workstation right pane.
        // ----------------------------------------------------------------
        // True while a TextField inside the inventory has keyboard focus.
        // Set by the search field. Read by Update() to suppress hotkey/closing handling.
        private bool _searchHasFocus;
        private bool _showWirelessStorage;
        // Prevents I from re-opening inventory the same frame it closed a machine panel.
        private bool _justClosedThisFrame;

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
            panel.style.top = 32; panel.style.bottom = 96;
            panel.style.right = 32; panel.style.width = 460;
            root.Add(panel);

            panel.Add(MakeTitle(c.Name));
            panel.Add(BuildSortableSlotGrid(c));
        }

        // ----- RIGHT (furnace) -----
        // Cached one-shot pulse value for animated flame icon (driven by Time.unscaledTime).
        private static float FlamePulse() => 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 6f);

        private void BuildRightFurnace(VisualElement root, Furnace f)
        {
            f.EnsureContainers();
            var panel = MakePanel();
            panel.style.position = Position.Absolute;
            panel.style.top = 32; panel.style.bottom = 96;
            panel.style.right = 32; panel.style.width = 480;
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
        private void BuildRightCoalGenerator(VisualElement root, VoxelEngine.Power.CoalGeneratorFuel f)
        {
            f.EnsureContainers();
            var panel = MakePanel();
            panel.style.position = Position.Absolute;
            panel.style.top = 32; panel.style.bottom = 96;
            panel.style.right = 32; panel.style.width = 460;
            root.Add(panel);

            var headerRow = new VisualElement(); headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 14;
            var t = MakeTitle("Coal Generator"); t.style.flexGrow = 1;
            headerRow.Add(t);
            var (pill, pillLbl) = MakeStatusPillWithLabel(
                f.IsBurning ? "RUNNING" : "OFFLINE",
                f.IsBurning ? new Color(0.95f, 0.50f, 0.15f) : new Color(0.30f, 0.30f, 0.35f));
            headerRow.Add(pill);
            panel.Add(headerRow);

            panel.Add(MakeSubtitle("Fuel"));
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center; row.style.marginTop = 8;
            row.Add(MakeLabeledSlot("Fuel", f.fuelC, 0));
            var barHolder = new VisualElement();
            barHolder.style.flexGrow = 1; barHolder.style.marginLeft = 12;
            var (bar, fill) = MakeProgressBarWithFill(f.FuelProgress01, new Color(0.95f, 0.55f, 0.10f), 0, 12, fillFlexGrow: true);
            barHolder.Add(bar);
            row.Add(barHolder);
            panel.Add(row);

            panel.Add(Spacer(8));
            var status = new Label($"Fuel left: {f.fuelRemaining:0.0}s / {f.fuelMaxDuration:0.0}s");
            status.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.90f));
            status.style.fontSize = 11;
            panel.Add(status);

            panel.Add(MakeDivider());
            // Port configuration
            var portConfig = f.GetComponent<VoxelEngine.Transport.PortConfig>();
            if (portConfig != null)
            {
                panel.Add(MakeDivider());
                panel.Add(PortConfigHud.Build(portConfig, Refresh));
            }
            var hint = new Label("Tip: place Coal in the fuel slot to start producing power. " +
                                 "Wood logs and planks also work but burn faster.");
            hint.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.65f));
            hint.style.fontSize = 11;
            hint.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(hint);
        }

        // ----- RIGHT (electric furnace) -----
        private void BuildRightElectricFurnace(VisualElement root, ElectricFurnace ef)
        {
            ef.EnsureContainers();
            var panel = MakePanel();
            panel.style.position = Position.Absolute;
            panel.style.top = 32; panel.style.bottom = 96;
            panel.style.right = 32; panel.style.width = 480;
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
            // Port configuration
            var portConfig = ef.GetComponent<VoxelEngine.Transport.PortConfig>();
            if (portConfig != null)
            {
                panel.Add(MakeDivider());
                panel.Add(PortConfigHud.Build(portConfig, Refresh));
            }
            var hint = new Label("Tip: connect cables from a generator. Insert Speed/Efficiency modules to tune output vs power use.");
            hint.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.65f));
            hint.style.fontSize = 10;
            hint.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(hint);
        }

        // ----- RIGHT (crafting bench / assembler) -----
        private void BuildRightStationCrafting(VisualElement root, CraftingStation st)
        {
            var panel = MakePanel();
            panel.style.position = Position.Absolute;
            panel.style.top = 32; panel.style.bottom = 96;
            panel.style.right = 32; panel.style.width = 460;
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
                emptyMessage: "No recipes available at this station tier.", panelId: "station_" + st.GetEntityId());
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

            // Stack merge.
            if (!srcStack.IsEmpty && !dstStack.IsEmpty &&
                dstStack.item == srcStack.item && srcStack.item.IsStackable)
            {
                int space = srcStack.item.maxStack - dstStack.count;
                int move  = Mathf.Min(space, srcStack.count);
                dstStack.count += move;
                srcStack.count -= move;
                destC.SetSlot(destIdx, dstStack);
                srcC.SetSlot(srcIdx, srcStack.count > 0 ? srcStack : new ItemStack());
            }
            else
            {
                // Plain swap.
                destC.SetSlot(destIdx, srcStack);
                srcC.SetSlot(srcIdx, dstStack);
            }
            CancelDrag();
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

        /// <summary>Drop the item from the given container slot into the world.</summary>
        public void DropItemFromSlot(IItemContainer c, int idx)
        {
            if (c == null) return;
            var stack = c.GetSlot(idx);
            if (stack.IsEmpty) return;
            // Spawn in front of the player.
            Vector3 spawnPos = Vector3.zero;
            Vector3 tossDir = Vector3.forward;
            if (inventory != null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    spawnPos = cam.transform.position + cam.transform.forward * 1.5f;
                    tossDir = cam.transform.forward;
                }
                else
                {
                    spawnPos = inventory.transform.position + Vector3.up * 1.5f + inventory.transform.forward * 1.5f;
                    tossDir = inventory.transform.forward;
                }
            }
            VoxelEngine.Items.DroppedItem.Spawn(stack, spawnPos, tossDir);
            c.SetSlot(idx, new ItemStack());
            VoxelEngine.UI.BuildFeedbackHud.Show(
                $"Dropped {stack.item.displayName}",
                $"-{stack.count}",
                stack.item.icon,
                new Color(0.85f, 0.35f, 0.25f));
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
        private void UpdateDragDrop()
        {
            if (!_inventoryOpen) return;

            // --- Read mouse state directly from the device ---
#if ENABLE_INPUT_SYSTEM
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
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(_root.panel,
                new Vector2(screenPos.x, Screen.height - screenPos.y));

            var slotRef = FindSlotAt(panelPos);

            if (lmbPressed)
            {
                if (slotRef == null)
                {
                    if (_dragSource.active)
                    {
                        // Dragging to empty area ALWAYS drops the item to the world.
                        // Use Shift+Click on an inventory slot to store into the network.
                        DropItemFromSlot(_dragSource.container, _dragSource.slotIndex);
                        CancelDrag();
                    }
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
        // your active hotbar slot" pattern from Minecraft / Terraria / Satisfactory.

        /// <summary>Shift-click: send the whole stack at (sourceC, sourceIdx) to the "other side".
        /// Smart routing: from player inventory, fuel items go to fuel slot, anything else goes to input.
        /// From any furnace slot back to player.</summary>
        private void QuickTransfer(IItemContainer sourceC, int sourceIdx)
        {
            if (inventory == null) return;
            var srcStack = sourceC.GetSlot(sourceIdx);
            if (srcStack.IsEmpty) return;

            // Storage terminal open: shift-click from inventory → insert into network.
            if (sourceC == inventory.container && _openStorageTerminal != null && _openStorageTerminal.ConnectedRack != null)
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

            // Wireless transmitter active + plain inventory open: shift-click from inventory → insert into network.
            if (sourceC == inventory.container && _openStorageTerminal == null)
            {
                var transmitters = VoxelEngine.Storage.WirelessTransmitter.GetAllOnline();
                if (transmitters.Length > 0 && transmitters[0].ConnectedRack != null)
                {
                    var rack = transmitters[0].ConnectedRack;
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

            IItemContainer dest = ResolveQuickTransferDestination(sourceC, srcStack.item);
            if (dest == null) return;

            var clone = new ItemStack { item = srcStack.item, count = srcStack.count, durability = srcStack.durability };
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
                if (_openQuarry != null)         return _openQuarry.Output;
                if (_openDiskManipulator != null) return _openDiskManipulator.sourceSlot;
                if (_openNAS != null)             return _openNAS.diskSlots;
                if (_openImporter != null)        return _openImporter.upgradeSlots;
                if (_openExporter != null)        return _openExporter.upgradeSlots;
                if (_openPowerstation != null)    return _openPowerstation.psuSlots;
                if (_openServerRack != null)      return _openServerRack.diskSlots;
                return null;
            }
            // Source is ANY non-player container: send to the player inventory.
            return inventory.container;
        }
        private void SwapHoveredWithHotbar(int hotbarIdx)
        {
            if (!_inventoryOpen) return;
            if (inventory == null || inventory.container == null) return;
            if (hotbarIdx < 0 || hotbarIdx >= Inventory.HOTBAR_SIZE) return;

#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;
            Vector2 screenPos = mouse.position.ReadValue();
#else
            Vector2 screenPos = Input.mousePosition;
#endif
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
            p.style.position = Position.Absolute;
            p.style.top = 28; p.style.bottom = 100;
            p.style.right = 28; p.style.width = 484;

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
