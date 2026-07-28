# IndustrialWorld — Changelog

**Branch:** `Dev`  
**Current Version:** `6.41.6-dev`

All release notes are maintained here so `Roadmap.md` remains focused on planned work and execution status.

### [6.41.6-dev] Enemy Spawner Compile Fix + Non-Destructive Ghoul Prefab

**Type:** PATCH — compile fix + non-destructive editor behaviour, save-compatible.

**Fixed — EnemySpawner.cs CS0103 (`The name 'g' does not exist in the current context`):**
- In `EnemySpawner.Update()` the freshly-instantiated ghoul was captured into a local named `ghoul`, but the spawn block still referenced the old loop variable `g` (the cull loop's variable, which was out of scope). The 6.41.5 rename only touched the declaration, not the usages, so the assembly failed to compile — which is *why* no `[EnemySpawner]` / `[Ghoul] Spawned` logs appeared at all (none of the combat runtime could run). Both usages now correctly reference `ghoul` (`if (ghoul != null)` + `_alive.Add(ghoul)`). The build compiles again.

**Fixed — Step 23 no longer deletes/overwrites your Ghoul prefab:**
- `BuildEnemyContent` previously called `AssetDatabase.DeleteAsset("Assets/Resources/Enemies/Ghoul.prefab")` before regenerating — every run wiped the prefab and triggered the recurring **"API has changed"** prompt. Step 23 is now **non-destructive**: if `Resources/Enemies/Ghoul.prefab` already exists (including one you built or customized by hand), it is **preserved exactly** and reused for the biome scatter; the freshly-built scene object is simply discarded. The prefab is only created on a genuine first run. No more destroyed prefabs, no more "API has changed" on repeat runs.

**Result:** Game now compiles. The `EnemySpawner` (auto-created via `RuntimeInitializeOnLoad`) and the biome-scatter Ghouls can actually run, so spawn diagnostics will finally show up in the Console. Build, run Step 23 (it will keep your existing Ghoul prefab), Play, and watch for the `[EnemySpawner] RuntimeInitialize` / `Awake — prefab=OK/NULL` / `Spawned ghoul #N` / `[Ghoul] Spawned at` logs.

**Files touched:**
- `Scripts/Combat/EnemySpawner.cs` (CS0103: `g` -> `ghoul`)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 23 non-destructive prefab handling)
- `Scripts/Core/GameVersion.cs` (6.41.5 -> 6.41.6)
- `Changelog.md`

---

### [6.31.0-dev] World Setting — Disable Ruin Loot Respawn + Rare Ruins Easy Numbers

**Type:** MINOR — new world setting + spawn tuning, save-compatible.

**Added — Create/Edit World option to disable respawning loot:**
1. `WorldSession` new bool `allowRuinLootRespawn` (default true) + `DefaultAllowRuinLootRespawn`. Added to `WorldSettingsData` as int `allowRuinLootRespawn` (1/-1) for JSON persistence.
2. Updated `SaveWorldSettings`, `LoadWorldSettings`, `TryReadWorldSettings` (new overload with 6 out params), `SaveWorldSettingsFor` (new 6-param overload), `ListWorlds`, `WorldSummary` (new bool field).
3. `MainMenuController`: new fields `_newAllowRuinLootRespawn` / `_editAllowRuinLootRespawn`, UI toggles in `BuildNewWorldPage` and `BuildEditWorldPage`: "Allow Ruin Loot to Respawn (uncheck to disable respawning loot)" — default checked. `StartEditWorld` and `ApplyWorldEdit` / `CreateAndLoadWorld` now save/load the toggle.
4. `RuinChest`: `Update()` now checks `WorldSession.Instance.allowRuinLootRespawn` — if false, never respawns (stays looted forever). `TryOpen()` shows different feedback: "Already looted — respawning disabled in world settings" when disabled vs "respawns in 30 min" when enabled.

**Fixed — Structures still WAYYYY too frequent:**
5. Ruins density was 0.0085/0.0065/0.0075 (0.85%/0.65%/0.75% per surface voxel = 5-15 ruins per 10 chunks = EVERYWHERE). User requested rarer + easy numbers not 0.005(something) confusing.
6. Now VERY RARE easy numbers: `RUIN_DENSITY_WAREHOUSE = 0.0008f` (0.08% = ~1 per 6-8 chunks), `FACTORY = 0.0005f` (0.05% = very rare, ~1 per 15 chunks), `BUNKER = 0.0006f` (0.06%). Defined as const floats at injection site with comment explaining math: 0.0008 = 0.08% per voxel, lower to 0.0004 for rarer, raise to 0.0015 for more common. Easy to change and look at.
7. Increased scale slightly (1.2-1.6, 1.3-1.8, 1.1-1.5) for premium large look even when rare.

**Files touched:**
- `Scripts/Menu/WorldSession.cs`
- `Scripts/Menu/MainMenuController.cs`
- `Scripts/Exploration/RuinChest.cs`
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (density constants + rarity comment)
- `Scripts/Core/GameVersion.cs` (6.30.3 → 6.31.0)
- `Changelog.md`

---

### [6.30.3-dev] Ruins Setup Crash Fix — PlacedBlock & Path Variable

**Type:** PATCH — editor setup crash fix + premium polish validation.

**Fixed Errors from your log:**
- `CS0103 path does not exist` at `SaveAsPrefabAsset(root, path)` — variable defined as `prefabPath` but used as `path`. Fixed to `prefabPath`.
- `CS0246 PlacedBlock not found` — `PlacedBlock` lives in `VoxelEngine.Building`, file had no using. Fully qualified to `VoxelEngine.Building.PlacedBlock` for all GetComponent/AddComponent calls.
- `CS0103 blockRuinWarehouse does not exist` inside `MakeRuinPrefab` (circular — block defined after prefab) — removed `pb.Item = blockRuinWarehouse` assignments, rubble now has Hp only, main walls use real tiered prefabs if available (mineable for wall tokens).
- `CS0618 FindFirstObjectByType` obsolete in `GridShapeVariantSetup.cs` + `CrestWaterSetupUtility.cs` — replaced with `FindAnyObjectByType`.
- `EnsureLiquidTankPorts` / `EnsureGasTankPorts` unused warnings benign.

**Result:** Step 11 `BuildSurvivalAndLogisticsContent` no longer throws NRE and no longer fails to compile. Premium ruins (walls together, visible chest, rusted steel, rare easy numbers 0.0020/0.0015/0.0018) now generate correctly.

**Files touched:**
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Editor/GridShapeVariantSetup.cs`
- `Scripts/Editor/CrestWaterSetupUtility.cs`
- `Scripts/Core/GameVersion.cs` (6.30.2 → 6.30.3)
- `Changelog.md`

---

### [6.30.2-dev] Premium Ruins Polish — Walls Together, Visible Chest, Rusted Steel & Rare Easy Numbers

**Type:** PATCH — premium visual + spawn tuning, save-compatible.

**User Feedback from image.png:**
- Walls scattered, not together → fixed exact module grid (3.75m), no random pos offset for base foundations, only slight rotation tilt 1.5-3° for ruined feel, not scattered. Foundations now 0.99*module scale, exact grid, all present for solid base.
- Chest invisible but lootable where supposed to be → added visible chest mesh (`ChestMesh` cube with dark metal `0.22,0.22,0.24` + glow light 1.2 intensity 5m range) inside `RuinChest_Visible` GO with BoxCollider 1.2x0.9x0.9, plus beacon light 1.8 intensity 20m range for distance visibility.
- Says steel and iron wall/found but actually wood (pic shows brown with green base) → now uses premium rusted metallic mats: `RustMain 0.66,0.34,0.18 orange rust`, `RustDark 0.42,0.24,0.14`, `RustLight 0.78,0.50,0.28`, not wood brown. Overrode all renderers, 65% rustMain, 20% rustDark, 15% rustLight/moss. No wood tier.
- Ruins spawn EVERYWHERE, ALOT → previously 0.0085 density gave ~5-15 per 10 chunks, too many. Now rarer with easy numbers: `RUIN_DENSITY_WAREHOUSE = 0.0020f`, `FACTORY = 0.0015f`, `BUNKER = 0.0018f` — 0.2%, 0.15%, 0.18% per surface voxel = ~1 ruin per 2-3 chunks, somewhat rare as requested. Easy numbers to tweak (user request: not 0.005(something) confusing).

**Added — Easy Density Tuning:**
- Defined constants at injection site with comment: "PREMIUM RARE — easy numbers to tweak (user request: not 0.005(something) confusing) — 0.002 = 0.2% = ~1 ruin per 500 surface voxels = somewhat rare". To make rarer: lower to 0.001, more common: raise to 0.004.

**Files touched:**
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (premium MakeRuinPrefab rewrite, visible chest, metallic rust, exact grid, rare easy densities)
- `Scripts/Core/GameVersion.cs` (6.30.1 → 6.30.2)
- `Changelog.md`

---

---

### [6.30.1-dev] Ruins Step 11 Crash Fix — PlacedBlock Namespace & Premium Overlap

**Type:** PATCH — editor crash + spawn visibility fix, save-compatible.

**Fixed — `NullReferenceException` at `MakeRuinPrefab:4191` + `PlacedBlock` not found:**
1. `VoxelEngineSetupWindow.cs:4251,4252,4286,4287,4351,4352` CS0246 `PlacedBlock` not found — `PlacedBlock` lives in `VoxelEngine.Building`, file had no using. Fixed by fully qualifying to `VoxelEngine.Building.PlacedBlock` via perl replace.
2. `blockRuinWarehouse` does not exist in `MakeRuinPrefab` scope (created after) — removed `if (blockRuinWarehouse != null) pb.Item = ...` assignments for foundation/wall/rubble fallback cubes. Rubble now has `PlacedBlock` Hp only (120-350) without Item, preventing NRE. Main walls use real tiered prefabs (`Wall_Steel/Iron`) which are already mineable for resources.
3. Root collider for scatter overlap: added large `BoxCollider` size `size*1.1, y*1.2` + `PlacedBlock` marker Hp 500 so `ChunkScatter` overlap check (`Tree/PlacedBlock/PlacedTieredBlock`) prevents ruins spawning inside each other.
4. Increased scatter density 7x: Warehouse 0.0012→0.0085, Factory 0.0009→0.0065, Bunker 0.0010→0.0075, scale 1.2-2.0 (was 0.9-1.3) — now ~5-15 ruins per 10 chunks vs ~1 before. Added biomes Forest/Beach to target list.
5. Fixed obsolete warnings `FindFirstObjectByType` → `FindAnyObjectByType` in `GridShapeVariantSetup.cs` and `CrestWaterSetupUtility.cs`.
6. `EnsureLiquidTankPorts` / `EnsureGasTankPorts` unused warnings are benign (local functions, kept for future use).

**Result:** Step 11 `BuildSurvivalAndLogisticsContent` no longer throws NRE, ruins actually spawn in Wasteland/Plains/Steppes/Desert/Forest/Beach with premium large rusted crusader base look (real building blocks, mineable walls, rubble, beacon light).

**Files touched:**
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Editor/GridShapeVariantSetup.cs`
- `Scripts/Editor/CrestWaterSetupUtility.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.30.0-dev] Premium Ruins — Real Crusader Bases, Mineable Rusted Walls & High Spawn Rate

All release notes are maintained here so `Roadmap.md` remains focused on planned work and execution status.

---

### [6.30.0-dev] Premium Ruins — Real Crusader Bases, Mineable Rusted Walls & High Spawn Rate

**Type:** MINOR — premium exploration upgrade, save-compatible (ruin prefabs + scatter density). No save schema break.

**Problem:** Previous ruins were tiny 4-cube shells (3x2x3) with density 0.0012 — reported as "no warehouses/ruins spawns" and not premium.

**Added — Premium Ruin Generation (real building blocks, mineable for resources):**
1. Rewrote `MakeRuinPrefab` to attempt loading real tiered building blocks from `TieredBlockRegistry` (Wall_Steel/Iron, Foundation_Steel/Iron). If registry exists, uses `Wall_Steel` / `Foundation_Steel` prefabs via `PrefabUtility.InstantiatePrefab` + rusted material override (70% rust, 20% dark rust, 10% moss) — walls are now **real building blocks** with `PlacedTieredBlock` (mineable, drops wall token) giving resources when mined, as requested.
2. Foundation base: 3x3 grid of foundations (9 foundations, 15% missing for rubble) — module 3.75 m, total ~11.25 m square, crusader outpost footprint.
3. Perimeter walls: 4 sides built from wall prefabs (or fallback primitive cubes with `PlacedBlock` + `Hp 250-350`), 18% chance wall missing = collapsed, 20% half-height = ruined, random lean ±3-4° for premium decayed feel.
4. Interior dividing wall + doorway gap for authentic base interior.
5. Rubble/debris piles: 8 small cubes (0.3-0.9 scale) with random rotation, rustDark/moss mats, `BoxCollider`, `PlacedBlock` Hp 120, scattered.
6. Root now has large `BoxCollider` (size.x*1.1, y*1.2, z*1.1, center y*0.4) + `PlacedBlock` marker Hp 500 so scatter overlap check (`Tree/PlacedBlock/PlacedTieredBlock`) prevents ruins spawning inside each other.
7. Point light: warm beacon (1.0,0.72,0.32, 1.4 intensity, 12 m range) on top for visibility at distance — premium exploration cue.
8. `RuinChest` on ROOT (not just child) so any rusted cube hit → `GetComponentInParent<RuinChest>` finds chest reliably. Child chest also gets copy for visual.

**Fixed — No spawns:**
9. Increased scatter density 7x: `Warehouse` 0.0012→0.0085, `Factory` 0.0009→0.0065, `Bunker` 0.0010→0.0075. Scale 0.9-1.3→1.2-1.8 (warehouse), 0.9-1.4→1.3-2.0 (factory), 0.9-1.2→1.1-1.6 (bunker). Now ~5-15 ruins per 10 chunks in target biomes vs ~1 before.
10. Added target biomes: previously Wasteland/Plains/Steppes/Desert, now also Forest/Beach for more exploration.
11. Scatter injection now null-checks prefabs before adding, logs "PREMIUM density 0.006-0.008" and skips duplicate "Ruin_" to avoid stacking.

**Premium Polish:**
12. Rusted materials: `Mat_{name}_RustMain` (base rust), `Mat_{name}_RustDark` (0.65x), `Mat_{name}_Moss` (0.22,0.38,0.22) for overgrown look. Random assignment per wall piece.
13. Random tilt ±3-5° on foundations/walls for collapsed, long-abandoned feel.
14. Walls are real building blocks (if registry exists) → mineable for resources (wall tokens → steel plates etc), as requested. Fallback cubes with `PlacedBlock` Hp 120-350 also mineable for fallback.

**Files touched:**
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (premium ruin generator + density + root collider + PlacedBlock marker)
- `Scripts/Core/GameVersion.cs` (6.29.2 → 6.30.0)
- `Changelog.md`

---

### [6.29.2-dev] Ruins Step 11 NullRef Fix — Robust Prefab Creation

All release notes are maintained here so `Roadmap.md` remains focused on planned work and execution status.

---

### [6.29.2-dev] Ruins Step 11 NullRef Fix — Robust Prefab Creation

**Type:** PATCH — editor setup crash fix, save-compatible.

**Fixed — NullReferenceException in Step 11 `MakeRuinPrefab`:**
1. `root.AddComponent<RuinChest>()` could return null if RequireComponent fails or script not compiled — now ensures root has BoxCollider before AddComponent, checks for null after AddComponent, logs error and returns null prefab if still fails.
2. `MakeColoredMat` could return null — now null-checked before assigning to renderer.
3. `steelPlate`, `ironPlate`, `copperWire`, `circuit`, `coal`, `bpNacelle` etc captured from outer scope may be null if Step 10 not run — now builds component/Fuel/Blueprint lists via null-coalesced collection (only non-null items added, fallback to `ironIngot` if all plates missing).
4. `SaveAsPrefabAsset` could return null if path invalid — returns null prefab handled.
5. Ruin block creation `MakeBlk` now guarded: only creates block if prefab non-null.
6. Biome scatter injection now checks `ruinWarehouse/Fac/ Bunker != null` before adding entries, avoids adding null prefabs to scatter.
7. Added `UnityEngine.Random.Range` fully qualified to avoid ambiguity with `System.Random`.

**Result:** Step 11 `BuildSurvivalAndLogisticsContent` no longer throws NRE at `rootChest.ruinName = ...` (line 4191). If plates missing, ruins still spawn with fallback loot.

**Files touched:**
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.29.1-dev] RuinChest Inventory Namespace Fix

**Type:** PATCH — compile fix, save-compatible.

**Fixed:**
- `RuinChest.cs(46)` CS0234 `VoxelEngine.Player.Inventory` does not exist — `Inventory` lives in `VoxelEngine.Items`. Changed `TryOpen(VoxelEngine.Player.Inventory)` → `TryOpen(Inventory)` (with `using VoxelEngine.Items` already present). Interaction via `PlayerInteractionTool` now correctly passes `Items.Inventory`.

**Files touched:**
- `Scripts/Exploration/RuinChest.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.29.0-dev] Ruins of Dead Civilization & Blueprint Data Cores (4.9.0)


---

### [6.28.1-dev] Compile Fixes — Obsolete API & Offline Service Wiring

**Type:** PATCH — build fix, no gameplay change. Save-compatible.

**Fixed — compile errors:**
1. `WorldStatePersistence.cs(144)` CS0103 `OfflineSurvivalService` not found — added fully qualified `VoxelEngine.Player.OfflineSurvivalService` for both EnsureInstance and Instance checks.
2. `PlayerSpawner.cs(63)` CS1626 Cannot yield inside try with catch — moved `yield return null` (grid restore frame) outside try block. Offline check now runs after extra frame, try only wraps the actual consume logic.
3. `GridEntity.cs` CS0219 `hasLandingGear` assigned never used — removed unused variable, keeping `anyLocked`/`anyGrounded` checks.

**Fixed — obsolete API warnings (CS0618):**
4. Replaced all `FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)` with `FindObjectsByType<T>(FindObjectsInactive.Exclude)` in:
   - `DeathScreenHud.cs` (8 occurrences)
   - `Building/Cryobed.cs` (5 occurrences)
   - `Player/OfflineSurvivalService.cs` (8 occurrences)
5. `VoxelCrestBlockFoamEmitter.cs(83)` CS0652 sbyte density <200 outside range — changed to `<100` (sbyte max 127) with comment.

**Files touched:**
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Player/PlayerSpawner.cs`
- `Scripts/GridSystem/GridEntity.cs`
- `Scripts/Building/Cryobed.cs`
- `Scripts/UI/DeathScreenHud.cs`
- `Scripts/Player/OfflineSurvivalService.cs`
- `Scripts/WaterSim/VoxelCrestBlockFoamEmitter.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.28.0-dev] Offline Survival & Room Oxygen (11.4 Completion)


---

### [6.27.3-dev] Grid Gravity & Planet Surface Alignment Fix


---

### [6.27.2-dev] Pipe-Only Water for Biofarm, Diagonal/Vertical Pipe Fixes & Ghost Port Commit


---

### [6.27.1-dev] Grid Biofarm UI, Variable Ports & Vertical Pipe Fix

**Type:** PATCH — biofarm UX + pipe connectivity fixes. Save-compatible.

**Fixed — grid biofarm had no UI:**
1. `PlayerInteractionTool.GridBlockHasUI` now includes `GridBiofarm` and `GridCryobed` so RMB opens the premium panel instead of doing nothing.
2. `GameUIController` already watched `biomassInput` and `GridBlockUI` had `BiofarmPanel`, but the block was blocked from ever opening — now fixed.
3. Grid cryobed was also missing from `GridBlockHasUI`, so its terminal panel wasn't reachable when not via `CryobedConfigHud` — now included for consistency.

**Added — variable ports on all biofarms (like grid tanks):**
4. `BuildSystem.TryGetGridTankVariablePortSnap` and `GridBuilder.TryGetGridTankVariablePortSnap` / `IsMatchingTankBlockForPipe` now accept `GridBiofarm` (and `GridH2O2Generator`) for both `Liquid` and `Gas` families.
5. Placing a liquid pipe (water) or gas pipe (O₂) while aiming at a biofarm hull spawns a colored variable port (`Port_LiquidIO_V` blue, `Port_GasIO_V` cyan) flush on the hull, and the pipe snaps to the detail lattice just outside it — same UX as HFO/MGO engines and grid gas/liquid tanks.
6. Added `GridTankVariablePorts` component auto-creation on first variable port install — persists via save/load.
7. `VoxelEngineSetupWindow` Step 12 now calls `EnsureGridTankPorts` for `Biofarm_Large` for both gas and liquid, so fixed N/S/E/W/Top/Bottom ports also exist as fallback.
8. **Static biofarm** (world) now participates in pipe visuals: `WaterPipe` and `GasPipe` `GetNeighbourPositions` now treat `Building.Biofarm` as endpoint for both grid-attached and free-placed pipes, so arms grow toward the biofarm model instead of stopping short.

**Fixed — pipes cannot connect vertically on land:**
9. `PipeAdjacency` vertical tolerance increased: `VerticalTolerance = 0.65f`. `IsCardinalNeighbour`, `IsCardinalLinkDelta`, and `IsAxisAlignedWithinDelta` now use larger tolerance when dominant axis is Y, allowing 0.4-0.5 m sideways drift from uneven terrain hit normals that previously broke vertical shafts.
10. `BuildSystem.IsPlacementValid` now allows thin stacking blocks (pipes/cables, `allowStacking = true`) to ignore static world geometry (terrain). Previously any overlap with static MeshCollider blocked placement, so starting a vertical column on rough ground failed. Dynamic rigidbody blocking still enforced.

**Files touched:**
- `Scripts/Player/PlayerInteractionTool.cs`
- `Scripts/Building/BuildSystem.cs`
- `Scripts/GridSystem/GridBuilder.cs`
- `Scripts/Fluids/WaterPipe.cs`
- `Scripts/Gas/GasPipe.cs`
- `Scripts/Networks/PipeAdjacency.cs`
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.27.0-dev] Passive Oxygen Generation — Biofarm Oxygen Garden (11.5)


---

### [6.26.6-dev] Cryobed UI Live Stats, Oxygen Tank Visual & Spawn Fixes


---

### [6.26.5-dev] Cryobed Oxygen Piping, Pipe Arms & Respawn Stability

**Type:** PATCH — cryobed life-support connectivity + respawn/world-stability fixes. Save-compatible (no save schema or public API changes).

**Fixed — Cryobed oxygen piping (the "no piped oxygen" bug):**
1. Grid Cryobeds now actively pull oxygen from connected oxygen tanks through the grid gas-pipe network (like any other gas consumer). Previously oxygen only spilled into a bed *after* every tank on the network was full, so a bed wired to a tank stayed empty forever and always reported "No piped oxygen in cryobed buffer".
2. Added a tunable `oxygenIntakePerSecond` field on `GridCryobed` (default 6 L/s) controlling the fill rate. Existing beds keep their stored oxygen; the new field simply defaults in.
3. The producer-overflow path (H2/O2 generator → bed with no tank) is preserved unchanged, so both direct and tank-buffered topologies now fill the bed.

**Fixed — gas pipe visual arms to Cryobeds:**
4. Gas pipes now draw their chunky brass connection arm to a Grid Cryobed's installed gas port. `GridCryobed` was missing from the gas-pipe endpoint list, so pipes never visually connected to the bed even though the placement ghost showed the port.
5. Cryobed arms aim at the real installed gas port and are suppressed until a port exists — no more arms burying into the centre of the large bed model.

**Fixed — respawn landing far from the chosen point:**
6. Death/bed respawn now parks the player at the true destination height on spherical worlds. The routine was still forcing the flat-world "park at Y≥250" trick on spheres, which parked the streaming viewer far below the surface spawn — chunks around the cryobed never streamed in, the wait timed out, and the player was dropped far from the selected respawn point. Now mirrors the first-spawn routine.

**Fixed — `MissingReferenceException` (destroyed `CelestialBody`) breaking the world:**
7. `ActiveWorld.Current` no longer hands back a destroyed world. The static pointer could hold a torn-down `SphereWorld` behind an interface reference (the C# `??` operator only tests reference-null, not Unity's overloaded destroyed-null), so per-frame callers like the dropped-item ice probe hit the dead `CelestialBody` and spammed `MissingReferenceException`, breaking chunk streaming. The getter now re-validates the backing Unity object and returns null when it is gone.
8. Both `SphereWorld` and the flat `VoxelWorld` now clear the static `ActiveWorld` pointer in `OnDestroy`.
9. `SphereWorld.WorldToVoxel` / `WorldToChunk` guard against a null/destroyed body and return safe defaults as a final safety net.

**Files touched:**
- `Scripts/Gas/GasPipe.cs`
- `Scripts/GridSystem/GridCryobed.cs`
- `Scripts/Player/PlayerSpawner.cs`
- `Scripts/Core/IVoxelWorld.cs`
- `Scripts/Core/VoxelWorld.cs`
- `Scripts/Cosmos/SphereWorld.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.26.4-dev] Cryobed Claiming, Naming & Oxygen Connectivity Fixes

**Type:** PATCH — cryobed ownership/UI/connectivity corrections. Save-compatible.

**Fixed — claiming and respawn behavior:**
1. Cryobeds can now be claimed even when they currently lack power or oxygen. Availability still controls whether they appear as usable death-screen respawn targets.
2. Claiming one cryobed no longer removes ownership from other cryobeds; ownership is tracked per cryobed.
3. Death screen no longer masks a named cryobed behind the generic `Linked Spawn` entry when the linked spawn points at a live bed/cryobed.
4. Cryobed names and ownership now persist for static and grid cryobeds. Grid cryobed internal oxygen also persists.

**Fixed — Cryobed Control UI:**
5. Number keys no longer trigger hotbar selection while the Cryobed name field is active, so typing numeric names no longer closes/soft-locks the UI.
6. Cryobed UI still closes with Escape.
7. The claim button remains usable while offline/unpowered/unoxygenated; unavailable beds simply cannot be used as respawn points until made available.

**Fixed — oxygen must be piped into Grid Cryobeds:**
8. Grid Cryobed availability now depends on its own internal oxygen buffer, not just generic grid oxygen storage.
9. Oxygen producers only fill cryobeds through the connected gas-pipe network and the cryobed's variable gas port, so players must pipe oxygen into the cryobed.

**Files touched:**
- `Scripts/Building/Cryobed.cs`
- `Scripts/GridSystem/GridCryobed.cs`
- `Scripts/GridSystem/GridGasNetwork.cs`
- `Scripts/UI/CryobedConfigHud.cs`
- `Scripts/UI/DeathScreenHud.cs`
- `Scripts/UI/GameUIController.cs`
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.26.3-dev] Cryobed Ownership, Terminal & Oxygen Port Polish

**Type:** PATCH — cryobed behavior/UI/connectivity fixes plus roadmap update. Save-compatible.

**Fixed — cryobed ownership and respawn list:**
1. Claiming a cryobed now marks that cryobed owned without clearing ownership on other cryobeds.
2. Death screen lists claimed and currently available cryobeds, so multiple owned cryobeds can appear.
3. The active linked spawn is hidden when it points at an unavailable cryobed, preventing unpowered Grid Cryobeds from remaining spawnable through the fallback linked-spawn entry.
4. Claim button is disabled when the cryobed is unavailable, and the button row wraps with shorter labels so Close stays inside the panel.
5. Escape now closes the Cryobed Control UI.
6. Holding a placeable block no longer opens the Cryobed UI; block placement keeps priority.

**Improved — Grid Cryobed terminal and oxygen:**
7. Grid Cryobed now has a dedicated Ship Control / grid block panel with status, power estimate, oxygen estimate, rename, claim/remove ownership, and transfer placeholder controls.
8. Gas pipes can now install variable gas ports on Grid Cryobeds, matching tank-style variable input.
9. Grid Cryobeds have an internal oxygen buffer and can receive oxygen from connected gas pipe infrastructure; oxygen/power estimates now show reserve time.

**Roadmap:**
10. Added `11.5 Passive Oxygen Generation` for an expensive passive Biofarm / oxygen garden system that can feed tanks, cryobeds, and future airtight spaces.

**Files touched:**
- `Scripts/Building/Cryobed.cs`
- `Scripts/GridSystem/GridCryobed.cs`
- `Scripts/GridSystem/GridBuilder.cs`
- `Scripts/GridSystem/GridGasNetwork.cs`
- `Scripts/GridSystem/UI/GridBlockUI.cs`
- `Scripts/Building/BuildSystem.cs`
- `Scripts/UI/CryobedConfigHud.cs`
- `Scripts/UI/DeathScreenHud.cs`
- `Scripts/UI/GameUIController.cs`
- `Scripts/Player/PlayerInteractionTool.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.26.2-dev] Cryobed Control UI & Ownership Tools

**Type:** PATCH — cryobed UX and respawn selection polish. Save-compatible.

**Added — cryobed UI:**
1. Added `CryobedConfigHud`, a premium control panel opened by right-clicking static or grid cryobeds.
2. The panel shows current availability, power status/estimate, oxygen status/estimate, and ownership state.
3. Cryobeds can now be renamed from the panel so respawn choices are easier to identify.
4. Added buttons for Claim, Remove Ownership, and Transfer placeholder for future multiplayer ownership handoff.
5. Already-claimed cryobeds now show `Already claimed by you` instead of blindly claiming again.

**Improved — death screen respawn list:**
6. Death screen uses cryobed names and availability text from the cryobed components.
7. Grid cryobeds use their `SpawnPoint` and filtered availability directly.

**Files touched:**
- `Scripts/UI/CryobedConfigHud.cs`
- `Scripts/UI/GameUIController.cs`
- `Scripts/UI/DeathScreenHud.cs`
- `Scripts/Building/Cryobed.cs`
- `Scripts/GridSystem/GridCryobed.cs`
- `Scripts/Player/PlayerInteractionTool.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.26.1-dev] Cryobed Availability Validation

**Type:** PATCH — respawn-choice availability polish. Save-compatible.

**Improved:**
1. Static `Cryobed` now exposes power/oxygen availability state and Step 11 adds a small `PowerConsumer` draw to the generated prefab.
2. `GridCryobed` now reports ONLINE / NO POWER / NO OXYGEN based on grid power and stored oxygen.
3. Death screen only lists cryobeds that are currently available, while World Spawn and the active linked spawn remain available fallback choices.

**Files touched:**
- `Scripts/Building/Cryobed.cs`
- `Scripts/GridSystem/GridCryobed.cs`
- `Scripts/UI/DeathScreenHud.cs`
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.26.0-dev] Death Screen & Respawn Selection

**Type:** MINOR — new save-compatible death/respawn UI flow. No save schema change.

**Added — premium death screen:**
1. Added `DeathScreenHud`, a full-screen `CRUSADER DOWN` overlay with a dark premium panel, red accent border, and respawn choice buttons.
2. Player death no longer instantly respawns. `PlayerStats.Die()` now opens the death screen and blocks gameplay input until a respawn option is selected.
3. Respawn choices include World Spawn, the active linked spawn, live Beds, static Cryobeds, and Grid Cryobeds.
4. Selecting a respawn choice restores player health/stamina/hunger/oxygen and sends the player through `PlayerSpawner.RespawnAt()` so the same safe chunk/grounding flow is used.

**Files touched:**
- `Scripts/UI/DeathScreenHud.cs`
- `Scripts/UI/GameUIController.cs`
- `Scripts/Player/PlayerStats.cs`
- `Scripts/Player/PlayerSpawner.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.25.0-dev] Player Fall Damage Foundation

**Type:** MINOR — new save-compatible player hazard behavior. No save schema change.

**Added — fall damage:**
1. `PlayerController` now tracks the player's downward impact speed along local gravity, so fall damage works on flat worlds and spherical/radial planets.
2. Landing above the safe threshold applies scaled HP damage through `PlayerStats.TakeDamage()`.
3. Water/swimming landings are ignored so underwater movement does not punish the player.
4. Added tuning fields for start speed, lethal speed, and curve exponent.
5. A small `Hard Landing` feedback toast reports damage and impact speed.

**Roadmap:**
6. Marked the current Fall Damage scope as **COMPLETED**. Armor upgrade mitigation remains part of the later armor-upgrade work.

**Files touched:**
- `Scripts/Player/PlayerController.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.24.4-dev] Equipment Row Left/Right Alignment Fix

**Type:** PATCH — inventory equipment layout correction. Save-compatible.

**Fixed:**
1. Jetpack Bay now anchors on the left side of the inventory equipment row.
2. Life Support now anchors on the right side of the same row.
3. Removed the compact/together layout from 6.24.3-dev that made both sections cluster on the left.

**Files touched:**
- `Scripts/UI/GameUIController.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.24.3-dev] Compact Equipment Row Alignment

**Type:** PATCH — inventory equipment layout polish. Save-compatible.

**Improved:**
1. Removed the 50% flex-basis layout from Jetpack Bay and Life Support so both sections are compact and sit together on the left side of the inventory panel.
2. Life Support now sits immediately to the right of Jetpack Bay with a small margin, removing the awkward empty space on the right side of the Life Support area.

**Files touched:**
- `Scripts/UI/GameUIController.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.24.2-dev] Equipment UI Compile Fix

**Type:** PATCH — Unity UI Toolkit compatibility fix. Save-compatible.

**Fixed:**
1. Removed unsupported `IStyle.columnGap` usage from the compact equipment row.
2. Replaced it with `marginLeft` on the Life Support panel so older/current Unity UI Toolkit versions compile cleanly.

**Files touched:**
- `Scripts/UI/GameUIController.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.24.1-dev] Compact Equipment Row & Oxygen Drain Activation

**Type:** PATCH — equipment UI layout and oxygen behavior polish. Save-compatible.

**Improved — equipment UI uses less inventory space:**
1. Jetpack Bay and Life Support now render side-by-side in one compact equipment row.
2. Removed the extra hint text under both equipment sections to keep more vertical room for the backpack grid.
3. The Life Support status remains next to its title, matching the cleaned-up Jetpack Bay style.

**Fixed — oxygen now drains underwater:**
4. `PlayerStats` now drains oxygen when the player's head is underwater or when swimming at high water depth.
5. Equipped Space Helmet + Oxygen Tank still increase max oxygen and reduce drain as intended.

**Files touched:**
- `Scripts/UI/GameUIController.cs`
- `Scripts/Player/PlayerStats.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.24.0-dev] Life Support Equipment Foundation

**Type:** MINOR — new save-compatible equipment items and oxygen behavior foundation. No save schema break.

**Added — helmet + oxygen tank equipment:**
1. Added `SpaceHelmetItem` and `OxygenTankItem` data assets.
2. `PlayerEquipment` now has a sealed helmet slot and oxygen tank slot in addition to the two jetpack slots.
3. Inventory now shows a lightweight `LIFE SUPPORT` section with helmet/tank slots and SEALED/OPEN status.
4. Shift-clicking a Space Helmet or Oxygen Tank from hotbar/backpack equips it into the matching slot; shift-clicking the equipment slot returns it to inventory.
5. Helmet/tank slots persist through save/load with additive player save fields.

**Added — oxygen behavior foundation:**
6. `PlayerStats` now uses equipped helmet+tank to extend max oxygen and reduce underwater drain.
7. Fixed the old oxygen/hunger reset inside `PlayerStats.Update()` so oxygen and hunger state can actually change over time.

**Setup:**
8. Step 11 now creates Space Helmet and Oxygen Tank items/recipes non-destructively.

**Roadmap:**
9. Updated `11.4 Cryobeds, Offline Survival & Oxygen` with life-support equipment progress. Offline death/environment validation still remains open.

**Manual Unity step:**
- Run **Tools → Voxel Engine → Voxel Engine Setup → 11. Build Survival + Industrial Logistics Content** once to create/connect the Space Helmet and Oxygen Tank recipes.

**Files touched:**
- `Scripts/Items/SpaceHelmetItem.cs`
- `Scripts/Items/OxygenTankItem.cs`
- `Scripts/Player/PlayerEquipment.cs`
- `Scripts/Player/PlayerStats.cs`
- `Scripts/UI/GameUIController.cs`
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.23.0-dev] Cryobed Offline-Survival Foundation

**Type:** MINOR — new save-compatible life-support/offline-survival foundation content. No save schema break.

**Added — static and grid cryobeds:**
1. Added `Cryobed` for static bases and `GridCryobed` for ships/stations.
2. Right-clicking either cryobed claims it as the player's respawn/offline-survival anchor using the existing spawn sidecar path.
3. Grid cryobeds expose basic `IGridDataProvider` status text for future screens/terminals.
4. Step 11 now creates the static Cryobed prefab, BlockItem, and recipe non-destructively.
5. Step 12 now creates the Grid Cryobed prefab, GridBlockItem, and recipe non-destructively.

**Roadmap:**
6. Marked `11.4 Cryobeds, Offline Survival & Oxygen` as **WORKING ON**. Cryobed anchors exist; oxygen-rich environment checks, offline death logic, and full life-support integration remain open.

**Manual Unity steps:**
- Run **Tools → Voxel Engine → Voxel Engine Setup → 11. Build Survival + Industrial Logistics Content** for the static Cryobed.
- Run **Tools → Voxel Engine → Voxel Engine Setup → 12. Build Grid System Content** for the Grid Cryobed.

**Files touched:**
- `Scripts/Building/Cryobed.cs`
- `Scripts/GridSystem/GridCryobed.cs`
- `Scripts/Player/PlayerInteractionTool.cs`
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.22.5-dev] Spawn Clearance Final Lift Hotfix

**Type:** PATCH — spawn clearance correction. Save-compatible.

**Fixed:**
1. Increased player spawn clearance to keep the CharacterController fully above terrain.
2. Added a final terrain-lift pass immediately before enabling player control so late-updated chunk meshes cannot leave the player slightly embedded.

**Files touched:**
- `Scripts/Player/PlayerSpawner.cs`

---

### [6.22.4-dev] Safe Player Spawn Lift & Own-Player Raycast Filter

**Type:** PATCH — spawn safety and crosshair filtering fixes. Save-compatible.

**Fixed — player no longer spawns stuck in the ground:**
1. `PlayerSpawner` now uses a larger ground clearance when placing fresh/bed spawns on terrain.
2. Saved near-surface positions are lifted out of the voxel surface after the target chunk loads, preventing older slightly-buried saves from enabling the CharacterController inside the ground.
3. The setup wizard now explicitly tags newly spawned player roots as `Player`.

**Fixed — crosshair/raycast tools no longer hit the local player:**
4. Added `PlayerRaycastFilter`, a shared helper that ignores only the local player's own colliders while keeping the design open for future multiplayer targeting of other players.
5. Applied the filter to mining/interaction, build ghosts, grid builder, tiered building, and the world inspection HUD.
6. Looking down should no longer target your own body/player collider.

**Files touched:**
- `Scripts/Player/PlayerRaycastFilter.cs`
- `Scripts/Player/PlayerSpawner.cs`
- `Scripts/Player/PlayerInteractionTool.cs`
- `Scripts/Building/BuildSystem.cs`
- `Scripts/Building/Tiered/BuildSystemV2.cs`
- `Scripts/GridSystem/GridBuilder.cs`
- `Scripts/UI/WorldInspectionHud.cs`
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.22.3-dev] Jetpack Bay Live Update & Boost Spool Polish

**Type:** PATCH — jetpack equipment UX and movement feel polish. Save-compatible.

**Fixed — jetpack slot UI live updates on drag/drop:**
1. Drag/drop swaps involving jetpack slots now call `Refresh()` after the drag completes, so removing a pack updates the Jetpack Bay ONLINE/EMPTY state immediately.
2. Shift-click removal already routes back to inventory; this pass keeps the equipment display synchronized after direct dragging too.

**Improved — calmer Jetpack Bay styling:**
3. Removed the boxed panel background/border around the Jetpack Bay section.
4. Moved the ONLINE/EMPTY status pill directly beside `JETPACK BAY` on the left, making the inventory section cleaner and less flashy.
5. Jetpack slots still keep the existing readable slot visuals but no longer appear inside a heavy framed box.

**Improved — Hydrogen Boost Pack feel:**
6. Hydrogen boost packs now spool boost over a very short charge-up instead of applying full boost instantly. The ramp is intentionally subtle so controls remain responsive.

**Files touched:**
- `Scripts/UI/GameUIController.cs`
- `Scripts/Player/PlayerController.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.22.2-dev] Jetpack Slot QoL & Inventory Polish

**Type:** PATCH — jetpack equipment UI/interaction polish. Save-compatible.

**Fixed — flight state updates immediately when jetpacks are removed:**
1. `PlayerController` now continuously validates flight permission while fly mode is active.
2. If the player has no research unlock and removes the last usable jetpack, fly mode shuts off immediately with a small feedback toast instead of staying active until both slots were manipulated/rechecked.

**Improved — shift-click jetpack routing:**
3. Shift-clicking a `JetpackItem` from hotbar or backpack now equips one into the first free jetpack slot before machine/storage routing.
4. Shift-clicking a jetpack equipment slot sends it back to normal inventory.

**Improved — jetpack inventory presentation:**
5. Removed the Sort button from the jetpack slot section.
6. Replaced the plain slot row with a styled `JETPACK BAY` panel showing ONLINE/EMPTY state, accent border, and a concise hint.

**Files touched:**
- `Scripts/Player/PlayerController.cs`
- `Scripts/UI/GameUIController.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.22.1-dev] Jetpack Slot Persistence & Inventory UI

**Type:** PATCH — jetpack equipment usability/persistence pass. Save-compatible; additive player save field only.

**Added — jetpack equipment persists:**
1. `WorldStatePersistence` now saves/restores the two `PlayerEquipment` jetpack slots using an additive `SavedPlayer.jetpackSlots` field.
2. Legacy saves leave the field null and restore with empty jetpack slots.

**Added — visible jetpack slots in inventory:**
3. The inventory panel now shows a `Jetpack Slots` section above the backpack grid when `PlayerEquipment` exists.
4. Slots use the existing drag/drop slot UI and the equipment container filter only accepts `JetpackItem` assets.

**Files touched:**
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/UI/GameUIController.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.22.0-dev] Jetpack Families Equipment Foundation

**Type:** MINOR — new save-compatible player equipment foundation and jetpack item family. No save schema break.

**Added — two jetpack equipment slots foundation:**
1. Added `PlayerEquipment` with two dedicated jetpack slots and a quick-equip path from the active hotbar stack.
2. `PlayerController` now ensures `PlayerEquipment` exists and allows fly mode when either research unlocks flight or a usable jetpack is equipped.
3. Fly speed and sprint/boost speed can now be modified by the equipped jetpack.

**Added — data-driven jetpack item families:**
4. Added `JetpackItem` and `JetpackFamily` with Hydrogen Boost, Atmospheric, and Hybrid definitions.
5. Step 12 now creates three non-destructive jetpack item assets and recipes:
   - Hydrogen Boost Pack.
   - Atmospheric Jetpack.
   - Hybrid Jetpack.
6. Existing balance values are preserved by the setup workflow where possible; this pass adds the equipment/data foundation while later passes can add full UI, hydrogen/power fuel accounting, persistence, and armor-station upgrade integration.

**Roadmap:**
7. Marked `11.3 Jetpack Families` as **WORKING ON**.

**Manual Unity step:**
- Run **Tools → Voxel Engine → Voxel Engine Setup → 12. Build Grid System Content** once to create/connect the jetpack items and recipes.

**Files touched:**
- `Scripts/Items/JetpackItem.cs`
- `Scripts/Player/PlayerEquipment.cs`
- `Scripts/Player/PlayerController.cs`
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.21.2-dev] Ice Grid Gravity Recovery Fix

**Type:** PATCH — ice-friction physics fix for movable grids. Save-compatible; no save schema change.

**Fixed — tilted grids no longer drift upward after sliding off ice:**
1. `GridEntity` now keeps a short ice-contact grace timer after any block touches Ice.
2. During that grace window, hover-hold dampeners cannot cancel gravity. This prevents thruster authority from making a recently-tilted grid hang in the air after losing ice contact.
3. Dampener braking during ice recovery now only brakes tangent drift, not gravity-axis velocity, so the grid can fall back toward the planet naturally.
4. Added a small temporary gravity multiplier during ice recovery for uncontrolled grids to make re-contact with the surface feel reliable.

**Files touched:**
- `Scripts/GridSystem/GridEntity.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.21.1-dev] Ice Friction Rigidbody Completion

**Type:** PATCH — completes the ice friction pass for physical drops and movable grids. Save-compatible; no save schema change.

**Added — physical drops slide on ice:**
1. `DroppedItem` now uses `IceFrictionUtility` while its Rigidbody is active.
2. Drops on Ice voxels use low linear/angular damping and wait longer before settling, so tossed items can slide naturally instead of instantly stopping.

**Added — movable grids skid more on ice:**
3. `GridEntity` samples its placed blocks for Ice contact during physics ticks.
4. When touching Ice, grid dampener braking and angular braking are reduced, and angular damping is lowered so landed ships/rovers feel less glued to the surface.

**Roadmap:**
5. Marked `11.2 Ice Friction` as **COMPLETED** for current scope: player, physical drops/static loose item bodies, and movable grids now have ice-specific low-friction behavior.

**Files touched:**
- `Scripts/Items/DroppedItem.cs`
- `Scripts/GridSystem/GridEntity.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.21.0-dev] Ice Friction Player Movement First Pass

**Type:** MINOR — new movement/hazard surface behavior. Save-compatible; no save schema change.

**Added — player ice friction:**
1. Added `IceFrictionUtility`, a shared active-world voxel sampler that detects solid `MaterialId.Ice` below a world position on both flat worlds and spherical planets.
2. `PlayerController` now detects when the grounded player is standing on Ice voxels.
3. Walking on ice uses much lower friction, reduced steering/braking acceleration, and lower slide friction so momentum carries naturally instead of stopping instantly.
4. The implementation is intentionally data-light and non-destructive; no prefab or material balance values are overwritten.

**Roadmap:**
5. Marked `11.2 Ice Friction` as **WORKING ON** because player ice movement is implemented, while static loose blocks and movable Grids still need their rigidbody/physics pass before the section can be completed.

**Files touched:**
- `Scripts/Environment/IceFrictionUtility.cs`
- `Scripts/Player/PlayerController.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`
- `Roadmap.md`

---

### [6.20.2-dev] Strict Cardinal Pipe/Cable Links & Single-Use Variable Ports

**Type:** PATCH — connection cleanup and variable-port behavior fix. Save-compatible.

**Fixed — pipes no longer visually/network-connect diagonally:**
1. `GridGasNetwork` and `GridLiquidNetwork` now filter proximity-discovered pipe neighbours through a strict Detail-lattice cardinal test before BFS links them.
2. `GasPipe` and `WaterPipe` visual providers now refuse connected-pipe arms unless the target pipe is cardinal on the Detail lattice. This removes the “one left and one up” diagonal visual bridge shown in the screenshot.
3. Pipe↔pipe tolerance for these grid visual/proximity links is tightened to `0.12 × DetailCell`, so vertical/side offsets cannot sneak through as valid cardinal links.

**Improved — power/data cable cardinal behavior:**
4. `PowerCable` now performs a grid-local strict cardinal check before allowing cable↔cable links, instead of relying on the looser radial-world distance fallback.
5. `DataCable` cardinal checks now use grid-local Detail-lattice math when placed on a grid, improving cable arm consistency on constructs.

**Changed — variable ports are single-use sockets:**
6. Existing dynamic variable ports (`*_V`) are no longer valid snap targets for another pipe.
7. Maritime engine variable ports now report as full instead of reusing the same dynamic port.
8. Tank variable ports already block through occupied Detail cells; the shared port-snap path now also rejects direct snapping to an existing `*_V` port with feedback.

**Files touched:**
- `Scripts/Building/BuildSystem.cs`
- `Scripts/GridSystem/GridBuilder.cs`
- `Scripts/GridSystem/GridGasNetwork.cs`
- `Scripts/GridSystem/GridLiquidNetwork.cs`
- `Scripts/Gas/GasPipe.cs`
- `Scripts/Fluids/WaterPipe.cs`
- `Scripts/Power/PowerCable.cs`
- `Scripts/Networks/DataCable.cs`
- `Scripts/Maritime/MaritimeVariablePorts.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.20.1-dev] Tank Variable Ports in BuildSystem & Strict Pipe Lattice Links

**Type:** PATCH — fixes variable tank port placement path and pipe diagonal false-links. Save-compatible.

**Fixed — variable tank ports now use the same placement path as engine ports:**
1. `BuildSystem` now handles variable gas/liquid tank ports when placing the normal static/world pipe items onto a grid. This matches the maritime engine behavior path that already worked.
2. `GridBuilder` support remains in place for grid-held pipe items, but the normal held gas/liquid pipe path now also previews/commits `Port_GasIO_V` / `Port_LiquidIO_V` on tank hulls.
3. Preview ring color is tank-family aware: liquid blue, gas sky-blue.

**Fixed — pipes no longer link diagonally one cell over + one cell up:**
4. `GasNetwork`, `FluidNetworkManager`, and `ItemPipeNetwork` now force all grid pipe↔pipe links to use the Detail lattice step, even if a legacy prefab forgot to mark itself as a precision attachment.
5. This removes the loose structural-grid tolerance that allowed diagonal links like “one left and one up” to pass as a cardinal pipe connection.

**Files touched:**
- `Scripts/Building/BuildSystem.cs`
- `Scripts/GridSystem/GridBuilder.cs`
- `Scripts/Gas/GasNetwork.cs`
- `Scripts/Fluids/FluidNetworkManager.cs`
- `Scripts/Transport/ItemPipeNetwork.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.20.0-dev] Variable Tank Ports for Grid Gas/Liquid Tanks

**Type:** MINOR — new save-compatible variable port feature for grid tanks. Additive save data only; existing saves remain compatible.

**Added — tank ports now work like maritime engine ports:**
1. New `GridTankVariablePorts` component stores player-installed variable ports on grid gas/liquid tanks.
2. Holding a matching pipe and aiming at a grid tank hull now previews a colored port ring exactly where the port will be installed:
   - Blue = liquid tank port.
   - Sky-blue = gas tank port.
3. Pipe placement snaps to the same Detail lattice cell used by the port preview, so ghost and placed pipe match.
4. Clicking installs a dynamic child port (`Port_LiquidIO_V` / `Port_GasIO_V`) on the actual tank block and seats the pipe outside the hull.
5. Network and visual systems already discover these variable ports through the existing port-prefix rules, so gas/liquid tank links use the new variable port instead of relying on fixed prefab markers.

**Persistence:**
6. Variable tank ports are saved/restored using the existing additive variable-port payload path. Legacy saves without tank ports still load normally.

**Files touched:**
- `Scripts/GridSystem/GridTankVariablePorts.cs`
- `Scripts/GridSystem/GridBuilder.cs`
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.19.6-dev] Prefab-Safe Grid Tank Port Repair

**Type:** PATCH — editor/setup bug fix. Save-compatible; no gameplay balance or schema changes.

**Fixed — tank ports no longer get created as loose scene objects:**
1. Step 12 `EnsureGridTankPorts()` now opens the actual tank prefab through `PrefabUtility.LoadPrefabContents()`, edits the prefab contents, saves with `PrefabUtility.SaveAsPrefabAsset()`, then unloads the prefab contents.
2. This prevents `Port_GasIO_*` / `Port_LiquidIO_*` primitives from being spawned into the open scene hierarchy instead of becoming children of the prefab asset.
3. Step 13 no longer runs its older direct primitive port creation for grid tanks. Grid tank ports are now owned by Step 12 only, where the grid tank prefabs are generated.
4. Existing prefab port transforms/facing tags are still repaired non-destructively; tank capacity, recipes, power, mass, and other tuning values are untouched.

**Manual Unity cleanup:**
- Delete any loose `Port_GasIO_*` / `Port_LiquidIO_*` objects that were accidentally added to the active scene by the previous setup run.
- Then run **Tools → Voxel Engine → Voxel Engine Setup → 12. Build Grid System Content** once.

**Files touched:**
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.19.5-dev] Step 12 Tank Ports & No-Fake Pipe Caps

**Type:** PATCH — non-destructive setup repair plus visual clarity for pipe endpoints. Save-compatible; no schema change.

**Fixed — Step 12 grid tanks now get ports when they are generated:**
1. Added non-destructive `EnsureGridTankPorts()` directly to **Step 12 / Build Grid System Content**.
2. `LiquidTank_Large` now receives `Port_LiquidIO_N/S/E/W/Top/Bottom` with `MaritimePortFacing` tags during Step 12.
3. `GasTank_Large` now receives `Port_GasIO_N/S/E/W/Top/Bottom` with `MaritimePortFacing` tags during Step 12.
4. Existing port transforms are preserved; missing facing tags are repaired and missing ports are created. No tank capacity, mass, power, recipe cost, or tuning values are overwritten.
5. The gas/liquid corridor probes now validate the tank's named port alignment before accepting a broad collider hit, preventing off-axis/nearby tanks from being treated as connected.

**Fixed — pipe visuals no longer look like they are trying to connect everywhere:**
6. `PipeVisualBuilder` now forwards its existing `showUnusedFaceCaps` flag to `IndustrialPipeMesh`.
7. Gas and liquid pipes set `showUnusedFaceCaps = false`, so standalone pipe hubs only show real connected arms instead of decorative unused caps that looked like fake connections.

**Manual Unity step:**
- Run **Tools → Voxel Engine → Voxel Engine Setup → 12. Build Grid System Content** once. This is the important one for grid gas/liquid tank ports.
- Step 13 is no longer required just to repair grid tank ports, but remains safe to run for maritime content.

**Files touched:**
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Networks/IndustrialPipeMesh.cs`
- `Scripts/Networks/PipeVisualBuilder.cs`
- `Scripts/Gas/GasPipe.cs`
- `Scripts/Fluids/WaterPipe.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.19.4-dev] Detail-Lattice Pipe-to-Tank 5-Cell Connection Fix

**Type:** PATCH — pipe/tank connectivity and visual-link bug fix. Save-compatible; no save schema or balance changes.

**Fixed — gas/liquid pipes connect to tanks across 5 Detail lattice cells:**
1. `GridGasNetwork` and `GridLiquidNetwork` now evaluate tank links against the **Small/Detail lattice step** only: 5 × 0.5 m cells, matching pipe↔pipe long links.
2. Tank connection checks are now **port-aware**. The network tests each tank's named `Port_Gas*` / `Port_Liquid*` marker first and only falls back to tank center if no marker exists. This fixes valid pipe runs aimed at tank ports being rejected because the tank body center was not aligned with the detail pipe.
3. Corridor probing now walks exactly 5 Detail cells instead of deriving a much larger range from the structural grid size.

**Fixed — pipe visuals now show the same 5-cell tank reach:**
4. `GasPipe` and `WaterPipe` now draw arms to grid gas/liquid tanks when the nearest tank port is within the same 5 Detail-cell cardinal link rule used by the network.
5. Visual arms target the named tank port, so the pipe points cleanly to the connector instead of skewing toward the tank body center.

**Manual Unity step:**
- If your gas tank prefab still lacks gas port markers, run **Tools → Voxel Engine → Voxel Engine Setup → 13. Build Maritime Content** once. Non-destructive.

**Files touched:**
- `Scripts/GridSystem/GridGasNetwork.cs`
- `Scripts/GridSystem/GridLiquidNetwork.cs`
- `Scripts/Gas/GasPipe.cs`
- `Scripts/Fluids/WaterPipe.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.19.3-dev] Grid Port Ghost Lattice Follow & Corridor Tank Yield Fix

**Type:** PATCH — bug fixes for grid-builder maritime port preview alignment and gas/liquid 5-cell tank connection reliability. No save schema changes and no breaking API changes.

**Fixed — engine service port ghost follows the pipe ghost on the grid lattice:**
1. `MaritimePortPlanner.FillSeatFromSurface()` now snaps the pipe seat to the Detail lattice first, then derives the preview/placed port collar from that same snapped seat. This keeps the colored port ghost and the pipe ghost moving together in `GridBuilder` instead of letting the port ring sit on a raw collider hit point.
2. Over-cap previews now still compute chassis/grid-bound port geometry before returning the red rejection state. This prevents the red port ring from falling back to default local positions when a service is full.

**Fixed — gas/liquid tanks found by 5-cell corridor probes now actually connect:**
3. `GridGasNetwork` and `GridLiquidNetwork` were marking tanks discovered by the 5-cell corridor probe as “seen” but not yielding them to the caller immediately. Because the later brute-force fallback skipped already-seen tanks, corridor hits could be suppressed instead of connected.
4. Both networks now collect newly discovered corridor tanks and yield them in the same BFS pass, preserving duplicate protection while making the advertised 5-grid-square tank connection reliable.

**Manual Unity step:**
- Run **Tools → Voxel Engine → Voxel Engine Setup → 13. Build Maritime Content** once if your local prefabs have not already been rebuilt after the gas-tank port setup. This is non-destructive and only creates/repairs missing prefab/item/recipe/research links.

**Files touched:**
- `Scripts/Maritime/MaritimeVariablePorts.cs`
- `Scripts/GridSystem/GridGasNetwork.cs`
- `Scripts/GridSystem/GridLiquidNetwork.cs`
- `Scripts/Core/GameVersion.cs`
- `Changelog.md`

---

### [6.19.2-dev] Grid Port Ghost & 5-Cell Tank Link Fix

**Type:** PATCH — bug fixes for maritime port ghost and gas/liquid tank connectivity. No save break, save-compatible, no prefab rebuild required beyond optional setup.

**Fixed — Port ghost on engine now follows pipe ghost and is grid-bound (ROOT CAUSE):**
1. `GridBuilder` had NO variable-port path for liquid/gas/item pipes on grid ships. Exhaust/shaft snapping existed, but Fuel/Coolant/Oxygen/Item services (player-placed color-coded ports) were only handled in `BuildSystem` (voxel world). Now grid builder:
   - Detects pipe family (Liquid/Gas/Item) via `IsPipeItem`.
   - Plans variable port via `MaritimePortPlanner.PlanPipe` with detail-lattice seat (`seatGridLocal` → precision cell).
   - Shows a **ghost port ring** anchored to the engine chassis (`block.transform.TransformPoint(portLocal)`) — NOT the Rigidbody root — fixing "port at end of Rigidbody far from chassis".
   - The ring color matches service (amber Fuel, teal Coolant, sky-blue Oxygen, green Item) and tints red when at cap. It follows the pipe ghost 100% because both use identical `PlanPipe` geometry.
   - Placement is grid-bound: anchorLocal = precisionPos * SmallCellSize, `layer.AddBlock` + `block.transform.localPosition = anchorLocal`. `PipeVisualBuilder` + `GridGasNetwork`/`GridLiquidNetwork` refreshed immediately.
   - Over-cap: ghost tints red, toast "Fuel/Coolant/Oxygen already connected (max N)" every 1.2s, exactly like voxel builder.
2. Added `_ghostPortRing` fields + `ShowGhostPortRing` / `HideGhostPortRing` + `ShowGhostPortRingForBlock` helpers, bound to GridBuilder lifecycle (hide on ghost hide, destroy on OnDestroy).

**Fixed — Gas pipes never connect to gas tank, same for liquid tanks and pipes — now 5-grid-square connection (ROOT CAUSE):**
3. Gas tanks had **no named `Port_GasIO_*` markers** — only liquid tanks had `Port_LiquidIO_*`. `BlocksAreGasLinked` / `ProximityBlocks` relied on port proximity first, falling back to body range only 2× cell. Without gas ports, tanks beyond ~5m were missed and the 5-cell corridor probe sometimes missed due to small buffer/radius.
   - Added `EnsureGasTankPorts()` in `VoxelEngineSetupWindow` — creates 6 gas port markers (N/S/E/W/Top/Bottom) with `MaritimePortFacing` outward, same positions as liquid ports, with glowing sky-blue material. Non-destructive (adds missing facing tag only if port exists).
   - Called in maritime content build: `EnsureLiquidTankPorts()`, `EnsureGasTankPorts()`, `EnsureLiquidTankClassicBridge()`.
4. `GridGasNetwork` and `GridLiquidNetwork` hardened for reliable 5-cell connection:
   - Probe buffers enlarged 12→32 colliders, proximity result list 12→32.
   - Detail-pipe proximity radius widened: `max(Large*1.15,2.25)` → `max(Large*1.5,3.25)`; structural pipes `max(cell,Small)*1.8` → `*2.0`; bodyRange `EffectiveCellSize*2.0` → `*3.0` for face-touch forgiveness.
   - Corridor probe `radiusScale` 1.6→2.2 — overlapping spheres more forgiving of slight off-axis.
   - Added brute-force cardinal fallback: after BFS, scan ALL grid tanks and if any tank is within 5 cells cardinally (`PipeAdjacency.IsCardinalLinkDelta`) of ANY visited pipe, yield it. Guarantees advertised "5 grid squares" even if OverlapSphere probe missed.
   - Classic probe buffers also 16→32, radiusScale 2.2 for liquid bridge.
5. Manual Unity steps (optional but recommended):
   - Tools → Voxel Engine → Setup Wizard → **13. Build Maritime Content** once — creates gas tank ports (non-destructive).
   - Place GasTank_Large / LiquidTank_Large and a gas/liquid pipe 5 cells away cardinally — verify network connects, tank UI shows flow, engine buffers fill.
   - Place maritime engine (Small/Medium/Giant), hold gas/liquid/item pipe, aim at hull — verify colored port ring follows pipe ghost, is bound to grid, sits on chassis (not Rigidbody end).
   - Click to place — verify port installed, pipe seated on detail lattice, visually outside hull.

**Files touched:**
- `Scripts/GridSystem/GridBuilder.cs` — variable-port ghost ring + grid-bound placement + chassis-anchored port (fix Rigidbody far bug).
- `Scripts/GridSystem/GridGasNetwork.cs` — 5-cell reliability: bigger buffers, wider radius, 2.2× corridor + brute-force cardinal fallback.
- `Scripts/GridSystem/GridLiquidNetwork.cs` — same as gas.
- `Scripts/Editor/VoxelEngineSetupWindow.cs` — `EnsureGasTankPorts()` + call.
- `Changelog.md`, `GameVersion.cs` (bump to 6.19.2-dev).

---

### [6.19.1-dev] Pipe Connectivity Overhaul — Tank Reach, Variable Port Preview, Pipe-Cap Feedback, Spatial-Hash Performance

**Type:** PATCH — bug fixes, balance/polish for the pipe network. No save changes, no API breaks, no prefab rebuilds required.

**Fixed — Pipes connect to tanks from the expected 0.5 m face-touch range:**
1. The 6.19.0 proximity radius for pipe↔tank links (~0.9 m) was too tight for a pipe snapped 0.5 m off a structural tank face (that is ~1.5–2 m from the tank's transform origin). Liquid and gas grid networks now use a **2.25 m proximity radius** for detail-attached pipes, and the classic fluid network uses a **2.75 m** endpoint-search cap, so a gas/liquid pipe placed flush against any tank (grid-mounted or world-placed) connects reliably at one face distance.
2. `GridStep` now returns the **detail cell size (0.5 m)** when one side of a link is a precision attachment, instead of using the structural cell size (2.5 m). This was the root cause of "pipe sits right next to the tank but does not link" — the adjacency test was rejecting perfectly valid 0.5 m gaps.
3. Pipe↔endpoint adjacency tolerance widened slightly (0.35 → 0.45× step), forgiving small placement misalignments without ever allowing diagonals.
4. `WaterPipe` / `GasPipe` visual arms now also draw toward **world-placed** `WaterTank`, `WaterPump` and `GasTank` endpoints (previously only grid-mounted blocks drew arms, so a pipe next to a world tank looked disconnected).

**Fixed — Variable ports are visible 100% of the time when aiming:**
5. A **color-coded ghost port ring** is drawn on the engine hull **before** the player clicks, exactly where the new variable port will be installed. Same color coding as a placed port (amber=Fuel, teal=Coolant, sky-blue=Oxygen). When the service is at capacity the ring tints red, so the player can read the rejection at a glance.
6. When aiming at a spot that would reuse an existing port, no new ring is drawn (the real port is already there), matching the existing "reuse existing port" logic.

**Fixed — Pipe-cap feedback (engine over-cap):**
7. When aiming at an engine whose service port is already at capacity (e.g. fuel port already installed, player tries to attach a second liquid pipe), the **ghost now tints RED** (non-placeable) instead of silently falling through.
8. A bottom-right toast fires every ~1.2 s while the player keeps aiming: title `"<family> pipe reached"`, detail `"<service> already connected (max N)"`. Uses the same `BuildFeedbackHud` as every other placement toast with a red accent.
9. Clicking while over-cap is blocked and shows a persistent `Port Full` toast — no more silent fails.

**Fixed — Pipe lag with many pipes placed (spatial hash rebuilds):**
10. `GasNetwork` and `ItemPipeNetwork` were rebuilding their neighbour graphs with a **brute-force O(N²) double loop** every time a pipe was added — the source of the frame-time spikes when the player built long runs. Both networks now use the same **5 m spatial hash** the fluid network already uses, reducing topology rebuild from O(N²) to O(N).
11. `connectRadius` defaults tightened across `FluidNode`, `GasPipe` and `ItemPipe` (3.0 m → 1.5 m) so the topology manager cannot accidentally grow multi-metre phantom links across gaps; the manager caps the range anyway, but a tighter default prevents edge cases.
12. Visual arm probe radii capped at **0.85 m** (down from effectively 3.4 m on large grid blocks) so pipe visuals do not waste cycles scanning for endpoints across the room, and do not grow spurious arms to blocks that are not actually linked.
13. Topology rebuilds across all three pipe networks (fluid / gas / item) now use a **120 ms settle delay** (matching the power network) instead of rebuilding every frame during rapid placement bursts.
14. `PipeVisualBuilder.NotifyTopologyChanged()` is now bumped from `PlaceOnDetailLattice`, after each topology rebuild, and on pipe unregister — so visuals refresh exactly once per topology change instead of relying on the slow safety-net poll.
15. `GasNetwork` tank probe buffer is now properly cleared between scans and enlarged to 24 entries; stale collider references could previously produce phantom tank hits.

---

### [6.19.0-dev] Variable Port Round 2 — Visible Propellers, Flush Color-Coded Ports, Fine-Lattice Pipe Snap, Crude O₂/Item Ports, Static Ports Removed, Tank Reach & Pipe-Visual Perf

**Type:** MINOR — extends the variable-port system to the Crude engine (+ new Item service), removes the static engine service ports, and bundles the round-2 fixes from playtest feedback. Save mechanism intact; **old saves are not a concern this cycle (single-tester), but nothing about the save format was broken.** One manual setup step required (rebuild maritime prefabs, mesh v23→v24).

**Fixed — Propellers now actually have blades (ROOT CAUSE):**

1. The lofted blades from 6.18.0 never showed up because (a) blade length was an **absolute** value instead of scaling with the cell — tiny relative to the hub on large-grid props — and (b) the thin lofted shell was **back-face culled**. **Fix:** blade length/chord/thickness now scale with the cell (proportional to the hub at any grid size), and the blade material is rendered **double-sided** (`_Cull = Off`) so the screw is visible from every angle regardless of winding. Small = 3 bronze blades, Large = 4 steel, electric pod = 3. Mesh `Version` 23 → 24.

**Fixed — Ports no longer float off the engine:**

2. Variable port collars were seated *outside* the collider surface, so they hovered in the gap. **Fix:** the collar now mounts **flush** — inset ~2 cm into the hull so it reads as bolted to the engine, with only a slim glowing eye + thin coupling nipple proud of the surface.

**Fixed — Port no longer swallows the gas pipe:**

3. The port visual was oversized (big collar + fat protruding cylinder) and covered a thin gas pipe. **Fix:** the collar, eye and nipple are now **low-profile and slim** (~0.24 m disc, ~0.07 m nipple) so a gas pipe stays clearly visible plugged into it.

**Fixed — Pipe placement uses the small (fine) lattice:**

4. A pipe snapped to a variable port was sitting at a free-floating position with no grid alignment. **Fix:** the pipe hub now **snaps to the Detail-lattice cell** just outside the surface — clean fine-grid placement, easy to chain.

**Added — Crude Inline-4 variable ports (Oxygen + Items):**

5. The Crude (Small) engine now takes player-placed color-coded **Oxygen** (sky-blue, gas pipe) and **Item intake** (green, item pipe) ports. New `PortService.Item` + `ItemPrefixes` (`Port_ItemIntake`). Per-tier gating: **Small = Oxygen + Item**, **Medium/Giant = Fuel + Coolant + Oxygen**. Aiming an unsupported pipe family at an engine (e.g. a liquid pipe on the Crude) just falls through to normal fine-lattice placement.

**Removed — Static engine service ports:**

6. The authored **fuel / coolant / oxygen / item** ports were removed from all three engines (HFO V8, MGO V12, Crude Inline-4). Those services are now **only** via player-placed variable ports. **Exhaust collectors and shaft/rotation ports stay** (exhaust pipe still snaps to the authored exhaust output and auto-orients). The engine's body-proximity fallback still lets a nearby pipe connect, so nothing dead-ends.

**Fixed — Tanks connect within ~0.5 m (incl. diagonal):**

7. Even after the 6.18 corridor rewrite, a tank ~0.5 m off a pipe run (especially diagonally, ~0.71 m) could miss both the corridor samples and the 0.675 m proximity check. **Fix:** corridor probe spheres enlarged (radius scale 1.1 → 1.6) **and** the pipe/tank proximity radius widened (×1.35 → ×1.8 ≈ 0.9 m for a detail pipe), so a tank one cell away connects reliably on any axis.

**Fixed — Pipe visuals no longer lag when pipes sit close together (ROOT CAUSE):**

8. Every `PipeVisualBuilder` re-scanned its neighbours **every 0.4 s** (a full grid-block sweep per pipe), which lagged with many nearby pipes. **Fix:** rebuilds are now **event-driven** — a global `TopologyVersion` is bumped by `GridEntity`/`GridPrecisionAttachmentLayer` whenever a block is added or removed, and each pipe only re-scans when that version changes (with a slow 2 s safety-net poll). Placing/removing a pipe, machine, tank or chest triggers exactly one neighbour re-check; idle pipes do nothing.

**Confirmed — "max reached" feedback:**

9. The service resolver no longer silently *swaps* a full service for an empty one. The carried type is inferred from the pipe run you're extending (via the cached liquid network), so connecting a second **fuel** run now correctly refuses with **"Fuel input already connected (max 1)"** (red ghost + blocked placement) instead of quietly becoming coolant.

**Notes:**
- Exhaust handling is unchanged from 6.18 (auto-orients onto the authored exhaust collector) — now that fuel/oxygen/coolant flow again, the engine runs end-to-end.
- The Item-intake port gives the Crude engine a color-coded item-pipe connection point; solid-fuel delivery follows the existing item-network rules.

**Roadmap Status:**
- Vehicle power foundations: **🛠️ WORKING ON** — propellers, ports, connectivity and pipe-visual performance overhauled. Thomas to validate fuel→engine→shaft→propeller end-to-end.

**Files touched:**
- `Scripts/Maritime/MaritimeMeshBuilder.cs` — propeller blades scale with cell + double-sided material; static fuel/coolant/oxygen/item ports removed (exhaust + shaft kept); `DoubleSided` helper; `Version` 23 → 24.
- `Scripts/Maritime/MaritimeVariablePorts.cs` — `PortService.Item` + `PipeFamily`; per-tier `IsServiceAllowed`; smaller flush port visual; planner reworked to family routing + carried-type inference (no auto-switch) + flush/lattice seating; unused `PlanExhaust`/`ResolveGasService` removed.
- `Scripts/Maritime/MaritimePorts.cs` — `ItemPrefixes`.
- `Scripts/Building/BuildSystem.cs` — route liquid/gas/**item** pipes by family; enable variable ports on the Crude engine (gated in planner); snap pipe hub to the Detail lattice.
- `Scripts/GridSystem/GridLiquidNetwork.cs` / `GridGasNetwork.cs` — corridor radius 1.1→1.6, pipe/tank proximity ×1.35→×1.8.
- `Scripts/Networks/PipeVisualBuilder.cs` — event-driven rebuild via `TopologyVersion`; slow safety-net poll.
- `Scripts/GridSystem/GridEntity.cs` / `GridPrecisionAttachmentLayer.cs` — bump `PipeVisualBuilder.NotifyTopologyChanged()` on block add/remove.
- `Scripts/Core/GameVersion.cs` — bumped to 6.19.0-dev.
- `Roadmap.md`, `Changelog.md`.

**Manual Unity Steps (one-time prefab rebuild + validation):**

*A. Rebuild maritime prefabs (required for the visible propeller blades):*
1. Pull all changed scripts, compile (expect **0 errors**).
2. Open **Tool → Voxel Engine → Voxel Engine Setup** and rebuild the maritime prefabs (the `__MaritimeMesh_v23` markers re-bake to `v24`). Confirm the **Small / Large / Electrical propellers** now show real curved blades (not a bare hub).
3. No new items / recipes / research are needed — nothing in the Setup item/recipe/research tables changes.

*B. Validate variable ports + static-port removal:*
4. Place an **HFO V8** or **MGO V12**. Confirm there are **no** built-in fuel/coolant/oxygen port cubes on the model (exhaust collector + shaft flange remain).
5. Aim a **liquid pipe** at the engine body → an **amber Fuel** collar appears **flush** on the hull; the pipe hub sits on the **fine lattice** just outside. Aim elsewhere with another liquid pipe fed from a coolant tank → a **teal Coolant** collar.
6. Aim a **gas pipe** → **sky-blue Oxygen** collar (slim — the gas pipe stays visible around it).
7. Connect a **second fuel** run → ghost goes **red**, placement refused with **"Fuel input already connected (max 1)"**.
8. Place a **Crude Inline-4** → confirm a **gas pipe** makes an **Oxygen** port and an **item pipe** makes a green **Item intake** port; a liquid pipe does **not** make a port (falls through to normal placement).

*C. Validate connectivity + performance:*
9. Put a **GridLiquidTank** / **GridGasTank** ~0.5–1 m off the end of a pipe run (try face-adjacent **and** diagonal) → buffers fill; engine runs.
10. Place **30+ pipes** close together → confirm **no lag** from visual updates; placing one more pipe updates neighbouring arms **once** and then idles.
11. **Save → reload** → variable ports + engine state persist.

---

### [6.18.0-dev] Variable Color-Coded Engine Service Ports, Pipe/Tank Connectivity Overhaul, Exhaust Auto-Orient & True Propeller Blades

**Type:** MINOR — new system (variable service ports) + connectivity/orientation fixes + propeller rebuild. **Fully save-compatible.** One manual setup step required (rebuild maritime prefabs) — see below.

**Added — Variable Color-Coded Service Ports (HFO V8 / MGO V12):**

1. **"Connect from anywhere" ports.** Instead of threading pipes onto fixed ports buried metres inside the engine hull, you now aim a liquid/gas pipe at **any face of the engine** and a color-coded service port is born exactly at the surface — always **outside** the body, always visible, always easy to chain the next pipe onto. This eliminates the "pipe disappears inside the engine input" problem at the source: a port can never be buried because it is created on the hull where you aimed.

2. **Color-coded by service.** Fuel = **amber**, Coolant = **teal**, Oxygen = **sky-blue**, Exhaust = **red**. Installed ports show a glowing collar + eye so you can read an engine's hookups at a glance.

3. **Per-engine caps enforced.** 1 × Fuel, 1 × Coolant, 1 × Oxygen, up to 2 × Exhaust. Trying to attach a pipe whose service is already full is **rejected**: the ghost tints red and placement is blocked with a message (e.g. *"Fuel input already connected (max 1)"*) — no pipe spam, and no silent free-pipe that would sneak past the cap.

4. **Smart service detection.** The service a port takes on is inferred from what your grid's tanks actually hold (with an "engine still needs it" tiebreaker). A second pipe of the same service re-snaps to the existing port instead of spawning a duplicate collar.

5. **Save-compatible by design.** Authored model ports stay in place as the engine's defaults (existing saves + existing pipe connections keep working untouched). Dynamic ports are purely additive child transforms named with the same `Port_*` prefixes + a `MaritimePortFacing` tag, so **all existing snapping, network topology and engine consumption code works unchanged**. Dynamic ports serialize across save/load.

**Fixed — Pipe / Tank Connectivity:**

6. **Tanks now connect from several cells away (ROOT CAUSE).** The liquid + gas "corridor probe" stepped in coarse **structural** cells (2.5 m) with thin radius, so a tank sitting between sample points was skipped — the reason a tank four cells off a pipe run wouldn't connect. **Fix:** both `GridLiquidNetwork` and `GridGasNetwork` now sweep the corridor on the **detail lattice** (0.5 m steps) with **overlapping** probe spheres out to five structural cells. A tank on a straight cardinal line within range is now sampled continuously and can never be skipped, and a pipe whose detail position sits slightly off the tank's row is forgiven.

7. **Liquid/gas now actually pumps into the engines.** Direct consequence of #6 — the engine draws fuel/coolant/oxygen via `DrawLiquidFor`/`DrawGasFor`, which only return anything once a connected tank is *found*. With the corridor fixed, the buffers fill and the engine runs. (Pipes are still mandatory — the no-pipe fallback stays removed.)

8. **Pipe hubs sit further off authored ports.** Authored-port snaps now seat the hub 1.4 detail cells outward (was 1.0) so the hub is visibly proud of the hull and easy to chain. Variable ports (the preferred path) seat from the actual hull surface and are always outside.

**Fixed — Exhaust Pipe Orientation:**

9. **Exhaust pipes now auto-orient the right way (ROOT CAUSE).** The exhaust pipe kept whatever player rotation it was placed with, so its intake flange (local −Z) frequently pointed sideways instead of down onto the engine's upward-facing exhaust collector. **Fix:** `GridExhaustPipe` now auto-orients on placement (and on save/load): it finds the nearest engine exhaust-output port (authored or variable) and rotates so the **intake flange faces the port** and the **outlet/stack points away** — flange down onto a top collector, every time. The flex-coupling still seals any residual gap.

**Changed — Maritime Propellers Rebuilt:**

10. **Real propeller blades.** `BuildPropeller` / `BuildEPropeller` previously fanned flat boxes around a hub. They are now genuine **lofted screw blades**: tapered chord, root→tip pitch twist, a cambered lens section and a swept tip, on a rounded hub with a faired tail cone. Small = 3 bronze blades, Large = 4 steel blades, electric pod = 3 matching blades. The `SpinPivot` name + Z-axis spin are preserved so `MaritimeAnimator` drives them exactly as before. (Mesh `Version` bumped 22 → 23 to force a prefab rebuild.)

**Notes / Known behaviour:**
- The per-service cap governs the clean color-coded ports. The legacy proximity connection (a free-placed pipe near an engine) still behaves as before; the cap is about the nice variable ports, not about blocking every possible pipe.
- Variable service ports are a **Medium (HFO V8)** and **Giant (MGO V12)** feature — the liquid-fuelled tiers that take piped fuel/coolant/oxygen. The Crude Inline-4 (solid fuel) is unaffected.

**Roadmap Status:**
- Vehicle power foundations: **🛠️ WORKING ON** — connectivity, port snapping, exhaust orientation and propellers overhauled. Thomas to validate the full fuel→engine→shaft→propeller chain end-to-end.

**Files touched:**
- `Scripts/Maritime/MaritimeVariablePorts.cs` — **NEW**: `PortService`, `VariablePortRecord`, `MaritimeVariablePorts` component (caps, color-coded port objects, save capture/rebuild) + `MaritimePortPlanner` (shared geometry + service resolution).
- `Scripts/Maritime/GridMaritimeEngine.cs` — `VariablePorts` accessor + `CaptureVariablePorts`/`RestoreVariablePorts` save hooks.
- `Scripts/Building/BuildSystem.cs` — variable-port snap path (ghost preview + commit), over-cap reject (red ghost + blocked placement + message), improved authored-port seat offset.
- `Scripts/Maritime/GridExhaustPipe.cs` — auto-orient intake flange toward the nearest engine exhaust port (placement + save/load).
- `Scripts/GridSystem/GridLiquidNetwork.cs` — detail-lattice corridor probe with overlapping spheres.
- `Scripts/GridSystem/GridGasNetwork.cs` — detail-lattice corridor probe with overlapping spheres.
- `Scripts/Networks/PipeAdjacency.cs` — `ProbeCardinal` gains an optional `radiusScale`.
- `Scripts/Maritime/MaritimeMeshBuilder.cs` — lofted propeller blades + `MakePropellerBladeMesh`/`MeshGo` helpers; `Version` 22 → 23.
- `Scripts/Persistence/WorldStatePersistence.cs` — additive `SavedMaritimePorts` capture/restore on engine blocks.
- `Scripts/Core/GameVersion.cs` — bumped to 6.18.0-dev.
- `Roadmap.md`, `Changelog.md`.

**Manual Unity Steps (one-time prefab rebuild + validation):**

*A. Rebuild the maritime prefabs (required for the new propellers):*
1. Pull all changed scripts, let Unity compile (expect **0 errors**).
2. Open **Tool → Voxel Engine → Voxel Engine Setup**.
3. Rebuild the maritime block prefabs (the setup window detects the `__MaritimeMesh_v22` markers and re-bakes them to `v23`). Confirm the **Small Propeller**, **Large Propeller** and **Electrical Propeller** prefabs now show real curved blades.
4. No new items/recipes/research are needed — this feature adds **no** new items; it only changes engine behaviour + propeller visuals, so nothing in the Voxel Engine Setup item/recipe/research tables needs touching.

*B. Validate variable service ports:*
5. Place an **HFO V8 (Medium)** or **MGO V12 (Giant)** engine on a ship grid.
6. Hold a **liquid pipe**, aim at the **side/top of the engine body** (not an existing port) and place → an **amber Fuel** collar appears where you aimed and the pipe hub sits just **outside** the hull. Aim elsewhere and place a second liquid pipe → a **teal Coolant** collar appears.
7. Hold a **gas pipe**, aim at the engine and place → a **sky-blue Oxygen** collar appears.
8. Try to place a **second fuel** liquid pipe when fuel+coolant are both installed → the ghost goes **red** and placement is refused with *"…already connected (max 1)"*.
9. Run liquid pipes from a **GridLiquidTank (HeavyFuelOil / MarineGasOil)** to within a few cells of the engine, and gas pipes from a **GridGasTank (Oxygen)** likewise — they do **not** need to touch the tank. Open the engine panel → confirm **Fuel, Oxygen and Coolant buffers fill**.
10. Start the engine → confirm it runs without *"OUT OF FUEL"* / *"NO OXYGEN"* warnings.

*C. Validate exhaust + propellers:*
11. Place an **Exhaust Pipe** on top of the running engine → confirm its **intake flange faces DOWN** onto the engine's exhaust collector and the **stack points UP**, and that it emits the tier-correct smoke.
12. Connect a drive shaft from the engine's `Port_ShaftOutput` to a **propeller** → confirm the new blades **spin about the shaft axis** and the ship produces thrust.
13. **Save → reload** → confirm the color-coded variable ports, pipe connections and engine state all persist (no ports lost, fuel still flowing).

---

### [6.17.3-dev] Final Pipe/Tank Connectivity, Pipe Seat, No-Pipe Fallback Removed & Tank Corridor Probe

**Type:** PATCH — critical connectivity fixes. Fully save-compatible. No setup step required.

**Fixed:**

1. **Pipes Need to Touch Tanks (ROOT CAUSE)** — The `ConnectedTanks` BFS in `GridLiquidNetwork` had face-touch and proximity checks but was MISSING the 5-cell cardinal corridor probe for `GridLiquidTank`. The classic bridge had this probe for `WaterTank`, but grid liquid tanks had no corridor search. **Fix:** Added `ProbeGridTankCorridor()` — a 5-cell cardinal corridor sweep from every pipe in the run, exactly matching how classic tanks are found. A GridLiquidTank up to 5 cells away on a straight cardinal line now connects.

2. **Fuel/Oxygen Not Flowing** — Same root cause as #1. The pipe network couldn't find the tank, so `DrawLiquidFor`/`DrawGasFor` returned 0. With the corridor probe, tanks within range are discovered.

3. **No-Pipe Fallback Removed** — Removed the direct grid-wide tank iteration from both `DrawLiquidFuel()` (MaritimeBlockBase) and `TickOxygen()` (GridMaritimeEngine). Players MUST place pipes to connect tanks to engines. No more cheating.

4. **Pipe Hub Sits Inside Engine (ROOT CAUSE)** — The reverse-raycast seating in `SeatAnchorOutsideMachineShell` was unreliable with complex banded hitboxes (MGO slim/full-width bands). The ray could miss the machine's outer collider, falling back to a half-cell plug that was still inside the engine body. **Fix:** Replaced the entire 50-line collider raycasting approach with a single line: `portLocal + outLocal * small` — one full Detail cell (0.5 m) outward from the port position. Always outside the engine. The doubled proximity range (2.5× cell) ensures the pipe is still found as connected.

5. **Port Snap Fallback Into Engine** — When the outward Detail cell was occupied, `TryGetMaritimePortSnap` fell back to the port's OWN cell, which is inside the engine body. **Fix:** Now returns `false` instead, letting placement fall through to normal grid placement.

6. **Shift-Click** — Confirmed the code path exists and should work. Test by opening an engine panel (right-click on the engine), then shift-click a module in the module slots — it should transfer to player inventory. The `QuickTransfer` fallback at step 4 routes any non-player-container source to the player's inventory.

**Roadmap Status:**
- Vehicle power foundations: **🛠️ WORKING ON** — oxygen/fuel/pipe connectivity fixed. Thomas should validate end-to-end.
- Cable performance: **✅ COMPLETED** — caching mitigates the frame-rate sink.

**Files touched:**
- `Scripts/GridSystem/GridLiquidNetwork.cs` — added ProbeGridTankCorridor
- `Scripts/Maritime/MaritimeBlockBase.cs` — removed no-pipe fallback
- `Scripts/Maritime/GridMaritimeEngine.cs` — removed no-pipe fallback
- `Scripts/Building/BuildSystem.cs` — simplified seat anchor to full-cell offset, removed port-cell fallback
- `Scripts/Core/GameVersion.cs` — bumped to 6.17.3-dev
- `Roadmap.md`
- `Changelog.md`

**Manual Unity Steps:**
1. Pull all changed files, compile (expect 0 errors).
2. Place an MGO engine on a grid with GridLiquidTank (HeavyFuelOil) and GridGasTank (Oxygen).
3. Place liquid pipes from near the engine's fuel port to within 5 cells of the fuel tank (straight cardinal line).
4. Place gas pipes from near the engine's O₂ port to within 5 cells of the oxygen tank.
5. Open the engine panel → confirm fuel, oxygen, and coolant buffers fill.
6. Start the engine → confirm it runs without starvation warnings.
7. Verify the pipe hubs sit OUTSIDE the engine body, not inside.
8. Verify that removing the pipes stops fuel/oxygen flow immediately.
9. Test shift-click: open engine panel, shift-click a module → transfers to inventory. If it doesn't, let me know the exact panel context and I'll investigate further.

---

### [6.17.2-dev] Engine Oxygen/Grid Gas Network, Fuel/Pipe Fallback, Performance Caching & Pipe Proximity Fixes

**Type:** PATCH — major connectivity and performance fixes. Fully save-compatible. No setup step required.

**Fixed / Changed:**

1. **Oxygen Not Flowing to Engines (ROOT CAUSE)** — `GridMaritimeEngine.TickOxygen()` used the classic `GasNetwork.Instance.FindTankNear()` which searches for `VoxelEngine.Gas.GasTank` (classic gas tank). Modern grid builds use `GridGasTank` — a completely different class. The classic network could never find grid gas tanks. **Fix:** Rewrote `TickOxygen()` to use `GridGasNetwork.Instance.DrawGasFor(this, GasType.Oxygen, ...)` with a direct grid-wide tank fallback when no gas pipes are connected.

2. **Fuel Not Flowing to Engines (ROOT CAUSE)** — `MaritimeBlockBase.DrawLiquidFuel()` checked `HasPipes(Grid)` — if true, it ONLY used `GridLiquidNetwork.DrawLiquidFor()`. If the pipe path returned 0 (pipe couldn't find the tank), the direct tank-iteration fallback was SKIPPED. Coolant worked because `RefillCoolant()` tried two liquid types (MarineEngineCoolant then Water), which had redundancy. **Fix:** `DrawLiquidFuel()` now ALSO runs the direct tank fallback when the pipe path draws less than requested.

3. **Performance: ConnectedTanks BFS Storm** — `ConnectedTanks()` in both `GridLiquidNetwork` and `GridGasNetwork` ran a full proximity BFS over ALL blocks on the grid every time `DrawLiquidFor`/`DrawGasFor` was called — every FixedUpdate per engine per liquid/gas type. With 40+ cables, this created massive lag. **Fix:** Added result caching with a 0.15 s TTL in both networks. Added `SetDirty()` methods that invalidate the cache when pipes are placed.

4. **Pipe Proximity Range Too Short** — `BlocksAreLiquidLinked` and `BlocksAreGasLinked` used a 1.5× cell size port range and 1.35× body range. MGO engine ports can sit 2+ cells from the origin — the pipe was placed at the port but fell outside the proximity check. **Fix:** Widened to 2.5× cell size port range and 2.0× body range.

5. **Pipes Inside Engines (Refined)** — Reverted the `SeatAnchorOutsideMachineShell` full-cell offset back to half-cell to restore proximity detection. The shell probe reverse-raycast still handles deep-buried ports.

6. **GAS/LIQUID Network SetDirty on Pipe Placement** — Added calls to `GridLiquidNetwork.Instance.SetDirty()` and `GridGasNetwork.Instance.SetDirty()` in `BuildSystem.PlaceOnDetailLattice` so the cache clears whenever a pipe is placed or removed.

**Roadmap Status:**
- Vehicle power foundations: **🛠️ WORKING ON** — oxygen and fuel now flow correctly. Thomas should validate the full engine power chain end-to-end.
- Cable performance: **🛠️ WORKING ON** — ConnectedTanks caching mitigates the primary frame-rate sink.

**Files touched:**
- `Scripts/Maritime/GridMaritimeEngine.cs` — oxygen rewrite to use GridGasNetwork
- `Scripts/Maritime/MaritimeBlockBase.cs` — fuel draw fallback
- `Scripts/GridSystem/GridLiquidNetwork.cs` — proximity range doubled, ConnectedTanks cache + SetDirty
- `Scripts/GridSystem/GridGasNetwork.cs` — proximity range doubled, ConnectedTanks cache + SetDirty
- `Scripts/Building/BuildSystem.cs` — reverted seat anchor to half-cell, added SetDirty calls
- `Scripts/Core/GameVersion.cs` — bumped to 6.17.2-dev
- `Roadmap.md`
- `Changelog.md`

**Manual Unity Steps:**
1. Pull all changed scripts, let Unity compile (expect 0 errors).
2. Place an MGO engine on a grid with a GridLiquidTank (HeavyFuelOil or MarineGasOil) and a GridGasTank (Oxygen) connected via liquid + gas pipes.
3. Open the engine panel → confirm Oxygen buffer fills, Fuel buffer fills, Coolant buffer fills.
4. Start the engine → confirm it runs without showing "NO OXYGEN" or "OUT OF FUEL".
5. Place 40+ cables on the same grid → confirm frame rate stays playable.
6. Place a liquid pipe snapped to an engine port → confirm the pipe hub sits just outside the engine body, not inside.
7. Test without pipes: fill GridLiquidTank and GridGasTank on the same grid as an engine but with no pipes → confirm the direct-tank fallback still delivers fuel and oxygen.
8. Save/reload → confirm engine state persists.

**Type:** PATCH — multiple bug fixes and polish. Fully save-compatible. No setup step required.

**Fixed / Changed:**

1. **Gas Network Connectivity (Issue 1)** — `GridGasNetwork` was missing the world-space proximity pass and 5-cell cardinal corridor probe that `GridLiquidNetwork` already had. This meant gas pipes had to be face-adjacent to connect to engines or tanks. Now gas pipes use:
   - Face-touch adjacency (same as before)
   - World-space proximity: gas pipes within `1.35× cellSize` of an engine/gas port body count as connected
   - 5-cell cardinal corridor probe: gas tanks up to 5 cells straight off any pipe in the run are reachable without touching
   - Matching oil/liquid pipe proximity and corridor behavior

2. **Pipe Port Snap — Pipes No Longer Inside Engines (Issue 2)** — `SeatAnchorOutsideMachineShell` in `BuildSystem.cs` now uses a **full cell offset** (0.5 m for Detail) instead of a half-cell offset when positioning pipes snapped to maritime ports. This guarantees the pipe hub clears the engine body even on deep-buried ports (MGO fuel/coolant/O₂ ports can sit several metres inside the hull).

3. **Turbocharger Snapping and Effects (Issue 6)** — 
   - Made `GetTurboAttachmentLocalOffset()` and `TransformLocalSlotOffsetToGrid()` in `GridMaritimeEngine` **public** so `GridBuilder` can compute turbo attachment cells.
   - Added `TrySnapTurboToEngine()` — when holding a turbo block without hitting an exact turbo cell, the builder scans the aimed engine's available turbo slots and snaps to the nearest free one.
   - Physical turbo blocks now apply ALL HighFlowTurbocharger module effects: +10% RPM cap per connected turbo, +10% fuel use per turbo, and smoke velocity multiplier.
   - Added `TurboSmokeSpeed` property and `_turboFuelMultiplier` field to `GridMaritimeEngine`.

4. **Drive Shaft Daisy-Chain Alignment (Issue 4)** — Shaft-to-shaft chaining now uses a full cell offset (not half cell) so consecutive shafts align perfectly without vertical drift. Previously half-cell offset could cause slight misalignment when chaining multiple shafts.

5. **Module Extraction (Issue 5)** — Confirmed existing `QuickTransfer` in `GameUIController` already handles non-inventory source containers (engine module slots → player inventory) through step 4 fallback. The slots are marked `interactive=true` in `MaritimeBlockUI`. No code change needed — test this in Unity by shift-clicking a module slot.

6. **Propeller Visuals (Issue 3)** — Noted for next Step 13 rebuild in `MaritimeMeshBuilder`. See manual steps below.

**Roadmap Status:**
- Vehicle power foundations: **🛠️ WORKING ON** — validation of the maritime-to-grid power chain remains open.
- Unified Grid positional indexing: **🛠️ WORKING ON** — no change this release.

**Files touched:**
- `Scripts/GridSystem/GridGasNetwork.cs` — proximity, corridor, pipe/tank discovery rewrite
- `Scripts/Building/BuildSystem.cs` — full cell port snap offset
- `Scripts/GridSystem/GridBuilder.cs` — turbo snap helper, drive shaft chain offset
- `Scripts/Maritime/GridMaritimeEngine.cs` — turbo methods public, turbo fuel/RPM/smoke effects
- `Scripts/Core/GameVersion.cs` — already 6.17.1-dev
- `Roadmap.md`
- `Changelog.md`

**Manual Unity Steps:**
1. Pull changed scripts, let Unity compile (expect 0 errors).
2. Test gas pipe connectivity: place a gas pipe run near (but not touching) an engine's O₂ port → confirm the engine draws oxygen through the pipe network.
3. Test gas pipe → tank: place a gas tank 3-4 cells away from a gas pipe on the same cardinal axis → confirm gas transfers (5-cell corridor).
4. Test pipe port snap: snap a liquid pipe to an MGO engine fuel port → confirm the pipe hub sits OUTSIDE the engine body, not inside.
5. Test turbo snap: hold a Small Turbocharger and aim at an engine's turbo mounting point → confirm it snaps to the correct slot.
6. Test turbo effects: place a physical turbo on an engine → open the engine panel → confirm RPM cap and fuel use increase.
7. Test drive shaft chain: place one drive shaft on an engine port, then chain a second by aiming at the first shaft's end → confirm alignment is perfect (no vertical drift).
8. Test module extraction: open an engine panel, shift-click a module in the module slots → confirm it transfers to player inventory.

**Propeller Visuals — When ready:**
- Open `Tools > Voxel Engine > Voxel Engine Setup`
- Run **Step 13 (Build Maritime Content)** once to rebuild propeller meshes with more realistic shapes
- The Step 13 run is non-destructive and preserves existing balance/recipes

---

### [6.17.0-dev] Drop-Void Confirmation & Per-World Warning Toggle

**Type:** MINOR — save-compatible UI/world-setting feature. Adds one non-generation world setting; no save schema migration, recipe changes, prefab changes, or setup step required.

**Added / Changed:**
- Manual item drops that exceed the physical world-drop limit no longer simply block the action.
- When a manual drop would exceed the limit, the player now gets a centered confirmation dialog explaining:
  - how many units they are trying to drop
  - how many physical item units can still spawn
  - how many units will be voided if confirmed
- Dialog buttons:
  - **CONFIRM VOID** — drops what fits and permanently voids the excess.
  - **DENY** — keeps the stack untouched in the inventory/container slot.
- Added a per-world checkbox: **Show this warning before voiding drops in this world**.
  - The checkbox is remembered in `world_settings.json`.
  - If disabled, future over-limit manual drops proceed directly: what fits spawns, excess is voided.
- New World and Edit World also expose the drop-void warning toggle for each save.
- Runtime build version constants now report `6.17.0-dev`.

**Roadmap Status:**
- Inventory Weight, Drop Warnings & Terminal Search remains **✅ COMPLETED** and now includes the over-limit manual drop confirmation workflow.

**Files touched (pull these):**
- `Scripts/UI/GameUIController.cs`
- `Scripts/Menu/WorldSession.cs`
- `Scripts/Menu/MainMenuController.cs`
- `Scripts/Core/GameVersion.cs`
- `Roadmap.md`
- `Changelog.md`

**Manual steps:** none — runtime/UI/settings only. No `Tools > Voxel Engine > Voxel Engine Setup` run required.

---

### [6.16.1-dev] Autosave Visibility, Five-Minute Cadence & Terminal Search Compile Fix

**Type:** PATCH — compile/UI/settings cleanup for 6.16.0-dev. Fully save-compatible; no save schema, recipe, prefab, setup, or public API changes.

**Fixed / Changed:**
- Fixed `CS0103` in `GridMasterTerminal` by using the existing `BlockStateLabel(...)` helper for terminal search instead of the non-existent `BlockStatus(...)` name.
- The Saves page **SAVES/HIDE** button now truly collapses the autosave slot panel instead of only darkening the slot cards.
- Default autosave cadence is now **5 minutes / 300 seconds**.
- Settings migration v12 upgrades profiles still using the old 30-second default to 5 minutes while preserving custom autosave choices such as Off, 15s, 1m, or 2m.
- Runtime build version constants now report `6.16.1-dev`.

**Roadmap Status:**
- Inventory Weight, Drop Warnings & Terminal Search remains **✅ COMPLETED**; this patch only cleans up compile/UI/settings issues reported after 6.16.0-dev.

**Files touched (pull these):**
- `Scripts/GridSystem/UI/GridMasterTerminal.cs`
- `Scripts/Menu/MainMenuController.cs`
- `Scripts/Settings/GameSettings.cs`
- `Scripts/Core/GameVersion.cs`
- `Roadmap.md`
- `Changelog.md`

**Manual steps:** none — compile/UI/settings cleanup only. No `Tools > Voxel Engine > Voxel Engine Setup` run required.

---

### [6.16.0-dev] Inventory Weight, Matter Stacks, Drop Warnings & Terminal Search

**Type:** MINOR — save-compatible inventory/world-management/UI feature set. No save schema migration, recipe changes, prefab changes, or setup step required.

**Added / Improved:**
- Default physical dropped-item limit is now **1000** instead of 90. Existing worlds can edit this from **Edit World**.
- Added right-corner toast warnings when physical world drops approach the active limit, hit the limit, or a spawned stack is capped by the remaining drop capacity.
- Added global **900-unit matter stacks** for stackable items. Per-item stack-size authoring no longer creates smaller stack caps during insertion, pipe buffering, machine input, storage extraction, or UI merge checks. Unique payload/tool items remain one-per-stack.
- Added world-configurable weight multipliers for:
  - player inventory max matter weight
  - chest/container/machine max matter weight
- New World and Edit World expose both weight multipliers as percentages without touching generation settings.
- Player inventory now shows a live **Matter Weight** readout and fill bar: current weight / max weight.
- Chests and machines now enforce container weight limits through `ItemContainer`; grid cargo keeps its authored larger mass cap through an override.
- Breaking a block now drains every contained `ItemContainer` first: contents try to enter the player's inventory, then any leftovers drop into the world. This covers chests, machines, module slots, and other inventory-bearing placed blocks.
- Storage disks now describe and behave as **matter-conversion storage**: heavier items consume more GB per unit, and storage UIs show GB usage.
- Ship Control Terminal now has a block search field and no longer lists armor blocks or liquid/water pipe utility blocks.
- Runtime build version constants now report `6.16.0-dev`.

**Roadmap Status:**
- World Management, Autosaves & Item Limits remains **✅ COMPLETED** and now includes weight/drop-limit world settings.
- Crafting / items / storage moved to **🛠️ WORKING ON** for active weight/matter-stack/storage progression work.

**Files touched (pull these):**
- `Scripts/Items/ItemDefinition.cs`
- `Scripts/Items/ItemStack.cs`
- `Scripts/Items/ItemContainer.cs`
- `Scripts/Items/Inventory.cs`
- `Scripts/Items/DroppedItem.cs`
- `Scripts/Building/PlacedBlock.cs`
- `Scripts/Menu/MainMenuController.cs`
- `Scripts/Menu/WorldSession.cs`
- `Scripts/UI/GameUIController.cs`
- `Scripts/UI/WorldInspectionHud.cs`
- `Scripts/GridSystem/UI/GridMasterTerminal.cs`
- `Scripts/GridSystem/GridCargoContainer.cs`
- `Scripts/GridSystem/GridElectricFurnace.cs`
- `Scripts/Crafting/ElectricFurnace.cs`
- `Scripts/Simulation/Assembler.cs`
- `Scripts/Simulation/Crusher.cs`
- `Scripts/Transport/ItemPipe.cs`
- `Scripts/Player/PlayerInteractionTool.cs`
- `Scripts/Storage/StorageDisk.cs`
- `Scripts/Storage/ServerRack.cs`
- `Scripts/Storage/NASBlock.cs`
- `Scripts/Storage/StorageUI.cs`
- `Scripts/Core/GameVersion.cs`
- `Roadmap.md`
- `Changelog.md`

**Manual steps:** none — runtime/UI-only. No `Tools > Voxel Engine > Voxel Engine Setup` run required.

---

### [6.15.1-dev] Autosave Compile Cleanup & Modern Power Node IDs

**Type:** PATCH — compile/warning cleanup for 6.15.0-dev. Fully save-compatible; no save schema, recipe, prefab, setup, or public API changes.

**Fixed:**
- Fixed `CS0136` in `WorldStatePersistence.WorldStatePath()` by renaming the nested `folder` local to `stateFolder` and the fallback folder local to `fallbackFolder`.
- Fixed Unity 6 deprecation warnings by replacing runtime `GetInstanceID()` calls with `GetEntityId().GetHashCode()` in `PowerNetworkManager.NeighbourSignature()` and the grid-liquid bridge cache key.
- Runtime build version constants now report `6.15.1-dev`.

**Roadmap Status:**
- World Management, Autosaves & Item Limits remains **✅ COMPLETED**; this patch only cleans up the compile/warning reported after 6.15.0-dev.

**Files touched (pull these):**
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Power/PowerNetworkManager.cs`
- `Scripts/GridSystem/GridLiquidNetwork.cs`
- `Scripts/Core/GameVersion.cs`
- `Roadmap.md`
- `Changelog.md`

**Manual steps:** none — compile/warning cleanup only. No `Tools > Voxel Engine > Voxel Engine Setup` run required.

---

### [6.15.0-dev] World Management Autosave Slots & Edit World

**Type:** MINOR — save-compatible world-management UI/system feature. No save schema migration, recipe changes, prefab changes, or setup step required.

**Added / Improved:**
- Added three rotating autosave slot files beside each world's `world_state.json`:
  - `world_state.autosave1.json`
  - `world_state.autosave2.json`
  - `world_state.autosave3.json`
- Background autosaves now write the current save first, then rotate that valid snapshot into slot 1 while preserving the previous slots. Manual quit/menu saves still write the normal current save without burning autosave history.
- The Saves page now shows three visible autosave slot cards per world with timestamp, size, empty-state text, and restore buttons.
- Restoring an autosave slot safely copies that slot back to `world_state.json` and backs up the previous current save as `world_state.before_autosave_restore.json`.
- Added **Edit World** for non-generation settings only:
  - world folder/display name
  - maximum active physical dropped items
- Edit World deliberately does not touch seeds, planets, terrain/chunks, save schema, placed blocks, recipes, research, or generated content.
- Save cards now use the requested management layout: primary **PLAY**, grouped **EDIT/SAVES**, and stacked **CLONE/DEL** controls.
- Clone now performs a true save clone by copying the existing world folder to the next available copy name.
- World summaries now surface each world's configured dropped-item limit in the save-card metadata.
- Runtime build version constants now report `6.15.0-dev`.

**Roadmap Status:**
- World Management, Autosaves & Item Limits: **🛠️ WORKING ON → ✅ COMPLETED**.

**Files touched (pull these):**
- `Scripts/Menu/MainMenuController.cs`
- `Scripts/Menu/WorldSession.cs`
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Core/GameVersion.cs`
- `Roadmap.md`
- `Changelog.md`

**Manual steps:** none — runtime/UI-only. No `Tools > Voxel Engine > Voxel Engine Setup` run required.

---

### [6.14.7-dev] Player Save Safety Final Guard

**Type:** PATCH — save-safety/runtime recovery fix. Fully save-compatible; no save schema, recipe, prefab, setup, or public API changes.

**Fixed / Improved:**
- `WorldStatePersistence.RestorePlayer` now validates the saved player position before writing it to the live player transform. Corrupt `NaN`/`Infinity` positions or positions buried deeply inside an active planetary body no longer poison physics, chunk streaming, or follow-up autosaves.
- Invalid saved player rotation now falls back to `0` degrees instead of applying an invalid yaw.
- Invalid player coordinates recover non-destructively to the best safe spawn in order: bed spawn, initialized world spawn, stored world spawn, active-body surface fallback, then flat-world high spawn.
- Recovery restores the player inventory at the fallback position but does **not** rewrite `world_state.json`, preserving the last known-good save for manual inspection or future recovery.
- Runtime build version constants now report `6.14.7-dev` so the main-menu footer and startup log match the changelog/roadmap version.

**Roadmap Status:**
- Player Save Safety: **🛠️ WORKING ON → ✅ COMPLETED**.

**Files touched (pull these):**
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Core/GameVersion.cs`
- `Roadmap.md`
- `Changelog.md`

**Manual steps:** none — runtime-only safety fix. No `Tools > Voxel Engine > Voxel Engine Setup` run required.

---

### [6.14.6-dev] Roadmap Status Governance & Current Focus Sync

**Type:** PATCH — documentation/roadmap tracking only. Fully save-compatible; no runtime, prefab, recipe, setup, save-schema, or API changes.

**Changed:**
- Synced `Roadmap.md` header to 6.14.6-dev and the current local date.
- Added explicit roadmap maintenance rules: never remove planned roadmap content during status passes; update only status markers, evidence notes, dates, version pointers, and immediate-next-step labels/order unless Thomas explicitly asks otherwise.
- Normalized Current State Snapshot status labels to the standard roadmap convention (`WORKING ON`, `PARTIALLY COMPLETE`, `COMPLETED`, `MISSING`) and fixed a malformed markdown table separator.
- Promoted roadmap rows to **COMPLETED** where the roadmap/changelog already recorded Thomas validation: conveyor belts/chutes, grid/static lighting + LED strips, configurable grid screens, and camera block live feeds. Broader milestone rows remain **WORKING ON** where open tasks still exist.
- Marked old splitter/funnel immediate-next-step validations as **COMPLETED** and appended the current active focus: 6.14.x pipe/port validation, vehicle power foundations validation, Step 5 Size-V5 two-run validation, and the safer centralized transport tick migration plan.

**Files touched (pull these):**
- `Roadmap.md`
- `Changelog.md`

**Manual steps:** none — documentation-only. No `Tools > Voxel Engine > Voxel Engine Setup` run required.

---

### [6.14.5-dev] Aim-True Port Selection, Collider-True Pipe Seating & Pipe-to-Pipe Chaining

**Type:** PATCH — placement/UX fixes. Fully save-compatible.

**Fixed:**
- **Pipes STILL placing inside engine inputs** — two-stage root cause, both removed:
  1. *Port selection* measured distance from the ray's **surface hit point**; ports buried deep in the MGO hull can sit beyond the snap radius from the visible hull face, so the snap silently fell back to plain cell placement inside the machine. Ports are now ALSO matched by **distance to the aim ray line** (accepting buried ports up to snap-range past the hull; far-side ports the ray would tunnel through stay rejected).
  2. *Seating* used renderer-AABB bounds to escape the hull — replaced with a **reverse raycast against the machine's own colliders**: cast back down the port's authored facing from far outside, take the first machine face struck, seat the hub half a cell beyond it. Surface ports resolve to the exact snug plug from before; buried ports now land physically outside the engine, on its real shell (hitboxes included — armor bands, stepped silhouettes, all of it).
- **No ghost when aiming at another pipe (can't attach pipes to pipe ends)** — pipe arms and end-caps are collider-free visuals; only the small hub box was ray-hittable, so aiming anywhere along a run or at its open tip found nothing: no ghost, no placement. New **chain aim**: when the camera ray misses and you hold a pipe, a forgiving fat sphere-cast along the view grips the nearest same-family pipe hub and computes the Detail cell one step from it — forward along the run axis when aiming past the tip (continuation), sideways when aiming at the pipe's side (branch). The ghost shows exactly the cell the click places (red when the cell is occupied), shared by `BuildSystem` preview and the `PlayerInteractionTool` ray-miss click — pipe runs now grow like tracks.
- Placement tail for all unified pipes unified into one `PlaceOnDetailLattice` (port-snapped, detail-placed and chain-placed pipes all get the same lattice visuals, network refresh and feedback).

**Files touched (pull these):**
- `Scripts/Building/BuildSystem.cs`
- `Scripts/Maritime/MaritimePorts.cs`
- `Scripts/Player/PlayerInteractionTool.cs`

**Manual steps:** none — runtime-only. (No `Voxel Engine Setup` / Step 13 run required; MaritimeMeshBuilder stays v22.)

Maritime subsystem → **2.26.5** (see `Scripts/Maritime/CHANGELOG.md`).

---

### [6.14.4-dev] Pipe Seating Outside Engine Hull & Cable Network Performance

**Type:** PATCH — behaviour + performance fixes. Fully save-compatible.

**Fixed:**
- **Pipes placing INSIDE the engine body**: ports authored buried inside a machine's hull (MGO fuel/coolant/O₂ can sit metres inside the collider surface) only got the standard half-cell plug, leaving the pipe hub rendering inside the engine. The snap now walks the seat **out along the port's authored facing to the machine's rendered shell** and plugs half a cell beyond it — the pipe lands just OUTSIDE the engine like a free-placed pipe beside it. Surface ports keep the exact snug fit that already worked (HFO coolant untouched). The claimed Detail-lattice occupancy cell follows the hub, and shell bounds are cached per machine so the per-frame ghost preview costs nothing extra.

**Performance (cable networks — "20 cables = A LOT of lag"):**
- **Factory-wide re-mesh storm killed**: every topology rebuild fired `onNeighboursChanged` on EVERY power node, so placing ONE cable rebuilt the generated cable meshes of the ENTIRE factory (destroying/respawning ~20 GameObjects per cable — O(N²) churn across a cabling session). The event now fires only for nodes whose connection set actually changed (neighbour-signature compare). Placing cable #21 rebuilds the 1–2 cables it touches, not 21.
- **Placement bursts coalesced**: holding the place button marked the topology dirty every frame → full rebuild + all-pair line-of-sight raycasts every frame. Rebuilds now settle for 0.12 s and batch — one rebuild per burst.
- **Zero-alloc line-of-sight**: `PowerNode.CanLinkTo` switched from allocating `Physics.RaycastAll` (one fresh array per node PAIR per rebuild) to `RaycastNonAlloc` with a shared buffer.
- **Material leak fixed**: cable tier-collar materials (`GridCableVisuals`) were re-created PER cable PER rebuild and the old ones never released — now one shared cached material per tier colour.
- **DataCable probes de-allocated**: the twice-per-second neighbour probe used allocating `OverlapBox`/`RaycastAll` per cable — now `OverlapBoxNonAlloc`/`RaycastNonAlloc` with shared buffers (that all-layers 5 m broadphase box was a GC spike per cable per scan).

**Files touched (pull these):**
- `Scripts/Building/BuildSystem.cs`
- `Scripts/Power/PowerNode.cs`
- `Scripts/Power/PowerNetworkManager.cs`
- `Scripts/Networks/GridCableVisuals.cs`
- `Scripts/Networks/DataCable.cs`

**Manual steps:** none — runtime-only. (No `Voxel Engine Setup` / Step 13 run required; MaritimeMeshBuilder stays v22.)

Maritime subsystem → **2.26.4** (see `Scripts/Maritime/CHANGELOG.md`).

---

### [6.14.3-dev] Unified Pipe Placement = Ghost, Pipe-Lag Cures & O₂/Exhaust Plumbing Separation

**Type:** PATCH — placement-path unification, performance and network-purity fixes. Fully save-compatible.

**Fixed:**
- **Pipes ignoring port snaps on placement** (ghost snapped, block landed "closest to the player"): a second, older placement path in `PlayerInteractionTool` (`TryPlaceStaticPipeOnGrid`) raced the BuildSystem on every right-click and knew nothing about maritime ports. It's removed — unified pipes always place through `BuildSystem.TryPlace`, so the placed pipe lands exactly where the ghost showed (port-anchored, facing-correct). Its Detail-lattice visual sizing and immediate network refresh were carried into the single path.
- **Heavy lag with ~20+ pipes**: three hot scans were re-running physics corrridor probes far more often than anything changes:
  - Grid-liquid bridge (BFS + 5-cell corridors per endpoint per tick) → 0.6 s result cache per endpoint/type/direction.
  - Item-pipe endpoint corridor sweeps → memoized 0.5 s per pipe (bypassed instantly on wrench/config rescan).
  - Gas `FindTankNear` pipe BFS → memoized 0.35 s per query origin/type.
- **Oxygen not flowing & pipes not reaching tanks** — mostly a knock-on of the placement fork (pipes nowhere near the ports); with the snap unbroken, the 5-lattice-cell corridor + tank bridges from 6.14.2 do their job.
- **O₂ line also connecting to the exhaust pipe / exhaust gas poisoning the oxygen supply**:
  - Gas pipes now suppress exhaust-tap arms whenever a clean (non-tap) gas port is closer — the O₂ line plugs into the intake, not the tap standing beside it.
  - The exhaust tap only feeds exhaust into networks seeded by pipes **anchored to its own exhaust pipe block** (the dedicated capture run). A nearby oxygen line stays clean — and the engine O₂ feed keeps finding breathable gas.

**Manual Unity Steps:** copy the changed scripts in, recompile, play. No Step-13 run needed for this one (runtime-only changes).

---

### [6.14.2-dev] Port-True Pipe Seating, Shaft Coupling Rule, Grid-Tank Classic Bridge & Lattice-True 5-Cell Probes (Mesh v22)

**Type:** PATCH — behaviour fixes on the 6.14.0 systems. Fully save-compatible; meshes rebuild in place via the v22 marker.

**Fixed:**
- **Pipes no longer land "beside" the port** (MGO fuel/coolant, HFO fuel): the placement anchor is now the port face pushed half a pipe-cell **straight out along the port's authored facing** — the hub plugs into the port from outside, centred, ghost ≡ placed.
- **O₂ intake placement offset**: same anchor rule fixes it on all three engines.
- **O₂ port orientation wrong on all engines** (and any edge-on port markers): ports no longer rotate their whole container (which turned disc markers sideways). The marker PRIM now faces outward with correct authoring (thin-Z discs on O₂/exhaust/gas-tap/item-intake/water-pump ports; cylinders point their axis along the facing).
- **Drive shaft placed halfway inside an already-placed shaft / not coupling to the gearbox**: shaft-driven blocks now mount **half a cell out along the port facing**, so coupling rings kiss flange-to-flange — shafts, gearboxes, generators, propellers all couple correctly. Exhaust pipes keep their plug-in centring.
- **Liquid/gas pipes still not seeing tanks**: probes now measure **five LATTICE cells using the host grid's own cell size** (2.5 m Large) — previously mounted pipes probed five tiny 0.5 m cells and missed tanks standing one or two grid spaces away.
- **The big grid LiquidTank finally joins the classic liquid pipe graph**: new `LiquidTankClassicAdapter` (a WaterTank-shaped shim mirroring stored litres both ways, installed non-destructively by Step 13) — classic liquid pipes link to the tank at five lattice cells and content stays in sync whichever side fills or drains.
- **O₂/fuel actually flowing to engines**: consequence of the above (corridor probes use the right step on every path; the tank is now a first-class network member).

**Manual Unity Steps:** copy the changed scripts (incl. NEW `Scripts\GridSystem\LiquidTankClassicAdapter.cs`), recompile, run **Step 13** once (meshes → v22, tank bridge installed — non-destructive).

---

### [6.14.1-dev] Step-13 Fix: MGO Banded-Hitbox MissingComponentException

**Type:** PATCH — editor-tool crash fix, no runtime change.

**Fixed:**
- `MissingComponentException` at `BoxCollider.set_center` when running Step 13: the MGO's banded hitbox used `GetComponent() ?? AddComponent()` followed by an unguarded property set. The C# `??` operator does not respect Unity's object-lifetime semantics, and this tool's prefab pipeline is documented to transiently invalidate freshly added components — so `set_center` could fire on a dead wrapper and abort the whole content build.
- Replaced with `FitBandHitbox(...)`: find-or-create each band child, explicit `== null` checks, re-fetch after add, and a never-throw guard that warns and retries next run instead of crashing. Root-box re-fetch added on the same path.

**Manual Unity Steps:** copy `Scripts\Editor\VoxelEngineSetupWindow.cs`, recompile, run **Step 13** again — it completes and the MGO gets its slim-lower / full-upper hitbox pair.

---

### [6.14.0-dev] Oriented Ports, 5-Cell Endpoint Proximity, Flex Exhaust Couplings, Ghost-True Placement & MGO Banded Hitbox (Mesh v21)

**Type:** MINOR — new save-compatible connectivity features + placement/snap fixes. Old saves load cleanly (new pose-save fields are optional on read; mesh rebuilds in place via the v21 marker).

**New — 5-cell endpoint proximity ("valid lattice direction") for every pipe family:**
- Tanks/containers/machines now join a pipe run from **up to five lattice cells away on a straight cardinal row** — no pipe needs to physically hump the tank shell anymore:
  - **Classic liquid** (`FluidNetworkManager`): pipe↔tank/pump links use the same 5-cell cardinal rule pipes already had.
  - **Gas**: tanks reachable from any pipe — or straight off a consumer port (engine O₂ intake, exhaust gas tap) — via a new cardinal corridor probe.
  - **Item pipes**: chests/machines connect and accept delivery from up to five cells in a valid direction; glass-pipe pellets animate the full hop.
  - **Grid liquid bridge**: classic tanks near (≤5 cells, cardinal) any pipe walked from a machine's liquid ports count as connected.

**New — True authored port orientation + exact port-centred placement:**
- Every maritime port now carries a `MaritimePortFacing` tag and a rotated container (+Z = outward attach direction): all three engines (exhaust, O₂, fuel, coolant, shaft), gearbox, exhaust pipe (intake + gas tap), drive-shaft coupling rings and the marine water pump. Liquid-tank markers get the tag too — linked non-destructively when missing.
- Snapped blocks now **place exactly ON the port, oriented along its real facing** — no more "middle of the engine", no more off-by-half-a-cell: ghost and placed block are pixel-identical, and the exact pose **persists through save/load** (new optional `localPosition` field in grid-block save records).

**New — Flex exhaust couplings:** every exhaust pipe grows a bellows stub that seals its intake flange to the served engine's REAL exhaust port (rescan at 2 Hz) — port-snapped exhaust runs always look welded shut, on every engine tier.

**Fixed:**
- **MGO (and any overhang machine) no longer sinks into its supporting face on placement** — `TryPlaceBlock` now keeps the exact ghost pose instead of the raw lattice cell.
- **Exhaust pipe snap on the smaller engines** no longer lands mid-block (cell resolution now follows the port's facing, not a position guess).
- **MGO exhaust snap** centred on the port instead of hugging its far end.
- **Liquid snap on the MGO works** (snap radius spans machine internals; ports sit up to ~4 m inside the collider surface).
- **Gas pipe → exhaust gas-tap snap works**; **gas pipe → O₂ intake is centred on the port**.
- **Engines actually draw O₂** from tanks that aren't touching the pipes (corridor probe + wider rescan reach).
- **MGO hitbox** swapped for a two-piece banded collider (slim lower band ≈62% width, full-width upper band) — walk right up to the crankcase. Refit rides the same non-destructive Step-13 gate (manual collider edits survive).
- **Steam/heat port removed** (mesh + prefix lists) — exhaust is the single hot-gas hookup, as requested.
- Round-half-up cell rounding (banker's rounding dragged half-cell ports off-centre).

**Manual Unity Steps:** copy the changed scripts in, recompile, run `Tools > Voxel Engine > Voxel Engine Setup` → **Step 13** once (rebuilds meshes to v21 in place, retrofits the MGO banded hitbox, tags tank-port facings — all non-destructive).

---

### [6.13.2-dev] Drive Shaft Floor Mounts Fully Removed (Mesh v20)

**Type:** PATCH — visual bugfix, fully save-compatible. Step 13 rebuilds the shaft mesh in place, non-destructively.

**Fixed:**
- **The driveshaft's pillow-block bearing pedestals are gone for good.** v18 removed the ground feet but the two cast-iron pillow-block stands (bodies, caps, race rings and grease nipples) stayed and still read as "floor mounts" — and they hung *below* the shaft line, clipping through decks and hulls. v20 deletes them entirely: the shaft is now a **pure floating shaft line** — end coupler flanges with bolt circles, full-cell polished rod with spline ribs, keyway, U-joint yoke, clamp collars and the gold `Port_ShaftIO` coupling rings. Couples port-to-port between machines with nothing standing on the deck.
- Box collider auto-refits to the slimmer bounds on the Step-13 rebuild (existing non-destructive gate: `isNew || needsMesh || colliderMissing`).

**Manual Unity Steps:** copy `Scripts\Maritime\MaritimeMeshBuilder.cs` into your clone, recompile, then run `Tools > Voxel Engine > Voxel Engine Setup` → **Step 13** once (marker `__MaritimeMesh_v20` forces the mesh rebuild in place — your balancing, scripts and item data are untouched).

---

### [6.13.1-dev] Compile Fix (CS0126) + Full Unity 6 Deprecated-Lookup Warning Sweep

**Type:** PATCH — pure bug fixes, zero behaviour change, fully save-compatible.

**Fixed:**
- **CS0126 compile error** in `GridBuilder.TryApplyMaritimePortSnap` (line 1020): a valueless `return;` inside the new `bool`-returning port-snap path broke the build on 6.13.0-dev. Now returns `false` so the placement simply falls back to normal rules when the port axis can't be resolved.
- **All CS0618 deprecated-find warnings swept repo-wide** (same warning class Thomas pasted, plus the identical sites that would surface right after): 
  - `GridLightBlock`, `LEDStrip`, `GridSlidingDoor` (motion sensors): `FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)` → the non-deprecated `(FindObjectsInactive)` overload. `None` never sorted, so order/behaviour is unchanged.
  - `CrestWaterSetupUtility` (5 direct calls) + reflection helper: now resolves the non-deprecated `FindObjectsByType(Type, FindObjectsInactive)` overload — identical results, no obsolete enum reference.
  - `CrestFlowSampler`, `WaterMeshBuilder`: same overload migration.
  - `GridShapeVariantSetup` (editor, ×4) + `CrestVoxelWaterBinder`: `FindObjectOfType` → `FindFirstObjectByType`.

**Manual Unity Steps:** copy the changed scripts into your clone, let Unity recompile — the console should now show **0 errors, 0 warnings** from this family.

---

### [6.13.0-dev] Chained Drive Shafts Physically Touch + Snap In Perfect Extension

**Type:** MINOR — save-compatible visual/link improvement (MaritimeMeshBuilder v19). Old saves load cleanly; existing shafts keep working and pick up the new look on the next Step-13 run.

**New:**
- **Shafts touch when chained**: the spinning chrome rod now spans the full cell, so two drive shafts placed in extension of each other meet exactly at the shared cell face — one continuous driveline, no more floating gap.
- **Gold coupling rings at both rod tips** (`Port_ShaftIO_F` / `Port_ShaftIO_B`): chained shafts meet ring-to-ring like a real bolted flange coupling — and because they're named shaft ports, a **held shaft now snaps exactly in extension** of a placed one (aim at the shaft, it magnetises onto the far tip's cell, collinear automatically).

**Changed:** MaritimeMeshBuilder **v19**. Keyway bar lengthened to match the full-cell rod.

**Manual Unity Steps:** copy the changed script, then run `Tools > Voxel Engine > Voxel Engine Setup` → **Step 13** once (rebuilds the driveshaft visual in place, non-destructively).

---

### [6.12.0-dev] Engine Oxygen, AIP Module, Universal Port Snapping, Placement Guards & Truly Non-Destructive Step 13

**Type:** MINOR — new save-compatible gameplay systems (engine oxygen requirement + Closed-Cycle AIP upgrade module + exhaust-gas tap), large placement/snapping fixes, and four reworked machine visuals (MaritimeMeshBuilder v18). Old saves load cleanly: socketed modules persist by item id; the new module kind appends to the end of the `EngineModuleKind` enum; engine oxygen starvation is runtime-only and never damages placed blocks.

**New — Engines Breathe Oxygen:**
- Every maritime engine now needs a small supply of **O₂** for combustion: an internal buffer (sips at 0.25 O₂-units per fuel unit) refills from any gas-tank reachable via **gas pipes plugged into the engine's new `Port_OxygenInput`** (air-filter housings visible on all three engine tiers). Starved engines stall harmlessly — no damage, no fuel burn — and the panel/screen warns clearly.
- New upgrade module: **Closed-Cycle AIP Module** (all tiers). Solid chlorate oxygen candles thermally decompose to release chemically-bound O₂ while a regenerative CO₂-scrubbed exhaust-recirculation loop returns the un-consumed oxygen fraction to the intake — the engine carries its own oxidiser, so the external air requirement disappears. Trade-off: +5% fuel use. Crafted from steel plates, advanced circuits, copper wire and glass.

**New — Exhaust Gas Tap (foundation for the concealed-space atmosphere sim on the Roadmap):**
- Exhaust pipes gained a chrome **`Port_ExhaustGasIO`** top flange. Gas pipes snapped onto it route captured exhaust into gas networks as the new **`GasType.ExhaustGas`** (storable in gas tanks) — captured exhaust visibly **thins the smoke plume**. Gas pipes snap to gas ports only; liquid pipes snap to liquid ports only.

**Fixed — Snapping:**
- **MGO exhaust snap**: the lattice-neighbour gate fired *before* the port snap and the MGO's collectors sit two cells from its origin cell, so the snap was unreachable — port mounts are now tried first, from ANY aim point on the machine (aim anywhere, it centres on the port's exact cell, per the HFO behaviour).
- **Exhaust orientation** follows the port's dominant axis: MGO top collectors raise a vertical stack, side ports lay a horizontal run reaching slightly outboard.
- **Fuel port snap + coolant placement**: the MGO's box collider was ~1.2× inflated (root scale double-applied in Step 13) and physically stood between your ray and the port — collider now fits exactly, snap range widened, and pipes land on the port's own Detail cell.
- **Gas pipes ⇄ exhaust pipes** snap both ways (exhaust held → gas pipe aims; gas pipe held → exhaust gas tap aims).

**Fixed — Placement physics:**
- **No more blocks inside the player** (no more being shoved upwards) and **no more blocks buried in terrain/other constructs** (no more grid kicks): a world-space obstruction test rejects both (GridBuilder + classic BuildSystem).
- **MGO bottom-alignment**: tall machine models placed on top of a block now rest their visual bottom ON the supporting face instead of sinking their overhang into it.
- Pipe visual **arms aim at the machine's actual port** (fuel/coolant/tank/oxygen/gas tap) instead of skewing inline through the block centre.

**Fixed — Liquid pipes ⇄ liquid tanks (final):** the grid liquid system and the classic FluidNetwork are now bridged — any pipe run touching a liquid port/body connects **both** tank types (`GridLiquidTank` and classic `WaterTank`) to engines, pumps and radiators, in both directions. Liquid tanks also gained visible **`Port_LiquidIO`** markers (N/S/E/W/Top) pipes snap to.

**Changed — Item block properties actually work:** placed blocks take **name, mass, max HP and current HP from the GridBlockItem asset**; the engine's tier auto-config no longer stomps the item's name (fallback-only). MaritimeMeshBuilder **v18** visuals: straight no-support exhaust stack, driveshaft without ground feet, oxygen intakes on engines, gas-tap flange.

**Changed — Step 13 is now strictly non-destructive:** prefab colliders re-fit ONLY on create/mesh-rebuild/missing (and fit correctly under root scale), block items/recipes/config populate only when new, missing scripts/materials are linked and broken recipe chains restored — your names, masses, HP, balancing values and prefab edits survive every re-run.

**Manual Unity Steps:** copy the changed scripts in, let Unity compile, then run `Tools > Voxel Engine > Voxel Engine Setup` → **Step 13 (Build Maritime Content)** once. It rebuilds the four v18 visuals in place, adds the oxygen ports/air filters, the gas-tap flange, the tank's liquid ports and the AIP module + recipe — non-destructively.

---

### [6.11.1-dev] Compile Fix — WaterPipe `CellSize` Extension Resolution

**Type:** PATCH — compile-error fix only; no behaviour, asset, or save changes. Old saves load cleanly.

**Fixed:**
- `CS1061` in `Assets/Scripts/Fluids/WaterPipe.cs` (lines 52 & 79): the liquid-pipe visual arm code introduced in 6.11.0-dev called the `GridSize.CellSize()` extension method without importing `VoxelEngine.GridSystem`, which blocked compilation. Both call sites now use the repo's standard fully-qualified static form `VoxelEngine.GridSystem.GridSizeExt.CellSize(...)` — identical runtime behaviour (proximity arm radius for grid-attached liquid pipes).

**Manual Unity Steps:** none — drop in the file, let Unity recompile, done. (Step 8 / Step 13 re-runs are NOT required; no assets changed.)

---

### [6.11.0-dev] Ground-Safe Placement, Exact Port Snapping, Liquid Network & Engine Realism Pass

**Type:** MINOR — new save-compatible gameplay systems (heat-seizure repair, free-ratio gearbox, realistic torque curve), large placement/connection fixes, and three rebuilt machine visuals (MaritimeMeshBuilder v17). Old saves load cleanly; placed gearboxes clamp into the new ratio range on first tick; seized-engine state is runtime-only and starts clean on load.

**New — Free-Ratio Gearbox:**
- The gearbox no longer has 20 fixed gears: type ANY ratio (0.25× – 20.0×) in the panel's input field or drag the new slider. Value applies live, no graph rebuild — high numbers for generators, low numbers for heavy props.

**New — Realistic Engine Torque & Stress:**
- Marine-diesel torque curve: available torque **sags as shaft speed climbs** (≈1.18× at idle → 0.58× at redline), so more speed really means less pull.
- Stress now rises with shaft speed plus load-versus-curve, with back-pressure and heat on top — running near redline genuinely overworks the engine, and an overstressed engine runs 35% hotter.
- Engine panel explains the curve under the speed readout.

**New — Heat-Seizure Repair:**
- Reaching 100 °C now **seizes** the engine permanently: the latch no longer auto-clears on cooldown. The engine panel opens an *Emergency Repair* section listing spare parts (a subset of that engine's own crafting recipe — e.g. iron plates/gears for HFO, steel/gears/circuits for MGO). Cool below 80 °C, have the parts in your inventory, press **REPAIR**.
- Spare-part lists are authored per engine prefab by Step 13 and only filled when empty (hand-tuned prefabs are preserved).

**Fixed — Placement:**
- **Blocks can no longer be placed into the ground, no matter their size.** Free-standing (new-construct) placements compute the prefab's real rotated render bounds and lift the pose along the surface normal until the lowest point clears the ground — the MGO V12 finally stands on its feet instead of sinking to its cylinders.
- **Build reach doubled: 8 m → 16 m** (both builders; stale serialized 8 m values on older player prefabs auto-upgrade).

**Fixed — Snapping & Connections (works with grid mode ON or OFF):**
- Maritime port snap now converts the port's **actual world position** to its lattice cell — exhaust pipes, drive shafts, gearboxes, generators and propellers magnet onto the engine's real port (even 2 cells out on the big models) instead of a cell beside the block's origin. This also fixes drive shafts ending up *inside* the medium engine.
- **Liquid pipes snap to liquid ports** (fuel/coolant/steam intakes) in both ghost preview and final placement — regardless of grid mode.
- **Liquid tanks now actually connect to liquid pipes**: the grid liquid network no longer relies on lattice face-touch alone — pipes within world reach of a machine/tank body or its named liquid ports join the network, and pipe visuals draw their arms across those links.
- Exhaust detection switched the same way (face OR proximity/ports), so engines see correctly-snapped pipes **and** the exhaust pipe finally reports *venting* + emits smoke again instead of "no active engines adjacent".

**Fixed — Block Info HUD:**
- Top-left inspection card now walks the ENTIRE ray until something resolvable is found and skips ghosts/viewmodels/held rigs — block info displays with items or tools in hand, and its probe distance grew to match the 16 m reach.

**New Visuals (MaritimeMeshBuilder v17 — rebuilt in place by Step 13):**
- **Exhaust Pipe**: bolted base flange, engine-side elbow with heat-tinted intake lip, tapered three-stage stack with weld beads and heat bands, dark inner throat, rain cap on tripod legs, side support braces, red `Port_ExhaustInput`.
- **Drive Shaft**: real line shaft — two pillow-block bearing pedestals with bolted feet and grease nipples, end flanges with six-bolt circles, and a spinning assembly of polished shaft, splined mid-section, brass keyway, U-joint yoke and clamp collars.
- **Maritime Generator**: skid rails, finned stator barrel with brass frame ties, rear fan cowl with vent slots, top terminal box with cable glands — and a **wide-open front bell with a safety-yellow guard ring** so the spinning driveshaft input coupling and stub shaft are always visible (gold `Port_ShaftInput` beyond the ring).

**Manual Unity Steps:**
1. Pull/reload `Dev` and let Unity compile (expect 0 errors, 0 warnings).
2. Open `Tools > Voxel Engine > Voxel Engine Setup`, run **Step 13** once — v17 rebuilds exhaust/shaft/generator in place and fills engine repair costs (no prefab deletion needed this time).
3. Run **Step 8** once to rename pipes to "Liquid Pipe (Solid/Glass) · 0.5 m".
4. Place an MGO engine on terrain → it rests on its feet; aim at any block from >8 m → HUD info appears; hold coal + look at a block → card still shows.
5. Hold an exhaust pipe near an engine's stack → it snaps to the exact port cell; hold a liquid pipe near an engine fuel/coolant flange → it snaps; confirm the tank feeds the engine (fuel gauge rises).
6. Open a gearbox: type `6` in the ratio field (or drag the slider) → output spins at 6×; throttle an engine up: torque readout sags at high RPM and stress climbs; overheat an engine → it seizes and the *Emergency Repair* section appears.
7. Run an engine with a snapped exhaust pipe → smoke puffs/columns/streams per tier and the pipe UI says *venting*.

---

### [6.10.1-dev] Compile Fix + Unity 6.4 GetEntityId Migration

**Type:** PATCH — compile error and deprecation-warning cleanup only; no gameplay change, no save schema break.

**Fixed:**
- **Compile error CS1501** in `MaritimeAnimator.SpinY` (a `Transform.Rotate` call passed 5 arguments — the maximum is 4). The maritime animator compiles again.
- **All CS0618 warnings (`Object.GetInstanceID()` is obsolete in Unity 6.4) migrated to `GetEntityId()`** — every one of the 9 call sites:
  - `Building/BuildSystem` — pipe-ghost candidate dedup (`HashSet<EntityId>`) and ghost target tracking (`EntityId _pipeGhostTargetId`).
  - `GridSystem/GridCameraBlock` — screen→camera feed consumer registry and its prune list now keyed by `EntityId`; feed camera and render-texture names use the new id.
  - `GridSystem/GridScreenBlock` — `dataSourceInstanceIds` is now `List<EntityId>`; `ToggleSource` / `SetPrimarySource` signatures updated accordingly.
  - `GridSystem/UI/GridScreenConfigUI` and `Power/Wind/WindTurbineUI` — source selection ids and the per-turbine scroll-restore owner id via `GetEntityId()`.
- **Save compatibility note:** screen source instance ids are session-local handles and were never stable across launches (old saves stored plain ints; 6.10+ stores `EntityId` text). Loading now seeds `EntityId.None` per source and lets `ResolveAllProviders()` re-bind the live ids on first read — identical behaviour, and positions/ids now stay strictly index-aligned.

**Manual Unity Steps:**
1. Pull/reload `Dev` and let Unity recompile — expect **0 errors, 0 warnings**.
2. Open a previously configured grid screen → data sources re-bind automatically and still display.
3. Open two different wind turbines in sequence → scroll position is still remembered per turbine.
4. Place/verify a maritime engine → pistons/crank animate (this is the file the compile error was blocking).

---

### [6.10.0-dev] Maritime Upgrade Modules, 20-Speed Gearbox & Premium Engine Rebuild

**Type:** MINOR — new save-compatible feature content (engine modules, heat system, generator speed bonus, 20-speed gearbox), plus bug fixes. Old saves load cleanly; placed blocks auto-migrate (module containers start empty, legacy 2000 RPM gearboxes migrate to the 10000 RPM cap on placement/tick).

**New — Engine & Generator Upgrade Modules:**
- New socketable **`Engine Upgrade Modules`** (new `EngineModuleItem` asset type) that slot into new **Module Slots** on engines (2/3/4 slots by tier) and maritime generators (2 slots) — open the block panel and drop modules in:
  - **High-Flow Turbocharger** (T1/T2/T3 engines): +20% output power, +10% RPM cap, +10% fuel use, faster exhaust smoke.
  - **Efficiency Tuning Chip** (T2/T3 engines + generators): +40% max output power, −15% fuel use — **unlocks a mandatory active-coolant requirement** (overheats in ~15 s without continuous coolant flow).
  - **Overclocked Fuel Injectors** (T2/T3 engines): +30% output power, +15% speed cap, +50% heat generation, dirty soot-black exhaust.
  - **Super-Cooler Radiator Jacket** (T2/T3 engines + generators): +200% heat dissipation, draws a continuous fresh/sea water feed (2 L/s per jacket) from connected tanks.
- Modules hot-swap live (no rebuild), persist in saves, support shift-click quick transfer, and are enforced one-per-kind per slot (maxStack 1).
- New **research tier 5: Maritime Performance Tuning** (after MSC Loreto-class Propulsion) unlocks all four module recipes at the Assembler.

**New — Engine & Generator Heat Model:**
- Live temperature on every maritime engine and generator: normal below 90 °C, **knocking** ≥ 90 °C (−25% fuel efficiency), **critical mechanical failure** ≥ 100 °C (shaft stops, heavy black smoke, latched until cooled below 80 °C).
- Radiator water draw, coolant flow, and heat are shown on the block panels (temperature stat, heat bar, knocking/critical warning pills, radiator flow status).
- Married to smoke: critical heat makes any adjacent exhaust stack belch heavy black smoke at increased rate; Overclocked Injectors darken the plume; High-Flow Turbochargers raise exhaust velocity.

**New — Generator Speed Bonus:**
- Maritime generators now earn up to **+50% output** as shaft speed approaches rated RPM (`MaritimeSettings.generatorSpeedBonus`, default 0.5) — gearing up into a generator finally pays off.

**New — Premium Engine Models (MaritimeMeshBuilder v16):**
- **Tier 1 Crude Inline-4** (~2 m): chipped blue-green cast iron, open-frame crankcase with a visible crankshaft, four open-air pistons, exposed pushrods + valve springs, grease-stained rear SAE drive flange.
- **Tier 2 HFO V8** (~4×2×2 m): faded-yellow 90° V-block, glass-paneled framed inspection windows on both flanks revealing the banks and crankshaft, valley intake plenum, HFO heating manifold with steam-traced fuel filters, geared output housing, insulated heat-discoloured (blue/orange) exhaust stack.
- **Tier 3 MGO V12** (~8×4×3 m): anodized red/silver precision diesel, dry-sump pan, electronic valve-train covers, four armored quartz viewing ports, gantry walkways with railings and access ladders along the whole block, four turbo trunks off the central exhaust plenum, front accessory belt driving an animated **SeaPump** seawater pump, massive splined PTO shaft in a bearing housing.
- All three share one deterministic crank angle: crankshaft, output shaft, pistons (true firing-order phases 1-3-4-2 / 1-8-4-3-6-5-7-2 / 1-12-5-8-3-10-6-7-2-11-4-9) and the SeaPump stay in lock-step, and pistons slide along their tilted V-bore axes. `engine_speed` (0–1) drives crank RPM and piston playback rate simultaneously.
- Exhaust smoke is now tier-styled: T1 pulsating RPM-synced puffs, T2 steady thick dark-grey column, T3 clean fast blueish-white stream.
- Invisible locator sockets (`Socket_*`, standard axes) are included in each engine prefab for alignment reference.

**Fixed / Improved:**
- **Gearbox gear ratio actually works now**: gear changes apply LIVE every tick (previously only refreshed on graph rebuild), the Input RPM readout was inverted (multiplied instead of divided), and the 2000 RPM output clamp that silently killed gears above ~1.3× on stock engines is gone (10000 cap, legacy blocks auto-migrate).
- **Gearbox is now 20-speed and truly bidirectional**: 20 selectable ratios (0.25×…6.00×) applied from the panel, and the job is tree-aware (BFS parent map) so power can enter EITHER side — the far side becomes the output. Branched drivetrains no longer leak gearbox ratios into sibling branches, and a generator only sinks torque on its own branch.
- **Crude engine "fuel seconds" were not seconds**: the buffer readout only matched reality at full throttle. It now shows an honest ETA (`EstimatedFuelSecondsRemaining` at the CURRENT draw rate, formatted as `1h 05m` / `2m 14s` / `43s`) on solid and liquid engines alike.
- **Shift-click coal into the fuel hopper works**: quick-transfer now routes burnable items to the engine hopper and modules to the module sockets (engines + generators) instead of falling through to network routing.
- **Maritime blocks and screens can be broken/mined**: LMB with any tool (or bare hands) damages grid blocks with proper tool strength/rate/durability; grinding and breaking now return the correct item via the block's `SourceItem` with a normalized name-search fallback ("Screen (Small)" ↔ `Screen_Small`), and removal works on the precision attachment layer instead of silently failing.
- **Screen datatypes for engines and generators**: `GridMaritimeEngine` and `GridMaritimeGenerator` now implement `IGridDataProvider` (categories "Maritime Engines" / "Maritime Generators") so grid screens can display RPM, torque/power, fuel ETA, heat and buffer status.
- **Several batteries now charge together**: surplus generation is shared equally across all Recharge/Auto batteries on a grid (water-filling rounds re-offer leftovers from full packs); discharge demand is likewise spread instead of draining only the first pack.
- Save system: engine fuel hopper + module sockets serialize together (legacy fuel-only saves fill the hopper and leave sockets empty), generator sockets persist, and multi-container (de)serialization stays aligned when a container is null.

**Manual Unity Steps:**
1. Pull/reload the `Dev` branch and let Unity compile.
2. In the Project window, **delete the three old engine prefabs** so the new v16 models are authored fresh:
   - `Assets/VoxelEngine/Maritime/Prefabs/Engine_Small_Large.prefab`
   - `Assets/VoxelEngine/Maritime/Prefabs/Engine_Medium_Large.prefab`
   - `Assets/VoxelEngine/Maritime/Prefabs/Engine_Giant_Large.prefab`
   (Item assets keep working — Step 13 reconnects `blockPrefab` automatically.)
3. Open `Tools > Voxel Engine > Voxel Engine Setup`.
4. Run **13. Build Maritime Content** once — it rebuilds the three engine prefabs with the new models, creates the four upgrade-module items + recipes, adds research tier 5 (Maritime Performance Tuning) and updates gearbox defaults. The step is non-destructive: existing prefabs/items/recipes keep your edits.
5. Place a Crude / HFO / MGO engine and confirm the new visuals, moving pistons (firing order), spinning crank + output shaft, and (V12) the belt-driven SeaPump.
6. Open an engine: check the fuel ETA line, the thermal section, and socket modules into the Module Slots; confirm the heat gauge rises without coolant when an Efficiency Tuning Chip is installed.
7. Shift-click coal with an engine panel open → it lands in the fuel hopper; shift-click a module → it lands in the module sockets.
8. Open a gearbox: 20 gear buttons apply live; feed power into either side and confirm the other side outputs at ratio.
9. Wire generators to grid batteries: multiple batteries now charge together; open a grid screen and add engine/generator data sources.
10. Break a maritime block and a screen with a normal tool (and with the grinder) and confirm you get the item back.

---

### [6.9.3-dev] Maritime Idle Output + Port Snapping Pass

**Type:** PATCH — drivetrain feedback, animation, and placement-snap refinement only (no save schema break, no balance reset, no API touch).

**Fixed / Improved:**
- Maritime engines now support a low **idle shaft output** when enabled, fueled, and properly exhausted, so they no longer appear permanently dead at zero helm throttle.
  - This gives visible idle behavior, low RPM output, fuel usage, and piston/flywheel motion even before throttle increases.
- Added missing visual drivetrain animation coverage:
  - `GridEncasedChainDrive` chain rotor now animates.
  - `GridRotationTransfer` bevel/transfer rotor now animates.
  - Engine output couplings now animate.
  - Maritime generator visible input coupling now animates.
- Maritime generator mesh now has a clearer visible rotational input coupling.
- Exhaust pipe visuals were rebuilt into a more directional exhaust piece with a connector side so snapping to engine exhaust positions reads better.
- Added **maritime port-aware placement snapping** in `GridBuilder` for:
  - Exhaust pipes
  - Drive shafts
  - Rotation transfers
  - Encased chain drives
  - Gearboxes
  - Maritime generators
  - Shaft-driven propellers
- These parts now try to snap to the nearest believable matching maritime port on the targeted engine or drivetrain block instead of only using generic adjacent-cell placement.
- Bumped `MaritimeMeshBuilder` again so Step 13 regenerates the updated couplings/exhaust visuals.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.9.3-dev`.
2. Open `Tools > Voxel Engine > Voxel Engine Setup`.
3. Run **13. Build Maritime Content (Hulls, Engines, Shafts, Propellers, Turbo, Helm + Maritime Research Tree)** once.
4. Place a Crude, Heavy Fuel Oil, or MGO engine with fuel and exhaust attached.
5. Confirm the engine now idles visibly instead of only reading as dead/idle forever.
6. Confirm pistons, flywheel/output coupling, and connected drivetrain pieces visibly move.
7. Place an exhaust pipe while aiming near an engine exhaust area and confirm it snaps to the correct adjacent position more naturally.
8. Place shafts/generator/propeller/gearbox parts while aiming near visible shaft ports and confirm placement snaps to a matching drivetrain port more cleanly.

---

### [6.9.2-dev] Engine Port Realism + Anti-Hollow Maritime Visual Pass

**Type:** PATCH — visual polish and port-placement refinement only (no save schema break, no balance reset, no API touch).

**Improved:**
- Reworked the three ship engines again to look less hollow and less synthetic.
- Moved major visible **rotation output / flywheel faces** into more believable front power-takeoff positions.
- Repositioned visible **fuel**, **coolant**, and **exhaust** ports so they sit where the machinery actually suggests they should.
- Updated visual **turbo attachment markers** so they appear on sensible engine locations rather than generic cell-offset positions.
- Improved the front machinery faces on the Crude, Heavy Fuel Oil, and MGO engines so the drive output now reads more like an intentional ship-engine assembly.
- Added more believable service-point placement across the regenerated maritime machinery while keeping Step 13 non-destructive.
- Bumped `MaritimeMeshBuilder` again so Step 13 regenerates the improved engine visuals automatically.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.9.2-dev`.
2. Open `Tools > Voxel Engine > Voxel Engine Setup`.
3. Run **13. Build Maritime Content (Hulls, Engines, Shafts, Propellers, Turbo, Helm + Maritime Research Tree)** once.
4. Inspect the regenerated Crude Engine, Heavy Fuel Oil Engine, and MGO Engine prefabs.
5. Confirm the visible fuel/coolant/exhaust/rotation ports now sit in more believable positions.
6. Confirm the flywheel/output faces read more like real ship machinery and less like hollow procedural shells.
7. If needed, send me updated Unity screenshots and I can keep refining toward an even more hand-authored industrial look.

---

### [6.9.1-dev] Engine Visual Densification + Reference-Inspired Ship Machinery Pass

**Type:** PATCH — visual polish and prefab-regeneration refinement only (no save schema break, no balance reset, no API touch).

**Improved:**
- Reworked the regenerated **Crude Engine**, **Heavy Fuel Oil Engine**, and **MGO Engine** meshes to feel much more solid and much less hollow.
- Added heavier body fill, denser housings, deeper front timing cases, thicker superstructure, more side structure, and larger visible flywheel treatment across the ship engines.
- **MGO Engine** now includes a far more massive upper engine-room silhouette inspired by the supplied reference image: large scavenging deck, raised top housing, side railings/catwalk language, and a heavier front machinery face.
- **Heavy Fuel Oil Engine** gained a denser top section, added rail/superstructure details, and a larger flywheel treatment.
- **Crude Engine** gained a more convincing starter-engine massing and flywheel presence.
- These changes continue using Step 13 regeneration and preserve the non-destructive authoring workflow.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.9.1-dev`.
2. Open `Tools > Voxel Engine > Voxel Engine Setup`.
3. Run **13. Build Maritime Content (Hulls, Engines, Shafts, Propellers, Turbo, Helm + Maritime Research Tree)** once.
4. Inspect the regenerated **Crude Engine**, **Heavy Fuel Oil Engine**, and **MGO Engine** prefabs in Unity.
5. Place all three in-world and confirm they now read as dense, solid ship machinery rather than hollow shells.
6. If you want, send me fresh screenshots from Unity and I can do another art pass focused specifically on silhouette, flywheel scale, or top-deck detail.

---

### [6.9.0-dev] Maritime Power Reliability + Monumental Ship Engine Pass

**Type:** MINOR — new save-compatible maritime power and Step 13 content pass (battery behavior, liquid-pipe engine hookups, solid-fuel hopper, and regenerated ship-engine visuals; no save-schema break).

**Fixed / Improved:**
- **Grid battery transfer modes:** battery charge/discharge now resolves through one central Grid power pass instead of independent per-battery `Update()` timing.
  - A battery set to **Discharge** can now actively feed a battery set to **Recharge**.
  - **Auto** batteries no longer behave unpredictably when explicit discharge/recharge batteries are on the same grid.
  - Battery panels now show live charging and discharging wattage.
- **Unified liquid-pipe hookup for maritime systems:** maritime engines and marine water pumps are now valid liquid-pipe endpoints.
  - Heavy Fuel Oil and MGO engines can draw fuel through connected liquid pipes.
  - Medium/Giant maritime engines can now receive coolant through the same liquid-pipe topology.
  - Marine Water Pump now fills connected liquid-pipe networks instead of only iterating raw tank lists.
- **Crude Engine fuel access:** the crude engine now has a dedicated internal **solid-fuel hopper** (4 slots) for coal, planks, logs, and other valid solid fuels, while still supporting cargo fallback.
  - Hopper contents persist through world save/load.
  - Maritime engine UI now shows the hopper inventory for solid-fuel engines.
- **Step 13 maritime prefab generation:** Step 13 now rebuilds ship-engine content with updated premium large-scale visuals by bumping `MaritimeMeshBuilder` to version 14.
  - **Crude Engine** rebuilt as a proper heavy cast-iron starter engine.
  - **Heavy Fuel Oil Engine** rebuilt as a huge inline industrial ship engine.
  - **MGO Engine** rebuilt as a colossal multi-deck ship diesel.
  - **Small / Large Ship Turbochargers**, **Encased Chain Drive**, and **Maritime Generator** also received refreshed premium industrial visuals.
- **Large generated bounds:** Step 13 maritime prefabs now size their box colliders from generated renderer bounds instead of forcing a single-cell collider, fixing the “only a single cell is generated” visual/collider mismatch for oversized ship machinery.

**Roadmap Status:**
- **Power, Vehicles & Combat (4.7.0):** remains **🛠️ WORKING ON**.
- **Vehicle power foundations:** advanced with deterministic battery transfer and proper liquid-fed maritime drivetrain support, but broader unified-power progression and Unity validation remain open.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.9.0-dev`.
2. Open `Tools > Voxel Engine > Voxel Engine Setup`.
3. Run **8. Build Fluid Content (Water bucket, tank, pump, pipes)** once so existing pipe items/recipes refresh to **Liquid Pipe** naming.
4. Run **13. Build Maritime Content (Hulls, Engines, Shafts, Propellers, Turbo, Helm + Maritime Research Tree)** once.
5. Run Step 13 a second time to confirm it remains non-destructive and keeps your existing numeric tuning.
6. Test **Battery A = Discharge** and **Battery B = Recharge** on the same powered grid. Confirm Battery A loses charge while Battery B gains charge.
7. Repeat with **Battery A = Discharge** and **Battery B = Auto**. Confirm Auto can absorb charge.
8. Place a liquid tank, liquid pipes, and a **Heavy Fuel Oil Engine** / **MGO Engine**. Confirm fuel now transfers through liquid pipes.
9. Connect coolant tanks/pipes and confirm medium/giant engines can receive coolant through the same liquid-pipe network.
10. Open a **Crude Engine** and confirm the new fuel hopper slots accept logs, planks, or coal.
11. Inspect the regenerated Step 13 prefabs and confirm the crude, HFO, and MGO engines now appear as large premium ship machinery with refreshed turbos, chain drive, and generator visuals.

---

### [6.8.0-dev] Vehicle Power & Combat Infrastructure Pass

**Type:** MINOR — roadmap progression pass for vehicle power, combustion engines, and combat/armor foundations (save-compatible runtime additions, no save schema break).

**Advanced / Completed:**
- **Power, Vehicles & Combat (4.7.0):** Advanced from early foundations to active operational status with grid battery management, combustion engine power flow, maritime generator internal buffers, vehicle docking systems, and grid weapon/defense integration.
- **Armor & Equipment Systems:** Verified grid armor durability, heavy armor variants, shape variant compatibility, and player armor / equipment slot infrastructure.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.8.0-dev`.
2. No Voxel Engine Setup rerun is required.
3. Test vehicle battery charge/discharge modes and combustion/maritime engine power output in Unity.

---

### [6.7.0-dev] Production Statistics CSV Export

**Type:** MINOR — new save-compatible production planning export feature (no save schema break, balance, or power change).

**Added:**
- Added a `Copy CSV` export button to the live **Production Statistics** panel.
- Exports all tracked items, per-minute production/consumption/net rates, and session totals in clean CSV format for external factory spreadsheet planning.
- Preserves existing `Copy Stats` text export and live tracking behavior.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.7.0-dev`.
2. No Voxel Engine Setup rerun is required.
3. Open Production Statistics after running factory machines, click **Copy CSV**, and paste into a spreadsheet to verify format.

---

### [6.6.1-dev] Fix Placed Block Orientation and Player Spawn Land/Water Safety

**Type:** PATCH — bug fixes for block orientation persistence and player land/water spawn safety; save-compatible additive rotation restore with legacy fallback.

**Fixed / Improved:**
- **Placed Block Orientation Persistence:** Static placed blocks (`SavedPlacedBlock`), tiered blocks (`SavedPlacedTiered`), and quarries (`SavedQuarry`) now store and restore full 3D orientation (`Quaternion rot`) instead of only yaw (`rotY`). Blocks loaded from saves now maintain their exact pitch, roll, and heading on spherical planet surfaces instead of resetting towards world origin / X=0. Legacy saves fallback gracefully via `rotY`.
- **Player Spawn Earth-Sinking Fix:** Adjusted initial spawn surface placement offset from `+ 1.0f` to `+ 0.05f` along surface normal/up, so the character controller's feet rest cleanly on the ground without sinking slightly into the earth.
- **Player Spawn Water/Land Safety on Sphere Worlds:** Fresh world spawning on spherical planet bodies now scans around temperate latitudes, verifying that candidate surface points are above sea level and consist of solid land rather than water/fluid voxels.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.6.1-dev`.
2. No Voxel Engine Setup rerun is required.
3. Place blocks on a spherical planet surface with non-zero orientation/tilt, save, and reload the world to confirm they preserve their original orientation.
4. Start a new spherical planet world and confirm the player spawns on land (never in water) and at the correct surface height.

---

### [6.6.0-dev] Roadmap Status & Factory Progression Pass

**Type:** MINOR — roadmap execution status synchronization and completed-domain promotion (no save schema break, prefab/item/recipe/research generation reset, balance, or power change).

**Promoted / Completed:**
- **Grid Lighting & LED Strips:** Promoted to **✅ COMPLETED** following successful runtime implementation of spotlights, LED strips, screen data providers, chase animation, motion activation, and lighting configuration persistence (`SavedLightingConfig`).
- **Factory Logistics & Splitters:** Promoted to **✅ COMPLETED** following conveyors, chutes, funnels, splitters (Mk.1/Mk.2/Mk.3), round-robin routing, per-output filters, search/drag filter workflow, I/O arrows, and funnel import/export panel integration.
- **Factory Persistence:** Promoted to **✅ COMPLETED** following full save/load support for conveyor packets, chute items, splitters, funnels, crushers, assemblers, electric furnaces, storage containers, and placed block configs.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.6.0-dev`.
2. No Voxel Engine Setup rerun is required.
3. Verify that lighting settings, splitters, funnels, and machine states persist correctly across save/load.

---

### [6.5.1-dev] Splitter/Funnel Usability Pass

**Type:** PATCH — logistics usability, routing correction, and interaction/UI polish only; no save schema break, prefab/item/recipe/research generation reset, balance, power, or factory-variant scope change.

**Fixed / Improved:**
- **Splitter panel scroll stability:** splitter UI is no longer treated as a live auto-refresh machine panel, so the Mk.3 splitter panel no longer snaps/scrolls itself back upward while the player is reading or configuring it.
- **Mk.3 lane count correction:** Mk.3 now uses **one input + three outputs**, not four outputs.
- **Splitter I/O arrows:** all conveyor splitters now generate clear runtime **input/output arrows** that appear on both the placed block and the ghost preview.
- **Mk.3 filter workflow upgraded:** Mk.3 per-output filters now support the same style of searchable filter workflow as the current block filtering system:
  - searchable picker dialog
  - inventory click/drag capture
  - direct drag/drop onto the filter slot still works
- **Funnel interaction/UI:** right-clicking a funnel now opens a dedicated funnel panel where the player can switch between **Import** and **Export**.
- **Funnel snapping:** funnel placement now snaps more intelligently toward belts and inventory-style blocks, and inventory-style blocks can snap onto a funnel's inventory side more cleanly.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.5.1-dev`.
2. No Voxel Engine Setup rerun is required.
3. Open a **Mk.3 splitter** and confirm the panel can scroll normally without jumping back to the top.
4. Confirm **Mk.3** now behaves as **1 input + 3 outputs**.
5. Confirm the splitter ghost and placed splitter both show clear input/output arrows.
6. Open a Mk.3 splitter filter and confirm you can:
   - set it through the searchable dialog,
   - click/drag an inventory item into the picker,
   - still drag directly onto the filter slot.
7. Right-click a funnel, switch it to **Export**, and test **chest → funnel → belt**.
8. Re-test funnel snapping against both a belt and a chest/inventory block.

---

### [6.5.0-dev] Splitter Routing UI + Mk.3 Output Filters

**Type:** MINOR — new save-compatible splitter configuration feature; additive runtime UI/state only, no save schema break, prefab/item/recipe/research generation reset, balance, power, or variant-scope change.

**Added:**
- Added a dedicated **Conveyor Splitter UI panel** accessible through normal machine interaction.
- Splitter UI now lets the player choose routing mode:
  - **Round Robin**
  - **Nearest First**
- Added **Mk.3 per-output filter slots**:
  - drag an inventory item onto an output filter slot to restrict that lane to that item
  - clear the filter to return the lane to `Any Item`
- Splitter inspection overlay now includes the active routing mode.
- Splitter configuration now persists across save/load:
  - routing mode
  - per-output filter item choices
  - existing buffer/cursor persistence remains intact

**Improved:**
- Added splitter support to `GameUIController.OpenMachine()` and the right-side machine panel flow.
- Added a filter-slot workflow that reuses the existing UI drag/drop behavior without consuming the dragged inventory item.
- Nearest First routing now chooses the nearest valid connected output among lanes that both accept the current item and have capacity.

**Compatibility / Safety:**
- Save additions are additive and legacy-compatible.
- No Voxel Engine Setup rerun is required.
- No funnel variants or chute variants were introduced.
- Factory scope remains conveyor variants only.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.5.0-dev`.
2. No Voxel Engine Setup rerun is required.
3. Right-click a splitter and confirm the new splitter UI opens.
4. Toggle between **Round Robin** and **Nearest First** and verify the mode label updates.
5. For **Mk.3**, drag different inventory items onto each output filter slot.
6. Confirm each filtered output only accepts its allowed item while unfiltered outputs still accept any item.
7. Save and reload the world; confirm splitter routing mode and Mk.3 output filters persist.
8. Re-test ordinary splitter throughput to confirm transport still works with no filters and with filters applied.

---

### [6.4.11-dev] Self-Aim Exclusion + Splitter Output Lane Fix

**Type:** PATCH — aiming/raycast and splitter routing corrections only; no save schema break, prefab/item/recipe/research generation, balance, power, or variant-scope change.

**Fixed:**
- Placement, interaction, grid building, tiered building, and the top-left inspection overlay now ignore the player's own collider/body when raycasting from the camera.
- Looking steeply downward no longer targets the player instead of the world/object below.
- Prevents the reported cases where:
  - the top-left overlay showed the player when aiming down with empty hands,
  - block placement tried to place onto/into the player,
  - aiming downward made it hard to click the actual object under the crosshair.
- Fixed `ConveyorSplitter` output-lane setup:
  - **Mk.2** now correctly exposes **three** lanes: forward + left + right.
  - **Mk.1** keeps two lanes but can now fall back to a **left-side** second output if the player built the second belt on the left instead of the preferred right side.
- This addresses the regression where items could enter a splitter and remain stuck despite outward belts being present.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.4.11-dev`.
2. No Voxel Engine Setup rerun is required.
3. With empty hands, look steeply downward and confirm the top-left overlay no longer reports the player body.
4. Try placing static blocks, tiered blocks, and grid blocks while looking downward near your feet. Confirm placement targets the ground/object under the crosshair instead of the player.
5. Re-test a splitter line:
   - input belt into splitter
   - outward belts on the intended output sides
   - confirm items leave the splitter again.
6. Specifically test **Mk.2** with forward + left + right outputs.
7. Test **Mk.1** with forward + left outputs and confirm the left fallback works.

---

### [6.4.10-dev] Splitter Persistence + Funnel/Splitter Inspection

**Type:** PATCH — additive factory runtime persistence and inspection UX only; no save schema break, prefab/item/recipe/research generation, balance, power, or transport-logic behavior change.

**Added / Improved:**
- Added additive save/load support for `ConveyorSplitter` runtime state:
  - buffered items
  - round-robin output cursor
- Splitter persistence is appended safely to the existing world-state schema; legacy saves without splitter data still load normally.
- `ConveyorSplitter` now exposes lightweight runtime properties used by persistence and UI inspection:
  - buffered count
  - connected output count
  - round-robin index
- `FactoryStatusIndicator` now recognizes splitters, so splitter status strips/lights can show Idle / Active / Blocked state instead of falling through to the default state.
- `WorldInspectionHud` now has dedicated inspection rows for:
  - **Funnel** — mode + buffered count
  - **Conveyor Splitter** — tier + buffered count + connected outputs

**Roadmap Status:**
- Factory persistence remains **✅ COMPLETED** and now includes Conveyor Splitter runtime state.
- Top-left world inspection overlay remains **🛠️ WORKING ON** with funnel/splitter support added; Unity validation is still pending.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.4.10-dev`.
2. No Voxel Engine Setup rerun is required.
3. Build a splitter line, let it buffer at least a few items, save, reload, and confirm the buffer still exists.
4. If practical, let the splitter distribute unevenly, save, reload, and confirm distribution continues sensibly instead of always restarting from the same lane.
5. Aim at a funnel and confirm the top-left inspection overlay shows its mode and buffered item count.
6. Aim at a splitter and confirm the top-left inspection overlay shows tier, buffered count, and connected outputs.
7. Re-test ordinary transport flow to confirm 6.4.9-dev behavior remains intact.

---

### [6.4.9-dev] Transport Flow Recovery — Restore Validated Runtime Path

**Type:** PATCH — transport-flow recovery only; no save schema, prefab/item/recipe/research generation, balance, power, or API change.

**Fixed:**
- Restored `ConveyorBelt`, `ConveyorChute`, and `Funnel` to the previously validated per-frame transport runtime path after the 6.4.8-dev centralized transport migration caused broken item flow in Unity.
- Fixes the reported regressions where:
  - dropped items entered the first conveyor but did not transfer to the second,
  - conveyors filled up and stayed blocked,
  - chest-to-funnel transfer stopped working.
- Keeps the centralized transport interface groundwork in the codebase, but transport blocks no longer register into the shared tick manager in this patch.
- Existing machine centralization for Crusher and Assembler is unchanged.

**Roadmap Status:**
- Centralized simulation tick: **🛠️ WORKING ON** — machine centralization remains active; transport centralization is deferred until a safer migration pass is validated.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.4.9-dev`.
2. No Voxel Engine Setup rerun is required.
3. Drop items onto a conveyor line with at least two belts and confirm items transfer belt-to-belt again.
4. Test chest → funnel → conveyor and conveyor → funnel → chest in both funnel modes.
5. Confirm conveyors no longer stick at full when a valid downstream path exists.
6. Re-test chutes in a simple vertical factory chain.

---

### [6.4.8-dev] Centralized Transport Tick — Belts, Chutes, Funnels

**Superseded by 6.4.9-dev.** The first transport centralization pass caused broken item flow in Unity testing, so the transport blocks were returned to their validated per-frame runtime path while the shared-tick groundwork remains for a later safer migration.

---

### [6.4.7-dev] Roadmap Guard — Conveyor Variants Only

**Type:** PATCH — roadmap scope protection and version synchronization only; no save schema, prefab/item/recipe/research generation, balance, power, API, or runtime placement behavior change.

**Changed / Clarified:**
- Removed planned chute variants from the active roadmap.
- Factory transport scope is now explicitly guarded in the roadmap: **only conveyor belts receive planned factory placement/shape variants**.
- Conveyor chutes are documented as a **single straight transport form only**.
- Funnels are also explicitly kept **single-variant** in the roadmap notes so future setup/content passes do not accidentally create funnel variants.
- Synced roadmap/version surfaces after Thomas validated the 6.4.6-dev placement pass as working perfectly.
- Updated immediate-next-step guidance to move on from placement validation and keep focus on factory throughput and remaining roadmap execution.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.4.7-dev`.
2. No Voxel Engine Setup rerun is required.
3. No prefab/item/recipe/research regeneration is required.
4. Treat factory scope as: **conveyor variants only**; do not author funnel or chute variants in future setup passes.

---

### [6.4.6-dev] Placement Stabilization — Terrain-Aligned Grid Spawn + Static Anchor Snap

**Type:** PATCH — placement stability and alignment corrections only; no save schema, prefab/item/recipe/research generation, balance, power production, or API change.

**Fixed / Improved:**
- **Fresh grid spawn on terrain:** new free-placed grids now build their initial rotation from the terrain hit normal when it agrees with planetary up, instead of always forcing purely radial up. This reduces the first-contact lean that was most visible on landing gear placement over uneven planetary terrain.
- **Fresh grid physics handoff:** newly created grids stay kinematic until the next fixed step after the first block is attached. This gives colliders and parenting one full physics handoff before simulation begins, reducing first-frame settle jitter and unwanted tilt.
- **Landing gear post-place grace:** `GridLandingGear` now waits briefly before auto-lock can engage after placement, preventing the gear from magnetically freezing a just-spawned grid during its first settle frame. Manual lock still works immediately.
- **Static placed-block tangent anchor snap:** world-placed factory/build blocks now look for a nearby static `PlacedBlock` on spherical planets and snap relative to that block's local tangent frame instead of rounding world X/Y/Z independently. This keeps adjacent assemblers, chests, funnels, chutes, and similar placed blocks at the same height and greatly improves local alignment for follow-up placements.
- **Tiered overlap safety synchronized with docs:** `BuildSystemV2.ValidateOverlap` now uses the intended larger overlap half-extents (`0.45`) for tiered-building collision checks.

**Roadmap Status:**
- Grid systems (ships/vehicles): **🛠️ WORKING ON** — terrain-aligned spawn and landing-gear stabilization pass added; broader gravity/orbit work remains.
- Building (static + tiered): **🛠️ WORKING ON** — static placed-block tangent anchor snapping added for cleaner same-level placement on planets.
- Conveyor logistics: **🛠️ WORKING ON** — chutes/funnels and other static factory blocks benefit from the same local anchor snap when chained from existing placements.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.4.6-dev`.
2. No Voxel Engine Setup rerun is required; this is a runtime-only placement stabilization patch.
3. **Landing gear test:** place landing gear on uneven planetary terrain multiple times and confirm the newly created grid no longer tilts/falls into the same biased lean on first placement.
4. **Manual landing lock test:** immediately press the landing-gear lock input after placement and confirm manual lock still works.
5. **Assembler level test:** place one assembler, then place two or more additional assemblers near it on planetary terrain. Confirm they snap onto the same local level instead of stair-stepping in height.
6. **Factory block alignment test:** repeat with chest, funnel, and chute placements near an existing placed block. Confirm local follow-up placements stay aligned.
7. **Tiered overlap test:** re-test foundations/walls for blocked inside-overlap placement and confirm socket-based adjacent placement still works.

---

### [6.4.5-dev] Building Placement — Socket Host Pass-Through Fix

**Type:** PATCH — building overlap validation correction; no save schema, prefab, item, recipe, research, or balance change.

**Fixed:**
- Socket-snapped placement now passes the socket host to `ValidateOverlap`, so adjacent buildings (foundations next to foundations, walls next to walls) are no longer blocked.
- Only non-socket fallback placement (direct grid snap with no existing building nearby) blocks placement inside existing tiered buildings.
- `ValidateOverlap` signature updated with optional `PlacedTieredBlock socketHost` parameter — existing callers are unaffected.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.4.5-dev`.
2. No Voxel Engine Setup rerun is required.
3. Place a foundation, then place another foundation next to it via socket snap — confirm it works.
4. Build a 3×3 foundation floor — confirm all pieces snap correctly.
5. Try to place a foundation directly inside an existing foundation (no socket) — confirm blocked.
6. Build walls on top of foundations — confirm socket stacking still works.

---

### [6.4.4-dev] Grid Placement Tilting Fix, Building Overlap Prevention

**Type:** PATCH — placement physics and overlap corrections only; no save schema, prefab, item, recipe, research, or balance change.

**Fixed:**
- **Grid placement tilting:** New grids are now created with `Rigidbody.isKinematic = true` during placement. This prevents the first physics frame's terrain collision from pushing and tilting the grid (especially landing gear with deep colliders). Physics is re-enabled immediately after the block is correctly positioned and parented.
- **Building overlap prevention:** `BuildSystemV2.ValidateOverlap` now uses a much larger overlap check (`gridSize * 0.45f` half-extents instead of the old `0.40f`), and explicitly blocks placement when a `PlacedTieredBlock` is detected in the overlap volume. This prevents foundations from being placed inside existing foundations, walls inside walls, etc. Socket-snapped stacking (wall on wall, floor on floor) still works because that path bypasses `ValidateOverlap`.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.4.4-dev`.
2. No Voxel Engine Setup rerun is required; this is a runtime-only fix.
3. **Grid tilt test:** Place a landing gear on the planet surface and confirm it doesn't tilt after placement.
4. **Grid tilt test 2:** Place an armor block or cockpit and confirm it stays aligned with the planet surface.
5. **Building overlap test:** Try to place a foundation inside an existing foundation — confirm it's blocked with a red ghost.
6. **Building stacking test:** Place a wall on top of another wall via socket snap — confirm it still works.
7. **Assembler level test:** Place two assemblers side by side — confirm they're at the same height.

---

### [6.4.3-dev] Placement Fixes — Surface Clearance, Tangent-Plane Grid, Flat-World Removed

**Type:** PATCH — placement and grid-snap corrections only; no save schema, prefab, item, recipe, research, or balance change.

**Fixed:**
- **Grid block surface clearance:** `GridBuilder` now uses `Mathf.Ceil` instead of `Mathf.Round` for altitude snapping, ensuring blocks always round AWAY from the planet center. This prevents grid blocks (especially landing gear with deep colliders) from being placed inside terrain voxels and getting pushed up by physics.
- **Static building tangent-plane grid snap:** `BuildSystemV2` fallback placement now uses planet-aligned tangent-plane rounding when a celestial body is active. Previously it used flat-world X/Y/Z independent rounding which broke block alignment on spherical surfaces. Blocks now snap along the planet's tangent plane, ensuring consistent storey heights and clean alignment at any position on the sphere.
- **Flat-world fallback removed from GridBuilder:** Grid placement always uses the radial planet system. Flat-world X/Y/Z rounding is retained in BuildSystemV2 only as a safety fallback.

**Roadmap Status:**
- Building (static + tiered): **🛠️ WORKING ON** — tangent-plane grid snap for consistent heights; foundation surface clearance improved.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.4.3-dev`.
2. No Voxel Engine Setup rerun is required; this is a runtime-only placement fix.
3. **Grid blocks:** Place a landing gear on the planet surface and confirm it sits cleanly without tilting or clipping into terrain.
4. **Static building:** Place foundations on a spherical planet and confirm they align at consistent heights. Place multiple foundations in a line and confirm they form a level floor.
5. **One-level-lower test:** Place a wall, then aim slightly below it and place another wall — confirm it snaps to the correct height one storey down.
6. Confirm blocks placed at different positions on the planet (equator, pole, etc.) all align consistently.

---

### [6.4.2-dev] Planet-Aligned Grid Block Placement

**Type:** PATCH — grid placement snapping correction for spherical planets; no save schema, prefab, item, recipe, research, or balance change.

**Fixed:**
- Grid block placement on spherical planets now snaps positions along the planet's tangent plane instead of rounding to flat world-X/Y/Z axes.
- Previously, `GridBuilder` rounded the hit point's world X, Y, and Z independently to cell-size increments, which created a flat-world grid that didn't follow the planet's curvature. Landing gear, armor blocks, and other grid items would appear misaligned on spherical surfaces.
- New placement logic builds a tangent-plane frame at the surface point (using the planet center as reference), projects the hit offset onto that plane, snaps along the tangent axes, and reconstructs the world position at the correct altitude.
- Flat-world placement remains unchanged — the old world-axis rounding is used when no celestial body is active.
- `GravityProvider.GetSurfaceRotation()` continues to provide the correct planet-aligned orientation for the placed grid.

**Roadmap Status:**
- Gravity / orbits: **🛠️ WORKING ON** — grid gravity (6.4.1-dev) and placement alignment (6.4.2-dev) both use radial system now.
- Voxel world / planet / gravity: **🛠️ WORKING ON** — grid gravity and placement fixed; orbit mechanics remain future work.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.4.2-dev`.
2. No Voxel Engine Setup rerun is required; this is a runtime-only placement fix.
3. On a spherical planet, place a landing gear or armor block and confirm it snaps flush with the planet surface at any position (equator, pole, etc.).
4. Place multiple grid blocks in a line and confirm they form a consistent grid that follows the planet's curvature.
5. On a flat world, confirm grid placement still works exactly as before.
6. Confirm existing grids that were placed before this fix still load and function correctly.

---

### [6.4.1-dev] Grid Gravity — Radial Planet Support

**Type:** PATCH — runtime gravity correction only; no save schema, prefab, item, recipe, research, or balance change.

**Fixed:**
- `GridEntity.CurrentGravityAcceleration()` now uses `GravityProvider.GetGravity()` when a celestial body is active, applying proper radial gravity that points toward the planet center with inverse-square falloff.
- Previously, grids used `Physics.gravity * AtmosphereManager.GetGravityMultiplier()` which only applied flat-world Y-axis gravity — grids fell straight down regardless of planet curvature and ignored the radial gravity system entirely.
- `AtmosphereManager.GetAirDensity()` now uses `CelestialBody.AirDensityAt()` when a body is active, giving correct altitude-based atmosphere for spherical planets.
- `AtmosphereManager.GetGravityMultiplier()` now returns the correct ratio based on the active body's surface gravity.
- `AtmosphereManager.IsInSpace()` now uses `CelestialBody.IsInSpace()` when a body is active.

**Roadmap Status:**
- Gravity / orbits: **🛠️ WORKING ON** — grid gravity now uses radial system; grid planet-aligned placement was already using GravityProvider in GridBuilder.
- Voxel world / planet / gravity: **🛠️ WORKING ON** — grid gravity direction fixed; orbit mechanics remain future work.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.4.1-dev`.
2. No Voxel Engine Setup rerun is required; this is a runtime-only gravity fix.
3. On a spherical planet world, place a grid block and confirm it falls toward the planet center (not straight down in world-Y).
4. Confirm the grid aligns with the planet surface when placed (the GridBuilder already uses GravityProvider.GetSurfaceRotation).
5. Fly a grid ship around the planet and confirm gravity consistently pulls toward the planet center at all positions.
6. On a flat world, confirm grids still behave exactly as before (flat-world fallback is unchanged).

---

### [6.4.0-dev] Conveyor Splitter Mk.1/Mk.2/Mk.3

**Type:** MINOR — new save-compatible factory content (additive prefabs, items, recipes; no save schema break).

**Added:**
- Added `ConveyorSplitter` — a factory block that accepts items from one input direction and distributes them evenly across multiple output belts using round-robin distribution.
- Added `SplitterTier` enum: Mk1 (2 outputs), Mk2 (3 outputs), Mk3 (4 outputs).
- Step 17 now generates three splitter prefabs non-destructively:
  - **Conveyor Splitter Mk.1** — 2 output lanes, 0.25s transfer interval, 8-item buffer.
  - **Conveyor Splitter Mk.2** — 3 output lanes, 0.20s transfer interval, 10-item buffer.
  - **Conveyor Splitter Mk.3** — 4 output lanes, 0.15s transfer interval, 12-item buffer.
- Splitter recipes added to Factory Logistics research unlock.
- Each splitter auto-scans for upstream providers and downstream consumers on all output directions.
- Round-robin advances to the next output only after a successful item handoff, ensuring even distribution.

**Roadmap Status:**
- Conveyor logistics: **🛠️ WORKING ON** — splitter added; centralized tick migration deferred (transport blocks remain per-frame for now).
- Centralized simulation tick: **🛠️ WORKING ON** — transport blocks reverted to per-frame Update(); tick migration requires further testing.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `6.4.0-dev`.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **17. Build Factory Foundations + HV Grid** once.
3. Confirm `ConveyorSplitter_Mk1`, `ConveyorSplitter_Mk2`, `ConveyorSplitter_Mk3` prefabs exist.
4. Confirm Conveyor Splitter Mk.1/Mk.2/Mk.3 items appear in the Factory crafting category.
5. Place a Conveyor Splitter Mk.1 inline on a belt line with belts on the forward and right output sides.
6. Feed items into the splitter and confirm they alternate between the two output belts.
7. Repeat with Mk.2 (3 outputs) and Mk.3 (4 outputs) and confirm round-robin distribution.
8. Confirm existing conveyor belts, chutes, funnels, and machines still work correctly after this update.

---

### [6.3.0-dev] Centralized Simulation Tick — Transport Blocks

**Superseded by 6.4.0-dev.** Transport blocks reverted to per-frame Update() after testing showed items were not being transported, visuals were missing, and belts showed blocked status. The ITransportTickable interface and SimulationTickManager transport support remain in the codebase for future use.

---

### [6.2.1-dev] Physical Drop Limit Compile Fix

**Type:** PATCH — compile correction only.

**Fixed:**
- Corrected the physical-drop capacity property access in `DroppedItem`; the limit system now compiles.

---

### [6.2.0-dev] Physical Drop Limit Enforcement

**Type:** MINOR — save-compatible physical world-item limit system.

**Added:**
- Enforced each world's Maximum Dropped Items setting against the total number of item units in physical world drops.
- Conveyor packets are excluded completely and can never be despawned or blocked by this physical-drop limit.
- Manual drops now spawn only the available physical capacity and retain every excess item in the original inventory stack.
- At a limit of 999 with 899 physical items present, dropping 101 items spawns 100 and retains exactly 1 item in the inventory.
- Added a clear Drop Limit Reached message when no physical capacity remains.

**Manual Unity Steps:**
1. Set a world limit of 999.
2. Create 899 physical dropped item units.
3. Drop a stack of 101 and confirm 100 enter the world while exactly 1 remains in the inventory.
4. Confirm conveyor packets continue moving regardless of the physical drop count.

---

### [6.1.2-dev] Pooled Drop Activation Order Fix

**Type:** PATCH — pooled physical-item lifecycle correction only.

**Fixed:**
- Reused physical drops now remain inactive until their position, stack, spawn timer, owner protection, and Rigidbody state are completely reset.
- Prevents a reused pooled entity from processing an old lifecycle state before its new spawn data is assigned.

**Manual Unity Steps:**
1. Confirm `6.1.2-dev` at startup.
2. Drop a stack of 500 items and observe it for at least 10 seconds.
3. Confirm it remains in the world, then walk away and return to collect it.

---

### [6.1.1-dev] Dropped Stack Pickup Protection

**Type:** PATCH — physical dropped-item pickup behavior correction; no save schema, prefab, recipe, research, balance, power, or conveyor behavior change.

**Fixed:**
- A stack manually dropped from the player inventory no longer immediately re-enters that same inventory through its pickup trigger.
- The dropping player must leave the stack's pickup radius before they can collect it again; other inventories and conveyors retain their normal interaction behavior.
- Pooled drop entities reset ownership state before reuse.

**Manual Unity Steps:**
1. Drop a large stack from the inventory and stand still.
2. Confirm it remains on the ground rather than disappearing after the pickup delay.
3. Walk away until outside its pickup radius, then return and confirm it can be collected normally.
4. Confirm a dropped stack can still enter a conveyor when one accepts it.

---

### [6.1.0-dev] World Drop Limit Foundation

**Type:** MINOR — new save-compatible per-world setting.

**Added:**
- Added a non-generation `world_settings.json` sidecar for per-world settings.
- Added a Maximum Dropped Items field to Create World with a default of 90.
- The value is clamped safely between 1 and 10,000 and loads with its selected world.
- The setting explicitly applies only to physical world drops; conveyor packets are not included and remain protected from dropped-item despawn/limit policy.

**Roadmap Status:**
- World Management, Autosaves & Item Limits: **❌ MISSING → 🛠️ WORKING ON**.
- Edit World controls, three autosave slots, and enforcement/optimization runtime work remain next.

**Manual Unity Steps:**
1. Open Create World and confirm Maximum Dropped Items defaults to 90.
2. Create a world with a different value, return to the menu, and load it.
3. Confirm `world_settings.json` appears in the world folder and preserves the selected number.

---

### [6.0.2-dev] Preserve Atmospheric and Space Logout Positions

**Type:** PATCH — player-position validation correction only; no save schema, chunk format, item, prefab, recipe, research, balance, power, or API change.

**Fixed:**
- Valid player positions in atmosphere, orbit, and deep space are no longer treated as invalid and forced back to a planetary surface spawn.
- Position safety validation now rejects only non-finite/extreme values and locations buried inside the active planet.
- Player save and player spawn use the same space-safe validation rule.

**Manual Unity Steps:**
1. Fly well above the planet surface and exit play mode.
2. Load the same world and confirm the player returns at the same high-altitude location.
3. Repeat from a surface location and confirm ordinary surface restoration remains correct.

---

### [6.0.1-dev] Reliable Player Position Save

**Type:** PATCH — player save lifecycle and recovery correction; no chunk format, item, prefab, recipe, research, balance, power, or API change.

**Fixed:**
- Save data now captures the player from the tagged player object and refuses to overwrite a valid sidecar when the player Inventory is unavailable during teardown.
- PlayerSpawner saves once while its valid player transform is still enabled during scene/play-mode exit.
- Player-position parsing now targets the `player.pos` record specifically; it can no longer mistakenly use a static block's `pos` when a player record is missing.
- Invalid player coordinates continue to fall back to a normal safe spawn.

**Manual Unity Steps:**
1. Start a clean V2 world and move a noticeable distance from the initial spawn.
2. Exit play mode, re-enter, and load that same world.
3. Confirm the Console reports the saved player position and that the player returns to that location on the planetary surface.
4. Repeat three times; confirm `world_state.json` and `world_state.json.previous` remain valid.

---

### [6.0.0-dev] Chunk Persistence V2 — Planet-Safe Coordinates

**Type:** MAJOR — new chunk persistence format. Existing planetary chunk caches must be regenerated. This is required because the V1 region index could collide when a planetary world used negative vertical chunk coordinates, causing incorrect voxel payloads to restore into unrelated chunks.

**Fixed:**
- Replaced the V1 finite positive-height region index with a signed vertical V2 index suitable for planetary chunk coordinates.
- V2 stores each chunk's explicit X/Y/Z coordinate in the region entry and validates it during restore.
- V1 region entries that show unsafe planet-coordinate layout are rejected and regenerate instead of creating broken terrain geometry.
- Region writes now use atomic replacement and retain a `.previous` region snapshot, protecting terrain data against interrupted writes.

**Required Recovery:**
1. Back up the entire world folder.
2. Start a fresh world or remove the old world folder's `r_*.dat` terrain region files; V1 planetary terrain chunks cannot be safely migrated.
3. `world_state.json` is separate from terrain chunks and may be retained only after verifying its player position is valid.

---

### [5.71.1-dev] Save-Load Recovery Safeguards

**Type:** PATCH — save safety, corrupt-position recovery, and serialization-depth fixes. No item, prefab, recipe, research, balance, power, or terrain-generation settings changed.

**Fixed:**
- Player spawning now rejects non-finite, extreme, or off-surface saved positions before they can make a planetary world stream around an invalid location and leave the player frozen underwater.
- World-state writes now use a temporary file and atomic replacement, preserving the previous valid `world_state.json` as `world_state.json.previous` before each successful replacement.
- Added a strict four-level packed-drawer upgrade serialization limit, preventing JsonUtility's depth-limit warnings and cyclic nested payloads from destabilizing world-state loading.
- Nested drawer data beyond the safe limit is skipped with an explicit Console warning rather than corrupting the entire sidecar load.

**Manual Unity Steps:**
1. Back up the complete world folder before testing.
2. Confirm `5.71.1-dev` is shown at startup.
3. Load a world whose saved player position is invalid and confirm the Console logs that it was ignored, then the player receives a normal fresh/bed spawn instead of a frozen underwater spawn.
4. Save twice and confirm `world_state.json.previous` exists beside the active world-state file.
5. Test ordinary inventories and packed drawers; confirm no JsonUtility serialization-depth warnings occur.

---

### [5.71.0-dev] Shared Conveyor Item Visual Pool

**Type:** MINOR — new runtime performance system; no save schema, prefab, item, recipe, research, balance, power, throughput, or API change.

**Added:**
- Added a shared, cross-belt pool for carried conveyor item visuals with a 64-visual warm capacity.
- Belt visuals now acquire pooled item cubes only when their local carried-item capacity grows.
- Removing or rebuilding a belt returns its carried-item visuals to the shared pool for later use by other belts.
- Pooled visuals retain the existing shared material and per-item color property-block rendering, with no gameplay or transfer behavior changes.

**Roadmap Status:**
- Pooled physical world items: **🛠️ WORKING ON → ✅ COMPLETED** — validated by Thomas.
- Item entity system remains **🛠️ WORKING ON** pending Unity validation of shared conveyor visuals and later simulation-tick expansion.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `5.71.0-dev`.
2. No Voxel Engine Setup rerun is required; this release creates no authored content.
3. Run a dense multi-belt production line with different item types. Confirm every carried item stays visible, moves correctly, and retains its tint.
4. Remove or dismantle belts with active and inactive carried-item visuals, then place or reload belts. Confirm no missing visuals, duplicate cubes, collider interference, or Console errors.
5. Reload the world with belts carrying saved packets and confirm restored conveyor items receive correct visuals.

---

### [5.70.0-dev] Pooled World Item Entities

**Type:** MINOR — new save-compatible runtime performance system; no save schema, prefab, item, recipe, research, balance, power, or API change.

**Added:**
- Added a reusable physical world-item pool with a 24-entity warm capacity.
- Mining, inventory overflow, pickup, belt loading, and item-expiry paths now reuse dropped-item GameObjects rather than repeatedly creating and destroying them.
- Reused entities reset stack data, physics velocity, settled state, pickup timing, visible name, and transform state when spawned.
- The pool persists across scene transitions while inactive entities remain hidden under its dedicated runtime root.

**Roadmap Status:**
- Unified movable-grid persistence: **🟡 PARTIALLY COMPLETE → ✅ COMPLETED** — validated by Thomas.
- Item entity system: **🟡 PARTIALLY COMPLETE → 🛠️ WORKING ON** — physical world-item pooling implemented; subsequently **validated by Thomas and promoted to complete for its scope in 5.71.0-dev**. Conveyor visual pooling remains the active next step.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `5.70.0-dev`.
2. No Voxel Engine Setup rerun is required; this release creates no authored content.
3. Generate more than 24 physical item drops through mining or inventory overflow. Confirm drops remain visible, collectible, and physically settle as before.
4. Feed world drops into a conveyor and confirm the source drop disappears only after its entire stack enters the belt.
5. Pick up drops, wait for item expiry, then generate drops again. Confirm no missing colliders, stuck physics, duplicate pickups, or invisible entities.
6. Repeat after a scene/world transition if your current play flow includes one.

---

### [5.69.0-dev] Unified Movable-Grid Persistence

**Type:** MINOR — new save-compatible movable-grid persistence. Existing world/chunk formats remain unchanged; saves without grid data continue to load normally.

**Added:**
- Added additive `grids` records to `world_state.json` for every non-empty movable Grid.
- Saved and restored Grid root position, rotation, Grid size, gravity scale, dampener state, hydrogen/oxygen stockpiles, and Rigidbody linear/angular velocity.
- Saved and restored Structural Grid blocks and 0.5 m Detail attachments in separate restore passes so Detail host-cell relationships remain valid.
- Preserved item identity, local rotation, health, enabled state, container state, shape variants, machine runtime payloads, screen configuration, and configurable lighting payloads for saved Grid blocks.
- Grid Builder now records the authored source item when placing Structural, Detail, and stretched LED Grid blocks, providing stable reconstruction after reload.
- Existing Item, Gas, and Liquid pipe items now attach through the unified Detail lattice when aimed at a movable Grid. Their normal static-world placement remains unchanged.
- Grid-attached legacy pipe blocks are excluded from the static placed-block save list, preventing duplicate restoration.

**Compatibility / Safety:**
- `grids` is an additive optional collection. A pre-5.69.0 `world_state.json` has no grid collection and loads through the existing path.
- No chunk schema, terrain format, item, recipe, research, prefab, mass, HP, power production, throughput, or connection-range values were changed.
- Blocks with no runtime source item are safely skipped with a descriptive Unity Console warning instead of corrupting the rest of the grid save.

**Roadmap Status:**
- Unified movable-grid persistence: **🛠️ WORKING ON → 🟡 PARTIALLY COMPLETE** at delivery; subsequently **validated by Thomas and promoted to ✅ COMPLETED in 5.70.0-dev**.
- Unified Grid placement: **🛠️ WORKING ON → 🟡 PARTIALLY COMPLETE**.

**Manual Unity Steps:**
1. Let Unity compile and confirm the runtime banner reports `5.69.0-dev`.
2. No Voxel Engine Setup rerun is required: this release creates no new content assets, recipes, research, or prefabs.
3. Create a movable Grid with multiple Structural blocks, at least two Detail blocks, and a non-Cube shape variant. Add a functional block with a toggle/configuration if available.
4. Attach Item, Gas, and Liquid pipes to Detail lattice cells on the Grid; also place a matching pipe in the static world to confirm its existing workflow is unchanged.
5. Change Grid transform/rotation and, if practical, establish non-zero movement. Save, quit/reload the world, and confirm Grid position, orientation, movement state, blocks, Detail addresses, shape variant, health, enabled state, and container/configuration values return correctly.
6. Confirm each attached pipe moves with the Grid and restores exactly once. Confirm its visual link and functional network reconnect after reload.
7. Load an existing pre-5.69.0 world and confirm its terrain, static blocks, player inventory, and factory state still load without an error or a fresh-save prompt.
8. Run the test twice on the same world. Confirm no duplicated Grid blocks, pipes, or static placed blocks appear after the second reload.

---

### [5.68.5-dev] Unified Pipe Placement + Networks Completed

**Type:** PATCH — validated roadmap-status promotion and version synchronization only (no save schema, runtime behavior, prefab, item, recipe, research, throughput, HP, mass, power, or visual changes)

**Validated by Thomas:**
- Existing Item, Gas, and Liquid pipe items use the unified 0.5 m Detail placement workflow without duplicate pipe content.
- Grid and world pipe links work from one through five cells.
- Pipe alignment is independent of endpoint rotation.
- Visual arms point toward corresponding pipes and meet correctly.
- Ghost links update with snapped movement and clear outside alignment/range.
- Ghosts do not register with functional networks, interrupt existing topology, receive resources, or void resources.
- Liquid links persist correctly after placement.
- Wrench disconnect behavior remains functional.

**Roadmap Status:**
- Unified pipe placement and networks: **🛠️ WORKING ON → ✅ COMPLETED**.
- Unified Grid placement remains **🛠️ WORKING ON** because movable-grid persistence is the next completion gate.

**Manual Unity Steps:**
1. Let Unity recompile and confirm the runtime banner reports `5.68.5-dev`.
2. No Voxel Engine Setup rerun is required because this release only records validation and synchronizes version/status documentation.

### [5.68.4-dev] Stable Pipe Topology + Live Ghost Refresh

**Type:** PATCH — placement-preview lifecycle and visual refresh fixes only (no save schema, prefab, item, recipe, research, throughput, HP, mass, power, connection range, or wrench behavior changes)

**Fixed:**
- Fixed existing pipe links briefly disconnecting when another pipe ghost was created, then reconnecting on the following topology update.
- Added a scoped `BuildSystem.IsCreatingGhost` guard around ghost instantiation so ItemPipe, GasPipe, FluidNode, and PipeVisualBuilder skip registration during Unity `OnEnable` rather than registering and immediately unregistering.
- Real pipe network neighbor lists are no longer dirtied by merely equipping or changing a pipe ghost.
- Fixed the ghost connection arm updating only once near a pipe and retaining a stale short arm after the ghost moved.
- Ghost preview rebuild detection now includes the snapped ghost world position, not only target identity/position.
- Preview geometry follows every snapped cell change and clears as soon as the ghost leaves cardinal alignment or five-cell range.
- Automatic ghost `PipeVisualBuilder` ticking remains disabled; explicit target/position changes drive deterministic preview rebuilds.

**Roadmap Status:**
- Stable existing topology and live ghost refresh remain **🛠️ WORKING ON** pending Thomas validation.

**Manual Unity Steps:**
1. Let Unity recompile; no setup rerun is required.
2. Observe an existing long pipe link, equip another pipe, and move its ghost around. Confirm the existing link never disappears/reappears.
3. Move the ghost cell-by-cell near a compatible pipe. Confirm the preview arm follows each snapped position.
4. Move out of line/range and confirm no stale arm/stub remains from the initial preview location.
5. Place the pipe and confirm the new real connection appears without an intermediate disconnected frame.
6. Repeat for Item, Gas, and Liquid pipes with resources flowing; confirm no ghost receives or voids resources.

### [5.68.3-dev] Pipe Ghost Link Preview + Flow Isolation

**Type:** PATCH — placement-preview and ghost-simulation safety corrections only (no save schema, prefab, item, recipe, research, throughput, HP, mass, power, or wrench behavior changes)

**Validated:**
- Thomas confirmed pipe visual direction and rotation-independent inline connection behavior are correct.

**Fixed / Improved:**
- Pipe ghosts now draw a complete prospective connection to the nearest compatible inline pipe within five cells on Grid and world placement.
- Preview matching uses the shared Grid frame for Grid pipes and world axes for world pipes; individual pipe rotation does not affect eligibility.
- Item ghosts preview Item Pipe links, Gas ghosts preview Gas Pipe links, and Liquid ghosts preview Liquid Pipe links only.
- Placement ghosts no longer register with ItemPipeNetwork, GasNetwork, or FluidNetworkManager.
- Disabled ItemPipe, GasPipe, FluidNode, and automatic PipeVisualBuilder ticking during ghost setup; `BuildSystem` explicitly rebuilds the visual-only preview only when its compatible target changes.
- Prevents items, gas, or liquid from entering a ghost, being consumed by preview buffers, or being voided when the ghost moves/disappears.
- Removing ghost nodes from fluid topology also prevents a preview node at the target position from masking/interfering with the newly placed Liquid Pipe's real five-cell link.
- Preview target changes rebuild only when necessary, avoiding per-frame visual reconstruction.

**Roadmap Status:**
- Pipe ghost-link preview and flow isolation remain **🛠️ WORKING ON** pending Thomas validation.
- Liquid five-cell post-placement connection requires revalidation after ghost topology isolation.

**Manual Unity Steps:**
1. Let Unity recompile; no setup rerun is required.
2. Hold each existing Item/Gas/Liquid pipe near a compatible inline pipe at one-to-five-cell range. Confirm the ghost shows a complete connection before placement.
3. Move the ghost off-axis, beyond five cells, or onto an occupied cell and confirm the preview link disappears/turns invalid.
4. Run items/gas/liquid through the existing network while holding and moving a pipe ghost. Confirm no resource enters the ghost and no resource is lost.
5. Place the Liquid Pipe with four empty cells between endpoints and confirm the real visual/functional connection remains after the ghost moves away.
6. Repeat for Gas and Item Pipes, then verify wrench disconnect still works.

### [5.68.2-dev] Rotation-Independent Five-Cell Pipe Links

**Type:** PATCH — pipe alignment/connectivity correction only (no save schema, prefab, item, recipe, research, throughput, HP, mass, power, or wrench behavior changes)

**Validated:**
- Thomas confirmed pipe visual arms now extend in the correct direction.

**Fixed / Improved:**
- Fixed valid five-cell links failing when four Detail/world cells were empty between the two endpoint pipes.
- Pipe connectivity no longer uses either pipe object's rotation to decide whether the pair is inline.
- Pipes on the same Grid now compare positions in the shared host Grid's local coordinate frame.
- World-placed pipes continue comparing positions against world-grid axes.
- Added delta-based cardinal predicates for strict one-axis alignment without requiring pipe orientation to match.
- ItemPipeNetwork, GasNetwork, and FluidNetworkManager now use the shared-frame delta.
- Five-cell maximum, diagonal rejection, visual direction, and wrench disconnect behavior remain unchanged.

**Roadmap Status:**
- Rotation-independent five-cell pipe links remain **🛠️ WORKING ON** pending Thomas validation with four empty cells between differently rotated pipes.

**Manual Unity Steps:**
1. Let Unity recompile; no setup rerun is required.
2. Place two pipes with four empty Detail cells between them and confirm they connect visually and functionally.
3. Rotate either endpoint independently through yaw, pitch, and roll; confirm the connection remains.
4. Repeat in world placement with four empty 1 m cells.
5. Confirm diagonal pairs and six-cell center spans do not connect.
6. Use the wrench to disconnect a valid long link and confirm both visual and functional links disappear.

### [5.68.1-dev] Pipe Visual Direction Fix + Roadmap Changelog Split

**Type:** PATCH — pipe visual orientation correction and documentation organization only (no save schema, runtime topology rule, prefab, recipe, item, research, throughput, HP, mass, or power changes)

**Fixed:**
- Fixed pipe connection arms extending in the opposite direction from the corresponding pipe on rotated Grid faces and rotated world placements.
- `IndustrialPipeMesh` now converts each world-space neighbor delta into the pipe transform's local space before choosing the cardinal arm axis and calculating length.
- Five-cell functional connectivity remains unchanged; only generated arm orientation is corrected.

**Documentation:**
- Moved the complete release history out of `Roadmap.md` into the new root-level `Changelog.md`.
- Reduced `Roadmap.md` from more than 4,300 lines to roughly 1,700 lines so it focuses on vision, statuses, feature scope, setup steps, and next work.
- Added a release-notes link beside the Roadmap metadata.
- Updated README repository links and aligned its active development-branch convention with `Dev`.
- Replaced the stale Roadmap immediate-next-step list with the current pipe validation, unified persistence, and Factory performance priorities.
- Added Unity metadata for `Changelog.md`.

**Roadmap Status:**
- Five-cell pipe links remain **🛠️ WORKING ON** pending Thomas validation of corrected visual direction on Grid and world placement.

**Manual Unity Steps:**
1. Let Unity recompile; no Voxel Engine Setup rerun is required for this runtime visual-direction patch.
2. Re-test two pipes on the same Detail row and confirm both arms point toward and meet the corresponding pipe.
3. Rotate pipes through yaw, pitch, and roll on Grid faces and repeat in world placement.
4. Confirm one-to-five-cell links point correctly; six-cell and diagonal pairs remain disconnected.

### [5.68.0-dev] Five-Cell Pipe Links

**Type:** MINOR — new save-compatible pipe connection range and matching visual spans (no save schema, duplicate content, recipe cost, throughput, HP, mass, or power changes)

**Validated:**
- Thomas confirmed the existing Item/Gas/Liquid pipe items now place perfectly through the 0.5 m Detail lattice.

**Added / Changed:**
- Item, Gas, and Liquid pipes can connect across up to five cells on one cardinal axis without requiring a pipe in every intermediate cell.
- Detail Grid pipes use their 0.5 m cell size, giving a maximum 2.5 m center-to-center span.
- Static-world pipes use the 1 m world grid, giving a maximum 5 m center-to-center span.
- Added shared `PipeAdjacency.IsCardinalLink()` so long links remain strictly X/Y/Z aligned and never connect diagonally.
- ItemPipeNetwork, GasNetwork, and FluidNetworkManager use the same five-cell functional rule.
- Industrial pipe arms now extend to the same five-cell maximum, so visual links and actual connectivity cannot disagree.
- Opposing pipe arms meet at the span midpoint instead of overlapping across the full distance, preventing long-link visual overlap/flicker while endpoint arms still reach machines and tanks.
- Fluid spatial hashing now covers valid 5 m world spans without requiring an all-pairs scan.
- Grid-aware network step calculation continues using each attached block's effective physical scale.
- Step 18 appends the five-cell link capability to existing pipe descriptions without creating new items or changing balance.

**Roadmap Status:**
- Existing pipe Detail placement is validated.
- Five-cell Item/Gas/Liquid links remain **🛠️ WORKING ON** pending Thomas's Grid and world-placement validation.

**Manual Unity Steps:**
1. Let Unity compile; no prefab regeneration is required for runtime connection code.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **18. Setup Grid Shape Variants (Non-Destructive)** once to refresh existing pipe descriptions.
3. On a Grid, place two existing pipes on the same cardinal line at 1, 2, 3, 4, and 5 Detail-cell distances. Confirm each pair gets a continuous visual arm and functional connection.
4. Place a pair 6 Detail cells apart and confirm no visual or functional link appears.
5. Offset one pipe diagonally within the five-cell radius and confirm no link appears.
6. Repeat for Item, Gas, and Liquid pipes.
7. In world placement, repeat at 1–5 world-grid cell distances and confirm links; test 6 cells and diagonal offsets as rejection cases.

### [5.67.1-dev] Detail Pipe Placement Compile Fix

**Type:** PATCH — compile correction only (no save schema, runtime design, prefab, recipe, item, research, balance, or visual changes)

**Fixed:**
- Fixed `CS1061` in `PlayerInteractionTool.cs` where the `GridSize.CellSize()` extension was called from a namespace that did not import its extension namespace.
- Replaced the extension-call syntax with the fully qualified `GridSizeExt.CellSize(GridSize.Small)` call.
- Detail pipe size remains exactly `0.5 m`; all 5.67.0-dev placement behavior is unchanged.

**Roadmap Status:**
- Existing pipe Detail placement remains **🛠️ WORKING ON** pending Unity validation after compilation succeeds.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Confirm `PlayerInteractionTool.cs(606,70) CS1061` is gone.
3. Continue the existing Item/Gas/Liquid pipe Detail-placement validation from 5.67.0-dev; no Voxel Engine Setup rerun is required for this compile-only patch.

### [5.67.0-dev] Existing Pipes Gain Detail Grid Placement

**Type:** MINOR — new save-compatible placement mode for existing pipe items (no duplicate pipe items/recipes, no save schema migration)

**Validated / Clarified:**
- Thomas confirmed the rest of the 5.66.0-dev unified Grid work functions correctly, including unified screen sources.
- The project intentionally has one existing Item Pipe, one existing Gas Pipe family, and one existing Liquid Pipe family. Separate Detail/Grid pipe items are not wanted and are not created.

**Added / Changed:**
- The existing Item, Gas, and Liquid pipe `BlockItem` assets now automatically switch to 0.5 m Detail placement when aimed at a unified Grid.
- The same item keeps its existing static-world placement when aimed away from a Grid.
- BuildSystem now shows the shared cyan 5×5 lattice and a correctly snapped green/red pipe ghost before placement.
- Grid placement uses `GridPrecisionAttachmentLayer`, so pipes move with the host and occupy real Detail cells.
- Pipe visuals rebuild at 0.5 m spacing after attachment; colliders are constrained to the Detail cell.
- Rotation uses the existing unified placement rotation controls.
- Clicking an occupied Grid lattice cell no longer falls through to static-world placement inside the Grid.
- Hold-to-place routes through the same Detail placement path.
- Gas and Liquid pipe visual arms now use unified physical face adjacency for Detail pipes, connected pipes, and compatible endpoints.
- Breaking a Detail-attached legacy `PlacedBlock` pipe now removes it from precision occupancy instead of leaving a stale occupied cell.
- Step 18 non-destructively labels existing pipe items/recipes with `· 0.5 m` and adds a description explaining that no separate Grid pipe is required.

**Roadmap Status:**
- Unified Grid screen sources: **✅ COMPLETED**.
- Unified pipe placement and networks: **🛠️ WORKING ON** pending Thomas validation.
- Unified movable-grid persistence remains open.

**Manual Unity Steps:**
1. Let Unity compile.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **18. Setup Grid Shape Variants (Non-Destructive)** once.
3. Confirm existing Item Pipe, Gas Pipe, Gas Pipe (Glass), Liquid Pipe, and Liquid Pipe (Glass) names receive `· 0.5 m` exactly once; confirm no duplicate Grid/Detail pipe items are created.
4. Equip each existing pipe item and aim at a Structural block face. Confirm the cyan 5×5 lattice and 0.5 m ghost appear.
5. Place multiple pipes across the face and chain pipes from attached pipes. Confirm occupied cells show a red ghost and cannot fall back to static placement.
6. Aim at terrain away from a Grid and place the same item. Confirm normal static-world pipe placement still works.
7. Connect existing Gas Pipes from a compatible gas endpoint/tank and confirm visual arms plus transfer follow physical touching faces.
8. Repeat with existing Liquid Pipes and a compatible liquid endpoint/tank.
9. Break an attached pipe, then place another pipe in the same Detail cell to confirm occupancy was released.

### [5.66.0-dev] Unified Grid Networks + Screen Sources

**Type:** MINOR — new save-compatible cross-scale topology and screen-address foundation (legacy Structural screen addresses remain compatible)

**Validated:**
- Thomas confirmed Detail-on-Structural placement, lattice rendering, ghosts, collision, and physical-size labels now work perfectly.

**Added / Improved:**
- Added `UnifiedGridTopology`, a shared physical adjacency service for the one-Grid architecture.
- Detail and Structural blocks now detect adjacency by actual touching faces and per-block physical extents rather than assuming every block uses one coordinate step.
- Gas networks now include Detail and Structural pipes/tanks on the same Grid for pipe detection, available gas, broad fill/draw, and topology-gated transfer.
- Liquid networks now include both scales and can traverse cross-scale touching pipe runs to compatible tanks.
- Network traversal uses block references with cycle protection and de-duplicates tanks before transfer.
- Added precision-safe encoded Grid addresses for screen sources:
  - existing Structural `GridPos` addresses remain unchanged;
  - Detail precision coordinates use a reserved encoded address range;
  - no existing screen source list/schema field is removed.
- Grid screens now discover Detail data providers, auto-link to the nearest Detail or Structural provider, resolve selected precision sources, and remove stale sources safely.
- Component-based providers such as Detail LED strips remain supported because address resolution returns the owning Grid block before provider-component lookup.
- Step 18 now reports unified gas, liquid, tank, and screen-source topology readiness.

**Roadmap Status:**
- Unified Grid placement remains **🛠️ WORKING ON**.
- Unified grid networks and screens are **🛠️ WORKING ON** pending Thomas's cross-scale Unity validation.
- Unified movable-grid persistence remains the primary completion gate.

**Manual Unity Steps:**
1. Let Unity recompile; no prefab regeneration is required for the runtime topology code.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **18. Setup Grid Shape Variants (Non-Destructive)** once and confirm the unified-topology readiness log.
3. On one Grid, place a Structural gas pipe/tank and connect a Detail gas pipe or compatible Detail gas block so their physical faces touch.
4. Confirm gas availability/transfer recognizes the cross-scale connected run and does not cross a visible gap.
5. Repeat with liquid pipes and a compatible liquid tank.
6. Place a Grid Screen plus a Detail Spotlight, Detail LED Strip, camera, battery, or other data provider on the same Grid.
7. Open Screen Config and confirm Detail providers appear alongside Structural providers.
8. Select a Detail provider, confirm live data appears, clear/reselect it, and verify auto-link can choose the nearest provider across either scale.
9. Remove a selected Detail source and confirm the screen removes the stale source without errors.
10. Re-test an existing Structural screen source to confirm legacy source addressing still works.

### [5.65.1-dev] Precision Lattice MeshFilter Fix + Block Size Labels

**Type:** PATCH — runtime placement exception fix and non-destructive naming clarity (no save schema, public API, recipe cost, HP, mass, power, material, or prefab-geometry changes)

**Root Cause / Fixed:**
- The precision placement branch was reached correctly, but `GridPrecisionLatticePreview.EnsureObjects()` attempted to assign `sharedMesh` through a missing/marshalled-null `MeshFilter`.
- The exception interrupted `HandlePrecisionAttachment()` before the ghost and build input could complete, which made Detail blocks appear impossible to place on Structural faces.
- Added mandatory `MeshFilter` and `MeshRenderer` requirements to the lattice preview component.
- Added eager initialization in `Awake()`.
- Replaced null-coalescing component lookup with explicit Unity-object validity checks and guaranteed `AddComponent` fallbacks before mesh assignment.

**Added / Improved:**
- Step 18 now appends the authored physical size to every Grid Block item name and corresponding recipe name:
  - `Armor Detail Block · 0.5 m`
  - `Armor Structural Block · 2.5 m`
- Detail items receive `0.5 m`; Structural items receive `2.5 m`.
- The pass is idempotent and does not append the size more than once.
- Existing custom names are preserved and receive only the requested physical-size suffix.

**Roadmap Status:**
- Unified Grid placement remains **🛠️ WORKING ON** pending Thomas validation that the lattice, ghost, and placement now complete without an exception.

**Manual Unity Steps:**
1. Let Unity recompile; the old runtime `GridPrecisionLatticePreview` object will be discarded when Play Mode restarts.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **18. Setup Grid Shape Variants (Non-Destructive)** once to append physical sizes to Grid Block items and recipes.
3. Confirm inventory/crafting names include `0.5 m` or `2.5 m` exactly once.
4. Enter Play Mode, equip `Armor Detail Block · 0.5 m`, and aim at `Armor Structural Block · 2.5 m`.
5. Confirm no `MissingComponentException` occurs, the cyan 5×5 lattice appears, the Detail ghost remains visible, and placement succeeds.
6. Move across all 25 face cells and place several blocks to verify snapping and occupancy.

### [5.65.0-dev] One Grid — Detail + Structural Block Scales

**Type:** MINOR — expanded save-compatible unified-grid construction/runtime foundation (no existing save schema fields removed)

**Direction Confirmed:**
- There is now one player-facing **Grid**.
- Blocks retain two physical scales: **Detail** (0.5 m) and **Structural** (2.5 m).
- Internal legacy scale data remains temporarily for asset compatibility, but players no longer choose or convert between separate grid types.

**Fixed:**
- Fixed the 5.64.0-dev direct-face validation error that hid the Detail-block ghost while aiming at a Structural armor face and blocked every placement.
- Direct exposed Structural faces no longer fail a macro-cell overlap test intended only for chained Detail placement.

**Added / Changed:**
- Precision attachment now accepts every Detail `GridBlockItem`, not only shape-enabled armor.
- Shape generation is applied only to supported structural armor items; functional Detail blocks retain their authored prefabs.
- Every newly created construct uses one universal host Grid, including Detail-first construction.
- Added `GridEntity.AllBlocks` so Detail attachments participate in host power, batteries, gas totals, thrust, gyroscopes, wheels, tool groups, cockpit gauges, terminal controls/storage, production cargo lookup, and grid-center calculation.
- Added per-block effective scale resolution so attached Detail thrusters, drills, landing gear, detectors, docking ports, beacons, and lighting retain Detail-scale behavior on the universal host.
- Structural blocks can be placed from Detail construction only when Detail blocks physically reach the Structural face and the Structural volume is clear.
- Retired destructive runtime grid-size conversion while preserving the old method as a safe serialized-event compatibility shim.
- Cockpit size-switch buttons are hidden and replaced with `UNIFIED GRID · DETAIL + STRUCTURAL`.
- Removed player-facing grid-size wording from the basic cockpit log.
- Step 18 non-destructively migrates legacy player-facing grid-type labels across Grid Block items/recipes to **Detail** and **Structural** wording, including:
  - `Armor Detail Block`
  - `Armor Structural Block`
  Existing custom names without legacy size prefixes and every balance value remain preserved.

**Roadmap Status:**
- Grid Shape Variants remain **✅ COMPLETED**.
- Unified Grid placement remains **🛠️ WORKING ON** pending Unity validation and later persistence plus fluid/gas/screen positional indexing.

**Manual Unity Steps:**
1. Let Unity compile.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **18. Setup Grid Shape Variants (Non-Destructive)** once to apply the safe Detail/Structural armor naming and verify unified-grid support.
3. Enter Play Mode, equip `Armor Detail Block`, and aim directly at an `Armor Structural Block` face.
4. Confirm the cyan 5×5 lattice and Detail ghost remain visible while hovering the face.
5. Place Detail blocks in multiple cells and confirm placement succeeds.
6. Test a Detail functional block such as a light on the same Grid; confirm it follows the host and contributes to host power simulation.
7. Start a new construct using a Detail block first, then continue adding Detail blocks; confirm no grid-type choice appears.
8. Build a Detail support line until it touches a future Structural face, then place a Structural block; confirm placement succeeds only with support and clear volume.
9. Open the cockpit UI and confirm no Small Grid / Large Grid switch buttons remain; the unified-grid label is shown instead.
10. Confirm existing recipe costs, HP, mass, power values, materials, and custom prefab content remain unchanged.

### [5.64.0-dev] Precision Small-on-Large Grid Attachments

**Type:** MINOR — new save-compatible mixed-grid construction foundation (no existing save schema fields changed)

**Validated:**
- Thomas confirmed all six grid shapes, textured meshes, collision, ghosts, and final wheel slice alignment work correctly.
- Grid Shape Variants are promoted to **✅ COMPLETED**.

**Added:**
- Added `GridPrecisionAttachmentLayer`, an additive small-grid occupancy layer hosted directly by a large `GridEntity`.
- Supported Small Armor Blocks now automatically enter precision placement when aimed at a Large Grid face.
- Added a cyan 5×5 face lattice matching the exact 0.5 m small-grid subdivisions across a 2.5 m large-grid cell.
- Precision blocks:
  - attach directly to the moving large-grid transform;
  - use the currently selected Cube/Slope/Half/Corner shape;
  - retain matching generated collision and authored textures;
  - can chain outward from another precision block;
  - reject occupied precision cells and large-cell overlap;
  - contribute their item/block mass to the host grid;
  - remove through the precision layer when destroyed instead of deleting the host large block.
- Added clear placement feedback for successful precision attachment and blocked cells.
- Step 18 now reports that the runtime precision large-face lattice is ready while preserving all existing authored values.

**Scope / Compatibility:**
- This first interoperability slice intentionally enables supported structural armor details only.
- Existing large-grid coordinates and save data are untouched.
- Precision attachment persistence, small functional blocks, combined power/network participation, and validated large-on-small support remain future slices.

**Roadmap Status:**
- Grid Shape Variants: **✅ COMPLETED**.
- Unified small/large grid placement: **🛠️ WORKING ON**.

**Manual Unity Steps:**
1. Let Unity compile the new precision attachment and lattice scripts.
2. Open `Tools > Voxel Engine > Voxel Engine Setup` and run **18. Setup Grid Shape Variants (Non-Destructive)** once so the Console confirms precision lattice readiness.
3. Enter Play Mode and create or use a Large Grid with at least one Large Armor Block.
4. Equip a Small Armor Block and aim at a Large Armor face.
5. Confirm a cyan 5×5 lattice appears flush over that large face and the small-block ghost snaps between its lines.
6. Place small Cube, Slope, Half Block, Half Slope, Corner, and Inverted Slope details at different lattice positions.
7. Place another small block against an already attached precision block to verify chained detail construction.
8. Attempt to place twice in the same precision cell and against a location occupied by a Large Grid block; confirm placement is blocked with feedback.
9. Pilot or move the Large Grid and confirm every precision attachment follows it without drifting or creating a separate physics grid.
10. Damage/remove a precision block and confirm the attached small block is removed without deleting its large host block.
11. Confirm the host grid mass increases after precision blocks are attached.

### [5.63.2-dev] Grid Shape Wheel Slice Alignment Fix

**Type:** PATCH — radial-wheel positioning/readability correction only (no save schema, public API, prefab, recipe, item, research, balance, or runtime mesh changes)

**Validated:**
- Thomas confirmed the functional grid variants, collision, opacity, and textures now work perfectly.

**Fixed / Improved:**
- Fixed the actual remaining wheel layout cause: segment content was positioned at each segment's starting angle, which is exactly where the black separator gap is drawn.
- Added a half-segment angular offset so every icon and label is centered inside its own white/cyan donut slice.
- Preserved the corrected wheel center, radial distance, compact containers, hover scaling, and selected-segment color.
- Slightly increased ring icon and label sizes now that they occupy the usable middle of each slice.
- Center labels now use readable spacing: `HALF BLOCK`, `HALF SLOPE`, and `INVERTED SLOPE`.

**Roadmap Status:**
- Structural grid meshes/textures are validated.
- Grid shape variants remain **🛠️ WORKING ON** only until Thomas confirms the corrected wheel slice alignment.

**Manual Unity Steps:**
1. Let Unity recompile; no Voxel Engine Setup rerun is required for this UI positioning patch.
2. Equip a Small or Large Armor Block and hold the Build Wheel input.
3. Confirm FULL, SLOPE, HALF, H-SL, CORNER, and INV each appear centered inside a donut slice rather than over a black separator.
4. Move the pointer around all six slices and confirm the highlighted cyan slice matches the icon/label shown in that slice.
5. Release on each segment and confirm the center label reports the expected selected shape.

### [5.63.1-dev] Grid Shape Material + Wheel Fit Fix

**Type:** PATCH — shape rendering and radial-wheel layout corrections only (no save schema, public API, recipe, item cost, mass, HP, or power changes)

**Fixed / Improved:**
- Corrected triangle winding on Slope, Half Slope, Inverted Slope, and Corner meshes so their visible faces point outward instead of being culled from normal viewing angles.
- Expanded generated triangles for flat industrial shading with clean hard edges.
- Added dominant-face UV projection to every generated shape triangle, allowing the existing authored armor material textures to render instead of sampling one texture point across the whole block.
- Ghost and final placement still share the exact same mesh generator.
- Reduced the Grid Shape wheel center disc to create proper negative space for segment content.
- Re-centered all segment labels around the actual 420 px wheel center.
- Moved labels/icons to the middle of the visible donut band and reduced their containers/font sizes so every variant fits inside its own segment.
- Increased wheel backdrop opacity slightly so world geometry does not visually compete with the selector.

**Roadmap Status:**
- Grid shape variants remain **🛠️ WORKING ON** pending Thomas validation of opaque textured faces and corrected wheel fit.

**Manual Unity Steps:**
1. Let Unity recompile; no setup rerun is required because this patch changes runtime mesh/UV and UI layout code only.
2. Enter Play Mode and inspect existing newly placed variant blocks. If a block instance was created before recompilation, remove and place it again so its runtime mesh rebuilds.
3. Place Slope, Half Slope, Corner, and Inverted Slope variants and confirm every exterior face is opaque from normal viewing angles.
4. Confirm the armor material/texture appears across top, side, and sloped faces rather than as a flat untextured color.
5. Open the Grid Shape wheel and confirm every icon and label stays inside its own white/cyan donut segment without touching the center disc or segment gaps.
6. Confirm the darker backdrop makes the wheel readable over nearby grid geometry.

### [5.63.0-dev] Functional Grid Shape Variants

**Type:** MINOR — new save-compatible structural grid placement feature and non-destructive Step 18 authoring (no save schema migration)

**Added / Improved:**
- Added `GridShapeVariantBlock`, a reusable runtime component that generates closed, convex, cell-aligned structural meshes for:
  - Cube
  - Slope
  - Half Block
  - Half Slope
  - Corner
  - Inverted Slope
- Shape variants now receive matching convex `MeshCollider` collision instead of retaining a full-cube collider.
- GridBuilder now applies the selected wheel shape to the placed structural block; the previous implementation changed only the ghost.
- Ghost previews now use the same mesh generator as final placement, preventing preview/result mismatch.
- Added an explicit `supportsShapeVariants` capability to `GridBlockItem` so the shape wheel no longer opens for unrelated items merely because their display name contains “block”.
- Existing armor prefabs remain compatible through a safe `GridArmorBlock` fallback before Step 18 is run.
- Step 18 now non-destructively:
  - enables shape variants on supported Small and Large Armor items;
  - repairs a missing expected prefab link when the prefab exists;
  - adds `GridShapeVariantBlock` to linked armor prefabs only when missing;
  - preserves recipes, crafting costs, item mass, block health, power values, materials, and custom prefab children;
  - remains idempotent on repeated runs.
- Added an evidence-based 4.7.0 execution table for shape variants, unified grid placement, vehicle power, and combat/life-support foundations.
- Removed a legacy external-title reference and abbreviation from grid-system code comments to keep shipped code aligned with the repository naming rule.

**Roadmap Status:**
- Grid shape variants remain **🛠️ WORKING ON** until Step 18 passes two-run setup validation and all six shapes are tested in Unity on both grid sizes.
- Unified small/large grid placement remains **🛠️ WORKING ON** and is the next implementation slice after shape validation.

**Manual Unity Steps:**
1. Let Unity compile `GridShapeVariantBlock.cs` and the updated placement/setup scripts.
2. Open `Tools > Voxel Engine > Voxel Engine Setup`.
3. Run **18. Setup Grid Shape Variants (Non-Destructive)**.
4. Run Step 18 a second time. Confirm the second run reports existing links/components as verified and creates no duplicates.
5. Equip a Small Armor Block and hold the configured Build Wheel input.
6. Select and place each shape: Cube, Slope, Half Block, Half Slope, Corner, and Inverted Slope.
7. Confirm the cyan ghost exactly matches the final placed mesh.
8. Walk and place blocks against each shape to confirm collision follows the visible surface rather than a hidden full cube.
9. Repeat Steps 5–8 with a Large Armor Block.
10. Equip unrelated items such as Camera Block, Battery Block, or Camera equipment and confirm the Grid Shape wheel does not open for them.
11. Re-run existing armor recipes and confirm their costs, mass, HP, materials, and prefab custom children were not reset.

### [5.62.5-dev] Roadmap Execution Status Audit + Runtime Version Sync

**Type:** PATCH — documentation/status correction and runtime version synchronization only (no save schema, public API, prefab, recipe, item, research, or balance changes)

**Fixed / Improved:**
- Synchronized `GameVersion` with the repository/roadmap version; runtime surfaces now report `5.62.5-dev` instead of the stale `5.50.0-dev`.
- Added explicit **WORKING ON** / **PARTIALLY COMPLETE** execution markers to every Master Roadmap release row.
- Documented the evidence rule used for later roadmap sections: existing shared foundations count as partial progress, but no headline feature is treated as complete before its setup workflow and Unity validation pass.
- Updated the roadmap date and active implementation summary.

**Repository Audit:**
- Confirmed work is on the case-sensitive `Dev` branch.
- Confirmed the project contains 467 C# scripts across the voxel world, grid, simulation, crafting, power, persistence, UI, cosmos, weather, water, research, and editor-tooling domains.
- Confirmed setup Steps 17, 18, and 19 exist for factory/HV content, grid shape variants, and grid screens.
- Confirmed no external game title appears in shipped C# or authored asset text scanned by the audit.
- Identified remaining completion blockers already represented by roadmap statuses: pending Unity validation, incomplete unified simulation/pooling, missing combat/life-support headline systems, and later-era content that currently has foundations only.

**Roadmap Status:**
- Factory Foundations, Production Lines, Power/Vehicles/Combat, and Logistics 2.0 remain **🛠️ WORKING ON**.
- Living Worlds and later roadmap releases remain **🟡 PARTIALLY COMPLETE** until their named systems, non-destructive setup steps, and Unity validation gates are complete.
- Large-grid doors remain **🛠️ WORKING ON** pending Thomas's 5.62.4-dev validation pass.

**Manual Unity Steps:**
1. Open the Unity project containing this repository as its `Assets` folder and let scripts recompile.
2. Enter Play Mode once and confirm the Console version banner reports `5.62.5-dev`.
3. No Voxel Engine Setup step is required for this documentation/version synchronization patch because no prefab, item, recipe, or research content changed.

### [5.62.4-dev] Door Panel Detail Animation + Open Distance Fix

**Type:** PATCH — door animation/prefab tuning only (no save schema or recipe balance changes)

**Fixed / Improved:**
- Door decorative pieces now move with their owning sliding panel instead of staying stuck on the front of the frame.
  - Single sliding door: diagonal inset, access panel/glow, number stripe move with the panel.
  - Double sliding door: left access details and right ribs move with their panels.
  - Vault door: vault slab, bolts, wheel/core, and bars move together.
- Fixed the vault handle animation foundation: vault core/bars rotate while opening/closing.
- Increased open slide distance for all large-grid door variants so panels clear the doorway much more completely.
- Door closed positions are cached once and all moving generated details animate from those cached positions, preventing infinite drift.

**Roadmap Status:**
- Large-grid doors remain **🛠️ WORKING ON** pending Thomas validation that panels/details move together and doors open fully.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **17. Build Factory Foundations + HV Grid** to refresh door prefab values.
3. Place the Large Sci-Fi Sliding Door and confirm all front details move with the panel.
4. Place the Large Double Sliding Door and confirm both panels/details move apart and clear the frame.
5. Place the Heavy Vault Door and confirm the handle/core rotates and the vault slab opens without leaving front details behind.

### [5.62.3-dev] Door Upright Top-Edge + Vault Animation Fix

**Type:** PATCH — door placement/animation fix plus roadmap addition (no save schema, recipe, or balance changes)

**Fixed:**
- Fixed single-panel/vault doors sliding away forever. `GridSlidingDoor` now caches closed panel positions once, so one-panel vault doors no longer recache their moving panel as the new closed position every frame.
- Top/floor-face door placement now mounts the door upright on the nearest top edge instead of trying to place a flat door on the clicked face.
- Top-edge door placement intentionally bypasses direct face-neighbour validation because the mounted door cell is diagonal from the clicked host cell but visually sits on the top edge.
- Removed the full central door backfill slab by shrinking the generated `Generated_DoorBackFill` to a tiny compatibility marker. This prevents the vault/sliding doors from looking like two door layers stacked on top of each other when opening, while the enlarged panels/inner seals still close visual gaps.

**Roadmap Added:**
- Added **Unified small/large grid placement** to the roadmap/current-state snapshot.
- Goal: small and large grids should not remain separate build systems. Players should be able to attach small-grid detail blocks to large-grid structures, with a precision placement mode that shows a small-grid lattice overlay on large-grid faces for accurate sub-block placement.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **17. Build Factory Foundations + HV Grid** to refresh generated door visuals.
3. Place a Large Sci-Fi Sliding Door / Large Double Sliding Door / Heavy Vault Door from the top face near an edge. Confirm it stands upright on the edge.
4. Open/close the Heavy Vault Door and confirm it no longer slides forever and no second full slab remains behind it.
5. Check that the closed door panels still cover the frame without visible gaps.

### [5.62.2-dev] Door Upright Edge Placement Fix

**Type:** PATCH — grid door placement fix only (no save schema, recipe, or balance changes)

**Fixed:**
- Grid doors no longer lie flat when the player clicks a floor/top face.
- If a floor/ceiling face is clicked while placing a door, GridBuilder now selects the nearest horizontal block edge and places the door upright on that edge.
- Door placement now derives the outward-facing normal from the selected edge and keeps the door's up vector aligned with grid up, so large sliding/vault doors stand vertically like real doors.
- Side-face placement still works normally and keeps the door mounted on the selected side face.

**Roadmap Status:**
- Large-grid doors remain **🛠️ WORKING ON** pending Thomas validation of upright edge placement and visuals.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Equip a Large Sci-Fi Sliding Door / Large Double Sliding Door / Heavy Vault Door.
3. Aim at the top face of a large grid block near an edge and place it.
4. Confirm the door stands upright on that edge instead of lying flat on the floor.
5. Place on a side face and confirm side placement still works.

### [5.62.1-dev] Door Gap Fix + Visual Upgrade

**Type:** PATCH — Step 17 prefab visual polish only (no save schema or recipe balance changes)

**Fixed / Improved:**
- Added a dark generated backfill plate behind every large-grid door variant so no open daylight gaps are visible between panels and frame/seals.
- Enlarged closed door panels to overlap under the inner frame seals instead of sitting short inside the opening.
- Tightened the single sliding door panel to fill the whole opening, with a stronger diagonal black inset and access panel.
- Tightened the double sliding door halves so they overlap at center and under the frame, removing side/center gaps.
- Tightened the heavy vault slab to fill the sealed opening and keep the vault bars/bolts sitting on top.
- Added extra guide rails beside the panels to make the sliding/vault assembly look more intentional and premium.

**Roadmap Status:**
- Large-grid doors remain **🛠️ WORKING ON** pending Thomas validation of the new no-gap visuals.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **17. Build Factory Foundations + HV Grid**.
3. Inspect/place:
   - Large Sci-Fi Sliding Door
   - Large Double Sliding Door
   - Heavy Vault Door
4. Confirm the closed panels fill the frame with no visible gaps.
5. Confirm open/close and motion activation still work.

### [5.62.0-dev] Premium Large Grid Door Variants

**Type:** MINOR — save-compatible large-grid door content refresh through Step 17 (small-grid door recipe removed from registry)

**Added / Changed:**
- Rebuilt Step 17 grid door generation into large-grid-only door content.
- Added three large-grid door variants:
  - **Large Sci-Fi Sliding Door** — single-panel premium sliding door inspired by Thomas's reference.
  - **Large Double Sliding Door** — two-panel sliding door with synchronized motion activation.
  - **Heavy Vault Door** — reinforced heavy-duty vault door with very high integrity, slower motion, and higher moving power draw.
- Removed the small-grid door from Step 17 generation/recipe registration because it is not needed. Existing old assets are left on disk for compatibility, but the setup registry no longer registers the old small-grid recipe.
- Door prefabs now use layered generated visuals: brushed outer frame, dark inner seal, orange/dark panels, access-glow panels, status strip, vault bolts/bars/core on vault variant.
- Door visuals/collider are offset toward the mounted face so large-grid doors sit on the block edge/face instead of centered awkwardly in the cell.
- `GridBuilder` now detects door items and orients them to the clicked grid face before placement, so doors snap/face correctly on block edges.

**Roadmap Status:**
- Grid doors remain **🛠️ WORKING ON** pending Thomas validation of the new large-only variants, edge snapping, and motion behavior.
- Airtight/pressure integration remains future work.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **17. Build Factory Foundations + HV Grid**.
3. Confirm generated items/prefabs:
   - `GridSlidingDoor_LargeSingle` / Large Sci-Fi Sliding Door
   - `GridSlidingDoor_LargeDouble` / Large Double Sliding Door
   - `GridVaultDoor_Heavy` / Heavy Vault Door
4. Confirm the old small-grid sliding door recipe no longer appears in normal crafting/registry output after setup.
5. Place each door on the edge/side face of a large grid block and confirm the visual sits on the face and faces outward correctly.
6. Test open/close and motion activation for all three variants.
7. Confirm Heavy Vault Door has much higher integrity/health than the sliding doors.

### [5.61.0-dev] Motion-Activated Grid Sliding Doors

**Type:** MINOR — new save-compatible grid door block foundation generated through Step 17 (no save schema break)

**Added:**
- Added `GridSlidingDoor`, a powered grid block with manual and motion-activated open/close behavior.
- Added Step 17 generated prefabs/items/recipes for:
  - **Small Grid Sliding Door**
  - **Large Grid Sliding Door**
- Door prefabs include a dark frame, split sliding panels, glowing window panels, and a status strip.
- Door settings include:
  - manual open/close
  - motion activation toggle
  - motion radius
  - hold time
  - slide speed
  - idle and moving power draw
- Added dedicated `GridSlidingDoor` UI panel in `GridBlockUI`.
- Grid doors now appear in Ship Control as **Grid Doors** with state labels `Open` / `Closed`.
- Grid doors implement `IGridDataProvider`, so they can be selected as screen data sources.

**Roadmap Status:**
- Grid doors / motion activation foundation started as an early slice of the later airtight/life-support door roadmap.
- Full airtight/pressure integration remains future work.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **17. Build Factory Foundations + HV Grid**.
3. Verify generated prefabs/items exist:
   - `GridSlidingDoor_Small` / Small Grid Sliding Door
   - `GridSlidingDoor_Large` / Large Grid Sliding Door
4. Place a small/large grid door and confirm the panels slide open/closed from its config panel.
5. Enable Motion and walk into/out of range to confirm automatic opening/closing.
6. Open Ship Control and confirm doors are grouped under **Grid Doors**.
7. Select the door as a screen data source and confirm it reports state/motion/power.

### [5.60.0-dev] LED Strip Path Reservation + End Cap Polish

**Type:** MINOR — save-compatible build interaction validation/polish for LED strips (no save schema break)

**Added / Improved:**
- Added LED strip path validation before placement. Every crossed grid cell must be clear before a stretched LED strip can be placed.
- Normal grid block placement now checks existing stretched LED strip paths and blocks placement into cells reserved by an LED strip visual path.
- `LEDStrip.CoversGridCell()` exposes a lightweight reservation check based on the strip's full visual length, surface offset, and width.
- Added generated end caps to runtime LED strips so stretched strips read as finished physical strips rather than raw glowing bars.
- The placement failure message now clearly reports when the LED path is blocked.

**Roadmap Status:**
- Corner-to-corner LED strip placement remains **🛠️ WORKING ON** pending Thomas validation of path blocking/reservation behavior and endpoint visuals.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Place a long stretched LED strip.
3. Try placing another grid block into a cell crossed by the strip and confirm placement is blocked.
4. Try placing an LED strip through already occupied cells and confirm it is blocked with feedback.
5. Confirm LED strips now show small end caps at both ends.
6. Save/reload a stretched strip and confirm its path still blocks later placement after reload.

### [5.59.5-dev] LED Strip Placement Compile Fix

**Type:** PATCH — compile fix only (no save schema, recipe, balance, or feature behavior changes)

**Fixed:**
- Fixed `CS0136` in `GridBuilder.cs` by renaming the LED preview branch local `cs` variable to `previewCellSize`.
- This removes the local-name collision with the later `cs` variable in the same method scope while preserving all 5.59.4-dev LED strip placement behavior.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Confirm the `GridBuilder.cs(230,27) CS0136` error is gone.
3. Continue validating LED strip edge ghost/even effects polish from 5.59.4-dev.

### [5.59.4-dev] LED Strip Edge Ghost + Even Effects Polish

**Type:** PATCH — LED strip placement/effect polish (no save schema break)

**Fixed / Improved:**
- First-click LED preview ghost now uses the same surface/edge snapping logic as final placement, so the starting ghost should snap to face center or face edge before placement.
- Edge detection now uses the actual clicked mounted face cell and hit point, improving edge-vs-center detection before final placement.
- Segment mode no longer lights the whole diffuser strongly; the continuous diffuser is dimmed while individual diode segments carry the visible light.
- Removed runtime point-light hotspots from LED strips; no more every-third segment/icon bright spots.
- Motion detection now checks distance to the full LED strip segment, not only the anchor/start block, so stretched strips trigger from their whole length.
- Clean Strip + Chase now uses a clamped moving pulse that stays inside the strip at the start/end instead of running outside the LED.
- Wake Chase now works in clean-strip mode: one start-to-end fill pass runs, then the strip stays solid while motion remains active.
- Added group LED effect controls in Ship Control group pages: **Sync FX**, **Chase**, **Pulse**, and **Static** for LED strip groups/categories.

**Roadmap Status:**
- Corner-to-corner LED strip placement remains **🛠️ WORKING ON** pending Thomas validation of pre-place edge ghost, path reservation, end caps, even segmented brightness, full-length motion activation, clean chase, and grouped sync.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Equip a grid LED strip and aim near the face edge before first click; confirm the start ghost snaps to that edge.
3. Place the strip and confirm final placement matches the ghost.
4. Turn Segments ON and confirm the diffuser is not fully lit behind all segments.
5. Confirm no periodic hotspot/light-icon bright spots remain.
6. Enable Motion on a long stretched strip and approach from the middle/end; confirm it triggers.
7. Use Clean Strip + Chase and confirm the pulse stays within the strip bounds.
8. Enable Motion + Wake Chase with segments off and confirm one fill/chase pass runs, then the strip stays solid.
9. Create/select a Ship Control group containing multiple LED strips and use Sync FX / Chase / Pulse / Static to confirm group effects start together.

### [5.59.3-dev] LED Strip Edge Snap + Even Chase Polish

**Type:** PATCH — LED strip placement/visual polish (no save schema break; additive persistence remains backward compatible)

**Fixed / Improved:**
- Edge snapping now uses the actual first clicked block face hit position instead of the new placement cell center, so edge/center detection is based on where the player clicked on the mounted face.
- LED stretch ghost uses the same surface and lateral edge offset as final placement, making preview and placement match more closely.
- LED strips no longer create point lights along the strip, removing the bright hotspots/light icons every few segments. The strip now uses an even emissive diffuser for clean visual brightness.
- Clean Strip + Chase mode no longer blinks; it now uses a moving emissive chase pulse along the continuous strip.
- Added **Wake Chase** option to LED strip motion activation: when motion turns the strip on, it runs one start-to-end chase/fill pass, then stays solid until motion expires.
- Wake Chase setting is persisted through `SavedLightingConfig`.

**Roadmap Status:**
- Corner-to-corner LED strip placement remains **🛠️ WORKING ON** pending Thomas validation of edge snap, matching ghost, even brightness, clean-strip chase, and wake chase.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Place a stretched LED strip by first-clicking near a face edge; confirm it snaps to that edge.
3. Confirm the cyan ghost is on the same face/edge as the final placed strip.
4. Confirm LED brightness is even and no every-third light hotspot remains.
5. Set LED strip to Clean Strip + Chase and confirm a chase pulse moves along the strip instead of blinking.
6. Enable Motion + Wake Chase, walk into sensor range, and confirm one chase pass runs from start to end before the strip stays solid.

### [5.59.2-dev] LED Strip Face/Edge Snap + Cost Polish

**Type:** PATCH — LED strip placement polish and balance behavior (no save schema break)

**Fixed / Improved:**
- LED strip visuals are now positioned closer to the mounted block face so they sit flush instead of hovering.
- LED strip placement now uses the first clicked face position to snap laterally to either the face center or the nearest face edge.
- Stretch ghost now uses the exact same surface/edge offset as final placement, so preview better matches the actual placed strip.
- LED strip cost now scales by occupied length: a 3-cell strip requires and consumes 3 LED strip items.
- If the player does not have enough LED strip items for the selected length, placement is blocked with a clear feedback message.
- Full-length collider remains active so right-click config works from the middle/end of stretched strips.

**Roadmap Status:**
- Corner-to-corner LED strip placement remains **🛠️ WORKING ON** pending Thomas validation of edge snapping, closer surface placement, and length-based item cost.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Equip a grid LED strip and test top-face center placement; confirm it sits closer/flush to the block.
3. Aim near a top-face edge before first click, place a stretched strip, and confirm it snaps to that edge.
4. Repeat on side faces.
5. Place a 3-cell strip and confirm it consumes 3 LED strip items.
6. Try placing a strip longer than your available LED strip count and confirm placement is blocked.
7. Confirm the ghost preview matches final placement height/edge offset.

### [5.59.1-dev] Surface-Snapped LED Strip Placement Polish

**Type:** PATCH — LED strip placement/interaction polish (no save schema break; uses existing additive offset fields)

**Fixed / Improved:**
- Corner-to-corner LED strips now mount onto the targeted block face instead of floating above the grid block.
- LED strips now build a surface-aware rotation where local strip **Y** follows the selected face normal and local **X** follows the strip direction.
- Visual strip offset now moves the strip back to the shared face plane (`-cellSize / 2`) so it touches the selected top/side face.
- Second-corner snapping ignores the mount-normal axis, so top-mounted strips run along top-face X/Z directions and side-mounted strips run along the side-face axes.
- LED strip interaction collider now covers the whole visual strip length, not only the first anchor cell.
- Long stretched LED strips can now be right-clicked from anywhere along their visual length to open config.

**Roadmap Status:**
- Corner-to-corner LED strip placement remains **🛠️ WORKING ON** pending Thomas validation of top/side snapping and full-length interaction.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Equip a grid LED strip and aim at the top face of a block.
3. Right-click first point, aim along the top face, right-click second point. Confirm it touches the top face instead of floating.
4. Repeat on a side face. Confirm it snaps to/touches the side.
5. Right-click the middle/end of a long stretched LED strip and confirm config opens from the whole strip, not only the start.

### [5.59.0-dev] Corner-to-Corner LED Strip Placement Foundation

**Type:** MINOR — new save-compatible build interaction for LED strips (no save schema break; additive LED offset fields remain backward compatible)

**Added / Improved:**
- GridBuilder now recognizes grid LED strip items and switches them into a two-click corner placement workflow:
  1. Right-click first grid corner/cell to anchor the strip.
  2. Aim a second grid corner/cell on the same grid and right-click again to place a stretched LED strip.
- Placement snaps the second point to the dominant grid axis from the first point, producing straight X/Y/Z strips rather than diagonal/ambiguous strips.
- Added a premium cyan stretch ghost between first and second point so the player sees the final strip length before confirming.
- Final LED strip uses `LEDStrip.SetStretch(length, offset)` so it visually spans from the first selected point toward the second selected point.
- Stretched LED strips persist their local visual offset through the existing `SavedLightingConfig` path.
- Standard one-cell LED strip placement remains possible by clicking the same cell twice.

**Known Scope / Next Polish:**
- This is the placement foundation. The stretched LED strip is anchored as one grid block at the first selected cell while its visuals extend toward the second point. Full multi-cell occupancy/reservation validation is planned as a follow-up so very long strips can reserve every crossed grid cell if desired.

**Roadmap Status:**
- Grid/static lighting and LED strips remain **🛠️ WORKING ON** pending Thomas's Unity validation of corner placement, ghost preview, and save/load of stretched strips.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Equip a Small or Large Grid LED Strip item.
3. Aim at an existing grid and right-click once to set the first corner.
4. Aim along the grid X/Y/Z direction and confirm the cyan stretch ghost follows the second point.
5. Right-click again to place the stretched LED strip.
6. Repeat by clicking the same cell twice to place a short/default strip.
7. Save/reload and confirm stretched strip length/offset restores.

### [5.58.0-dev] Lighting Runtime Persistence + Screen Restore Guard Fix

**Type:** MINOR — additive save-compatible persistence fields for placed lighting config (legacy saves remain compatible)

**Added / Fixed:**
- Added additive `SavedLightingConfig` persistence for placed blocks that contain `GridLightBlock` and/or `LEDStrip`.
- Placed spotlight/light settings now save and restore:
  - color
  - range
  - cone angle
  - intensity
  - light type
  - watts draw
  - motion activation
  - motion radius
  - motion hold time
- Placed LED strip settings now save and restore:
  - color
  - brightness
  - length
  - segment count
  - strip width
  - segmented/clean mode
  - animation mode
  - animation speed
  - motion activation
  - motion radius
  - motion hold time
  - watts draw
- Fixed a persistence restore guard bug where factory runtime restore returned early when `saved.machine == null`, preventing non-machine configs after that point, including screen configs, from restoring on placed blocks.
- Lighting config restore is additive and null-safe; legacy saves without `lightingConfig` load normally.

**Scope Note:**
- This pass persists lighting configs for the existing `WorldStatePersistence` placed-block path. Full movable-grid save/load persistence is not present in this repository path yet, so tuned settings on future fully persisted movable grids will need to use the same `SavedLightingConfig` data when grid save serialization is added.

**Roadmap Status:**
- Grid/static lighting and LED strips remain **🛠️ WORKING ON** pending Unity save/load validation.
- Corner-to-corner LED strip placement foundation is implemented; 5.59.1-dev adds surface snapping/touching and full-length interaction collider polish.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Place/configure a static/placeable spotlight or LED strip where WorldStatePersistence tracks placed blocks.
3. Change color/intensity/range/motion/LED mode/length/segments.
4. Save and reload the world.
5. Confirm tuned settings restore.
6. Re-test any placed screen config save/load, because 5.58.0-dev also fixes the early return that could skip screen config restore on non-machine placed blocks.

### [5.57.3-dev] Grid Screen Black Display Fix

**Type:** PATCH — visual bug fix only (no save schema, recipe, or balance changes)

**Fixed:**
- Fixed grid screens appearing black after the depth-test text pass.
- Screen text is now positioned farther in front of the physical screen surface, preventing the screen's own surface mesh from occluding the text when depth testing is enabled.
- `MakeTextOpaque()` now preserves Unity's working TextMesh font material/shader and only changes depth-test settings, instead of swapping to a generic cutout shader that could make glyphs invisible/black.
- Text still uses depth testing against world geometry, so the previous "text visible through ground/blocks" issue remains addressed without self-occluding the display.

**Roadmap Status:**
- Grid screens remain **✅ COMPLETED** once Thomas validates screen text/feed visibility again.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Enter Play Mode and look at a grid screen in any text mode such as Custom, Power, or Summary.
3. Confirm text is visible again and no longer just black.
4. Put terrain/blocks between the camera/player and the screen and confirm text no longer renders through occluders.
5. Re-test Camera mode if needed; Screen Config should now open from 5.57.2-dev and text fallback/status should be visible.

### [5.57.2-dev] Screen Config Access + Data-Type Visibility Fix

**Type:** PATCH — runtime UI/filter fix only (no save schema, recipe, or balance changes)

**Fixed:**
- Screen Config right-click now uses a guaranteed `GridScreenConfigUI.Instance` path, so the config UI is created/found before opening instead of silently doing nothing when the singleton was null.
- `GridScreenConfigUI` now ensures a `UIDocument` exists on its GameObject during Awake, preventing misconfigured objects from failing to mount.
- Grid Screen right-click handling now runs before generic grid-block UI handling, so screens always open Screen Config first.
- Ship Control data type controls no longer enable/disable actual blocks.
- Data type controls now only show/hide that category from Screen Config source lists.
- Selected-block data type controls were renamed to `TYPE SHOW` / `TYPE HIDE` and now only affect Screen Config visibility.
- Hidden data types are included in `IsHiddenFromScreenConfig()`, so categories like Spotlights can be hidden from the Screen Config picker without turning off the lights.

**Roadmap Status:**
- Grid lighting remains **🛠️ WORKING ON**; this patch corrects the intended data-type behavior before the next validation pass.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Right-click a grid screen and confirm Screen Config opens.
3. In Ship Control, click `Show Types`, then `Hide` for Spotlights. Confirm the spotlights stay powered/working.
4. Open Screen Config and confirm Spotlights are not offered as selectable sources.
5. Click `Show` for Spotlights and confirm they appear again in Screen Config.
6. Confirm the screen no longer stays black because the config can be opened and display mode/source can be changed.

### [5.57.1-dev] Grid Screen Config Singleton + Power Relay Missing Script Fix

**Type:** PATCH — compile/runtime prefab fix only (no save schema, recipe, or balance changes)

**Fixed:**
- `GridScreenConfigUI` no longer destroys/removes its own component when a duplicate singleton exists.
- Runtime singleton handling now prefers the scene/player-authored `GridScreenConfigUI` object over an auto-generated root object.
- Runtime-generated root `GridScreenConfigUI` is the only variant marked `DontDestroyOnLoad`; player-child/scene-authored objects are left in place.
- `EnsureInstance()` now first searches for an existing inactive/active `GridScreenConfigUI` before creating a new one.
- Repaired the missing `CompactPowerNode` script reference on the legacy `VoxelEngineAssets/HighVoltage/Prefabs/PowerRelay.prefab`.
- Step 17 now also refreshes the legacy `PowerRelay` prefab path non-destructively so old worlds/items are repaired alongside the newer LV/HV relays.

**Roadmap Status:**
- Grid lighting remains **🛠️ WORKING ON**; this patch clears setup/runtime errors before the next validation pass.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Enter Play Mode and confirm the `GridScreenConfigUI` component remains on its object.
3. Confirm no `PowerRelay` missing script warning appears.
4. If the warning persists from an already-open prefab instance, run `Tools > Voxel Engine > Voxel Engine Setup` → **17. Build Factory Foundations + HV Grid** once to refresh the legacy relay prefab.

### [5.57.0-dev] Collapsible Data Types + Motion-Activated Lighting

**Type:** MINOR — save-compatible lighting/control UX feature (no save schema migration)

**Added / Improved:**
- Ship Control `DATA TYPES` is now hidden/collapsed by default.
- Added a `Show Types` / `Hide Types` button in the terminal block-list header.
- Blocks hidden through Ship Control no longer appear in Screen Config source lists. Existing linked sources remain functional, but hidden blocks are not offered as new selectable screen sources.
- LED strips now support a player-facing **Segments: ON / Clean Strip** toggle.
- LED strip `Chase` mode now visibly chases across diode segments instead of looking static.
- LED strip point lighting is distributed along the strip instead of using one center point light, reducing the brighter-middle hotspot.
- LED strips now support **Motion Activation**:
  - Motion ON/OFF
  - Sensor radius
  - Hold time after last detection
- Grid spotlights now also support **Motion Activation** with radius and hold time controls.
- Motion activation turns the light on when a player is nearby, enabling motion-sensitive ship/base lighting.

**Door Note:**
- Grid doors are not yet authored as grid blocks in the current repository, so the motion-sensor UI was added to lights first. The same motion-activation pattern is ready to be applied when grid doors/airtight doors are added in the later life-support/building pass.

**Roadmap Status:**
- Grid lighting remains **🛠️ WORKING ON** while Thomas validates collapsed data types, hidden source filtering, clean/segmented LED mode, chase animation, and motion activation.
- Next planned implementation target remains runtime persistence for tuned spotlight/LED settings, then corner-to-corner LED strip placement.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Open Ship Control Terminal and confirm `DATA TYPES` is hidden by default.
3. Click `Show Types`; confirm the data-type ON/OFF controls appear. Click `Hide Types`; confirm they collapse.
4. Hide a block/group in Ship Control, then open Screen Config and confirm that hidden block is not offered as a new source.
5. Open an LED strip config panel and toggle `Segments: ON` / `Clean Strip`.
6. Set LED strip mode to `Chase` and confirm the lit segment travels along the strip.
7. Confirm LED strip brightness is no longer concentrated at only the middle.
8. Enable Motion on an LED strip or spotlight, walk out of radius and back in, and confirm it turns off/on based on player proximity.

### [5.56.0-dev] LED Strip Screen Data Provider + Component Source Resolution

**Type:** MINOR — save-compatible screen/data UX feature (no save schema migration)

**Added / Improved:**
- `LEDStrip` now implements `IGridDataProvider`, so LED strips can be selected as screen data sources.
- LED strip screen data reports state, mode, draw, length, and brightness.
- `GridScreenBlock` data-source resolution now supports provider components attached to a `GridBlock`, not only `GridBlock` subclasses.
- Screen auto-link and available source lists now find component-based providers such as LED strips.
- This keeps future utility components lightweight: they can expose screen data without needing a new `GridBlock` subclass.

**Roadmap Status:**
- Ship Control data-type toggles and LED config from 5.55.0-dev remain ready for Thomas validation.
- Grid/static lighting remains **🛠️ WORKING ON**. Next planned implementation target: runtime persistence for tuned spotlight/LED settings, then corner-to-corner LED placement.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Place a grid LED strip and a screen on the same grid.
3. Right-click the screen and verify the LED strip appears as a selectable Light source.
4. Select the LED strip and use Mixed/Summary or System display mode.
5. Confirm the screen reports LED strip state, mode, draw, length, and brightness.
6. Change the LED strip mode/brightness/length from its config panel and confirm the screen data updates live.

### [5.55.0-dev] Ship Control Data-Type Toggles + LED Strip Configuration UI

**Type:** MINOR — save-compatible terminal/lighting UX feature (no save schema migration)

**Added / Improved:**
- Ship Control Terminal now includes a **DATA TYPES** section in the left block list.
- Each data type row shows enabled count / total count and provides **ON** / **OFF** buttons.
- The selected block details page now also shows its data type and has **TYPE ON** / **TYPE OFF** controls for that whole category.
- Spotlights are categorized as **Spotlights**, so the player can disable/enable all spotlight blocks as a single data type.
- LED strip grid blocks are categorized as **LED Strips** and also support type-wide ON/OFF controls.
- Added dedicated right-click/grid-terminal LED strip config panel:
  - On / Off
  - Static / Pulse / Blink / Chase animation mode
  - Brightness
  - Runtime Length
  - Segment count
  - Color presets
- LED strip config uses the existing runtime `SetLength(float meters)` foundation, preparing for the future corner-to-corner placement tool.

**Roadmap Status:**
- Grid lighting remains **🛠️ WORKING ON** while Thomas validates data-type toggles and LED strip config.
- Grid/static lighting and LED strips remain **🛠️ WORKING ON**; next planned step is persistence for tuned spotlight/LED settings, then corner-to-corner LED placement.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Open Ship Control Terminal.
3. In **DATA TYPES**, click **OFF** beside **Spotlights** and confirm every spotlight disables.
4. Click **ON** beside **Spotlights** and confirm every spotlight enables.
5. Select an individual spotlight and test **TYPE OFF** / **TYPE ON** from its details panel.
6. Place/select a grid LED strip, right-click it, and verify the LED strip config panel opens.
7. Change LED color, brightness, length, segments, and animation mode; confirm it updates live.

### [5.54.0-dev] Grid Spotlight Right-Click Configuration UI

**Type:** MINOR — new save-compatible grid lighting configuration UI (no save schema migration; tuned light setting persistence remains future work)

**Added / Improved:**
- Right-clicking a `GridLightBlock` / grid spotlight now opens its configuration panel when the player is not holding a grid block.
- Added a dedicated `GridLightPanel` in `GridBlockUI` instead of falling back to the generic `INFO` panel.
- Grid spotlight config now exposes live controls for:
  - On / Off
  - Intensity
  - Range
  - Cone angle
  - Color presets: White, Warm, Cyan, Blue, Green, Amber, Red
  - Reset Defaults
- The panel header now reports actual light state: `ON`, `OFF`, or `NO POWER` instead of generic `INFO`.
- Ship/grid terminal block state labels now show grid lights as `On` / `Off`.
- Grid lights are categorized under **Grid Lighting** in the ship/grid terminal.
- Dual-output spotlights use the same config and apply changes to all beam lights through the existing multi-light control path.

**Roadmap Status:**
- Grid lighting remains **🛠️ WORKING ON** while Thomas validates right-click config and terminal labels.
- Grid/static lighting and LED strips remain **🛠️ WORKING ON**; runtime persistence for customized light settings is still pending.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Place any grid spotlight and right-click it with empty hands / non-grid item equipped.
3. Confirm the spotlight config panel opens.
4. Change Intensity, Range, Cone, and Color; confirm the light updates live.
5. Open the Ship/Grid Control panel and select a spotlight. Confirm it shows `ON`, `OFF`, or `NO POWER` instead of generic `INFO`.
6. Test a dual spotlight and confirm both beams change together.

### [5.53.0-dev] Large Grid Spotlights + Premium Segmented LED Strip Variants

**Type:** MINOR — new save-compatible grid lighting content/setup plus screen rendering polish (no save schema migration)

**Added / Improved:**
- Step 17 now generates large-grid lighting content non-destructively through `Tools > Voxel Engine > Voxel Engine Setup`:
  - **Large Grid Spotlight** — single long-range industrial spotlight.
  - **Small Dual Grid Spotlight** — compact two-beam spotlight.
  - **Large Dual Grid Spotlight** — large-grid two-beam flood spotlight.
  - **Large Grid LED Strip** — larger segmented grid LED strip.
- Existing small grid spotlight item is renamed to **Small Grid Spotlight** with a clearer description.
- Spotlights are inspired by Thomas's reference: rugged lamp cans, black bezels, hot lenses, grille bars, body/mount details, and dual-output variants where applicable.
- `GridLightBlock` now controls every non-status child `Light`, so dual-output prefabs can use two synchronized beam lights without unmanaged stray lights.
- `LEDStrip` visual generation rebuilt into a more realistic segmented strip:
  - dark backing rail, lit diffuser, individual diode segments, configurable width/length/segment count.
  - supports runtime `SetLength(float meters)` as a foundation for future corner-to-corner LED placement.
  - small and large setup-authored variants use different default lengths/segment counts.
- Screen TextMesh depth handling hardened: screen text now uses a depth-tested cutout material (`ZTest LessEqual`, `ZWrite On`, alpha-test queue) so text should no longer render through terrain or blocks.

**Roadmap Status:**
- Grid lighting: **🟡 PARTIALLY COMPLETE → 🛠️ WORKING ON** while the new Step 17 content awaits Unity validation.
- Grid/static lighting and LED strips: **🟡 PARTIALLY COMPLETE → 🛠️ WORKING ON** for Step 17 generation + validation.
- LED corner-to-corner placement: **🟡 PARTIALLY COMPLETE foundation** — runtime length is supported; interactive two-corner placement workflow remains a future build-tool step.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **17. Build Factory Foundations + HV Grid**.
3. Run Step 17 a second time to verify it remains idempotent/non-destructive.
4. Verify new generated prefabs/items exist:
   - `GridSpotlight_Large` / Large Grid Spotlight
   - `GridSpotlight_DualSmall` / Small Dual Grid Spotlight
   - `GridSpotlight_DualLarge` / Large Dual Grid Spotlight
   - `LEDStrip_LargeGrid` / Large Grid LED Strip
5. Place small and large grid variants and confirm sizes match their grid type.
6. Power the grid and confirm single/dual spotlights turn on/off with grid power.
7. Place small and large LED strips and confirm the segmented diode strip visuals appear.
8. Put a screen behind terrain/blocks and confirm screen text no longer shows through occluders.

### [5.52.0-dev] Grid Light Power-State Hardening + Screen Data Provider

**Type:** MINOR — save-compatible grid lighting feature/polish (no save schema migration; no recipe or balance reset)

**Added / Improved:**
- `GridLightBlock` now implements `IGridDataProvider`, so configurable screens can use Grid Lights as a live data source.
- Grid Light display data reports state, draw, range, and intensity.
- Grid Light now exposes stable source/category labels: `Grid Light Block` / `Light`.
- Grid Light now respects actual grid power for illumination: enabled + powered grid = on; unpowered grid = light off with red indicator.
- Grid Light `PowerDraw` remains counted while enabled even during a deficit, so Power screens continue to show the light in current loss instead of hiding the load.
- Added `wattsDraw` as an inspector-configurable power draw value while preserving the previous 25 W default.
- Runtime light/indicator creation is now idempotent and reuses existing generated children where present instead of duplicating them.
- Runtime indicator now updates live: configured light color when online, red when unpowered, muted grey when disabled.

**Roadmap Status:**
- Grid screens / displays: **🛠️ WORKING ON → ✅ COMPLETED** after Thomas validated the camera feed/config fixes.
- Grid lighting: **❌ MISSING → 🟡 PARTIALLY COMPLETE** based on repository audit plus this power/data-provider polish.
- 4.5.0 Grid/static lighting and LED strips remain **🟡 PARTIALLY COMPLETE** pending full lighting configuration UX and Unity validation.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Place a powered grid with a Grid Light Block and a Screen.
3. Right-click the screen and select the Grid Light as a data source.
4. Use Mixed/Summary or System display mode and verify the light reports state, draw, range, and intensity.
5. Remove/disable grid power and confirm the light turns off and its indicator turns red.
6. Restore power and confirm the light turns back on and the indicator returns to the configured light color.
7. Open a Power screen and confirm the light appears in current loss while enabled.

### [5.51.3-dev] Screen Config UI Toolkit Compile Fix

**Type:** PATCH — compile fix only (no save schema, recipe, balance, or runtime behavior design changes)

**Fixed:**
- Fixed `CS1061` in `GridScreenConfigUI.cs` caused by using `IStyle.zIndex`, which is not available in this Unity UI Toolkit version.
- Replaced root z-index assignment with `VisualElement.BringToFront()` and kept the high `UIDocument.sortingOrder` reflection path for supported Unity versions.
- Updated the frontmost-panel fallback comment so it matches the Unity-safe implementation.

**Roadmap Status:**
- Grid screens / displays remain **🛠️ WORKING ON** pending Thomas's Unity validation after compile succeeds.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Confirm the `GridScreenConfigUI.cs` compile error is gone.
3. Re-test opening Screen Config from Ship Control / grid terminal and confirm it still appears in front.

### [5.51.2-dev] Cockpit Right-Click + Screen Config Layering + Camera Feed Isolation

**Type:** PATCH — interaction/UI/camera-feed fixes (no save schema, no recipe balance changes, no breaking API change)

**Fixed / Improved:**
- Restored right-click entry for `GridCockpit` when the player is not holding a grid block. Existing Helm / Ship Control Console right-click entry remains intact.
- Screen Config UI now forces itself to the front with a high UIDocument sorting order and root z-index, so opening screen config from the ship control terminal no longer appears behind the ship control UI.
- Camera identity repair now treats default `Iron Ore` / `iron_ore` item identity as invalid for camera blocks. Runtime camera source names normalize to `Camera Block`, and Step 19 repairs the camera item name/description non-destructively.
- Camera item description upgraded to: live screen stream, 30 W draw, and green/yellow/red LED state explanation.
- Power display mode now shows grid-wide current gain, current loss, and net power using W/kW/MW formatting.
- Camera display mode now uses the most recently selected camera source if multiple camera sources exist. Selecting a camera source in Screen Config makes it the screen's primary source and switches the screen to Camera mode, preventing multiple cameras from interfering with the same screen.
- Each camera feed now uses a unique hidden runtime camera and unique RenderTexture name based on instance id.
- Screen camera feed now also uses a dedicated runtime quad in front of the screen surface with culling disabled, making the live video path independent of cube-face UV/culling quirks.

**Roadmap Status:**
- Grid screens / displays remain **🛠️ WORKING ON** until Thomas validates live feed, screen config layering, power mode, and camera identity in Unity.
- Camera block live feed remains **🛠️ WORKING ON** pending this validation pass.

**Manual Unity Steps:**
1. Let Unity recompile.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` → **19. Setup Grid Screens & Displays (Non-Destructive)** once to repair the Camera Block item name/description if it still appears as Iron Ore in inventory/crafting.
3. Right-click a Cockpit with empty hands or a non-grid item equipped — confirm you enter the cockpit.
4. Right-click a Helm / Ship Control Console — confirm control-seat entry still works.
5. Open Ship Control / grid terminal, open a screen config from that UI, and confirm Screen Config appears in front.
6. Set screen mode to Power and confirm it shows Gain, Loss, and Net.
7. Link Camera A to a screen, then add Camera B and select it in Screen Config — confirm the selected camera owns that screen and feeds do not mix.
8. Confirm the live camera feed is visible on the screen, not black.

### [5.51.1-dev] Camera Feed Visibility + Screen Appearance Live Update Fixes

**Type:** PATCH — bug fixes and UI polish only (no save schema, recipe, balance, or API-breaking changes)

**Fixed:**
- Fixed camera screens appearing black by enabling the capture camera while a screen samples the feed texture and by using an unlit runtime screen-feed material so the video does not depend on scene lighting.
- Camera feed now prepares the capture camera immediately on texture access, removing the disabled-camera/stale black texture case.
- Screen `Border` setting now live-updates generated glow strips and corner dots while the config panel is open.
- Screen `Font` setting now live-updates the screen TextMesh style, size, character scale, and spacing while the config panel is open.
- Border and Font buttons now refresh their active-highlight state immediately when clicked.
- Custom Text input styling now forces a dark editor field with bright readable text across the TextField internals, fixing white-on-white unreadable input.

**Roadmap Status:**
- Grid screens / displays remain **🛠️ WORKING ON** until Thomas validates live feed, appearance controls, and custom text in Unity.
- Camera block live feed remains **🛠️ WORKING ON** pending this Unity validation pass.

**Manual Unity Steps:**
1. Let Unity recompile; no setup rerun is required for this patch.
2. Use your existing test grid with a powered Camera Block and Screen.
3. Right-click the screen → select the camera source → choose **Camera** display mode.
4. Confirm the screen now shows live video instead of black.
5. Change Border between None / Thin / Thick / Glow and confirm the screen updates immediately.
6. Change Font between Default / Mono / LCD / Terminal and confirm the screen text updates immediately.
7. Switch to Custom mode, edit the Custom Text field, and confirm the input text is readable.

### [5.51.0-dev] Live Camera Screen Feed + Premium Camera Prefab

**Type:** MINOR — save-compatible screen/camera feature (no save schema migration; Camera display mode is appended and old screen configs remain parse-compatible)

**Added / Changed:**
- Added optional `IGridCameraFeedProvider` for live camera sources without changing the lightweight text data-provider path.
- Added `ScreenDataMode.Camera` so configurable grid screens can switch from text/data modes to a live camera feed mode.
- `GridScreenBlock` now resolves the linked camera source, registers itself as an active feed consumer, applies the camera RenderTexture directly to `Generated_ScreenSurface`, hides the center text overlay while the feed is live, and keeps a clean `CAMERA / LIVE` title/status overlay.
- `GridCameraBlock` now renders only when at least one powered screen is actively consuming the feed, with configurable feed resolution and render interval for performance.
- Camera feed orientation now looks out through the generated lens (`lensLooksAlongNegativeZ`) instead of capturing from the block body.
- Camera status LED behavior added: **green** when at least one screen is using the feed, **yellow** when the camera is online but idle, and **red** when the camera is offline/no grid power.
- `GridScreenConfigUI` now exposes Camera mode automatically and updates the no-source hint to include cameras.

**Setup Wizard (Non-Destructive):**
- Step 19 now refreshes generated screen visuals and the camera prefab while preserving custom non-generated child objects.
- Step 19 now repairs required item/prefab/recipe/registry links without resetting existing stack sizes, mass, hit points, recipe costs, crafting times, unlock flags, or authored tuning.
- Camera prefab remade through Step 19 with a boxy warm-alloy housing, dark lens stack, bolted front flange, side mount ears, lower rail, glass highlight, and physical status LED/light inspired by Thomas's reference image.

**Roadmap Status:**
- 4.8.0 Logistics 2.0, Screens & Trajectory: **🛠️ WORKING ON**.
- Grid screens / displays: **✅ COMPLETED → 🛠️ WORKING ON** while the new live camera feed and Step 19 premium camera prefab await Unity validation.
- Camera block live feed: **🛠️ WORKING ON** (runtime code + setup authoring complete; manual validation pending).

**Manual Unity Steps:**
1. Open the project on the `Dev` branch and let Unity finish compiling.
2. Go to `Tools > Voxel Engine > Voxel Engine Setup`.
3. Run **19. Setup Grid Screens & Displays (Non-Destructive)**. Run it a second time to confirm idempotency and check the Console for preserved/repaired logs.
4. Inspect `VoxelEngineAssets/GridSystem/Prefabs/CameraBlock.prefab` — confirm the new warm-alloy camera body, lens stack, mount ears, bolts, and status LED/light are present.
5. In a test scene, place a powered grid with a Camera Block and any Screen block.
6. Right-click the screen → select the Camera source → choose **Camera** display mode.
7. Verify the screen surface shows the live camera view. The camera LED should be **green** while the screen is using the feed.
8. Switch the screen away from Camera mode or clear the source — the powered camera LED should turn **yellow** after a short moment.
9. Disable camera/grid power — the camera LED should turn **red**, and the screen should show camera offline text.
10. Re-test existing text/data screen modes to confirm Summary, Power, Inventory, Bars, Custom text, color, border, and font settings still work.

### [5.42.0-dev] Factory Persistence Complete — Funnel Buffer & Mode Save/Load

**Type:** MINOR — new save-compatible factory persistence (additive fields, fully backward compatible)

**Added — Funnel persistence (was the last missing piece):**
- **Funnel buffer save/restore:** `CaptureFactoryRuntime` now checks for a `Funnel` component and saves its internal buffer items (item ID, count) plus operating mode (Import/Export) into a new `SavedFunnelState` class.
- **Funnel mode restore:** Load restores the funnel's mode via `SetMode()` and re-inserts buffered items.
- Added `public ItemContainer Buffer` property to `Funnel.cs` to expose the private `_buffer` for persistence access.
- Added `SavedFunnelState` to the save schema with `mode` (string) and `bufferItems` (list of SavedTransportItem).

**Existing persistence already complete (validated):**
- ConveyorBelt items (progress, lateralOffset) ✓
- ConveyorChute items (slideProgress) ✓
- Crusher recipeId + progress + userEnabled + input/output/upgrade containers ✓
- Assembler recipeId + progress + userEnabled + input/output/upgrade containers ✓
- ElectricFurnace input/output/upgrade containers ✓
- Furnace input/fuel/output containers ✓
- Chest, Drawer, StorageDisplay containers ✓
- Quarry depth/cursor/phase/upgrades/output ✓
- Player position/rotation/inventory/hotbar ✓
- Tiered building placement/tier/HP ✓
- Item-port routing config (per-face direction + filters) ✓
- Legacy saves remain 100% compatible ✓

**Roadmap Status:**
- 4.5.0 Factory persistence: **🛠️ WORKING ON → ✅ COMPLETED**
- All factory machines now fully persist their runtime state across save/load cycles.

**Manual Unity Steps (no setup step needed):**
1. Place a Funnel in Import or Export mode with some items buffered, save/load — verify mode and buffer
2. Place a ConveyorBelt with items moving, save/load — verify items resume at correct progress
3. Place a Crusher with active recipe, save/load — verify recipe and progress resume

### [5.41.0-dev] Research UI Spatial Pan/Zoom Canvas Overhaul

**Type:** MINOR — new save-compatible UI overhaul (no save/API/balance touch)

**Added / Changed — ResearchUI.cs completely rebuilt:**
- **Spatial pan/zoom canvas** replaces the old fixed-size tier-column layout. The tree surface now scales via `_canvas.style.scale` with a responsive Zoom slider (− / + / reset buttons) ranging from 35% to 200%.
- **Zoom controls** in the header bar: [−] [Zoom %] [+] [Reset] buttons with premium dark styling.
- **Breathing glow effect** on available (ready-but-not-started) research nodes — a subtle pulsing cyan halo that cycles opacity at 1.8 Hz, drawing the player's eye to what they can research next.
- **Pulsing connector lines** — bezier prerequisite arrows between nodes now pulse cyan when the prerequisite is met and the target node is ready. Completed paths show a solid green line.
- **Era label** in the header auto-updates based on the highest tier researched (shows "Era 1: Mechanized" through "Era 7: Architect").
- **Bottom details panel** replaces the old right-side panel — collapsible, shows node name, tier, cost pills with have/need counts, description, action buttons (Research Now / Start at Lab / Cancel), and unlock preview (up to 4 recipe names).
- **Spacebar shortcut** — pressing SPACE researches the selected node (instant-research from inventory or starts lab research).
- **Increased card size** to 190×110 with compact cost icons (12px swatches) for better readability.
- **Cleaner layout** — panel is now 94% width × 92% height (max 1400×860) for better responsiveness.
- **Node card glow/shadow** — cards have breathing outer glow when available, and subtle hover scale (1.03x) with micro-transitions.
- Added optional `eraLabel` field to `ResearchNode.cs` for future era categorization.

**Roadmap Status:**
- 4.6.0 Research UI overhaul: **❌ MISSING → 🛠️ WORKING ON** (spatial canvas complete; power-user polish and era-based grouping next)

**Manual Unity Steps (no setup step needed):**
1. Open the game scene and press the Research key (default `T`)
2. Verify the new spatial canvas with zoom controls appears
3. Click a research node — verify bottom details panel updates
4. Press SPACE on an available node — verify research starts
5. Use [+] and [−] zoom buttons — verify canvas scales smoothly
6. Hover a ready node — verify the breathing glow is visible
7. Try the category filters on the left — verify tree re-filters

### [5.40.1-dev] Premium Wheel Text Cutoff Fixes + Grid Shape Variant Wheel Foundation

**Type:** PATCH + MINOR foundation — save-compatible UI polish + new reusable wheel (no save/API touch)

**Fixed:**
- HammerBuildWheel segment labels: increased container sizes (80×68), radius, font sizes, switched to Overflow.Visible, better positioning — eliminates text cutoff on long family names and costs.
- Center disc labels: increased fonts, added explicit whiteSpace.NoWrap for title/page/hint — no more clipping.
- ConveyorShapeWheel segment labels: same treatment (76×60 containers, larger icons, Overflow.Visible, adjusted radius) — clean full labels.

**Added:**
- New `GridShapeWheel.cs` (UI/GridShapeWheel) — full premium radial wheel for grid block shape variants (Cube / Slope / HalfBlock / HalfSlope / Corner / InvertedSlope).
  - Matches exact visual language of the polished Hammer + Conveyor wheels (cream ring, deep overlay, accent colors, micro scale/glow/hover, parallax).
  - Self-contained: automatically shows when holding a structural grid/armor block item while BuildWheel is held.
  - CurrentShape static accessor ready for future placement logic (GridBuilder, GridBlockMeshBuilder, etc.).
- Auto-spawn of GridShapeWheel added to Step 2 (Spawn Player + UI) in VoxelEngineSetupWindow — non-destructive (only adds if missing). Sorting order 610 (above hammer wheel).
- Updated roadmap table entry for "Grid shape variant wheel".

**Roadmap Status Updates:**
- Building Hammer wheel & placement: **🛠️ WORKING ON** (premium visuals + text cutoff fixed).
- Conveyor logistics (shape wheel): **🛠️ WORKING ON** (premium visuals + text cutoff fixed).
- Grid shape variant wheel (4.7.0): **🛠️ WORKING ON** (wheel foundation + premium UI complete; shape application logic and setup-authored variants planned for next).
- 4.7.0 Power, Vehicles & Combat: now actively progressing (shape wheel is the first deliverable).

**Manual Unity Steps (no new steps required for polish):**
1. (Optional) Re-run **2. Spawn Player + UI in Scene** if you want the GridShapeWheel auto-added to an existing player.
2. Equip a grid armor / structural block (e.g. from grid system) → hold Build Wheel key.
3. Verify the new premium Grid Shape Variant wheel appears with full visible text.
4. Test the existing Hammer and Conveyor wheels — all labels now fully visible, no cutoff.
5. When ready for actual shape variants: run `Tools > Voxel Engine > Voxel Engine Setup` (Step 18 area) — the wheel is already wired.

**Next Steps Ready:**
- Implement actual shape variant prefabs/variants via the Voxel Engine Setup (non-destructive).
- Hook selected shape into GridBuilder placement + GridBlockMeshBuilder.
- Continue 4.7.0 (armor, weapons, damage, etc.).

### [5.40.0-dev] Premium Build Wheel & Conveyor Shape UI Polish + Roadmap Progress

**Type:** MINOR — save-compatible premium UI visual upgrade for construction wheels (no save/API touch)

**Added / Improved:**
- HammerBuildWheel (building hammer radial selector) completely restyled for premium industrial look:
  - Larger 560px wheel with thicker clean cream/off-white ring matching reference aesthetic.
  - Cleaner segmented ring texture: bold outer/inner borders, subtle industrial bevel, red-tinted active/hover segments.
  - Center disc upgraded to match reference: deep navy background, prominent icon area, larger title + subtitle + cost with better typography and spacing.
  - Enhanced hover/selection feedback: scale + glow + accent ring on segments, premium micro-transitions.
  - Icons and labels repositioned inward with tighter clipping, better contrast.
- ConveyorShapeWheel (conveyor mode radial selector) upgraded in parallel:
  - Same premium ring styling (cream ring on dark overlay, clean segments).
  - Larger center badge with tier + selected mode + premium typography.
  - Improved ring texture, segment hover states, parallax and scale polish.
  - Prompt pill restyled to match overall premium UI.
- Both wheels now use consistent premium color tokens (cream ring, cyan/red accents, deep center).
- Non-destructive code changes only; no prefabs/recipes touched.
- Bumped to 5.40.0-dev (MINOR, save-compatible UI polish).

**Roadmap Status Updates (per guidelines):**
- 4.6.0 Production Lines & UI Revolution: **✅ COMPLETED** (production UI final polish + wheel aesthetics now match premium target).
- Building Hammer wheel & placement: **🛠️ WORKING ON** → upgraded to premium reference style; Unity validation next.
- Conveyor logistics (shape wheel): **🛠️ WORKING ON** → visual polish complete; ready for full validation.
- Grid shape variant wheel (4.7.0): **🟡 PARTIALLY COMPLETE** (roadmap target for later; wheel architecture already reusable).
- All new UI changes follow core pillars: simplicity, sleek aesthetics, production value (micro-interactions + premium ring).

**Manual Unity Steps:**
1. Tools → Voxel Engine → Voxel Engine Setup → **3. Build Main Menu Scene** (ensures theme consistency).
2. Open Game scene, equip Hammer (hold Build Wheel key) — verify new larger premium cream ring, center disc, hover feedback.
3. Equip conveyor item (hold Build Wheel) — verify matching premium conveyor shape wheel.
4. Test hover, click-select, scroll pages, parallax on both wheels at multiple resolutions.
5. No prefab/recipe changes required — pure runtime UI polish.

**Next Roadmap Steps Started:**
- Continuing 4.6.0 completion and moving focus to 4.7.0 Power/Vehicles (armor, grid shape variants, combat prep).
- Grid Shape Variant Wheel will reuse the polished wheel architecture when implemented via Step 18+ setup.

---

### [5.38.1-dev] Theme System Compile Fix

**Type:** PATCH — fix compile errors in theme override and applier

**Fixed:**
- Fixed `UIThemeOverride.cs` CS1061 errors: removed invalid `Crusher.Definition` / `Assembler.Definition` references (machines don't expose Definition property). Now uses only `UIThemeOverride` component for accent/theme resolution.
- Fixed `UIThemeApplier.cs` SetProperty reflection path to avoid CS1061 on `IStyle.SetProperty` — now uses TryGetMethod with safe fallback, no hard dependency on Unity's internal SetProperty extension.
- Removed hard `style.SetProperty` calls from `UITheme.Panel()` and `AccentDivider()` that caused compile errors on older UI Toolkit versions; USS vars now injected solely via `UIThemeApplier.ApplyThemeToRoot()` which is safe.
- `ThemedPanel` and `ThemedDocument` now use coroutine `DelayedApply` instead of string-based `Invoke(nameof(...))` to support protected methods.
- Bumped version to 5.38.1-dev (PATCH — no save/API touch).

---

### [5.38.0-dev] Complete UI Theme System — USS Variables, ThemedPanel & Custom Editor

**Type:** MINOR — save-compatible completion of UI theme system (4.6.0)

**Added:**
- Added `ThemedPanel` abstract MonoBehaviour base class — all premium panels derive from this, subscribe to `UIThemeManager.OnThemeChanged`, apply theme reactively without scene reload.
- Added `ThemedDocument` lightweight component for existing UIDocuments (GameUI, MainMenu, Pause) to become theme-reactive.
- Added `UIThemeApplier` static utility that injects USS custom properties (`--theme-accent`, `--theme-panel`, `--theme-text`, `--theme-radius`, `--theme-glow`, `--theme-border`) into any VisualElement root, and applies semantic class styling (`themed-panel`, `themed-accent-divider`, `themed-title`, `themed-subtitle`).
- Added `UIThemeDatabase` ScriptableObject that holds all 10 built-in `UIThemeDefinition` references, sorted by enum order, loadable via Resources or AssetDatabase.
- Expanded `UIThemeDefinition` to meet full roadmap spec: accent, panel, text, border, background, panelOpacity, cornerRadius, borderThickness, accentGlow, backgroundDim, animationSpeed, transitionCurve, customFont, fontAssetName, baseFontSize, description.
- Expanded `UIThemeManager` with:
  - Events `OnThemeChanged` and `OnDefinitionApplied` for reactive pipeline.
  - New persisted properties `AccentGlow` (0-1) and `AnimationSpeed` (0.2-3x) with PlayerPrefs backing.
  - `ApplyDefinition(UIThemeDefinition)` to apply a ScriptableObject directly.
  - `GetCurrentDefinition()`, `GetAllDefinitions()`, `DescriptionFor()`, `ResetToDefault()`.
  - Export code now includes glow and animation speed (backward compatible with 7-part codes).
  - Loads `UIThemeDatabase` at startup for fast lookup.
- Expanded `UIThemeOverride` to support full per-block overrides: `overrideTheme`, `themeOverride`, `overrideAccent`, `accentColor`, `iconStyleOverride`, `zoneLabel`, `tintStatusLights`, plus static helpers `ResolveTheme`, `ResolveAccent`, `ResolveIconStyle`, `Ensure`.
- New `CustomThemeEditorUI` dedicated full editor panel with live preview card, built-in theme selector, custom accent RGB + preset chips, shape editors, effects & motion editors, import/export/duplicate/reset row.
- Refactored `UITheme.Panel()` and `AccentDivider()` and `Title()/Subtitle()` to add themed- classes and inject USS variables via `SetProperty`.
- Refactored `SettingsUI.InterfaceTab` to full premium editor: description hint, live preview with dot + LIVE pill, custom accent toggle with RGB sliders + 11 preset color chips, opacity/radius sliders with live readout, glow + animation speed sliders, share row with Copy/Import/Duplicate/Reset, code display, production accent separate section, and premium editor explanatory hint referencing ThemedPanel + USS variables.

**Setup Wizard (Non-Destructive):**
- Step 3 now generates 10 enriched `UIThemeDefinition` assets with border/background/glow/dim/animation/curve/fontSize/description fields populated non-destructively (preserves user edits, only fills defaults when zero).
- Step 3 now creates/updates `Assets/VoxelEngineAssets/UI/UIThemeDatabase.asset` containing all 10 themes sorted by enum, with clear logging of created vs updated count.
- Step 17 `UIThemeOverride` components on Crusher/Assembler/ElectricFurnace prefabs continue to be verified non-destructively (accent preserved).

**Roadmap Status:**
- 4.6.0 UI theme system moved to **✅ COMPLETED** — all requirements met: 10 themes, colors/fonts/radius/opacity/glow/animation curves, ThemeDefinition ScriptableObjects, player switching in Settings → Interface, per-block overrides (ThemeOverride, AccentColorOverride, IconStyleOverride), custom theme editor with live preview and export/import, UIThemeManager loads ScriptableObjects and applies USS variables reactively, ThemedPanel base class for all panels, no reload required.

**Manual Unity Steps:**
1. In Unity, open Tools → Voxel Engine → Voxel Engine Setup.
2. Click **3. Build Main Menu Scene** — this regenerates the 10 enriched theme assets and creates/updates UIThemeDatabase non-destructively. Console will log `updated theme database with 10 themes`.
3. Verify `Assets/VoxelEngineAssets/UI/Themes/` contains 10 Theme_*.asset files and `Assets/VoxelEngineAssets/UI/UIThemeDatabase.asset` exists.
4. Click **17. Build Factory Foundations + HV Grid** to ensure factory machine prefabs still carry `UIThemeOverride` components.
5. Open MainMenu scene and Game scene — check Interface tab shows 10 themes, custom accent toggle, preset chips (11 colors), opacity/radius/glow/animation sliders, live preview card that updates without reload, Copy/Import/Reset buttons.
6. Test reactive theming: change theme in Interface tab → all open panels (inventory, machine UI, etc.) should update instantly via OnThemeChanged without scene reload.

---

### [5.37.0-dev] Theme Asset Generation & Machine Overrides

**Type:** MINOR — new save-compatible setup-authored theme assets and machine override wiring

**Added / Improved:**
- Step 3 now generates 10 `UIThemeDefinition` assets under `Assets/VoxelEngineAssets/UI/Themes`.
- Generated theme assets cover all roadmap built-in themes and are created non-destructively.
- Step 17 now adds optional `UIThemeOverride` components to generated Crusher, Assembler, and Electric Furnace prefabs.
- Crusher and Assembler UIs already resolve these overrides for per-block accent colors when enabled.
- This connects the setup workflow to the broader UI theme system without touching gameplay saves.

**Roadmap Continued — 4.6.0 UI Theme System:**
- Setup-authored theme assets and machine override wiring are now started.
- Next targets: USS variable application and richer custom theme editor polish.

---

### [5.36.0-dev] Advanced Theme Shape & Import Export

**Type:** MINOR — new save-compatible custom theme editor controls

**Added / Improved:**
- Interface settings now include advanced theme shape controls for Panel Opacity and Corner Radius.
- Panel opacity and corner radius persist locally through PlayerPrefs.
- Runtime panels now use the custom corner radius.
- Added `Copy Theme Code` for sharing current theme settings as a compact string.
- Added `Import Clipboard` to apply a copied theme code.
- Reset Interface Theme now resets built-in theme, custom accent, opacity, and radius.

**Roadmap Continued — 4.6.0 UI Theme System:**
- Advanced custom theme editing is now started with shape controls and import/export.
- Next targets: USS variable application and deeper per-block overrides.

---

### [5.35.0-dev] Custom Theme Accent Editor

**Type:** MINOR — new save-compatible custom interface theme control

**Added / Improved:**
- Added persistent Custom Accent override to `UIThemeManager`.
- Interface settings now include a Custom Accent toggle.
- When enabled, players can edit accent RGB sliders directly in the Interface tab.
- Custom accent is persisted through PlayerPrefs.
- Reset Interface Theme now resets both built-in theme and custom accent override.

**Roadmap Continued — 4.6.0 UI Theme System:**
- Custom theme editor work is now started with runtime RGB accent editing.
- Next targets: USS variable application and advanced custom theme editing.

---

### [5.34.0-dev] Runtime Theme Application & Per-Block Overrides

**Type:** MINOR — new save-compatible UI theme application and override foundation

**Added / Improved:**
- Global UI theme selection now affects standard panel background, panel border accent, default accent dividers, and title text tone at runtime.
- Added Interface theme preview card and reset button.
- Added `UIThemeOverride` MonoBehaviour for optional per-block UI accent overrides.
- Crusher and Assembler panels now respect `UIThemeOverride` accents when present.
- This advances the UI theme pipeline from selection-only toward actual runtime visual application.

**Roadmap Continued — 4.6.0 UI Theme System:**
- Runtime theme application and per-block override foundations are now started.
- Next targets: USS variable application and custom theme editor.

---

### [5.33.0-dev] UI Theme System Starter

**Type:** MINOR — new save-compatible interface/theme settings foundation

**Added:**
- Added `UIThemeDefinition` ScriptableObject type for authored theme assets.
- Added `BuiltInUITheme` with ten roadmap themes: Industrial Steel, Midnight Operator, Hazard Amber, Arctic Frost, Bio-Luminescent, Military Olive, Neon Cyber, Corporate Clean, Rust Belt, and Void Black.
- Added `UIThemeManager` with persistent PlayerPrefs-backed theme selection and accent lookup.
- Added an `Interface` tab to both Main Menu settings and in-game Pause settings.
- Interface tab lets players choose the global UI theme and production panel accent override.
- Existing production Recipe Browser and Production Statistics panels continue using production accent overrides.

**Roadmap Continued — 4.6.0 UI Theme System:**
- Broader UI theme system work is now started.
- Next targets: USS variable application, per-block UI overrides, and custom theme editor.

---

### [5.32.0-dev] Recipe Browser Craftability Filters

**Type:** MINOR — new save-compatible recipe browser filtering/sorting controls

**Added:**
- Added `Have Mats` filter to Recipe Browser.
- Have Mats filters visible recipes down to methods craftable from the player's current carried inventory.
- Added sort toggle between `Sort: Name` and `Sort: Methods`.
- Recipe Browser result counts continue showing visible output items and recipe methods after filters are applied.
- Craftability and sort preferences persist locally through PlayerPrefs.

**Roadmap Continued — 4.6.0 Production Lines:**
- Recipe browser now supports craftability filtering and sorting.
- Next target: final planner UX polish and transition toward broader UI theme system work.

---

### [5.31.0-dev] Production Stats Actions & Planner Polish

**Type:** MINOR — new save-compatible production statistics controls and planner navigation

**Added / Improved:**
- Production Statistics now has `Copy Stats`, exporting current production/consumption/net rates as text.
- Production Statistics now has `Reset`, clearing the current session's production tracker.
- Bottleneck hint visibility and hidden hint items now persist locally through PlayerPrefs.
- Bottleneck/surplus hint rows now include `View`, opening Recipe Browser focused on that item.
- Material Summary keeps Missing Only and CSV export from the previous planner export pass.

**Roadmap Continued — 4.6.0 Production Lines:**
- Final planner UX polish continues with cross-panel navigation and stats export/reset actions.
- Next target: transition toward broader UI theme system work.

---

### [5.30.1-dev] Planner Export Compile Fix

**Type:** PATCH — UI compile fix

**Fixed:**
- Fixed CSV quote escaping in `RecipeBrowserUI.cs`.
- Fixed newline escaping in `RecipePinHud.cs`.
- Planner export and pinned recipe copy features now compile correctly.

---

### [5.30.0-dev] Planner Export & Pin UX Polish

**Type:** MINOR — new save-compatible planner export and pin HUD quality-of-life controls

**Added / Improved:**
- Material Summary now has a Missing Only / Show All toggle.
- Missing-only material filtering persists locally with the other planner settings.
- Added `Copy CSV`, exporting material summary as CSV with item, required, have, missing, and type columns.
- Pinned Recipe HUD now has `Copy Pins` to copy all pinned recipe cards as readable text.
- Pinned Recipe HUD now has `Clear` to remove all pinned recipes at once and persist the cleared state.

**Roadmap Continued — 4.6.0 Production Lines:**
- Planner export and pin UX polish now includes CSV and pinned-list controls.
- Next target: final planner UX polish and transition toward broader UI theme system work.

---

### [5.29.0-dev] Production Shopping List Export

**Type:** MINOR — new save-compatible production planner export aid

**Added / Improved:**
- Material Summary now shows an Inventory Coverage line summarizing missing item types and total missing units.
- Added `Copy Missing`, exporting only currently missing materials as a shopping list.
- `Copy Missing` respects current batch count, method preference, depth, and inventory coverage.
- `Copy Plan` continues exporting the full plan with Have/Missing counts.

**Roadmap Continued — 4.6.0 Production Lines:**
- Graph export polish now includes missing-material shopping lists.
- Next target: planner UX polish and graph export polish.

---

### [5.28.0-dev] Inventory-Aware Production Planner

**Type:** MINOR — new save-compatible production planning UX

**Added / Improved:**
- Material Summary now compares required materials against the player's current inventory.
- Each material line shows Have and Missing counts.
- Copy Plan now includes Have and Missing counts for every material.
- Recipe Browser now receives the active player inventory so planner coverage can update live.

**Roadmap Continued — 4.6.0 Production Lines:**
- Production planner UX now includes inventory-aware material coverage.
- Next target: graph export polish and richer planning controls.

---

### [5.27.0-dev] Production Planner UX Polish

**Type:** MINOR — new save-compatible planner UX refinements

**Fixed / Improved:**
- Added a `Reset` control for target/minute, returning the planner target to 60/min.
- Moved `Used By` below Dependency Chain and Material Summary so planning information appears before downstream usage.
- Recipe Browser search now reports focus to GameUI, preventing inventory/research/hotkey UI closures while typing into the search field.

**Roadmap Continued — 4.6.0 Production Lines:**
- Production planner UX polish continues with target reset, better section order, and safer search focus handling.
- Next target: richer graph controls and persistent planner refinements.

---

### [5.26.0-dev] Recipe Method Comparison

**Type:** MINOR — new save-compatible recipe planner comparison UI

**Added:**
- Recipe Browser now shows a Method Comparison section when an item has multiple production methods.
- Method Comparison displays per-method output/minute and estimated machine count for the current target/minute.
- Added Prefer buttons for supported alternate paths, allowing players to steer Dependency Chain and Material Summary toward AI-assembler or Assembler Station methods.
- Preferred method selection persists through the existing planner preference system.

**Roadmap Continued — 4.6.0 Production Lines:**
- Production planner UX polish now includes method comparison.
- Next target: planner UX polish and graph export polish.

---

### [5.25.1-dev] Production Planner Compile Fix

**Type:** PATCH — UI compile fix

**Fixed:**
- Fixed `CS0841` / `CS0165` in `RecipeBrowserUI.cs` by declaring the target-per-minute label before local refresh callbacks use it.
- Production planner controls compile again while keeping the target/minute live update behavior.

---

### [5.25.0-dev] Production Machine Planner

**Type:** MINOR — new save-compatible production-rate planning aid

**Added:**
- Material Summary now includes a target-per-minute planner.
- Target/minute can be adjusted with `−` and `+` controls.
- Recipe Browser estimates how many machines are needed for the selected recipe path.
- Machine estimates respect current method preference, recipe output count, and process time.
- Target/minute is included in copied production plans.
- Target/minute persists locally through PlayerPrefs.

**Roadmap Continued — 4.6.0 Production Lines:**
- Production planner UX polish is now started with machine-count estimates.
- Next target: richer graph controls and planner persistence polish.

---

### [5.24.0-dev] Recipe Graph Export Polish

**Type:** MINOR — new save-compatible recipe graph export controls

**Added:**
- Recipe Browser details now has `Copy Methods`, exporting every known way to make the selected item as readable text.
- Dependency Chain now has `Copy Chain`, exporting the currently selected dependency path as readable text.
- Exported chain text respects current depth, raw-input visibility, and method preference.
- Existing `Copy Plan` remains available for material shopping lists.

**Roadmap Continued — 4.6.0 Production Lines:**
- Graph export polish is now started.
- Next target: production planner UX polish.

---

### [5.23.0-dev] Recipe Browser Method Filters

**Type:** MINOR — new save-compatible recipe graph filtering controls

**Added:**
- Recipe Browser now has quick method filters: All, Hand, Station, AI, and Smelt.
- Filter selection persists locally through PlayerPrefs.
- Recipe Browser shows result counts for visible items and recipe methods.
- Clear search control appears when a search term is active.

**Roadmap Continued — 4.6.0 Production Lines:**
- Richer graph controls now include method filtering.
- Next target: production planner UX and graph export polish.

---

### [5.22.0-dev] Persistent Recipe Planner Controls

**Type:** MINOR — new save-compatible local planner preference persistence

**Added / Improved:**
- Recipe Browser now persists selected recipe target locally.
- Dependency Chain depth persists locally.
- Show Raw / Hide Raw preference persists locally.
- Chain method preference persists locally: Auto, Prefer AI, or Prefer Station.
- Material Summary batch count persists locally.
- All persistence uses PlayerPrefs and does not touch save schema.

**Roadmap Continued — 4.6.0 Production Lines:**
- Planner persistence is now started.
- Next target: richer graph controls and production planner UX.

---

### [5.21.0-dev] Production Plan Controls

**Type:** MINOR — new save-compatible production planning controls

**Added / Improved:**
- Material Summary now supports batch planning with `−`, batch count reset, and `+` controls.
- Material totals update locally without rebuilding the whole Recipe Browser panel.
- Added `Copy Plan` to copy a plain-text production plan to the clipboard.
- Copied plans include selected output, batch count, chain preference, depth, and grouped material requirements.

**Roadmap Continued — 4.6.0 Production Lines:**
- Graph export / production planning controls are now started.
- Next target: richer graph controls and planner persistence.

---

### [5.20.0-dev] Recipe Material Summary

**Type:** MINOR — new save-compatible recipe planning summary

**Added:**
- Recipe Browser details now include a Material Summary section.
- Material Summary recursively follows the selected dependency path and estimates base/raw input requirements for one selected output batch.
- Summary respects the current dependency chain depth and recipe method preference.
- Materials are grouped by item with counts and raw/item tags.

**Roadmap Continued — 4.6.0 Production Lines:**
- Richer recipe graph controls continue with material summary planning.
- Next target: graph export / production planning controls.

---

### [5.19.0-dev] Persistent Production UI Settings

**Type:** MINOR — persistent local UI preference support

**Added / Improved:**
- Production panel theme selection now persists locally through PlayerPrefs.
- Recipe pins now persist locally through PlayerPrefs.
- Pinned recipes are restored automatically when the HUD mounts.
- Pin removal updates persisted data immediately.

**Roadmap Continued — 4.6.0 Production Lines:**
- Persistent theme and pin settings are now started.
- Next target: richer recipe graph controls.

---

### [5.18.0-dev] Recipe Pin HUD & Graph Preferences

**Type:** MINOR — new save-compatible recipe planning HUD and graph preference controls

**Fixed / Improved:**
- Dependency Chain controls now update button text and tree content locally when raw inputs/depth/method preference changes.
- Smelting recipe display names now show clean labels such as `Smelting: Copper` instead of asset names like `Smelt_Copper`.
- Added method preference cycling for dependency chains: Auto, Prefer AI, and Prefer Station. This lets players choose between AI-assembler and Assembler Station paths when both exist.

**Added:**
- Added Recipe Pin HUD on the center-right side of the screen.
- Recipes can be pinned from Recipe Browser details.
- Up to 4 pinned recipes are shown at once. Pinning a fifth removes the oldest pin.
- Pinned recipe cards show output, method, and required inputs.

**Roadmap Continued — 4.6.0 Production Lines:**
- Recipe planning HUD is now started.
- Next target: persistent theme/pin settings and richer graph controls.

---

### [5.17.0-dev] Production Panel Theme Overrides

**Type:** MINOR — new save-compatible UI theme override starter

**Fixed / Improved:**
- Dependency Chain controls now refresh only the chain content, preserving the current scroll position when toggling raw inputs or changing chain depth.
- Recipe Browser icon no longer uses a glyph that can render as a square on unsupported fonts.

**Added:**
- Added a lightweight production panel theme override state.
- Production Statistics and Recipe Browser now include a `Theme` button cycling Steel, Amber, Cyan, and Violet accent styles.
- This starts the roadmap theme-override work without touching save data.

**Roadmap Continued — 4.6.0 Production Lines:**
- Theme overrides for production panels are now started.
- Next target: richer graph controls and persistent theme settings.

---

### [5.16.0-dev] Polished Recipe Chain Graph Controls

**Type:** MINOR — new save-compatible recipe graph control/polish

**Changed:**
- Dependency Chain was rebuilt from plain text into stacked visual cards with colored side bars, method badges, item tint dots, and per-node metadata.
- Added chain controls for depth adjustment and raw-input visibility.
- Chain nodes can be clicked to focus that recipe target.
- Raw inputs now display as compact RAW cards instead of plain indented text.
- Continued use of `AI-assembler` naming for factory machine assembling recipes.

**Roadmap Continued — 4.6.0 Production Lines:**
- Richer recipe graph controls are now started.
- Next target: theme overrides for production panels.

---

### [5.15.0-dev] Recipe Dependency Chain View

**Type:** MINOR — new save-compatible recipe planning visualization

**Added / Improved:**
- Added a recursive Dependency Chain section to the Recipe Browser details panel.
- The chain shows the selected item, the preferred immediate recipe, and nested craftable inputs down to several levels.
- Cycle protection prevents repeated recipes from endlessly expanding.
- Machine assembling recipes now display as `AI-assembler` as requested.

**Roadmap Continued — 4.6.0 Production Lines:**
- Deeper recipe chain visualization is now started.
- Next target: theme overrides and richer graph controls.

---

### [5.14.3-dev] Recipe Browser Grouping & Labels

**Type:** PATCH — recipe browser grouping/readability fix

**Fixed / Improved:**
- Recipe Browser now groups production targets by player-facing item name, so duplicate item assets with different internal ids but the same visible item appear under one row.
- Copper LV Wire and similar duplicated setup-era items now appear as one production target with all methods under Made By.
- Station-tier recipes now show `Assembler Station` instead of `Assembler` to avoid confusion with the factory Assembler machines.
- Machine assembling recipes were initially separated from station recipes; later releases renamed this label to `AI-assembler`.
- Recipe de-duplication keys now use the same player-facing item grouping for inputs.

---

### [5.14.2-dev] Recipe Browser Polish & GameUI Panel Guard

**Type:** PATCH — UI polish and null-panel safety fix

**Fixed / Improved:**
- Recipe Browser left list now groups by output item, so duplicated alternate recipes no longer appear as repeated selectable rows.
- `None:` recipe labels are now shown as `Hand Crafting:` in Made By sections.
- Recipe Browser entries are de-duplicated by kind/name/output/input signature.
- Scrollbar thumbs now stay inside the scrollbar track.
- GameUI now guards against a temporarily missing UI Toolkit panel, fixing the `RuntimePanelUtils.ScreenToPanel` NullReference when selecting/clicking the GameUI object in the Hierarchy.

---

### [5.14.1-dev] Recipe Browser Interaction Fix

**Type:** PATCH — UI interaction/focus fix

**Fixed:**
- Recipe Browser search no longer rebuilds the entire inventory UI on each typed character, so the search field keeps focus while typing.
- Selecting a recipe now updates the details panel immediately.
- Recipe list selection highlight refreshes locally without closing/reopening the browser.
- Clicking craftable inputs inside the details panel now jumps to that input's recipe without closing the UI.

---

### [5.14.0-dev] Recipe Browser Dependency View & Hideable Bottleneck Hints

**Type:** MINOR — new save-compatible production planning UI

**Added:**
- Added Recipe Browser panel accessible from the inventory panel.
- Recipe Browser lists crafting, smelting, and machine recipes from the validated recipe graph.
- Selecting a recipe output shows Made By, Used By, and Immediate Inputs sections.
- Craftable input rows can be clicked to jump to that input's recipe.
- Added search support for recipe/output names and recipe kinds.

**Improved:**
- Bottleneck Hints can now be hidden globally for the current session.
- Individual item bottleneck/surplus hints can be hidden.
- Hidden item hints can be restored with Unhide All Items.
- Opening Recipe Browser and Production Statistics is mutually exclusive so panels do not overlap.

**Roadmap Continued — 4.6.0 Production Lines:**
- Recipe Browser dependency view is now started.
- Next target: deeper multi-step chain visualization and theme-overridden production panels.

---

### [5.13.0-dev] Production Bottleneck Hints

**Type:** MINOR — new save-compatible production-line insight UI

**Added:**
- Production Statistics now includes a Bottleneck Hints card.
- Items consumed faster than they are produced are listed as shortages with the extra production per minute needed.
- Items being produced with no recent consumer are listed as idle surplus.
- Stable production displays a green “Production Stable” message.

**Roadmap Continued — 4.6.0 Production Lines:**
- Production-line UI remains **WORKING ON** with bottleneck hints now started.
- Next target: Recipe Browser dependency view.

---

### [5.12.8-dev] Wire Cancel, Connector Snapping & Generator Battery Pause

**Type:** PATCH — wire UX, connector placement, and generator fuel economy

**Fixed / Improved:**
- Right-clicking while holding a manual LV/HV wire now cancels the wire placement if no connector/relay/station is targeted.
- Manual wire links are now injected into power topology regardless of distance, so connector-to-connector wire spans actually join the same power network.
- Compact connectors can auto-tap a nearby generator/consumer while keeping one manual wire span.
- Compact wire connectors snap to nearby electrical objects so placement aligns to the object face/grid instead of looking offset.
- Coal Generator UI now shows a battery reserve icon/line.
- Coal Generator pauses fuel burn when connected batteries are full and no power is being used, then resumes when demand returns.
- LV connector/grid capacity descriptions show `100 kW`; HV connector/grid capacity descriptions show `Infinite`.

---

### [5.12.7-dev] Manual Wire Attachment & LV/HV Relay Split

**Type:** PATCH — wire interaction and voltage display fixes

**Fixed / Improved:**
- Manual LV/HV wire tool now raycasts all objects as a fallback when the station layer mask misses compact connectors/relays.
- Manual wire linking accepts both left-click and right-click while holding a wire item.
- Player interaction now yields right-click to the wire tool while holding LV/HV wire, preventing connector/relay clicks from opening the voltage grid panel during wire placement.
- Added separate LV Power Relay and HV Power Relay generation in Step 17.
- LV grid max capacity now reports `100 kW` instead of a huge placeholder number.
- HV grid max capacity now reports `Infinite`.
- Compact connector/relay runtime node split prevents compact devices from using energy-pipe visuals.

---

### [5.12.6-dev] Compact Connector Visual Fix

**Type:** PATCH — compact power connector visual/runtime fix

**Fixed:**
- Compact LV/HV connectors and Power Relay no longer use `PowerCable` as their runtime node.
- Added `CompactPowerNode`, a cable-kind topology node without energy-pipe visuals.
- Step 17 removes accidental `PowerCable` components from compact connector/relay prefabs and installs `CompactPowerNode` instead.
- This prevents LV Wire Connector from visually/physically behaving like an Energy Pipe after placement.

---

### [5.12.5-dev] Debug Power Spawn & Manual Wire Clarity

**Type:** PATCH — debug-spawner filtering and setup naming clarity

**Fixed / Improved:**
- Debug Spawner's “Spawn All POWER Blocks” now spawns only placeable `BlockItem` power blocks, not manual wire resource items. This prevents Copper LV Wire from being confused with LV Wire Connector during testing.
- LV wire resources are moved to the `Wire` category and receive descriptions explaining they are manual wire-link items, not placeable energy pipe blocks.
- Step 17 continues repairing generated block item identity every run so connector/crusher/assembler items do not retain default ItemDefinition names.

---

### [5.12.4-dev] Step 17 Compile Fix

**Type:** PATCH — editor compile fix

**Fixed:**
- Fixed `CS0103` in `VoxelEngineSetupWindow.cs` caused by using Step 17's local `repairedLinkCount` variable inside an older power setup helper scope.
- Legacy energy-pipe migration still repairs display names, descriptions, prefab links, and stackable placement settings; it simply no longer increments an out-of-scope counter.

---

### [5.12.3-dev] Energy Pipe Placement & UI Cleanup Repair

**Type:** PATCH — setup identity repair, placement snapping, and UI cleanup

**Fixed / Improved:**
- Step 17 now repairs generated block item identity fields every run so failed setup-created items no longer display default names like Iron Ore.
- Legacy and new Energy Pipe block items are marked stackable/thin so adjacent pipe placement validates and pipes can snap next to each other.
- Legacy electrical pipe block assets are migrated to Energy Pipe display names and reconnected to the generated Energy Pipe prefabs.
- Coal Generator inline port-configuration UI was removed; it now uses the same clickable Item Ports modal workflow style.
- Existing placed legacy coal generators can receive `CoalGeneratorFuel` at interaction time if the prefab was old.
- Energy pipe visual arms now use local-space targets so rotated pipe networks and wall-mounted relays draw in the correct direction.

---

### [5.12.2-dev] Prefab Script Compatibility & Energy Pipe Rename

**Type:** PATCH — setup resilience, coal-generator repair, and power visual fixes

**Fixed / Improved:**
- Restored `MachineVisualAnimators` as a compatibility MonoBehaviour so old Funnel prefabs can load and be repaired instead of blocking prefab saves.
- Step 17 now repairs the legacy Coal Generator prefab by adding `CoalGeneratorFuel` and reconnecting the legacy Coal Generator block item to the repaired prefab.
- Power cable visuals now build from local-space neighbour positions, fixing arms/wires pointing in the wrong world direction on rotated or wall-mounted placement.
- Renamed future generated electrical pipe content to Energy Pipe naming:
  - Copper Energy Pipe
  - Iron Energy Pipe
  - Gold Energy Pipe
  - Superconductor Energy Pipe
- Updated energy-pipe recipes and research lookup keys to the new names.

**Manual Validation Required:**
- Let Unity recompile, run Step 17 without deleting folders, then verify Funnel saves, Coal Generator UI opens, and energy pipe visuals connect in the correct direction.

---

### [5.12.1-dev] Step 17 Missing Script Repair

**Type:** PATCH — Unity prefab serialization and setup repair fixes

**Fixed / Improved:**
- Split machine animator MonoBehaviours into one class per file so Unity can serialize `AssemblerMotionAnimator`, `CrusherMotionAnimator`, and `FunnelMotionAnimator` reliably.
- Split compact voltage station MonoBehaviours into matching files/classes for `LvWireConnectorStation`, `HvWireConnectorStation`, and `PowerRelayStation`.
- Step 17 now strips missing script references from loaded prefabs before saving, then re-adds required components. This prevents Unity's “cannot save prefab with missing script” errors.
- Step 17 now adds `CoalGeneratorFuel` to the Coal Generator prefab so right-clicking opens the coal generator fuel UI.
- Step 17 connector/relay prefabs now use the corrected component class names.

**Manual Validation Required:**
- Run Step 17 again without deleting folders. Existing missing scripts should be removed automatically and prefabs should save normally.

---

### [5.12.0-dev] Machine UI Recovery & Compact Power Relays

**Type:** MINOR — new save-compatible power connector blocks plus UI/interaction fixes

**Validated:**
- Thomas confirmed the recipe graph validator now reports `0` errors and `0` warnings.

**Fixed / Improved:**
- Crusher and Assembler interaction now opens their machine UI so recipes can be selected.
- Production Statistics automatically closes when crafting opens or when another right-side UI/machine panel is opened.
- Coal Generator UI is no longer covered by a stale Production Statistics panel.
- Electric Furnace no longer shows the old inline power-port UI; it keeps the clickable Item Ports modal workflow.
- Electric Furnace power consumers can now draw from nearby energy pipes without being blocked by item-port face settings.
- Electric Furnace hides old world port indicator squares to avoid duplicate port presentation.

**Added:**
- Added compact `LV Wire Connector` block: wall/foundation mount, max 1 connection.
- Added compact `HV Wire Connector` block: wall/foundation mount, max 1 connection.
- Added compact `Power Relay` block: wall/foundation mount, relays power only, max 8 connections.
- Added max-auto-connection support to power nodes and topology rebuilds.
- Step 17 generates the new connector/relay prefabs, block items, descriptions, and recipes non-destructively.

---

### [5.11.0-dev] Live Production Statistics Panel

**Type:** MINOR — new save-compatible production visibility UI

**Added:**
- Added `ProductionStatsTracker`, a save-free runtime tracker for produced/consumed item counts.
- Crusher, Assembler, and Electric Furnace now report consumed inputs and produced outputs when batches complete.
- Added a live Production Statistics panel accessible from the inventory panel.
- Panel shows per-minute produced/consumed/net rates plus session totals for tracked items.

**Validation:**
- Thomas confirmed the recipe graph is down to `0` errors after the repair workflow.

**Roadmap Continued — 4.6.0 Production Lines:**
- Recipe graph validation moved to **COMPLETED**.
- Production-line UI remains **WORKING ON** with live statistics now started.
- Next targets: bottleneck hints and Recipe Browser dependency view.

---

### [5.10.2-dev] Final Recipe Error Repair & Duplicate Warning Cleanup

**Type:** PATCH — editor validation/repair polish

**Fixed / Improved:**
- Added targeted repair for `Recipe_GThrustLarge.asset` by reconnecting its missing output to the existing large thruster item.
- Added Crusher stone byproduct repair so Sand is connected when available.
- Validator now downgrades expected progression duplicates to Info instead of Warnings when duplicates are clearly intentional root/domain recipe mirrors or hand-craft/machine alternatives.

**Validation Result From Previous Pass:**
- Recipe graph errors reduced from `74` to `1` after `5.10.1-dev` repair.
- Remaining target for this patch: `Recipe_GThrustLarge.asset` missing output.

---

### [5.10.1-dev] Recipe Graph Missing-Link Repair

**Type:** PATCH — editor repair tooling for invalid recipe references

**Added / Improved:**
- Added `Tools > Voxel Engine > Repair Missing Recipe Links`.
- Added a `Repair Missing Links` button directly inside the Recipe Graph Validator.
- Repair pass is non-destructive: it fills missing recipe outputs/inputs, copies valid duplicate recipe links where available, and creates missing base ore resources required by factory recipes.
- Added targeted repair coverage for early crafting recipes, tiered build-token recipes, science packs, fluid duplicate recipes, maritime recipes, nuclear control rods, smelting recipes, and Crusher ore inputs.
- Validator automatically rescans after running the repair button.

**Manual Validation Required:**
- Run the repair once, then run `Scan Project` again and send the new report if any errors remain.

---

### [5.10.0-dev] Production Lines Kickoff & Recipe Graph Validator

**Type:** MINOR — new save-compatible editor tooling for production-line validation

**Added:**
- Added `Tools > Voxel Engine > Recipe Graph Validator`.
- Validator scans crafting recipes, smelting recipes, and machine recipes without modifying assets.
- Reports missing outputs, missing inputs, invalid counts, zero/negative processing times, suspicious byproduct settings, duplicate outputs, input-only base resources, and potential dependency cycles.
- Copyable markdown report makes it easy to paste validation results into planning notes or issues.

**Roadmap Continued — 4.6.0 Production Lines & UI Revolution:**
- Marked 4.6.0 as **WORKING ON**.
- Marked Recipe Graph Validation as **COMPLETED**.
- Next recommended implementation target is the Production Statistics Panel, followed by the Recipe Browser dependency view.

**Roadmap Status:**
- Current version and roadmap version synchronized to `5.10.0-dev`.
- Factory Foundations remain active for final polish/validation, but new feature work has moved into Production Lines.

---

### [5.9.2-dev] Crisp Responsive UI & Conveyor Ramp Corner Fix

**Type:** PATCH — responsive UI/text-quality fixes and conveyor topology polish

**Fixed / Improved:**
- Runtime UI documents force crisp constant-pixel scaling to avoid low-quality scaled text.
- Shared PanelSettings setup now authors constant-pixel UI scaling.
- Center crafting, inventory, machine, and large terminal panels use safer responsive widths and margins.
- Conveyor shape wheel hover updates selected shape immediately so the ghost preview changes while the wheel is open.
- Straight conveyors detect ramp/vertical downstream neighbours by socket position, improving corner/turn inference beside slope pieces.

---

### [5.9.1-dev] Responsive Build Wheels & Conveyor Ghost Preview Fix

**Type:** PATCH — wheel interaction, ghost-preview, and responsive-panel polish

**Fixed / Improved:**
- Conveyor ghosts remain visible while holding the build wheel key and can rebuild disabled preview meshes.
- Releasing the build wheel key over a conveyor shape selects it.
- Hammer wheel supports hold-release selection.
- Wheel labels clip hover backgrounds so rings no longer spill out of button bounds.
- Machine panels scroll internally and slot rows wrap to avoid horizontal overflow.

---

### [5.9.0-dev] Factory Placement, Machine UI & Visual Polish

**Type:** MINOR — save-compatible machine UI, visual animation, and placement fixes

**Added / Improved:**
- Added Crusher and Assembler Mk.1–Mk.3 UI panels with recipe selection, power state, progress, enabled toggle, slots, and item-port integration.
- Added runtime recipe selection support for Crusher and Assembler.
- Added new generated visual animation components for Funnel, Crusher, and Assembler machines.
- Upgraded Funnel, Crusher, and Assembler generated prefab visuals through the non-destructive Step 17 workflow.
- Crusher is larger, top-fed, and includes falling/crushing item animation.
- Assemblers are larger and include gantry/press/work-piece animation.

**Fixed:**
- Build ghosts no longer participate in logistics, preventing conveyor items from being deleted into ghost consumers.
- Stairs can chain onto other stairs.

---

### [5.8.0-dev] Seamless Foundations, Bidirectional Stairs & Factory State Persistence

**Type:** MINOR — new save-compatible factory runtime persistence plus construction snapping/visual fixes

**Validated:**
- Thomas confirmed the `3.75 m` Size-V4 construction scale, exact Foundation neighbor spacing, and player-away Door swing work correctly in Unity.

**Fixed / Improved:**
- Foundation deck planks now overlap subtly and cover the complete perimeter structure, removing top seams and visible lower-frame overflow.
- Foundation top-center no longer captures Stairs; four directional perimeter sockets choose the intended edge.
- Aiming at a Foundation or Floor side with Stairs anchors the upper tread at that edge and extends the staircase down one complete storey.
- Aiming at the horizontal top perimeter places the opposite upward-rising orientation.
- Doorways now expose a dedicated exterior threshold Stair anchor so a descending staircase can lead directly up to the opening.
- Stair rotation keeps the anchored high/low tread fixed while rotating around the selected socket.
- Step 5 migrates recognized Setup-generated prefabs to `Generated_SizeV5`, while preserving custom geometry, materials, recipes, health, costs, and balance tuning.

**Roadmap Continued — Factory Persistence:**
- Conveyor item identity, packet count, path progress, and lateral visual position now save and restore.
- Chute item identity, packet count, and slide progress now save and restore.
- Crusher and Assembler input, output, and upgrade buffers now save and restore.
- Crusher and Assembler active recipe, process progress, and player-enabled state now save and restore.
- All fields are additive; legacy saves load with empty transport state and default machine behavior.

**Roadmap Status:**
- Size-V4 scale, Foundation spacing, and player-away Doors: **✅ COMPLETED**.
- Seamless Foundation deck and bidirectional Stair snapping: **🛠️ WORKING ON** — Unity validation pending.
- Factory persistence: **🛠️ WORKING ON** — Unity save/reload validation pending.

---

### [5.7.2-dev] 25% Larger Construction, Exact Foundation Spacing & Player-Away Doors

**Type:** PATCH — construction scale, snapping-distance, and directional Door behavior fixes

**Fixed / Improved:**
- Increased every Setup-generated tiered construction piece by exactly 25%, moving the standard module from `3.0 m` to `3.75 m` while preserving all recipe, tier, health, and material balance values.
- Corrected Foundation neighbor sockets from a half-module offset to a full `3.75 m` center-to-center offset, preventing adjacent Foundations from occupying the same volume.
- Updated same-plane Wall, Doorway, Window, Half Wall, and Floor neighbor sockets to use complete module spacing instead of overlapping roots.
- Kept Wall, Doorway, Window, and Half Wall placement on the Foundation's true upper perimeter at half-module offsets.
- Increased the tiered construction grid to `3.75 m`, socket search radius to `3.25 m`, and Foundation terrain offset proportionally.
- Doors now evaluate which side the interacting player is standing on each time they open and swing away from that player; reopening from the opposite side reverses the swing.
- Step 5 migrates recognized Setup-generated Size-V3 prefabs to the Size-V4 geometry marker while preserving imported/custom prefab geometry, authored materials, recipes, costs, and other balance edits.

**Roadmap Status:**
- Size-V4 construction, exact Foundation spacing, and player-away Door swing: **WORKING ON** — awaiting Unity validation.

---

### [5.7.1-dev] Size-V3 Edge Snapping, Door Fit & Wheel/UI Refinement

**Type:** PATCH — construction scale, socket orientation, door fit, wheel interaction, and UI authoring fixes

**Fixed / Improved:**
- Construction module increased to `3.0 m` and rebuilt as Size-V3 player-scale geometry.
- Added dedicated Foundation top-edge sockets for Wall, Doorway, Window, and Half Wall placement; the center Top socket no longer accepts wall-like pieces.
- Socket snapping now preserves the exact host/socket quaternion instead of reconstructing world Euler yaw, eliminating slight rotational drift on planetary surfaces.
- Lateral sockets use true ±1.50 m edges at root height; wall-like and floor-like neighbors remain level.
- Door enlarged to closely fill the player-sized Doorway opening while preserving a small movement gap.
- Doorway remains empty; Door remains a separate family on the adjacent Hammer page and opens toward local interior space.
- Hammer wheel labels/icons moved farther inward and full-wheel pointer coordinates are used for reliable inner-edge selection.
- Door is positioned directly beside Doorway on the first Hammer page; remaining families continue on the scrollable second page.
- Conveyor wheel labels narrowed and inset from the ring edge.
- Step 3 explicitly repairs shared PanelSettings scaling/atlas quality, assigns the shared asset to every Game-scene UIDocument, and runtime systems continue preserving Inspector edits.

**Roadmap Status:**
- Size-V3 snapping, orientation, Door/Doorway fit, wheel selection, and UI fit: **WORKING ON** — awaiting Unity validation.

---

### [5.7.0-dev] Player-Scale Construction, Separate Doors & UI Authoring Repair

**Type:** MINOR — new Door building family plus construction-scale and shared UI authoring changes

**Added / Changed:**
- Appended `Door` as the tenth Build Family without reordering serialized family values.
- Doorway remains an empty structural opening and gains a center socket dedicated to the separate Door family.
- Door prefabs include a hinged panel, handle, collider, smooth animation, four material tiers, token, recipe, and Hammer-wheel entry.
- Standard Hammer construction grid increased from `1 m` to `2.5 m`; socket search radius increased to `2.2 m`.
- Foundation expanded to a 2.5 m raised deck with larger planks, perimeter beams, legs, and braces.
- Walls, Doorways, Windows, Floors, Stairs, Roofs, Pillars, and Half Walls rebuilt at player-appropriate dimensions.
- Lateral sockets moved to ±1.25 m outer edges at the host root height, fixing sockets embedded inside blocks and vertical drift between neighboring pieces.
- Step 5 detects known setup-generated prefabs, replaces their legacy geometry with Size-V3 geometry, and leaves imported/custom prefabs untouched.
- Hammer wheel icons/labels moved inward; conveyor-wheel labels narrowed and inset from the outer rim.
- Step 3 now explicitly authors shared MenuPanelSettings quality/fit values, including Match Width/Height and 256 px atlas subtextures.
- Runtime controllers continue respecting developer-authored PanelSettings without overwriting them.

**Fixed:**
- Doorway segment selection benefits from full-wedge Hammer hit testing.
- Side-by-side Foundations, Walls, Doorways, Windows, Floors, Pillars, and Half Walls snap on the same level.
- A Door can be selected independently, snapped into a Doorway, and toggled with RMB after placement.

**Roadmap Status:**
- Player-scale Size-V3 construction, separate Door family, edge sockets, wheel spacing, and Step 3 UI authoring: **WORKING ON** — awaiting Unity validation.

---

### [5.6.0-dev] Finite Quarry, Premium Construction & UI Fit Repair

**Type:** MINOR — new quarry progression constraints plus construction/UI system upgrades

**Added / Changed:**
- Removed new-world generation of the unbreakable bedrock floor and planetary bedrock core; legacy material value `20` is retained only for save compatibility and treated as Stone.
- Quarry now uses a configurable finite depth (`64` layers by default, clamped to `8–256`) and completes at that limit.
- Advanced Quarrying moved to Tier 5 with higher research cost, longer research time, and substantially more expensive steel/electronics recipe requirements.
- Hammer wheel registry now resolves from the active `BuildSystemV2`, fixing false `Free` labels when the registry asset is outside Resources.
- Hammer wheel hit testing moved to the complete wheel container so the full wedge responds, not only its icon/label.
- Global UI blocking now prevents Hammer-wheel scrolling from changing the player hotbar.
- Hammer segment labels and costs use clipped no-wrap bounds so text stays inside the ring.
- Tiered socket discovery now scans sockets on nearby placed hosts, fixing same-level Foundation-to-Foundation snapping.
- Wooden Foundation rebuilt as a low deck with five planks, perimeter beams, four legs, and front braces.
- Doorway prefabs now receive an animated hinged door and handle; RMB toggles the door.
- Step 5 creates premium procedural wood, stone, brushed iron, and plated steel surface textures/materials and migrates only known setup-generated flat materials.
- MenuPanelSettings now uses Match Width/Height scaling with a larger atlas subtexture limit.
- Runtime systems no longer overwrite assigned PanelSettings every frame or on startup, allowing Inspector changes to persist.
- World Inspection resolves actual voxel materials and inventory hover information as introduced in `5.5.0-dev`.

**Roadmap Status:**
- Finite Quarry, Hammer wheel fixes, same-level snapping, doorway, Foundation visuals, premium materials, and UI fit: **WORKING ON** — awaiting Unity validation.

---

### [5.5.0-dev] Defense & Autopilot Roadmap, Hammer Wheel & Inspection Upgrade

**Type:** MINOR — new Hammer wheel UI system plus expanded inspection and construction workflows

**Roadmap Added:**
- Light gun, heavy ballistic, flamethrower, mortar, giant-shell, anti-air, missile, and energy/relic defensive turrets.
- Factory ammunition production for casings, propellant, projectiles, magazines, shells, flame fuel, guidance components, and special payloads.
- Automated ammunition replenishment through provider/requester chests, belts, pipes, drones, and docks.
- Special ammunition for cryogenic, volcanic, acid-rain, vacuum, Ghoul, naval-boss, and Basilisk-class threats.
- Manual grid route calculation reporting distance, time, thrust, power/fuel requirement, and reserve margin from live ship contents and condition.
- Recorded interplanetary routes, cargo-stop actions, route editing, and full Grid Autopilot behavior.
- Rogue-territory avoidance and intervention rules for automated ships.
- Coordinate Jump Drive with charge, mass-scaled range, cooldown, arrival error, safe-volume checks, gravity restrictions, and Autopilot route legs.
- Paginated/scrollable Building Hammer wheel requirements for future Orbital Station construction families.

**Runtime Added / Improved:**
- Building Hammer wheel rebuilt as an eight-segment paginated donut matching the conveyor wheel visual language.
- Mouse-wheel page scrolling, future-family page capacity, selected/hovered segment feedback, cost colors, center Upgrade Mode, and subtle parallax.
- Escape reliably closes the wheel and exits the selected Hammer build family before the Pause menu can open.
- Tiered construction now places through the standard Build action (`RMB` by default) instead of Mine (`LMB`).
- Player interaction routing yields RMB exclusively to active Hammer placement.
- Tiered building materials now use generated premium wood grain, stone aggregate, brushed iron, and plated steel textures with tier-specific metallic/smoothness values.
- Existing custom prefab materials remain untouched; only missing or known setup-generated flat materials are upgraded.
- World Inspection now resolves actual active-world voxel material, hardness, and mining tier instead of showing the world bootstrap object name.
- Hovering interactive inventory slots displays item name, category, stack capacity, total mass, and durability.

**Changed:**
- Step 5 material creation is idempotent and preserves existing materials.
- Hammer page architecture reserves capacity for research-unlocked Orbital Station families.
- Updated runtime and roadmap version to `5.5.0-dev` under Semantic Versioning because the Hammer wheel is a new UI/build-selection system.

**Roadmap Status:**
- Hammer wheel, RMB placement, Escape exit, premium tier materials, and inspection upgrades: **WORKING ON** — awaiting Unity validation.
- Turret automation, Grid Autopilot, route calculator, and Jump Drive: planned for their documented progression eras.
- Step 5 prerequisite repair remains **WORKING ON** until setup validation is reported.

---

### [5.4.0-dev] Pollution & Planetary Ecology Roadmap + World Inspection Overlay

**Type:** MINOR — new save-compatible inspection UI plus major roadmap content expansion

**Roadmap Added:**
- Pollution sources, spread, persistence, cleanup, filters, contamination, sensors, and map overlays.
- Pollution-driven enemy attraction that escalates scouts, packs, elites, siege creatures, and regional bosses at the source.
- Planet-specific Ecology Profiles for passive mobs, hostiles, elites, bosses, resistances, loot, and spawn budgets.
- Acid-rain creatures adapted to corrosive conditions and therefore stronger than equivalent temperate creatures.
- Fallen Crusaders, Dead Priests, corruption-created Ghouls, and relic-linked Order history.
- Rogue Space Crusader territories, warnings, patrols, pursuit, boarding, reputation, tribute, and commander bosses.
- Themed passive creatures and signature bosses for temperate, barren, ice, volcanic, acid-rain, oceanic, gas-giant, asteroid, and anomaly worlds.
- Pollution Service, Ecology Registry, Threat Director, and Territorial Space AI architecture.
- Expanded Living Worlds and Step 20 setup requirements.
- Crusader player identity, livestock, mythical enemies, boss relic gates, Dyson Sphere, Star Builder, and Orbital Station Hammer family from Section 4.6.

**Runtime Added:**
- A premium top-left World Inspection Overlay for the current crosshair target.
- Target name, category/type, operating state, conveyor/chute occupancy, power demand/output, stack size, distance, and integrity where available.
- Animated fade and slide transitions with no pointer capture.
- Context resolution for placed blocks, tiered buildings, grid blocks, machines, conveyors, chutes, dropped items, and generic world objects.
- Weather HUD moved beneath the inspection surface to prevent overlap.

**Removed:**
- Experimental Corner/Spiral chute selection, runtime variant generation, and chute-shape save fields.
- Chutes no longer activate the conveyor-only radial selector.

**Changed:**
- The Shape Wheel remains exclusive to Basic, Fast, and Express conveyors.
- Conveyor Chutes return to the validated Straight transport workflow and are marked **PARTIALLY COMPLETE**.
- Updated runtime and roadmap version to `5.4.0-dev` under Semantic Versioning because this release adds the World Inspection Overlay.

**Roadmap Status:**
- World Inspection Overlay: **WORKING ON** — awaiting Unity validation.
- Pollution, planetary ecology, Rogue Space Crusaders, and themed bosses/passive mobs: planned for Living Worlds and later eras.
- Step 5 prerequisite repair remains **WORKING ON** until setup validation is reported.

---

### [5.3.2-dev] Precision Vertical Alignment & Wheel Hover Feedback

**Type:** PATCH — snapping precision, collider alignment, and interaction feedback polish

**Fixed:**
- Vertical item paths now begin/end at the same `0.52 m` belt-surface offset used by Straight and Ramp conveyors.
- Vertical meshes, rails, rollers, arrows, and status lines are shifted to the same shared surface height.
- Vertical shape colliders now match the visible upright frame instead of retaining the wide horizontal conveyor collider.
- Ramp colliders now cover the complete low-to-high sloped assembly.
- Straight/Ramp ↔ Vertical transitions no longer look vertically offset even when their logical sockets already match.

**Improved:**
- Hovering a wheel segment now changes the full wedge to a stronger light blue.
- The hovered segment icon and label turn blue, scale to `1.12x`, and receive a subtle glow backing.
- Selected segments remain cyan/white while still receiving the hover-scale response.

**Changed:**
- Collider dimensions update whenever a build shape changes and restore to authored straight values for Straight/Corner mode.
- Updated the runtime and roadmap version to `5.3.2-dev`.

**Roadmap Status:**
- Vertical transition alignment and wheel feedback: **WORKING ON** — awaiting Unity validation.
- Step 5 prerequisite repair remains **WORKING ON** until setup validation is reported.

---

### [5.3.1-dev] Donut Wheel Polish & Vertical Transition Fixes

**Type:** PATCH — UI presentation, conveyor visual polish, snapping, and transfer fixes

**Added:**
- A true three-segment donut rendering for Straight, Ramp, and Vertical modes.
- Angular segment hit-testing, narrow segment gaps, a full dark center disc, and tier/mode labels integrated into the ring.
- Selected cyan, hovered pale-cyan, and idle light-industrial segment states.
- Bright emissive runtime arrow material shared by adaptive Straight, Corner, Ramp, and Vertical arrows.
- Exact orthogonal transition support when either connected belt is Vertical.

**Fixed:**
- The conveyor wheel no longer reads as three floating cards around a center badge.
- Ramp belt surfaces now sit on a sloped underbed instead of floating above a flat frame.
- Ramp supports follow the low and high ends of the slope.
- Ramp arrows are aligned to the belt plane rather than hovering horizontally above it.
- Vertical Down arrows are larger, brighter, and oriented as a visible downward chevron.
- Vertical Up arrows sit flush against the conveyor face.
- Vertical mode snaps its input socket directly onto Straight or Ramp output sockets.
- Straight and Ramp conveyors can hand items into Vertical conveyors through exact matched sockets.
- Vertical conveyors can hand items back into Straight or Ramp conveyors through exact matched sockets.

**Changed:**
- The subtle mouse-follow animation remains active on the complete donut wheel.
- Updated the runtime and roadmap version to `5.3.1-dev`.

**Roadmap Status:**
- Conveyor wheel/transition polish: **WORKING ON** — awaiting Unity validation.
- Step 5 prerequisite repair remains **WORKING ON** until setup validation is reported.

---

### [5.3.0-dev] Contextual Conveyor Shape Wheel & Step 5 Repair

**Type:** MINOR — new save-compatible radial build UI and conveyor mode selection

**Added:**
- A contextual hold-to-open conveyor wheel using the existing rebindable `Build Wheel` action (`B` by default).
- Per-tier Straight, Ramp, and Vertical selections for the existing Basic, Fast, and Express conveyor items.
- A prompt above the hotbar showing the current key and selected conveyor mode.
- Mouse-click radial selection with hover scaling, animated color states, and subtle mouse-follow parallax.
- Aim-based Up/Down resolution for Ramp and Vertical placement.
- Additive persistence for explicitly selected Ramp/Vertical shapes; legacy saves remain compatible.

**Changed:**
- The separate twelve-item/twelve-recipe variant plan from `5.2.0-dev` is superseded before setup validation.
- Step 17 no longer generates separate conveyor variant prefabs, items, recipes, materials, or research entries.
- One conveyor item and recipe per speed tier now provides every supported shape.
- The same contextual `Build Wheel` binding is shared safely with the Hammer wheel because each wheel only activates for its own held item.
- Step 5 now resolves prerequisite resources by canonical path or item ID and creates only missing canonical assets instead of incorrectly demanding another Step 4 run.
- Updated the runtime and roadmap version to `5.3.0-dev` under Semantic Versioning because this release adds a new UI/build-selection system.

**Roadmap Status:**
- Conveyor Shape Wheel: **WORKING ON** — awaiting Unity interaction validation.
- Step 5 prerequisite repair: **WORKING ON** — awaiting Unity setup validation.
- Step 17 base factory setup: **COMPLETED**.

---

### [5.2.0-dev] Full-Tier Ramp & Vertical Conveyor Variants

**Superseded by `5.3.0-dev` before Unity setup validation; separate variant assets are no longer generated.**

**Type:** MINOR — new save-compatible conveyor blocks, recipes, prefabs, and placement behavior

**Added:**
- Twelve explicit conveyor variant blocks generated through Step 17:
  - Basic, Fast, and Express Ramp Up.
  - Basic, Fast, and Express Ramp Down.
  - Basic, Fast, and Express Vertical Up.
  - Basic, Fast, and Express Vertical Down.
- One non-destructive prefab, item, and recipe per new variant.
- Research unlock merging for all new variant recipes.
- Full-height one-block ramp item paths and socket offsets.
- Vertical conveyor runtime meshes with backplates, rails, rollers, direction chevrons, and centered status lines.
- Editor-visible authored previews for every variant prefab.

**Changed:**
- Explicit ramp and vertical prefabs disable automatic horizontal shape conversion.
- Ramp placement snaps forward from a target belt; Ramp Down also lowers its root by one block so its upper input aligns with the source.
- Vertical variants stack upward/downward and can snap to configured vertical item ports.
- Belt connection discovery now uses shape-specific socket positions rather than assuming every socket is half a block from the root.
- Runtime ramp visuals now rise or descend a complete block over one horizontal cell.
- Step 17 is marked **WORKING ON** until the new twelve-variant generation pass completes two-run Unity validation.
- Updated the runtime and roadmap version to `5.2.0-dev` under Semantic Versioning because this release adds new save-compatible blocks and recipes.

---

### [5.1.13-dev] Closed-Loop Corners, Chute-to-Belt Snap & Status Cleanup

**Type:** PATCH — topology resolution, reciprocal snapping, and requested visual cleanup (no save/public API change)

**Fixed:**
- Conveyor loops no longer deadlock while every future corner waits for the next belt to expose a matching input.
- Shape inference now uses the placed belt rotation as its intended output and only requires an adjacent forward belt while resolving topology.
- Closed circles resolve each unambiguous side/rear input into the correct corner during the immediate refresh pass.
- A conveyor aimed at the top or bottom of a chute now snaps to that chute face and inherits its rotation.

**Changed:**
- Step 17 no longer creates the legacy rotated `Generated_Arrow` status square.
- Step 17 removes that specific obsolete generated square from existing conveyor prefabs, as explicitly requested.
- Direction chevrons remain independent from the centered `Generated_StatusLine`.
- Step 17 reports the number of removed obsolete generated visuals while preserving custom visuals and all balance values.
- Updated the runtime and roadmap version to `5.1.13-dev`.

---

### [5.1.12-dev] Responsive Corners, Bidirectional Chute Snap & Conveyor Indicators

**Type:** PATCH — placement responsiveness, snapping behavior, and visual communication polish (no save/public API change)

**Added:**
- Immediate conveyor topology refresh when a conveyor is enabled or finishes placement.
- A guarded next-frame topology verification pass for newly registered physics colliders.
- Step 17-generated direction chevrons on every Basic, Fast, and Express conveyor prefab.
- A dedicated emissive center status line generated and linked non-destructively through Step 17.
- A dynamic status line for adaptive straight/corner geometry, including safe runtime renderer retargeting.

**Fixed:**
- Adaptive corners no longer wait for the slower periodic connection scan before changing shape.
- A chute aimed at the upper conveyor face/upper side now snaps above the conveyor.
- A chute aimed at the lower face/lower side continues to snap below the conveyor.
- Runtime status materials are restored and released safely when adaptive geometry changes renderer targets.

**Changed:**
- Existing conveyor status indicators pointing at the legacy arrow are migrated to `Generated_StatusLine`; balance, colors, materials, and custom tuning remain preserved.
- Default straight conveyors keep their authored Step 17 visuals, while adaptive geometry uses an equivalent centered live status line.
- Updated the runtime and roadmap version to `5.1.12-dev`.

---

### [5.1.11-dev] Adaptive Corners, Strict Lane Isolation & Chute Snapping

**Type:** PATCH — transport routing, adaptive visual, snapping, and modal stability fixes (no save/public API change)

**Fixed:**
- Conveyor handoff and pull operations now revalidate live socket alignment, preventing stale targets from moving items into a neighboring parallel lane.
- Belt socket tolerance is tighter and shared by discovery, topology inference, pull, and handoff checks.
- Conveyor shape inference now supports every unambiguous perpendicular input/output pair instead of assuming side-in/forward-out.
- Ambiguous junctions and loose endpoints remain straight.
- Item Ports no longer disappear when live logistics changes the open chest inventory.
- Item Ports keep Escape handling active while chest contents update and rebuild the latest inventory state after closing.

**Added:**
- Smooth segmented corner-belt geometry that follows the same curve used by transported items.
- Dynamic corner support base, four legs, endpoint rollers, curved rails, and an output-direction arrow.
- Chute placement snapping beneath conveyors.
- Chute placement snapping to configured top/bottom item ports.
- Bidirectional stacking above or below existing chutes based on the selected chute face.

**Changed:**
- Straight belts with a non-default inferred axis use matching dynamic geometry; default straight belts continue using their authored Step 17 visuals.
- Updated the runtime and roadmap version to `5.1.11-dev`.

---

### [5.1.10-dev] Unity-Safe Transport Visual Initialization

**Type:** PATCH — Unity lifecycle compatibility fix (no save/public API change)

**Fixed:**
- `BeltVisualController` no longer creates a `MaterialPropertyBlock` from a MonoBehaviour field initializer.
- `ConveyorChute` no longer creates a `MaterialPropertyBlock` from a MonoBehaviour field initializer.
- Both components now allocate their property block during `Awake`, with a guarded runtime fallback before first use.
- Asset import workers can deserialize conveyor and chute prefabs without invoking forbidden native object creation from MonoBehaviour constructors.

**Changed:**
- Updated the runtime and roadmap version to `5.1.10-dev`.

---

### [5.1.9-dev] Directional Conveyor, Chute & Item-Port Fixes

**Type:** PATCH — transport routing and UI-close bug fixes (no save/public API change)

**Fixed:**
- Parallel conveyor lanes no longer detect each other as side inputs.
- A conveyor accepts another belt only when the two physical sockets meet and their input/output directions oppose correctly.
- Rear-fed conveyor lines remain straight instead of changing the first or last segment into an accidental L shape.
- Side-fed corners require one valid side provider and a valid forward continuation, preventing loose or ambiguous corner conversion.
- Side conveyors no longer pull from or push into the middle of an unrelated straight belt.
- Chutes scan immediately when enabled and use a wider nearest-endpoint search above and below.
- Chutes can transfer between belts, compatible inventory interfaces, and correctly configured item-port faces.
- Item-port routing and filters are respected when a chute inserts into a configured endpoint.
- Escape now closes the active item-filter dialog first, then the Item Ports overlay on the next press; each consumes pause input and leaves the underlying inventory open.

**Changed:**
- Conveyor and chute roadmap audit notes now reflect the directional routing fixes.
- Updated the runtime and roadmap version to `5.1.9-dev`.

---

### [5.1.8-dev] Conveyor & Chute Runtime Reliability

**Type:** PATCH — transport bug fixes and visual lifecycle polish (no save/public API change)

**Added:**
- Pooled, color-coded moving item representations inside conveyor chutes.
- Shared fallback chute materials used only when authored setup visuals are unavailable.
- Material-property-block item coloring for belts and chutes without per-item material allocation.

**Fixed:**
- Authored conveyor and chute visuals are no longer duplicated at runtime.
- Straight conveyors reuse their detailed Step 17 prefab visuals; runtime meshes are reserved for dynamic corner/ramp shapes.
- Conveyor corner items now follow their configured input/output directions.
- Conveyor and chute extraction now returns the full amount removed across multiple item packets.
- Connection discovery now finds the correct provider/consumer component even when another component appears first on the target object.
- Chute connection scans now follow the chute's local up axis for spherical-world placement.
- Factory status indicators now preserve authored light intensity instead of resetting it to `1`.
- Runtime status materials are released when their object is destroyed.

**Changed:**
- Step 17 is marked **COMPLETED** after successful two-run Unity validation.
- Conveyor belts and conveyor chutes are marked **WORKING ON** while authored shape variants remain outstanding.
- Updated the runtime and roadmap version to `5.1.8-dev`.

---

### [5.1.7-dev] Non-Destructive Step 17 Factory Setup Hardening

**Type:** PATCH — editor setup safety fix (no save/runtime API change)

**Added:**
- Step 17-specific non-destructive asset and prefab creation helpers.
- Additive recipe, research prerequisite, research unlock, and machine-recipe link merging.
- Clear setup summary logging for created assets, created prefabs/components, preserved content, and repaired links.
- Conflict handling that leaves wrong-type or unreadable existing assets untouched instead of deleting them.

**Changed:**
- Existing materials retain their authored colors, emission, shader properties, and other tuning.
- Existing generated or custom prefab children retain their transforms, meshes, materials, and effects.
- Existing component values are initialized only when the component is newly added.
- Existing item health, mass, stack limits, grid values, power draw, throughput, connection limits, processing speeds, recipe costs, crafting times, and research costs are preserved.
- Required prefab, registry, machine-recipe, research-tree, prerequisite, and unlock links are repaired additively without removing custom entries.
- Legacy duplicate assets are no longer automatically deleted by Step 17.
- Updated the runtime and roadmap version to `5.1.7-dev`.

**Validation:**
- Static non-destructive-path checks passed.
- Source brace, whitespace, conflict-marker, version, and external-title checks passed.
- Unity two-run idempotency validation remains required and is documented in the delivery instructions.

---

### [5.1.6-dev] Factory Foundations Audit & Roadmap Execution Tracking

**Type:** PATCH — documentation/status alignment and version synchronization (no save/API touch)

**Added:**
- A three-state roadmap execution convention: **WORKING ON**, **PARTIALLY COMPLETE**, and **COMPLETED**.
- A repository-backed execution status table for Factory Foundations.
- A clear completion gate for the 4.5.0 section.

**Changed:**
- Marked Factory Foundations as **WORKING ON**.
- Marked its New Content, Improved Features, and Code Improvements groups as **PARTIALLY COMPLETE**.
- Synchronized the roadmap and runtime version to `5.1.6-dev`.
- Updated the roadmap date and active implementation status.

**Audit Findings:**
- Core conveyor, chute, machine, power transmission, and lighting foundations are present.
- Authored conveyor/chute variants, full shared machine UI, unified ticking, pooled item entities, and factory persistence remain incomplete.
- Step 17 creates and connects the intended content, but its rerun path still requires hardening so existing balance values are never reset.

**Notes:**
- No Unity scenes, prefabs, recipes, items, research assets, save schemas, or public runtime APIs were changed.
- The next implementation priority is the non-destructive Step 17 setup hardening required by Section 7.4.

---

### [4.5.7-dev] Iterated Factory-Forward Roadmap — Radiation, Heat, Oxygen, Airtight Systems & Painting

**Type:** PATCH — roadmap refinement (no save/API touch)

**Added:**
- **Radiation system** to 5.1.0:
  - Uranium reactor and thorium reactor (thorium material, more efficient, less rare, late-game unlock).
  - Radioactive waste system; thorium waste is much less radioactive.
  - Radiation-sealed containers and radiation sealing blocks.
  - Radiation damage to players; hazmat suit and geiger counter.
  - Reactor stays shielded unless waste storage overflows.
  - Large container-style reactor design.
- **Heat system** to 5.1.0:
  - Heat tolerance for every grid block, shown in descriptions.
  - Heat generation for engines, thrusters, reactors, and exhaust pipes.
  - Heatshield block, atmospheric entry heat simulation, cockpit heat indicator, player heat UI.
- **Life support** features:
  - Space helmet with visor toggle, oxygen tank for chest slot.
  - Suffocation in space/underwater without helmet/tank.
  - Airtight sliding doors and vents.
- **Armor Station** and **Jetpack** to 4.7.0.
- **Armor upgrades** tiers 1–5 for heat, radiation, oxygen, and fall impact.
- **Fall damage** and **oxygen underwater** to 4.7.0.
- **Painting system** to 4.7.0 with 15 material finishes.
- New cross-cutting services: Life Support Service, Pressure & Airtight Service, Thermal Simulation Service, Painting Service.

**Changed:**
- Roadmap version bumped from `4.5.6-dev` to `4.5.7-dev`.
- Updated current state snapshot with radiation, heat, oxygen, airtight, fall damage, painting, and armor crafting.
- Updated 4.7.0, 4.9.0, and 5.1.0 feature breakdowns and manual Unity steps.

### [4.5.6-dev] Iterated Factory-Forward Roadmap — Power Pole Wire System, Substations & LED Strips

**Type:** PATCH — roadmap refinement (no save/API touch)

**Added:**
- **Power Pole & Wire System** to 4.5.0:
  - Player crafts Wire and runs it from poles to machine Cable Inputs.
  - Generators have Cable Outputs that feed into the network.
  - Standard power pole supports up to 6 connections.
  - Electrical Substation relays power over 100+ meters.
- **LED Strips** to 4.5.0 for accent lighting on grids and static surfaces.
- New interfaces `IPowerConsumer` and `IPowerProducer` in the simulation namespace.

**Changed:**
- Roadmap version bumped from `4.5.4-dev` to `4.5.6-dev`.
- Updated 4.5.0 manual Unity steps to include power poles, substations, wire items, cable sockets, and LED strips.

**Notes:**
- No code or Unity scenes modified in this deliverable.
- All future feature work targets the `Dev` branch and follows Semantic Versioning 2.0.0.

---

### [4.5.4-dev] Iterated Factory-Forward Roadmap — Grid Shape Variant Wheel

**Type:** PATCH — roadmap refinement (no save/API touch)

**Added:**
- **Grid Shape Variant Wheel** to 4.7.0:
  - When holding a light or heavy armor block, the player can open the same round build wheel used by the build hammer.
  - Switch between shape variants on the fly: full block, slope, half block, half slope, corner, inverted slope.
  - Variants share the same recipe/material cost scaled by volume.
- Added shape variants to grid building improvements: half blocks, half slopes, corners, inverted slopes.
- Added "Grid shape variant wheel" to current state snapshot.
- Updated progression curve Era 3 to include shape variants.
- Updated manual Unity steps for 4.7.0 to include shape variant prefabs and wheel UI.

**Changed:**
- Roadmap version bumped from `4.5.3-dev` to `4.5.4-dev`.
- Vision now emphasizes deep shape customization in grid-based engineering.

**Notes:**
- No code or Unity scenes modified in this deliverable.
- All future feature work targets the `Dev` branch and follows Semantic Versioning 2.0.0.
- Game titles are referenced only as genre descriptions; no external game names are written into shipped code or assets.

**Type:** PATCH — roadmap refinement (no save/API touch)

**Added:**
- **Sky & Space Ambiance** features in 4.9.0:
  - Planet-specific skies and atmospheres (blue skies, auroras, ash-orange horizons, starfields).
  - Seamless sky-to-space transition.
  - Planets render as colored spheres from orbit.
  - Overhauled space ambiance: black void, stars, nebulae, sun glare, vacuum audio ducking.
- **Gravity & Orbit Fixes** in 4.9.0:
  - Consistent gravity for players, grids, dropped items, and projectiles.
  - Realistic orbital mechanics with velocity + altitude, atmospheric drag, escape velocity.
- **Space Stations** in 5.0.0:
  - Buildable orbital platforms with hull, docking ports, gravity rings, solar arrays.
- **Conveyor Chutes** replacing inserters in 4.5.0.
- **Grid Lights & Static Flood Lights** in 4.5.0.
- **Player Armor Slots** in 4.7.0.
- **Bombs, Explosive Charges, and Nuclear Warheads** in 4.7.0 and 5.1.0.
- **Grid Building Improvements** in 4.7.0:
  - Sloped blocks, heavy armor blocks, heavy armor sloped blocks.
  - Small-grid usability improvements.
  - Maritime grid improvements (buoyancy, hull blocks, propellers).
- **Configurable Grid Screens / Displays** in 4.8.0:
  - Multiple sizes: 1×1, 2×2, 4×4, wide banner.
  - Display text, values, charts, and live camera feeds.
  - User-friendly data-source picker and styling options.
- **Camera Block** that feeds render textures to screens.
- **Nuclear Warheads & Heavy Ordinance** in 5.1.0.
- **New Voxel Engine Setup Workflow** cross-cutting section:
  - All prefabs, recipes, items, and research nodes must be generated via `Tools > Voxel Engine > Voxel Engine Setup`.
  - Non-destructive: create if missing, preserve user edits, never overwrite balance values.
  - Idempotent, versioned steps, clear console logging.
- New emotional beats: *first sky, first orbit*.

**Changed:**
- Roadmap version bumped from `4.5.2-dev` to `4.5.3-dev`.
- Current game version updated from `4.4.0-dev` to `4.5.1-dev`.
- Master roadmap table updated:
  - 4.5.0 → Factory Foundations (conveyors, chutes, lights).
  - 4.7.0 → Power, Vehicles & Combat (armor, bombs, grid improvements).
  - 4.8.0 → Logistics 2.0, Screens & Trajectory.
  - 4.9.0 → Living Worlds (skies, gravity, space ambiance).
  - 5.0.0 → Orbital Expansion (space stations).
  - 5.1.0 → Interplanetary Age (nuclear warheads).
- Vision now emphasizes atmospheric grandeur and awe.
- Current state snapshot expanded with: sky/space rendering, gravity/orbits, space stations, conveyor logistics, grid screens, grid lighting, sloped/armored blocks, player armor slots.
- Manual Unity steps expanded for all versions, including non-destructive Voxel Engine Setup steps.
- All "inserter" references replaced with "chutes".

**Notes:**
- No code or Unity scenes modified in this deliverable.
- All future feature work targets the `Dev` branch and follows Semantic Versioning 2.0.0.
- Game titles are referenced only as genre descriptions; no external game names are written into shipped code or assets.
