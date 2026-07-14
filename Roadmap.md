# 🏭 IndustrialWorld — Factory-Forward Development Roadmap

**Branch:** `Dev`
**Current Version:** `5.1.11-dev`
**Roadmap Version:** `5.1.11-dev`
**Date:** 2026-07-14
**Status:** Active Implementation — Adaptive Corners, Strict Lane Isolation & Chute Snapping

---

## 1. Executive Vision

IndustrialWorld is already a rich engineering sandbox: voxel terrain, planet gravity, grid-based vehicles/ships, modular wind power, fluids, gases, crafting, and a sleek premium UI. The next evolution is to make the **factory loop** the heart of the experience — without losing the freedom of building, exploration, and vehicular engineering.

We want the player to feel:

1. **Curious** — *“What can I build next?”*
2. **Smart** — *“If I wire this like that, production doubles.”*
3. **Powerful** — *“My base runs itself while I explore.”*
4. **Ambitious** — *“I need titanium — that means a base on the ice moon.”*
5. **Vulnerable** — *“That storm could break my turbines…”*
6. **Intrigued** — *“What’s inside that ruined warehouse?”*
7. **Awe** — *“Look at that sky… and I can fly straight into orbit.”*
8. **Hooked** — *“Just one more research tier…”*

The design goal is a seamless blend of:

- **Voxel-crafting sandbox** — dig, build, explore a procedural planet.
- **Grid-based engineering** — assemble ships, rovers, maritime vessels, and airborne platforms block by block with deep shape customization.
- **First-person factory logistics** — conveyors, chutes, assemblers, power grids, and resource chains.
- **Survival exploration** — hostile weather, ruins of a dead civilization, hidden blueprints, dangerous fauna.
- **Combat & defense** — personal weapons, grid weapons, bombs, static missile batteries, enemy encounters.
- **Interplanetary factory expansion** — every planet and asteroid field holds unique resources, so space is a natural next step, not a distant finish line.
- **Atmospheric grandeur** — every planet has a signature sky, and space feels vast, silent, and real.

> **Rule:** No other game titles appear in shipped code, assets, or public-facing copy. Use genre descriptions only.

---

## 2. Current State Snapshot

| Domain | Status | Notes |
|--------|--------|-------|
| Voxel world / planet / gravity | ✅ Mature | Planet-aligned placement added in 4.4.0 |
| Cosmos / star system framework | 🟡 Exists | Needs planet-specific resources and interplanetary travel |
| Grid systems (ships/vehicles) | ✅ Mature | Needs lights, screens, armor, sloped blocks |
| Maritime grid | 🟡 Basic | Needs refinement and feature parity |
| Small grid | 🟡 Basic | Needs improvement and usability |
| Power (wind, hydrogen) | ✅ Mature | Modular turbines are excellent |
| Fluids / gases | ✅ Good | Pipe-gated transfer in 2.20.0 |
| Building (static + tiered) | ✅ Good | Tiered base building exists |
| Sky / atmosphere / space rendering | 🟡 Basic | Needs planet-specific skies and proper space ambiance |
| Gravity / orbits | 🟡 Buggy | Player and grids sometimes fall; orbits not realistic |
| Space stations | ❌ Missing | No buildable orbital platforms |
| Conveyor logistics | ❌ Missing | No conveyors, chutes, robots |
| Grid screens / displays | ❌ Missing | No configurable digital panels |
| Grid lighting | ❌ Missing | No flood lights or block lights |
| Sloped / armored grid blocks | ❌ Missing | Only cube blocks exist; need shape variants |
| Grid shape variant wheel | ❌ Missing | No way to switch block shape on the fly |
| Player armor slots | ❌ Missing | No equipable armor system |
| Crafting / items / storage | ✅ Exists | Needs deeper recipe chains |
| Research / tech tree | ✅ Exists | Can be expanded into eras |
| UI / UX | ✅ Premium | Design system rolling out |
| Farming | 🟡 Early | Good seed, needs integration |
| Nuclear | 🟡 Present | Could become endgame power |
| Factory logistics | ❌ Missing | No conveyors, chutes, robots |
| Progression / game loop | ❌ Weak | No clear early→mid→endgame arc |
| Ruins / exploration rewards | ❌ Missing | No dead-civilization POIs or blueprint gating |
| Water simulation | 🟡 Basic | Needs realistic flow, level, and physics |
| Weather system | ❌ Missing | No storms, wind variation, planetary climate |
| Camera / trajectory tools | 🟡 Basic | Needs zoom-to-trajectory and orbit map |
| UI theming | 🟡 Early | Design system exists, needs 10+ themes and per-block overrides |
| Research UI | 🟡 Functional | Needs visual overhaul and better UX |
| Damage / destruction | ❌ Missing | Grids and static blocks are indestructible |
:-|:--|:--|
| Weapons / combat | ❌ Missing | No swords, guns, missiles, turrets |
| Radiation system | ❌ Missing | No reactor radiation, waste, or hazmat protection |
| Heat system | ❌ Missing | No atmospheric entry heat, block heat tolerance, heatshields |
| Oxygen / life support | ❌ Missing | No underwater/space suffocation, helmets, tanks |
| Airtight systems | ❌ Missing | No airtight doors, vents, or pressurized rooms |
| Fall damage | ❌ Missing | Player takes no damage from falls |
| Painting / finishes | ❌ Missing | No block painting or material finishes |
| Armor crafting/upgrades | 🟡 Early | Needs armor station, jetpack, hazmat, heat/oxygen upgrades |
| Enemies / hazards | ❌ Missing | World feels safe, low tension |
| Narrative / context | ❌ Missing | Player lacks long-term purpose |
| Multiplayer | ❌ Missing | Future consideration |

---

## 3. Core Design Pillars (From README, Applied)

1. **Simplicity First** — Each new system must be understandable in 60 seconds.
2. **Sleek Aesthetics** — Factory blocks should look like premium industrial hardware.
3. **Production Value** — Every machine must have animation, sound, status lights, and “juice.”

Additional gameplay pillars for the factory pivot:

4. **Tangible Resources** — Ores have physical form. Items travel on belts. Fluids move through pipes.
5. **Composable Complexity** — Simple parts combine into surprising emergent systems.
6. **Meaningful Progression** — Every research tier unlocks a new verb, not just a bigger number.

---

## 4. The Progression Curve

A great factory game gives the player a new tool just as the old one starts feeling slow. We structure progress in **eras**. Each era ends with a “logistics problem” that the next era solves.

```
Era 0: Stranded          (hand-craft, gather, survive weather & wildlife)
Era 1: Mechanized        (basic machines, power, smelting, first ruins)
Era 2: Automated         (conveyors, chutes, assembler lines, defense)
Era 3: Industrial        (trains, drones, chemical plants, grid weapons)
Era 4: Orbital           (rockets, space stations, asteroid mining, off-world outposts)
Era 5: Interplanetary    (cargo rockets, planetary bases, rare resources, nuclear warheads)
Era 6: Transcendent      (matter conversion, megastructures, fusion)
Era 7: Architect          (forge custom planets, reshape the star system)
```

### Era Transition Feel

| Era | Player Says | New Verbs |
|-----|-------------|-----------|
| 0 | *“I need iron, and that storm is getting close.”* | Mine, chop, hand-craft, build shelter, scout ruins |
| 1 | *“Smelting by hand is slow.”* | Furnaces, wind power, water pumps, repair turbine |
| 2 | *“I want this to run while I explore.”* | Conveyors, chutes, assemblers, storage, turrets |
| 3 | *“My base is spaghetti — and raiders are coming.”* | Trains, drone ports, logistics networks, grid weapons, shape variants |
| 4 | *“I need resources I can’t find here.”* | Rockets, orbital stations, asteroid mining |
| 5 | *“My factory spans multiple worlds.”* | Interplanetary cargo, planetary bases, rare alloys |
| 6 | *“Can I automate everything?”* | Fusion, matter printers, mega-projects |
| 7 | *“Can I build my own world?”* | Planetary forge, custom resource worlds, warp gates |

---

## 4.5 Multi-Planet Resource Philosophy

Space is **not the finish line** — it is the factory’s next frontier. The player should reach orbit not because the tech tree says so, but because the home planet no longer supplies a required resource.

### Core Rule

> **No single planet contains everything.**

Every body in the star system has a unique industrial identity.

### Planet / Body Archetypes

| Body Type | Identity | Sky / Atmosphere | Typical Resources | Factory Purpose |
|-----------|----------|------------------|-------------------|-----------------|
| **Temperate Home World** | Starting planet | Blue sky, white clouds, orange sunsets | Iron, copper, coal, water, biomass | Early base, basic components |
| **Barren Moon** | Low gravity, no atmosphere | Black sky, sharp shadows, star-filled | Titanium, silicon, helium-3 traces | Lightweight alloys, solar cells |
| **Ice Moon** | Extreme cold, sub-surface ocean | Pale auroras, thin haze, bright rings | Water ice, rare gases, cryo fluids | Coolants, hydrogen, life support |
| **Volcanic Planet** | High heat, toxic atmosphere | Ash-orange sky, lightning, glowing horizon | Sulfur, nickel, tungsten, uranium | High-temp alloys, nuclear fuel |
| **Gas Giant Atmosphere** | Cannot land, orbit only | Banded giant planet sky, storms above | Hydrogen, deuterium, helium-3 | Fuel, fusion research |
| **Asteroid Belt** | Zero gravity, scattered rocks | Pitch black, distant sun, dusty haze | Platinum, rare earths, ice chunks | High-end electronics, propellant |
| **Dead Core / Anomaly** | Late-game only | Unsettling chromatic sky, no sun | Exotic matter, ancient alloys | Megastructures, fusion, endgame |

### Why This Works

1. **Natural motivation** — the player wants titanium; the game says *“build a rocket.”*
2. **Specialized outposts** — each world becomes a themed factory district.
3. **Logistics problems** — moving ore between worlds is a fun engineering challenge.
4. **No artificial gates** — progression is driven by geography, not arbitrary locks.
5. **Replayability** — starting planet traits can vary per save.

### Resource Distribution Tiers

| Tier | Availability | Examples |
|------|--------------|----------|
| **Common** | Home planet + most bodies | Iron, stone, coal, water |
| **Planetary** | Biome-locked on home world | Uranium in wastelands, lithium in salt flats |
| **Interplanetary** | Only on specific planets/moons | Titanium on barren moon, tungsten on volcanic world |
| **Asteroidal** | Only in asteroid belts | Platinum, rare earths, exotic isotopes |
| **Exotic** | Late-game anomalies / crafted | Antimatter, ancient alloys, stabilized void matter |

---

## 5. Master Roadmap

| Version | Theme | Scope | Manual Unity Work |
|---------|-------|-------|-------------------|
| **4.5.0** | Factory Foundations | Conveyor belts, chutes, basic machines, grid lights, machine UI | Medium — prefab generation, animation clips |
| **4.6.0** | Production Lines & UI Revolution | Assemblers, recipe chains, UI theme system, research UI overhaul | Medium — recipes, themes, panels |
| **4.7.0** | Power, Vehicles & Combat | Engines, batteries, damage, armor slots, bombs, grid weapons, armor blocks | High — combat prefabs, physics |
| **4.8.0** | Logistics 2.0, Screens & Trajectory | Trains, drones, configurable screens, trajectory camera, orbit map (`M`) | High — train track, camera rigs, panels |
| **4.9.0** | Living Worlds | Ruins, weather, water flow, enemies, planet skies, gravity/orbit fixes | Very High — worldgen, AI, fluids, rendering |
| **5.0.0** | Orbital Expansion | Rockets, space stations, asteroid mining, orbital cargo, space ambiance | Very High — new scene/zone system |
| **5.1.0** | Interplanetary Age | Planetary bases, exo-resources, nuclear fission, nuclear warheads | Very High — empire dashboard |
| **5.2.0** | Architect Era | World forge, megastructures, fusion, save schema v2 | Very High — new save format |
| **5.3.0+** | Live Ops | Modding API, multiplayer foundations, seasonal content | TBD |

### 5.1 Execution Status Convention

| Marker | Meaning |
|--------|---------|
| 🛠️ **WORKING ON** | Active implementation is the current team focus. |
| 🟡 **PARTIALLY COMPLETE** | Some production code or content exists, but one or more roadmap requirements or validation gates remain open. |
| ✅ **COMPLETED** | The complete scoped section is implemented, generated through the required setup step, validated in Unity, and documented. |

Statuses are evidence-based and move forward only after code/content review and Unity validation. A section may remain **PARTIALLY COMPLETE** even when its core script exists if variants, persistence, setup automation, UX, or verification are still outstanding.

---

## 6. Detailed Feature Breakdown

### 6.1 Version 4.5.0 — Factory Foundations — 🛠️ WORKING ON

**Goal:** Make the player feel the factory fantasy within the first hour.

#### Execution Status

| Area | Status | Repository Audit |
|------|--------|------------------|
| Conveyor belts | 🛠️ WORKING ON | Live socket validation and tighter tolerances isolate parallel lanes even after topology changes. Corners now infer any unambiguous input/output pair and render a smooth supported curve with rollers, rails, and an exit arrow. Authored ramp and vertical variants remain. |
| Conveyor chutes | 🛠️ WORKING ON | Chutes transfer through compatible vertical endpoints and now snap beneath conveyors, onto configured top/bottom item ports, and above or below existing chutes. Corner and spiral authored variants remain. |
| Basic machines | 🟡 PARTIALLY COMPLETE | Electric Furnace, Crusher, and three Assembler tiers exist. Crusher and Assembler use the centralized simulation tick; shared UI and persistence still need completion. |
| Storage blocks | 🟡 PARTIALLY COMPLETE | A basic chest and the wider storage system exist. The planned Wooden Crate → Iron Chest → Steel Chest → Provider/Requester progression is not complete. |
| Power pole, wire, and substation | 🟡 PARTIALLY COMPLETE | Manual wiring, poles, substations, transformers, and high-voltage assets exist. Setup reruns still need full non-destructive balance preservation. |
| Grid/static lighting and LED strips | 🟡 PARTIALLY COMPLETE | Grid light, floodlight logic, and static/grid LED assets exist. Configuration UX, power validation, and complete authored variants still need verification. |
| Shared Machine UI | 🟡 PARTIALLY COMPLETE | Item Ports overlays remain mounted while live logistics changes container contents, retain Escape handling, and refresh the underlying inventory after closing. Shared inventory slots, recipe identity, transition polish, and final machine binding remain incomplete. |
| Item entity system | 🟡 PARTIALLY COMPLETE | Dropped world items exist and conveyors render carried packets. A unified pooled physical-item entity lifecycle is not complete. |
| Recipe registry refactor | 🟡 PARTIALLY COMPLETE | ScriptableObject crafting and machine recipes exist. Shaped/shapeless/smelting/machine unification and validation remain incomplete. |
| Centralized simulation tick | 🟡 PARTIALLY COMPLETE | Crusher and Assembler register with `SimulationTickManager`; belts, chutes, and several older machines still run per-frame updates. |
| Factory persistence | 🟡 PARTIALLY COMPLETE | Placed blocks and selected containers persist. Belt/chute contents plus Crusher/Assembler buffers and progress are not yet serialized. |
| Step 17 setup workflow | ✅ COMPLETED | Non-destructive create/repair logic preserves existing balance, visual, material, and effect tuning while merging required links. Static checks and the two-run Unity idempotency validation pass. |

> **Completion gate:** This section becomes **✅ COMPLETED** only after Step 17 is non-destructive, all listed variants are authored through the setup wizard, factory runtime state persists, and the Unity validation checklist passes.

#### New Content — 🟡 PARTIALLY COMPLETE

1. **Conveyor Belt Block**
   - Straight, corner, ramp, vertical variants.
   - Items visually travel on the belt.
   - Speed tiers: Basic → Fast → Express.
   - Snap to existing grid and to static building sockets.

2. **Conveyor Chute**
   - Drops items from one elevation to another.
   - Straight, corner, and spiral variants.
   - Items slide visually and audibly.
   - Snap to conveyors and machine outputs.

3. **Basic Machine Blocks**
   - Electric Furnace (smelts ore → ingots).
   - Crusher (stone → gravel, ore → dust for bonus yield).
   - Assembler Mk.1 (crafts components from ingots).

4. **Storage Blocks**
   - Wooden Crate → Iron Chest → Steel Chest → Provider/Requester chests.
   - Visual inventory display (items stacked inside).

5. **Power Pole & Wire System**
   - Player crafts **Wire** and runs it from power poles to machine **Cable Inputs**.
   - Generators have **Cable Outputs** that feed into the wire network.
   - Each standard power pole supports up to **6 connections** (machines, other poles, or generator outputs).
   - Subtle physical wire rendering between connected points.

6. **Electrical Substation**
   - Connects distant wire networks over **100+ meters**.
   - Acts as a relay and voltage step-up/step-down hub.
   - Required for large bases and long-range power transmission.

7. **Grid Light Block**
   - Small spotlight / floodlight for grid vehicles and bases.
   - Configurable color, intensity, range.
   - Toggle on/off via grid power state.

8. **Static Flood Light**
   - Non-grid placeable light for bases and outposts.
   - Wall-mounted and tripod variants.

9. **LED Strip**
   - Thin, flexible light strip for accent lighting.
   - Configurable color, brightness, and blink/pulse patterns.
   - Snap to grid edges and static building surfaces.

#### Improved Features — 🟡 PARTIALLY COMPLETE

6. **Machine UI**
   - Shared `MachinePanel` using UI Toolkit.
   - Shows recipe, progress bar, input/output slots, power status.
   - Animated status LEDs.

7. **Item Entity System**
   - Physical items can sit on belts, in chests, or be dropped in world.
   - Object pooling for performance.

8. **Recipe Registry Refactor**
   - ScriptableObject-driven recipes.
   - Support for shaped, shapeless, smelting, and machine recipes.

#### Code Improvements — 🟡 PARTIALLY COMPLETE

9. **IndustrialWorld.Simulation namespace**
   - Move machine, conveyor, chute, and power logic here.
   - Define `IMachine`, `IItemConsumer`, `IItemProvider`, `IPowerConsumer`, `IPowerProducer` interfaces.

10. **Tick Manager**
    - Centralized simulation tick (fixed interval) for machines, belts, fluids.
    - Avoids `Update()` spam per block.

11. **Save/Load for Item Entities**
    - Extend save schema to store belt contents and machine buffers.

---

### 6.2 Version 4.6.0 — Production Lines & UI Revolution

**Goal:** Reward the player for designing clean production lines, and make every UI feel premium and personalizable.

#### New Content

1. **Assembler Mk.2 / Mk.3**
   - More inputs, faster crafting, module slots.

2. **Chemical Plant**
   - Combines fluid + item recipes.
   - Example: water + coal → oil processing early line.

3. **Ore Washing / Enrichment**
   - Byproduct system: crushed ore → washed ore + tailings.
   - Tailings can be processed or stored (pollution hook for future).

4. **Component Items**
   - Gears, circuits, steel beams, pipes, motors, batteries.
   - Each has a distinct visual icon.

5. **Research Tiers Expansion**
   - Logistics (unlocks belts/chutes).
   - Automation (unlocks assemblers).
   - Advanced Material Processing.

6. **UI Theme System**
   - 10 built-in themes shipped with the game:
     1. Industrial Steel (default)
     2. Midnight Operator
     3. Hazard Amber
     4. Arctic Frost
     5. Bio-Luminescent
     6. Military Olive
     7. Neon Cyber
     8. Corporate Clean
     9. Rust Belt
     10. Void Black
   - Each theme defines colors, fonts, border radius, panel opacity, accent glow, and animation curves.
   - Themes are stored as `ThemeDefinition` ScriptableObjects.
   - Players switch themes in Settings → Interface.

7. **Per-Block UI Overrides**
   - Any machine, container, or grid block can override its UI theme.
   - Useful for distinguishing production zones or faction-owned blocks.
   - Override fields on the block definition: `ThemeOverride`, `AccentColorOverride`, `IconStyleOverride`.

8. **Custom Theme Editor (Runtime + Editor)**
   - Players can duplicate a built-in theme and edit every value.
   - Live preview in a dedicated UI panel.
   - Export/import theme files for sharing.

#### Improved Features

9. **Research UI Overhaul**
   - Tech tree as a spatial canvas (pan/zoom).
   - Animated unlock lines and glowing locked/unlocked nodes.
   - Show resource cost, dependencies, and unlock preview.
   - Filter by era or category.

10. **Recipe Browser**
    - In-game recipe tree showing dependencies.
    - Click a recipe → highlight required machines.

11. **Production Statistics Panel**
    - Items produced/consumed per minute.
    - Bottleneck highlighting.

#### Code Improvements

12. **Recipe Graph Validation**
    - Editor tool to detect unreachable or circular recipes.

13. **Machine Module System**
    - Modules: Speed, Efficiency, Productivity.
    - Modules are items inserted into machines.

14. **UI Theme Pipeline**
    - `UIThemeManager` loads theme ScriptableObjects and applies USS variables at runtime.
    - `ThemedPanel` base class for all panels to inherit theme changes.
    - Theme changes are reactive — no scene reload required.

---

### 6.3 Version 4.7.0 — Power, Vehicles & Combat

**Goal:** Make vehicles and power feel like part of the factory, and give the player tools to survive a dangerous world.

#### New Content

1. **Combustion Engine Block**
   - Burns fuel (coal, biofuel) for power.
   - Produces exhaust particles and heat.

2. **Electric Motor / Battery Blocks**
   - Store power for vehicles and machines.
   - Charge/discharge visual feedback.

3. **Biofuel Chain**
   - Farm crops → biomass press → biofuel.
   - Links farming into industry.

4. **Vehicle Bay / Dock**
   - Recharges rover batteries and refuels engines.
   - Transfers items between vehicle cargo and base.

5. **Solar Panel Tiers**
   - Small → Large → Tracking array.

6. **Damage System Framework**
   - Every grid block and static block has health, armor, and damage type modifiers.
   - Damage sources: ballistic, explosive, impact, fire, electrical.
   - Grid blocks deform visually and emit sparks/smoke before breaking.
   - Static terrain caves in on heavy impacts (voxel deformation).

7. **Gas Tank Explosions**
   - Damaged gas tanks explode.
   - Explosion strength scales with stored gas amount and flammability.
   - Chain reaction risk for nearby tanks.

8. **Personal Weapons**
   - Melee: wrench, sword, pickaxe upgrades.
   - Ranged: pistol, rifle, shotgun, grenade launcher.
   - Configurable fire modes, reload animations, recoil.

9. **Player Armor Slots**
   - Helmet, chestplate, leggings, boots, backpack.
   - Armor provides damage resistance, hazard protection, inventory space.
   - Visible on player model.

10. **Bombs & Explosives**
    - Timed bomb (place and run).
    - Remote-detonated charge.
    - Demolition pack for terrain/grid mining.

11. **Armor Station**
    - New crafting station for armor, armor upgrades, and jetpacks.
    - Upgrade modules: heat tolerance, radiation shielding, oxygen efficiency, mobility.

12. **Jetpack**
    - Separate inventory slot.
    - Unlocks the existing flight system.
    - Consumes fuel or hydrogen.
    - Upgradable thrust and fuel capacity at the armor station.

13. **Player Armor Upgrades**
    - Heat tolerance tiers 1–5.
    - Radiation shielding tiers 1–5.
    - Oxygen efficiency tiers 1–5.
    - Fall impact reduction tiers 1–5.

14. **Hazmat Suit & Hazmat Armor Upgrade**
    - Full hazmat suit for heavy radiation zones.
    - Hazmat upgrade module can be applied to any armor piece.

15. **Space Helmet & Oxygen Tank**
    - Helmet seals against vacuum; player can toggle visor open/closed.
    - Visor open: no oxygen use, but no pressure protection.
    - Visor closed: uses oxygen from chest tank.
    - Without helmet/tank in vacuum/underwater: rapid suffocation damage.

16. **Geiger Counter**
    - Handheld or suit-integrated tool.
    - Clicks and displays radiation level in sieverts.
    - Warns when entering dangerous zones.

17. **Painting System**
    - Painting tool item.
    - Paint any static block or grid block.
    - 15 material finishes: futuristic, metallic, rusty, industrial, carbon, chrome, matte, glossy, etc.
    - Finish is cosmetic only and preserved on save.

18. **Grid Weapons**
    - Small turret block for rovers/ships.
    - Missile launcher block.
    - Railgun block (late-tier).

19. **Grid Building Improvements**
    - **Sloped blocks** for aerodynamic ships and rovers.
    - **Heavy armor blocks** with high health and mass.
    - **Heavy armor sloped blocks**.
    - **Half blocks**, **half slopes**, **corner pieces**, **inverted slopes**.
    - **Shape Variant Wheel**: when holding a light or heavy armor block, press a key to open the same round build wheel used by the build hammer and pick the desired shape variant.
    - Variants share the same recipe/material cost scaled by volume.
    - Better snap behavior for small grids.
    - Maritime grid improvements: buoyancy, hull blocks, propellers.
    - All new blocks are authored via `Tools > Voxel Engine > Voxel Engine Setup`.

#### Improved Features

20. **Grid Power Integration**
    - Vehicles with generators contribute power when docked.
    - Power pole auto-connection visualizer.

21. **Wind Turbine Upgrades**
    - Module slots for lubricant and blade upgrades.

22. **Crash & Collision Damage**
    - Grids take damage proportional to impact force.
    - Heavy grids damage terrain; terrain damages grids at high speed.
    - Optional invulnerability timer after spawning to prevent spawn-killing.

23. **Fall Damage**
    - Player takes damage from high falls.
    - Armor upgrades reduce impact damage.

24. **Oxygen Underwater**
    - Player drowns without oxygen tank.
    - Underwater exploration requires sealed helmet and tank.

#### Code Improvements

25. **Unified Power Network**
    - Merge grid power, machine power, and vehicle power into one `PowerNetwork`.
    - Support AC/DC separation if desired.

26. **Damage Service**
    - Central `DamageSystem` handles all damage events.
    - `IDamageable` interface for blocks, grids, entities, terrain.
    - Damage events are deterministic and network-ready for future multiplayer.

27. **Ballistics & Projectile Pooling**
    - Object-pooled bullets, missiles, railgun slugs.
    - Raycast + projectile hybrid: bullets use raycast, missiles use physics bodies.

28. **Grid Block Shape Registry**
    - Support cube, slope, and heavy variants from a single block definition.
    - Non-destructive setup via Voxel Engine Setup.

29. **Life Support Service**
    - Tracks oxygen, pressure, radiation exposure, and heat for the player.
    - Modular upgrade system for armor and helmets.

30. **Painting Service**
    - Stores cosmetic finish data per block.
    - Separates visual material from block type.

---

### 6.4 Version 4.8.0 — Logistics 2.0, Screens & Trajectory

**Goal:** Solve base spaghetti with satisfying long-distance logistics, and give the player powerful camera tools for grids and space.

#### New Content

1. **Train System**
   - Straight/curved/ramp rail blocks.
   - Locomotive + cargo wagon grid vehicles.
   - Train stations with loading/unloading arms.
   - Schedule UI.

2. **Drone Ports**
   - Flying logistics drones between ports.
   - Battery-powered, recharges at port.
   - Great for vertical/supply runs.

3. **Logistic Chests**
   - Provider Chest: drones/belts pull from here.
   - Requester Chest: drones/belts fill it.
   - Buffer Chest: hybrid.

4. **Long-Distance Power Poles**
   - High-voltage transmission towers.

5. **Configurable Grid Screens / Displays**
   - Multiple sizes: 1×1, 2×2, 4×4, wide banner.
   - Display text, values, bar charts, or live camera feeds.
   - User-friendly setup: click screen → choose data source (power, inventory, speed, trajectory, camera).
   - Customizable font, color, background, border.
   - Can show information from any block on the same grid or connected network.

6. **Trajectory Camera Mode**
   - Bound to a configurable input key (default: `T`).
   - Only active when the player is piloting or editing a grid vehicle.
   - First zoom-out from first-person switches to third-person.
   - Second zoom-out with trajectory enabled draws a predicted path:
     - Velocity vector line.
     - Gravity arc.
     - Impact marker on terrain or predicted orbit.
   - Toggled in Settings → Controls → `Trajectory Camera`.

7. **Star Map / Orbit Overlay (`M`)**
   - Pressing `M` opens the system map.
   - Shows:
     - All planet and moon orbits.
     - Grid ships currently in orbit or in flight.
     - Asteroid fields.
     - Player bases and landing pads.
   - Click a body to set navigation target.
   - Optional trajectory trails for all orbiting bodies.

#### Improved Features

8. **Grid-based Vehicle Autopilot**
   - Set waypoints for rovers.
   - Auto-mine / auto-deliver loops.

9. **Map / Radar UI**
   - Shows train lines, drone routes, base zones.
   - Integrated with star map for seamless zoom from local to cosmic.

#### Code Improvements

10. **Pathfinding Service**
    - A* for trains on rail graph.
    - 3D pathfinding for drones.

11. **Trajectory Predictor**
    - Physics simulation step for grid vehicles.
    - Handles gravity wells and aerodynamic drag.
    - Caches trajectory for performance; updates on velocity change.

12. **Grid Screen Data Binding**
    - `ScreenBlock` queries any `IDataProvider` block on the grid.
    - Camera block feeds a render texture to the screen.
    - Config saved per screen.

---

### 6.5 Version 4.9.0 — Living Worlds

**Goal:** Make every planet feel alive, dangerous, and worth exploring — with ruins to loot, weather to survive, and enemies to fight.

#### New Content

1. **Ruins of a Dead Civilization**
   - Rare broken warehouses, collapsed factories, and derelict bases scattered across biomes.
   - Visual style: rusted, overgrown, damaged versions of real player blocks.
   - Loot containers hold components, fuel, and — most importantly — **damaged blueprint data cores**.

2. **Blueprint / Recipe Unlock System**
   - Some recipes are **not available at game start**.
   - Finding a rusted wind turbine nacelle in a ruin unlocks the real nacelle recipe.
   - Finding a broken gearbox unlocks the gearbox recipe.
   - Only a small, curated set of recipes are gated this way — enough to slow progress without frustration.
   - Damaged blueprints must be taken to a research station to be restored.

3. **Planet Resource Registry**
   - Each planet/moon has a fixed resource signature.
   - ScriptableObject-driven: `PlanetDefinition` with gravity, atmosphere, hazards, ores.

4. **New Ore Tiers**
   - Copper, aluminum, nickel, sulfur, uranium, titanium, tungsten, rare earths.
   - Biome-locked deposits on the home world.
   - Planet-locked deposits beyond the home world.

5. **Biome Hazards**
   - Radiation zones require protective suit.
   - Toxic atmosphere requires filters.
   - Extreme temperatures require heating/cooling modules.

6. **Caves & Resource Nodes**
   - Large, finite ore nodes.
   - Encourage outpost building.

7. **Fauna / Flora**
   - Passive creatures for atmosphere.
   - Hostile creatures in deep biomes.

8. **Environmental Radiation Zones**
   - Certain biomes and ruins emit low-level radiation.
   - Hazmat suit or radiation upgrades reduce exposure.
   - Geiger counter warns the player.

9. **Environmental Heat Zones**
   - Hot biomes and volcanic areas deal heat damage without protection.
   - Heat tolerance armor upgrades allow longer exposure.

10. **Airtight Doors and Vents**
    - Sliding futuristic doors for grid bases.
    - Airtight variants seal rooms for pressurization.
    - Vents pump oxygen in or out of sealed spaces.

11. **Enemies**
    - **Wildlife**: territorial beasts that attack if provoked.
    - **Automated Drones**: remnants of the dead civilization, patrolling ruins.
    - **Raider Vehicles**: occasional roaming grid vehicles that attack bases (mid/late game).
    - Enemy AI uses senses: sight, sound, damage events.

12. **Weather System**
   - Each planet type has its own climate profile:
     - Temperate: light rain, overcast, occasional storms.
     - Barren: dust devils, meteor showers.
     - Ice: blizzards, auroras.
     - Volcanic: ash clouds, acid rain, heat waves.
     - Gas giant moons: ion storms.
   - Weather affects:
     - Wind speed → turbine output and degradation.
     - Solar panel efficiency.
     - Player exposure/hazard levels.
     - Visibility and flight handling.

13. **Wind Turbine Degradation**
    - Generator and gearbox lose condition very slowly over time.
    - Storms increase degradation rate slightly.
    - Higher-tier parts degrade slower.
    - Broken parts can be repaired with steel plates and lubricant.

14. **Static Anti-Air / Base Defense**
    - Missile turret block for base defense.
    - Flak cannon block.
    - Requires power and ammunition.

15. **Prospecting Tools**
    - Ore detector, terrain scanner, sample drill.

#### Improved Features

16. **Realistic Water Simulation**
    - Water has volume and seeks its own level.
    - Flows downhill, fills cavities, exerts pressure.
    - Pumps can move water; dams can hold it back.
    - Visual: caustics, foam, translucency, reflections.
    - Interacts with voxel terrain (erosion optional, performance-dependent).

17. **Planet-Specific Skies & Atmospheres**
    - Each planet type gets a unique skybox / atmosphere shader.
    - Temperate worlds: blue sky, white clouds, realistic sunset gradients.
    - Barren moons: black starry sky, crisp shadows.
    - Volcanic worlds: ash-orange sky, lightning, glowing horizon.
    - Ice moons: auroras, thin haze, ring shadows.
    - Transition from sky to space is seamless and cinematic.
    - From orbit, planets render as colored spheres matching their atmosphere.

18. **Gravity & Orbit Fixes**
    - All grids, dropped items, and players experience consistent planetary gravity.
    - No more falling through the world or zero-gravity bugs on surfaces.
    - Realistic orbital mechanics: velocity + altitude = orbit.
    - Atmospheric drag slows low orbits; escape velocity possible.
    - Stable physics for landed grids and docked ships.

19. **Space Ambiance Overhaul**
    - Space is black, silent, and filled with distant stars and nebulae.
    - Nearby planets and moons are visible as proper spheres.
    - Sun glare, lens flares, and subtle dust particles.
    - Audio ducking: exterior sounds muted in vacuum.

20. **World Generation Refactor**
    - Planet-aware, biome-aware ore placement.
    - Larger, more distinct biomes.
    - Ruin placement influenced by biome and planet history.

21. **Environmental Suit System**
    - Suit modules tied to research.
    - Weather, radiation, and heat resistance are module stats.

#### Code Improvements

22. **Biome Registry**
    - ScriptableObject biome definitions with hazards, ores, flora, weather profile.

23. **Planet Generation Service**
    - Procedural planet parameters: size, gravity, atmosphere, resource density, climate.
    - Seed-based star system generation.

24. **Weather Service**
    - Deterministic weather based on planet seed and time.
    - Local weather cells that move across the planet.
    - Event-driven weather transitions.

25. **Water Simulation System**
    - Cellular-automata or shallow-water-equation based flow.
    - Chunk-based updates with LOD for distant water.
    - Save/load water state.

26. **Enemy AI Framework**
    - Behavior trees for wildlife, drones, raiders.
    - Faction system (player, wild, remnant, raiders).
    - Spawn controller tied to player progression and biome threat level.

27. **Atmosphere & Gravity Service**
    - Unified gravity field for players, grids, items, and projectiles.
    - Atmospheric density curves per planet.
    - Sky shader parameters driven by `PlanetDefinition`.

28. **Pressure & Airtight Service**
    - Detects sealed rooms using grid blocks and airtight doors.
    - Tracks oxygen level per room.
    - Vents add or remove oxygen.

---

### 6.6 Version 5.0.0 — Orbital Expansion (MAJOR)

**Goal:** Make space the natural next step in the factory chain.

#### New Content

1. **Rocket Platform & Rocket Parts**
   - Build multi-stage rockets from hull, engine, fuel tank, cargo bay.
   - Crew capsule for player travel.
   - Cargo capsule for item transport.

2. **Space Stations**
   - Buildable orbital platforms using grid blocks.
   - Dedicated station hull blocks with internal atmosphere option.
   - Docking ports for ships and cargo capsules.
   - Solar arrays, life support, and gravity ring modules.
   - Stations can be expanded into massive orbital factories.

3. **Asteroid Mining**
   - Asteroid fields accessible from orbit.
   - Specialized mining ship grids with drills and cargo.
   - Platinum, rare earths, ice chunks.

4. **Satellite Network**
   - Scan planets for resource deposits.
   - Relay power or data between worlds.

5. **Interplanetary Cargo Rocket**
   - Schedule launches between planets.
   - Carry bulk resources or rare samples.

#### Improved Features

6. **Star Map UI**
   - Visual map of the solar system.
   - Shows known resources per body.
   - Plan routes for rockets and drones.

7. **Atmospheric Entry / Landing**
   - Rockets descend to planetary surfaces.
   - Landing pads required for safe touchdown.

#### Code Improvements

8. **Scene/Zone Streaming**
   - Load planets, orbits, and asteroid fields as separate zones.
   - Persistent base state across zone transitions.

9. **Interplanetary Save Data**
   - Save orbital stations, asteroid positions, rocket schedules.
   - Requires save schema v2.

---

### 6.7 Version 5.1.0 — Interplanetary Age (MAJOR)

**Goal:** Turn the player into a multi-world industrial empire.

#### New Content

1. **Planetary Base Kits**
   - Deployable starter base modules for new worlds.
   - Includes power, life support, storage, and landing pad.

2. **Nuclear Fission — Uranium Reactor**
   - Uranium processing, fuel rods, reactors, waste.
   - High risk / high reward.
   - Unlocked by rare isotopes from volcanic worlds / asteroid belts.
   - Reactor is a large container-style block inspired by compact molten salt designs.
   - Reactor itself is shielded; radiation only leaks if radioactive waste storage overflows.

3. **Thorium Reactor & Thorium Material**
   - New late-game reactor fuel: thorium.
   - Thorium is more abundant than uranium and more efficient per rod.
   - Recipe unlocks after advanced nuclear research.
   - Produces far less radioactive waste than uranium.

4. **Radioactive Waste System**
   - Uranium reactors produce radioactive waste.
   - Thorium reactors produce a much smaller amount of lower-radioactivity waste.
   - Waste must be stored in radiation-sealed containers.
   - Overflowing waste causes radiation leakage around the reactor.

5. **Radiation-Sealed Container**
   - Block for safely storing radioactive waste.
   - Has limited capacity.
   - Must be kept near the reactor or transported to storage.

6. **Radiation Sealing Block**
   - Special grid block used to build shielded reactor rooms.
   - Reduces radiation passing through walls.
   - Required for safe large-scale nuclear power.

7. **Radiation Damage**
   - Players exposed to high radiation take damage over time.
   - Hazmat suit and radiation armor upgrades reduce exposure.
   - Geiger counter shows current exposure level.

8. **Heat System for Grids**
   - Every grid block has a heat tolerance value shown in its description.
   - Engines, thrusters, reactors, and exhaust pipes generate heat.
   - Thruster nozzles and side surfaces heat nearby blocks.
   - Maritime engines produce significant heat.
   - Blocks take damage or fail when heat tolerance is exceeded.

9. **Heatshield Block**
   - Special block with extremely high heat tolerance.
   - Used to protect grids during atmospheric entry and near reactors/thrusters.

10. **Atmospheric Entry Heat**
    - Ships entering atmosphere at high speed heat up based on velocity and atmospheric density.
    - Cockpit UI shows heat warning: green, yellow, red.
    - Ship blocks take damage if heat exceeds tolerance.
    - Heatshield blocks and shallow entry angles reduce risk.

11. **Cockpit Heat Indicator**
    - Shows current external heat in degrees.
    - Green = safe, yellow = approaching limit, red = taking damage.
    - Linked to ship thermal state and armor heat tolerance.

12. **Player Heat UI**
    - Shows player temperature in degrees.
    - Green/yellow/red indicator.
    - Excessive heat causes damage over time.
    - Heat tolerance armor upgrades raise the safe threshold.

13. **Nuclear Warheads & Heavy Ordinance**
    - Craftable nuclear warheads for grid-mounted missiles.
    - Massive blast radius and radiation zone.
    - Anti-installation weapon for late-game threats.
    - Requires secure storage and launch authorization.

15. **Exo-Alloys & Advanced Components**
    - Alloys requiring resources from multiple worlds.
    - Example: titanium + nickel + rare earths → aerospace alloy.

16. **Mass Driver / Orbital Cannon**
    - Launch cargo containers between worlds without rockets.
    - Late-game high-throughput logistics.

17. **Warp Gate Prototype**
    - Experimental travel to distant star systems.
    - Endgame expansion hook.

18. **Planetary Forge / World Builder**
    - Late-megastructure that lets the player **craft a new planet or moon**.
    - Costs an immense amount of resources and sustained gigawatts of power.
    - The player chooses the new body’s type (barren, ice, volcanic, etc.) and a **limited, non-overpowered resource signature**.
    - Resource signature rules prevent cheating:
      - Maximum one rare resource type per forged body.
      - Rare resource yield is lower than natural bodies.
      - Body size is smaller than natural equivalents.
    - Creates a permanent new zone in the star system.
    - Purely additive — does not replace exploration or trivialize scarcity.

#### Improved Features

19. **Empire Dashboard UI**
    - Overview of all bases, production rates, and cargo routes.
    - Alerts for low stock or bottlenecks on any world.

20. **Save Schema v2 Final**
    - Full persistence for planet-aligned rotation, multi-world grids, orbital cargo, empire state.

#### Code Improvements

21. **Save Migration Pipeline**
    - Automatic v1 → v2 migration.
    - Versioned save serializers.

22. **Distributed Simulation**
    - Dormant worlds simulate at reduced tick rate.
    - Active world runs full simulation.

23. **Nuclear & Radiation Service**
    - Tracks fallout zones from warheads and reactor meltdowns.
    - Radiation affects player, enemies, and crops over time.

24. **Thermal Simulation Service**
    - Tracks heat generation, dissipation, and damage for grid blocks.
    - Heat maps for thrusters, engines, reactors, exhaust pipes.
    - Atmospheric reentry heat curves.

---

## 7. Code & Architecture Improvements (Cross-Cutting)

These improvements run parallel to feature work and raise the quality floor of every release.

### 7.1 Performance

1. **Object Pooling Everywhere**
   - Items on belts, projectiles, particles, drones.

2. **Chunk Simulation Culling**
   - Machines outside player range sleep.
   - Configurable simulation radius.

3. **Burst/Job System**
   - Use Unity Jobs for belt updates, fluid networks, pathfinding.

### 7.2 Modularity

4. **Plugin Architecture**
   - Define clear interfaces: `IPowerNode`, `IItemTransport`, `IMachine`.
   - New machines should be addable without touching core code.

5. **Event Bus**
   - Replace scattered `Action` wiring with a typed event bus.
   - Reduces coupling.

### 7.3 Tooling

6. **Editor Windows**
   - Recipe validator.
   - Machine balance simulator.
   - Tech tree visualizer.

7. **Automated Build Checks**
   - Compile tests for editor scripts.
   - Scene reference validator.

### 7.4 Voxel Engine Setup Workflow (Non-Destructive)

Every prefab, recipe, item, and research node added by this roadmap must be generated through `Tools > Voxel Engine > Voxel Engine Setup`.

1. **Create if missing** — if a prefab/recipe/item/research node does not exist, the wizard creates it.
2. **Preserve user edits** — if it already exists, the wizard updates links and connections only.
3. **Never overwrite balance values** — power output, crafting cost, health, and other numeric fields are never reset.
4. **Idempotent runs** — running the setup step multiple times produces the same result.
5. **Versioned steps** — each major feature gets its own step number in the wizard.
6. **Clear console logging** — every change is reported so the developer can verify nothing was lost.

This rule applies to:
- Factory blocks (conveyors, chutes, machines, lights).
- Grid blocks (screens, armor, sloped blocks, weapons).
- Vehicles and grid weapon prefabs.
- Ruined structure prefabs.
- Recipes, items, and research nodes.

### 7.5 Quality of Life

8. **Universal Undo/Redo**
   - For building, mining, recipe changes.

9. **Tutorial System**
   - Contextual hints, not walls of text.
   - Tracks player progress.

10. **Accessibility**
    - Colorblind-friendly indicators.
    - Adjustable UI scale.
    - Subtitle support for audio cues.

11. **Theme-Agnostic UI Components**
    - All new UI panels must derive from `ThemedPanel`.
    - Hard-coded colors are forbidden; use theme tokens only.
    - Per-block overrides are optional and documented.

---

## 8. Player Experience Loop (The “Feel” Target)

To make players love the game, every session should hit this loop:

```
Discover need → Design solution → Build it → Power it → Watch it run →
Discover bottleneck → Optimize → Unlock next tier → Repeat
```

### Emotional Beats

| Phase | Emotion | How We Deliver |
|-------|---------|----------------|
| Boot | Wonder | Beautiful planet, slick menu |
| First sky | Awe | Blue atmosphere fading to starfield |
| First craft | Competence | Clear hand-crafting, tactile UI |
| First machine | Power | Animation, sound, lights |
| First belt line | Satisfaction | Items flowing, no friction |
| First automation | Pride | Base runs while exploring |
| First storm survived | Relief | Turbines spinning faster, base intact |
| First ruin found | Intrigue | Rusted blueprint core inside |
| First enemy repelled | Adrenaline | Turret fire, sparks, debris |
| First train | Scale | Long-distance connection |
| First orbit | Awe | Silent black space, curved planet below |
| First rocket | Triumph | Cinematic launch |
| First custom planet | Godlike | World forge activation |

---

## 9. Manual Unity Steps Guide

For each version, these are the high-level Unity tasks you will perform manually. Detailed per-step instructions will be provided when we implement each feature.

### For 4.5.0 (Factory Foundations)

1. **Create conveyor prefabs** using the voxel mesh builder.
   - Basic, fast, express tiers.
   - Add animated belt texture / scrolling UV material.
2. **Create conveyor chute prefabs**
   - Straight, corner, spiral variants.
   - Sliding item animation and sound.
3. **Create machine prefabs**
   - Furnace, crusher, assembler.
   - Add emissive status lights.
4. **Create grid light, static flood light, and LED strip prefabs**
   - Configurable color, range, intensity, and pulse patterns.
5. **Create power pole and electrical substation prefabs**
   - Pole with 6 connection points; substation with long-range relay logic.
6. **Create wire item and cable input/output socket prefabs**
   - Wire is crafted and placed between poles, generators, and machines.
8. **Set up ScriptableObjects**
   - `ConveyorDefinition`, `ChuteDefinition`, `MachineDefinition`, `LightDefinition`, `PowerPoleDefinition`, `WireDefinition`.
9. **Wire UI Toolkit panels**
   - `MachinePanel.uxml`, `MachinePanel.uss`.
10. **Add sounds & particles**
    - Belt hum, chute clatter, machine thump, item drop poof, wire hum.
11. **Run setup wizard step (non-destructive)**
    - Extend `VoxelEngineSetupWindow` with Step 16 for factory blocks.
    - Verify existing power values are preserved.

### For 4.6.0 (Production Lines & UI Revolution)

1. Author new recipe ScriptableObjects.
2. Create chemical plant prefab with fluid ports.
3. Add component item icons and visuals.
4. Expand research tree nodes.
5. Build recipe browser UI.
6. Create 10 `ThemeDefinition` ScriptableObjects.
7. Refactor all panels to use `ThemedPanel` base class.
8. Rebuild Research UI as a spatial canvas with pan/zoom.
9. Add custom theme editor panel.
10. **Run setup wizard step (non-destructive)**
    - Add recipes, items, and research nodes via Step 17.
    - Ensure existing recipe costs are not reset.

### For 4.7.0 (Power, Vehicles & Combat)

1. Build combustion engine and battery block prefabs.
2. Update rover/ship grid recipes to use electric motors.
3. Add biofuel chain farming machines.
4. Create vehicle bay prefab.
5. Create personal weapon prefabs (sword, pistol, rifle, shotgun, grenade launcher).
6. Create grid weapon prefabs (turret, missile launcher, railgun).
7. Create bomb / explosive charge / remote-detonated charge prefabs.
8. Create shape variant prefabs: slope, half block, half slope, corner, inverted slope for light and heavy armor.
9. Implement the shape variant wheel UI, reusing the build hammer wheel.
10. Improve small-grid snap and maritime grid buoyancy blocks.
11. Add damage VFX: sparks, smoke, fire, debris.
12. Set up collision damage thresholds for grids and terrain.
13. Create player armor models and inventory slots (helmet, chest, legs, boots, backpack, jetpack).
14. Create armor station prefab for crafting armor, upgrades, and jetpacks.
15. Create jetpack prefab with fuel slot and upgrade tiers.
16. Create hazmat suit and hazmat armor upgrade module prefabs.
17. Create space helmet and oxygen tank prefabs with visor toggle.
18. Create geiger counter item/tool.
19. Create painting tool item and 15 material finish variants.
20. Add fall damage system and oxygen underwater system.
21. **Run setup wizard step (non-destructive)**
    - Step 18 for power/vehicle/combat/armor/painting blocks.
    - Preserve existing grid power values and weapon damage.

### For 4.8.0 (Logistics 2.0, Screens & Trajectory)

1. Design rail blocks and train vehicle grid.
2. Build drone port prefab with landing pad.
3. Implement logistics chest models.
4. Create map/radar UI.
5. Build configurable screen prefabs in multiple sizes.
6. Add camera block that feeds render texture to screens.
7. Add trajectory camera rig and predicted path renderer.
8. Build star map UI with orbit lines and body labels.
9. Configure input bindings for trajectory toggle and star map.
10. **Run setup wizard step (non-destructive)**
    - Step 19 for trains, drones, screens, and trajectory blocks.

### For 4.9.0 (Living Worlds)

1. Create new biome definitions and materials.
2. Author ore deposit prefabs and nodes.
3. Add hazard zones and suit modules.
4. Update world generation settings.
5. Build ruined warehouse/factory prefabs (rusted variants of real blocks).
6. Create blueprint data core item and research restoration UI.
7. Author weather VFX and climate profiles per planet type.
8. Implement water flow materials and simulation settings.
9. Create enemy prefabs: wildlife, drones, raider vehicles.
10. Build static missile turret and flak cannon prefabs.
11. Set up planet-specific skybox / atmosphere shaders.
12. Fix gravity for players, grids, dropped items, and projectiles.
13. Implement orbital mechanics and atmospheric drag.
14. Overhaul space ambiance: starfield, nebulae, sun glare, vacuum audio.
15. Add environmental radiation zones and heat zones to biomes.
16. Create sliding airtight door and vent prefabs.
17. **Run setup wizard step (non-destructive)**
    - Step 20 for ruins, enemies, weather, water, sky, and life-support systems.

### For 5.0.0 (Orbital Expansion)

1. Build rocket parts and launch pad prefabs.
2. Implement orbital station scene/zone.
3. Create buildable space station grid blocks (hull, docking port, gravity ring, solar array).
4. Create asteroid field zone and mining ship recipes.
5. Build star map UI.
6. **Run setup wizard step (non-destructive)**
   - Step 21 for rockets, space stations, and orbital cargo.

### For 5.1.0 (Interplanetary Age)

1. Build planetary base kit prefabs.
2. Author exo-alloy recipes requiring multi-world inputs.
3. Create mass driver / orbital cannon prefab.
4. Create nuclear warhead and missile silo prefabs.
5. Create uranium reactor prefab as large container-style block.
6. Create thorium material and thorium reactor prefab.
7. Create radioactive waste item and radiation-sealed container block.
8. Create radiation sealing block prefab.
9. Create heatshield block prefab.
10. Add heat tolerance values to all grid block descriptions.
11. Implement heat generation for engines, thrusters, reactors, and exhaust pipes.
12. Build cockpit heat indicator UI.
13. Build player heat UI with green/yellow/red indicator.
14. Implement atmospheric entry heat simulation.
15. Build empire dashboard UI.
16. **Run setup wizard step (non-destructive)**
    - Step 22 for planetary bases, exo-alloys, nuclear, radiation, and heat systems.

### For 5.2.0 (Architect Era)

1. Design world forge megastructure prefab.
2. Create custom planet creation UI (body type + resource signature).
3. Implement resource-signature validation rules.
4. Add warp gate prototype prefab.
5. Finalize save schema v2 migration.
6. **Run setup wizard step (non-destructive)**
   - Step 23 for world forge and megastructures.

---

## 10. Suggested Immediate Next Steps

1. **Merge this roadmap into `Dev`** as `Roadmap.md`.
2. **Open a planning issue / discussion** for 4.5.0 conveyor design.
3. **Begin 4.5.0 implementation** with the conveyor belt system — it is the highest-impact factory feature.
4. **Create a feature branch** `feature/4.5.0-conveyor-foundation` from `Dev`.

---

## 11. Changelog

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

---

Done Thomas :)
