# Maritime Propulsion & Mechanical Network — Changelog

Branch: **Dev** · Semantic Versioning 2.0.0

---

## [2.18.0] — Maritime Propulsion Core Engine (Part 1 of 5)

**Type:** MINOR — new system, save-compatible (adds new scripts + a component; no save/chunk schema change).

### Added — `Scripts/Maritime/` (new domain)
| File | Role |
|------|------|
| `MechanicalNodeType.cs` | Byte-backed enum of every block role (Engine, Shaft, Gearbox, Waterwheel, Propeller, ElectricalPropeller, Generator, Turbocharger, ExhaustPipe, Hull). |
| `MechanicalNode.cs` | Blittable, Burst-friendly per-block record (geometry, mass, buoyancy, torque/RPM, gear, flow, computed force/torque). + flag accessors + `TurboBoost = 1.40`. |
| `WaterProbeSystem.cs` | Batched `GetWavesHeights(positions, outHeights)` API over the voxel FluidManager, with per-frame column cache + chunk flow-field sampling. |
| `MaritimeSettings.cs` | Tuning ScriptableObject (water density, thrust coeff, cavitation, gearbox cap, rudder, …) with runtime fallback. |
| `MechanicalPropagationJob.cs` | `[BurstCompile] IJobParallelFor` over **chains** — sweeps torque/RPM source→shaft→gearbox→consumer; applies turbo boost, broken-shaft cutoff, generator load. Zero GC. |
| `BuoyancyJob.cs` | `[BurstCompile] IJobParallelFor` over **every node** — Archimedes buoyancy, propeller thrust, waterwheel paddle thrust, E-propeller, hull drag; accumulates force + `cross(r,F)` righting torque. |
| `IMechanicalBlock.cs` | Interface Part-2 blocks implement (`PopulateMaritimeNode` static + `RefreshMaritimeNode(throttle)` dynamic). |
| `MaritimePropulsionSystem.cs` | The **only** MonoBehaviour in the stack. Rebuilds the cached propulsion graph on ship edit; each FixedUpdate: refresh → sample water → propagation job → buoyancy job → sum → `AddForce`/`AddTorque` on Rigidbody. Turbocharger adjacency + Helm rudder steering included. |

### Changed
- **`GridEntity.cs`** — `AddBlock`/`RemoveBlock` now call `NotifyMaritimeDirty()`; new `Maritime` accessor lazily caches the propulsion system. Flying-ship thrusters/gyros untouched.
- **`GameVersion.cs`** — `2.17.x → 2.18.0`.

### How it fits the requirements
1. ✅ `IJobParallelFor` + Burst, zero-GC, `NativeArray`/`Unity.Mathematics`.
2. ✅ `MechanicalNode` struct for Engine/Shaft/Waterwheel/Propeller (+ more).
3. ✅ `BuoyancyJob`: submergence 0–1, Archimedes buoyancy, `Thrust = RPM × Submergence × Size`, waterwheel torque-from-flow / paddle-thrust.
4. ✅ Turbocharger: adjacent Giant Diesel → `×1.40` torque (flagged at rebuild, applied in job).
5. ✅ Single resultant `Vector3` force + torque applied to the parent Rigidbody in `FixedUpdate`.
6. ✅ Component-driven network graph cached as a struct array; **zero** per-block MonoBehaviour loops.

### Manual Unity steps (Part 1)
1. After pulling, let Unity recompile (new `Scripts/Maritime/` folder auto-included by the `VoxelEngine` asmdef — Burst/Mathematics already referenced).
2. To test on a ship: select a grid's root GameObject → **Add Component → Maritime Propulsion System**. Assign the `MaritimeSettings` asset (or leave null for runtime defaults — Part 5 will create the asset).
3. (Optional) tweak balance live on the `MaritimeSettings` asset.

### Next parts (roadmap)
- **2.18.2** — Hull materials (Untreated Wood, Tar-Coated Plank, Iron Hull, Balsa, Bilge Pump) + waterlogging + Helm→cockpit steering.
- **2.18.3** — MGO + Heavy Fuel Oil refinery/chemical-plant recipes.
- **2.18.4** — Setup-Wizard expansion: prefabs/items/recipes + 4-tier "Maritime Engineering" research tree.

---

## [2.18.1] — Propulsion & Power GridBlocks (Part 2 of 5)

**Type:** MINOR — new blocks + two liquid types, save-compatible.

### Added — new block classes (`Scripts/Maritime/`)
| File | Block(s) | Role |
|------|----------|------|
| `MaritimeBlockBase.cs` | (base class) | Shared `GridBlock + IMechanicalBlock` base with exhaust-check, liquid-fuel draw, solid-fuel draw helpers. |
| `GridMaritimeEngine.cs` | Small / Medium / Giant Diesel Engine | Torque source. Burns wood/coal (solid) or HFO/MGO (liquid) from grid storage. Requires adjacent exhaust pipe or chokes. Giant flagged for turbocharger. |
| `GridDriveShaft.cs` | Drive Shaft | Torque conduit. Disabled shaft = severed chain (Broken flag). |
| `GridGearbox.cs` | Gearbox | 6-speed ratio (0.5×–4×). More speed = less torque. RPM hard-clamped. |
| `GridWaterwheel.cs` | Waterwheel | Dual-mode: torque-from-flow (stationary) or paddle-thrust (shaft-driven). |
| `GridPropeller.cs` | Small / Large Propeller + Electrical Propeller | Thrust from RPM×submergence×size. E-prop draws grid electricity. |
| `GridTurbocharger.cs` | Turbocharger | Boosts adjacent Giant Diesel ×1.40 (flag set at graph rebuild). |
| `GridMaritimeGenerator.cs` | Maritime Generator | Shaft torque → electricity (P=τω). Feeds grid power pool via `PowerOutput`. |
| `GridExhaustPipe.cs` | Exhaust Pipe | Passive block — its adjacency is required by engines. |
| `GridHelm.cs` | Helm (Ship's Wheel) | Walk up + E to take control. W=throttle, A/D=steer. Drives `MaritimePropulsionSystem.Throttle/Steer/HelmActive`. |
| `StationaryMaritimeEngine.cs` | Stationary Maritime Engine | World-placed power plant. Burns fuel → electricity via `PowerGenerator`. Turbo toggle for +40%. |

### Changed
- **`LiquidType.cs`** — added `HeavyFuelOil` (4) + `MarineGasOil` (5) with display names, tints, densities.
- **`IMechanicalBlock.cs`** — added `ApplyResults(in MechanicalNode)` for post-job write-back (generator electricity, RPM readouts).
- **`MaritimePropulsionSystem.cs`** — calls `ApplyResults` on every live block after the buoyancy job completes.
- **`GameVersion.cs`** — `2.18.0 → 2.18.1`.

### Fuel system
- **Small Engine**: burns wood logs / planks / coal from `GridCargoContainer` slots. Buffer in burn-seconds.
- **Medium Engine**: burns `HeavyFuelOil` from `GridLiquidTank` blocks. Buffer in litres.
- **Giant Diesel**: burns `MarineGasOil` from `GridLiquidTank` blocks. Larger buffer + consumption.
- All engines require an **adjacent Exhaust Pipe** or they produce zero torque (choked).
- The `MaritimeBlockBase` provides `DrawLiquidFuel()` and `DrawSolidFuel()` helpers that scan the grid's storage.

### Manual Unity steps (Part 2)
1. Pull and recompile — all new classes are in the `VoxelEngine` assembly (no asmdef change needed).
2. **No prefabs exist yet** — the blocks are code-only. Part 5 (setup wizard) will generate prefabs + items + recipes. Until then you can test by manually adding components:
   - Create a grid, add `MaritimePropulsionSystem`.
   - Add child GameObjects with e.g. `GridMaritimeEngine` + `GridExhaustPipe` + `GridDriveShaft` + `GridPropeller` components.
   - Place the grid in water and add a `GridHelm` — press E near it to steer.

### Next parts (roadmap)
- **2.18.4** — Setup-Wizard expansion + 4-tier "Maritime Engineering" research tree.

---

## [2.18.3] — MGO + Heavy Fuel Oil Fuel Chain (Part 4 of 5)

**Type:** MINOR — new processing recipes, save-compatible.

### Added — 3 new ProcessingRecipe assets
| Recipe | Machine | Input | Output | Notes |
|--------|---------|-------|--------|-------|
| Proc_RefineHeavyFuelOil | Refinery | 80L Refined Oil | 55L Heavy Fuel Oil | Thick bunker fuel for Medium Engines |
| Proc_RefineMGO | Refinery | 60L Heavy Fuel Oil | 45L Marine Gas Oil | Clean high-grade distillate for Giant Diesel |
| Proc_SynthesiseMGO | Chemistry | 50L Refined Oil | 30L Marine Gas Oil | Catalytic-crack shortcut (skips HFO) |

### The complete fuel chain
```
Crude Oil (1)
  -> Refinery -> Refined Oil (2)
       |-> Refinery -> Heavy Fuel Oil (4)   [fuels Medium Engine]
       |    -> Refinery -> Marine Gas Oil (5)  [fuels Giant Diesel]
       -> Chemistry -> Marine Gas Oil (5)  [shortcut / catalytic crack]
```

### Changed
- VoxelEngineSetupWindow.cs (Step 10) - 3 new MakeProc calls; stationary Oil Refinery now gets HFO + MGO recipes; stationary Chemical Plant now gets the cracked-MGO recipe.
- VoxelEngineSetupWindow.cs (Step 12) - grid Refinery loads HFO + MGO recipe assets; grid Chemical Plant loads the cracked-MGO recipe.
- GameVersion.cs - 2.18.2 to 2.18.3.

### Manual Unity steps (Part 4)
1. Pull and recompile.
2. Re-run the Setup Wizard (Tools > Voxel Engine > Setup Wizard):
   - Click Step 10 (Industrial Content) - creates the new recipe assets + wires them into the stationary refinery and chemical plant.
   - Click Step 12 (Grid System Content) - wires them into the ship Refinery and Chemical Plant.
3. After re-running, a refinery on a ship can process: Crude -> Refined -> HFO -> MGO. Set the liquid tanks to the correct type (refinery input = Refined Oil/HFO, output tank = HFO/MGO).

---

## [2.18.2] — Hull Materials, Waterlogging & Cockpit Integration (Part 3 of 5)

**Type:** MINOR — new blocks + batched waterlogging system, save-compatible.

### Added — hull materials + bilge pump (`Scripts/Maritime/`)
| File | Block(s) | Role |
|------|----------|------|
| `GridHullBlock.cs` | `GridHullBlock` (base) | Buoyancy factor, waterproof flag, waterlogging state + `ContentMass`. |
| | `GridUntreatedWood` | Buoyant (0.85) but SOAKS water (40kg max, 1.5kg/s). Forces progression. |
| | `GridTarCoatedPlank` | Buoyant (0.9), 100% waterproof. Reliable mid-game hull. |
| | `GridIronHull` | Zero buoyancy (sinks!), 5x HP. Needs air pockets to float. |
| | `GridBalsaWood` | Max buoyancy (1.0), ultra-light, fragile (0.4x HP). |
| `GridBilgePump.cs` | `GridBilgePump` | Drains waterlogged hulls in a radius. Draws power. Batched tick. |

### Added — waterlogging system (in `MaritimePropulsionSystem`)
- **Batched waterlogging tick** — after the buoyancy job, hull blocks that are submerged + non-waterproof absorb water (increasing `ContentMass` -> heavier ship -> sinks lower -> soaks more). Bilge pumps drain first each tick.
- Proper `GridHullBlock.buoyancyFactor` now read at graph rebuild (replaces name-based heuristic).
- `_waterlogHulls` + `_bilgePumps` tracking lists populated at rebuild.

### Added — cockpit maritime integration (in `GridCockpit`)
- **`DriveMaritime()`** — when seated in a cockpit on a ship with a `MaritimePropulsionSystem`, the cockpit doubles as the helm: **W = throttle up, S = throttle down, mouse-yaw = rudder steer**.
- **`ZeroMaritime()`** — called on `Exit()` to cut throttle + rudder cleanly.

### Changed
- **`MaritimePropulsionSystem.cs`** — hull tracking + waterlogging tick + bilge pump batch.
- **`GridCockpit.cs`** — maritime drive/zero methods + import.
- **`GameVersion.cs`** — `2.18.1 -> 2.18.2`.

---

## [2.18.4] — Setup Wizard Expansion + Maritime Research Tree (Part 5 of 5)

**Type:** MINOR — new wizard step + research tree, save-compatible.

### Added — Step 13 in VoxelEngineSetupWindow
Creates ALL maritime content in one click: prefabs, items, recipes, the 4-tier
research tree, and the MaritimeSettings balance asset.

**Hull materials:**
- Untreated Wood Hull, Tar-Coated Plank, Iron Hull, Balsa Wood

**Propulsion blocks:**
- Waterwheel, Drive Shaft, Small Propeller, Large Propeller, Exhaust Pipe
- Small Engine (solid fuel), Medium Engine (HFO), Giant Diesel (MGO)
- Turbocharger, Gearbox, Maritime Generator, Electrical Propeller

**Control + utility:**
- Helm (ship's wheel), Bilge Pump

### Added — "Maritime Engineering" research tree (4 tiers)
1. **Tier 1: Hydro-Mechanics** -> Waterwheel, Drive Shaft, Untreated Wood, Small Propeller, Exhaust, Helm
2. **Tier 2: Steam & Internal Combustion** -> Small Engine, Tar Plank, Balsa, Gearbox
3. **Tier 3: Heavy Industrial Maritime** -> Medium Engine, Iron Hull, Bilge Pump, Generator, E-Propeller
4. **Tier 4: MSC Loreto-class Propulsion** -> Giant Diesel, Turbocharger, Large Propeller

### Added — Maritime spawner button
- DebugSpawnerWindow now has a dedicated "Spawn All MARITIME Blocks" button.

### Changed
- VoxelEngineSetupWindow.cs - new Step 13 button + BuildMaritimeContent() method
- DebugSpawnerWindow.cs - maritime category spawner
- GameVersion.cs - 2.18.3 to 2.18.4

### Manual Unity steps (Part 5)
1. Pull and recompile.
2. Run the Setup Wizard (Tools > Voxel Engine > Setup Wizard):
   - Click Step 13 (Build Maritime Content) AFTER Steps 4, 6, 7, 10, 12.
3. To test with items: open Tools > Debug (Spawner) > click "Spawn All MARITIME Blocks".
4. Build a ship in play mode: place hull blocks in water, add an engine+exhaust+shaft+propeller chain, sit in a cockpit or helm, press W to throttle.

---

## [2.18.5] — Idempotent Setup Wizard (never overwrite user edits)

**Type:** PATCH — behavioral fix to the wizard, no save/API touch.

### Fixed
All maritime wizard helpers now follow the **"create-only" idempotency rule**:
if the asset already exists, its fields are NEVER overwritten. Only missing
essentials (prefab reference on items, component type on prefabs, recipe
registration) are backfilled.

| Helper | Before | After |
|--------|--------|-------|
| `MakeMItem` | Overwrote all fields every re-run | Sets fields only on new items; backfills missing prefab ref + category |
| `MakeMPref` | Rebuilt mesh + re-ran config every re-run | Builds mesh + config ONLY on new prefabs; existing prefabs get component added-if-missing |
| `MaterialPersister` | Deleted + recreated existing mats | Returns existing mat as-is |
| `AddMRecipe` | Overwrote all recipe fields every re-run | Sets fields only on new recipes |
| `MakeProc` (Step 10) | Overwrote recipe fields every re-run | Early-returns if recipe exists |
| `MakeMaritimeNode` | Overwrote node fields every re-run | Sets fields only on new nodes |

### Changed
- `GameVersion.cs` — `2.18.4 to 2.18.5`.

---

## [2.18.6] — Critical Bug Fixes + Exhaust Gas System + Prefab Visuals (Part 6)

**Type:** PATCH + MINOR features — bug fixes + new exhaust gas mechanic.

### Fixed (critical)
1. **Nothing floats** — `MaritimePropulsionSystem` is now AUTO-ATTACHED to every `GridEntity` in `Awake()`. Buoyancy works for all ships out of the box.
2. **Missing scripts on prefabs** — `GetOrCreatePrefab` now calls `StripMissingScripts()` before saving. Uses `GameObjectUtility.RemoveMonoBehavioursWithMissingScript()` to clean up stale/broken script references from previous runs. This is why ALL maritime prefabs failed to save.
3. **Bad prefab visuals** — maritime blocks now use proper `GridBlockMeshBuilder` styles via `GridStyleFor()` (engines→HydrogenEngine/Reactor, propellers→Thruster, exhaust→GasPipe, helm→Cockpit, hulls→Armor, etc.) instead of `Style.Generic` for everything.

### Added — Exhaust Gas System
- Engines now accumulate **exhaust gas** while running (0..capacity).
- If exhaust gas fills to 100%, the engine **stops completely** (choked).
- Above 80% fill, the engine loses up to 70% power from back-pressure.
- Exhaust gas vents through adjacent **Exhaust Pipe** blocks at a fixed rate.
- Exhaust gas adds to the engine's `ContentMass` (compressed gas is heavy).

### Added — Exhaust Smoke VFX
- `GridExhaustPipe` now emits **smoke particles** when adjacent engines are venting.
- Giant Diesel → heavy black smoke; Small Engine → light grey sputter.
- Particles rise, expand, and fade realistically.

### Added — Item Descriptions
All 18 maritime items now have detailed descriptions including fuel types, dimensions, and usage tips.

### Added — Data fields for Part 7 UIs
- Engine: `CurrentUsage`, `CurrentTorque`, `Stress01`, `IsOverstressed`, `IsChoked`, `ExhaustFill01`
- Generator: `BufferCharge`, `BufferFill01`, `bufferCapacityWh` (internal battery buffer)
- Gearbox: `InputRPM`, `OutputRPM`, `Stress01`, `IsOverstressed`
- Turbocharger: `BoostPressure` (bar), `TurboRPM`

### Changed
- `GameVersion.cs` — 2.18.5 to 2.18.6

---

## [2.18.7] — All Maritime Block UIs (Part 7)

**Type:** MINOR — new UI system, save-compatible.

### Added — MaritimeBlockUI.cs (12 industrial-themed panels)
All panels use the shared `UITheme` design system (dark-steel OS aesthetic
with amber/cyan accents). Shown both on right-click AND in the ship terminal.

| Block | Panel shows |
|-------|-------------|
| **Engine** (Small/Medium/Giant) | Liquid: fuel tank + exhaust gas tank. Solid: burn-rate bar. Usage L/s, torque, speed RPM, stress bar, OVERSTRESSED status. Exhaust gas choke warning. |
| **Generator** | Power output, rated max, shaft RPM, production bar. Internal battery buffer gauge (Wh). |
| **Gearbox** | Gear ratio, input/output speed + torque, stress bar, OVERSTRESSED. 6 clickable gear selectors (0.5x-4x). |
| **Bilge Pump** | Drain rate, radius, power, draining/standby status. |
| **Propeller** | Speed RPM, submergence %, thrust N, size. Submergence bar. |
| **Electric Propeller** | Speed RPM, thrust N, power usage W, rated max. |
| **Turbocharger** | Boost pressure (bar), turbo rotations (RPM), boost multiplier. Pressure bar. Connected/disconnected status. |
| **Waterwheel** | Speed RPM, submergence, wheel size. Dual-mode description. |
| **Drive Shaft** | Speed RPM, max safe RPM. |
| **Exhaust Pipe** | Vent rate, smoke status (venting/idle). |
| **Helm** | Throttle %, steer value, throttle bar. Manned/unmanned status. |
| **Hull Block** | Buoyancy %, waterproof, mass, integrity. Waterlogging bar (if applicable). |

### Changed
- `GridBlockUI.cs` — routes maritime blocks to `MaritimeBlockUI.BuildPanel()`.
- `GridMasterTerminal.cs` — maritime blocks categorized + live quick-status in terminal list.
- `GameVersion.cs` — 2.18.6 to 2.18.7.

### Engine UI specifics (per Thomas's request)
- **Small Engine**: no tank — shows a solid-fuel burn-rate bar instead, with remaining seconds.
- **Medium/Giant Engine**: fuel tank gauge (shows fuel name + L), exhaust gas tank gauge.
- **Exhaust gas full** → engine stops. Warning pill shows "EXHAUST BACKING UP".
- **No exhaust pipe** → engine choked. Warning pill shows "NO EXHAUST PIPE — ENGINE CHOKED".
- **Stress > 95%** → OVERSTRESSED status pill.

---

## [2.18.8] — Bespoke Maritime Prefab Meshes + Missing-Script Fix (Part 8)

**Type:** PATCH — visual polish + bug fix, no save/API touch.

### Fixed — Missing scripts on hull prefabs
- **Root cause**: `StripMissingScripts` removed the broken script reference, but the
  `isNew` guard prevented re-adding the component on existing prefabs.
- **Fix**: The component `T` is now ALWAYS ensured (`GetComponent → AddComponent if null`)
  regardless of whether the prefab is new. This guarantees every maritime prefab has
  its script attached even after a strip.

### Added — MaritimeMeshBuilder.cs (15 bespoke procedural models)
Every maritime block now has a unique, recognizable mesh built from primitives:

| Block | Visual |
|-------|--------|
| **Small Propeller** | Bronze hub + 3 angled blades + cast iron packing gland |
| **Large Propeller** | Dark steel hub + 4 heavy blades + mounting boss |
| **Electric Propeller** | Bronze torpedo pod housing + 3 blades |
| **Small Engine** | Cast iron block + copper boiler + 2 brass pistons + flywheel |
| **Medium Engine** | Inline-4 cast iron block + 4 cylinders + belt drive + oil sump |
| **Giant Diesel** | Massive V-block + 6 angled cylinders + steel manifold + fuel pump |
| **Turbocharger** | Chrome snail housing + glowing red core + inlet/outlet pipes |
| **Gearbox** | Cast iron housing + large gear with 12 teeth + input/output shafts |
| **Waterwheel** | Iron rim + 8 oak paddles + steel hub + spokes |
| **Drive Shaft** | Chrome shaft + steel coupling flanges + universal joint |
| **Generator** | Dark steel housing + 3 copper coil windings + brass terminals + glow strip |
| **Exhaust Pipe** | Cast iron vertical pipe + flange + 6 vent holes + top cap |
| **Bilge Pump** | Dark steel housing + motor with cooling fins + copper outlet + status light |
| **Helm** | Oak ship's wheel (ring + 8 spokes + brass handles) + pedestal + compass binnacle |
| **Hull blocks** | Untreated Wood (plank lines), Tar Plank (dark), Iron Hull (rivets), Balsa (light) |

### Changed — MakeMPref
- Uses `MaritimeMeshBuilder` instead of `GridBlockMeshBuilder` (with `GridStyleFor`).
- **Version-marker force-rebuild**: each prefab gets a `__MaritimeMesh_vN` marker child.
  If the marker version doesn't match `MaritimeMeshBuilder.Version`, the mesh is rebuilt.
  This means re-running Step 13 after a mesh builder update automatically upgrades
  all prefabs — no manual deletion needed.
- Materials use the maritime material persister (saves as `MMat_*.mat`).

### Changed
- `GameVersion.cs` — 2.18.7 to 2.18.8.

### ⚠️ Manual Unity steps
1. Pull and recompile.
2. **Re-run Step 13** — the version marker will detect the old meshes and rebuild ALL
   maritime prefabs with the new bespoke models. No need to delete anything manually.

---

## [2.18.9] — Realistic Meshes + Animations + Turbo Tiers + Engine Renames (Part 9)

**Type:** MINOR — new visual system + turbo tiers + engine rebalance.

### Added — MaritimeAnimator.cs
Lightweight per-block visual driver (the only Update() in the maritime stack).
Drives named animation pivots created by the mesh builder:
- **Propellers**: blades spin at CurrentRPM (SpinPivot)
- **Turbocharger**: compressor wheel spins at TurboRPM (TurboSpin)
- **Engines**: pistons bob up/down at engine RPM with firing-order phase offset (Piston_N)
- **Crankshaft**: pulley spins (CrankPulley)
- **Waterwheel**: paddles rotate (SpinPivot)
- **Gearbox**: gear rotates at OutputRPM (GearRotor)
- **Generator**: coil rotor spins (GenRotor)
- **Helm**: wheel rotates with steer input (HelmWheel)

### Added — Turbocharger Tiers
- **Small Turbocharger** (1×1×1): +15% boost per unit. 8-blade chrome compressor.
- **Large Turbocharger** (2×2×2 - MASSIVE): +25% boost per unit. 12-blade compressor, intense red glow.
- Turbo slots per engine: Crude=1, HFO=2, MGO=4.
- Boost stacks additively (4 large turbos on MGO = +100% torque = 2× output).
- Engine self-computes `TurboBoostTotal` in `CountTurbos()` each tick.

### Renamed engines
| Old | New | Fuel | Max Torque | Turbo Slots |
|-----|-----|------|-----------|-------------|
| Small Engine | **Crude Engine** | Wood/Coal (solid) | 8,000 N·m | 1 small |
| Medium Engine | **Heavy Fuel Oil Engine** | Heavy Fuel Oil | 40,000 N·m | 2 (S or L) |
| Giant Diesel | **MGO Engine** | Marine Gas Oil | **500,000 N·m** (2.5× buff) | 4 (S or L) |

MGO Engine is now MASSIVE: 500k N·m torque, 12 L/s consumption, expensive recipe (48 steel plate, 24 iron gear, 12 adv circuit, 32 copper wire).

### Upgraded meshes (v3 — auto-rebuilds on Step 13)
Every mesh rebuilt with more detail and named animation pivots:
- **MGO Engine**: V12 block with 12 angled cylinders, twin exhaust manifolds, brass fuel rail, cooling fins, injector glow points.
- **Large Turbo**: 12-blade spinning compressor inside chrome snail housing, red hot-side, oil feed line.
- **Helm**: 8-spoke oak wheel with brass knobs, pedestal, compass binnacle.
- All engines now have named `Piston_N` children for animation.

### Changed
- `GameVersion.cs` — 2.18.8 to 2.18.9.
- Removed double-counting turbo boost from `MechanicalPropagationJob` (engine self-computes now).

---

## [2.18.10] — Hull Script Fix + Blade Geometry + Shaft Animation (Part 10)

**Type:** PATCH — bug fixes + visual fixes, no save/API touch.

### Fixed — Hull prefabs missing scripts (CRITICAL)
- **Root cause**: Hull prefabs created during compile-error runs had broken/unloadable
  script GUID references that `StripMissingScripts` couldn't fully resolve.
- **Fix**: Wizard now detects hull prefabs with ANY broken component (`comp == null`)
  and **force-deletes** them before recreating clean. Runs automatically on Step 13.

### Fixed — Propeller/turbo blades clamped together
- **Root cause**: All blades were positioned at the same offset from center and rotated
  around their OWN local origin instead of the hub center — so they overlapped.
- **Fix**: Each blade now gets its own **pivot GameObject at the hub center**.
  The pivot rotates by the blade angle → the blade mesh (offset from pivot) fans out radially.
  Applied to: Small Propeller (3 blades), Large Propeller (4 blades), Electric Propeller (3 blades),
  Turbocharger compressor (8/12 blades).

### Added — Drive shaft rotation animation
- Drive shaft now has a `ShaftSpin` pivot with visible U-joint cross.
- `MaritimeAnimator` spins it at CurrentRPM.
- Drive shaft added to the animator's auto-attach list.

### Changed
- `MaritimeMeshBuilder.Version` → 4 (auto-rebuilds all prefabs).
- `GameVersion.cs` — 2.18.9 to 2.18.10.

---

## [2.18.11] — Split Multi-Class Files (FINAL missing-script fix) 

**Type:** PATCH — critical structural fix.

### Root Cause FOUND
Unity's prefab serialization uses `[fileID, GUID]` to reference scripts. The fileID
is computed from the class name **AND the file it's defined in**. When a MonoBehaviour
subclass lives in a file whose name doesn't match the class name (e.g. `GridBalsaWood`
inside `GridHullBlock.cs`), Unity CANNOT resolve the script reference when loading the
prefab → "The associated script can not be loaded".

### Fix — Split every MonoBehaviour into its own file
| Old file | Split into |
|----------|-----------|
| `GridHullBlock.cs` (5 classes) | `GridHullBlock.cs` (base only) |
| | `GridUntreatedWood.cs` |
| | `GridTarCoatedPlank.cs` |
| | `GridIronHull.cs` |
| | `GridBalsaWood.cs` |
| `GridPropeller.cs` (2 classes) | `GridPropeller.cs` (PropellerTier enum + GridPropeller only) |
| | `GridElectricalPropeller.cs` |

Each class filename now matches its class name exactly.

### Wizard fix — Force-delete ALL maritime prefabs
Step 13 now force-deletes EVERY prefab in the Maritime/Prefabs folder before
recreating them, ensuring all script references point to the correct new files.

### Changed
- `MaritimeMeshBuilder.Version` → 5 (auto-rebuild).
- `GameVersion.cs` — 2.18.10 to 2.18.11.

---

## [2.18.11b] — Removed force-delete (preserve user edits)

**Type:** PATCH — behavioral fix.

### Fixed
Now that the root cause (multi-class files) is fixed, the brute-force
force-delete of ALL maritime prefabs has been REMOVED. Step 13 is now fully
idempotent again:

  1. StripMissingScripts cleans broken refs on load
  2. GetComponent<T> + AddComponent if null fixes the script
  3. Mesh rebuild ONLY when MaritimeMeshBuilder.Version changes
  4. Config/tuning ONLY applied to brand-new prefabs

User prefab/material/value edits are preserved across re-runs. The first run
after this update (mesh v5) will rebuild meshes + fix scripts. After that,
subsequent runs touch nothing.

---

## [2.18.12] — RequireComponent Fix + Running-State Animations + Right-Click UI

**Type:** PATCH — three bug fixes.

### Fixed — "Can't remove GridMaritimeEngine because MaritimeAnimator depends on it"
- **Root cause**: `MaritimeAnimator` had `[RequireComponent(typeof(GridBlock))]`.
  When StripMissingScripts tried to clean a broken GridMaritimeEngine reference
  on a prefab, Unity blocked it because MaritimeAnimator "depends on" GridBlock
  (and GridMaritimeEngine IS a GridBlock).
- **Fix**: Removed `[RequireComponent]` entirely. MaritimeAnimator already
  null-checks `_block` in Awake + Update.

### Fixed — Animations run unconditionally
- **Was**: Propellers spun, pistons pumped, turbos whirred even when the block
  was off / out of fuel / no exhaust.
- **Now**: Every animator checks the running state before animating:
  - Engine pistons + crank: only when `IsRunning` (fuel + enabled + exhaust)
  - Propeller/turbo/gearbox/generator/shaft/waterwheel: only when RPM > 0.5
  - Helm: always animates (steer input is fine regardless)

### Fixed — Right-click on maritime blocks doesn't open UI
- **Root cause**: `GridBlockHasUI()` in PlayerInteractionTool didn't include
  any maritime block types.
- **Fix**: Added `MaritimeBlockBase`, `GridHullBlock`, and `GridBilgePump`
  checks. Now right-clicking any maritime block opens its UI panel.

### Note — On/Off toggle
The ship terminal already supports toggling any block ON/OFF via the
`Enabled` flag. Maritime blocks respect this in `RefreshMaritimeNode`.
This was already working — the missing piece was the right-click UI access.

---

## [2.18.13] — Buoyancy Fix + Helm Enter Mode + Engine I/O Cubes

**Type:** MINOR — critical gameplay fix + new features.

### Fixed — Blocks float into the air like balloons (CRITICAL)
- **Root cause**: Large-grid block volume (15.6 m³) × water density (1025 kg/m³) × gravity
  produced ~157,000 N buoyancy per block, but minimum block mass is only 2,500 kg
  (~33,100 N weight). Net force = ~124,000 N upward = 5G acceleration!
- **Fix**: Buoyancy force is now capped to `weight × (1 + buoyancyFactor × 0.5)`.
  A fully submerged buoyant block produces at most 1.5× its weight in upward force —
  enough to rise to the surface but not shoot into the sky.

### Changed — Helm enters instead of opening UI
- Right-clicking a Helm now calls `Enter()` — sets up a third-person camera above
  the helm so you can see the wheel sticks + water ahead.
- **Scroll wheel** = zoom in/out (ship-size-aware default distance).
- W/S = throttle, A/D = steer, F or right-click = exit.
- Helm excluded from `GridBlockHasUI()` so it enters instead of opening a panel.

### Added — Engine I/O Port Cubes
Every engine now has distinct colored cubes showing connections:
| Port | Color | Location | Purpose |
|------|-------|----------|---------|
| Fuel Input | **Blue** (glowing) | -Z face | Fuel/items in |
| Exhaust Output | **Red** (glowing) | +Y top | Exhaust gas out |
| Shaft Output | **Gold** (glowing) | +Z face | Rotation out (spinning) |

MGO Engine has dual exhaust outputs + larger ports.

### Added — Propeller Input Cube
Propellers now have a **gold** shaft input cube on the -Z face showing where
rotation connects.

### Changed
- `MaritimeMeshBuilder.Version` → 6 (auto-rebuild).
- `GameVersion.cs` — 2.18.12 to 2.18.13.
