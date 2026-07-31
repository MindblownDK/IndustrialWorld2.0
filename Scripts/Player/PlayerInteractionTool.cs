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
        public VoxelEngine.Core.IVoxelWorld world;
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
            if (world      == null) world      = VoxelEngine.Core.ActiveWorld.Current;
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
            if (VoxelEngine.GridSystem.GridCockpit.AnyPilotSeatActive) return;
            IsGrinding = false; // reset each frame — HandleGrind sets it true when active
            if (world      == null) world      = VoxelEngine.Core.ActiveWorld.Current;
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
            bool hasHit = TryRaycastIgnoringSelf(ray, out var hit, reach);

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
                    // Mountable creature (horse): look at it and press the cockpit key to ride.
                    var horse = hit.collider.GetComponentInParent<VoxelEngine.Fauna.RideableAnimal>();
                    if (horse != null && horse.Rider == null)
                    {
                        VoxelEngine.UI.InteractionHud.Show("H / RMB", "Mount Horse");
                        if (GameSettings.WasPressed(InputAction.EnterCockpit))
                        {
                            var pc = GetComponentInParent<VoxelEngine.Player.PlayerController>();
                            if (pc != null) horse.Enter(pc);
                        }
                    }
                    else
                    {
                        VoxelEngine.UI.InteractionHud.Hide();
                    }
                }
            }
            else
            {
                VoxelEngine.UI.InteractionHud.Hide();
            }

            if (!mineHeld && !buildHeld && !buildDown) return;

            // RMB on a rideable horse mounts it (takes priority over building/placing).
            if (buildDown && hasHit)
            {
                var rmbHorse = hit.collider.GetComponentInParent<VoxelEngine.Fauna.RideableAnimal>();
                if (rmbHorse != null && rmbHorse.Rider == null)
                {
                    var pc = GetComponentInParent<VoxelEngine.Player.PlayerController>();
                    if (pc != null && !pc.IsMounted) rmbHorse.Enter(pc);
                    return;
                }
            }

            // RMB a turret/artillery -> open the defense control panel (targeting, reload, shells).
            if (buildDown && hasHit)
            {
                Component def = hit.collider.GetComponentInParent<VoxelEngine.Combat.Artillery>();
                if (def == null) def = hit.collider.GetComponentInParent<VoxelEngine.Combat.Turret>();
                if (def != null)
                {
                    VoxelEngine.UI.GameUIController.Instance?.OpenDefense(def);
                    return;
                }
            }

            var heldStack = inventory.ActiveStack;

            // ── WEAPON dispatch — LMB attacks with melee/ranged/thrown weapons, even when
            //    aiming at open sky (each mode does its own hit detection). ──
            if (mineHeld && !heldStack.IsEmpty && heldStack.item is VoxelEngine.Combat.WeaponItem wep
                && Time.time >= _nextHit)
            {
                HandleWeaponAttack(wep, ray);
                _nextHit = Time.time + Mathf.Max(0.1f, wep.attackCooldown);
                return;
            }

            if (!hasHit)
            {
                // Mining tools still play their swing when aimed at the sky (nothing to hit).
                if (mineHeld && Time.time >= _nextHit
                    && !heldStack.IsEmpty && heldStack.item is ToolItem mt
                    && (mt.toolType == ToolType.Pickaxe || mt.toolType == ToolType.Axe))
                {
                    GetComponent<VoxelEngine.Player.HeldToolView>()?.DoSwing();
                    _nextHit = Time.time + 1f / Mathf.Max(0.1f, mt.fireRate);
                    return;
                }
                // Unified pipes: extending a run by aiming at its open end has to work
                // even when the camera ray slips between the thin arm/cap visuals and
                // hits nothing at all. The BuildSystem ghost previews this exact chain
                // cell right now, so clicking here places what the player sees.
                if ((buildDown || buildHeld) && Time.time >= _nextHit
                    && !inventory.ActiveStack.IsEmpty
                    && inventory.ActiveStack.item is BlockItem heldPipe
                    && IsUnifiedPipeItem(heldPipe)
                    && BuildSystem.Instance != null
                    && BuildSystem.Instance.TryPlaceUnifiedPipeChain(heldPipe, ray))
                {
                    int takenPipe = inventory.container.Remove(heldPipe, 1);
                    if (takenPipe == 0) TryNetworkConsume(heldPipe, 1);
                    VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(heldPipe.displayName, heldPipe, 1);
                    _nextHit = Time.time + 0.2f;
                }
                return;
            }

            // ── WRENCH dispatch — short-circuits all other tool behaviour. ──
            //   LMB = connect/select  •  RMB = disconnect  •  Shift modifies both
            if (!heldStack.IsEmpty && heldStack.item is WrenchTool)
            {
                if (_wrench == null) _wrench = new WrenchInteraction();
                if (mineDown)  { _wrench.OnUse(hit, this);    _nextHit = Time.time + 0.15f; return; }
                if (buildDown) { _wrench.OnAltUse(hit);        _nextHit = Time.time + 0.15f; return; }
                // Holding either button without a fresh press: swallow input so the
                // wrench never accidentally mines a block or places a phantom item.
                if (mineHeld || buildHeld) return;
            }

            // While the Building Hammer has an active family, RMB belongs exclusively
            // to BuildSystemV2 placement. Prevent the generic interaction path from
            // opening or toggling the object underneath the placement ghost.
            if (!heldStack.IsEmpty && heldStack.item is Hammer
                && HammerBuildWheel.Instance != null && HammerBuildWheel.Instance.ActiveFamily.HasValue
                && (buildDown || buildHeld))
                return;

            // ── Storage drawer direct interaction ─────────────────────
            var drawer = hit.collider.GetComponentInParent<VoxelEngine.Storage.StorageDrawer>();
            if (drawer != null && IsFrontHit(drawer.transform, hit))
            {
                if (mineDown && Time.time >= _nextHit)
                {
                    int amount = 1;
                    if (IsShiftHeld() && drawer.storedItem != null)
                        amount = Mathf.Max(VoxelEngine.Storage.StorageDrawer.DefaultBaseStackSize, ItemStack.MaxItemsPerStack(drawer.storedItem));
                    drawer.TryPlayerExtract(inventory, amount);
                    _nextHit = Time.time + 0.12f;
                    return;
                }
                if (buildDown && Time.time >= _nextHit)
                {
                    bool shiftHeld = IsShiftHeld();
                    if (!inventory.ActiveStack.IsEmpty)
                    {
                        bool moved = false;
                        if (shiftHeld)
                        {
                            var controller = VoxelEngine.Storage.StorageDrawerController.FindNearest(drawer.transform.position);
                            if (controller != null)
                            {
                                controller.RefreshLinks();
                                moved = controller.TryPlayerInsert(inventory, true, drawer.transform.position, requireExistingMatch: true);
                            }
                        }
                        if (!moved) drawer.TryPlayerInsert(inventory, shiftHeld);
                        _nextHit = Time.time + 0.12f;
                    }
                    else
                    {
                        UI.GameUIController.Instance?.OpenMachine(drawer);
                    }
                    return;
                }
                // The drawer front is an interaction face, not a mining face.
                // To break the drawer, hit any side/back face instead.
                if (mineHeld || buildHeld) return;
            }

            // ── Drawer controller direct insertion ───────────────────
            var drawerController = hit.collider.GetComponentInParent<VoxelEngine.Storage.StorageDrawerController>();
            if (drawerController != null && buildDown && Time.time >= _nextHit && IsFrontHit(drawerController.transform, hit))
            {
                bool shiftHeld = IsShiftHeld();
                if (!inventory.ActiveStack.IsEmpty)
                {
                    drawerController.RefreshLinks();
                    drawerController.TryPlayerInsert(inventory, shiftHeld, drawerController.transform.position);
                    _nextHit = Time.time + 0.12f;
                }
                else
                {
                    UI.GameUIController.Instance?.OpenMachine(drawerController);
                }
                return;
            }

            // ---------- LMB ----------
            if (mineHeld)
            {
                if (Time.time < _nextHit) return;

                // 1) Tree?
                var tree = hit.collider.GetComponentInParent<Tree>();
                if (tree != null) { HitTree(tree); return; }

                // Grid block breaking — resolver tolerates colliders sitting on child
                // visuals (maritime machinery, screens) instead of the block root.
                var gridBlock = ResolveGridBlockFromHit(hit);
                if (gridBlock != null)
                {
                    var grindStack = inventory.ActiveStack;
                    if (!grindStack.IsEmpty && grindStack.item is VoxelEngine.GridSystem.GrinderTool grinder)
                    {
                        HandleGrind(gridBlock, grinder, hit);
                        return;
                    }
                    // Any other tool (or bare hands) damages the block so maritime
                    // blocks and screens can be broken exactly like any other structure.
                    HitGridBlockWithTool(gridBlock, hit);
                    return;
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
            var heldForPlace = inventory.ActiveStack;
            if (buildDown && !heldForPlace.IsEmpty && heldForPlace.item != null)
            {
                string heldId = heldForPlace.item.itemId ?? string.Empty;
                if (heldId == "hv_wire" || heldId.EndsWith("_lv_wire", System.StringComparison.OrdinalIgnoreCase))
                    return; // HighVoltageWireTool owns manual wire clicks.
            }

            // If holding a placeable block (cable, pipe, etc), RMB places it directly.
            if (buildDown && !heldForPlace.IsEmpty && heldForPlace.item is BlockItem heldBlock)
            {
                bool aimingUnifiedPipeAtGrid = IsUnifiedPipeItem(heldBlock)
                    && hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>() != null;
                // ONE placement path for unified pipes: BuildSystem.TryPlace routes
                // them through the maritime port-snap (ghost ≡ placed). The old
                // static fork bypassed the snap and dropped pipes at the raw ray hit.
                if (Time.time >= _nextHit && BuildSystem.Instance != null && BuildSystem.Instance.TryPlace(heldBlock, hit, ray.direction))
                {
                    int taken = inventory.container.Remove(heldBlock, 1);
                    if (taken == 0) TryNetworkConsume(heldBlock, 1);
                    VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(heldBlock.displayName, heldBlock, 1);
                    _nextHit = Time.time + 0.2f;
                }
                else if (Time.time >= _nextHit && aimingUnifiedPipeAtGrid)
                {
                    VoxelEngine.UI.BuildFeedbackHud.Show("Detail Pipe Blocked", "Choose an empty 0.5 m lattice cell", heldBlock.icon, Color.red);
                    _nextHit = Time.time + 0.15f;
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
                // Armor equip (RMB an armor item in the hotbar -> wear it). Routes through the
                // dedicated armor equipment slot so the inventory UI and PlayerStats.equippedArmor
                // (read by TakeDamage) stay perfectly in sync. Old armor returns to the inventory.
                {
                    var armorStack = inventory.ActiveStack;
                    if (!armorStack.IsEmpty && armorStack.item is VoxelEngine.Combat.ArmorItem armor)
                    {
                        var equipment = inventory.GetComponent<VoxelEngine.Player.PlayerEquipment>();
                        if (equipment != null)
                        {
                            var armorSlots = equipment.ArmorSlots;
                            // Return currently-equipped armor (if any) to the inventory first.
                            var current = equipment.EquippedArmor;
                            if (current != null)
                            {
                                armorSlots.SetSlot(0, new VoxelEngine.Items.ItemStack());             // clear -> sync sets equippedArmor = null
                                inventory.container.Insert(new VoxelEngine.Items.ItemStack(current, 1));
                            }
                            armorSlots.SetSlot(0, new VoxelEngine.Items.ItemStack { item = armor, count = 1 }); // sync sets equippedArmor = armor
                            inventory.container.Remove(armor, 1);
                            inventory.container.RaiseChanged();
                            VoxelEngine.UI.BuildFeedbackHud.Show("Equipped", armor.displayName, armor.icon, new Color(0.4f, 0.8f, 1f));
                        }
                        return;
                    }
                }

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

                // Blueprint data core — RMB to restore blueprint and unlock recipe (4.9.0)
                if (!eatStack.IsEmpty && eatStack.item is VoxelEngine.Items.BlueprintDataCoreItem bpCore)
                {
                    if (bpCore.TryUnlock())
                    {
                        inventory.container.Remove(bpCore, 1);
                        VoxelEngine.UI.BuildFeedbackHud.Show($"Blueprint Restored", $"{bpCore.targetDisplayName} unlocked!", bpCore.icon, new Color(0.45f, 0.85f, 1f));
                    }
                    else
                    {
                        VoxelEngine.UI.BuildFeedbackHud.Show($"Already Unlocked", $"{bpCore.targetDisplayName} is already restored", bpCore.icon, Color.gray);
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

                var tieredDoor = hit.collider.GetComponentInParent<VoxelEngine.Building.Tiered.TieredDoor>();
                if (tieredDoor != null)
                {
                    tieredDoor.Toggle(transform.position);
                    return;
                }

                // 1) Open container if looking at chest / furnace / crafting bench.
                var ruinChest = hit.collider.GetComponentInParent<VoxelEngine.Exploration.RuinChest>();
                if (ruinChest != null) { ruinChest.Open(); return; }

                var chest = hit.collider.GetComponentInParent<Chest>();
                if (chest != null) { UI.GameUIController.Instance?.OpenContainer(chest.container, chest); return; }

                // Piston Interaction: Right-click to toggle push/pull.
                var piston = hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridPiston>();
                if (piston != null)
                {
                    piston.Toggle();
                    return;
                }

                // Lighting Control: if hit object is a light, select it for the UI.
                var lightCtrl = hit.collider.GetComponentInParent<VoxelEngine.Power.VoxelLightController>();
                if (lightCtrl != null)
                {
                    VoxelEngine.UI.LightingManager.Instance?.SelectLight(lightCtrl);
                    return;
                }

                bool holdingPlaceable = !inventory.ActiveStack.IsEmpty
                    && (inventory.ActiveStack.item is BlockItem || inventory.ActiveStack.item is VoxelEngine.GridSystem.GridBlockItem);
                if (!holdingPlaceable)
                {
                    var cryobed = hit.collider.GetComponentInParent<VoxelEngine.Building.Cryobed>();
                    if (cryobed != null) { VoxelEngine.UI.CryobedConfigHud.Open(cryobed); return; }
                    var gridCryobed = hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridCryobed>();
                    if (gridCryobed != null) { VoxelEngine.UI.CryobedConfigHud.Open(gridCryobed); return; }
                }
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
                var legacyCoalPower = hit.collider.GetComponentInParent<VoxelEngine.Power.PowerGenerator>();
                if (legacyCoalPower != null && legacyCoalPower.gameObject.name.ToLowerInvariant().Contains("coal"))
                {
                    coalGen = legacyCoalPower.GetComponent<VoxelEngine.Power.CoalGeneratorFuel>();
                    if (coalGen == null) coalGen = legacyCoalPower.gameObject.AddComponent<VoxelEngine.Power.CoalGeneratorFuel>();
                    UI.GameUIController.Instance?.OpenCoalGenerator(coalGen);
                    return;
                }

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
                var biofarm = hit.collider.GetComponentInParent<VoxelEngine.Building.Biofarm>();
                if (biofarm != null) { UI.GameUIController.Instance?.OpenMachine(biofarm); return; }
                var liquidPump = hit.collider.GetComponentInParent<VoxelEngine.Fluids.WaterPump>();
                if (liquidPump != null) { UI.GameUIController.Instance?.OpenMachine(liquidPump); return; }

                // Wind turbines — right-click ANY part (tower, nacelle, blade…) to open
                // the turbine dashboard (assembly checklist / output / condition).
                var windTurbine = hit.collider.GetComponentInParent<VoxelEngine.Power.Wind.WindTurbineController>();
                if (windTurbine == null)
                {
                    var windPart = hit.collider.GetComponentInParent<VoxelEngine.Power.Wind.WindTurbinePart>();
                    if (windPart != null) windTurbine = windPart.Controller;
                }
                if (windTurbine != null) { UI.GameUIController.Instance?.OpenMachine(windTurbine); return; }

                // Factory machines.
                var crusher = hit.collider.GetComponentInParent<VoxelEngine.Simulation.Crusher>();
                if (crusher != null) { UI.GameUIController.Instance?.OpenMachine(crusher); return; }
                var assembler = hit.collider.GetComponentInParent<VoxelEngine.Simulation.Assembler>();
                if (assembler != null) { UI.GameUIController.Instance?.OpenMachine(assembler); return; }
                var funnel = hit.collider.GetComponentInParent<VoxelEngine.Simulation.Funnel>();
                if (funnel != null) { UI.GameUIController.Instance?.OpenMachine(funnel); return; }
                var splitter = hit.collider.GetComponentInParent<VoxelEngine.Simulation.ConveyorSplitter>();
                if (splitter != null) { UI.GameUIController.Instance?.OpenMachine(splitter); return; }

                // Industrial fluid processors.
                var oilRefinery = hit.collider.GetComponentInParent<VoxelEngine.Crafting.OilRefinery>();
                if (oilRefinery != null) { UI.GameUIController.Instance?.OpenMachine(oilRefinery); return; }
                var chemPlant = hit.collider.GetComponentInParent<VoxelEngine.Industrial.StationaryChemicalPlant>();
                if (chemPlant != null) { UI.GameUIController.Instance?.OpenMachine(chemPlant); return; }

                // Grid (ship/vehicle) blocks that expose a UI panel. Cockpit is handled
                // separately via EnterCockpit, so we skip it here.
                // Holding a placeable block? Don't open the UI — let the GridBuilder
                // place the block instead (so you can build on existing blocks).
                bool holdingGridBlock = IsHoldingGridBlock();
                var gridBlock = hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
                // Cockpit / control seats: right-click enters directly when not holding a grid block.
                if (!holdingGridBlock && gridBlock is VoxelEngine.GridSystem.GridCockpit cockpit)
                {
                    var pc = GetComponentInParent<VoxelEngine.Player.PlayerController>();
                    if (pc != null && cockpit.Pilot == null) cockpit.Enter(pc);
                    return;
                }
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

                // Grid Screen Block: right-click opens the screen config panel before any generic grid UI path.
                if (!holdingGridBlock && gridBlock is VoxelEngine.GridSystem.GridScreenBlock screenBlock)
                {
                    VoxelEngine.GridSystem.UI.GridScreenConfigUI.Instance.Open(screenBlock);
                    return;
                }

                if (!holdingGridBlock && gridBlock != null && GridBlockHasUI(gridBlock))
                {
                    UI.GameUIController.Instance?.OpenMachine(gridBlock);
                    return;
                }

                // Storage system.
                var storageDrawerController = hit.collider.GetComponentInParent<VoxelEngine.Storage.StorageDrawerController>();
                if (storageDrawerController != null && IsFrontHit(storageDrawerController.transform, hit)) { UI.GameUIController.Instance?.OpenMachine(storageDrawerController); return; }
                var itemDisplay = hit.collider.GetComponentInParent<VoxelEngine.Storage.StorageItemDisplayBlock>();
                if (itemDisplay != null) { UI.GameUIController.Instance?.OpenMachine(itemDisplay); return; }
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

                var electric = hit.collider.GetComponentInParent<ElectricFurnace>();
                if (electric != null) { UI.GameUIController.Instance?.OpenElectricFurnace(electric); return; }

                var vStation = hit.collider.GetComponentInParent<VoxelEngine.Simulation.IVoltageStation>();
                if (vStation != null && vStation is MonoBehaviour mb)
                {
                    UI.GameUIController.Instance?.OpenMachine(mb);
                    return;
                }

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
                bool aimingUnifiedPipeAtGrid = IsUnifiedPipeItem(block)
                    && hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>() != null;
                bool placed = BuildSystem.Instance != null && BuildSystem.Instance.TryPlace(block, hit, ray.direction);
                if (placed)
                {
                    int taken = inventory.container.Remove(block, 1);
                    if (taken == 0) TryNetworkConsume(block, 1);
                    VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(block.displayName, block, 1);
                    _nextHit = Time.time + 0.2f;
                }
                else if (aimingUnifiedPipeAtGrid)
                {
                    _nextHit = Time.time + 0.15f;
                }
            }
        }


        private bool TryRaycastIgnoringSelf(Ray ray, out RaycastHit hit, float maxDistance)
        {
            var hits = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Transform selfRoot = transform.root;
            for (int i = 0; i < hits.Length; i++)
            {
                var candidate = hits[i];
                if (candidate.collider == null) continue;
                if (selfRoot != null && candidate.collider.transform.IsChildOf(selfRoot)) continue;
                if (PlayerRaycastFilter.IsOwnPlayerCollider(candidate.collider, transform)) continue;
                hit = candidate;
                return true;
            }
            hit = default;
            return false;
        }

        private static bool IsFrontHit(Transform blockTransform, RaycastHit hit)
        {
            if (blockTransform == null) return false;
            return Vector3.Dot(hit.normal.normalized, blockTransform.forward) > 0.55f;
        }

        private static bool IsShiftHeld()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
#else
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
        }

        private bool IsHoldingGridBlock()
        {
            if (inventory == null) return false;
            var stack = inventory.ActiveStack;
            return !stack.IsEmpty && stack.item is VoxelEngine.GridSystem.GridBlockItem;
        }

        /// <summary>
        /// Unified pipe placement: the existing pipe item remains the only item. When aimed
        /// at a Grid it is attached at Detail scale through the 0.5 m precision lattice;
        /// elsewhere the same item retains its normal static-world placement.
        /// </summary>
        private static bool IsUnifiedPipeItem(BlockItem block)
        {
            if (block == null || block.placedPrefab == null) return false;
            return block.placedPrefab.GetComponentInChildren<VoxelEngine.Transport.ItemPipe>(true) != null
                || block.placedPrefab.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null
                || block.placedPrefab.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null;
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

        // ── COMBAT (Phase 1) — apply weapon damage to whatever IDamageable the crosshair hits. ──
        private static Material _effectMat;
        private static Material EffectMat
        {
            get
            {
                if (_effectMat == null)
                {
                    Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                    _effectMat = new Material(sh);
                    _effectMat.color = new Color(1f, 0.85f, 0.35f);
                    if (_effectMat.HasProperty("_BaseColor")) _effectMat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.35f));
                }
                return _effectMat;
            }
        }

        private static void MuzzleFlash(Vector3 pos)
        {
            var go = new GameObject("MuzzleFlash");
            go.transform.position = pos;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(go.transform, false);
            sphere.transform.localScale = Vector3.one * 0.14f;
            sphere.GetComponent<Renderer>().sharedMaterial = EffectMat;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.7f, 0.35f); light.range = 6f; light.intensity = 6f;
            Object.Destroy(go, 0.07f);
        }

        private static void Tracer(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len < 0.01f) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = (from + to) * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(dir);
            go.transform.localScale = new Vector3(0.05f, 0.05f, len);
            go.GetComponent<Renderer>().sharedMaterial = EffectMat;
            Object.Destroy(go, 0.12f);
        }

        private void HandleWeaponAttack(VoxelEngine.Combat.WeaponItem weapon, Ray ray)
        {
            float dist = Mathf.Max(0.5f, weapon.range);

            if (weapon.attackMode == VoxelEngine.Combat.WeaponItem.AttackMode.Ranged)
            {
                // Ammo: spend one per shot if the weapon uses ammo. No ammo → no shot.
                if (weapon.ammoItem != null &&
                    inventory.container.Remove(weapon.ammoItem, Mathf.Max(1, weapon.ammoPerShot)) <= 0)
                {
                    VoxelEngine.UI.BuildFeedbackHud.Show("Empty", "Out of " + weapon.ammoItem.displayName, weapon.ammoItem.icon, Color.yellow);
                    return;
                }
                inventory.container.RaiseChanged();

                var hv = GetComponent<VoxelEngine.Player.HeldToolView>();
                Vector3 muzzle = hv != null ? hv.MuzzleWorldPosition : ray.origin + ray.direction * 0.6f;
                Vector3 impact = ray.origin + ray.direction * dist;
                if (TryRaycastIgnoringSelf(ray, out var hit, dist))
                {
                    impact = hit.point;
                    var d = hit.collider.GetComponentInParent<VoxelEngine.Combat.IDamageable>();
                    if (d != null && d.IsAlive)
                        d.TakeDamage(new VoxelEngine.Combat.DamageEvent {
                            amount = weapon.damage, type = weapon.damageType,
                            point = hit.point, direction = ray.direction, source = gameObject });
                }
                MuzzleFlash(muzzle);
                Tracer(muzzle, impact);
                GetComponent<VoxelEngine.Player.HeldToolView>()?.DoRecoil();
                return;
            }

            if (weapon.attackMode == VoxelEngine.Combat.WeaponItem.AttackMode.Thrown)
            {
                Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(ray.origin);
                Vector3 origin = ray.origin + ray.direction * 1.2f;
                Vector3 throwVel = ray.direction * weapon.throwForce + up * 3.2f; // forward + upward arc
                VoxelEngine.Combat.BombProjectile.Spawn(origin, throwVel, gameObject, weapon.explosionMaterial,
                    weapon.explosionRadius, weapon.explosionDamage, weapon.fuseTime, weapon.voxelDamageRadius, weapon.explosionMaterial);
                GetComponent<VoxelEngine.Player.HeldToolView>()?.DoRecoil();
                inventory.container.Remove(weapon, 1);   // consumable: spend one grenade
                inventory.container.RaiseChanged();
                return;
            }


            // ── Melee: play the swing, then damage the closest target in front.
            GetComponent<VoxelEngine.Player.HeldToolView>()?.DoSwing();

            VoxelEngine.Combat.IDamageable target = null;
            Vector3 hitPoint = ray.origin + ray.direction * dist;
            if (TryRaycastIgnoringSelf(ray, out var mHit, dist))
            {
                target = mHit.collider.GetComponentInParent<VoxelEngine.Combat.IDamageable>();
                hitPoint = mHit.point;
            }
            if (target == null)
            {
                // Forgiving arc: a sphere sweep in front of the camera catches close targets
                // even when the crosshair isn't perfectly centered on them.
                Vector3 center = ray.origin + ray.direction * (dist * 0.5f);
                var cols = Physics.OverlapSphere(center, 0.65f, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                foreach (var c in cols)
                {
                    var d = c.GetComponentInParent<VoxelEngine.Combat.IDamageable>();
                    if (d == null || !d.IsAlive) continue;
                    if (!(d is MonoBehaviour mb)) continue;
                    float dd = Vector3.Distance(mb.transform.position, ray.origin);
                    if (dd < best) { best = dd; target = d; }
                }
            }
            if (target != null && target.IsAlive)
                target.TakeDamage(new VoxelEngine.Combat.DamageEvent {
                    amount = weapon.damage, type = weapon.damageType,
                    point = hitPoint, direction = ray.direction, source = gameObject });
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

                // Return the block to the player as an item (SourceItem-first, so
                // renamed maritime blocks/screens resolve reliably), then remove it.
                ReturnGridBlockItemToPlayer(block, "Ground down");
                RemoveGridBlockFromGrid(block);

                ConsumeDurability(inventory.ActiveStack);
                _nextHit = Time.time + 0.3f;
            }
        }

        // ── Grid block breaking / item recovery ─────────────────────

        /// <summary>Resolve the GridBlock under the crosshair. Colliders on maritime
        /// machinery and screens often sit on child visuals instead of the block root,
        /// so walk up the hierarchy first; if the hit belongs to a grid but not a
        /// specific block collider, fall back to mapping the hit point to a grid cell.</summary>
        private VoxelEngine.GridSystem.GridBlock ResolveGridBlockFromHit(RaycastHit hit)
        {
            if (hit.collider == null) return null;

            var direct = hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            if (direct != null) return direct;

            var grid = hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>();
            if (grid == null) return null;

            var pos = grid.WorldToGrid(hit.point);
            return grid.GetBlock(pos);
        }

        /// <summary>LMB fallback for grid blocks when no grinder is held: any tool
        /// (or bare hands) chips away at the block's HP so maritime machinery and
        /// screens can be broken and recovered like any other structure.</summary>
        private void HitGridBlockWithTool(VoxelEngine.GridSystem.GridBlock block, RaycastHit hit)
        {
            var stack = inventory.ActiveStack;
            int damage = (int)handStrength;
            float rate = handFireRate;
            if (!stack.IsEmpty && stack.item is ToolItem t)
            {
                damage = (int)t.strength;
                rate   = t.fireRate;
                ConsumeDurability(stack);
            }

            // Capture identity before Damage() potentially removes and destroys it.
            bool destroyed = block.Damage(damage);
            _feedback?.Trigger(hit.point, hit.normal, new Color(0.7f, 0.7f, 0.75f));

            if (destroyed) ReturnGridBlockItemToPlayer(block, "Broke down");
            _nextHit = Time.time + 1f / Mathf.Max(0.1f, rate);
        }

        /// <summary>Give the item that placed <paramref name="block"/> back to the
        /// player. Prefers the runtime SourceItem (set by GridBuilder at placement)
        /// because blocks are renamed on placement ("Screen (Small)", "Crude Engine"),
        /// then falls back to a normalized name search across GridBlockItem assets.</summary>
        private void ReturnGridBlockItemToPlayer(VoxelEngine.GridSystem.GridBlock block, string verb)
        {
            if (block == null) return;
            string blockName = block.blockName;
            VoxelEngine.GridSystem.GridBlockItem foundItem = block.SourceItem as VoxelEngine.GridSystem.GridBlockItem;

            if (foundItem == null)
            {
                string wanted = SimplifyBlockName(blockName);
                var allItems = Resources.FindObjectsOfTypeAll<VoxelEngine.GridSystem.GridBlockItem>();
                foreach (var gbi in allItems)
                {
                    if (gbi == null) continue;
                    if (SimplifyBlockName(gbi.displayName) == wanted
                        || SimplifyBlockName(gbi.itemId) == wanted
                        || SimplifyBlockName(gbi.name) == wanted)
                    {
                        foundItem = gbi;
                        break;
                    }
                }
            }

            if (foundItem != null)
            {
                inventory.Add(foundItem, 1);
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    $"{verb} {blockName}", $"+1 {foundItem.displayName}",
                    foundItem.icon, VoxelEngine.UI.UITheme.AccentOrange);
            }
            else
            {
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    $"{verb} {blockName}", "Block recovered",
                    null, VoxelEngine.UI.UITheme.AccentOrange);
            }
        }

        /// <summary>Remove a block from its owning grid, honouring the precision
        /// attachment layer (plain Grid.RemoveBlock silently fails for those).</summary>
        private static void RemoveGridBlockFromGrid(VoxelEngine.GridSystem.GridBlock block)
        {
            if (block == null) return;
            if (block.Grid != null)
            {
                if (block.IsPrecisionAttachment)
                    block.Grid.GetComponent<VoxelEngine.GridSystem.GridPrecisionAttachmentLayer>()
                        ?.RemoveBlock(block.PrecisionGridPos);
                else
                    block.Grid.RemoveBlock(block.GridPos);
            }
            else
            {
                Destroy(block.gameObject);
            }
        }

        /// <summary>Normalize a name for matching: lowercase, alphanumerics only.
        /// Bridges placement-renamed blocks ("Screen (Small)") and their item assets
        /// ("Screen_Small").</summary>
        private static string SimplifyBlockName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
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
                || b is VoxelEngine.GridSystem.GridBiofarm
                || b is VoxelEngine.GridSystem.GridCryobed
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
                || b is VoxelEngine.GridSystem.GridOreDetector
                || b is VoxelEngine.GridSystem.GridSlidingDoor
                || b is VoxelEngine.Simulation.GridLightBlock
                || b.GetComponent<VoxelEngine.Simulation.LEDStrip>() != null;
        }
    }
}
