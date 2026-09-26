# IndustrialWorld — Changelog

**Branch:** `Dev`  
**Current Version:** `12.41.4-dev`

All release notes are maintained here so `Roadmap.md` remains focused on planned work and execution status.

### [12.41.4-dev] Pipe-on-Pipe Network Snapping, Solid Connector Interior, and Machine Face Penetration

**Type:** PATCH - fixes pipe snapping onto other pipes and junctions, eliminates invisible backface culling on connector housings and inside socket cups, and guarantees conduits visually connect and penetrate flush into machine prefabs and generators.

**Pipe-on-pipe and junction placement:** Refined placement validation in `BuildSystem` to remove probe over-rejection while preserving duplicate placement protection (`dist < 0.25m`). Players can seamlessly snap straight pipes, bends, 90-degree risers, and 4-way / 6-way junctions onto existing placed pipes and socket endpoints.

**Solid connector housing & socket interior:** Re-orthogonalized the local coordinate frame in `EnergyPipeMeshBuilder.BuildConnectorHousing` to guarantee a strictly right-handed orthonormal basis (`Cross(right, up) == normal`) and corrected triangle winding across the front face, back face, side walls, and inside socket cup cylinder walls. Socket cups and connector blocks render completely solid, opaque, and visible from all viewing angles with no see-through hollow faces.

**Machine visual conduit bridging:** In `PowerCable.RebuildVisuals`, endpoint proximity probes (`Physics.OverlapSphereNonAlloc`) immediately detect adjacent machine colliders and calculate exact surface contact points (`col.ClosestPoint`). Dual conduits now extend directly from pipe endpoints into the machine wall (with 0.06m penetration) capped by a mounting flange, ensuring zero visual gaps.

**GitHub title:** `[12.41.4-dev] Pipe-on-Pipe Network Snapping, Solid Connector Interior, and Machine Face Penetration`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `6. Build Power Content` and `17. Build Factory Foundations + HV Grid` (safe and non-destructive). Equip an Energy Pipe:
1. Aim at an existing placed pipe (straight, bend, or 4-way cross): confirm that new pipes snap directly to its sockets and place cleanly without being blocked.
2. Inspect the connector block from all angles (both front and rear faces, and looking into the dual circular socket cups): verify that the housing and socket interior are completely solid and opaque (no invisible or backface-culled surfaces).
3. Place an Energy Pipe facing a Coal Generator or machine prefab: verify that the dual conduits automatically extend flush into the machine's wall with no floating gaps and power flows seamlessly.

### [12.41.3-dev] Machine Power Network Bridging, Hotbar Scroll Lock, and Conduit Overlap Guard

**Type:** PATCH - restores seamless power transmission between Energy Pipes and machines (generators, batteries, consumers), adds automatic visual extension bridging flush to machine faces so pipes never float in mid-air, blocks hotbar slot switching while holding V to scale straight pipe length, and strictly prevents placing conduits inside or overlapping existing placed conduits.

**Machine power transfer & visual bridging:** Replaced legacy rigid 1-unit grid delta tests in `PowerCable.CanLinkTo` and `PowerNode.CanLinkTo` with direct endpoint-to-socket and collider surface proximity tests. Energy Pipes now reliably link to generators, batteries, and consumers across all 9 shape variants and lengths. In `RebuildVisuals`, `PowerCable` detects connected machine surfaces and extends its dual conduits and connector flange directly to meet the machine face flush, completely eliminating visual floating gaps.

**Hotbar scroll lock (Hold V + Scroll):** Updated `GameUIController` to suppress hotbar slot cycling whenever the `V` key is held. Holding V and scrolling the mouse wheel now smoothly and exclusively scales the straight energy pipe length (1m to 5m) without inadvertently switching items in the player's hotbar.

**Conduit overlap prevention:** `IsPlacementProbeColliderAllowed` in `BuildSystem` now strictly disallows conduits from overlapping or being placed inside any existing conduit colliders, regardless of block stacking flags.

**GitHub title:** `[12.41.3-dev] Machine Power Network Bridging, Hotbar Scroll Lock, and Conduit Overlap Guard`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `6. Build Power Content` and `17. Build Factory Foundations + HV Grid` (safe and non-destructive). Equip an Energy Pipe:
1. Hold V and scroll the mouse wheel: confirm the straight pipe length scales (1m-5m) smoothly without switching hotbar item slots.
2. Connect a Coal Generator or Battery to a power consumer using Energy Pipes: confirm power immediately flows across the circuit and machines operate.
3. Observe the pipe-to-machine interface: confirm the dual conduits extend flush into the machine collider surface without floating gaps.
4. Attempt to place a pipe directly overlapping or inside an existing placed pipe: verify placement is refused and the ghost remains red.

### [12.41.2-dev] Connector Socket Snapping, Lattice Outward Alignment, and Glare/Backface Fix

**Type:** PATCH - eliminates see-through backface culling on connector housings, removes washed-out specular glare, enables true socket-to-socket endpoint snapping across all conduit shapes (including 90-degree risers and bends), prevents overlapping/intersecting pipe placement, and supports outward-facing orientation and full 3-axis rotation when mounting energy pipes to machine lattice faces.

**Socket-to-socket endpoint snapping:** When aiming at existing placed `PowerCable` segments, `BuildSystem` queries `EnergyPipeMeshBuilder.GetLocalEndpoints` to resolve the closest target socket (e.g. top of a 90-degree riser, end of a straight run, or junction branch). The held piece's entry connector snaps flush to the target socket, aligns to the outward normal, and rotates around the connection axis with `_rotSteps` (via BuildRotate / R key or Ctrl+Scroll). Placement validation verifies that new pipes connect exclusively at valid sockets and cannot be placed intersecting through existing pipe bodies.

**Lattice face outward alignment:** When placing an Energy Pipe against a machine or block surface in `TryGetStaticSurfaceAttachmentPose`, the pipe defaults to pointing perpendicular (straight out) along the surface normal. Players can freely cycle 90-degree rotation steps across all three axes using the rotation controls to point straight out, up, down, left, or right across the lattice.

**Connector backface and glare correction:** Corrects triangle winding order across the front face, rear face, side bevels, and recessed port socket cups in `BuildConnectorHousing`, ensuring all surfaces render opaque, solid, and without hollow gaps. Adjusts material metallic (0.15 - 0.50) and smoothness (0.25 - 0.40) to eliminate harsh white specular glare and showcase rich saturated tier metal and dark rubber grommets.

**GitHub title:** `[12.41.2-dev] Connector Socket Snapping, Lattice Outward Alignment, and Glare/Backface Fix`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `6. Build Power Content` and `17. Build Factory Foundations + HV Grid` (safe and non-destructive). Equip an Energy Pipe:
1. Aim at a machine face (generator/battery lattice): confirm the ghost can point straight out from the face and rotates cleanly along any axis with R or Ctrl/Shift+Scroll.
2. Place a 90-degree riser (`BendUp`), then aim at its top connector: verify a second riser or straight pipe snaps directly to the top socket and continues upward or turns without clipping or offsetting into the ground.
3. Attempt to place a pipe intersecting the middle of an existing pipe: confirm placement is refused and the ghost turns red.
4. Inspect the connector block: confirm the front face is completely solid (no see-through gaps, no inverted backfaces) and the finish is rich without white specular glare.

### [12.41.1-dev] Energy Pipe Connector Housing Revamp, Shape Wheel Alignment, and V-Key Length Adjustment

**Type:** PATCH - fixes radial Energy Pipe Shape Wheel segment label centering and texture alignment, refines energy pipe connector housings with rounded rectangular blocks, recessed dual circular port bezels, and strain-relief cable collars matching Pic 5, repairs missing endpoint connectors on vertical risers and compound bends (Pic 2 & 3), enables full rotation when aiming at existing power pipes, and remaps straight pipe length adjustment to Hold V + Scroll to prevent Ctrl rotation conflict.

**Energy Pipe Shape Wheel layout:** Corrects 9-slice radial wheel segment positioning, label centering (half-slice angular offset), and ring texture radius math. Slices render with premium cream backgrounds when unselected (using crisp dark charcoal typography) and vibrant cyan backgrounds when hovered/selected (with white typography). All 9 variant titles and icons fit cleanly without overlap or edge clipping.

**Connector housing and cable revamp:** Connectors on all pipe and conduit variant endpoints are redesigned to match Pic 5:
- Rounded rectangular solid housing body with corner bevels, finished in the active cable tier material (Copper, Iron, Gold, Superconductor).
- Two recessed circular dark port socket bezels with inner cylindrical cups and metallic central contact terminals.
- Rear strain-relief cable boot collars connecting each cylindrical cable cleanly to the back of the housing.
- Both/all terminal ends on every variant (Straight, 90-degree Horizontal Elbow, 90-degree Vertical Riser, Vertical S-Step, Horizontal S-Curve, Left-to-Up Bend, Right-to-Up Bend, 4-Way Cross, 6-Way Hub) now generate complete connector housings facing the true connection normals.

**Placement rotation and snapping:** When aiming at existing placed Energy Pipes, the placement ghost snaps flush to the nearest cardinal socket/endpoint and responds fully to player rotation controls (`_rotSteps` via BuildRotate / R key, Ctrl + Scroll, and Shift + Scroll). Straight pipe length scaling is remapped from Ctrl+Scroll to Hold V + Scroll, freeing Ctrl exclusively for orientation rotation. Placed `PowerCable` instances dynamically resize their `BoxCollider` to match active variant geometry for precise cursor raycasting.

**GitHub title:** `[12.41.1-dev] Energy Pipe Connector Housing Revamp, Shape Wheel Alignment, and V-Key Length Adjustment`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `6. Build Power Content` and `17. Build Factory Foundations + HV Grid` (safe and non-destructive). Equip an Energy Pipe in hand: hold the Build Wheel keybind (default B) to open the Energy Pipe Shape Wheel and verify that all 9 slices display centered icons and titles with high contrast. Select the Straight variant: hold V and scroll the mouse wheel to dynamically scale length between 1m and 5m; hold Ctrl and scroll to rotate the piece in 90-degree increments. Place vertical risers (Bend Up) and compound bends (Left/Right to Up): verify that both ends feature the complete rounded rectangular connector block with recessed dual port rings matching Pic 5. Aim at placed energy pipes and rotate with R or Ctrl+Scroll to confirm full rotational snapping.

### [12.41.0-dev] Energy Pipe Shape Variant Wheel, Dual-Conduit Aesthetics, and Tier Overload Explosions

**Type:** MINOR - introduces the radial Energy Pipe Shape Wheel UI for selecting conduit shapes, dynamic length adjustment (Ctrl + Scroll for 1-5m straight runs), dual-conduit procedural geometry with bolted terminal flanges, tier-specific aesthetics (oxidized copper, rusted iron, yellow gold, and superconductor energy beam animation), and active power overload explosions for finite energy pipe tiers (Copper, Iron, Gold).

**Energy Pipe Shape Wheel & Variants:** Holding an Energy Pipe and pressing the BuildWheel keybind opens a 9-slice radial selector (`EnergyPipeShapeWheel` using UI Toolkit) supporting Straight (1-5m), 90-degree Horizontal Elbow, 90-degree Vertical Riser, Vertical S-Step, Horizontal S-Curve, Left-to-Up Compound Bend, Right-to-Up Compound Bend, 4-Way Planar Cross Junction, and 6-Way 3D Omni Hub. For the Straight variant, players can hold Ctrl and scroll the mouse wheel to dynamically scale the placed conduit length from 1m up to 5m in real time with live ghost preview updates.

**Dual-Conduit Aesthetics & Tier Styling:** Procedural mesh generation (`EnergyPipeMeshBuilder`) builds parallel dual conduits with bolted end-flange plates and spiral hazard band styling. Tier materials feature distinct visuals:
- Copper: Warm oxidized copper bronze with subtle patina undertones.
- Iron: Dark cast iron steel with warm rust highlights.
- Gold: Radiant yellow metallic gold.
- Superconductor: Clean white-cyan ceramic shell with an active animated purple energy beam line core running through the conduit.

**Conduit Overload Explosions:** Finite Energy Pipe tiers enforce their real electrical throughput ratings (Copper: 10,000 W, Iron: 30,000 W, Gold: 50,000 W). When power flow across a circuit exceeds the pipe's capacity, the overloaded pipe triggers an immediate explosion flash, enters the `OverheatedPowerCable` state (pulsating red-hot heat for 2.0 seconds), and is destroyed. Superconductor conduits remain unlimited.

**GitHub title:** `[12.41.0-dev] Energy Pipe Shape Variant Wheel, Dual-Conduit Aesthetics, and Tier Overload Explosions`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `6. Build Power Content` and `17. Build Factory Foundations + HV Grid` (safe and non-destructive). Equip any Energy Pipe in hand: hold the Build Wheel keybind (default B) to open the new Energy Pipe Shape Wheel and select between the 9 shape variants. With the Straight pipe selected, hold Ctrl and scroll the mouse wheel to adjust length from 1m to 5m, verifying that the placement ghost updates immediately. Place each shape variant: verify the dual parallel shafts, bolted flange plates, and tier-specific materials render cleanly. For Superconductor Energy Pipes, confirm the glowing purple energy beam core is visible. To test overload explosions, connect a high power source/load (e.g. 15,000 W+) through a 10,000 W Copper Energy Pipe: confirm the pipe immediately triggers an explosion flash, glows fiery red-hot for 2.0 seconds, and disintegrates.

### [12.40.7-dev] Energy Pipe Visual Alignment, Overload Fault Destruction, and Battery Equalisation

**Type:** PATCH - repairs Energy Pipe visual silhouette and endpoint reach, restores full connector explosion and red-hot burning line destruction upon power overload, and resolves power transfer across Energy Pipes connecting to batteries and machines without modifying save schema or public APIs.

**Energy Pipe aesthetics and connectivity:** Energy Pipe mesh generation (`IndustrialPipeMesh.ProfileFor` with `PipeStyle.WireArm`) is refactored from bulky multi-shaft wheels to a sleek, unified cylindrical conduit profile with refined joint collars, matching the polished aesthetic of gas and liquid piping. `GridCableVisuals` passes `showUnusedFaceCaps = false` to suppress 6-way unlinked face spike nubs and applies the authentic metallic tier tint to conduit shafts with polished collar accents. `PowerCable.TouchesPowerEndpoint` dynamically tests adjacent grid cell reach against machine colliders, enabling Energy Pipes to reliably connect to batteries, generators, consumers, and compact wire connectors. In addition, machine endpoint visual arms terminate flush against the target collider face rather than embedding into internal machine origins.

**Overload fault destruction:** `PowerNetworkManager` side-power measurement now accounts for battery discharge availability and charge demand alongside generators and consumers. When electrical transfer across a rated compact connector exceeds its capacity (e.g. >1,500 W on a copper LV wire span), the connector triggers an immediate high-intensity explosion flash, is destroyed, and detaches the overloaded line. Overloaded manual wires and attached cables receive `OverheatedManualWire` and `OverheatedPowerCable` feedback: they glow with pulsating emissive red-hot heat in world space for 2.0 seconds before disintegrating. Station disconnection logic protects active burning wires from premature removal.

**Battery equalisation:** Batteries connected across valid Energy Pipe and wire routes now belong to the unified power network and smoothly redistribute stored energy toward a common capacity-weighted fill percentage within battery I/O limits and available conductor bandwidth.

**GitHub title:** `[12.40.7-dev] Energy Pipe Visual Alignment, Overload Fault Destruction, and Battery Equalisation`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `17. Build Factory Foundations + HV Grid`, and run it once (safe and non-destructive). Place Copper, Gold, Iron, or Superconductor Energy Pipes: verify they render with clean cylindrical shafts, sleek joint collars, and no stray spiky end caps. Connect an Energy Pipe directly to a Battery, Coal Generator, Power Light, or LV Wire Connector: verify the connection is established and the visual arm meets the machine collider face flush. Connect two batteries with differing charge levels using Energy Pipes and an LV Wire Connector with a copper wire: verify charge equalisation occurs and power flows across the conduit run. To test overload faults, place a high-demand consumer or battery draw exceeding 1,500 W through a 1,500 W Copper LV Wire connector: verify the connector explodes and is destroyed, while the wire and attached cable glow pulsating red-hot for 2 seconds before disintegrating.

### [12.40.6-dev] Direct Conduit Links and Finite Manual Wire Transfer

**Type:** PATCH - corrects utility topology, power endpoint contact, and power-transfer limits without changing save data, item IDs, prefab serialization, or public API. Item, gas, liquid, Energy Pipe, and Data Cable topology now permits only real direct cardinal/coplanar neighbour links. The runtime no longer infers an L/elbow route from an offset pair and the shared conduit mesh no longer renders one. A player must place the intervening pipe or cable segment to turn a run.

**Energy Pipes and endpoints:** an Energy Pipe may discover a broad nearby power node, but it creates a machine/connector link only after its endpoint reaches that target prefab's collider surface. It no longer draws a visual arm or transfers power across nearby empty space. Energy Pipes are unlimited: `ElectricalPipeDefinition.capacityWatts` is retained only as legacy tier metadata and is no longer a network bottleneck, overload input, heat source, or destruction condition.

**Manual wires and batteries:** player-drawn LV/HV wire links retain their finite `manualLinkCapacities` and are the only links that constrain a network's transfer budget. The generated LV tiers remain 1,500 W (copper), 15,000 W (gold), and 50,000 W (graphite). A compact connector overload is now evaluated only against an attached finite manual-wire span; it flashes/removes the connector and uses the existing manual-wire red-hot fault path, never damages an Energy Pipe. Batteries in one valid power network now move stored watt-hours from batteries above the capacity-weighted common fill percentage to those below it. Equalisation honours each battery I/O rate and the unused finite manual-wire capacity for the tick.

**GitHub title:** `[12.40.6-dev] Direct conduit links and finite manual wire transfer`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `17. Build Factory Foundations + HV Grid`, and run it once; the setup remains non-destructive/idempotent and preserves existing production values while reconnecting missing generated assets. Verify strict utility placement by placing matching pipe pairs and Energy/Data Cable pairs with only a diagonal or height-offset gap: they must show no joining arm and no transfer. Add the real cardinal intermediary segment(s), then confirm each direct placed link joins normally. Place an Energy Pipe within its broad discovery area but with a visible gap to a generator, consumer, battery, or connector: it must not draw to or power that target. Move/place it so the endpoint reaches the target collider face and confirm it links. For a manual-wire test, use a copper LV wire and demand above 1,500 W; confirm the route is limited/trips through a compact connector rather than treating the wire as unlimited, with the manual line using its red-hot removal feedback. Repeat with gold and graphite LV wire tiers. Set a test Energy Pipe definition's legacy capacity field low, then verify a valid Energy Pipe route does not throttle, heat, or burn because of that field. Finally, connect batteries with visibly different charge percentages through a valid wire network with no external load/source; their stored charge should converge toward one common percentage at their I/O rate and within the available wire throughput. Confirm normal consumers, relays, static surface taps, connector two-link limits, Data Cable placement/removal, and existing saves remain stable.

### [12.40.5-dev] Connector Power Transfer and Overload Safety

**Type:** PATCH - repairs compact LV/HV Wire Connector power routing and makes connector terminals a strict two-link part. Connector links now use one shared two-terminal budget across nearby Energy Pipes and player-drawn manual wires; the manual-link topology pass can no longer bypass that budget, and the wire tool checks both endpoints before consuming an item. This restores generator-to-consumer transfer through a connector with two valid links while refusing a third link and directing the player to a Power Relay. Setup Step 17 now repairs generated connector nodes to the two-link limit and updates their item descriptions without changing relay limits or production values. The stale `IsCardinalNeighbour` reference in `DataCable.OnDisable` is corrected to an internal neighbour predicate, resolving CS0103.

**Overload behaviour:** before the regular network bottleneck silently throttles a two-terminal compact connector, the power manager measures the isolated generator-to-demand transfer on its two sides. If that transfer exceeds a finite Energy Pipe or manual-wire rating, the connector trips: it emits a short red overload flash and is destroyed, attached Energy Pipes glow/pulse red-hot for two seconds before destroying themselves, and a drawn manual wire detaches into the world, burns red-hot for the same interval, then disappears. Superconducting/infinite-capacity links do not trip. Overload state is runtime-only and is not saved. No save-schema or public API change is required.

**Superseded power-limit detail:** `12.40.6-dev` corrects this initial fault scope: Energy Pipes are unlimited and never burn from `capacityWatts`; only finite manual-wire spans can constrain or fault a compact connector.

**GitHub title:** `[12.40.5-dev] Connector power transfer and overload safety`

**Manual steps:** in Unity on `Dev`, first let scripts compile and confirm the prior `DataCable.cs(80,34)` CS0103 error is gone. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, then select `17. Build Factory Foundations + HV Grid`; it is idempotent and updates only the generated compact connector limit/description. Place a generator, two Energy Pipes, one LV or HV Wire Connector, and a consumer in a simple isolated line. Keep generation and demand below the selected Energy Pipe rating; after topology settles, verify the consumer is powered through the connector. Attach a third Energy Pipe or manual wire to that connector: it must be refused with `Connector Full` and no wire item consumed. Confirm an LV/HV Power Relay accepts the intended additional links. For overload, use an isolated two-link connector route with a finite-rated Energy Pipe or LV wire and make the generator-to-consumer transfer exceed that rating. Verify the connector flashes and disappears, each attached Energy Pipe or manual wire is visibly red-hot for about two seconds, then the damaged cable/wire is gone and the consumer loses power. Finally verify a below-rated run, infinite-capacity link, normal cable placement, Data Cable removal/replacement, static power taps, and existing saves remain stable.

### [12.40.4-dev] Orthogonal Pipe and Cable Riser Links

**Type:** PATCH - fixes local X/Y/Z pipe and cable links that were rejected whenever a neighbouring terrain placement introduced a small off-plane offset, and fixes the resulting geometry that previously projected every connection onto one straight nearest-axis arm. Item, gas, and liquid pipe pairs now accept one bounded orthogonal secondary leg alongside their normal 1–5 cell primary lattice run; arbitrary three-axis diagonals and overlong offsets remain rejected. Energy and data cable pairs use the same one-cell bounded elbow rule. The shared industrial conduit mesh retains the secondary local delta and builds a collared ninety-degree riser, including midpoint-owned pipe/cable half-links, so an uneven-ground run visibly reaches its connected endpoint instead of terminating in empty space. Power cable target coordinates are now passed once in world space before local conversion, repairing rotated/surface-mounted cable plane errors. Pipe-pair spatial hashes cover the complete bounded elbow envelope. No save, public API, prefab, item, recipe, research, balance, or Setup change is required.

**Superseded topology detail:** `12.40.6-dev` removes inferred elbow/riser links and geometry. Turns now require player-placed direct intermediary utility segments.

**GitHub title:** `[12.40.4-dev] Orthogonal Pipe and Cable Riser Links`

**Manual steps:** no Setup run is required. In Unity on `Dev`, first allow the project to compile and clear the Console. On flat terrain, place matching Item, Gas, Water, and Energy Pipe pairs one normal local grid step apart on each of local X, local Y, and local Z; confirm every pair joins and transfers its matching resource/power. Next create a stepped or uneven-terrain run: place two matching pipes one primary grid step apart with the second endpoint roughly 0.25–1.0 cell higher or lower. Confirm the route shows continuous primary shafts and a visible collared right-angle riser at the shared midpoint, with no straight arm ending in air, then verify flow across the pair. Repeat with two Energy Pipes and, where available, two Data Cables; confirm their bundled wire geometry follows the same correct local plane/riser and the Energy Pipe still powers a connected consumer. Repeat once on a rotated grid/static face. Finally, confirm a three-axis diagonal pair, an offset greater than one cell, and a player-wrench-blocked pair do not connect; verify ordinary same-plane pipe/cable runs, static face power taps, Portal Frames, and existing saves remain unchanged.

### [12.40.3-dev] Embedded Static Lattice Compile Repair

**Type:** PATCH - resolves CS0246 when `BuildSystem` was updated without its newly introduced preview helper source file. `StaticSurfaceLatticePreview` now lives in `BuildSystem.cs`, the only script that creates it, so the static utility lattice compiles as one self-contained placement change. The matching runtime `SurfacePowerTap` helper now lives in `PowerNode.cs`, its owning power topology script, eliminating a second fragile cross-file helper dependency. Behaviour is unchanged from 12.40.2-dev: portal ground support, static utility face lattices, and touching static power taps remain intact. No save, API, prefab, item, recipe, research, or Setup change is required.

**GitHub title:** `[12.40.3-dev] Embedded static lattice compile repair`

**Manual steps:** no setup run is required. Replace `Scripts/Building/BuildSystem.cs`, `Scripts/Power/PowerNode.cs`, and `Scripts/Power/PowerCable.cs` together, then let Unity compile. Confirm the prior `StaticSurfaceLatticePreview` CS0246 error is gone before repeating the 12.40.2-dev Portal Frame, static utility lattice, and static Battery-to-consumer Energy Pipe checks.

### [12.40.2-dev] Static Utility Surface Lattice and Power Taps

**Type:** PATCH - Portal Frames now resolve their real collider support plane against the aimed surface, so the 5 m root is lifted by its 2.5 m half-height and stands on terrain instead of being buried. Pipes, energy pipes, data cables, compact relays, and LV/HV Wire Connectors now mount cleanly to ordinary static `PlacedBlock` faces through a cyan 1 m surface lattice. The mount pose uses the actual host and held collider support planes, clamps cells to the visible face, preserves a tiny non-overlap clearance, and permits only the intended host contact during placement validation. Static power cables and compact voltage terminals discover a touching battery/generator/consumer after placement or load and create a direct, face-anchored power tap; cable arms now terminate at that touched surface rather than pointing through a large machine's centre. Existing grid placement, cable extension, ports, roads, conveyors, saves, and public APIs remain compatible. No prefab/item/recipe/research authoring is required.

**GitHub title:** `[12.40.2-dev] Static utility surface lattice and power taps`

**Manual steps:** no Setup run is required. In Unity, first place a new Portal Frame on flat terrain and confirm its lower support plane rests just above the ground; existing already-buried frames are intentionally not moved. Aim an Item/Gas/Water Pipe, Energy Pipe, or LV/HV Wire Connector at a static block face: a cyan lattice must appear, the ghost must stay outside the face, and only valid cells inside the face may be selected. Mount an Energy Pipe to a static Battery, extend its run by clicking cable segments, and mount the final segment to a static powered consumer. After the normal physics/topology settle, confirm the consumer receives power and the end arms visibly meet the two machine faces. Repeat with an LV/HV Wire Connector on a large or irregular powered block, then save/reload and confirm its touching power tap rebinds. Finally, verify normal grid pipe/cable placement, conveyors, roads, portals, and a deliberately intersecting Portal Frame still behave as before.

### [12.40.1-dev] Build Placement Hot-Path Cleanup

**Type:** PATCH - audited and tightened both every-frame construction preview paths before import. Standard `BuildSystem` and Hammer/tiered `BuildSystemV2` aiming now use reusable non-alloc raycast buffers and choose the nearest usable hit without sorting; unusually dense 64-hit stacks retain exhaustive fallback queries. Static-anchor/socket discovery and placement-overlap checks now use reusable non-alloc probes with equivalent overflow fallbacks. Large static edge snapping caches each held prefab's collider/classification data and reuses a collider list for the target. Ghost renderers now rebuild material-slot arrays only when validity changes, not every preview frame; the tiered path also removes an unused placement-feedback string build. Placement rules, special pipe/road/factory/busbar/turbine paths, portal overlap protection, save data, and APIs are unchanged.

**GitHub title:** `[12.40.1-dev] Build placement hot-path cleanup`

**Manual steps:** no setup run is required. In Unity, open the Profiler with GC allocation recording, then warm each preview once before sampling. Hold an ordinary block while aiming around terrain and a dense factory, repeat with Portal Frames, and repeat with the Hammer build wheel at ordinary and socketed tiered construction. The stable ghost-preview path should show no managed allocation from ray targeting, nearby anchor/socket probing, placement-overlap probing, or reapplying an unchanged ghost tint. Confirm Portal Frames remain flush, a deliberately intersecting frame is refused, and ordinary blocks, pipes, roads, factory connections, busbars, turbine sockets, and tiered socket snaps retain their placement behaviour.

### [12.40.0-dev] Portal Networks with Destination Selection

**Type:** MINOR - portals now form deterministic multi-endpoint networks. Matching NAME + CODE still identifies the shared network, but each controller now receives a persistent endpoint id and player-editable endpoint label. The controller panel exposes a Destination selector: choose an exact endpoint and every transit through that controller routes there. The endpoint id, label, and selected destination are additive `SavedPlacedBlock` fields, so they persist across save/load without changing or invalidating existing saves; apertures still never restore open. Existing two-portal saves retain automatic pairing when exactly one matching endpoint exists. A network with more than one possible destination deliberately refuses arbitrary routing until the player chooses one. No prefab, item, recipe, research, or Setup Step 99 authoring change is required.

**GitHub title:** `[12.40.0-dev] Portal networks with destination selection`

**Manual steps:** no setup run is required. In Unity, build or load three valid powered portals. Give all controllers the same Name and Code, label their endpoints (for example Home, Mine, and Orbit), press REFRESH ENDPOINTS on Home, select Mine as its Destination, then open all three. Home's Linked to row must name Mine and a player or ship entering Home must exit Mine, never Orbit. Save and reload: Home's endpoint label and selected Mine destination must remain. Finally, make a fresh two-controller Name + Code pair without selecting a destination and confirm its established automatic pairing still works.

### [12.39.7-dev] Large Static Block Edge Snap

**Type:** PATCH - ordinary static placement now resolves a clicked static block's collider support face before the legacy 1 m grid rounding. When either collider is larger than one grid cell along that face, the held prefab uses its resolved rotation and actual enabled collider geometry to land flush just outside the target rather than inside it. This fixes adjacent 5 m Portal Frames on every face, including rotated frames, and gives future large static prefabs the same safe edge snap. Pipes, roads, factory components, power cables/busbars, and wind-turbine sockets retain their existing dedicated placement paths. No prefab, item, recipe, research, save-data, or public API change is required.

**GitHub title:** `[12.39.7-dev] Large static block edge snap`

**Manual steps:** no setup run is required. In Unity, enter a world with grid snap enabled, place a Portal Frame, then aim at each side or top/bottom face with another Portal Frame selected. The ghost must sit flush and remain valid before placement; repeat after rotating the first frame 90 degrees. Finally, extend one static pipe, road, factory connection, busbar, and turbine socket to confirm their dedicated snapping behaviour is unchanged.

### [12.39.6-dev] Portal Placement Fix Compile Repair

**Type:** PATCH - 12.39.5 shipped one compile error: the new PortalPieceHalfExtents helper was inserted between IsThinConduitPlacement's final return statement and its closing brace, nesting the helper inside that method (a local function with an accessibility modifier - CS0106). The method now closes before the helper, and the duplicated brace after the helper is removed. No behaviour change beyond what 12.39.5 intended.

**GitHub title:** `[12.39.6-dev] Portal placement fix compile repair`

**Manual steps:** none. Verify it compiles.

### [12.39.5-dev] Portal Placement Volume Fix, the Controller Monolith

**Type:** PATCH - two portal fixes. Placement: portal frames could be planted inside each other, because the build system validates static placement with a small probe at the target centre, which cannot see a partial overlap between 5 m cells. Portal-sourced blocks (frames and controllers) now get a real volume test - the union of the placed prefab's colliders - against other placed blocks; flush neighbours (touching, not interpenetrating) still pass, frames inside frames are refused, and terrain stays permissive so a ring can still kiss a slope. The portal items no longer allow stacking. The Portal Controller is rebuilt as a 2.7 x 4.5 x 2.7 m monolith: plinth, hull column, glowing screen, steel pylons with lit tips, arch over a glowing core, rear cooling fins, conduit strips, twin antennae and a spinning energy dial that rotates while the block is powered (the prefab upgrade is idempotent - re-running Step 99 grows an existing console in place, designer children are untouched). The unit's stats match its body: 3000 HP, 2500 kg, max stack 5, and its cable connect radius grows to 2.5 m so wires can reach the wider frame. Re-run Setup Step 99 to upgrade the prefab and items.

**GitHub title:** `[12.39.5-dev] Portal placement volume fix, controller monolith`

**Manual steps:** Tools -> Voxel Engine -> Voxel Engine Setup -> "99. Build Portals" (re-run: upgrades the controller prefab to the monolith, rescales existing named parts, applies the item stat changes).

### [12.39.4-dev] Portal Frames at 5x Scale

**Type:** PATCH - portal frames grow 5x: the frame cell is now a 5 m slab (5x5x1.5, rims scaled to match) instead of 1 m. Every downstream metric derives from the frames' bounds, so the whole system scales with it - cell size, aperture metrics, the per-cell surface mesh and the transit radius. The 64x64-cell cap now means apertures up to 320 m a side. Per-block power and recipes are unchanged, so a maxed 64x64-cell portal still draws about 6.16 MW while open. Setup Step 99 renames the old PortalFrame_1m prefab to PortalFrame_5m in place (the guid follows the rename, so the item's reference and every placed frame keep working), rescales its authored visuals idempotently, and creates fresh installs at the new size; frames already placed re-size with the prefab and their portal re-scans on the next tick. Frame item description updated.

**GitHub title:** `[12.39.4-dev] Portal frames at 5x scale`

**Manual steps:** Tools -> Voxel Engine -> Voxel Engine Setup -> "99. Build Portals" (re-run to upgrade an existing frame prefab).

### [12.39.3-dev] Portal Compile Fix: Setup Window PowerConsumer Qualification

**Type:** PATCH - Setup Step 99 referenced PowerConsumer bare, but VoxelEngineSetupWindow has no using for VoxelEngine.Power (the file's convention is fully qualified references - every other PowerConsumer use in it was already qualified). Both call sites now read VoxelEngine.Power.PowerConsumer. No behaviour change.

**GitHub title:** `[12.39.3-dev] Portal compile fix: setup window PowerConsumer qualification`

**Manual steps:** none. Verify it compiles.

### [12.39.2-dev] Portal Compile Fix: EntityId as the Transit Key

**Type:** PATCH - follow-up to 12.39.1: Unity 6.5's EntityId is a wrapper struct, and its implicit conversion to int is itself obsoleted as an error, so keying the transit-immunity dictionary by int no longer compiles. The dictionary is now Dictionary<EntityId, float> and the TryGetValue/indexer calls are unchanged. Runtime-only state, nothing persisted - saves are unaffected.

**GitHub title:** `[12.39.2-dev] Portal compile fix: EntityId as the transit key`

**Manual steps:** none. Verify it compiles.

### [12.39.1-dev] Portal Compile Fix: Unity 6.5 API Drift

**Type:** PATCH - three Unity 6.5 API corrections in the 12.39 portal scripts, no behaviour change. PortalFrameBlock tested cached bounds with `Bounds.valid`, which does not exist - the cache now uses an explicit flag (and reads the collider once instead of twice). PortalControllerBlock used `FindObjectsByType(FindObjectsSortMode.None)`, deprecated in favour of the parameterless overload, and `GetInstanceID()`, which Unity 6.5 obsoletes as an error - both replaced (`FindObjectsByType<T>()`, `GetEntityId()`); the transit-immunity dictionary keys change type source but nothing is persisted, so saves are unaffected.

**GitHub title:** `[12.39.1-dev] Portal compile fix: Unity 6.5 API drift`

**Manual steps:** none. Verify it compiles.

### [12.39.0-dev] Player-Built Portals: Frames, Controllers, Name + Code Pairing

**Type:** MINOR - the warp gate's successor direction ships as a buildable system: portals the player assembles block by block, in any closed shape up to 64x64 cells, linked to each other by a custom NAME and CODE. Portal Frames are static placed blocks (placeable on station hulls and planetary ground alike - they are NOT grid blocks); assembled into a sealed outline they define the aperture. The Portal Controller mounts in front (within 8 m) and owns everything: it re-scans the frame network every 3 s (co-plane check, border flood fill, enclosed-interior extraction, 64x64 cap - a ring gets a ring-shaped aperture, a square a square one), charges the portal (45 s baseline at 60 kW + 60 W per interior cell) and then opens - and the open aperture is the real bill: 20 kW + 1,500 W per cell per second, so a full 64x64 burns about 6.16 MW while open, and losing the supply collapses the portal into a 60 s cooldown. Pairing is the two fields on the panel: two OPEN, valid portals whose trimmed names match (case-insensitive) and whose trimmed codes match exactly link to each other - that pair of strings is the whole address book. While open, the first hull (grid ship) or on-foot player inside the aperture crosses to the partner's mouth: the same floating-origin hop the drive and gate use (TeleportSubjectToCosmic plus SettleGridAfterHop), arriving just outside the linked aperture flying out along its facing at entry speed, collision-vetoed by ArrivalBlocked, with a 6 s re-entry immunity and a 3 s per-side transit lock so nothing ping-pongs. The aperture renders as a generated mesh - one quad per sealed interior cell under a pulsing warp material with a graceful shader fallback - so the portal looks like exactly the portal that was built. The controller panel carries name and code fields (applied live), state, shape and cell count, scan verdict, charge percent, live draw, the open-drain preview, the linked portal's name and the OPEN/CLOSE switch. Name, code, charge and cooldown persist on the placed block (new additive SavedPlacedBlock fields - legacy saves restore exactly as before); a portal never restores open. Authored by the new non-destructive Setup Step 99: PortalFrame_1m and PortalController prefabs (visuals only when missing), static Block_PortalFrame and Block_PortalController items (category Machines), deliberately expensive recipes (frame: 12 Steel Plate + 2 Advanced Circuit + 2 Lithium @ Assembler 12 s; controller: 20 Steel Plate + 10 Advanced Circuit + 4 Uranium Ore + 6 Lithium @ Assembler 60 s), and the Stable Portals research (tier 8, behind Warp Gate). The shipped GridWarpGate stays as-is; this is the design direction going forward.

**GitHub title:** `[12.39.0-dev] Player-built portals: frames, controllers, name + code pairing`

**Manual steps:** Tools -> Voxel Engine -> Voxel Engine Setup -> "99. Build Portals" (run after 98 so the research chain connects; non-destructive). Then verify it compiles.

### [12.38.1-dev] Compile Fix: Route Types Namespace

**Type:** PATCH - GridWarpDrive.cs imported IndustrialWorld.Navigation, but RouteWaypoint and RouteBook live in VoxelEngine.Navigation (the Navigation folder carries both namespaces). The using is corrected; every other touched file was audited and already resolves its route types (qualified or correctly used). No gameplay, save or setup change beyond making 12.38 compile.

**GitHub title:** `[12.38.1-dev] Route types namespace compile fix`

**Manual steps:** none. Verify it compiles.

### [12.38.0-dev] Warp Gate Prototype

**Type:** MINOR - the Era 7 headline ships as a buildable prototype: the Warp Gate, a fixed paired transit structure. Two gates sharing a pairing code (0-99, cycled on the gate panel; 0 = unpaired) pair up; a gate that is powered, charged (90 s at 120 kW) and in vacuum opens its aperture for a 25-second window, and the first ship whose hull enters the 220 m sphere is delivered to the paired gate's rendezvous point - 2 km off the partner on the approach line, collision-vetoed like every jump (a buried partner refuses transits instead of burying ships), arriving at rest. The hop reuses the drive's exact machinery (TeleportSubjectToCosmic plus the new shared SettleGridAfterHop settle) and the full transit FX (WarpFx gains a drive-less grid overload, so gates get the same tunnel, flash and streaks). One transit consumes the window; the coils then cool 120 s. The drive's commit path is unchanged - FinishArrival and ArrivalUnsafe became thin wrappers over the new public statics, zero behaviour change. The gate panel shows charge, state (OFFLINE/UNPAIRED/CHARGING/READY/OPEN/COOLDOWN), partner, draw, aperture and cooldown; changing the pairing code resets the charge honestly. Gate charge, cooldown and pairing code persist with the grid (no schema change - new dedicated save fields, same pattern as the drive). Authored by the new non-destructive Setup Step 98: prefab (ring housing + core, visuals only when missing), GItem_WarpGate, recipe (60 Steel Plate + 20 Advanced Circuit + 10 Uranium Ore + 8 Lithium @ Assembler), research res_warpgate (tier 8, behind Warp Drive). Prototype scope stated honestly: interplanetary pairing inside the charted system - the interstellar expansion hook stays open.

**GitHub title:** `[12.38.0-dev] Warp gate prototype`

**Manual steps:** one - run Tools > Voxel Engine > Voxel Engine Setup, press button 98 "Build Warp Gate", confirm the dialog. Verify in Unity: build two ships (or a ship and a station) each with a gate and power, set the same code on both panels, fly one hull into the charged gate's aperture, and confirm it arrives 2 km off the partner with the transit FX; flip one gate's code and confirm the state reads UNPAIRED; save/reload and confirm codes and charge come back.

### [12.37.0-dev] Route-Book Warp Legs

**Type:** MINOR - the auto-run shuttle loop now buys long legs from the warp drive instead of the tanks. When an armed loop's remaining leg exceeds one warp hop plus a 100 km margin, the loop engages the ship's own drive: it aims the exact frame the drive fires along through the gyros, banks the leg price plus the arrival reserve from the pooled drive battery (auto-recharge borrows the drive without touching the player's recharge toggle), keeps cruising toward the hold point while the coils spin, and fires without the confirm wheel the moment the cone is inside two degrees. After the jump the loop re-aims from the re-anchored origin and chains the next hop until the leg is short enough to finish on thrusters. Refusals degrade honestly: no drive, cooling, atmosphere, or a short leg all just cruise; a ship without gyroscopes logs that warp legs are off and cruises until re-armed; a stalled charge or three consecutive refusals abandon the leg for 30 seconds and cruise; the pilot's keys outrank everything as always. Disarming or pausing releases the drive and clears the borrowed auto-recharge. A WARP LEGS ON/OFF row sits on the auto-run panel (default on). No recipe, setup or save changes.

**GitHub title:** `[12.37.0-dev] Route-book warp legs`

**Manual steps:** none - code-only. Verify in Unity: arm a shuttle loop whose leg is longer than one hop with a warp drive, gyros and a powered grid aboard, and watch it spin, jump, and continue the loop; watch the WARP LEGS row toggle to OFF and confirm it cruises the whole way; grab the stick mid-charge and confirm the loop yields instantly.

### [12.36.0-dev] Warp Coil Resonator Item, Drive Upgrade Slots

**Type:** MINOR - the coil upgrade is now an item you install, not a research rank. A new crafted item, the Warp Coil Resonator (4 Advanced Circuit + 6 Lithium + 2 Uranium Ore at an Assembler, recipe unlocked by the Warp Coil Resonance research), is installed in three new RESONATORS slots on the warp drive panel - each installed resonator trims 15% off the drive's spin-up (0.85^count, same maths as before, stacking with the drive-count assist). The research node no longer grants a passive rank effect; it unlocks the recipe, like every other machine research - anyone who bought ranks in 12.35 keeps the recipe unlock, ranks just no longer add anything on their own. The resonator slots persist with the ship through the grid container save path (new save branch, no schema change). The panel's Coils row now shows the installed resonator count instead of the research rank. Setup Step 97 is reworked (same button, re-run safely): it authors the item, the recipe, and connects the research node behind Warp Drive; new nodes are single-rank. Balance and prefabs untouched.

**GitHub title:** `[12.36.0-dev] Warp coil resonator item, drive upgrade slots`

**Manual steps:** one - re-run Tools > Voxel Engine > Voxel Engine Setup, press button 97 "Build Warp Coil Resonator", confirm the dialog (it now also authors the item and recipe). Verify in Unity: research Coil Resonance, craft a resonator at an Assembler, open the drive panel, drop it into a RESONATORS slot, and watch the Coils row count it and the spin-up time drop; save and reload the ship and confirm the installed resonators come back.

### [12.35.0-dev] Warp Coil Resonance Research, Setup Step 97

**Type:** MINOR - the warp drive's charge time is now research-driven, closing the last half of the multi-drive roadmap line. A new repeatable research node, Warp Coil Resonance (res_warpcoils, 3 ranks, behind Warp Drive research), trims 15% off the spin-up per rank (0.85^rank - three ranks spool a lone drive in under 28 seconds), stacking with the drive-count assist from 12.34.0-dev. The drive reads the rank live through the new ResearchManager.GetRank(string) overload; with no research manager (menu, tests) the base time is used. Authored by the new Setup Step 97 (Tools > Voxel Engine > Voxel Engine Setup): non-destructive - creates the node only when missing, always connects it behind res_warpdrive, never touches recipes, prefabs or tuned values, and is safe to re-run. The drive panel's Coils row shows the research rank (R1-R3) next to the effective spin-up. No recipe, prefab or balance changes; the node costs 80 Science T2 + 40 Science T3 per rank.

**GitHub title:** `[12.35.0-dev] Warp coil resonance research, setup step 97`

**Manual steps:** one - run Tools > Voxel Engine > Voxel Engine Setup, press button 97 "Build Warp Coil Resonance", and confirm the dialog. It creates Research/Nodes/res_warpcoils.asset and links it behind Warp Drive; re-running only reconnects what is missing. Verify in Unity: research one rank (research UI, tier 7, after Warp Drive), open the drive panel, and confirm the Coils row reads R1 with spin-up down to ~38 s on a solo drive; ranks 2 and 3 bring it to ~32 s and ~27 s.

### [12.34.0-dev] Route Destinations in the Picker, Spin-Up Assist

**Type:** MINOR - the drive-panel destination picker now lists route-book destinations: every committed cosmic route on the ship's own book appears as an amber row and jumps to its final plotted point. The end point resolves live through the existing waypoint rules - waymarks track their block, body pins ride the world, frozen points stay frozen - so a jump lands where the route GOES, not where it was recorded. Scene-local routes (road and water networks on one planet) are skipped; a deleted route greys its row to GONE; a half-written legacy waypoint degrades to its frozen plot. And multiple drives now spool together: every enabled drive on the grid resonates its coils, shortening the commanded drive's spin-up by sqrt of the count (4 drives spin twice as fast, 9 three times). The bank, power draw and cooldown are untouched - assist only shortens the spin. The panel gains a Coils row showing how many drives aid and the effective spin-up time. Research-driven charge time remains open (needs a dedicated bonus research node). No recipe, research or setup changes.

**GitHub title:** `[12.34.0-dev] Route destinations in the picker, spin-up assist`

**Manual steps:** none - code-only. Verify in Unity: record a route in space (or load a ship with one), open the drive panel, and confirm an amber route row with live distance and price; jump it and arrive at the route's end point; rename/delete the route and watch the row grey to GONE; fit two or more enabled drives and confirm the Coils row counts them and the spin-up bar fills faster than a solo drive.

### [12.33.0-dev] Warp Safety: Damage Gate, Arrival Checks, Bank Reserve

**Type:** MINOR - the drive enforces its roadmap safety rules. A drive below 35% health refuses to spin or fire (repair first; the panel state reads DAMAGED). Every arrival is collision-checked: the point is pushed at least 25 km above every charted body's surface (and off the star as before), and a blind hop scatters its arrival laterally by up to 0.5% of the hop length - mapped locks (planet, singularity, locator, picker) stay exact; if collision safety still cannot find a valid arrival the jump is refused with the ship and bank untouched. Jumps keep an arrival reserve: a full jump must leave 10% of the price in the pooled bank and a partial hop banks the same share, so a ship never lands on an empty drive; a refused partial keeps every Wh because the partial now validates the arrival before spending. The autopilot banks the leg price plus the reserve so capture legs still land on their shelf in one hop. Balance note: with defaults, one full drive flies about 91% of a hop (2500 km becomes ~2272 km); set Arrival Reserve Fraction to 0 on the prefab for the old one-hop-per-drive. New prefab fields default in code; Setup Step 50 is untouched. No recipe, research or setup changes.

**GitHub title:** `[12.33.0-dev] Warp safety: damage gate, arrival checks, bank reserve`

**Manual steps:** none - code-only. Existing Warp Drive prefabs pick the new fields up at script defaults (35% health gate, 10% arrival reserve, 25 km arrival floor, 0.5% blind scatter). Verify in Unity: batter a drive below 35% health (state DAMAGED, charging and firing refuse with a toast); fire blind hops repeatedly and watch the arrival point scatter a few km between jumps; fire a locked pick and confirm it lands exact; run the bank down near the price of a long jump and confirm the partial popup keeps roughly the reserve; a full-price jump with a full bank now leaves 10% in the drive.

### [12.32.0-dev] Warp Arrival Fix, Drive Destination Picker

**Type:** MINOR - charged warps move the ship again: the 12.31.4 hull re-anchor registered the hull as a shift root, so ShiftWorld moved it together with the world and cancelled its own anchor change — every jump was a geometric no-op (FX played, kWh drained, the cooldown ran, the ship never moved). The jumping hull now holds its scene position while the rest of the world slides past it, which is what makes the anchor change real; nested riders stay on the hull and save/load restore is strictly more correct. The warp drive panel gains a destination picker (the roadmap's open destination-select line): charted planets and moons (never the star) plus powered beacons off the grid, rows with live distance, live price and affordability colour; a row click locks the target, the drive plots the approach shelf itself (near side of a world at the arrival altitude, 2 km off a beacon on the approach line) and runs the same fuel check and confirm wheel. A short bank still offers Jump partway, now flown along the target line instead of the nose. Rows grey out when the drive is not ready and mark a destroyed beacon GONE. No recipe, research or setup changes.

**GitHub title:** `[12.32.0-dev] Warp arrival fix, destination picker`

**Manual steps:** none - code-only. Verify in Unity: charge a drive in space, fire an aimed jump, and confirm the hull actually arrives (the arrival toast distance matches the new view); open the drive panel, pick a charted world, confirm, and arrive on its near-side shelf with gravity and streaming handing over; pick a powered beacon and arrive 2 km off it; fire a short-banked lock and confirm the partial hop heads toward the target; destroy the beacon's grid and watch its row grey to GONE without errors.

### [12.31.4-dev] Warp Moves the Hull, Vacuum Life, Planet Handoff

**Type:** PATCH - charged warp re-anchors on the grid hull (not the seated pawn), so ShiftWorld actually carries the ship. SetFrame now fires OnFrameChanged so gravity, grass and voxel streaming retarget. After the hop the frame is force-re-evaluated. Livestock and grass do not spawn in vacuum. Distant planet GPU surfaces sleep above 400 km. Proximity hold uses surface distance (not centre), so approaching a world switches frame and gravity. No recipe or setup changes.

**GitHub title:** `[12.31.4-dev] Warp hull hop, vacuum, planet handoff`

**Manual steps:** none. Verify in Unity: charged warp moves the hull (not only FX); no animals or grass in space; Earth scatter gone when far; flying up to another planet streams its surface and gravity takes over.

### [12.31.3-dev] Warp Actually Jumps, Save Depth, Less Fly Hitch

**Type:** PATCH - warp FX particle velocity curves mixed Constant/TwoConstants so PlayJump threw, left the drive pending, and the teleport never ran. Streaks are constant-mode and cheaper. Packed-drawer upgrades are a flat SavedUpgradeStack so JsonUtility no longer hits depth 10. Restored kinematic hulls no longer set velocity. Origin late-object sweep is 8 s and skips inactive. No recipe or setup changes.

**GitHub title:** `[12.31.3-dev] Warp jump, save depth, fly hitch`

**Manual steps:** none. Verify in Unity: charged warp actually arrives; load a world without the drawerUpgrades spam; fly without a hitch every two seconds.

### [12.31.2-dev] SavedGrid Cosmic Fields and Star Name Compile Fix

**Type:** PATCH - SavedGrid was missing hasCosmic / cosmicX/Y/Z (the save path wrote them, the type did not declare them). Warp star check uses SunSettings.displayName. No gameplay change beyond making 12.31 compile.

**GitHub title:** `[12.31.2-dev] SavedGrid cosmic fields compile fix`

**Manual steps:** none. Verify it compiles.

### [12.31.1-dev] SolarHazard Compile Fix

**Type:** PATCH - leftover duplicate closing braces in SolarHazard.cs (CS8803). No gameplay change.

**GitHub title:** `[12.31.1-dev] SolarHazard compile fix`

**Manual steps:** none. Verify it compiles.

### [12.31.0-dev] Warp Redo, Deep-Space Save, No Sun Hops

**Type:** MINOR - warp transit is a new tunnel (cockpit overlay + hull streaks, no camera shake). A hop never arrives inside the star. Deep-space logout writes cosmic km on the hull and restores the origin before the grids, so a ship saved in space comes back. Confirm wedges click like the build wheel. Nested origin shifts (seated pawn registered twice) were the cockpit twitch; velocity along the nose is carried through the jump. SOL APPROACH no longer stacks six identical cards. No recipe or setup changes.

**GitHub title:** `[12.31.0-dev] Warp redo, deep-space save, no sun hops`

#### Warp

The old bubble/shake sequence is gone. Pre-charge stretches a forward tunnel, a short flash drops you out, then the streaks fade. Screen FX only for the seated local player; observers still see the hull tunnel. No AddShake. FOV kick is mild and reset on arrival.

#### Sun

Planet-lock skips the star. Every destination is pushed outside SolarHazard.SafeWarpStandoffKm. Heat warnings read the seat/hull, not a parked pawn, and identical toasts refresh in place.

#### Save / twitch / autopilot

Grids save hasCosmic. Load restores the player (and origin) first, then hulls from cosmic km. ShiftWorld no longer double-moves a seated pawn. Arrival snaps the body, reseats the pilot, resets camera transients, and keeps forward speed so F3 cruise has something to work with.

#### Confirm

Mouse is free when the wheel opens. Point left or right and click, same as the building menu. Ctrl still locks look if you want it.

**Manual steps:** none - code-only. Verify in Unity: save/quit in space beside a ship (hull is there on rejoin); warp (tunnel, no shake, no SOL spam, not next to the star); point-and-click JUMP/ABORT; F3 after a hop (ship moves unless you already arrived on the nav target).

### [12.30.1-dev] Confirm Cursor Compile Fix

**Type:** PATCH - ConfirmDialogHud.ApplyCursor now uses UnityEngine.Cursor so it compiles under UI Toolkit (Cursor was ambiguous with UnityEngine.UIElements.Cursor). No gameplay change.

**GitHub title:** `[12.30.1-dev] Confirm cursor compile fix`

**Manual steps:** none - code-only. Verify in Unity: the project compiles; the jump wheel still opens.

### [12.30.0-dev] Jump Confirm Slider, Input-System Fix, Seated Autopilot, Seated Save

**Type:** MINOR - the jump confirm actually stays up (it was dying every tick on UnityEngine.Input under Input System only). The wheel now has a drive-count slider: Ctrl frees the mouse to drag it, the last choice is remembered (and saved on the grid) until you change it. F3 while seated flies the hull you are in instead of looking 60 m from a parked pawn. Saving while seated no longer deletes the ship: the player is unparented for the snapshot so the hull is written as its own grid. No recipe or setup changes.

**GitHub title:** `[12.30.0-dev] Jump confirm slider, seated autopilot, seated save`

#### Confirm wheel

ConfirmDialogHud never calls UnityEngine.Input when the Input System package is the handler (that InvalidOperationException was aborting Tick, so the ring never held). Look stays locked until Ctrl; then the mouse is free for the DRIVES slider (1..N enabled drives). A/D still pick JUMP / ABORT. The selection is GridEntity.WarpDrivesToUse (0 = all), restored on load, and the same slider sits on the warp drive panel.

#### Autopilot from the seat

F3 was measuring distance from the disabled player pawn (left at the last foot position). After you flew, that was further than 60 m. Seated F3 now engages ActiveControlGrid directly.

#### Seated save

SaveAll unparents the seated pawn, writes the grid, then reparents. A nested pawn made the hull look like player hierarchy and it did not come back on rejoin.

**Manual steps:** none - code-only. Verify in Unity: press warp (wheel stays up, no Input exception); two-plus drives, Ctrl, drag the slider, jump, reopen (same count); F3 from the seat with a nav target (engages, no "no ship in reach"); sit in a ship, save/quit/rejoin (hull is there).

### [12.29.0-dev] Confirm Every Jump, Warp Leave-Behind, Beacon Map, Real Dampeners

**Type:** MINOR - seated warp always asks before the hop (full bank or partial); autopilot still fires without the wheel. The jump no longer leaves the hull/pilot behind: world origin shift moves each registered rigidbody with its transform, and arrival snaps the grid body and seated pilot onto the cockpit. The confirm wheel is dark wedges with white labels; hover is left/right of screen centre (or A/D / arrows), so abort is actually selectable. A powered Grid Beacon paints the hull on the orbital map even when unnamed. Inertia dampeners brake with the grid's thrusters and gyros instead of zeroing velocity. No recipe or setup changes.

**GitHub title:** `[12.29.0-dev] Confirm every jump, warp leave-behind, beacon map, real dampeners`

#### Confirm before warp

Firing from the seat always opens the wheel (Jump / Abort, or Jump partway when the bank is short). Enter only takes the highlighted wedge; Esc / right-click abort. Autopilot warp legs pass skipConfirm so cruise does not stall on the dialog. Cockpit warp input is ignored while the wheel is open.

#### Warp leave-behind

SpaceOrigin.ShiftWorld now adds the same delta to each registered root's Rigidbody.position. After TeleportCosmic, CommitJump (and the partial hop) snap Grid.Body.position to the transform and the seated Pilot onto the cockpit, then zero residual velocity.

#### Confirm wheel

Wedges are charcoal; hover is a dark green / dark red. Labels stay white. Hover follows mouse X versus screen centre (dead zone in the middle) plus A/D and arrows. Default hover is none, so Enter does not fire until a wedge is chosen. Click uses the hovered wedge, not the inner disc.

#### Beacon on the map

GridBeacon keeps a live All list. OrbitalTrackingService adds a powered unnamed beacon hull as an in-range contact (named GridIdentity still wins; unnamed scrap without a beacon stays off).

#### Real dampeners

Seated and autopilot hold call ApplyAutonomousDampenerThrust (thrusters opposing velocity) and gyro torque against spin. Unmanned grids still preserve the gravity axis unless hover-hold. No linearVelocity / Acceleration-30 snap.

**Manual steps:** none - code-only. Verify in Unity: sit in a charged warp drive, press the warp key (wheel, point right or D then Enter/click to jump, left or A then Enter/click or Esc to abort); fire a short-banked hop the same way; after a jump the hull and you arrive together; F3 warp legs still auto-fire with no wheel; a powered beacon on an unnamed hull appears on the orbital map; dampeners on with thrusters fitted bleed speed instead of a hard stop, and a ship with no gyros keeps spinning.

### [12.28.2-dev] Partial-Jump Wheel - Point and Click, No Mouse Lock

**Type:** PATCH - the partial-jump confirm sat under a locked cockpit cursor, so neither button could be reached. It is now a two-wedge radial in the hammer-wheel language: UIState unlocks the look, point at JUMP or ABORT, click. Enter/Space takes the highlighted wedge, Esc / right-click aborts. No recipe or setup changes.

**GitHub title:** `[12.28.2-dev] Partial-jump wheel - point and click, no mouse lock`

**Manual steps:** none - code-only. Verify in Unity: fire a short-banked warp from the seat; the cursor frees, the ring appears, point right and click (or press Enter) to jump partway, Esc or the left wedge aborts; look relocks after.

### [12.28.1-dev] Autopilot Turns the Ship, Godmode That Actually Works, Mass Cap 12x

**Type:** PATCH - engaging fly-to now points the hull: gyros swing the strongest thrust axis onto the flight line, and warp legs aim the drive cone themselves (seated or unmanned). Infinite health stopped working because every frame the prefab checkbox wrote false over the Settings toggle; Settings is now the source of truth and DoT / vacuum / heat honour it too. Mass penalty cap is 12x (was 8x). No recipe changes.

**GitHub title:** `[12.28.1-dev] Autopilot turns the ship, godmode that actually works, mass cap 12x`

#### Autopilot orientation

Cruise used to command velocity with rotation pinned at zero, and a seated warp leg handed the mouse a parked gyro. The ship never lined up, so main engines sat idle and the warp cone never closed. Gyros now own the nose while engaged: cruise points the strongest of the six thrust axes along the commanded velocity (or at the target when holding), warp points the cockpit/grid forward at the destination and fires when the cone is inside the lock. No gyros still translates; warp refuses with the named reason. Stick take-over is unchanged.

#### Infinite health

PlayerController.Update was copying the inspector checkbox onto GameSettings every tick, so the Testing row could never stay on. Settings is the live flag; the checkbox mirrors it. Poison, burn, caustic, vacuum, heat, toxic and radiation drains now skip the same way TakeDamage already did.

#### Mass cap

maxMassFactor default and fallback are 12. Step 50 writes 12 when the field is zero or still the old 8 default; any other authored value is kept.

**Manual steps:** none - code-only. Verify in Unity: F3 with gyros fitted (nose turns onto the target, warp aims and fires without touching the mouse); Settings > Testing > Infinite Health, take a hit and stand in a hazard (no damage, toggle still on after reopen); a 6400 t hull shows 12x / 208 km hop.

### [12.28.0-dev] Jump Mass Penalty - Heavy Hulls Hop Shorter

**Type:** MINOR - maximum warp range now falls as the ship gets heavier. Hop length and Wh-per-km share one live factor, sqrt(hull mass / 100 t), capped at 8x, so one full drive still equals one hop. Cargo already sits in Grid.TotalMass, so filling a hold is the same as adding plates. The drive panel shows max hop, hull mass, the penalty and the live price; the autopilot banks that heavier price. Light ships at or under 100 t are unchanged. No recipe or setup changes.

**GitHub title:** `[12.28.0-dev] Jump mass penalty - heavy hulls hop shorter`

#### How the penalty works

The drive is rated at 100 t. Below that the published 2500 km hop and 4 Wh/km still hold. Above it the factor is sqrt(live mass / 100 t), so 400 t costs 2x and hops 1250 km, 1600 t costs 4x and hops 625 km. The cap is 8x (312 km hop, 32 Wh/km) so a loaded hauler still jumps. Planet-lock jumps still travel the real distance - they just pay the heavier price. Blind hops shrink. Dumping cargo recovers range on the next tick.

#### What the player sees

The warp panel adds Max hop, Hull mass and Mass penalty (RATED in green, or 1.41x in amber) and the jump price now ticks live. A short bank on a heavy ship still offers the partial-jump popup, with the mass-adjusted numbers, and the refuse toast says dump cargo as well as fit more drives.

#### Manual steps

None - code-only. Existing Warp Drive prefabs pick up the new fields at script defaults (100 t / 8x). Re-running Tools > Voxel Engine > Voxel Engine Setup step 50 fills ratedMassKg / maxMassFactor only when they are zero; power, hop, recipe and research are not touched.

Verify in Unity: open a warp drive on a light empty hull (penalty RATED, 2500 km hop, 4 Wh/km); load cargo until well past 100 t (penalty >1, hop shorter, price up, Range now down); fire a blind hop and confirm the shorter distance; dump the cargo and watch hop and price recover.

### [12.27.0-dev] F3 Double-Toggle Fix, Orbital Map Save, Godmode That Sticks and Partial Jumps

**Type:** MINOR - F3 engaged then instantly disengaged because the hotkey fired from two tick paths; the duplicate is removed and Toggle is now guarded to one action per frame. The orbital map no longer vanishes on load: equipment instrument slots are now saved and restored like every other slot group. Infinite health actually sticks now: the toggle persists in settings (new Testing row plus the PlayerController checkbox) instead of dying with the spawner. Mouse gyro parks while the autopilot flies (warp legs excepted, where you still aim). And a short bank no longer means no jump: firing with insufficient energy offers a partial-jump popup showing distance, percentage and remainder, and Accept flies the part the bank buys. No recipe or setup changes.

**GitHub title:** `[12.27.0-dev] F3 double-toggle fix, orbital map save, godmode that sticks and partial jumps`

**Manual steps:** none - code-only. Verify in Unity: press F3 once (stays engaged); save/load with the orbital map equipped (still there); tick infinite health, reload, take a hit (no damage); engage and move the mouse (nose stays); fire short-banked (popup with % and km, Accept jumps partway).

### [12.26.0-dev] Autopilot Take-Over, F3 Key, God-Mode Toggle and Warp Screen Redo

**Type:** MINOR - the fly-to autopilot now behaves: it requires an AutoRunPilot block aboard (engage refuses and a live leg disengages without one), and grabbing the stick disengages with Autopilot disabled, player input detected instead of fighting you for the ship. The autopilot hotkey moves off P (landing gear / parking) to F3, is wired up for the first time (it was map-button-only despite the label), and stays rebindable in Settings like every other action. PlayerController gets an infiniteHealth testing toggle (damage immunity; forced respawn still works). And the warp screen is redone: thin bright speed lines instead of chunky bars, a much subtler edge glow. No recipe or setup changes.

**GitHub title:** `[12.26.0-dev] Autopilot take-over, F3 key, god-mode toggle and warp screen redo`

**Manual steps:** none - code-only. Verify in Unity: engage fly-to without an AutoRunPilot (refused), with one (flies), grab the stick (disengages with the message); press F3 to toggle; rebind it in Settings; tick infiniteHealth and take a hit; jump and confirm the new screen effect.

### [12.25.1-dev] Warp Smear Fix, Arrival Readout and Particle API Fix

**Type:** PATCH - the warp effect smeared the ship itself: the bubble's near wall washed over the hull and the streak particles were huge additive smears, so the bubble now renders its far shell only (ship stays crisp inside) and the streaks are small dim speed lines. Fixes the WarpFx compile error on this Unity version (StretchBillboard does not exist here, Stretch does) and the obsolete ShapeModule.box warning (now scale). And the arrival finally answers where you are: the jump toast shows the distance jumped, and the cockpit WARP LCD holds an arrival line for ~25 s (destination, origin to here, km, plus nearest body after a blind hop). No recipe or setup changes.

**GitHub title:** `[12.25.1-dev] Warp smear fix, arrival readout and particle API fix`

**Manual steps:** none - code-only plus one shader tweak. Verify in Unity: fire the drive and confirm it compiles clean, the ship stays crisp inside the bubble with small streaks past the hull, the toast shows the km jumped, and the cockpit WARP line holds the arrival readout afterwards.

### [12.25.0-dev] Warp FX - Jump Effect, Live Warp Key and Cockpit Warp Readout

**Type:** MINOR - the jump drive gets its moment: firing the drive now swallows the ship in a pulsing energy bubble, stretches the stars into warp streaks past the hull, tunnels the screen with a flash, punches the FOV wide and rumbles through a synthesized riser-to-whoosh (all procedural, no assets; screen FX only for the grid the player is aboard). Also fixes the warp key mixup: the v17 rebind moved warp N to U but the drive still said press N, so the prompt now shows the live binding and the setup docs say U. And the cockpit finally shows the autopilot warp leg line (aim/charge/bank) on its LCD, so a seated pilot can see the ship is waiting on their aim instead of wondering why it never fires. No recipe or setup changes.

**GitHub title:** `[12.25.0-dev] Warp FX - jump effect, live warp key and cockpit warp readout`

**Manual steps:** none - code-only plus one shader file. Verify in Unity: charge and fire the drive (U by default) and confirm the bubble/streak/screen/FOV/sound sequence plays and the ship arrives; engage the autopilot on a long leg while seated and confirm the cockpit WARP LCD line appears and the drive auto-fires once aimed.

### [12.24.1-dev] Grid Gas Topology Cache - Piped Thruster Lag Fix

**Type:** PATCH - per-tick thruster gas queries on grids walked OverlapSpheres, corridor sweeps and port-tree scans every 0.15 s per thruster (hundreds of physics probes per second per ship: the 5 FPS freezes with pipes near thrusters). Link discovery now runs once per topology change into a cached per-grid snapshot using the same predicates, and queries follow cached links in microseconds. Wrench blacklist, tank enabled/type/mode stay live per query, so routing behaviour is unchanged. No recipe or setup changes.

**GitHub title:** `[12.24.1-dev] Grid gas topology cache - piped thruster lag fix`

**Manual steps:** none - code-only fix. Verify in Unity: build a ship like the report (a few brass pipes feeding 4 hydrogen thrusters), run it, and confirm the frame rate stays smooth while thrusting; then place/remove a pipe mid-run and confirm gas connects within a couple of seconds.

### [12.24.0-dev] Warp Core - Gas Pipe Perf Fix, Warp Battery Fuel and Drive Panel

**Type:** MINOR - gas tank lookups move from per-query physics BFS to a cached tank map (the 5 FPS at 10+ pipes), and the warp drive gets its fuel model plus the hero panel: an internal battery per drive, pooled range, a recharge toggle, max-draw display, and a live lime-on-black widget with power visibly streaming in while it charges. No recipe or setup changes.

**GitHub title:** `[12.24.0-dev] Warp core - gas pipe perf fix, warp battery fuel and drive panel`

#### Gas pipes stop melting the frame rate

Every gas tank lookup used to walk the pipe network with physics: each visited pipe fired a sphere probe plus a 31-probe corridor sweep, so one question on a 10-pipe run cost 300+ overlap queries - twice a second per machine. Tank discovery now happens once per topology change plus one pipe per quarter-second on a rolling refresh, and queries are dictionary lookups with a cheap range check (no physics, no BFS, no per-query allocation). New tanks still register instantly through topology dirties, and cached links drop the moment grids drift apart instead of drawing gas across the gap. Same tanks found, same filters, roughly 10x cheaper.

#### Jump fuel: range is bought, not granted

Each warp drive now carries a 10 kWh internal battery fed from the grid bus, and all enabled drives on a grid pool their stores. Jump cost is distance times Wh-per-km (default 4, so one full drive flies exactly one fixed hop), consumed proportionally across the pool; short banks refuse with the exact numbers and the honest advice (recharge, or fit more drives). The 45-second spin-up stays as the coils warming - fuel and spin-up are independent, and a starved grid banks slowly instead of pretending. Stored energy, the recharge toggle and cooldowns persist with the grid.

#### The drive panel

Opening a warp drive now shows the hero widget: big fill %, live charge kW with a status dot, a bolt in a ring that pulses while power streams in from both sides, a time-to-full pill plus red stop, then stored/pooled/range/max-draw/price/spin-up/cooldown stats and RECHARGE, SPIN UP and JUMP controls. The autopilot banks for its legs automatically (player toggle untouched) and reports BANK vs need on the map card.

### [12.23.0-dev] Jump Legs - Autopilot Warp Integration

**Type:** MINOR - long fly-to legs now jump: the autopilot aims the ship, charges the warp drive, fires when aligned, and resumes the cruise after arrival - planet-lock captures in one jump, longer hauls by repeated aimed hops. No recipe or setup changes; no new blocks or items.

**GitHub title:** `[12.23.0-dev] Jump legs - autopilot warp integration`

#### How a warp leg flies

When the remaining distance is past one fixed hop, or a target body sits inside the drive's capture band, the cruise hands over to the warp states: WARP-AIM turns the ship (unmanned ships steer themselves through their gyros; seated pilots aim with the mouse and the map shows how many degrees off they are), WARP-CHARGE holds the aim while the drive charges, then fires automatically when charged and aligned. The ship keeps cruising throughout - a jump zeroes velocity anyway, so stopping first would only waste time. Capture jumps land exactly on the autopilot's own hold shelf, so arrival just happens.

#### Honest jumping

Everything reads the drive's live tuning: capture band, hop range and cone angle all follow the block's own numbers. Cooldowns are cruised through and shown on the map card; a stalled charge says STALLED (power?); three refused fires, unmanned aim without gyros, or 90 seconds of failing to line up abandons warp for the leg with the reason announced - the cruise still completes, just slower. Disengaging never destroys a paid-for charge: a charging drive finishes and sits ready for a manual firing.

#### Safety rails

Two new live guards ride every leg: retargeting to the sun mid-flight releases the ship instead of flying into it, and touching atmosphere releases it instead of lithobraking at cruise speed. Aiming is computed through the exact frame the drive fires along, so a sideways cockpit can no longer send the jump the wrong way.

#### What stays open

Route-book warp legs for the auto-run loops and the destination-select UI remain open under item 15; hazard avoidance, dock approach, cargo ops and atmospheric legs remain open under item 8.

### [12.22.1-dev] Fly-To Compile Fix

**Type:** PATCH - fixes the 12.22.0-dev compile error (`reason` used before assignment on the engage path) and the `FindObjectsSortMode` deprecation warnings (now uses the `FindObjectsInactive.Exclude` overload like the rest of the codebase). No behaviour changes.

**GitHub title:** `[12.22.1-dev] Fly-to compile fix`

### [12.22.0-dev] Fly To - Nav-Target Autopilot Cruise Control

**Type:** MINOR - press P (or the map's ENGAGE AUTOPILOT card) and the ship in reach flies itself to the orbital map's nav target: a braking-curve cruise computed from live thrust and mass, stick override with resume, and an arrival hold on the warp shelf. No recipe or setup changes; no new blocks, items or wizard steps.

**GitHub title:** `[12.22.0-dev] Fly to - nav-target autopilot cruise control`

#### Engage and fly

Set a nav target on the orbital map (M), stand by your ship (or stay in the seat), and press P - or use the new AUTOPILOT card on the map itself. The nearest ship within 60 m flies the leg: seated pilots keep their seat, camera and tools while the cruise owns translation, and unseated engages get a 3-second STAND CLEAR countdown first. The map card shows the live leg underneath: state, target, distance, speed and ETA.

#### Honest physics, said out loud

Speed is governed by a braking curve, not a wish: v = sqrt(2*a*d) from the ship's own directional thrust and live body mass, capped at 2500 m/s, so a heavy ship with a weak drive genuinely takes longer to stop. Legs that cannot brake along the flight line are refused (or released mid-flight) with the reason announced, legs that make no progress for 25 seconds are abandoned rather than flown forever, and the autopilot will not fly into the sun. Touching the translation stick overrides instantly and the cruise resumes on release; rotation always stays live.

#### Arrival and hold

Bodies are met at the surface plus 90 km - the same shelf the warp drive arrives on - while ships, stations and other contacts hold at 300 m. On arrival the ship keeps station and says so; P releases it. Exiting the seat mid-cruise does not stop the ship: it continues unmanned and announces that too.

#### What stays open

This is the first autopilot leg only: vacuum flight, one ship at a time, no terrain or traffic avoidance, no atmospheric legs, no docking, and no warp legs yet. Those remain open under roadmap item 8 (warp legs additionally under item 15).

### [12.21.0-dev] True Colours - Planet Hues, Pollution Readout, Bigger Text and Orbit Pace

**Type:** MINOR - planets and moons wear their authored hues on the star map, every body row carries a pollution readout (wired, awaiting the simulation), all map text is bigger, and world creation offers a Realistic / Arcade orbit pace so planets visibly sweep in arcade worlds. No recipe or setup changes; old saves default to realistic pace.

**GitHub title:** `[12.21.0-dev] True colours - planet hues, pollution readout, bigger text and orbit pace`

#### Planets in their own colours

Every planet and moon asset authors a `displayColor` - the same hue its sky beacon already wears - and the map now uses it for markers, labels and sidebar rows, so Mars reads red, ice reads pale and volcanic reads ember. Bodies without an authored hue keep the classic blue/grey kind colours. Orbit rings stay uniformly green: one trajectory language, no rainbow spaghetti.

#### Pollution, ready when you are

Each planet and moon row now shows a `POLLUTION 0%` line under its motion state. The value flows through the tracking snapshot from a clearly-marked seam that returns zero until the pollution simulation lands - wire the real per-body source there and every readout lights up with no further map changes.

#### Bigger text

All map type is bumped: floating labels grow to 12/11 px, sidebar rows to 11/9/8 px, and the header, status, focus, nav and hint lines each step up. Layout and the 310 px sidebar are unchanged; everything still fits.

#### Orbit pace at world creation

The new-world form's solar-system section gains an `ORBIT PACE` picker: `REALISTIC` (true Keplerian periods - a year takes days) or `ARCADE` (planet orbital speed x120, a year in minutes, visibly sweeping on the map). The choice is stored per world in the cosmos sidecar next to the system and seeds; old saves without the key default to realistic, and it is deliberately not editable after creation since it shapes generation. Only planet elements are accelerated - moons, rails craft, seasons and lighting keep normal time.

#### Manual steps in Unity

1. Apply the patch on top of 12.20.0-dev and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press `M`: planets wear distinct hues in markers, labels and rows; each planet/moon row shows `POLLUTION 0%`; all text is visibly larger.
4. Create a new world: the solar-system section offers `REALISTIC` / `ARCADE` orbit pace with realistic preselected.
5. Start an arcade world, open the map and watch: planets visibly move along their rings within a minute or two.
6. Load an older save: it plays exactly as before (realistic pace, no behaviour change).

### [12.20.0-dev] Clean Chart - Trajectory Toggles and Map Legibility

**Type:** MINOR - the star map gets trajectory visibility toggles per class (planets, constructs, satellites), zoom-adaptive planet markers that stay readable when zoomed in, and label decluttering so the system's heart stops piling names on top of each other. No save, recipe or setup changes.

**GitHub title:** `[12.20.0-dev] Clean chart - trajectory toggles and map legibility`

#### Trajectory toggles

A small `TRAJECTORIES` card sits top-right of the map with three checkboxes: Planets (planet and moon paths), Grids (ship and station trajectories) and Satellites. Unchecked hides that class's rings and trails; checked shows them. The choice persists across sessions, and the equipped device's orbit-path capability stays the master switch underneath. Flipping a checkbox never pans the map or disturbs the nav target.

#### Readable at any zoom

Body markers kept to true scale shrank to dots the moment you zoomed in on their moons. Markers now keep a zoom-adaptive floor (3 px at system zoom, growing to 20 px fully zoomed in), so a planet stays a readable disc at any magnification. Click hit-testing follows the same sizes, so what you click is still what you see.

#### Declutter

A moon hugging its planet keeps its name to itself until zoomed in enough to stand clear of it, which alone clears the label pile-up at the system's heart. The belt's label rides the bottom edge of its ring instead of sitting on the crowded centre. Planet, sun and construct labels are unchanged.

#### Manual steps in Unity

1. Apply the patch on top of 12.19.2-dev and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press `M`: the `TRAJECTORIES` card sits top-right with Planets, Grids and Satellites checked.
4. Uncheck Planets: planet and moon rings and trails vanish; recheck and they return. Same for the other two.
5. Zoom fully in on Earth: the planet stays a readable disc instead of a dot, and the Moon's label appears once it stands clear.
6. Close and reopen the game: toggle choices are remembered.

### [12.19.2-dev] True Plane - Orbit Map Projection and Path Fixes

**Type:** PATCH - fixes how orbits display on the star map: the map projected the XZ plane while the system actually orbits in the XY reference plane, so every planet drew in an edge-on row; planet solar ellipses were skipped entirely; and body rings were axis-aligned fictions. The map now projects the true orbital plane, draws every ellipse, and samples body rings and trails from the live elements. No save, recipe or setup changes.

**GitHub title:** `[12.19.2-dev] True plane - orbit map projection and path fixes`

#### The map was edge-on

Orbital elements are seeded about the reference (XY) plane, but the map projected top-down XZ - exactly edge-on to every orbit. That is why the whole system drew as a single row of planets. The projection now uses XY, so the system opens face-on: planets spread around the sun with their true shapes, and the same frame carries labels, clicks, the belt and the nav reticle with it.

#### Rings for every planet

The ellipse pass skipped any entry with a null parent pointer - which is every planet, since the sun is not a `BodyInstance`. Planets now resolve their parent by name (the sun's entry is always present), so each planet draws its solar ellipse. Craft behave exactly as before.

#### Exact paths, exact trails

Body rings are no longer axis-aligned ellipses from apoapsis/periapsis radii: the tracking service now carries each body's node, argument of periapsis and live true anomaly, and the map samples the ring through the same elements-to-position path as propagation. Whatever orientation and inclination was seeded, the ring matches it - and the body always sits exactly on its own ring. Trails are the true-anomaly arc behind the body's live position through the same sampler. The parent-surface padding that inflated moon rings is gone for bodies (their radii were already centre-based); craft keep it, since their telemetry is altitude-based.

#### Belt band

The belt gains its inner edge: the shell now reads as a band between its nearest- and furthest-rock radii instead of a single ring, with the rock motes scattered between.

#### Manual steps in Unity

1. Apply the patch on top of 12.19.1-dev and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press `M`: planets spread face-on around the sun, each with its own ellipse - no more single row.
4. Zoom to Earth and the Moon: the Moon sits on its ring with Earth at the focus, and its trail hugs the ring behind it. (If the ring still looks off-centre, check the local `Moon_Earth` template's `orbitEccentricity` - the focus offset is real physics for the authored value; 0 gives a centred ring.)
5. Confirm clicks, the nav reticle, the belt band and the sidebar behave as in 12.19.0-dev.

### [12.19.1-dev] Local Click - Orbital Map Pointer Fix

**Type:** PATCH - fixes a compile error in the star map's click-to-acquire handler: `PointerUpEvent` exposes the cursor as `localPosition`, not `localMousePosition`, which only exists on the mouse-event family. One-word fix, no behaviour change.

**GitHub title:** `[12.19.1-dev] Local click - orbital map pointer fix`

#### Manual steps in Unity

1. Apply the patch on top of 12.19.0-dev and recompile: the CS1061 error is gone.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press `M` and click a contact: the navigation target still acquires exactly as in 12.19.0-dev.

### [12.19.0-dev] Chart the Belt - Star Map Navigation Targets, Asteroid Fields and Trails

**Type:** MINOR - the orbital map grows its three missing pieces: click any contact to set a persistent navigation target, the system's asteroid shell draws as a rocky region instead of empty space, and orbiting bodies trail their paths behind them. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.19.0-dev] Chart the belt - star map navigation targets, asteroid fields and trails`

#### Click a contact, keep it

Clicking a contact on the map canvas now sets it as the navigation target: a gold corner-tick reticle marks it on the chart, a `NAV TARGET` line names it in the header, and its sidebar row carries a `[NAV]` tag. When the equipped device allows focus switching the map also follows the new target. Clicking empty space clears the target and hands pan control back. The target persists across sessions and always resolves live - bodies from the cosmic registry, craft from their grid identity - so it tracks a moving ship even with the map closed. The new `NavigationTarget` service is UI-free on purpose: item 8 (route recorder and autopilot) will follow it without touching the map.

#### The belt on the chart

The tracking snapshot aggregates the system's scattered rocks into one `Asteroid Belt` contact at the shell centroid, listed in the BODIES section like any other body. The map draws it as a region rather than a disc: a boundary ring at the shell radius plus up to 220 stride-sampled rocks as dust motes, so the shell reads as a belt at any zoom without swallowing the inner system. The ring is clickable by its edge - its centre is usually the sun, which keeps its own grab radius. Paint, labels and click hit-testing now share one view-frame helper, so what you click is always what you see.

#### Trails without history

Every contact with a drawn orbit ellipse now trails a fading arc behind its current position - three chunks along the last stretch of its own path, brightest at the body. The arcs are analytic, read straight off the solved apoapsis/periapsis elements in the same focus-offset convention as the ellipses, so they lie exactly on the drawn orbit with no position-history buffer to maintain. Trails share the orbit-path device gate, like the ellipses themselves.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Equip an Orbital Map and press `M`: zoom out and find the belt ring with its dust motes and the `Asteroid Belt` row under BODIES.
4. Click a moon: the gold reticle, the `NAV TARGET` header line and the `[NAV]` row tag appear, and the map follows it.
5. Click empty space: target and follow clear, and drag-panning works freely again.
6. Confirm orbiting moons and craft trail fading arcs along their ellipses.
7. Close the game, reopen, press `M`: the navigation target is restored.

### [12.18.0-dev] Silence Between Stars - Vacuum Audio Ducking

**Type:** MINOR - new global vacuum ducking: exterior sound now fades with the air at the listener, so spacewalks go silent while sealed cockpits keep hearing the ship. UI, music and pickup cues bypass it entirely. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.18.0-dev] Silence between stars - vacuum audio ducking`

#### One question, one answer

The new `VacuumAudio` asks the same question every frame - how much exterior sound survives at the listener - and every exterior emitter multiplies by the answer. Sampling is physical: open-sky pressure from the atmosphere model, ship-room pressure from the pressure system, station-room fill from the room solver, best air wins. Full sound at 0.3 atm and above, linear fade below, hard silence at zero. The value eases toward its target (fast out, slower back), so flying through an airlock threshold never pops.

#### What goes quiet, what stays

Machine loops, thruster roar, wind, wildlife and cave beds, the full weather mix including thunder, positional one-shots (horns, steam, placement) and tool hits all scale with the air. A pilot in a pressurised cockpit hears everything; an engineer on EVA hears nothing but the suit-adjacent layer: UI clicks, toasts, pickups and music are untouched by design. Thin high-altitude air lands in between - quieter, not gone.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Fly a ship out of atmosphere: machine and thruster loops fade to nothing; UI clicks and music stay.
4. Sit in a sealed, pressurised cockpit in vacuum: the ship reads full again.
5. Step back into air: the soundscape swells back over about two seconds.
6. Confirm weather, ambience and one-shots behave as before on the surface.

### [12.17.4-dev] Quiet Console - Craft Diagnostics Removed

**Type:** PATCH - removes the temporary craft-failure diagnostics now the floats are proven working: click/refusal traces, float mirrors, the render probe and the now-unused destination state helper. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.17.4-dev] Quiet console - craft diagnostics removed`

#### What left the code

The `[Craft]` click and refusal traces in both recipe browsers, the `[CraftFeedback]` mirror and position logs plus the half-second render probe in `FloatAt`, and `Crafter.DescribeSpace`, which only existed to feed the refusal line. Player-facing feedback is untouched: failed crafts still float the reason at the button and post a toast for later. The genuine error guards stay - a missing float layer still warns once, and craft exceptions still log with a full stack.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Click CRAFT on a blocked recipe: float and toast appear, console stays silent.
4. Craft successfully: no new console noise, queues and batches behave as before.

### [12.17.3-dev] Floats Rise By Hand - Manual Animation And Prefixed Mass

**Type:** PATCH - the render probe caught the invisible float red-handed (opacity already 0.00 half a second after spawn): style transitions snap on freshly-created elements, so the rise-and-fade is now driven per-frame by hand. Mass ratios revert to proper SI prefixes on both sides per feedback. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.17.3-dev] Floats rise by hand - manual animation and prefixed mass`

#### Transitions out, ticks in

The probe line said it all: attached panel, sane bounds, Flex display, Visible visibility - and opacity 0.00 at +500ms into a 2-second fade. UITK style transitions never animate on an element that has not painted its start values yet; they jump straight to the target. `FloatAt` now drives the animation itself with 16ms scheduler ticks over unscaled time: ease-out rise across 46px, linear fade across 2 seconds, then the ticker pauses and the label removes itself. Same look as designed, none of the transition system. The probe stays one more round to confirm opacity reads ~0.75 at +500ms.

#### Prefixes stay prefixed

The single-unit ratio experiment is reverted: current/max mass pairs print each side in its own correct SI unit again ("222.24 t / 450 kg", kilotonnes when greater), on the inventory CARGO LOAD card, the grid cargo readout and the grid terminal list. The overweight float and the console state dump follow the same rule, so every mass the player reads carries its prefix.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Click CRAFT on an overweight-blocked recipe: the reason visibly floats up from the button and fades over 2 seconds.
4. Confirm the PROBE line now reads opacity ~0.75 and the overweight text carries prefixes.
5. Shed load and confirm crafting resumes with no new console noise.

### [12.17.2-dev] One Unit Per Ratio - Cargo Load And Float Probe

**Type:** PATCH - the cargo load card mixed units ("222 t / 450 kg"), hiding a 500x overload at a glance; all three current/max mass ratios now share one unit. Plus a render probe on the floating text, which logs show is spawned correctly but never draws. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.17.2-dev] One unit per ratio - cargo load and float probe`

#### The overload you could not see

The 12.17.1 console ladder proved the craft refusals were legitimate: the test inventory carried 222,242 kg against a 450 kg cap. But the CARGO LOAD card formatted each side independently, printing "222.24 t / 450 kg" - two units side by side, trivially misread as fine. The card now uses the existing `MassFormat.FormatRatio`, which picks one unit from the capacity: "222242 / 450 kg". Unmissable. The same mixed-unit pattern in the grid cargo readout and the grid master terminal inventory list is fixed in the same way.

#### Hunting the invisible float

The logs also proved the float pipeline runs cleanly: callback fires, reason computed, label spawned on the TopLayer at the button's position, no exceptions - yet nothing draws, while toasts on the HUD layer render fine. A scheduled probe now logs the label's live render state half a second after spawn (attached panel, world bounds, resolved opacity, display, visibility), which separates a detached tree, an off-screen position and a transparency fault in one retest.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Open the inventory: CARGO LOAD shows both numbers in one unit.
4. Click CRAFT on an overweight-blocked recipe and copy the `[CraftFeedback] PROBE` console line.
5. Shed load below the cap and confirm crafting resumes with no new console noise.

### [12.17.1-dev] Feedback You Cannot Miss - Console Mirror And Toasts Return

**Type:** PATCH - the 12.17 floats never appeared in-game, so failed crafts now report three ways at once: a console log (the guaranteed channel), the floating text at the button, and the returning toast (visible after closing the panels). No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.17.1-dev] Feedback you cannot miss - console mirror and toasts return`

#### Three channels, one reason

Every failed craft in both recipe browsers now logs `[Craft] Refused ...` with the exact reason plus a destination state dump (space verdict, live weight, accept-gate flag), floats the reason at the clicked CRAFT button, and posts a toast for after the panels close. Clicking CRAFT also logs `[Craft] Click ...`, which proves whether the button callback runs at all. Floats mirror to `[CraftFeedback]` in the console with their layer and position, and a throwing float can no longer swallow its own caller.

#### What this diagnoses

One retest now separates every suspect: no `[Craft] Click` means the button never fires; a `Refused` line names the exact gate (ingredients, space, weight, filter); console lines without visuals mean the feedback layers misbehave. The bench chrome was verified click-safe - every frame, divider and lamp element ignores picking.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Click CRAFT on a failing recipe, then copy the `[Craft]` / `[CraftFeedback]` console lines and report them plus whether the float and the toast appeared.
4. Confirm successful crafts still craft silently with no new console lines beyond the click entry.

### [12.17.0-dev] Craft Failures Float Up - Floating Feedback Text

**Type:** MINOR - new floating combat-text feedback: failed crafts now say why right at the clicked CRAFT button, naming the exact refusal (missing items, full inventory, overweight with live kg). No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.17.0-dev] Craft failures float up - floating feedback text`

#### Why floats instead of toasts

The 12.16.1 failure toasts worked, but they render on the HUD layer - behind the open inventory - so the player only saw them after closing the panels. The new `BuildFeedbackHud.FloatAt` renders on the topmost tooltip layer instead: a dark pill with the reason appears at the button, rises and fades over 2 seconds, then removes itself. It never blocks input.

#### The game names the exact gate

A shared `Crafter.CraftFailReason` mirrors the craft refusal checks so both recipe browsers explain themselves identically: the station right-pane browser (bench, assembler and friends) and the inventory centre browser. "Missing ingredients" in red, "Inventory full", "Overweight 449/450 kg", containment and carry gates in amber. A partial batch that crafts some and then fails still refreshes normally; the float only appears when nothing was crafted. Exceptions still log to the console and float "Craft error".

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Fill the inventory (or hit the weight cap) and click CRAFT on a craftable recipe: the reason floats up from the button and fades.
4. Try the same in the inventory centre browser: identical float.
5. Make space and craft again: crafts as before, no float.
6. Confirm queueing, progress, cancel and batch amounts all behave as before.

### [12.16.1-dev] Cryobed Docks Right And Craft Buttons Speak Up - Bench Fix Round

**Type:** PATCH - bugfix round for four bench-adjacent reports: the cryobed panel opened centre-screen, its oxygen bar looked plain, the filter search caret sat high while empty, and failed crafts died silently. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.16.1-dev] Cryobed docks right and craft buttons speak up - bench fix round`

#### Cryobed joins the side dock

The cryobed control panel now docks on the right like every other machine panel - full height, right edge, transparent backdrop - instead of floating centre-screen. It stays modal (Close button or Pause to dismiss), and the starship rails and hull divider from 12.16 carry over unchanged.

#### A tank worthy of the name

The oxygen readout is now a proper pressure vessel: valve cap and neck stacked above the body, cylinder shading down the flanks and level ticks at 25 / 50 / 75 %. The live fill, the empty/low/ok colour cues and the centred percentage all behave exactly as before.

#### Caret fix and honest craft buttons

The filter search fields in all three dialogs stretch their inner text element to the full field height, so the caret stays vertically centred while the field is empty instead of riding high. And the CRAFT button now reports failure: "Missing ingredients" when items are short, "No room for output" when the inventory is full or overweight, and "Craft error" (plus a console log entry) if anything throws. The success path is untouched.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on a cryobed: panel docks right, oxygen tank has valve, shading and ticks.
4. Open each filter dialog: the empty search caret sits centred.
5. Click CRAFT on a bench recipe: crafts as before; if one ever fails, a toast now says why - report the toast text.

### [12.16.0-dev] Dialogs And Decision Cards - Chrome For The Floating UI

**Type:** MINOR - presentation-only chrome for the floating dialogs and decision cards: item filter dialogs, the two void-confirmation modals, the item-ports overlay, the cryobed config dialog, the grid screen config dialog and the crafting bench card. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.16.0-dev] Dialogs and decision cards - chrome for the floating UI`

#### Every floating card framed

The machine panels are all chromed, so this round dresses the dialogs that float above them. The three item filter dialogs, the item-ports overlay and both void confirmations take industrial chrome - hazard divider, girder frame - with the drop-limit and tank-void modals keeping their caution semantics. The cryobed dialog takes the starship hull treatment with green rails when staffed and amber when calling for a colonist; the grid screen config takes high-tech scanlines in display cyan.

#### The bench reads its queue

The crafting bench card is the one decision card with a running state, so it earns status lamps: green while the queue has work, amber at rest. The pure dialogs and modals stay lampless - nothing runs inside them, so there is nothing to report. Every value, slot, filter, toggle and button is preserved - only the presentation changed.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Open each filter dialog on a filtered face: hazard divider and girder frame on all three.
4. Press E on a cryobed and a grid screen: hull rails and scanline divider respectively.
5. Open the item-ports overlay, both void modals and the crafting bench: framed cards, and green bench lamps while a craft is queued.
6. Confirm filters, ports, voids, naming, screen settings and crafting all behave as before.

### [12.15.0-dev] Holo Racks And Data Brackets - Storage Goes High-Tech

**Type:** MINOR - presentation-only restyle of all eleven storage network panels onto the high-tech instruments. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.15.0-dev] Holo racks and data brackets - storage goes high-tech`

#### One network, one family

The storage network is the most advanced system in the game, so all eleven panels take high-tech chrome as a single family: storage, pattern and crafting terminals, importer, exporter, disk manipulator, NAS block, server rack, drawer, drawer controller and item display. Corner-bracket holo frames with status-reactive accents, scanline dividers - green brackets on a live rack, red on a dead one, purple on patterns, amber on exports. Every value, slot, filter, queue, search field and hint is preserved - only the instruments changed.

#### Framed on every path

The three terminals each carry an offline branch for a missing or dead rack, and each branch is framed on its own path - no fallback card ships bare. The server's divider sits below its power bar exactly where it always did; the drawer's teal accent follows its filled state.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on each storage block: holo brackets and scanline divider on every card.
4. Check the accents read true: green when linked and online, red on NO RACK, purple patterns, amber exports, teal on a filled drawer.
5. Confirm search, sorting, filters, craft queueing, disk transfers and the offline terminal branches all behave as before.

### [12.14.0-dev] Engine Room And Helm - Maritime Chromed

**Type:** MINOR - presentation-only restyle of all fourteen maritime block panels, plus a scroller fix port. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.14.0-dev] Engine room and helm - maritime chromed`

#### The engine room goes industrial

Thirteen panels take the steel: engine, generator, gearbox, bilge pump, marine water pump, both propellers, turbocharger, waterwheel, drive shaft, shaft housing, exhaust pipe and hull. Hazard dividers, girder frames, and lamps wired to each block's own status ladder - critical heat, overstress, choke and missing exhaust read red; running, spinning, pumping and venting read green. The engine's tier-tinted divider retires with the swap; tier still shows in the block name and the coolant section it gates.

#### The helm goes starship

The helm is a nav console, not machinery, so it docks with the starship family: hull divider and status-reactive rails, matching the pilot and locator. Manned reads green, unmanned dim.

#### Fix: maritime scroller keeps frames on the panel

`MaritimeBlockUI.MakeScrollable` ports the 12.9 `ThemeFrame` guard, so the engine, generator and helm frames stay anchored while their content scrolls. Same four-line rule, same behaviour everywhere else.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on each maritime block: girders or bulkheads, themed divider, and lamps on the thirteen machines.
4. Check the lamps read true: red on critical heat, overstress shutdown, choke, missing exhaust or oxygen, disconnected turbo, dry seal and soaked hull; green when running, spinning, pumping or venting; amber when idle.
5. Scroll the tall engine, generator and helm cards fully: frames stay pinned, content slides beneath.

### [12.13.0-dev] Sparks And Star Charts - World Machines And Nav Consoles Chromed

**Type:** MINOR - presentation-only restyle of eighteen side-docked machine panels onto the industrial and starship instruments. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.13.0-dev] Sparks and star charts - world machines and nav consoles chromed`

#### The world machines go industrial

Thirteen panels take the steel: the shared processor shell (oil refinery, chemical plant, distillation plant, catalytic cracker through one builder), wind turbine, voltage station, drone port, fluid pump, armor station, and the seven right-side cards in `GameUIController` (world battery, turret defences, container, solid-fuel furnace, coal generator, electric furnace, powerstation). Hazard dividers, girder frames, and lamps that read the block's own state - burning, generating, smelting, in flight, charging. Panels whose status already shows in bespoke chrome (voltage load bar, turret stock strip, passive chests) take divider and frame only.

#### The nav consoles go starship

The planetary observatory, auto-run pilot, connector pad, route recorder and refuel pad dock as starship telemetry with hull dividers and status-reactive rails, matching the grid-side season monitor. Every early-return branch (missing block, local-only routes, unavailable machines) is framed on its own path so no fallback card ships bare.

#### Deliberately deferred

Maritime (fourteen panels) and storage (eleven panels) are each a full round on their own and follow next. The LCD centre screens (production stats, recipe browser, ship control centre), the drop/void confirm modals, the crafting-bench card and the item-ports overlay stay on their own chrome - they are player screens, not machine cards. The two unbound UIDocument panels (`SharedMachinePanel`, `RecipeSelectionPanel`) are unused legacy and untouched.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on each machine: girders or bulkheads, themed divider, and lamps where fitted.
4. Check the lamps read true: red when unpowered, stalled or switched off; green when burning, generating, smelting, flying or charging; amber when idle.
5. Confirm recipe books, defence toggles and sliders, ports overlays, the unavailable-machine fallbacks and the route recorder's local-only branch all behave as before.

### [12.12.0-dev] Chrome On Every Console - The Grid Set Is Complete

**Type:** MINOR - presentation-only restyle of the last ten grid block panels onto the four existing instrument families. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.12.0-dev] Chrome on every console - the grid set is complete`

#### Four families, ten consoles, zero new code

No new theme this round: every remaining `GridBlockUI` panel takes chrome from the family that matches its block. The battery joins the industrial line as power-plant equipment with a hazard divider and lamps; the vault and harvester keep their live singularity visuals under high-tech holo brackets and scanline dividers; the locator, both satellite consoles and the season monitor dock as starship telemetry with hull dividers and status-reactive rails; the wagon coupler gets brass rivets and finishes the rail set. All static styling, safe under the machine cadence rebuilds.

#### Deliberately preserved

The LED strip and spotlight keep their colour-reactive dividers - the divider is the only place the configured colour shows, so it stays, with industrial lamps and girders around it. The vault and harvester black-hole visuals, pressure gauge, efficiency bar, warning banners and live animation loops are untouched; only the outer chrome changed. Every value, slot, mode button, slider, colour key and hint is preserved.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on each console: frames, dividers and (where fitted) lamps on every card.
4. Check the lamps read true: battery green while charging or discharging, amber idle; lights red when off or unpowered, green when lit.
5. Confirm the offline satellite branch still shows its requirements card framed, and LED/light colour keys still recolour the divider live.

### [12.11.0-dev] Girders Across The Grid - Ship Modules Get Structured Steel

**Type:** MINOR - presentation-only restyle of sixteen grid block panels onto the existing industrial instruments. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.11.0-dev] Girders across the grid - ship modules get structured steel`

#### The same steel, a second hangar

No new theme this round: the sixteen grid modules move onto the `IndustrialTheme` instruments from 12.10 - girder frame with bolt dots, hazard-block divider, tri-lamp status stack. Same static styling, safe under the machine cadence rebuilds, and the `ThemeFrame` tags keep the girders on the panel when the card scrolls.

#### The grid modules

Liquid tank, gas tank, H2/O2 generator, cargo container, ship refinery and chemical plant (through the shared processor builder), electric furnace, drill, landing gear, wheel, sliding door, grid biofarm, exhaust scrubber, gas vent, flare stack, air vent and the generic fallback card all move onto the new chrome. Every value, slot, recipe list, slider, toggle and hint is preserved - only the instruments changed. The bespoke consoles (battery, vault, harvester, locator, satellites, season monitor, LED and light, rail coupler) stay on their own instruments.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on each module: girders, hazard divider and lamps on every card.
4. Check the lamps read true: red when off, unpowered or starved; green when running, working, open, locked, grounded or holding stock; amber when idle.
5. Confirm drain, gas type select and fill dock, recipe select, gear and wheel toggles, door sliders, and the ship terminal button all behave as before.

### [12.10.0-dev] Girders And Warning Lamps - Industrial Machines Get Structured Steel

**Type:** MINOR - presentation-only restyle of nine industrial machine panels covering ten machines (crusher and assembler share a builder). No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.10.0-dev] Girders and warning lamps - industrial machines get structured steel`

#### The instrument library grows a fourth family

`IndustrialTheme` is the workhorse counterpart to the brass, holo and naval instruments: a steel girder frame of beams and posts with bolt dots along the beams, a hazard divider with a caution-block cluster, and a tri-lamp status stack - red fault, amber idle, green running - like a control cabinet. Everything is static styling with no scheduled anims, safe under the machine cadence rebuilds.

#### The workhorses

Crusher, assembler (all tiers through the shared builder), funnel, splitter, quarry, jack pump, biofarm, flare stack, gas tank and steam turbine all move onto the new chrome. Every value, slot, recipe list, filter, upgrade and hint is preserved - only the instruments changed. The turbine joins the industrial family as power-plant equipment.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on each workhorse: girders, hazard divider and lamps on every card.
4. Check the lamps read true: red when unpowered, blocked, dry or shut; green when running; amber when idle.
5. Confirm splitter Mk3 filters, quarry upgrades and ports, flare fuel select and recovery, and gas type select and fill dock all behave as before.

### [12.9.0-dev] Rivets On The Frame - Rail Polish And A Scroller Fix

**Type:** MINOR - presentation-only: one frame-anchoring fix plus the rail-family polish. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.9.0-dev] Rivets on the frame - rail polish and a scroller fix`

#### Fix: theme frames stay on the panel

`MakeScrollable` moved every panel child into the scroller - including the starship frame rails, which then scrolled with the content and painted over section titles and slot cards (the ship reactor in the screenshot; the docking port was never wrapped, which is why it looked clean). Frame elements are now tagged and left on the panel, so the chrome stays put while the content scrolls inside it.

#### The steampunk frame, and the last two brass panels

`SteampunkTheme.Frame` completes the brass chrome: a riveted inner border with a rivet on each corner, applied to all five rail consoles. The two panels that were still half-dressed join them fully: the water tower gets a riveted divider, a TANK dial in place of the tank bar, and the frame; the bogie gets a riveted divider, seven brass selector keys (drive, reverse, snap, auto-snap, couple, uncouple) and the frame. Every value, slot and callback is unchanged.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on the ship reactor, gatling, ship engine, ore detector and cryobed: rails stay fixed at the panel edges while the content scrolls, nothing paints over titles or slots.
4. Press E on the tower (dial, rivets, frame), the bogie (brass keys, frame) and the station, switch, engine, schedule and display consoles (riveted frames).
5. Confirm every button and slot still works.

### [12.8.0-dev] Bulkheads And Vector Needles - Ship Systems Go Sci-Fi

**Type:** MINOR - presentation-only restyle of eight ship-system panels plus a dial-centering fix. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.8.0-dev] Bulkheads and vector needles - ship systems go sci-fi`

#### The instrument library grows a third family

`StarshipTheme` is the deep-space naval counterpart to the steampunk and high-tech instruments: a bulkhead frame of side-rails with docking ticks top and bottom, a hull divider with a centred diamond, and vector meters that read fuel, buffers and efficiency as a bright needle position on a track with a ghost trail. The frame accent follows block status like the holo frame does. Everything is static styling with no scheduled anims, safe under the machine cadence rebuilds.

#### The ship systems

Gatling weapon, ship reactor, solar panel, ship hydrogen engine, docking port, ore detector, beacon and cryobed all move onto the new chrome; the reactor fuel, solar efficiency and H2 buffer gauges become vector meters. Every value, slot, button, list and hint is preserved - only the instruments changed. The battery, containment vault, harvester, locator and satellites are already bespoke animated consoles and stay untouched, as do the industrial ship modules and the rail family.

#### Fix: steampunk dials sit centred

The 3px brass bezel is a real USS border, so dial innards anchored to the padding box sat 3px off the bezel centre with ticks straying toward the cream edge. The face, tick ring, needle and hub now compensate - needles and ticks sit fully inside their circles.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on the gatling, ship reactor, solar, ship engine, docking port, ore detector, beacon and cryobed: rails, diamond divider and vector needles on each.
4. Footplate: all three dials centred, needles and ticks fully inside the cream.
5. Confirm every slot and button still works and every value still updates live.

### [12.7.0-dev] Targeting Computers - Advanced Machines Go High-Tech

**Type:** MINOR - presentation-only restyle of six advanced machine panels. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.7.0-dev] Targeting computers - advanced machines go high-tech`

#### The instrument library grows a second family

`HighTechTheme` is the advanced-machine counterpart to the steampunk instruments: a corner-bracket holo frame that pins targeting-computer brackets to the panel corners, a scanline divider with one bright segment, segmented cell meters that read fuel, rods and buffers as discrete glowing cells instead of a continuous bar, and a hero numeric readout for the one number the operator watches. The frame accent follows machine status - green running, red fault, dim idle - so an overheating reactor wears a red frame. Everything is static styling with no scheduled anims, safe under the machine cadence rebuilds.

#### The nuclear and hydrogen lines

Reactor core, portable reactor, enrichment centrifuge, waste reprocessor, electrolyser and hydrogen engine all move onto the new chrome: hero readouts for core temperature and power output, segmented cells for control rods, fuel, enrichment, reprocessing, electrolysis and the H2/O2 buffers. Every value, slot, tank and hint is preserved - only the instruments changed. The steam turbine and water tower stay as they are for the rail-polish round, and the quarry stays industrial.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on the reactor core, portable reactor, centrifuge, reprocessor, electrolyser and hydrogen engine: each wears corner brackets, a scanline and cell meters.
4. Run the reactor hot: past safe max the temp readout, its cells and the frame go red.
5. Confirm every slot still takes items and every value still updates live.

### [12.6.0-dev] Brass Needles And A Firebox - The Railway Moves To The Side Dock

**Type:** MINOR - new firebox slot (additive save through the container snapshot) and five consoles migrated to docked machine cards. No breaking changes.

**GitHub title:** `[12.6.0-dev] Brass needles and a firebox - the railway moves to the side dock`

#### More than a colour: the steampunk instrument library

`SteampunkTheme` is the railway's own instrument maker. Its analog dials are real gauges, not bars: a machined brass bezel on an iron body, a cream face, an eleven-mark tick ring over the 270-degree sweep with longer ends and middle, a red needle on a brass hub - and a redline past which the ticks themselves turn red, because a boiler pressure gauge redlines. Riveted dividers and brass selector keys complete the chrome. Everything is static styling with no scheduled anims, so the 4 Hz machine cadence rebuilds live panels around it safely.

#### The railway leaves the center modal

Station, switch, footplate, schedule and display all move into `RailPanels` on the right dock, opened through `OpenMachine` like every other machine - every feature carried over: station naming, roles and hold; switch routing; the whistle and the fire toggle; the full stop-list editor with its station picker and wait conditions; display kind, source and custom text. The bogie console merges into the docked truck card (retitled Bogie, brass accented) and brings its auto-snap toggle, service note and live speed with it. `RailConfigHud`, `TrainScheduleHud` and `DisplayConfigHud` are deleted. A text-field focus guard (`SteampunkTheme.IsTextInputFocused`) joins the refresh and hotkey guards, so a live rebuild never eats a station name or a dwell time mid-typing.

#### The firebox

The steam engine gains its coal slot: a 1-slot firebox the stoker burns from first, falling back to shoveling out of any cargo container aboard when it runs dry - LOAD-station coaling keeps working exactly as before. The slot persists through the standard container snapshot, so fuel survives a reload. The footplate shows it beside three needles: redlined boiler pressure, boiler water in litres, and flywheel RPM.

#### Manual steps in Unity

1. Apply the patch and recompile. Three HUD scripts are deleted - confirm the compile is clean.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on a station, a switch, a bogie, an engine, a schedule block and a display: each docks right with steampunk chrome.
4. Footplate: watch the three needles move; put coal in the firebox slot and confirm it burns before tender coal; reload and confirm the slot kept its fuel.
5. Station: rename it mid-game (typing must not be interrupted), set roles, open the hold, press E to come back.
6. Schedule: add, reorder and delete stops; display: switch kind, source and custom text.

### [12.5.0-dev] The Tower On The Hill - A Grand Steel Water Tower

**Type:** MINOR - the water tower is rebuilt as a landmark and gains its own console. No save changes; stored levels restore against the bigger tank.

**GitHub title:** `[12.5.0-dev] The tower on the hill - a grand steel water tower`

#### A tower worth looking at

The water tower is no longer a barrel on sticks: it is a classic six-legged steel water tower, 10 m to the brass finial. A galvanized tank with iron seam bands and rivets, a rust-red conical roof, a railed balcony walkway with a boarding gap, ladders from the ground to the balcony and up the tank, a central riser pipe with a valve house, and a stayed spout arm over the platform side. The legs lean inward with girt rings and X-bracing, and only structural parts keep colliders - rivets, rungs and rails are decor, so the tower costs no more physics than before. Existing towers rebuild into the grand tower when step 96 is re-run.

#### A tank worth filling

Capacity grows from 4 000 L to 12 000 L, with fill rates to match: 24 L/s from a water network, 6 L/s seeping from open water (now a real `seepRate` field instead of a hardcoded number). Towers still on untouched 12.4.0 tuning migrate to the new values automatically; anything already tuned is left alone. Stored water restores as before and clamps against the bigger tank, so no save work was needed.

#### The level gauge

E on the tower opens its panel on the right dock, beside the inventory, like every other machine - no center modal. A live water gauge with litres aboard, capacity, level percent and supply source, kept honest by the 4 Hz machine refresh cadence, plus a status pill that reads FULL, FILLING FROM NETWORK, SEEPING FROM OPEN WATER or ISOLATED straight from the tower's new supply tracking. The panel wears steampunk brass, because the UI matches the block: rail and steam hardware gets brass, and the fleet-wide UI round will dress every other family to match its own hardware (`LcdHudTheme` gains the shared brass palette it draws from).

#### Also in this round

`GameVersion` was stuck at 9.55.0 while the project shipped 12.4.0 - the console banner, menu footer and saves now report 12.5.0-dev from the same constant again.

#### Manual steps in Unity

1. Recompile; run **Tools -> Voxel Engine -> Voxel Engine Setup -> 96. Build the Steam Railway**.
2. In a save: place or find a Water Tower and check the new silhouette against the old barrel.
3. Press E on the tower: the level gauge shows fill, capacity and supply state live.
4. Stand it beside a water network or a pond and watch the status pill change as it drinks.
5. Berth a thirsty steam engine within reach of the spout and confirm the boiler still fills.

### [12.4.0-dev] Steam Takes The Rails - Piston Engines, Water Towers And A Whistle

**Type:** MINOR - new traction source and two blocks. Additive save fields only.

**GitHub title:** `[12.4.0-dev] Steam takes the rails - piston engines, water towers and a whistle`

#### Rotational power, and nothing else

The **Steam Engine** is a machine, not a locomotive: a vertical boiler with a chimney, a horizontal steam cylinder, a crosshead on slide bars, a connecting rod down to a crank pin and a flywheel across the frames. Its only product is ROTATION - `CurrentRPM` at the flywheel, zero banked or starved, idle at first steam, full speed at full pressure. It generates no electricity and grants no traction flag: the bogie's mechanical drive takes the flywheel's turn when the grid has no electric power (the gate walks the coupled consist at 2 Hz and asks for a shaft turning above 30 RPM), and the brass screens' rotational tap reads the same number - a steam train's departure board runs off its own engine's shaft.

#### The piston gear is solved, not waved at

The flywheel spins, the crank pin circles with it, and the crosshead rides its bars at the exact slider-crank position (z = pin.z + sqrt(L^2 - dy^2)) with the connecting rod laid between pin and crosshead at its true length and angle. At speed it reads as an engine working; at rest it holds its last position like one. The chimney breathes WHITE steam smoke - white, not soot: a coal fire with draft and water in the boiler breathes steam, and steam is what this block sells - plus `Sfx.SteamChuff` twice per revolution under way and `Sfx.SteamWhistle` on the whistle cord.

#### The tender is the train

The firebox carries no private fuel slot: it shovels coal - or wood, at half the patience - out of any cargo container on the grid, like a real tender coaling from the wagon behind it. Coaling is therefore a cargo operation: a LOAD station with a coal filter coals an engine through the berth service pass from 12.3.0, and fuel persists for free with the containers. Water comes from a `GridLiquidTank` aboard (a tank wagon) while running, and from a **Water Tower** platform-side while berthed. Fire without water loses pressure; water without fire loses it slower; neither turns the flywheel.

#### The footplate and the tower

E on an engine opens the footplate in `RailConfigHud`: boiler pressure and water as bar readouts, live flywheel RPM, LIGHT/BANK THE FIRE, and WHISTLE. Water and fire survive a reload (additive fields); pressure deliberately does not - a banked fire restarting hot between sessions would be a free head of steam. The **Water Tower** is a tank on legs with a standpipe: it fills itself from a water network it stands beside (same FluidNode source a sprinkler drinks from) or slowly from open water - a tower by a pond seeps full the way real ones were pumped. Its level saves additively.

#### New in the setup window

Step **96. Build the Steam Railway** (needs 95 for brass): authors the Steam Engine grid item and prefab (bed, frames, steam cylinder, crosshead and rod, connecting rod, spoked flywheel with crank pin, brass-banded vertical boiler, chimney), the Water Tower block and prefab, and both recipes - engine steel x24 + brass x6 + wire x4 at the assembler, tower steel x10 + brass x2 at the bench. Non-destructive, safe to re-run.

#### Manual steps in Unity

1. Recompile; run **Tools -> Voxel Engine -> Voxel Engine Setup -> 96. Build the Steam Railway**.
2. In a save: craft a Steam Engine, build it onto a train grid, put coal in any container aboard, and open the footplate (E) to watch pressure climb and the flywheel turn the piston gear.
3. Cut the grid's power (or build the train with no generator at all) and drive: the flywheel's rotation pulls the train mechanically.
4. Craft a Water Tower beside a water network or a pond, and berth a thirsty engine within reach of the standpipe.
5. Pull the whistle cord. You will know when it works.

### [12.3.0-dev] The Freight Actually Rides - Stations Service Berthed Trains

**Type:** MINOR - closes the cargo loop the schedule system was built around. No new assets, no save changes.

**GitHub title:** `[12.3.0-dev] The freight actually rides - stations service berthed trains`

#### The hole: `ServiceTrain` had no caller

`RailStation.ServiceTrain` - the transfer that moves items between a station hold and a docked train, honouring the station filter, putting back anything the destination refuses - existed since the station rework and was called from nowhere. Trains arrived, waited their condition, and left without a single item moving. The hold waits in a schedule were watching a hold that nothing but pipes ever touched, and a "LOAD until hold empty" stop released the moment a factory drained the hold, whether or not the train had taken anything.

#### The fix: a berth service pass on the bogie

`GridRailBogie` now runs a 4 Hz service pass while the train is effectively stopped (under 0.05 m/s) on rails: it finds the station whose platform it stands at (`RailStation.Nearest`, new static, same 4 m service radius), and if that station works (role not Passing) it calls `ServiceTrain` for every cargo store on the grid - every block implementing `IGridItemStore`, rescanned every 5 s so adding a wagon mid-route joins the transfer. Hand-driven trains berth and trade exactly like scheduled ones; a train braked by a signal in front of a station does not, because it is not stopped at the platform radius... it is stopped wherever the section ends, and if that is inside the radius, trading is what a real train would call a courtesy stop.

The pass reports itself: `ServicingStation` and a one-line `ServiceNote` ("Loading at Ore Head - 34 items aboard", "Berthed at Mill - train empty or station hold full"), shown in the bogie console and appended to the schedule block's waiting label, so a stalled transfer says why instead of silently watching a clock.

#### Train-side wait conditions

Two waits appended to `ScheduleWait` (saves store the int, so nothing reorders): **TRAIN EMPTY** - leave when every container aboard is empty, the natural release for an unload stop - and **TRAIN FULL** - leave when every slot aboard carries something, the natural release for a load stop. A train with no containers reads as empty and never as full. The schedule console offers both buttons and both descriptions. With these, a service pattern can say what it actually means: load until the train is full, unload until it is empty, and the hold waits stay for players who think in station terms.

#### Manual steps in Unity

None - code only, no setup step, no new assets. Recompile and in a save: give a station the LOAD role with items in its hold, put a cargo container on a train, add a stop with TRAIN FULL, and watch the note count items aboard until the train leaves full.

### [12.2.0-dev] Brass, Tubes, Cobbles And A Bogie That Actually Snaps

**Type:** MINOR - new material and console, rebuilt visuals, one placement rule tightened. All save fields additive.

**GitHub title:** `[12.2.0-dev] Brass ingots, real nixie tubes, proper ballast stone, and a bogie that snaps`

#### The bogie snaps now - and tells you about it

The old snap put the GRID ORIGIN on the railhead and kept the construct's old rotation, so a truck parked at an angle "snapped" while its wheels stayed in the air and it faced across the line. `TrySnapToTrack` now sets the pose, not a coordinate: the construct rotates to face along the track, then shifts so the truck block itself sits on the railhead, wherever on the hull it was built.

The truck is renamed **Bogie** (item, recipe, prefab block name - step 92 re-runs the rename over old assets), and E on it opens a proper console in `RailConfigHud`: ON/OFF RAILS state, speed, **AUTO-SNAP** toggle (on by default; a parked wagon stays parked when you turn it off), SNAP NOW and LIFT OFF buttons. Auto-snap polls once a second while unrailled, so a train built beside the line, reloaded, or overtaken by the line growing into the yard joins the rails by itself. The policy survives saves (additive field). The old inline attach/detach toast is gone - the state and its control live on one screen now.

#### Brass: the steampunk metal, priced to fit

New **Brass Ingot**: copper x2 + iron x1 in the furnace, one out - between copper and steel, exactly where steel (iron x2) leaves room for it, so nothing upstream changes price. Step 95 authors it and then blends ONE steel-for-brass swap into every rail piece recipe and every display-family recipe (total ingots unchanged; the blend skips recipes that already carry brass, so re-runs never stack it).

#### The nixie readout is tubes now

Not digits painted on a window: four glass envelopes with domed tops standing in brass sockets on a brass base, pins between them, one glowing digit floating inside each tube - the clock on the reference photo. A tube with nothing to show stays dark. Glass is alpha-blended URP lit so the digit reads through its envelope, and updates still flicker the emission.

#### The analog gauge is a gauge now

Square brass backplate with corner bolts, round brass body, cream face, an eleven-mark tick ring over the 240-degree sweep (ends and middle longer), red needle with a counterweight tail on a black hub, and a glass cover. The tick ring and the needle share one angle convention, so the needle actually points at its marks.

#### Ballast is crushed stone, not scattered boxes

The first cobble scatter - 26 loose cubes on a dark slab - read as boxes sprinkled on dirt in the world. The bed now builds a dense jittered GRID of angular stones (full three-axis rotation, three granite greys, shoulders sloping at the rim, tops proud of the slab so sleepers bed INTO stone) as one combined mesh per tone: three draw calls for the whole layer instead of 27 per cell, so a long line costs less than before while looking packed. Step 93 rebuilds the stone layer once on prefabs authored before this (V2 child marker); re-runs leave a good bed alone.

#### Rails are laid, not placed

Hand-placing a rail block produced a dead cell: no links, no junction logic, no ballast, and it blocked the corridor tool while looking like track. `BuildSystem.TryPlace` now refuses any block whose prefab carries a RailTrack and says why: hold the Rail Layer and drag a run.

#### Manual steps in Unity

1. Let the project recompile.
2. Run setup **92** (rename to Bogie), **93** (new stone layer) and **95** (brass ingot + brass blend) from Tools -> Voxel Engine -> Voxel Engine Setup. All three are non-destructive and safe to re-run.
3. In a save: smelt brass (copper x2 + iron x1), check a rail recipe in the crafting list shows its brass ingot, and lay a run - the bed under it is packed crushed stone now.
4. E on a bogie: the console shows rail state, auto-snap, snap now, lift off. Park a train beside track with auto-snap on and walk away - it should be railed when you look back.
5. Try placing a Rail Track block by hand: it should refuse and point you at the Rail Layer.

### [12.1.0-dev] Schedules, Screens And The Sound Of A Station Waking Up

**Type:** MINOR - two new block families plus a track bug fix. All save fields additive; old saves load unchanged.

**GitHub title:** `[12.1.0-dev] Train schedules, steampunk displays and the ballast that went missing`

#### The ballast you could not see (bug fix)

Track laid since the x3 formation could come out with sleepers and rails but no stone bed - the bed item resolved by ID, and when the lookup came back empty the corridor silently skipped the ballast pass and kept laying. Two fixes, belt and braces:

- `PlayerInteractionTool` now self-heals a missing ballast definition at drag time (`railballast` item, `Item_Stone` material) and shows one amber toast if the assets genuinely are not there, instead of charging stone and placing nothing.
- `RailCorridor.Commit` gained a re-bed pass: any committed cell that has track but no bed under it gets one. Re-bedded cells are counted, charged as stone, and named on the commit toast - so laying one new run also heals the old bare stretches it touches.

#### Train Schedule block - services without a driver

A grid block for the train itself. Build it on the same grid as the Rail Truck, press E, and write the service: an ordered list of stations, each stop with the condition that releases the train again - dwell seconds, hold until full, hold until empty, or hold while there is space left. The bogie gained real destination routing for it: `SetDestination` asks `RailNetwork` for a path, the truck walks it cell by cell (re-syncing from the path front if it is nudged off line), and fires `Arrived` - at which point the schedule advances the stop, starts the condition, and dispatches again. Stations are matched by name, so renaming a station in the console re-points every train that calls at it. The service pattern and the current stop survive a save.

#### The display family - split-flap, nixie and analog, all steampunk

One component, five authored housings, because "modular" should mean the hardware is shared and the configuration is yours. Every screen opens a console on E: kind (split-flap rows / glowing nixie digits in a brass cage / analog needle over a dial) and source (train speed, consist load, station departures, station status, or custom text).

- **Brass Display Screen** (grid): the train's own board. Takes ROTATIONAL power - while any shaft, gearbox or engine on the grid turns above idle the drums keep clicking; park the engine and the board goes dark mid-word.
- **Display Cabinet**, **Hanging Departure Board** and **Nixie Readout** (stationary): mains-fed through a small electric engine inside that turns the flap drums. The hanging board lists every scheduled service calling at the station beside it, from the schedule blocks' live state - the departure board and the timetable are the same data.
- The reference board's look is honoured: dark cards, warm text, brass bezels on everything.

And the sound that makes it a split-flap board: `Sfx.SplitFlapFlip` - click, slap, rattle - staggered per row as the cards turn edge-on, swap invisible, and land. A board updating should sound like a board updating, not like one clap. Nixie updates flicker their emission instead. Screen kind, source and custom text are saved for both grid and stationary housings (additive fields).

#### New in the setup window

Step **95. Build Steampunk Displays & Schedules** (Tools -> Voxel Engine -> Voxel Engine Setup): authors the Train Schedule and Brass Display Screen grid items and prefabs, the three stationary housings with their BlockItems, and five recipes (steel + copper wire, bench/assembler). Non-destructive as always - creates what is missing, repairs unresolvable links, never touches authored tuning. Safe to re-run.

#### Manual steps in Unity

1. Let the project recompile (Console should stay clean).
2. Run **Tools -> Voxel Engine -> Voxel Engine Setup -> 95. Build Steampunk Displays & Schedules**.
3. In a save: craft a Train Schedule (steel x4 + wire x2), build it onto a train grid next to the Rail Truck, press E and add at least two stops.
4. Craft any screen - e.g. the Brass Display Screen (steel x6 + wire x4) on a train, or a Display Cabinet (steel x8 + wire x4) at a station wired into power - press E to pick kind and source.
5. Re-lay or extend a track run over older bare stretches and watch the commit toast: the re-bed pass should charge and place ballast under them.

### [12.0.1-dev] Compile Cleanup - Obsolete Finds And A Missing-Script Call That Never Existed

**Type:** PATCH - compile diagnostics only. No save, no API, no behaviour touched.

**GitHub title:** `[12.0.1-dev] Compile cleanup: obsolete find overloads and the missing-script scrub`

#### The error: CS0117 on `GameObjectUtility.RemoveMonoBehavioursWithMissingScripts`

The retirement scrub in setup 85 called a plural API that does not exist in Unity 6.5. The project already solves this exact problem in step 17 of the setup window with the singular, per-GameObject call - `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` - walked over `GetComponentsInChildren<Transform>(true)`. Step 85 now follows the same pattern instead of inventing a second one, so inactive children are scrubbed too and the two passes cannot drift apart.

#### The warnings: CS0618 on `FindObjectsByType` with `FindObjectsSortMode`

Both call sites in `LogisticsMapData` now use the `FindObjectsInactive.Exclude` overload the rest of the project already standardised on (Cryobed, SpaceOrigin and others). One was the new v2 bogie marker from 12.0.0-dev; the other was the pre-existing chest-zone gather, fixed in the same pass because a warning left in the log is a warning that hides the next real one. Sort order was never relied on at either site - markers are regrouped by kind afterwards - so nothing observes the change.

#### Manual step in Unity

None. Code-only patch; recompile and the Console should be clean. Setup 85 still needs its re-run from the 12.0.0-dev round if you have not done it yet.

### [12.0.0-dev] The v1 Train Is Gone - One Railway, Not Two

**Type:** MAJOR - removes the 11.15.0 `RailTrain` entity and everything that pointed at it. BREAKING: saves that contain a v1 locomotive or its schedule cannot carry it across; start a fresh save.

**GitHub title:** `[12.0.0-dev] The v1 train is gone - one railway, not two`

#### What was removed and why now

The rework brief in the roadmap ended with two items: automatic junctions at crossings, and retiring the 11.15.0 `RailTrain`. The junctions landed in 11.41.0-dev; the retirement is this round, and it is the reason this version is a MAJOR. A v1 train was a scheduled agent walking a private graph - the one large buildable in the game that was not a player-built grid, could not be designed block by block, took no damage, held no paint, and needed its own console. Train System v2 made a train an ordinary grid with a Rail Truck on it, and every feature since has been written once, against grids. Keeping the v1 entity meant keeping a second railway forever.

The retirement waited for a MAJOR on purpose: removing the entity breaks any save that still holds a locomotive, and that belongs in a version bump a player can see, not in a minor they cannot.

#### What changed in code

- `RailTrain` and its `TrainState` enum are deleted. Nothing else in the project compiled against them after this round.
- `RailConfigHud` loses the train console. A train configures itself through the grid terminal like every other buildable; the console keeps its station and switch panels.
- The logistics map reads `GridRailBogie` instead of the retired entity list, so v2 trains appear where v1 trains used to - name, RUNNING / REVERSING / IDLE / OFF RAIL, and an alert tint when a bogie is off the rail.
- The E-interaction branch that opened the v1 console is gone; E on a rail truck and on a coupler remain the train-side verbs.
- Setup step 85 now scrubs the dead script off any locomotive prefab it finds (`RemoveMonoBehavioursWithMissingScripts`). The prefab shell stays on disk - a MAJOR demands a fresh save, not a deleted history - but no missing-script component survives to warn on every load. The recipe left the registry back in 11.39.0.

#### What a player loses, precisely

A saved v1 locomotive and its schedule. Nothing else: track, switches, signals, stations, truck bogies, couplers and consists are all v2 systems and are untouched. A locomotive placed in an old save loads as an inert shell block in the fresh-save migration case, and as nothing at all once the save is retired, which is the honest outcome of removing an entity rather than stubbing one.

#### Manual step in Unity

Re-run setup **85** from Tools -> Voxel Engine -> Voxel Engine Setup to scrub dead scripts off old locomotive prefabs. Non-destructive: it only removes components whose script no longer exists. Start a fresh save afterwards - this is the MAJOR that the old ones were waiting out.

### [11.41.0-dev] Junctions That Join, Curves That Bend, And A Bill While You Drag

**Type:** MINOR - rail graph, track visuals and a new drag HUD. Save-compatible (one additive save field).

**GitHub title:** `[11.41.0-dev] Junctions that join, curves that bend, and a bill while you drag`

#### Turns are smooth now, because a bend is finally drawn as a bend

Two separate faults made the same kink. The rotation pass aimed each cell's WHOLE rotation at the next cell, which replaced the corridor solver's mitred yaw with the chord direction - a half-step zigzag per cell on every curve. It now keeps the solver's yaw and adds only the pitch the ground asks for.

The second fault is geometric: a track cell is a rigid box set, and on a curve the outside of a bend is longer than the inside, so rigid cells gapped outboard and overlapped inboard - the dashed, fanned corner in the screenshot. `RailTrack` now derives the signed curvature of its own through route from the link graph (circumcircle of the two most opposite neighbours) and deforms its children onto that arc: sleepers fan radially, each square to the tangent at its own station, and each rail stretches to the arc length at its own offset, so rail ends meet end-to-end through the bend. It is derived, not stored, so a save reloads and re-derives it, and a junction re-link heals it.

Rail runs also ask for a gentler fillet than roads do: 2.75 m minimum radius for single track instead of the road solver's 1.25 m, where one cell turned through 46 degrees and no dressing could read as a curve.

#### Crossings become junctions - and do not reroute your trains

Laying a line across another used to produce visible overlapping track the graph could not use: the existing cell kept its two-link straight budget, so the new arms had nothing to link into. Three changes fix it at the root.

- A station is SPLICED into the run at the exact position of any existing cell the route crosses, so that cell becomes the shared node: both lines meet it at proper cell spacing instead of stacking inside it or stopping short of it.
- Promotion to a junction now counts ARM DIRECTIONS, not neighbours - three directions 22.5 degrees apart - and runs on every link rebuild, not only on corridor commits. A hand-placed cell completing a T, and a save reloading its graph, heal into junctions through the same path. Parallel double track never promotes: its cells have no external neighbour to trigger the candidate rule.
- A fresh junction routes STRAIGHT THROUGH by default (`RailTrack.NextFrom` picks the straightest continuation of the entry leg). This is what retired the old fear that auto-junctioning would silently reroute trains: nothing turns unless a player sets the points, and whether they did is saved (additive field `railPointsSetByPlayer`), so a reload does not quietly reset a yard.

Adjacency also tightened from 1.45 m to 1.10 m. Real neighbours sit at most 1.06 m apart (1 m arc plus the gradient cap); 1.45 m let the 1.41 m lattice diagonal and parallel lines one metre abeam reach into a cell's link budget. A 5% ordering penalty keeps fore/aft ahead of a pure lateral tie without ever excluding a junction arm.

#### The bill arrives while you drag, not after the click

A new bottom-centre card, `RailCostHud`, reads out the run as it grows: metres, cell count, gauge, and per-material need against what the inventory actually holds, green while affordable and red the moment it is not. It is driven from the SAME plan the ghost draws and the commit lays, so the number on the card, the preview on the ground and the bill after the click cannot disagree. A refused run names its reason on the card in amber while you are still aiming.

#### The formation is three times the width

Sleeper 1.5 m to 4.5 m, gauge 1.05 m to 3.15 m, rail heads 0.11 m to 0.33 m, ballast bed 2.1 m to 6.3 m - the whole permanent way scaled together so the real 0.70 sleeper-to-gauge ratio survives. The Rail Truck bogie is re-gauged from the same number in step 92, so wheels sit on rail heads rather than running down the middle of the sleepers. The ghost preview measures the deck off the prefab instead of trusting a constant, so it previews exactly as wide a formation as the commit lays.

All three setup steps apply the width to prefabs authored before it through geometry-only, idempotent re-gauge passes: a prefab already at the new width is left byte-identical, and nothing but running gear moves - no component, material, recipe or tuning value is touched.

#### Manual step in Unity

Re-run setup **85** (track formation), **92** (bogie re-gauge) and **93** (ballast bed) from Tools -> Voxel Engine -> Voxel Engine Setup. All three are non-destructive re-gauge passes; existing track in a save heals its curves and junctions on load by itself.

### [11.40.0-dev] A Real Bogie, And Track That Flows

**Type:** MINOR - rail visuals, load physics and a new coupler block. Save-compatible.

**GitHub title:** `[11.40.0-dev] A real bogie, and track that flows`

#### Track now flows over hills instead of stepping up them

Your photo showed each cell as a separate level slab at a different height - a staircase, not a railway. The cause: rotation came straight from the corridor solver, which works on a **flat plane**, so every sleeper stayed perfectly level while the positions climbed underneath.

Each cell is now pitched to aim at the next one, so consecutive cells share an edge rather than overlapping at a corner. Smoothing also went from 6 passes to 18, because at 6 a real hillside still left steps big enough to see - a worst step of 1.10 m now grades to 0.29 m.

The last cell of a run aims *back* at its predecessor, so the end does not flip flat and re-create the seam there.

#### The bogie looks like a bogie

Rebuilt from your reference: two side frames carrying the axleboxes, a bolster across the middle that the wagon rests on, a centre pivot, visible coil springs over each axlebox, orange brake gear, and flanged wheels on real axles at the 1.05 m gauge the track uses.

The springs matter more than they look. They are what makes it read as something that **carries weight**, which is exactly the mechanic underneath it.

#### Weight actually matters now

| Load | 1 bogie | 2 bogies | 4 bogies |
|---|---|---|---|
| 6 t | 14.0 m/s | 14.0 | 14.0 |
| 12 t | 7.0 | 14.0 | 14.0 |
| 24 t | 3.5 | 7.0 | 14.0 |
| 48 t | 2.1 | 3.5 | 7.0 |

Mass is summed across the **whole consist** and divided by the combined rated load of every bogie in it, so your rule falls out directly: heavier is slower, more bogies is faster, and the gain is capped because the factor never exceeds 1.

Load is shared rather than per-bogie deliberately - a real consist spreads weight across every axle, and checking each bogie against only its own grid would let a player defeat the rule by putting the heavy wagon in the middle. The floor is 15% of top speed, never zero: a train that cannot move at all reads as a bug rather than as overloaded.

#### Trucks snap to rails, and cannot be stacked

Placing a truck on rail now **attaches immediately**. Previously the only way on was a button inside the block console, so a player who built a train beside a line had no indication a further step existed - it just sat there.

Stacking is refused, and the block is returned rather than sitting there inert. Stacked bogies break the load model as well as physical sense: each would claim its own rated capacity while carrying the same mass, making an overloaded train arbitrarily fast. Side-by-side trucks stay legal, because that is a real four-wheel arrangement - only *directly below* is checked.

#### Attach, detach, couple, release

- **E on a rail truck** - toggles on and off the rails
- **E on a coupler** - attaches to the car in front, or releases

The new **Wagon Coupler** block is what you asked for. It holds no physics joint - consists still follow the leader's recorded path, since a joint between kinematic bodies does nothing - it is the player-facing control for that system, mounted where the connection physically is.

#### Also

Removed the "re-run setup step 85/93" text from the failure toasts.

#### Manual step in Unity

Re-run **85** (track pitch) and **92** (new bogie, wagon coupler). Both non-destructive.

### [11.39.0-dev] Retire The Locomotive, Widen The Gauge, Make It Cobblestone

**Type:** MINOR - retires a superseded block and reworks rail visuals. Save-compatible.

**GitHub title:** `[11.39.0-dev] Retire the locomotive, widen the gauge, make it cobblestone`

The diagnostics added in 11.38.0 did their job: *"'Rail Track' has no placed prefab"* named the exact cause immediately, where three previous releases had guessed. Track is laying now.

#### The v1 locomotive is retired

Step 85 was still authoring it, so the game offered two ways to build a train - and the old one is strictly worse: it cannot carry grid blocks, take damage, be painted, pressurised, or designed by the player.

Its recipe is **removed from the registry** rather than the asset being deleted. A save may still contain one, and deleting the asset would turn that into a missing reference on load - the block would vanish from the player's world with no explanation. Dropping the recipe makes it uncraftable, so it stops being a choice for new play while anything already built keeps working until the MAJOR release that removes `RailTrain` outright.

#### The dependency is now stated

Steps 92, 93 and 94 all need step 85 - they place, lay and signal the track it authors. Step 93 already refused with a clear message, but the wizard buttons said nothing, so the ordering was only discoverable by hitting the error. All three buttons now read **"needs 85"**.

#### The gauge was far too narrow

Rails sat 0.56 m apart on a 1 m cell, which reads as a narrow ladder down the middle of a wide bed rather than a railway.

| | Was | Now |
|---|---|---|
| Gauge | 0.56 m | 1.05 m |
| Sleeper length | 0.78 m | 1.5 m |
| Sleepers per cell | 2 | 4 |
| Rail profile | 0.07 square | 0.11 x 0.12 (taller than wide) |

The gauge-to-sleeper ratio is now 0.70, which is what real track uses. Sleeper count doubled because at 1 m spacing two per cell left visible gaps at every cell boundary - a run looked like a dashed line rather than continuous track.

#### The ballast reads as cobblestone

It was one smooth cube. A flat surface has no self-shadowing, so it reads as poured concrete however it is tinted - the tint was never the problem.

The bed is now a base slab plus **26 jittered cobbles** in three tones, each with a random yaw and a slight tilt, because the shadows *between* stones are what makes ballast look like ballast. It is also wider (2.1 m) so the shoulder spreads past the sleeper ends like real ballast does.

Two details worth naming:
- **The jitter is deterministic**, seeded by a constant. A prefab authored twice must be identical, or two setup runs produce visibly different track.
- **Cobbles have no colliders.** They are decoration on the bed; 26 extra colliders per cell would be a real cost on a long line.

#### Re-tuned the stacking against the new geometry

Changing the ballast changed where its top sits, so the rail rise was recomputed rather than left alone: cobbles top out at 0.115 m, and the rail now rises 0.11 m, putting the sleeper underside at 0.110 m - **bedded into the stones** rather than floating 6.5 cm above them, which is what the old value would have produced.

#### Manual step in Unity

Re-run **85** (retires the locomotive, widens the gauge) and **93** (rebuilds the ballast). Both are non-destructive.

Existing track keeps its old prefab; the new geometry applies to track laid after the rebuild.

### [11.38.0-dev] The Ghost Was White And The Failure Was Silent

**Type:** PATCH-level fixes shipped as MINOR (new diagnostics API). Save-compatible.

**GitHub title:** `[11.38.0-dev] The ghost was white and the failure was silent`

Your screenshot solved this. The white slabs in it **are** the ghost - it was rendering the whole time, just colourless - and that told me planning was working and the failure was downstream in `Commit`.

#### Why I kept missing this

`Commit` had four early returns that all did the same thing: `return 0`. Null plan, null track block, null placed prefab, empty cell list - every one produced a bare zero, and the caller printed "the route produced no placeable cells" for all of them. The message actively misdirected the search, and I spent three releases looking at the planner because that is what it pointed at.

**Every early return now states its own reason**, surfaced in the toast and logged with the full counts - planned, solved, missed ground, blocked, skipped. A silent failure path in a tool the player invokes is a bug in its own right, independent of whatever caused it.

#### The likely root cause, and a fix that does not depend on setup order

A Rail Layer asset created by an earlier setup run has `trackBlock` null, because the field did not exist when it was authored. Step 93 repairs it - but only when re-run, and the tool in your inventory was already made.

`Commit` then hit `trackBlock == null` and returned 0 silently. The tool now **finds the Rail Track block by id at runtime** if its reference is missing, logs that it did, and carries on. An upgrade can no longer leave a dead tool in the player's hands regardless of what order steps are run in.

#### The ghost was white for the same reason the asteroids were

I set vertex colours and used URP/Unlit - which does not read vertex colours. Identical to the asteroid material bug in 11.28.1, and I made it again. The alpha was ignored too, because that fallback shader is opaque, which is why it read as a solid white slab rather than a translucent hint.

Rather than hunt for a vertex-colour shader, the ghost is now **two meshes with two real materials**: green for placeable cells, red for blocked ones. That needs no special shader, so it cannot silently stop working if the render pipeline changes.

#### What to expect now

If a run still lays nothing, the toast will name the actual cause and the Console will carry a `[RailLayer]` line with every count. That turns the next report into a fix rather than another round of guessing.

No manual Unity step, though re-running **93** will persist the recovered reference.

### [11.37.0-dev] Multi-Point Runs, Real Junctions, Honest Ghost

**Type:** MINOR - rail layer rework. Save-compatible.

**GitHub title:** `[11.37.0-dev] Multi-point runs, real junctions, honest ghost`

#### "Nothing laid" - and why I kept failing to fix it

Two releases were spent guessing at this, and the reason is that the message could not distinguish its own causes. "No placeable cells" was printed whether the solver produced nothing, the ground probe missed, or every cell was obstructed. I had no more information than you did.

So the first change is **diagnostics that name the failure with numbers**: how many cells the corridor solved, how many found no ground, how many were blocked. A message that can only say one thing cannot be debugged.

The probe itself had two real faults:

- **It took the first thing it hit.** A plain `Raycast` happily returns the player's own collider, the ghost, a train, or track already laid - and then that becomes "the ground". It now sorts all hits and skips anything with a rigidbody, any grid, any existing rail, and the ghost.
- **It used the start point's gravity for the whole run.** On a small planet the "up" at the far end of a 400 m run is measurably different, which tilts the probe. Each cell now samples its own gravity.

#### The ghost is now green and red, per cell

`EvaluateCell` checks each cell for three real obstructions and the ghost colours each one individually:

| Condition | Result |
|---|---|
| Below the waterline | red - "underwater" |
| Solid terrain above the formation | red - "buried, the route runs into terrain" |
| An existing placed block | red - "blocked by a placed block" |
| Existing rail | **allowed** - that is a crossing |

Per cell, not per run, on purpose: a route that clips one rock shows **one red cell you can nudge around** instead of turning the whole line red and leaving you to guess which end is wrong. The blocked cells are kept rather than discarded for exactly that reason - throwing them away would hide the information you need.

#### Crossings become real junctions

Plain track holds at most two links, so where a new line crossed an old one the extra arms were **silently dropped**. The rails visibly crossed and a train could not take the turn.

Any cell that ends up with three or more rail neighbours is now promoted to a switch, which widens its link budget from two to four, and both lines are re-linked afterwards - the existing line too, or it keeps the two links it had before the junction appeared. A straight run still has two neighbours, so nothing becomes a switch by accident.

#### Multi-point runs

Laying a curve meant committing a leg, then starting again from its end. Now:

- **Left-click** starts a run
- **Right-click** adds a corner - as many as you like
- **E** lays the whole route
- **Escape** cancels

The corridor solver already fillets every interior corner, so a chained route curves properly rather than forming hard angles. Left-click does nothing once a run is started, so a mis-click cannot commit a route you were still shaping.

The ghost previews the confirmed corners **plus the leg you are currently aiming**, because the fillet at the previous corner depends on where the next leg goes - showing the legs in isolation would preview a shape the commit would not produce.

No manual Unity step.

### [11.36.0-dev] Graded Formation, And A Ghost To Aim With

**Type:** MINOR - fixes a hard crash and three rail-layer failures. Save-compatible.

**GitHub title:** `[11.36.0-dev] Graded formation, and a ghost to aim with`

#### 1. The crash - my mistake, with the fix already in the file

`InvalidOperationException` every frame the Rail Layer was held: I called `Input.GetKey` for the Ctrl modifier, and this project has legacy Input switched **off** in Player Settings.

What makes this worse than a simple slip is that `PlayerInteractionTool` already had `IsCtrlHeld()` - a guarded dual-backend helper - a few hundred lines above my code, with a comment directly beside it warning that a bare `Input` call throws every frame here. I wrote a fourth variant instead of using it. Now fixed to call the existing helper, and I checked no other raw `Input` call survives in the rail path.

#### 2. "No placeable cells" on a straight, flat-looking drag

The corridor solves on a **flat plane** through the drag's start point, then every cell is dropped onto the real ground. The probe searched 6 m up and 14 m down.

On a curved planet, or any real slope, cells far from the start sit further from that plane than the probe could reach. Those cells missed the ground entirely, silently kept their flat-plane position, and the gradient check then measured the plane-versus-ground divergence as a vertical cliff - refusing the run.

The probe now reaches 60 m up and 200 m down, and a genuine miss is reported as *"No ground under that route"* instead of being silently passed on as a bogus cell.

#### 3. Rails now grade the formation instead of refusing terrain

You asked for the rails to form the ground, and that is also simply how railways are built: the formation is cut and filled to suit the track, not the other way round. Refusing every natural slope made the tool unusable on exactly the terrain a railway exists to cross.

The run is now **smoothed before it is judged**. Six light passes ease the vertical profile toward a gentle grade, with the endpoints pinned so the line still starts and finishes where you clicked. Only terrain the smoothing genuinely cannot absorb is refused.

I checked the numbers rather than guessing: a bumpy 40-cell hillside with a worst step of **0.79 m** smooths to **0.27 m**, comfortably under the 0.34 m limit. Across a valley the line fills by up to about **0.9 m** - which is exactly what the ballast bed added in 11.35.0 represents.

Only the component along gravity is smoothed. Touching the horizontal would drag the line off the route the corridor solved and undo the curve fitting.

#### 4. The missing ghost

Between the two clicks you were committing to a route you could not see, and every refusal arrived only *after* the second click.

`RailGhost` now draws the pending run every frame, built from **the same plan the commit uses** - not a separate approximation. That property matters: if the ghost shows a route, the commit lays that route. A preview computed differently from the thing it previews can lie, which is worse than no preview.

A refused run still draws, in **red**, using whatever cells it solved. Hiding it would leave you aiming blind at precisely the moment you need to see what is wrong.

No manual Unity step - 11.35.0's step 93 already authored everything.

### [11.35.0-dev] Ballast, And Rail That Actually Costs Something

**Type:** MINOR - fixes the rail layer and gives track a proper stone bed. Save-compatible.

**GitHub title:** `[11.35.0-dev] Ballast, and rail that actually costs something`

Four problems with the 11.33.0 rail layer, all reported together and all real.

#### 1. "Already laid" on empty ground

The duplicate check rejected any cell within **0.45 m** of existing track, against cells spaced **1 m** apart. That sounds safe, and on flat ground it is.

But every cell is draped onto real terrain. On a slope or through a curve, neighbouring cells pull well inside half a metre of each other - so the run rejected **its own cells**, one after another, reported that the route already had track, laid nothing, and charged nothing. On perfectly empty ground.

The threshold is now a quarter of the cell spacing, derived from the actual cell size rather than hard-coded: tight enough to catch a genuine duplicate, loose enough that legitimately adjacent draped cells survive.

#### 2. It reported the wrong reason

"Already laid" was printed whenever nothing was placed, whatever the cause. That is what made this so confusing to diagnose - a failed solve and a genuinely occupied route produced identical messages.

`Commit` now returns how many cells it skipped, and the three outcomes read differently: cells laid, all cells already occupied, or no placeable cells produced. A message that can only say one thing is not a message.

#### 3. It consumed nothing

The tool charged a generic "rail material" that the setup step wired to **steel**, so laying track never consumed track.

It now consumes the **Rail Track item itself** - the same one you craft and place by hand - plus stone for the bed:

| Per cell | Cost |
|---|---|
| Rail Track | 1 |
| Stone | 2 |

That is the right relationship: **the tool saves effort, not materials.** A laid run costs exactly what laying it by hand would, so the choice to use it is about time rather than economy.

Material is also charged against what was **actually placed**, not what was planned, so skipped cells are never billed.

#### 4. No ballast

Track sat directly on the draped ground, half-sinking into anything uneven. Real track is laid on a raised stone bed, which is exactly what the reference photo shows.

Every cell now places a **Rail Ballast** slab under the rail, and the rail is lifted onto it. The slab is 1.5 m wide against a 1 m cell, so the shoulder overhangs the sleepers the way real ballast does.

I worked the offsets out numerically rather than eyeballing them: the slab pivots at its own centre, so it is sunk by half its height to sit flush with the ground, and the rail rises 0.18 m - leaving it 5 mm proud of the bed. Sleepers rest **on** the stone rather than floating above a gap or buried inside it.

Ballast is its own mineable block rather than part of the rail prefab, because a bed and a rail wear out for different reasons and the player should be able to see and remove it.

#### Manual step in Unity

**Tools -> Voxel Engine -> Voxel Engine Setup**, then **93. Build the Rail Layer** again. It is non-destructive and will author the ballast block and rewire the tool's costs without touching anything else.

### [11.34.0-dev] One Train Per Section

**Type:** MINOR - Train System v2, phase 4. Save-compatible and additive.

**GitHub title:** `[11.34.0-dev] One train per section`

Signalling and block occupancy - the last piece of the rework brief.

#### The problem

Two trains on one line drove straight through each other. Everything else was in place - grids on rails, consists, drag-laid corridors - and the thing stopping a player running more than one train was that a second train was a guaranteed overlap rather than a scheduling problem.

#### Sections are derived, not placed

The obvious design is a signal block the player places, which owns the track after it. That has a bad failure mode: a line with no signals is one giant section, so a new player's first railway cannot run two trains and the feature is invisible until they learn it exists.

Instead a section is **derived from the graph**: the track between two junctions is one section, because a junction is exactly where routes diverge and therefore where a train's path becomes uncertain. Signalling works the moment there is track, with nothing to place, and gets finer automatically as the network grows more complex - which is precisely when it is needed.

Same principle as deep ore nodes, hazard zones and asteroid placement: derived state cannot desynchronise from the thing it describes.

**A junction is its own single-cell section.** It is the one place two routes physically share metal, so it must be exclusive even when the lines either side are clear.

#### The detail that makes it correct

Section ids are the **lowest cell hash** in the section, not the first cell found. Two trains approaching the same stretch from opposite ends must compute the *same* id - otherwise each thinks it owns a different section and both enter it, which is exactly the head-on collision the system exists to prevent.

#### Self-deadlock, avoided twice

The classic way a signalling system fails is a train blocking itself:

- **A consist claims as one entity.** The claim is made by the head bogie, so a five-car train is one claimant rather than five competing over the same section. Followers never run the claim path at all.
- **A train releases everything behind it** as it moves, keeping only the section it occupies and the one it is entering. Both are held while crossing a boundary, because a train straddles two sections at that moment. Without the release, one lap of a loop would deadlock the network against its own train.

Claims are also dropped when a train is destroyed or lifted off the rails. A claim held by a dead object would block that line forever, and the only symptom would be trains mysteriously refusing to cross empty track.

#### Braking, not teleporting

Lookahead scales with actual stopping distance (`v^2 / 2a`), so a fast train gets more warning than a shunting one. A fixed distance would either stop a slow train absurdly early or fail to stop a fast one in time. A blocked train brakes to a halt rather than stopping the frame the signal turns red, which would look broken and throw anything riding on it.

#### The signal block is deliberately not load-bearing

Occupancy is automatic. Removing every signal in the world changes nothing about whether trains collide.

What a signal does is make an invisible rule legible: a train stopping for no apparent reason is a puzzle, and a red lamp at the point it stops is an explanation. It reads state and never changes it, so it cannot disagree with the thing it reports.

#### Manual step in Unity

**Tools -> Voxel Engine -> Voxel Engine Setup**, then **94. Build the Rail Signal**. Optional - occupancy already works without it. Right-click a signal to read its state.

### [11.33.0-dev] Lay A Line, Not A Thousand Cells

**Type:** MINOR - Train System v2, phase 3. Save-compatible and additive.

**GitHub title:** `[11.33.0-dev] Lay a line, not a thousand cells`

Wider gauges and draggable smart placement - the last two items from the rework brief before signalling.

#### The problem

Laying rail one cell at a time was the most tedious thing in the game. A line between two bases is hundreds of clicks, every curve is stepped by hand, and a gradient mistake is only discovered when a train refuses to connect to its own track.

Meanwhile the road system has had click-and-drag multi-lane corridors with solved curves for a long time.

#### Reusing the road solver rather than writing a rail one

`RoadCorridor` already solves exactly this geometry: a centreline through waypoints, fillet curves at corners, N parallel lanes with an explicit four-corner footprint per cell, and a refusal when a corner is too tight for the width. The roadmap named it as the precedent, and it was the right call - writing a second solver would mean two implementations of the same maths drifting apart.

What rail adds is only what roads genuinely do not care about:

- **Gradient.** Rail refuses a slope a road drapes over. The corridor is checked against `RailTrack.maxGradientMetres` per lane *along the direction of travel* - comparing across lanes would measure the cant of the formation, which is not a gradient at all.
- **Gauge meaning.** A road's lanes are independent surfaces; a rail corridor's lanes are **parallel tracks**, so a 2-wide run is a double-track mainline rather than one wide rail.

#### Refusals, not partial success

A plan is either fully placeable or fully refused, and the refusal names the reason - the steepest rise found, the gauge that cannot take the corner, the length of an over-long drag. Laying half a line because the far end was too steep would leave the player with track that goes nowhere and no explanation.

Material is counted **before** anything is placed, so a run the player cannot afford lays nothing and charges nothing rather than stopping halfway.

#### One ordering detail that matters

`Commit` places every cell first and links the whole run afterwards. Linking as it went would let each cell fill its limited link budget with the cell behind it before the cell ahead existed - producing a line of disconnected pairs that looks like track and behaves like gravel.

Cells that already have track are skipped, so crossing an existing line extends the network through the normal adjacency rules instead of stacking duplicate rails inside each other.

#### Deliberately not included: automatic junctions

The tool does not place switches where two runs cross. Auto-junctioning needs to know which of two crossing routes is the through line, and guessing wrong would silently reroute a player's trains. Crossing corridors simply connect, and the player places a switch where they actually want one.

#### Manual step in Unity

**Tools -> Voxel Engine -> Voxel Engine Setup**, then **93. Build the Rail Layer**. It requires step 85, because it lays the same Rail Track block you place by hand.

Hold the tool, click a start, aim at the far end, click again. Right-click cancels. **Ctrl + scroll** picks the gauge, 1 to 3 parallel tracks.

### [11.32.0-dev] Couple Them Up

**Type:** MINOR - Train System v2, phase 2. Save-compatible and additive.

**GitHub title:** `[11.32.0-dev] Couple them up`

Multi-car consists: park a railed construct behind another and couple them.

#### The docking-port precedent did not survive contact

The roadmap said to couple grids "the way docking ports already join grids". Docking ports use a `FixedJoint` - and that turns out to be exactly wrong here, for two reasons I only found by reading the code rather than assuming:

- **A railed grid is kinematic** (11.31.0 takes it out of the solver so the rail constraint is exact). A joint between kinematic bodies does nothing at all.
- **A joint trails like a rope.** A wagon holding a fixed distance from the locomotive's *current position* cuts every corner and ends up beside the track on any curve.

So consists use path history instead. The leader records where it has been, and each wagon samples that trail at its own distance back.

#### Why path history is the right shape

It gives the behaviour for free that a naive follower has to fake:

- A wagon retraces the **exact route** the locomotive took, so it stays on the rails through curves and points.
- It inherits the leader's routing decisions, **including which way a switch was thrown**, with no track logic of its own.
- Spacing accumulates **along the chain**, not straight-line, so the third wagon sits three gaps back along the actual route rather than three gaps as the crow flies.

A wagon that cannot find history far enough back holds station rather than snapping to the leader, which is what stops a freshly coupled train telescoping into itself.

#### Decisions worth naming

- **A towed wagon cannot drive.** Coupling forces its power off, and its `FixedUpdate` returns before any track logic runs. Two powered bogies on one consist fight each other, and a second bogie resolving switches independently could split a train across a junction.
- **Coupling keeps the spacing you parked at**, rather than snapping to a constant, so a consist holds the shape you built.
- **Refusals say why.** Too far, already coupled, not on rails, would form a loop - each returns a reason, because "I pressed couple and nothing happened" is indistinguishable from a bug.
- **Loop detection walks the chain before coupling.** Without it, a cycle would make every consist walk in the file run forever; every walk is also bounded at 64 cars as a second line of defence.

#### Breaking a consist safely

Removing a wagon from the middle relinks both neighbours **and hands its spacing to the one behind**, so the rest of the train does not lurch forward into the gap. Destroying the locomotive uncouples the wagon behind it and re-latches it to the track, so it is immediately drivable rather than stranded following a corpse.

#### Still to come

Wider gauges, draggable smart placement, and signalling. The 11.15.0 `RailTrain` stays until those land.

No manual Unity step - the Rail Truck from step 92 is all that is needed.

### [11.31.0-dev] A Train Is Just Something You Built

**Type:** MINOR - Train System v2, phase 1. Save-compatible and additive; the 11.15.0 rail network is untouched and existing trains keep working.

**GitHub title:** `[11.31.0-dev] A train is just something you built`

Section 6.4 item 1b - unifying rail with the grid system.

#### The open question turned out to be a wrong premise

The roadmap blocked this rework on one thing: *"how does a grid-based train keep running while its chunks are unloaded?"* - because unattended operation is the entire reason rail beat rovers for bulk haul. The expected answer was a dormant analytic mode along the rail path, mirroring `OrbitalRails`.

I checked before building it, and the premise was wrong. **Grids are not chunk-streamed.** They are persistent scene objects saved by body anchor, and nothing distance-culls them. Rail track is `PlacedBlock`, likewise never distance-culled. So a grid on rails keeps ticking wherever the player is, and the hard part did not need building at all.

That is worth stating plainly because it inverts the cost of the whole rework: the property that justified keeping trains separate was never actually at risk.

#### A train is now an ordinary construct

Build any grid, put a **Rail Truck** on it, drive it onto track. There is no locomotive entity and no special vehicle type.

Everything that already works on a grid now works on a train, for free:

- Any grid block works on a wagon - containers, tanks, refineries, turrets - because it is a grid and those are grid blocks.
- Damage, paint, power, pressurisation and the inspection overlay all apply.
- The console folds into the normal grid block UI instead of a parallel rail window.

#### Why a block rather than a flag

The Orbital Programme declares a satellite through `GridIdentity` - a property of the whole construct. Being railed is deliberately different: it is **hardware**. The player builds it, pays for it, can remove it, and it takes space on the hull. A flag would make every grid a potential train for free, and the rail capability would live nowhere the player can see.

#### Design decisions worth naming

- **The slowest truck wins.** A consist is tuned from the minimum speed and weakest acceleration across every truck aboard, because a train is limited by its worst component. Taking the best would mean bolting one fast truck to a heavy wagon made the whole thing fast, which is backwards.
- **A railed grid goes kinematic.** A rail is a hard constraint, and fighting the physics solver to hold one is how a train jitters, climbs its own track, or gets shoved off by a collision. Movement uses `MovePosition`, so anything standing on the train is carried rather than left behind.
- **Removing the last truck detaches and deletes the bogie**, handing the grid back to ordinary physics rather than leaving it frozen on track it can no longer drive.
- **The track cell is not saved.** The rail graph rebuilds from placed blocks, so a bogie re-latches from its restored world position a frame after load. Only the player's intent - powered, reversed - is state.

#### Still to come in this rework

Multi-car consists, wider gauges, draggable smart placement and signalling. The 11.15.0 `RailTrain` remains in place and working; it will be retired once consists land, rather than removing a working feature before its replacement is complete.

#### Manual step in Unity

**Tools -> Voxel Engine -> Voxel Engine Setup**, then **92. Build the Rail Truck**. Build a grid, place a truck on it, park within a few metres of track, then SNAP TO RAIL and DRIVE.

### [11.30.0-dev] Don't Overwrite What You Couldn't Read

**Type:** MINOR - save schema versioning and corruption recovery. Fully backward compatible: saves made before this release load unchanged and are migrated on the spot.

**GitHub title:** `[11.30.0-dev] Don't overwrite what you couldn't read`

Section 6.6 item 9 - Interplanetary Save Data.

#### What was actually missing

I checked what the item still needed before writing anything. Orbital stations already save (`OrbitalRails` Keplerian elements, restored at the correct phase), asteroid positions are derived from the world seed and correctly never saved at all, and cargo schedules shipped in 11.24.0. The item's content was done.

What was missing was the part the roadmap called "requires save schema v2" - and looking for it turned up something considerably worse than a missing version number.

#### The bug: a failed load would silently destroy the world

The loader caught every exception, logged it, and carried on with an empty world. The autosave timer then fired a few minutes later and wrote that empty world **over the save it had just failed to read**.

So a save that was merely unreadable - a truncated write, a half-flushed file, one corrupt field - became permanently lost data, automatically, with the only warning buried in the console.

Worse, the atomic save has been writing a `.previous` sidecar on every single save for a long time. **Nothing ever read it.** A perfectly good backup sat next to the broken file while the game overwrote the original.

Three fixes:

- **The backup is now read.** A failed primary load falls back to `.previous` and recovers from it, saying so clearly.
- **A failed load blocks all saving for the session.** Not just autosave - every entry point funnels through `SaveAll`, including quit and the pause menu, and I traced all four to confirm. The file and its sidecar are left untouched so the player still has something to recover from.
- **Truncated-but-parseable saves are caught.** `JsonUtility` happily returns an object for some malformed input, so a save missing its player block - the one field every save must have - is now treated as a failure rather than loaded as an empty world.

#### Schema versioning

`SaveData` now carries `schemaVersion`, stamped on every write. Saves from before this release have no field, deserialize as 0, and are treated as v1 and migrated to v2 on load.

The v1 to v2 migration is deliberately a no-op beyond the stamp itself. Every field added to this format has been additive - a missing list just takes its default - which is exactly why saves have kept working without a version until now. That only holds while changes stay additive, and the moment one does not, there is now somewhere for the fix to live and a number to decide which fix to apply.

A save from a **newer** build is loaded rather than refused, with a clear warning that anything this build does not understand will be dropped on the next write. Silently discarding a newer save's data would be worse than saying so.

#### Verified by case, not by inspection

| Scenario | Outcome |
|---|---|
| No file yet | Normal new world |
| Good save | Loads, migrates if old |
| Primary truncated, backup good | **Recovered from backup** |
| Primary and backup both corrupt | **Saving blocked, files preserved** |
| Exception during restore | **Saving blocked, files preserved** |
| Save from a newer build | Loads with a warning |

No manual Unity step.

### [11.29.0-dev] Your Base Keeps Working

**Type:** MINOR - a new system, save-compatible. Adds serialized per-machine state that defaults to "never serviced", so existing saves simply start their clock on load.

**GitHub title:** `[11.29.0-dev] Your base keeps working`

Section 6.6 item 8 - the "persistent base state across zone transitions" half of Scene/Zone Streaming.

#### What I found

Chunk streaming already exists, and terrain edits and placed blocks already persist. So the interesting half of item 8 was not loading - it was this: **every producing machine in the game ticks in `Update`**, which means a base on a planet you have flown away from produces absolutely nothing.

That quietly undermined the whole interplanetary layer. 11.24.0 shipped cargo pads so a second world could feed the first, but there was nothing to feed them with - the mine meant to fill the pad froze the moment you left orbit.

#### Catch-up, not background simulation

The obvious fix is to keep ticking unloaded machines. That is the wrong one: it costs CPU forever, scales with everything the player has ever built, and runs thousands of Updates for things nobody can see.

Instead a machine records **when it was last serviced**, and on waking asks how much simulated time passed. For a constant-rate machine, producing N seconds of output in one step is the same result as N seconds of ticking, at a fraction of the cost and with zero per-frame work while away.

Same principle already used three times here: satellites ride analytic orbits, trains walk a graph, deep ore nodes are derived.

Time comes from `CosmicRegistry.SimulationSeconds` - the one clock that is authoritative **and saved**, so it advances across a session boundary. `Time.time` resets on load and would hand out a free harvest or none at all depending on load order.

#### Deliberate limits, stated not hidden

- **Offline output is 45% of live output.** A base you are standing in must always be the better base, or the optimal play becomes logging out - a miserable design.
- **Catch-up caps at 12 hours.** Long enough that a real break is rewarded, bounded enough that an idle world cannot bank an infinite harvest.
- **Power cannot be verified retroactively**, so the extractor assumes it held but only claims the reduced offline rate.

#### Livestock plays by a different rule, on purpose

An unattended pen still drains hunger and thirst - you genuinely have to keep it stocked. But an animal can **never starve to death while you were unable to reach it**. Condition decays to a floor above the damage threshold and health is left alone.

Returning to a field of corpses you had no opportunity to prevent is a punishment for playing the rest of the game, not a consequence of neglect. Walk away and your herd is hungry and unproductive; it is not dead.

#### A real bug this uncovered

`CargoFlightRegistry` was ticked **only from a loaded pad's `Update`**. Fly away from both ends of a route and the shipment froze in transit indefinitely - the exact failure unattended freight exists to avoid, in the feature built to avoid it.

Flights now advance on the cosmic clock, driven by a tiny always-present `OfflineSimulationDriver` bootstrapped via `RuntimeInitializeOnLoadMethod`. No scene object to place and none to forget - a feature that silently dies because something was missing from one scene is a class of bug this project has already lost two releases to.

#### Two double-pay traps closed

- A machine running **live** pins its clock every frame, or an hour of real work would also be claimed as an hour of absence.
- A machine **stopped** (unpowered, output full) also pins its clock. Without that, a jammed extractor would bank its idle hours and pay them out as catch-up - rewarding the player for the time it spent jammed.

Catch-up is surfaced on the machine's status line rather than appearing silently, because a pile of ore with no explanation reads as a bug.

No manual Unity step.

### [11.28.2-dev] Mining Was Switched Off In Space

**Type:** PATCH - makes asteroids actually minable and correctly named. No save impact.

**GitHub title:** `[11.28.2-dev] Mining was switched off in space`

The rocks look right now, but hitting one did nothing and the label read "SpaceAsteroidField". Both had real causes.

#### Nothing happened because the tool disabled itself in deep space

`PlayerInteractionTool.Update` opened with:

```
if (world == null || shootCamera == null || inventory == null || registry == null) return;
```

There is no planet voxel world in deep space, so `world` is null out there and **the entire interaction tool returned before casting a single ray**. Every asteroid branch I added in the last two releases sat downstream of that line and could never run. I had checked that `MineVoxel` handled asteroids before its own world guard, but never checked whether `MineVoxel` was being reached at all.

The gate no longer requires a world. The planet-only paths that genuinely need terrain - liquid scooping and the fire igniter - now guard individually instead, so nothing that needs voxels runs without them.

#### It was called "SpaceAsteroidField" because rocks were children of the spawner

Every UI that names a hit surface falls back to `hit.collider.transform.root.name`. Rocks were parented to the field object, so the root was the spawner.

Two fixes, because one alone would have been a patch over a symptom:

- **Rocks are no longer parented to the field.** They register with `SpaceOrigin` individually as world roots. This also removes a latent trap: `SetParent(transform, false)` keeps the *local* pose and reinterprets the spawn position, which only worked because the field happens to sit at the origin. Despawn now unregisters the root, or `SpaceOrigin` would keep shifting a growing list of dead transforms.
- **The inspection HUD understands asteroids.** It resolves the voxel material under the crosshair *before* the planet lookup and the root-name fallback, so the label reads the actual material - "Iron", "Ice" - with an ASTEROID tag and a percentage remaining.

The GameObject is also named `Asteroid (Iron)` rather than `SpaceAsteroid_Iron`, so any other UI falling back to a root name reads sensibly.

#### What I got wrong in process

Twice now I have fixed something downstream of a gate without checking the gate. Verifying that `MineVoxel` handles asteroids proves nothing if `Update` returns three hundred lines earlier. Tracing the path from input to effect - not just inspecting the destination - is what would have caught this the first time.

### [11.28.1-dev] The Density Sign Bug

**Type:** PATCH - fixes the shattered, white, near-spherical rocks that 11.28.0 produced. No save impact.

**GitHub title:** `[11.28.1-dev] The density sign bug`

Your screenshot showed the real problem clearly, and it was not "a bit blocky": the surface was **broken into disconnected floating quads**, unlit white, on a shape that was still basically a sphere. Three separate bugs, all mine.

#### 1. Empty voxels must be NEGATIVE, not zero

This was the shattered surface.

`SurfaceNetsJob` finds the iso-crossing with `(da > 0) != (db > 0)` and places the vertex at `t = da / (da - db)`. I stored air as density **0**. The sign test still fired, but `t` collapsed to exactly 0 or 1, so every vertex snapped to a cell corner instead of interpolating between them. Worse, pass 2 decides which cells are solid with `IsTerrainSolid` (also `> 0`), so the two passes disagreed about which cells even had vertices - and the quads that should have joined them were skipped. Hence floating, disconnected faces.

The engine already had the right convention and I had not looked: `SphereDensity.EvaluateAsteroidVoxel` stores **solid as +1..127 and empty as -127..-1**. Asteroids now follow it exactly, including carrying the overshoot through as negative density when a dig empties a voxel - so a fresh crater has a real gradient to interpolate against instead of a faceted edge.

#### 2. White because the shader ignored vertex colours

The mesher bakes material colour into **vertex colours**. I fell back to a plain URP/Lit material, which does not read them, so every rock rendered flat white no matter what ore was in it.

Rocks now use the terrain's own material (`SphereWorld.terrainMaterial`, the `VoxelEngine/VoxelTerrain*` vertex-colour shader). Not a lookalike - the same material asset, so rocks and ground shade identically by construction. That is also the correct answer to "use the same materials as the planets".

#### 3. Still spheres because the noise was far too weak

I had used +/-11% and +/-5.5% displacement, which is a sphere with a faint orange-peel texture. It is now three octaves at +/-38%, +/-18% and +/-8%, on top of a wider per-axis ellipsoid stretch (0.55-1.45).

#### The consequence I had to solve for

Stronger displacement means a rock can reach `radius * stretch * 1.64`, which would have grown straight through the grid padding and been **sliced flat** where it ran out of voxels. The nominal radius is now clamped against that worst case explicitly.

Solving it at 0.5 m voxels capped rocks at a 3 m pebble, so voxels are now **1 m - exactly the planet's `VoxelConstants.VOXEL_SIZE`**. The 32-cell grid then spans 32 m, giving 5-14 m rocks, and a mining brush carves the same volume out of rock as it does out of ground. Spawner range is 2.5-6 m nominal, verified to fit with no clipping at every size.

### [11.28.0-dev] Smooth Rocks, Real Materials

**Type:** MINOR - replaces asteroid meshing and shaping. Save-compatible; asteroids are procedural and were never saved.

**GitHub title:** `[11.28.0-dev] Smooth rocks, real materials`

#### The blockiness was my own mesher

11.27.0 made asteroids into voxel bodies, but I wrote a hand-rolled exposed-face mesher for them - so every rock came out as stacked cubes. The game already had a smooth iso-surface mesher, `SurfaceNetsJob`, and I did not use it.

Asteroids are now meshed with **the exact job the planets use**. Same smooth surface, same shading path, same material colours - because it is literally the same code, not a lookalike.

#### Sizing the rock to the mesher, not writing a second mesher

`SurfaceNetsJob` is hard-wired to a padded `CHUNK_SIZE_P` (34) cube. Rather than generalise it - and risk destabilising planet terrain, which is the thing that matters most in the game - an asteroid's voxel grid is **exactly one chunk**.

That is not a real limitation: at 0.5 m voxels, 32 inner cells is a 14 m rock, which is already the top of the size range we want. The upside is that asteroids inherit every future fix to terrain meshing for free, and there is only one mesher to maintain.

Rock radius is now **2.5-7 m**, which is what actually fits the grid without touching the padding. The previous 11 m ceiling would have been silently clamped.

#### Not all spheres

A field of identical balls reads as procedural filler, so shape comes from three things layered together:

- **A random ellipsoid stretch** per rock (0.62-1.35 on each axis), so they are potatoes and shards rather than balls.
- **Two octaves of value noise** on the surface radius, for an irregular silhouette.
- **Zero to two gouges** - large spherical bites taken out of the body, so some rocks are cracked or cratered rather than whole.

#### Density has to be graded, not binary

The detail that actually makes it smooth: density is **signed and graded** (ramping at the skin, full strength deeper) rather than a hard 0/127. Surface nets positions each vertex by interpolating the iso-crossing between neighbouring voxels - with a binary field every crossing lands exactly halfway and you get the blocky look back, just with triangles.

Mining follows the same rule. A dig **softens** density at the rim of the brush instead of deleting it outright, so a fresh crater is rounded the same way the original surface is. A hard cut would leave faceted holes in an otherwise smooth rock.

#### Planet materials throughout

Voxels store real `MaterialId` values and are coloured through the shared `MaterialRegistry`, so asteroid stone is the same colour as planet stone and asteroid iron matches planet iron. Mining yields the registry's configured drops. There is no separate asteroid material table that could drift out of sync.

#### One placement subtlety

The mesher emits vertices at `cell * voxelSize`, so the mesh occupies a `0..17 m` box rather than straddling the origin. The renderer and collider therefore live on a **child** pushed back by half the grid, which puts the rock's true centre on its transform - so it tumbles about itself rather than swinging around a corner, and the world-to-local mining maths lines up. I checked the mapping numerically: centre cell 16.5 lands at local 0.0 from both directions.

No manual Unity step.

### [11.27.0-dev] Rocks You Dig Into

**Type:** MINOR - replaces how asteroids work. Save-compatible; asteroids are procedural and were never saved.

**GitHub title:** `[11.27.0-dev] Rocks you dig into`

#### What was wrong

11.26.0 made asteroids spawn, but they were still **destructible props**: one lumpy mesh with a health bar. You hit it, it popped, it gave you ore. You could not tunnel into a rock, could not see where the ore was, and could not leave one half-mined and come back.

That is not asteroid mining, it is breaking a crate that happens to be in space.

#### Asteroids are now real voxel bodies

Each rock is a small dense voxel volume of **stone shot through with ore veins**. Mining carves material out of it a scoop at a time; the mesh and collider rebuild from whatever is left; the rock disappears only when it is genuinely hollowed out. Health is gone entirely - a rock is not killed, it is consumed.

Ore is placed as **veins**, not a uniform mix, and biased toward the interior. That is the whole reason to dig rather than shoot: there is something to follow, and the valuable material is actually inside. A uniform sprinkle would make every cubic metre identical and the digging pointless.

#### Why its own voxel grid and not the planet's

`SphereWorld` is planet-scale - it streams chunks around a viewer and anchors coordinates to a body's centre. An asteroid is a free-floating object a few metres across that **drifts and tumbles**. Putting it in the planet grid would mean either it cannot move, or the grid has to support moving sub-volumes, which is a far bigger change than this is worth.

So each rock owns a small voxel array in **local** space and meshes itself. Because the data is local, the whole thing moves and rotates by just moving its transform - exactly what a drifting rock needs.

#### Deliberately blocky, deliberately small

The mesher emits only **exposed faces** as quads rather than running surface nets. A rock is at most ~48 cells per axis and is remeshed only when actually mined, so a smooth mesher buys nothing - and blocky faces read as *"this is voxel material you are digging"*, which is the point.

Rock size dropped again, from 4-26 m to **3-11 m**, because a voxel rock is remeshed on every dig and cost grows with the **cube** of the radius. At 26 m a rock was 373,000 cells and ~136,000 vertices per remesh. At 11 m it is 110,000 cells and ~24,000 vertices, comfortably inside the 16-bit index limit with a 32-bit fallback in place regardless.

#### Two collider decisions, opposite ways

- **Planet-side props want convex.** That was the 11.26.0 fix.
- **A voxel asteroid must be non-convex.** A convex hull would fill in the tunnels you just dug - you would mine a cave and still bump into a solid ball. Safe here because the rock is on a kinematic rigidbody.

#### Mining had to be taught about them

`MineVoxel` returned early whenever there was no active planet world, so mining in deep space was impossible by construction. Asteroids are now intercepted **before** that guard, and carve through their own path - they have no chunk streaming, no sea level and no biome, so sharing the planet path would mean threading "is this an asteroid" through all of it.

Under-tier tools still work, just slowly, the same rule as planet mining - a player must never hit an invisible hard lock out in space with no way back. A full inventory drops the ore at the rock rather than voiding it.

I also found and removed an **older asteroid path** in the same file that still called `TakeDamage`. It would have broken the build and, worse, shortcut the new voxel path entirely.

No manual Unity step. Fly above 12 km and start digging.

### [11.26.0-dev] Asteroids That Actually Exist

**Type:** MINOR - fixes a system that never worked, and corrects a claim I made last release. Save-compatible, no save format change.

**GitHub title:** `[11.26.0-dev] Asteroids that actually exist`

#### I was wrong last release

In 11.25.0 I marked Asteroid Mining COMPLETE after reading the code and seeing spawning, ore pools, drops and colliders all present. You tested it and they never spawned. The code existed; it could not work. Reading a system is not testing it, and I should not have ticked that item off without runtime evidence.

#### Root cause: the keep-out sphere was bigger than the spawn ring

`IsInsidePlanet` rejected any spawn within `radiusKm * 2` of a body - an entire extra planet radius of exclusion.

Planets in this game are **6-8 km in radius**, so that rejected everything within **12-16 km** of a body. The spawn ring only reached **14 km**. The arithmetic:

| Planet | Radius | Old keep-out | Ring | Spawnable band |
|---|---|---|---|---|
| Mars | 6 km | 12 km | 1.2-14 km | 2 km sliver |
| Pirate World | 7 km | 14 km | 1.2-14 km | **none** |
| Olympus | 7 km | 14 km | 1.2-14 km | **none** |

And because a rock only spawns once the player is 12 km *above* the surface, the viewer sits 19 km from the centre - where a 14 km ring mostly points back at the planet it just rejected. Inside any planet's frame, effectively every attempt failed.

The margin is now a **flat clearance above the surface** (2.5 km, plus the atmosphere where a body has one) rather than a multiple of the radius. A clearance is what the rule always meant - do not put a rock inside the ground or in the air a player is flying through - and it does not scale absurdly with body size.

#### The colliders were real but half-inert

`MeshCollider` was attached but left **non-convex**. A non-convex mesh collider cannot collide with other non-convex colliders and is skipped by several sweep paths, so rocks would stop a raycast while ships flew straight through them. Convex is also *required* for a collider on a moving body, and these drift and tumble. Now convex - which is correct anyway, since an asteroid is a lumpy ball.

#### They were also far too big

You described them as small voxel spheres; they were **8-140 m**. Against a 7 km planet a 140 m rock is 4% of a planet's diameter, and against a 0.5 m grid cell it is 280 blocks across - a moon, not something you mine. Rescaled, with the field tuned to match:

| | Was | Now |
|---|---|---|
| Rock radius | 8-140 m | 4-26 m |
| Spawn ring | 1.2-14 km | 0.4-6 km |
| Separation | 450 m | 90 m |
| Cluster radius | 250-900 m | 120-420 m |
| Despawn | 30 km | 12 km |

Separation mattered: at 450 m apart, rocks 8 m across meant a "cluster" was spread fifty times wider than its members.

#### It will not fail silently again

The reason this survived review is that a fully-rejected spawn pass looked exactly like "nothing to do". The field now warns once, naming the counts, when several passes in a row produce nothing while it is empty - so a mistuned filter reports itself instead of producing an empty sky.

No manual Unity step. Fly above 12 km and look around.

### [11.25.0-dev] Plug The Ends In

**Type:** MINOR - closes the automation gap on two existing blocks. Save-compatible, no save format change; existing placed pads and stations gain ports on load.

**GitHub title:** `[11.25.0-dev] Plug the ends in`

#### What I found while looking for the next feature

I went to start Asteroid Mining (section 6.6 item 3) and found it was **already done**: `SpaceAsteroidField` spawns fields in open space, the ore pool already includes platinum, gold, cobalt and ice, `SpaceAsteroid` derives from `Damageable` with real drops, and mining ships are just grids with drills. There was nothing to build, so I have marked it complete rather than re-shipping it.

What I found instead was a genuine gap that mattered more: **the Rail Station and the Cargo Launch Pad had no item ports.** Both had to be loaded and emptied by hand.

That quietly broke the point of both blocks. The whole argument for putting the cargo hold on the station rather than the train was that a factory could fill or drain it on its own schedule while the train just turns up - and that only works if a belt can reach it. The cargo pad is the last link in a chain that starts at a mine, and it could not be fed from one.

#### Both blocks now accept belts and pipes

`RailStation` and `CargoLaunchPad` both implement `IItemPortHost`, so conveyors, chutes, funnels and pipes route into and out of them like any other machine.

They differ in one deliberate way:

- **The rail station allows both directions on its hold.** Its LOAD / UNLOAD role already decides which way cargo flows through the *train*, and a train services the hold through a separate path. Pinning port direction as well would duplicate that decision and let the two disagree.
- **The cargo pad follows its role.** A SEND pad is input-only and a RECEIVE pad is output-only, because a SEND pad is *accumulating* toward a full launch load - if a belt could also pull from it, it would never reach launch size and the pad would sit there loading forever.

Changing a pad's role now flips its port direction with it, through a property that drops the cached descriptor. Setting the raw field would have left belts facing the old way.

#### Existing builds are fixed automatically

Both use `[RequireComponent(typeof(ItemPortRouting))]` rather than having the setup step attach routing. That matters: a pad or station **already placed in a save** gains routing when it loads, instead of only newly built ones working after re-running setup.

No manual Unity step for this release.

### [11.24.1-dev] The Missing Setup Steps

**Type:** PATCH - no gameplay change, no save impact. Restores editor scripts that were never reaching the repository.

**GitHub title:** `[11.24.1-dev] The missing setup steps`

#### Root cause found

Setup steps 89, 90 and 91 kept vanishing between deliveries. The runtime code persisted every time; only the editor scripts and their wizard buttons disappeared, which is why the features looked half-shipped - all the behaviour existed but nothing could author the assets in Unity.

The cause was **workspace size**, not the code. The project was carrying an `Imported Textures` folder of **3,760 files and 49 MB**, putting the repository at **10,076 files and 119 MB** - right on the snapshot limits of roughly 10,000 files and 128 MB. Files past the cap were silently dropped when the workspace was saved, and the most recently written ones lost the race.

`Imported Textures` has been removed from this workspace at your request (it remains in your local copy). The project is now **6,321 files and 70 MB**, with comfortable headroom.

#### Restored

- **Step 89** - Orbital Station Family. Eight hammer families, four tiers each, plus the `orbital_construction` node.
- **Step 90** - Station Life Support. The oxygen source for sealed station compartments.
- **Step 91** - Interplanetary Cargo Pad. Bulk freight between bodies.
- All three wizard buttons in the setup window.

#### Verified this time

Rather than assuming writes landed, this release was checked end to end after the fact:

- All three setup scripts and their `.meta` files exist on disk.
- All three wizard buttons are present.
- Every runtime file from 11.13.0 through 11.24.0 is present - **no other losses**.
- Brace balance verified across all sixteen recently touched files.
- No orphaned `.meta` files and no `.cs` file missing one.

#### Manual step in Unity

**Tools -> Voxel Engine -> Voxel Engine Setup**, then run **89**, **90** and **91**. All three are non-destructive and safe to re-run.

### [11.24.0-dev] Freight Between Worlds

**Type:** MINOR - a new system, save-compatible. Two additive save lists, both empty until cargo pads exist.

**GitHub title:** `[11.24.0-dev] Freight between worlds`

Section 6.6 item 5 - the Interplanetary Cargo Rocket.

#### First: two setup steps were missing again

Steps **89** and **90** did not survive into the repository on their previous deliveries - the runtime code persisted every time, but the editor scripts that author the assets, and their wizard buttons, were lost. That means the station family and life support had no way to be created in Unity.

Both are restored and verified in place alongside step 91. **Please run 89, 90 and 91.** I have checked all three source files, their `.meta` files and all three wizard buttons exist this time rather than assuming the write landed.

#### Why cargo pads rather than a buildable rocket

The roadmap lists a multi-stage rocket vehicle as well. That is deliberately **not** what shipped here, because the game already has a way to reach orbit: build a grid with thrusters and fly it. A separate rocket entity would be a second flying thing that is not a player-built grid - exactly the split the rail system is being reworked to remove. Manned flight stays a grid you build.

So the cargo pad handles what a piloted grid is genuinely bad at: **unattended, repeatable, scheduled bulk freight**. The two answer different problems, which is what keeps both worth having.

This completes the logistics ladder, and it is the rung that was missing:

| Tier | Range |
|---|---|
| Belts | Metres, inside a factory |
| Trains | Kilometres, across one planet |
| Drone ports | 400 m, point to point |
| **Cargo pads** | **Between bodies** |

Without it a second planet is a place you visit rather than a place you can industrialise, because nothing built there can feed anything built at home.

#### Flights are simulated, not flown

A launch is a timer and a manifest, not a physics object - the same reasoning as trains walking a graph and satellites on analytic rails. A shipment completes whether or not either world is loaded, which is the entire promise of unattended freight.

The flights live in a central registry rather than on the pads, deliberately. A flight outlives its endpoints' loaded state: the origin is usually on a planet you have left and the destination on one you have not reached. On a pad it would stop being ticked exactly when it matters.

Transit time is derived from the real distance between the two bodies, through a square root so that near and far destinations are both usable, then clamped at both ends - a floor so nothing is instant, a ceiling so an unlucky planetary alignment cannot strand cargo for a whole session.

#### Cargo is never destroyed

Three separate cases, all resolved the same way:

- **Destination pad missing on arrival** - the flight holds and re-checks, rather than evaporating. It may simply be in an unloaded chunk, and deleting cargo for that would be silent theft.
- **Destination hold full** - the flight circles and retries.
- **Partial delivery** - the remainder stays in flight instead of being lost.

A launch also needs a **full load of one item**. A pad waits rather than burning an entire flight on a handful of ingots.

#### One subtle placement rule

A pad records which body it was built on **once**, at placement, and never re-samples it. The active body changes as the *player* travels, so a pad reading it live would think it had relocated to whatever world its owner happened to be standing on. The recorded body is saved and restored explicitly for the same reason - pads are restored while the player may be on an entirely different world.

#### Manual steps in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Run **89**, **90** and **91** (89 and 90 were missing from previous drops).
3. Research **Interplanetary Logistics**.
4. Build a pad on each body, name them, set one SEND pointing at the other and one RECEIVE, then power the sender and fill its hold.

Right-click a pad for the console and its flight board.

### [11.23.0-dev] Hold Your Breath, Or Don't

**Type:** MINOR - a new system, save-compatible. One additive save list, empty until a station is actually pressurised.

**GitHub title:** `[11.23.0-dev] Hold your breath, or don't`

The world room solver - the half of Space Stations that 11.22.0 deliberately did not claim.

#### Finishing what was deferred

11.22.0 shipped the Orbital Station hammer family and explicitly refused to claim pressure integration, because `GridPressureSystem` and `GridRoom` are a **ship** system: they walk a grid's integer block dictionary and know nothing about world-placed objects. Rather than fake it, that release recorded the sealing intent and left the note. This is the missing half.

#### A separate solver, a shared algorithm

The two systems live in genuinely different coordinate spaces. A ship has an authoritative integer lattice with one block per cell; a hammer station is loose world objects at arbitrary positions. Forcing station pieces into the grid solver would mean inventing a fake grid for them, and every future change to ship pressure would have to keep that fiction alive.

So `StationRoomSolver` shares the **algorithm** that was already proven in the grid solver - a bounded flood fill with a one-cell escape shell - and keeps its own coordinate handling.

**The escape shell is the whole trick.** A fill that reaches the shell has found a way out, so that volume is open space rather than a room. That is what makes "sealed" mean something: three walls and optimism will not pressurise anything, and opening an airlock genuinely vents the compartment because the next solve escapes through it.

Solving is event-driven and coalesced - placing a piece marks the solver dirty and the fill runs at most a few times a second - so building a wall stays cheap. A fill that exceeds its cell budget is treated as open rather than allowed to run away.

#### Air has to be produced

A newly sealed volume starts **empty**. A station is built in vacuum, and assuming a full charge the moment a room closes would make the hull and the airlock decorative.

The new **Station Life Support** unit is the source. It is a placed machine that:

- Costs power continuously, so holding an atmosphere is the ongoing price of living up there - and orbital power generation finally has a real consumer.
- **Leaks.** A room slowly loses air, so life support is not a switch you flip once. Cut the power and the compartment goes stale. The rate is deliberately gentle: losing a compartment should give you time to notice, not punish you for walking away.

A **DOCK** collar does not seal, by design. A room with one in its wall will not hold pressure until a ship mates into it, which is exactly what a docking port should mean.

#### One seam, everywhere

Station rooms plug into `RoomAtmosphereService`, the existing single point that answers "is the air here breathable". Player life support, HUDs and offline survival all ask there, so a pressurised station compartment now works for all of them at once without touching any of them.

It is checked **after** ship rooms and deliberately not folded into `RoomAt`, which returns a `GridRoom` - a station room is a different type in world space, and widening that return would force every existing caller to handle a case it does not have.

#### Persistence

Only the **charge** is saved, keyed by room anchor. The rooms themselves are a pure function of which station pieces exist, and those are already saved as placed blocks - storing the geometry too would be a second copy that could disagree with the first.

Charges are applied **one frame after load**, because the pieces are restored by the same load pass and a solve running immediately would find an empty world. A charge whose room no longer exists is dropped rather than leaking into whatever replaced it.

#### Manual steps in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Run **89. Build the Orbital Station Family** if you have not already - it was missing its setup file in the 11.22.0 drop and is included again here.
3. Run **90. Build Station Life Support**.
4. Seal a compartment out of station pieces, place a Life Support unit inside it, and give it power.

### [11.22.0-dev] Somewhere To Live Up There

**Type:** MINOR - a new build family and its research, save-compatible. `BuildFamily` values are APPENDED, so every piece already placed keeps its meaning.

**GitHub title:** `[11.22.0-dev] Somewhere to live up there`

Section 6.6 item 2 - Space Stations & the Orbital Station Hammer Family.

#### Eight new families on the Hammer wheel

| Piece | Role |
|---|---|
| HULL | Pressure wall with structural ribs |
| DECK | Interior floor with a utility channel |
| CORRIDOR | Open-ended tube; chains into runs |
| JUNCTION | Open on four sides; where a station branches |
| VIEWPORT | Framed reinforced window |
| AIRLOCK | Sealed hatch with a mating ring |
| DOCK | Open collar a ship mates into |
| DOME | Observation cap for a hull or junction run |

Each exists at all four build tiers, as every hammer family does. The tier ladder is deliberately flat here - upgrading a station piece is cheap rather than a second full build, because a station is already an end-game structure and making it a four-times-over material sink would just be tedium.

#### The wheel now has groups, not more pages

The obvious implementation was to append eight families to the existing list. That would have pushed the wheel from two pages to three and buried the everyday pieces a player uses constantly behind the ones they use occasionally.

Instead the wheel has two **groups**, and `TAB` swaps between them while it is open. It always opens on STRUCTURAL, because that is what gets used most even after the station set unlocks. The TAB hint only appears once Orbital Construction is researched, so the station set reads as a discovery rather than a permanently greyed-out tease.

`TAB` is contextual rather than a global keybind - it only means anything with the wheel up, so it costs no key the player might want elsewhere.

#### Station pieces only snap to station pieces

A station is a sealed pressure vessel. If a wooden wall could close a hull run you would get a "sealed" compartment with a plank in it, which is exactly the sort of thing that makes a pressure system feel arbitrary once one is wired up. The socket rules enforce the separation in both directions.

Between station pieces the rules are deliberately **permissive** - edge-to-edge on all four sides plus top and bottom. A station is built in open space with nothing to anchor to, and over-constraining the sockets would make it impossible to close a ring corridor back on itself.

#### Pressure: scoped honestly

The roadmap says airtight station pieces integrate with room pressure and oxygen. That part is **not** claimed here, and the roadmap entry is marked partial rather than ticked.

The reason: the pressure simulation in this codebase (`PressureRules`, `GridRoom`) operates on `GridBlock`. It is a **ship** system and has no concept of world-placed blocks at all. Wiring hammer pieces into it needs a world-side room solver that does not exist yet, and shipping half of one would produce compartments that look sealed and behave like open vacuum - worse than not claiming it.

What ships instead is the honest groundwork: `StationPiece` records the sealing intent per piece (every family seals except the DOCK collar, which is open by design) and keeps a registry ready for that solver.

#### Implementation note

**Tier upgrades re-tag the piece.** The upgrade path destroys and rebuilds the GameObject, so a station hull would silently stop being a station piece the first time it was upgraded from wood to steel. Both the place path and the upgrade path now call the same tagging helper.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **89. Build the Orbital Station Family**.
3. Research **Orbital Construction**.
4. Hold the Hammer, open the build wheel, press **TAB** to reach the ORBITAL STATION set.

### [11.21.0-dev] Prospect From Orbit

**Type:** MINOR - a new payload and its research, save-compatible. No save format change; survey results are derived, not stored.

**GitHub title:** `[11.21.0-dev] Prospect from orbit`

Section 6.6 item 4 - Satellite Network: scan planets for resource deposits. The first entry from 5.0.0 Orbital Expansion.

#### Why this one first

It is the piece that makes three separate systems into one chain. 11.13.0 put satellites in orbit, 11.14.0 gave them sensor payloads, and 11.18.0 buried finite ore deposits that have to be found on foot with a hand scanner. This connects them: a satellite now prospects the ground beneath it, so the orbital programme finally pays back into the industry on the surface instead of only reporting weather.

#### The Resource Scanner

A fourth satellite payload. Where the hand-held Deep Survey Scanner reports **the single nearest deposit** within 1.4 km, an orbital scanner maps **every deposit** within 6 km of the satellite's ground track and ranks them by distance.

| Payload | Capability | Idle |
|---|---|---|
| Sensor Array | Planet-wide season telemetry | 120 W |
| Weather Radar | + live weather and forecast | 220 W |
| Climate Control Array | + weather influence | 260 W |
| Satellite Resource Scanner | Maps deep ore deposits | 340 W |

Deliberately **not** a strict upgrade of the weather tiers. A scanner is a survey instrument with no meteorological hardware at all - it reports seasons like every payload does, but has no radar and no influence. Keeping the branches distinct stops the newest payload from simply being "all of the above", which would retire the other three.

It surveys from the **satellite's** position, not the player's. Reporting what is under the player's feet would make the satellite a pointless middleman for a tool they can already carry; surveying the ground track is what makes the orbit itself matter, and what gives a player a reason to care where they put it.

Exhausted deposits are still listed, greyed out and marked EXHAUSTED, so a player does not fly to one they already drained and conclude the scanner lied to them.

#### It feeds the logistics map

Surveyed deposits now appear on the `L` map as purple triangles, with their own layer toggle and a SURVEYED DEPOSITS section in the sidebar.

Crucially the map shows **only what a scanner has actually seen**, never every deposit in the world. Revealing them all would make the scanner pointless and hand the player a finished prospecting answer for free. What the satellite has surveyed, the map draws - nothing more. With no scanner in service the section says so plainly rather than sitting empty.

#### New research

**Orbital Prospecting** (tier 6), behind Orbital Science. It gets its own node rather than riding along with the existing payloads, because prospecting from orbit is a genuinely different capability from watching the weather, and bundling it would hide it behind a name that does not suggest it exists.

#### Implementation note

**The survey result list is per-instance, not a shared static.** A static scratch list returned to callers is silently overwritten the moment a second scanner surveys, so a caller iterating one scanner's results would start reading another's partway through - the kind of bug that only appears once a player builds their second satellite.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **84. Build the Orbital Programme** again - non-destructive, adds the Resource Scanner and the Orbital Prospecting node without touching anything authored.
3. Research Orbital Prospecting, build a Resource Scanner onto a satellite, and commit it to orbit.
4. Open the payload console for the ranked list, or press `L` to see deposits on the map.

### [11.20.0-dev] Earn The Late Game

**Type:** MINOR - a new system, save-compatible. One additive save list. Existing research nodes are untouched and keep working exactly as before.

**GitHub title:** `[11.20.0-dev] Earn the late game`

Section 6.5 item 11 - the progression half of Mythical Enemies & Bosses: enemy tiers, Boss Relic Cores, and relic-gated research.

#### What already existed

Seven creatures already ship: Basilisk, Ghoul, Griffin, Ifrit, Karkadann, Manticore and Roc, each with its own AI, drops and health bar. The bestiary was not the gap.

The gap was that **beating one meant nothing**. There were no tiers, no relics, and nothing in the tech tree that required defeating anything. The roadmap's own rule - "low-tier farming cannot replace boss progression" - had no mechanism enforcing it.

#### A relic is not a rare drop

A rare drop is a lottery ticket: kill things until the number comes up. That trains grinding, which is exactly what the roadmap forbids. So a Boss Relic Core is:

- **Guaranteed** from its boss. The encounter is the cost, not the RNG.
- **Unique** to that boss. No substituting an easier one.
- **Never consumed.** It is proof, not currency.

That last point is the important one. The relic is recorded in a permanent ledger, and research checks the ledger rather than spending the item. Consuming it would mean a player who researched one node is locked out of a second node needing the same relic - and would have to re-kill a unique boss that may never respawn.

| Relic | Boss | Gates |
|---|---|---|
| Sky Core | Storm Roc | Stellar Engineering (also needs an orbital lab) |
| Petrified Core | Elder Basilisk | Petrification Studies |
| Ember Core | Ifrit Sultan | Ember Forge Mastery |
| Brute Core | Karkadann Tyrant | reserved for heavy structural research |
| Abyss Core | Leviathan | reserved for maritime research |

Stellar Engineering is the roadmap's Star Builder / Dyson Sphere gate: it needs the relic **and** an orbiting satellite laboratory. The relic proves the encounter, the lab proves the infrastructure.

#### Boss variants are separate prefabs

`Boss_Basilisk`, `Boss_Ifrit`, `Boss_Roc` and `Boss_Karkadann` are built from instances of the existing creatures, so they inherit the authored mesh, colliders, AI and ordinary drops exactly - then get scaled up and given a health multiplier. The ordinary creatures are **completely untouched** and still spawn as before.

Health is a multiplier applied at spawn rather than a separately authored number, so a boss and its common version cannot drift apart every time the base creature is retuned.

#### Where the gate lives

`ResearchNode.ScienceCost` is typed to `ScienceItem`, and widening it would have touched every node in the project. A relic requirement is also not really a cost - it is a facility-style prerequisite, the same shape as `requiresOrbitalLab`. So it reuses that gate and plugs into `GetFacilityBlockReason`, which **both** research entry points already funnel through, so the gate cannot be bypassed by the other path.

#### The research UI now explains itself

Worth calling out because it was a pre-existing hole: the orbital gate shipped in 11.13.0 was enforced in the manager but never surfaced in the UI. The button looked live and simply did nothing when pressed. Locked nodes now state the actual requirement - "Requires a Sky Core, recovered by defeating a Roc" - which is the whole difference between a designed gate and an apparent bug. This fixes the orbital gate's presentation too.

#### Implementation note

**The relic is granted in `OnDestroy`, not in an `Update` poll.** `Damageable.Die` calls `Destroy(gameObject)` in the same frame health reaches zero, so a polling check can miss the death entirely - the object is gone before the next tick. `OnDestroy` is the only hook guaranteed to run. It is guarded against scene unload and play-mode exit, since those destroy the object too and must not hand out a relic.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **88. Build Boss Relic Cores**.
3. Boss variants appear in `Resources/Enemies` as `Boss_*` prefabs, ready to place or spawn.

If it reports missing base creatures, run the enemy content steps first and re-run.

### [11.19.0-dev] Keep Them Alive

**Type:** MINOR - a new system, save-compatible. No save format change. Existing animals gain husbandry when the setup step runs; wild herds are unaffected until a pen is built near them.

**GitHub title:** `[11.19.0-dev] Keep them alive`

Section 6.5 item 7 - the livestock half of Fauna / Flora & Livestock.

#### What already existed

Passive fauna shipped some time ago: `PassiveAnimal` already does cows, sheep and pigs with wander-and-flee AI, correct tangent-plane movement on a spherical world, and drops on death. Raw Meat, Hide and Wool items already exist.

So this release deliberately adds **only** the husbandry layer the roadmap asked for - food, water, shelter, health, reproduction and population limits - rather than re-shipping animals that already work.

#### Husbandry is a component, not a new animal

`LivestockHusbandry` attaches to the existing `PassiveAnimal`. A second animal class would have duplicated the AI, the spherical-gravity movement and the drop handling, and left two implementations to keep in step forever.

Being additive has a nice consequence: a wild herd and a farmed herd are the same object with different components, so a player can farm animals they found rather than only animals they bought. An animal with no pen nearby behaves exactly as it always has.

#### Why livestock is worth building next to hunting

Hunting is extractive - kill once, walk further next time. Husbandry is renewable, and critically **milk and wool are produced without death**. Killing the animal ends the income, so the mechanic pushes the player toward keeping animals alive without needing a rule that says so.

| Animal | Renewable product |
|---|---|
| Cow | Milk |
| Sheep | Wool |
| Pig | None - meat and hide only |

#### Needs are a chain, not a timer

An animal that just needs feeding every N minutes is a chore. These needs feed each other:

**Hunger and thirst drive HEALTH. Health gates PRODUCTION. Health and maturity gate BREEDING.**

So a neglected pen degrades visibly rather than failing all at once: production stops first, condition drops, and only then do animals start dying - slowly, at well under one health per second. A player who logs off with a half-full trough comes back to unhappy animals, not a pen of corpses. Hungry animals also visibly slow down, which signals neglect with no UI at all.

Shelter halves consumption and speeds production. That is the entire mechanical argument for building a barn instead of leaving animals in a field.

#### The pen

`LivestockPen` owns the trough for the same reason the rail station owns the cargo hold: the animal moves and is often not where the player is, while the pen is fixed and always addressable. Put the supply on the pen and a factory can belt or pipe feed into it on its own schedule - the player keeps the pen stocked instead of chasing cows.

It feeds, waters, shelters, breeds and harvests everything within 12 m, and its console lists **every animal individually** with its own food, water and condition. A totals-only readout would report a farm as "fine" right until animals started dying, because an average hides the one starving sheep.

**The population cap is the load-bearing rule.** Uncapped breeding wrecks performance and the economy simultaneously. The cap is per-pen, enforced at the moment of breeding, and shown in the header so hitting it reads as a designed limit rather than the feature quietly breaking.

#### Implementation notes

- **Starvation does not use `TakeDamage`.** `PassiveAnimal.TakeDamage` triggers the flee reflex, so routing attrition through it would make a hungry animal bolt every frame and scatter a penned herd. Added `ApplyAttritionDamage` for damage that is not an attack.
- **Newborns are reset.** A calf is cloned from a parent so it inherits the authored prefab exactly, which also means it inherits the parent's age - it would be born adult and instantly breedable without `MarkNewborn()`.
- **Produce is refunded if it cannot be stored.** A weight-capped container can reject what `HasSpace` allowed, so the product goes back to the animal rather than vanishing.
- Feed is matched by item id substring (wheat, corn, grain, hay, biomass, root crops), so the pen works with whatever crops the project has authored and keeps working as more are added.
- **Products are direct serialized references, not a runtime name lookup.** `Item_Wool` lives under `VoxelEngineAssets`, not under a `Resources` folder, so a `Resources.LoadAll` search would have found nothing and stalled every sheep in the game while looking perfectly healthy. Setup step 87 assigns the references directly and only fills in ones that are missing.
- **Milk did not exist.** Wool and Hide were authored by the earlier fauna step but Milk never was, so cows would have filled up with nothing to hand over. The step now authors it beside the other animal products, and skips it if any Milk item already exists anywhere in the project.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **87. Build Livestock Husbandry**. It attaches husbandry to the Cow, Sheep and Pig prefabs and authors the pen. If it reports missing animal prefabs, run the fauna content step first and re-run.
3. Craft a Livestock Pen, place it where animals graze, and put feed and water in it.

Right-click the pen for the herd roster.

### [11.18.0-dev] Something Worth Building On

**Type:** MINOR - a new system, save-compatible. One additive save list, empty until a player actually mines a node. Deposits themselves are derived, so existing worlds already have them.

**GitHub title:** `[11.18.0-dev] Something worth building on`

Section 6.5 item 6 - Caves & Resource Nodes.

#### What was already there, and what was not

Caves already exist: `PlanetField.CaveCarve` carves them into the voxel terrain, sealed under a protective crust and never below the sea. That half of the item was done.

What was missing was the second half - "large, finite ore nodes" that "encourage outpost building". Those two clauses are the same requirement said twice: a node only encourages an outpost if it is worth travelling to **and** worth staying at.

#### A deep node is a different verb, not a bigger vein

The important decision: a deep node **cannot be mined by hand at all**. It sits below the voxel world, is found with an instrument, and is extracted by a powered machine that runs unattended. That is what turns "I found ore" into "I am going to build here" - because the extractor needs power, power needs infrastructure, and infrastructure is an outpost.

It also keeps the existing tools relevant rather than obsolete:

| Tool | Role |
|---|---|
| Hand mining | Immediate, portable, tiny yield |
| Ship drill | Mobile, player-driven, follows visible veins |
| Deep Core Extractor | Fixed, unattended, huge finite yield, needs a base |

#### Where nodes are: derived. What is left: stored.

Node placement is a pure function of (world seed, position), sampled from Worley noise on a 900 m lattice at 30% occupancy - the same technique as the hazard zones in 11.17.0. No spawning, no registry, no streaming, and **every existing world already has deposits** without regenerating anything. Two players on one seed find the same nodes.

Depletion is the one thing that cannot be derived, because it is a record of what the player did. So the save stores exactly one thing: a list of `node key -> amount taken`. An untouched node has no entry, so a fresh world and a legacy save both cost zero bytes. That is the minimum possible save surface for a finite resource.

Ten materials appear, weighted so scarcity survives: iron is roughly ten times more likely than gold, and the rare ores also come in smaller nodes. A uranium find is valuable without being a permanent solution to uranium.

#### Depletion is the design

An infinite extractor would end the resource game the first time one was built. A finite one gives an outpost a **lifespan** - so the player keeps surveying, keeps expanding, and eventually abandons and relocates. That loop is why the roadmap wanted finite nodes rather than just bigger veins.

One subtle correctness point: the extractor takes from the node *first* and banks what was actually granted, rather than producing and then decrementing. Reversed, two extractors on one deposit would each mint the last item. And anything the output buffer refuses is **refunded to the deposit** rather than dropped - silently destroying ore one item at a time is the worst possible bug for a finite resource, because it is invisible.

#### Finding them

A deposit below the crust is invisible by design - if it could be seen from orbit it would be a pickup, not a find. But an invisible resource with no instrument is arbitrary rather than mysterious, so the **Deep Survey Scanner** is the other half of the feature. Carry it and a strip reports the nearest deposit and the distance to it, with a meter that fills as you close. Surveying becomes a metal-detector loop: walk, watch the number, know immediately whether the last step helped. Stand on the deposit and it switches from distance to how much is actually in it.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **86. Build the Deep Core Programme**.
3. Craft a Deep Survey Scanner, carry it, and walk until the readout says HERE.
4. Place a Deep Core Extractor on the deposit and give it power.

Deposits exist in worlds saved before this version - nothing needs regenerating.

### [11.17.0-dev] Nowhere Is Uniformly Safe

**Type:** MINOR - a new system, save-compatible. No new save fields at all; hazard zones are derived, not stored, so existing worlds gain them on load.

**GitHub title:** `[11.17.0-dev] Nowhere is uniformly safe`

Section 6.5 items 5, 8 and 9 - Biome Hazards, Environmental Radiation Zones, Environmental Heat Zones - which are one system, so they ship as one.

#### What was actually missing

The game already had radiation damage, heat damage, hazmat plating, Radiation Shielding and Heat Tolerance upgrades. What it did not have was **anywhere in particular**.

A body had one `radiationLevel` and one surface temperature, so a planet was uniformly lethal or uniformly safe. That makes protection a packing-list item: check the number before you launch, bring the suit, never think about it again. The roadmap asks for hazard ZONES, and a zone is a different design object - it makes a planet something you read as you cross it, with hot spots to route around, and it finally gives the Geiger counter a job.

#### Zones exist without being stored

`HazardField` samples zones from noise seeded by the body's own `genParams.seed`, exactly the way ore veins already work. So:

- **No save data.** A zone is a pure function of (body, position) - identical on every load, identical across a rebuild, and **every existing world gains zones for free**.
- **No spawning, streaming or registry.** A zone can be asked about at any position, including inside chunks that were never loaded.

Same reasoning that put satellites on analytic orbits and trains on a graph: derive it when that is cheap, rather than simulate and store it.

Worley (cellular) noise rather than fractal, for the reason ore veins use it: Worley makes discrete blobs with clear centres and clear gaps, which is what an *avoidable* zone needs. Fractal noise makes a smear the player can never be sure they have left. Coverage is a deliberately low 34% - a zone that is everywhere is just a bigger planet constant wearing a costume.

#### The planet constant became the floor, not the answer

Authored `radiationLevel` still applies everywhere as a baseline, and zones only ever **add** on top. No existing world becomes safer and no authored value is overridden.

Zones also scale with the planet's own character rather than being pasted on:

| Zone | Only appears where | Because |
|---|---|---|
| Radiation | The body has any radiation at all | A clean world should not sprout hot spots it was never authored to have |
| Heat | Surface is above -10 C | A frozen moon does not grow lava fields |
| Toxic | There is real air, and it is not breathable | Toxic air needs air. The dangerous case is a dense atmosphere that is not oxygen |

#### Toxic atmosphere

The new third channel. It is stopped by **sealed air, not plating**: a breathing kit with gas remaining is total protection, and anything less is none. No partial credit on purpose - a half-sealed suit in poison gas is not half-safe.

#### The Geiger counter

A hazard the player cannot detect until they are dying in it is not a feature, it is an ambush. `HazardWarningHud` is the half that makes zones playable, and it shows exactly three things: what the hazard is, how strong it is (a meter that fills *before* the damage gets serious), and **whether the player is actually protected against that specific hazard** - because "radiation, and you are fine" and "radiation, and you are not" are completely different situations that a bare number cannot tell apart.

It only appears for a genuine LOCAL zone. A uniformly irradiated planet is a constant the player already accounts for, and a permanent warning would train them to ignore the strip exactly when a real zone shows up. Only an unprotected reading pulses; the flash is the loudest thing the strip can do, so it is reserved for what can kill you.

The strip reads `PlayerStats.LastHazard` rather than re-sampling the field. Two independent samples of the same noise can disagree at a boundary, and a HUD that says SAFE while the damage path disagrees is worse than no HUD.

#### Also

Deaths from these hazards now name their cause: DIED OF RADIATION EXPOSURE, BURNED ALIVE IN A HEAT ZONE, BREATHED A TOXIC ATMOSPHERE.

No manual Unity step - this release is pure code, and it applies to worlds that already exist.

### [11.16.0-dev] Everything On One Sheet

**Type:** MINOR - a new system, save-compatible. Adds one keybind and bumps the settings version; no save data changes.

**GitHub title:** `[11.16.0-dev] Everything on one sheet`

The Map / Radar UI - section 6.4 Improved Features item 9. Press `L`.

#### The problem it solves

Rail, drones, logistic chests and roads were each built in their own version, and none of them could see the others. The player had four networks and no way to look at them together.

So the map's real job is not decoration - it is **finding the joins that are missing**. The station with no track beside it, the drone port with no power, the base zone nothing serves. Those go in a NEEDS ATTENTION section at the top of the sidebar, because on a mature base that list is the only reason to read the rest.

#### What it shows

| Layer | Drawn as |
|---|---|
| Rail lines | Solid edges walked from the rail graph |
| Rail stations | Squares, with LOAD / UNLOAD / NO TRACK |
| Trains | Filled diamonds - the only moving thing, so the easiest shape to pick out |
| Drone routes | Solid when a drone is flying, faint when merely paired |
| Drone ports | Dots, with READY / IN FLIGHT / NO POWER / DORMANT |
| Base zones | Circles inferred from logistic chest clusters |
| Roads | Faint dotted underlay |

Every layer toggles. A map that cannot be simplified is unreadable on a mature base.

#### Base zones are inferred, not authored

There is no "base" object in this game, so the map has to work one out. A cluster of logistic chests is the honest proxy: it is exactly the thing the logistics layer already treats as one place. Chests within 48 m of any member join the same zone - the same radius a drone port actually serves, so a circle on the map means the same thing as a zone the game genuinely serves rather than a decorative blob.

A single chest is not a base. Two or more is a place worth naming.

#### Scale and reading

Metres, top-down on XZ, same projection as the orbital map so the two read consistently - but deliberately a separate map. They answer different questions and share no scale; zooming one into the other is still open on the roadmap.

The map frames itself on the full extent of everything it found when it opens, so it never opens on empty space. The background grid picks a round spacing for the current zoom and labels it, so distance is readable without a legend. Clicking any sidebar entry centres it - on a large network, finding a named station by dragging is hopeless.

#### Implementation notes

- **`LogisticsMapData` is a separate gathering layer**, for the same reason `OrbitalTrackingService` exists: the painter runs inside `generateVisualContent` and must stay cheap, while gathering walks several registries and allocates. Split, the map repaints on pan and zoom without re-walking the world. Gathering runs on a 0.5 s tick, never per frame.
- **Edges are emitted from one end only.** Rail links and drone pairings are both symmetric, so without a hash tie-break every line draws twice.
- **Labels are pooled `Label` elements**, not `MeshGenerationContext.DrawText` - the lesson recorded when the orbital map shipped.
- Roads are drawn as per-cell marks rather than traced runs. The road layer has no edge list, and building one here would duplicate work the road system deliberately does not do.

#### Controls

`L` opens and closes the map, rebindable in Settings -> Controls. Settings version 18; the existing migration fills the new binding in on old profiles without touching anything the player rebound.

No manual Unity step - this release is pure code.

### [11.15.0-dev] The Permanent Way

**Type:** MINOR - a new system, save-compatible. All new save fields are additive; a world with no rail behaves exactly as before.

**GitHub title:** `[11.15.0-dev] The permanent way`

The Train System - section 6.4 item 1, and the last unbuilt entry in that block.

#### Why a railway is not just a road

The codebase already has belts for short haul, drones for point-to-point, and roads for driving. A railway had to be a fourth distinct answer, not a reskin of one of them, or it would not be worth building. The line that makes it distinct:

**A train is a scheduled agent that walks a graph, not a vehicle that drives on a surface.**

A road is queried by position - "what am I standing on". A rail is walked by topology - "what comes next". That one difference buys the property that makes bulk haul belong on rails: **a train keeps running while its chunks are unloaded**, because walking a graph costs nothing and needs no colliders. A rover cannot do that.

This is the same reasoning that put satellites on analytic orbits in 11.13.0. Anything the player expects to keep working while they are elsewhere must not depend on being simulated.

#### The permanent way

`RailTrack` is a `PlacedBlock`, so mining, damage, saves and the inspection overlay already work on it. It auto-connects to orthogonal neighbours like the road's neighbour mask, so the player lays cells and the line forms itself with no shape to pick.

Two rules stop it being a road with extra steps:

- **Gradient.** Rail refuses a slope a road would happily drape over. A railway that climbs anything is just an expensive road, so the refusal is what makes the player cut, fill and route around terrain. The rule lives in the graph itself: too-steep cells simply do not connect, rather than connecting and then failing mysteriously when a train tries it.
- **Degree.** Plain track holds at most two connections. Three or four requires a switch, and a buffer holds one. The network stays a set of lines rather than an undifferentiated mesh, which is what makes routing meaningful.

#### Pathfinding

`RailNetwork` is a hash-grid registry (mirroring `RoadSurfaceUtility`) plus A* over the cell graph - the roadmap's "A* for trains on rail graph" item.

A* rather than the road system's corridor solver because a railway is already a sparse graph with explicit edges, which is exactly the shape A* wants, whereas roads are a dense surface that must be traced first. Reusing the road planner would mean rediscovering topology the rail graph already knows.

Edge cost is real distance divided by the cell's speed multiplier, so the planner prefers good line over a marginally shorter bad one. The heuristic is straight-line distance, which is admissible because no edge is ever shorter than the gap it spans - so the result is a genuine shortest path, not merely a path.

#### Stations and schedules

A station owns the cargo hold, not the train. A train is in motion and often unloaded; a station is fixed and always addressable. Putting the buffer at the station lets the factory either side fill or drain it on its own schedule, so the train only has to show up - which is what makes a railway asynchronous instead of something the player babysits.

Stations are matched **by name, not by reference**, the convention the drone ports already set here. A schedule that names "North Pit" keeps working after the player demolishes and rebuilds that station.

| Role | Effect |
|---|---|
| LOAD | Station hold to train |
| UNLOAD | Train to station hold |
| PASSING | Timing point, no cargo moves |

Transfers put back anything the destination refuses, so a full train never destroys cargo. A dwell cap stops a jammed or mis-filtered station stranding a train forever on an order that can never complete.

#### Driving feel

Acceleration is deliberately low - mass is what a train is for. Arrival uses `v = sqrt(2as)`, the fastest speed from which the train can still stop in the distance remaining, so it brakes into a platform instead of overshooting it.

#### Switches

Right-click a switch to set the points. One detail worth calling out: a train arriving from the leg the points happen to be set to takes the next available route instead, so a switch can never bounce a train straight back the way it came.

#### Persistence

Station names, station roles and switch settings all survive a reload.

Switch settings are re-applied **one frame after load**, deliberately. A switch clamps its selection against its live link list, and that list is still filling while neighbouring track instantiates - applying immediately would clamp against a partial list and silently change the player's routing.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **85. Build the Rail System**.
3. Lay track between two sites, keeping the gradient gentle.
4. Place a station beside the track at each end. Right-click each to name it and set LOAD or UNLOAD.
5. Place a locomotive on the track. Right-click it, add both stops, press START SCHEDULE.

Right-click any station, train or switch to open its console.

### [11.14.0-dev] Reasons To Launch

**Type:** MINOR - new systems, save-compatible. All new save fields are additive; a world with no satellites behaves exactly as before.

**GitHub title:** `[11.14.0-dev] Reasons to launch`

Phase two of the Orbital Programme: the satellite payloads that make a launch worth doing, plus the UI change requested for the orbital map.

#### Orbital Systems is now its own box

The orbital map was sharing the LIFE SUPPORT card. It now has a dedicated ORBITAL SYSTEMS box directly below it, which is also more useful: instead of a single status line it shows the device name, its capability tier, its tracking range, and the open-map prompt. PERSONAL SYSTEMS reads 4 MODULES.

#### Satellite payloads

Three blocks, one component, escalating capability. Each requires the host construct to be classified a SATELLITE and committed to orbit - the same rule the research station uses, so there is only one requirement to learn for all orbital hardware.

| Payload | Capability | Idle | Active |
|---|---|---|---|
| Satellite Sensor Array | Planet-wide season telemetry | 120 W | - |
| Satellite Weather Radar | Adds live weather and forecast | 220 W | - |
| Satellite Climate Control Array | Adds weather influence | 260 W | 2660 W |

The sensor tier is the quiet but real win: season data for a planet **without standing on it**. Temperature, solar multiplier, wind multiplier, days remaining, next season, forecast.

The radar tier is honest about its limit. The weather simulation only runs for the body the player is actually at, so rather than fabricating a remote planet's live sky, the panel says exactly that and points at the season telemetry, which genuinely is planet-wide.

#### Weather influence, not weather command

Climate Control shifts the odds of the next weather change. It does not set the sky.

The hook is a single point: `PickNextState` is the one place `WeatherManager` chooses what comes next, so biasing the roll and scaling storm chance there covers every climate path - temperate, snow, and no-precipitation worlds - without touching the individual branches.

Two properties make this a system rather than a cheat:

- **Diminishing returns.** Satellite influence is combined through `1 - e^-total`, so each additional satellite adds less than the last and the total can never quite reach 1. A constellation steers a climate; it can never lock one. Every weather outcome stays reachable.
- **It costs.** An active climate array draws 2.66 kW. Steering an atmosphere should hurt.

Directives are MONITOR, SUPPRESS and ENCOURAGE, and the chosen directive is a standing order, so it persists through a save/load rather than quietly reverting.

#### New research

- **Climate Engineering** (tier 6) unlocks the climate array - and is itself flagged `requiresOrbitalLab`. You must already have a working satellite in orbit before you can research the ability to steer weather from orbit. This is the first shipped node that uses the orbital gate added last release, so the gate now has a real consumer rather than only a mechanism.
- The Sensor Array and Weather Radar ride along with **Orbital Science**, so getting a lab up also gets you something to point at the planet.

#### Block consoles

Both the payloads and the research station now have panels. When one is offline it states **which** requirement is missing (off / no power / not a satellite / not in orbit) and how to fix it, because a silent dead panel on a block whose entire purpose is its requirements would just read as a bug.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **84. Build the Orbital Programme** again - it is non-destructive and will add the three new payload blocks and the Climate Engineering node without touching anything authored.
3. Research Orbital Science, build a Sensor Array or Weather Radar onto a satellite, and commit it to orbit.
4. For weather control: put a Satellite Research Station in orbit first, research Climate Engineering there, then build the Climate Control Array.

### [11.13.1-dev] Compile Fixes And Conventional Keys

**Type:** PATCH - compile fixes and a keybind correction. No save impact.

**GitHub title:** `[11.13.1-dev] Compile fixes and conventional keys`

#### Fixed

- **`OrbitalTrackingService.cs` (158, 189) CS1061 - `SunSettings` has no `name`.** `SunSettings` is a plain serializable class, not a `ScriptableObject`, so it never had the implicit `.name` that Unity objects carry. The correct field is `displayName`. Both the sun's own map entry and the fallback parent name for planets now read it.

- **`GridIdentityHud.cs` (187-188) and `OrbitalMapScreen.cs` (228-229) CS0104 - ambiguous `Cursor`.** `UnityEngine.UIElements` declares its own `Cursor` type, so in a file that pulls in both namespaces the bare name is ambiguous.

  Fixed with `using Cursor = UnityEngine.Cursor;` rather than fully-qualifying each use site. This is not an arbitrary choice - it is the convention the codebase already uses: `InGamePauseMenu.cs` has had exactly this alias for the same reason. Matching it keeps the two files consistent with the existing pattern instead of introducing a second style.

#### Changed - keybinds

`N` is the conventional rename key and the construct registry is fundamentally a rename dialog, so the two have been swapped:

| Action | Was | Now |
|---|---|---|
| Construct registry (rename/classify) | `U` | **`N`**, and **`F2`** |
| Warp Drive | `N` | **`U`** |

`F2` opens the registry as well. It is deliberately a fixed convention rather than a second rebindable binding: F2-to-rename is a platform idiom on Windows and in most file managers and editors, not a user preference, so it should always work regardless of what the primary key is bound to.

Settings version bumped to 17 with a targeted migration. Profiles still sitting on the old defaults move to the new ones; a player who deliberately rebound either key keeps their choice, since the migration only rewrites a binding that still holds the exact previous default.

#### Manual step in Unity

None.

### [11.13.0-dev] Name Your Fleet, Own The Sky

**Type:** MINOR - new systems, save-compatible. Grids without an identity, and every legacy save, load and behave exactly as before. The keybind table gains two entries and migrates itself.

**GitHub title:** `[11.13.0-dev] Name your fleet, own the sky`

This is phase one of the Orbital Programme. It delivers the foundation: construct naming and classification, the orbital map device, on-rails orbits for stations and satellites, and the orbital research gate. Satellite sensor payloads (season and weather tracking, weather influence) follow in the next release, and they are built on exactly the layers added here.

#### Construct registry - naming and classification

Every grid can now be named and classified as a VESSEL, SATELLITE or STATION. Press `U` while piloting.

A satellite is not a separate entity type, as requested: it is an ordinary player-built grid wearing a different label. That declaration is what the rest of the systems key off.

Naming lives in its own `GridIdentity` component rather than as fields on `GridEntity`, for three reasons: the physics class does not grow a UI concern, a grid that was never named costs nothing at all, and an unnamed grid still reports a stable generated designation (`LC-4471`) so the map never draws a blank label. A static registry keeps the map and the satellite services off `FindObjectsByType`.

#### The orbital map

`M` opens a full-system view of every celestial body and every named construct, with live telemetry: apoapsis, periapsis, orbital period, inclination, and a plain-language state word (ORBITING, DRIFTING, SUBORBITAL, ESCAPING, IN FLIGHT).

**The map is a device, not a menu.** It requires an Orbital Map equipped in the new personal instrument slot, which sits in the LIFE SUPPORT card next to the helmet and oxygen tank as requested. The item is expensive (12 steel + 24 copper wire at the Assembler) and gated behind the Orbital Telemetry research node, so the first one is a real milestone.

The device also defines how good the map is - tracking range, whether full telemetry shows, whether orbit ellipses draw, whether focus switching is allowed. That gives the tier ladder somewhere to go later without any new UI.

Rendering notes:

- Orbits and bodies are drawn by a single `generateVisualContent` painter, one mesh per frame, so a system with dozens of tracked objects stays cheap.
- Ellipses are offset by their focal distance so the parent body sits at a **focus** of the ellipse rather than its centre. That is the visual signature of a real orbit and the main reason the view reads like a map instead of a diagram.
- Name labels are pooled `Label` elements in an overlay rather than `MeshGenerationContext.DrawText`, which needs a font resolved at paint time and is not dependable across Unity versions.
- Out-of-range contacts are listed and flagged rather than hidden, so the player can see that a better instrument would reach them.

#### On-rails orbits

A construct classified as a satellite or station can be committed to orbit from the registry panel.

**A rigidbody cannot hold an orbit.** Float precision, a fixed timestep and the fact that an unloaded chunk stops simulating all mean a physics-integrated satellite will decay, drift, or simply stop existing while the player is away. That is fatal for a feature whose whole promise is "put a station up and it stays there".

So a committed construct switches to the same Keplerian propagation the planets and moons already use. It becomes kinematic and is placed from solved elements each frame. It cannot decay, it keeps its orbit while streamed out, and the map can draw an exact ellipse instead of guessing.

Commitment is validated rather than forced. The game refuses to freeze a construct that is inside an atmosphere, below a minimum altitude, on a suborbital path, or already escaping - and says which, instead of silently circularising and stealing the player's intent. Any release hands the construct back to physics **with the exact velocity its orbit implies**, so leaving orbit is continuous rather than a dead stop.

The state-vector to classical-elements conversion handles the degenerate cases properly: equatorial orbits (undefined ascending node) and circular orbits (undefined periapsis) are both special-cased rather than producing NaN. The mean anomaly is rewound to the simulation epoch on commit, without which the station would visibly jump the moment it was committed.

#### Orbital research gate

Research nodes gain a `requiresOrbitalLab` flag. A flagged node can only be researched at a **Satellite Research Station** aboard a construct classified as a satellite and actually in orbit.

That single rule is what turns the orbital programme from decoration into progression: to finish the tech tree you have to build a satellite, get it up, and keep it there.

The gate is enforced in `ResearchManager` on both entry paths - timed lab research and the instant inventory path - because a gate enforced in only one place is a gate that eventually leaks. When research is unavailable the game reports the blocker from the lab **closest to working**, so the player is told the last thing standing in their way ("Host satellite must be committed to a stable orbit") rather than an arbitrary complaint.

#### Persistence

Names, classifications and committed orbits all survive a save/load round trip.

An orbit is saved as its **Keplerian elements, not as a pose**. A station has to come back on the same orbit at the correct phase for the reload time, which a frozen position could never express. Orbits are restored after the grid's blocks, so the hull has its real mass and bounds before it is parked on rails.

All fields are additive. A save with no identity restores a componentless, unnamed grid exactly as before.

#### Keybinds

| Key | Action |
|---|---|
| `M` | Orbital map (requires the device) |
| `U` | Construct registry, while piloting |

`M` was reserved for the star map in the roadmap and this is that feature, so it takes the key. `U` was chosen after checking the table - `N` is the Warp Drive, `T` is Tool Cycle, `G` is Build Toggle Grid. Both are rebindable; settings version 16.

#### Manual step in Unity

1. Open **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **84. Build the Orbital Programme**.
3. Research **Orbital Telemetry**, craft an **Orbital Map**, equip it in the instrument slot in your LIFE SUPPORT panel.
4. Press `M`.
5. To put something up: build a grid, fly it to a stable orbit, press `U`, name it, classify it as SATELLITE or STATION, then COMMIT TO ORBIT.
6. To gate a research node behind orbit, tick **Requires Orbital Lab** on that node.

### [11.12.1-dev] Unity API Deprecations

**Type:** PATCH — compile fix and warning cleanup. No behaviour change, no save impact.

**GitHub title:** `[11.12.1-dev] Unity API deprecations`

#### Fixed

- **`TransmissionTower.cs` (141) CS0619 x2 — `Object.GetInstanceID()` is now an error.** The span cable used the two towers' instance ids to decide which end draws the shared line, so a pair is never drawn twice.

  Unity suggests `GetEntityId()`, but that only exists on the newest editor and swapping to it would break the project on any older version. The tie-break never actually needed an engine id — it only needs a value that is unique and stable per instance. It now uses a private monotonic serial assigned lazily on first use, which is version-proof and does the same job.

- **`DroneNetwork.cs` (48) and `LogisticsNetwork.cs` (58) CS0618 — `FindFirstObjectByType<T>()` is deprecated.** Both are singleton guards checking whether an instance already exists, so ordering is irrelevant and `FindAnyObjectByType<T>()` is the correct replacement (it is also the faster of the two).

These were the only three occurrences of either API in the codebase.

#### Manual step in Unity

None.

### [11.12.0-dev] Where You Will Actually Land

**Type:** MINOR — a new pilot system. Save-compatible: no block, item or save field changes. The keybind table gains one entry and migrates itself.

**GitHub title:** `[11.12.0-dev] Where you will actually land`

#### Why this round

Section 6.4 item 6, the Trajectory Camera, was the only entry in that block still marked PARTIAL. The cockpit already had an orbital flight computer reading out PE/AP, tangential and circular speed. What it could not tell you was the one thing a pilot actually wants on the way down: **where the ship touches the ground, and how hard**.

#### Two different questions, two different solvers

The existing `OrbitalTelemetry` solves a two-body conic analytically. That is correct for "what orbit am I on" and it stays exactly as it was.

The new `TrajectoryPredictor` answers a different question by forward-integrating the coast path and then probing the world:

- It steps the **same forces `GridEntity` applies in FixedUpdate** — scaled radial gravity on a planet, the flat-world fallback off one, and the atmospheric drag model using the identical block-count frontal-area estimate. The predicted curve therefore matches the flight the ship will actually have, instead of a vacuum parabola that lies inside an atmosphere.
- Semi-implicit Euler, 0.25 s steps, 45 s horizon. Same integrator family as the physics step, and unlike explicit Euler it does not spiral a circular orbit outwards.
- Each segment is raycast against the world, so the impact point is real terrain rather than an assumed sphere.

It never touches the rigidbody — it reads state, integrates a copy, and reports.

#### Performance

A coasting ship re-solves to the same curve every frame, so the solution is cached and only rebuilt when the motion actually changed. Velocity change is the dirty signal, because thrust, gravity turns and collisions all move the velocity. Hard floor of 0.05 s between solves, hard ceiling of 0.5 s before a refresh. One `LineRenderer` and one marker exist for the whole game, hidden rather than destroyed.

#### The overlay

Gated in three stages, in order: the toggle is on, the player is piloting, and the camera is in the **wide exterior view** — the second zoom-out. First-person and the tight chase view stay clean, which is what the roadmap asked for.

| Outcome | Line colour | HUD text |
|---|---|---|
| Impact | Red | `IMPACT IN 4.2s · 88 m/s` |
| Will orbit | Green | `PATH CLEAR · WILL ORBIT` |
| Leaving the well | Violet | `PATH CLEAR · LEAVING WELL` |
| Clear, still climbing | Blue | `PATH CLEAR` |

The line fades toward its far end, because the prediction is least trustworthy the further out it runs and should not claim equal confidence along its whole length. The impact marker is scaled by camera distance so it stays readable from 500 m up, and pulses so it reads as a warning rather than scenery.

While the path is on screen, the cockpit trajectory module swaps its apsis row for the plain-language impact readout — more useful than a second copy of the conic.

#### Two details that would otherwise have been bugs

- The path raycast **ignores the grid it belongs to** and the pilot. Without that filter every prediction reports an instant impact with the ship it is predicting for. Probing is also suppressed until the path has cleared the hull.
- The impact marker has **no collider** — a marker with one would be hit by the very raycasts that produced it.

#### Keybind

`J` toggles the trajectory camera, rebindable in Settings. The roadmap suggested `T`, but `T` is already Tool Cycle, so `J` was taken instead — free, and next to `K` for the Grid Inspector. The settings version bumped to 15, so existing profiles pick the new bind up automatically without losing custom binds.

#### Manual step in Unity

None. No prefab, item, recipe or research is involved — this is script-only and works as soon as it compiles.

To try it: sit in a cockpit, scroll out twice, and fly. Press `J` if you want it off.

### [11.11.0-dev] The Grid Reaches Out

**Type:** MINOR — a new power block. Save-compatible: nothing existing changes shape, and a world with no towers behaves exactly as before.

**GitHub title:** `[11.11.0-dev] The grid reaches out`

#### Why this round

Section 6.4 item 4, the last untouched entry in the Logistics 2.0 block: long-distance power poles.

Cables only link one grid step at a time. That is the right rule for a base — it keeps wiring readable and stops power tunnelling through walls — but it meant a remote site could only be powered by dragging a cable run across the world one block at a time. The drone ports made this worse rather than better: they link over 400 m and need power at BOTH ends, so the logistics reach had outgrown the power reach.

#### The tower

Two Transmission Towers within 128 m link automatically and carry the network between them. A remote outpost joins the home grid with two blocks instead of a few hundred cables.

| Property | Value |
|---|---|
| Span range | 128 m, tower to tower |
| Span capacity | 20 kW |
| Local tap | 4 m, picks up cables and machines at its own base |
| Max spans | 3 — two makes a line, three makes a junction |

#### How it fits the existing power layer

A tower is just a `PowerNode`. The power layer already merges everything reachable into one network and prices it by its weakest link, so a tower only has to do two things: be a node, and declare long edges. Generation, storage, bottleneck maths and every existing power readout keep working untouched.

The span is registered as a **manual link**, which is the mechanism the code already had for an intentional long-range edge. This matters: `CanLinkTo` short-circuits on a manual link before its distance and line-of-sight checks, so a span crosses 128 m of terrain without the tower having to opt out of the rules that keep ordinary cables honest.

The local tap is deliberately kept at 4 m rather than widening `connectRadius` to the span range. Widening it would make a tower hoover up every machine within 128 m, which is not what a pylon does.

The span is capacity-rated at 20 kW and takes part in the normal bottleneck rule, so a thin cable feeding the tower is still the limit. A tower line is wide, not infinite.

#### Visuals

A lattice pylon — four splayed legs, a mast, two cross-arms — with a hanging catenary cable drawn between spanned towers. Each pair is drawn by exactly one end, so the line is never doubled up.

#### Re-spanning

Placing or removing a tower re-spans the whole set, because a new tower can change which pairs are nearest and a removed one frees a slot on its partners. A re-entrancy guard makes a burst of towers streaming in cost one pass rather than one per tower.

#### Manual step in Unity

1. Open **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **83. Build the Transmission Tower**.
3. Crafted at the Crafting Bench from 8 steel ingots and 4 copper wire.
4. Place one near your generators and another within 128 m, at the remote site.
5. Run a short cable from each tower to the local machines. The two grids are now one.

### [11.10.0-dev] Landed, Not Teleported

**Type:** MINOR — chest weight limits, plus three fixes. Save-compatible: the weight field is additive and defaults to the world limit every chest already used.

**GitHub title:** `[11.10.0-dev] Landed, not teleported`

#### 1. Items arrived out of sync with the drone

The flight timer covers the whole ROUND TRIP, but the handover was wired to the end of it. So the drone reached the destination at the halfway mark, visibly released its crate, flew all the way home — and only then did the items appear. The transfer was correct; its timing was not.

Touchdown is now its own event. `TickFlight` reports the moment half the round trip has elapsed, which is exactly where the visual drone releases its crate, and the network hands the cargo over there. `BeginReturnLeg` then lets the drone fly home empty instead of ending the trip on the spot. A `CargoDelivered` flag makes the handover strictly once-only, and it is recomputed on load so a save taken after touchdown cannot deliver the same payload twice.

#### 2. The wireless panel duplicated itself

Adding a request appended a second WIRELESS LOGISTICS block below the first. The rebuild re-inserted the panel by remembered index, and that index was no longer valid once the tree had changed. The panel now lives in a fixed container that is cleared and refilled, so it cannot be inserted twice by construction.

#### 3. Drone-reachable items read as "none in range"

A request supplied by drone was reported as unavailable, because the readout only measured the 48 m wireless radius. A drone route is a supply line too. `AvailableByDroneFor` now totals what linked ports can reach, and such an item shows a blue dot and "N by drone" instead of an amber "none in range". Genuinely unreachable items are unchanged.

#### 4. Chests have weight limits

`ItemContainer` already enforced a weight limit; chests simply used the world default. The three logistic chests now carry their own, which gives them distinct roles beyond their slot counts:

| Chest | Limit | Why |
|---|---|---|
| Provider | 1200 kg | a loading bay — pipes fill it fast |
| Buffer | 900 kg | local working stock for an outpost |
| Requester | 600 kg | a delivery shelf at the point of use |

A limit of 0 means "use the world default", so every ordinary chest is untouched. Setup only fills an UNSET limit, so a value you tune by hand is never overwritten. The panel shows Load in kg with a bar that turns amber past 85% and red at FULL — which is what lets a chest that has stopped accepting deliveries explain itself instead of looking broken.

The limit is reapplied after a reload, since a restored container is a fresh object and would otherwise revert to the world default.

#### Manual step in Unity

Run **78. Build the Logistic Chests** again to stamp the weight limits onto the three prefabs. Chests already placed keep whatever their prefab gives them.

### [11.9.0-dev] Heavy Lift

**Type:** MINOR — drone upgrades and a new drone model, plus two persistence fixes. Save-compatible: every new saved field is additive and a world written before this round loads with sensible defaults.

**GitHub title:** `[11.9.0-dev] Heavy lift`

#### 1. Request lists were never saved

A requester came back from a reload asking for nothing. The cause was in the persistence layer, not the chest: both the capture and the restore went straight to `ItemPortRouting`, which knows about faces and filters but nothing about the wireless request list or a buffer's stock target. `Chest.CapturePortSnapshot` and `Chest.ApplyPortSnapshot` were written to carry both and were simply never called.

Persistence now prefers the chest's own capture and restore when the block has a `Chest`, falling back to the routing component for every other machine. Requests and buffer targets survive a reload.

#### 2. Drone ports had no persistence at all

Everything about a port reset on load: its name, its visual toggle, its tuning, its lifetime counters — and, far worse, **any flight in progress**. The cargo leaves the source chests at takeoff, so a save that caught a drone mid-air would have destroyed those items on reload.

Ports now save by position and re-bind by proximity, the same model the refuel pads use. The in-flight manifest is saved with them and the flight resumes exactly where it left off, so nothing is lost. Restore runs in two passes because a manifest can only be re-bound once every port exists.

#### 3. The drone looks the part

Rebuilt from the reference: a white composite fuselage with a raised carbon spine, **eight arms in a radial ring** each with a motor can and a two-blade rotor, twin landing skids on four legs, and a gimballed pod at the nose. The slung cargo crate still tints itself to what it is carrying and still disappears on the empty return leg. Still primitives, so no art asset is needed, and still collider-free.

#### 4. Upgradable drones

The ports take the **universal modules that already exist** (setup step 75), so there is no second upgrade economy to learn:

| Module | Header in the panel | Effect on the drone |
|---|---|---|
| Machine Speed Module | **DRONE UPGRADES** | flies faster (x1.25 per module) |
| Machine Efficiency Module | **DRONE UPGRADES** | carries more (x1.25 payload per module) |

Efficiency is inverted deliberately. On a machine it is a power multiplier below 1 (x0.8 = draws less); for a drone that reads as "each item costs you less to carry", so capacity scales by 1 divided by it. A x0.8 module gives x1.25 payload, which matches the speed module's feel rather than inventing a new curve.

Two slots per port, they stack, and the panel shows the upgraded figure with the base value beside it in green so the gain is visible. Round-trip time is priced from the upgraded speed and the payload from the upgraded capacity.

#### 5. The drone flies chest to chest

It flew port to port, which is not where the items are. The ports are the relay that makes the trip legal, but the cargo leaves a chest and arrives in a chest, so the drone visibly started and ended in the wrong place.

The network now reports which chest the payload was actually drawn from and predicts which chest will receive it, and the drone flies that route. The delivery target is a prediction only — the real destination is decided on landing, so if the situation has changed the items still go wherever they fit. The flight remains presentation-only in every case.

#### Manual step in Unity

No new setup step. If the upgrade modules are not in your world yet, run **75. Author Universal Machine Upgrade Modules** — the same modules the Electric Furnace and Oil Refinery use. Then right-click a Drone Port and drop them into the two DRONE UPGRADES slots.

### [11.8.0-dev] The Drone You Can Watch

**Type:** MINOR — a new visual system plus two bug fixes. Save-compatible.

**GitHub title:** `[11.8.0-dev] The drone you can watch`

#### 1. Filters did not refresh the wireless panel

Adding an item filter to a face updated that face's card and nothing else. The WIRELESS LOGISTICS box above it is built from the same state, so it kept showing stale numbers until the screen was closed and reopened.

The panel is now rebuilt in place whenever a face card changes, and the reverse is wired too: a request added or removed inside the logistics box restates every face card. The two halves of the screen can no longer disagree, and neither rebuild disturbs scroll position.

#### 2. The drone ports did not really have 400 m of range

They advertised 400 m, and the check was correct — but it could never be reached. The world streams: chunks are 32 m and the view distance is 6 to 8 of them, so anything past roughly 192 to 256 m is unloaded, and the blocks inside it are disabled. The port deregistered itself in `OnDisable`, so the far end of a long route simply vanished from the network well before 400 m.

Registration now lasts until the block is genuinely destroyed, so a route survives its far end being streamed out. Distance is measured from a remembered `NetworkPosition`, which stays meaningful while a port is unloaded.

What a streamed-out port cannot do is touch its chests, because they are not in memory — so it is reported as **dormant** rather than quietly dropped. The route is kept and resumes the moment the chunk loads. This is an honest limit of a streaming world rather than something a logistics system can paper over, and the panel now says so in plain words instead of leaving the player to guess.

#### 3. The transport drone

A visible drone now flies the route: it climbs away from the source, arcs to the destination carrying a crate tinted to its cargo, lands, and returns empty with its crate hidden. It is assembled from primitives, so it needs no art asset, and its colliders are stripped — it cannot bump the player or a vehicle.

It is deliberately **presentation only**. The delivery is already decided and paid for at dispatch: the cargo has left the source chests and the landing is on a timer. The drone reads the port's own `FlightProgress` rather than keeping a clock, so the model can never drift from the delivery. This matters — if the simulation waited on the model, a drone that clipped terrain or streamed out would strand real items.

Each port has its own **DRONE VISIBLE / DRONE HIDDEN** toggle in its panel, as asked. Switching it off changes nothing about the logistics.

#### 4. Unpowered links are named

A link that exists but cannot work was previously silent. The panel now reports "One linked port has no power. It cannot send or receive until it does.", pluralising properly when several are affected, and separately reports dormant ports.

#### Known limitation

`showDrone` and a renamed `portName` are runtime values and are **not yet persisted** — a reloaded world returns both to their defaults. The flight state is likewise not saved, so a drone airborne at save time completes on load rather than resuming mid-air. Worth fixing in a later round; called out here so it is not mistaken for a bug.

#### No Unity step

No setup step: recompile and the changes apply to ports already placed.

### [11.7.1-dev] The Range Readout Tells The Truth

**Type:** PATCH — fixes a misleading readout. No behaviour change to transfers, no save format change.

**GitHub title:** `[11.7.1-dev] The range readout tells the truth`

#### What was wrong

The WIRELESS LOGISTICS line on a logistic chest counted every provider and requester **in the world**, with no distance test at all. Walking a chest to the far side of the map still reported its partners as present, which read as "everything is in range" and made the 48 m limit look broken or ignored.

The limit was never actually broken. `FindNearestProviderWith` and `AvailableFor` both applied it correctly, so items only ever moved within 48 m — but the panel said otherwise, and the panel is what the player believes. The per-item "none in range" status was the only honest number on the screen.

#### The fix

`ProvidersInRangeOf` and `RequestersInRangeOf` are new, and they apply exactly the same distance and buffer-to-buffer rules as the fulfilment pass, so the readout and the transfer can no longer disagree. The panel now reports "2 providers · 1 requester **in range**", and turns amber when the count that matters for this chest's role is zero.

The world total is not discarded, just relabelled. When a chest has no partner in range but partners exist elsewhere, a second line says so and names the range — so "I built one but it is too far away" is distinguishable from "I never built one", and it points at the Drone Ports as the way to bridge the distance.

`ProviderCount` and `RequesterCount` keep their world-wide meaning and are now documented as such, with a warning not to use them for anything shown on a single chest's panel. That was the trap this bug fell into.

#### No Unity step

Recompile and reopen a chest panel.

### [11.7.0-dev] The Drone Port

**Type:** MINOR — a new block and a new transport layer. Save-compatible: nothing existing changes shape, and a world with no drone ports behaves exactly as before.

**GitHub title:** `[11.7.0-dev] The drone port`

#### Why this round

`LogisticsNetwork` answers a request instantly, but only within 48 m. That radius is deliberate — stock teleporting across a whole world would make distance meaningless — but it left the last open item in the section 6.4 logistics line: an outpost further away than that could not be supplied at all, so the player hand-carried everything.

A pair of Drone Ports bridges the gap without dissolving distance. Two ports link over 400 m and a drone physically flies the difference: it loads, takes real time in transit, lands, unloads and returns. Long-range supply costs power, a round trip and a pair of blocks instead of being free.

#### A bridge, not a third kind of storage

A Drone Port holds no inventory. It serves the logistic chests already within its own 48 m service radius, so a port is a bridge between two local networks and the player keeps using the one storage concept they already learned: a chest asks, the network answers.

The dispatch pass runs from the destination end and is demand-driven. A port looks at what the requesters around it still want, and — importantly — skips anything the local wireless network can already supply. A drone is only ever launched against a real shortfall, so stock that could have been delivered locally never takes a flight it did not need. The source is then the nearest linked port whose own providers hold the item.

#### Cargo is never destroyed

The payload is removed from the source chests at takeoff, so for the whole flight it exists in exactly one place. On landing it is offered to the destination's requesters, then to any local chest with room. If it still cannot be placed it is flown home and stored there. Only if BOTH ends are full does the drone hold the cargo, log a warning and retry every five seconds until space appears. There is no path on which items silently vanish.

#### Power

Idle 20 W, plus 140 W while a drone of that port is airborne, and the draw follows the flight state. Power gates dispatch only: a drone already in the air completes its trip, so a brown-out strands nothing permanently. Both ends must be powered for a launch, since the destination pays the cost of receiving.

#### Tuning

| Value | Default |
|---|---|
| Link range | 400 m |
| Service radius (chests served) | 48 m, matching the wireless network |
| Payload | 64 items per round trip |
| Drone speed | 18 m/s |
| Handling | 2 s at each end |
| Dispatch tick | every 2 s |

All of these are per-port fields, so a placed port can be tuned without touching code, and setup never resets an authored value.

#### Manual step in Unity

1. Open **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **82. Build the Drone Port**.
3. The block is crafted at the Assembler from 10 steel ingots, 4 circuits and 6 copper wire.
4. Build **two** ports, one at each site, within 400 m of each other, and give both power.
5. Put logistic chests within 48 m of each port: providers near the source, requesters near the destination.
6. Right-click a port to see its link count, the current flight with a progress bar, and lifetime trips and items delivered.

### [11.6.0-dev] The Buffer Chest

**Type:** MINOR — a new block and a new chest role. Save-compatible: the new lock mode and the new saved field are additive, and every existing chest keeps the role and the ports it already had.

**GitHub title:** `[11.6.0-dev] The buffer chest`

#### Why this round

The Logistic Chests line in section 6.4 had one item still open: the Buffer Chest, the hybrid of the other two. A Provider only gives and a Requester only takes, so a remote outpost had to reach across the whole base for every single item it consumed. A Buffer holds local working stock: it keeps itself topped up from the providers, and other requesters draw from it.

#### The third role

`PortLockMode` gains `Buffer`. It is the first role that is on the wireless network without having pinned faces, and that distinction is now explicit in the code rather than implied:

| | Wireless network | Item ports |
|---|---|---|
| **Provider** | hands stock OUT | INPUT — pinned |
| **Requester** | receives stock IN | OUTPUT — pinned |
| **Buffer** | both — receives from providers, supplies requesters | **free** — not pinned |

`Chest.IsDirectionPinned` is the new test for "are the faces locked", and it is false for a Buffer. Everything that used to read `portLock != Free` as "pinned" now asks this instead: `EnforcePortLock` leaves a Buffer's directions alone, the panel gives it the ordinary three-way face pill rather than the ON/OFF switch, and the port capability flags advertise both halves. A Buffer legitimately needs an input and an output, so pinning it would have made it useless.

`SuppliesNetwork` and `RequestsFromNetwork` replace the old two-way switch in the network's registration: a Buffer registers in both lists.

#### The stock target, and why it exists

A Buffer that requested without limit would simply drain every Provider it could reach, which is the failure mode this kind of block usually has. So a Buffer requests only up to `bufferStockTarget` (default 64) of each item, editable per chest in the panel. `Chest.ShortfallOf` returns what a chest still wants — unbounded for a Requester, the remaining gap to the target for a Buffer — and the network's transfer is capped by it.

Two buffers are also never allowed to stock each other. Without that rule a pair of buffers both requesting the same item would pass one stack back and forth forever, and a chain of them would drain the real providers unevenly. Buffers are stocked by true Providers only. The "in range" readout applies the same rule, so the number shown matches what will actually arrive.

#### Persistence

`bufferStockTarget` is player-editable at runtime, so it rides along on the port snapshot next to the request list. It is additive and a save written before this round has no value for it, which reads as zero and is explicitly ignored so the chest keeps its authored default rather than being silently zeroed.

#### Manual step in Unity

1. Open **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **78. Build the Logistic Chests**. It now authors three variants; the two existing ones are untouched beyond the usual non-destructive corrections.
3. The Buffer Chest is 36 slots, crafted at the Crafting Bench from 6 iron ingots, 4 copper ingots and 4 planks.
4. Place one, open it, click ITEM PORTS. It shows a violet BUFFER banner, a KEEP IN STOCK field, and a REQUESTS list.
5. Add a request and set the target. The chest fills to that number from providers in range and stops; other requesters can then draw from it.

### [11.5.2-dev] The Item Ports Panel Opens Again

**Type:** PATCH — fixes a regression introduced by 11.5.1-dev. No new systems, no save format change.

**GitHub title:** `[11.5.2-dev] The item ports panel opens again`

#### What broke

11.5.1-dev made the item-ports overlay rebuild its body in place so the REQUESTS list updates live. That rebuild opened with a guard that bailed out when the scroll view was not yet attached to a panel — sensible for a *re*build triggered from a scheduled callback, wrong for the *first* build, which runs while the overlay is still being assembled and has not been added to the root yet. The guard fired immediately, the body was never created, and the overlay opened as a title bar and a close button with nothing between them.

#### The fix

The first build now happens after the overlay is attached to the root, so the panel is live and anything the body schedules has somewhere to run. The bail-out guard is gone from the top of the rebuild: the body is always constructed, and only the scroll-offset restoration is conditional — it is skipped when the captured offset is zero, which is exactly the first-build case. The guard that remains is inside the scheduled callback, where an unattached panel really does mean "give up".

Live refresh and scroll preservation are unchanged: removing a request still updates the list immediately, and still does not scroll the panel anywhere.

### [11.5.1-dev] One Row Per Item, And The Request List Updates While You Watch

**Type:** PATCH — three bug fixes against 11.5.0-dev. No new systems, no save format change.

**GitHub title:** `[11.5.1-dev] One row per item, and the request list updates while you watch`

#### 1. Searching "iron ore" returned four Iron Ores

Only one of them worked. The cause is older than the logistic chests: `ItemDefinition` used to arrive pre-filled with `iron_ore` / `Iron Ore` as its default id and name, so any asset authored back then whose identity was never typed in kept those values and now genuinely claims to be iron ore. Three such assets survive in the project — the gravel item, the stationary radar beacon block and the fire igniter tool — and every item picker listed all four as indistinguishable rows. Picking the wrong one bound a request to gravel wearing the ore's name.

This is fixed from both ends.

The **data** is repaired by new setup step 81, which finds every asset holding the legacy id that is not the canonical ore and gives it an identity derived from its own asset name: `Item_Gravel` becomes `gravel` / "Gravel", `Block_StationaryRadarBeacon` becomes `stationary_radar_beacon` / "Stationary Radar Beacon". The same sweep repairs assets whose id was authored correctly but whose display name was left on the default, which is what made the iron and copper material assets read as ore. The canonical `Item_IronOre` and `Item_CopperOre` are never touched, nothing is deleted, no reference is repointed, and an asset that already carries its own authored identity is left alone. Safe to re-run.

The **UI** is hardened independently, so the pickers stay honest even if a duplicate id reappears later. The item catalogue behind every search box now collapses to one entry per id, and where several assets share an id it keeps the best-authored candidate: an asset whose own file name resolves to the id wins, having an icon helps, and a block or tool that merely inherited the id loses to a real resource item. So the ore beats its impostors even before step 81 is run.

Copper was checked for the same fault. Only the material asset was affected, and step 81 covers it.

#### 2. Removing a request did not refresh the panel

The REQUESTS list only redrew after the item-port screen was closed and reopened. The overlay built its body once and passed an empty `onChanged` callback, so every edit inside it correctly changed the chest and then told nobody. The callback now rebuilds the panel body in place.

Only the body is replaced — the card, the header and the scroll view itself survive the swap. **The scroll position is preserved across the rebuild** rather than snapping anywhere: the offset is captured before the swap and restated after layout resolves, so a long request list does not jump to the top, and it does not jump to the bottom either. Removing the fourth of ten requests leaves you looking at exactly where the fourth one was.

#### Manual step in Unity

1. Open **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Scroll to the bottom and click **81. Repair Stolen Item Identity**.
3. Read the Console. Every change logs with `[Setup 81]` and names the asset, its old identity and its new one.
4. If the dialog reports skipped assets, those could not be named automatically from their file name — give them an id by hand.
5. Open a Requester chest, click ITEM PORTS, then Request Item, and search "iron ore". One row, and it is the real ore.

### [11.5.0-dev] The Logistic Chests Point The Right Way, And Requests Stand Alone

**Type:** MINOR — corrects the 11.4.0-dev model. The port roles are inverted and the wireless request list becomes its own field with its own saved data. Save-compatible: the new field is additive and a pre-existing chest reads as requesting nothing.

**GitHub title:** `[11.5.0-dev] The logistic chests point the right way, and requests stand alone`

#### Why this round

11.2.0-dev pinned the ports the wrong way round, and 11.4.0-dev then built the wireless network on top of that mistake by reusing the port filters as the request list. Both are corrected here.

The item ports and the wireless network are **two different transports**, and a chest's role in one is the mirror of its role in the other:

| | Wireless network | Item ports |
|---|---|---|
| **Provider Chest** | hands stock OUT to requesters | **INPUT** — pipes and belts fill it |
| **Requester Chest** | receives stock IN from providers | **OUTPUT** — it feeds the pipes downstream |

A Provider has to be filled by something, and that something is a pipe or a belt — so its ports are inputs. A Requester exists to supply the machines past it, so its ports are outputs. Previously each was the reverse of what it needed to be, which made a Requester a dead end: the network filled it and nothing could get the items back out.

#### 1. The inversion

`Chest.PinnedDirection` is now the single place that decides a locked chest's face direction, and it returns Input for a Provider and Output for a Requester. Everything that consults it follows automatically: `EnforcePortLock`, the container capability flags in `GetPortContainers`, the legacy pipe API (`GetInputContainer` / `GetOutputContainer` / `HasOutputReady` / `CanAcceptInput`), and `TryAcceptFromPipe`, which now refuses a push into a Requester rather than into a Provider. Setup step 78 seeds the same direction, so a freshly authored prefab matches.

The port panel follows too: the face pill reads INPUT ON for a Provider and OUTPUT ON for a Requester, the banner explains the two-transport split in one line, and the round-robin toggle is hidden on a Provider (whose ports are inputs) rather than on a Requester.

#### 2. Requests are their own list

The request list is no longer scraped from the port whitelists. `Chest` carries a dedicated `_requests` list with `AddRequest` / `RemoveRequest` / `SetRequests`, and `LogisticsNetwork` reads exactly that. The two are now cleanly separated:

- **Port filter** — what may leave this chest down a pipe.
- **Request list** — what the network delivers into it.

Reusing the filters conflated those, and with the ports inverted it became impossible to express: a Requester's faces are outputs, so its filters govern departures and could never have described what it wants delivered.

#### 3. The same picker, a new place

The panel's REQUESTS section lists each requested item with a live "N in range" or "none in range" readout and a remove button, plus a "＋ Request Item" button. That button opens `ItemFilterDialog.OpenList`, a new overload of the dialog the port filters already use — same search box, same inventory-click capture, same chips. The player learns one picker and uses it for both jobs.

#### 4. Requests survive a save

`ItemPortSnapshot` gains `requestItemIds`, written and restored by the chest's existing snapshot bridge. It is additive: a save written before this round has no list, which restores as an empty request list — the correct default. No new save plumbing and no schema break.

#### What to run

1. Replace `Scripts/Building/Chest.cs`, `Scripts/UI/PortConfigHud.cs`, `Scripts/UI/ItemFilterDialog.cs`, `Scripts/Transport/LogisticsNetwork.cs`, `Scripts/Transport/ItemPortRouting.cs`, `Scripts/Transport/IItemPortHost.cs` and `Scripts/Editor/LogisticChestsSetup.cs`. Let Unity compile.
2. Re-run **step 78** so the two prefabs seed their faces in the corrected direction. Chests already placed in the world re-pin themselves on load.
3. Open a Provider Chest: the banner should say pipes fill it, and its faces should read INPUT ON. Run a belt into it and confirm items arrive.
4. Open a Requester Chest: its faces should read OUTPUT ON, and it should have a REQUESTS section. Add Iron Ingot there — not in the port filter.
5. Stock the Provider with iron ingots within 48 m. The Requester's entry should go green with a count, and ingots should arrive in batches of 16 once a second.
6. Run a pipe out of the Requester into a machine or chest: the delivered ingots should flow onward. That is the loop that was impossible before this round.
7. Save and reload: the requests, the locks and the deliveries all resume.

### [11.4.0-dev] The Chests Talk To Each Other: Wireless Request and Fulfilment

**Type:** MINOR — new system, save-compatible. One new runtime component, a registration hook on the chest, and a new panel section. No new asset, no setup step, no saved field: the request list is the per-face item filter that already saves and restores.

**GitHub title:** `[11.4.0-dev] The chests talk to each other — wireless request and fulfilment`

#### Why this round

11.2.0-dev shipped the Provider and Requester chests and the roadmap recorded exactly one thing still open on the storage line: "automated request/fulfilment routing between logistic chests (a network-level feature, not a block)". Until now the two locks only described intent — a Requester across the base from a Provider stayed empty unless the player ran a pipe between them, which is precisely the spaghetti the logistic chests exist to remove.

#### 1. The network

New `VoxelEngine.Transport.LogisticsNetwork`, a singleton that runs one fulfilment pass per second. A locked chest registers itself on enable and drops out on disable, so the network holds only logistic chests and a free chest costs it nothing. Each pass walks the requesters and, for every item a requester asks for, finds the nearest in-range provider holding it and moves up to 16 units.

| Rule | Value |
|---|---|
| Pass interval | 1 s |
| Reach | 48 m |
| Items per item-type per pass | 16 |

The metering matters: a large provider drains into a requester smoothly over several seconds rather than teleporting its whole contents in a single frame, which reads as a supply line rather than a save-load.

#### 2. What a Requester asks for

The request list is the union of the per-face **whitelists** already on the chest — no new field and no new UI to learn. A face with no filter is deliberately ignored rather than treated as "send me anything": an unconfigured chest should sit quiet instead of hoovering up the base. The player opts in by naming items in the filter, and because those filters are already part of the port snapshot, a chest's requests survive a save and reload with no schema change.

#### 3. Transfers are honest

Two traps the ore round taught us are handled explicitly. The network counts and moves the **provider's own item instance** rather than the definition the requester asked with — `ItemContainer.Remove` compares by reference, so handing it a different asset that merely means the same thing would remove nothing while still inserting into the requester, duplicating stock. And the removal is driven by what the destination actually **accepted**, so a full requester can never make items vanish.

#### 4. The panel shows the network

A locked chest's port panel gains a WIRELESS LOGISTICS section: the reach, how many providers and requesters are on the network, and — for a Requester — every item it asks for with a green dot and a live count when the network can supply it, or an amber "none in range" when it cannot. An unfulfilled request now reads as a stock problem the player can go solve, instead of looking like a broken block. A free chest's panel is unchanged.

#### What to run

1. Add `Scripts/Transport/LogisticsNetwork.cs` (new file) and replace `Scripts/Building/Chest.cs` and `Scripts/UI/PortConfigHud.cs`. Let Unity compile. There is no setup step — the network creates itself when the first logistic chest wakes.
2. Place a Provider Chest and put iron ingots in it. Place a Requester Chest within 48 m.
3. Open the Requester, switch a face on, and add Iron Ingot to that face's filter. Its panel should list Iron Ingot with a green dot and the count available.
4. Close the panel and wait. Ingots should arrive in the Requester in batches of 16, once a second, until the provider is empty or the requester is full.
5. Move the Requester out past 48 m: the entry should flip to "none in range" and deliveries should stop.
6. Save and reload with a stocked pair in place: the filters, the locks and the deliveries all resume.

### [11.3.0-dev] One Ore To Smelt: Iron Ore and Copper Ore Become Canonical

**Type:** MINOR — content consolidation, save-compatible. Two duplicate item assets are retired, every reference is repointed, and existing saves are bridged by an id alias so no stack is lost. Two new setup steps (80 and the 79 audit from 11.2.2-dev). No schema change.

**GitHub title:** `[11.3.0-dev] One ore to smelt — Iron Ore and Copper Ore become canonical`

#### Why this round

11.2.2-dev made the furnaces tolerate the duplicate ore assets, but tolerating a duplicate is not the same as not having one — and the wrong asset was winning. The furnaces accepted the bare `Items/Item_Iron` ("iron"), while the ore the player actually mines and sees in the UI is the Resource-typed `Industrial/Items/Item_IronOre` ("iron_ore") with the real icon, the Resource category and the proper description. This round picks the right one and deletes the other.

| Logical item | Survives | Retired |
|---|---|---|
| Iron Ore | `Industrial/Items/Item_IronOre` (`iron_ore`) | `Items/Item_Iron` (`iron`) |
| Copper Ore | `Industrial/Items/Item_CopperOre` (`copper_ore`) | `Items/Item_Copper` (`copper`) |

#### 1. The item id no longer defaults to iron ore

`ItemDefinition.itemId` and `displayName` defaulted to `"iron_ore"` / `"Iron Ore"`. That is the root cause behind the whole class of bug: every asset whose id was never authored silently claimed to BE iron ore, which is how a gravel item, a radar beacon block and a fire igniter tool all ended up holding that id. Both fields now default to blank — an obviously unset value that can be detected and repaired rather than quietly colliding with real content.

`ItemIdentity` follows: a blank id carries no identity, so such an asset is only ever equal to itself by reference. The old default is kept as `ItemIdentity.LegacyDefaultId` purely so step 79 can still recognise assets serialized before this change.

#### 2. Setup step 80 — Consolidate the Ore Items

New wizard button 80, implemented in `OreConsolidationSetup`. Per ore it repoints every reference to the retired duplicate and then deletes it:

- **Smelting recipes** — `Smelt_Iron`, `Smelt_Copper` and `Smelt_Steel` now take the canonical ore.
- **Crafting and machine recipes** — every input, output and byproduct, including the crusher's ore recipes.
- **The voxel material drop** — `Mat_Iron` and `Mat_Copper` now drop the canonical ore, so mining yields the item the recipes expect. This is the reference that decides what ends up in the player's hands.
- **The persistence catalogue** — the retired asset is removed and the canonical one added, so saved stacks resolve at load.

The step resolves the whole plan before writing anything and refuses to run if a canonical asset is missing. Re-running it is a no-op that reports both ores already consolidated.

#### 3. Old saves keep their ore

Deleting an asset a save refers to would normally lose the stack. New `ItemIdAliases` maps each retired id to its replacement, and `WorldStatePersistence` consults it after building the item cache — so a saved stack of `iron` loads back as Iron Ore. An alias never shadows a live id, so an asset re-introduced under a retired id takes precedence again.

#### 4. The setup no longer recreates the duplicates

Step 1 authored `Item_Iron` and `Item_Copper` from the voxel material table, so re-running it would have resurrected exactly what step 80 deletes. It now adopts the canonical Industrial asset for those two materials instead of authoring its own, leaving the icon and category intact. Steps 4, 10 and the research step resolve ore through one shared `LoadCanonicalOre` helper, so the whole project points at one asset per ore.

#### What to run

1. Add `Scripts/Items/ItemIdAliases.cs` and `Scripts/Editor/OreConsolidationSetup.cs` (new files) and replace `Scripts/Items/ItemDefinition.cs`, `Scripts/Items/ItemIdentity.cs`, `Scripts/Editor/ItemIdentityAuditSetup.cs`, `Scripts/Persistence/WorldStatePersistence.cs` and `Scripts/Editor/VoxelEngineSetupWindow.cs`. Let Unity compile.
2. Run **step 80 (Consolidate the Ore Items)**. The console logs every repointed reference and the two deletions.
3. Run **step 79 (Audit and Repair Item Identity)** afterwards to give the remaining unauthored assets their own ids and list any duplicates left.
4. Mine iron ore and copper ore. Each should stack as the icon-bearing Iron Ore / Copper Ore, and smelt in both the Solid Fuel Furnace and the Electric Furnace.
5. Load a save made before this round that held the old ore: the stacks should come back as the canonical ore rather than vanishing.

### [11.2.2-dev] The Furnaces Recognise Their Own Ore

**Type:** PATCH — bug fix. No save schema change, no API removal. One new shared helper, three machines switched to it, and one new audit step (79).

**GitHub title:** `[11.2.2-dev] The furnaces recognise their own ore`

#### The report

Both the Solid Fuel Furnace and the Electric Furnace refused iron and copper ore, reporting "Iron Ore cannot be smelt in this machine" with the ore sitting in the input slot.

#### What was actually wrong

The recipes were fine. `Smelt_Iron` and `Smelt_Copper` are authored, linked, assigned to both furnace prefabs, and point at real ingots. The fault was that the project contains **two different assets for the same ore**:

| Logical item | Asset | itemId |
|---|---|---|
| Iron Ore | `Items/Item_Iron.asset` | `iron` |
| Iron Ore | `Industrial/Items/Item_IronOre.asset` | `iron_ore` |
| Copper Ore | `Items/Item_Copper.asset` | `copper` |
| Copper Ore | `Industrial/Items/Item_CopperOre.asset` | `copper_ore` |

The smelting recipes point at the `Items/` pair. The persistence catalogue that repopulates the player's inventory on load carries the `Industrial/` pair. Both display "Iron Ore", so they are indistinguishable in the UI — but `FindRecipeForInput` compared them with `==`, which is reference equality on a ScriptableObject. Ore that arrived through the catalogue could never satisfy a recipe pointing at the other asset, so the furnace correctly concluded it had no recipe and said so.

A second, quieter fault made this hard to see. `ItemDefinition.itemId` defaults to the literal string `"iron_ore"`, so every asset whose id was never authored silently claims to be iron ore. Four assets are in that state: the real Industrial iron ore, plus a gravel item, a radar beacon block and a fire igniter tool.

#### 1. Identity instead of reference equality

New `VoxelEngine.Items.ItemIdentity` answers one question — are these two references the same item? Reference equality first, which is the fast exact path and covers every normal case. When that fails, a case-insensitive `itemId` match. The fallback deliberately rejects the unauthored default id, so a mis-authored tool can never masquerade as ore.

`Furnace`, `ElectricFurnace` and `GridElectricFurnace` now route their recipe matching, smeltable test and auto-pull top-up check through it. Nothing else changes: a recipe that matched before still matches, by the same fast path it always took.

#### 2. Setup step 79 — Audit and Repair Item Identity (Non-Destructive)

New wizard button 79, implemented in `ItemIdentityAuditSetup`. It scans every `ItemDefinition` under `VoxelEngineAssets`, gives each asset still carrying the unauthored default an id derived from its own file name, and then reports every id still claimed by more than one asset.

The one asset whose own name resolves to `iron_ore` — the real `Item_IronOre` — keeps it; the three impostors become `gravel`, `stationary_radar_beacon` and `fire_igniter`. An asset that already carries an authored id is never rewritten, and no field but `itemId` is ever touched, so running it twice is a no-op.

The remaining duplicates (the ore pairs, several nuclear items, the farming and survival food sets) are logged as warnings rather than merged: which of two assets should survive is a content decision. The furnaces now treat them as the same item either way, so smelting works regardless.

#### What to run

1. Add `Scripts/Items/ItemIdentity.cs` and `Scripts/Editor/ItemIdentityAuditSetup.cs` (new files) and replace `Scripts/Crafting/Furnace.cs`, `Scripts/Crafting/ElectricFurnace.cs`, `Scripts/GridSystem/GridElectricFurnace.cs` and `Scripts/Editor/VoxelEngineSetupWindow.cs`. Let Unity compile.
2. Put iron ore into a Solid Fuel Furnace with coal: it should smelt to an Iron Ingot. Repeat with copper ore, and with both in an Electric Furnace on a powered network.
3. Optionally run **step 79** and read the console: it reports what it repaired and lists every id still shared by two assets.

### [11.2.0-dev] The Storage Line Closes: Provider and Requester Chests

**Type:** MINOR — new content plus one new save-compatible field, no schema break. Two new storage blocks on the existing Chest component, a `portLock` field that defaults to the current behaviour, a port panel that adapts to a locked host, and one new setup step (78). Existing chests, their saves and their port snapshots are unaffected.

**GitHub title:** `[11.2.0-dev] The storage line closes — Provider and Requester chests`

#### Why this round

The roadmap's storage line has named one remaining gap since 11.1.0-dev: "only the Provider/Requester (port-locked) end of the progression remains". A plain chest can be wired either way on every face, which is flexible but means a logistics bus has no block that is guaranteed to only supply, or only receive — one mis-clicked face turns a supply buffer into a sink and quietly drains the line.

#### 1. A lock on the chest that already works

Rather than a new component, the two variants are the same `VoxelEngine.Building.Chest` with one new field, `portLock`:

| Variant | Slots | Lock | Recipe | Station |
|---|---|---|---|---|
| Provider Chest | 18 | every active face is an OUTPUT | Iron Ingot x4 + Copper Ingot x2 + Wooden Plank x2 | Crafting Bench, 3 s |
| Requester Chest | 18 | every active face is an INPUT | Iron Ingot x4 + Copper Ingot x2 + Wooden Plank x2 | Crafting Bench, 3 s |

The lock is enforced in three places so no path can contradict it. `EnforcePortLock` re-pins every active face on Awake and again after a port snapshot is restored, so an older save carrying free directions is corrected on load. `GetPortContainers` advertises only the half the lock allows, so the shared routing layer refuses the wrong transfer at the source. The legacy pipe API answers to match — a Provider returns no input container and rejects a pipe push outright; a Requester reports no output ready. `portLock` defaults to `Free`, which is exactly today's behaviour, so every existing chest and tier is untouched.

#### 2. The panel says what the block is

`PortConfigHud` now recognises a host that implements the new `IPortLockedHost` interface. A locked chest's panel gains a banner — an amber PROVIDER or blue REQUESTER badge with the rule in one line — and each face card's pill becomes a clean ON/OFF switch reading "OUTPUT ON" / "INPUT ON" / "OFF" instead of a three-way cycle, because the direction is not the player's to choose. The round-robin / priority toggle is hidden on a Requester, where it means nothing. Filters, per-face item whitelists and the two-column card grid all behave exactly as before. A free host renders the panel unchanged, down to the hint text.

#### 3. Setup step 78 — Build the Logistic Chests (Non-Destructive)

New wizard button 78 in `Tools > Voxel Engine > Voxel Engine Setup`, implemented in `LogisticChestsSetup`. Per variant it creates or repairs the prefab, the block item and the recipe, following the same contract as step 77: missing assets are authored, and an existing asset is only corrected where it cannot be right — a missing Chest component, a wrong slot count, display name or lock mode, a null recipe output or ingredient, a missing registry membership. Authored craft times, quantities, health and icons are never reset.

The one addition over step 77 is face seeding: a locked prefab with no active face at all gets its +X face switched on in the pinned direction, so the block is useful the moment it is placed. A prefab that already has an authored face layout keeps it — only a face pointing against the lock is re-pinned. The step refuses to run without step 4's content (plank / iron ingot / copper ingot must resolve) and logs every decision with the `[Setup 78]` prefix.

#### What to run

1. Add `Scripts/Editor/LogisticChestsSetup.cs` (new file) and replace `Scripts/Transport/IItemPortHost.cs`, `Scripts/Building/Chest.cs`, `Scripts/UI/PortConfigHud.cs` and `Scripts/Editor/VoxelEngineSetupWindow.cs`. Let Unity compile; the console should be clean.
2. Open `Tools > Voxel Engine > Voxel Engine Setup` and run **step 78 (Build the Logistic Chests)**.
3. Craft a Provider Chest at a Crafting Bench and place it. Right-click it: the panel should read "Provider Chest" with the amber PROVIDER banner, and each face pill should toggle between "OUTPUT ON" and "OFF" only.
4. Run a pipe into the Provider Chest from a full source: nothing should enter it. Run a pipe out of it into a Requester Chest: items should move one way only.
5. Open a plain Chest and a Steel Chest to confirm they still cycle None to Input to Output with no banner.
6. Fill a Provider Chest, switch a face off, save and reload: the items, the face state and the lock all come back.
7. Re-run step 78: the console should report "both variants already present and correct, nothing written."

### [11.1.1-dev] The Chest Tier Setup Compiles Again

**Type:** PATCH — compile fixes only. No behaviour change, no save touch, no API change.

**GitHub title:** `[11.1.1-dev] The chest tier setup compiles again`

Two compiler errors in `StorageChestTiersSetup.cs` shipped with 11.1.0-dev and blocked the whole editor assembly, so step 77 could not be run at all:

- **CS0136 at line 193** — the local `chest` declared while building a new tier prefab collided with the `chest` used in the repair branch further down the same method. The creation-path local is renamed `newChest`; the repair path is unchanged.
- **CS0019 at line 328** — `RecipeIngredient` is a struct, so `recipe.inputs[i] == null` is not a legal comparison. The null test is dropped; the remaining `item == null || count <= 0` check already covers every broken-ingredient case the guard was written for, so the repair logic behaves identically.

### [11.1.0-dev] The Chest Progression Lands: Wooden Crate, Iron Chest, Steel Chest

**Type:** MINOR — new content, save-compatible. Three new storage blocks on the existing Chest component, three new recipes, and one new setup step (77). Nothing about existing chests, their saves, or the port system changes; the pre-existing 30-slot Chest is never touched.

**GitHub title:** `[11.1.0-dev] The chest progression lands — Wooden Crate, Iron Chest, Steel Chest`

#### Why this round

The roadmap's storage line has sat at PARTIALLY COMPLETE with one gap named: "the planned Wooden Crate → Iron Chest → Steel Chest → Provider/Requester progression is not complete". The game ships a single 30-slot chest for planks x8, so early game has no cheap overflow storage and end game has no place to put the ingots the smelters just learned to make in volume.

#### 1. Three tiers on the chest that already works

Instead of a new component, the tiers are three more authored blocks on the SAME `VoxelEngine.Building.Chest` component, which already carries the port configuration, the belt/pipe plumbing, and the save/restore. A tier is just a slot count, a display name, and a recipe:

| Tier | Slots | Recipe | Station | Craft time |
|---|---|---|---|---|
| Wooden Crate | 9 | Wooden Plank x4 | Inventory | instant |
| Iron Chest | 18 | Iron Ingot x4 + Wooden Plank x2 | Crafting Bench | 2 s |
| Steel Chest | 36 | Steel Ingot x4 + Iron Ingot x4 | Assembler | 4 s |

Because the component is the one the game already knows, every surrounding system picks the tiers up with no code change: right-click opens the panel and the panel title comes from the component's `displayName`, `WorldStatePersistence` saves and restores the container plus its port snapshot by component lookup, and the item-port grid (per-face direction and filters) renders as it does for the original chest. The Wooden Crate's inventory-tier recipe means a player with four planks can build overflow storage before they have a bench.

#### 2. Setup step 77 — Build the Storage Chest Tiers (Non-Destructive)

New wizard button 77 in `Tools > Voxel Engine > Voxel Engine Setup`, implemented in `StorageChestTiersSetup`. Per tier it creates, or repairs, three assets:

- **The prefab** (`StationPrefabs/<Tier>.prefab`): a box mesh on a `Chest` component with the tier's slot count and display name. An existing prefab is only saved when something is actually wrong — a missing Chest component, a wrong slot count, or a wrong display name is corrected; every other authored property is left alone.
- **The block item** (`Blocks/Block_<Tier>.asset`): placeable, Storage category, the placed-prefab reference pointed at the verified prefab. Authored quantities, health, mining tier and icons are kept; only missing metadata and a broken placed-prefab link are repaired.
- **The recipe** (`Recipes/Recipe_<Tier>.asset`): registered in the existing `RecipeRegistry`. When the recipe already exists its quantities and craft time are preserved — only a null output or ingredient link (the same failure mode that broke the smelting recipes in 11.0.0-dev) is re-linked, and a missing registry membership is restored. When it doesn't exist, it is authored from the tier table above.

The step refuses to run without step 4's content (plank / iron ingot / steel ingot must resolve) and logs every decision with the `[Setup 77]` prefix. Icons bind through the usual icon sync — new items get their `ItemIcons` PNG the next editor session, and until then they render their tint fallback exactly like any other un-iconed item.

#### 3. What this round does NOT do

The Provider/Requester end of the roadmap line stays open: that is a port-locked variant (a chest whose faces are fixed to output-only or input-only) and needs a field on the component plus the port UI to honour it, which is its own round. This round delivers the three storage tiers the line names first.

#### What to run

1. Replace or add `Scripts/Editor/StorageChestTiersSetup.cs` (new file) and `Scripts/Editor/VoxelEngineSetupWindow.cs` (one new wizard button). Let Unity compile; the console should be clean.
2. Open `Tools > Voxel Engine > Voxel Engine Setup` and run **step 77 (Build the Storage Chest Tiers)**. The dialog lists the three tiers; the console logs every create/repair with the `[Setup 77]` prefix.
3. Craft a Wooden Crate (planks x4, straight from the inventory crafting list) and place it: the panel should read "Wooden Crate" with 9 slots. Craft an Iron Chest at a Crafting Bench and a Steel Chest at an Assembler to verify the other two tiers and their slot counts.
4. Re-run step 77 once more: the console should report "all three tiers already present and correct, nothing written." That is the non-destructive contract.
5. Place a tier chest, fill it, save and reload: the items and any per-face port configuration come back.

### [11.0.1-dev] The Furnaces Say Why They Stand Still, and the Well Sees Past Its Own Derrick

**Type:** PATCH — bug fixes only. No save changes, no API removals: the furnace panels now surface the stall reasons 11.0.0-dev already computes and logs, and the Jack Pump's well probe no longer stops at the machine's own colliders.

**GitHub title:** `[11.0.1-dev] The furnaces say why they stand still, and the well sees past its own derrick`

#### Why this round

Test feedback: an item in the input, coal in the fuel, nothing happens — and the furnace panel says "No input", which is not true; the electric furnace reads the same way, and a Jack Pump placed into a base does not pump.

#### 1. The furnace panel showed "No input" for every stall that was not "no input"

`StallReason` arrived in 11.0.0-dev — No power, No input, No recipe for this input, No fuel, Fuel slot holds something that does not burn, Output full, Switched off — and the log half was wired: each change is reported once with the reason and the state of the recipe links. The panel half never was. `TickFurnaceLiveUI` still carried the old label:

```csharp
_liveSmeltLabel.text = f.Current != null ? $"{...}% smelted" : "No input";
```

`Current` is null in every stalled case, so a furnace holding ore with broken smelt links, a furnace out of fuel, and a furnace with an empty input slot all read "No input". A player who put ore in the slot watched the machine claim the slot was empty, with no way to tell which repair path applied.

Now:

- **The smelt label carries the actual `StallReason`** (it wraps to two lines when the reason is long). "No input" appears only when the input slot really is empty.
- **The header pill carries the short form of the same reason** — NO POWER / NO INPUT / NO RECIPE / NO FUEL / BAD FUEL / OUTPUT FULL / SWITCHED OFF — red for faults the player has to fix, amber for states that are merely waiting.
- **A one-line hint under the smelt bar for the "No recipe for this input" case.** When one of the assigned recipes lost its input or output link (new public `HasBrokenRecipes` on both static furnaces), the hint names the exact step to re-run: **step 4 (Build Crafting Content)** on the fuel furnace, **steps 4 and 10** on the electric furnace (which also carries the glass recipe). When the links are sound, the hint says which item in the slot cannot be smelt in that machine.
- **First paint uses the same rule as the per-frame tick**, so the panel never flashes a stale IDLE before the reason arrives.

Nothing about the matching, fuel, or batch logic changed: `StallReason` is read, not rewritten, and a furnace with sound recipes and ore in it smelts exactly as before. The 11.0.0-dev console line is unchanged and remains the definitive report for anything the panel cannot show.

#### 2. The well probe stopped at the derrick's own collider

`Pumpjack.DetectReservoir` cast one raycast per probe column and took the first hit. The first hit is often not the world: the derrick's own collider, a foundation or plate the pump stands on, a machine under it. None of those has a `CelestialBody` in its parent chain, so the column found "no body" — and a Jack Pump installed on a base read NO CRUDE BELOW forever, on an oil world, over a real seep.

The probe now digs: a hit that is not the body's terrain advances the ray past the collider and re-casts, up to 8 hops per column, all within the configured `scanDepth`. The derrick's own collider, placed blocks, and machines are transparent to the probe, and a ray that starts inside the derrick (a zero-distance hit) steps forward a floored 0.02 m instead of stalling. A body whose ore layers do not carry crude still ends its columns immediately, so a non-oil world is refused just as fast as before. No new fields, no new setup step; the 1 s rescan cadence is unchanged.

#### What to run

1. Replace `Scripts/Crafting/Furnace.cs`, `Scripts/Crafting/ElectricFurnace.cs`, `Scripts/Crafting/Pumpjack.cs`, and `Scripts/UI/GameUIController.cs`. Let Unity compile; the console should be clean.
2. No setup step to re-run: this round authors nothing.
3. Put ore and coal in a furnace. If it smelts, done. If it does not, the panel now says why: a NO RECIPE pill with the "missing item links" hint means re-run `Tools > Voxel Engine > Voxel Engine Setup`, step 4 (and 10 for the electric furnace), once — the hint says exactly that. Any other pill text is the real cause, word for word.
4. On an oil world, stand a Jack Pump on a foundation or any block over a seep and open its panel: it should read PUMPING, not NO CRUDE BELOW. On a world without crude, NO CRUDE BELOW is still the right answer.

### [11.0.0-dev] A Parked Hull Stays Parked, the Water Probe Stops Throwing, and the Well Produces Crude

**Type:** MAJOR — this round corrects a shipped regression, and in doing so changes the save schema: the three frame-relative velocity fields added to `SavedGrid` in 10.1.0-dev are removed. The Jack Pump also stops having item slots, so its prefab changes shape. Both changes are load-compatible in one direction only — a save written by this round cannot be read by 10.1.0-dev or 10.2.0-dev — which is what the major bump records. **One setup step is required: `Tools > Voxel Engine > Voxel Engine Setup`, step 76.**

**GitHub title:** `[11.0.0-dev] A parked hull stays parked, the water probe stops throwing, and the well produces crude instead of barrels`

#### Why this round

Test feedback named four things that were wrong. Each is dealt with below in the order it was reported, and the first is a regression this project shipped in 10.1.0-dev.

#### 1. A hull sank further into the ground on every rejoin

**The cause was the frame-relative velocity from 10.1.0-dev, and it is removed.** That round stored a movable grid's linear velocity relative to the motion of the scene's reference frame, on the reasonable-sounding grounds that a raw scene velocity describes motion against a frame that is itself orbiting. The arithmetic was wrong. `SpaceOrigin.FrameVelocityKmS` is in kilometres per second and a `Rigidbody.linearVelocity` is in metres per second, and the conversion multiplied by 1000 in the right direction for one leg of the round-trip and not the other. The result: a hull parked on the ground was saved with roughly zero relative velocity, and restored with its planet's ent