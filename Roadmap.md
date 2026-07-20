# 🏭 IndustrialWorld — Factory-Forward Development Roadmap

**Branch:** `Dev`
**Current Version:** `6.2.0-dev`
**Roadmap Version:** `6.2.0-dev`
**Date:** 2026-07-19
**Status:** Chunk Persistence V2 Working On — Fresh World Required
**Release Notes:** [`Changelog.md`](Changelog.md)

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
- **Crusader player identity** — every player belongs to an industrial Crusader Order, advancing from stranded initiate to armored stellar architect through heraldry, relics, armor, and engineering.
- **Combat & defense** — personal weapons, grid weapons, bombs, static missile batteries, and battles against mythical beasts.
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
| Detail-scale grid blocks | 🟡 PARTIALLY COMPLETE | Detail blocks now share the unified Grid with Structural blocks. Save/restore now covers Structural and Detail addresses; Unity validation and remaining positional network indexing are open. |
| Unified grid placement | 🟡 PARTIALLY COMPLETE | Detail/Structural placement, shape variants, screen sources, and unified Item/Gas/Liquid pipe placement/networks are validated. **5.69.0-dev** adds additive movable-grid save/restore for Structural and Detail blocks, variants, settings, and attached pipes; Thomas validated it in Unity. Remaining positional-indexing work keeps this broader area partially complete. |
| Power (wind, hydrogen) | ✅ Mature | Modular turbines are excellent |
| Fluids / gases | ✅ Good | Pipe-gated transfer in 2.20.0 |
| Building (static + tiered) | 🛠️ Working On | 3.75 m spacing, scale, rotation, and player-away Doors are Unity-validated. Size-V5 closes Foundation deck seams and adds upward/downward Stair anchors at Foundation/Floor edges and Doorway thresholds; final validation is pending. |
| Advanced Quarry | 🛠️ Working On | Unbreakable bedrock generation removed; late Tier-5 quarry uses a finite configurable 64-layer default depth |
| Sky / atmosphere / space rendering | 🟡 Basic | Needs planet-specific skies and proper space ambiance |
| Gravity / orbits | 🟡 Buggy | Player and grids sometimes fall; orbits not realistic |
| Space stations | ❌ Missing | No buildable orbital platforms |
| Conveyor logistics | 🟡 Good | Conveyors, ramps, vertical belts, chutes, contextual shape wheel, ghost previews, and persistence exist. Remaining work: pooled item entities, more chute variants, and final long-run throughput validation. |
| Grid screens / displays | ✅ COMPLETED | All sizes, live text+power states, right-click+terminal config, custom text+custom colors+border+font, visual bar charts, multi-source, live camera feeds, power gain/loss/net mode, persistence, and camera block are validated by Thomas. (5.51.3-dev) |
| Grid lighting | 🛠️ WORKING ON | Detail/Structural single and dual spotlights, Structural LED strip, premium segmented/clean LED visuals, screen data providers, configuration UI, visible chase animation, and motion activation exist. Static/placed settings persist; unified movable-grid persistence remains future work. |
| Sloped / armored grid blocks | ✅ COMPLETED | Cube, Slope, Half Block, Half Slope, Corner, and Inverted Slope variants are implemented and validated with textured meshes, collision, ghosts, and rotation. |
| Grid shape variant wheel | ✅ COMPLETED | Thomas validated all six structural variants, textured collision meshes, accurate ghosts, and the corrected radial-wheel slice alignment in 5.63.2-dev. Step 18 remains the non-destructive authoring path. |
| Player armor slots | ❌ Missing | No equipable armor system |
| Crafting / items / storage | ✅ Exists | Needs deeper recipe chains |
| Research / tech tree | ✅ Exists | Can be expanded into eras |
| UI / UX | 🛠️ Improving | Runtime crisp UI scaling, responsive machine panels, build-wheel fit, and recipe validation tooling are active; broad screen-size validation remains required. |
| Top-left world inspection overlay | 🛠️ Working On | Crosshair targets, active voxel materials, mining requirements, power, occupancy, integrity, and inventory-item hover details are implemented; Unity validation pending |
| Building Hammer wheel & placement | 🛠️ WORKING ON (Premium polish complete) | Segmented paginated donut wheel, hold-release selection, scroll pages, RMB placement, Escape exit, stair chaining, and premium procedural tier materials are implemented. **5.40.0-dev** added premium cream/off-white ring + red accents + larger center disc + hover micro-interactions to match high-fidelity reference style (non-destructive). Unity validation pending. |
| Farming | 🟡 Early | Good seed, needs integration |
| Nuclear | 🟡 Present | Could become endgame power |
| Factory logistics | 🛠️ Working On | Belts, chutes, Crusher, Electric Furnace, Assembler Mk.1–Mk.3, machine UIs, and visual animations are implemented; production-line validation and statistics are next. |
| Progression / game loop | ❌ Weak | No clear early→mid→endgame arc |
| Ruins / exploration rewards | ❌ Missing | No dead-civilization POIs or blueprint gating |
| Water simulation | 🟡 Basic | Needs realistic flow, level, and physics |
| Weather system | ❌ Missing | No storms, wind variation, planetary climate |
| Camera / trajectory tools | 🟡 Basic | Needs zoom-to-trajectory and orbit map |
| UI theming | 🟡 Early | Design system exists, needs 10+ themes and per-block overrides |
| Research UI | 🛠️ WORKING ON | Spatial pan/zoom canvas with era labels, glowing connectors, zoom controls, and bottom detail panel (5.41.0-dev) |
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
| Crusader player identity | ❌ Missing | Player faction, heraldry, armor presentation, and Order progression need implementation |
| Passive livestock | ❌ Missing | Breedable cows, sheep, and pigs need husbandry, food, and population systems |
| Mythical enemies / bosses | ❌ Missing | Griffin, Roc, Manticore, Karkadann, Ghouls, Ifrit Djinn, Leviathan, and Basilisk-class encounters are planned |
| Boss relic loot gates | ❌ Missing | Higher-tier bosses must award relics required by selected late-game research and megastructures |
| Pollution / industrial threat | ❌ Missing | Emissions do not yet contaminate regions or attract escalating enemy attacks to their source |
| Planetary ecology registry | ❌ Missing | Each planet needs themed hostile, passive, elite, and boss populations adapted to local hazards |
| Rogue space crusaders | ❌ Missing | No territorial hostile Crusader fleets currently pursue players in space |
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

## 4.6 Crusader Order, Living Fauna & Mythical Threats

### Player Identity — The Crusader Order

The player characters are **Crusaders**: armored members of an industrial Order tasked with restoring civilization, reclaiming lost worlds, defending settlements, and constructing humanity’s largest machines.

- Character silhouettes progress from rugged field armor to sealed stellar plate.
- Helmets, shields, tabards, banners, armor finishes, and heraldic colors communicate Order identity.
- Engineering remains the primary power fantasy; Crusader equipment combines forged armor, advanced machinery, relic technology, and optional holy abilities.
- Order ranks provide narrative milestones without replacing the research tree.
- Co-operative players may use different heraldry while remaining members of the same Crusader Order.

### Passive Livestock

Temperate worlds support peaceful, breedable livestock:

| Animal | Core Products | Husbandry Role |
|--------|---------------|----------------|
| **Cow** | Meat, hide, optional milk | High food yield, leather/armor material, slower breeding |
| **Sheep** | Meat, wool | Textile production, insulation, banners, moderate breeding |
| **Pig** | Meat | Efficient food production, fast breeding, high feed consumption |

Livestock rules:

- Animals require food, water, shelter, and enough enclosure space.
- Compatible adults can breed after their needs remain satisfied for a configurable period.
- Population limits prevent uncontrolled simulation growth.
- Humane harvesting and automated husbandry become optional mid-game factory systems.
- Weather, temperature, radiation, predators, and starvation can affect health and reproduction.

### Mythical Enemy Roster

#### Aerial Harassers & Skirmishers

1. **Griffin / Gryphon**
   - Heraldic lion-eagle predator.
   - Dive-bombs Crusaders, disrupts shield formations, grabs isolated targets, and attempts dangerous drop attacks.
   - Drops feathers, talons, hide, and a rare Griffin Heart used in aerial equipment.

2. **Roc / Ruc**
   - Colossal bird of prey from Middle Eastern folklore.
   - Mini-boss and environmental threat capable of carrying massive prey.
   - Wing attacks create localized dust or sandstorms, reduce visibility, and push Crusaders toward cliffs or hazards.
   - Drops Giant Pinions and a Roc Storm Core used by advanced flight and weather-control research.

#### Frontline Brutes & Heavy Hitters

3. **Manticore**
   - Persian lion-bodied predator with a humanoid face and venomous scorpion tail.
   - Fires mid-range tail spikes, attacks aggressively, and applies poison that bypasses part of heavy armor protection.
   - Drops venom glands, tail spikes, and armored hide for toxin-resistant equipment.

4. **Karkadann**
   - Massive armored horned beast from Arabic and Persian tradition.
   - Performs committed straight-line charges that trample or skewer targets.
   - Heavy frontal armor forces flanking, terrain traps, shield timing, or coordinated attacks.
   - Drops horn fragments and plated hide for high-impact armor and heavy machinery.

#### Ambushers & Spellcasters

5. **Ghouls / Ghul**
   - Fast desert and ruin-dwelling shape-shifters associated with cemeteries and the dead.
   - Burrow from sand, rubble, or ruined floors; attack from behind and swarm separated Crusaders.
   - Can feed on fallen creatures to regenerate unless interrupted.
   - Drops grave ash, corrupted bone, and rare restoration reagents.

6. **Ifrit Djinn**
   - High-tier spirit formed from smokeless fire.
   - Teleports between tactical positions, throws fireballs, and summons fire walls that separate a Crusader formation.
   - Heats heavy steel armor, creating escalating burn damage unless players cool, disengage, or use heat protection.
   - Drops an Ifrit Ember required for advanced thermal, fusion, and stellar research.

#### Epic Bosses

7. **Leviathan**
   - Biblical sea serpent and coastal/orbital-ocean boss.
   - Attacks ships and maritime platforms with crushing coils, body strikes, and boiling-water breath.
   - Encounter requires vessel repair, turret management, movement, and protection of critical ship systems.
   - Drops Leviathan Scales, a Leviathan Heart, and a unique Oceanic Relic Core.

8. **Cockatrice / Basilisk**
   - Heraldic serpent-tailed terror with petrifying gaze and corrosive attacks.
   - Players must look away, raise a shield, break line of sight, or interrupt the gaze during telegraphed phases.
   - Leaves persistent toxic and corrosive trails that reshape safe movement zones.
   - Drops a Petrified Eye, corrosive gland, and a unique Gaze Relic Core.

### Tiered Loot & Required Boss Progression

- Normal mythical enemies drop common creature materials and a small chance of specialized components.
- Elite variants drop refined organs, plated hides, magical cores, and blueprint fragments.
- Mini-bosses drop guaranteed named relic components.
- Epic bosses drop unique **Boss Relic Cores** plus the highest-tier creature materials.
- Higher-tier enemies always provide access to higher-tier loot tables; low-tier farming cannot replace boss progression.
- Selected late-game research requires proof of victory rather than research packs alone.
- The **Star Builder / Stellar Forge** and **Dyson Sphere** require multiple unique Boss Relic Cores before their final research nodes and construction stages can be completed.
- Boss relic requirements are deterministic and clearly previewed in the research UI so progression never depends on an undisclosed random drop.

### Dyson Sphere Megastructure

The player can eventually construct a **Dyson Sphere around the system’s sun**, producing an immense amount of energy for interplanetary factories and stellar engineering.

- Constructed in many orbital stages rather than as one instant recipe.
- Requires autonomous solar collectors, structural frames, heat-resistant materials, orbital logistics, and sustained construction power.
- Early stages operate as a partial solar swarm; later stages form a complete stellar power network.
- Output scales with completed coverage and the star’s luminosity.
- Energy is distributed through beam relays, orbital substations, or late-game transmission infrastructure.
- Damage, alignment failure, and interrupted logistics can reduce output without deleting completed progress.
- Final activation requires boss relic research, multi-world resources, and Architect-era technology.

### Star Builder / Stellar Forge

- A late Architect-era megastructure capable of creating or stabilizing a custom star.
- Requires relic knowledge from multiple epic bosses, immense power, exotic matter, and a completed stellar safety research chain.
- New stars must obey strict mass, luminosity, system-spacing, and resource-balance limits.
- The system is additive and cannot erase an existing inhabited star system.

### Orbital Station Building Family

The Building Hammer gains a research-locked **Orbital Station** family with a clean, modular, futuristic habitat aesthetic:

- Pressurized wall, floor, ceiling, and foundation panels.
- Curved corridor and junction modules.
- Reinforced windows and observation domes.
- Airlocks, pressure doors, maintenance hatches, and docking frames.
- Exterior armor, radiator, solar, cable, and utility attachment surfaces.
- Pieces are airtight where appropriate and integrate with life support.
- The family appears in the Hammer wheel only after **Orbital Construction** research is completed.
- The Hammer wheel uses a paginated segmented donut; mouse-wheel scrolling moves between construction pages so large orbital families do not overcrowd one ring.
- Locked pages preview their research requirement without exposing unusable pieces as selectable blocks.
- All blocks, recipes, research nodes, and prefab links are authored non-destructively through the Voxel Engine Setup workflow.

---

## 4.7 Pollution, Planetary Ecology & Territorial Threats

### Pollution Simulation

Industrial activity creates pollution that spreads outward from its source and changes local threat levels.

#### Pollution Sources

- Solid-fuel generators, combustion engines, furnaces, refineries, chemical plants, mining machines, waste overflow, damaged reactors, rockets, and heavy vehicles emit different pollution types.
- Emission is measured per simulation tick and accumulated into local world cells or chunks.
- Each source exposes current output, lifetime output, filtration, and operating-state contributions.
- Existing machine balance values remain independent; pollution is an additional data-driven stat.

#### Spread, Persistence & Cleanup

- Wind carries airborne pollution downwind and storms can spread or temporarily dilute it.
- Water and soil can retain contamination longer than open air.
- Forests, filters, scrubbers, sealed processing, cleaner fuel, and advanced Crusader technology reduce pollution.
- Dormant regions simulate pollution at a reduced tick rate.
- Pollution maps and sensors show source intensity, spread direction, local danger, and predicted thresholds.

#### Enemy Attraction

- Pollution creates an industrial scent/energy signature that hostile creatures can track back to the exact source area.
- Low pollution attracts scouts such as isolated Ghouls, scavengers, or curious predators.
- Moderate pollution creates packs, ambushes, and repeated attacks on exposed logistics.
- High pollution creates organized waves, elite mutations, flying attackers, and planet-specific siege creatures.
- Extreme pollution can awaken regional bosses or provoke territorial factions.
- More pollution increases enemy count, tier, frequency, and detection distance, with population caps and recovery cooldowns to prevent unbounded spawning.
- Destroying or filtering the source gradually lowers pressure; enemies already committed to an attack do not disappear instantly.

### Planet-Specific Hostile & Passive Ecology

Each planet owns an `EcologyProfile` defining passive creatures, predators, pollution responders, elites, bosses, resistances, loot, and spawn rules.

| Planet Theme | Passive Life | Standard Threats | Elites / Bosses |
|--------------|--------------|------------------|-----------------|
| **Temperate Home World** | Cows, sheep, pigs, deer-like grazers, small heraldic birds | Ghouls formed from fallen Crusaders, Griffins, Manticores | Griffin Matriarch, Elder Manticore |
| **Barren Moon** | Vacuum-adapted crystal mites, timid regolith burrowers | Dust Wraiths, Lunar Ghouls, armored alien stalkers | Pale Roc, Crater Devourer |
| **Ice Moon** | Aurora wisps, woolly ice grazers, shell-backed snow crawlers | Frost Wyverns, Ice Burrowers, frozen dead Crusaders | Frost Basilisk, Aurora Queen |
| **Volcanic World** | Heat-feeding ember beetles, basalt shellbacks | Ifrit Djinn, Magma Manticores, ash ghouls | Ifrit Sultan, Obsidian Karkadann |
| **Acid-Rain World** | Acid-shelled grazers, glass-wing insects, corrosion-resistant marsh walkers | Corrosive Drakes, Mire Ghouls, Acid Spitters, plated alien hunters | Corrosion Sovereign, Caustic Hydra |
| **Ocean / Coastal World** | Reef grazers, luminous shoals, gentle shell leviathans | Drowned Crusaders, siren predators, armored reef hunters | Leviathan, Abyssal Broodmother |
| **Gas-Giant Atmosphere** | Storm rays, balloon grazers, luminous cloud shoals | Ion Djinn, lightning hunters, aerial parasites | Tempest Roc, Sky Leviathan |
| **Asteroid Belt** | Ore-eating crystal mites, harmless void floaters | Void Ghouls, mining parasites, Rogue Space Crusader patrols | Rogue Crusader Grandmaster, Asteroid Maw |
| **Dead Core / Anomaly** | Rare anomaly wisps and non-hostile echo creatures | Dead Priests, fallen stellar Crusaders, reality-warped aliens | Hollow Pontiff, Relic Warden |

### Fallen Crusaders, Dead Priests & Ghouls

- Crusaders who die in heavily corrupted, polluted, irradiated, or relic-saturated regions may return as armored Ghouls.
- Dead Priests retain fragments of ritual knowledge and use corrupted support abilities, fear effects, false healing zones, and relic curses.
- Fallen Crusaders retain recognizable armor silhouettes, broken heraldry, shields, and damaged weapons.
- Higher-ranking fallen enemies drop Order insignia, corrupted relics, journals, and restoration research materials.
- Their presence connects the Crusader Order’s history directly to ruins and late-game corruption.

### Rogue Space Crusaders

- Rogue Crusader Orders control marked orbital territories, abandoned stations, asteroid claims, and forbidden relic sites.
- Patrol ships issue a warning when the player approaches a territorial boundary.
- Remaining too close, entering with weapons active, mining claimed resources, or ignoring warnings escalates hostility.
- Rogue ships pursue, flank, disable engines, board vessels, demand cargo, and retreat for reinforcements when damaged.
- Reputation, heraldry, treaties, tribute, and recovered Order records can create alternatives to combat.
- Pollution, reactor signatures, weapon discharge, and high-energy cargo increase the range at which rogue patrols detect a player ship.
- Named rogue commanders function as space bosses and award navigation keys, station blueprints, Crusader relics, and high-tier ship components.

### Planetary Boss Rules

- Every major planet theme receives at least one signature boss adapted to its gravity, weather, atmosphere, terrain, and dominant hazard.
- Harsh worlds produce stronger baseline creatures because local life has evolved or mutated to survive acid rain, vacuum, extreme cold, radiation, pressure, or volcanic heat.
- Planetary hazards remain active during boss encounters and are part of the mechanics rather than background decoration.
- Boss loot tier scales with planet danger and progression era.
- Boss Relic Cores remain guaranteed on first kill and feed the late-game research gates documented in Section 4.6.

---

## 4.8 Automated Defense, Route Planning & Jump Travel

### Base Defense Turret Network

Crusader factories require automated defenses that integrate with production and logistics rather than relying on manually spawned ammunition.

| Defense | Battlefield Role | Ammunition / Supply |
|---------|------------------|---------------------|
| **Light Gun Turret** | Fast tracking against Ghouls, wildlife, and light infantry | Box magazines and standard cartridges |
| **Heavy Ballistic Turret** | Armored creatures, vehicles, and medium flyers | Armor-piercing belts and high-caliber magazines |
| **Flamethrower Turret** | Close-range swarms, burrowers, and area denial | Pumped liquid fuel or pressurized flame canisters |
| **Mortar Turret** | Indirect fire over walls and terrain | Explosive, smoke, illumination, and specialized mortar shells |
| **Giant Shell Turret** | Bosses, siege creatures, capital targets, and fortified positions | Factory-built heavy shells delivered individually |
| **Anti-Air Turret** | Griffins, Rocs, drones, missiles, and atmospheric attackers | Proximity, fragmentation, guided, or planet-specific anti-air rounds |
| **Energy / Relic Turret** | Hazard-resistant elites and late-game threats | Charged cells, exotic capacitors, or researched relic ammunition |

#### Automated Ammunition Production

- Ammunition chains include casings, propellant, projectile cores, primers, magazines, shells, fuel, guidance components, and optional special payloads.
- Assemblers, chemical plants, foundries, and explosives facilities produce ammunition continuously.
- Provider chests, requester chests, belts, chutes, item pipes, drones, and vehicle docks replenish turret buffers automatically.
- Turrets expose minimum stock, reserve stock, accepted ammo, priority, target class, firing arc, and conserve-ammo settings.
- Empty turrets publish logistics requests and clear warnings in the production statistics UI.
- Damaged or disconnected supply lines create visible defensive weak points.

#### Planet-Specific Ammunition

- Cryogenic rounds slow heat-adapted volcanic creatures.
- Corrosion-sealed ammunition survives acid-rain worlds.
- Vacuum-rated propellant operates reliably on moons and asteroids.
- Incendiary and radiant rounds are effective against Ghouls and corrupted Crusaders.
- High-pressure naval shells are designed for Leviathan and deep-ocean encounters.
- Petrification-resistant mirror or flash ammunition can interrupt Basilisk-class gaze mechanics.
- Specialized ammunition requires research and local resources but never invalidates standard ammunition entirely.

### Grid Route Recorder & Energy Calculator

Grid ships can record, calculate, validate, and automate repeatable routes.

1. **Manual Route Calculation**
   - Select `Calculate Route To` and choose a discovered planet, moon, station, base, asteroid field, or waypoint.
   - The system reports total distance, estimated travel time, gravity wells, atmosphere segments, required thrust, expected power/fuel use, and reserve margin.
   - Calculation uses current ship mass, cargo contents, batteries, fuel, hydrogen, reactor output, engine efficiency, damage state, and selected speed profile.
   - Clear warnings explain whether the current ship can complete the route and what resource is missing.

2. **Recorded Routes**
   - A piloted journey can be recorded as waypoints, approach vectors, safe altitudes, docking actions, and speed limits.
   - Recorded paths can connect planets, stations, mining sites, and cargo docks.
   - Routes are editable, reversible, nameable, and visible on the star map.

3. **Grid Autopilot**
   - Enabling Autopilot lets a grid follow a validated route, manage cruise thrust, reserve braking power, avoid terrain, and perform configured docking approaches.
   - Autopilot pauses and alerts the player if mass, damage, power, fuel, territory, weather, or route obstruction makes the plan unsafe.
   - Cargo schedules can trigger loading, unloading, charging, refueling, and return journeys.
   - Rogue Crusader territory and hostile encounters can cause avoidance, retreat, escort requests, or player intervention.

### Coordinate Jump Drive

A late-game **Jump Drive** provides charged, coordinate-based faster-than-light travel without replacing normal engines or route planning.

- The player chooses a known destination, beacon, or safe coordinate and sees range, charge cost, mass penalty, cooldown, and arrival error before committing.
- Maximum range decreases as ship mass and cargo increase.
- The drive requires a large stored-energy charge and cannot operate while critically damaged, obstructed, inside prohibited gravity depths, or without a safe arrival volume.
- Multiple drives can combine range or reduce charge time according to research and grid configuration.
- Blind jumps carry larger arrival error and are blocked when collision safety cannot find a valid destination.
- Jump calculations include territorial warnings, stellar hazards, atmosphere restrictions, and minimum reserve power after arrival.
- Autopilot can use approved jump legs inside recorded interplanetary routes.

---

## 5. Master Roadmap

| Version | Theme | Execution Status | Scope | Manual Unity Work |
|---------|-------|------------------|-------|-------------------|
| **4.5.0** | Factory Foundations | 🛠️ **WORKING ON** | Conveyor belts, chutes, basic machines, grid lights, machine UI | Medium — prefab generation, animation clips |
| **4.6.0** | Production Lines & UI Revolution | 🛠️ **WORKING ON** | Assemblers, recipe chains, UI theme system, research UI overhaul | Medium — recipes, themes, panels |
| **4.7.0** | Power, Vehicles & Combat | 🛠️ **WORKING ON** | Engines, batteries, damage, armor slots, bombs, grid weapons, armor blocks | High — combat prefabs, physics |
| **4.8.0** | Logistics 2.0, Screens & Trajectory | 🛠️ **WORKING ON** | Trains, drones, configurable screens, trajectory camera, orbit map (`M`) | High — train track, camera rigs, panels |
| **4.9.0** | Living Worlds | 🟡 **PARTIALLY COMPLETE** | Ruins, weather, water flow, enemies, planet skies, gravity/orbit fixes | Very High — worldgen, AI, fluids, rendering |
| **5.0.0** | Orbital Expansion | 🟡 **PARTIALLY COMPLETE** | Rockets, space stations, asteroid mining, orbital cargo, space ambiance | Very High — new scene/zone system |
| **5.1.0** | Interplanetary Age | 🟡 **PARTIALLY COMPLETE** | Planetary bases, exo-resources, nuclear fission, nuclear warheads | Very High — empire dashboard |
| **5.2.0** | Architect Era | 🟡 **PARTIALLY COMPLETE** | World forge, megastructures, fusion, save schema v2 | Very High — new save format |
| **5.3.0+** | Live Ops | 🟡 **PARTIALLY COMPLETE** | Modding API, multiplayer foundations, seasonal content | TBD |

> **Audit basis (5.62.5-dev):** Active sections have current production work recorded in their detailed status tables. Later sections are marked **PARTIALLY COMPLETE** only because reusable foundations already exist (cosmos, planetary bodies, water/weather, nuclear, research, persistence, and grid systems); their named headline features and completion gates remain open. No section is promoted to **COMPLETED** without setup generation and Thomas's Unity validation.

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
| Conveyor belts | 🛠️ WORKING ON | Vertical geometry, item paths, and colliders now share the horizontal belt-surface height for precise Straight/Ramp transitions. Wheel hover now brightens the complete segment and animates its icon/label. Unity validation is pending. |
| Conveyor chutes | 🟡 PARTIALLY COMPLETE | Straight vertical transport, snapping, moving-item visuals, inventory endpoints, and save-compatible placement are validated foundations. |
| Basic machines | 🟡 PARTIALLY COMPLETE | Electric Furnace, Crusher, and three Assembler tiers exist. Crusher/Assembler have recipe-selection UIs, visual animation, centralized simulation ticks, additive buffers/progress/enabled persistence, and Unity smoke validation from Thomas; production statistics and module systems remain. |
| Storage blocks | 🟡 PARTIALLY COMPLETE | A basic chest and the wider storage system exist. The planned Wooden Crate → Iron Chest → Steel Chest → Provider/Requester progression is not complete. |
| Power pole, wire, and substation | 🟡 PARTIALLY COMPLETE | Manual wiring, poles, substations, transformers, compact LV/HV one-link connectors, and 8-link wall/foundation relays exist. Setup reruns preserve balance while adding missing links. |
| Grid/static lighting and LED strips | 🛠️ WORKING ON | Grid light, floodlight logic, static/grid LED assets, small/large spotlight variants, dual-output spotlights, large-grid LED strip, premium segmented LED visuals, right-click/grid-terminal spotlight config UI, data-type toggles, LED strip config UI, LED strip screen data sources, clean/segmented strip toggle, visible chase animation, and **5.57.0-dev** motion-activated lighting now exist. **5.69.0-dev** carries grid-attached light settings through movable-grid save/restore; Unity validation remains pending. |
| Shared Machine UI | 🟡 PARTIALLY COMPLETE | Crusher and Assembler panels now expose recipe selection, progress, power, toggles, inventory slots, scrolling, and item-port integration. Remaining work: complete unification across every machine, production statistics, and theme overrides. |
| Item entity system | 🛠️ WORKING ON | Thomas validated the **5.70.0-dev** pooled physical world-item lifecycle. **5.71.0-dev** adds a shared cross-belt conveyor-carried visual pool; Unity factory load validation remains pending. |
| Recipe registry refactor | 🟡 PARTIALLY COMPLETE | ScriptableObject crafting and machine recipes exist. Shaped/shapeless/smelting/machine unification and validation remain incomplete. |
| Centralized simulation tick | 🟡 PARTIALLY COMPLETE | Crusher and Assembler register with `SimulationTickManager`; belts, chutes, and several older machines still run per-frame updates. |
| Factory persistence | ✅ COMPLETED | Conveyor/Chute item packets, Crusher/Assembler recipe+progress+enabled, Funnel buffer+mode, all machine containers save and restore. Legacy saves compatible. (5.42.0-dev) |
| Step 5 tiered setup workflow | 🛠️ WORKING ON | Generated Size-V4 prefabs migrate to Size-V5 seamless Foundation decks and Stair anchors. Missing resources are repaired safely while custom prefabs, materials, recipes, and balance values remain preserved. Unity two-run validation is pending. |
| Step 17 setup workflow | ✅ COMPLETED | Step 17 remains non-destructive, refreshes generated visuals/colliders safely, preserves balance values, and connects upgraded Funnel/Crusher/Assembler prefabs plus contextual conveyor shape workflow. |

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

10. **Player-Scale Hammer Construction**
   - Standard construction module is 3.75 meters wide/tall, an exact 25% increase over Size-V3 so Crusaders fit comfortably through rooms and openings.
   - Foundation, Wall, Doorway, Window, Floor, Stairs, Roof, Pillar, Half Wall, and Door families each retain four material tiers.
   - Foundation neighbor sockets target a full 3.75 m center-to-center offset; wall-like top sockets remain on the true perimeter edge and all lateral roots stay level.
   - Foundation deck planks overlap subtly and extend across the full perimeter structure so no seams expose or undersized top surface remains.
   - Stairs anchor at Foundation/Floor perimeter edges and Doorway thresholds: side-face aiming descends from the selected level, while top-face aiming rises outward.
   - Doorway is an empty structural opening; Door is a separate placeable family that snaps into the Doorway center socket.
   - Each Door evaluates the interacting player's side every time it opens and swings away from that player.
   - Hammer wheel uses paginated donut pages with labels inset from the ring edge.

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

### 6.2 Version 4.6.0 — Production Lines & UI Revolution — 🛠️ WORKING ON

**Goal:** Reward the player for designing clean production lines, and make every UI feel premium and personalizable.

#### Execution Status

| Area | Status | Repository Audit |
|------|--------|------------------|
| Assembler Mk.2 / Mk.3 | ✅ COMPLETED | Mk.2 and Mk.3 exist with larger buffers, faster tier multipliers, upgraded visuals, and machine UI binding. |
| Recipe graph validation | ✅ COMPLETED | Validator and non-destructive repair pass are in place. Thomas validated the graph at 0 errors after repair. Remaining duplicate-output notes are informational/progression warnings. |
| Production-line UI | ✅ COMPLETED | Final polish pass: themed-panel tokens everywhere, entrance pop animation (0.18s scale+opacity), hover scale 1.02x + BgHover, responsive minWidth 300/280, flex wrap at 1280×720 to ultrawide, styled scrollers with production accent, micro-interactions on recipe cards and bottleneck hints, theme-aware text colors via UIThemeManager. Crusher/Assembler UIs, live Production Statistics with bottleneck/surplus hints, hideable hints, Recipe Browser dependency view, recursive chain cards, persistent graph depth/raw/method controls, method filters, method comparison, theme override, pinned recipes with copy/clear, inventory-aware material summary, missing-only filter, CSV export, batch planning, machine-count estimates, copyable plans, shopping lists, method summaries, dependency chains — all polished. |
| Advanced processing | 🟡 PARTIALLY COMPLETE | Chemical processing and oil systems exist in code, but ore washing/enrichment and tailing loops are not complete. |
| UI theme system | ✅ COMPLETED | `UIThemeDefinition` full spec, 10 enriched assets + `UIThemeDatabase`, USS variables reactive via `OnThemeChanged`, `ThemedPanel`/`ThemedDocument`, `UIThemeApplier`, Interface tab with description, preview, RGB chips, opacity/radius/glow/animation sliders, copy/import/reset — scroll-preserving rebuilds. Premium editor explanatory text removed as requested. |
| Research UI overhaul | 🛠️ WORKING ON | Spatial pan/zoom canvas with era labels, zoom controls, breathing glow on ready nodes, pulsing connector lines, bottom details panel with unlock previews, search, and Space-to-research shortcut (5.41.0-dev) |

#### New Content

1. **Assembler Mk.2 / Mk.3**
   - More inputs, faster crafting, module slots.

2. **Chemical Plant**
   - Combines fluid + item recipes.
   - Example: water + coal → oil processing early line.

3. **Ore Washing / Enrichment**
   - Byproduct system: crushed ore → washed ore + tailings.
   - Tailings can be processed or stored (pollution hook for future).

4. **Advanced Finite-Depth Quarry**
   - Late Tier-5 production research rather than an early mining shortcut.
   - Expensive Assembler recipe using large quantities of steel, gears, circuits, advanced circuits, and wire.
   - Configurable finite mining depth with a default limit of 64 voxel layers.
   - Completes cleanly when the configured depth is reached; no unbreakable bedrock material is required.
   - Range, Speed, and Efficiency upgrades improve operation without removing the depth limit.
   - Quarry UI reports current depth, maximum depth, output capacity, power demand, and completion state.

5. **Component Items**
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

### 6.3 Version 4.7.0 — Power, Vehicles & Combat — 🛠️ WORKING ON

**Goal:** Make vehicles and power feel like part of the factory, establish the player as an armored Crusader of the industrial Order, and provide the tools needed to survive a dangerous world.

#### Execution Status

| Area | Status | Repository Audit |
|------|--------|------------------|
| Grid shape variants | ✅ COMPLETED | Thomas validated all six structural meshes, textures, collision, placement ghosts, selection behavior, and corrected wheel alignment. Step 18 provides the non-destructive setup connection. |
| Unified grid placement | 🟡 PARTIALLY COMPLETE | Thomas validated Detail-on-Structural lattice placement and size-labelled content. Gas/liquid topology and screen data-source addressing now resolve both block scales on one Grid; **5.69.0-dev** persistence was Unity-validated by Thomas. Remaining positional-indexing work is open. |
| Unified grid screen sources | ✅ COMPLETED | Thomas validated the unified screen/data-source work. Detail providers use precision-safe encoded addresses while legacy Structural addresses remain compatible. |
| Unified pipe placement and networks | ✅ COMPLETED | Thomas validated existing Item/Gas/Liquid pipe Detail placement, one-to-five-cell Grid/world links, rotation-independent alignment, correct visual direction, midpoint arms, live ghost previews, stable topology, resource-safe ghosts, and wrench disconnect behavior. No duplicate pipe content was introduced. |
| Vehicle power foundations | 🟡 PARTIALLY COMPLETE | Grid batteries, solar, hydrogen engines, reactors, power accounting, docking, and multiple vehicle systems exist; the planned unified power network and full vehicle progression remain incomplete. |
| Damage, armor, weapons, and life support | 🟡 PARTIALLY COMPLETE | Basic block HP/damage and one grid weapon foundation exist. Full typed damage, player armor, pooled ballistics, hazards, airtight support, and combat content remain open. |

**Crusader identity requirements:**
- Crusader armor silhouette, sealed helmet, tabard/heraldry slots, Order banners, and rank presentation.
- Heavy armor must feel protective without removing the need to dodge mythical brute attacks, poison, heat, petrification, or magic.
- Armor Station upgrades visually and mechanically advance the Crusader from field knight to stellar knight.

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

18. **Grid Weapons & Automated Turret Defense**
    - Light gun, heavy ballistic, flamethrower, mortar, giant-shell, anti-air, missile, and late-tier energy/relic turret blocks.
    - Small and large grid variants where mass, recoil, ammunition, and power requirements allow.
    - Target-class priorities for mythical creatures, flyers, Ghouls, corrupted Crusaders, vehicles, missiles, and bosses.
    - Factory ammunition chains for cartridges, magazines, propellant, explosive shells, flame fuel, guidance parts, and special planetary ammunition.
    - Provider/requester logistics, belts, item pipes, drones, and docks automate turret replenishment.
    - Turret UI exposes reserve stock, accepted ammunition, firing arc, engagement range, priority, and conserve-ammo rules.
    - Missile launcher and railgun remain high-tier grid weapon options.

19. **Grid Building Improvements**
    - **Sloped blocks** for aerodynamic ships and rovers.
    - **Heavy armor blocks** with high health and mass.
    - **Heavy armor sloped blocks**.
    - **Half blocks**, **half slopes**, **corner pieces**, **inverted slopes**.
    - **Shape Variant Wheel**: when holding a light or heavy armor block, press a key to open the same round build wheel used by the build hammer and pick the desired shape variant.
    - Variants share the same recipe/material cost scaled by volume.
    - **Unified Grid placement:** there is one player-facing Grid, not separate small-grid and large-grid constructs. Blocks retain **Detail** (0.5 m) and **Structural** (2.5 m) physical scales. Detail blocks use a 5×5 precision lattice on Structural faces, while Structural placement on Detail construction is accepted only when support and clearance rules pass.
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

### 6.4 Version 4.8.0 — Logistics 2.0, Screens & Trajectory — 🛠️ WORKING ON

**Goal:** Solve base spaghetti with satisfying long-distance logistics, and give the player powerful camera tools for grids and space.

#### Execution Status

| Area | Status | Repository Audit |
|------|--------|------------------|
| Configurable grid screens / displays | 🛠️ WORKING ON | Text/power/data modes, multi-source selection, styling, persistence, and terminal/right-click config exist. Live camera feed rendering and premium camera prefab refresh are implemented; 5.51.2-dev fixes feed visibility, screen config layering, power gain/loss display, and camera identity. Step 19 + Unity validation pending before returning this area to completed. |
| Camera block live feed | 🛠️ WORKING ON | `GridCameraBlock` now exposes a live RenderTexture through `IGridCameraFeedProvider`; `GridScreenBlock` Camera mode applies it directly to the screen surface. Camera LED states are green when a feed is used, yellow when online and idle, and red when offline. |
| Trajectory camera / orbit tools | 🟡 PARTIALLY COMPLETE | Roadmap design exists; final trajectory/orbit-map implementation and validation remain future work. |

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

8. **Grid Route Recorder, Calculator & Autopilot**
   - Manually calculate distance, travel time, required thrust, power/fuel cost, and reserve margin to a selected body or waypoint.
   - Uses live ship mass, cargo, batteries, fuel, hydrogen, generation, efficiency, and damage state.
   - Record piloted paths between planets, stations, bases, mines, and docks.
   - Autopilot follows validated routes, avoids hazards, manages braking reserves, and performs configured cargo/charging/refueling stops.
   - Route safety reacts to weather, gravity, territory, pollution signatures, hostile encounters, and changed ship contents.

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

7. **Fauna / Flora & Livestock**
   - Passive creatures for atmosphere.
   - Breedable cows, sheep, and pigs with food, water, shelter, health, reproduction, and population limits.
   - Livestock supplies renewable meat plus hide, wool, and optional milk production chains.
   - Hostile mythical creatures occupy deep biomes, ruins, deserts, mountains, coasts, and volcanic zones.

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

11. **Mythical Enemies & Bosses**
    - **Aerial:** Griffins and Rocs disrupt formations, carry targets, and create wind hazards.
    - **Brutes:** Manticores combine venomous ranged pressure with aggression; Karkadanns use armored charges and frontal defense.
    - **Ambushers:** Ghouls burrow from terrain and regenerate from fallen creatures.
    - **Spellcasters:** Ifrit Djinn teleport, create fire walls, and heat Crusader armor.
    - **Epic bosses:** Leviathan maritime encounters and Cockatrice/Basilisk petrification encounters.
    - **Automated Drones:** remnants of the dead civilization continue patrolling selected ruins.
    - **Raider Vehicles:** occasional roaming grid vehicles attack established bases in the mid/late game.
    - Enemy AI uses sight, sound, damage events, formation disruption, telegraphed boss mechanics, and biome-specific navigation.
    - Enemy tier determines loot tier; named bosses guarantee unique Boss Relic Cores.
    - Boss relics are required for selected late-game items and research, including the Star Builder and Dyson Sphere.

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

16. **Pollution & Industrial Threat Director**
   - Chunk/cell pollution accumulation for air, soil, and water.
   - Wind-driven spread, persistence, filtration, cleanup, and reduced-rate dormant simulation.
   - Pollution source inspection, map overlays, warning thresholds, and production statistics integration.
   - Escalating source-seeking attacks: scouts → packs → elites → siege creatures → awakened regional bosses.
   - Planet Ecology Profiles choose appropriate passive life, pollution responders, enemy tiers, and bosses.

17. **Planetary Ecology & Territorial Space Factions**
   - Different hostile and passive populations on every planet theme.
   - Fallen Crusaders, Dead Priests, and corruption-created Ghouls in suitable ruins and hazard zones.
   - Rogue Space Crusader territories, warnings, pursuit, boarding, reputation, tribute, and named commander bosses.
   - Acid-rain and other extreme worlds use stronger hazard-adapted creatures and unique loot.

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

29. **Pollution Service**
    - Deterministic chunk/cell emissions, spread, decay, filtration, contamination, source attribution, and reduced-rate distant simulation.

30. **Ecology Registry**
    - ScriptableObject planet profiles containing passive species, hostile species, resistances, pollution responses, elites, bosses, loot tables, and spawn budgets.

31. **Threat Director**
    - Converts pollution, progression, biome danger, territory, recent attacks, and cooldowns into fair source-seeking enemy pressure.

32. **Territorial Space AI**
    - Rogue Crusader borders, warnings, reputation, patrol routes, pursuit, retreat, reinforcements, boarding, and commander encounters.

---

### 6.6 Version 5.0.0 — Orbital Expansion (MAJOR)

**Goal:** Make space the natural next step in the factory chain.

#### New Content

1. **Rocket Platform & Rocket Parts**
   - Build multi-stage rockets from hull, engine, fuel tank, cargo bay.
   - Crew capsule for player travel.
   - Cargo capsule for item transport.

2. **Space Stations & Orbital Station Hammer Family**
   - Buildable orbital platforms using grid blocks and a dedicated Building Hammer family.
   - Researching **Orbital Construction** adds the Orbital Station family to the round Hammer wheel.
   - Dedicated pressurized foundations, walls, floors, ceilings, curved corridors, junctions, reinforced windows, observation domes, airlocks, and docking frames.
   - Modular visual language: clean futuristic habitat panels, readable seals, structural ribs, utility channels, and premium negative space.
   - Docking ports for ships and cargo capsules.
   - Solar arrays, radiators, life support, exterior armor, and gravity ring modules.
   - Airtight pieces integrate with room pressure and oxygen simulation.
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

18. **Coordinate Jump Drive**
    - Charged faster-than-light grid block for known beacons, destinations, and validated safe coordinates.
    - Range and energy cost scale with grid mass, cargo, installed drives, damage, gravity depth, and desired accuracy.
    - Destination preview reports charge, cooldown, arrival error, obstruction, territorial risk, and reserve power.
    - Can be included as approved legs in recorded Autopilot routes.
    - Safety prevents jumps into occupied volumes, prohibited gravity depths, or destinations without a valid arrival corridor.

19. **Planetary Forge / World Builder**
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

### 6.8 Version 5.2.0 — Architect Era (MAJOR)

**Goal:** Let fully established Crusader Orders reshape stellar systems without trivializing exploration, bosses, or logistics.

#### New Content

1. **Dyson Sphere / Solar Swarm Construction**
   - Multi-stage megastructure constructed around the system sun.
   - Partial stages operate as a solar swarm before complete enclosure.
   - Produces immense scalable energy based on stellar luminosity and completed coverage.
   - Requires orbital factories, automated launches, heat-resistant materials, beam relays, and multi-world logistics.
   - Final stages require unique Boss Relic Cores and Architect-era research.

2. **Star Builder / Stellar Forge**
   - Creates or stabilizes a custom star under strict mass, luminosity, spacing, and safety limits.
   - Requires exotic matter, massive sustained power, and relic knowledge from multiple epic bosses.
   - Cannot replace or erase inhabited stellar systems.

3. **Megastructure Control UI**
   - Star-map construction overlay showing orbital lanes, coverage, delivery status, projected output, structural risk, and missing relic requirements.
   - Every stage exposes clear material and energy bottlenecks.

4. **Architect Relic Research**
   - Dedicated research chain consuming guaranteed Griffin/Roc, Ifrit, Leviathan, and Basilisk-class relics where appropriate.
   - Research UI previews which boss unlocks each requirement.
   - Relics are progression keys and are not consumed by unrelated routine crafting.

#### Code Improvements

5. **Stellar Megastructure Service**
   - Distributed stage simulation, construction scheduling, output calculation, damage state, and orbital save data.

6. **Boss Progression Registry**
   - Tracks first kills, repeat kills, guaranteed relic rewards, unlocked research gates, and multiplayer participation credit.

7. **Save Schema v2**
   - Persists custom stars, Dyson construction stages, orbital station families, boss progression, and relic-gated research.

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

12. **Top-Left World Inspection Overlay**
    - Shows the name and type of the block, machine, item, creature, vehicle part, voxel material, or world object under the crosshair.
    - Terrain inspection reads the active voxel world and displays the actual material, hardness, and required mining tier instead of the world bootstrap object name.
    - Hovering inventory items displays name, category, stack size, total mass, and tool durability.
    - Displays relevant integrity, power, operating state, inventory throughput, conveyor/chute occupancy, distance, faction, hazard, or creature disposition.
    - Uses animated fade/slide transitions and never captures pointer input.
    - Future definition interfaces can provide richer custom rows without coupling the overlay to every system.

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
6. Create grid/base defense prefabs: light gun, heavy ballistic, flamethrower, mortar, giant-shell, anti-air, missile launcher, and railgun.
7. Author ammunition items, magazines, shell/fuel recipes, special planetary ammunition, turret buffers, and automated requester/provider replenishment routes.
8. Create bomb / explosive charge / remote-detonated charge prefabs.
8. Create shape variant prefabs: slope, half block, half slope, corner, inverted slope for light and heavy armor.
9. Implement the shape variant wheel UI, reusing the build hammer wheel.
10. Improve small-grid snap and maritime grid buoyancy blocks.
11. Add damage VFX: sparks, smoke, fire, debris.
12. Set up collision damage thresholds for grids and terrain.
13. Create Crusader armor models, sealed helmets, tabards/heraldry, Order banners, rank presentation, and inventory slots (helmet, chest, legs, boots, backpack, jetpack).
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
9. Build route recording, waypoint editing, destination calculation, ship capability report, and Autopilot controls.
10. Add cargo-stop actions for docking, loading, unloading, charging, refueling, waiting, and return trips.
11. Configure input bindings for trajectory toggle, star map, route calculation, and Autopilot override.
12. **Run setup wizard step (non-destructive)**
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
9. Create passive livestock prefabs and husbandry definitions: cows, sheep, pigs, breeding needs, products, and population limits.
10. Create mythical enemy prefabs and AI profiles: Griffin, Roc, Manticore, Karkadann, Ghouls, Ifrit Djinn, Leviathan, and Cockatrice/Basilisk.
11. Author tiered enemy loot tables, guaranteed Boss Relic Cores, boss research gates, and first-kill progression records.
12. Build static missile turret and flak cannon prefabs.
13. Set up planet-specific skybox / atmosphere shaders.
14. Fix gravity for players, grids, dropped items, and projectiles.
15. Implement orbital mechanics and atmospheric drag.
16. Overhaul space ambiance: starfield, nebulae, sun glare, vacuum audio.
17. Add environmental radiation zones and heat zones to biomes.
18. Create sliding airtight door and vent prefabs.
19. Author pollution values for every emitting machine, vehicle, fuel, waste source, and cleanup system.
20. Create planet Ecology Profiles with passive mobs, hostiles, elites, bosses, hazard resistances, and loot tiers.
21. Build Fallen Crusader, Dead Priest, and Rogue Space Crusader prefabs, territories, ships, warnings, and commander encounters.
22. **Run setup wizard step (non-destructive)**
    - Step 20 for ruins, livestock, mythical enemies, pollution, ecology profiles, territorial factions, bosses, weather, water, sky, and life-support systems.

### For 5.0.0 (Orbital Expansion)

1. Build rocket parts and launch pad prefabs.
2. Implement orbital station scene/zone.
3. Add the research-locked Orbital Station family to the Building Hammer wheel.
4. Generate pressurized station foundations, walls, floors, ceilings, curved corridors, junctions, windows, domes, airlocks, docking frames, radiators, and utility attachment panels.
5. Create buildable station grid systems (docking port, gravity ring, solar array, life support).
6. Create asteroid field zone and mining ship recipes.
7. Build star map UI.
8. **Run setup wizard step (non-destructive)**
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
15. Build coordinate Jump Drive prefab, charge/range calculator, safe-arrival validation, destination UI, and Autopilot route integration.
16. Build empire dashboard UI.
17. **Run setup wizard step (non-destructive)**
    - Step 22 for planetary bases, exo-alloys, nuclear, radiation, and heat systems.

### For 5.2.0 (Architect Era)

1. Design world forge megastructure prefab.
2. Create custom planet creation UI (body type + resource signature).
3. Implement resource-signature validation rules.
4. Build staged Dyson solar-swarm and sphere construction prefabs, orbital lanes, beam relays, and control UI.
5. Build the Star Builder / Stellar Forge megastructure and stellar safety UI.
6. Author Boss Relic Core items and relic-gated Architect research nodes.
7. Add warp gate prototype prefab.
8. Finalize save schema v2 migration for boss progression, relics, custom stars, and Dyson construction stages.
9. **Run setup wizard step (non-destructive)**
   - Step 23 for world forge, Star Builder, Dyson Sphere, boss relic gates, and megastructures.

---


## 11. Added Roadmap Requirements — Save Safety, Mobility & Multiplayer

### 11.1 Player Save Safety — 🛠️ WORKING ON
- Player position must be captured while the player object, Inventory, and valid planetary surface position still exist.
- A missing player record must never be interpreted as a static block position.
- Invalid player coordinates must fall back to a safe fresh/bed spawn without overwriting the last known-good save.

### 11.2 Ice Friction — ❌ MISSING
- Ice surfaces will use low-friction physics for players, static loose blocks, and movable Grids.

### 11.3 Jetpack Families — ❌ MISSING
- Add two dedicated jetpack equipment slots.
- Hydrogen boost pack: Shift activates a boost; hydrogen-only flight remains possible with no power draw.
- Atmospheric jetpack: atmospheric propulsion role.
- Hybrid jetpack: atmospheric plus ion operation using power only.

### 11.4 Cryobeds, Offline Survival & Oxygen — ❌ MISSING
- Add static and Grid cryobed items/blocks.
- An offline player requires an active cryobed or oxygen-rich environment.
- If oxygen depletes or no valid offline-survival condition exists, the player dies.

### 11.5 Multiplayer — LAST ROADMAP MILESTONE — ❌ MISSING
- This is the final roadmap milestone after all single-player systems are complete.
- Support self-hosted server creation on Windows and Linux.
- Support LAN discovery/connection and direct connection to self-hosted servers.
- Add player teams with invitations and team-only interaction permissions for Grid blocks and static blocks.
- Add pirate and merchant NPC roles.

### 11.6 World Management, Autosaves & Item Limits — 🛠️ WORKING ON
- Add three visible autosave slots in the Saves panel, with safe load/restore flow.
- Add Edit World for non-generation settings only: world name and dropped-item limit.
- Default maximum active physical dropped items is 90 and must be configurable in both Create World and Edit World.
- Conveyor packets are separate from physical dropped items, are protected from dropped-item despawn/limits, and must be optimized independently for dense factories.
- Main-menu world cards: primary Play button; Edit and Saves controls together; Clone and a smaller Delete control stacked beside them.

## 10. Suggested Immediate Next Steps

1. **Validate pooled conveyor-carried visuals in Unity** under dense factory throughput, belt removal/rebuild, and world reload.
2. **Continue Factory Foundations performance work** with broader centralized simulation ticking.
3. **Complete remaining unified Grid positional indexing** where legacy systems still read Structural-only coordinates.
4. **Keep UI fit validation active** at 1280×720, 1366×768, 1920×1080, and ultrawide resolutions.

---
