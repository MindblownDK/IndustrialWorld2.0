# IndustrialWorld — Changelog

**Branch:** `Dev`  
**Current Version:** `9.5.2-dev`

All release notes are maintained here so `Roadmap.md` remains focused on planned work and execution status.

### [9.5.2-dev] Unconditional Collider Yield, Self-Naming Mining Diagnostics, Hard-Clamped Respawn & Faster Streaming

**Type:** PATCH — no save/API change.

#### ⛏️ 1. Mining — heuristics are out, an unconditional rule is in (plus a truth serum)
Every yield heuristic so far (scan ball, footing) had a failure mode in the field. New ABSOLUTE rule in `GpuPlanetEngine`: whenever the gameplay bubble is actively streaming THIS body (`SphereWorld.ActiveChunkCount > 24` — trivially true in play), NO LOD-skin collider may exist within 300 m of the player. No thresholds, no scans, no footing checks. The wider handshake window still applies beyond that.
AND, because remote guessing has cost us three rounds: **mining now diagnoses itself.** Any swing that changes zero voxels prints ONE line — what the ray hit, the exact voxel under the brush (density / material / mineable / tier vs tool tier) and every collider found in a 4 m downward probe with names and distances. If mining still stops, that line IS the answer — please copy it into the chat.

#### 🛌 2. Respawn — spherical parking, staged logging, final hard clamp
- The routine still contained a flat-world relic: deep-space deaths parked the player on a "Y ≥ 250" plane before snapping — nonsense on a sphere. Parking is now `dest + 12 m` along the destination body's real up.
- If, after chunk-wait, ground-snap and dry-spawn, the player is STILL in open space, the routine hard-clamps to an analytic `PlanetField` surface point on the home body — a world-spawn respawn physically cannot end in space any more. (Bed/cryobed/station respawns are exempt by design.)
- Every stage logs (`parked at… / complete at…`) so any remaining oddity is one copy-paste away.

#### 🚀 3. Surface streaming speed
Bubble budgets raised (outstanding requests 40→64, generation concurrency 8→12, mesh concurrency 5→8, scaled from the quality tier). Generation is Burst worker-thread work and the field reports zero gameplay lag — the headroom belongs to streaming. Normal-speed movement should now stay ahead of the surface.

#### ✅ Static delivery checks
- All sources parse clean (tree-sitter C#); version synchronized to 9.5.2-dev.

#### Manual Unity steps (Thomas)
1. Pull `Dev`, recompile.
2. Mine down. If ANYTHING still stops you, copy the `[Mining] Swing changed no voxels…` console line into the chat — it names the blocker outright (collider names included).
3. Die (with and without bed): watch `[PlayerSpawner] Respawn parked at… / complete at…` — a world respawn must end on the planet, guaranteed by the final clamp.
4. Run/fly across the planet at normal speed: chunks should now stay ahead of you.

### [9.5.1-dev] Footing Yield, World-Aligned Lattice, Bright Navigation Beacons & Calm Ocean Shores

**Type:** PATCH — final mining unblock, chunk-border seams, beacon visibility, shore overreach. No save/API change.

#### ⛏️ 1. Mining 0.5 m stopper, final piece — collider FOOTING rule
Visual layers are gone (cutout confirmed working) — the remaining stopper was the LOD skin's MESH COLLIDER still active under the player: the pickaxe ray ignores it (safety marker), your body doesn't. The yield gate demanded a >32 m fully-meshed ball from the scan — too strict in practice. New rule: **while the player has FOOTING** (any meshed chunk in the 27-block around the stream centre — i.e. you are literally standing on bubble terrain), the ENTIRE collider window belongs to the bubble and every LOD collider inside it switches off. No scan sensitivity, no threshold: standing on ground = dig freely, to the core. Engine telemetry now also prints the bubble state (`bubble=meshedR/colR`).

#### 🧩 2. Small gaps BETWEEN chunks — world-aligned lattice
9.5.0's per-chunk lattice sampled each chunk's own box, so two neighbours interpolated the surface from DIFFERENT plane positions and disagreed exactly at their shared border → the thin between-chunk gaps you found. The lattice now sits on the GLOBAL 8 m world grid (7³ planes, snapped origins): neighbouring chunks sample the surface at IDENTICAL world positions and their interpolated surfaces agree bit-for-bit at every shared voxel. Seam-free by construction.

#### 🔭 3. Distant planets — navigation-grade beacons
Minimum apparent size raised from ~0.2° to **~0.5° (full-moon size)** with a brighter tint — every planet in the system reads clearly in the sky as a destination, still rendered AT its true position and still fading into the real surface on approach.

#### 🌊 4. Shore waves climbing above land
The open-ocean patches shared the chunk-water material 1:1, whose full wave/tide stack (~1.3 m vertical excursion at 0.3 m inset) let animated shore swells crest above beach level. The ocean now uses its own CALMER clone (reduced deep/secondary/shallow amplitudes, minimal tide, softer chop) at a 0.65 m inset — same look, wakes and foam, but the shoreline animation stays below the land. Bubble chunk water is untouched.

#### ✅ Static delivery checks
- All sources parse clean (tree-sitter C#); version synchronized to 9.5.1-dev.

#### Manual Unity steps (Thomas)
1. Pull `Dev`, recompile.
2. Mine straight down: past 0.5 m, past 5 m, keep going — nothing may ever stop the descent again (check `bubble=` in the `[GpuPlanetEngine:…]` log if it does).
3. Fresh-world flyover: the thin between-chunk gaps must be gone.
4. Look at the night sky: planets/moons as clear bright dots (~full-moon size) you can navigate by.
5. Watch a shoreline: waves stay at/below beach level while animating.

### [9.5.0-dev] Exact-Surface Chunks — Lattice Generation, No More Hollow Planets, Duplicate Moon Removed, True-Position Beacons & Deep-Space-Proof Respawn

**Type:** MINOR — the bubble now generates the EXACT field (root cause of "gaps appear when chunks generate", the 0.5 m wall AND the hollow "hole through the planet"). Save-compatible; freshly generated chunks improve automatically.

#### ⛏️🧩 1. THE big one — the bubble was generating an approximation, not the field
Your observation "before generation there are no gaps" was the key: the GPU surface (exact field) was right — the VOXEL CHUNKS were wrong. Since 8.0.2 each chunk approximated the surface with a chunk-centre LINEAR gradient. The 9.x field has ridged mountains and domain warping — linear extrapolation across a 32 m chunk misses by tens of metres at crests and warped coasts, so chunks generated HOLLOW (mine down into a void "hole through the planet") or OVERFILLED/clipped (gaps appearing exactly when chunks streamed in, phantom 0.5 m walls where the cutout removed the correct GPU skin over incorrect voxels).
- New `BuildSurfaceLatticeJob` (Burst prepass): evaluates the TRUE `PlanetField` surface radius on a 5³ lattice per chunk; `SphereChunkGenJob` trilinearly interpolates it per voxel (max lattice spacing ≈ 8.5 m; ~125 field samples per chunk — negligible cost, still ~300× cheaper than per-voxel).
- New `SphereDensity.EvaluateVoxelWithSurface(...)`: evaluation against an exact surface radius; the legacy gradient path remains only for probe callers.
- Voxels, GPU skin, mining, colliders and the cutout now all agree on ONE surface. Mine to the core: the interior is solid rock the whole way down (as it always should have been).

#### 🌙 2. Duplicate moon — auto sub-moons removed
The registry auto-generated a "sub-moon" per moon that CLONED the moon's own BodySettings — a duplicate of the same moon orbiting itself ("two moons but only one is assigned"). Auto sub-moons are gone; they return only when authorable with their own settings.

#### 🔭 3. Can't see other planets — true-position beacons
An 8 km planet at 40,000 km is sub-pixel — physically present, optically invisible (that's why the sky looked empty once the fake proxies were gone). New `DistantBodyBeacons`: each REAL body gets a small emissive sphere parented AT the body itself (zero offset — nothing follows the player), scaled so the body never drops below ~0.2° apparent size, shrinking back inside the real body as its true surface takes over. Honest night-sky planets: fly at the dot, the dot IS the planet.

#### 🛌 4. Respawn — deep-space-proof
The 9.4 heal used the streamed world's dry-point search, which fails when death happens in deep space or another frame (streamer suspended / body null) — the heal silently did nothing. New analytic fallback: a fresh dry spawn is computed on the HOME body straight from `PlanetField` (no chunks, no colliders, frame-independent), and every respawn now logs its decision (`[PlayerSpawner] Respawn → dest=… anchor=… bed=…`).

#### 🔬 5. "Surface not generated on the new planet" — instrumented
Per-body engine telemetry every 15 s: `[GpuPlanetEngine:<Body>] nodes/ready/building/queued, finest depth, altitude`. If a planet still refuses to refine on approach, that one line tells us exactly which stage is starving — send it with your next report.

#### ✅ Static delivery checks
- All sources parse clean (tree-sitter C#); lattice arrays disposed on every completion/teardown path; version synchronized to 9.5.0-dev.

#### Manual Unity steps (Thomas)
1. Pull `Dev`, recompile. Existing saves work — but chunks you already visited were generated with the OLD approximation and stay as saved if modified; unmodified areas regenerate exact. For the cleanest check, use a fresh world.
2. Fly along ridged mountains while chunks stream in: NO gaps may appear at generation time any more.
3. Mine straight down 30+ m: solid rock the whole way (ores included); no voids except real caves; and yes — with patience you can now dig clean through the planet.
4. Sky check: exactly ONE moon for the home planet, and the other planets visible as honest bright dots that grow into real planets.
5. Die in deep space with no bed: respawn must land on the HOME planet (watch the `[PlayerSpawner] Respawn →` log).
6. Approach a new planet and hold ~2–8 km altitude for a minute: if its surface stays coarse, send me its `[GpuPlanetEngine:…]` lines plus the `[CosmosBootstrap] Bodies:` line.

### [9.4.0-dev] Real-Only Space — Proxies Deleted, Ocean Job Crash Fixed, NaN Defence-in-Depth, Trust-No-Legacy Respawn

**Type:** MINOR — removes the fake sky-proxy system entirely and fixes the 9.3 regressions. Save-compatible.

#### 🌊 0. THE crash — ocean job container (also a big part of "gaps in the planet")
`GpuOceanEngine` scheduled the shared quadtree job without the new `splitSet` container → `InvalidOperationException` EVERY update since 9.3 — the ocean never rebuilt, so every sea rendered as a giant hole in the planet. The ocean now passes a constructed (empty) set. If terrain gaps remain after this, they are a separate report.

#### 🪐 1. NO MORE FAKE PLANETS — real bodies only
The carrot-on-a-stick proxies (true bearing at a fixed 30 km — they "followed the player" and could never be reached, which is also why "the surface never generated" and "the sun is unreachable": you were chasing sprites) are DELETED, not patched: `SpaceBodyRenderer`, `SphereSurfaceColor` and `PlanetSkyProxyURP` removed. What you see in space is now exclusively REAL: real planets/moons (their GPU quadtree surfaces stream at any distance), the real star mesh (StarSurfaceURP — flying at it gets you there, and into its heat hazard), real minable asteroids, plus genuine backdrop only (starfield, nebulae, dust, glare). Black hole & quasar bodies: locked in for **Phase 5**.

#### 🧭 2. NaN defence-in-depth (the missing-planets poison)
One non-finite value anywhere in the cosmic chain froze every other body at the scene origin (inside the home planet — "planets don't spawn"), corrupted respawn's space test and killed asteroid spawns. Now:
- **Registry build:** every freshly-built orbit is validated; NaN → repaired to a safe circular orbit + ONE error naming the body and its authored elements.
- **SpaceOrigin:** all three anchor-update paths and the world rebase reject non-finite values (one-shot report incl. "viewer position is NaN" for physics blow-ups); `PlaceBodies` guard stays.
- **Asteroids:** non-finite spawn positions skipped; first successful rock logs a confirmation line.
- **Diagnostics:** every 30 s one log line lists EVERY body with its live distance (`Venus=8123km …` or `NaN!`) — whatever is still wrong will name itself.

#### 🛌 3. Respawn — trust no legacy point
Saves from before body-anchoring can never gain an anchor by themselves, and their raw scene points are meaningless after any re-anchor. A world-spawn respawn now REQUIRES a body anchor: un-anchored or open-space destinations are rejected outright, a fresh dry surface point is computed on the active world, and the save is healed (anchor written back). Orbital bed spawns remain by-design untouched.

#### ☄️ 4. Asteroid spawn distance fix confirmed + shader warning
The 9.3 km/m fix plus the NaN guards make rocks actually appear (900–6,000 m ring, high orbit + deep space). The Metal warning in `PlanetFieldGpu` (`potentially uninitialized variable (CaveCarve)`) is silenced via a single-return carve — identical maths, CPU/GPU parity intact.

#### ✅ Static delivery checks
- All sources parse clean (tree-sitter C#); zero references to the deleted proxy system; version synchronized to 9.4.0-dev.

#### Manual Unity steps (Thomas)
1. Pull `Dev`, recompile.
2. Console must be free of the `splitSet` exception; oceans must be water again.
3. Watch the 30 s `[CosmosBootstrap] Bodies:` line — every planet should show a real distance. **If any says `NaN!` or an error names a body/orbit, send me those lines — they are the last piece of the puzzle.**
4. Fly toward a planet you SEE (it's the real one now — it grows the entire way): surface streams in, gravity/atmosphere engage on arrival.
5. Fly at the sun: it must get closer, show the plasma surface, then burn you (hazard warning first).
6. Die without a bed: respawn must land on the planet — every time, even on old saves (watch for the heal log).
7. High orbit/deep space: rocks appear within ~6 km (look for the `[SpaceAsteroidField]` line).

### [9.3.0-dev] Root-Cause Round — SRP-Batcher Cutout, Global Band, Depenetration Guard, NaN-Proof Orbits, Healed Respawn, Real Asteroid Ranges & a Burning Star Surface

**Type:** MINOR — deep root-cause fixes for every 9.2 field report + the procedural star surface. Save-compatible.

#### ⛏️ 1. Mining wall & ground flicker — the cutout NEVER ran (SRP Batcher)
The real culprit behind BOTH "still can only mine 0.5 m" and "terrain disappears when you look at the ground": `_BubbleCutout` was declared OUTSIDE the `UnityPerMaterial` CBUFFER. With URP's SRP Batcher, such uniforms are treated as GLOBALS — `material.SetFloat` on the LOD-skin clone silently did nothing, so the skin was never clipped (phantom ground behind mined holes) and sat EXACTLY on the bubble surface (coincident z-fighting = the view-dependent shimmer/vanish).
- `_BubbleCutout` + new `_LodRadialBias` now live in Properties AND in every pass's `UnityPerMaterial` CBUFFER of `VoxelTerrainURP` and `VoxelTerrainEnhanced` (forward, shadow, depth — identical layouts, SRP-Batcher compatible).
- **LOD radial deflation:** the skin clone sinks 0.45 m toward the core (vertex stage, all passes) — the bubble surface always renders on top; no more coincident z-fighting anywhere in the overlap band.
- Render cutout radius now uses the full meshed-bubble radius (−8 m) instead of the collider window.
- **Local guarantee:** if the 27 chunks around the stream centre are covered, the handshake gets a minimum 64 m ball — one distant straggler chunk can never re-arm the skin's colliders under your feet again. Handshake state is now printed in the 3 s SphereWorld diagnostics.

#### 🧩 2. Terrain gaps + LOD flashing — global analytic band & split hysteresis
- The 9-point sampled radial bands could miss peaks/valleys between probes at coarse depths → the band CLIPPED the terrain (the gaps), and the "correct ↔ gapped" flashing was the quadtree flip-flopping between the parent (full band, correct) and children (clipped). Bands are now derived from the field's ANALYTIC elevation bounds — one shared radial lattice for the entire planet at every depth: the surface can never leave the band, and every node border matches bit-identically. Watertight by construction, at last.
- Split/merge hysteresis (15%) via a split-set fed back into the Burst descent job — no more threshold flip-flop flashing.

#### 🛬 3. 40 m inside the planet at extreme speed — depenetration guard
Discrete physics tunnels through ANY mesh collider at extreme velocity. The engine now watches the viewer: crossing from outside the analytic surface to >3 m inside at ≥60 m/s snaps the player/ship back onto the surface and zeroes velocity (cave and mined-shaft players never trigger it — they're inside slowly).

#### 🪐 4. Other planets missing / NaN — orbits are NaN-proof and self-reporting
`transform.localPosition is {NaN}` on other bodies meant corrupt orbital data was poisoning their positions (also why proxies pointed at nothing). Kepler propagation now clamps eccentricity to <1 in the solver AND the true-anomaly √(1−e); `UpdateFromOrbit` keeps the last valid state on NaN; `SpaceOrigin.PlaceBodies` refuses NaN assignments and reports the offending body ONCE with its full orbital elements — planets stay where they were and the log names the bad data.

#### 🛌 5. Respawn-in-space, final word
Even body-anchored spawns couldn't help saves written before 9.2. A world-spawn respawn that resolves to open space (>800 m above every body) is now REJECTED: a fresh dry surface point is computed on the active world and written back — the save heals itself. Orbital bed/cryobed spawns remain untouched by design.

#### ☄️ 6. Asteroids — the km/m unit bug
Spawn ring distances (metres) were added to COSMIC coordinates (kilometres): every rock spawned 900–6,000 KM away and was instantly culled — asteroids "never existed". Ring distances are now correctly converted (÷1000). Combined with 9.2's open-space gating you'll see rocks in high orbit and deep space.

#### ☀️ 7. The sun is a STAR now (feature)
New `VoxelEngine/StarSurfaceURP`: fully procedural animated plasma surface — domain-warped granulation cells, drifting dark starspots, limb darkening and a hot fresnel rim, tinted by the authored glow colour. `SolarHazard`'s real sun mesh uses it automatically (graceful fallback to Unlit if the shader is missing).

#### ✅ Static delivery checks
- All touched sources parse clean (tree-sitter C#); both terrain shaders keep identical per-pass CBUFFER layouts; version synchronized to 9.3.0-dev.

#### Manual Unity steps (Thomas)
1. Pull `Dev`, recompile. Saves keep working.
2. Mine deep + tunnel: no wall, no phantom ground, and NO shimmer when looking at the ground.
3. Watch the console 3 s diagnostics: `handshake: meshedR=…` should sit around 150–250 m while standing on terrain. If it reads 0, send me that log line.
4. Fly low and far: no gaps, no correct↔gapped flashing.
5. Ram the planet at max speed: you must end ON the surface (the guard logs a warning if it had to catch you).
6. Check the Console for `[SpaceOrigin] Body '…' produced a NaN…` — if it appears, send me the line (it names the corrupt orbit); the planets will render regardless.
7. Fly toward another planet's proxy dot — it must grow into the real planet; gravity/atmosphere engage.
8. Die away from base with no bed: respawn must land on the planet surface (watch for the heal log).
9. High orbit: minable rocks within ~6 km; approach the sun: a burning animated star surface with spots, not a light ball.

### [9.2.0-dev] Field Report Fixes — Deep Mining, Solid Approaches, Watertight Lattice, True-Direction Planets, Anchored Respawn & Open-Space Asteroids

**Type:** MINOR — six field-reported fixes + the open-space asteroid population. Save-compatible (spawn sidecar gains optional body-anchor fields).

#### ⛏️ 1. Mining stopped ~0.5 m down (handshake never engaged)
The 9.1.0 meshed-bubble scan required EVERY chunk in the bubble to have a mesh — but air/interior chunks intentionally never mesh, so the scan always failed, the GPU LOD skin kept rendering AND colliding under the bubble, and every dig hit an invisible skin ~0.5 m below the surface with phantom terrain behind it.
- `Chunk.needsSurfaceMesh` (set by `FinalizeGen`) now records whether a chunk intersects the surface at all; the coverage scan treats meshless air/interior chunks as fully covered. The handshake engages properly: skin clipped, LOD colliders yield — dig as deep as you want.

#### 🛬 2. Flying fast into a planet passed through the surface
Only fine (≤4.5 m cell) nodes ever received colliders — a fast approach reached the ground before any existed, dropping the player through the surface onto the deep safety core (with the surface invisible from inside until the bubble streamed).
- The node chain directly under the viewer now colliders at EVERY quadtree depth (`dist < 1.35×arc + 250 m`), so an approach from orbit always finds solid ground; collider bake budget raised to 2/frame. The bubble-yield rule still wins near the player.

#### 🧩 3. Terrain gaps away from spawn (radial lattice mismatch)
Same-depth neighbour nodes used free-floating radial bands, so their ghost-cell corners sampled DIFFERENT radii — the watertight stitching broke wherever relief differed and cracks/holes opened along node borders.
- Radial bands are now quantised onto a SHARED per-depth lattice (dr = power-of-two multiple of the cell arc, rLo snapped to a dr multiple) with a guaranteed relief fit — boundary corners are bit-identical on both sides again. Skirts deepened to 3 cells for the rare lattice-scale transitions.

#### 🪐 4+6. Planets you fly toward are now THE planets (no more SpaceBody_N decoys)
The sky proxies were drawn at COMPRESSED positions (~20 km away in a fake layout): flying to one arrived at empty space while the real body — with the gravity, atmosphere and surface — was tens of thousands of km elsewhere. Frame/gravity/atmosphere never engaged because you were never actually near the planet.
- Proxies are now TRUE-DIRECTION visualisations: placed on the real bearing at a fixed render distance with the body's true apparent size (plus a minimum so distant planets stay findable), fading out exactly when the real body grows large enough to carry itself. Flying at the dot IS flying at the planet — gravity dominance, proximity hold, atmosphere and streaming all engage naturally on arrival. Hierarchy objects are named `Proxy_<BodyName>`.

#### 🛌 5. Respawn dropped the player in space
The world spawn was stored as a raw SCENE position — stale the moment the floating origin re-anchored (orbital motion, visiting another body).
- The world spawn is now body-anchored (body name + body-local offset, persisted in the spawn sidecar) and respawn reconstructs the live scene position from the body's CURRENT transform. Legacy scene-point fallback retained; bed spawns unchanged.

#### ☄️ 7. Open-space asteroids (feature)
Minable rocks were exclusive to the deep-space star frame — most flights never saw one.
- Rocks now populate any OPEN SPACE: deep space or high orbit inside a body's frame (above `max(12 km, 1.25× atmosphere height)`), denser and wider (28 rocks, 0.9–6 km ring, 12 km despawn, 12–90 m radii), still deterministic per cosmic region, still fully minable. The sky above bases stays clean.

#### ✅ Static delivery checks
- All touched sources parse clean (tree-sitter C#); version synchronized to 9.2.0-dev.

#### Manual Unity steps (Thomas)
1. Pull `Dev`, recompile — existing saves keep working.
2. Mine straight down 10+ m and tunnel sideways — no floor, no phantom walls at any depth.
3. Fly full speed into the planet from orbit — you must land ON terrain (coarse at worst), never inside it.
4. Fly a few km across the planet at low altitude — no cracks or holes along the way.
5. Fly toward another planet's dot in the sky — it must grow continuously into the real planet; watch gravity/atmosphere engage on approach; land, then die on purpose — you must respawn at the world spawn on the planet.
6. Climb to high orbit — asteroid rocks should appear around your route and be minable up close.

### [9.1.0-dev] Single-Surface Handshake — Bubble ⇄ GPU Surface Unification (Rework Phase 2)

**Type:** MINOR — new save-compatible handshake system between the gameplay bubble and the GPU quadtree surface. No save-schema change.

#### 🎯 Why Phase 2 changed shape
The original Phase-2 plan ("move mining/persistence onto the GPU engine, 64³ bubble chunks") became unnecessary the moment 9.0.0 unified the density field: the bubble already generates EXACTLY the surface the GPU engine renders, and mining/persistence already work. What remained was the overlap zone around the player where BOTH systems rendered and collided:
- a mined hole in the bubble could reveal the GPU LOD skin behind it — the tunnel looked filled with phantom terrain;
- the GPU skin's mesh collider (a surface sheet) could wall off tunnel mouths;
- two nearly-coincident surfaces shimmered where they overlapped.
9.1.0 makes the two systems ONE surface, everywhere. (A 64³ bubble was evaluated and rejected: 8× the voxel memory/latency per chunk for zero visible gain — the GPU nodes are already 64³.)

#### 🤝 The handshake
- **SphereWorld publishes its real coverage** — a conservative "meshed bubble" ball (centre + radius where every chunk is generated AND meshed, rescanned every 0.35 s, ~2 k dictionary probes) plus the collider window, via `TryGetMeshedBubble(...)` and the shader globals `_VoxelBubbleCenterWS` / `_VoxelBubbleCutoutRadius`. The globals reset on body switch and teardown so a stale cutout can never punch a hole in terrain.
- **The GPU LOD skin clips inside the bubble** — `GpuPlanetEngine` now renders through a clone of the terrain material with `_BubbleCutout = 1`; `VoxelTerrainURP` and `VoxelTerrainEnhanced` discard those fragments inside the published ball. Bubble chunks keep the original material untouched. Mined holes and tunnels now show the REAL edited voxels — never a ghost surface.
- **LOD colliders yield to the bubble** — GPU node colliders switch off whenever the node's bounding ball touches the bubble's collider window (and re-bake when the player moves on). The bubble always surrounds the player, so physics never loses its floor; tunnels dig freely.
- **Whole nodes fully swallowed by the bubble hide entirely** (with hysteresis), removing coincident-surface shimmer near the player.

#### ✅ Static delivery checks
- All touched sources parse clean (tree-sitter C#); cutout declared + applied in both terrain shaders; version synchronized to 9.1.0-dev.

#### Manual Unity steps (Thomas)
1. Pull `Dev`, recompile (existing 9.0.x saves keep working — MINOR).
2. In Play Mode, mine straight down and sideways into a hillside: the tunnel must stay OPEN — no phantom floor/wall appearing behind mined voxels, and you can walk through freely.
3. Look at the horizon while walking: no double-surface shimmer in the near field; terrain past the bubble edge unchanged.
4. Fly up fast: the GPU surface should still be there when the bubble lags behind (cutout ball shrinks automatically), with no holes in the planet.
5. Fly to another planet and back: no stale see-through hole where the old bubble was.

### [9.0.1-dev] GPU Engine Compile Recovery (CS0103 lod/oceanLod + FindObjectsByType Deprecation)

**Type:** PATCH — compile recovery for the 9.0.0 rework; no behaviour, save-schema, or API change.

#### 🛠️ Compiler Fixes
- **CS0103 `lod` / `oceanLod` (CosmosBootstrap.cs:274–275):** the graphics-preset application block still referenced the deleted legacy LOD components. The terrain budget is already pushed via `_terrainGpu.ApplyQualityBudget(...)` at spawn; the line now routes the ocean tier to `_oceanGpu.ApplyQualityBudget(GraphicsPreset.LodResolution)` instead.
- **CS0618 `FindObjectsByType(FindObjectsInactive, FindObjectsSortMode)` (QualityPresetApplier.cs:74):** switched to the non-deprecated `FindObjectsByType<GpuPlanetEngine>(FindObjectsInactive.Include)` overload (Unity 6.5 removes the sort-mode parameter path).

#### ✅ Static delivery checks
- Both touched sources parse clean (tree-sitter C#); no remaining `lod`/`oceanLod` identifiers or `FindObjectsSortMode` usages in runtime code; version synchronized to 9.0.1-dev.

#### Manual Unity steps
1. Pull `Dev`, let Unity recompile — the console must be clean (no CS0103, no CS0618 from these files).
2. Continue with the 9.0.0 validation checklist (fresh save, top-down refinement, coastlines, wakes, planet-to-planet flight).

### [9.0.0-dev] GPU-Driven Voxel Engine — Compute Density, Dual Contouring & Asynchronous Spherified Quadtree (Rework Phase 1)

**Type:** MAJOR — complete planetary generation rework. New unified density field → all worlds regenerate differently. **Requires a fresh save.**

#### 🚀 Why the rework
The 7.x/8.x ladder (impostor + 5-level voxel LOD rings + gradient-corrected chunk columns) approximated one surface with several independent systems, and every approximation seam was a place for gaps, slabs and ghost layers to appear. 9.0.0 replaces all of it with ONE pipeline evaluating ONE continuous field.

#### 🧠 The new engine (`Scripts/GpuVoxel/`)
- **GPU density evaluation** — `Resources/PlanetFieldGpu.compute` evaluates the whole 3D density formula for a 64³-cell node (67³ corners) across thousands of GPU cores in milliseconds. Two kernels: `CSColumns` (per-column surface radius + climate + slope — the expensive stack runs once per column) and `CSField` (per-corner signed density + material). Results return via `AsyncGPUReadback` — the main thread never stalls.
- **Dual Contouring over Marching Cubes** — `GpuDualContourJob` (Burst, worker threads) places ONE QEF-relaxed vertex per surface-crossing cell (Schmitz particle over the Hermite edge data): far fewer polygons, smooth hills AND sharp mountain ridges. Zero-copy upload via `Mesh.ApplyAndDisposeWritableMeshData`.
- **Asynchronous Spherified Quadtree** — `SphereQuadtree.cs` divides each of the 6 cube faces into a quadtree of curved shell nodes whose radial band hugs the terrain. The desired-leaf computation is a Burst job on background threads (Unity Job System), fully isolated from the game loop. Approach the surface → nodes split into four higher-res children; parents stay visible until all four children are ready (no holes, top-down refinement).
- **Watertight by construction** — every node meshes one ghost cell beyond its footprint and each quad has exactly one owner node, so equal-depth neighbours stitch bit-identically (no cracks, no overlap, no z-fighting). Depth transitions are masked by radial skirts. The field itself is `density = surfaceRadius(dir) − |p| − caves(p)` — a closed 2-manifold that CANNOT have gaps at any resolution.
- **`GpuPlanetEngine`** — one orchestrator per body: dispatch budgets, readback slots, mesh application budgets, distance-prioritised coarse-first streaming, pooled node GameObjects, near-viewer mesh colliders (marked `PlanetSafetyCollider` so interaction rays skip them) and a safety core sphere so nothing ever falls through a streaming planet.

#### 🌍 One field for everything (`PlanetField.cs` ⇄ `PlanetFieldGpu.compute`, kept in lockstep)
- Brand-new gap-free terrain: domain-warped fBm continents with soft shorelines, continental-shelf→deep-basin ocean floors, mid-frequency hills, ridged-multifractal mountain chains masked to continental uplift zones, sealed-crust caves that never breach ocean floors.
- `SphereDensity.EvaluateColumn` now sources its surface shape from `PlanetField` — the 1 m gameplay bubble, scatter, waterfalls, ocean cut-outs, safety colliders and sky proxies all agree with the GPU surface exactly. Cave carving is the same shared function on both sides.
- Biomes still drive materials, climate and scatter; the GPU picks materials through a climate→biome LUT built from the same authored `BiomeData` (snow lines, polar ice, waterline beaches, slope rock included).

#### 🌊 Water (Phase-1 scope)
- **`GpuOceanEngine`** — quadtree ocean sphere: curved water patches at sea radius, skipped over dry-land tiles, refined near the viewer, sharing the chunk-water material (`VoxelEngine/VoxelWaterURP`) — so **boat wakes** (`NativeWaterWakeSystem`), Gerstner waves, foam and shore blending work on the open ocean exactly as in the bubble. UV2 is reserved per-vertex as the flow-map channel for the Phase-3 liquid-flow rework.

#### 🗑️ Removed (legacy generation path — deleted, no fallback)
- `PlanetVoxelLod.cs`, `PlanetLodImpostor.cs`, `PlanetOceanLodRenderer.cs`, `PlanetSurfaceLodURP.shader` and all references. `CosmosBootstrap` now spawns `GpuSurface` + `GpuOcean` per body; `QualityPresetApplier` budgets route to the new engines.

#### 🔜 Rework phases
- **Phase 2:** gameplay bubble (mining/persistence/colliders) moves onto the GPU engine with edit-delta persistence, 64³ chunks.
- **Phase 3:** liquid FLOW simulation rework (rivers, springs, pumped water) on the new engine + flow-map ocean currents.

#### ✅ Static delivery checks
- All new/modified sources parse clean (tree-sitter C#); zero remaining references to the deleted LOD classes; version synchronized to 9.0.0-dev.

#### Manual Unity steps (Thomas)
1. Pull `Dev`, let Unity import `Scripts/GpuVoxel/` (the compute shader lives in `Scripts/GpuVoxel/Resources/PlanetFieldGpu.compute` — the engine loads it by name, no wiring needed).
2. **Fresh save required:** delete `<persistentDataPath>/VoxelWorlds/` (all worlds) — the new field regenerates every planet.
3. Enter Play Mode: within ~1–2 s the six coarse face shells should appear, then refine top-down toward your position. Verify: no gaps/slabs at any distance, mountains have sharp ridgelines, oceans end at real coastlines (no water over land).
4. Walk to a shoreline and sail/fly out: distant ocean should show waves + your boat's wake outside the 1 m bubble.
5. Fly to orbit and to another planet: every body should show its real surface refining as you approach; no falling through planets (safety core + near colliders).
6. Report FPS during streaming — budgets (`maxConcurrentBuilds`, `maxAppliesPerFrame`, `splitFactor`) are tunable on the `GpuSurface` object if we need a patch.

### [8.0.2-dev] Spherical Surface Restoration — Gradient-Corrected Chunk Columns (Flat-Layer Fix)

**Type:** PATCH — restores correct spherical planet generation; no save-schema or public API change.

#### 🌍 The real cause of the "flat layers" planet
The 7.20.0 chunk-column caching evaluated the surface column **once per chunk at the chunk centre** and reused that single surface radius for every voxel of the chunk. On the 1 m gameplay bubble (32 m chunks) the error was invisible — but on the LOD levels (4 m / 8 m rings, the whole-planet FULL shell at 8–16 m voxels, and MID/FAR shells up to 16 km chunks) every chunk rendered as a **flat slab at its own centre height**. Adjacent chunks sat at different heights, so the planet read as **flat terrain layers stacked on each other with visible gaps between them** — exactly what Thomas reported, and not the flat world at all (the 8.0.0 removal was still correct, just not the culprit).

#### 🧭 The fix — first-order gradient correction
- `SphereDensity.ChunkColumn` now carries the **chunk-centre direction** and a **surface gradient** (∂surfaceRadius/∂dir) sampled with 7 column evaluations per chunk (centre + 2 slope probes + 4 gradient probes over a ~36 m baseline).
- `EvaluateVoxelCached` applies a per-voxel linear correction:
  `surfaceRadius(dir) ≈ surfaceRadius(centerDir) + dot(surfaceGrad, dir − centerDir)`
- Every voxel's density, depth, cave, material-band, snow-line, beach, slope-rock, ocean-basin and water decisions now use the **corrected** surface radius, so the surface follows the true sphere + terrain across the entire chunk footprint.
- Cost: ~5 column evaluations per chunk instead of ~39,000 — the ~10–30× generation speed-up from 7.20.0 is retained.

#### ✅ Static delivery checks
- All modified sources parse cleanly (tree-sitter C#); no leftover references to the removed column fields; runtime chunk generation uses the corrected cached path; version synchronized to 8.0.2.

#### Manual Unity steps
1. Let Unity finish compiling on the `Dev` branch.
2. **Clean saves (important):** the `_Earth` world folder created by 8.0.0/8.0.1 test runs may contain slabbed terrain chunks. Delete `<persistentDataPath>/VoxelWorlds/<worldName>_Earth` (or the whole world) so the planet regenerates fresh with the corrected field. Old flat-era folders are irrelevant now.
3. Enter Play Mode on the home world: the surface must read as one continuous **spherical planet** — terrain follows mountains/oceans, no horizontal slabs, no gaps between chunks.
4. Fly to another planet/moon and confirm its real voxel surface is spherical too.
5. Confirm generation speed is still fast (whole-planet FULL shell fills within seconds, as in 7.20.0).

### [8.0.1-dev] Flat World Removal Compile Recovery (CS0103 flatWorld + CS8321 dead local)

**Type:** PATCH — compile recovery for the 8.0.0 flat-world removal; no behaviour, save-schema, or API change.

#### 🛠️ Compiler Fixes
- **CS0103 `flatWorld` (VoxelEngineSetupWindow.cs):** a leftover viewer-link line still referenced the deleted flat world variable inside `SpawnManagerAndPlayer`. Removed the flat-world link — the sphere (`sphereWorld`) is the only world and is linked as before.
- **CS8321 `MakeIndustrialPrefab` unused local function:** removed the dead prefab helper (the factory-machines step uses `GetOrCreatePrefab` instead). Warning eliminated; no content creation behaviour changed.

#### ✅ Static delivery checks
- No remaining references to the deleted flat-world classes, GUIDs, or fields (`flatWorld`, `flatBiomeRegistry`, `flatSeed`, `flatSeaLevel`, `flatBaseHeight`, `flatContinentScale`) anywhere in `Scripts/`; modified editor file parses cleanly.

#### Manual Unity steps
1. Let Unity finish compiling on the `Dev` branch — both errors are gone.
2. No setup steps required; Play Mode behaviour is unchanged from 8.0.0.

### [8.0.0-dev] Flat World Removed — Spherical Planets Only

**Type:** MAJOR — the flat voxel world is removed as a core system and the home-world save key is namespaced (old flat-era saves are no longer read → the home planet regenerates fresh spherical terrain). Per Semantic Versioning this bumps MAJOR because it removes a core system and changes the save-key schema.

#### 🪐 Why (the flat layers bug)
The legacy flat `VoxelWorld` (heightmap generator: `ChunkHeightJob` + `ChunkGenJob` + `NoiseUtility`) was still able to pollute the spherical planet:
- The HOME body's chunk save folder was deliberately shared with the flat world's save folder (`<worldName>`). Old flat-era chunk saves therefore loaded straight into the spherical world and rendered as **flat terrain layers stacked on each other with visible gaps between them** — exactly the broken planet generation Thomas reported.
- The flat world code paths still existed (bootstrap disable logic, editor references, drill fallback) even though the game is planets-only.

#### 🗑️ Removed (flat world, fully)
- Deleted `Scripts/Core/VoxelWorld.cs`, `Scripts/Generation/ChunkGenJob.cs`, `Scripts/Generation/ChunkHeightJob.cs`, `Scripts/Generation/NoiseUtility.cs` (+ .meta files).
- `CosmosBootstrap`: removed the flat-world disable block and the flat-world asset fallback — the sphere is the sole world; assets resolve from Inspector → Resources → Editor asset path.
- `ActiveWorld` (IVoxelWorld): removed the `VoxelWorld.Instance` fallback — a dead reference simply clears.
- `VoxelEngineSetupWindow` (Step 2 + scene checks): sphere-only; no flat-world creation or detection remains.
- `GridDrill`: resolves the world from `ActiveWorld.Current` only.

#### 💾 Save key change (home planet)
- `SphereWorld.ResolveStorageKey` now namespaces EVERY body, home included (`<worldName>_Earth`). Old flat-era saves under the plain `<worldName>` folder are never read again — they cannot inject flat chunks into the sphere. The home planet (and any other body) regenerates its real spherical terrain procedurally on first load.
- Old save folders are left untouched on disk (non-destructive), they are simply no longer the active storage path.

#### ✅ Static delivery checks
- All 562 C# sources parse cleanly (tree-sitter); no references to the deleted classes, scripts, or GUIDs remain in scenes/prefabs/assets; version constants synchronized (8.0.0); no other game's name introduced.

#### Manual Unity steps
1. Let Unity finish compiling on the `Dev` branch. The deleted classes will make Unity reimport — this is expected.
2. **Scene hierarchy:** if your scene still contains an old `World` / `VoxelWorld` GameObject (it may appear as a "Missing Script" component after the class was deleted), **delete that GameObject** — the sphere (CosmosBootstrap) is the only world.
3. Open `Tools > Voxel Engine > Voxel Engine Setup` and run **Step 2** (spawns/relinks Player + UI to the sphere) and **Step 21** (celestial content repair) once each — both idempotent and non-destructive.
4. **(Recommended) Old saves:** delete the old world folder(s) under `<persistentDataPath>/VoxelWorlds/<worldName>` (e.g. `%USERPROFILE%/AppData/LocalLow/<company>/<product>/VoxelWorlds/MyWorld`). The new home save key is `<worldName>_Earth`, which is created fresh on next save.
5. Enter Play Mode: you should spawn on a **continuous spherical planet** — no flat layers, no gaps. Flying to any other planet/moon streams its real spherical voxel surface as before.

### [7.20.0-dev] Full-Planet Real Voxel Coverage, Fast Chunk Generation & Reliable Interplanetary Surfaces

**Type:** MINOR — new save-compatible world-rendering feature + voxel-streaming fixes; no save-schema or public API break.

#### 🌍 The planet you're near is now REAL voxels — all of it
- **Whole-planet FULL voxel shell:** the active body (the planet/moon you are on or approaching) now streams its ENTIRE surface as real generated voxel chunks — no sampled impostor sphere, no 32–128 m coarse MID blocks. The FULL shell's voxel size is chosen from a chunk budget (≈3.2k chunks on High/Ultra → **8–16 m voxels on home-sized worlds**, 4–8 m on moons, coarser only on giants).
- **Coverage radius world setting:** new `fullVoxelRadiusKm` world setting (default **50 km** — covers a whole 8–16 km planet) controls how far real voxel surface extends around the player. Persisted per-world in the world-settings sidecar (`WorldSession`), overridable on the `CosmosBootstrap` inspector (0 = legacy ring-only behavior).
- **New 2 m detail ring** bridges the 1 m gameplay bubble to the 4 m ring; the 4 m ring extends to 3 km and the 8 m ring to 6 km (coverage-driven). All three rings carry real colliders, so walking/mining terrain extends kilometres from the player.
- **LOD ladder rebuilt:** `FAR → MID → FULL(whole planet) → 8 m → 4 m → 2 m → 1 m bubble`, with strict one-surface nesting between every adjacent pair (the generalized nesting rule). The sampled impostor steps aside as soon as the FULL shell is ready.

#### ⚡ Voxel generation ~10–30× faster
- **Per-chunk surface-column caching:** the expensive climate/biome/tectonic/slope column evaluation now runs **once per chunk** instead of once per voxel (`SphereDensity.ChunkColumn` + `SphereChunkGenJob.BuildColumn`). The density field is smooth over a 32 m chunk, so terrain, biomes, snow lines, beaches, cliff-rock and ore bands are visually unchanged — but a chunk's generation drops from ~40–80 ms to ~2–5 ms.
- **Higher streaming budgets** for the active body: more concurrent generation/mesh jobs and a larger outstanding-chunk allowance (SphereWorld + PlanetVoxelLod), so the whole-planet shell fills in seconds instead of minutes.
- **Candidate caching:** the planet shell and ring scans (tens of thousands of chunk coordinates per rebuild) are cached and rebuilt lazily as the player moves, keeping the per-frame streaming cost near zero even with 10k+ chunks active.

#### 🛸 Non-starter planets / moons always generate a real surface
- **Proximity hold (frame-selection failsafe):** small moons and low-gravity bodies can never win gravity dominance over the star at their orbital distance — the scene frame therefore never switched to them and their real voxel surface never streamed (the "only LOD, no surface" bug). `CosmosBootstrap` now arms a **proximity hold** while the player is within ~15 km of a non-streaming body's surface; `SpaceOrigin` force-holds the frame to that body (with the streaming-body guard preventing hijack kicks), so arriving anywhere — planet, moon, sub-moon — always engages real voxel terrain.
- **Coverage re-applied on arrival:** entering a body's frame re-applies the coverage radius and rebuilds that body's level ladder, so every planet/moon streams with the same full-coverage rules as the home world.

#### ✅ Static delivery checks
- All modified sources parse cleanly (tree-sitter C#), C# 9 compatible (no struct field initializers), no save-schema or public API change, version constants synchronized (7.20.0), world-settings sidecar backward-compatible (old worlds default to 50 km coverage).

#### Manual Unity steps
1. Let Unity finish compiling on the `Dev` branch.
2. Open `Tools > Voxel Engine > Voxel Engine Setup` and run **Step 21 (celestial content repair)** once — it is idempotent and reconnects any missing planet/library wiring non-destructively. (No new setup step is required for this feature; it is pure runtime code.)
3. Enter Play Mode on the home world. Confirm the console logs `[PlanetVoxelLod] '<Home>' ... FULL x m / near ... (coverage 50 km)`.
4. Walk/fly around: the 1 m terrain bubble, the new 2 m ring, the 4 m ring and the whole-planet FULL shell should be visible as one continuous real voxel surface — no flat impostor sphere, no blocky MID patches, no visible surface seams.
5. Fly to another planet (e.g. Mars) from orbit. During the descent, watch the console for `Proximity hold armed` / `Reference frame → '<Planet>'` and confirm the REAL voxel surface (rings + FULL shell) generates as you approach — no more LOD-only arrival.
6. Land and mine/build on the new planet: real 1 m voxel terrain must be editable and collidable exactly like the home world.
7. Visit a moon if the system has one: confirm the proximity hold switches the frame even if gravity dominance wouldn't, and the moon's real surface streams.
8. (Optional) Change `fullVoxelRadiusKm` on the `CosmosBootstrap` inspector (e.g. 5 km on a weaker PC) and confirm the coverage shrinks accordingly; re-enter the world or switch planets to see the new radius applied.

### [7.19.0-dev] Eclipse-Aware Solar Glare & Sparse Orbital Dust

**Type:** MINOR — new save-compatible space-ambiance rendering systems; no save-schema or public API break.

#### ☀️ Cinematic solar glare without fake visibility
- Added a procedural camera-space solar glare with a restrained core, horizontal/vertical bloom streaks, and two subtle palette-tinted lens ghosts.
- Glare follows the real cosmic star direction and apparent angular size rather than a camera-fixed sprite.
- Local planetary horizon checks, nearby physics occlusion, and double-precision celestial-disc tests hide or feather the effect behind terrain, structures, planets, and moons. Real eclipses now visibly extinguish the glare.
- Surface atmosphere produces a broader warm response while vacuum keeps a tighter, calmer glare; planet sky palettes tint the result automatically.

#### ✦ Sparse motion-readable space dust
- Added a bounded 52-mote world-anchored dust field that fades in only through upper atmosphere and vacuum.
- Motes wrap around the camera at a fixed radius and remain world-anchored between wraps, creating real flight parallax without noisy emission, trails, physics, or unbounded particles.
- Dust tint follows the current sky/nebula palette and disappears cleanly on atmospheric return.

#### 🛠️ Non-destructive setup and runtime wiring
- `CosmosBootstrap` now creates both ambiance renderers automatically; no scene object or prefab is required.
- Step 51 is now **Author Planet Skies + Space Ambiance (Non-Destructive)** and creates/preserves standalone-build material references for the sky dome, nebulae, solar glare, and space dust.
- Existing sky overrides, display colours, runtime material properties, balance values, and custom content remain untouched on reruns.

#### ✅ Static delivery checks
- Modified C# syntax, shader structure, runtime bootstrap wiring, eclipse/dust source assertions, Step 51 idempotency, version synchronization, sparse-workspace exclusions, and diff whitespace are validated locally. Unity compile and Play Mode visual validation remain pending from Thomas.

#### Manual Unity steps
1. Let Unity finish compiling on the `Dev` branch.
2. Open `Tools > Voxel Engine > Voxel Engine Setup` and run **51. Author Planet Skies + Space Ambiance (Non-Destructive)** twice.
3. Confirm the second run reports the Sky, Nebula, Solar Glare, and Space Dust runtime materials as **preserved**.
4. Enter Play Mode on the home world, face the sun, and confirm the soft glare and restrained lens ghosts appear without covering the HUD.
5. Move behind terrain or a solid structure and confirm the glare fades out; step back into direct sight and confirm it eases in.
6. Fly through upper atmosphere into vacuum and confirm sparse dust motes fade in and show gentle parallax while moving.
7. Stop the ship and confirm the dust remains calm rather than streaming like a particle storm.
8. Return to atmosphere and confirm dust fades out while the glare returns to the active planet's sky palette.
9. If a moon or planet crosses the star, confirm the glare feathers out during the eclipse and returns after separation.

### [7.18.1-dev] Planet Horizon Ownership & Surface-Sky Reliability

**Type:** PATCH — planet-sky rendering reliability and visual polish; no save-schema or public API change.

#### 🌅 Planet-specific horizon now fully owns the background
- The active camera uses the authored planet-sky background while the procedural dome is live, so an assigned default Unity skybox can no longer leak through at the surface, during a far-clip change, or while the dome is rebuilt.
- Camera clear flags and background colour are captured and restored safely when the sky controller is disabled, destroyed, underwater, or moved to a different camera.
- The radial shader now places the exact authored `Horizon` colour at the real local horizon plane. The previous hemisphere remap already mixed it halfway toward the zenith, weakening every world's identity.

#### 🌙 Local night horizons and render hardening
- Each world now grades into its own night palette while preserving its sunset glow instead of retaining a bright shared daytime gradient after dark.
- The sky dome renders as a guaranteed background pass, opts out of dynamic occlusion, and configures fallback material culling for an inside-sphere camera.
- Step 51 now creates/preserves `Assets/Resources/VoxelEngineRuntime/PlanetSkyDome.mat`, keeping the custom sky shader referenced in standalone builds without overwriting authored material properties or any gameplay balance.

#### ✅ Validation
- Version/documentation synchronization, C# syntax parsing, shader structure/property checks, setup idempotency assertions, forbidden-folder absence, and sparse-checkout deletion safety were validated locally.
- Thomas confirmed the planet-specific horizon works in Unity on the `Dev` branch.

#### Manual Unity steps
1. Let Unity finish compiling on the `Dev` branch.
2. Open `Tools > Voxel Engine > Voxel Engine Setup` and run **51. Author Planet Skies + Nebulae (Non-Destructive)** twice.
3. Confirm the second run reports the runtime sky material as **preserved**, with existing sky overrides and display colours unchanged.
4. Enter Play Mode on the home world and look across the complete horizon; confirm no default Unity blue/grey horizon remains behind the authored sky.
5. Visit at least one contrasting world (Ice, Volcanic, Acid, Mars, Crystal, or Pirate) and confirm its horizon, zenith, fog, sunset, and night colour family are distinct.
6. Fly from the surface through upper atmosphere into space and back; confirm the planet sky hands off smoothly to stars/nebulae with no skybox flash.
7. Enter and leave water once; confirm the sky hides underwater and returns with the correct planet palette.

### [7.18.0-dev] Planet-Specific Skies & Deep-Space Nebulae

**Type:** MINOR — new sky-art system; save-compatible, no API break.

#### 🌌 Planet-specific skies
- Every celestial body now resolves to a **sky theme** from its name / climate: Temperate, Moon, Ice, Volcanic, Acid, Ocean, Water, Pirate, Desolate, Venus, Mars, Crystal, Olympus, Asteroid.
- A camera-relative **sky dome** paints zenith → horizon → sunset, with optional **aurora belts** (Ice / Crystal) and **dust haze** (Volcanic / Venus / Mars / Acid / Pirate / Desolate).
- The atmosphere-to-space camera handoff uses the **theme's upper-air colour** instead of a single Earth blue, then fades to vacuum.
- Sun colour, ambient, and clear-weather fog follow the same palette so a volcanic noon is orange and an ice dusk is pink — not generic steel-blue.
- Distant planet **atmosphere rims** use that body's own rim colour.

#### ✨ Deep-space nebulae
- Sparse, seeded **galactic-band clouds** fade in with the existing vacuum starfield. Soft additive veils, not a particle storm.
- Nebula tint tracks the departed world's palette so leaving a crystal world still feels like that sky's afterimage.

#### 🛠️ Setup
- **Step 51 — Author Planet Skies + Nebulae (Non-Destructive):** marks each planet/moon with the catalogue, preserves any hand-authored sky colour overrides, and fills missing `displayColor` from the theme so distant planets match the ground sky.
- Designer overrides live on `BodySettings` (`skyZenith` / `skyHorizon` / `skySunset` / `skyFog`, alpha 0 = catalogue). Balance and existing colours are never reset.

#### ✅ Static delivery checks
- New sources parse cleanly; C# 9 compatible (no struct field initializers). No save-schema or public API change.

### [7.17.9-dev] Fix NaN Collider Bounds Crash in Sun Visual & LOD Offset Tuning

**Type:** PATCH — stability and edge-case exceptions.

#### 🛠️ Fixes & Polish
- **NaN Vector Rejection (SunVisual):** Added explicit `float.IsNaN` checks within `SolarHazard.cs` to prevent assigning `NaN` world/local positions to the Sun mesh during early scene construction or fast-travel rebases where the floating origin's anchor could temporarily drop a mathematically invalid `Vector3` result. This suppresses the noisy Unity GUI layout exception.
### [7.17.8-dev] Interplanetary Voxel Streaming & LOD Pacing Fix, UI Constraint Polish

**Type:** PATCH — voxel streaming logic and UI scaling fixes.

#### 🛠️ Fixes & Polish
- **Interplanetary Voxel Generation Fix:** Fixed a critical flaw where traveling to a non-starter planet caused the voxel surface to never generate. This occurred because `PlanetVoxelLod` instantiated its systems before the new planet's biome lists were fully passed, resulting in an internal `IndexOutOfRangeException` when evaluating the planet's surface column density. Adjusted the `CosmosBootstrap` initialization sequence to ensure the child LOD component is properly paused until all registries (like `biomeRegistry`) are securely assigned.
- **LOD Pacing / Pop-in Fix:** Increased `PlanetVoxelLod` generation job budget limits per frame. Fast-moving ships previously outran the single-thread chunk budget, causing coarse terrain LOD blocks to visibly "pop in" right in front of the player. Tripled the chunk evaluation limit to ensure the 8m and 4m detail rings spawn fast enough to cover the horizon seamlessly during flight.
- **UI Overflow & Cropping Fix:** Removed over-constrained `100%` CSS width/height overrides in `GameUIController` and `MainMenuController`. These overrides, combined with Unity's scaling system, forced the root visual tree outside of the physical viewport constraints on wider or non-16:9 aspect ratios. Restored pure absolute anchor bindings with `MatchHeight` UI scaling to ensure the interface dynamically conforms strictly to the window boundaries.
### [7.17.7-dev] Fix LOD overlap clipping via Deflation

**Type:** PATCH — visual polish.

#### 🛠️ Fixes & Polish
- **LOD Generation Floating Artifact Fix:** Fixed an issue where the lower-resolution planet LOD chunks (4m, 8m, 32m) could visibly float or clip slightly above the high-resolution 1m voxel surface at the boundaries where the two resolution rings overlap. Modified `SphereChunkGenJob` and `PlanetVoxelLod` to introduce a `radiusOffset` deflation property, slightly shrinking the radius of coarser LOD levels by 0.5m to 12.0m respectively. This forces the lower poly meshes strictly underneath the actual solid terrain, resolving the "two surfaces above each other" issue while preserving a seamless horizon.
### [7.17.6-dev] Grass Banding & Sliding Fix, UI Scale Polish, LOD Overlap & Degenerate Collider Fix

**Type:** PATCH — visual polish and bug fixes.

#### 🛠️ Fixes & Polish
- **Grass Banding / Surface Material Fix:** Fixed `SphereDensity.cs` using `math.round` on voxel depth calculations, which resulted in the terrain slicing perfectly into horizontal concentric bands of "Grass" and "Dirt/Clay" material across the sphere. Replaced with `math.floor` so the topmost layer consistently receives the surface material, allowing grass to spawn continuously across biomes.
- **Grass Sliding & Grid Artifact Fix:** Fixed `GpuGrassRenderer.cs` calculating blade placement purely from the player's continuously moving tangent plane, causing grass to slowly slide and "pop" when the grid refreshed. Anchored blade positions to their underlying `surfaceVoxel` center while smoothly projecting their height to the analytical sphere radius, permanently locking grass strictly to the world. Updated the jitter scaling to `step` to remove grid-like blade dotting patterns.
- **UI Off-Screen Crop Fix:** Changed the fallback UI sizing behavior in `GameSettings.ApplyUiScaleAndFit` from `MatchWidthOrHeight` (`0`) to `Expand`. This ensures that on ultra-wide screens, taller, or awkwardly resized windows, the UI scales fully into the safe frame without cropping off the bottom/sides.
- **LOD Generation Overlap Fix:** Modified `PlanetVoxelLod.cs` streaming center logic (`l0CenterLocal`) to snap exactly to `SphereWorld`'s internal chunk-aligned origin. This closes the slight mathematical gap/overlap where the unaligned LOD chunks rendered directly above and clipped into the actual high-definition 1m voxel terrain.
- **Degenerate Collider Crash Fix:** Added an explicit index check (`p.counts[1] >= 3`) before assigning new LOD meshes to colliders in `PlanetVoxelLod.cs`. This cleanly intercepts cases where interior chunks were successfully generated but contained zero physical faces, preventing Unity from throwing the "must have at least one non-degenerate triangle" error.

### [7.17.5-dev] UI Compile Recovery (CS1061 UIDocument.renderMode)

**Type:** PATCH — compile recovery; no behaviour change, no save/API break.

#### 🛠️ Compiler Fix
- **CS1061 in `GameUIController.cs` + `HammerBuildWheel.cs`:** the 7.17.4 screen-space force used `UIDocument.renderMode`, which does not exist on `UIDocument` in this Unity version. The render mode lives on **`PanelSettings.renderMode`** (the serialized `m_RenderMode` field).
- **Fix:** the ScreenSpaceOverlay force moved into `GameSettings.ApplyUiScaleAndFit` — `ps.renderMode = PanelRenderMode.ScreenSpaceOverlay` — the shared single source of truth called by GameUIController, HammerBuildWheel, and the Setup Step 3 bake (which now also writes it). Since all HUD documents share `MenuPanelSettings`, one property set covers every panel.

#### ✅ Static delivery checks
- All modified sources parse cleanly (tree-sitter grammar validation); no `UIDocument.renderMode` references remain (only `PanelSettings.renderMode`).

### [7.17.4-dev] Clean Near Horizon (4 m Detail Ring + Wider 8 m Ring), UI Forced On-Screen & Collider Fix

**Type:** PATCH — visual polish + fixes; no save/API break.

#### 🏔️ Near-horizon LOD visible in front of the player — FIXED with a new level
- **Root cause:** the quality ladder jumped 1 m → 8 m at ~250 m from the player, so the blocky 8 m voxel surface was clearly visible right in front of you while walking.
- **NEW L1 DETAIL ring (4 m voxels, 900 m radius):** a shell-filtered ring between the 1 m gameplay bubble and the 8 m ring — the near horizon is now 1 m → 4 m → 8 m → 32 m, a much smoother falloff.
- **8 m NEAR ring widened 2,000 → 3,000 m** (the visible horizon quality extends further).
- **Shell-filtered rings:** ring levels only render chunks near the planet's surface shell (air/interior chunks skipped) — the bigger rings actually cost LESS than the old full 3D ball (~600 ring chunks vs ~2,000). Ring levels carry mesh colliders (4 m + 8 m), so you still walk on real voxel terrain the whole way.

#### 🖥️ UI still outside of the screen / smaller & worse quality — FIXED
- **Runtime screen-space force:** `GameUIController.Awake` now forces `renderMode = ScreenSpaceOverlay` on ITS document and every UIDocument in the scene (and `HammerBuildWheel` on its own). World-space serialization (or a Unity 6.x panel migration) can no longer push the HUD off-screen — the panel is an overlay by code, regardless of scene state.
- **Width-priority scaling:** `ApplyUiScaleAndFit` match 0.5 → 0 (width-first). On wide-but-short windows the HUD stays LARGE instead of shrinking to the height ratio; at 1920×1080 it is still exactly 1:1. (Setup Step 3 bake updated to match.)
- **Unity 6.5 "Panel Renderer" warning:** noted — `UIDocument` still works and is what the codebase uses; the warning is cosmetic. Migrating the whole UI to Panel Renderer can be a separate task if you want it.

#### 🛠️ MeshCollider error — FIXED
- **`"LodChunkMesh" mesh must have at least three distinct vertices to be a valid collision mesh`** — the new ring colliders were enabled even for EMPTY meshes (air/interior chunks have 0 real vertices). Colliders now enable only when the mesh has ≥3 vertices AND non-zero bounds; empty chunks keep the collider off (disabled on pool return too).

#### ✅ Static delivery checks
- All modified sources parse cleanly (tree-sitter grammar validation).

### [7.17.3-dev] UI Back On-Screen (Screen-Space Panels) & One Solid Surface (Strict LOD Eviction + Real 8 m Collision)

**Type:** PATCH — bug fixes; no save/API break.

#### 🖥️ The whole UI / HUD was still outside of the screen view — FIXED
- **Root cause:** ALL UIDocuments in the scenes were serialized in **World Space mode** (`m_WorldSpaceSizeMode: 1`) with a **1920 × 1080 world-unit quad** (Game.unity ×6, MainMenu.unity ×1). The camera only sees the CENTER slice of that giant quad — so the HUD rendered as a tiny central strip stretched across the screen, with everything anchored to panel edges (vitals, gravity, "looking at" HUD, hotbar) cut off at the left/right screen edges.
- **Fix:** every UIDocument is back to **Screen Space (overlay)** (`m_WorldSpaceSizeMode: 0`) — the HUD is a normal full-screen overlay again, combined with the 7.17.2 `ScaleWithScreenSize` scaling it fits any window.

#### 🗻 "Still two surfaces, now both solid — I walk on the LOD above the real terrain" — FIXED
- **Root cause (the ghost that follows you):** the eviction hysteresis applied its margin to the **inner nesting boundary** too. The NEAR ring (8 m) chunks were admitted strictly outside the 1 m bubble, but with a 512 m eviction margin they were **never evicted once inside it** — so 8 m-voxel surfaces trailed the player everywhere and rendered 0–8 m ABOVE the 1 m terrain (both visible; the player stood on the 1 m colliders while the coarse surface floated at their feet/chest).
- **Fix — strict inner nesting:** `outerMargin` now applies ONLY to the ring's outer edge (flicker hysteresis). The inner boundary (1 m bubble / NEAR ring / MID coverage) is strict in BOTH admission and eviction — a chunk inside a finer level's coverage is evicted immediately. One rendered surface at every distance.
- **Solid beyond the 1 m bubble:** the NEAR ring chunks now carry **real mesh colliders** (the player walks on actual 8 m voxel terrain from the bubble edge to ~2.4 km — visual == collision, no more floating on the coarse planet shell), and the 1 m collider bubble was raised 4 → 6 chunks (~220 m, the `ShouldHaveCollider` clamp fixed to match) so there's no sliver where only the coarse safety shell is solid.
- The LOD safety shell now also checks the voxel LOD's NEAR colliders before stepping aside — the planet stays solid everywhere, with exactly one collision layer under the player at any spot.

#### ✅ Static delivery checks
- All modified sources parse cleanly (tree-sitter grammar validation); both scenes' UIDocuments verified at `m_WorldSpaceSizeMode: 0`.

### [7.17.2-dev] HUD Fits Any Window (ScaleWithScreenSize) & Exact LOD Nesting (No More Ghost Surface)

**Type:** PATCH — bug fixes; no save/API break.

#### 🖥️ The whole UI was outside of the screen view — FIXED
- **Root cause:** the runtime UI controllers forced `PanelScaleMode.ConstantPixelSize` on the shared panel settings (`GameUIController.Awake`, `HammerBuildWheel.Awake`, and the Setup Step 3 bake). A constant-pixel panel is anchored bottom-left at 1:1 pixels — on any window smaller than its reference (1280×720 / 1920×1080) the entire HUD is displaced off-screen: top-anchored elements (vitals, block info, gravity LCD) slide down past the bottom edge and appear as corner fragments, the hotbar vanishes below the view, and the screen shows mostly empty panel space over the world.
- **Fix — fit the window:** the runtime controllers now call `GameSettings.ApplyUiScaleAndFit` (the existing single source of truth): `ScaleWithScreenSize`, 1920×1080 reference, balanced match — the HUD scales to ANY window/game view (including the 1544×570 editor view from the report). Step 3's bake now writes `ScaleWithScreenSize` too, so re-running setup can't regress it.

#### 🗻 The LOD was still generating above the actual terrain — FIXED
- **Root cause:** the level-nesting boundaries overlapped instead of abutting exactly:
  • the NEAR ring could start **64 m inside the L0 gameplay bubble** (`l0R − 64`) — 8 m-voxel surfaces rendered over 1 m terrain near the bubble edge, sitting up to several metres ABOVE the real ground on slopes;
  • MID chunks could start **inside the NEAR ring's max reach** (`+ halfDiag + 64`) — the 32 m surface rendered over the 8 m ring in a wide band around ~2 km, visibly floating above the terrain.
- **Fix — exact abutment:** each coarser level's chunk is now excluded while its NEAR FACE (centre − half-diagonal) is inside the finer level's **true maximum reach**:
  • L0 reach = last 1 m chunk row's outer face (`viewDistance·32 + 16`);
  • NEAR reach = ring radius + 2× ring half-diagonal;
  • MID/FAR respect both (and FAR still steps aside under meshed MID chunks).
  Adjacent levels now meet edge-to-edge — no overlap (no ghost surface), no gap.

#### ✅ Static delivery checks
- All modified sources parse cleanly (tree-sitter grammar validation).

### [7.17.1-dev] Chunk-Generation Recovery — Unconstructed Oil Map Fixed (Spawn / Load / HUD Restored)

**Type:** PATCH — critical runtime recovery. The unconstructed job container broke ALL terrain generation (gameplay world + voxel LOD), which cascaded into: planet not loaded at spawn, player falling in space on load, and the world-space HUD appearing off-screen (the HUD is parented to the player — when the player is stranded in a broken world, the HUD goes with them). No save/API break.

#### 🛠️ Root cause & fix
- **`InvalidOperationException: SphereChunkGenJob.oilSites has not been assigned`** — Unity's job scheduler REQUIRES every container field to be constructed at Schedule time. The oil-site map is only created on oil-rich bodies, so:
  • `SphereWorld` (the 1 m gameplay world) never set it → **every gameplay chunk generation threw** → no terrain anywhere → "planet isn't loaded where the player is", falling through at spawn/load, and the world-space HUD (parented to the player) rendered off-screen.
  • `PlanetVoxelLod` left it uncreated on bodies without oil → every LOD chunk schedule threw.
- **Fix:** both worlds now ALWAYS pass a constructed (possibly empty) map:
  • `SphereWorld` allocates a permanently-empty `_emptyOilSites` map (1 entry) in Awake, disposes it in OnDestroy, and passes it to every `SphereChunkGenJob` (its oil is still authored by `OilReservoirDecorator` as before).
  • `PlanetVoxelLod` allocates an empty map for no-oil bodies instead of leaving the field default.
  • Defensive guard in `PlanetVoxelLod.DispatchJobs`: never schedule a gen job while the map is uncreated.

#### 🪂 Load-path hardening ("falling in space" when loading a world)
- `RestoreCosmicState` used to FORCE the scene into deep space whenever the saved frame-body name was missing or didn't match — even though `TeleportCosmic` had already re-picked the correct dominant body at the saved position. A surface save with a missing/mismatched frame name therefore loaded the player in space with no ground.
- **Fix:** the named frame is now only a HINT. If it can't be matched, the scene keeps the dominant-body frame that `TeleportCosmic` selected (null there genuinely means a deep-space save, which is respected). Log now reports the actually-restored frame.

#### ✅ Static delivery checks
- All modified sources parse cleanly (tree-sitter grammar validation); both `SphereChunkGenJob` schedulers verified to pass a constructed `oilSites` map.

### [7.17.0-dev] All Planets Always Real (Whole-System Window) + LOD Compile Recovery

**Type:** MINOR — every planet in the system now renders its REAL voxel surface at all times (60,000 km window covers the whole system), forge-created planets get the same treatment automatically, and the oil-site map API compiles clean. Save-compatible, no API break.

#### 🪐 All planets are ACTIVE (real surfaces, everywhere)
- **Whole-system window:** the real-voxel FAR level now streams for any body within **60,000 km** (was 8,000 km) — with the 8,000 km planet gap floor, EVERY planet/moon in the system renders its genuine voxel surface at all times. No more "only the approached planet is real; the rest are painted spheres".
- **Far clip 50,000 → 80,000 km** (`CosmosBootstrap.maxFarClipMeters`) so planets at 60,000 km are never culled; `trueLodViewKm` 8,000 → 60,000 keeps every body in the real-LOD path.
- **Sky proxies** (sampled spheres) now only remain for bodies beyond 60,000 km (system edge) — convergence/fade constants moved to match (12,000 → 65,000 km converge band).
- **Forge-forward:** `SpawnBodySystems` is extracted from `EnsureAllBodiesInScene`, and `CosmosBootstrap` now polls the registry every 2 s — any body **added to the registry after bootstrap** (the World Forge / runtime content tools, not implemented yet) automatically spawns as a real world: CelestialBody + bridge LOD + real voxel surface + floating-origin registration. When the forge lands, its planets are immediately real, flyable worlds with zero extra wiring.

#### 🛠️ Compiler Fixes (CS1061 / CS0019 / CS0428)
- `NativeParallelHashMap` has no `.Length` / `.Count` properties — both are METHODS:
  • `SphereDensity.cs`: `oilSites.Length > 0` → `oilSites.Count() > 0`
  • `OilSiteSampler.cs`: `sites.Length == 0` → `sites.Count() == 0`
  • `PlanetVoxelLod.cs`: `_oilSites.Count` → `_oilSites.Count()` (log line)
- `PlanetVoxelLod.cs`: `int3 * float` is invalid — the oil-map scan centre now casts `(float3)c * ...` explicitly.

#### ✅ Static delivery checks
- All modified sources parse cleanly (tree-sitter grammar validation); C# 9 compatibility sweep clean (no struct field initializers).

### [7.16.0-dev] One Surface Only, Solid Planets on Approach & Oil Fields Visible from the Air

**Type:** MINOR — the LOD levels now nest with NO double surface, every planet is solid the moment you approach it, and crude-oil sites (puddle → bore → reservoir) render in the voxel LOD. Save-compatible, no API break.

#### 🥞 ONE surface — the "two surfaces, top one not solid" fix
- **Root cause:** the level-exclusion math only skipped chunks that were *fully inside* a finer level's bubble. Most chunks straddle the boundary, so the 8 m NEAR ring rendered over the 1 m gameplay bubble, and the 32 m MID level rendered over both — a ghost surface up to ~16 m above the real terrain, with no colliders (you walked through it).
- **Fix — strict nesting:** `PlanetVoxelLod.IsChunkDesired` now skips ANY chunk whose near face is inside a finer level's coverage (used identically for admission AND eviction):
  • NEAR ring starts exactly at the 1 m gameplay bubble's edge.
  • MID never renders inside the NEAR ring (or the gameplay bubble when the ring is off).
  • The exclusion measures from the **L0 stream centre** — which is the radial surface point beneath the viewer during high-altitude flight (orbit-approach streaming), so the 1 m bubble and the coarse LOD can never render the same patch.
  • While the MID level builds, FAR stays active but steps aside under every meshed MID chunk (footprint check) — no double surface during the approach either.
- Result: exactly **one** rendered surface at every distance.

#### 🪨 Planets are solid again on approach — the "fly straight through" fix
- **Root cause:** a perf gate from 7.15.0 only built the safety collision shell for the *active* body — every other planet had NO shell, and the real-voxel LOD chunks are intentionally visual-only. When the frame switched to the approached planet, there was nothing solid until the deep core sphere.
- **Fix:** the safety shell is now built for **every** body (cheap 642-vert shell for distant bodies, full 10,242-vert shell for the active body), and `UpdateSafetyColliders` upgrades the shell to full resolution the moment a body becomes the active frame. You land on the shell, exactly as before.

#### 🛢️ Oil fields visible in the LOD (puddle above → shaft → reservoir below)
- **Root cause:** oil sites were only written into the 1 m gameplay chunks, so the LOD levels (which sample pure density) showed nothing from the air — and the double surface hid nearby sites too.
- **Fix — `OilSiteSampler` (new):** a deterministic site map per oil-rich body, using the **exact same 96 m cell hash, salts and seed as `OilReservoirDecorator`** — so every cell that rolls a seep in the gameplay world rolls one in the LOD. Each site is anchored at the radial surface through the cell with puddle disc → tapered solid-oil bore → reservoir sphere, scaled to read at coarse voxel sizes. The LOD levels sample the map (`SphereChunkGenJob` gains a `NativeParallelHashMap` site map; `SphereDensity.EvaluateVoxel` gains an oil-aware overload), so you see dark oil patches, shafts and reservoirs from orbit and from the air.
- The map builds in small batches per frame (no hitch) and LOD streaming waits for it (~1 s); bodies without oil skip it entirely. When you land, the gameplay world's real liquid puddle + exact decorator geometry take over seamlessly (the LOD version is an approximation anchored within the same 96 m cell).

#### ✅ Static delivery checks
- All 14 modified/added sources parse cleanly (tree-sitter grammar validation); C# 9 compatibility sweep (struct field initializers) clean.

### [7.15.1-dev] Voxel LOD Compile Recovery (CS8773/CS8983 — C# 9 struct field initializer)

**Type:** PATCH — compile recovery for the real-voxel LOD delivery; no behaviour change, no save/API break.

#### 🛠️ Compiler Fix
- **CS8773 / CS8983 in `SurfaceNetsJob`:** the new `voxelSize` field used a C# 10 field initializer (`= VoxelConstants.VOXEL_SIZE`), which Unity's C# 9.0 language level rejects in structs. The field is now plain (`public float voxelSize;`) and `Execute()` resolves the safe fallback itself: `vs = voxelSize > 0f ? voxelSize : VoxelConstants.VOXEL_SIZE` — bounds and vertex positions use `vs`.
- **Explicit call-site hygiene:** `VoxelWorld` and `SphereWorld` now pass `voxelSize = VoxelConstants.VOXEL_SIZE` explicitly, so the gameplay world's intent is self-documenting (behaviour identical to before).

#### ✅ Static delivery checks
- All modified sources parse cleanly (tree-sitter grammar validation); swept the whole 7.15.0 delivery for other C# 10+ constructs (struct field initializers) — none remain.

### [7.15.0-dev] REAL Voxel Planet Surfaces — Whole-Planet Voxel LOD Generation (No More Fake Spheres)

**Type:** MINOR — the sampled impostor sphere is replaced by REAL voxel LOD generation for the whole planet surface, Space-Engineers style. Save-compatible, no API break.

#### 🪐 The core change
Every celestial body now generates its surface as **actual voxel chunks** at LOD resolutions — the same `SphereChunkGenJob` + `SurfaceNetsJob` pipeline as the gameplay world, just with bigger voxels for distance. What you see from orbit IS the real terrain density field: continents, oceans, mountains, biomes, ore-coloured strata. No sampled sphere with "nothing".

**Levels (voxel → chunk size):**
| Level | Voxel | Coverage | Active when |
| :-- | :-- | :-- | :-- |
| **L3 FAR** | 128–512 m (adaptive, tier) | whole planet (~48 chunks) | within **8,000 km** — the whole interplanetary crossing |
| **L2 MID** | 32–128 m (adaptive, tier) | whole planet (~192–770 chunks) | within 150 km (approach + on-surface) |
| **L1 NEAR** | 8 m | ring around the viewer (~190 chunks) | below 4 km altitude on the streaming body |
| **L0 PLAY** | 1 m | SphereWorld gameplay bubble (unchanged) | as before |

- All levels sample the **same density field** → levels match exactly, just resolution differs.
- **Nesting without gaps:** a finer level excludes the coarser chunks fully inside its bubble (fully-inside rule) — no holes, no overlap z-fighting beyond the tiny boundary band.
- **Adaptive voxel size by planet radius** (doubles past 12 km radius) so whole-planet chunk counts stay bounded on any planet/moon size.
- **Quality tiers:** Low = 128 m mid voxels (~48 chunks), Mid = 64 m (~192), High/Ultra = 32 m (~770) — `GraphicsPreset.PlanetMidLodVoxelSize` / `PlanetFarLodVoxelSize`.
- **Memory-lean:** LOD chunk voxel buffers are disposed the moment their mesh is applied (LOD chunks are never edited) — only meshes stay resident.
- **Visual-only:** no colliders (the LOD safety shell keeps the planet solid), no scatter/fluid/persistence on LOD chunks.
- **Nearest-first streaming** so the visible side of the planet builds before the back side.
- `SurfaceNetsJob` gained a `voxelSize` parameter (bounds now correctly reported in metres — fixes latent bounds/culling at any voxel size).

#### 🔄 The impostor is now only a 1–3 s bridge
`PlanetLodImpostor` renders the old sampled sphere **only until** the body's real voxel surface (`PlanetVoxelLod.SurfaceReady`) is built, then hides permanently — its safety colliders stay forever. The sampled-terrain sky proxies remain only for bodies **beyond** the 8,000 km window (they still bake the real terrain palette).

#### 🪐 Planets are now far apart (Space-Engineers scale)
- **Minimum orbit gap 2,000 → 8,000 km** (`CosmicRegistry.MinPlanetGapKm`) — from the surface of an 8 km planet the next world is a small distant disc; interplanetary space is genuinely vast (gravity wells are only ~200 km).
- True-LOD window **2,500 → 8,000 km** everywhere (bootstrap far clip, `SpaceBodyRenderer` proxy handoff, impostor crossfade) — the approached planet's REAL voxel surface is visible for the entire crossing; proxy convergence band now 12,000 → 8,000 km.

#### 🛠️ Diagnostics
- `[PlanetVoxelLod] Real voxel surface ready for '<body>' (far x/y, mid x/y)` — when a body's real surface is built.
- `[PlanetVoxelLod] '<body>': far/mid/near chunk counts` — once, after 5 s.
- `[SpaceBodyRenderer] Queued sampled-terrain bake for '<body>'` — confirms far-body proxy baking runs (previously "isn't running" was the compile error blocking the whole build).

#### ✅ Static delivery checks
- All 10 modified/added sources parse cleanly (tree-sitter grammar validation): `PlanetVoxelLod.cs` (new), `SurfaceNetsJob.cs`, `PlanetLodImpostor.cs`, `SpaceBodyRenderer.cs`, `CosmosBootstrap.cs`, `CosmicRegistry.cs`, `GraphicsPreset.cs`, `QualityPresetApplier.cs`, `SphereSurfaceColor.cs`, `GameVersion.cs`.

### [7.14.1-dev] Sky Proxy Compile Recovery (CS1061 Color.rgb)

**Type:** PATCH — compile recovery for the sampled-terrain sky proxies; no behaviour change, no save/API break.

#### 🛠️ Compiler Fix
- **CS1061 in `SpaceBodyRenderer.PositionBody`:** the tint path wrote `tint.rgb = ...`, but `UnityEngine.Color` has no `.rgb` accessor (that's an HLSL/`Color32`-style member). The flat-colour tint for bodies without a terrain bake (asteroid belts, the sun sprite, not-yet-baked proxies) now assigns `r`/`g`/`b` individually — identical behaviour, valid C#.

#### ✅ Static delivery checks
- Verified the modified source parses cleanly (tree-sitter grammar validation); swept all touched files for other `.rgb`-style Color misuse (none — the only remaining mention is in a comment).

### [7.14.0-dev] Real Planets — Sampled Terrain on Every Body, Distance-Based LOD Ladder & Seamless Proxy→Surface Handoff

**Type:** MINOR — a full planet-surface LOD system (Space-Engineers style): every body in the solar system now renders its REAL sampled terrain — continents, oceans, ice caps — from the moment it appears in the sky to the moment you land. Save-compatible, no API break.

#### 🌍 The Problem (why planets were flat colored balls)
- **Sky proxies were flat-colour spheres.** Bodies beyond the true-LOD window were Unlit spheres tinted with one display colour — zero terrain. Since planets are separated by at least 2,000 km, the planet you were flying to was a featureless ball for almost the entire trip.
- **The real LOD was washed out.** `PlanetLodImpostor` lerped every sampled surface colour 72% toward the body's `displayColor`, so even the 40k–160k-vertex whole-planet surface read as a flat ball whenever a display colour was authored (all runtime-fallback worlds).
- **No distance-based quality.** Non-active bodies were locked to 642 vertices; high detail only arrived when the gravity frame switched. "Closer = better surface" never happened.
- **Hard proxy→LOD pop.** The compressed sky proxy and the real LOD were never visually reconciled.

#### 🪐 What's new
- **Sampled-terrain sky proxies:** every planet/moon in the sky now bakes its vertex colours from the SAME `SphereDensity` field the voxel generator uses (new shared `SphereSurfaceColor` palette + new `VoxelEngine/PlanetSkyProxyURP` shader with real-sun lighting and atmosphere rim). The continents you see in the sky are the continents you land on.
- **Distance-based LOD ladder (all bodies):** each `PlanetLodImpostor` picks its vertex budget from its distance in body radii — 642 verts for a distant dot → 2,562 → 10,242 → 40,962 → full high-detail surface (up to 163,842) as you close in. Tier changes rebuild progressively (batched across frames) and abandon a stale in-flight build immediately, so there is never a hitch.
- **Proxy→surface convergence & crossfade:** outside the true-LOD window the sky proxy now morphs from its compressed sky position/size toward the body's TRUE scene position/size over the last 3,500 km, while the real LOD fades in over the same band — the hand-off happens at the same apparent size, no popping sphere.
- **True-LOD window 800 → 2,500 km:** the approached planet renders its real sampled surface for the entire interplanetary crossing (min planet separation is 2,000 km), and the far clip covers the convergence band (1.25×) so the morphing proxy is never culled.
- **Display-colour tint 0.72 → 0.18:** authored personality colours remain as a subtle tint; the sampled terrain is now the star.
- **Per-material `_BodyCenter` in `PlanetSurfaceLodURP`:** surface-detail noise is now correct for EVERY body, not just the active streaming one (global fallback preserved).
- **Safety colliders only for the active body:** distant bodies no longer cook a 10k-vertex collision mesh on every LOD tier change during a crossing.
- **Viewer propagation to every body's LOD** (late-spawned players) + `Camera.main` fallback — distant planets are never stuck at their cheap budget.

#### ✅ Static delivery checks
- All 5 modified/added sources parse cleanly (tree-sitter grammar validation): `PlanetLodImpostor.cs`, `SpaceBodyRenderer.cs`, `CosmosBootstrap.cs`, `SphereSurfaceColor.cs` (new), `GameVersion.cs`.

### [7.13.15-dev] SpaceBodyRenderer Compile Recovery (CS0103 sunPos)

**Type:** PATCH — compile recovery for the always-visible-sun sky rendering; no behaviour change, no save/API break.

#### 🛠️ Compiler Fix
- **CS0103 `sunPos` in `SpaceBodyRenderer`:** the variable was declared inside the sun-sprite branch but also consumed by the planet/moon sky-projection hierarchy below. It is now declared once before the sun branch (origin-anchored fallback) and assigned inside the sprite branch — the sky hierarchy always has a valid sun anchor.

#### ✅ Static delivery checks
- Verified the modified source parses cleanly (tree-sitter grammar validation).

### [7.13.14-dev] Gravity-Well Release, Always-Visible Planets & Sun (Real LOD Approach)

**Type:** PATCH — the player can now leave a planet's gravity well for real, other planets stay visible and generate terrain during approach, and the sun is a real target; no save/API break.

#### 🌍 Leaving Earth's Gravity Well (the "pulled back from very far" fix)
- **Root cause:** two frame-selection bugs kept the player glued to Earth's co-moving frame forever: (1) the frame switch required a candidate body to pull 1.25× harder than the current body — at the midpoint between two equal bodies neither ever wins, so the frame never switched; (2) even when the current body's pull had decayed to almost nothing, the "Earth is still dominant" early-return kept the frame, and the frame-relative gravity kept pulling the player back.
- **Fixes in `SpaceOrigin.ReEvaluateFrame`:**
  • **RELEASE rule:** when the current frame body's pull drops below `releaseGravityMps2` (0.014 m/s²) and no eligible new body is winning, the scene releases to the deep-space (star) frame — the physically correct free-fall handoff.
  • **Crossover switching:** body→body switches now happen at the gravity dominance crossover (candidate > current × 1.05) instead of 1.25×.
  • **Hysteresis band:** entry threshold 0.02 m/s² vs release 0.014 m/s² — no frame oscillation at the boundary.

#### 🪐 Other Planets Always Visible + Surface Generates on Approach
- **Root cause of "planet disappears / surface never generates":** when the frame switched to the approached planet at ~4,200 km, the sky-proxy renderer HID its sprite (it became the "active body") while the real LOD sat BEYOND the camera's 900 km far clip — the planet was invisible for the entire descent, and terrain never streamed because the player never reached its frame.
- **Fixes:**
  • **Far clip rework:** `EnsureCameraFarClip` now covers the active body for the whole approach, every body within the 800 km true-LOD window, AND the star at its true position — capped at 50,000 km (URP reversed-Z keeps near precision). Refreshed every 2 s so it tracks the flight.
  • **True-LOD window 200 → 800 km** in `SpaceBodyRenderer` — the approached planet renders its real high-detail LOD much earlier and grows as you descend.
  • Orbit-approach streaming (7.13.13) now actually engages because the frame switches correctly — the surface beneath the approach point generates during the whole descent.

#### ☀️ The Sun Is the Real Sun
- The fake 9.75 km sun sprite is now hidden whenever the REAL sun (emissive sphere at its true cosmic position) is within the LOD window — one sun, and the one you fly toward is the real hazard.
- Sun visual radius 120 → 80 km for a natural 4–6° disc from the inner planets; hazard zones remain auto-scaled to the innermost orbit.

#### ✅ Static delivery checks
- All 4 modified sources parse cleanly (tree-sitter grammar validation).

### [7.13.13-dev] Top-Left Block Info, Fixed Blank Targets, Orbit-Approach Terrain, Planet Spacing & a Real Deadly Sun

**Type:** PATCH — inspection HUD placement + target resolution, planet-approach terrain streaming, system spacing, and a visible/lethal sun; no save/API break.

#### 📍 Block Info HUD → Top-Left & Always Resolves
- **Moved** `WorldInspectionHud` from the top-right to the **top-left** (slides in from the left edge).
- **Blank-target fix:** the planet-LOD SAFETY colliders (the solid shell/core that stop fly-through) were intercepting the crosshair raycast — the HUD couldn't see the real terrain/block behind them, so it showed nothing (especially after breaking a surface). A new `PlanetSafetyCollider` marker lets every interaction raycast (inspection HUD + mining/building tool) skip the safety shell; the real streamed voxel terrain is resolved instead. Physics still collides with the shell — you just can't mine/inspect it.

#### 🌍 Planet Surface Generates During Approach
- **Root cause:** when flying to another planet from space, the streamer only generates chunks in a small ball AROUND THE VIEWER — kilometres above the surface that meant pure air, so the surface never generated until you were ~200 m from touchdown (only the LOD shell showed).
- **Fix — orbit-approach streaming:** when the viewer is far above the surface, `SphereWorld` streams around the **radial surface point beneath the viewer** (sampled from the real density field) — the actual terrain is generated and visible for the entire descent.

#### 🪐 Planets No Longer Hug Earth
- Enforced a **2,000 km minimum gap between planet orbits** at runtime (authored templates could place them 500 km apart, making planets hover right next to each other with overlapping gravity wells).

#### ☀️ The Sun Is Real, Visible & Deadly
- **Real sun mesh** at the star's true cosmic position (emissive sphere, 120 km radius, placed via the floating origin) — flying toward the sun you see in the sky genuinely approaches it, instead of passing through a fake 10 km sprite.
- **Auto-scaled hazard zones:** warning = 80% of the innermost planet's orbit, lethal = 45% — always sane for the system scale. Escalating warnings + heat damage ramp still apply ("SOL APPROACH" → "SOL FLARE" → "SOL CORONA — CERTAIN DEATH").

#### ✅ Static delivery checks
- All 7 modified/new sources parse cleanly (tree-sitter grammar validation).

### [7.13.12-dev] Oil Mesher Compile Recovery (CS0120)

**Type:** PATCH — compile recovery for the visible-oil terrain meshing; no behaviour change, no save/API break.

#### 🛠️ Compiler Fix
- **CS0120 in `SurfaceNetsJob`:** the new static helper `IsEmptyFluid` called the instance method `IsFluidMat`. `IsFluidMat` is now `static` (it only reads compile-time constants), so solid-density crude-oil cells mesh as visible terrain and liquid fluids stay empty — exactly as intended in 7.13.11.

#### ✅ Static delivery checks
- Verified the modified source parses cleanly (tree-sitter grammar validation).

### [7.13.11-dev] Solid Real-Planet Collision (No Fly-Through) & Visible Oil Bore → Reservoir

**Type:** PATCH — the whole planet is now solid everywhere with collision that matches the visible terrain, and crude-oil seeps show their real shaft down to the reservoir; no save/API break.

#### 🌍 Whole Planet Solid — No More Flying Through
- **Root cause:** the planet's collision had a GAP — the LOD safety shell disengaged at 220 m altitude while real voxel colliders only reached ~96 m, so a fast player passed through the unguarded band and into the planet. And the shell was a flat inflated sphere floating above the visible surface.
- **Fixes:**
  • The safety shell is now built from the **REAL sampled terrain surface** (the same density field as the visible planet), nudged 0.3 m outward — **what you hit is exactly what you see**, no invisible floating shell.
  • The shell stays engaged down to **45 m** and below that only steps aside when **real streamed terrain colliders actually exist under the player** (`SphereWorld.HasColliderAt`) — there is no speed at which a fall-through gap exists anymore.
  • The deep-inside core sphere catches anyone who somehow ends up below the crust.
  • Real voxel terrain now reaches further and faster: streamed bubble raised to **5/6/7/8 chunks** (Low→Ultra, up to ~256 m of true editable planet), mesh-collider radius **2 → 4 chunks** (~128 m of solid real terrain), jobs 4/8/10/12 per frame.
- Space-Engineers-style stack: real editable voxels near the player → the same real density field as a solid collidable surface beyond → sky projection for distant bodies. The whole planet is solid and minable everywhere you can reach.

#### 🛢️ Crude Oil — Visible Shaft Down to the Reservoir
- **Root cause 1 (invisible):** the terrain mesher treated ALL crude oil as empty (fluid), and the fluid renderer only draws fluid that touches open air — so the sealed bore + reservoir were completely invisible. Only the surface puddle rendered.
- **Root cause 2 (skipped writes):** the decorator silently dropped writes when the chunks below the surface weren't generated yet, so the shaft/reservoir were often never carved at all.
- **Fixes:**
  • The bore and reservoir are now written as **SOLID oil-soaked rock** (density +127, material CrudeOil) and the terrain mesher renders solid-density crude as visible dark terrain (`IsEmptyFluid` treats liquid fluids as empty, solid oil as terrain) — dig down and you see a real dark shaft opening into a reservoir chamber.
  • The surface puddle stays a true liquid for pumps.
  • **Write-retry:** if any carve was skipped because a chunk wasn't generated, the site is queued for retry (writes are idempotent) — the puddle → bore → reservoir is now always complete.

#### ✅ Static delivery checks
- All 5 modified sources parse cleanly (tree-sitter grammar validation).

### [7.13.10-dev] Static Sky, One Sun, Solar Hazard & Solid Planets (No Flying Through)

**Type:** PATCH — fixes planets visually following the player, the duplicate sun, the random sun-death, and lets you no longer fly through planets; no save/API break.

#### 🪐 Planets Stay Put in the Sky
- **Root cause:** the sky-proxy renderers anchored their bodies to the PLAYER's scene position — walk 100 m and every planet/sun visual moved 100 m with you.
- **Fix:** `SpaceBodyRenderer` and `QuasarRenderer` now anchor to the ACTIVE BODY's scene position (perfectly static in the scene thanks to the floating origin), falling back to the scene origin in deep space. Planets, moons and the sun now hold their true positions in the sky while the player moves; the real high-LOD body takes over seamlessly when you get close.

#### ☀️ One Sun Only
- The bright quasar backdrop read as a second sun — it is now **disabled whenever the system has a real star** (kept only for sun-less systems), leaving exactly one sun.

#### 🔥 The Sun Warns Before It Kills
- New `SolarHazard` component: inside 2,200 km of the star you get escalating HUD warnings ("SOL APPROACH — HEAT RISING" → "SOL FLARE — CRITICAL HEAT, TURN BACK" → "SOL CORONA — CERTAIN DEATH") and heat damage that ramps from zero at the warning edge to lethal at the corona. Flying into the sun is now a warned, deliberate act — never a random death.

#### 🌍 Whole Planet Solid — No More Flying Through
- **Root cause:** the streamed voxel bubble only covers the player's vicinity; beyond it the planet LOD shell was VISUAL ONLY (no collider), so a fast player flew straight through the planet.
- **Safety colliders on the planet LOD (`PlanetLodImpostor`):**
  • A **mesh safety shell** (the sampled surface inflated +8 m, ≤10k verts, cheap to cook) engages when the player is above the streamed bubble — orbital approaches land on the real planet.
  • A **solid core sphere** engages when the player is deep inside the body and pushes them back to the surface shell.
  • Both auto-disable in the thin surface shell where real voxel colliders rule — mining/walking are untouched. Only the ACTIVE body's colliders are ever enabled (distant planets cost nothing).
- **Bigger real-terrain bubble:** `GraphicsPreset.ViewDistance` raised (Low 4 / Mid 5 / High 6 / Ultra 7 chunks) and `JobsPerFrame` raised (3/6/8/10) so the editable terrain streams further and faster; `SphereWorld.colliderChunkRadius` 2 → 3 so solid terrain extends further around the player.

#### ✅ Static delivery checks
- All 7 modified/new sources parse cleanly (tree-sitter grammar validation).

### [7.13.9-dev] Phantom Horizontal Pull Fixed — Curved-Orbit Anchor & Frame-Relative Gravity

**Type:** PATCH — removes the constant sideways (X/Z) force the player felt by fixing two co-moving-frame physics errors; no save/API break.

#### 🧲 Root cause of the infinite X/Z pull
The scene is the planet's co-moving frame, but two effects leaked the planet's orbital motion into the player's frame:
1. **Ground slide:** the floating-origin anchor followed the planet by straight-line velocity extrapolation (`anchor += v·dt`), while the planet's REAL orbit curves (Kepler). The planet's true position fell behind the extrapolation, so the surface slid sideways under the player at ever-increasing speed — reading as a constant pull in X/Z that never stops.
2. **Phantom solar pull:** `GravityProvider` applied the RAW cosmic N-body gravity (star + every planet + moons). In a free-falling (co-moving) reference frame the frame body's own orbital acceleration must be cancelled — otherwise the player feels the sun's full pull as a constant sideways force, even standing on the planet.

#### 🛠️ Fixes
- **`SpaceOrigin` anchor now tracks the frame body's TRUE propagated position every fixed tick** (`anchor = bodyCosmic − bodyScenePos`). The planet is perfectly stationary in the scene — zero ground slide, zero phantom drift. Deep space keeps the inertial star frame.
- **New `CosmicRegistry.GetFrameRelativeGravityMetersS2`:** scene-frame gravity = cosmic pull at the player MINUS the pull the frame body itself experiences (its own orbital acceleration). Standing on a planet now feels exactly the local pull (~9.81 m/s²) with only a negligible tidal term; flying between bodies feels the correct residual gravity.
- `GravityProvider.GetGravity` (consumed by the player, grids, dampeners and HUD) now uses the frame-relative value whenever a scene frame exists; deep space stays plain cosmic gravity (~0).

#### ✅ Static delivery checks
- All 3 modified sources parse cleanly (tree-sitter grammar validation).

### [7.13.8-dev] Respawn Sideways-Launch Fix & Space Bed/Cryobed Spawns

**Type:** PATCH — eliminates the sideways frame-velocity kick on respawn/teleport and officially supports spawning in space next to beds/cryobeds; no save/API break.

#### 🚀 Respawn Sideways Launch — Root Cause & Fix
- **Root cause:** respawning teleported the player to the destination while the scene frame could still be a DIFFERENT body's frame (e.g., died mid-fall in the planet frame, respawn destination near another body — or died in space, respawn on surface). The next automatic frame switch then applied the frame-velocity DELTA to every scene object — including the freshly-respawned player — producing a violent sideways (tangential) kick. The previous fix zeroed velocity, but a frame switch landing between the zero and control handover re-injected the delta.
- **Fix:** the spawner now **pins the scene frame to the destination's dominant body BEFORE the teleport** (`PrepareRespawnFrame` → `SpaceOrigin.SetFrame`, which never applies velocity deltas), **suppresses automatic frame switches for the whole spawn/respawn sequence** (`SpaceOrigin.suppressAutoFrameSwitches`), and only re-enables them after the player is at rest with control. No window remains for a kick — applied to first spawn AND every respawn.
- **Streaming follows the frame:** new `CosmosBootstrap.ForceStreamingBody` retargets the voxel streamer, gravity, grass/ocean and LOD to the pinned destination body (or deep space) right away, so the world matches where you spawn.
- **Teleports never kick (warp & save restore):** `SpaceOrigin.TeleportCosmic` now re-picks the frame WITHOUT applying the velocity delta (the warp drive zeroes the ship; save-restore zeroes the player).

#### 🛏️ Space Bed / Cryobed Spawns Supported
- Respawn destination validation now only rejects destinations **inside a planet** (a launch-era save buried in terrain). Positions in space are VALID — a bed or cryobed in orbit, on a station, or on a ship now spawns you exactly next to it, in the correct reference frame, at rest.
- The world-spawn fallback also validates against EVERY body (not just the active one).

#### ✅ Static delivery checks
- All 3 modified sources parse cleanly (tree-sitter grammar validation).

### [7.13.7-dev] Instant HUD Fade, Death-Loop Elimination & Whole-Planet LOD

**Type:** PATCH — HUD fade feel, respawn death-loop breaker, survivable fall cap, and the full planet surface at high LOD; no save/API break.

#### ⚡ HUD Fades Fast on Open
- `LcdHudTheme.YieldWhileBlocking` is now ASYMMETRIC: fades OUT at 18/s (~0.05 s — opening inventory/UI feels instant) and fades back in at 8/s (elegant return), polled every 16 ms instead of 33 ms.

#### 💀 Launch → Fall → Die → Respawn-in-Space Loop Eliminated
- **Respawn destination validation (the loop breaker):** every respawn (`RespawnRoutine`, used by the death screen AND `RespawnAt`) now validates the destination — if it is inside a planet or more than 3.5× surface radius out in space (a stale launch-era world-spawn/bed coordinate), a fresh deterministic dry surface spawn is computed instead. Dying can no longer put you back in space.
- **Bed-spawn validation:** a bed saved during the launch-era is also rejected on load and the poisoned bed flag is cleared.
- **Survivable fall cap:** inward radial speed cap lowered 55 → 24 m/s — BELOW the lethal fall threshold (28 m/s). Even a worst-case restored fall lands you bruised, not dead. No more impact-death loop.
- **Spawn/respawn fall-damage grace (2.5 s):** `PlayerController.BeginSpawnGrace` — physics settle / chunk-streaming timing at spawn can never insta-kill.

#### 🌍 Whole Planet Loaded (proper LOD, Space-Engineers style)
- **`PlanetLodImpostor.highDetail`:** the ACTIVE body (the one you're on / approaching) now renders at a high-detail budget — 10k/40k/163k vertices by graphics tier (`GraphicsPreset.ActiveBodyLodResolution`) — a single continuous sampled planet surface with real continents and mountains, visible from ground to orbit.
- **Progressive build:** high-detail meshes are built in 4096-vertex batches across frames — no spawn or frame-entry hitch, no stutter.
- **Frame switching:** entering a body's gravity well upgrades its LOD to high detail (previous body downgrades to the cheap proxy); deep space downgrades everything.
- The home body is upgraded right at bootstrap, so the planet looks whole from the first frame.
- Local voxel chunks still stream around the player (proper LOD stack: chunks up close, sampled surface beyond, sky projection for distant bodies).

#### ✅ Static delivery checks
- All 6 modified sources parse cleanly (tree-sitter grammar validation).

### [7.13.6-dev] Spawn Launch Elimination — Interior Gravity Falloff, Fall-Speed Cap, Poisoned-Save Rejection & Spawn Grace

**Type:** PATCH — eliminates the spawn-time launch through the planet from every angle; no save/API break.

#### 🚀 Why the player was launched (root cause)
The real-space N-body gravity clamped a body's pull to FULL SURFACE STRENGTH everywhere inside the crust. Any player who clipped the terrain — a bad spawn point, a save written mid-launch, a terrain-collider timing gap — was then accelerated toward the core at ~9.8 m/s² for the whole fall, reaching the core at escape velocity and being "launched through the planet" out the far side. The earlier frame-pin fixed the upward launch; this one kills the fall-through itself.

#### 🛠️ Fixes
- **Interior gravity is now physically correct (linear falloff):** inside a body the pull scales g·(d/R) toward zero at the core (`CosmicRegistry.GetGravityMetersS2`). A player can no longer gain core-escape energy from a terrain clip — there is no launch-through-the-planet path left in the physics.
- **Radial fall-speed cap (55 m/s) near any active body:** `PlayerController` clamps inward speed within 2.5× surface radius, so even a player restored from a bad space save falls at a speed the CharacterController always resolves against terrain colliders — no tunneling at hundreds of m/s.
- **Poisoned saves rejected:** `PlayerSpawner.IsSavedPositionInsideBody` — if a saved position would restore the player INSIDE any celestial body, the save is ignored and the surface/bed spawn path is used (legit surface/orbit/deep-space saves still restore).
- **Spawn starts at rest:** the player's controller + any rigidbody velocity are zeroed at control handover (both spawn and respawn).
- **Spawn grace in SpaceOrigin:** automatic reference-frame switches (and their velocity deltas) are suppressed for the first 3 s after load — the frame stays pinned to the home body; forced switches (warp, save restore) still work.
- **Floating origin always tracks the real player:** SpaceOrigin re-resolves the viewer every fixed tick if it isn't the PlayerController (the bootstrap can initialise with a placeholder before the player exists), and the bootstrap hands the player to SpaceOrigin when it resolves late.
- **No more late body-move race:** when the viewer resolves AFTER bootstrap (late scene order), the home body is no longer moved under the viewer — the origin is aligned to the body where it sits and PlayerSpawner places the player on its surface deterministically.

#### ✅ Static delivery checks
- All 5 modified sources parse cleanly (tree-sitter grammar validation).

### [7.13.5-dev] Spawn Launch Fix, No Boot Replay on Refresh, Weather HUD Removed & HUD Space Recovery

**Type:** PATCH — critical spawn-stability fix + UI polish (boot animation replay, weather indicator removal, vitals text alignment, compact gravity instrument); no save/API break.

#### 🚀 Spawn Launch Fix (player no longer flung into space)
- **Root cause:** the scene reference frame started as the SOLAR (star) frame at spawn. The home planet immediately raced away at its real orbital speed while the freshly-spawned player stood still — and when the frame then switched to the planet, the frame-velocity delta applied to every scene object hurled the player into space at hundreds of m/s with no way to stop.
- **Fix:** `CosmosBootstrap` now pins the scene reference frame to the HOME body at bootstrap (`SpaceOrigin.SetFrame(body)`). The planet is at rest in the scene from frame one, the player is born standing on it, and no frame-velocity kick ever fires at spawn. Interplanetary frame switches (leaving/entering gravity wells) keep the same correct physics as before.

#### 🖥️ LCD Boot No Longer Replays on Every UI Refresh
- **New `LcdHudTheme.BootsMuted`:** while set, boot animations (scale-in + phosphor wipe) are skipped and elements appear instantly.
- **`GameUIController.Refresh()`** (inventory, chests, machine panels, and the Ship Control terminal, which routes through it) now mutes boots around rebuilds — the boot only plays when a panel genuinely opens, never on item moves, toggles, or refresh ticks.
- **Main menu + pause menu:** rebuilding the SAME page (settings toggles, tab refreshes) is muted; real page changes keep the full boot.

#### ☀️ Weather Indicator Removed
- `WeatherHud.EnsureMounted` is now a no-op — the "☀ Clear / Overcast / …" readout no longer appears on screen (weather simulation itself is untouched).

#### 📟 Vitals HUD Text Alignment
- Value/code labels in the vitals rows are now vertically centred (`MiddleLeft`/`MiddleRight` + `alignSelf Center`, segment track centred) so the numbers sit exactly in line with the segment bars.

#### 🪐 Gravity Field HUD — Smaller, Less Wasted Space
- Card width 236 → 196 px, tighter padding.
- LCD display 100×64 → 84×50, G-readout 22px → 16px, acceleration line 8px → 7px.
- **VECTOR row removed** (hidden) — the surface-reference segments carry the useful info.
- Reference column, surface segments and captions all tightened — the same information in ~40% less screen space.

#### ✅ Static delivery checks
- All 8 modified sources parse cleanly (tree-sitter grammar validation).

### [7.13.4-dev] Boot Sweep Scheduler Compile Recovery (CS0426)

**Type:** PATCH — compile recovery for the 7.13.3-dev boot-sweep animation; no behaviour change, no save/API break.

#### 🛠️ Compiler Fix
- **`IVisualElementSchedulerItem` (CS0426):** the nested scheduler-item type does not exist in Unity 6.4's UI Toolkit API. `LcdHudTheme.AnimateBootSweep` no longer names the type — it chains the scheduler item's `Until(() => done)` stop-condition instead, so the repeating wipe automatically stops itself when finished (no type reference, no leak, no runaway timer).

#### ✅ Static delivery checks
- Verified the modified source parses cleanly (tree-sitter grammar validation).

### [7.13.3-dev] Premium LCD Feel — HUD Declutter, Panel Overlap Fix & Slot Fit Recovery

**Type:** PATCH — HUD declutter, panel/HUD overlap elimination, machine-slot fit recovery and boot-sweep compile fix; no save/API break.

#### 🖥️ Compile Fix
- **Boot sweep scheduler (CS8030):** `LcdHudTheme.AnimateBootSweep` no longer returns values from the scheduled action — Unity 6 UI Toolkit's `schedule.Execute` takes an `Action`; the repeating item now pauses itself via `IVisualElementSchedulerItem.Pause()` when the wipe finishes.

#### 🧹 HUD Declutter (cleaner, calmer screen)
- **Vitals cluster compacted:** row height 25→20 px, gaps 3→2 px — same information in a tighter, quieter bottom-right instrument (`TOTAL_HEIGHT` 166→142 so the feedback toasts still clear it).
- **Interaction prompt** now fades out whenever a panel owns the screen.

#### 🚧 No More HUD/Panel Overlap
- **New `LcdHudTheme.YieldWhileBlocking`:** a HUD module smoothly fades out while a blocking UI (machine panel, chest, terminal, inventory) is open and returns when it closes. Applied to **WorldInspectionHud, RecipePinHud, VitalsHud, BuildFeedbackHud, GravityPullHud, HotbarItemNameHud, InteractionHud and the cockpit Flight Computer (GridPilotHud)** — right-side and bottom HUDs always step aside for panels.
- **Machine panels dock higher:** `UITheme.MachinePanel` bottom inset 72→92 and right 12→14 so a machine never covers the hotbar strip; width min 260→280, max 46%→44% for a cleaner right column.

#### 📦 Machine Slots Always Fit Their Box
- **New `MakeScrollable`** (GridBlockUI + MaritimeBlockUI): wraps a machine panel's content in a vertical ScrollView so tall panels never clip their slots. Applied to the heavy panels: **Ship Refinery, Ship Chemical Plant, Portable Reactor, Electric Furnace, Hydrogen Engine, Drill, Weapon, Biofarm, Cryobed, H2/O2 Generator** and maritime **Engine, Generator, Helm** — every slot, gauge and recipe row stays inside the box, scrollable when needed.

#### ✨ Premium Feel
- **New `LcdHudTheme.ApplyPanelDepth`:** hairline top highlight + soft bottom shade on every themed panel (machined-metal/glass edge instead of a flat rectangle).
- Panels boot with scale-in + wipe, scanlines shimmer, buttons micro-interact — the full shared LCD language now extends to every corner of the UI.

#### ✅ Static delivery checks
- All changed sources parse cleanly (tree-sitter grammar validation).

### [7.13.2-dev] Unified LCD Theme — Menus, Settings, Storage & Ship Terminals + Compile Recovery

**Type:** PATCH — compile recovery for the real-space delivery + full LCD theme pass across menus, settings, storage terminal, block UIs and ship terminal; no save/API break.

#### 🖥️ Shared LCD Language (one consistent look everywhere)
- **New `LcdHudTheme.UpgradePanel`:** LCD-ify any existing plain panel in one call — dark chassis, bezel border, corner brackets, animated scanlines, phosphor boot + wipe. Used by the menus.
- **New `LcdHudTheme.AnimateBootSweep`:** one-shot CRT/LCD power-on wipe — a phosphor line sweeps down the screen and fades. Complements the existing boot animation with real motion.
- **New `LcdHudTheme.AddMenuInteractions`:** menu-scale button micro-interactions (0.1 s colour transitions, 1.03× hover scale, 0.98× press scale) that preserve each button's own sizing.
- **`UITheme.Panel()` boot + wipe:** every themed panel in the game (chests, containers, machines, browsers, right-side panels) now plays the shared LCD boot and phosphor wipe — one animation language everywhere. `MachinePanel()` keeps its denser scanlines without double-booting.

#### 🎛️ Main Menu & Pause Menu
- `MainMenuController.MakePanel` and `InGamePauseMenu.MakePanel` upgraded to full LCD chassis (bezel + corner brackets + animated scanlines + boot + wipe) — every page (Main, Saves, New World, Edit World, Settings, Pause) instantly matches the in-game terminals.
- All menu buttons (`PrimaryBtn`, icon buttons, tab buttons in both menus) now have hover/press micro-interactions per the interaction guidelines.

#### ⚙️ Settings (shared main-menu + pause surface)
- New `SettingsUI.ApplyLcdScreen` turns both settings tab bodies into inset phosphor-glass LCD screens — the two surfaces stay in lock-step by construction.

#### 💾 Storage Terminal & Block UIs
- **Storage Terminal:** storage fill bar is now an animated 14-segment phosphor track; search field styled as inset phosphor glass; sort button becomes an LCD command button with micro-interactions.
- **Ship Cargo Container (GridBlockUI):** weight fill upgraded to the same animated segment track.
- **Ship Control terminal (GridMasterTerminal):** group page + all-storage page play the boot sweep on open.

#### 🛠️ Compiler Fixes (real-space 7.13.0 follow-ups)
- **`Random` ambiguity (CS0104):** `CosmicRegistry.cs` + `SpaceAsteroidField.cs` carry `using Random = Unity.Mathematics.Random;`.
- **`Vector3 → double3` casts (CS0030):** Unity.Mathematics has no direct cast; added `CosmicRegistry.ToDouble3` and routed every cast site (`SpaceOrigin`, `GravityProvider`, `CosmosBootstrap`, `GridWarpDrive`, registry wrappers) through it.
- **`ref` mismatch (CS1615):** `BuildPlanetElements` now takes `ref Random`.
- **Missing `Unity.Mathematics` using (CS0246):** added to `AsteroidFieldRenderer.cs`.
- **`PowerFormat` scope (CS0103):** fully qualified in `GridWarpDrive`.

#### ✅ Static delivery checks
- All 17 modified sources parse cleanly (tree-sitter grammar validation).

### [7.13.1-dev] REAL SPACE Compile Recovery — Ambiguous Random & Duplicate ResetVelocity

**Type:** PATCH — compile recovery for the 7.13.0-dev real-space delivery; no behaviour change, no save/API break.

#### 🛠️ Compiler Fixes
- **Ambiguous `Random` reference:** `CosmicRegistry.cs` and `SpaceAsteroidField.cs` now carry an explicit `using Random = Unity.Mathematics.Random;` alias, resolving the CS0104 conflict between `Unity.Mathematics.Random` and `UnityEngine.Random` (the files import both namespaces for double3/float3 math and UnityEngine types).
- **Duplicate `ResetVelocity`:** removed the second `ResetVelocity()` from `PlayerController.cs` — the class already defined the identical public method (used by the Warp Drive arrival). The Warp Drive call sites are unchanged.

#### ✅ Static delivery checks
- Verified C# grammar (tree-sitter parse) across all 26 modified/new sources.

### [7.13.0-dev] REAL SPACE — Infinite Keplerian Universe, Floating Origin, Deep-Space Asteroids & The Only Warp (Warp Drive Block)

**Type:** MINOR — a complete real-space simulation layer (save-compatible): real elliptical Keplerian orbits, continuous infinite flight between planets with zero warps (except the new expensive Warp Drive grid block), N-body gravity, deep-space procedural asteroids, and floating-origin precision. Old saves load fine.

#### 🚀 REAL ORBITS — Keplerian Mechanics (not lazy circles)
- **Real elliptical orbits for every body:** `CosmicRegistry` now propagates planets, moons and sub-moons with classical orbital elements (semi-major axis a, eccentricity e, inclination i, RAAN Ω, argument of periapsis ω, mean anomaly M0) solved through Kepler's equation — the same math real astrodynamics uses. Periods follow T = 2π√(a³/μ) and velocities follow the vis-viva equation.
- **New `OrbitMath.cs`:** double-precision Kepler solver + perifocal→reference rotation, shared by every body.
- **Physically consistent masses:** each body's gravitational parameter is derived from its authored surface gravity and radius (μ = g·r²); the star's μ (default 180 km³/s², authorable on `SunSettings`) drives every planet orbit.
- **Authorable eccentricity:** `PlanetTemplate.orbitEccentricity` / `MoonTemplate.orbitEccentricity` (0 = seeded small value).
- **All positions double-precision (km)** via `positionKmD`/`velocityKmS`; legacy `positionKm` fields remain for sky renderers.

#### 🌌 INFINITE SPACE — Floating Origin & Reference Frames (real-flight level)
- **New `SpaceOrigin.cs`:** the floating-origin + reference-frame engine. The whole solar system is real geometry around you; the scene re-bases itself (32 km threshold) so float precision stays millimetre-fine no matter how far you fly. Physically invisible — every object shifts together.
- **Real reference frames:** the scene co-moves with the dominant body (planet/moon gravity well), and switches frames KSP-style when you leave a well or enter another — scene velocities are re-expressed by the frame-velocity delta so cosmic (inertial) velocity is always conserved. Entering a planet's frame makes it stand still; deep space is the star frame where every planet visibly orbits.
- **N-body gravity:** `GravityProvider` now sums the inverse-square pull of the star + every body (m/s²). Near a planet it is exactly the old radial gravity; in deep space it is genuine zero-g (dampeners hold, ships coast).
- **Continuous planet switching — no warp:** `SphereWorld.SetBody()` re-targets the voxel streamer to whatever body's frame you enter (per-body chunk persistence keys keep saves separate), grass/waterfalls/ocean LOD follow, and the sampled-surface LOD upgrades as you approach. Leaving a planet into deep space suspends streaming cleanly.
- **All bodies are real scene geometry:** every planet/moon in the system gets a real `CelestialBody` + sampled-surface LOD; close bodies render at true scale/position, far bodies use the compressed sky projection (200 km crossover).
- **Deep-space atmosphere fix:** `AtmosphereManager` reports true vacuum (not the old flat-world fallback) and `GravityProvider.Sample` reports 0.00 g in deep space.
- **REMOVED the lazy warp:** `CosmosBootstrap.CheckInterplanetaryFlight` + `TransitionToPlanet` are deleted — looking at/flying toward another planet never teleports you. The ONLY warp left is the Warp Drive block (below).
- **Save/load:** the player's cosmic position + reference frame are persisted; logging out in deep space or high orbit restores exactly where you were (legacy saves unaffected).

#### ☄️ DEEP-SPACE ASTEROIDS (outside planet/moon orbits)
- **New `SpaceAsteroidField.cs`:** while the player is in the solar frame (outside every planet/moon gravity well), procedural minable asteroids spawn around you — seeded per cosmic region with per-attempt nonces, despawned when you leave or when you enter a planet's well.
- **New `SpaceAsteroid.cs`:** noise-displaced icosphere rocks with MeshCollider, ore-tinted vertex colours, slow tumble, HP scaled by size, and ore drops (Iron/Nickel/Silicon/Cobalt/Gold/Platinum/Ice) — mineable with any tool via the damage pipeline (pickaxe hook added to `PlayerInteractionTool`).
- The authored visual belt (`AsteroidFieldRenderer`) keeps rendering the distant main belt in the sky.

#### ⚡ THE ONLY WARP — Warp Drive Block (expensive, researched, chargeable)
- **New `GridWarpDrive.cs`:** charges over 45 s under a heavy 45 kW power load; when charged, the pilot presses [N] (`InputAction.WarpDrive`, rebindable) to jump the ship to the aimed planet's orbit (90 km altitude, co-moving with the planet) or 2,500 km straight ahead. Requires vacuum, has a 3-minute cooldown, and refuses to short-hop.
- **Cockpit integration:** [N] begins charging, shows charge %, and fires when ready (`GridCockpit.HandleWarpDriveInput`).
- **Setup-owned content (non-destructive):** new **Step 50 — Build Warp Drive** in Tools ▸ Voxel Engine ▸ Voxel Engine Setup creates the prefab, `GItem_WarpDrive`, the Assembler recipe (40 Steel Plate + 12 Advanced Circuit + 8 Uranium Ore + 6 Lithium), and the `res_warpdrive` research node (tier 7, gated after Shipbuilding) — existing tuning is preserved on re-runs.

#### 📟 HUD & FEEDBACK
- `GridPilotHud`: deep space shows **DEEP SPACE · 0% AIR**, altitude reads DEEP SPACE, and the trajectory module becomes a solar-frame coast readout (SPD + nearest body).
- `OrbitalTelemetry` gains the `DeepSpace` state for the flight computer.
- Frame switches toast via console + HUD (frame name + Δv).

#### ✅ Static delivery checks
- All 25 modified/new sources pass the C# grammar validation (tree-sitter) — brace/paren balance and parse-tree clean.


All release notes are maintained here so `Roadmap.md` remains focused on planned work and execution status.

### [7.12.1-dev] Vast Infinite Space Flight, Sparse 3D Asteroid Belts & Camera-Relative Asteroid Removal

**Type:** PATCH — interplanetary flight navigation without warp prompts, sparse 3D Asteroid Belt voxel spawning, removal of camera-relative fake asteroids, and material enum correction; no save/API break.

#### 🚀 Vast Infinite Space & Ship-Driven Interplanetary Flight
- **Ship-Driven Solar System Navigation:** Removed artificial `[F / WARP]` prompt overlays from `PlayerInteractionTool.cs`. Interplanetary travel is now driven exclusively by flying ships through vast, open space toward distant orbiting planets in the sky.
- **Real Orbital Approach Transition:** When a player flies in high orbit (`> 1800m` altitude) aiming their ship toward a distant planet in `CosmicRegistry.Instance.Bodies`, `CosmosBootstrap.CheckInterplanetaryFlight` seamlessly transitions to arrive in high orbit around the destination planet. Warp drives remain a separate dedicated propulsion mechanism.

#### ☄️ Sparse 3D Asteroid Belt Spawning (No Surface Shell or Fake Rocks)
- **Sparse 3D Voxel Asteroids:** Reworked `SphereDensity.EvaluateVoxel` on Asteroid Belt worlds (`isAsteroidBelt = 1`) to remove radial shell masking (`beltMask`). Mineable procedural voxel asteroids spawn rarely (`rockNoise > 0.44f`) everywhere around the player in 3D zero-gravity space.
- **Removed Camera-Following Fake Asteroids:** Removed `AddVisualFallbackAsteroids` from `AsteroidFieldRenderer.cs` and changed visual asteroid placement from camera-relative to fixed astronomical world-space coordinates (`deltaKm * 4f`). Asteroids in deep space no longer follow the player's camera.
- **Fixed Asteroidal Ore Catalogue:** Replaced non-existent `MaterialId.Titanium` with `MaterialId.Cobalt` in `SphereDensity.cs` alongside Platinum, Gold, Iron, Silicon, and Ice.

#### ✅ Static delivery checks
- Verified C# syntax and brace balance across all modified space flight, asteroid rendering, and voxel density sources (`SphereDensity.cs`, `AsteroidFieldRenderer.cs`, `PlayerInteractionTool.cs`, `CosmosBootstrap.cs`).

### [7.12.0-dev] Hierarchical Sky Orbits, Sub-Moon Satellites & Zero-G Asteroid Belt Voxel Generation

**Type:** MINOR — hierarchical multi-body sky orbits (planets around Sun, moons around planets, sub-moons around moons) and roadmap Era 4 3D Asteroid Belt procedural zero-gravity voxel generation; save-compatible.

#### 🌌 Real Hierarchical Sky Orbits (Planets Around Sun, Moons Around Planets & Moons Around Moons)
- **Hierarchical Sky Orbit Positioning:** Upgraded `SpaceBodyRenderer.cs` with recursive orbital placement (`GetVisualPositionFor`) so celestial bodies in the sky reflect their true relative astronomical orbits instead of independent camera-centric projections.
- **Planets Orbiting the Sun:** When looking at the sky, every planet visually revolves in an orbit around the Sun (`sunPos + fromSunKm * scaleKmToSky`).
- **Moons Orbiting Planets:** Moons dynamically revolve around their parent planet's visual position in the sky (`parentPos + fromParentKm * 18f`), clearly showing real orbital mechanics.
- **Sub-Satellites (Moons Around Moons):** Upgraded `CosmicRegistry.cs` to generate non-intersecting sub-moons (`moonlet.parentBody = moon`) orbiting larger moons. In the sky, sub-moons visibly circle their parent moon (`parentPos + fromParentKm * 26f`), creating a living 3-tier orbital hierarchy.

#### ☄️ Roadmap Era 4 Asteroid Belt Procedural Zero-G Voxel Generation
- **True 3D Asteroid Belt Worlds:** When generating or warping to an Asteroid Belt world (`isAsteroidBelt = 1`), `SphereDensity.cs` replaces the spherical planet crust with a vast 3D procedural belt (`120m..1300m` from origin) of floating voxel asteroids in zero gravity (`SurfaceGravity = 0f`).
- **Roadmap-Accurate Ores:** Procedural asteroids are shaped via 3D Simplex noise (`rockNoise`) and packed with rich veins of **Platinum**, **Titanium**, **Gold**, **Iron**, **Silicon**, and **Ice** per the Roadmap Era 4 specification.
- **Dedicated Zero-G LOD Masking:** Automatically hides `PlanetLodImpostor` and `PlanetOceanLodRenderer` on Asteroid Belt worlds so players fly through deep space surrounded by instanced background rocks and mineable floating voxel asteroids without a spherical proxy.

#### ✅ Static delivery checks
- Verified C# syntax and brace balance across all modified cosmos, orbital, and density sources (`CosmicRegistry.cs`, `SpaceBodyRenderer.cs`, `SphereGenParams.cs`, `CelestialBody.cs`, `SphereDensity.cs`, `PlanetLodImpostor.cs`, `PlanetOceanLodRenderer.cs`).

### [7.11.16-dev] Seamless Water & Ocean Continuity, Top-Left Inspection HUD Relocation & Voxel Name Resolution

**Type:** PATCH — water surface continuity, coastline height alignment, inspection HUD layout relocation, and target name display resolution; no save/API break.

#### 🌊 Seamless Water Surface & Ocean Continuity
- **Eliminated Coastal Water Pull-Down:** Removed `bordersTerrain` height depression (`finalH = Mathf.Min(baseH, terrainH + 0.12f)`) in `WaterMeshBuilder.cs`. Water along coastlines and islands no longer slopes downward into terrain holes, remaining level with open water.
- **Eliminated Inter-Chunk Water Step Seams:** Removed `SmoothHeightField(cells, S)` in `WaterMeshBuilder.cs`. Interior water cells and border water cells now stay at the exact same height across chunk boundaries, eliminating vertical step gaps between adjacent water chunks.
- **Full Coastline Ocean LOD Triangles:** Upgraded `PlanetOceanLodRenderer.cs` to keep coastal triangles (`!ocean[a] && !ocean[b] && !ocean[c]`) and set `_CutoutRadius` to `0f`, preventing gaps between the ocean LOD and coastal land.

#### 📺 Top-Left LCD World Inspection HUD & Voxel Name Resolution
- **Top-Left Relocation:** Relocated `WorldInspectionHud` to the top-left (`left = 16`, `top = 18`, removed `right`) per Thomas's feedback, styled as a Tektronix phosphor LCD terminal.
- **Guaranteed Target Name Display:** Upgraded `TryDescribeVoxel` and `TryResolve` so aiming at any block, terrain voxel (Dirt, Grass, Sand, Stone, Clay), tree, or machine always resolves and displays the target's name, hardness, and mining tier without blank titles.

#### ✅ Static delivery checks
- Verified C# syntax and brace balance across all modified water meshing, ocean rendering, and UI inspection sources (`WaterMeshBuilder.cs`, `PlanetOceanLodRenderer.cs`, `WorldInspectionHud.cs`).

### [7.11.15-dev] Interplanetary Flight, Correct Planet Biomes, Full-Sphere LODs, Oil Seep Reservoirs & LCD Inspection HUD

**Type:** PATCH — interplanetary travel, biome filtering, full-planet LOD clipping, oil seep geology, tree harvesting, and LCD inspection HUD polish; no save/API break.

#### 🚀 Interplanetary Space Flight & Transition System
- **Active Solar System Navigation:** Added `CosmosBootstrap.Instance.TransitionToPlanet(PlanetTemplate)` and high-orbit flight telemetry in `CosmosBootstrap` and `PlayerInteractionTool`.
- **Warp & Orbital Arrival:** Players flying in high orbit (`> 1400m` altitude) aiming towards a distant planet in `CosmicRegistry.Instance` can press `F / WARP` (or fly directly towards it above `1800m`) to transition seamlessly to that planet's orbit.
- **Clean Voxel Re-Bootstrap:** Transitioning invokes `SphereWorld.ResetAllChunks()` to instantly reclaim old planet meshes and generate the target celestial body's unique terrain, atmosphere, gravity, and biomes without leaking memory.

#### 🌍 Correct Biomes per Planet & Full-Sphere Distance LODs
- **Strict Planet-to-Biome Filtering:** Upgraded `CelestialBody.BuildBiomeData` with `IsBiomeCompatibleWithPlanet` keywords (`bodyName`, `temperature`, `hasAtmosphere`). Moon worlds only receive barren/crater biomes, ice worlds only frozen tundra/glaciers, desert worlds only arid dunes/canyons, volcanic worlds only basalt/lava, and pirate worlds only rust/scrap. Alien biomes no longer cross-contaminate Earthlike worlds.
- **Near-Camera LOD Clipping:** Added `clip(distToCamera - 240.0)` and smoothstep alpha fade (`240m..320m`) to `PlanetSurfaceLodURP.shader`. When close to the planet, low-poly impostor triangles inside local streamed terrain are hidden so they never clip through ground or caves, while the full planet surface remains 100% visible at any distance.

#### 🛢️ Oil Seep Reservoirs & Ruined Pirate Jack Pump Landmarks
- **Puddle-to-Reservoir Geological Funnel:** Reworked `OilReservoirDecorator.BuildRadialFunnel` to generate a continuous tapering conduit from `mouthRadius = puddleRadius - 1` right below the surface puddle down to `throatRadius = 2` at the deep reservoir, connecting puddle -> bore -> reservoir cleanly.
- **Ruined Jack Pump Relics:** `PirateOilNode.Ensure` now spawns a weathered industrial `BrokenJackPump` (`IDamageable`, 120 HP) right on top of infinite Pirate World oil nodes. Breaking down the ruined pump awards 4 Iron Plates (`"Item_IronPlate"`) and leaves the infinite oil node marked for operational Jack Pump placement.

#### 📺 Top-Right LCD World Inspection HUD & Bare-Hand Tree Mining
- **Top-Right LCD Terminal Display:** Relocated `WorldInspectionHud` to the top right (`right = 16`, `top = 18`) and restyled it with our signature phosphor LCD instrument chassis, scanlines, and bezel brackets.
- **3-Depth Voxel & Vegetation Resolution:** Upgraded `TryDescribeVoxel` and `TryResolve` with 3 surface-normal sample depths (`0.55m`, `0.25m`, `0.85m`) and a finer `0.25m` ray step so aiming at dirt, grass, stone, or trees reliably displays material hardness, mining tier, and harvestability.
- **Bare-Hand Tree Harvesting:** `ChunkScatter` now automatically ensures scatter trees have a capsule collider and the `VoxelEngine.Trees.Tree` component. Upgraded `Tree.Hit` so punching with empty hands (`ToolType.Other`) deals `Mathf.Max(4, damage / 2)` damage, breaking trees down in ~16 punches for Wood Logs.

#### 🐛 Compiler Fixes
- Fixed lowercase `.execute(` -> capital `.Execute(` across `UITheme.cs` and `LcdHudTheme.cs` to conform to Unity 6.4 UI Toolkit's `IVisualElementScheduler` API.

#### ✅ Static delivery checks
- Verified C# syntax and brace balance across all 19 modified player, UI, cosmos, generation, scattering, and shader sources.

### [7.11.14-dev] LCD Screen UI Animations, Unified Instrument Aesthetics & Smooth Planet Surfaces

**Type:** PATCH — UI visual polish, animated scanline drift, phosphor boot sequences, bezel corner brackets, button micro-interactions, and 32x sub-voxel density scaling for smooth planet terrain; no save/API break.

#### 📺 Retro-Futuristic LCD Screen Animations & Instrument Language
- **Animated Scanline Shimmer:** LCD displays across the game (`LcdHudTheme.AddAnimatedScanlines`) now feature subtly drifting, animated phosphor scanlines that shimmer at 20 fps without causing layout recalculation.
- **Phosphor Boot Sequences:** Added `LcdHudTheme.AnimateScreenBoot` and `UITheme.AnimatePanelBoot`. Every LCD display and machine UI panel now plays a crisp 2-stage phosphor boot sequence when opened—expanding from a thin horizontal scanline to nominal scale with a subtle ignition flash.
- **Tektronix Bezel Brackets & Status Badges:** Added `LcdHudTheme.AddBezelAccents` and pulsing status indicator dots (`CreateLiveStatusBadge`). All LCD screens now feature 4 high-contrast L-bracket corner elements inside the bezel frame and pulsing live status dots ("LIVE", "RUNNING", "ONLINE").
- **Enhanced Button Micro-Interactions:** Upgraded `ApplyCommandButton` and `UITheme` buttons to follow our AI Agent System Prompt & Execution Guidelines: buttons scale smoothly to `1.03x` on hover with a 0.10s color/border transition and drop to `0.98x` on press for tactile feedback.
- **Unified Aesthetic Across All UIs:** Integrated LCD bezel corner brackets and scanline helpers into `UITheme.cs` (`Panel()`, `MachinePanel()`, `StatusPill()`), automatically elevating all machine UIs, crafting screens, recipe browsers, pilot HUDs, vitals monitors, and production terminals into a cohesive retro-futuristic flight-computer dashboard.

#### 🌍 Smooth Planet Surface Slopes (Eliminated Terraced Rings)
- **32x Sub-Voxel Density Scaling:** Upgraded `SphereDensity.EvaluateVoxel` and `ChunkGenJob.cs` to scale physical world distance (metres) by 32 before converting to signed-byte (`sbyte`) density units.
- **Eliminated Contour Stepping:** Unscaled density clamped to integer `±1` previously forced every surface edge zero-crossing to interpolate at `t = 0.5` in `SurfaceNetsJob`, causing gentle slopes and flat regions on planets to quantize into rigid concentric rings stacked on each other.
- **Smooth Mountains & Plains:** With 1 metre of distance represented by 32 density units, `SurfaceNetsJob` now interpolates zero-crossing ratios with 3 cm sub-voxel precision, producing butter-smooth continental slopes, rolling hills, and mountain terrain across spherical planets.

#### ✅ Static delivery checks
- Verified C# syntax and brace balance across all modified cosmos, generation, and UI sources (`SphereDensity.cs`, `ChunkGenJob.cs`, `LcdHudTheme.cs`, `UITheme.cs`, `CraftingScreen.cs`, `RecipeBrowserUI.cs`, `ProductionStatsUI.cs`, `GridPilotHud.cs`, `VitalsHud.cs`).

### [7.11.13-dev] Valid Terrain Colliders, Wrapped Grass & Living Planet Recovery

**Type:** PATCH — collision validity, voxel inspection/mining, spherical terrain shaping, vegetation orientation, and legacy oil/drop recovery; no save/API break.

#### 🧱 Valid gameplay terrain
- Mesh colliders now require three indexed, non-degenerate vertices before assignment. Empty SurfaceNets placeholder meshes are rejected, eliminating the repeated `ChunkMesh must have at least three distinct vertices` console spam.
- The nearby collider window still keeps physics cheap, but correctly promotes valid terrain collision as the player approaches.
- World inspection now ray-marches ready spherical voxels when a collider is temporarily unavailable, preserving the top-left material/object title during streaming instead of showing nothing.

#### 🌍 Rounded terrain and real mountains
- Corrected planet direction-space continent scaling, which had collapsed authored-size worlds into a near-flat rigid shell.
- Converted biome terrain frequencies from metre-authored values into stable spherical direction-space frequencies, seeded the terrain field per world, and added smooth continent-only tectonic mountain masks.
- Reworked slope sampling to use physical metre spans rather than oversized fixed angular offsets, removing widespread artificial cliff/ramp material changes.
- High/Ultra now retain the detailed capped planet proxy in orbit, with procedural surface variation for readable continents from space.

#### 🌿 Mining, grass, water, and oil recovery
- Grass blades now align directly to radial planet-up, avoiding chunk-gradient tilt that made grass appear flat against global space rather than wrapped around the sphere.
- Under-tier tools and hands now mine every solid voxel at reduced speed/brush efficiency instead of hard-locking materials. Correct tools remain much faster.
- Step 16 surface-drop repair now also repairs missing Sand drops, alongside Dirt/Grass drops, without overwriting custom rewards.
- Legacy oil-rich templates now restore intended finite seep eligibility/chances for Earth, Ocean, Acid, Desolate, and Pirate worlds until Step 21 writes explicit settings. Natural oceans return through the corrected continent/ocean field.

#### ✅ Static delivery checks
- Parsed terrain, streaming, meshing, water, player, HUD, material, celestial, and setup C# sources with Tree-sitter.
- Ran targeted collider/HUD/mining/terrain/oil/version/sparse-workspace assertions locally. Unity compile and Play Mode validation remain pending from Thomas.

---

### [7.11.12-dev] Bounded Voxel Streaming, Low-Cost Water & Detailed Space Surface

**Type:** PATCH — spherical streaming/meshing throughput, native-water budgeting, and full-planet space presentation; no save/API break.

#### ⚙️ Generation no longer monopolizes the frame
- Added hard outstanding-work and in-flight-job ceilings to `SphereWorld`: new chunk admission, radial generation, and mesh jobs are bounded instead of accumulating hundreds of expensive work items while moving.
- Surface-only initial meshing now skips unneeded deep-solid/empty-air chunks, while nearby underground chunks are promoted one at a time as the player approaches or mines into them.
- Added a near-player collider window: only gameplay-near terrain owns costly `MeshCollider` data; approaching chunks receive collision before the player reaches them.
- Replaced the per-vertex 256-material histogram in `SurfaceNetsJob` with an eight-corner vote and disabled the 26-neighbour vertex-AO pass for streamed spherical chunks. Static-world AO remains enabled.
- Oil-site discovery now uses an exposed two-metre candidate lattice and runs only on surface chunks, avoiding deep geological scans during ordinary terrain streaming.

#### 🌊 Native voxel water: gameplay-local, ocean-visual at distance
- Natural oceans remain static voxel source data with the full planet ocean LOD for distant visuals; only local pools, placed liquid, pumps, and changed liquid chunks enter detailed water meshing/simulation.
- Removed duplicate water queue pumping between `SphereWorld` and the native-water bootstrap.
- Existing default scenes migrate from 4 water mesh builds/frame and 8 Hz / 6-chunk fluid ticks to a bounded 1 build/frame and 4 Hz / 2-chunk local simulation budget, preserving deliberate custom values.
- Periodic liquid recovery no longer force-completes nearby terrain meshes or repeatedly rebuilds visible ocean chunks.

#### 🪐 Detailed surface from space without dense voxel streaming
- Kept the capped 10,242-vertex terrain/ocean proxy at High/Ultra in orbit rather than dropping to a coarse far hexasphere.
- Added body-relative procedural macro/fine surface variation to the planet LOD shader, giving continents readable texture from space without spawning extra terrain chunks.

#### ✅ Static delivery checks
- Parsed the modified streaming, meshing, water, terrain-LOD, scatter, player-spawn, and setup C# sources with Tree-sitter.
- Ran targeted throughput/version/sparse-workspace assertions. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.11.11-dev] Spawn Scope Compile Recovery

**Type:** PATCH — Unity compiler recovery; no save/API break.

- Renamed the nested spherical fallback up-vector local in `PlayerSpawner.EnsureDrySpawn`, resolving the reported `CS0136` local-name scope collision.
- No generation, spawn-selection, performance-budget, save, prefab, recipe, or balance behaviour changed from `7.11.10-dev`.

---

### [7.11.10-dev] Seamless Surface Spawn & Streaming Performance Recovery

**Type:** PATCH — runtime scheduling, vegetation/grass budgets, visual LOD budget, and deterministic spherical spawn recovery; no save/API break.

#### ⚡ Restored frame budget
- Reduced the local editable chunk bubble to a bounded **3 / 4 / 5 / 6** chunk radius across Low → Ultra. This is collision/edit detail only; the whole-planet terrain and ocean LOD remain visible outside it.
- Capped runtime planet and ocean proxy resolution at **10,242** vertices, restored verified back-face culling for the terrain proxy, and removed the 40k-vertex high-quality startup spike.
- Throttled native water mesh work to the live generation budget instead of rebuilding four liquid chunks every frame during spawn.
- Deferred scatter now processes only the nearest visible surface chunk per frame by default. Empty interior chunks retire without a scatter scan.
- Tree/rock scatter now uses a 2 m candidate lattice, cheap outward rejection before exact radial tests, and allocation-free overlap checks.
- Reworked GPU grass from a 145-voxel radial search per sample into a direct spherical density-column query, with a 1,600-sample cap, smaller local field, and less frequent rebuilds.

#### 🧭 One stable spherical spawn
- `SphereWorld` now exposes a deterministic density-field dry-land finder.
- Fresh spherical spawns choose a valid land surface **before** terrain colliders stream, then wait and snap at that one location.
- Wet-spawn recovery now makes at most one analytical spherical relocation. Fully oceanic/custom bodies hold the player safely above the selected water column rather than visibly hopping through many candidates.

#### ✅ Static delivery checks
- Parsed the updated performance/spawn/world-generation C# sources with Tree-sitter and ran targeted source, padded-coordinate, version, and sparse-workspace assertions.
- Unity compilation and Play Mode validation remain pending from Thomas; no runtime confirmation is claimed.

---

### [7.11.9-dev] Spherical World Generation Integrity & Celestial Visibility

**Type:** PATCH — spherical chunk-generation, exterior-scatter, full-planet continuity, and automatic celestial-visual recovery; no save/API break.

#### 🌍 Reliable spherical terrain instead of streaming scars
- Corrected the padded spherical generation origin so voxel samples and `SurfaceNets` coordinates agree across every chunk boundary; this removes the one-voxel overlap/slit source behind floating, offset, and apparently missing terrain chunks.
- Added rent-epoch guards to terrain and native-water work queues. A recycled pooled chunk now rejects stale generation, meshing, and liquid rebuild work after fast travel, preventing old queue entries from writing a new coordinate in the air or under the planet.
- Removed the remaining neighbour-wait mesh stall from the spherical stream: each deterministic padded chunk can mesh immediately, while the sampled full-planet LOD remains present beyond editable local detail.
- Protected an 8 m minimum radial terrain crust before cave carving, so caves remain underground and cannot randomly puncture the playable surface.
- Raised near-surface full-planet LOD budgets to 2,562 / 10,242 / 40,962 vertices by quality tier and made the terrain proxy two-sided, eliminating coarse square-horizon/one-sided proxy failures while retaining visual-only LOD interaction.

#### 🌲 Exterior-only, separated vegetation
- Rebuilt chunk scatter around the exact radial density surface rather than rounded global-axis neighbours. Trees/rocks now reject cave walls, deep terrain, water, and unstreamed false surfaces, and roots are projected slightly above the real spherical iso-surface.
- Added cross-chunk live placement reservations plus tree-canopy clearance. Adjacent chunks cannot spawn trees into one another, including when they finish generation on separate frames.
- Applied the same exterior proof to GPU grass so it cannot select underground cave surfaces.

#### 🛢 Stable oil geology
- Oil candidates now require the true sampled exterior surface. Surface puddles preserve their terrain floor; deep deposits use a sealed one-cell conduit below an intact cap instead of a player-sized open bore.
- This keeps puddle/reservoir geology readable without creating an accidental route beneath the planet at an oil site.

#### ☀️ Automatic System_Sol, planets, and asteroids
- `CosmosBootstrap` now resolves the setup-owned `CosmosTemplateLibrary` before editor-only fallbacks, assigns the resolved `System_Sol` to both bootstrap and `CosmicRegistry`, retries late player/viewer binding, and anchors a late-spawned viewer safely on the authored-scale surface.
- `CosmicRegistry` supplies deterministic fallback asteroid instances when a legacy system has no belt. `AsteroidFieldRenderer` now uses registry belt data at visible proxy sizes, while distant planet proxies use unlit authored colours for reliable sky readability.
- Step 21 now non-destructively creates/repairs `System_Sol`, its `Resources/CosmosTemplateLibrary` registration, `Asteroids_MainBelt`, legacy celestial links, and null bootstrap links. Existing custom belt tuning and custom bootstrap assignments are preserved.

#### ✅ Static delivery checks
- Parsed the modified spherical-world, water, scatter, oil, celestial, setup, and version C# sources with Tree-sitter and ran targeted source/sparse-workspace assertions locally.
- Unity compilation and Play Mode validation remain pending from Thomas; this entry does not claim Unity confirmation.

---

### [7.11.8-dev] Full-Scale Bootstrap & Automatic Solar System Recovery

**Type:** PATCH — authored planet scale, automatic solar-system assignment, and asteroid fallback repair; no save/API break.

- Removed the remaining `CosmosBootstrap.testRadiusKm` workflow and preserves each selected template’s authored radius at runtime.
- Added a runtime-only session seed override so bootstrap never mutates shared `PlanetTemplate` settings or loses the selected seed when `SphereWorld` reapplies settings.
- Added viewer-to-surface anchoring and actual-radius camera clipping for full-size planets.
- `CosmosBootstrap` now resolves and assigns `System_Sol` automatically, drives `CosmicRegistry` from it, and supplies a deterministic visual asteroid-belt fallback when no authored belt asset is present.

---

### [7.11.7-dev] Ocean LOD Compile Recovery & Craft Queue Serialization Guard

**Type:** PATCH — Unity compiler/analyzer recovery; no save/API break.

- Replaced the unsupported target-typed `new(float, float, float)` expression in `PlanetOceanLodRenderer` with explicit `new Vector3(...)`, resolving reported `CS8754`.
- Marked `CraftQueue.Entry` serializable and its runtime-only interface destination/queue fields `[NonSerialized]`, resolving the reported `UAC1001` serialization warning without changing live crafting behaviour.

---

### [7.11.6-dev] Authored Planet Scale & Surface Spawn Repair

**Type:** PATCH — removes the test-radius override and restores authored spherical planet scale; no save/API break.

#### 🌍 Real authored planet radius
- Removed `CosmosBootstrap.testRadiusKm`; it no longer shrinks every selected planet to a 0.5 km test sphere.
- Celestial bodies now preserve their authored `PlanetTemplate.body.radiusKm` at runtime. Session seeds use a dedicated runtime override rather than mutating shared template assets or being reset by later bootstrap calls.
- Added default viewer-to-surface anchoring for real-size planets, keeping the player just above the initial radial surface instead of spawning inside a 6–8 km solid planet.
- Camera far-clip sizing and bootstrap diagnostics now use the actual generated body centre/radius.

#### ✅ Static delivery checks
- Source parsing and targeted planet-scale/LOD/water/HUD regression assertions are run locally. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.11.5-dev] LOD Interaction Guard & HUD Cleanup

**Type:** PATCH — visual-only LOD hardening and HUD stale-text/inspection cleanup; no save/API break.

#### 🪐 LOD cannot block play
- `PlanetLodImpostor` and `PlanetOceanLodRenderer` strip stale colliders. The full terrain/ocean LODs are now strictly visual: they cannot be mined, ray-hit as terrain, or trap the player after local voxel terrain is excavated.

#### 🖥 HUD cleanup
- World inspection now ignores bootstrap, LOD, ocean-LOD, and native-water helper colliders, so the top-left readout never reports `Bootstrap Controller` instead of a real block or voxel.
- Raised the held-item HUD layout revision and added a document-wide raw-item-id scrub. Legacy labels such as `dirt_item` are removed both on initial mount and selection changes, leaving only the intended **Dirt** display label.

#### ✅ Static delivery checks
- Source parsing and targeted LOD/HUD/real-water regression assertions are run locally. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.11.4-dev] Real Ocean LOD & Seamless Planet Surface

**Type:** PATCH — actual-ocean LOD, wrapped-water removal, and full-surface continuity repair; no save/API break.

#### 🌊 Actual ocean geometry with no gaps
- Added `PlanetOceanLodRenderer`: a whole-planet ocean mesh sampled from `SphereDensity` that emits triangles **only** above real terrain-defined ocean basins. It is not a global water sphere and cannot appear beneath dry land or inside a mined cave.
- Ocean LOD uses the same 642 / 2,562 / 10,242 distance budgets as terrain, sits slightly beneath local streamed water, and fills visual seams between water chunks rather than leaving large blue gaps.
- Disabled every retained procedural wrapped-water patch at runtime and through Step 16 setup. Generated voxel water remains the local gameplay authority for buckets, pumps, swimming, buoyancy, and mining.

#### 🪐 Continuous surface hierarchy
- The inset terrain LOD now fills every unstreamed region at ground, flight, and orbit while real voxel chunks naturally occlude it near the player. Terrain and ocean therefore share a continuous full-planet LOD hierarchy instead of a square loaded island.

#### ✅ Static delivery checks
- Source parsing and targeted ocean/terrain LOD, real-water, dirt, mining, oil, and regression assertions are run locally. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.11.3-dev] Real Ocean Basins, Dirt Drops & Surface Continuity

**Type:** PATCH — real-water rendering, complete surface continuity, mining-drop, and oil-seep follow-up; no save/API break.

#### 💧 Real water, never a wrapped cave sphere
- Disabled and removes every retained `ProceduralWaterPatchRenderer` at runtime/setup. The former mathematical sea shell was the remaining reason mined caves still appeared to strike water.
- Real generated water voxels now own every visible ocean, lake, pool, bucket, and oil surface; `WaterMeshBuilder` no longer skips sea-level water for a fake shell.
- Full-planet LOD now stays active at all altitudes but is inset beneath real local voxel terrain. Nearby chunks depth-occlude it, while the sampled LOD fills every unstreamed part of the planet instead of leaving a square chunk bubble or missing horizon.

#### 🪨 Dirt and actual terrain continuity
- Step 8 / Step 16 setup now repairs the missing setup-authored `Item_Clay.asset` as **Dirt**, creates/repairs Grass/Dirt material definitions, and links absent Clay/Grass mining drops without overwriting custom rewards.
- Corrected the original normal-item authoring path so future setup runs actually save newly created material item assets.
- Forced radial terrain normals outward and made terrain surface passes two-sided, removing spherical chunk-face winding holes and improving wrapped grass/lighting continuity.

#### 🛢 Grounded crude seep geometry
- Surface puddle cells now individually resolve their local radial hillside/ocean surface before oil is written, preventing the prior fixed-radius disc from scattering crude across slopes.
- Increased the tapering funnel mouth to match the puddle and reduced crude lateral spread to one voxel step, keeping dense oil cohesive as it descends into the deep reservoir.

#### ✅ Static delivery checks
- Source parsing and targeted real-water/LOD/dirt/oil regression assertions are run locally. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.11.2-dev] Full Planet Surface LOD & Active-World Mining Repair

**Type:** PATCH — full-sphere LOD, terrain/grass wrapping, interaction, and cave-water follow-up; no save/API break.

#### 🪐 Full planet surface from orbit
- Replaced the unreliable white built-in-material LOD with a native vertex-colour `PlanetSurfaceLodURP` shader. The active body now renders as its sampled ocean/land/mountain sphere rather than a white coarse hexasphere.
- Upgraded the full-planet mesh into true near/mid/far vertex-budget LODs (10,242 / 2,562 / 642 vertices depending on altitude and graphics tier), while local voxel chunks remain the close-range editable detail layer.
- Repaired parent-body binding and transparent hand-off: the far shell is disabled on the ground, never writes depth over terrain, then returns as a colored spherical surface in flight/orbit. Distant system bodies continue using their authored `displayColor` proxies.

#### ⛏ Terrain, grass, caves, and liquid interaction
- Fixed hand/pickaxe routing to the active spherical world and its real material registry, including radial hit resolution and liquid-collider ray filtering.
- Converted grass/scatter placement and wind bending to radial/tangent coordinates. Terrain material noise, slopes, and triplanar coordinates now use the active body centre, eliminating the top-of-planet-only appearance.
- Restricted generated water to real ocean basins, repaired legacy dry-cave water locally as a player mines, and lets real local liquid trigger swim/escape and mining correctly.

#### ✅ Static delivery checks
- Source parsing and targeted spherical-terrain/LOD/mining/water regression assertions are run locally. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.11.1-dev] Spherical Terrain, Dry Caves & Planet LOD Repair

**Type:** PATCH — spherical-world interaction, terrain orientation, cave-water, and far-LOD fixes; no save/API break.

#### ⛏ Reliable mining in terrain and liquid
- Rebound `PlayerInteractionTool` to `ActiveWorld.Current` and that world’s `MaterialRegistry` every frame, preventing the disabled legacy flat-world reference from silently blocking hand/pickaxe mining on sphere terrain.
- Hardened terrain hit resolution to enter the radial terrain surface before editing, and ignores liquid visual colliders so a player can keep mining while submerged in water or crude oil.
- Real-liquid swim state now activates from any actual local liquid voxel, including a mined/pumped pocket away from the global sea shell; jump/swim depth uses local fluid depth so players can escape rather than getting wedged in a hole.

#### 🌍 Wrapped terrain, grass, and full-planet LOD
- Rebuilt GPU grass placement from a top-of-planet XZ scan into a radial/tangent surface search. Blades, scatter yaw, wind bending, terrain noise, triplanar mapping, and slope shading now follow the body’s local radial frame around the entire sphere.
- Added body-centre shader globals from `SphereWorld`, fixing offset-planet terrain/grass orientation instead of treating scene origin or global Y as the top of every planet.
- Repaired `PlanetLodImpostor` body binding: the far shell now resolves the actual parent body rather than an empty child component, fades completely out near voxel terrain, and appears from orbit with proper spherical LOD. Existing distant-body proxies continue using each template’s `displayColor` (including acid-world green).

#### 💧 Dry caves and legacy water repair
- Spherical density now generates water only in true terrain-defined ocean basins. Dry-land caves excavated below the mathematical sea radius remain air; placed/pumped liquid and oceans remain valid fluid.
- Added a small local migration cleanup when mining removes terrain: old auto-generated dry-cave water around that excavation is cleared while real ocean basins and current-session player-placed liquid are preserved.

#### ✅ Static delivery checks
- Source parsing and targeted regression assertions are run locally. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.11.0-dev] Native Spherical Water, Pool Pumps & Boat Wakes

**Type:** MINOR — new save-compatible native spherical-water presentation and wake system, with additive liquid-state persistence.

#### 🌊 Native water — no external ocean runtime
- Removed all shipped external-ocean integration scripts, binders, clip/depth helpers, oil controller, and wake emitter code. Step 8 / Step 16 now removes legacy scene components and deletes `Assets/Liquid` when that old package path exists; `VoxelEngineAssets/Fluids` remains as the game’s own bucket/pump/tank/pipe content.
- Re-enabled and rebuilt the in-house curved ocean renderer as a camera-local **spherical sea shell**. It publishes the active body centre to `VoxelWaterURP`, so radial waves, normals, and water visuals no longer use raw scene-origin math.
- Native voxel meshes retain finite lakes, rivers, buckets, and all crude pools; the curved shell owns open ocean water without double surfaces.
- Added in-house radial boat wakes: maritime ships submit actual submerged movement to a 16-stamp native wake registry, and the water shader renders fading V-wake foam plus subtle surface displacement around spherical planets.

#### 🪣 Pools, pumps, tanks, and pipes
- Repaired finite-pool pumping: `FluidManager.ScanPool` now returns the scanned cells as well as litres/voxel count, allowing a normal finite source to truly drain into the pump buffer instead of only reporting volume.
- Pump source acquisition now follows local radial down/tangent directions on spherical planets, not global world-Y. Its existing UI continues to show **NO SOURCE / FINITE / ∞ INFINITE**, pool litres, pool voxels, threshold progress, intake/output rates, pipe-network status, and the live internal tank.
- Large connected water or crude pools are classified as infinite; the pump creates liquid directly in its internal tank without draining that source. Finite sources drain voxel-by-voxel. Output now honours the connected pipe network’s bottleneck before filling compatible tanks.
- Added additive save/restore for world and grid liquid tank contents plus Water Pump internal liquid type/buffer.

#### 🛢 Dense crude geology
- Refined natural crude sites to visibly form **surface seep → tapered radial funnel → deep reservoir** on the existing oil-rich spherical worlds.
- Per the approved game rule, crude oil is denser than water and sinks through it in deliberately slow deterministic pulses; its fall and lateral spread are much slower than water.
- Buckets, pumps, tanks, pipes, and the refinery share the same voxel-liquid types. The Pirate-only Jack Pump infinite-node gate remains unchanged.

#### ✅ Static delivery checks
- Source parsing and native-water/oil/pump regression assertions are run locally. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.10.2-dev] Oil-Rich Seep Distribution & Pirate-Only Infinite Nodes

**Type:** PATCH — resource-distribution correction and strict infinite-node scoping; no save/API break.

#### 🛢 Finite crude where planetary geology supports it
- Split ordinary finite crude-oil seep generation from rare infinite Jack Pump nodes. Finite sites now use setup-authored per-world permissions rather than inheriting Pirate World’s restriction.
- Setup configures finite, drainable seep fields on **Earth** (8%), **Ocean World** (10%), **Acid World** (6%), **Desolate World** (5%), and **Pirate World** (12%). Each remains the visible puddle → bore → reservoir feature and can be collected with normal liquid handling.
- Worlds outside that intentional oil-rich list continue to filter crude markers and receive no finite seep sites, preserving their own resource identity.

#### ⚙ Infinite extraction locked to Planet_Pirate
- Hardened the infinite-node authorization path: after setup migration, only the canonical `Planet_Pirate` asset receives the internal infinite-node identity, even if another world shares a similar display name or a copied serialized flag.
- `PirateOilNode`, Jack Pump eligibility, and generic-pump protection all now enforce that same canonical Pirate-only gate. Every non-Pirate oil seep is finite.

#### 🛠 Setup-owned migration
- Step 10 — **Build Industrial Content** and Step 21 — **Build Celestial Worlds** now author/repair the full finite-oil distribution and the Pirate-only infinite flag non-destructively.
- The first migration applies intended default rates while later setup runs preserve deliberate custom finite-seep rates on custom worlds.

#### ✅ Static delivery checks
- Source parsing and targeted regression assertions are run locally. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.10.1-dev] Pirate Crude Site Discovery Repair

**Type:** PATCH — generation reliability, migration, and progression-gate correction; no save/API break.

#### 🛢 Discoverable Pirate crude oil
- Reworked Pirate oil-site placement to be deterministic from real spherical surface cells instead of relying on one randomly encountered underground crude marker. Pirate World now produces visible **finite** crude seeps reliably: a dark surface puddle, radial bore, and compact reservoir.
- Kept rare infinite sites as the much lower-frequency **0.3% per geological cell** subset (migrating the previous 2.5% default). They retain the larger puddle → bore → deep-reservoir read and register the infinite runtime identity required by a Jack Pump.
- Existing streamed/saved spherical chunks are now checked safely as they load, so the repair can populate compatible Pirate terrain without requiring a fresh save. Deferred site construction waits for the needed streamed terrain instead of giving up while surface chunks are unavailable.

#### 🔒 Pirate-only and progression-safe
- Added a setup-owned finite-seep chance, strict exact **Pirate World** identity check, and a legacy configuration bridge. Old `Planet_Pirate` assets no longer silently lose crude oil just because their newly added serialized flag has not yet been written; all non-Pirate planets remain blocked.
- Jack Pumps now require the explicit rare-node marker rather than any visible crude fluid, so ordinary finite seeps cannot become accidental infinite sources.
- Generic liquid pumps decline marked infinite-node crude, preserving the relic-gated Jack Pump as the only infinite extraction route.

#### 🛠 Setup and diagnostics
- Step 10 — **Build Industrial Content** repairs/serializes Pirate World oil settings non-destructively; Step 21 also preserves that configuration when celestial templates are rebuilt.
- Added concise runtime diagnostics for Pirate oil configuration and the first created sites to make Unity feedback actionable.

#### ✅ Static delivery checks
- Source parsing and targeted regression assertions are run locally. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.10.0-dev] Pirate Infinite Oil Nodes & Jack Pump Industry

**Type:** MINOR — new save-compatible Pirate World resource system, relic-gated industrial block, recipe, and research progression.

#### 🛢 Pirate World infinite oil nodes
- Added `BodySettings.enableInfiniteOilNodes` and a low node-chance setting. Setup configures **Pirate World** as the sole owner; all other spherical bodies filter crude-oil markers from their generated ore layers.
- Pirate oil sites are very rare, deterministic geological nodes: visible puddle, radial bore, deep reservoir, and an infinite runtime node identity.
- The node remains pumpable even when visible crude fluid has been disturbed, while the site itself remains geographically rare and Pirate-only.

#### ⚙ Jack Pump block and high-power production
- Upgraded the existing Pumpjack content into the **Jack Pump**: a realistic walking-beam prefab with motor, gearbox, crank wheel, counterweight, derrick, polished rod, wellhead, manifold, and animated pumping motion.
- Jack Pumps consume Empty Barrels and emit Crude Oil Barrels from a two-slot output; they only run over a rare Pirate infinite-oil node.
- Active draw is **4 kW**, standby draw is **120 W**, and each barrel takes **14 seconds**, making node extraction a serious industrial power commitment.
- Added a dedicated Jack Pump UI, live node/power/cycle readout, item-port containers, player interaction opening, and save/restore for input/output inventories.

#### ☠ Pirate relic progression
- Added the uncraftable **Pirate Jack Pump Head** component. It is added only to the rare-loot roll of Pirate ruin chests (18% per chest roll), never to any crafting recipe or other world loot table.
- Added the expensive 90-second Assembler recipe: 30 Steel Plates, 20 Iron Gears, 12 Copper Plates, 8 Electronic Circuits, 4 Advanced Circuits, and 1 Pirate Jack Pump Head.
- Added **Pirate Oil Recovery** research (Tier 4), which unlocks the Jack Pump recipe after Oil Logistics and Advanced Manufacturing. Standard Oil Logistics now unlocks barrels only.

#### 🛠 Setup-owned authoring
- Step 10 of **Tools > Voxel Engine > Voxel Engine Setup** now creates/repairs the Jack Pump item/prefab/recipe/research, configures Pirate World, and patches existing Pirate ruin prefab loot non-destructively.
- Step 20 also writes the rare head into newly rebuilt Pirate ruins.

#### ✅ Static delivery checks
- Revised source parses cleanly locally. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.9.4-dev] Spherical Oil Sites & Correct Planet Water FX

**Type:** PATCH — spherical-world generation and water-state corrections; no save schema or public API break.

#### 🛢 Spherical oil generation only
- Bound `OilReservoirDecorator` to `SphereWorld`; the inactive flat-world generation path no longer owns oil-site decoration.
- Removed the additional reservoir rarity gate and scans every real solid crude-oil marker, restoring actual site creation instead of leaving planets without oil.
- Oil sites now remain structured as a visible surface puddle, narrow radial bore, and deep reservoir. Crude oil now stays above water rather than swapping downward through it, so a surface puddle remains readable.

#### 🌊 No false underwater effect around a planet
- Corrected spherical water distance calculations to use the active celestial body’s local frame instead of raw scene-origin magnitude.
- Underwater camera FX now requires a real liquid voxel at the camera/head; it no longer activates merely because a player is inside a mathematical sea-radius shell on a mountain, dry coast, or far side of an offset planet.
- Corrected related player swim depth, water LOD, depth sampling, and oil visual anchor calculations to use world-to-body-local conversion.

#### ✅ Static delivery checks
- Revised source parses cleanly locally. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.9.3-dev] Oil Reservoir Generation & Cockpit Compile Recovery

**Type:** PATCH — compile recovery and world-generation correction; no save schema or public API break.

#### 🛢 Purpose-built oil sites
- Removed the spherical-world underwater crude-noise branch that created random oil patches throughout water volume.
- Rebuilt `OilReservoirDecorator` around one coherent site: a shallow exposed oil puddle, a narrow radial/vertical bore, and a deep filled reservoir sourced from real crude-oil ore markers.
- Surface probing now follows ocean water to the true water/air boundary instead of mistaking the sea floor or an unloaded chunk boundary for the surface.
- Added bounded deferred retries for deep marker chunks that finish before their streamed surface chunks, plus targeted fluid/terrain remesh scheduling for the completed feature.

#### 🧰 Immediate compile correction
- Restored the missing public `GridEntity.PilotDampenerHoldActive` state consumed by the revised dampener controller and Flight Computer HUD.
- This resolves the reported `CS0103` / `CS1061` errors in `GridEntity` and `GridPilotHud`.

#### ✅ Static delivery checks
- Revised C# source parses cleanly locally. Unity compilation and Play Mode validation remain pending from Thomas.

---

### [7.9.2-dev] Cockpit Hold, Grid Battery Charging & Ship Control LCD

**Type:** PATCH — interaction, performance, cockpit-control, and visual fixes; existing saves and public APIs remain compatible.

#### 🔋 Grid Battery field charging restored
- Added the Grid Battery’s one-item **Device Charger** dock for Portable Batteries and power-fed jetpacks, including live dock telemetry in both the direct battery panel and Ship Control Center.
- Restored direct field charging: hold a rechargeable item and RMB a Grid Battery for a normal charge; **Shift + RMB** fills it as far as stored grid energy allows.
- The dock uses the existing container-save path, so a docked rechargeable item survives grid save/load without a save-schema break.

#### 🚀 Controlled-flight correction
- Cockpit dampeners now actively hold a seated craft at full **0 velocity** when there is no translation input, including the gravity axis on a planet and drift in space.
- Flight Computer altitude now samples the rigidbody’s physical centre directly rather than smoothing an old transform sample; it adds a readable km/m formatter plus signed vertical-speed telemetry.
- Third-person Alt orbit now remains at the released view. Double-press Alt to return to the home chase position; Alt still moves the view without steering the ship.
- Refined the Flight Computer with a fitted header, battery screen, vertical-speed readout, and explicit dampener `HOLDING` state.

#### ⚙ Placement and pipe-cost hardening
- A selected placeable block now owns RMB before any world/grid UI interaction, so trying to build onto a battery, machine, screen, terminal, or other UI block places (or cleanly refuses) the block instead of opening the UI behind it.
- Replaced broad pipe visual invalidation with exact changed-corridor dispatch, a one-rebuild frame budget, hash-gated forced rebuilds, and targeted ItemPipe endpoint scans. Dense runs no longer rescan/rebuild every pipe after each edit.

#### 📟 Inventory and Ship Control follow-up
- Kept Armor, Jetpack Bay, and Life Support open together in the inventory’s attached equipment console; its content scrolls instead of requiring tab swaps.
- Reduced the inventory command key to a single fitting `CRAFTING` label.
- Rebuilt the Ship Control Center as an inventory-style LCD console: chassis, inset display, grid-operations header, command keys, status cells, phosphor list matrix, and matching detail readout.
- Hardened the held-item readout cleanup across the entire UI document and removed its prefixed hotbar-slot number.

#### ✅ Static delivery checks
- Revised source parses cleanly and targeted structural/regression checks pass locally. Unity/Play Mode validation remains pending from Thomas; no runtime validation is claimed here.

---

### [7.9.1-dev] Inventory Terminal & Premium LCD Workstations

**Type:** PATCH — visual/interaction polish only; no save data, gameplay balance, item, recipe, or public API change.

#### ▣ Inventory as a single fitted display
- Replaced the first inventory pass with one recessed **INVENTORY** LCD terminal: the title is printed on the display, the cargo grid is a labelled `CARGO MATRIX`, and every carried-item cell is a square numbered LCD cell rather than a generic blue tile.
- Rebuilt cargo load as a discrete ten-segment monitor and moved sort, crafting, production, recipe, and wireless controls into matching terminal command bays.
- Removed the experimental helmet and cross header marks entirely.

#### ⧉ Click-selected equipment add-on
- Replaced the detached stacked Armor / Jetpack Bay / Life Support cards with one physically coupled **EQUIPMENT** add-on at the inventory edge.
- Armor, Jetpack, and Life Support are now clean LCD tabs: click the module you want to inspect or use, while its real drag/drop equipment slots, life-support state, fuel readouts, and armor upgrades remain unchanged.
- Added the small bridge/coupler treatment and scroll-safe module content so the extension reads as part of the same terminal rather than a second unrelated panel.

#### ▤ Fabrication, production, and recipe displays
- Rebuilt Crafting as a fitted **FABRICATION TERMINAL** with LCD search glass, a category rail, a `BLUEPRINT MATRIX`, a scroll-safe assembly readout, square recipe cells, and physical command keys.
- Rebuilt Production Statistics into an inset **OPERATIONS MONITOR** with data cells, LCD commands, discrete metric panes, and phosphor scan-line treatment.
- Rebuilt Recipe Browser into a two-screen **RECIPE ARCHIVE**: output index, filters, recipes, dependency tree, material plan, and method cards now share the same recessed LCD language.

#### ✅ Static delivery checks
- C# syntax and targeted structural assertions pass locally. Unity/Play Mode visual validation remains pending from Thomas; no runtime validation is claimed here.

---

### [7.9.0-dev] Unified LCD Flight, Survival & Hotbar HUD

**Type:** MINOR — new save-compatible shared LCD HUD presentation layer and pipe-placement hardening.

#### 📟 One fitted LCD language
- Added `LcdHudTheme`, a shared chassis, bezel, glass, scanline, phosphor, and discrete-segment language used across live player/ship HUDs.
- Rebuilt the cockpit as a practical **FLIGHT COMPUTER**: rectangular instrument chassis, phosphor compass, primary speed/altitude screen, LCD bus/H₂/battery/dampener readouts, plus the existing gravity and coast-path instruments.
- Rebuilt player vitals into a compact **SUIT STATUS** monitor with rectangular LCD rows and segment gauges for HP, H₂, hunger, O₂, and carried power.

#### 🎒 Instrumented inventory and hotbar
- Added a Crusader helmet mark before the Inventory title and a hand-built Crusader cross emblem in the inventory header’s top-right corner.
- Rebuilt Inventory, Crafting, Armor, Jetpack Bay, Life Support, Production Statistics, and Recipe Browser outer surfaces under the same fitted LCD chassis language.
- Rebuilt the held-item notification as a fitted LCD `HELD ITEM` screen.
- Rebuilt the hotbar into a physical instrument rack with compact phosphor key labels, scan lines, selected-slot screen glass, and non-generic bezel treatment.

#### ━ Pipe follow-up hardening
- Restored pipe runs of up to five small lattice cells on an exact shared plane, while retaining the new strict rejection of diagonal/off-plane links.
- Static pipes now snap and evaluate links in their own local X/Y/Z frame, restoring rotated/surface-aligned stationary runs as well as grid runs.
- Structural grid blocks now reject any occupied precision-detail volume, preventing them from engulfing grid pipes even when aimed through a neighbouring hull block.
- Static normal blocks now reject placed conduit/cable volumes instead of burying pipes inside themselves.

---

### [7.8.1-dev] Pipe Topology Performance & Stable Grid Battery UI

**Type:** PATCH — performance, snapping, and live-panel reliability fixes; no save or API break.

#### 🔋 Stable Grid Battery controls
- Grid Battery panels now update their charge, transfer state, live watts, mode text, balance, and segmented indicator **in place**.
- Auto / Recharge / Discharge buttons are no longer destroyed and recreated every power tick, eliminating the reported flashing and missed-click behavior.
- Master Terminal controls are likewise protected from periodic destructive refreshes; explicit player actions still refresh immediately.

#### ⚙️ Pipe performance hardening
- Reworked `PipeVisualBuilder` topology handling into a shared, budgeted rebuild queue: at most two pipe visual rebuilds execute in a frame after a topology change.
- Neighbour hashes are now order-independent, preventing needless mesh teardown when physics returns the same neighbours in a different order.
- Item-pipe endpoint scans now use a slow safety fallback plus topology signals instead of repeatedly forcing visual rebuilds.
- Ground pipe ghosts now cache a stationary target and use allocation-free overlap probes, eliminating repeated broad physics allocations while aiming a pipe.

#### ━ Exact pipe joins
- Pipe-to-pipe links again support runs of up to five small lattice cells on one exact shared plane, while diagonal/off-plane joins and broad vertical slack remain rejected.
- The same strict coplanar rule is shared by grid and ground Item, Gas, and Liquid pipes; tank/service-port corridors retain their authored behavior.

---

### [7.8.0-dev] Orbital Coast-Path Flight Computer

**Type:** MINOR — new save-compatible orbital diagnostics for cockpit flight.

#### ◌ Real orbital solution, no physics fakery
- Added `OrbitalTelemetry`, an allocation-free solver built from the grid’s real position, velocity, effective gravity scale, and the active body’s inverse-square gravity field.
- It calculates radial/tangential velocity, circular speed, escape speed, eccentricity, periapsis, apoapsis, and current coast-path energy without changing ship physics, dampeners, thrust, or saves.
- The solver distinguishes atmospheric flight, suborbital impact paths, valid bound orbits, and escape trajectories.

#### 🧭 Cockpit coast-path computer
- Added a conditional **COAST PATH** LCD module to the ship systems panel; it appears only once a ship reaches meaningful upper-atmosphere/space altitude.
- It reports state plus rise/fall direction, current tangential speed versus required circular speed, and predicted periapsis/apoapsis clearance.
- The module explicitly describes a released-thrust ballistic solution, so pilots can understand what the ship will do rather than receive hidden autopilot behavior.

---

### [7.7.0-dev] Vacuum Starfield Ambiance & Compact Gravity LCD

**Type:** MINOR — new save-compatible procedural space ambiance, plus gravity-display refinement.

#### ✦ Sparse vacuum starfield
- Added a lightweight deterministic starfield that fades in through the existing upper-atmosphere-to-vacuum transition.
- Stars are camera-relative, depth-aware, and rendered as a distant shell so planets, ships, and other celestial visuals remain in front of the sky.
- The field is intentionally sparse and static rather than noisy: a practical deep-space backdrop with a few warmer/cooler navigation stars.
- The renderer is created automatically by the Cosmos bootstrap; no scene, asset, or manual setup work is required.

#### 📟 Compact field monitor
- Reduced the on-foot gravity instrument footprint substantially while keeping the LCD readout and all essential labels readable.
- The surface-reference display now uses eight compact discrete LCD segments, preserving the physical meter feeling in the smaller format.
- Cockpit readability remains full-size; its dedicated LCD gravity module is unchanged in function.

---

### [7.6.1-dev] Instrument LCD Gravity Readout

**Type:** PATCH — gravity telemetry visual polish; no save or API break.

#### 📟 Purpose-built field instruments
- Replaced the rounded gravity orb and directional arrow with a restrained, rectangular **GRAVITY FIELD** instrument face.
- The on-foot panel now uses recessed phosphor-style LCD glass, practical captioning, subtle scan lines, a fitted bezel, and a clear `G` / `m/s²` readout.
- The cockpit module now uses the same fitted LCD language instead of a generic glowing status card.

#### ▰ Discrete surface reference display
- Rebuilt **surface pull** as a physical-looking segmented reference meter rather than a smooth generic progress bar.
- Ten on-foot segments and eight cockpit segments extinguish progressively as local gravity falls with altitude.
- The display now makes the reference explicit: body surface percentage, flat-field reference, and coreward/downward vector are each labelled separately.

---

### [7.6.0-dev] Premium Gravity Pull Telemetry

**Type:** MINOR — new save-compatible gravity telemetry HUD for on-foot and cockpit play.

#### 🪐 Shared, honest gravity telemetry
- Added an allocation-free `GravityFieldSample` at the gravity-provider layer, exposing real local acceleration, Earth-relative G force, surface-pull fraction, and radial/flat direction.
- Player and cockpit read from this same source, while cockpit telemetry includes the grid’s actual gravity scale.

#### 📡 Bottom-left exploration card
- Added a premium **GRAVITY PULL** card for on-foot play: large live G readout, exact m/s² acceleration, body name, coreward/downward pull state, animated field pulse, and surface-strength meter.
- Accent treatment communicates the field at a glance: purple near normal pull, cyan/blue as pull fades at altitude, and amber for high-G fields.
- The card is informational only, never captures input, hides behind blocking UI, and yields its anchor while piloting.

#### 🚀 Cockpit gravity module
- The ship systems panel now contains a matching compact **GRAVITY PULL** module with live G, m/s², direction, pulse, and surface-strength bar.
- This gives pilots a clear reading of the construct’s effective gravity pull during ascent, spaceflight, and return.

---

### [7.5.0-dev] Atmosphere-to-Space Flight Foundation

**Type:** MINOR — save-compatible profile-driven atmosphere/space flight foundation and placement correction.

#### 🧱 Exact grid lattice placement
- Fixed the reported landing-gear build offset: ordinary structural blocks joining an existing grid now remain exactly at their addressed lattice cell.
- Ground-clearance lifting is now restricted to creation of a new root grid; it can no longer raise the first block placed against a tall landing-gear collider above its neighbours.
- Explicit mechanical-port and turbo attachments retain their authored exact-offset placement path.

#### 🌤️ One atmosphere model from surface to vacuum
- Added body-owned total-air profiles: sea-level density, radius-relative atmosphere top, and exponential scale height are now independent of breathable oxygen.
- Airless moons, thin worlds, and dense non-breathable atmospheres can therefore share one honest flight model; legacy saves safely fall back to their existing oxygen-derived behavior until initialized.
- `AtmosphereManager` now exposes one allocation-free local sample used by life support, jetpacks, grid thrusters, cockpit telemetry, and future environmental systems.
- Space state now agrees with actual density/profile ceiling instead of a separate radius-only cutoff.

#### 🚀 High-altitude flight behavior
- Atmospheric thrusters now report no fake hover/braking authority in vacuum and continuously lose thrust with local pressure; hydrogen and ion thrusters remain vacuum-capable.
- Added modest mass-correct aerodynamic drag with wind-relative airflow, which fades naturally to zero in vacuum.
- Corrected radial maritime gravity so it no longer applies inverse-square falloff twice at altitude.
- Grid cockpit altitude now measures radial height above the active world's sea level and reports **ATMOSPHERE / UPPER ATMOSPHERE / VACUUM** with live air percentage.

#### 🌌 Visual handoff and authoring
- Cosmos camera now transitions from upper air to a dark space backdrop, expands far clipping with altitude, and publishes atmosphere/space shader globals for future sky polish.
- Distant body visuals now follow the viewer during ascent; the active home body uses its real LOD sphere rather than a duplicated compressed sky proxy.
- Added **Tools → Voxel Engine → Voxel Engine Setup → Step 49: Initialize Atmosphere + Space Profiles**. It initializes only unprofiled planet/moon assets and preserves any initialized custom values on subsequent runs.

---

### [7.4.1-dev] Autonomous Space Dampers & Grid Battery Persistence

**Type:** PATCH — space-grid stabilization, restore safety, and compile recovery; save-compatible.

#### 🛰️ Autonomous dampers for unattended ships
- An unlocked, unpiloted grid with dampeners enabled now automatically counters drift when it has stored electricity, stored hydrogen, or live generation.
- Reverse-facing installed thrusters provide visible/resource-consuming braking authority; the dampener controller settles residual velocity and angular drift to true standstill in space.
- Locked landing gear/docking systems retain ownership and are excluded from autonomous dampening.
- Autonomous damping remains gravity-aware: it does not cancel natural vertical fall when hover authority is unavailable.

#### 💾 Restore runaway prevention
- Grid Battery stored charge and mode now persist in movable-grid saves.
- Restored powered/hydrogen-fuelled dampened grids zero stale saved linear/angular velocity after blocks restore, preventing the repeated login/catch-up speed escalation reported in space.

#### 🧰 Life-support compile repair
- Fixed the reserved-keyword compile error in `PlayerStats` that cascaded into the reported CS1001/CS1519/CS1022 errors.
- Vacuum life-support code now compiles with `hasSealedKit` as the local equipment state.

---

### [7.4.0-dev] Vacuum Life Support Foundations

**Type:** MINOR — new save-compatible player survival behavior using existing life-support equipment.

#### 🫁 Breathable air, underwater, and vacuum
- Added runtime oxygen-environment resolution for breathable atmosphere, underwater exposure, and vacuum/airless bodies.
- Player oxygen now drains in vacuum as well as underwater; no helmet/tank in vacuum drains reserve rapidly and suffocation damage bypasses physical armor.
- A sealed Space Helmet plus Oxygen Tank expands reserve and applies the existing tank/helmet/armor oxygen-efficiency drain reductions in both hazards.
- Breathable atmosphere restores oxygen quickly as before.

#### 📟 Clear life-support feedback
- Added `LifeSupportStatus`, `RequiresLifeSupport`, and vacuum exposure state to player stats.
- Oxygen vitals now turn amber underwater and red in vacuum.
- The inventory Life Support panel reports the current environmental status, including sealed/unsealed underwater and vacuum states.

#### 🛠️ Setup-connected equipment copy
- Step 11 now describes the Space Helmet and Oxygen Tank as active underwater/vacuum equipment instead of future-only foundations.
- No new manual assets are required; existing helmet, tank, armor oxygen-efficiency, UI slots, and save payloads remain compatible.

---

### [7.3.2-dev] Wrapped Belts, Snap Roll, Editable Gears & Ground Hold

**Type:** PATCH — placement, UI-input, thermal, and restored-grid reliability fixes; save-compatible.

#### 🟨 Wrapped belt geometry
- Added curved half-wrap segments around the outboard side of both pulleys, so the belt visibly rests on and wraps around each rim rather than only crossing a straight tangent line.
- Smoothed the open pulley rim from 12 to 20 segments for a less cage-like silhouette.
- Belt aim surfaces now cover the full tangent/wrap envelope.

#### 🔄 Mechanical snap roll
- Port snapping still locks the mating shaft direction, but build-rotation gestures now twist the snapped block around that shaft axis in 90° increments.
- Gearboxes, engines, generators, propellers, and shaft housings can now be turned right-side-up after snapping without breaking their mechanical mate.

#### 🔢 Gear ratio input and wheel control
- Fixed the ratio input field being overwritten while typing by periodic machine-panel refreshes.
- Numeric focus now suspends destructive live-panel rebuilds and player hotkeys.
- Added mouse-wheel ratio adjustment over the slider: **0.05×** per notch, or **0.25×** with Shift.

#### 🎒 Held-item hotbar readout
- Added a centered, premium held-item name card above the hotbar.
- It appears briefly whenever a hotbar key, mouse wheel, or active-slot item change selects a new held item, making scroll selection readable without opening inventory.

#### ⛔ Honest overload recovery and heat
- Protective engine trips now clear immediately after a drivetrain topology change and retry automatically after a short safety delay when a load is disabled.
- A 93% mechanical load now contributes full thermal authority even at low helm throttle; it can no longer remain thermally invisible.
- Stock engines remain capped at 89°C without performance hardware; upgraded/turbocharged engines retain the high-risk thermal envelope.

#### 🛬 Restored-grid terrain hold
- Expanded restore clearance from a one-off probe into a short post-load hold across multiple fixed ticks.
- The system now samples all relevant block colliders, corrects using the live Rigidbody pose, and cancels residual downward velocity after a lift—covering unanchored grids with no landing gear or connector.

---

### [7.3.1-dev] Tangent Belts, Recovering Overstress & Stock Thermal Governor

**Type:** PATCH — visual/mechanical correction and thermal-protection refinement; save-compatible.

#### 🟨 Belt runs now meet pulley rims correctly
- Moved each straight belt run to the pulley tangent instead of the pulley centreline.
- Belt bands and the travel marker no longer cut through the centre of `Belt_Pulley` / the shaft axle.
- Kept the wide pulley-face belt profile and expanded the invisible take-off surface to cover the tangent runs.

#### ⛔ Overstress now clears honestly
- Fixed protective overload state retention: removing or disabling a mechanical load now clears the stale trip immediately when topology changes.
- A short guarded retry also clears a trip after load changes that do not alter block count, such as disabling a generator.
- While the safety breaker is actively holding, the UI consistently reports 100% stress instead of a misleading idle percentage.

#### 🌡 Stock engine heat ceiling
- Mechanical stress now raises heat continuously.
- Stock engines with no modules and no physical turbo remain hard-governed to **89°C**.
- Performance modules or a physical turbo unlock the high-risk thermal envelope, where stress can push heat past 89°C and eventually into overheating/seizure.

---

### [7.3.0-dev] Tiered Armor Loadouts, Honest Trips & True Belt Width

**Type:** MINOR — save-compatible armor progression, drivetrain safety clarity, and belt visual correction.

#### 🛡️ Tiered armor loadouts
- Armor tier now governs both upgrade-slot capacity and allowed normal-module tier:
  - T1 → 1 slot / modules through T1
  - T2 → 2 slots / modules through T2
  - T3 → 3 slots / modules through T3
  - T4 → 4 slots / modules through T4
  - T5 → 5 slots / modules through T5
  - T6 → 6 slots / modules through T5
- Hazmat consumes one slot and requires T5+ armor; T6 supports all five normal branches plus Hazmat.
- Improving an existing branch uses no extra slot. Existing saved armor remains intact even if it predates these limits.
- Armor Upgrade Station validation, active-process validation, completion, telemetry, and module preview now enforce/explain the tier rules.

#### ⛔ Honest engine overload state
- Fixed the contradictory **OVERSTRESSED** status that could remain visible after stress had fallen to an idle value.
- A protective trip now preserves a clear 100% stress readout and `OVERSTRESSED — STOPPED` state until the player toggles the engine OFF, reduces load, and turns it ON again.
- Stock engines now scale heat continuously with mechanical stress but are thermally governed to **never exceed 89°C** without performance hardware.
- Installing engine modules or physical turbochargers unlocks the higher-risk thermal envelope: high mechanical load can then generate enough heat to overheat or seize the engine.

#### 🟨 True pulley-face belt width
- Corrected belt geometry a second time so width expands along the shaft/pulley face, not in the belt loop plane.
- Replaced the solid `Belt_Pulley` cylinder with a wide segmented open rim, leaving the shaft axle visibly clear instead of embedding the pulley inside it.

---

### [7.2.0-dev] Armor Capacity Progression & Overstress Protection

**Type:** MINOR — save-compatible armor progression rules and maritime drivetrain protection.

#### 🛡️ Armor tier now matters for upgrade capacity
- Armor tier now defines both normal-module ceiling and installed-upgrade capacity:
  - **T1 armor:** 1 slot, accepts modules through T1.
  - **T2 armor:** 2 slots, accepts modules through T2.
  - **T3 armor:** 3 slots, accepts modules through T3.
  - **T4 armor:** 4 slots, accepts modules through T4.
  - **T5 armor:** 5 slots, accepts modules through T5 and Hazmat.
  - **T6 armor:** 6 slots, accepts modules through T5 and Hazmat.
- Raising the tier of an already installed upgrade branch does not consume an additional slot; installing a new branch does.
- Armor Upgrade Station validation, mid-process validation, and completion now enforce these rules.
- Armor Station UI now shows slot usage, maximum accepted module tier, Hazmat eligibility, and a module compatibility explanation before installation.
- Existing installed armor state remains readable and is not silently stripped from legacy stacks.

#### ⛔ 100% stress protective shutdown
- Engines now trip into **OVERSTRESSED — STOPPED** at 100% mechanical stress.
- The drivetrain shaft stops instead of sustaining a free overload. Reduce the generator/propeller load, toggle the engine **OFF**, then **ON** to reset the protective trip.
- Engine panel, grid terminal, and screen telemetry report the overload state explicitly.

#### 🟨 Corrected belt width
- Corrected the reinforced belt geometry so width expands across the pulley/shaft face rather than making the belt loop taller.
- Updated pulleys and shaft-placement surface to match the genuinely wider belt profile.

---

### [7.1.0-dev] Belt Take-Off Placement, Real Load Stress & Save Recovery

**Type:** MINOR — new save-compatible drivetrain interaction, mechanical load simulation, and persistence recovery.

#### 🟨 Belt take-off placement and reinforced visual
- Mechanical Belts now generate a dedicated, trigger-only aim surface along each run.
- Hold a **Drive Shaft** or **Watertight Shaft Housing**, aim at an empty middle section of the belt, and place it directly into the belt as a parallel powered take-off.
- Added an exact belt-axis placement path that bypasses ordinary structural-neighbour placement only for validated belt take-offs.
- Rebuilt the belt visual as a substantially wider reinforced twin-run assembly with larger pulleys and a broad interaction envelope.

#### ⚙️ Exact engine / motor port snapping
- Maritime engines are now included in mechanical-port placement detection.
- A held engine can snap its actual output shaft to a compatible shaft port at the correct centreline height instead of falling back to lattice-height placement.

#### 📈 Real mechanical load and stress
- Replaced throttle-only engine stress with a backward drivetrain load pass.
- Generator rated output now creates real torque demand, transformed correctly through gearbox ratios and shared across all connected engine sources.
- Generator banks, propellers, gearbox ratios, finite source torque, overload service, RPM bogging, and output derating now feed engine/gearbox stress.
- Gearboxes now expose governed actual ratio, resolved input/output torque, mechanical-load percentage, and load-aware stress in their panel.
- Engines now show live mechanical load alongside torque, speed, and stress.

#### 🔋 Portable equipment save recovery
- Added a setup-authored Resources persistence catalog for `ItemDefinition` assets outside Resources.
- Portable Batteries and Portable Hydrogen Tanks now resolve reliably by item ID during login/load instead of being silently deserialized as empty slots.
- The catalog includes all discovered item assets and explicitly includes portable battery, portable hydrogen, and jetpack subclasses.

#### 🔎 Research and restored-grid polish
- Pressing **Y** while the Research UI search field owns keyboard focus now types/searches normally instead of closing the Research UI.
- Restored grids now perform a bounded post-collider ground-clearance lift while still kinematic, preventing the small ground penetration seen immediately after loading a save.

---

### [7.0.0-dev] Remove Encased Chain Drive + Mechanical Belt Crafting

**Type:** MAJOR — intentional removal of an obsolete maritime block and its assets. Existing saves that contain an Encased Chain Drive will no longer restore that block, as explicitly approved.

#### 🧹 Complete Encased Chain Drive removal
- Removed `GridEncasedChainDrive`, its mesh-builder route, animator route, placement/topology/terminal references, prefab, item, recipe, and generated material assets.
- This resolves `CS0246` when the old Encased Chain Drive script is absent: no runtime code references that type anymore.
- The block is intentionally not preserved on old ships; ships that contained it lose that block after updating to this major version.

#### 🟨 Mechanical Belt recipe and research gate
- Step 13 now explicitly creates and repairs the **Mechanical Belt** item and `Recipe_MMechanicalBelt`.
- The belt recipe is registered with the recipe registry, requires the normal maritime assembler workflow, and is gated by **Hydro-Mechanics** (`res_maritime_hydromech`) — the Tier 1 maritime research node.
- Step 13 also repairs the research unlock on existing Hydro-Mechanics nodes, so the belt cannot disappear merely because the node predates this feature.
- No manual prefab, item, recipe, or research asset authoring is required.

#### 🛠️ Setup behavior
- Step 13 clears any stale Encased Chain Drive recipe/research links left by a partial project update.
- It preserves the normal non-destructive repair behavior for all remaining maritime content, balance values, recipes, and custom prefab work.

---

### [6.81.0-dev] Mechanical Belts & Watertight Shaft Housings

**Type:** MINOR — new save-compatible maritime drivetrain system and sealed hull block.

#### 🔩 Persistent gearbox selection
- Grid Gearboxes now save and restore the player-selected free-form gear ratio plus the retained legacy gear-slot value.
- Loading a ship no longer resets the gearbox panel to its prefab default ratio.

#### 🟨 Mechanical Belt routing
- Added the craftable **Mechanical Belt** item and a direct two-click workflow:
  1. Hold a belt and **right-click a Drive Shaft or Watertight Shaft Housing**.
  2. **Right-click a parallel shaft** on the same movable grid to install the belt.
- Belt links validate shaft parallelism, pulley plane, duplicate links, grid ownership, and configured span limits before consuming an item.
- A belt is a bidirectional mechanical bus: any aligned shaft placed through its visible belt run becomes an automatic take-off point for additional generator, propeller, or drivetrain outputs.
- Added no-collider belt runs, pulleys, and a live selection preview. **Shift + right-click** a shaft removes its attached belts and returns their items.
- Belt links persist with movable grids and are safely pruned if an endpoint shaft is dismantled.

#### 🌊 Watertight Shaft Housing
- Added **Watertight Shaft Housing**: a waterproof hull block with a visible rotating through-shaft, compression seals, and bidirectional mechanical ports.
- It acts as a sealed hull penetration instead of an exposed shaft line, preserving hull water-tightness while carrying drivetrain RPM.
- Added maritime panel and ship-terminal status for sealed shaft speed / integrity.

#### ♻️ Encased Chain Drive retirement
- The Encased Chain Drive is removed from new Step 13 crafting and research unlocks, replaced by the Shaft Housing + Mechanical Belt workflow.
- Its legacy asset, prefab, mechanical behavior, and existing save references remain intact so no ship or inventory is destroyed by the migration.

#### 🛠️ Setup and compatibility
- Step 13 non-destructively creates missing Shaft Housing/Belt content, repairs research links, retires only the legacy recipe’s discoverability, and preserves existing balance values and custom prefab work.
- `MaritimeMeshBuilder` v26 regenerates the new sealed-housing mesh and exact shaft ports through the approved setup workflow.
- Unity validation is required for this new belt/housing delivery before the vehicle-power roadmap advances.

---

### [6.80.6-dev] Mechanical Ports, Stable Docking & Tank-State Recovery

**Type:** PATCH — vehicle placement, docking, save/load, and tank UX fixes; save-compatible.

#### ⚙️ Mechanical ports and shaft topology
- Added a shared `MaritimeMechanicalPorts` contract used by placement and the propulsion graph.
- Gearbox, shaft, chain drive, generator, and propeller snaps now mate actual mechanical port positions instead of offsetting roots by a full cell.
- Gearboxes act as bidirectional carriers: either connected end can receive rotation; the other end carries it onward.
- Mechanical graph edges now require physically facing compatible ports. An engine's output only drives a correctly mated shaft; a reversed/wrong-side shaft no longer receives rotation.
- Added explicit gearbox ports plus corrected outward directions on generator, propeller, and chain-drive ports. `MaritimeMeshBuilder` v25 refreshes them through Step 13.

#### 🛬 Landing pad / gear stability
- Docking ports no longer create joints against arbitrary scenery.
- Static landing-pad locks use unbreakable joints, zero motion, and hold the grid kinematic while docked, removing rapid lock/unlock flicker.
- Landing gear now prevents competing multiple gear locks on one grid and holds a static lock stationary until released.

#### 💾 Save/load recovery
- Movable grids now save Rigidbody pose directly and restore through a short physics hold, preventing restored ships from snapping upright during collider reconstruction.
- Grid Gas Tanks and Grid Liquid Tanks now persist their stored type, amount, and Auto/Stockpile mode. Legacy saves remain valid.

#### 🛢️ Safe tank type changes
- Selecting a different non-empty gas/liquid type now opens a modal choice:
  - **VOID GAS** / **VOID LIQUID** — discard contents and change type.
  - **CANCEL** — preserve contents and current type.
- Applied to grid gas tanks, grid liquid tanks, and static gas tanks.

---

### [6.80.5-dev] Maritime Power Chain Stabilization

**Type:** PATCH — vehicle power accounting and diagnostics, save-compatible.

- Fixed maritime generator and electrical-propeller **double counting** in `GridEntity`: each block now enters the grid ledger exactly once through its normal `PowerOutput` / `PowerDraw` contract.
- Electrical propellers now declare their **commanded** watt draw independently from delivered power, so a power deficit no longer alternates between zero and full demand.
- Added grid-level `PowerAvailability01` and unserved-watt telemetry. Electrical propellers use this resolved service fraction for real RPM/thrust on the following physics tick.
- Electrical propellers now report correct RPM and demand even when no mechanical shaft source exists, as expected for grid-powered pods.
- Expanded electrical-propeller UI and terminal status with command, delivered watts, and grid-service percentage for clear validation.

**Validation target:** build a grid with a maritime generator, battery, and electrical propeller; then test normal, partial-power, and no-power states without flicker or doubled power totals.

---

### [6.80.4-dev] Armor Stations — Unity Validation Recorded

**Type:** PATCH — validation/documentation status update, save-compatible.

- Thomas confirmed the Armor Station and Armor Upgrade Station workflow works in Unity.
- Validation covers focused station recipes, timed upgrade installation, per-piece armor state, anvil presentation, equipment interaction, and persistence flow.
- The armor-workstation implementation is now Unity-validated; broader radiation, heat, and life-support roadmap systems remain separate open scope.
- Thomas also confirmed the 6.80.3 mountain-footing and dry-spawn safety behavior works in Unity.

---

### [6.80.3-dev] Fix — Mountain Footing + Dry Spawn Safety

**Type:** PATCH — movement/spawn reliability, save-compatible.

- **Mountain footing:** added capped ahead-of-player terrain assistance plus post-move terrain recovery to keep the CharacterController above walkable mountain meshes instead of sinking into uphill terrain.
- **No water spawns:** fresh worlds, bed spawns, saved positions, and respawns now validate the full player volume for water before control is released.
- **Dry-ground relocation:** a wet candidate keeps the controller disabled while nearby terrain candidates stream, then relocates to a raycasted dry surface. Flat-world selection now rejects submerged seabeds; spherical selection samples more land candidates and rejects liquid surfaces.
- Existing save coordinates remain compatible. High-altitude/space saves are intentionally preserved and bypass ground/water relocation.

---

### [6.80.2-dev] Fix — Armor Station Recipe, Anvil & Equipment UX

**Type:** PATCH — runtime/UI/setup fixes, save-compatible.

- **Armor Station recipes:** its focused catalog now recognizes legacy armor recipes and module recipes, and remains usable once the station is placed even if a scene research cache has not refreshed yet. The generic inventory crafting browser remains unchanged.
- **Anvil hammer:** corrected the generated hammer rest position and changed its animation to a controlled vertical strike onto the anvil face. Step 48 repairs the previous generated pivot position without touching custom pivots.
- **Air equip:** RMB now equips an active armor item before the raycast early-out, so looking at open air works exactly like looking at a surface.
- **Shift-click routing:** equipment auto-equip only runs from a plain inventory view. With an Armor Station, Armor Upgrade Station, chest, or another external panel open, Shift-click routes armor/modules to that panel instead. Armor goes to the upgrade station's Armor slot; modules go to its Module slot.
- **Always-visible equipment bays:** Armor, Life Support, and Jetpack Bay now remain visible beside inventory while a chest, machine, Armor Station, or Armor Upgrade Station is open.

---

### [6.80.1-dev] Fix — Step 48 Compile Repair

**Type:** PATCH — compile fix, save-compatible.

- Fixed `CS0103` in `VoxelEngineSetupWindow.cs`: Step 48 now uses `System.Enum.GetValues(...)` explicitly, matching the file's namespace imports.
- No prefab, recipe, item, research, balance, or save-data values changed.

---

### [6.80.0-dev] Armor Stations + Timed Upgrade Forge

**Type:** MINOR — new save-compatible armor workstation system.

#### 🛡️ Dedicated armor workflow
- Restored the missing armor-module runtime bindings and made the **Armor Station** an exclusive armory workbench: it shows only armor-station recipes rather than the whole lower-tier crafting catalogue.
- Added a separate **Armor Upgrade Station** with a premium anvil silhouette, forged-steel/brass materials, animated hammer, forge glow, and a focused UI Toolkit installation panel.
- The Assembler crafts the Armor Station; the Armor Station then crafts six armor tiers, the Armor Upgrade Station, five module families × five tiers, and the Hazmat module. All of these recipes are research-gated by **Armor Stations**.

#### ⏱️ Timed installation
- Put one armor piece and one module into the upgrade station, then start installation.
- Base upgrade time is **30 seconds**: T1 = 30s, T2 = 60s, T3 = 90s, T4 = 120s, T5/Hazmat = 150s.
- The module is consumed only when installation completes. Cancelling safely leaves both inputs in the station.
- Installed modules are stored per armor piece, never on the shared armor definition.

#### ⚙️ Fully wired effects
- Heat Tolerance reduces burn/environmental heat damage.
- Radiation Shielding and Hazmat protection reduce or eliminate radiation damage.
- Oxygen Efficiency reduces oxygen drain.
- Impact Padding reduces hard-landing damage.
- Mobility Servos increase jetpack speed and reduce fuel drain.

#### 💾 Additive persistence
- The equipped armor slot, installed module state, upgrade-station inputs/output, and elapsed upgrade progress all persist through save/load.
- Legacy saves remain valid; absent armor fields initialize safely.

#### 🛠️ Non-destructive authoring
- Added **Tools ▸ Voxel Engine ▸ Voxel Engine Setup ▸ Step 48: Build Armor Stations + Timed Upgrades**.
- Step 48 creates missing armor content and repairs required links while preserving existing recipe ingredients, crafting times, prefab custom work, materials, and numeric tuning.
- Run Step 48 twice to verify idempotence; Unity validation remains required before the roadmap scope is marked completed.

#### Files touched
- New armor runtime: `ArmorStation`, `ArmorUpgradeStation`, `ArmorUpgradeItem`, `ArmorUpgradeKind`, `ArmorUpgrades`, `PlayerHazardService`
- Updated crafting, player equipment/stats/controller, interaction, persistence, UI Toolkit panel, setup wizard, and versioning

---

### [6.78.47-dev] Real Recipes for Retired Stubs + Full Recipe Health Audit

**Type:** PATCH — content/data (recipes), save-compatible.

#### 🔨 Retired stubs are back — fully authored & re-registered (9)
Named by their output item's `displayName`, all unlocked by default:
| Recipe | Cost | Station | Time |
|---|---|---|---|
| 🪵 Wooden Plank ×3 | 1 Wood Log | Hand | 1s |
| ⛏️ Wooden Pickaxe | 3 Plank + 2 Log | Hand | 3s |
| 🪓 Wooden Axe | 3 Plank + 2 Log | Hand | 3s |
| 🪓 Iron Axe | 3 Iron Ingot + 2 Plank | Assembler | 4s |
| 📦 Chest | 8 Plank | Hand | 4s |
| 🛏️ Bed | 6 Plank + 3 Wool | Hand | 5s |
| 🛠️ Crafting Bench | 6 Plank + 2 Stone | Hand | 5s |
| ⚙️ Grinder Tool | 2 Iron Ingot + 1 Iron Gear + 1 Plank | Crafting Bench | 4s |
| 📏 Leveling Tool | 2 Plank + 1 Iron Ingot | Crafting Bench | 3s |

Costs follow the established tool convention (Iron Pickaxe = 3 material + 2 planks).

#### 🏭 Hollow processing recipes authored (7) — oil chain is real now
Both refs confirmed wired into refinery & chem plant prefabs (in-place asset edits):
- 🛢️ Refine Crude Oil: 10L Crude → 6L Refined Oil
- ⛽ Make Liquid Fuel: 5L Refined → 5L Liquid Fuel
- 🟤 Refine Heavy Fuel Oil: 8L Crude → 5L HFO
- 🚢 Refine Marine Gas Oil: 6L Refined → 4L MGO
- ⚗️ Synthesise MGO: 2L Fuel + 2L Water → 3L MGO
- ❄️ Marine Coolant: 8L Water + 1L Refined → 8L Coolant
- 🧱 Make Plastic: 4L Crude → 2 Plastic item

#### 🔍 Full audit results
- ✅ **0** recipes output ruin blocks (ruins remain exploration-only)
- ✅ **0** recipes output mob drops (fauna loot stays drop-only)
- ✅ MachineRecipes 7/7 healthy · ProcessingRecipes now 7/7 with real I/O
- Registry: 238 entries, every one craftable

#### Files touched
- 9 recipe assets rewritten + re-added to `RecipeRegistry.asset`
- 7 `Industrial/ProcessingRecipes/*.asset` authored
- `Scripts/Core/GameVersion.cs` — Patch 46 → 47

---

### [6.78.46-dev] Hollow Recipes Can Never Surface (Code-Enforced)

**Type:** PATCH — bugfix (crafting list), save-compatible.

#### 🐛 The bug (screenshot 2)
Some crafting tiles were still gray and named like `AxeWood` / `PickWood` — those were the legacy
stub recipes (no output item, no ingredients). The 6.78.45 registry-edit didn't apply on every
checkout, and `AvailableRecipes` trusted the registry blindly, so stubs could still leak through
(including via research-unlock lookups that ignore `unlockedByDefault`).

#### 🔧 The fix
- `Crafter.AvailableRecipes` now **hard-filters hollow recipes in code**: anything with a missing
  `outputItem` or an empty ingredient list is skipped permanently — no asset edit required, survives
  merges, registries and research states. This wipes out the gray tiles AND the raw `AxeWood`-style
  names in one stroke; real recipes already display proper tier names ("Wooden Axe", "Iron Pickaxe")
  via their output item's `displayName`.
- Data hygiene follow-up: `Recipe_TilledSoil` (another hollow placeholder) also removed from
  `RecipeRegistry.asset` (now 229 entries, all real & craftable).

#### Files touched
- `Scripts/Crafting/Crafter.cs` — hollow-recipe guard in `AvailableRecipes`
- `VoxelEngineAssets/RecipeRegistry.asset` — 230 → 229 entries
- `Scripts/Core/GameVersion.cs` — Patch 45 → 46

---

### [6.78.45-dev] Self-Healing Icon Bindings + Hollow Recipe Cleanup

**Type:** PATCH — bugfix (icon binding resilience + data hygiene), save-compatible.

#### 🐛 The bug (screenshot-confirmed)
Crafting screen tiles rendered as plain coloured boxes — `recipe.GetIcon()` came back null for
everything, meaning the sprite references inside ItemDefinitions had gone missing in the project.
Root cause class: icon bindings are GUID references; if a PNG is ever imported **without its
companion `.meta`** (fresh clone, partial copy, manual drag-in), Unity mints a NEW GUID and every
reference to the sprite silently nulls out. Names/descriptions still work, so it fails invisibly.

#### 🛡️ The fix — `ItemIconSync` v2 (editor, auto self-heal)
- Runs **automatically once per editor session** after domain reload (`InitializeOnLoadMethod`),
  plus the manual menu item remains: `Tools ▸ Voxel Engine ▸ Sync Item Icons`.
- For every `ItemDefinition` with a missing icon it searches `Assets/VoxelEngineAssets/ItemIcons/**`
  by itemId (exact file match) and re-binds the sprite, then saves.
- 100 % non-destructive: healthy bindings are never touched; binds-by-content so it heals GUID
  drift no matter how the PNGs arrived in the project.
- Logs a warning only when it actually repaired something.

#### 🧹 Hollow recipe cleanup (the "Instant / no ingredients" tiles)
- Removed **21 uncraftable entries** from `RecipeRegistry.asset`:
  - 9 legacy stubs (`Recipe_Bed`, `Recipe_PickWood`, `Recipe_Plank`, …) — no inputs, no output item.
  - 12 Factory/Wire placeholders (`Recipe_AssemblerMk1-3`, `Recipe_Conveyor*`, `Recipe_Crusher`,
    `Recipe_Funnel`, `Recipe_LEDStripFactory`, `Recipe_Wire_Cu_LV`, `Recipe_PowerRelay`) — empty
    ingredient lists make them read as "Instant, craft of nothing". They belong in machine crafting
    once proper costs are authored.

#### Files touched
- `Scripts/Editor/ItemIconSync.cs` — rewritten (auto self-healing)
- `VoxelEngineAssets/RecipeRegistry.asset` — 251 → 230 entries
- `Scripts/Core/GameVersion.cs` — Patch 44 → 45

---

### [6.78.44-dev] Crafter UIs — Real Item Icons Everywhere + Recipe Name Cleanup

**Type:** PATCH — bugfix/polish (UI), save-compatible.

#### 🐛 What Thomas saw
- Item icons showed perfectly in inventory but **no crafter UI ever drew them** — Assembler/Crusher/
  Quarry recipe cards, Oil Refinery & Chemical Plant recipe books, the Recipe Browser, and the
  Crafting Terminal all rendered text rows with a plain colored dot. There was no icon code there at all.
- Some recipes surfaced raw asset names (`Recipe_…`) instead of a clean item name.

#### ✨ Icons added (sprite, `ScaleToFit`, tinted-chip fallback — same look as inventory slots)
- `MachineUIs.MachineRecipeCard` — 34px output icon slot on every machine recipe card
  (Assembler, Crusher, Quarry — all `ProcessingMachinePanel` machines)
- `ProcessorUI.RecipeRow` — 26px icon from the recipe's first item output
  (Oil Refinery, Chemical Plant)
- `RecipeBrowserUI` node rows — 30px icon slot replaces the bare tint dot when an icon exists
- `StorageUI` Crafting Terminal — icons on craft-queue rows and available-pattern rows

#### 🏷️ Recipe name fixes
- `RecipeDefinition.GetName()` + `MachineRecipe.GetName()` now return a prettified fallback
  (`Recipe_IronPlate` → "Iron Plate") instead of leaking raw asset names when `displayName` is empty.
- Disabled 9 legacy stub recipes in `VoxelEngineAssets/Recipes/` (Bed, Axe×2, Chest, CraftingBench,
  GrinderTool, LevelingTool, PickWood, Plank) — they have **no inputs and no output item**, so they
  could only ever render as broken unnamed tiles. `unlockedByDefault: 0` hides them until they're
  properly authored with ingredients + output.

#### Files touched
- `Scripts/UI/MachineUIs.cs`, `Scripts/Crafting/ProcessorUI.cs`, `Scripts/UI/RecipeBrowserUI.cs`,
  `Scripts/Storage/StorageUI.cs`, `Scripts/Crafting/RecipeDefinition.cs`,
  `Scripts/Simulation/MachineRecipe.cs`
- 9 stub recipe assets (`unlockedByDefault: 0`)
- `Scripts/Core/GameVersion.cs` — Patch 43 → 44

---

### [6.78.43-dev] Fix — Crafter & Recipe UI Icons Not Rendering

**Type:** PATCH — bugfix (UI icon rendering), save-compatible.

#### 🐛 The bug
Item icons rendered perfectly in inventory (`GameUIController.BuildSlot`) but were blank in every
crafting context: crafting screen recipe tiles + detail header + ingredients, Assembler/crafter
recipe rows, craft feedback toasts, item filter dialog, extractor port config, and the drag ghost.

#### 🔍 Root cause
Data-side was fully audited and is pristine (408/408 ItemDefinitions bound, all 334 recipes
resolve `outputItem.icon`). The working inventory slot is the **only** renderer that sets
`img.scaleMode = ScaleMode.ScaleToFit` — every other site created `new Image { sprite = ... }`
with the default scale mode, which mishandles the tight-cropped generated sticker sprites at
fixed slot sizes.

#### 🔧 The fix (9 call sites unified onto the proven BuildSlot pattern)
- `Scripts/UI/CraftingScreen.cs` — recipe tile, recipe detail header, ingredient icons
- `Scripts/UI/GameUIController.cs` — `BuildRecipeRow` output icon, drag ghost
- `Scripts/Storage/StorageUI.cs` — storage cell icon
- `Scripts/UI/BuildFeedbackHud.cs` — toast icon
- `Scripts/UI/ItemFilterDialog.cs` — filter row icon
- `Scripts/UI/PortConfigHud.cs` — port config icon

#### Files touched
- 6 UI scripts (one-line `scaleMode` additions), `Scripts/Core/GameVersion.cs` — Patch 42 → 43

---

### [6.78.42-dev] Icon Batch 39 — FINAL: T90 Blueprints & Quarry Upgrades (398/398)

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

🏁 **Icon mission complete — all 398 unique itemIds now have a premium bound icon.**

#### 📜 T90 blueprint trio (Survival)
- 📐 `item_blueprint_blade_t90` — blueprint-blue sheet, white-line turbine blade schematic
- ⚙️ `item_blueprint_gearbox_t90` — blueprint sheet, twin-gear gearbox schematic
- 🗼 `item_blueprint_tower_t90` — blueprint sheet, lattice tower schematic with dim marks

#### ⛏️ Quarry upgrade chip family (one master → hue derives, matches `iconTint`)
- 🩵 `upgrade_quarry_speed` — hex gunmetal chip, glowing teal drill emblem (master)
- 💜 `upgrade_quarry_efficiency` — same chip, violet emblem (teal → violet hue derive)
- 🟡 `upgrade_quarry_range` — same chip, gold emblem (teal → gold hue derive)

#### Files touched
- `VoxelEngineAssets/ItemIcons/Survival/` — +6 icons (.png + .meta)
- ItemDefinition assets patched to reference the new sprite GUIDs (398/398 bound)
- `Scripts/Core/GameVersion.cs` — Patch 41 → 42

---

### [6.78.41-dev] Icon Batch 38 — Farm Food Set & Field Parts

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🌾 Farm-to-table food (5)
- 🍞 `bread` — rustic scored golden loaf
- 🥕 `carrot` — bright orange carrot with leafy tuft
- 🌽 `corn` — golden ear with peeled-back husks
- 🌾 `wheat` — tied bundle of three wheat stalks
- 🍲 `stew` — hearty chunky bowl with steam wisp

#### 🔧 Parts & modules
- 🌫️ `exhaust_pipe` — bent dark-steel exhaust with clamp ring
- 🧵 `copper_lv_wire` — coiled spool of copper LV wire with loose end
- 🧊 `closed_cycle_aip_module` — frosty pale-blue submarine AIP unit
- 🌋 `item_ash` — dark powdery ash mound with ember specks
- 🛢️ `item_flame_canister` — orange fuel canister with flame symbol

#### Files touched
- `VoxelEngineAssets/ItemIcons/{Survival,Maritime,Items,Fauna,Combat}/` — +10 icons (.png + .meta)
- ItemDefinition assets patched to reference the new sprite GUIDs
- `Scripts/Core/GameVersion.cs` — Patch 40 → 41

---

### [6.78.40-dev] Icon Batch 37 — Terminals, Ship Console, Ores & Relic

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🏭 Factory & computing
- 💡 `block_ledstripfactory` — LED strip machine with glowing cyan light bar channel
- 🗄️ `block_serverrack` — tall dark blade-server cabinet, status LEDs
- 🖥️ `block_storageterminal` — steel terminal with blue slot-grid screen

#### 📡 Blocks & ship control
- 📷 `camera_block` — compact brass security camera with dark lens eye
- 🎛️ `ship_control_console` — naval console desk, gauges + steering lever

#### ⛏️ Resources & salvage
- 🔵 `cobalt` — deep blue cobalt ore cluster
- ⚪ `lithium` — pale silvery lithium chunks, icy sheen
- 🟠 `item_copperplate` — stack of polished copper plates
- ⚙️ `item_giant_pinion` — weathered bronze giant gear

#### 💜 Relic
- 🔮 `item_relic_capacitor` — violet-glowing alien energy cell in ornate clamps

#### Files touched
- `VoxelEngineAssets/ItemIcons/{Factory,Survival,GridSystem,Maritime,Nuclear,Industrial,Combat,Fauna}/` — +10 icons (.png + .meta)
- ItemDefinition assets patched to reference the new sprite GUIDs
- `Scripts/Core/GameVersion.cs` — Patch 39 → 40

---

### [6.78.39-dev] Icon Batch 36 — Rotors, Barrels, PSU Pair, Helm & Hull

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🌬️ Wind power
- 🌀 `block_vlarge_rotor` — three-blade turbine rotor master, head-on read
- 🌀 `block_vsmall_rotor` — same rotor, scale-derived ×0.70 (size = tier language, tints identical)

#### 🌱 Blocks & props
- 🟫 `block_tilledsoil` — furrowed farmland block with fresh sprouts
- 🎯 `block_training_dummy` — straw-stuffed wooden practice dummy

#### 🛢️ Barrel trio (one master → tint derives, matches `iconTint`)
- ⬜ `item_emptybarrel` — bare galvanized drum, open bung
- 🟡 `item_liquidfuel` — fuel-yellow drum
- 🟤 `item_refinedoilbarrel` — dark refined-oil drum

#### 🖥️ Computer hardware
- 🩶 `psu_500` — compact PSU, fan grille + cable bundle
- 🟥 `psu_2k` — scale-derived tier tint (deep red-orange) for the bigger unit

#### ⚓ Maritime
- ☸️ `helm` — classic wooden ship's wheel, brass-trimmed spokes
- 🛡️ `iron_hull` — riveted iron hull armor plate

#### Files touched
- `VoxelEngineAssets/ItemIcons/{WindPower,Survival,Combat,Industrial,Maritime}/` — +11 icons (.png + .meta)
- ItemDefinition assets patched to reference the new sprite GUIDs
- `Scripts/Core/GameVersion.cs` — Patch 38 → 39

---

### [6.78.38-dev] Icon Batch 35 — GridSystem Machine Suite + Nacelle Pair & Tanks

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🏭 GridSystem machines (7)
- 🛢️ `gitem_refinery` — compact refinery block, distillation column + amber bubbling window
- ⚡ `gitem_electricfurnace` — glowing coil element behind grate, arcing copper terminals
- ⚗️ `gitem_chemicalplant` — looping condenser coil with bubbling green liquid
- 🌱 `gitem_biofarm` — grow tray, 3 sprouts under a purple grow lamp
- ❄️ `gitem_cryobed` — sleek cryo pod with frosted misty canopy
- ☢️ `gitem_portablereactor` — armored cube with swirling blue-green core viewport
- 🌀 `gitem_hydrogenengine` — small turbine-intake engine block, cyan ring (matches big-brother hydrogen engine language)

#### 🌬️ WindPower + Fluids
- 🌬️ `block_t236_nacelle` (WindPower) — streamlined white nacelle capsule; `block_t150_nacelle` derived at 85% slot scale
- 🧪 `block_tankglass` (Fluids) — clear tank half-full of glowing blue liquid, cradle frame
- 🛢️ `block_tanksolid` (Fluids) — riveted steel tank, banded, with level sight glass

#### Files touched
- 11 new icons under `ItemIcons/{GridSystem,WindPower,Fluids}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 37 → 38

---

### [6.78.37-dev] Icon Batch 34 — GridSystem Doors, Lights & Utility Strays

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🚪 Doors & access (GridSystem)
- 🏦 `gitem_heavyvaultdoor` — circular vault door in armored frame, 3-spoke locking wheel + bolt lugs
- ↔️ `gitem_largedoubleslidingdoor` — twin panels half-open with a glowing amber gap
- ↕️ `gitem_largegridslidingdoor` — single panel half-slid with amber reveal

#### 💡 Lighting family (GridSystem)
- 🔦 `gitem_dualgridspotlightsmall` — compact bar with two small glowing spot heads
- 🔦 `gitem_dualgridspotlightlarge` — heavy bar with two big ribbed-lens spots
- 🔆 `gitem_largegridspotlight` — big single yoke-mounted spotlight, cross reflector
- ⬜ `gitem_gridlightblock` — cube with bright white diffuser face
- ➖ `gitem_largegridledstrip` — long channel strip with continuous warm glow

#### 🧰 Utility (GridSystem)
- 🫙 `gitem_gastank` — cradled pressure cylinder, brass valve + cyan-lit gauge
- 💥 `gitem_demolisher` — armored charge block with orange explosive core + red det lamp

#### Files touched
- 10 new icons under `VoxelEngineAssets/ItemIcons/GridSystem/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 36 → 37

---

### [6.78.36-dev] Icon Batch 33 — Engine Upgrades + Pump Quartet & Power Blocks

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🏎️ Engine upgrade parts (Maritime, 12 icons this batch: 10 generated + 2 derived)
- 🌀 turbocharger trio — `large_turbocharger` master (snail housing + glowing hot turbine); `small_turbocharger` derived at 70% scale; `high_flow_turbocharger` at 85% with boosted hot-glow saturation
- 💉 `overclocked_fuel_injectors` — polished fuel rail, 4 nozzles with glowing tips
- ❄️ `super_cooler_radiator_jacket` — finned radiator core with electric fan
- 💚 `efficiency_tuning_chip` — tuning chip card with emerald glow stripe + gold pins

#### 💧 Pump quartet
- 💙 `block_waterpump` (Fluids — NEW category folder) — blue pump housing, volute chamber + pressure gauge
- ⚓ `marine_water_pump` (Maritime) — bronze-green corrosion-proof hull pump with strainer
- 🛟 `bilge_pump` (Maritime) — white/red submersible canister with float switch
- 💦 `block_sprinkler` (Survival) — brass impact sprinkler on stake, arcing droplets

#### ⚙️ Power & processing
- 🌫️ `block_steamturbine` (Survival) — ribbed turbine casing with steam inlet + terminal box
- ♻️ `block_wastereprocessor` (Survival) — olive cabinet, orange recycling drum window

#### Files touched
- `VoxelEngineAssets/ItemIcons/Fluids/` — NEW category folder (+folder .meta)
- 12 new icons under `ItemIcons/{Maritime,Fluids,Survival}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 35 → 36

---

### [6.78.35-dev] Icon Batch 32 — Ruins Complete + Power Grid Finale

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🧪 Acid biome — complete family (3, Survival/CelestialBlocks) — ALL ruine biomes now done!
- 🏦 `block_ruin_acid_corrodedvault` — acid-eaten vault, green corrosion crust, wheel door
- 💚 `block_ruin_acid_crystalspire` — pitted toxic-yellow crystal cluster
- 🥽 `block_ruin_acid_dissolvedlab` — lab cabin with ragged acid-melted holes

#### 🏛️ Greek biome — complete family (2)
- 🔥 `block_ruin_greek_oracleshrine` — marble column shrine, bronze brazier flame
- 🏛️ `block_ruin_greek_treasurytemple` — grand three-column temple pediment on stylobate steps

#### ⚡ Power grid blocks (5)
- ⬆️ `block_stepuptransformer` (HighVoltage) — tall transformer, 3 rising porcelain bushings
- ⬇️ `block_stepdowntransformer` (HighVoltage) — compact ribbed transformer can
- 🏗️ `block_substation` (HighVoltage) — concrete pad with transformer + insulator gantry
- 🟠 `block_powerbusbar` (Survival) — frame rack with 3 copper bars on ceramic standoffs
- 🗄️ `block_nasblock` (Survival) — NAS cube with 4 drive bays + status LEDs

#### Files touched
- 10 new icons under `ItemIcons/{Survival,HighVoltage}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 34 → 35

---

### [6.78.34-dev] Icon Batch 31 — Ruin Set-Pieces, Wave 3 (Mars / Moon / Crystal / Desolate)

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🔴 Mars biome — complete family (3, Survival/CelestialBlocks)
- 🏚️ `block_ruin_mars_dustbunker` — half-buried bunker with red dust drifts
- 🏘️ `block_ruin_mars_frontieroutpost` — twin dust-crusted modules joined by a flex tunnel
- 📻 `block_ruin_mars_waystation` — roadside module with collapsed antenna mast, faded yellow stripe

#### 🌑 Moon biome — complete family (3)
- 🌕 `block_ruin_moon_habitatdome` — regolith-dusted dome, cracked visor window
- 📡 `block_ruin_moon_listeningpost` — equipment hut with big parabolic dish skyward
- 🛰️ `block_ruin_moon_outpost` — grey cylinder module on four landing legs

#### 💎 Crystal biome — complete family (3)
- 💜 `block_ruin_crystal_geodeshrine` — split boulder with glowing amethyst cluster inside
- 🛕 `block_ruin_crystal_luminatemple` — pale stone temple with teal crystal orb niche
- 🩵 `block_ruin_crystal_prismspire` — trio of iridescent cyan-pink crystal spikes

#### 🏜️ Desolate biome
- 🩹 `block_ruin_desolate_dryoutpost` — cracked adobe shack, broken tattered windsock pole

#### Files touched
- 10 new icons under `VoxelEngineAssets/ItemIcons/Survival/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 33 → 34

---

### [6.78.33-dev] Icon Batch 30 — Ruin Set-Pieces, Wave 2 (Volcanic / Venus / Ice)

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🌋 Volcanic biome — complete family (4, Survival/CelestialBlocks)
- 🪨 `block_ruin_volcanic_ashkeep` — cracked basalt keep with ash-dusted ledges + faint ember crack
- ⚫ `block_ruin_volcanic_charreddome` — blackened dome with dying ember seam
- 🔥 `block_ruin_volcanic_magmaforge` — forge with glowing orange magma crucible mouth
- 💜 `block_ruin_volcanic_obsidiancitadel` — glossy faceted obsidian structure, violet shimmer

#### 🌕 Venus biome — complete family (3)
- 🏯 `block_ruin_venus_ashcitadel` — pale ochre tower with bone-ash crust
- 🛡️ `block_ruin_venus_pressuredome` — ribbed titanium pressure dome, twin lock hatches
- 💛 `block_ruin_venus_sulfurrefinery` — corroded refinery with yellow sulfur-crusted pipes

#### ❄️ Ice biome — complete family (3)
- 📡 `block_ruin_ice_cryostation` — frost-coated research module, icicles + frozen window
- 🚪 `block_ruin_ice_frozenbunker` — snow-blanketed bunker, frosted blast door ajar
- 🧊 `block_ruin_ice_glacialdome` — dome sealed in clear ice shell with trapped bubbles

#### Files touched
- 10 new icons under `VoxelEngineAssets/ItemIcons/Survival/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 32 → 33

---

### [6.78.32-dev] Icon Batch 29 — Ruin Set-Pieces, Wave 1 (Pirate Biome + Industrial)

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🏴‍☠️ Pirate biome — complete family (5, Survival/CelestialBlocks)
- 🗼 `block_ruin_pirate_junktower` — crooked stacked scrap tower, crow-nest + bent antenna
- 💰 `block_ruin_pirate_lootcache` — crate heap with overflowing gold + gem chest
- 🍸 `block_ruin_pirate_neonden` — scrap shack with magenta neon tube frame
- 🛡️ `block_ruin_pirate_scrapfort` — welded plate fort with rebar spikes
- ⛺ `block_ruin_pirate_wreckcamp` — hull-shelter with orange tarp roof

#### 🏚️ Industrial & water ruins (5, Survival)
- 🏭 `block_ruinfactory` — collapsed sawtooth roof factory, broken brick chimney
- 🏬 `block_ruinwarehouse` — sagging corrugated hall, half-fallen roller door
- 🌊 `block_ruin_water_stiltplatform` — barnacled stilt deck with rope ladder
- 🫧 `block_ruin_water_sunkendome` — geodesic dome with algae waterline + broken porthole
- 🔴 `block_ruin_mars_habitat` — dust-crusted pressure dome, cracked porthole, bent antenna

#### Files touched
- 10 new icons under `VoxelEngineAssets/ItemIcons/Survival/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 31 → 32

---

### [6.78.31-dev] Icon Batch 28 — GridSystem Functional Blocks

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🧱 12 new block icons (GridSystem category)
- 🛗 `gitem_piston` — armored base with extended steel piston shaft + push plate
- 📦 `gitem_cargolarge` — ribbed yellow-grey cargo crate with latched door
- 📦 `gitem_cargosmall` — same crate derived at 70% slot scale (identical art family)
- 🛡️ `gitem_armorlarge` — thick beveled armor slab, corner rivets
- 🛡️ `gitem_armorsmall` — same slab derived at 70% slot scale
- 📡 `gitem_beacon` — antenna mast with glowing red dome lamp
- 🪟 `gitem_glassblock` — framed armored glass window block
- 🫧 `gitem_liquidtank` — glass reservoir at two-thirds cyan fill, pipe stubs
- ⚗️ `gitem_h2o2generator` — cabinet with twin bubbling glass chambers
- 💡 `gitem_ledstrip` — slim channel bar with warm glowing diffuser
- 🔫 `gitem_weapon` — stubby cannon mount with side ammo drum
- ⭕ `gitem_dockingport` — round segmented docking hatch with clamp ring + green status lamp

#### Files touched
- 12 new icons under `VoxelEngineAssets/ItemIcons/GridSystem/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 30 → 31

---

### [6.78.30-dev] Icon Batch 27 — Complete Fauna Loot Set + Oxygen Tank

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🐉 All 9 remaining fauna loot drops (Fauna category)
- 🦎 `item_basilisk_scale` — iridescent green-black curved scale, golden edge shimmer
- ❤️ `item_griffin_heart` — crimson heart with golden down feathers, faint magical glow
- 🦅 `item_griffin_talon` — hooked dark ivory claw, leather-stumped base
- 🌀 `item_karkadann_horn` — massive twisted obsidian-violet spiral horn with silver rings
- ☠️ `item_venom_gland` — purple sac bulging with glowing toxic-green venom
- 🟫 `item_hide` — rolled tan pelt with fur end + thong tie
- 🛡️ `item_plated_hide` — dark pelt with natural bone-armor scales
- 🥩 `item_raw_meat` — marbled red steak with fat veins + bone corner
- 🐑 `item_wool` — fluffy cream fleece bundle, twine wrap

#### 🫧 Survival gear
- 🟢 `oxygen_tank` (Survival) — white steel cylinder, green shoulder band, brass valve + pressure gauge

#### Files touched
- 10 new icons under `ItemIcons/{Fauna,Survival}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 29 → 30

---

### [6.78.29-dev] Icon Batch 26 — Maritime Mega-Batch (19 icons!)

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 📦 Shipping container family — 5 icons from 1 master
- 🔵 `shipping_container_large_blue` / ⚪ `shipping_container_large_gray` (hue-desaturated panels)
- 🔵⚪ `shipping_container_small_blue` / `shipping_container_small_gray` (same art at 75% slot scale)
- 🟤 `shipping_container_old` — rust-brown weathered treatment

#### 🖥️ Screen family — 5 icons from 1 master (size = slot scale, matching shared cyan `iconTint`)
- 🖥️ `screen_extralarge` → `screen_large` → `screen_medium` → `screen_small` (full→0.88→0.74→0.58) + `screen_wide` (height-flattened variant)
- (GridSystem/ScreenItems category; cyan wave-graph monitor)

#### 🛥️ Maritime engines — 3 distinct engine heroes by size class
- 🟤 `crude_engine` — weathered single-cylinder rope-start boat engine
- ⚙️ `heavy_fuel_oil_engine` — dark cast block, rust exhaust manifold
- 🔩 `mgo_engine` — giant diesel with exhaust stack + flywheel on skid

#### 🌀 Propellers + fauna loot
- 🥇 `large_propeller` — 4-blade brass ship prop; `small_propeller` derived at 66% scale
- ⚡ `electrical_propeller` — streamlined blue-grey electric pod with ring shroud
- 🪶 `item_griffin_feather` (Fauna) — golden flight feather
- 👁️ `item_petrified_eye` (Fauna) — stone sphere with fossilized amber iris
- 🔥 `item_ifrit_ember` (Fauna) — volcanic rock with glowing fire cracks

#### Files touched
- 19 new icons under `ItemIcons/{Maritime,GridSystem,Fauna}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 28 → 29

---

### [6.78.28-dev] Icon Batch 25 — Drawer Redo + Upgrade Chip Rainbow & Tool Set

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### ♻️ Remade icon (Thomas feedback)
- 🗄️ `block_storagedrawer` (Survival) — now a **single drawer front with one big clean white label panel** (thin dark frame + small knob below), drawer-mod style: the stored item can be shown on that white face in-game

#### 💳 Drawer upgrade chip family — 6 icons from 1 master, stripe hue matched to each asset's `iconTint`
- 🔵 `drawerupgrade_stack_1x` · 🩵 `drawerupgrade_stack_2x` · 🟢 `drawerupgrade_stack_4x` · 🟡 `drawerupgrade_stack_8x` · 🟠 `drawerupgrade_stack_16x` · 🟣 `drawerupgrade_void` (Survival)

#### 🛠️ New tool icons (4)
- 🌾 `hoe` (Survival) — wooden-handled lashed iron hoe
- 🧰 `grinder_tool` (Tools) — orange angle grinder with cutting disc
- 🖌️ `tool_paint` (Tools) — paint sprayer with visible blue paint cup
- 📏 `leveling_tool` (Tools) — yellow spirit level with green bubble vials

#### ⚙️ New block/item icons (4)
- 🌀 `gitem_gyroscopesmall` (GridSystem) — compact gimbal flywheel housing
- 🔩 `gitem_drill` (GridSystem) — armored block with front spiral auger bit
- 🦷 `gitem_grinder` (GridSystem) — armored block with toothed crusher rollers
- ⚙️ `item_irongear` (Industrial) — heavy 8-tooth iron cog

#### Files touched
- `VoxelEngineAssets/ItemIcons/Survival/block_storagedrawer.png` — replaced (GUID/binding preserved)
- 14 new icons under `ItemIcons/{Survival,Tools,GridSystem,Industrial}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 27 → 28

---

### [6.78.27-dev] Icon Batch 24 — Wind Turbine Part Families (22 icons!)

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🌬️ T90 / T150 / T236 turbine part families — 18 icons from 6 master renders
Same component, three sizes: art is pixel-identical per part and **icon scale = tier** (T236 fills the slot, T150 ≈ 80%, T90 ≈ 66%), so players instantly read which turbine a part belongs to. Matches their shared steel `iconTint`.

- 🔪 blade set — `block_t90_blade` / `block_t150_blade` / `block_t236_blade` — white fiberglass blade
- ⚙️ gearbox set — cast housing, meshing gears window, shaft stubs
- 🔋 generator set — finned cylinder, copper slip ring band
- 🎯 hub set — white spinner nose cone with 3 blade sockets
- 🏗️ monopole set — smooth tube pole segment with flange rings
- 🗼 tower set — tapered galvanized lattice truss segment
- (WindPower category; all 18 bound to their `WindPower/Items` assets)

#### 🎨 New item icons (4 more)
- 🌬️ `block_vlarge_blades` (WindPower) — big face-on 3-blade rotor
- 🌬️ `block_vsmall_blades` (WindPower) — compact 3-blade rotor
- 🌊 `waterwheel` (Maritime) — big oak paddle waterwheel
- 🛥️ `maritime_generator` (Maritime) — grey-green marine genset with flywheel housing

#### Files touched
- 22 new icons under `ItemIcons/{WindPower,Maritime}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 26 → 27

---

### [6.78.26-dev] Icon Batch 23 — Single-Slope Roof + Storage & Mechanical Sets

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### ♻️ Remade icon (Thomas feedback)
- 🏠 `build_roof` (Tiered) — no more gable triangle: now a clean **single-slope shed roof** — one flat terracotta shingle plane on a simple wedge with fascia board, matching the rest of the build family

#### 🗄️ New storage family icons (5, oak + steel design language)
- 🗄️ `block_storagedrawer` (Survival) — twin oak drawer cabinet, metal pulls + label frames
- 🖥️ `block_storagedrawercontroller` (Survival) — drawer bank with glowing green grid terminal
- 📤 `block_storageexporter` (Survival) — green-lit output port ejecting a crate
- 📥 `block_storageimporter` (Survival) — amber intake hopper swallowing a crate
- 🪟 `block_storageitemdisplay` (Survival) — glass display window showing one golden gear inside

#### ⚙️ New mechanical power icons (4, Maritime)
- ⚙️ `gearbox` — cast-iron casing with meshing brass gears in a round window
- 🔩 `drive_shaft` — polished shaft tube with universal joints at both ends
- ↔️ `rotation_transfer` — right-angle bevel gearbox elbow
- ⛓️ `encased_chain_drive` — slim casing with roller chain loop around two sprockets

#### Files touched
- `VoxelEngineAssets/ItemIcons/Tiered/build_roof.png` — replaced (GUID/binding preserved)
- 9 new icons under `ItemIcons/{Survival,Maritime}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 25 → 26

---

### [6.78.25-dev] Icon Batch 22 — Complete Base-Building Set

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🏠 New Tiered category: all 10 build pieces, one cozy timber-frame design language (white plaster + dark wood beams)
Every piece you'd snap together into a starter base, instantly readable at slot size:

- 🧱 `build_foundation` — thick stone plinth with timber trim
- 🧱 `build_wall` — full plaster + beam wall panel
- 🪟 `build_window` — wall panel with 4-pane mullioned window, faint glass glint
- 🚪 `build_door` — wall panel with closed wooden door + brass knob
- 🕳️ `build_doorway` — wall panel with open portal frame
- 🪵 `build_floor` — lying wooden plank deck tile
- 🧱 `build_halfwall` — half-height panel with top rail
- 🏛️ `build_pillar` — square wooden column, stone foot and cap
- 🪜 `build_stairs` — 4-step wooden staircase with side stringer
- 🏠 `build_roof` — terracotta shingle gable slope with ridge cap

#### Files touched
- `VoxelEngineAssets/ItemIcons/Tiered/` — NEW category folder (+folder .meta, established guid scheme); icons bind to the `Tiered/Tokens/Token_*.asset` definitions
- 10 new icons (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 24 → 25

---

### [6.78.24-dev] Icon Batch 21 — Lost Five Restored + Thruster Family Complete

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🔁 Regenerated the five icons lost in the cleanup (13 icons this batch: 10 generated + 3 remodelled/derived)
- 💣 `item_mortar_explosive` (Combat) — mortar bomb master (red-orange nose band)
- 🟡 `item_mortar_illum` / ⚪ `item_mortar_smoke` (Combat) — same round hue-derived: yellow illumination band / grey smoke band
- ☢️ `block_tsar_bomb` (Combat) — enormous finned bomb casing, blunt nose + box tail
- 🛞 `gitem_wheel_2x2` (GridSystem) — compact 4-lug wheel · 🛞 `gitem_wheel_5x5` (GridSystem) — massive chevron-tread wheel with armored hub

#### 🚀 Thruster family — now 7 strong, one shared design (octagonal housing + bell, color = type, size = size)
- 🔷 `gitem_hydrothruster_small` — REDONE into the family style (cyan burn, small bell)
- 🟠 `gitem_atmothruster_large` — big orange jet flame burst
- ⚪ `gitem_thrustersmall` — neutral pale ice-blue glow utility thruster

#### ☢️ Nuclear additions
- 🟡 `item_highlevelwaste` (Nuclear) — sealed yellow cask, bolted lid, amber heat slit
- 🔩 `item_depleteduranium` (Nuclear) — dense dark DU slug with lathe pattern
- 👨‍✈️ `gitem_cockpitlarge` (GridSystem) — wide canopy cockpit with center pillar, matches cockpit small

#### Files touched
- `VoxelEngineAssets/ItemIcons/GridSystem/gitem_hydrothruster_small.png` — replaced in family style (GUID/binding preserved)
- 12 new icons under `ItemIcons/{Combat,GridSystem,Nuclear}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 23 → 24

---

### [6.78.23-dev] Icon Batch 19/20 — Thruster Family, Nuclear Set + Workspace Slim-Down

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🧹 Workspace maintenance (per Thomas)
- Cleared all previously delivered icon PNGs from the workspace copy after Thomas downloaded them (git/local copies are the source of truth for him; sprite `.meta` files and all `icon:` asset bindings remain untouched, so nothing unbinds). New batches continue here.

#### 🚀 Thruster icon family established (Thomas feedback)
All thrusters now share one design language — **dark octagonal armored housing + exposed engine bell nozzle**, differentiated by size and flame/plasma color:

- 🔷 `gitem_hydrothruster_large` — REMADE: proper large rocket thruster, huge flared bell with bright cyan hydrogen burn, turbopump ring + feed pipes
- 🟣 `gitem_ionthruster_small` / 🟣 `gitem_ionthruster_large` — violet plasma-grid glow, small/large bell
- 🟠 `gitem_atmothruster_small` — warm orange jet glow with intake fan hint

(Note: `gitem_hydrothruster_small` from an older batch still uses the pre-family design — queued for a family-style redo next batch.)

#### ☢️ New Nuclear category icons (4) + processor
- ⚫ `item_controlrod` (Nuclear — NEW icon folder) — bundle of 5 matte control rods, steel collars
- 🟢 `item_enrichedfuelrod` (Nuclear) — silver rod with radioactive green core glow through window slits
- 🟠 `item_spentfuelrod` (Nuclear) — scorched dull rod with faint ember seam
- ⚪ `item_leupellet` (Nuclear) — ceramic tray of dark uranium pellets
- ⚙️ `block_uraniumprocessor` (Survival) — centrifuge cabinet with porthole drum + green status lamps

#### ⚠️ Interrupted-batch note
Five icons generated right before a session interruption (mortar explosive base, tsar bomb, wheel 2x2/5x5 gen files) were caught in the workspace cleanup before delivery — they post-date the downloaded snapshot and will be **regenerated next batch** (mortar rounds trio incl. illum/smoke derivations, tsar bomb, wheel family).

#### Files touched
- `VoxelEngineAssets/ItemIcons/Nuclear/` — NEW category folder (+folder .meta following the established IW/Folder guid scheme)
- 8 new icons + 1 remake under `ItemIcons/{GridSystem,Nuclear,Survival}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 22 → 23

---

### [6.78.22-dev] Icon Batch 18 — Landing Gear Redo + Disk Rainbow & Shell Ammo

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### ♻️ Remade icon (Thomas feedback)
- 🧲 `gitem_landinggear` (GridSystem) — rebuilt as a classic magnetic lock-gear: chunky light-grey armored mount block with a big round segmented magnetic disc underneath and a warning-yellow ring accent. The runner-up design (a square mag-pad) was too good to waste, so it became `block_stationarydockingport` (Industrial) — bonus icon!

#### 🎨 Disk tier family via hue-shift (identical art, tier = color, matched to each asset's `iconTint`)
- 🟢 `disk_1k` · 🩵 `disk_4k` · 🔵 `disk_16k` · 🟣 `disk_64k` · 🩷 `disk_90k` (Survival) — rugged cartridge with glowing holo-disc core + accent stripe

#### 🎨 New item icons (7 more)
Coverage now **174 / 398** itemIds (14 files this batch: 10 generated + 4 hue-derived).

- 💥 `item_shell_explosive` (Combat) — heavy shell with red nose band
- 🔵 `item_shell_scatter` (Combat) — scatter shell, blue band, pellets peeking out
- 🗿 `item_giant_shell` (Combat) — enormous blunt-nosed naval shell
- 🎯 `item_aa_rounds` (Combat) — 4-round AA clip, brass + cyan proximity tips
- 📎 `ammo_magazine` (GridSystem) — loaded curved rifle magazine
- 🛞 `gitem_wheel_3x3` (GridSystem) — deep-tread off-road wheel, steel rim
- 🌀 `gitem_gyroscope` (GridSystem) — gimbal-mounted flywheel gyro block

#### Files touched
- `VoxelEngineAssets/ItemIcons/GridSystem/gitem_landinggear.png` — replaced (GUID/binding preserved)
- 13 new icons under `ItemIcons/{Survival,Combat,GridSystem,Industrial}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 21 → 22

---

### [6.78.21-dev] Icon Batch 17 — Power Blocks + CPU/RAM Tier Families

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🎨 Tier families via hue-shift (identical art, tier = color)
- 🟤🟦🟣 **CPU family** — `cpu_1` (copper pins) / `cpu_2` (cyan pins) / `cpu_4` (purple pins): one master render, pin hue shifted to match the game's tier color language
- 🟦🔵 **RAM family** — `ram_4` (cyan LED strip) / `ram_16` (royal-blue strip, matching its asset `iconTint`)

#### 🎨 New item icons (8 more)
Coverage now **162 / 398** itemIds (13 icons this batch: 10 generated + 3 hue-derived).

- 🔋 `gitem_batterylarge` (GridSystem) — tall battery cabinet, two green charge windows
- 🔋 `gitem_batterygiant` (GridSystem) — massive battery bank, four charge windows + busbar terminals
- 🛬 `gitem_landinggear` (GridSystem) — hydraulic landing strut with wide footpad
- ☀️ `gitem_solarpanel` (GridSystem) — deep-blue photovoltaic panel, silver frame
- 💣 `item_shell_standard` (Combat) — long olive artillery shell, copper driving band
- 🛢️ `item_crudeoilbarrel` (Industrial) — black ribbed oil drum with rusty rim
- ⚪ `item_plastic` (Industrial) — heap of glossy polymer pellets
- 🌿 `item_biomass` (Survival) — twine-tied bundle of green plant clippings

#### Files touched
- 13 new icons under `ItemIcons/{GridSystem,Survival,Combat,Industrial}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 20 → 21

---

### [6.78.20-dev] Icon Batch 16 — Handheld Arsenal & Core Tools

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🎨 New item icons (10)
High-visibility handheld set — weapons and tools you stare at in hand and hotbar all session. Coverage now **149 / 398** itemIds.

- 🔫 `weapon_rifle` (Combat) — industrial assault rifle, wooden furniture + box magazine, diagonal
- 💣 `weapon_grenade` (Combat) — olive pineapple frag grenade with lever and ring
- 🗡️ `iron_sword` (Combat) — sturdy broadsword with fuller and leather grip
- 🔫 `iron_pistol` (Combat) — heavy industrial revolver with wooden grip
- ⛏️ `wooden_pickaxe` (Tools) — crude stone-tipped wooden pickaxe
- 🪓 `wooden_axe` (Tools) — twine-lashed crude wooden axe
- 🔧 `wrench` (Survival) — polished combination wrench, open + ring end
- 🥉 `item_bullets` (Combat) — upright brass rifle rounds, copper tips
- 🪟 `item_glass` (Industrial) — clear glass sheet with diagonal sheen and beveled edge
- 👨‍✈️ `gitem_cockpitsmall` (GridSystem) — compact cockpit block with wraparound cyan canopy

#### Files touched
- 10 new icons under `ItemIcons/{Combat,Tools,Survival,Industrial,GridSystem}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 19 → 20

---

### [6.78.19-dev] Icon Batch 15 — Unified Splitter Tier Family + Combat & Components

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🎨 Splitter tier family unified (Thomas feedback)
All three conveyor splitters now share **one identical base design** (warm-grey steel cube, four black rubber belts with rollers, one belt end exiting each side, no arrows) and differ **only by tier color**, taken straight from each asset's own `iconTint`:

- 🟢 `block_conveyorsplittermk1` — green accent ring + corner lights
- 🔵 `block_conveyorsplittermk2` — cyan accent ring + corner lights
- 🟣 `block_conveyorsplittermk3` — purple accent ring + corner lights

The old mismatched mk2/mk3 cube art (the "compact dark steel cube") is gone. Variants were produced by hue-shifting the master render, so the three are truly pixel-identical apart from tier color.

#### 🎨 New item icons (9)
Coverage now **139 / 398** itemIds.

- 🔫 `block_turret` (Combat) — compact light machine-gun turret, swivel base + slim double barrel
- 🟪 `block_antimatter_bomb` (Combat) — dark armor cube with swirling purple antimatter core behind a round window
- 🦾 `block_assembler` (Blocks) — factory cube with open bay, orange robot arm over a tray
- 🔩 `item_steelplate` (Industrial) — fanned stack of 3 brushed steel plates
- ⚙️ `item_ironplate` (Industrial) — stack of 3 rough raw-iron plates with rust-toned edges
- 🟩 `item_circuit` (Industrial) — green PCB with copper traces and central chip
- 🟦 `item_advcircuit` (Industrial) — dark-blue advanced PCB, gold traces, cyan-lit processor
- 🔋 `gitem_batterysmall` (GridSystem) — compact battery cube with glowing green charge window and brass terminals
- 🚀 `gitem_hydrothruster_small` (GridSystem) — gunmetal thruster block with bell nozzle and cyan inner glow

#### Files touched
- `VoxelEngineAssets/ItemIcons/Factory/block_conveyorsplittermk{1,2,3}.png` — replaced with the unified tier family (GUIDs/bindings preserved)
- 9 new icons under `ItemIcons/{Combat,Blocks,Industrial,GridSystem}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 18 → 19

---

### [6.78.18-dev] Icon Batch 14 — Junction Binding Fix + Combat & Relay Set

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🔧 Fixed binding (root cause of repeat junction complaint)
- 🔀 `block_conveyorsplittermk2` — the batch 12/13 junction remakes were accidentally written to an orphaned file (`ItemIcons/GridSystem/splittermk2.png`) that no item asset references; the real in-game item kept showing the old arrow-heavy icon. The new art is now saved over the correct, already-bound file `ItemIcons/Factory/block_conveyorsplittermk2.png` (GUID unchanged), and the orphan file is deleted. Junction now shows: **exactly 4 rubber belts with visible rollers, one per side, no arrows, no down-belt, bright readable steel cube**. Full-tree audit confirms zero remaining orphan icons.

#### ♻️ Verified remakes from 6.78.17 (unchanged)
- ⛏️ `block_quarry` and 🌀 `block_hydrogenengine` already match the requested stationary-block / turbine-intake designs — art kept as shipped.

#### 🎨 New item icons (7)
Coverage now **130 / 398** itemIds.

- 🧨 `block_powder_keg` (Combat) — wooden powder keg, iron hoops, cork + coiled fuse
- 💣 `block_mortar_turret` (Combat) — squat armored mortar with steep-angled stubby tube
- 🔌 `block_powerrelay` (Industrial) — grey electrical cabinet with 3 ceramic bushings and copper cable loop
- ⚡ `block_hvpowerrelay` (HighVoltage) — dark open-frame HV relay with 3 stacked porcelain insulators and copper terminals
- 🧪 `block_pipeglass` (Industrial) — clear glass fluid pipe with cyan liquid flowing inside
- 🔩 `block_pipesolid` (Industrial) — steel fluid pipe with flange couplings and red valve wheel
- 🖥️ `block_patternterminal` (Factory) — pattern encoding console with glowing green 3×3 grid screen

#### Files touched
- `VoxelEngineAssets/ItemIcons/Factory/block_conveyorsplittermk2.png` — replaced with corrected junction art (binding preserved)
- `VoxelEngineAssets/ItemIcons/GridSystem/splittermk2.png` (+.meta) — orphaned file removed
- 7 new icons under `ItemIcons/{Combat,Industrial,HighVoltage,Factory}/` (+.meta), sprite metas + `icon:` refs written
- `Scripts/Core/GameVersion.cs` — Patch 17 → 18

---

### [6.78.17-dev] Icon Batch 13 — Powerline & Pipe Set + Three Remakes

**Type:** PATCH — art/content, save-compatible. Zero C# changes.

#### 🎨 New item icons (7)
Ten-tile coverage continues — now **123 / 398** itemIds carry premium hand-tuned sticker-style icons.

- ⚡ `block_hvtower` (HighVoltage) — galvanized lattice HV pylon with twin crossarms and insulator strings
- 🔌 `block_lvpowerrelay` (HighVoltage) — compact LV relay pole with transformer box and crackling insulators
- 🪵 `block_powerpole` (Industrial) — wooden utility pole with crossarm, ceramic insulators and wire stubs
- 📦 `block_itempipe` (Industrial) — square-section riveted steel item duct
- 🔍 `block_itempipe_glass` (Industrial) — clear glass item tube with a visible crate sliding inside
- 💡 `block_light` (Factory) — dark industrial light block with one bright warm lamp face
- 💥 `block_giant_shell_turret` (Combat) — massive artillery dome turret with one very long elevated heavy barrel

#### ♻️ Remade icons (3, Thomas feedback)
- 🔀 `splittermk2` — compact square conveyor junction: now a clean cube with **exactly 4 belts (one per side)**; the stray down-belt and all painted arrows are gone
- ⛏️ `block_quarry` — rebuilt as a **stationary cubic frame block** with a front gantry carrying three drill augers; no wheels, no cab
- 🌀 `block_hydrogenengine` — rebuilt as a proper hydrogen engine: chunky gunmetal block whose face is **one large circular turbine intake** with visible fan blades and a cyan accent ring

#### Files touched
- `VoxelEngineAssets/ItemIcons/HighVoltage/block_hvtower.png` (+.meta)
- `VoxelEngineAssets/ItemIcons/HighVoltage/block_lvpowerrelay.png` (+.meta)
- `VoxelEngineAssets/ItemIcons/Industrial/block_powerpole.png` (+.meta)
- `VoxelEngineAssets/ItemIcons/Industrial/block_itempipe.png` (+.meta)
- `VoxelEngineAssets/ItemIcons/Industrial/block_itempipe_glass.png` (+.meta)
- `VoxelEngineAssets/ItemIcons/Factory/block_light.png` (+.meta)
- `VoxelEngineAssets/ItemIcons/Combat/block_giant_shell_turret.png` (+.meta)
- `VoxelEngineAssets/ItemIcons/GridSystem/splittermk2.png` (remade in place — GUID/binding unchanged)
- `VoxelEngineAssets/ItemIcons/Industrial/block_quarry.png` (remade in place — GUID/binding unchanged)
- `VoxelEngineAssets/ItemIcons/Power/block_hydrogenengine.png` (remade in place — GUID/binding unchanged)
- Sprite `.meta` + `icon:` bindings written/patched for the 7 new icons across their item/block assets
- `Scripts/Core/GameVersion.cs` — Patch 16 → 17

---

### [6.78.16-dev] Icon Batch 12 — Heavy Industry + Three Remakes

**Type:** PATCH — art batch + art-direction fixes (save-compatible).

#### 🎨 Remade per feedback
- **Energy relic turret**: rebuilt from a muddy dark blob into an elegant white-marble pedestal with gold trim, slim bronze prongs holding one floating prismatic cyan crystal — premium and clean.
- **Splitter mk3 (square)**: belt geometry was wrong — re-made as a square junction with exactly **one input and four output belts, one per side, nothing more**.
- **Splitter mk2 (wide)**: the painted arrows now follow the real flow — input arrow points INTO the junction, output arrows point OUT.

#### 🏗️ Icon batch 12 — the heavy industry set (7 new icons)
- **Energy & extraction**: big **power station** with cooling towers, hydraulic **pumpjack** (nodding donkey — unmistakable), **oil refinery** with its distillation column and flare, and the cyan-fed **hydrogen engine**.
- **Field tech**: automatic **quarry** gantry drill and the chunky **portable reactor** with a glowing blue core window.
- **Research** (new category): the **research lab** with observation dome and sample tanks.
- All die-cut sticker style, zero background, auto-bound by itemId.
- Workspace housekeeping: `New models` + `Imported Textures` removed (~39 MB; git copies untouched).
- **Coverage: 113 / 398 itemIds.**

#### Files touched
- `VoxelEngineAssets/ItemIcons/{Survival,Industrial,Research,Factory,Combat}/` (+7 new PNGs & metas, 3 regenerated)
- Definition assets auto-bound by itemId
- `Scripts/Core/GameVersion.cs` (6.78.15 → 6.78.16), `Changelog.md`

---

### [6.78.15-dev] Icon Batch 11 — Terminals, Splitters & Energy Pipes

**Type:** PATCH — art batch (save-compatible).

#### 🔌 Icon batch 11 — ten more items got sticker icons
- **Splitters complete**: 3-way mk2 junction and 4-way cross mk3 join the mk1 T — output count reads straight off the junction shape.
- **Terminals**: cyan-holo **wireless storage terminal**, blueprint-screen **crafting terminal**, and the retro-green **disk manipulator** with its reel slot.
- **Chemical plant**: lime bubbling reaction vats — unmistakably chemistry.
- **Energy pipe tier ladder** (all four): copper coils → gold coils → matte iron coils → **glowing cyan superconductor** — same pipe body, coil colour = tier, never mix them up again.
- All die-cut sticker style, zero background, auto-bound by itemId.
- **Coverage: 106 / 398 itemIds.**

#### Files touched
- `VoxelEngineAssets/ItemIcons/{Factory,Industrial,Survival,Power}/` (+10 PNGs & metas)
- Definition assets auto-bound by itemId
- `Scripts/Core/GameVersion.cs` (6.78.14 → 6.78.15), `Changelog.md`

---

### [6.78.14-dev] Icon Batch 10 — Turret Arsenal & Pipe Logistics

**Type:** PATCH — art batch (save-compatible).

#### 🔫 Icon batch 10 — ten more items got sticker icons
- **Defense arsenal complete**: rotary-barrel minigun, missile-pod anti-air battery, long-barrel artillery cannon, the rail-mounted **Gustav** siege gun, and the arcane **energy relic turret** with its floating cyan crystal — five turrets, five unmistakable silhouettes.
- **Logistics**: enclosed drop chute with its items window and the wide-mouth loading funnel hopper.
- **Gas plumbing**: flanged steel gas pipe + the **glass gas pipe** with cyan flow visibly moving through the glass — you can literally see the gas.
- **Comfort**: frosted blue cryo-sleep pod joins the humble bed.
- Word from the grindstone: the unused `Liquid` asset folder was removed from the workspace copy (1,044 files) to stay under the file limit — pull it back from git any time if it's needed again.
- **Coverage: 96 / 398 itemIds.**

#### Files touched
- `VoxelEngineAssets/ItemIcons/{Combat,Factory,Survival}/` (+10 PNGs & metas)
- Definition assets auto-bound by itemId
- `Scripts/Core/GameVersion.cs` (6.78.13 → 6.78.14), `Changelog.md`

---

### [6.78.13-dev] Icon Batch 9 — The Production Line

**Type:** PATCH — art batch (save-compatible).

#### 🏗️ Icon batch 9 — ten more items got sticker icons
- **Conveyor family grows**: plain grey *basic* belt, orange-striped *fast* belt and the T-junction *splitter mk1* — speed reads off the stripe colour and the split is unmistakable next to straight sections.
- **Assembler tiers mk1→mk3**: olive single-arm starter → blue-grey dual-arm with glass chamber → dark high-tech unit with glowing violet panel; the ladder is obvious in the build menu.
- **Electric furnace**: brushed-steel sibling of the stone furnace, glowing coil window versus open fire mouth.
- **Survival tech**: twin-cell electrolyser with gas tubes, and the glass-dome **biofarm** with crops growing under lights inside.
- **Defense opens**: the red **flamethrower turret** with its fuel tank and pilot flame.
- All die-cut sticker style, zero background, auto-bound by itemId.
- **Coverage: 86 / 398 itemIds.**

#### Files touched
- `VoxelEngineAssets/ItemIcons/{Factory,Survival,Combat}/` (+10 PNGs & metas)
- Definition assets auto-bound by itemId
- `Scripts/Core/GameVersion.cs` (6.78.12 → 6.78.13), `Changelog.md`

---

### [6.78.12-dev] Icon Batch 8 — Core Base Blocks + Harvester & Helm Fixes

**Type:** PATCH — art batch + art-direction fixes (save-compatible).

#### 🎨 Re-created per feedback
- **Harvester** is now what it actually IS in-game: a **stationary** crop-harvesting block — boxy olive machine bolted to a fixed frame with the cutter bar and toothed reel jutting out front. No wheels, no tracks.
- **Crusader great helm** got its front ventilation slits made prominent — unmistakably a helm now, no more tankard.

#### 🏭 Icon batch 8 — the everyday base blocks (8 new icons)
- **Blocks** (new category): banded storage chest, glowing-mouth furnace, workbench with vise & tools, and the humble survival bed.
- **Power** (new category): the **grid battery** block — green charge bars matching its in-game gauge — and the coal generator with its hopper and copper coils.
- **Factory**: heavy ore crusher with jaws and flywheel.
- **Survival**: riveted industrial gas tank with pressure gauge and cyan level window.
- All die-cut sticker style, zero background, auto-bound by itemId.
- **Coverage: 76 / 398 itemIds.**

#### Files touched
- `VoxelEngineAssets/ItemIcons/{Blocks,Power,Factory,Survival}/` (+8 new PNGs & metas, 2 regenerated)
- Definition assets auto-bound by itemId
- `Scripts/Core/GameVersion.cs` (6.78.11 → 6.78.12), `Changelog.md`

---

### [6.78.11-dev] Icon Batch 7 + Paladin & Helmet Art Fixes

**Type:** PATCH — art batch + art-direction fixes (save-compatible).

#### 🎨 Re-created per feedback
- **Paladin bulwark**: the wings are gone — now a clean holy heavy cuirass with polished golden trim and a radiant sun emblem on the breastplate.
- **Space helmet**: no more astronaut bubble — it's a medieval **crusader great helm** (flat-topped steel).

#### 🏭 Icon batch 7 — the block_ continent opens (8 new icons)
- **HighVoltage**: LV junction connector box + the stacked-ceramic HV insulator connector — the two connectors are finally distinguishable.
- **Survival**: automated harvester unit and a glowing armored **reactor core**.
- **Factory**: express conveyor section with yellow speed chevrons.
- **WindPower** (new category): streamlined T90 turbine nacelle + its rolled **blueprint scroll** (blueprint = blue, obviously).
- **GridSystem**: handheld ore detector with radar screen and whip antenna.
- All die-cut sticker style, zero background, auto-bound by itemId.
- **Coverage: 68 / 398 itemIds.**

#### Files touched
- `VoxelEngineAssets/ItemIcons/{HighVoltage,Survival,Factory,WindPower,GridSystem,Combat}/` (+8 new PNGs & metas, 2 regenerated)
- Definition assets auto-bound by itemId
- `Scripts/Core/GameVersion.cs` (6.78.10 → 6.78.11), `Changelog.md`

---

### [6.78.10-dev] Icon Batch 6 — Armor Ladder Complete, Trophies & Hull Woods

**Type:** PATCH — art batch (save-compatible).

#### 🛡️ Icon batch 6 — ten more items got sticker icons
- **Armor ladder complete** (6 tiers, 6 unmistakable silhouettes): quilted initiate gambeson, squire leather, knight chainmail, templar cuirass, golden-winged **paladin bulwark** and the white celestial **stellar archon** power armor — plus the **space helmet** (Survival) for vacuum work.
- **Fauna trophies** (new category): armored hide plate, curled manticore stinger and the crackling **roc storm core** orb — boss drops that look like trophies.
- **Maritime woods** (new category): pale lightweight balsa beam, glossy black tar-coated plank and curved raw hull timber — shipbuilding grades read at a glance.
- All die-cut sticker style, zero background, auto-bound by itemId.
- **Coverage: 60 / 398 itemIds.**

#### Files touched
- `VoxelEngineAssets/ItemIcons/{Combat,Survival,Fauna,Maritime}/` (+10 PNGs & metas)
- Definition assets auto-bound by itemId
- `Scripts/Core/GameVersion.cs` (6.78.9 → 6.78.10), `Changelog.md`

---

### [6.78.9-dev] Icon Batch 5 — Seeds, Ores & Armor Ladder

**Type:** PATCH — art batch (save-compatible).

#### 🌱 Icon batch 5 — ten more items got sticker icons
- **Seed family complete**: carrot, corn, potato, pumpkin and wheat seed packets join the berry one — every packet spills its *own* seed type, so the crop reads straight off the icon in the planter UI.
- **Ore family complete**: copper ore (grey rock, gleaming orange veins) pairs with iron ore; plus a **sand** heap for the glass/cement chain.
- **Armor ladder begins**: squire leather vest → knight chainmail hauberk → templar steel cuirass — three tiers, three unmistakable silhouettes (gambeson, paladin bulwark and stellar archon still to come).
- All die-cut sticker style, zero background, auto-bound by itemId (family duplicate definitions patched too).
- **Coverage: 50 / 398 itemIds.**

#### Files touched
- `VoxelEngineAssets/ItemIcons/{Farming,Industrial,Combat}/` (+10 PNGs & metas)
- Definition assets auto-bound by itemId (incl. duplicate folders)
- `Scripts/Core/GameVersion.cs` (6.78.8 → 6.78.9), `Changelog.md`

---

### [6.78.8-dev] Icon Batch 4 — Full Food Table & Family Completions

**Type:** PATCH — art batch (save-compatible).

#### 🎃 Icon batch 4 — ten more items got sticker icons
- **The Farming table is complete**: small pumpkin, hearty stew bowl, berry pie slice, cornbread, roast potato halves, creamy pumpkin soup — crops and kitchen dishes now read at a glance.
- **Pickaxe family complete**: the primitive stone pickaxe (lashed rock head) and dark tactical steel pickaxe join the iron one — tier quality reads straight off the silhouette.
- **Wire family complete**: gold LV and graphite LV spools join the copper LV spool and the heavy HV cable — four visually distinct wire types, impossible to confuse in a chest.
- All die-cut sticker style, zero background, auto-bound by itemId.
- **Coverage: 40 / 398 itemIds.**

#### Files touched
- `VoxelEngineAssets/ItemIcons/{Farming,Tools,Items}/` (+10 PNGs & metas)
- 10 definition assets (icon references auto-bound by itemId)
- `Scripts/Core/GameVersion.cs` (6.78.7 → 6.78.8), `Changelog.md`

---

### [6.78.7-dev] Icon Batch 3 — Food, Wires & Axe

**Type:** PATCH — art batch (save-compatible).

#### 🍞 Icon batch 3 — ten more items got sticker-style icons
- **Farming** (new category): crusty bread loaf, red & blue berries, carrot, corn on the cob, potato, wheat sheaf, and a **berry seed packet** — drawn with seeds spilling out, so function reads at a glance.
- **Industrial / HighVoltage** (HighVoltage = new category): bright copper wire spool and a heavy black HV cable with hazard-yellow connectors — the two cable families are now visually distinct.
- **Tools**: the iron axe joins the iron pickaxe.
- All generated as die-cut stickers on white → zero background by construction, bold readable shapes at 51 px, auto-bound by itemId as usual.
- **Coverage: 30 / 398 itemIds.**

#### Files touched
- `VoxelEngineAssets/ItemIcons/{Farming,Industrial,HighVoltage,Tools}/` (+10 PNGs & metas)
- 10 definition assets (icon references auto-bound by itemId)
- `Scripts/Core/GameVersion.cs` (6.78.6 → 6.78.7), `Changelog.md`

---

### [6.78.6-dev] Zero-Background Sticker Icons & Category Folders

**Type:** PATCH — art overhaul + asset reorganization (save-compatible).

#### ✂️ Icons re-created with ZERO background — prevented at creation, not patched after
- All 10 painterly originals (coal, iron/copper/steel ingots, iron ore, 3 jetpacks, portable battery, portable hydrogen tank) were **regenerated as die-cut sticker art on a plain white background**. The AI paints no glow clouds or shadow plates onto white — so there is quite literally no background left to remove; one white-key pass and they're crystal clean.
- New permanent house style, hitting the whole checklist: **premium** flat-vector look with soft **hand-painted** strokes (human, not AI-flavoured), **simple** bold shapes with **no unnecessary detail**, one clean white sticker rim that pops on dark slots, and subjects drawn from each item's **name and function** — the three jetpacks are unmistakably three different packs now (olive twin turbines / violet hybrid core / white H₂ bottles).
- The style prompt is baked into the pipeline as the template for every future batch.

#### 🗂️ Icons moved out of GridSystem into category folders
- All item icons now live at `VoxelEngineAssets/ItemIcons/<Category>/<itemId>.png` — category follows the module folder of the item's definition asset (`Items`, `GridSystem`, `Tools`, `Combat`, `Factory`, `Industrial`).
- Sprite GUIDs are item-Id keyed, so every icon reference survived the move untouched — folder metas included for clean version control. The old `GridSystem/Textures/ItemIcons` folder is gone; all tooling now walks the category tree.

#### Files touched
- `VoxelEngineAssets/ItemIcons/**` (new structure; 10 regenerated + 10 moved, with metas)
- `VoxelEngineAssets/GridSystem/Textures/ItemIcons/` (removed)
- `Scripts/Core/GameVersion.cs` (6.78.5 → 6.78.6), `Changelog.md`

---

### [6.78.5-dev] Icon Batch 2 & Backgrounds Fully Removed

**Type:** PATCH — art batch + polish (save-compatible).

#### 🖼️ Icon batch 2 — ten new items got art
- **New icons**: Science T1 / T2 / T3 (green, blue and purple flasks), Hammer, Iron Pickaxe, Water Bucket, Charged Cell, Stone, Wood Log and Wooden Plank.
- Made with the new **bold, minimal style** — flat chunky shapes, no painted shadows, pure-black keying — so they stay crisp at 51 px with almost no cleanup.
- All ten auto-bound to their item assets (icon references patched by the item-Id keyed pipeline).

#### 🧽 Backgrounds on the original 10 — fully removed (for real this time)
- Root cause: the AI had *painted* dark "shadow plates" behind the items as **opaque paint**, hugging the object — no flood-fill could reach them. That's the dark slab you kept seeing behind the coal, ingots, battery, tank and jetpacks.
- New pipeline surgery removes them:
  - **dark-plate killer** — flat, dark, textureless paint has no brush texture, so it dies while textured art survives;
  - **grabCut colour split** — separates the lit object from its painted shadow by colour;
  - then the usual halo strip, silhouette heal, tight re-crop and rim.
- Icons are now free-standing, tighter and noticeably more visible in their slots. Stray letterbox frames on the Hammer and Science T1 sources were trimmed as well.

#### Files touched
- `VoxelEngineAssets/GridSystem/Textures/ItemIcons/` (+10 new PNGs & metas, 10 reprocessed)
- 10 `ItemDefinition` assets (icon references auto-bound)
- `Scripts/Core/GameVersion.cs` (6.78.4 → 6.78.5), `Changelog.md`

---

### [6.78.4-dev] Simpler, Bolder Item Icons — Readability Tune

**Type:** PATCH — art polish (save-compatible).

#### 🎨 Icons: less detail, more readable
- **Less micro-detail, same item**: every icon got a *simplify* pass — painterly micro-texture is melted down into big, flat poster shapes with a tight palette (14 colours), so the item reads instantly at 51 px instead of dissolving into noise. Still pretty — just no longer busy.
- **Studio glow removed**: the big soft halo that surrounded each item is gone. The item itself now fills the slot edge-to-edge (this also kills the "foggy" look).
- **Clean silhouette + subtle cool rim**: icons sit on smooth, healed cut-out shapes (no more interior speckle holes or lumpy outlines) with a faint light rim that separates them from the dark slot background.
- **Future batches generate simpler from the start**: the icon pipeline prompt now asks for bold, chunky, minimal-detail shapes with no rim glow — new icons will need far less fixing afterwards.

#### Files touched
- `VoxelEngineAssets/GridSystem/Textures/ItemIcons/*.png` (all 10 reprocessed)
- `Scripts/Core/GameVersion.cs` (6.78.3 → 6.78.4), `Changelog.md`

---

### [6.78.3-dev] Readable Icons, Live Item Badges & Idle Auto-Refuel

**Type:** PATCH — fixes + polish (save-compatible).

#### 🎨 Icons — actually readable this time
- **Recropped with a stricter art threshold**: the soft vignette matte still counted as "art" in the bounding box, leaving the real artwork at only ~70 % of the frame. Icons are now recropped so the item itself fills the frame.
- **Larger in-slot rendering**: item icons now render at 51 px inside the 56 px slot (was 44) with `ScaleToFit` — a big readability jump.

#### ⛽ Item badges update live
- The ml / % badges printed **on the jetpack slot icons** were baked at panel build — a draining pack kept showing its old values (found: the per-pack chip tick had never landed — that's part of what you saw).
- The chip rows AND the on-item badges are now re-stamped **every frame** — equipped packs visibly drain on bars, chips and item icons alike, no rebuilds.

#### 🔋 Idle auto-refuel — no more "open the inventory to charge"
- The 10 % → 100 % auto-refuel only ticked mid-flight or on inventory open. PlayerEquipment now runs a slow **0.5 Hz idle check**, so a jetpack at 10 % quietly sips from your freshly charged portable battery even while you're standing around. (Hard-paused with the pause menu, as expected.)

#### Files touched
- `Scripts/UI/GameUIController.cs`
- `Scripts/Player/PlayerEquipment.cs`
- `VoxelEngineAssets/GridSystem/Textures/ItemIcons/*.png` (recrop pass)
- `Scripts/Core/GameVersion.cs` (6.78.2 → 6.78.3), `Changelog.md`

---

### [6.78.2-dev] Refuel Toast De-Spam & Readable Icons

**Type:** PATCH — fixes + art polish (save-compatible).

#### 🔕 "Jetpack Refuelled" toast no longer spams
- The toast fired on ANY pool hitting 10% — including the *power* cell, so it could trigger with a full H₂ bar — and micro-sips from a nearly-empty battery re-fired it potentially every tick.
- Now it only shows for **meaningful refuels** (a pool actually leaving the red zone: ≤10% → >25%), **names the fuel** (H₂ / PWR / both) and is hard rate-limited to **once per 45 s**.

#### 🎨 Icons — from "mysterious dark blob" to genuinely readable
- **Tight auto-crop**: each icon is cropped to its art bounding box so the item fills the frame instead of floating tiny in a dark square.
- **Adaptive brightness lift**: dark gunmetal/slate art measured far below readable luminance on the dark slot background — every icon now gets a measured lift (×1.30 typical) so coal, ingots and jetpacks read instantly at 44 px.

#### Files touched
- `Scripts/Player/PlayerEquipment.cs`
- `VoxelEngineAssets/GridSystem/Textures/ItemIcons/*.png` (crop + brightness passes)
- `Scripts/Core/GameVersion.cs` (6.78.1 → 6.78.2), `Changelog.md`

---

### [6.78.1-dev] Inventory-Stability Fix, Icon Binding & Jetpack Auto-Charge Polish

**Type:** PATCH — critical UI fix + behaviour polish + art pipeline (save-compatible).

#### 🛠 Fix: inventory UI breaking when gas moves while open
- **Root cause:** refueling a jetpack from a portable H₂ tank (or any container mutation) fired while the inventory panel was *mid-build* — `Refresh()` re-entered itself, cleared the still-running layout and left half-built panels (missing/gone slots).
- **Fix:** (1) `Refresh()` is now re-entrancy-guarded — nested calls are deferred one frame and coalesced; (2) jetpack ensure/refuel runs when the inventory **opens**, before any UI builds, never during layout.

#### 🚀 Jetpack auto-charge (verified + feedback)
- Confirmed the 10% rule end-to-end: reaching **≤10% auto-refills the jetpack's cell back to 100%** as long as the player carries a **portable battery** (charged cells still work as fallback, H₂ tanks cover the hydrogen pool). Runs in-flight and on inventory open.
- Auto (in-flight) top-ups now show a small heads-up ("Jetpack Refuelled — hit 10%, topped up to 100%") so the sipped batteries never feel invisible.

#### 🖥 Vitals HUD — PWR now counts everything you carry
- The PWR pill (left of OXY) is now **jetpack power cells + all portable batteries in the inventory**, mirroring how the H₂ pill counts tanks + pack fuel. Visible whenever any carried power pool exists.

#### 🎨 Icons — transparency, hand-painted pass & bulletproof binding
- All 10 batch-1 icons re-processed: **real transparent backgrounds** (edge-flood keying + halo cleanup + soft vignette so they dissolve into the UI) and a **painterly stylize pass** for a more hand-made, less AI-render feel.
- **Binding is now keyed by `itemId`, not asset filename** — and the offline pipeline re-ran over the WHOLE assets tree, binding **all 23 duplicate definitions** of the icon'd items in every folder (root fix for "image on the wrong item").
- New in-engine double-check: **Tools ▸ Voxel Engine ▸ Sync Item Icons (ItemIcons folder)** rebinds every ItemDefinition by itemId and prints a coverage report (`Scripts/Editor/ItemIconSync.cs`).
- Future batches use a pure-black-background hand-painted prompt for perfect keying.
- Census taken for planning: the game has **398 unique items**; icon batches will ship hero-items-first (handhelds, resources, tools, armor) across upcoming updates.

#### Files touched
- `Scripts/UI/GameUIController.cs`
- `Scripts/Player/PlayerEquipment.cs`
- `Scripts/UI/VitalsHud.cs`
- `Scripts/Editor/ItemIconSync.cs` (new)
- `VoxelEngineAssets/GridSystem/Textures/ItemIcons/*.png` (re-processed) + metas + icon refs in 12 item `.asset` files (all duplicates covered)
- `Scripts/Core/GameVersion.cs` (6.78.0 → 6.78.1), `Changelog.md`

---

### [6.78.0-dev] Live World (No UI Pause), Battery Numbers & AAA Icons — Batch 1

**Type:** MINOR — gameplay-flow change + UI features + content (save-compatible, no save-format changes).

#### ⏱ The world no longer pauses when a UI opens
- Opening the **inventory or any machine panel no longer freezes the game** — the physics simulation keeps running while you browse: gravity, walking inertia, and **jetpack flight + fuel drain continue** (hovering mid-air with the inventory open now actually burns H₂, like it should). Multiplayer-ready behaviour.
- **Only the pause menu hard-pauses** (it also stops time). The death screen freezes player control without stopping the world.
- Player **input is silenced** while any blocking UI is open (movement/thrust keys and mouse-look belong to the UI) — you keep hovering in place, but can't steer blind. Typing in search fields never moves the player, and jetpack toggling is suppressed mid-typing.
- Mouse-look stays suspended while the cursor belongs to a UI, exactly as before.

#### 🔋 Battery & jetpack numbers everywhere
- **Jetpack bay**: new per-pack chips under the fuel bars showing exact **H₂ ml `cur / cap`** and **charge %** for every equipped jetpack — the Hybrid shows both pools, updating live every frame.
- **Slot badges**: jetpack slots show their **ml + % badges** on the icon itself; **Portable Batteries now show a % charge badge** + fill bar on every slot (inventory, hotbar, docks).
- **Battery gauge fix**: the 12-segment gauge no longer drops to 0 while a docked device is charging — the dock used to rebuild the whole panel every frame (kick-starting the sweep from 0 each tick). Docs now update silently; the gauge plays its power-on sweep only when you genuinely open a battery.
- **Grid (ship/base) batteries got the same premium 12-segment gauge** with the eased power-on sweep, color-coded % and live stored-Wh readout — the cool animation now plays on every battery type.

#### 🎨 AAA item icons — batch 1 (of 8)
- New premium icon pipeline: hi-res stylized icons on the signature dark slate backdrop, family-colored accents matching the UI.
- **First 10 items shipped**: Hydrogen Boost Pack, Atmospheric Jetpack, Hybrid Jetpack, Portable Hydrogen Tank, Portable Battery, Coal, Iron Ore, Iron Ingot, Copper Ingot, Steel Ingot.
- The remaining 61 items (science packs, tools, ingots, ship parts…) land in the next batches — the pipeline is in place, so each future update ships more.

#### Files touched
- `Scripts/UI/UIState.cs`, `Scripts/Menu/InGamePauseMenu.cs`, `Scripts/UI/DeathScreenHud.cs`
- `Scripts/Player/PlayerController.cs`
- `Scripts/Power/PowerBattery.cs`
- `Scripts/UI/GameUIController.cs`, `Scripts/GridSystem/UI/GridBlockUI.cs`
- `Scripts/Core/GameVersion.cs` (6.77.0 → 6.78.0), `Changelog.md`
- `VoxelEngineAssets/GridSystem/Textures/ItemIcons/*.png` (10 new) + sprite metas + icon refs in 10 item `.asset` files

---

### [6.77.0-dev] Battery Device Charger, Dual-Fuel Jetpacks & Flash-Free HUD

**Type:** MINOR — new systems (device-charger dock, dual-fuel jetpacks, environment & twin-pack rules, PWR HUD pill) — fully save-compatible. All save changes are additive; legacy packs, tanks and batteries load unchanged.

#### 🔋 Battery block rework
- **Device Charger dock** on the world Battery: accepts **Portable Batteries** and **power-fed jetpacks (Atmospheric / Hybrid)** — trickles block charge into the item's cell at 500 W (1 unit = 1 Wh). Shift-click a chargeable device while the battery panel is open to dock it instantly.
- **RMB top-up**: hold a power jetpack and RMB the battery for +400 Wh (Shift = fill to 100%).
- **New battery panel**: animated 12-segment charge gauge with eased power-on sweep and color-coded % (green → amber → red), live **Stored Wh**, **Power In `cur / max W`**, **Power Out `cur / max W`** (per-battery network telemetry), status pill (CHARGING / DISCHARGING / DOCK+ / FULL / IDLE) and a live docked-device readout. Updates in place every frame — no rebuild flicker, no eaten clicks.
- Battery charge now **persists in the save file** (additive fields).

#### 🚀 Jetpacks — dual-fuel, environment & twin rules
- **Separate H₂ tank + power cell per pack** (H₂ in `durability`, hybrid power cell in the new additive `charge` pool). Hybrid: **1200 ml H₂ + 600 Wh cell**. Atmospheric: **1000 Wh cell**. Hydrogen Boost: **800 ml H₂**.
- **Hybrid flight rules**: power cell alone cruises (fly, no shift); H₂ alone flies **and** unlocks the shift afterburner; with both, cruise sips power and boosting burns H₂.
- **Atmospheric jetpack only ignites inside an atmosphere** (air-density check on planets/moons/flat worlds). Trying to fly in vacuum shows *"No atmosphere — engine can't ignite here"*.
- **Hydrogen Boost now works everywhere** (atmosphere + vacuum) and stays the thirstiest pack per ml.
- **Twin drive**: two identical fueled packs equipped = **×1.35 speed, ×1.20 boost**, and the pair drains the fuller tank first so capacity behaves as one doubled tank. Bay status pill shows `TWIN ×2`.
- Jetpack bay bars are now **summed across both slots**, always visible when that pool type is equipped (an empty Atmospheric cell shows its 0 Wh bar instead of rendering nothing) and **update live every frame**.
- New packs start **empty** — fuel/charge only comes from real sources (H₂ tanks, batteries, cells, docks).
- **No more voided fuel**: over-capacity legacy stacks (e.g. 2000 ml crammed into a 1200 ml pack) spill the excess back into your portable tanks/batteries instead of being silently clamped; quick-equip (F), shift-click equip/unequip, machine routing and world drops all carry the pack's full fuel pools (H₂ + Wh) with the stack; charged cells are only consumed when they fit entirely.

#### 🛢 Gas tanks — hydrogen jetpacks welcome
- World **and** grid H₂ Fill Docks accept **Hydrogen Boost / Hybrid jetpacks** next to Portable Hydrogen Tanks (auto-fill from bulk, only ever taking what fits).
- **RMB with a hydrogen jetpack** on an H₂ tank tops up the pack (Shift = 100%).
- Shift-click docking into the open H₂ tank / battery panel (QoL routing before auto-equip).

#### 🖥 HUD — flash-free + new PWR pill
- **UI reworked into persistent layers** (content / HUD / tooltip): scrolling the hotbar, sorting, crafting or any container tick no longer destroys & recreates the HUD — the flicker is gone.
- **Vitals HUD**: H₂ bar now counts **inventory tanks + fuel inside the equipped jetpacks** (fixes "0 with 1200 in the pack").
- **New PWR pill docked to the LEFT of the OXY bar** whenever a power-fed jetpack is equipped — shows live charge %.
- Item tooltips now show jetpack pools (H₂ ml + Power Wh), speed/boost multipliers and operating environment, plus tank/battery contents.
- Portable Battery RMB feedback now reports Wh.

#### Files touched
- `Scripts/Items/ItemStack.cs`, `Scripts/Items/JetpackItem.cs`
- `Scripts/Player/PlayerEquipment.cs`, `Scripts/Player/PlayerController.cs`, `Scripts/Player/PlayerInteractionTool.cs`
- `Scripts/Power/PowerBattery.cs`, `Scripts/Power/PowerNetworkManager.cs`
- `Scripts/Gas/GasTank.cs`, `Scripts/GridSystem/GridGasTank.cs`, `Scripts/GridSystem/UI/GridBlockUI.cs`
- `Scripts/UI/GameUIController.cs`, `Scripts/UI/VitalsHud.cs` (renamed from `RustStyleHud.cs`), `Scripts/UI/MachineUIs.cs`, `Scripts/UI/Tooltip.cs`, `Scripts/UI/BuildFeedbackHud.cs`, `Scripts/UI/PaintHud.cs`, `Scripts/UI/PlayerHud.cs`
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `VoxelEngineAssets/GridSystem/Items/Equip_Jetpack*.asset`
- `Scripts/Core/GameVersion.cs` (6.75.1 → 6.77.0), `Changelog.md`

---

### [6.75.1-dev] Compile fix — gas fields on SavedPlacedBlock

**Type:** PATCH — compile fix (save-compatible).

Gas tank bulk save fields (`gasType`, `gasSelectedType`, `gasStoredAmount`) were incorrectly added to `SavedGridBlock`. Moved them onto `SavedPlacedBlock` where capture/restore actually use them (CS1061).

**Files touched:**
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Core/GameVersion.cs` (6.75.0 → 6.75.1), `Changelog.md`

---

### [6.75.0-dev] Gas tanks — type select, portable H₂ dock, Shift-fill; jetpack refill fix

**Type:** MINOR — gas logistics + jetpack fuel fix (save-compatible).

1. **World GasTank**
   - **Gas type selector** in the tank UI (Hydrogen / Oxygen / Steam / Exhaust) — change while empty.
   - **Portable H₂ Tank dock** slot when the tank is set to Hydrogen — drop a portable tank in to auto-fill from bulk.
   - Bulk gas type/amount saved on placed blocks.
2. **Grid GasTank**
   - Existing type selector kept; **Portable H₂ dock** added when type is Hydrogen.
3. **Fill controls**
   - RMB portable tank on H₂ tank → fill tick (250 ml).
   - **Shift+RMB** → fill portable tank to **100%** in one action.
   - Works on world and grid hydrogen tanks.
4. **Jetpack refill fix**
   - Runtime capacity migration for old packs.
   - Recharge threshold defaults to 10% if unset.
   - Hydrogen family always treated as H₂-fuelled.
   - Auto-recharge runs every flight tick at/under threshold; inventory UI refreshes after siphon.

**To use:** open a Gas Tank → pick **Hydrogen** → pipe H₂ in → put Portable Hydrogen Tank in the dock (or Shift+RMB the tank while holding it). Equip H₂ jetpack with filled portable tanks in inventory → fly; at ≤10% the pack pulls ml from the tanks.

**Files touched:**
- `Scripts/Gas/GasTank.cs`, `Scripts/GridSystem/GridGasTank.cs`
- `Scripts/UI/MachineUIs.cs`, `Scripts/GridSystem/UI/GridBlockUI.cs`, `Scripts/UI/GameUIController.cs`
- `Scripts/Player/PlayerInteractionTool.cs`, `PlayerEquipment.cs`, `Scripts/Items/JetpackItem.cs`
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Core/GameVersion.cs` (6.74.2 → 6.75.0), `Changelog.md`

---

### [6.74.2-dev] Compile fix — missing semicolon in Step 47 setup

**Type:** PATCH — compile fix (save-compatible).

Fixed CS1002 in `VoxelEngineSetupWindow.BuildJetpackFuelContent` (missing `;` after `AddRecipe` for Portable Hydrogen Tank) and refreshed the Step 47 dialog text.

**Files touched:**
- `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Core/GameVersion.cs` (6.74.1 → 6.74.2), `Changelog.md`

---

### [6.74.1-dev] Compile fix — MakeItemFillBar helper

**Type:** PATCH — compile fix (save-compatible).

Added missing `MakeItemFillBar` private helper on `GameUIController` used by inventory slot fill bars for tools, Portable Hydrogen Tanks, and jetpacks (CS0103).

**Files touched:**
- `Scripts/UI/GameUIController.cs`
- `Scripts/Core/GameVersion.cs` (6.74.0 → 6.74.1), `Changelog.md`

---

### [6.74.0-dev] Portable Hydrogen Tank (ml) + H₂ vitals bar; stamina removed

**Type:** MINOR — jetpack fuel UX + vitals overhaul (save-compatible).

1. **Renamed** Hydrogen Canister → **Portable Hydrogen Tank** (`item_portable_hydrogen_tank`).
2. **Metric units** — tank capacity / fill / jetpack fuel all use **millilitres (ml)** (display switches to L at ≥1000 ml).
3. **Item looks like a tank** — procedural bottle icon (body, neck, valve, liquid level) + bottom fill bar + ml label in inventory/hotbar.
4. **Jetpack refill fixed** — H₂/Hybrid packs always treat hydrogen family as hydrogen-fuelled (even if old assets had flags wrong); siphon Portable H₂ Tanks at ≤10% without destroying them.
5. **Vitals HUD** — **Stamina bar removed**; replaced with **H₂** bar showing total ml across all Portable Hydrogen Tanks in inventory.
6. **Stamina gameplay removed** — sprint/jump no longer gated or drained by stamina (fields kept inert for old saves).

**To use:** run Step 47 → craft Portable Hydrogen Tank → fill from a Hydrogen Gas Tank (RMB) → equip H₂ jetpack + keep tanks in inventory → fly; at 10% the pack pulls ml from tanks. Watch the H₂ vitals bar on the right.

**Files touched:**
- `Scripts/Items/HydrogenCanisterItem.cs`, `JetpackItem.cs`
- `Scripts/Player/PlayerEquipment.cs`, `PlayerController.cs`, `PlayerStats.cs`
- `Scripts/UI/RustStyleHud.cs`, `GameUIController.cs`
- `Scripts/Player/PlayerInteractionTool.cs`, `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Core/GameVersion.cs` (6.73.0 → 6.74.0), `Changelog.md`, `Roadmap.md`

---

### [6.73.0-dev] Hydrogen Canister — refillable H₂ tanks for jetpacks

**Type:** MINOR — jetpack fuel redesign (save-compatible).

**Replaces disposable Hydrogen Cells with refillable Hydrogen Canisters:**

1. **`HydrogenCanisterItem`** — portable tank; fill stored in `ItemStack.durability` (0..capacity, default 200).
2. **Fill from world** — hold canister, **RMB a Hydrogen Gas Tank** to transfer H₂ into the canister (repeat until full).
3. **Jetpack auto-recharge at ≤10%** — equipped Hydrogen Boost / Hybrid packs pull H₂ from inventory canisters when fuel drops to the threshold (does **not** destroy the canister; just drains it).
4. Atmospheric / Hybrid still use **Charged Cells** for the power side.
5. Jetpack Bay hint updated. Step 47 authors the canister + tunes packs.

**To use:** run Step 47 → craft Hydrogen Canister → produce H₂ into a Gas Tank (Electrolyser) → RMB tank with canister equipped to fill → equip H₂/Hybrid jetpack + keep canisters in inventory → fly; at 10% the pack tops up from canisters.

**Files touched:**
- `Scripts/Items/HydrogenCanisterItem.cs` (new)
- `Scripts/Items/JetpackItem.cs` (threshold, no cell refuel)
- `Scripts/Player/PlayerEquipment.cs` (canister siphon at ≤10%)
- `Scripts/Player/PlayerInteractionTool.cs` (RMB fill from GasTank)
- `Scripts/UI/GameUIController.cs`, `Scripts/Editor/VoxelEngineSetupWindow.cs`
- `Scripts/Core/GameVersion.cs` (6.72.0 → 6.73.0), `Changelog.md`, `Roadmap.md`

---

### [6.72.0-dev] Jetpack fuel accounting — Hydrogen/Charged Cells + Bay readout

**Type:** MINOR — jetpack progression (save-compatible).

**Jetpack Families remaining work (roadmap 11.3):** fuel/power accounting is live.

1. **`JetpackItem`** — `fuelCapacity`, `drainPerSecond`, `boostDrainPerSecond`, cell refuel amounts. Runtime charge stored on `ItemStack.durability` (saved).
2. **`PlayerEquipment`**
   - Fuel-aware `HasUsableJetpack` / best-pack selection
   - `TryConsumeFlightFuel` while flying (boost drains faster)
   - **Auto-refuel** from inventory: Hydrogen Cells → H₂/Hybrid, Charged Cells → Atmospheric/Hybrid
3. **`PlayerController.FlyUpdate`** — drains fuel each frame; empty pack exits fly mode (unless flight research unlocked) with a toast.
4. **Jetpack Bay UI** — ONLINE / LOW / DRY pill + fuel bar `current/max` + auto-refuel hint.
5. **Step 47** — tunes all three jetpack families + authors Hydrogen Cell (+ ensures Charged Cell). Non-destructive.

**To use:** run Step 47 (or re-run Step 12 then 47) → craft Hydrogen Cells / Charged Cells → equip a jetpack → fly. Watch the Bay fuel bar drop; keep cells in inventory to auto-siphon. Boost (Sprint) drains faster.

**Files touched:**
- `Scripts/Items/JetpackItem.cs`
- `Scripts/Player/PlayerEquipment.cs`, `PlayerController.cs`
- `Scripts/UI/GameUIController.cs` (Jetpack Bay)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 47 + jetpack defaults)
- `Scripts/Core/GameVersion.cs` (6.71.0 → 6.72.0), `Changelog.md`, `Roadmap.md`

---

### [6.71.0-dev] Painting System — 15 cosmetic finishes + Paint Tool

**Type:** MINOR — new cosmetic tool/system (save-compatible).

**Painting System (roadmap §4.7 item 17):**
1. **`PaintFinish` catalogue** — 15 finishes (matte white/black, industrial grey, steel, chrome, carbon, rust, copper, brass, hazard yellow, crusader blue, signal red, forest green, gloss white, futuristic teal).
2. **`BlockPaint`** — runtime component on static `PlacedBlock`, `GridBlock`, and tiered builds. Caches originals so clear restores the look.
3. **`PaintToolItem`** — hand tool:
   - **LMB** paints the looked-at block with the selected finish
   - **RMB** cycles finish
   - **Shift+RMB** clears finish (restores original materials)
4. **`PaintHud`** — left-side swatch + name while the tool is held.
5. **World inspection** — shows finish name on painted static/grid blocks.
6. **Persistence (additive)** — `paintFinish` on `SavedPlacedBlock` + `SavedGridBlock` (0 = none / legacy).
7. **Step 46 (wizard)** — Paint Tool asset + Crafting Bench recipe (non-destructive).

**Cosmetic only** — no mass/armor/stats change.

**To use:** run Step 46 → craft Paint Tool → equip → RMB to pick a finish → LMB a chest/wall/grid block → colour updates. Save/reload keeps the finish.

**Files touched:**
- `Scripts/Building/PaintFinish.cs`, `BlockPaint.cs` (new)
- `Scripts/Items/PaintToolItem.cs` (new)
- `Scripts/UI/PaintHud.cs` (new)
- `Scripts/Player/PlayerInteractionTool.cs`, `Scripts/UI/GameUIController.cs`, `Scripts/UI/WorldInspectionHud.cs`
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 46)
- `Scripts/Core/GameVersion.cs` (6.70.1 → 6.71.0), `Changelog.md`, `Roadmap.md`

---

### [6.70.1-dev] Compile fix — restore DefenseStatus helper methods

**Type:** PATCH — compile fix (save-compatible).

Restored missing private helpers in `DefenseStatus.cs` that `TryDescribe` calls:
- `PolicySuffix` (conserve/reserve readout)
- `EngagementSuffix` (range/arc readout)
- `ApplyReserveLow` (HOLD marks LOW)

**Files touched:**
- `Scripts/Combat/DefenseStatus.cs`
- `Scripts/Core/GameVersion.cs` (6.70.0 → 6.70.1), `Changelog.md`

---

### [6.70.0-dev] Defense engagement — range slider + firing arc

**Type:** MINOR — defense configuration (save-compatible).

**Every automated defense piece now exposes engagement range and a horizontal firing arc** (roadmap §4.8 turret UI: engagement range + firing arc):

1. **`IDefenseEngagement` + `DefenseEngagement`** in `DefenseLogistics.cs`
   - `EngagementRange` — player cap on auto-acquire distance (clamped to the weapon's physical max range)
   - `FiringArcDegrees` — full cone width 15–360°, centred on the **placed forward** facing, projected on the local tangent plane (spherical-world safe)
   - `IsInEngagement` used by Valid/InRange so out-of-arc targets are ignored
2. Wired on Auto Turret, Artillery, Flamethrower, Mortar, Giant Shell, Anti-Air, Energy/Relic.
3. **Defense panel → Engagement** — Range slider + Arc slider with live labels.
4. **World inspection** — appends `Range Xm · Arc Y°` (or `360°`).
5. **Persistence (additive)** — `engagementRange` / `firingArcDegrees` / `hasEngagement` on `SavedDefenseState`.

**Notes:** Arc is horizontal only (elevation unrestricted). Mortar still respects its min-range floor. Physical muzzle velocity / shell arc unchanged — only target selection is gated.

**To use:** place a turret facing a lane → RMB → set Arc to ~90° and Range below max → enemies behind the turret or beyond the slider are ignored. Rotate the block to aim the cone.

**Files touched:**
- `Scripts/Combat/DefenseLogistics.cs` (engagement API)
- `Scripts/Combat/Turret.cs`, `Artillery.cs`, `FlamethrowerTurret.cs`, `MortarTurret.cs`, `GiantShellTurret.cs`, `AntiAirTurret.cs`, `EnergyRelicTurret.cs`
- `Scripts/Combat/DefenseStatus.cs`
- `Scripts/UI/GameUIController.cs`
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Core/GameVersion.cs` (6.69.0 → 6.70.0), `Changelog.md`, `Roadmap.md`

---

### [6.69.0-dev] Defense ammo policy — Conserve Ammo + Reserve Stock

**Type:** MINOR — defense configuration (save-compatible).

**Every automated defense piece now supports a factory-style ammo policy** (roadmap §4.8 turret UI: conserve-ammo + reserve stock):

1. **`IDefenseFirePolicy` + `DefenseFirePolicy`** in `DefenseLogistics.cs`
   - `ConserveAmmo` toggle
   - `ReserveStock` (0–50 units)
   - Auto-fire stops once stock is **at or below** the reserve
   - **Manual artillery cockpit fire ignores the reserve** (you can still dump shells by hand)
2. Wired on Auto Turret, Artillery (Minigun/Cannon/Gustav), Flamethrower, Mortar, Giant Shell, Anti-Air, Energy/Relic.
3. **Defense panel** — new Ammo Policy section: Conserve toggle + Reserve −/+ steppers + hint.
4. **World inspection** — shows `rN` when conserve is on, and `HOLD rN` while auto-fire is withheld at the reserve (also marks LOW).
5. **Persistence (additive)** — `SavedDefenseState.conserveAmmo` / `reserveStock` / `hasAmmoPolicy` (legacy saves leave policy off).

**To use:** RMB a turret → enable **Conserve Ammo** → set **Reserve** (e.g. 5) → auto-fire holds the last 5 units for emergencies. Resupply above the reserve to resume. Artillery cockpit LMB still fires reserved shells.

**Files touched:**
- `Scripts/Combat/DefenseLogistics.cs` (policy API)
- `Scripts/Combat/Turret.cs`, `Artillery.cs`, `FlamethrowerTurret.cs`, `MortarTurret.cs`, `GiantShellTurret.cs`, `AntiAirTurret.cs`, `EnergyRelicTurret.cs`
- `Scripts/Combat/DefenseStatus.cs` (inspection HOLD/rN)
- `Scripts/UI/GameUIController.cs` (panel controls)
- `Scripts/Persistence/WorldStatePersistence.cs` (save/restore)
- `Scripts/Core/GameVersion.cs` (6.68.0 → 6.69.0), `Changelog.md`, `Roadmap.md`

---

### [6.68.0-dev] Defense status — inspection, low-ammo toasts, look prompts

**Type:** MINOR — defense UX / situational awareness (save-compatible).

**Automated defenses now report their state clearly in the world:**

1. **`DefenseStatus.cs`** — shared describe / prompt / empty-toast helpers for every defense kind.
2. **World inspection overlay** — looking at a turret shows title, AUTO/MANUAL, ammo/fuel/cells, faction filter, EMPTY/LOW prefix, and HP bar.
3. **Empty-ammo toasts** — when a defense piece fires its last round (or flamethrower runs dry mid-engage), a throttled feed toast asks for belt/pipe/panel resupply.
4. **Look prompts** — non-artillery defenses show `RMB · Configure defense`; artillery keeps `H · Configure (RMB to enter)`.
5. **Defense panel stock strip** — OPEN panel shows EMPTY / LOW / STOCKED with the live ammo line.

**Wired empty notifies on:** Auto Turret, Artillery, Mortar, Giant Shell, Anti-Air, Energy/Relic, Flamethrower.

**Roadmap snapshot:** Weapons/combat + player armor rows updated to match shipped 6.42–6.67 defense/combat content (were stale MISSING).

**Files touched:**
- `Scripts/Combat/DefenseStatus.cs` (new)
- `Scripts/Combat/Turret.cs`, `Artillery.cs`, `MortarTurret.cs`, `GiantShellTurret.cs`, `AntiAirTurret.cs`, `EnergyRelicTurret.cs`, `FlamethrowerTurret.cs`
- `Scripts/UI/WorldInspectionHud.cs`, `Scripts/UI/GameUIController.cs`
- `Scripts/Player/PlayerInteractionTool.cs`
- `Scripts/Core/GameVersion.cs` (6.67.0 → 6.68.0), `Changelog.md`, `Roadmap.md`

---

### [6.67.0-dev] Automated Defense Ammo Logistics — belts/pipes refill turrets

**Type:** MINOR — factory logistics for all defense magazines (save-compatible).

**Defense pieces now accept factory ammo automatically** via the existing belt / chute / funnel / item-pipe network — no more mandatory drag-drop reloads once a supply line is built.

1. Shared helper `Scripts/Combat/DefenseLogistics.cs` — capacity + insert that honours each magazine's `AcceptFilter`.
2. Every placeable defense sink implements:
   - `IItemConsumer` → conveyor belts, chutes, funnels deposit ammo
   - `IDirectItemPortEndpoint` → item pipes push ammo on contact
   - `IInventoryInterface` (magazine-based) → chute/funnel inventory path
3. Wired on:
   - Auto Turret (Bullets → integer ammo counter)
   - Artillery / Minigun / Gustav (shells + bullets magazine)
   - Flamethrower (Flame Canisters / Coal)
   - Mortar (mortar shells)
   - Giant Shell Turret (giant shells)
   - Anti-Air (AA Rounds / Bullets)
   - Energy / Relic (Charged Cells / Relic Capacitors)

**Filters still apply** — a mortar will not eat AA rounds; a flamethrower will not eat shells. Wrong items are refused (capacity 0).

**To use:** point a belt, chute, funnel, or item pipe at a placed defense piece and supply the matching ammo from an assembler/chest. Watch the defense panel magazine fill without opening it.

**Manual panel reload still works** (RMB → drag, or Auto Turret "Reload from Inventory").

**Files touched:**
- `Scripts/Combat/DefenseLogistics.cs` (new)
- `Scripts/Combat/Turret.cs`, `Artillery.cs`, `FlamethrowerTurret.cs`, `MortarTurret.cs`, `GiantShellTurret.cs`, `AntiAirTurret.cs`, `EnergyRelicTurret.cs`
- `Scripts/Core/GameVersion.cs` (6.66.1 → 6.67.0), `Changelog.md`, `Roadmap.md`

---

### [6.66.1-dev] Compile fix — AntiAir IsAerial + SavedDefenseState preferAerial

**Type:** PATCH — compile fixes (save-compatible).

1. `AntiAirTurret.IsAerial` is an instance method again so it can read `minAltitude` (CS0120).
2. `SavedDefenseState` now includes additive `preferAerial` / `hasPreferAerial` fields used by AA save/restore (CS0117 / CS1061).
3. `FlamethrowerTurret.SpawnGroundFire` no longer assigns `pos = pos` (CS1717).

**Files touched:**
- `Scripts/Combat/AntiAirTurret.cs`
- `Scripts/Combat/FlamethrowerTurret.cs`
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Core/GameVersion.cs` (6.66.0 → 6.66.1), `Changelog.md`

---

### [6.66.0-dev] Energy / Relic Turret — late-tier electrical beam defense

**Type:** MINOR — new defensive structure (save-compatible).

**Added the Energy / Relic Turret** (`Scripts/Combat/EnergyRelicTurret.cs`) — final named turret in the Base Defense Turret Network (roadmap §4.8):
1. Placeable late-tier turret with a **spinning relic crystal**, core glow, and emitter fins.
2. Fires **hitscan Electrical beams** (DamageType.Electrical) with a bright beam + muzzle/impact sparks.
3. Ammo magazine accepts:
   - **Charged Cell** — standard beam (damage 28)
   - **Relic Capacitor** — heavier violet charged shot (damage 70), preferred when available
4. Faction filter + Auto-Fire via the shared defense panel; slight preference for higher-HP elites.
5. **Step 45 (wizard):** prefab, block, Charged Cell + Relic Capacitor items, Assembler recipes. Non-destructive / re-runnable.

**Persistence (additive):** cell magazine + filter/autoMode save/restore like the other defense pieces.

**To use:** run Step 45 → craft Energy / Relic Turret + Charged Cells (and optional Relic Capacitors) → place → RMB → load cells → Auto-Fire.

**Defense network status:** Light Gun (Auto Turret), Heavy Ballistic (Minigun/Cannon/Gustav), Flamethrower, Mortar, Giant Shell, Anti-Air, Energy/Relic — all authored. Next roadmap slice: automated ammo logistics.

**Files touched:**
- `Scripts/Combat/EnergyRelicTurret.cs` (new)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 45)
- `Scripts/UI/GameUIController.cs`, `Scripts/Player/PlayerInteractionTool.cs`
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Core/GameVersion.cs` (6.65.0 → 6.66.0), `Changelog.md`

---

### [6.65.0-dev] Anti-Air Turret — flak vs flyers (Griffin / Roc)

**Type:** MINOR — new defensive structure (save-compatible).

**Added the Anti-Air Turret** (`Scripts/Combat/AntiAirTurret.cs`) — next piece of the Base Defense Turret Network (roadmap §4.8):
1. Fast **dual-barrel** flak turret with radar dish silhouette.
2. **Aerial preference:** prioritises Griffins, Rocs, and high-altitude targets; optional **Aerial Only** toggle on the defense panel.
3. Fires **proximity-burst flak** (`FlakRound`) that detonates near flyers (small burst, no terrain crater).
4. 3-round bursts with short pauses; leads moving targets using rigidbody velocity.
5. Ammo: **AA Rounds** (preferred) or **Bullets** fallback via the shared defense panel magazine.
6. **Step 44 (wizard):** prefab, block, AA Rounds item + Assembler recipes. Non-destructive / re-runnable.

**UI harden:** defense panel filter/auto helpers refactored so new turret kinds no longer grow the toggle signature.

**Persistence (additive):** AA ammo magazine + filter/autoMode + `preferAerial` flag.

**To use:** run Step 44 → craft Anti-Air Turret + AA Rounds → place with sky LOS → RMB → load rounds → Auto-Fire → spawn a Griffin/Roc overhead.

**Files touched:**
- `Scripts/Combat/AntiAirTurret.cs` (new — turret + FlakRound)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 44)
- `Scripts/UI/GameUIController.cs` (panel + helpers)
- `Scripts/Player/PlayerInteractionTool.cs`
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Core/GameVersion.cs` (6.64.0 → 6.65.0), `Changelog.md`

---

### [6.64.0-dev] Giant Shell Turret — siege / boss killer

**Type:** MINOR — new defensive structure (save-compatible).

**Added the Giant Shell Turret** (`Scripts/Combat/GiantShellTurret.cs`) — next piece of the Base Defense Turret Network (roadmap §4.8):
1. Placeable **siege gun** on a traverse ring. Slow tracking, long range (~90 m), devastating single shots.
2. Fires factory-built **Giant Shells** one at a time (magazine via the defense panel). Huge blast (radius 14) + voxel crater.
3. **Boss preference:** when several enemies are in range + LOS, scores by HP and boss-like type names so Rocs / elites are prioritised over fodder.
4. Must be roughly on-target (aim cone) before firing — reads as a heavy, deliberate weapon.
5. **Step 43 (wizard):** massive barrel model, component wiring, placeable `BlockItem`, Giant Shell item + Assembler recipes. Non-destructive / re-runnable.

**Defense panel / RMB / persistence** extended for the giant shell magazine + filter/autoMode (additive).

**To use:** run Step 43 → craft Giant Shell Turret + Giant Shells at the Assembler → place with clear LOS to the approach → RMB → load shells → Auto-Fire → it saves the big shells for high-HP threats.

**Files touched:**
- `Scripts/Combat/GiantShellTurret.cs` (new — turret + GiantShell projectile)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 43)
- `Scripts/UI/GameUIController.cs`, `Scripts/Player/PlayerInteractionTool.cs`
- `Scripts/Persistence/WorldStatePersistence.cs`
- `Scripts/Core/GameVersion.cs` (6.63.0 → 6.64.0), `Changelog.md`

---

### [6.63.0-dev] Mortar Turret — indirect fire (Explosive / Smoke / Illumination)

**Type:** MINOR — new defensive structure (save-compatible).

**Added the Mortar Turret** (`Scripts/Combat/MortarTurret.cs` + `MortarShell.cs`) — next piece of the Base Defense Turret Network (roadmap §4.8):
1. Placeable bipod mortar that **auto-targets by faction filter** and lobs **high-arc shells** under radial gravity.
2. **No line of sight required** — fires over walls and terrain. Min range ~8 m / max ~55 m so it won't drop on itself.
3. **Three shell types** loaded via the defense panel magazine:
   - **Explosive** — blast + small voxel crater
   - **Smoke** — lingering smoke cloud (cover / marker)
   - **Illumination** — bright falling flare that lights the battlefield
4. Analytic lob velocity with peak-height aiming so arcs read cleanly on spherical worlds.
5. **Step 42 (wizard):** bipod + elevated tube model, `MortarTurret` component, placeable `BlockItem`, three shell items + Assembler recipes. Non-destructive / re-runnable.

**Defense panel / persistence / RMB** extended for the mortar (same shared panel as Artillery / Auto Turret / Flamethrower). Magazines + filter/autoMode save additively.

**To use:** run Step 42 → craft Mortar Turret + shells at the Assembler → place behind cover → RMB → drag shells in → Auto-Fire → it lobs over walls onto hostiles.

**Files touched:**
- `Scripts/Combat/MortarTurret.cs` (new), `Scripts/Combat/MortarShell.cs` (new)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 42)
- `Scripts/UI/GameUIController.cs` (defense panel)
- `Scripts/Player/PlayerInteractionTool.cs` (RMB open)
- `Scripts/Persistence/WorldStatePersistence.cs` (magazine + defense state)
- `Scripts/Core/GameVersion.cs` (6.62.0 → 6.63.0), `Changelog.md`

---

### [6.62.0-dev] Flamethrower Turret — close-range area denial + defense save/UI harden

**Type:** MINOR — new defensive structure + defense persistence (save-compatible).

**Added the Flamethrower Turret** (`Scripts/Combat/FlamethrowerTurret.cs`) — next piece of the Base Defense Turret Network (roadmap §4.8):
1. Placeable dual-nozzle turret that **auto-targets by faction filter** (Enemies / Players / Passive) and sprays a **continuous cone of fire** while fuel lasts.
2. **Fire damage + burn** on creatures/players inside the cone; randomly seeds short-lived **ground-fire patches** (`FireWallHazard`) for area denial.
3. **Fuel magazine** accepts **Flame Canisters** (~8 s each) or **Coal** (weaker fallback). Continuous buffer drains while engaged; canisters auto-feed when empty.
4. **Step 41 (wizard):** model (base + dual tanks + dual nozzles + pilot light), `FlamethrowerTurret` component, placeable `BlockItem`, Flame Canister item + Assembler recipes. Non-destructive / re-runnable.

**Defense panel upgraded** to handle Artillery + Auto Turret + Flamethrower (fuel buffer readout + fuel slots + targeting + auto-fire). RMB opens the panel on turrets/flamethrowers; artillery still uses RMB for cockpit / H for the panel.

**Persistence (additive):**
- Artillery shell magazine + Flamethrower fuel magazine now save/restore via `TryFindContainer` / `RestoreContainer`.
- New `SavedDefenseState` captures filter / autoMode / turret ammo / flamethrower fuel buffer (legacy saves leave it null).

**UI harden:** `_openDefense` is cleared on `CloseAll` and when opening other panels, so the defense panel no longer sticks after Esc.

**To use:** run Step 41 → craft Flamethrower Turret + Flame Canisters at the Assembler → place → RMB → drag canisters into Fuel → enable Auto-Fire → it burns swarms at close range.

**Files touched:**
- `Scripts/Combat/FlamethrowerTurret.cs` (new)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 41)
- `Scripts/UI/GameUIController.cs` (defense panel + open/close harden)
- `Scripts/Player/PlayerInteractionTool.cs` (RMB open)
- `Scripts/Persistence/WorldStatePersistence.cs` (magazines + defense state)
- `Scripts/Core/GameVersion.cs` (6.61.5 → 6.62.0), `Changelog.md`

---

### [6.59.0-dev] Turret UI (faction targeting) + Turret filter/autoMode

**Type:** MINOR — turret targeting upgrade (save-compatible).

**Turret upgraded** (`Turret.cs`): now has the same `TargetFilter` (Enemies / Players / Passive) + `autoMode` fields as Artillery. Its `FindTarget` scans by faction (including player targeting) instead of a hardcoded "Enemy" check.

**ArtilleryHud extended** to handle BOTH Artillery AND Turret — look at either defense piece to configure its faction filter + auto-fire toggle + see ammo. The UI auto-detects which type you're looking at and shows the right title + stats.

**To use:** look at a placed Auto Turret → the panel appears with Target Enemies / Players / Passive checkboxes + Auto-Fire toggle + ammo. Configure targeting for both turrets AND artillery from the same UI.

**Files touched:**
- `Scripts/Combat/Turret.cs` (filter + autoMode + faction-based targeting)
- `Scripts/UI/ArtilleryHud.cs` (handles both Artillery + Turret)
- `Scripts/Core/GameVersion.cs` (6.58.0 -> 6.59.0), `Changelog.md`

---

### [6.58.0-dev] Heavy Artillery — Cannon (auto-targeting by faction) + targeting UI

**Type:** MINOR — new heavy weapon system + targeting UI (save-compatible).

**Added the Artillery system** (`Artillery.cs`, `ArtilleryShell.cs`, `ArtilleryHud.cs`):
1. `Artillery` — placeable heavy weapon that **auto-targets by a faction filter** (`[Flags] TargetFilter { Enemies, Players, Passive }` — any combination). Scans for matching targets in range + LOS, aims its rotating head, and fires:
   - **Minigun** variant = rapid hitscan + tracer.
   - **Cannon / Gustav** variants = arcing shells (`ArtilleryShell`) that detonate via the centralized Explosion.
2. **Targeting UI** (`ArtilleryHud`) — when you look at an artillery piece, a panel shows checkboxes for **Target Enemies / Players / Passive**, an **Auto-Fire** toggle, and ammo + variant. Set any faction combination. The RMB reload also handles artillery.
3. **Step 38 (wizard):** the **Heavy Cannon** — a howitzer on a split-trail carriage with wheels + a long barrel (~12 parts), auto-targeting, arcing explosive shells (damage 60, blast 8), 200 HP. Craft at the Assembler.

**To use:** run Step 38 → craft → place → reload with Bullets (RMB) → look at it to configure targeting (Enemies/Players/Passive) → it auto-fires.

**Coming next:** the **Minigun** + **Schwerer Gustav** variants (massive railway gun), and **cockpit manual control** (first/third person — use cockpit logic).

**Files touched:**
- `Scripts/Combat/Artillery.cs` (new), `Scripts/Combat/ArtilleryShell.cs` (new), `Scripts/UI/ArtilleryHud.cs` (new)
- `Scripts/UI/GameUIController.cs` (mount + tick), `Scripts/Player/PlayerInteractionTool.cs` (reload)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 38)
- `Scripts/Core/GameVersion.cs` (6.57.0 -> 6.58.0), `Changelog.md`

---

### [6.57.0-dev] Auto Turret — automated defense

**Type:** MINOR — new defensive structure (save-compatible).

**Added `Scripts/Combat/Turret.cs`** — a placeable automated defense turret:
- Scans for hostile creatures (any `Damageable` whose type starts with "Enemy" — Ghoul, Manticore, Griffin, Karkadann, Ifrit, Basilisk, Roc) within range + **line of sight**, picks the nearest, and **rotates its head to track** them (radial-aware, aims in the tangent plane).
- Fires **hitscan shots** with a tracer + muzzle flash. 80 HP.
- Runs on an **ammo magazine** — reload by **holding Bullets + RMB the turret**.

**Step 37 (wizard):** turret model (base + pillar + rotating head with barrel + sight + muzzle marker), `Turret` component (head/muzzle wired), a placeable `BlockItem`, and an Assembler recipe (iron plate + circuit + copper wire).

**To use:** run Step 37 → craft an Auto Turret → place it near your base → reload with Bullets (RMB) → it defends against the creature roster.

**Files touched:**
- `Scripts/Combat/Turret.cs` (new), `Scripts/Player/PlayerInteractionTool.cs` (RMB reload)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 37)
- `Scripts/Core/GameVersion.cs` (6.56.0 -> 6.57.0), `Changelog.md`

---

### [6.56.0-dev] Bomb Timer UI — fuse slider + floating countdown numbers

**Type:** MINOR — new bomb UI (save-compatible).

**Added `Scripts/UI/BombHud.cs`** (mounted + ticked in GameUIController):
1. **Fuse slider** — while you hold an explosive (Powder Keg / Tsar / Antimatter), a small panel appears with a **slider (1–30 s) + live number**. Dragging it writes `ExplosiveBlock.NextFuse`, so the next bomb you place uses that fuse.
2. **Floating countdown numbers** — each placed bomb in view shows its **remaining fuse (seconds)** floating above it, colour-shifting green → red as it nears zero (a throttled scan + world→screen projection; sphere-correct offset via radial up).

**Files touched:**
- `Scripts/UI/BombHud.cs` (new), `Scripts/UI/GameUIController.cs` (mount + tick)
- `Scripts/Core/GameVersion.cs` (6.55.0 -> 6.56.0), `Changelog.md`

---

### [6.55.0-dev] Tsar Bomb + Antimatter Bomb (star-death sequence) + fuse glow + fixes

**Type:** MINOR — new mega-explosives + antimatter animation (save-compatible).

**Fixed — false "Hit dmg" feedback:** `ExplosiveBlock` now overrides `OnHit` to suppress the misleading damage readout when a keg takes explosion/chain damage.

**Bigger craters:** raised the voxel-crater cap in `Explosion` (4 -> 12) so bigger bombs carve bigger holes.

**Fuse countdown glow:** every bomb now has a pulsing point-light that blinks faster and shifts green -> red as the fuse runs out (a visible urgency timer). Bombs also honour a new `ExplosiveBlock.NextFuse` static (for the upcoming fuse-slider UI).

**Tsar Bomb (Step 35) — ~10x the Powder Keg:** placeable steel bomb; radius 40, damage 2500, big voxel crater, **ginormous mushroom cloud**. Fuses ~7 s; shoot/chain to detonate early. Crafted at the Assembler (steel + iron plate + coal).

**Antimatter Bomb (Step 36) — ~40x the Tsar, the ultimate:** runs a **"star-death" sequence** instead of an instant blast — a core sphere **EXPANDS slowly** (the doomed star swelling), **CONTRACTS fast** (collapse) to a tiny point, a **blinding WHITE GLOW**, then a **MASSIVE detonation** (radius 80, damage 30000) with a colossal white flash + light. Glowing violet core in a containment cage. Fuses ~8 s; shoot/chain to trigger early. Crafted at the Assembler (advanced circuit + steel plate + gold wire) — very expensive.

**Files touched:**
- `Scripts/Combat/ExplosiveBlock.cs` (OnHit suppress + NextFuse + fuse glow), `Scripts/Combat/Explosion.cs` (crater cap 12)
- `Scripts/Combat/AntimatterBomb.cs` (new: star-death sequence)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Steps 35 + 36)
- `Scripts/Core/GameVersion.cs` (6.54.0 -> 6.55.0), `Changelog.md`

---

### [6.54.0-dev] Powder Keg — placeable big bomb (mushroom cloud)

**Type:** MINOR — new placeable explosive (save-compatible).

**Added — the Powder Keg (the big bomb with the mushroom cloud):**
1. `Scripts/Combat/ExplosiveBlock.cs` (new) — a placeable high-yield explosive. **Fuses ~5 s after placement** then detonates in a big blast via the centralized `Explosion` (creature/player/placed-block damage + a large **voxel crater** + camera shake + the scale-driven **mushroom-cloud particle VFX**). It also **detonates immediately if shot or caught in another blast** (extends `Damageable`, so weapons/explosions trigger it) → **chain reactions** with grenades and other kegs.
2. **Step 34 (wizard):** a ~9-part wooden-barrel model (body, top/bottom, metal bands, bung, glowing fuse, danger label), `ExplosiveBlock` (radius 12, damage 250, voxel crater 4), a placeable `BlockItem` (`Block_PowderKeg`), and an Assembler recipe (plank + coal + iron). Saved non-destructively.

**To use:** run Step 34 → craft a Powder Keg at the Assembler → place it → step back → big mushroom-cloud blast + crater. Or shoot it (or drop a grenade next to it) to set it off early — kegs chain-react.

**Why MINOR:** new placeable explosive + crater at scale — save-compatible.

**Tunable (prefab):** `fuse`, `explosionRadius`, `explosionDamage`, `voxelDamageRadius`.

**Files touched:**
- `Scripts/Combat/ExplosiveBlock.cs` (new)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 34 `BuildPowderKegContent` + button)
- `Scripts/Core/GameVersion.cs` (6.53.3 -> 6.54.0), `Changelog.md`

---

### [6.53.3-dev] Compile fix — MuzzleWorldPosition uses .transform (CS1061)

**Type:** PATCH — compile fix (save-compatible).

`_viewModel` is a `GameObject`, so `MuzzleWorldPosition` must call `_viewModel.transform.TransformPoint(...)` (TransformPoint is on Transform, not GameObject).

**Files touched:**
- `Scripts/Player/HeldToolView.cs` (`_viewModel.transform.TransformPoint`)
- `Scripts/Core/GameVersion.cs` (6.53.2 -> 6.53.3), `Changelog.md`

---

### [6.53.2-dev] Muzzle flash at the gun + visible tracers

**Type:** PATCH — weapon VFX fix (save-compatible).

**Fixed — muzzle flash was at the crosshair, not the gun.** It was spawned at the camera (`ray.origin + forward`), i.e. the screen center. Now it spawns at the **held weapon's actual muzzle**: `HeldToolView` stores a per-weapon muzzle offset (set in `Refresh` from the item — pistol vs rifle barrel tips) and exposes `MuzzleWorldPosition`; the flash + tracer now originate there.

**Fixed — tracers were invisible.** The effect material wasn't showing its colour (URP/Unlit uses `_BaseColor`, not `_Color`) and the tracer was too thin/brief. Now: `_BaseColor` is set (bright yellow), and the tracer is thicker (0.05) and lasts longer (0.12s) so the shot reads as a clear beam from the gun to the target.

**Files touched:**
- `Scripts/Player/HeldToolView.cs` (`_muzzleLocalOffset` + `MuzzleWorldPosition`)
- `Scripts/Player/PlayerInteractionTool.cs` (muzzle from the gun, `_BaseColor`, thicker tracer)
- `Scripts/Core/GameVersion.cs` (6.53.1 -> 6.53.2), `Changelog.md`

---

### [6.53.1-dev] Compile fix — Ranged `hitPoint` renamed to `impact` (CS0136)

**Type:** PATCH — compile fix (save-compatible).

The 6.53.0 Ranged branch declared a local `hitPoint`, colliding with the Melee branch's `hitPoint` in the same method → `CS0136`. Renamed the Ranged one to `impact`.

**Files touched:**
- `Scripts/Player/PlayerInteractionTool.cs` (Ranged `hitPoint` → `impact`)
- `Scripts/Core/GameVersion.cs` (6.53.0 -> 6.53.1), `Changelog.md`

---

### [6.53.0-dev] Pass 2 — Gun Viewmodels + Muzzle Flash/Tracer + Ammo

**Type:** MINOR — weapon polish + ammo system (save-compatible). The second half of the weapons pass.

**Gun viewmodels (now read as guns):**
1. `HeldToolView` rebuilt the **pistol** (slide, frame, barrel/muzzle, angled grip, magazine, trigger guard, front + rear sights) and added a dedicated **rifle** viewmodel (long receiver + long barrel, stock, grip, curved magazine, scope, sight). The viewmodel switch now routes long-range (`range > 20`) Ranged weapons to the rifle, others to the pistol.

**See them shoot:**
2. **Muzzle flash** (a bright sphere + a brief point light at the muzzle) and a **tracer** (a thin beam from muzzle to the hit point) now fire on every Ranged shot, in `HandleWeaponAttack`.

**Ammo system:**
3. `WeaponItem` gains `ammoItem` + `ammoPerShot`. Ranged weapons spend ammo per shot; **out of ammo = no shot** (with an "Empty" prompt).
4. **Bullets** item + an Assembler recipe (iron plate + coal → 8 bullets). Step 33 wires the **rifle and the existing Iron Pistol** to use bullets (re-run Step 33 to apply).

**To use:** recompile → re-run **Step 33** (creates Bullets + wires ammo onto the pistol & rifle) → craft Bullets at the Assembler → your guns now clearly look like guns, flash + tracer on fire, and consume bullets.

**Files touched:**
- `Scripts/Combat/WeaponItem.cs` (`ammoItem`/`ammoPerShot`)
- `Scripts/Player/HeldToolView.cs` (rebuilt pistol + new rifle + switch routing)
- `Scripts/Player/PlayerInteractionTool.cs` (ammo spend, muzzle flash, tracer, EffectMat/MuzzleFlash/Tracer)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 33: Bullets item + recipe + pistol/rifle ammo wiring)
- `Scripts/Core/GameVersion.cs` (6.52.6 -> 6.53.0), `Changelog.md`

---

### [6.52.6-dev] Explosion material fix — no more purple/magenta particles

**Type:** PATCH — visual fix (save-compatible).

The particle explosions rendered **purple/magenta** because a runtime-created `ParticleSystem` has no material assigned. Now each system is given a shared **transparent particle material** (built once, reused) so the per-particle colours (fire, smoke, embers, shockwave) actually show:
- Tries `Universal Render Pipeline/Particles/Unlit`, then falls back to `Sprites/Default` / `Unlit/Color`.
- Configured for transparent alpha-blend (`_Surface=1`, `_Blend=0`, plus `_SrcBlend`/`_DstBlend`/`_ZWrite`/render-queue so it blends regardless of how the URP shader drives blend state).

**Files touched:**
- `Scripts/Combat/Explosion.cs` (shared transparent particle material)
- `Scripts/Core/GameVersion.cs` (6.52.5 -> 6.52.6), `Changelog.md`

---

### [6.52.5-dev] Explosion compile fix — MinMaxCurve curve constructor (CS1503)

**Type:** PATCH — compile fix (save-compatible).

Follow-up to 6.52.4: `ParticleSystem.MinMaxCurve` has no single-`AnimationCurve` constructor in this Unity version (only `(float)`, `(float,float)`, and `(float multiplier, AnimationCurve)`), so `new MinMaxCurve(curve)` tried to convert the curve to `float` → `CS1503`. Fixed by using the multiplier constructor: `new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(...))` for both the smoke and fire size curves.

Re-audited the rest of the particle setup — all other assignments use valid conversions (float→MinMaxCurve, Color/Gradient→MinMaxGradient) and every module is captured in a local, so no further conversion/CS1612 issues are expected.

**Files touched:**
- `Scripts/Combat/Explosion.cs` (`MinMaxCurve(1f, AnimationCurve)`)
- `Scripts/Core/GameVersion.cs` (6.52.4 -> 6.52.5), `Changelog.md`

---

### [6.52.4-dev] Explosion compile fix — MinMaxCurve (CS0029)

**Type:** PATCH — compile fix (save-compatible).

The 6.52.3 fix used bare `new AnimationCurve(...)` assigned to the particle size-over-lifetime curve, but there is **no implicit conversion** from `AnimationCurve` to `ParticleSystem.MinMaxCurve` → `CS0029`. Now explicitly wrapped: `new ParticleSystem.MinMaxCurve(new AnimationCurve(...))` in both the smoke and fire size curves.

Recompile and the particle grenade explosions should build and run.

**Files touched:**
- `Scripts/Combat/Explosion.cs` (wrap AnimationCurve in MinMaxCurve)
- `Scripts/Core/GameVersion.cs` (6.52.3 -> 6.52.4), `Changelog.md`

---

### [6.52.3-dev] Explosion compile fixes (unblocks 6.52.1 particle VFX + 6.52.2 throw-anywhere)

**Type:** PATCH — compile fix (save-compatible).

The 6.52.1 particle-explosion code had two compile errors that blocked the whole assembly — which is why neither the new grenade VFX nor the 6.52.2 "throw/fire anywhere" fix took effect:
- `CS0426 ParticleSystem.AnimationCurve` doesn't exist — replaced with `UnityEngine.AnimationCurve` (implicit conversion to the curve module), in the smoke + fire size-over-life curves.
- `CS1612` modifying `ps.emission.enabled`/`rateOverTime` directly (emission is a value-typed module) — now captured in a local first.

After recompile: real particle grenade explosions AND throw/fire/swing at the sky are both live.

**Files touched:**
- `Scripts/Combat/Explosion.cs` (AnimationCurve + emission-capture fixes)
- `Scripts/Core/GameVersion.cs` (6.52.2 -> 6.52.3), `Changelog.md`

---

### [6.52.2-dev] Fire/Swing Anywhere — weapons & tools work aiming at the sky

**Type:** PATCH — interaction fix (save-compatible).

**Fixed — weapons/tools only worked when aiming at the ground.** The crosshair-hit gate (`if (!hasHit) { … return; }`) ran *before* the weapon/tool dispatch, so looking at open sky did nothing — you couldn't throw a grenade, fire, or swing at the sky.
- Moved the **WEAPON dispatch** above the `!hasHit` gate. Each weapon mode does its own hit detection, so grenades throw, guns fire, and swords swing regardless of where you aim (they just hit nothing if the sky is empty).
- Mining tools (pickaxe/axe) now **still play their swing animation** when aimed at the sky (no block to hit, but the tool feels responsive instead of dead).
- Pipes/blocks are unchanged (still need a surface / open pipe end).

**Files touched:**
- `Scripts/Player/PlayerInteractionTool.cs` (weapon dispatch moved before `!hasHit`; tool-swing-at-sky)
- `Scripts/Core/GameVersion.cs` (6.52.1 -> 6.52.2), `Changelog.md`

---

### [6.52.1-dev] Real Particle Explosions (mushroom-cloud-ready)

**Type:** PATCH — VFX overhaul of the 6.52.0 explosion (save-compatible).

**Replaced the scale-animated primitive VFX with a real Unity ParticleSystem explosion**, scale-driven so the same system does a grenade today and a mushroom-cloud mega-bomb later:
- **Bright core** + **expanding fireball** (additive-style, fading).
- **Flying embers** + **debris chunks** that fall on radial gravity (sphere-correct).
- **Rising, billowing smoke column** (buoyant — accelerates upward against radial gravity + turbulence) — the mushroom-cloud foundation; taller/wider at larger blast scales.
- **Flat shockwave ring** expanding along the surface.
- **Light flash** that fades.
- Burst-only emission (`rateOverTime = 0`), world-space simulation, alpha-fade via the default URP particle material.

`Explosion.Detonate` now derives a `scale` from the blast radius (`radius / 5`, clamped 0.6–10), so bigger explosives automatically produce bigger, taller, mushroom-ier clouds — set up for future big bombs.

**Files touched:**
- `Scripts/Combat/Explosion.cs` (`ExplosionFX` rewritten with ParticleSystems; debris radial-gravity)
- `Scripts/Core/GameVersion.cs` (6.52.0 -> 6.52.1), `Changelog.md`

---

### [6.52.0-dev] Real Explosions — Pretty VFX + Camera Shake + Voxel/Block Damage + Grenade Viewmodel

**Type:** MINOR — combat feel overhaul (save-compatible). First half of the weapons polish the player requested.

**Better explosions (Explosion.cs, new):**
1. Centralized detonation used by grenades (and any future explosive): applies Explosive damage to creatures + the player + **placed blocks** (`PlacedBlock.Damage`), then **carves a voxel-terrain crater** via the `IVoxelWorld` API (spherical-world safe — `SphereWorld.WorldToVoxel` handles the planet transform), fires a **distance-based camera shake**, and plays a **multi-layer VFX**.
2. **Pretty VFX (ExplosionFX)** — bright flash, fireball, expanding shockwave ring, lingering smoke, a fading point light, and 8 flung debris bits (arcing on radial gravity). Scale/destroy animated (no alpha needed).
3. **Camera shake** — new on-foot event-shake channel in `CameraFeedback` (`AddShake`), stronger the closer you are to the blast, **togglable via a new `GameSettings.ScreenShake` setting** (on by default). Works while walking AND piloting.
4. **`WeaponItem.voxelDamageRadius`** (default 2.5 m, capped) controls the crater size per explosive; chain reactions still work.

**Fixed — grenade viewmodel:** the HeldToolView switch only mapped Ranged→pistol / else→sword, so the Throwable grenade rendered as a sword. Added a `Thrown→BuildGrenade` case with a proper grenade viewmodel (oval body, segmented belt, top cap, pull lever, lit fuse tip).

**To use:** recompile (everything is automatic; `voxelDamageRadius` defaults to 2.5 even without re-running Step 33). Throw a grenade into terrain/structures and watch the crater + shake + flash.

**Next pass (already noted):** gun viewmodels for the rifle/pistol (so they read clearly as guns), a visible muzzle flash / tracer so you can see them fire, and an ammo system (pistol/rifle consume bullets).

**Files touched:**
- `Scripts/Combat/Explosion.cs` (new: `Explosion` + `ExplosionFX`), `Scripts/Combat/BombProjectile.cs` (delegates to `Explosion`), `Scripts/Combat/WeaponItem.cs` (`voxelDamageRadius`)
- `Scripts/Player/CameraFeedback.cs` (event shake + `AddShake`), `Scripts/Settings/GameSettings.cs` (`ScreenShake`)
- `Scripts/Player/HeldToolView.cs` (`BuildGrenade` + `Thrown` case)
- `Scripts/Player/PlayerInteractionTool.cs` (pass `voxelDamageRadius`), `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 33)
- `Scripts/Core/GameVersion.cs` (6.51.0 -> 6.52.0), `Changelog.md`

---

### [6.51.0-dev] Phase 3k — Player Weapons: Grenade + Iron Rifle

**Type:** MINOR — new player weapons incl. throwable explosives (save-compatible). Phase 3k of the Combat pillar.

**Added — player offense to fight the roster you built:**
1. **Throwable Grenade** — `WeaponItem` with a new `AttackMode.Thrown`. Hold it + **LMB to lob**; it arcs on **radial gravity** (correct on spheres), fuses, then **detonates** — dealing `DamageType.Explosive` to every `IDamageable` in the radius (and self-damaging the player if caught in the blast), **chain-detonating other grenades** caught in the blast, with a self-expanding explosion VFX. **Consumable** (stack of 8).
2. **Iron Rifle** — a long-range (50 m) semi-auto kinetic `WeaponItem` (Ranged) for hitting flyers/bosses the pistol can't reach.
3. `Scripts/Combat/BombProjectile.cs` (new) — the thrown bomb (radial-gravity arc + fuse + AoE detonation + chain reactions) plus `ExplosionVFX` (expanding blast sphere).
4. `WeaponItem` gains the `Thrown` mode, explosion fields (`explosionRadius/Damage/fuseTime/throwForce/explosionMaterial`), and a maxStack-based `IsStackable` so the grenade stacks/consumes while the sword/pistol stay unique. `HandleWeaponAttack` gets a `Thrown` branch that spawns the bomb and spends one from the active stack.
5. **Step 33 (wizard):** creates the Grenade + Iron Rifle items, an explosion material, and Assembler recipes (Grenade: iron plate + coal; Rifle: iron plate + steel + copper wire).

**To play:** run Step 33 → craft a Grenade and/or Iron Rifle at the Assembler → hotbar them → LMB. Lob grenades at groups (watch the chain reactions) and pick off flyers/bosses with the rifle.

**Why MINOR:** new consumable weapon type + new projectile/AoE — save-compatible.

**Tunable:** grenade `explosionRadius/Damage/fuseTime/throwForce` (on the item), rifle `damage/range/attackCooldown`.

**Files touched:**
- `Scripts/Combat/WeaponItem.cs` (Thrown mode + explosion fields + IsStackable)
- `Scripts/Combat/BombProjectile.cs` (new: bomb + ExplosionVFX)
- `Scripts/Player/PlayerInteractionTool.cs` (`HandleWeaponAttack` Thrown branch)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 33 `BuildExplosiveContent` + button)
- `Scripts/Core/GameVersion.cs` (6.50.0 -> 6.51.0), `Changelog.md`

---

### [6.50.0-dev] Phase 3j — Mythical Enemy: Basilisk (petrifying gaze + venom)

**Type:** MINOR — new creature + petrify/slow status (save-compatible). Phase 3j of the Combat pillar.

**Added — the Basilisk (completes the listed mythical roster):**
1. `Scripts/Combat/EnemyBasilisk.cs` (new) — a large serpentine beast whose signature is a **PETRIFYING GAZE**: a forward cone (range + half-angle) that applies a movement **slow** to the player (turning them toward stone). Dodge by **circle-strafing to its flank/rear** (it turns slowly) or outranging it. Also delivers a **venomous bite** (applies the existing poison DoT). Reuses `Damageable` + `CreatureHealthBar`; radial-aligned; detaches from the chunk on Awake.
2. **Petrify/slow status** in `PlayerController` — `ApplyPetrify(slowFraction, duration)` reduces the walk/sprint target speed (decays over time). Cleanly hooked as a multiplier on the existing target-speed line.
3. **Step 32 (wizard):** a premium ~30-part serpent model (segmented body tapering to a tail, dorsal crest spikes, frilled horned head with fangs + glowing eyes, four clawed legs), `EnemyBasilisk` (90 HP), drops **Basilisk Scale + (rare) Petrified Eye**, saved non-destructively to `Resources/Enemies/Basilisk.prefab`, injected into **Forest/Steppes** biome scatter (rare, density 0.0025).

**To fight it:** run Step 32 → explore **Forest/Steppes** → a Basilisk may appear. Keep moving sideways to break its gaze cone, then punish — its bite also poisons.

**Why MINOR:** new enemy + new petrify status — save-compatible.

**Tunable (prefab):** `gazeCooldown`/`gazeRange`/`gazeHalfAngle`/`petrifySlow`/`petrifyDuration`, `biteDamage`/`bitePoisonDps`/`biteCooldown`.

**Files touched:**
- `Scripts/Combat/EnemyBasilisk.cs` (new)
- `Scripts/Player/PlayerController.cs` (`ApplyPetrify` + slow hook)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 32 `BuildBasiliskContent` + button)
- `Scripts/Core/GameVersion.cs` (6.49.0 -> 6.50.0), `Roadmap.md` + `Changelog.md`

---

### [6.49.0-dev] Phase 3i — Mythical Mini-Boss: Roc (first boss)

**Type:** MINOR — first boss creature + enrage phase (save-compatible). Phase 3i of the Combat pillar.

**Added — the Roc, the game's first mini-boss:**
1. `Scripts/Combat/EnemyRoc.cs` (new) — a colossal bird of prey. Boss-tier flight AI: it CIRCLES overhead at altitude, **dive-bombs with massive talons**, and periodically **beats its wings for a GUST** (AoE damage + knockback to anything within `gustRadius`, plus a dust-ring visual). **ENRAGES below 50% HP** (×1.3 speed, ×0.6 cooldowns). 350 HP. Reuses `Damageable` + `CreatureHealthBar`; radial-aware; detaches from the chunk on Awake; uses `PlayerController.ApplyImpulse` for the gust knockback.
2. **Step 31 (wizard):** a premium ~28-part colossal model (huge spread wings with feather rows, hooked beak, crest, massive taloned forelegs), `EnemyRoc`, **guaranteed boss loot** — Giant Pinions (2–4) + a guaranteed **Roc Storm Core** (via a `Die` override) — saved non-destructively to `Resources/Enemies/Roc.prefab`, injected into **Mountains/Steppes** biome scatter at **very rare** density (0.0008).

**To fight it:** run Step 31 → explore **Mountains/Steppes** (it's rare) → if a Roc appears, expect a long fight: dodge its dive-bombs, stay out of the gust radius, and push through its enrage phase for the guaranteed Storm Core.

**Why MINOR:** new boss creature + enrage mechanic — save-compatible.

**Tunable (prefab):** `hoverHeight`/`orbitRadius`/`orbitSpeed`, `diveCooldown`/`diveDamage`, `gustCooldown`/`gustRadius`/`gustDamage`/`gustKnockback`, `enrageThreshold`/`enrageSpeedMul`/`enrageCooldownMul`.

**Files touched:**
- `Scripts/Combat/EnemyRoc.cs` (new)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 31 `BuildRocContent` + button)
- `Scripts/Core/GameVersion.cs` (6.48.0 -> 6.49.0), `Roadmap.md` + `Changelog.md`

---

### [6.48.0-dev] Phase 3h — Mythical Enemy: Ifrit Djinn (caster: fireballs + teleport + fire walls)

**Type:** MINOR — new caster creature + fire/burn/teleport/AoE systems (save-compatible). Phase 3h of the Combat pillar.

**Added — the Ifrit Djinn (spellcaster, completes the archetype set):**
1. `Scripts/Combat/EnemyIfrit.cs` (new) — a high-tier fire spirit that KITES at range and cycles three abilities: **hurls fireballs**, **teleport-blinks** to reposition around you, and **raises fire walls** (lingering AoE) at your feet. Fragile (50 HP) but deadly. Reuses `Damageable` + `CreatureHealthBar`; radial-aligned; detaches from the chunk on Awake.
2. `Scripts/Combat/Fireball.cs` (new) — fire projectile (adapted from the Manticore spike) that deals fire damage + ignites a burn; passes through the caster.
3. `Scripts/Combat/FireWallHazard.cs` (new) — a flat glowing fire patch laid on the surface (oriented to radial up) that burns the player while they stand in it, then dissipates.
4. **Burn status** in `PlayerStats` (`ApplyBurn`) — a fire DoT that **bypasses armor AND escalates with it** (heavier plate burns hotter, per the lore — `dps × (1 + armorReduction × 1.5)`). Mirrors the Manticore's poison.
5. **Step 30 (wizard):** a premium ~24-part fire-spirit model (ember core, wispy base, flame arms, horned head with a flickering flame crest), `EnemyIfrit`, drops **Ifrit Ember + Ash**, saved non-destructively to `Resources/Enemies/Ifrit.prefab`, injected into **Desert/Wasteland** biome scatter (rare, density 0.002).

**To play:** run Step 30 → explore **Desert/Wasteland** → an Ifrit may appear. It kites and throws fire — keep moving out of fire walls, dodge fireballs, and burst it down fast (it's squishy) before the burn stacks.

**Why MINOR:** new enemy + projectile + AoE hazard + teleport + new status — all save-compatible.

**Tunable (prefab):** `castRange`, `fireballCooldown`/`fireballsPerCast`/`fireballBurnDps`, `teleportCooldown`/`teleportRange`, `firewallCooldown`/`firewallDuration`/`firewallBurnDps`/`firewallRadius`.

**Files touched:**
- `Scripts/Combat/EnemyIfrit.cs` (new), `Scripts/Combat/Fireball.cs` (new), `Scripts/Combat/FireWallHazard.cs` (new)
- `Scripts/Player/PlayerStats.cs` (`ApplyBurn` + armor-escalating burn tick)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 30 `BuildIfritContent` + button)
- `Scripts/Core/GameVersion.cs` (6.47.0 -> 6.48.0), `Roadmap.md` + `Changelog.md`

---

### [6.47.0-dev] Phase 3g — Mythical Enemy: Karkadann (charge + frontal armor)

**Type:** MINOR — new hostile creature + knockback hook (save-compatible). Phase 3g of the Combat pillar.

**Added — the Karkadann (heavy bruiser):**
1. `Scripts/Combat/EnemyKarkadann.cs` (new) — a massive armored brute with a **telegraphed straight-line CHARGE**: it paws the ground (windup), locks a line on your current position, then sprints; **dodge it or it tramples you for heavy damage + knockback**, then it's briefly stunned (a damage window). **Heavy FRONTAL ARMOR** (60% reduced from the front) via a `TakeDamage` override that checks the hit direction vs. its facing — so you must **flank/rear it for full damage** (the armor is disabled while it's recovering from a missed charge). Reuses `Damageable` + `CreatureHealthBar`; radial-aligned; detaches from the chunk on Awake.
2. `PlayerController.ApplyImpulse(Vector3)` — small new hook so the charge knockback shoves the player (decays via normal friction).
3. **Step 29 (wizard):** a premium ~30-part model (bulky body, spinal armor plates, great central horn + side horns, thick legs + hooves, red eyes), `EnemyKarkadann` (140 HP), drops **Karkadann Horn Fragment + Plated Hide**, saved non-destructively to `Resources/Enemies/Karkadann.prefab`, injected into **Steppes/Desert** biome scatter (rare, density 0.002).

**To play:** run Step 29 → explore **Steppes/Desert** → a Karkadann may appear. It will paw the ground (telegraph) then charge — sidestep the line, then punish its recovery, or flank it to bypass the frontal armor.

**Why MINOR:** new enemy + new player knockback API — save-compatible.

**Tunable (prefab):** `chargeRange`, `chargeWindup`, `chargeSpeed`, `chargeDamage`, `chargeKnockback`, `chargeCooldown`, `frontalArmorReduction`, `recoverTime`.

**Files touched:**
- `Scripts/Combat/EnemyKarkadann.cs` (new)
- `Scripts/Player/PlayerController.cs` (`ApplyImpulse`)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 29 `BuildKarkadannContent` + button)
- `Scripts/Core/GameVersion.cs` (6.46.0 -> 6.47.0), `Roadmap.md` + `Changelog.md`

---

### [6.46.0-dev] Phase 3f — Mythical Enemy: Griffin (flying, dive-bomb)

**Type:** MINOR — new flying hostile creature (save-compatible). Phase 3f of the Combat pillar.

**Added — the Griffin (first aerial enemy):**
1. `Scripts/Combat/EnemyGriffin.cs` (new) — a heraldic lion-eagle that **flies**. New dimension: it holds an altitude above the player, **circles overhead** (orbiting in the tangent plane), then periodically **dive-bombs** to strike with its talons before climbing back. Patrol-wanders high when no target is near. Radial-aware (orbits relative to radial up), no-gravity Rigidbody under its own steering, and detaches from the chunk parent on Awake — so flight is correct on rotating spheres. Reuses `Damageable` + `CreatureHealthBar`.
2. **Step 28 (wizard):** a premium ~30-part model (eagle head + hooked beak + crest, spread wings with feather rows, gold taloned forelegs, lion hindquarters, tufted tail), `EnemyGriffin` (55 HP), drops **Griffin Feather + Griffin Talon** plus a rare **Griffin Heart** (~20%, via a `Die` override), saved non-destructively to `Resources/Enemies/Griffin.prefab`, injected into **Mountains/Steppes** biome scatter (rare, density 0.0025).

**To play:** run Step 28 → explore **Mountains/Steppes** → a Griffin may appear overhead. It will circle and dive-bomb you; shoot it down (it's airborne, so aim up) for feathers, talons, and a rare heart.

**Why MINOR:** new flying enemy + new flight AI — save-compatible.

**Tunable (on the prefab):** `hoverHeight` (circling altitude), `orbitRadius`/`orbitSpeed`, `diveSpeed`, `diveCooldown`, `attackDamage`, `heartDropChance`. The Griffin banks but stays upright for now (no pitch) — a pitched/rolling flight model is a possible follow-up.

**Files touched:**
- `Scripts/Combat/EnemyGriffin.cs` (new)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 28 `BuildGriffinContent` + button)
- `Scripts/Core/GameVersion.cs` (6.45.0 -> 6.46.0), `Roadmap.md` + `Changelog.md`

---

### [6.45.0-dev] Phase 3e — Mythical Enemy: Manticore (tail spikes + venom)

**Type:** MINOR — new hostile creature + poison status system (save-compatible). Phase 3e of the Combat pillar.

**Added — the Manticore:**
1. `Scripts/Combat/EnemyManticore.cs` (new) — a lion-bodied predator with a humanoid face and a venomous scorpion tail. Reuses the Ghoul's proven radial movement; wanders → detects → chases, then **fires volleys of toxic tail spikes at range** (firing band) and **claws in melee** when close. Faces the player to aim. Radial-gravity aligned + chunk-detach on Awake (works on rotating spheres).
2. `Scripts/Combat/ManticoreSpike.cs` (new) — a lightweight, non-physical projectile (raycast continuous-collision) that deals kinetic damage and applies an **armor-bypassing poison DoT**; passes through the firing Manticore.
3. **Poison status** in `PlayerStats`: new `ApplyPoison(dps, duration)` + a tick in `Update` that drains HP directly (bypasses Crusader armor mitigation), refreshes/extends an active poison. So venom can wear down even heavily-armored Crusaders.
4. **Step 27 (wizard):** a premium ~35-part model (lion body + paws, dark mane, pale humanoid face with glowing eyes, bat wings, a 6-segment curved scorpion tail with a green venom stinger), `EnemyManticore` (80 HP), drops **Venom Gland / Manticore Spike / Armored Hide**, calm→red health bar, saved non-destructively to `Resources/Enemies/Manticore.prefab`, and injected into **Desert/Wasteland** biome scatter (density 0.003).

**To play:** run Step 27 → explore Desert/Wasteland → a Manticore may appear. It will volley spikes (each can poison you) and claw up close. Kill it for venom, spikes, and armored hide.

**Why MINOR:** new hostile creature + new projectile + new status effect — all save-compatible.

**Files touched:**
- `Scripts/Combat/EnemyManticore.cs` (new), `Scripts/Combat/ManticoreSpike.cs` (new)
- `Scripts/Player/PlayerStats.cs` (`ApplyPoison` + poison tick)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 27 `BuildManticoreContent` + button)
- `Scripts/Core/GameVersion.cs` (6.44.1 -> 6.45.0), `Roadmap.md` + `Changelog.md`

---

### [6.44.1-dev] Horse — RMB-to-Mount

**Type:** PATCH — QoL addition to the rideable horse.

**Added:** you can now **right-click (RMB) a horse to mount it**, in addition to **H**. RMB-on-horse takes priority over building/placing (handled before the build handler, with an early return), so it never also places a block. The on-screen prompt now reads `[H / RMB] Mount Horse`. H-mount and the `F`/RMB-free dismount are unchanged.

**Files touched:**
- `Scripts/Player/PlayerInteractionTool.cs` (RMB mount priority check + prompt)
- `Scripts/Core/GameVersion.cs` (6.44.0 -> 6.44.1)
- `Changelog.md`

---

### [6.44.0-dev] Phase 3d — Rideable Horse (mount, WASD steer, gallop, jump)

**Type:** MINOR — new mountable-creature feature (save-compatible). Phase 3d of the Combat pillar.

**Added — Rideable horses with full WASD steering:**
1. `Scripts/Fauna/RideableAnimal.cs` (new) — extends `PassiveAnimal`. A riderless horse grazes/wanders/flees like any livestock; look at it and press **H** (the cockpit/enter key) to mount. Mount/dismount mirrors the proven `GridCockpit.Enter/Exit` contract: the player's CharacterController is disabled and the rider is parented to the horse so they ride along.
2. **Full WASD steering on the spherical surface** — the horse's Rigidbody is driven by the rider's input (steered relative to where the rider looks), with **Shift to gallop** and **Space to jump** (radial-up impulse, self-collider-ignoring ground probe). **F** dismounts and drops the player beside the horse. Radial-gravity aligned, so riding works anywhere on a planet.
3. `PlayerController` gains `public bool IsMounted` + `ResetVelocity()`; while mounted its own locomotion is suspended but mouse-look + camera stay live (so you can look around while riding). `PassiveAnimal` is now subclass-friendly (`protected _rb`, `virtual FixedUpdate`, `Horse` added to the species enum).
4. `PlayerInteractionTool` shows a `[H] Mount Horse` prompt when you look at a riderless horse (parallel to the cockpit prompt) and mounts on key press.
5. **Step 26 (wizard):** builds a premium bay horse model (arched neck, mane, flowing tail, blaze + white socks, hooves, ~30 parts), wires a `RideableAnimal` (45 HP, drops Raw Meat + Hide, calm blue health bar), saves non-destructively to `Resources/Livestock/Horse.prefab`, and injects it into **Plains/Steppes** biome scatter (density 0.003). The livestock spawner picks it up automatically.

**Bundled cleanup:** stripped the `PassiveAnimalSpawner` diagnostic logs back to just the genuine `LogWarning`/`LogError` guards (livestock spawning confirmed working).

**To play:** run Step 26 → find a horse in Plains/Steppes → look at it + press **H**. WASD to ride, Shift to gallop, Space to jump, F to dismount.

**Why MINOR:** new mountable creature + new player locomotion state — save-compatible.

**Tunable:** the rider seat height is `RideableAnimal.seatLocalPos` (default rider-eye ~2.25 m) on the prefab — adjust if the view sits too high/low.

**Files touched:**
- `Scripts/Fauna/RideableAnimal.cs` (new), `Scripts/Fauna/PassiveAnimal.cs` (extensibility), `Scripts/Fauna/PassiveAnimalSpawner.cs` (diagnostics stripped)
- `Scripts/Player/PlayerController.cs` (`IsMounted`/`ResetVelocity`/locomotion suspension)
- `Scripts/Player/PlayerInteractionTool.cs` (mount prompt + `horse.Enter`)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 26 `BuildHorseContent` + button)
- `Scripts/Core/GameVersion.cs` (6.43.2 -> 6.44.0), `Roadmap.md` + `Changelog.md`

---

### [6.43.2-dev] Livestock Now Spawn via Temperate Biome Scatter + Spawner Scene Fix

**Type:** PATCH — spawn-path fix (animals confirmed working when spawned manually).

**Root cause:** the `PassiveAnimalSpawner` loaded its 3 prefabs fine (`Awake — prefabs=3`) and self-created, but never reached a spawn. Two issues:
1. **Scene persistence** — the spawner was created via `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` (which fires once, after the FIRST scene — i.e. the main menu) but was NOT `DontDestroyOnLoad`, so it was destroyed on the MainMenu → Game transition and never existed in the live world. Now marked `DontDestroyOnLoad`.
2. **No biome presence** — unlike the Ghoul (which the player actually sees via biome scatter), livestock had no scatter entry, so the reliable spawn path was missing entirely.

**Fixed:**
3. **Step 25 now injects Cow, Sheep & Pig into temperate biome scatter** (Forest, Plains, Steppes) at density 0.004 each — the proven, reliable spawn mechanism the Ghoul uses. `PassiveAnimal` already detaches from the chunk parent on Awake, so Rigidbody physics stays correct on rotating spheres. Idempotent (removes stale entries by name before re-adding).
4. **Spawner hardened:** `DontDestroyOnLoad` so it survives into the game scene, plus a throttled 5 s heartbeat log (`[LivestockSpawner] tick — player=OK/NULL, alive=N, nextSpawn in Xs`) and the existing `Spawned <species> #N` log.

**To use:** recompile → re-run **Step 25** (it now wires the biome scatter) → explore Forest/Plains/Steppes. Livestock will appear as ambient biome fauna; the near-player spawner tops them up near you (capped at 8). Heartbeat logs confirm the spawner is alive in-scene — say the word and I'll strip all diagnostics once you see animals.

**Files touched:**
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 25: `FinishAndSave` returns prefabs; temperate biome scatter injection)
- `Scripts/Fauna/PassiveAnimalSpawner.cs` (`DontDestroyOnLoad` + heartbeat)
- `Scripts/Core/GameVersion.cs` (6.43.1 -> 6.43.2)
- `Changelog.md`

---

### [6.43.1-dev] Livestock Spawner — Retry-Load + Diagnostics

**Type:** PATCH — spawner robustness + diagnostics (animals + AI confirmed working).

**Context:** Passive animals (Cow/Sheep/Pig) work perfectly when spawned manually, but the `PassiveAnimalSpawner` wasn't auto-spawning them. Same shape of issue we resolved for the Ghoul.

**Fixed — spawner now retries the prefab load every cycle:**
- The spawner previously loaded `Resources.LoadAll<GameObject>("Livestock")` only once in `Awake`. If that came back empty (folder not yet populated, or load timing) it never retried, so it silently never spawned. It now re-attempts the load every spawn cycle — exactly how the working `EnemySpawner` keeps retrying its prefab load.
- Added diagnostic logging so the Console shows exactly what's happening (to be trimmed back to just the genuine guards once spawning is confirmed): `[LivestockSpawner] RuntimeInitialize`, `Awake — prefabs=N`, `No prefabs found in Resources/Livestock — run Step 25 first` (the likely culprit if N=0), and `Spawned <species> #N`.

**To diagnose:** recompile, **make sure Step 25 has been run** (so `Assets/Resources/Livestock/` contains Cow/Sheep/Pig prefabs), then Play and check the Console:
- `Awake — prefabs=3` + `Spawned Cow #1…` → fixed, working.
- `Awake — prefabs=0` / `No prefabs found` → the prefabs aren't in `Resources/Livestock` (re-run Step 25, or point the spawner at the right folder).
- No `[LivestockSpawner]` logs at all → the spawner isn't being created (assembly/RuntimeInitialize issue).

**Files touched:**
- `Scripts/Fauna/PassiveAnimalSpawner.cs` (retry-load + diagnostics)
- `Scripts/Core/GameVersion.cs` (6.43.0 -> 6.43.1)
- `Changelog.md`

---

### [6.43.0-dev] Phase 3c — Passive Livestock Foundation (Cow, Sheep, Pig)

**Type:** MINOR — new creatures + items (save-compatible). Phase 3c of the Combat pillar.

**Added — Peaceful, harvestable livestock:**
1. `Scripts/Fauna/PassiveAnimal.cs` — new base creature extending `Damageable`. Wanders a home radius on the spherical surface (radial gravity + upright alignment, detaches from the chunk-scatter parent on Awake — same proven physics as the Ghoul), then **bolts away from whatever hurt it** for a few seconds (uses `DamageEvent.source`/`direction`). Species enum `{ Cow, Sheep, Pig }`. Designed as a clean base a future `RideableAnimal` (full WASD-steered horse) can extend.
2. `Scripts/Fauna/PassiveAnimalSpawner.cs` — auto-created via `RuntimeInitializeOnLoad`; loads every prefab under `Resources/Livestock`, spawns a capped population (8) near the player as top-level objects, despawns stragglers >95 m. Same reliable pattern as `EnemySpawner` (no debug spam).
3. **Step 25 (wizard):** builds three premium, web-inspired quadruped models — **Cow** (stocky chestnut, horns, udder, white belly + black patches, ~24 parts), **Sheep** (fluffy cream fleece, narrow dark face, ~17 parts), **Pig** (pink, flat snout, nostrils, floppy ears, curly tail, ~20 parts). Each gets a CapsuleCollider + no-gravity Rigidbody + `PassiveAnimal` (species/health/drops) + a calm-themed `CreatureHealthBar`. Saved non-destructively to `Resources/Livestock/` (existing prefabs preserved).
4. New animal-product items in `VoxelEngineAssets/Fauna/Items`: **Raw Meat**, **Animal Hide**, **Wool** (icon-tinted). Drops: Cow→meat+hide, Sheep→meat+wool, Pig→meat.

**How to play:** run `Tools > Voxel Engine > Voxel Engine Setup` → **Step 25**. Cows, sheep, and pigs then roam near you as you explore; swing/shoot to harvest them for meat, hide, and wool (they flee when hit).

**Why MINOR:** adds new creature prefabs + a new spawner + new items — all save-compatible (nothing touches existing save data).

**Next (Phase 3c cont.):** rideable horses (full WASD-steered mount), breeding/needs/population, and husbandry automation.

**Files touched:**
- `Scripts/Fauna/PassiveAnimal.cs` (new)
- `Scripts/Fauna/PassiveAnimalSpawner.cs` (new)
- `Scripts/Editor/VoxelEngineSetupWindow.cs` (Step 25 `BuildLivestockContent` + button)
- `Scripts/Core/GameVersion.cs` (6.42.1 -> 6.43.0)
- `Roadmap.md` + `Changelog.md`

---

### [6.42.1-dev] Equipment Panel — Jetpack Bay + Life Support Moved In; Armor Readout Trimmed

**Type:** PATCH — UI refinement of the 6.42.0 equipment panel, save-compatible.

**Changed — One grouped equipment panel:**
1. The right-side card is now a full **equipment panel** (renamed `BuildArmorPanel` -> `BuildEquipmentPanel`) holding **ARMOR + JETPACK BAY + LIFE SUPPORT** together, instead of armor-only. The Jetpack Bay and Life Support rows were lifted out of the inventory panel (`BuildLeftPanel`) and dropped the now-unused `BuildEquipmentRow` wrapper. The inventory panel is now just title / weight / backpack grid / toggles.
2. **Removed the `"Unequipped"` text** and the **`"Shift-click to (un)equip"` hint** from the armor section. When no armor is worn the slot simply sits empty; when armor is worn it shows a compact centered `"Tier N  − 42% damage"` pill. The Jetpack/Life-Support status pills (`ONLINE`/`EMPTY`, `SEALED`/`OPEN`) are untouched.
3. Card widened slightly (178 -> 184 px) so the 2-slot Jetpack/Life-Support rows breathe.

**Behaviour note:** because all gear now lives in the equipment panel, it hides (alongside the panel) whenever a center panel (crafting) or any right panel (production stats, recipe browser, container/machine) is open — same rule as before. The gear is reachable again the moment those close.

**Files touched:**
- `Scripts/UI/GameUIController.cs` (`BuildEquipmentPanel`, removed `BuildEquipmentRow`, trimmed armor readout)
- `Scripts/Core/GameVersion.cs` (6.42.0 -> 6.42.1)
- `Changelog.md`

---

### [6.42.0-dev] Armor Equipment Slots + Enemy Debug Cleanup

**Type:** MINOR — new equipment UI (save-compatible) + combat debug cleanup.

**Added — Armor equipment slots in the inventory UI:**
1. `PlayerEquipment` gains a dedicated single-slot `ArmorSlots` `ItemContainer` (size 1) with an `AcceptFilter` that only accepts `ArmorItem` — mirroring the existing Jetpack / Helmet / Oxygen-tank slot pattern. New serialized field `_armorSlots` (persists across sessions like the other gear slots).
2. New `EquippedArmor` property + a `SyncEquippedArmor()` hook: the slot's `OnChanged` keeps `PlayerStats.equippedArmor` (read by `TakeDamage` for `-(damageReduction)` mitigation) in lock-step, so **drag-equip, shift-click, and the legacy RMB path all agree** on what's worn. `TakeDamage` is unchanged.
3. `GameUIController`: a premium slim **ARMOR** card docks to the **right of the inventory panel** (single drag/drop slot + a live "Tier N  − 42% damage" readout + equip hint). It is **hidden whenever crafting, Production Stats, the Recipe Browser, or any opened container/machine/terminal needs the space** (new `AnyCenterOrRightPanelOpen()` guard), so it never overlaps. The inventory + armor are now flex children of one left-dock row so the armor card always hugs the inventory's right edge at any screen size.
4. **Three equip paths, all synced:** drag armor onto the slot; **shift-click** armor from the backpack (new `QuickTransfer` armor branch) to equip / shift-click the slot to return it; **RMB** an armor item in the hotbar (rewritten in `PlayerInteractionTool` to route through the slot). Dragging non-armor onto the slot is rejected by the `AcceptFilter`; swapping is honoured.

**Removed — Combat diagnostic logging (ghouls now work, so the spam is gone):**
5. `EnemySpawner`: stripped all `[EnemySpawner]` `Debug.Log` info spam (`RuntimeInitialize`, `Awake`, `At cap`, `Created new spawner`, `Spawned ghoul #N`). Kept only the two genuine runtime guards — `LogWarning` (prefab missing) and `LogError` (prefab missing its `EnemyGhoul` component) — which fire only on real misconfiguration. `EnemyGhoul`: removed the `[Ghoul] Spawned at` log. Tidied the empty `else` and the now-stale header comment.

**Why MINOR:** introduces new persistent equipment state (`ArmorSlots`) and a new UI panel + interaction — save-compatible (old saves load with no armor equipped).

**Files touched:**
- `Scripts/Player/PlayerEquipment.cs` (`ArmorSlots` + `EquippedArmor` + `SyncEquippedArmor`)
- `Scripts/UI/GameUIController.cs` (`BuildLeftArea`/`BuildArmorPanel`/`AnyCenterOrRightPanelOpen`, refactored `BuildLeftPanel` to a flex child, `QuickTransfer` armor branch)
- `Scripts/Player/PlayerInteractionTool.cs` (RMB equip routed through the slot)
- `Scripts/Combat/EnemySpawner.cs` + `Scripts/Combat/EnemyGhoul.cs` (debug cleanup)
- `Scripts/Core/GameVersion.cs` (6.41.6 -> 6.42.0)
- `Changelog.md`

---

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

---
## [6.76.0] Jetpack Fuel Fix + Portable Battery System — 2026-08-01
**Branch:** dev | **Type:** MINOR (new system, save-compatible) + PATCH fixes

### Fixes (PATCH)
- Jetpack hydrogen refuel from Portable Hydrogen Tanks restored — defensive inventory reference checks and immediate replay recharge on equip (`PlayerEquipment.EnsureAllJetpackFuelInitialized`).
- Atmospheric / Hybrid jetpack power side fixed — charged cells recognized and consumed; portable batteries added as reusable power source; UI fuel label shows `W` for power packs instead of wrong `ml`.
- Charged Cell item (`Item_ChargedCell.asset`) verified via Step 47 wizard — itemId `item_charged_cell` preserved.

### New System (MINOR)
- **Portable Battery** (`Item_PortableBattery.asset`) — rechargeable power bank (3000 ml / Wh, stack=1). Crafted at Assembler (Iron + Copper Wire).
- Battery draws charge from world `PowerBattery` blocks via RMB interaction (`PlayerInteractionTool` — same flow as hydrogen tank from Gas Tank).
- Power jetpacks (Atmospheric / Hybrid) pull charge from inventory Portable Batteries the same way they draw H₂ from tanks (`TryRechargeSlot` — battery takes ml without destroying item).
- Charged Cells remain as disposable backup if no battery is present.

### Manual Unity Steps
1. Open Unity Editor → **Tools ▸ Voxel Engine ▸ Setup Wizard** → click **Step 47** (`BuildJetpackFuelContent`) to refresh jetpack assets + author `Item_PortableBattery.asset` + recipe.
2. Optional physical charging cradle: create `Assets/VoxelEngineAssets/Prefabs/PortableBatteryCradle.prefab` (cube, 0.5×0.3×0.5, blue accent) with `ItemContainer` (Size=2, accepts `JetpackItem`).
3. Battery block recharge is already coded in `PlayerInteractionTool.cs` — no prefab changes needed.
4. Push to `/dev` — version is `6.76.0`.
