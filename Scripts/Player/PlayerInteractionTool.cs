// Assets/Scripts/VoxelEngine/Player/PlayerInteractionTool.cs
//
// Replaces the old PickaxeTool. Routes LMB / RMB based on what's in the player's
// active hotbar slot AND what their crosshair is on.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Building.Tiered;
using VoxelEngine.Core;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Modification;
using VoxelEngine.Networks;
using VoxelEngine.Settings;
using VoxelEngine.Trees;
using InputAction = VoxelEngine.Settings.InputAction;
using Tree        = VoxelEngine.Trees.Tree;

namespace VoxelEngine.Player
{
    public class PlayerInteractionTool : MonoBehaviour
    {
        [Header("Refs")]
        public Camera shootCamera;
        public VoxelWorld world;
        public MaterialRegistry registry;
        public Inventory inventory;

        [Header("Tuning")]
        public float reach = 8f;
        [Tooltip("Bare-hand mining stats — used when no pickaxe is held.")]
        public float handStrength = 25f;
        public float handBrushRadius = 1.0f;
        public int   handTier = 0;
        public float handFireRate = 2.5f;

        private float _nextHit;
        private ToolFeedback _feedback;

        // Lazy-init wrench runtime — only created the first time the player swings a wrench.
        private WrenchInteraction _wrench;

        private void Awake()
        {
            if (world      == null) world      = VoxelWorld.Instance;
            if (shootCamera== null) shootCamera= Camera.main;
            if (inventory  == null) inventory  = GetComponentInParent<Inventory>();
            if (registry   == null) registry   = Object.FindAnyObjectByType<MaterialRegistry>();
            if (shootCamera != null)
            {
                _feedback = shootCamera.GetComponent<ToolFeedback>();
                if (_feedback == null) _feedback = shootCamera.gameObject.AddComponent<ToolFeedback>();
            // Grid builder for ship/vehicle construction.
            if (GetComponent<VoxelEngine.GridSystem.GridBuilder>() == null)
            {
                var gb = gameObject.AddComponent<VoxelEngine.GridSystem.GridBuilder>();
                gb.buildCamera = shootCamera;
                gb.inventory = inventory;
            }
            }
        }

        private void Update()
        {
            if (VoxelEngine.UI.UIState.IsBlocking) return;   // suppress mining/build while menus open
            // While piloting a ship, the cockpit owns left-click (drill/weapon) — don't
            // let the on-foot tool mine/break the world.
            if (VoxelEngine.GridSystem.GridCockpit.ActivePilotSeat != null) return;
            IsGrinding = false; // reset each frame — HandleGrind sets it true when active
            if (world      == null) world      = VoxelWorld.Instance;
            if (inventory  == null) inventory  = GetComponentInParent<Inventory>();
            if (registry   == null) registry   = Object.FindAnyObjectByType<MaterialRegistry>();
            if (world == null || shootCamera == null || inventory == null) return;

            bool mineHeld  = GameSettings.IsHeld (InputAction.Mine);
            bool mineDown  = GameSettings.WasPressed(InputAction.Mine);
            bool buildHeld = GameSettings.IsHeld (InputAction.Build);
            bool buildDown = GameSettings.WasPressed(InputAction.Build);

            // Wrench owns its own per-frame tick (selection timeout + indicator follow)
            // so call it BEFORE the early-out — otherwise a player holding the wrench
            // but not pressing any button never sees their selection time-out.
            if (_wrench != null) _wrench.Tick();

            var ray = shootCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            bool hasHit = Physics.Raycast(ray, out var hit, reach);

            // ── INTERACTION HUD (Context Prompts) ──
            if (hasHit && !VoxelEngine.UI.UIState.IsBlocking)
            {
                var cockpit = hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridCockpit>();
                if (cockpit != null && cockpit.Pilot == null)
                {
                    string key = GameSettings.GetKey(InputAction.EnterCockpit);
                    VoxelEngine.UI.InteractionHud.Show(key, "Enter Cockpit");

                    if (GameSettings.WasPressed(InputAction.EnterCockpit))
                    {
                        var pc = GetComponentInParent<VoxelEngine.Player.PlayerController>();
                        if (pc != null) cockpit.Enter(pc);
                    }
                    // Swallow input if we interacted? No, cockpits usually exclusive.
                    // But we want to hide it if we are looking at something else.
                }
                else
                {
                    VoxelEngine.UI.InteractionHud.Hide();
                }
            }
            else
            {
                VoxelEngine.UI.InteractionHud.Hide();
            }

            if (!mineHeld && !buildHeld && !buildDown) return;

            if (!hasHit) return;

            // ── WRENCH dispatch — short-circuits all other tool behaviour. ──
            //   LMB = connect/select  •  RMB = disconnect  •  Shift modifies both
            var heldStack = inventory.ActiveStack;
            if (!heldStack.IsEmpty && heldStack.item is WrenchTool)
            {
                if (_wrench == null) _wrench = new WrenchInteraction();
                if (mineDown)  { _wrench.OnUse(hit, this);    _nextHit = Time.time + 0.15f; return; }
                if (buildDown) { _wrench.OnAltUse(hit);        _nextHit = Time.time + 0.15f; return; }
                // Holding either button without a fresh press: swallow input so the
                // wrench never accidentally mines a block or places a phantom item.
                if (mineHeld || buildHeld) return;
            }

            // ---------- LMB ----------
            if (mineHeld)
            {
                if (Time.time < _nextHit) return;

                // 1) Tree?
                var tree = hit.collider.GetComponentInParent<Tree>();
                if (tree != null) { HitTree(tree); return; }

                // Grid block grinding.
                var gridBlock = hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
                if (gridBlock != null)
                {
                    var grindStack = inventory.ActiveStack;
                    if (!grindStack.IsEmpty && grindStack.item is VoxelEngine.GridSystem.GrinderTool grinder)
                    {
                        HandleGrind(gridBlock, grinder, hit);
                        return;
                    }
                }

                // Wild crop?
                var wildCrop = hit.collider.GetComponentInParent<VoxelEngine.Farming.WildCrop>();
                if (wildCrop != null)
                {
                    int dmg = (int)handStrength;
                    float rate = handFireRate;
                    var wStack = inventory.ActiveStack;
                    if (!wStack.IsEmpty && wStack.item is ToolItem wt) { dmg = (int)wt.strength; rate = wt.fireRate; }
                    wildCrop.Hit(dmg, inventory);
                    _nextHit = Time.time + 1f / Mathf.Max(0.1f, rate);
                    return;
                }

                // 2a) Placed TIERED block?
                var tiered = hit.collider.GetComponentInParent<PlacedTieredBlock>();
                if (tiered != null) { HitTieredBlock(tiered, hit); return; }

                // 2b) Placed legacy block?
                var placed = hit.collider.GetComponentInParent<PlacedBlock>();
                if (placed != null) { BreakPlaced(placed, hit); return; }

                // 3) Voxel terrain.
                MineVoxel(ray, hit);
                return;
            }

            // ---------- RMB ----------
            // If holding a placeable block (cable, pipe, etc), RMB places it directly.
            var heldForPlace = inventory.ActiveStack;
            if (buildDown && !heldForPlace.IsEmpty && heldForPlace.item is BlockItem heldBlock)
            {
                if (Time.time >= _nextHit && TryPlaceStaticPipeOnGrid(heldBlock, hit))
                {
                    int taken = inventory.container.Remove(heldBlock, 1);
                    if (taken == 0) TryNetworkConsume(heldBlock, 1);
                    VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(heldBlock.displayName, heldBlock, 1);
                    _nextHit = Time.time + 0.2f;
                }
                else if (Time.time >= _nextHit && BuildSystem.Instance != null && BuildSystem.Instance.TryPlace(heldBlock, hit, ray.direction))
                {
                    // Consume from inventory first; if empty AND the player has
                    // researched "Wireless Build Sync" with an active transmitter,
                    // pull the replacement from the storage network.
                    int taken = inventory.container.Remove(heldBlock, 1);
                    if (taken == 0) TryNetworkConsume(heldBlock, 1);
                    VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(heldBlock.displayName, heldBlock, 1);
                    _nextHit = Time.time + 0.2f;
                }
                return; // DONE — don't open any machine UI
            }

            if (buildDown)
            {
                // 0) Water Bucket placement.
                var stackRmb = inventory.ActiveStack;
                if (!stackRmb.IsEmpty && stackRmb.item is WaterBucket && stackRmb.durability > 0)
                {
                    // Place the carried liquid into the fluid sim (the voxel cell stays AIR;
                    // the liquid mesh renders it). Buckets can now carry water or crude oil.
                    var pos = world.WorldToVoxel(hit.point + hit.normal * 0.5f);
                    var existing = world.GetVoxelWorld(pos);
                    if (existing.density <= 0)
                    {
                        var carried = stackRmb.payload is VoxelEngine.Items.LiquidType lt ? lt : VoxelEngine.Items.LiquidType.Water;
                        if (carried == VoxelEngine.Items.LiquidType.CrudeOil)
                            VoxelEngine.Fluids.FluidSimManager.Instance?.PlaceOil(pos, 255);
                        else
                            VoxelEngine.Fluids.FluidSimManager.Instance?.PlaceWater(pos, 255);
                        stackRmb.durability = 0;
                        stackRmb.payload = null;
                        inventory.container.RaiseChanged();
                    }
                    return;
                }

                // Eat food if holding a FoodItem.
                var eatStack = inventory.ActiveStack;
                if (!eatStack.IsEmpty && eatStack.item is VoxelEngine.Farming.FoodItem food)
                {
                    var stats = inventory.GetComponent<VoxelEngine.Player.PlayerStats>();
                    if (stats != null)
                    {
                        stats.Feed(food.hungerRestore);
                        stats.Heal(food.healthRestore);
                        if (food.staminaRestore > 0) stats.RegenStamina(food.staminaRestore / stats.staminaRegen);
                        inventory.container.Remove(food, 1);
                        VoxelEngine.UI.BuildFeedbackHud.Show($"Ate {food.displayName}", $"+{food.hungerRestore:0} hunger", food.icon, new Color(0.85f, 0.60f, 0.15f));
                    }
                    return;
                }

                // Farm plot: plant seed or harvest.
                var farmPlot = hit.collider.GetComponentInParent<VoxelEngine.Farming.FarmPlot>();
                if (farmPlot != null)
                {
                    // Try harvest first.
                    if (farmPlot.growthProgress >= 1f) { farmPlot.TryHarvest(inventory); return; }
                    // Try plant seed.
                    var seedStack = inventory.ActiveStack;
                    if (!seedStack.IsEmpty && seedStack.item is VoxelEngine.Farming.SeedItem seed && seed.crop != null)
                    {
                        if (farmPlot.TryPlant(seed.crop))
                        {
                            inventory.container.Remove(seed, 1);
                            VoxelEngine.UI.BuildFeedbackHud.Show($"Planted {seed.crop.cropName}", "-1 seed", seed.icon, new Color(0.40f, 0.75f, 0.30f));
                        }
                    }
                    return;
                }

                // Pickup dropped items from the world.
                var droppedItem = hit.collider.GetComponentInParent<VoxelEngine.Items.DroppedItem>();
                if (droppedItem != null) { droppedItem.TryPickup(inventory); return; }

                // 1) Open container if looking at chest / furnace / crafting bench.
                var chest = hit.collider.GetComponentInParent<Chest>();
                if (chest != null) { UI.GameUIController.Instance?.OpenContainer(chest.container, chest); return; }

                var bed = hit.collider.GetComponentInParent<VoxelEngine.Building.Bed>();
                if (bed != null) { bed.ClaimAsSpawn(); return; }

                var lab = hit.collider.GetComponentInParent<VoxelEngine.Research.ResearchLab>();
                if (lab != null)
                {
                    lab.EnsureContainers();
                    UI.GameUIController.Instance?.OpenContainer(lab.scienceInput);
                    VoxelEngine.Research.ResearchUI.Instance?.Open();
                    return;
                }

                var coalGen = hit.collider.GetComponentInParent<VoxelEngine.Power.CoalGeneratorFuel>();
                if (coalGen != null) { UI.GameUIController.Instance?.OpenCoalGenerator(coalGen); return; }

                var quarry = hit.collider.GetComponentInParent<VoxelEngine.Transport.Quarry>();
                if (quarry != null) { UI.GameUIController.Instance?.OpenQuarry(quarry); return; }

                // Nuclear & Gas machines — generic opener.
                var reactor = hit.collider.GetComponentInParent<VoxelEngine.Nuclear.ReactorCore>();
                if (reactor != null) { UI.GameUIController.Instance?.OpenMachine(reactor); return; }
                var turbine = hit.collider.GetComponentInParent<VoxelEngine.Nuclear.SteamTurbine>();
                if (turbine != null) { UI.GameUIController.Instance?.OpenMachine(turbine); return; }
                var portReactor = hit.collider.GetComponentInParent<VoxelEngine.Nuclear.PortableReactor>();
                if (portReactor != null) { UI.GameUIController.Instance?.OpenMachine(portReactor); return; }
                var uProcessor = hit.collider.GetComponentInParent<VoxelEngine.Nuclear.UraniumProcessor>();
                if (uProcessor != null) { UI.GameUIController.Instance?.OpenMachine(uProcessor); return; }
                var reprocessor = hit.collider.GetComponentInParent<VoxelEngine.Nuclear.WasteReprocessor>();
                if (reprocessor != null) { UI.GameUIController.Instance?.OpenMachine(reprocessor); return; }
                var electrolyser = hit.collider.GetComponentInParent<VoxelEngine.Gas.Electrolyser>();
                if (electrolyser != null) { UI.GameUIController.Instance?.OpenMachine(electrolyser); return; }
                var hydroEngine = hit.collider.GetComponentInParent<VoxelEngine.Gas.HydrogenEngine>();
                if (hydroEngine != null) { UI.GameUIController.Instance?.OpenMachine(hydroEngine); return; }
                var gasTank = hit.collider.GetComponentInParent<VoxelEngine.Gas.GasTank>();
                if (gasTank != null) { UI.GameUIController.Instance?.OpenMachine(gasTank); return; }
                var liquidPump = hit.collider.GetComponentInParent<VoxelEngine.Fluids.WaterPump>();
                if (liquidPump != null) { UI.GameUIController.Instance?.OpenMachine(liquidPump); return; }

                // Industrial fluid processors.
                var oilRefinery = hit.collider.GetComponentInParent<VoxelEngine.Crafting.OilRefinery>();
                if (oilRefinery != null) { UI.GameUIController.Instance?.OpenMachine(oilRefinery); return; }
                var chemPlant = hit.collider.GetComponentInParent<VoxelEngine.Industrial.StationaryChemicalPlant>();
                if (chemPlant != null) { UI.GameUIController.Instance?.OpenMachine(chemPlant); return; }

                // Grid (ship/vehicle) blocks that expose a UI panel. Cockpit is handled
                // separately via EnterCockpit, so we skip it here.
                // Holding a placeable block? Don't open the UI — let the GridBuilder
                // place the block instead (so you can build on existing blocks).
                bool holdingGridBlock = !inventory.ActiveStack.IsEmpty &&
                                        inventory.ActiveStack.item is VoxelEngine.GridSystem.GridBlockItem;
                var gridBlock = hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
                // Helm / Ship Console: right-click enters the control seat.
                if (!holdingGridBlock && gridBlock is VoxelEngine.Maritime.GridHelm helm)
                {
                    var pc = GetComponentInParent<VoxelEngine.Player.PlayerController>();
                    if (pc != null) helm.Enter(pc);
                    return;
                }
                if (!holdingGridBlock && gridBlock is VoxelEngine.Maritime.GridShipConsole console)
                {
                    var pc = GetComponentInParent<VoxelEngine.Player.PlayerController>();
                    if (pc != null) console.Enter(pc);
                    return;
                }
                if (!holdingGridBlock && gridBlock != null && GridBlockHasUI(gridBlock))
                {
                    UI.GameUIController.Instance?.OpenMachine(gridBlock);
                    return;
                }

                // Storage system.
                var storageTerminal = hit.collider.GetComponentInParent<VoxelEngine.Storage.StorageTerminal>();
                if (storageTerminal != null) { UI.GameUIController.Instance?.OpenMachine(storageTerminal); return; }
                var serverRack = hit.collider.GetComponentInParent<VoxelEngine.Storage.ServerRack>();
                if (serverRack != null) { UI.GameUIController.Instance?.OpenMachine(serverRack); return; }
                var patternTerm = hit.collider.GetComponentInParent<VoxelEngine.Storage.PatternTerminal>();
                if (patternTerm != null) { UI.GameUIController.Instance?.OpenMachine(patternTerm); return; }
                var craftTerm = hit.collider.GetComponentInParent<VoxelEngine.Storage.CraftingTerminal>();
                if (craftTerm != null) { UI.GameUIController.Instance?.OpenMachine(craftTerm); return; }
                var exporter = hit.collider.GetComponentInParent<VoxelEngine.Storage.StorageExporter>();
                if (exporter != null) { UI.GameUIController.Instance?.OpenMachine(exporter); return; }
                var importer = hit.collider.GetComponentInParent<VoxelEngine.Storage.StorageImporter>();
                if (importer != null) { UI.GameUIController.Instance?.OpenMachine(importer); return; }
                var diskManip = hit.collider.GetComponentInParent<VoxelEngine.Storage.DiskManipulator>();
                if (diskManip != null) { UI.GameUIController.Instance?.OpenMachine(diskManip); return; }
                var nasBlock = hit.collider.GetComponentInParent<VoxelEngine.Storage.NASBlock>();
                if (nasBlock != null) { UI.GameUIController.Instance?.OpenMachine(nasBlock); return; }
                var powerstation = hit.collider.GetComponentInParent<VoxelEngine.Storage.Powerstation>();
                if (powerstation != null) { UI.GameUIController.Instance?.OpenMachine(powerstation); return; }

                // Grid cockpit — removed old RMB enter logic (now on H).
                
                var electric = hit.collider.GetComponentInParent<ElectricFurnace>();
                if (electric != null) { UI.GameUIController.Instance?.OpenElectricFurnace(electric); return; }

                var furnace = hit.collider.GetComponentInParent<Furnace>();
                if (furnace != null) { UI.GameUIController.Instance?.OpenFurnace(furnace); return; }

                var station = hit.collider.GetComponentInParent<CraftingStation>();
                if (station != null) { UI.GameUIController.Instance?.OpenStation(station); return; }
            }

            if (buildHeld)
            {
                // Hold RMB to continuously place blocks.
                var stack = inventory.ActiveStack;
                if (stack.IsEmpty || !(stack.item is BlockItem block)) return;
                if (Time.time < _nextHit) return;
                if (BuildSystem.Instance != null && BuildSystem.Instance.TryPlace(block, hit, ray.direction))
                {
                    int taken = inventory.container.Remove(block, 1);
                    if (taken == 0) TryNetworkConsume(block, 1);
                    VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(block.displayName, block, 1);
                    _nextHit = Time.time + 0.2f;
                }
            }
        }

        /// <summary>
        /// Unified pipe QoL: if the player uses a normal world pipe item while aiming at a
        /// grid, place the matching grid pipe block instead of a static world pipe. This lets
        /// the same pipe items work both in bases and on ships.
        /// </summary>
        private bool TryPlaceStaticPipeOnGrid(BlockItem block, RaycastHit hit)
        {
            if (block == null || block.placedPrefab == null) return false;
            var targetGrid = hit.collider != null ? hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>() : null;
            if (targetGrid == null) return false;

            bool isUnifiedPipe = block.placedPrefab.GetComponentInChildren<VoxelEngine.Transport.ItemPipe>(true) != null
                              || block.placedPrefab.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null
                              || block.placedPrefab.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null;

            if (!isUnifiedPipe) return false;

            float cs = VoxelEngine.GridSystem.GridSizeExt.CellSize(targetGrid.gridSize);
            Vector3Int gridPos = targetGrid.WorldToGrid(hit.point + hit.normal * (cs * 0.55f));
            if (!targetGrid.CanPlace(gridPos))
                gridPos = targetGrid.WorldToGrid(hit.point + hit.normal * cs);
            if (!targetGrid.CanPlace(gridPos) || !targetGrid.HasNeighbor(gridPos)) return false;

            var go = Instantiate(block.placedPrefab);
            go.name = block.displayName;
            go.transform.rotation = targetGrid.transform.rotation;

            var gridBlock = go.GetComponent<VoxelEngine.GridSystem.GridBlock>();
            if (gridBlock == null) gridBlock = go.AddComponent<VoxelEngine.GridSystem.GridBlock>();
            gridBlock.blockName = block.displayName;
            gridBlock.BlockMass = Mathf.Max(block.massPerUnit, 80f);
            gridBlock.maxHP = Mathf.Max(block.blockHealth, 100f);

            var collider = go.GetComponent<Collider>();
            if (collider == null) collider = go.AddComponent<BoxCollider>();
            if (collider is BoxCollider box) box.size = Vector3.one * cs;

            targetGrid.AddBlock(gridPos, gridBlock);

            // The static pipe components registered before the object was parented/moved into
            // the grid, so force their network/visual graph to rebuild at the final position.
            var pipeVisual = go.GetComponentInChildren<VoxelEngine.Networks.PipeVisualBuilder>(true);
            if (pipeVisual != null)
            {
                pipeVisual.gridSize = cs;
                pipeVisual.ForceRebuild();
            }
            VoxelEngine.Transport.ItemPipeNetwork.Instance?.SetDirty();
            VoxelEngine.Gas.GasNetwork.Instance?.SetDirty();
            VoxelEngine.Fluids.FluidNetworkManager.Instance?.SetDirty();
            return true;
        }

        /// <summary>
        /// Pulls <paramref name="count"/> of <paramref name="item"/> from the storage
        /// network through the player's selected Wireless Transmitter. No-op unless
        /// the research node "res_build_from_network" is unlocked AND a transmitter
        /// is online. Used by the place-block path so building a wall doesn't stop
        /// the moment the player's hotbar stack runs out.
        /// </summary>
        private void TryNetworkConsume(VoxelEngine.Items.ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return;
            var rm = VoxelEngine.Research.ResearchManager.Instance;
            if (rm == null || !rm.IsUnlocked("res_build_from_network")) return;
            var ui = VoxelEngine.UI.GameUIController.Instance;
            var rack = ui != null ? ui.GetActiveWirelessRack() : null;
            if (rack == null || !rack.IsOnline) return;
            rack.NetworkExtract(item.itemId, count);
        }

        private void HitTree(Tree tree)
        {
            var stack = inventory.ActiveStack;
            ToolType used = ToolType.Other;
            int damage = (int)handStrength;
            float rate = handFireRate;
            if (!stack.IsEmpty && stack.item is ToolItem t)
            {
                used = t.toolType;
                damage = (int)t.strength;
                rate = t.fireRate;
                ConsumeDurability(stack);
            }
            int dealt = tree.Hit(damage, used);
            if (dealt > 0)
            {
                _feedback?.Trigger(tree.transform.position + Vector3.up * 1.2f, -shootCamera.transform.forward, new Color(0.45f, 0.30f, 0.16f));
                _nextHit = Time.time + 1f / Mathf.Max(0.1f, rate);
            }
        }

        private void BreakPlaced(PlacedBlock placed, RaycastHit hit)
        {
            var stack = inventory.ActiveStack;
            int damage = (int)handStrength;
            float rate = handFireRate;
            int  tier  = handTier;
            if (!stack.IsEmpty && stack.item is ToolItem t)
            {
                damage = (int)t.strength;
                rate   = t.fireRate;
                tier   = t.miningTier;
                ConsumeDurability(stack);
            }
            if (placed.Item != null && tier < placed.Item.miningTier) return; // can't break with this tool
            placed.Damage(damage, inventory);
            _feedback?.Trigger(hit.point, hit.normal, new Color(0.7f, 0.7f, 0.75f));
            _nextHit = Time.time + 1f / Mathf.Max(0.1f, rate);
        }

        private void HitTieredBlock(PlacedTieredBlock tiered, RaycastHit hit)
        {
            var stack = inventory.ActiveStack;
            // Hammer? -> upgrade.
            if (!stack.IsEmpty && stack.item is Hammer)
            {
                if (BuildSystemV2.Instance != null && BuildSystemV2.Instance.TryUpgrade(tiered))
                {
                    _feedback?.Trigger(hit.point, hit.normal, new Color(0.95f, 0.85f, 0.20f));
                    ConsumeDurability(stack);
                    _nextHit = Time.time + 1f / Mathf.Max(0.1f, ((ToolItem)stack.item).fireRate);
                }
                return;
            }

            // Otherwise, tool damages it (must meet mining-tier requirement).
            int damage = (int)handStrength;
            float rate = handFireRate;
            int  tier  = handTier;
            if (!stack.IsEmpty && stack.item is ToolItem t)
            {
                damage = (int)t.strength;
                rate   = t.fireRate;
                tier   = t.miningTier;
                ConsumeDurability(stack);
            }
            tiered.Damage(damage, tier, inventory);
            _feedback?.Trigger(hit.point, hit.normal, new Color(0.7f, 0.7f, 0.75f));
            _nextHit = Time.time + 1f / Mathf.Max(0.1f, rate);
        }

        private void MineVoxel(Ray ray, RaycastHit hit)
        {
            if (world == null || registry == null || inventory == null || inventory.container == null) return;

            var stack = inventory.ActiveStack;

            // Water Bucket: LMB scoops one water cell. Only works if the bucket is EMPTY.
            if (!stack.IsEmpty && stack.item is WaterBucket wb)
            {
                if (stack.durability > 0)
                {
                    Debug.Log("[Bucket] Already full — place the water first before scooping more.");
                    _nextHit = Time.time + 0.3f;
                    return;
                }
                var hitPos = world.WorldToVoxel(hit.point - ray.direction.normalized * 0.2f);
                bool scooped = false;
                VoxelEngine.Items.LiquidType scoopedLiquid = VoxelEngine.Items.LiquidType.Water;
                // Try the new fluid sim first (player-placed liquids + oceans seeded into the sim).
                var fsm = VoxelEngine.Fluids.FluidSimManager.Instance;
                if (fsm != null && fsm.TryDrainWaterAt(hitPos))
                {
                    scooped = true;
                    scoopedLiquid = VoxelEngine.Items.LiquidType.Water;
                }
                else if (fsm != null && fsm.TryDrainOilAt(hitPos))
                {
                    scooped = true;
                    scoopedLiquid = VoxelEngine.Items.LiquidType.CrudeOil;
                }
                else
                {
                    // Fall back: legacy WaterVoxel OR CrudeOil material in the voxel grid.
                    var here = world.GetVoxelWorld(hitPos);
                    if (here.material == (byte)VoxelEngine.Materials.MaterialId.WaterVoxel ||
                        here.material == (byte)VoxelEngine.Materials.MaterialId.WaterLiquid)
                    {
                        world.SetVoxelWorld(hitPos, new VoxelEngine.Core.Voxel(-127, (byte)VoxelEngine.Materials.MaterialId.Air));
                        scooped = true;
                        scoopedLiquid = VoxelEngine.Items.LiquidType.Water;
                    }
                    else if (here.material == (byte)VoxelEngine.Materials.MaterialId.CrudeOil)
                    {
                        world.SetVoxelWorld(hitPos, new VoxelEngine.Core.Voxel(-127, (byte)VoxelEngine.Materials.MaterialId.Air));
                        scooped = true;
                        scoopedLiquid = VoxelEngine.Items.LiquidType.CrudeOil;
                    }
                }
                if (scooped)
                {
                    stack.durability = 1;
                    stack.payload = scoopedLiquid;
                    inventory.container.RaiseChanged();
                }
                _nextHit = Time.time + 0.3f;
                return;
            }

            // Leveling Tool: special path — flatten terrain instead of mining it.
            if (!stack.IsEmpty && stack.item is LevelingTool lt)
            {
                Vector3 hitPos = hit.point;
                bool didFlatten = VoxelEngine.Modification.LevelingOp.ApplyAt(
                    world, registry, hitPos, lt.brushRadius, verticalReach: 6);
                if (!didFlatten)
                    Debug.Log("[LevelingTool] Target Y anchored at " + VoxelEngine.Modification.LevelingOp.TargetWorldY +
                              ". Click again on any surface within range to flatten to that height.");
                _feedback?.Trigger(hit.point, hit.normal, new Color(0.9f, 0.85f, 0.5f));
                ConsumeDurability(stack);
                _nextHit = Time.time + 1f / Mathf.Max(0.1f, lt.fireRate);
                return;
            }

            float strength = handStrength;
            float radius   = handBrushRadius;
            float rate     = handFireRate;
            int   tier     = handTier;
            if (!stack.IsEmpty && stack.item is ToolItem t)
            {
                strength = t.strength;
                radius   = t.brushRadius;
                rate     = t.fireRate;
                tier     = t.miningTier;
            }

            // Tier check on the surface voxel material (cheap).
            var hitVoxelPos = world.WorldToVoxel(hit.point - ray.direction.normalized * 0.2f);
            var v = world.GetVoxelWorld(hitVoxelPos);
            if (v.density > 0 && registry != null)
            {
                var def = registry.Get(v.material);
                if (def != null && def.miningTier > tier) return; // wrong tool tier — no progress
            }

            VoxelEditor.Subtract(world, registry, hit.point - ray.direction.normalized * 0.2f, radius, strength);
            // Sample tint from the hit material.
            Color tint = new Color(0.85f, 0.78f, 0.6f);
            if (registry != null && v.density > 0)
            {
                var def = registry.Get(v.material);
                if (def != null) tint = def.color;
            }
            _feedback?.Trigger(hit.point, hit.normal, tint);

            // Material-aware mining impact SFX (3 pre-baked variants + pitch jitter
            // so repeated hits never sound identical).
            if (v.density > 0)
            {
                // Safe audio call (stubs may not implement MiningSfxForMaterial yet)
                try
                {
                    var msfx = VoxelEngine.FX.SfxLibrary.MiningSfxForMaterial(v.material);
                    // Note: Sfx is an enum, so we can't compare to null. We just attempt playback.
                    VoxelEngine.FX.AudioManager.PlayAt(
                        VoxelEngine.FX.SfxLibrary.GetVariant(msfx, 3),
                        hit.point, volume: 0.7f,
                        pitch: UnityEngine.Random.Range(0.92f, 1.08f), maxDistance: 22f);
                }
                catch { /* Audio system not fully implemented yet */ }
            }
            if (!stack.IsEmpty && stack.item is ToolItem) ConsumeDurability(stack);
            _nextHit = Time.time + 1f / Mathf.Max(0.1f, rate);
        }

        private void ConsumeDurability(ItemStack stack)
        {
            stack.durability -= 1;
            if (stack.durability <= 0)
            {
                // Remove broken tool from the slot.
                var slot = inventory.container.GetSlot(inventory.activeHotbarIndex);
                if (slot == stack) inventory.container.SetSlot(inventory.activeHotbarIndex, new ItemStack());
            }
            inventory.container.RaiseChanged();
        }

        // ── Grid Grinder ────────────────────────────────────────────
        private VoxelEngine.GridSystem.GridBlock _grindTarget;
        private float _grindProgress;

        /// <summary>Grind progress 0-1 for the HUD progress bar.</summary>
        public float GrindProgress01 { get; private set; }
        public bool IsGrinding { get; private set; }

        private void HandleGrind(VoxelEngine.GridSystem.GridBlock block,
            VoxelEngine.GridSystem.GrinderTool grinder, RaycastHit hit)
        {
            if (_grindTarget != block) { _grindTarget = block; _grindProgress = 0f; }
            float grindTime = Mathf.Max(grinder.minGrindTime, grinder.baseGrindTime);
            _grindProgress += Time.deltaTime;
            GrindProgress01 = Mathf.Clamp01(_grindProgress / grindTime);
            IsGrinding = true;

            // Spark particles every 0.2s.
            if (Mathf.Repeat(_grindProgress, 0.2f) < Time.deltaTime)
                _feedback?.Trigger(hit.point, hit.normal, new Color(1f, 0.6f, 0.1f));

            if (_grindProgress >= grindTime)
            {
                _grindProgress = 0f; _grindTarget = null;
                GrindProgress01 = 0f; IsGrinding = false;

                // Find the matching GridBlockItem to return to the player.
                string blockName = block.blockName;
                VoxelEngine.GridSystem.GridBlockItem foundItem = null;

                // Search all loaded GridBlockItem assets.
                var allItems = Resources.FindObjectsOfTypeAll<VoxelEngine.GridSystem.GridBlockItem>();
                foreach (var gbi in allItems)
                {
                    if (gbi.displayName == blockName || gbi.name.Contains(blockName))
                    {
                        foundItem = gbi;
                        break;
                    }
                }

                // Give the item back to the player.
                if (foundItem != null)
                {
                    inventory.Add(foundItem, 1);
                    VoxelEngine.UI.BuildFeedbackHud.Show(
                        $"Ground down {blockName}", $"+1 {foundItem.displayName}",
                        foundItem.icon, VoxelEngine.UI.UITheme.AccentOrange);
                }
                else
                {
                    // Fallback: spawn as dropped item.
                    VoxelEngine.UI.BuildFeedbackHud.Show(
                        $"Ground down {blockName}", "Block recovered",
                        null, VoxelEngine.UI.UITheme.AccentOrange);
                }

                // Remove the block from its grid.
                if (block.Grid != null) block.Grid.RemoveBlock(block.GridPos);
                else Destroy(block.gameObject);

                ConsumeDurability(inventory.ActiveStack);
                _nextHit = Time.time + 0.3f;
            }
        }

        // Which grid blocks open an interaction panel on the Interact key. Pure
        // structural blocks (armor, glass, pipes…) and the cockpit are excluded.
        private static bool GridBlockHasUI(VoxelEngine.GridSystem.GridBlock b)
        {
            // Maritime blocks — any MaritimeBlockBase EXCEPT Helm (Helm enters instead of UI).
            if (b is VoxelEngine.Maritime.MaritimeBlockBase
                && b is not VoxelEngine.Maritime.GridHelm
                && b is not VoxelEngine.Maritime.GridShipConsole) return true;
            if (b is VoxelEngine.Maritime.GridHullBlock) return true;
            if (b is VoxelEngine.Maritime.GridBilgePump) return true;
            if (b is VoxelEngine.Maritime.GridMarineWaterPump) return true;

            return b is VoxelEngine.GridSystem.GridLiquidTank
                || b is VoxelEngine.GridSystem.GridGasTank
                || b is VoxelEngine.GridSystem.GridH2O2Generator
                || b is VoxelEngine.GridSystem.GridBattery
                || b is VoxelEngine.GridSystem.GridCargoContainer
                || b is VoxelEngine.GridSystem.GridWeapon
                || b is VoxelEngine.GridSystem.GridRefinery
                || b is VoxelEngine.GridSystem.GridChemicalPlant
                || b is VoxelEngine.GridSystem.GridPortableReactor
                || b is VoxelEngine.GridSystem.GridDockingPort
                || b is VoxelEngine.GridSystem.GridLandingGear
                || b is VoxelEngine.GridSystem.GridDrill
                || b is VoxelEngine.GridSystem.GridHydrogenEngine
                || b is VoxelEngine.GridSystem.GridElectricFurnace
                || b is VoxelEngine.GridSystem.GridBeacon
                || b is VoxelEngine.GridSystem.GridOreDetector;
        }
    }
}
