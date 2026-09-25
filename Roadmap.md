# 🏭 IndustrialWorld — Factory-Forward Development Roadmap

**Branch:** `Dev`  
**Current Version:** `12.39.6-dev`
**Roadmap Version:** `12.39.6-dev`
**Date:** 2026-09-25
**Status:** Working dev version.
**Release Notes:** [`Changelog.md`](Changelog.md)

### How this roadmap is written (rule for every future edit)

1. **No changelog prose in the roadmap.** A table cell or an item line carries a version tag and at
   most one clause about *what* shipped. The narrative, the reasoning and the numbers live in
   `Changelog.md` only — if a sentence here would read the same in the changelog, it does not belong
   here.
2. **One `Recently Done` block at the top of this file**, newest round first, three short lines per
   round, describing the state of the code rather than the story of reaching it. **Hard cap: five
   rounds.** When a sixth lands, the oldest entry is deleted here — its permanent home is the
   `Changelog.md` entry of the same version, so nothing is lost and this block never becomes the
   changelog by another name.
3. **Open scope stays in plain sight.** Anything deliberately deferred is written as its own open
   item with the version that deferred it named — never as a paragraph buried inside a completed row.
4. **Non-obvious balance rules are stated where they bind** (in the section they govern), so they
   survive a status row being summarised.
5. **A shipped feature is a struck-through item** with `*(version)*`, and its design section keeps a
   one-line status note pointing at the code and the setup step that authors it.

---

## 0. Recently Done

### 12.39.0-dev - Player-Built Portals, Name + Code Pairing
- **Portal system** (`PortalFrameBlock`, `PortalControllerBlock`, `PortalUI`): frames seal any closed outline up to 64x64; the controller charges, pairs by NAME + CODE, and hands ships and players to the partner's mouth. Open drain scales with area - 64x64 is megawatts.
- **Persistence** (`WorldStatePersistence`): portal name, code, charge and cooldown on the placed block; never restores open.
- **Setup** (`VoxelEngineSetupWindow`): Step 99 authors frame/controller prefabs, static block items, expensive recipes, Stable Portals research (tier 8 behind Warp Gate).

### 12.38.0-dev - Warp Gate Prototype
- **Gate** (`GridWarpGate`): paired fixed-structure transit — matching codes, 25 s aperture, first ship inside hops to the partner's rendezvous, collision-vetoed, at rest.
- **Setup** (`VoxelEngineSetupWindow`): Step 98 authors prefab, item, recipe, research (tier 8 behind Warp Drive).

### 12.37.0-dev - Route-Book Warp Legs
- **Auto-run** (`GridRouteAutopilot`): legs past one hop engage the drive — aim, auto-charge, jump, chain hops, cruise the rest; WARP LEGS toggle on the panel.
- **Honest refusals:** no gyros / cooldown / atmosphere / stall all cruise with a log line; pilot keys outrank everything.

### 12.36.0-dev - Warp Coil Resonator Item, Drive Upgrade Slots
- **Drive panel** (`GridWarpDrive`, `GridBlockUI`): three RESONATORS slots — the crafted Warp Coil Resonator installs on the drive, -15% spin-up each.
- **Setup** (`VoxelEngineSetupWindow`): Step 97 authors item + recipe + research link; the node unlocks the recipe (no passive ranks).

### 12.35.0-dev - Warp Coil Resonance Research, Setup Step 97
- **Research** (`GridWarpDrive`, `ResearchManager`): repeatable Warp Coil Resonance node — 15% faster spin-up per rank, read live by the drive; Coils row shows the rank.
- **Setup** (`VoxelEngineSetupWindow`): non-destructive Step 97 authors and connects the node (manual run, once).

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

*(12.21.0-dev: the star map already shows a per-planet/moon pollution readout; values stay 0% until this simulation lands and feeds the tracking snapshot.)*

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

### Engine Works — Custom Engine Builder Block

A machine that builds the engine the player specified instead of the engine a recipe offers. The
interesting part is not the UI: it is that every choice the player makes has to survive contact with
the same heat, air, fuel and mass rules the rest of the game already enforces.

1. **The parameters, and what each one really changes**
   - **Configuration** — inline, V, W, radial, boxer, opposed, turbine. Configuration is a geometry
     choice with consequences: V and W banks give more cylinders in a shorter block (more power per
     metre, wider, harder to cool on one side); radial is compact and heavy at the front; boxer is
     low and flat, which is what a hull ring actually wants; turbine is a different law entirely —
     continuous heat, no idling, and it eats air.
   - **Cylinder count** — the boring-looking dial that is the main power control, with diminishing
     returns past the point where the block can breathe.
   - **Bore and stroke** — square, overbore, oversquare. Overbore revs and makes power up high and
     drinks air; oversquare makes torque at low revs, which is what a loaded hauler wants and what a
     planing hull does not care about.
   - **Displacement** — the product of the above, shown as a number the player can see and compare,
     and the thing the intake has to feed.
   - **Intake volume (L)** — how much air the engine can drink per cycle. This is the knob that ties
     the engine to the atmosphere work: an engine built to breathe hard needs either a big pipe run
     or a lot of room, and `CombustionAirRules` then decides what it actually gets.
   - **Fuel** — gasoline, kerosene, diesel, marine gas oil, heavy fuel oil, LPG, hydrogen. The fuel
     list is deliberately the fractionation output list, so the column and the engine shop are one
     decision, not two.
   - **Compression ratio** — the risk dial: more output, more heat, and a hard ceiling set by the
     fuel's octane. Diesel at a gasoline ratio is fine; gasoline at a diesel ratio is a hole in the
     piston crown.
   - **Cooling** — air, raw water loop, or closed loop with coolant. Air is free and cannot cope with
     a big engine; water cools superbly and needs a line and an outlet; closed loop needs
     `MarineEngineCoolant` and a radiator that can dump the heat it collects.
   - **Reduction gearing and output direction** — where the power goes and how fast the shaft turns;
     also what lets a built engine drive a thruster, a wheel, a belt or a generator.
   - **Governor and idle target** — the player's answer to "how much does it burn doing nothing",
     which is the same number the route book bills as standing load.
   - **Tune intent** — economy / balanced / performance / continuous-duty, which biases the balance
     sheet and the wear rate without overriding the physical choices.

2. **The balance sheet (this is the machine's actual output)**
   - Before anything is built, the block shows what the specification means: power and torque across
     the rev band, specific fuel consumption, air demand at full throttle, heat rejection at full
     throttle, mass, size in cells, wear rate, and the tolerance it will be held to.
   - A spec that cannot work is refused with the reason named — starved of air, overheating at this
     compression with this fuel, no coolant path, intake bigger than the block's own volume, more
     power than the chosen gearing can carry — and every refusal points at the dial to turn.
   - A built engine is a real `GridBlock` afterwards: it heats itself and its neighbours, it reports
     waste heat into the room, it can be piped, gutted and rebuilt. Nothing about it is a stat sheet
     pretending to be a machine.

3. **Progression by parts, and by artefacts at the top**
   - Craft cost is a function of the specification, not a flat recipe: block, crank, camshafts or
     turbine discs, heads, liners, bearings, injection, the cooling loop and the control parts, each
     counted by how much of it this engine needs. Bigger and better means more items, and the item
     list is what makes a home-built engine feel like an engine.
   - Past the large end, the design requires **artefacts**: recovered exotic bearings, a relic
     turbine disc, a crucible part from a ruin or a boss. Not a gate for its own sake — it is the
     game's way of saying the very best crankshaft is a thing you find, then learn to copy.
   - Research gates the *dials*, not the machine: an early shop lets a player build a small inline
     four on gasoline with air cooling, and V banks, turbine geometry, high compression and closed
     loops unlock as the tech tree opens. A player who finds the parts before the theory can still
     build a crude version of it.
   - Reuse beats rebuild: an existing engine can be stripped for parts, and a stripped engine's
     blocks are worth most of what they cost. A player editing their own design should never feel
     taxed for thinking.

4. **Settled for the first round**
   - First round is the **reciprocating family plus a design template library**: inline, V, W, radial
     and boxer with cylinders, bore and stroke, intake, compression, cooling, gearing and governor, and
     a library that saves, names and hands over a design. The turbine branch waits for its own round —
     it wants its own air and heat behaviour, not a borrowed one.
   - A design is **both an item and save data**: the item is how a colony or a trader gives you one,
     the save data is how your shop remembers the seven versions you tried. A design that only lives in
     the shop would not survive being interesting.

5. **Still open**
   - Whether a built engine can be reverse-engineered back into a design (recommended: yes, slowly —
     that is the reason to go looking at wrecks).
   - Whether the turbine branch shares the heat and fuel plumbing with reciprocating engines or gets
     its own rules — it must share the *heat* rules at minimum, or it becomes a free cheat on cooling.
   - How loud this is in the world: an engine shop is a place a base is built around, and the
     artefact ceiling may need a second source to keep it from being a single-ruin dependency.

### Grid Inspector Overlay (Heat · Damage · Centre of Mass)

*(shipped in 9.37.0-dev — `Scripts/UI/GridInspectorHud.cs`: the tint pass, the hotkey ring, the
worst-block marker and the centre-of-mass ball/thrust-line markers; `GameSettings.GridInspector`
action; Setup Step 68 authors the three research nodes. Unity validation of the pass budget on
dense hulls and of the 24-block degrade readout on very large ships is still open.)*

A single togglable world overlay that lets the player read the three things they currently have to
open a panel for, right on the blocks they are looking at. It is a viewing mode, not a new HUD: it
changes what the existing block visuals mean, never what the ship does.

1. **Three modes, one hotkey cycle**
   - `HEAT`: every block tinted by how far it sits through its own heat tolerance, using the existing
     per-family tolerance data rather than a second scale. The worst plate on screen gets a marker so
     the eye lands on the problem, not on the pretty part.
   - `DAMAGE`: block integrity as a ramp from intact to lost, with the fraction of the block's own
     `maxHP` as the only input. Reads correctly on a shielded hull plate and on a glass panel.
   - `CENTRE OF MASS`: a ball at the grid's mass centre, sized by total mass and coloured by how far
     the centre sits from the thrust line. This is the view that makes ship design teachable: the
     player sees why the stern swings out when the drive is off-axis.
   - The hotkey cycles off → heat → damage → centre of mass, and is **rebindable in Settings**
     alongside the existing bindings. Each mode is also selectable from the overlay itself.

2. **Rendering rules (zero bloat)**
   - One shared overlay pass per mode, driven by per-block material property data; no per-block
     GameObjects, no per-frame allocation, no new scene objects while the overlay is off.
   - Blocks keep their own damage decals underneath: the overlay tints, it does not replace
     `BlockDamageVisual` — the two are meant to read as one picture.
   - Cost of a frame in overlay mode must be within the existing block-render budget; if it is not,
     the mode degrades to the 24 nearest blocks rather than dropping frames.

3. **Progression gate**
   - The overlay is a research unlock, not a settings toggle. The three modes are unlocked in
     sequence by their own nodes so the reader tier follows the machinery tier: damage first
     (structural awareness), heat second (engine-room awareness), centre of mass last (ship design).
   - An attempt to use the hotkey with nothing researched says why, in one line, and does nothing else.

4. **Settled**
   - One a research unlock, not a settings toggle. The three modes are unlocked in
     sequence by their own nodes so the reader tier follows the machinery tier: damage first
     (structural awareness), heat second (engine-room awareness), centre of mass last (ship design).
   - An attempt to use the hotkey with nothing researched says why, in one line, and does nothing else.

4. **Settled**
   - One feature per round, each with its own version, changelog and setup step. Order, as decided
     with the shuttle design: **waymarks + connector + auto-run first** (the next round after
     9.34.0-dev), then the inspector overlay, then the petroleum chain, then asphalt, then the
     Engine Works. The shuttle round goes first on purpose: it makea settings toggle. The three modes are unlocked in
     sequence by their own nodes so the reader tier follows the machinery tier: damage first
     (structural awareness), heat second (engine-room awareness), centre of mass last (ship design).
   - An attempt to use the hotkey with nothing researched says why, in one line, and does nothing else.

4. **Settled**
   - One feature per round, each with its own version, changelog and setup step. Order, as decided
     with the shuttle design: **waymarks + connector + auto-run first** (the next round after
     9.34.0-dev), then the inspector overlay, then the petroleum chain, then asphalt, then the
     Engine Works. The shuttle round goes first on purpose: it makes the route book *used* rather than
     merely readable, and the petroleum fractions land into a fuel system that already has somewhere
     to be spent.
   - The overlay draws on **any** grid, on wreckage and on base blocks — the interesting use is
     inspecting something the player is not standing in.
   - Three research nodes, one per mode, in the order damage, then heat, then centre of mass.

5. **Still open**
   - ~~Whether the centre-of-mass ball is drawn for an unboarded grid, and whether the thrust line is
     drawn beside it. It should be: the gap between the two is the whole point of the mode.~~ *(Settled
     in 9.37.0-dev: both are drawn — the ball rides the grid's solved mass centre for any grid, and
     the weighted thrust line is drawn beside it, because the gap between the two is the whole point
     of the mode.)*

### Grid Route Recorder & Energy Calculator

Grid ships can record, calculate, validate, and automate repeatable routes.

*(1 and 2 shipped in 9.34.0-dev — `Scripts/Navigation/GridRoute*.cs` and `RouteBook.cs`, authored by
Setup Step 65. Item 3's first line — fly the route, stop here — shipped in 9.35.0-dev with the loop
and the refuel pad; avoidance, dock approaches and cargo scheduling are still open.)*

### Grid Waymarks, Named Connectors & the Auto-Run Shuttle Loop

*(shipped in 9.35.0-dev — `Scripts/Navigation/GridWaymark.cs`, `GridConnectorBlock.cs`,
`GridRouteAutopilot.cs`, `GridConnectorUI.cs`, authored by Setup Step 66; the sections below marked
"Open" are still open)*

A ship should be able to be *told a job* rather than flown: mark the two ends, name the place it
refuels, tell it how full to come back, and let it run the trip until the numbers say stop. This is
the round that turns the route book from a readout into a schedule — and it is the round that finally
spends the thing the route book was built to hold.

1. **A destination is a waymark, not only a celestial body**
   - The book's points are today either a cosmic position or a pin to a body. A shuttle needs a third
     kind: a **named waymark** — a connector, a base block, a beacon, or a point the player pinned and
     called "the smelter". A waymark is a label, a position, and (when the thing it names sits on a
     world) the body it rides, so a waymark over a moon is still over that moon at 21:00.
   - A waymark on a *moving grid* (a shuttle's own connector, a tanker) is not frozen: it tracks the
     source block while that source exists, and freezes to its last position with a stated reason when
     the source is destroyed. Frozen is correct; a crashed lookup is not.
   - Waymarks are nameable, searchable and distance-sorted. The destination picker reads the same list
     the player maintains, so "fly to the connector" and "fly to Europa" are one interaction.

2. **The connector block: what a ship and a place swap while the ship is there**
   - A new block in the grid family, Large and Small, built on what already exists rather than beside
     it: the item flow of `GridDockingPort` (buffer container, `IItemPortHost`), the typed dynamic port
     model of the variable ports, and the `PortService` vocabulary already carried by engines — so a
     connector's flanges read as the same plumbing a player learned on a maritime engine.
   - Transfers: electric (charge toward the ship's target state of charge), hydrogen, liquid fuels
     including the petroleum fractions, water/coolant, and items.
   - **Honest engineering flag:** the grid has *no* cross-grid transfer today for power, liquid or gas
     — a docked pair is a `FixedJoint` plus item buffers only. This round must introduce that one
     shared bridge, deliberately, and every other service hangs off it. Anything else is a special case
     invented inside the shuttle.
   - A large connector offers the magnetic lock of a docking port: a locked shuttle transfers fast and
     cannot drift. A small connector is a soft capture inside a marked radius: slower, forgiving, and
     enough for a deckhouse. A loop is allowed to require the lock for high rates, and must say so on
     the panel rather than transfer at half speed and let the player guess why.
   - Rate is a function of the *supplying side* — production, buffer state, and how many things are
     drawing at once — never a hard ceiling invented in a config file: the presence-not-throughput rule
     already established for ventilation applies here too. A base that can only just feed itself makes a
     slow shuttle, and that is a scheduling problem worth having.
   - **Targets are per service and live on the ship's side of the transaction** (the ship owns the tank
     being filled): state of charge, fuel litres, hydrogen, cargo fill. That is what makes the loop
     leave: the ship departs because *its* number was met, not because the station got around to it.
   - The connector has a name the player types, and that name is what the waymark is. Two players'
     "SMELTER FEED" and "SMELTER RETURN" are two waymarks; the naming is the feature, not decoration.

3. **The loop, and what "until it runs out" means**
   - A loop is: outbound waymark, optional service stop at a named connector, return waymark, optional
     service stop, repeat. Modes: continuous round trip, one way and park, or a fixed number of runs.
   - Stop conditions the player can arm, any one of them enough: reserve low, cargo full or empty, hull
     or block damage past a threshold, a leg that has become unsafe, or someone boarding the ship.
   - `UNTIL IT RUNS OUT` is offered as an explicit, armed, confirm-once mode. It means exactly what it
     says: no reserve, the ship flies until the arithmetic refuses, and then it stops where it is and
     says why. Nobody should discover this mode by accident, and nobody should be denied it either.
   - The reservation rule is what keeps the ordinary mode from being a rescue mission: every planned leg
     reserves enough to finish the leg **and** reach a safe state afterwards (the reserve margin already
     defined in `RouteRules`). When a trip is no longer possible at current mass and stored energy, the
     shuttle completes the leg it is on, returns to the last safe waymark, and reports the reason — it
     does not strand itself in the middle of a system to prove a schedule.

4. **Autonomy, honestly scoped**
   - `GridEntity` already writes an autonomous channel: `UpdateThrust()` returns early when nobody is
     at the controls and runs `ApplyAutonomousDampenerThrust()` instead. That is the shape to extend —
     a real command channel (steer, throttle limited to the route's planned speed, brake, hold, stop at
     the waymark) — **not** a fake pilot seated in the cockpit. Faking a pilot would give an unmanned
     shuttle the pilot's camera, seat and input privileges for free, and it would be the wrong seam.
   - The autopilot may: hold the planned speed profile, brake for gravity wells and atmospheres using the
     same figures the plan refused with, stop at a waymark within its arrival envelope, decelerate hard
     when a warning appears, and yield the instant a player boards (resuming after a short settle).
   - The autopilot may not: avoid terrain, perform a dock sequence, schedule cargo operations, reroute
     around a hazard, or use a jump leg. Those remain open — this closes "fly the route and stop here",
     roadmap item 3's first line, and nothing more.
   - Autonomy is granted by the recorder block: a grid flies a loop only if it can cost one. That keeps
     the cost function and the flight controller arguing with the same numbers, which is the entire
     point of having built the book first.

5. **Persistence & the panel**
   - **Shipped as something smaller than the design asked for:** there is no `waymarks` list and no new
     registry key. A waymark's label lives on the block that answers to it (`blockName`, already
     persisted), and the live list is built by scanning sources — so a destroyed pad loses its waymark
     by ceasing to exist rather than leaving a record nobody cleans up. Additive keys that did ship:
     `SavedGrid.loops` (ends, mode, targets, armed stops) and `SavedWaypoint.waymarkName`. A loop that
     was flying resumes **paused** at the same leg, never teleported and never restarted mid-leg.
   - Right-clicking a connector opens it as a waymark: name, per-service flow, targets, which ships are
     scheduled to visit, and what is currently preventing a transfer.
   - Setup authors connector prefabs, items and recipes (Large and Small) linked to the Grid Utilities
     node; the *loop* is gated behind its own research node after Grid Utilities — a shuttle is a
     machine, and a machine should be earned. Naming is not gated: a label is not a technology.

6. **Settled for the first round**
   - This is the **next round after 9.34.0-dev**, ahead of the inspector overlay.
   - **Arrival is by block size, and the difference is real:** the large connector is a magnetic lock
     (the docking port's capture and `FixedJoint`) with the full rate, and a shuttle that misses the
     approach cone holds off and tries again rather than transferring at half speed. The small connector
     is a soft capture inside a marked radius — forgiving, slower, and enough for a deckhouse. A loop
     that needs the rate needs the lock, and the panel states that requirement in one line instead of
     leaving it to be inferred from a trickle.
   - **One transfer at a time, and the queue is visible.** The connector serialises visitors: each
     scheduled ship sees its place, the connector shows the line, and the head of the queue is the only
     one drawing. A player who wants a hauler to wait for a passenger run gets that by watching the
     queue and moving it, not by a priority field nobody reads.
   - Because the queue is the throughput limit, the queue is also the honest place to put the
     reservation check: a ship entering the queue re-prices its remaining legs, and if the wait has made
     the trip unaffordable it leaves the queue and says so rather than sitting at the connector
     indefinitely.

7. **Open**
   - Whether the star map draws waymarks and the active loop, and with what marker budget. (The route
     rendering deferred in 9.34.0-dev wants to be paid here, in the same round that makes routes
     flyable — an invisible schedule is a scary thing.)

8. **Settled after the first round — the pad that is not on a grid** *(shipped in 9.36.0-dev, Step 67)*
   - 9.35.0-dev could only refuel between two grids, which left the ordinary case unserved: a base on the
     ground is `PowerNetwork` + `FluidNetwork` + `GasNetwork` and no `GridEntity` anywhere near them. So
     the **static refuel pad** exists: a world-placed block in the quarry's family (BlockItem +
     placedPrefab + `PlacedBlock`), deliberately *not* a `TieredBlockDefinition`, because a pad is a
     machine you place and not a deck you upgrade tier by tier.
   - **A pad is a consumer, not a conduit.** It registers as a `PowerConsumer` whose `wattsPerSecond` is
     the demand it is actually serving — 0 W idle, rated while pumping — and the base network's own
     all-or-nothing `IsPowered` answer gates the flow. A base that cannot sustain the load refuses and
     logs it; it does not trickle-charge a ship on watts that do not exist.
   - **Supply is graph membership, never proximity:** the pad carries a `WaterTank` node so the base's
     pumps fill it through the fluid run, a `GasTank` with the collider that makes it a gas endpoint, and a
     drum behind `PortConfig` faces for item pipes plus the `IItemConsumer`/`IItemProvider` pair for belts,
     chutes and funnels. A block standing next to a pipe is not connected to it, and a pad that behaved that
     way would be a pad that ignores the cable the player just ran to it.
   - **One seam, two bodies:** `IRefuelPad` is what the loop drives, so a schedule can point at a station
     connector or at a strip of concrete. The queue, the waymark face and the visitor rules are shared;
     the pumping is not, because the two supplies are genuinely different problems.
   - **Ground rigs are free:** a car or lorry here is a grid with wheels, so it takes the same service —
     and a rig with no fuel tank asks for no fuel rather than hanging at the hose forever.
   - **Still open on the pad:** it never pumps back into a world run (the base drives its graphs, the pad
     only empties what it was given), and it will not reach into a visiting ship's cargo to take items —
     unloading is a docked port's export or the drum's Output face. No pricing, no broker, no filter beyond
     what the faces already do.

1. **Manual Route Calculation**
   - Select `Calculate Route To` and choose a discovered planet, moon, station, base, asteroid field, or waypoint.
   - The system reports total distance, estimated travel time, gravity wells, atmosphere segments, required thrust, expected power/fuel use, and reserve margin.
   - Calculation uses current ship mass, cargo contents, batteries, fuel, hydrogen, reactor output, engine efficiency, damage state, and selected speed profile.
   - Clear warnings explain whether the current ship can complete the route and what resource is missing.

2. **Recorded Routes**
   - A piloted journey can be recorded as waypoints, approach vectors, safe altitudes, docking actions, and speed limits.
   - Recorded paths can connect planets, stations, mining sites, and cargo docks.
   - Routes are editable, reversible, nameable, and visible on the star map. *(editable, reversible and
     nameable shipped in 9.34.0-dev; star-map visibility is still open, see the waymark round below)*

3. **Grid Autopilot** *(first line shipped 9.35.0-dev: hold the planned profile, brake for wells and
   atmosphere, stop at a named waymark, service at a pad, yield to a boarding pilot — via
   `GridEntity.ApplyAutonomousFlightThrust`. The rest of this list is open.)*
   - Enabling Autopilot lets a grid follow a validated route, manage cruise thrust, reserve braking power, avoid terrain, and perform configured docking approaches.
   - Autopilot pauses and alerts the player if mass, damage, power, fuel, territory, weather, or route obstruction makes the plan unsafe.
   - Cargo schedules can trigger loading, unloading, charging, refueling, and return journeys.
   - Rogue Crusader territory and hostile encounters can cause avoidance, retreat, escort requests, or player intervention.

### Crude Fractionation, Product Use & Flare Disposal

*(shipped in 9.38.0-dev, first part — `Scripts/Crafting/DistillationPlant.cs`: the dedicated plant
machine, one feed tank + six typed product tanks + `PlantFluidStore`; `Scripts/Crafting/OilRefinery.cs`
walked back to its legacy machine and both refineries lost the retired fuel chain (refined oil / heavy
fuel oil / marine gas oil); `Scripts/Items/LiquidType.cs`: LPG/Naphtha/Kerosene/Diesel/Gasoline
appended with densities and gauge colours; Setup Step 69 (`PetroleumColumnSetup.cs`) authors the
plant-hall model with an analog dial above every outlet and inlet, the Atmospheric Cut, the Refined Oil
re-runistillationPlant.cs`: the dedicated plant
machine, one feed tank + six typed product tanks + `PlantFluidStore`; `Scripts/Crafting/OilRefinery.cs`
walked back to its legacy machine and both refineries lost the retired fuel chain (refined oil / heavy
fuel oil / marine gas oil); `Scripts/Items/LiquidType.cs`: LPG/Naphtha/Kerosene/Diesel/Gasoline
appended with densities and gauge colours; Setup Step 69 (`PetroleumColumnSetup.cs`) authors the
plant-hall model with an analog dial above every outlet and inlet, the Atmospheric Cut, the Refined Oil
re-run and the naphtha plastic recipe, the block item and the Atmospheric Distillation research gate.
Still open within this design: freezing points, per-world crude assay recipes, where Refined Oil is
produced from now on, engine fuel preferences, the off-gas line and the flare stack.)*

Crude oil stops being one number and becomes a column of products. The point is not realism for its
own sake: it is that a player who wants the light end must also carry the heavy end, and the heavy
end is worth something too.

1. **The distillation plant (a dedicated new machine — decided by the 9.38 playtest)**
   - The **Distillation Plant** is a new wide plant-hall block, deliberately NOT the Oil Refinery: the
     refineries keep their plastics work (and no longer make refined oil, HFO or MGO) and crude
     conversion happens only here. The plant carries one feed tank plus **one tank per product**
     (six output tanks, each typed, each readable by a pipe run) and reads like a real plant — the six
     outlets run in a row along the deck front, the crude and refined inlets stand on the left end,
     and every outlet and inlet has an **analog dial above it**, rimmed and hub-capped in that
     liquid's colour, its needle sweeping with the tank it reads. The recipe shape already supports
     multiple `fluidOutputs`.
   - One batch of crude yields the realistic ladder, per 100 L of crude, and the fractions add up to
     the input with a small gas loss to the flare line:

     | Fraction | Per 100 L crude | LiquidType | What it is for |
     |---|---|---|---|
     | LPG | 2 L | `LPG` | cooking and heating, canister fill, chemical plant feedstock |
     | Naphtha | 8 L | `Naphtha` | plastics and solvents — the chemical plant's preferred feed |
     | Kerosene | 12 L | `Kerosene` | jet and turbine fuel, lamps, the small-engine band |
     | Diesel | 26 L | `Diesel` | land vehicles, gensets, pumps, mid maritime engines |
     | Gasoline | 18 L | `Gasoline` | fast, light, dangerous: small craft and personal engines |
     | Heavy fuel oil | 32 L | `HeavyFuelOil` (exists) | big maritime engines, boilers, the flare stack's best feed |
     | Loss to gas | 2 L | — | off-gas, only if the column is not hooked to a flare or vent |

   - Each fraction gets its own `LiquidType`, its own density, its own burn value and its own freezing
     point, so the number on the tank gauge means something. Density and burn value drive mass,
     pipe flow and engine output; a cold planet makes waxing a real problem.
   - The column is not "one recipe": fractions shift with the recipe the player picks (a light-sweet
     crude behaves differently from a heavy one), so oil sites are worth surveying. `OilSiteSampler`
     is where the assay comes from — a world that produces mostly heavies is a refining world, not a
     junk world.

2. **Products have to be spent, or the column is a pipe with extra steps**
   - Every fraction is a crafting ingredient somewhere it is genuinely better than anything else:
     kerosene in lamps and turbines, naphtha in plastics and solvents, gasoline in fast light
     engines, LPG in canisters and heating, diesel in land logistics, heavy oil in bunkers and
     boilers. Asphalt (below) is the sink for what nobody wants.
   - Maritime engines keep their existing per-engine `liquidFuel` field and gain a real preference:
     a big engine on light fuel runs hot and inefficient, a small one on heavy fuel cokes and
     starves. The fuel a player can burn is therefore a function of the column they built.
   - Recipes that consumed `RefinedOil` keep working through a conversion step, so an old base does
     not wake up broken; the new fractions are simply better, and progression gets a little slower on
     purpose — that is the point of the change.

3. **Flare stack (disposal, with a reward for piping it properly)**
   - Burns excess light liquids and gases: the off-gas the column cannot hold, surplus LPG, and
     fuel nobody has an engine for. Destroys what arrives, like `GasVent` does for gas, so it is a
     run terminator and not a storage device.
   - A flare is *not* free disposal: it consumes oxygen from the room it stands in and it dumps its
     heat into the compartment it is mounted through, exactly like every other `IHeatSourceBlock`.
     A flare inside a sealed base is a mistake, and the panels say so.
   - A **waste-heat recovery** attachment converts the burn into power at a poor-but-honest efficiency,
     which is the whole reason a player builds a flare next to a boiler house instead of on a cliff.
   - Sizing: a small vent-flare for a deckhouse and a full tower for a refinery. A flare that is fed
     more than it can burn back-pressures the line, and the line then refuses the feed — the same
     discipline the ventilation ladder already teaches.
   - Balance rule of record, settled in the 9.32.0-dev / 9.33.0-dev ventilation rounds — do not
     re-open it here: **per-service port caps are Oxygen 1, Exhaust 2 on Medium/Giant.** A flare is
     an Exhaust-port service, so two flares (or a flare and the ship's other exhaust service) still
     share that cap like every other vent service.
   - Burning it is one choice of three for the light end: flare it, burn it in a genset, or sell it.
     Only the flare destroys it outright, so it is the last resort and reads like one.
   - **A flare fouls its neighbourhood**: it lifts the local pollution signature as well as the room
     heat, so a stack beside a farm, a colony wall or a hunting ground has a consequence the world
     reacts to and not only a thermodynamic one. The waste-heat recovery attachment burns cleaner and
     hotter — it trades pollution for power, and both prices are shown on the panel.

4. **Open**
   - Whether fractions are stored in dedicated tank blocks per type or in one selectable tank that
     reports its contents. Default: one tank, typed at placement, like the gas tanks.
   - Whether gasoline is worth its power despite a real explosion risk. Default: yes, and a hot
     spill of it becomes a hazard the fire system can already handle.

### Asphalt Roads

> **Status (9.43.0-dev):** surface in two cell sizes (1 m patch + 4 m carriageway) **plus a cobble
> Stone Pathway**, a **point-to-point planner** with width control, a live ghost, whole-line refusal
> and section removal, per-run area-weighted wear with visible cracking, movement/traction bonuses,
> slope-based grade pricing and area-priced repair are shipped — `Scripts/Building/AsphaltRoad.cs`,
> `RoadRun.cs`, `AsphaltRoadMesh.cs`, `RoadPaver.cs`, `Scripts/Environment/RoadSurfaceUtility.cs`,
> `Scripts/Items/RoadPaverTool.cs`, `Scripts/Simulation/RoadSurfaceWheel.cs`, authored by
> **Setup Step 72** under `res_asphalt_roads` (the pathway recipe is ungated on purpose).
> **9.43.0-dev** replaced the leg-stitching planner with a carriageway solver
> (`Scripts/Building/RoadCorridor.cs`) that fillets corners to a real radius and cuts the route into
> shared cross-sections, so every lane tiles exactly; no new assets, so no new setup step.
> **9.43.1-dev** fixed the first Unity pass: preview refusals no longer reach the HUD, a one-click
> plan lays what its ghost shows, junction pockets against existing pavement are filled again, and
> width goes to 10 cells. **9.44.0-dev** added water crossings (culverts and bridges on piers,
> `BridgeSpan`, Step 73), **9.44.1-dev** made drawbridges reachable, and **9.44.2-dev** automated
> them — auto-open for hulls in the channel via `WaterProbeSystem`, a 450 W swing billed through an
> abutment `PowerConsumer`, horn and beacons, the interact key without the paver, and the multi-lane
> leaf geometry fixed around one `SpanFrame` projection (all runtime behaviour: no new assets, no
> new step, no new saved fields). **9.44.3-dev** rebuilt the surface wheel as the conveyor-style
> triangle ring — the drawbridge card's hover defect (rendered, but unreachable by the mouse) is
> gone, and the wheel now shares the conveyor ring contract. **9.45.0-dev** stood the deck over
> the water (freeboard 3.6 m fixed / 2.4 m drawbridge over the waterline, ramped at 15% from both
> shores, with the waterline read from voxel water AND the sphere world's ocean shell — the shell
> was invisible to the voxel probe and the demand never fired; shallow beds classify as water too),
> gave fixed crossings a real substructure — edge girders, capped piers on a 40 m bed
> probe, **stone 2/cell** for it wired by Step 73 — and replaced the "level the strip first"
> refusal with commit-time grading (3 m cut / fill; buildings and >6 m steps still refuse; all
> terrain work precedes the first slab, so graded stations ramp into their neighbours instead of
> stepping). **9.47.0-dev** added manual road guidance; **9.48.0-dev** adds named loaded-network
> snapshots (`RoadNetworkSnapshot` / `RoadNetworkUI`, existing recorder Step 65).
> **9.50.0-dev:** first unattended wheel run in `RoadWheelAutopilot`.
> **9.51.0-dev:** dedicated `AutoRunPilot` blocks and controls, Setup Step 74; Unity validation pending.
> Traffic scheduling, global membership and lamps remain open.

A cheap surface the player lays on terrain to make the world move faster and cleaner. The idea is
one sentence: **a road is the difference between walking a route and being able to run a schedule on it.**

1. **Material and craft**
   - ~~Made from the heavy end of the column plus aggregate: bitumen from residue, plus sand and
     gravel. A road is therefore literally how a refinery spends what it does not want.~~ *(9.41.0-dev —
     `Proc_BlownBitumen` HFO 40 L → 4 bitumen; `MachineRecipe_MixAsphalt` bitumen 1 + sand 2 + gravel 3 → 8 hot mix)*
   - ~~Placed like a `PlacedBlock` on terrain, snapped to the voxel surface, with a small set of shapes
     (straight, bend, junction, crossing, ramp for slopes) so a road follows the ground instead of
     fighting it. A hand-laid strip must feel as good as a paved straight.~~ *(9.41.0-dev — shipped
     better than specified: there is **no shape set and no ramp shape**. The cell drapes the ground
     through five samples, so the drape IS the ramp, and a `RoadEdgeMask` neighbour mask only decides
     whether an edge gets a raised kerb. Shapes are read out (`ShapeLabel`), never chosen.)*
   - ~~Repair is a can of the same material, so a bombed-out colony road is a chore and not a loss.~~
     *(9.41.0-dev — dragging the paver along a worn run repairs the **whole run** in one gesture, priced
     at `wear × cells × repairMaterialPerCell`; there is no separate repair item)*

2. **What a road is actually worth**
   - ~~Player and creatures move faster and cost less stamina on it; carried weight stops mattering as
     much, so a portage across a range becomes a walk.~~ *(9.41.0-dev — player walk speed scales by the
     run's `WalkSpeedMultiplier`, 1.30 at GOOD down to 0.88 broken. **Creature speed and the
     carried-weight/stamina side are still open.**)*
   - ~~Vehicles get their own bonus: better traction on slopes, no bogging in rain or mud, and the
     terrain speed penalties that already exist should stop applying on asphalt.~~ *(9.41.0-dev —
     `GridWheel` takes traction 1.25→0.85 and grip 1.40→0.92 from the run, a road under a wheel counts
     as firm ground so it stops bogging, and the road's multipliers override the terrain term. **Less
     rolling resistance is NOT shipped as a separate term, and weather (rain/mud/ice) does not modify
     a road's grip — both stay open.**)*
   - Wheels, conveyor lines and drone routes follow it: the road is a *routing* surface, and it is
     the natural place to hang the pathfinding that autopilot and delivery drones still do not have.
     A player who paved their logistics corridor should get the smarter traffic for it.
     *(**PARTLY OPEN.** Driver guidance: 9.47.0-dev; network snapshots: 9.48.0-dev;
     first unattended wheel run: 9.50.0-dev. Traffic scheduling and drones remain open.)*
   - Lighting along it: a road is where a player wants lamps, and lamp spacing along a road should be
     a thing the game notices (safety at night, no spawns on lit ground).
     *(**OPEN — deferred by 9.41.0-dev.** No roadside furniture shipped at all.)*

3. **Rules that keep it from being free**
   - ~~Roads wear: they need a maintenance material at a rate that is annoying, not punishing, and a
     road under heavy traffic wears faster.~~ *(9.41.0-dev — `WEAR_METRES_PER_CELL = 22000` per run cell,
     footstep load 1, wheel load 14 scaled by grid mass, so a heavy vehicle wears a strip far faster
     than a walker. That is the upkeep economy.)*
   - ~~Grading matters. Laying asphalt on rough ground either costs more material or is refused until
     the player levels the strip — the ground truth of the voxel terrain must not be bypassed.~~
     *(9.41.0-dev — `EvaluateSite` returns six bands; ≤ 0.22 rise per cell paves at 1 asphalt,
     ≤ 0.50 at 2, and `TooRough` / `NoGround` / `Underwater` / `Buried` are refused by both the drag
     and `BuildSystem.IsPlacementValid`, with `DescribeSite` putting the reason on screen. The terrain
     is never modified by paving.)*
   - A road does not run through a wall, a body, or water without a culvert or a bridge: the same
     volume discipline pipes already use. *(**PARTLY OPEN.** Water is refused outright (`Underwater`)
     and buried/obstructed ground is refused (`Buried`), so nothing is paved through a body or a wall —
     but there is **no culvert or bridge block**, so a road currently stops at water rather than
     crossing it.)*

4. ~~**Settled for the first round**~~ *(all three shipped as settled in 9.41.0-dev)*
   - ~~First round is **surface plus the drag-to-pave tool**: lay a run by dragging, repair it with the
     same material, keep the movement, traction and wear rules. Block-by-block placement stays as the
     fallback for one culvert or a patched bend.~~ *(done — `RoadPaver` for the drag, and a held
     `Block_AsphaltRoad` routes through the same snap and the same grade verdict via `BuildSystem`)*
   - ~~The road **network** object (a name, a connected-run trace, a traffic readout) is deliberately held
     back: it is the hook autopilot and drone scheduling want, and it should arrive with the routing it
     feeds rather than as an empty register.~~ *(9.48.0-dev — named loaded-network snapshots now
     accompany 9.47.0-dev's driver routing; global membership remains open.)*
   - ~~The pave tool ships with the surface, not with the network: a player holding the material should be
     able to pave without having earned a traffic system.~~ *(done — `Tool_RoadPaver` is gated by the same
     `res_asphalt_roads` node as the block, and by nothing else)*

5. ~~**Still open** — whether wear is tracked per block or per run.~~ **SETTLED 9.41.0-dev: per run.**
   Per-run won on both counts the question named: it is cheaper to simulate (one float per connected
   strip rather than one per cell) *and* it reads better on a half-repaired road, because a repair is
   one gesture over the whole strip at a price the player can reason about, instead of a cell-by-cell
   patch job. The cost is `RoadRun.ResplitAround` — lifting a middle cell has to re-flood the run from
   what remains and split it into however many runs the gap created, each inheriting the wear it was
   part of. Merging takes the worst of the two, so a worn strip joined to a new one is a worn strip.

6. **Still open after 9.53.0-dev**
   - ~~**Named loaded road networks** with cross-run condition/traffic readouts~~ *(9.48.0-dev)* —
     `RoadNetworkSnapshot` / `RoadNetworkUI`, existing Route Recorder (Setup Step 65); Thomas confirmed working in Unity.
   - **Network extensions remain open (deferred 9.48.0-dev):** unloaded/global membership,
     automatic label inheritance for new cells, persistent network IDs and lifetime traffic counters.
     Readouts cover loaded cells only; traffic is an area-attributed estimate from current run ledgers.
   - ~~**Driver road guidance**~~ *(9.47.0-dev)* — `RoadRoutePlanner` and `RoadDriverGuidance`,
     exposed by the existing Route Recorder panel (Setup Step 65); Thomas confirmed working in Unity.
   - ~~**First unattended wheel run: steering, speed control and brakes**~~ *(9.50.0-dev)* —
     `RoadWheelAutopilot`, existing Route Recorder (Setup Step 65); Unity validation pending.
   - ~~**Dedicated Large/Small Auto-Run Pilot blocks**~~ *(9.51.0-dev)* — `AutoRunPilot`,
     `AutoRunPilotUI`, `AutoRunPilotSetup` (Setup Step 74); Unity authoring and runtime validation pending.
   - ~~**Recorded/world-point local route start workflow**~~ *(9.52.0-dev)* — `LocalRouteUI`,
     `RouteDestinationPicker`, `LocalRoutePilot`; existing pilot/recorder, Unity validation pending.
   - ~~**Planner/pilot separation and visible route paths**~~ *(9.53.0-dev)* — `LocalRouteUI`,
     `AutoRunPilotUI`, `RouteRunSession`, `RoutePathOverlay`, Routes inspector; no new authoring assets.
   - ~~**Simple loaded-road end-to-end operation**~~ *(9.54.0-dev)* — Planner-saved network runs; Pilot executes two braked phases.
   - **Network branching, loops and irregular corridors remain open (9.54.0-dev):** explicit destinations required until traversal policy is agreed.
   - **Unity acceptance (9.54.0-dev):** real draped-road capture, URP path visibility, curved-road reversing and save/load.
   - ~~**Pilot execution budgets and arrival battery telemetry**~~ *(9.55.0-dev)* — read-only selected-route estimates and session arrival snapshots.
   - **Forecast calibration remains open (9.55.0-dev):** real chassis, terrain, marine shaft-fuel range, variable generation and travel/holding times.
   - **Map-based destination and route selection remains open (deferred 9.52.0-dev, Thomas's choice):**
     map interaction, waypoint editing, route preview and mode-appropriate map planning.
   - **Local ship navigation extensions remain open (deferred 9.52.0-dev):** powered marine braking,
     mooring/docking, obstacle detours, long-distance/unloaded execution and safe saved in-flight continuation.
   - **Automation extensions remain open (deferred 9.50.0-dev):** reverse/three-point recovery,
     traffic priority, docking/service schedules, automatic rerouting/restart, delivery drones,
     unloaded-road travel and persistent driving sessions. Loaded-road runs restore braked, not driving.
   - **Creatures** do not take the walk-speed bonus; only the player and grid wheels do. Stamina and
     carried-weight relief on pavement are not modelled at all.
   - **Rolling resistance** is not a separate wheel term — the road only scales traction and grip.
     Weather (rain, mud, ice) does not modify a road's grip.
   - ~~**Culverts and bridges**~~ *(9.44.0-dev)* — the paver now inserts a culvert or a bridge on
     piers where the line reaches water, at a deck level taken from its own approaches and billed
     from a separate iron-and-stone pot.
   - ~~**Drawbridges**~~ *(9.44.1-dev)* — reachable now: a third surface-wheel card lays a road whose
     crossings open, the interact key swings a deck you are aiming at, and the structure and its
     swing survive a save. *(9.44.3-dev closed the gap 9.44.1 left: the card rendered but its hover
     test could never reach it — the wheel is now the conveyor-style triangle ring.)*
   - ~~**Drawbridge automation**~~ *(9.44.2-dev)* — shipped, all four items: **auto-open on ship
     approach** (an overlap volume under the deck classified along the channel, with the maritime
     verdict from `WaterProbeSystem.GetSubmergence` at the grid's centre of mass), **power draw**
     (450 W while swinging through an abutment `PowerConsumer` — an unwired deck no longer swings),
     a **warning light and horn** (beacons dark/flashing/steady at both approaches, a procedural
     two-tone horn on every swing), and **operating without the paver in hand** (the standard
     interact prompt on any aimed deck cell). Still open within the same design: a moored hull
     inside the hold volume holds the channel open until it moves.
   - ~~**Approach barrier arms**~~ *(9.46.0-dev)* — `BridgeSpan`, runtime-generated; no setup step.
   - ~~**Per-span automation switch**~~ *(9.49.0-dev)* — `BridgeSpan` / `BridgeControlTarget`,
     runtime-generated; no setup step. Thomas confirmed working in Unity.
   - ~~Water is refused rather than crossed~~ — `GradeBand.Underwater` and the `DescribeSite` string
     "needs a culvert" are now reached only when the paver has no bridge material configured, which
     is the correct remaining case: a tool that cannot afford a crossing should say so.
   - No road-side furniture either — no lamps, kerbstones, signage, barriers, manholes or drains
     (the kerb is part of the road mesh).
   - ~~**One tier, one curve.** Asphalt is the only paved surface~~ *(9.42.0-dev — a cobble **Stone
     Pathway** is the second surface, with its own walk curve, no vehicle handling and immunity to
     wheel wear)*; there is still no concrete, gravel or dirt road and no second wear curve. No
     asphalt temperature, laying window, roller or compaction pass — hot mix cures instantly.
   - ~~**Corners and crossings are still square.**~~ *(9.42.0-dev — bent cells now carry a curve
     frame: mitred entry/exit cross-sections, so straights keep straight kerb lines and a chain of
     bent cells tiles into one continuous arc, while junction boxes stay square like real
     intersections; per-cell corner rounding was tried first and rejected for beading every curve)*
     What remains open: ~~per-lane mitres on 3-wide curves~~ *(9.43.0-dev — superseded rather than
     fixed: lanes no longer fan off a centreline mitre at all. `RoadCorridor` cuts the carriageway
     into shared cross-sections and each lane takes its own quad between two of them, so the inside
     and outside of a bend are correct by construction instead of by stretch factor)*, and
     decorative junction furniture — crossings, stop lines, signage.
   - **No auto-routing.** A corridor whose ground cannot be paved is refused whole, with a reason —
     the planner never moves the road somewhere the player did not choose. If that ever changes it
     changes here, not silently in the gesture.
   - **Bitumen comes from Heavy Fuel Oil only.** No natural bitumen deposit, tar sands ore or
     alternative binder.

### Coordinate Jump Drive

A late-game **Jump Drive** provides charged, coordinate-based faster-than-light travel without replacing normal engines or route planning.

*(drive, charge, pool, hop, planet lock, fly-to, jump legs, FX, partial jumps shipped through 12.27.0-dev — `GridWarpDrive`, `NavFlightAutopilot`, `WarpFx`; Setup Step 50 authors the block. 12.28.0-dev adds the mass penalty. 12.32.0-dev adds the drive-panel destination picker and fixes the warp arrival no-op. 12.33.0-dev adds the safety gates. 12.34.0-dev adds route-book destinations and the spin-up assist. 12.35.0-dev adds the researched charge-time bonus, which 12.36.0-dev turns into crafted resonator items installed on the drive. 12.37.0-dev adds auto-run warp legs.)*

**Mass penalty (12.28.0-dev, cap 12.28.1-dev):** `factor = min(maxMassFactor, sqrt(TotalMass / ratedMassKg))` at or above rated mass, else 1. Defaults: rated 100 t, cap 12. Price multiplies, hop divides. One full drive still equals one hop. Cargo is `ContentMass` inside `TotalMass` — do not add a second cargo scale.

- ~~The player chooses a known destination, beacon, or safe coordinate and sees range, charge cost, mass penalty, cooldown, and arrival error before committing.~~ *(12.32.0-dev — drive-panel picker: charted bodies and powered beacons with live distance, live price and affordability colour; lock-and-jump through the confirm wheel; partial jumps fly the target line. Free-coordinate entries in the picker remain open; route-book destinations shipped in 12.34.0-dev.)*
- ~~Maximum range decreases as ship mass and cargo increase.~~ *(12.28.0-dev — `GridWarpDrive.MassFactor` / `EffectiveHopKm` / `EffectiveRateWhPerKm`; Setup Step 50 fills `ratedMassKg` and `maxMassFactor` only when zero.)*
- ~~The drive requires a large stored-energy charge and cannot operate while critically damaged, obstructed, inside prohibited gravity depths, or without a safe arrival volume.~~ *(12.33.0-dev — health gate with DAMAGED state, vacuum gate, arrival clamp 25 km over every body plus refusal veto; Setup Step 50 untouched.)*
- ~~Multiple drives can combine range or reduce charge time according to research and grid configuration.~~ *(range and bank pooling 12.24.0-dev; spin-up assist — sqrt(enabled count) — 12.34.0-dev; item-driven charge time 12.36.0-dev — crafted Warp Coil Resonators installed on the drive (recipe unlocked by the Coil Resonance research, Setup Step 97), -15% spin-up each.)*
- ~~Blind jumps carry larger arrival error and are blocked when collision safety cannot find a valid destination.~~ *(12.33.0-dev — lateral scatter up to 0.5% of the hop; mapped locks stay exact.)*
- Jump calculations include territorial warnings, stellar hazards, atmosphere restrictions, and minimum reserve power after arrival. *(stellar standoff, vacuum gate and the 10% arrival reserve shipped through 12.33.0-dev; territorial warnings deferred until a territory system exists to warn about.)*
- ~~Autopilot can use approved jump legs inside recorded interplanetary routes.~~ *(12.37.0-dev — the auto-run loop engages the ship's own warp drive for legs past one hop: aims, banks price plus reserve, fires unconfirmed, chains hops; WARP LEGS toggle on the auto-run panel.)*

---

## 5. Master Roadmap

| Version | Theme | Execution Status | Scope | Manual Unity Work |
|---------|-------|------------------|-------|-------------------|
| **4.5.0** | Factory Foundations | 🛠️ **WORKING ON** | Conveyor belts, chutes, basic machines, grid lights, machine UI | Medium — prefab generation, animation clips |
| **4.6.0** | Production Lines & UI Revolution | 🛠️ **WORKING ON** | Assemblers, recipe chains, UI theme system, research UI overhaul | Medium — recipes, themes, panels |
| **4.7.0** | Power, Vehicles & Combat | 🛠️ **WORKING ON** (Combat + base defense in progress) | Engines, batteries, damage framework, Iron Sword/Pistol, Training Dummy, Ghoul enemy, 6-tier Crusader armor + equipment panel, passive livestock (Cow/Sheep/Pig), 6 mythical enemies + Roc boss, grenade + rifle, Auto Turret, Heavy Cannon/Minigun/Gustav, Flamethrower Turret (6.62.0), Mortar Turret (6.63.0), Giant Shell Turret (6.64.0), Anti-Air Turret (6.65.0), Energy / Relic Turret (6.66.0), automated ammo logistics (6.67.0), defense status UX (6.68.0), conserve-ammo/reserve (6.69.0), engagement range/arc (6.70.0), painting system (6.71.0), jetpack fuel (6.72.0), H₂ canister (6.73.0), portable H₂ tank ml + H₂ vitals (6.74.0), Armor Station + timed Armor Upgrade Station (6.80.0–6.80.4-dev; Unity-validated) | High — combat prefabs, physics |
| **4.8.0** | Logistics 2.0, Screens & Trajectory | 🛠️ **WORKING ON** | Trains, drones, configurable screens, trajectory camera, orbit map (`M`) | High — train track, camera rigs, panels |
| **4.9.0** | Living Worlds | 🟡 **PARTIALLY COMPLETE** | Ruins, weather, water flow, enemies, planet skies, gravity/orbit fixes | Very High — worldgen, AI, fluids, rendering |
| **5.0.0** | Orbital Expansion | 🟡 **PARTIALLY COMPLETE** | Rockets, space stations, asteroid mining, orbital cargo, space ambiance | Very High — new scene/zone system |
| **5.1.0** | Interplanetary Age | 🟡 **PARTIALLY COMPLETE** | Planetary bases, exo-resources, nuclear fission, nuclear warheads | Very High — empire dashboard |
| **5.2.0** | Architect Era | 🟡 **PARTIALLY COMPLETE** | World forge, megastructures, fusion, save schema v2 | Very High — new save format |
| **5.3.0+** | Live Ops | 🟡 **PARTIALLY COMPLETE** | Modding API, multiplayer foundations, seasonal content | TBD |

> **Audit basis (6.17.0-dev):** Active sections have current production work recorded in their detailed status tables. Later sections are marked **PARTIALLY COMPLETE** only because reusable foundations already exist (cosmos, planetary bodies, water/weather, nuclear, research, persistence, and grid systems); their named headline features and completion gates remain open. No section is promoted to **COMPLETED** without setup generation and the Unity validation. Roadmap maintenance passes do not remove planned scope; they only update status markers, evidence notes, dates, versions, and immediate-next-step labels.

### 5.1 Execution Status Convention

| Marker | Meaning |
|--------|---------|
| 🛠️ **WORKING ON** | Active implementation is the current team focus. |
| 🟡 **PARTIALLY COMPLETE** | Some production code or content exists, but one or more roadmap requirements or validation gates remain open. |
| ✅ **COMPLETED** | The complete scoped section is implemented, generated through the required setup step, validated in Unity, and documented. |

Statuses are evidence-based and move forward only after code/content review and Unity validation. A section may remain **PARTIALLY COMPLETE** even when its core script exists if variants, persistence, setup automation, UX, or verification are still outstanding.

### 5.2 Roadmap Maintenance Rules

- **Never remove planned roadmap content** during status passes. Preserve feature scope, historical requirements, and future ideas unless the team explicitly asks to delete them.
- Status passes may update only status markers, evidence notes, dates, version pointers, and immediate-next-step labels/order.
- Mark a task **🛠️ WORKING ON** when it is the current implementation or validation focus.
- Keep a task **🟡 PARTIALLY COMPLETE** when code/content exists but setup generation, persistence, UI/UX, variants, balance, or Unity validation remains open.
- Mark a task **✅ COMPLETED** only when production code/content exists, required `Tools > Voxel Engine > Voxel Engine Setup` steps connect it non-destructively, Unity validation confirms it in Unity, and the changelog/manual steps are documented.
- If a completed feature later needs a fix, keep the parent scope **✅ COMPLETED** and track the specific fix as **🛠️ WORKING ON** until validated.

---

## 6. Detailed Feature Breakdown

### 6.1 Version 4.5.0 — Factory Foundations — 🛠️ WORKING ON

**Goal:** Make the player feel the factory fantasy within the first hour.

#### Execution Status

| Area | Status | Repository Audit |
|------|--------|------------------|
| Conveyor belts | ✅ COMPLETED | Straight, corner, ramp, and vertical conveyor flows are implemented with consistent belt-surface height, precise transitions, item visuals, shape workflow, I/O arrows, and validated persistence. |
| Conveyor chutes | ✅ COMPLETED | Straight vertical transport, snapping, moving-item visuals, inventory endpoints, and save-compatible placement are validated. Chutes intentionally remain a single authored transport form; no corner, spiral, or other chute variants are planned. |
| Basic machines | 🟡 PARTIALLY COMPLETE | Electric Furnace, Crusher, and three Assembler tiers exist. Crusher/Assembler have recipe-selection UIs, visual animation, centralized simulation ticks, additive buffers/progress/enabled persistence, and Unity smoke Unity validation; production statistics and module systems remain. |
| Storage blocks | 🟡 PARTIALLY COMPLETE | A basic chest and the wider storage system exist. 11.1.0-dev ships the Wooden Crate → Iron Chest → Steel Chest tiers (setup step 77); **11.2.0-dev** adds the port-locked Provider and Requester chests (setup step 78); **11.4.0-dev** adds `LogisticsNetwork`, the wireless request/fulfilment pass; **11.5.0-dev** inverts the port roles to mirror the wireless ones (Provider ports IN, Requester ports OUT) and gives the request list its own saved field and picker. **11.5.1-dev** fixes the duplicate item rows in the request picker (setup step 81) and makes the panel refresh live when a request is removed. Pending Unity validation. **11.6.0-dev** adds the Buffer Chest (both roles, free faces, per-item stock target), closing the Logistic Chests line. **11.7.0-dev** adds the Drone Port for out-of-range delivery. Remaining: vehicle carriers, and a visible drone mesh in transit. |
| Power pole, wire, and substation | 🟡 PARTIALLY COMPLETE | Manual wiring, poles, substations, transformers, compact LV/HV one-link connectors, and 8-link wall/foundation relays exist. Setup reruns preserve balance while adding missing links. |
| Grid/static lighting and LED strips | ✅ COMPLETED | Detail/Structural single and dual spotlights, Structural LED strip, premium segmented/clean LED visuals, screen data providers, configuration UI, visible chase animation, motion activation, and saved lighting config persistence are implemented and validated. |
| Shared Machine UI | 🟡 PARTIALLY COMPLETE | Crusher and Assembler panels now expose recipe selection, progress, power, toggles, inventory slots, scrolling, and item-port integration. Remaining work: complete unification across every machine, production statistics, and theme overrides. |
| Item entity system | 🛠️ WORKING ON | Unity validation covered the **5.70.0-dev** pooled physical world-item lifecycle. **5.71.0-dev** adds a shared cross-belt conveyor-carried visual pool; Unity factory load validation remains pending. |
| Recipe reansport blocks to the previously validated per-frame runtime path while keeping the centralized transport interface groundwork for a later safer migration. |
| Factory persistence | ✅ COMPLETED | Conveyor/Chute item packets, Conveyor Splitter buffer+round-robin cursor+routing mode+per-output filters, Crusher/Assembler recipe+progress+enabled, Funnel buffer+mode, and all machine containers save and restore. Legacy saves compatible. **9.57.0-dev** extends the same additive record to the petroleum chain through one shared `IMachineProcessState` payload: batch + locked recipe + fluid tanks for the Distillation Plant, Catalytic Cracker, Oil Refinery, Chemical Plant, stationary Flare Stack and both ship machines, plus the item slots of the four world machines. **9.58.0-dev** extends the same record to the player: `hasAnchor` / `anchorBody` / `anchorLocalX/Y/Z` place a rejoin relative to the body it was saved on, and `cosmicSimulationSeconds` restores the orbital phase, while a save-side guard now refuses any scene position inside a body whatever the active frame is. **10.2.0-dev** closes the round's own deferred item: `Furnace`, `ElectricFurnace` and `Pumpjack` implement the same `IMachineProcessState`, so batch progress, burning fuel, the pump cycle and the electric furnace's `userEnabled` / `autoPull` switches all save and restore. **9.59.0-dev** anchors placed blocks to the body they stand on (`hasBodyAnchor` / `anchorBody` / `anchorLocalX/Y/Z`, additive) so a block survives a moved body, a rebase and a frame switch, and adds the per-body chunk-store identity file that refuses to load chunks generated from another field. **10.0.0-dev** fixes the stored payload itself — three bytes per voxel instead of two, so a stored chunk is no longer missing its last third — and retires the old files with `RegionFile` V4 and identity format 2. **10.1.0-dev** anchors movable grids the same way (`SavedGrid.hasBodyAnchor` / `anchorBody` / `anchorLocalX/Y/Z` plus a body-local rotation), so a hull survives a moved body, a rebase and a frame switch; a grid with no anchor keeps restoring from its scene coordinate. **Corrected in 11.0.0-dev:** that round also stored the linear velocity relative to the scene frame, which handed a parked hull its planet's orbital speed and sank it further on every rejoin; the three velocity fields are removed and the saved scene velocity is restored as before, while the position and heading anchor is kept. **11.0.0-dev** makes the Jack Pump a tank-only crude producer — its item slots and barrel items are gone, and its crude and part-batch travel in the same machine-process record; setup step 76 converts an existing prefab. **Open, and corrected here:** physical dropped items were recorded as holding scene coordinates — they are not saved at all. `DroppedItem` has a 300 s lifetime and no save record, so a drop on the ground is gone after a reload by design. Persisting them is its own item, not part of this family. **Open, from 11.0.0-dev:** `Item_CrudeOilBarrel` and `Item_EmptyBarrel` are now orphaned — the Jack Pump no longer produces or consumes them and `OilRefinery` has always taken liquid crude through `fluidIn`, so no recipe in the project reads either item. Retiring the two items, their recipes and their icons is its own small piece of work, deferred rather than folded into a bug-fix round. |
| Step 5 tiered setup workflow | 🛠️ WORKING ON | Generated Size-V4 prefabs migrate to Size-V5 seamless Foundation decks and Stair anchors. Missing resources are repaired safely while custom prefabs, materials, recipes, and balance values remain preserved. Unity two-run validation is pending. |
| Step 17 setup workflow | ✅ COMPLETED | Step 17 remains non-destructive, refreshes generated visuals/colliders safely, preserves balance values, and connects upgraded Funnel/Crusher/Assembler prefabs plus contextual conveyor shape workflow. |

> **Completion gate:** This section becomes **✅ COMPLETED** only after Step 17 is non-destructive, all listed variants are authored through the setup wizard, factory runtime state persists, and the Unity validation checklist passes.
>
> **Factory scope guard:** Only conveyor belts receive factory placement/shape variants. Funnels and conveyor chutes remain single-variant unless the roadmap is deliberately revised later.

#### New Content — 🟡 PARTIALLY COMPLETE

1. **Conveyor Belt Block**
   - Straight, corner, ramp, vertical variants.
   - Items visually travel on the belt.
   - Speed tiers: Basic → Fast → Express.
   - Snap to existing grid and to static building sockets.

2. **Conveyor Chute**
   - Drops items from one elevation to another.
   - Single straight transport form only; no chute variants are planned.
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
| Recipe graph validation | ✅ COMPLETED | Validator and non-destructive repair pass are in place. Unity validation covered the graph at 0 errors after repair. Remaining duplicate-output notes are informational/progression warnings. |
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
| Grid shape variants | ✅ COMPLETED | Unity validation covered all six structural meshes, textures, collision, placement ghosts, selection behavior, and corrected wheel alignment. Step 18 provides the non-destructive setup connection. |
| Unified grid placement | 🟡 PARTIALLY COMPLETE | Unity validation covered Detail-on-Structural lattice placement and size-labelled content. Gas/liquid topology and screen data-source addressing now resolve both block scales on one Grid; **5.69.0-dev** persistence was Unity-validated. Remaining positional-indexing work is open. |
| Unified grid screen sources | ✅ COMPLETED | Unity validation covered the unified screen/data-source work. Detail providers use precision-safe encoded addresses while legacy Structural addresses remain compatible. |
| Unified pipe placement and networks | 🛠️ WORKING ON | Unity validation covered existing Item/Gas/Liquid pipe Detail placement, rotation-independent alignment, correct visual direction, midpoint arms, live ghost previews, stable topology, resource-safe ghosts, and wrench disconnect behavior. **7.8.1-dev** budgets visual rebuilds; **7.9.0-dev** restores up-to-five-small-cell same-plane runs, rejects off-plane links, and blocks structural conduit engulfing. Unity validation is pending. No duplicate pipe content was introduced. |
| Vehicle power foundations | 🛠️ WORKING ON | **6.9.0-dev** centralizes battery transfer so Recharge/Discharge modes work together, keeps Auto batteries deterministic, routes maritime fuel/coolant through the unified liquid-pipe network, adds a dedicated crude-engine solid-fuel hopper, and refreshes Step 13 ship-engine/turbo/chain-drive/generator visuals at large premium scale. **6.9.1-dev** further densifies the engine meshes so they read as solid industrial ship machinery with heavier superstructure, railings, housings, and larger flywheels inspired by the supplied reference. **6.9.2-dev** refines the three ship engines again to look less synthetic, relocates visual fuel/coolant/exhaust/rotation oduced. |
| Vehicle power foundations | 🛠️ WORKING ON | **6.9.0-dev** centralizes battery transfer so Recharge/Discharge modes work together, keeps Auto batteries deterministic, routes maritime fuel/coolant through the unified liquid-pipe network, adds a dedicated crude-engine solid-fuel hopper, and refreshes Step 13 ship-engine/turbo/chain-drive/generator visuals at large premium scale. **6.9.1-dev** further densifies the engine meshes so they read as solid industrial ship machinery with heavier superstructure, railings, housings, and larger flywheels inspired by the supplied reference. **6.9.2-dev** refines the three ship engines again to look less synthetic, relocates visual fuel/coolant/exhaust/rotation ports to more believable service points, centers the major flywheel/output faces, and moves turbo attachment markers onto sensible engine locations. **6.9.3-dev** adds idle engine shaft output for visible running behavior, animates chain-drive/rotation-transfer/generator coupling visuals, and snaps exhaust/shaft-driven maritime parts to the nearest believable matching engine or drivetrain port during placement. **6.17.1-dev** adds [DefaultExecutionOrder(-20)] to MaritimePropulsionSystem (runs before GridEntity), [DefaultExecutionOrder(0)] to GridEntity, maritime power sync in GridEntity.UpdatePower() so generator output and electrical-propeller demand flow into the grid battery pool, and fixes coolant consumption timing (use current-frame conditions instead of last-frame IsRunning). **6.18.0-dev** adds color-coded VARIABLE service ports on the HFO V8 / MGO V12 (aim any engine face → a fuel/coolant/oxygen port appears on the surface, always outside the hull; 1 fuel / 1 coolant / 1 oxygen / 2 exhaust caps, over-cap placement refused with a red ghost + message), rewrites the liquid+gas tank corridor probe onto the detail lattice with overlapping spheres so tanks a few cells off a pipe run connect and fuel/oxygen/coolant actually pump in, auto-orients the exhaust pipe's intake flange onto the engine's exhaust collector, and rebuilds the maritime propellers as true lofted screw blades. **6.19.0-dev** makes the propeller blades scale with the cell and render double-sided (they were invisible before), mounts the color-coded port collars flush on the hull (no more floating), slims the port so it no longer swallows a gas pipe, snaps the pipe hub to the fine Detail lattice, extends variable ports to the Crude Inline-4 (oxygen + item intake, new Item service, per-tier gating), removes the static fuel/coolant/oxygen/item engine ports (exhaust + shaft kept), widens the tank corridor/proximity reach so a tank ~0.5 m off a run connects (incl. diagonal), makes pipe-visual rebuilds event-driven (TopologyVersion bumped on block add/remove) to kill the close-pipe lag, and infers the carried liquid type so a second fuel run is refused with "Fuel input already connected (max 1)". **6.80.5-dev** fixes maritime generator/electrical-propeller double billing, stabilizes partial-power electrical propeller thrust with a resolved grid service fraction, and exposes command/delivered-power diagnostics in the propeller UI/terminal. **6.80.6-dev** adds exact mechanical-port snapping/directed topology, stable landing-pad/gear locks, restored grid Rigidbody pose, grid tank type/amount/mode persistence, and explicit void-or-cancel tank type prompts; Unity validation confirmed the full maritime fuel→engine→shaft→propeller chain. **6.81.0-dev** adds persistent gearbox settings, a two-click Mechanical Belt bus that powers aligned shaft take-offs, and the sealed Watertight Shaft Housing. **7.0.0-dev** intentionally removes the Encased Chain Drive class, prefab, item, recipe, and generated materials; old ships may lose that block by approved design. Step 13 explicitly repairs the Mechanical Belt recipe and its Tier 1 Hydro-Mechanics research unlock. **7.1.0-dev** adds aim-at-belt shaft take-off placement, exact engine output-port snaps, finite generator/propeller drivetrain loads with engine/gearbox stress and RPM bogging, a Resources item-persistence catalog for portable batteries/H₂ tanks, Research-search Y-key protection, and restored-grid ground-clearance recovery. **7.3.2-dev** adds curved belt wraps around open pulley rims, snapped-block shaft-axis rotation, focus-safe numeric gear editing plus wheel adjustment, a held-item name card above the hotbar, true low-throttle stress heating, recoverable 100% engine trips, and a multi-tick terrain hold for unanchored restored grids; Unity validation confirmed that drivetrain checklist. **7.4.1-dev** adds powered/hydrogen autonomous dampers, restored Grid Battery charge/mode persistence, and stale-space-velocity cancellation pending Unity validation. **7.5.0-dev** corrects ordinary grid-cell placement against landing gear and begins pressure-aware atmosphere-to-space flight behavior; Unity validation confirmed it. **7.6.0-dev** adds cockpit gravity-pull telemetry; **7.6.1-dev** restyles it as a fitted LCD field display with a discrete surface-reference meter; **7.7.0-dev** compacts the matching on-foot monitor and adds vacuum-starfield ambiance, Unity-validated. **7.8.0-dev** adds cockpit coast-path orbital diagnostics, Unity-validated. **7.8.1-dev** fixes Grid Battery panel/terminal flashing and dense pipe topology performance. **7.9.0-dev** restores strict same-plane five-cell pipe runs and blocks conduit engulfing by structural grid blocks, pending Unity validation. |
| Damage, armor, weapons, and life support | 🟡 PARTIALLY COMPLETE | Basic block HP/damage and one grid weapon foundation exist. **6.80.4-dev:** Unity validation confirmed the dedicated Armor Station, timed anvil-style Armor Upgrade Station, five module branches (five tiers each), Hazmat sealing, per-piece persistence, and the slim environmental heat/radiation hook. Step 48 authors/repairs the content non-destructively. Full typed damage, pooled ballistics, airtight support, and the rest of the radiation/heat combat content remain open. |

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

11. **Armor Station + Armor Upgrade Station**
    - **Armor Station:** exclusive armory workbench for armor, module, and station crafting.
    - **Armor Upgrade Station:** separate premium anvil that installs one module onto one armor piece at a time.
    - Both stations, their recipes, items, research links, and generated visuals are authored non-destructively through `Tools > Voxel Engine > Voxel Engine Setup` Step 48.

12. **Jetpack**
    - Separate inventory slot.
    - Unlocks the existing flight system.
    - Consumes fuel or hydrogen.
    - Upgradable thrust and fuel capacity at the armor station.

13. **Player Armor Upgrades**
    - Heat tolerance, radiation shielding, oxygen efficiency, impact padding, and mobility servo tiers 1–5.
    - Installation time starts at 30 seconds for T1 and scales to 150 seconds at T5; Hazmat sealing also takes 150 seconds.
    - Progress, station inputs/output, and installed state persist additively through save/load.

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

23. **Fall Damage** — ✅ COMPLETED
   - Player takes damage from high falls.
   - Armor upgrades reduce impact damage.
   - **6.25.0-dev:** Base fall damage is implemented for flat and radial-gravity worlds using local-gravity impact speed. Armor-upgrade mitigation remains future armor-station work.

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
| Configurable grid screens / displays | ✅ COMPLETED | All sizes, live text/power/data modes, right-click and terminal config, custom text/colors/border/font, visual bar charts, multi-source selection, live camera feeds, power gain/loss/net mode, persistence, and camera block integration are implemented and validated. |
| Camera block live feed | ✅ COMPLETED | `GridCameraBlock` exposes a live RenderTexture through `IGridCameraFeedProvider`; `GridScreenBlock` Camera mode applies it directly to the screen surface with correct online/idle/offline LED states and validated screen-source behavior. |
| Trajectory camera / orbit tools | ✅ COMPLETED (trajectory) | `TrajectoryPredictor` + `TrajectoryOverlay` (11.12.0-dev) give the predicted path, terrain impact marker and time/speed-to-impact in the wide exterior view. The separate Star Map / orbit overlay (item 7) is complete (12.19.0-dev). |

#### New Content

1. **Train System** - ~~rail blocks~~ ~~locomotive~~ ~~stations with loading/unloading~~ ~~schedule UI~~ *(11.15.0-dev)* - **COMPLETE**
   - `RailTrack` (straight / switch / buffer), `RailNetwork` (registry + A*), `RailStation` (named stop, cargo hold, LOAD/UNLOAD/PASSING), `RailTrain` (scheduled hauler), `RailConfigHud` (one console for all three).
   - **Core design rule:** a train is a scheduled agent that WALKS A GRAPH, not a vehicle that drives on a surface. That is what lets it keep running through unloaded chunks, and it is the whole reason bulk haul belongs on rail rather than on rovers.
   - **Balance rule:** rail refuses a gradient a road would drape over. A railway that climbs anything is just an expensive road; the refusal is what forces cut, fill and routing decisions.
   - **Design rule:** the cargo hold lives on the STATION, not the train, so factories fill and drain on their own schedule and the train only has to show up. Stations are matched by name, so a schedule survives its station being rebuilt.
   - **Implementation note:** switch settings must be re-applied a frame after load - a switch clamps its selection against a link list that is still filling while neighbouring track restores.
   - **SUPERSEDED - do not extend this implementation.** See item 1b. The 11.15.0 rail system stays in place and playable, but no further work goes into it; cargo wagons, signalling and laying asould have to be written twice - once for grids, once for rails. Unifying is cheaper than maintaining that split forever.
   - **Target:** a locomotive and its wagons are ordinary player-built GRIDS that are constrained to a rail, rather than a separate entity type. This mirrors the decision already proven by the Orbital Programme, where a satellite is an ordinary grid the player DECLARES a satellite rather than a bespoke object.
   - **Consequences to design for:**
     - Consists become real: couple grids into a train the way docking ports already join grids.
     - A wagon is just a grid, so any grid block works on it - containers, tanks, refineries, turrets.
     - Grid damage, paint, power, pressurisation and the inspection overlay all apply for free.
     - The rail console folds into the existing grid terminal instead of being a separate UI.
   - ~~**Open question:** how a grid-based train keeps running while its chunks are unloaded~~ - **SETTLED 11.31.0-dev: the premise was wrong.** Grids are not chunk-streamed (persistent scene objects, saved by body anchor, never distance-culled), and `PlacedBlock` track is not either. A grid on rails keeps ticking regardless of where the player is, so no dormant analytic mode is required.
   - ~~A locomotive and its wagons are ordinary player-built grids constrained to a rail~~ *(11.31.0-dev)* - `GridRailTruck` (the block) + `GridRailBogie` (the constraint). Setup step 92.
   - ~~The rail console folds into the existing grid terminal~~ *(11.31.0-dev)*.
   - ~~**Wider rail tracks.** Multi-cell track widths (2-wide and 3-wide gauge)~~ *(11.33.0-dev)* - delivered via `RailCorridor`, which does follow `RoadCorridor` rather than inventing a second approach.
   - ~~**Draggable rail placing with smart routing.** Click a start, drag to an end~~ *(11.33.0-dev)* - auto-straights, auto-curves and a gradient-aware refusal that names the reason. Auto-junctions were deliberately left out (see phase 3).
   - ~~**Phase 2:** multi-car consists~~ *(11.32.0-dev)* - **COMPLETE.** Coupling uses PATH HISTORY, not the docking port's `FixedJoint`: a railed grid is kinematic so a joint is inert, and a joint trails like a rope so wagons cut corners. The leader records its route and wagons sample it at an accumulated chain distance.
   - ~~**Phase 3:** wider gauges and draggable smart placement~~ *(11.33.0-dev)* - **COMPLETE.** `RailCorridor` reuses `RoadCorridor` for centreline, fillets and multi-lane footprints; rail adds the gradient check and treats lanes as parallel tracks. Setup step 93.
   - ~~Open from phase 3: automatic junctions where runs cross~~ *(11.41.0-dev)* - crossings splice a shared junction node; no guessing needed because a fresh junction routes straight through until a player sets the points.
   - ~~**Phase 4:** signalling and block occupancy~~ *(11.34.0-dev)* - **COMPLETE.** `RailSignalling` derives sections from the graph (track between junctions); trains brake for occupied line. `RailSignal` (step 94) is the visible readout and is deliberately not load-bearing.
   - ~~**Smooth bends** *(11.41.0-dev)* - mitred yaw plus per-cell curve deformation (fanned sleepers, arc-length rails) derived from the link graph; rail fillet radius floor raised to 2.75 m.
   - ~~**Live material readout while dragging** *(11.41.0-dev)* - `RailCostHud`, driven from the same plan the ghost and commit use.
   - ~~**Triple-width formation** *(11.41.0-dev)* - sleeper 4.5 m / gauge 3.15 m / rail heads 0.33 m / bed 6.3 m, bogie re-gauged in step 92; idempotent re-gauge passes in steps 85/92/93.
  - **Rework brief is now fully delivered, retirement included (12.0.0-dev).** The rail family has no open architecture left; future rail work is content and balance, not rework.
   - ~~**Retirement:** the 11.15.0 `RailTrain` entity~~ *(12.0.0-dev)* - **COMPLETE, MAJOR.** Entity, enum, console, map markers and interaction branch removed; setup 85 scrubs dead scripts off locomotive prefabs. Saves holding a v1 locomotive do not carry it across - the bump they were waiting out.

2. **Drone Ports** — ~~flying logistics drones between ports~~ *(11.7.0-dev)*
   - `DronePort` pairs with another port over 400 m and serves the logistic chests within 48 m of each end; `DroneNetwork` owns pairing, dispatch and delivery.
   - Powered rather than battery-swapping: 20 W idle, +140 W in flight. Power gates dispatch only, so an airborne drone completes its trip.
   - **Balance rule:** a drone only carries what the local wireless network cannot. Anything a provider within 48 m of the requester can supply never takes a flight, so drones never compete with local logistics.
   - ~~A visible drone mesh flying the route~~ *(11.8.0-dev)* — `TransportDrone`, presentation-only, per-port toggle.
   - **Streaming limit:** a port outside the loaded radius (~192-256 m) keeps its route but cannot trade until its chunk loads. Reported as dormant in the panel.
   - ~~Persistence for `showDrone` / `portName` / in-flight state~~ *(11.9.0-dev)* — saved by position, flight resumes on load.
   - ~~Upgradable drones~~ *(11.9.0-dev)* — universal Speed / Efficiency modules as speed / capacity, two slots per port.
   - Open: vehicle carriers for cargo beyond one payload, and recharge mechanics.

3. **Logistic Chests** — ~~Provider Chest~~ ~~Requester Chest~~ *(11.2.0-dev)*, ~~wireless request/fulfilment routing between them~~ *(11.4.0-dev, roles corrected 11.5.0-dev)*, ~~Buffer Chest~~ *(11.6.0-dev)* — **COMPLETE**
   - Provider Chest: pipes and belts FILL it (ports pinned to Input); `LogisticsNetwork` hands its stock to in-range requesters.
   - Requester Chest: the network fills it wirelessly; its ports (pinned to Output) FEED the pipes downstream. Its request list is a dedicated field, separate from the port filters.
   - **Weight limits (11.10.0-dev):** Provider 1200 kg, Buffer 900 kg, Requester 600 kg, enforced by the container weight system and shown as a Load bar.
   - Buffer Chest: both roles at once, faces NOT pinned. Tops itself up to a per-item `bufferStockTarget` from providers and supplies other requesters; buffer-to-buffer stocking is refused. Authored by setup step 78.
   - **Balance rule:** a buffer requests only up to its stock target. Without that cap a buffer drains every provider it can reach, which is the standard failure mode of this block.

4. **Long-Distance Power Poles** — ~~high-voltage transmission towers~~ *(11.11.0-dev)* — **COMPLETE**
   - `TransmissionTower`: 128 m span, 20 kW, max 3 spans, 4 m local tap. Authored by setup step 83.
   - Spans are registered as manual links, so they cross terrain that ordinary cables may not, while cables keep their strict one-grid-step rule.
   - **Balance rule:** the span is capacity-rated and obeys the network bottleneck, so a thin cable feeding a tower is still the limit.

4b. **Orbital Programme** - ~~grid naming/classification~~ ~~orbital map device~~ ~~station and satellite orbits~~ ~~orbital research gate~~ *(11.13.0-dev)* - **PHASE 1 COMPLETE**
   - `GridIdentity` (name + VESSEL/SATELLITE/STATION), `OrbitalRails` (analytic Kepler orbits that cannot decay), `OrbitalTrackingService` (the data layer), `OrbitalMapScreen` (`M`), `GridSatelliteLab` (orbital research gate).
   - **Design rule:** a satellite is a player-built grid the player DECLARES a satellite. Never a separate entity type.
   - **Design rule:** an orbit is saved as Keplerian elements, never as a pose, so a station reloads at the correct phase for the reload time.
   - **Balance rule:** the map is a researched, expensive device in a Life Support instrument slot. Its stats gate tracking range and telemetry detail, so the tier ladder has somewhere to go.
   - ~~Phase 2: satellite sensor payloads - season tracking, weather tracking, weather influence~~ *(11.14.0-dev)* - **COMPLETE**
   - `GridSatellitePayload`: Sensor Array / Weather Radar / Climate Control Array, all gated on SATELLITE class + committed orbit.
   - **Balance rule:** influence shifts the odds at `PickNextState`, never sets the sky. Combined with `1 - e^-total`, so a constellation steers a climate but can never lock it and every outcome stays reachable.
   - **Honesty rule:** live weather only resolves for the body the player is at. The radar says so rather than fabricating a remote sky; season telemetry is the genuinely planet-wide readout.
   - `Climate Engineering` is flagged `requiresOrbitalLab` - the first shipped node to use the gate.
   - Open: payload upgrades, multi-body sensor networks, and using orbital coverage to extend the map's tracking range.

5. **Configurable Grid Screens / Displays**
   - Multiple sizes: 1×1, 2×2, 4×4, wide banner.
   - Display text, values, bar charts, or live camera feeds.
   - User-friendly setup: click screen → choose data source (power, inventory, speed, trajectory, camera).
   - Customizable font, color, background, border.
   - Can show information from any block on the same grid or connected network.

6. **Trajectory Camera Mode** — ~~predicted path, gravity arc, impact marker~~ *(11.12.0-dev)* — **COMPLETE**
   - `TrajectoryPredictor`: semi-implicit Euler, 0.25 s steps, 45 s horizon, integrating the same scaled radial gravity and block-count drag model the physics step uses. Cached against velocity change.
   - `TrajectoryOverlay`: one shared `LineRenderer` plus a collider-free, distance-scaled impact marker. Colour-coded impact / orbit / escape / clear.
   - Third gate is the WIDE exterior view (second zoom-out), so first-person and the tight chase view stay clean.
   - **Implementation note:** the path raycast must ignore its own grid and the pilot, or every prediction impacts the ship it belongs to.
   - Bound to `J`, rebindable (the roadmap's suggested `T` was already Tool Cycle).
   - Only active when the player is piloting or editing a grid vehicle.
   - First zoom-out from first-person switches to third-person.
   - Second zoom-out with trajectory enabled draws a predicted path:
     - Velocity vector line.
     - Gravity arc.
     - Impact marker on terrain or predicted orbit.
   - Toggled in Settings → Controls → `Trajectory Camera`.

7. **Star Map / Orbit Overlay (`M`)** - ~~system map with all orbits, craft and bodies~~ *(11.13.0-dev)* - **COMPLETE**
   - `OrbitalMapScreen` on `M`: all bodies, all named constructs, live orbital telemetry, true focus-offset ellipses, pan/zoom/focus.
   - Gated on an equipped `OrbitalMapItem` - the map is a researched device, not a free menu.
   - **Implementation note:** name labels are pooled Labels, NOT `MeshGenerationContext.DrawText`, which needs a paint-time font and is not dependable across Unity versions.
   - ~~Open: click-to-set-navigation-target, asteroid fields on the map, and trajectory trails for orbiting bodies.~~ *(12.19.0-dev: persistent click-to-set navigation target with live resolution, the asteroid shell drawn as a ring-plus-rocks region, and analytic trajectory trails on every drawn ellipse)*
   - Legacy design, still accurate:
   - Pressing `M` opens the system map.
   - Shows:
     - All planet and moon orbits.
     - Grid ships currently in orbit or in flight.
     - ~~Asteroid fields.~~ *(12.19.0-dev)*
     - Player bases and landing pads.
   - ~~Click a body to set navigation target.~~ *(12.19.0-dev)*
   - ~~Optional trajectory trails for all orbiting bodies.~~ *(12.19.0-dev)*
   - *(12.19.2-dev: map projects the true XY orbital plane instead of edge-on XZ; every planet draws its solar ellipse; body rings and trails are sampled from the live elements; belt shell draws its inner edge)*
   - *(12.20.0-dev: trajectory visibility toggles per class, zoom-adaptive body markers, moon-label and belt-label declutter)*
   - *(12.21.0-dev: authored per-body hues, pollution readout, larger text, Realistic/Arcade orbit pace at world creation)*

8. **Grid Route Recorder, Calculator & Autopilot**
   - Manually calculate distance, travel time, required thrust, power/fuel cost, and reserve margin to a selected body or waypoint.
   - Uses live ship mass, cargo, batteries, fuel, hydrogen, generation, efficiency, and damage state.
   - Record piloted paths between planets, stations, bases, mines, and docks.
   - Autopilot follows validated routes, avoids hazards, manages braking reserves, and performs configured cargo/charging/refueling stops. *(12.22.0-dev ships the first autopilot leg: seated/unseated fly-to-nav-target cruise with braking curve, stick override/resume and arrival hold. 12.23.0-dev adds warp legs: auto-aim/charge/fire with cruise resume. Hazard avoidance, dock approach, cargo ops and atmospheric legs remain.)*
   - Route safety reacts to weather, gravity, territory, pollution signatures, hostile encounters, and changed ship contents.

#### Improved Features

8. **Grid-based Vehicle Autopilot**
   - Set waypoints for rovers.
   - Auto-mine / auto-deliver loops.

9. **Map / Radar UI** - ~~shows train lines, drone routes, base zones~~ *(11.16.0-dev)* - **COMPLETE**
   - `LogisticsMapScreen` on `L` + `LogisticsMapData`: rail lines/stations/trains, drone routes/ports, inferred base zones, road underlay, per-layer toggles, click-to-centre.
   - **Purpose rule:** the map's job is finding MISSING joins, not decoration. Alerts (station with no track, port with no power, blocked train) are collected at the top of the sidebar.
   - **Design rule:** base zones are INFERRED from logistic chest clusters at the drone port's own 48 m radius. There is no authored base object, and a circle must mean a zone the game genuinely serves.
   - **Implementation note:** gathering is split from painting and runs on a slow tick; symmetric edges (rail links, drone pairings) must emit from one end only or every line draws twice.
   - Open: integration with the star map for seamless zoom from local to cosmic - still deliberately separate, as the two maps use different scales and projections.

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

### 6.5 Version 4.9.0 — Living Worlds — 🟡 PARTIALLY COMPLETE (Ruins 6.29.0)

**Goal:** Make every planet feel alive, dangerous, and worth exploring — with ruins to loot, weather to survive, and enemies to fight.

#### New Content — Ruins Implemented in 6.29.0-dev + Premium 6.31.0-dev

1. **Ruins of a Dead Civilization — ✅ COMPLETED (6.29.0) + PREMIUM 6.30.0**
   - Rare broken warehouses, collapsed factories, and derelict bases scattered across biomes. Initially 0.001 density (user reported no spawns), now **PREMIUM density 0.006-0.0085** in Wasteland/Plains/Steppes/Desert/Forest/Beach with scale 1.2-2.0 → ~5-15 ruins per 10 chunks, large and visible.
   - **6.29.0 MVP:** 4-cube collapsed frame with rusted materials + `RuinChest` loot point (root + child chest colliders).
   - **6.30.0 Premium:** Real crusader bases using **actual building blocks** from game (`Wall_Steel/Iron`, `Foundation_Steel/Iron` from `TieredBlockRegistry`) — mineable for resources (wall tokens → steel plates). Foundations 3x3 grid (9 foundations, 15% missing for rubble), perimeter walls with 18% missing/collapsed, 20% half-height ruined, random lean ±3-5° for decayed feel, interior dividing wall + doorway gap, 8 rubble debris piles (0.3-0.9 scale, rustDark/moss mats, `PlacedBlock` Hp 120 for resource drop), large root `BoxCollider` (size.x*1.1, y*1.2) + `PlacedBlock` marker for scatter overlap prevention, warm point light (1.0,0.72,0.32, 1.4 intensity, 12 m range) for distance visibility — premium exploration cue.
   - Loot containers hold components (steelPlate, ironPlate, copperWire, circuit), fuel (coal), and — most importantly — **damaged blueprint data cores**.

2. **Blueprint / Recipe Unlock System — ✅ COMPLETED (6.29.0)**
   - Some recipes are **not available at game start** (gated via `BlueprintUnlockManager`).
   - Finding a rusted wind turbine nacelle in a ruin unlocks the real nacelle recipe (`Recipe_t90_Nacelle`), gearbox (`Recipe_t90_Gearbox`), blade, tower.
   - Only a small, curated set of recipes are gated this way — enough to slow progress without frustration.
   - Damaged blueprints are consumed on RMB in hotbar → `TryUnlock()` → `BuildFeedbackHud` toast + `blueprint_unlocks.json` persistence. `ResearchManager.IsRecipeUnlocked` now checks blueprint manager first. (Note: original design said research station restoration, we simplify to direct RMB consumption for MVP — can be moved to lab later.)

3. **Planet Resource Registry**
   - Each planet/moon has a fixed resource signature.
   - ScriptableObject-driven: `PlanetDefinition` with gravity, atmosphere, hazards, ores.

4. **New Ore Tiers**
   - Copper, aluminum, nickel, sulfur, uranium, titanium, tungsten, rare earths.
   - Biome-locked deposits on the home world.
   - Planet-locked deposits beyond the home world.

5. **Biome Hazards** - ~~radiation zones~~ ~~toxic atmosphere~~ ~~extreme temperatures~~ *(11.17.0-dev)* - **COMPLETE**
   - `HazardField` samples localised radiation / heat / toxic zones; `HazardWarningHud` is the Geiger warning. Mitigated by Radiation Shielding and hazmat, Heat Tolerance, and a sealed breathing kit respectively.
   - **Core rule:** zones are DERIVED from body-seeded Worley noise, never stored - no save data, no streaming, and existing worlds gain them for free.
   - **Compatibility rule:** the authored planet constant is the floor; zones only ever add.
   - **Design rule:** Worley not fractal noise, at 34% coverage, because a zone must be AVOIDABLE. Fractal noise makes a smear the player can never be sure they have left.
   - Open: hazard-aware route planning, and ore deposits that correlate with radiation zones.

6. **Caves & Resource Nodes** - ~~large, finite ore nodes~~ ~~encourage outpost building~~ *(11.18.0-dev)* - **COMPLETE**
   - Cave carving already shipped in `PlanetField.CaveCarve` (crust-sealed, never below sea). 11.18.0 added the resource-node half: `DeepOreField`, `DeepCoreExtractor`, `DeepSurveyHud`, setup step 86.
   - **Design rule:** a deep node cannot be hand-mined at all. Hand mining / ship drill / extractor stay three distinct verbs, so the new machine does not obsolete the old ones.
   - **Derivation rule:** placement derived from the world seed; only depletion is stored. Legacy saves gain deposits for free.
   - **Balance rule:** finite by design - an outpost has a lifespan, which is what keeps the player surveying and relocating.
   - Open: cave-specific deposits that require descending rather than surface placement, and extractor upgrade tiers.

7. **Fauna / Flora & Livestock** - ~~passive creatures~~ ~~breedable cows/sheep/pigs with food, water, shelter, health, reproduction, population limits~~ ~~hide, wool, milk chains~~ *(11.19.0-dev)* - **LARGELY COMPLETE**
   - Passive fauna (`PassiveAnimal`, `PassiveAnimalSpawner`) already shipped with wander/flee AI and Raw Meat / Hide / Wool drops. 11.19.0 added the husbandry layer: `LivestockHusbandry`, `LivestockPen`, `LivestockPenHud`, setup step 87.
   - **Reuse rule:** husbandry is an additive component on the EXISTING animal, never a second animal class - so wild and farmed herds stay one object and the AI has one implementation.
   - **Design rule:** needs form a chain (hunger/thirst -> health -> production -> breeding), so a neglected pen stops earning well before anything dies. A flat "feed or die" timer gives the player no warning and no reason to care.
   - **Balance rule:** milk and wool come without killing, so keeping animals alive is the profitable choice without a rule enforcing it. Population caps are per-pen.
   - **Implementation note:** attrition damage must bypass `TakeDamage`, which triggers flee. See `PassiveAnimal.ApplyAttritionDamage`.
   - Open: hostile mythical creatures in deep biomes/ruins/volcanic zones (tracked as item 11), flora/crop expansion, and horse breeding on top of the existing `RideableAnimal`.

8. **Environmental Radiation Zones** - ~~low-level radiation areas~~ ~~hazmat/upgrades reduce exposure~~ ~~Geiger counter~~ *(11.17.0-dev)* - **COMPLETE** (see item 5)
   - Open: ruins specifically emitting radiation, on top of the terrain zones.

9. **Environmental Heat Zones** - ~~volcanic areas deal heat damage~~ ~~heat tolerance allows longer exposure~~ *(11.17.0-dev)* - **COMPLETE** (see item 5)
   - Heat zones only form where the surface is already above -10 C, so a frozen moon does not grow lava fields.

10. **Airtight Doors and Vents** — ✅ COMPLETED (9.27.0-dev)
    - Sliding futuristic doors for grid bases.
    - Airtight variants seal rooms for pressurization (doors seal only while fully closed).
    - Vents pump oxygen in or out of sealed spaces (PRESSURISE / DEPRESSURISE / IDLE).

11. **Mythical Enemies & Bosses** - ~~enemy tier determines loot tier~~ ~~named bosses guarantee unique Boss Relic Cores~~ ~~boss relics required for late-game research including Star Builder and Dyson Sphere~~ *(11.20.0-dev)* - **PROGRESSION COMPLETE, BESTIARY ONGOING**
    - Creatures already shipped: Basilisk, Ghoul, Griffin, Ifrit, Karkadann, Manticore, Roc. 11.20.0 added the progression layer: `BossRelicKind`, `BossRelicLedger`, `BossEncounter`, relic-gated research, setup step 88.
    - **Balance rule:** relics are guaranteed and unique per boss, never a rare roll. This is the mechanism enforcing "low-tier farming cannot replace boss progression".
    - **Design rule:** relics are NEVER consumed - a permanent ledger records the encounter, so one relic gates several nodes and a unique boss is never killed twice.
    - **Implementation note:** grant relics in `OnDestroy`; `Damageable.Die` destroys the object the same frame health hits zero.
    - Open: the Leviathan and Cockatrice encounters (Abyss Core is authored but has no boss yet), automated ruin drones, raider vehicles, and richer boss mechanics (formation disruption, telegraphed phases, biome-specific navigation).
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

15. **Prospecting Tools & Geological Scanning**
   - **Handheld Geological Prospecting Scanner (`ProspectingScanner`):** Field acoustic radar probe tool. Right-clicking the ground sends an acoustic wave up to 24 meters into subterranean voxels along the active planetary radial down vector, reporting nearest mineral vein depth and volume via color-coded HUD feedback. (Implemented 9.26.0-dev)
   - **Spherical-Safe Radial Ore Detector (`GridOreDetector`):** Large grid scanning array projecting subterranean cones coreward on spherical planets and curved celestial bodies via `GravityProvider.ActiveBody.UpAt()`. (Implemented 9.26.0-dev)
   - **Screen Telemetry Integration (`IGridDataProvider`):** Grid Ore Detectors broadcast live geological scan telemetry directly to connected `GridScreenBlock` LCD monitors. (Implemented 9.26.0-dev)
   - **Step 59 Setup Wizard:** Non-destructive generation of scanner tool items, craft bench recipes, and registration. (Implemented 9.26.0-dev)
   - Terrain core drill and acoustic seismic survey rig. (Upcoming)

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

18. **Gravity & Orbit Fixes** — 🛠️ WORKING ON
    - Grids are currently not affected by gravity and do not align with the planet surface on placement. This is the primary blocker.
    - All grids, dropped items, and players must experience consistent planetary gravity.
    - No more falling through the world or zero-gravity bugs on surfaces.
    - Realistic orbital mechanics: velocity + altitude = orbit.
    - Atmospheric drag slows low orbits; escape velocity possible.
    - Stable physics for landed grids and docked ships.

19. **Space Ambiance Overhaul** — 🛠️ WORKING ON
    - Space is black, silent, and filled with distant stars and nebulae. Sparse starfield and palette-tinted nebulae are implemented.
    - Nearby planets and moons are visible as proper real-surface spheres.
    - **7.19.0-dev:** eclipse-aware sun glare, restrained lens ghosts, and bounded world-anchored dust particles are implemented; Unity validation is pending.
    - ~~Audio ducking: exterior sounds muted in vacuum while suit, cockpit, UI, and internal-grid feedback remain readable~~ *(12.18.0-dev)* - `VacuumAudio` scales all exterior emitters by listener air pressure; sealed rooms count as air.

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

28. **Pressure & Airtight Service** — ✅ COMPLETED (9.27.0-dev)
    - Detects sealed rooms using grid blocks and airtight doors (bounded flood fill, breach-aware, event-driven re-solve).
    - Tracks oxygen level and pressure per room; charge is carried across hull edits and saves.
    - Vents add or remove oxygen against the grid gas network.

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

1. **Rocket Platform & Rocket Parts** - **RESCOPED, see note**
   - ~~Cargo capsule for item transport~~ *(11.24.0-dev)* - delivered as the Cargo Launch Pad (item 5).
   - **Scope decision:** a buildable multi-stage rocket VEHICLE is deliberately not planned. The game already reaches orbit by building a grid with thrusters and flying it, and a separate rocket entity would be a second large flying thing that is not a player-built grid - the same split the Train System v2 rework (6.4 item 1b) exists to remove.
   - Crew capsule / player travel is therefore already served by piloted grids plus `OrbitalRails`.
   - Open: if rocket PARTS are still wanted, they should be ordinary grid blocks (staged engines, drop tanks) rather than a new entity type.

2. **Space Stations & Orbital Station Hammer Family** - ~~dedicated Hammer family~~ ~~Orbital Construction adds it to the wheel~~ ~~hull, decks, corridors, junctions, reinforced windows, domes, airlocks, docking frames~~ ~~habitat visual language~~ *(11.22.0-dev)* - **PARTIALLY COMPLETE**
   - 8 families via `BuildFamily` (appended, save-safe), `StationPiece`, station-only socket rules, `orbital_construction` node. Authored by setup step 89.
   - **UI rule:** the wheel has GROUPS, not more pages. `TAB` swaps between STRUCTURAL and ORBITAL STATION while the wheel is open; it opens on structural because that is the common case.
   - **Design rule:** station pieces snap only to station pieces - a wooden wall must not be able to close a pressure hull. Between station pieces the rules are permissive so a ring corridor can close on itself.
   - ~~Airtight pieces integrate with room pressure and oxygen simulation~~ *(11.23.0-dev)* - `StationRoomSolver` flood-fills sealed compartments from the placed pieces; `StationLifeSupport` (setup step 90) produces the air. Plugged into `RoomAtmosphereService` so player survival and HUDs work unchanged.
   - **Design rule:** a sealed volume starts EMPTY and air must be produced and maintained - pressurisation is an ongoing power cost, not a property of geometry. A DOCK collar deliberately does not seal.
   - Open: functional docking ports for ships and cargo capsules, solar arrays/radiators/gravity ring modules, and exterior armour.

3. **Asteroid Mining** - ~~asteroid fields accessible from orbit~~ ~~mining ship grids with drills and cargo~~ ~~platinum, rare earths, ice chunks~~ *(fixed and verified 11.26.0-dev)* - **COMPLETE**
   - `SpaceAsteroidField` spawns and culls fields in open space with cluster families; `SpaceAsteroid` is a `Damageable` with a real ore payload, so any drill or weapon mines it. Ore pool covers Iron, Nickel, Silicon, Cobalt, Gold, Platinum and Ice.
   - Mining ships need no special type - a grid with a `GridDrill` and cargo is one, the same "no bespoke entity" rule the rail rework applies.
   - **Bug that hid this:** the keep-out margin (`radiusKm * 2`) exceeded the spawn ring, so nothing ever spawned inside a planet's frame. Keep-out is now a flat surface clearance, which is what the rule actually meant.
   - **Implementation note:** asteroid colliders MUST be convex - they move, and non-convex mesh colliders are ignored by sweeps and by other non-convex colliders.
   - **Scale rule:** planets are only 6-8 km in radius, so rocks are 3-11 m. Anything larger reads as a moon rather than as minable, and remesh cost grows with the cube of the radius.
   - ~~Rocks are voxel bodies you tunnel into, not props you break~~ *(11.27.0-dev)* - `AsteroidVoxelBody`, stone with interior ore veins, carved by the normal mining tools.
   - ~~Smooth iso-surface rocks with varied shapes and planet materials~~ *(11.28.0-dev)* - meshed by the planets' own `SurfaceNetsJob`; ellipsoid stretch, noise and gouges so they are not all spheres.
   - Open: dedicated asteroid-only ores, and richer rocks further from the sun.

4. **Satellite Network** - ~~scan planets for resource deposits~~ *(11.21.0-dev)* - **PARTIALLY COMPLETE**
   - `SatellitePayloadKind.ResourceScanner` maps deep ore deposits within 6 km of the satellite's ground track; results feed the payload console and the `L` logistics map. Research: `Orbital Prospecting`. Authored by setup step 84.
   - **Design rule:** surveys from the SATELLITE's position, not the player's - surveying under the player would make the satellite a middleman for the hand scanner.
   - **Disclosure rule:** the map reveals only surveyed deposits, never the whole field.
   - **Design rule:** the scanner is a survey instrument with no weather hardware, so it is not a strict upgrade of the other payloads and does not retire them.
   - Open: relay power or data between worlds - needs the interplanetary power/data layer that does not exist yet.

5. **Interplanetary Cargo Rocket** - ~~schedule launches between planets~~ ~~carry bulk resources~~ *(11.24.0-dev)* - **COMPLETE**
   - `CargoLaunchPad` (named, SEND/RECEIVE, full-load launches), `CargoFlightRegistry` (central, keeps flying while worlds are unloaded), `CargoPadHud` (console + flight board). Setup step 91.
   - **Scope rule:** implemented as PADS, not a rocket vehicle. Grids already fly to orbit; a second non-grid flying entity is the split the rail rework is removing. See item 1.
   - **Simulation rule:** flights live centrally, not on pads, because a flight outlives its endpoints' loaded state.
   - **Safety rule:** missing pad, full hold and partial delivery all hold or retry - cargo is never silently destroyed.
   - Open: multi-item manifests, and routing cargo onward from a receiving pad into a train or drone network automatically.

#### Improved Features

6. **Star Map UI**
   - Visual map of the solar system.
   - Shows known resources per body.
   - Plan routes for rockets and drones.

7. **Atmospheric Entry / Landing**
   - Rockets descend to planetary surfaces.
   - Landing pads required for safe touchdown.

#### Code Improvements

8. **Scene/Zone Streaming** - ~~load planets, orbits and asteroid fields as separate zones~~ ~~persistent base state across zone transitions~~ *(11.29.0-dev)* - **COMPLETE**
   - Chunk streaming, `ChunkStorage` terrain persistence and body-anchored placed blocks already existed; 11.29.0 added the missing half - production that survives the player leaving, via `OfflineClock` catch-up.
   - **Cost rule:** catch-up on wake, never background ticking. Cost is zero while away and does not scale with how much the player has built.
   - **Clock rule:** the saved cosmic clock is the only valid time source across a session boundary.
   - **Balance rule:** offline is 45% of live, capped at 12 h, so presence always beats absence.
   - Open: extending catch-up to the remaining producers (biofarm, refineries) - the pattern is one `OfflineClock` field plus a settle-on-wake call.

9. **Interplanetary Save Data** - ~~save orbital stations~~ ~~asteroid positions~~ ~~rocket schedules~~ ~~save schema v2~~ *(11.30.0-dev)* - **COMPLETE**
   - Orbital stations already saved as Keplerian elements (11.13.0), cargo schedules in 11.24.0, and asteroid positions are DERIVED from the world seed so saving them would be wrong, not missing.
   - Schema v2 adds `SaveData.schemaVersion` plus a migration hook; legacy saves read as v1 and migrate in place.
   - **Safety rule:** a failed load must never be written back. Autosave previously overwrote unreadable-but-recoverable worlds with an empty one.
   - **Recovery rule:** the `.previous` sidecar the atomic save already maintains is now actually read on a failed primary load.
   - Open: a player-facing "restore from backup" option in the menu, rather than recovery only happening automatically.

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

8. **Heat System for Grids** *(shipped 9.29.0-dev to 9.31.0-dev: block temperature, thruster self-heat and plume heating, thermal damage, and in 9.31.0 every machine is an `IHeatSourceBlock` (hydrogen engines, portable reactors, furnaces, maritime engines and generators, exhaust stacks with a real gas plume) and every block family has its own heat tolerance shown in tooltips and panels)*
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

11. **Cockpit Heat Indicator** *(9.30.0-dev: the cockpit environment line shows hull band and peak plate temperature in degrees, green/amber/red, once the hull is warm)*
    - Shows current external heat in degrees.
    - Green = safe, yellow = approaching limit, red = taking damage.
    - Linked to ship thermal state and armor heat tolerance.

12. **Player Heat UI** *(9.30.0-dev: shipped as the suit TMP strip driven by `PlayerSuitThermal`; damage threshold marker moves with installed Heat Tolerance tiers)*
    - Shows player temperature in degrees.
    - Green/yellow/red indicator.
    - Excessive heat causes damage over time.
    - Heat tolerance armor upgrades raise the safe threshold.

14. **Concealed-Space Atmosphere & Exhaust Simulation** *(shipped 9.32.0-dev on the 6.12.0 foundation: `GridRoom` tracks trapped waste heat and exhaust as its own atmosphere, `ThermalService.ReportWasteHeat` feeds it from every running machine, a blocked-in exhaust stack dumps its stream into the volume instead of the sky, an engine resolves its combustion air through one shared `CombustionAirRules` contract — piped O₂, compartment air, or an open intake side on a breathable world — and the new `GridExhaustScrubber` pumps heat and foul gas overboard while the new `GasVent` destroys the gas at the end of a run — with the follow-up round's hookups and readouts shipped too, and the deferred ventilation balance closed by 9.33.0-dev)*
    - Enclosed/concealed volumes (engine rooms, tanks, caves sealed by blocks) track their own gas composition. *(done: `GridRoom.HeatLoadC` / `ExhaustHeatC` / `TemperatureC`, solved energy-first against the volume's own heat capacity and hull)*
    - Exhaust gas pumped into a concealed space **heats the space up**; beyond a threshold it damages and destroys surrounding blocks (engine rooms need ventilation, not just a pipe). *(done: the compartment's air is what the blocks inside it slew toward, and `ThermalRules.RoomDamageHeatC` at 260 °C above the outside air is where the volume starts consuming its own contents; plumes are suppressed and stacks superheat when blocked in)*
    - Burning engines **deplete the room's oxygen**; below the critical O₂ fraction engines stall (no combustion air) and the player **cannot breathe** inside the space (links into the player oxygen/breathing system). *(done for engines: `GridRoom.DrawCombustionOxygen`, `PressureRules.SupportsCombustion`, stall below `CombustionAirMinAtm`; the suffocation path stays on the existing 9.27.0 breathable-room rule, which the engine can never push under)*
    - An engine picks **one** intake: a plumbed oxygen line owns the intake exclusively, a sealed compartment burns its own air at 10 percent down, and an unenclosed engine on a breathable planet breathes through the open cell on its facing side at up to 25 percent down. What a **dry piped line** costs is the player's call — the panel's FALLBACK ON / STRICT switch either lets the engine drop back to room or planet air at that source's price, or holds it to the line and stalls with `O2 LINE EMPTY`; the setting is per-engine and saved with the block. *(done: `CombustionAirRules.Resolve(block, airIndependent, pipeConnected, pipeCanFeed, bufferHasOxygen, allowFallback)`, `GridMaritimeEngine.TickOxygen`, quality multiplied into `node.MaxTorque`)*
    - Exhaust is a **product, not a tax**: a run can be terminated in a `GasVent` that destroys whatever arrives (draft when unpowered, extractor when powered) so a sealed engine room can pump its foul gas out without owning a vessel big enough to hold it. *(done: `Scripts/Gas/GasVent.cs`, `GridGasNetwork.FillGasFrom` storage-then-vent, `GridExhaustPipe` tap fills tanks first and spills the rest to the vent)*
    - Synergies: Closed-Cycle AIP modules ignore room air entirely (closed-loop subs), radiators dump room heat, quality-of-life goals: vents/fans/bulkhead doors that exchange gas between concealed spaces and atmosphere. *(done: AIP skips the room model completely, Super-Cooler Radiator Jackets dump into the compartment, bulkhead vents already swap oxygen, an open hatch vents heat for free, and a vent set into a compartment moves gas from the pipes into that room's air; dedicated fan throughput tuning remains optional)*

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
    - *(12.38.0-dev ships the interplanetary prototype: `GridWarpGate`, paired by code, aperture transit to the partner's rendezvous; authored by Setup Step 98. 12.39.0-dev starts the successor: player-built portals — frames seal the aperture, controllers pair by name and code, Setup Step 99. Interstellar pairing and portal networks remain open.)*

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
6. **Clear console logging** — every change is reported so the team can verify nothing was lost.

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
   - Single straight chute only; do not create chute variants.
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
13. Run **Tools > Voxel Engine > Voxel Engine Setup > 48. Build Armor Stations + Timed Upgrades**. It non-destructively creates/repairs the six armor definitions, Armor Station, anvil-style Armor Upgrade Station, module items, recipes, and research links; do not manually create those prefabs or assets.
14. Run Step 48 a second time and confirm the Console reports preserved/repaired links without resetting recipe costs, craft times, materials, or custom prefab children.
15. Create jetpack prefab with fuel slot and upgrade tiers through the applicable Voxel Engine Setup step.
16. Create hazmat suit and Hazmat module content through Step 48; verify the module appears in the Armor Station after its research unlock.
17. Create space helmet and oxygen tank prefabs with visor toggle through the Voxel Engine Setup workflow.
18. Create geiger counter item/tool.
19. Create painting tool item and 15 material finish variants.
20. Add fall damage system and oxygen underwater system.
21. **Run setup wizard steps (non-destructive)**
    - Step 13 for maritime prefabs after drivetrain/port changes. **7.3.2-dev:** run it twice to refresh the v26 Watertight Shaft Housing mesh plus gearbox/generator/propeller/shaft port markers without overwriting ship balance or custom prefab work. Step 13 explicitly repairs the Mechanical Belt recipe/Hydro-Mechanics unlock and creates the Resources persistence catalog that prevents Portable Battery and Portable Hydrogen Tank save-loss. The removed Encased Chain Drive content stays absent by design.
    - Step 18 for power/vehicle/combat/painting blocks.
    - Step 48 for the complete armor workflow, including both station prefabs, items, recipes, research links, timed installation setup, and T1→1-slot/T1-module through T6→6-slot/T5-module progression gates.
    - Preserve existing grid power values, weapon damage, recipe costs, craft times, materials, and custom prefab work.

### For 4.8.0 (Logistics 2.0, Screens & Trajectory)

1. Design rail blocks and train vehicle grid.
2. Build drone port prefab with landing pad.
3. Implement logistics chest models.
4. Create map/radar UI.
5. Build configurable screen prefabs in multiple sizes.
6. Add camera block that feeds render texture to screens.
7. Add trajectory camera rig and predicted path renderer.
8. Build star map UI with orbit lines and body labels.
9. ~~Build route recording, waypoint editing, destination calculation, ship capability report, and
   Autopilot controls.~~ *(9.34.0-dev: recording, waypoint capture, destination calculation, the
   capability report and the panel are shipped; the Autopilot controls remain open on purpose — a
   route can be costed and flown by hand, nothing flies it yet)*
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
9. 🚧 **6.43.0-dev** delivered passive livestock prefabs (Cow/Sheep/Pig) + spawner + products (meat/hide/wool). Remaining: breeding needs, husbandry definitions, and population limits.
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
9. ~~Create heatshield block prefab.~~ *(9.29.0-dev, Step 61)*
10. ~~Add heat tolerance values to all grid block descriptions.~~ *(9.31.0-dev)*
11. ~~Implement heat generation for engines, thrusters, reactors, and exhaust pipes.~~ *(9.29.0 to 9.31.0-dev)*
12. ~~Build cockpit heat indicator UI.~~ *(9.30.0-dev hull line; 9.32.0-dev adds the engine room line)*
13. ~~Build player heat UI with green/yellow/red indicator.~~ *(9.30.0-dev suit strip; 9.32.0-dev reports the crew's compartment on the same panel)*
14. ~~Implement atmospheric entry heat simulation.~~ *(9.29.0-dev; concealed-space exhaust and room heat closed by 9.32.0-dev, Step 63)*
14b. ~~Grid Route Recorder & Energy Calculator: manual route calculation and recorded routes.~~ *(9.34.0-dev, Step 65 — distance, travel time, gravity wells, atmosphere segments, required thrust, power and hydrogen use, reserve margin and named warnings, with routes saved as waypoint lists on the grid. Autopilot remains open.)*
15. Build coordinate Jump Drive prefab, charge/range calculator, safe-arrival validation, destination UI, and Autopilot route integration. *(prefab, charge/range maths and safe-arrival validation shipped earlier with `GridWarpDrive`; 12.23.0-dev ships the Autopilot legs for nav-target flights (auto-aim/charge/fire with cruise resume); 12.24.0-dev adds the internal-battery fuel model (pooled range, proportional drain, persisted) and the drive panel. 12.32.0-dev ships the destination-select UI for charted bodies and powered beacons on the drive panel. route-book autopilot warp legs shipped 12.37.0-dev — 9.34.0-dev shipped the route book both build on)*
16. Build empire dashboard UI.
17. **Run setup wizard step (non-destructive)**
    - Step 22 for planetary bases, exo-alloys, nuclear, radiation, and heat systems.
    - Step 63 (9.32.0-dev) authors the engine room atmosphere set, Step 64 (9.33.0-dev) tunes its
      ventilation against compartment volume, and Step 65 (9.34.0-dev) authors the Route Recorder and
      Nav Plotter for the route book; all three are re-runnable and preserve authored balance.
18. ~~Ship the **Grid Inspector Overlay** (heat / damage / centre of mass): one shared overlay pass,
    three modes on one rebindable hotkey, three research nodes in sequence.~~ *(9.37.0-dev —
    `Scripts/UI/GridInspectorHud.cs` runs one shared per-renderer `MaterialPropertyBlock` pass that
    tints block renderers and restores them on exit, degrading to the 24 blocks nearest the camera
    above 240 blocks; `GameSettings` gains the `GridInspector` action, default K; Setup Step 68
    authors INTEGRITY SCAN / THERMAL SCAN / CENTRE OF MASS under Grid Utilities. Unity validation of
    the pass budget and the degrade readout is pending.)*
19. ~~Convert crude into a **column of products** on a machine built for it.~~ *(9.38.0-dev —
    the playtest decided the column is a dedicated plant, not a refinery refit: the NEW
    `DistillationPlant` block carries one feed tank plus one typed tank per product (LPG, naphtha,
    kerosene, diesel, gasoline, heavy fuel oil), each readable by its own run, with an analog dial
    above every outlet and inlet; the refineries keep their plastics work and lose the old fuel
    chain. `LiquidType` gained the five new liquids with densities and gauge colours. Still open
    within this design: per-world crude assays from `OilSiteSampler`, freezing points, where
    Refined Oil is produced from now on, and the burn-value table the fuel-ladder round needs.)*
20. ~~Spend the fractions: recipes and engine fuel preferences per fraction, with `RefinedOil` recipes
    kept alive through a conversion step so an old base does not wake up broken.~~ *(9.39.0-dev —
    `LiquidType` gains `BurnEnergyMJPerL()`, `FreezingPointC()`, and `IsCombustible()`; `StationaryMaritimeEngine`
    and `GridMaritimeEngine` burn fractionated cuts with realistic energy and efficiency scaling)*
21. ~~Author the **Flare Stack** (small + tower) as a gas/liquid run terminator with oxygen draw, room
    heat and an optional waste-heat recovery attachment.~~ *(9.39.0-dev — `Industrial/Prefabs/FlareStack.prefab`
    derrick tower and Large/Small `GridFlareStack` vents dispose of excess liquids and gases, convert
    thermal energy to electric watts via waste-heat recovery, and simulate room oxygen draw; Step 70)*
22. ~~Author **asphalt roads** as terrain-placed surface blocks (straight, bend, junction, crossing,
    ramp) with movement, traction and wear rules~~, then the road *network* readout that autopilot and
    drone routing hang on. *(9.41.0-dev, Step 72 — surface, drag-to-pave, per-run wear, grade pricing,
    movement/traction/grip and repair all shipped, but **without a shape set**: the cell drapes the
    ground so the drape is the ramp, and a neighbour mask only decides where the kerb rises. 9.44.0-dev
    added the crossings (Step 73), 9.44.1-dev the drawbridges, 9.44.2-dev their automation, 9.44.3-dev
    the wheel fix that made the drawbridge card selectable, 9.45.0-dev the freeboard deck, the stone
    substructure and commit-time grading — all
    runtime behaviour with no new setup step of their own. 9.46.0-dev adds approach barriers,
    9.47.0-dev driver guidance, and 9.48.0-dev named loaded-network snapshots.
    Still open: global network membership, traffic scheduling, drones, creature and stamina bonuses,
    rolling resistance and roadside lamps/furniture; the per-span automation switch shipped in 9.49.0-dev.)*
23. Author the **Engine Works** block: parameter set, balance sheet, refusal reasons, craft cost by
    specification, artefact requirements at the large end, and template storage.
24. ~~Author **grid waymarks and the named connector block** (the cross-grid power/liquid/gas/item
    bridge is the hard part; it does not exist yet), then the **auto-run shuttle loop** on top of the
    existing autonomous thrust channel, with armed stop conditions and a reservation rule per leg.~~
    *(9.35.0-dev, Step 66 — the bridge was the hard part, and it now exists in
    `GridConnectorBlock`; still open: terrain avoidance, dock approaches, cargo schedules)*
25. ~~Author the **static refuel pad** for ground bases — a waymark you place on dirt, not on a hull.~~
    *(9.36.0-dev, Step 67: `Scripts/Navigation/StaticRefuelPad.cs`, `IRefuelPad`, metered
    `PowerConsumer` draw, and a pad that is a real member of the base's power, fluid, gas and item graphs
    rather than a block standing near them; ground rigs are served because a car here is a grid with
    wheels. Still open: the pad pumping back into a world run, and cargo schedules.)*
26. **Run setup wizard step (non-destructive)** for 18–25 as each ships; Steps 66–72 are used
    (69 = 9.38.0-dev's Distillation Plant content, 70 = 9.39.0-dev's Flare Stack content,
    71 = 9.40.0-dev's Catalytic Cracking & Petrochemicals content, 72 = 9.41.0-dev's Asphalt Roads
    content) and each of these features takes its own step.

### For 5.2.0 (Architect Era)

1. Design world forge megastructure prefab.
2. Create custom planet creation UI (body type + resource signature).
3. Implement resource-signature validation rules.
4. Build staged Dyson solar-swarm and sphere construction prefabs, orbital lanes, beam relays, and control UI.
5. Build the Star Builder / Stellar Forge megastructure and stellar safety UI.
6. Author Boss Relic Core items and relic-gated Architect research nodes.
7. Add warp gate prototype prefab. *(12.38.0-dev — `GridWarpGate` + Setup Step 98: prefab, item, recipe, research.)*
8. Finalize save schema v2 migration for boss progression, relics, custom stars, and Dyson construction stages.
9. **Run setup wizard step (non-destructive)**
   - Step 23 for world forge, Star Builder, Dyson Sphere, boss relic gates, and megastructures.

### For 11.0.0-dev (Parked Hulls, the Water Probe, and the Tank-Only Jack Pump)

1. Replace `Scripts/Persistence/WorldStatePersistence.cs`, `Scripts/WaterSim/FluidManager.cs`, `Scripts/Crafting/Pumpjack.cs`, `Scripts/Crafting/Furnace.cs`, `Scripts/Crafting/ElectricFurnace.cs`, `Scripts/UI/MachineUIs.cs`, `Scripts/UI/GameUIController.cs` and `Scripts/Editor/VoxelEngineSetupWindow.cs`, and add `Scripts/Editor/PumpjackTankSetup.cs`. Let Unity compile; the console should be clean.
2. Run `Tools > Voxel Engine > Voxel Engine Setup`, then press **4. Build Crafting Content** and **10. Build Industrial Content**. These repair the smelting recipe assets: `Smelt_Iron`, `Smelt_Copper`, `Smelt_Steel` and `Smelt_Glass` are on disk with a null `input` and `output`, so no furnace can match them. Expect one `[Setup] Smelt_Iron: Iron x1 -> Iron Ingot x1 in 4s.` line per recipe. Both steps are non-destructive - they load the existing asset and repair its links.
3. Then press **76. Convert the Jack Pump to a Tank-Only Crude Producer**. It is non-destructive: an authored tank capacity, litres per batch, power draw, scan depth or scan radius is never reset, and every change it makes is logged. It finds the prefab through the placed-block reference on `Block_Pumpjack` first, then the authored `Industrial/Prefabs/Pumpjack.prefab` path, then any prefab carrying a `Pumpjack` component - and it logs which one it used. If none exists it stops and says so rather than writing a stub, so run the industrial content step first. The block's own description is re-authored by the industrial step: crude goes into the tank, no barrels, and the Pirate Jack Pump Head is still required to build one.
4. Park a ship or rover on the ground, save, quit and rejoin. It must be exactly where it was left, and it must stay there on the next two rejoins. Before this round it sank a little further each time.
5. Sail or walk a hull through water with the maritime propulsion running. The console must be clean: the `SphereChunkGenJob ... JobHandle.Complete()` exception that came out of the water probe every physics step is gone.
6. Open the Jack Pump panel. It now shows a crude tank gauge and no item slots. Right-click it with a liquid canister to draw crude off, or pipe the tank into the refinery. The pump draws power, stops when the tank is full, and needs crude under the derrick.
7. Start a Jack Pump batch, save mid-cycle and rejoin: the tank holds the crude it had, and the stroke picks up rather than starting over.
8. Put ore and fuel in a Furnace. The progress bar must move. If it does not, the console now logs one line per change and it says which of three cases this is: `no smelting recipe is assigned to this furnace` (the prefab has none), `N smelting recipe(s) assigned but M of them have no input or output item, so they can never match - re-run the crafting content setup step to repair the links` (the assets exist but lost their links), or `N smelting recipe(s) assigned, all linked` (the recipes are sound and the reason at the front of the line - No power, No input, No recipe for this input, No fuel, Fuel slot holds something that does not burn, Output full - is the real one). Send that line and the fix is determined by it.
9. Load a world saved before this round. Hulls restore at their saved scene velocity, so a hull that was parked comes back parked. A pumpjack's crude and part-batch come back through the machine-process record; barrels left in its old slots do not, because those slots no longer exist.

### For 10.2.0-dev (Smelter and Pumpjack Process Persistence)

1. Replace `Scripts/Crafting/MachineProcessState.cs`, `Scripts/Crafting/Furnace.cs`, `Scripts/Crafting/ElectricFurnace.cs` and `Scripts/Crafting/Pumpjack.cs`. Let Unity compile; the console should be clean.
2. Run `Tools > Voxel Engine > Voxel Engine Setup`. This round authors no prefab, item, recipe or research node, so the run is only the standing non-destructive check. No step needs to be re-run.
3. Load a Furnace with ore and fuel and let it get part-way through a batch, then save and quit. Rejoin: the progress bar is where it was, the fuel bar is still burning down from the same point, and the batch completes without a second fuel item being eaten.
4. Turn an Electric Furnace OFF from its ENABLED pill, switch auto-pull ON, save and quit. Rejoin: it is still OFF and drawing no power, and auto-pull is still ON. Before this round both reset to their prefab defaults on load.
5. Start a Jack Pump cycle over a Pirate oil node, save mid-cycle and rejoin: the walking beam picks the stroke back up rather than starting the 14 s lift over.
6. Load a world saved before 10.2.0-dev: every machine loads idle and on its prefab defaults, with no console error. A missing record is the expected legacy case.

### For 10.1.0-dev (Movable-Grid Body Anchor)

1. Replace `Scripts/Persistence/WorldStatePersistence.cs`. Let Unity compile; the console should be clean.
2. Run `Tools > Voxel Engine > Voxel Engine Setup`. This round authors no prefab, item, recipe or research node, so the run is only the standing non-destructive check that every earlier authored asset is still connected. No step needs to be re-run for the new save fields to work.
3. Park a ship or rover on the ground near your base, note which way it faces, save and quit.
4. Rejoin: the hull must be where you parked it and facing the same way. The load line now reads `[WorldState] Loaded N tiered + M blocks + G movable grids (A from a body anchor) from ...`, and `A` must equal the number of hulls you had.
5. Rejoin twice more, with time passing between joins so the planet has orbited on: same spot, same heading, every time.
6. Save a hull drifting in deep space, then rejoin: it comes back where it was, with `0 from a body anchor` for that hull. A hull with no anchoring body keeps using its scene coordinate, which is what it did before this round.
7. Load a world saved before 10.1.0-dev: every hull restores exactly as it did before, at its scene coordinate, with no new console warning. A missing anchor is the expected legacy case.
8. If a hull still comes back in the wrong place, send the `[WorldState] Loaded ...` line and any `[WorldState] A movable grid ...` warning from the load — the warning names the body and the reason.

### For 10.0.0-dev (Stored-Chunk Payload)

1. Replace `Scripts/Persistence/ChunkSaveData.cs`, `Scripts/Persistence/RegionFile.cs`, `Scripts/Persistence/ChunkStorage.cs` and `Scripts/Persistence/ChunkStoreIdentity.cs`. Let Unity compile.
2. Load the world that was broken. Expect one `[ChunkStorage] ... (store format 1 -> 2) - moved N stored region file(s) to a 'stale_' folder ...` line, then the normal verified line on every load after it.
3. Return to where you stood when you left: the ground is there, the speckled slabs are gone, and the blocks restored from `world_state.json` still stand where they were placed.
4. Rejoin twice more: the seed, the store state and the terrain must be identical, with no `[RegionFile]` or `[ChunkStorage]` warning in the console.
5. Delete `stale_<utc>/` once the world has been healthy for a session — it holds terrain that was never whole.

### For 9.59.0-dev (Chunk Store Identity Guard, Placed-Block Body Anchor)

1. Replace `Scripts/Persistence/ChunkStoreIdentity.cs` (new, with its `.meta`), `Scripts/Persistence/ChunkStorage.cs`, `Scripts/Cosmos/SphereWorld.cs` and `Scripts/Persistence/WorldStatePersistence.cs`. Let Unity compile.
2. A world that is already broken: close the game, open `VoxelWorlds/<worldName>/Bodies/<BodyName>` (the folder the console prints as `[ChunkStorage] World folder:`), rename it to `<BodyName>_old` and load the world. The chunks regenerate; blocks, machines, grids, containers and the inventory come back from `world_state.json`.
3. Every load prints one line: `[SphereWorld] World '<world>' streaming '<Body>' (seed N, store <Adopted|Verified|Quarantined>, M region file(s))`. On an existing world the first load reports `Adopted` (it predates the guard) and writes `store.json`; later loads report `Verified`.
4. Place a block, save, quit, rejoin: the console must report `[WorldState] Restored N placed block(s) — M from a body anchor, ...` and the block must stand where it was placed.
5. If a join still breaks, send the `[SphereWorld] World ... streaming ...` line, the `[ChunkStorage] World folder:` line and any `[ChunkStorage]` warning from both a good join and the bad one — the seed and the store state are the whole question.

### For 9.58.1-dev (Compile Fix)

1. Replace `Scripts/Persistence/WorldStatePersistence.cs` and `Scripts/Player/PlayerSpawner.cs`; the rest of the round is unchanged. Let Unity compile — the console should be clean.
2. Continue with the 9.58.0-dev acceptance run: Step 75 for the universal modules, the Catalytic Cracker panel, and three rejoins from your base.

### For 9.58.0-dev (Rejoin Spawn, Live Reactor Panel, Universal Modules)

1. Replace the eight edited scripts plus the new `Scripts/Editor/UniversalUpgradeSetup.cs` (and its `.meta` if meta files are tracked). Let Unity compile.
2. Run `Tools > Voxel Engine > Voxel Engine Setup` and click **75. Author Universal Machine Upgrade Modules**. It creates only what is missing; a missing ingredient stops it by name instead of authoring a module nobody could craft. Existing multipliers, icons, recipe quantities and research settings are preserved.
3. Search crafting near an Assembler for `Machine Speed Module` / `Machine Efficiency Module`; if they are absent, research **Advanced Manufacturing** first (the recipes are gated by that node).
4. Shift-click one of each into an Oil Refinery's upgrade slots and check the multiplier readout reads x1.25 throughput / x0.8 power draw before a new batch starts.
5. Watch a Catalytic Cracker's panel while the reactor warms: temperature, catalyst bed and efficiency should move every frame, and scrolling the panel should never be interrupted.
6. Save and quit at your base, then rejoin three times: same spot, same planet, ground streamed under you, no deep-space line in the console, and the base exactly where you left it.
7. Fly into deep space, save and quit, then rejoin: same place in space, zero-g, no falling through a planet.
8. Load a world saved before 9.58.0-dev: the bed/world-spawn path places the player, the system starts at t = 0 as before, and no console error is reported.

### For 9.57.0-dev (Machine Process Persistence)

1. Replace the eight edited scripts plus the new `Scripts/Crafting/MachineProcessState.cs` (and its `.meta` if meta files are tracked). Let Unity compile.
2. Run `Tools > Voxel Engine > Voxel Engine Setup`. This round authors nothing, so the run is only the standing non-destructive check that every earlier authored asset is still connected — no step needs to be re-run for the new save record to work.
3. Fill a Distillation Plant's feed tank, let a batch finish, save and reload: feed and all six cuts return at their levels, the panel keeps its recipe and progress, and the world dials ease back to the same readings.
4. Heat a Catalytic Cracker and let the bed drop, save and reload: reactor temperature and catalyst bed percentage return, and cracking efficiency reads the same value before the first new tick.
5. Shut a Flare Stack, enable waste-heat recovery, target kerosene, save and reload: all three settings return and the status line still reads Shut.
6. Install a speed and an efficiency module in an Oil Refinery, save and reload: the multipliers are already applied before any new batch starts.
7. Pick a recipe on a ship refinery, save and reload: the panel still shows that recipe instead of Auto.
8. Load a world saved before 9.57.0-dev: every machine loads empty and idle with no console error. A missing record is the expected legacy case.

---


## 11. Added Roadmap Requirements — Save Safety, Mobility & Multiplayer

### 11.1 Player Save Safety — ✅ COMPLETED
- Player position is captured only while the player object, Inventory, and a valid planetary/space position still exist.
- A missing player record is ignored safely and is never interpreted as a static block position.
- Invalid player coordinates now fall back to a safe bed/world/body spawn without overwriting the last known-good save.
- **10.0.0-dev:** the stored chunk payload is the whole chunk. `Voxel` is a three-byte struct (`density`, `material`, `waterLevel`) and the store wrote and read two bytes per voxel — a leftover from before 9.16.0 — so a third of every stored chunk (z-major slices 23-33) was never on disk, with the CRC covering the truncation so nothing ever complained; on load that third kept whatever the recycled chunk held, which is the speckled terrain above and the hole the player fell through. The payload is now sized from `UnsafeUtility.SizeOf<Voxel>()`, `RestoreInto` refuses a short payload, and `RegionFile` V4 plus identity format 2 quarantine every pre-fix store instead of reading it.
- **9.59.0-dev:** a placed block is saved the same way its owner is — a body anchor (`hasBodyAnchor` / `anchorBody` / `anchorLocalX/Y/Z`, additive) resolved through the body on load, with the scene coordinate kept as the fallback and the diagnostic; the per-body chunk store writes a `store.json` identity (seed, radius, base height, sea, continent/mountain scale, body name) and quarantines stored chunks that belong to another field instead of loading them into this one.
- **9.58.0-dev:** the saved pose is written as a body anchor (`hasAnchor` / `anchorBody` / `anchorLocalX/Y/Z`) instead of a raw scene coordinate, so it survives a rebase, a frame switch and a warp; a scene position inside a celestial body is refused whatever the active frame is; the cosmic clock is saved and restored before any cosmic coordinate is resolved; and the floating origin is re-anchored only for a position that is clear of every body, with the frame and the voxel streamer re-pointed at the ground the player lands on.
- **6.14.7-dev:** `WorldStatePersistence.RestorePlayer` validates saved player position and rotation before touching the live player transform, restores inventory at the safe fallback when needed, and logs the recovery as non-destructive.
- **6.80.3-dev:** `PlayerSpawner` now validates the complete player volume for water on fresh, bed, saved, and respawn targets; wet candidates stream/search for dry terrain while control is disabled. PlayerController also gains capped uphill terrain assistance and post-move footing recovery to prevent mountain-mesh penetration. Unity testing confirmed this behavior works in Unity.

### 11.2 Ice Friction — ✅ COMPLETED
- Ice surfaces use low-friction physics for players, static loose blocks, and movable Grids.
- **6.21.0-dev:** Player movement detects solid Ice voxels underfoot and applies low-friction, reduced-steering movement on ice.
- **6.21.1-dev:** Physical dropped items use low Rigidbody damping and delayed settling on ice, and movable Grids reduce dampener/angular braking while touching Ice.
- **6.21.2-dev:** Movable Grids keep gravity active for a short grace period after ice contact, dampen only tangent drift during that recovery, and fall back toward the planet instead of hanging upward after tilting off ice. Current ice-friction scope is complete after Unity Unity validation.

### 11.3 Jetpack Families — 🛠️ WORKING ON
- Add two dedicated jetpack equipment slots.
- Hydrogen boost pack: Shift activates a boost; hydrogen-only flight remains possible with no power draw.
- Atmospheric jetpack: atmospheric propulsion role.
- Hybrid jetpack: atmospheric plus ion operation using power only.
- **6.22.0-dev:** Added `PlayerEquipment` with two jetpack slots, `JetpackItem` data assets for Hydrogen Boost / Atmospheric / Hybrid families, quick-equip from the active hotbar stack, Step 12 item/recipe generation, and PlayerController flight gating/speed modifiers from equipped jetpacks.
- **6.22.1-dev:** Jetpack slots now appear in the Inventory UI and persist through save/load via an additive player save field.
- **6.22.2-dev:** Jetpack slot UX polish: shift-click from hotbar/backpack equips into the first free slot, shift-click from equipment returns to inventory, the Sort button is removed from equipment slots, the Jetpack Bay panel has an ONLINE/EMPTY visual state, and flight mode shuts off immediately when the last usable jetpack is removed.
- **6.22.3-dev:** Jetpack Bay no longer uses a heavy boxed frame, the ONLINE/EMPTY pill sits beside the title on the left, drag/drop slot changes refresh immediately, and Hydrogen Boost Pack sprint boost has a short subtle spool-up. **6.72.0–6.73.0-dev:** Fuel/power accounting shipped. **6.73.0:** refillable Hydrogen Canisters (fill from world H₂ Gas Tanks); H₂/Hybrid packs auto-recharge from inventory canisters at ≤10% fuel; Charged Cells for power packs; Jetpack Bay fuel bar; empty-pack flight cut. **6.80.0-dev** adds the dedicated Armor Station plus a separate timed Armor Upgrade Station: Mobility Servos (jetpack speed +6%/tier, fuel drain −6%/tier), Heat Tolerance / Radiation Shielding / Oxygen Efficiency / Impact Padding (5 tiers each), and Hazmat sealing. Installation runs from 30 seconds at T1 to 150 seconds at T5/Hazmat; Unity validation confirmed the complete armor-station flow in 6.80.4-dev.

### 11.4 Cryobeds, Offline Survival & Oxygen — ✅ COMPLETED (6.28.0-dev)
- Add static and Grid cryobed items/blocks.
- An offline player requires an active cryobed or oxygen-rich environment.
- If oxygen depletes or no valid offline-survival condition exists, the player dies.
- **6.23.0-dev:** Added static `Cryobed` and grid `GridCryobed` blocks, setup-generated items/recipes in Steps 11 and 12, right-click spawn/offline-survival anchor claiming, and basic grid screen data text.
- **6.26.1-dev:** Cryobeds now expose availability state; static cryobeds get setup-generated power draw, grid cryobeds report power/oxygen availability, and the death screen lists only currently available cryobed choices.
- **6.26.2-dev:** Added a right-click Cryobed Control UI with status, power/oxygen estimates, rename, claim, remove ownership, and transfer placeholder controls. Death-screen entries now use cryobed names and component availability text.
- **6.26.4-dev:** Fixed claim behavior so cryobeds can be owned before power/oxygen is available, preserved ownership on multiple cryobeds, persisted cryobed names/ownership/oxygen, prevented numeric name input from triggering hotbar actions, and made Grid Cryobed availability require oxygen piped into its internal buffer via variable gas ports.
- **6.24.0-dev:** Added Space Helmet and Oxygen Tank equipment items/recipes, Life Support equipment slots in Inventory, save/load for helmet/tank slots, and underwater oxygen reserve/drain reduction when helmet+tank are equipped.
- **6.24.1-dev:** Equipment UI now places Jetpack Bay and Life Support side-by-side to preserve backpack room, removes extra hint text, and activates underwater oxygen drain using head-underwater/deep-swim checks.
- **6.26.6-dev:** Made cryobed UI live-ticking with oxygen tank visual (fill %, color cues), fixed linked spawn name resolving to actual cryobed name, and fixed world spawn fallback 0,250,0 bug by persisting final grounded spawn.
- **6.28.0-dev:** Completed offline survival: new `OfflineSurvivalService` saves UTC logout time/pos/cryobed to `offline_state.json` on every save/quit, on next login computes offline hours (clamped 0-720h, <2 min ignored), consumes `offlineOxygenPerHour * hours` from claimed `GridCryobed.oxygenStored` (or checks `Cryobed.IsPowered + HasOxygenEnvironment` for static), checks oxygen-rich environment (powered biofarm producing within 10 m, O₂ tank >5-10 L within 6-7 m, powered cryobed with O₂ within 6 m) when no claimed cryobed, and kills player offline (clears bed spawn, triggers `Die()` with feedback HUD showing reason) if O₂ depleted / no power / no O₂-rich env / cryobed destroyed. Room O₂ for static cryobed now checks nearby biofarm/tank/cryobed via `Cryobed.IsOxygenRichAt()` instead of always true.

### 11.5 Passive Oxygen Generation — ✅ COMPLETED (6.27.0-dev) + PATCHES 6.27.1 & 6.27.2
- Add an expensive Biofarm / oxygen garden system that passively generates oxygen over time.
- The Biofarm should require power, water, biomass or plant inputs, and enough physical space to prevent cheap oxygen spam.
- Static and Grid variants should connect to gas pipes and feed oxygen tanks, cryobeds, life support rooms, and future airtight spaces.
- Output should be slower than industrial H2/O2 generation but reliable, renewable, and useful for offline survival infrastructure.
- **6.27.0-dev:** Added static `Biofarm` (`Building/Biofarm.cs`) and grid `GridBiofarm` (`GridSystem/GridBiofarm.cs`) blocks: 65-70 W, 0.18-0.20 L/s water, 0.55 O₂ L/s, 45s per biomass, 260 L O₂ buffer + 180 L water buffer, biomass auto-pull from cargo/item pipes, water from liquid pipes/tanks, O₂ push via gas pipes into tanks/cryobeds. Added `Biomass` compressed crop item + recipes, premium UI panels (`MachineUIs.BiofarmPanel` / `GridBlockUI BiofarmPanel`), interaction via `PlayerInteractionTool`, non-destructive setup in Steps 11+12 (`Item_Biomass`, `Block_Biofarm`, `GItem_Biofarm` + port markers), research wiring into `res_farming` + `res_gas_processing`, and anti-spam via large prefab scale + triple input + slow rate.
- **6.27.1-dev:** Fixed grid biofarm UI not opening (missing from `GridBlockHasUI`), added variable ports on all biofarms like grid tanks (`GridTankVariablePorts` for liquid+gas on `GridBiofarm` + `GridH2O2Generator`, port markers for gas+liquid, visual arms in `WaterPipe`/`GasPipe` for both static `Building.Biofarm` and grid `GridBiofarm`), and fixed vertical pipe connection on land (`PipeAdjacency` vertical tolerance 0.65m, `IsPlacementValid` allows thin stacking blocks to ignore terrain for vertical shafts).
- **6.27.2-dev:** Enforced pipe-only water for biofarm (removed adjacent-tank cheat + voxel water fallback, now requires nearby `WaterPipe` whose network has a `WaterTank`), fixed diagonal pipe connections on grid and land by switching `PipeAdjacency` to Euclidean `sqrt(other1²+other2²)` checks for all axes (prevents diagonal with both offsets 0.5), and fixed grid ghost port not committing (added `GridBiofarm` to `TryGetGridTankVariablePortSnap` / `IsMatchingTankBlockForPipe` in both `BuildSystem` and `GridBuilder`, ensured `EnsureGridTankPorts` creates gas+liquid fixed ports and variable `Port_*_V` ports persist via `WorldStatePersistence`).

### 11.6 Multiplayer — LAST ROADMAP MILESTONE — ❌ MISSING
- This is the final roadmap milestone after all single-player systems are complete.
- Support self-hosted server creation on Windows and Linux.
- Support LAN discovery/connection and direct connection to self-hosted servers.
- Add player teams with invitations and team-only interaction permissions for Grid blocks and static blocks.
- Add pirate and merchant NPC roles.

### 11.7 World Management, Autosaves & Item Limits — ✅ COMPLETED
- Add three visible autosave slots in the Saves panel, with safe load/restore flow.
- Add Edit World for non-generation settings only: world name and dropped-item limit.
- Default maximum active physical dropped items is 1000 and must be configurable in both Create World and Edit World.
- Conveyor packets are separate from physical dropped items, are protected from dropped-item despawn/limits, and must be optimized independently for dense factories.
- Main-menu world cards: primary Play button; Edit and Saves controls together; Clone and a smaller Delete control stacked beside them.
- **6.15.0-dev:** Background autosaves now rotate into three visible slot files, the Saves page exposes restore controls with current-save backup, Edit World safely renames the folder and updates dropped-item limits only, and save cards use the requested management layout.
- **6.15.1-dev:** Unity compile cleanup fixed the `WorldStatePersistence` local-name collision and replaced the remaining deprecated runtime `GetInstanceID()` calls with `GetEntityId()`.
- **6.16.0-dev:** World settings now also include inventory/container weight multipliers, the default physical drop limit is 1000, and drop-limit warning toasts protect players from silent physical-drop culling.
- **6.16.1-dev:** Ship Control search compile fix, autosave slots now fully hide when collapsed, and the default autosave cadence is 5 minutes.
- **6.17.0-dev:** Manual drops above the physical item limit now show a per-world confirm/deny void warning with a remembered show-warning checkbox; confirmed over-limit drops void only the excess instead of blocking the action.

---
t-save backup, Edit World safely renames the folder and updates dropped-item limits only, and save cards use the requested management layout.
- **6.15.1-dev:** Unity compile cleanup fixed the `WorldStatePersistence` local-name collision and replaced the remaining deprecated runtime `GetInstanceID()` calls with `GetEntityId()`.
- **6.16.0-dev:** World settings now also include inventory/container weight multipliers, the default physical drop limit is 1000, and drop-limit warning toasts protect players from silent physical-drop culling.
- **6.16.1-dev:** Ship Control search compile fix, autosave slots now fully hide when collapsed, and the default autosave cadence is 5 minutes.
- **6.17.0-dev:** Manual drops above the physical item limit now show a per-world confirm/deny void warning with a remembered show-warning checkbox; confirmed over-limit drops void only the excess instead of blocking the action.

---
dStatePersistence` local-name collision and replaced the remaining deprecated runtime `GetInstanceID()` calls with `GetEntityId()`.
- **6.16.0-dev:** World settings now also include inventory/container weight multipliers, the default physical drop limit is 1000, and drop-limit warning toasts protect players from silent physical-drop culling.
- **6.16.1-dev:** Ship Control search compile fix, autosave slots now fully hide when collapsed, and the default autosave cadence is 5 minutes.
- **6.17.0-dev:** Manual drops above the physical item limit now show a per-world confirm/deny void warning with a remembered show-warning checkbox; confirmed over-limit drops void only the excess instead of blocking the action.

---
er-world confirm/deny void warning with a remembered show-warning checkbox; confirmed over-limit drops void only the excess instead of blocking the action.

---
dStatePersistence` local-name collision and replaced the remaining deprecated runtime `GetInstanceID()` calls with `GetEntityId()`.
- **6.16.0-dev:** World settings now also include inventory/container weight multipliers, the default physical drop limit is 1000, and drop-limit warning toasts protect players from silent physical-drop culling.
- **6.16.1-dev:** Ship Control search compile fix, autosave slots now fully hide when collapsed, and the default autosave cadence is 5 minutes.
- **6.17.0-dev:** Manual drops above the physical item limit now show a per-world confirm/deny void warning with a remembered show-warning checkbox; confirmed over-limit drops void only the excess instead of blocking the action.

---
