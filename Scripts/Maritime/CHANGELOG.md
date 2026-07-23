# Maritime Propulsion & Mechanical Network — Changelog

Branch: **Dev** · Semantic Versioning 2.0.0

---

## [2.26.2] — Port-True Pipe Seating, Shaft Coupling Rule, Grid-Tank Classic Bridge (Mesh v22)

**Type:** PATCH — behaviour fixes (game version 6.14.2-dev, MaritimeMeshBuilder v22).

### Fixed
- Pipes plug **straight out of the port face, centred** (anchor pushed half a pipe-cell along the authored facing) — no more MGO/HFO "beside the port" placements; O₂ ports line up on all engines.
- Port markers face outward correctly: prim-face orientation (containers stay axis-aligned); thin-Z disc authoring restored on O₂ / exhaust / gas-tap / item-intake / water-pump ports.
- Shaft-driven blocks mount half a cell **out** along the facing — coupling rings kiss; drive shafts no longer sink halfway into placed shafts and couple to gearboxes properly.
- Five-cell endpoint probes use the **host grid's lattice cell size** (mounted pipes were probing 5×0.5 m instead of 5×2.5 m).
- **Grid LiquidTank joins the classic liquid graph** via the new `LiquidTankClassicAdapter` (Step-13 installed, non-destructive) — classic liquid pipes connect at five lattice cells; O₂ and fuel flow to engines.

---

## [2.26.1] — Step-13 Fix: MGO Banded-Hitbox MissingComponentException

**Type:** PATCH — editor-tool crash fix (game version 6.14.1-dev).

### Fixed
- Step 13 no longer aborts with `MissingComponentException` at `BoxCollider.set_center` while retrofitting the MGO banded hitbox: `GetComponent() ?? AddComponent()` + unguarded sets replaced by the null-safe `FitBandHitbox` helper (find-or-create, re-fetch, warn-and-retry instead of crash).

---

## [2.26.0] — Oriented Ports, 5-Cell Endpoint Proximity, Flex Couplings & MGO Banded Hitbox (Mesh v21)

**Type:** MINOR — save-compatible connectivity features + snap/placement fixes (game version 6.14.0-dev, MaritimeMeshBuilder v21).

### Added
- **`MaritimePortFacing`** tag on every maritime port (engines, gearbox, exhaust pipe, shaft rings, water pump, liquid-tank markers): true authored outward direction, ending all position-guess mis-aims. Ports place **exactly centred on the port object along its facing**, ghost ≡ placed ≡ restored-after-load.
- **Five-cell cardinal endpoint proximity** for gas tanks, classic liquid tanks/pumps and item containers ("valid lattice direction, never diagonal") — engines refuel, breathe and offload exhaust without pipes touching the shell.
- **Exhaust flex couplings**: bellows stub seals each exhaust pipe's flange to the engine's real exhaust port at 2 Hz rescan.
- **MGO banded hitbox**: slim lower / full-width upper pair — walkable up close (Step-13 retrofit, non-destructive).

### Fixed
- Overhang machines (MGO) placed on deck faces no longer sink into the support — exact ghost pose is kept and persisted.
- Exhaust snap centred on smaller engines and the MGO; liquid snap works on the MGO; gas taps snap and centre; O₂ feed actually drawn.
- **Steam-heat port removed** — exhaust only, by request.

---

## [2.25.2] — Drive Shaft Floor Mounts Fully Removed (Mesh v20)

**Type:** PATCH — visual bugfix, save-compatible (game version 6.13.2-dev).

### Fixed
- **Pillow-block bearing pedestals removed** from the drive shaft model: v18 dropped the ground feet but left the cast-iron stands, caps, race rings and grease nipples — v20 removes them completely. The shaft is now a pure floating shaft line (end coupler flanges, spinning full-cell rod, spline ribs, keyway, U-joint yoke, clamp collars, gold `Port_ShaftIO_F/B` rings) with nothing hanging off the axis to clip decks or neighbour blocks. Collider refits on the Step-13 rebuild.

---

## [2.25.1] — Port-Snap Compile Fix (CS0126)

**Type:** PATCH — build fix, zero behaviour change (game version 6.13.1-dev).

### Fixed
- `GridBuilder.TryApplyMaritimePortSnap`: a valueless `return;` in the bool-returning snap path (port-axis unresolvable fallback) broke compilation with CS0126. Now returns `false`, falling back to standard placement exactly as designed.

---

## [2.25.0] — Chained Drive Shafts Touch & Snap In Extension

**Type:** MINOR — save-compatible visual/link improvement, MaritimeMeshBuilder v19 (game version 6.13.0-dev).

### Added
- **Full-cell shaft rod + gold coupling rings** (`Port_ShaftIO_F`/`_B`): collinear chained shafts now physically touch at the shared cell face, meeting ring-to-ring like a bolted flange coupling.
- **Snap-in-extension**: the ring ports are named shaft ports, so a held drive shaft magnetises straight onto the end of a placed one — effortless collinear drivelines.

---

## [2.24.0] — Engine Oxygen, AIP Loop, Universal Port Snap, Exhaust-Gas Tap & Straight Stack

**Type:** MINOR — save-compatible gameplay systems + snapping/placement fixes and MaritimeMeshBuilder v18 (game version 6.12.0-dev).

### Added
- **Engine oxygen requirement**: internal O₂ buffer (0.25 units per fuel unit) refilled through the new visible `Port_OxygenInput` air intakes on all three engine tiers from gas-pipe-fed oxygen tanks; starved engines stall cleanly with panel/screen warnings (`OxygenStarved`, `OxygenFill01`, `HasOxygen`).
- **Closed-Cycle AIP Module** (`EngineModuleKind.AirIndependentPropulsionLoop`, `removesOxygenRequirement`): chlorate oxygen candles + regenerative CO₂-scrubbed exhaust recirculation close the oxygen loop — no external air needed, +5% fuel use. Works on T1/T2/T3.
- **Exhaust-gas tap**: straight exhaust pipe carries a top `Port_ExhaustGasIO` flange; connected gas networks capture part of the stream as the new storable `GasType.ExhaustGas` and the smoke plume visibly thins (foundation for the concealed-space atmosphere sim on the Roadmap).
- **`MaritimePorts` shared registry**: one source of truth for liquid/gas/exhaust/shaft port prefixes used by the builder, the build system, pipe visuals and networks.

### Fixed
- **MGO exhaust snap** (port mounts now evaluated before the lattice-neighbour gate that big models could never pass) — snapping works from any aim point on the machine and always centres on the port's own cell; orientation follows the port's dominant axis (vertical stacks on top collectors, horizontal runs on side ports).
- **Gas pipes ⇄ exhaust pipes snap both ways**; fuel/coolant snap no longer defeated by the inflated MGO collider (Step-13 collider fit now divides out root scale); snap range widened.
- **No placement into the player or into terrain/constructs** (new world-space obstruction test on both builders) — no more player launches or grid kicks.
- **Tall machines rest on their visual bottom** when placed on top of a block (MGO sump no longer buries into armour).
- **Liquid networks bridged with the classic FluidNetwork**: classic `WaterTank`s behind any pipe run touching a liquid port/body now feed (and are filled by) engines, pumps and radiators; pipe arms aim at the real port instead of the block centre; liquid pipes snap to liquid ports only (steam moved to the gas family); liquid tanks gained `Port_LiquidIO` markers.
- **Item block properties**: engine tier auto-config no longer stomps the item-driven block name — name/mass/maxHP/currentHP all come from the `GridBlockItem` asset.

### Changed
- MaritimeMeshBuilder **v18**: straight exhaust stack (no L, no ground supports), driveshaft ground feet removed, oxygen intakes + air filters on all engines, gas-tap flange on the exhaust.
- Step 13 strictly non-destructive: colliders re-fit only on create/rebuild/missing, everything else already populate-if-new/link-if-missing.

---

## [2.23.1] — Compile Fix: WaterPipe `CellSize`

**Type:** PATCH — compile-error fix only (game version 6.11.1-dev).

### Fixed
- `CS1061` build break in `Scripts/Fluids/WaterPipe.cs`: `GridSize.CellSize()` was invoked as an extension method without `using VoxelEngine.GridSystem;` in scope. Switched both call sites to the explicit static call `VoxelEngine.GridSystem.GridSizeExt.CellSize(...)` (matches the pattern used in `VoxelEngineSetupWindow` / `PlayerInteractionTool`). No behaviour change — the 2.23.0 liquid-arm proximity visuals work exactly as delivered once the project compiles again.

---

## [2.23.0] — Ground-Safe Placement, Port Snap, Liquid Links, Torque Curve & Seizure Repair

**Type:** MINOR — save-compatible gameplay + connection fixes and MaritimeMeshBuilder v17 (game version 6.11.0-dev).

### Added
- **Free-ratio gearbox (0.25×–20×)**: typed input field + slider in the panel replaces the 20 fixed gears; `GridGearbox.SetRatio` applies live.
- **Marine-diesel torque curve** on engines (`TorqueCurveAtSpeed`: 1.18× idle → 0.58× redline) feeding real shaft torque; speed-based stress model; overstressed engines run 35% hotter.
- **Heat-seizure repair**: 100 °C seizes an engine until repaired with spare parts (subset of its recipe) under 80 °C (`NeedsRepair`, `TryRepairCriticalFailure`, prefab-authored `repairCost` filled by Step 13).
- **Ground-clearance lift**: free-standing placements raise out of the terrain using the prefab's true rotated bounds — no more MGO engine sinking into the ground.
- **Build reach 8 m → 16 m** with auto-upgrade of stale serialized values.

### Fixed
- **Maritime port snap to the actual port cell**: shafts/exhaust/gearboxes/generators/propellers magnet onto the engine's real port position (several cells out on big models) — fixes shafts spawning inside the medium engine.
- **Liquid pipes snap to liquid ports** (both ghost + final placement, grid mode agnostic), and the grid liquid network treats world-space proximity to machine bodies/liquid ports as connected — tanks really feed engines via pipes again; WaterPipe arms draw across proximity links.
- **Exhaust detection**: engines' `HasAdjacentExhaust` and the exhaust pipe's engine scan now use face neighbours OR port/body proximity — the pipe reports *venting* and emits smoke again (was stuck at "no active engines adjacent").
- **WorldInspectionHud**: the card probes every hit along the ray and skips ghosts/viewmodels, so block info shows with items/tools held; distance raised to 16 m.

### Visuals (MaritimeMeshBuilder v17)
- Exhaust Pipe → bolted flange, heat-tinted elbow intake, tapered stack with weld beads/heat bands, inner throat, rain cap, braces, red `Port_ExhaustInput`.
- Drive Shaft → pillow-block bearings, bolted end flanges, splined shaft with brass keyway, U-joint yoke, clamp collars.
- Generator → skid rails, finned stator barrel, rear fan cowl, terminal box, open front bell with **safety-yellow guard ring and visible spinning input coupling** (gold `Port_ShaftInput`).

### Manual Unity steps
1. Run **Step 13** (v17 meshes rebuild in place; repair costs fill when empty — no prefab deletion needed).
2. Run **Step 8** once to apply the "Liquid Pipe (Solid/Glass)" rename.
3. Slightly smoky legacy builds reconnect automatically (proximity detection).

---

## [2.22.1] — Animator Compile Fix (Rotate Arity)

**Type:** PATCH — compile repair only; no behaviour change (game version 6.10.1-dev).

### Fixed
- `MaritimeAnimator.SpinY` passed 5 arguments to `Transform.Rotate` (maximum is 4) — compile error CS1501 resolved; piston/crank animations build again.
- Related cleanup shipped in the same game patch: all `Object.GetInstanceID()` call sites migrated to Unity 6.4's `GetEntityId()` (BuildSystem, GridCameraBlock, GridScreenBlock, GridScreenConfigUI, WindTurbineUI, WorldStatePersistence).

### Manual Unity steps
1. Let Unity recompile — expect 0 errors, 0 warnings.
2. Place an engine and confirm pistons, crank, output shaft (and V12 SeaPump) animate.

---

## [2.22.0] — Upgrade Modules, Heat Model, 20-Speed Gearbox & v16 Engine Models

**Type:** MINOR — save-compatible feature update (game version 6.10.0-dev).

### Added
- **Engine/generator upgrade modules** (`EngineModuleItem` + Module Slots): High-Flow Turbocharger, Efficiency Tuning Chip (mandatory coolant requirement), Overclocked Fuel Injectors (dirty exhaust, +50% heat), Super-Cooler Radiator Jacket (+200% dissipation, 2 L/s fresh/sea water per jacket).
- **Live heat model** on engines and generators: knocking ≥ 90 °C (−25% fuel efficiency), critical failure ≥ 100 °C (latched shutdown until < 80 °C), radiator water feed from grid tanks.
- **Generator speed bonus**: up to +50% output near rated RPM (`MaritimeSettings.generatorSpeedBonus`).
- **20-speed bidirectional gearbox** with live gear application; the propagation job now walks a BFS parent map so either face can be the physical input and branch splits behave correctly.
- **IGridDataProvider on engines and generators** so grid screens can display their live data (RPM, torque/power, fuel ETA, heat, buffers).
- **MaritimeMeshBuilder v16** — three rebuilt engine models: Crude Inline-4 (open-air pistons, exposed valvetrain, open-frame crank), HFO V8 (glass inspection windows, valley plenum, steam-traced filters), MGO V12 (quartz viewports, gantry walkways, ladders, belt-driven SeaPump, splined PTO). Deterministic crank/shaft/piston animation with real firing orders; tier-styled exhaust smoke (pulsating puffs / thick column / clean fast stream) with module and critical-heat effects.
- Research tier 5 **Maritime Performance Tuning** + four module recipes (Assembler).

### Fixed
- Gearbox ratio changes now apply live (was stale until graph rebuild); Input RPM readout no longer inverted; legacy 2000 RPM cap auto-migrates to 10000.
- Crude engine fuel readout shows a true ETA at the current draw rate instead of misleading "buffer seconds".
- Shift-click quick transfer routes fuel items to the engine hopper and modules to module sockets.
- Maritime blocks and grid screens are now breakable by hand/tool and grind correctly: item return uses `SourceItem` with a normalized-name fallback; precision-attachment removal no longer silently fails.
- Grid batteries charge/discharge fair-share across all packs instead of one at a time.

### Manual Unity steps
1. Delete the old engine prefabs `Engine_Small/Medium/Giant_Large.prefab` under `Assets/VoxelEngine/Maritime/Prefabs/`.
2. Run **Tools > Voxel Engine > Voxel Engine Setup > Step 13** — engine prefabs are rebuilt (v16), module items/recipes/research are created. Non-destructive; repeat-safe.
3. Old saves load as-is: module sockets start empty; legacy gearboxes migrate on their first tick.

---

## [2.21.1] — Item-Port Overlay Guard, Cargo Scrolling & Dry-Land Buoyancy Fix

**Type:** PATCH — UI hardening, recipe access, and buoyancy correction; save-compatible.

### Fixed
- **Item Port overlay stacking**: opening Item Ports now stores a single active overlay reference and ignores further button presses until it is closed.
- **Shipping Container / large cargo overflow**: cargo inventory grids now sit inside a scroll view, so high-slot containers no longer spill out of the panel.
- **Dry-land buoyancy**: water probes now return a no-water sentinel when a column contains no fluid voxel, preventing hulls from continuing to float after leaving water.

### Changed
- Maritime buoyancy defaults retuned to `buoyancyGain = 1.0` and `buoyancyReserve = 1.6` for stronger-but-controlled flotation.
- Step 13 clamps existing Maritime Settings into a safer buoyancy range instead of pushing them into sky-launch values.

### Added
- Craftable **Lithium** recipe so lithium-gated batteries are actually craftable after Step 6/Power Content.

### Manual Unity steps
1. Pull/reload and let Unity compile.
2. Run **Step 6 / Build Power Content** to register the Lithium recipe if needed.
3. Run **Step 13 / Build Maritime Content** to clamp MaritimeSettings to the safer buoyancy range.
4. Test Item Ports by pressing the button repeatedly while the overlay is open — only one overlay should exist.
5. Open a Shipping Container and scroll its inventory list.
6. Drive/fly a hull out of water and confirm buoyancy stops once the blocks are no longer in water.

---

## [2.21.0] — Configurable Shipping I/O, Multi-Engine Torque Bus & Lithium Batteries

**Type:** MINOR — save-compatible logistics, mechanical-network, resource, and battery content update.

### Added
- **Shipping Container configurable item ports**: implements the same `IItemPortHost + PortConfig + ItemPortRouting` flow as chests, with one `Storage` container that can input and output through customizable faces and filters.
- **Multi-source mechanical torque bus**: each connected mechanical chain now aggregates every live engine/waterwheel source in the chain, so multiple engines feeding Rotation Transfers / Encased Chain Drives combine torque no matter which side the player used as input.
- **Lithium resource** (`Item_Lithium.asset`) for high-density power storage.
- **Giant Battery Pack** grid block generation in Step 12 with large capacity/discharge and lithium-heavy recipe.

### Changed
- Stationary **Battery** recipe now requires Lithium.
- Grid **Small Battery** and **Large Battery** recipes now require Lithium.
- Step 12 battery content now generates Small, Large, and Giant Battery Pack variants.
- Mechanical propagation no longer depends on a single chosen source node; producer torque is combined chain-wide before consumers receive RPM.

### Manual Unity steps
1. Pull/reload and let Unity compile.
2. Run **Step 12 / Build Grid System Content** to generate/update the Giant Battery Pack and lithium battery recipes.
3. Run **Step 13 / Build Maritime Content** if you want the Shipping Container prefab regenerated with chest-style port components.
4. Open a Shipping Container UI and confirm the item-port configuration widget appears below the inventory panel.
5. Build two engines into the same shaft/transfer/chain-drive network and verify connected propellers/generators receive combined power.

---

## [2.20.0] — Maritime Camera Polish, Buoyancy Reserve & Chain-Drive Logistics

**Type:** MINOR — save-compatible blocks, prefab markers, control-seat camera features, and physics tuning.

### Added
- **Helm + Ship Console camera controls**: `V` toggles first/third person and `Alt` enables free pivot/look around the active control seat.
- **Auto-oriented turbo placement**: turbochargers now rotate themselves so their local bottom sits against the engine turbo attachment point.
- **Buoyancy reserve tuning** via `MaritimeSettings.buoyancyReserve` so ship blocks displace more water and float higher.
- **Rotation Transfer** block: shaft-compatible transfer casing for straight/up/down routing; rotating the block turns the route left/right.
- **Encased Chain Drive** block: protected shaft segment with visible chain casing and named propeller mount points.
- **Shipping Container** maritime storage block: real-life container visual, 60 slots, and 5x Large Cargo Container mass capacity.
- **Propeller input cube** named `Rotation input point 0` on both shaft-driven propeller prefabs.

### Changed
- **`MaritimeMeshBuilder.Version` → 13** to rebuild propellers, chain-drive blocks, transfer blocks, and shipping containers.
- Existing Maritime Settings assets are upgraded by Step 13 to at least `buoyancyGain = 1.25` and `buoyancyReserve = 2.0`.
- Existing maritime research nodes now merge newly generated recipe unlocks instead of only populating unlocks on brand-new nodes.

### Manual Unity steps
1. Pull/reload and let Unity compile.
2. Open **Tools → Voxel Engine → Setup Wizard**.
3. Run **Step 13 / Build Maritime Content** to generate the new blocks, update research unlocks, rebuild mesh version 13 prefabs, and bump buoyancy settings.
4. Test Helm and Ship Console: press `V` to toggle camera and hold `Alt` to free-look/pivot.
5. Test turbo placement: hold a turbo and place it on a valid engine turbo attachment point; it should auto-orient with its bottom against the marker.
6. Test watercraft buoyancy after Step 13; existing MaritimeSettings assets should now have stronger displacement reserve.

---

## [2.19.0] — Cockpit-Style Helm & Ship Console Control Seats

**Type:** MINOR — save-compatible control-seat feature and interaction fix.

### Fixed
- **Helm / Ship Console right-click entry bounce** — right-click no longer instantly exits the seat on the same input frame.
- Helm and Ship Console now use the same configured **Exit Cockpit** input as `GridCockpit` instead of hardcoded/right-click exit behavior.

### Added
- **Cockpit-style auxiliary control seat registry** in `GridCockpit` so Helm and Ship Console count as active pilot seats for input blocking, hotbar hiding, and grid terminal routing.
- **External control frame support** in `GridEntity` so non-cockpit seats can drive ship movement using their own transform as the control frame.
- **Ship Console space-flight controls**: WASD/Jump/Down thrust, mouse yaw/pitch, Q/E roll, Z dampeners, P landing gear, scroll tool groups, LMB/RMB drill/weapon flow.

### Changed
- **Helm** keeps dedicated water-ship behavior: W/S throttle, A/D steer, scroll zoom, but now seats/restores the player/camera like a cockpit and clears stale space-flight input.
- **Ship Console** can now fly spaceships and still feeds maritime throttle/rudder when a `MaritimePropulsionSystem` is present.
- On-foot interaction and hotbar scrolling are suppressed while seated in a cockpit, helm, or ship console.

### Manual Unity steps
1. Pull/reload and let Unity compile.
2. Test right-click entry on both **Helm** and **Ship Control Console**.
3. Press your configured **Exit Cockpit** key to leave the seat.
4. For Ship Console spaceship testing, ensure the grid has thrusters/gyros and use WASD + mouse like a normal cockpit.

---

## [2.18.20] — Turbo Attachment-Point Placement + Compile Fix

**Type:** PATCH — compile fix, placement hardening, and visual clarity; save-compatible.

### Fixed
- **`PlayerInteractionTool.cs`** — moved grid-block-in-hand detection into `IsHoldingGridBlock()` so UI suppression no longer depends on a fragile local variable scope.

### Changed
- **Turbocharger placement** is now valid only when the target grid cell is one of an engine's named turbo attachment slots.
- **Turbo boost counting** now scans only those attachment slots, so off-slot adjacent turbos no longer grant torque.
- **Large turbo compatibility** is restricted to HFO/MGO engines; small turbos remain valid for Crude/HFO/MGO engine slots.

### Added
- **Engine turbo slot markers** named exactly `Turbo attachment point 0`, `Turbo attachment point 1`, etc.
  - Crude Engine: 1 marker.
  - Heavy Fuel Oil Engine: 2 markers.
  - MGO Engine: 4 markers.
- **`MaritimeMeshBuilder.Version` → 12** so re-running the Maritime setup rebuilds engine prefabs with cyan attachment cubes.

### Manual Unity steps
1. Pull/reload the project and let Unity recompile.
2. Open **Voxel Engine Setup** and run **Step 13 / Build Maritime Content** once to rebuild the maritime prefabs from mesh version 12.
3. Place a Crude/HFO/MGO engine and confirm the cyan cubes appear as `Turbo attachment point N` children in the hierarchy.
4. Hold a turbocharger and aim at an attachment cube/face; the ghost should appear only on valid engine slots.

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
