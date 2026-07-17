# 🏭 IndustrialWorld — Factory-Forward Development Roadmap

**Branch:** `Dev`
**Current Version:** `5.53.0-dev`
**Roadmap Version:** `5.53.0-dev`
**Date:** 2026-07-17
**Status:** Active Implementation — Large Grid Lighting + LED Strip Variants

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
| Small grid | 🟡 Basic | Needs improvement and usability |
| Power (wind, hydrogen) | ✅ Mature | Modular turbines are excellent |
| Fluids / gases | ✅ Good | Pipe-gated transfer in 2.20.0 |
| Building (static + tiered) | 🛠️ Working On | 3.75 m spacing, scale, rotation, and player-away Doors are Unity-validated. Size-V5 closes Foundation deck seams and adds upward/downward Stair anchors at Foundation/Floor edges and Doorway thresholds; final validation is pending. |
| Advanced Quarry | 🛠️ Working On | Unbreakable bedrock generation removed; late Tier-5 quarry uses a finite configurable 64-layer default depth |
| Sky / atmosphere / space rendering | 🟡 Basic | Needs planet-specific skies and proper space ambiance |
| Gravity / orbits | 🟡 Buggy | Player and grids sometimes fall; orbits not realistic |
| Space stations | ❌ Missing | No buildable orbital platforms |
| Conveyor logistics | 🟡 Good | Conveyors, ramps, vertical belts, chutes, contextual shape wheel, ghost previews, and persistence exist. Remaining work: pooled item entities, more chute variants, and final long-run throughput validation. |
| Grid screens / displays | ✅ COMPLETED | All sizes, live text+power states, right-click+terminal config, custom text+custom colors+border+font, visual bar charts, multi-source, live camera feeds, power gain/loss/net mode, persistence, and camera block are validated by Thomas. (5.51.3-dev) |
| Grid lighting | 🛠️ WORKING ON | Grid Light Block, LED strips, static lighting setup, and lighting UI foundations exist. **5.53.0-dev** adds small/large single and dual spotlight setup variants, large-grid LED strip, premium segmented LED visuals, and screen text depth hardening; Unity Step 17 validation pending. |
| Sloped / armored grid blocks | ❌ Missing | Only cube blocks exist; need shape variants |
| Grid shape variant wheel | 🟡 PARTIALLY COMPLETE | Premium radial wheel foundation complete (5.40.1-dev) with full visual parity to Hammer/Conveyor wheels. CurrentShape accessor + auto-spawn ready. Compile error in GridBuilder fixed. Shape application + authored variants next (via Setup Step 18). |
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
| Conveyor belts | 🛠️ WORKING ON | Vertical geometry, item paths, and colliders now share the horizontal belt-surface height for precise Straight/Ramp transitions. Wheel hover now brightens the complete segment and animates its icon/label. Unity validation is pending. |
| Conveyor chutes | 🟡 PARTIALLY COMPLETE | Straight vertical transport, snapping, moving-item visuals, inventory endpoints, and save-compatible placement are validated foundations. |
| Basic machines | 🟡 PARTIALLY COMPLETE | Electric Furnace, Crusher, and three Assembler tiers exist. Crusher/Assembler have recipe-selection UIs, visual animation, centralized simulation ticks, additive buffers/progress/enabled persistence, and Unity smoke validation from Thomas; production statistics and module systems remain. |
| Storage blocks | 🟡 PARTIALLY COMPLETE | A basic chest and the wider storage system exist. The planned Wooden Crate → Iron Chest → Steel Chest → Provider/Requester progression is not complete. |
| Power pole, wire, and substation | 🟡 PARTIALLY COMPLETE | Manual wiring, poles, substations, transformers, compact LV/HV one-link connectors, and 8-link wall/foundation relays exist. Setup reruns preserve balance while adding missing links. |
| Grid/static lighting and LED strips | 🛠️ WORKING ON | Grid light, floodlight logic, static/grid LED assets, small/large spotlight variants, dual-output spotlights, large-grid LED strip, and premium segmented LED visuals now exist in setup code (5.53.0-dev). Step 17 generation, persistence/config UX, and Unity validation pending. |
| Shared Machine UI | 🟡 PARTIALLY COMPLETE | Crusher and Assembler panels now expose recipe selection, progress, power, toggles, inventory slots, scrolling, and item-port integration. Remaining work: complete unification across every machine, production statistics, and theme overrides. |
| Item entity system | 🟡 PARTIALLY COMPLETE | Dropped world items exist and conveyors render carried packets. A unified pooled physical-item entity lifecycle is not complete. |
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

### 6.3 Version 4.7.0 — Power, Vehicles & Combat

**Goal:** Make vehicles and power feel like part of the factory, establish the player as an armored Crusader of the industrial Order, and provide the tools needed to survive a dangerous world.

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

## 10. Suggested Immediate Next Steps

1. **Validate `5.10.0-dev` in Unity** by opening the Recipe Graph Validator and scanning the project.
2. **Address any validator errors first** — missing output/input references and invalid counts block clean production-line progression.
3. **Continue 4.6.0 Production Lines** with the Production Statistics Panel: item/min tracking, machine throughput, and bottleneck hints.
4. **Then implement Recipe Browser dependency view** using the validated recipe graph as the data source.
5. **Keep UI fit validation active** at 1280×720, 1366×768, 1920×1080, and ultrawide resolutions before adding larger panels.

---

## 11. Changelog

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

### [5.41.0-dev] GridBuilder Compile Fix + Shape Wheel Integration Readiness

**Type:** PATCH — save-compatible bugfix (no save schema, balance, or API touch)

**Fixed:**
- GridBuilder.cs: Invalid token / type / tuple errors (CS1519, CS1031, CS8124, CS1026, CS1022) caused by misplaced closing brace in ShowGhost method — statements for collider stripping, material build, and positioning were incorrectly placed at class scope.
- Restored correct nesting and indentation so ghost rebuild logic, cleanup, and transform application execute every frame.
- GridShapeWheel.CurrentShape hook comment and GridBlockMeshBuilder remain ready for shape variants.

**Roadmap Status:**
- Grid shape variant wheel (4.7.0): **🛠️ WORKING ON** (foundation complete + compile error fixed; next step is non-destructive authored variants via Setup).

**Manual Unity Steps (correct Voxel Engine Setup workflow):**
1. Tools > Voxel Engine > Voxel Engine Setup → run **2. Spawn Player + UI in Scene** (non-destructive; ensures GridShapeWheel UIDocument at 610 is present).
2. Equip grid armor/structural block → hold Build Wheel key — verify no errors and premium shape wheel shows.
3. When authoring variants: use the next appropriate Setup step (Step 18 area) — non-destructive.

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

---

Done Thomas :)
