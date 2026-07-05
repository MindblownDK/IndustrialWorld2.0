# Water & Crest Bridge — Changelog

Branch: **Dev** · Semantic Versioning 2.0.0

---

## [3.23.1] — Actually Kill Ocean Plane + Premium Water Look

**Type:** PATCH — Save-compatible. Fixes leftover Crest plane and retunes
water visuals so they look great instead of like the pre-Crest starting point.

### Fixed
- **Infinite Crest ocean plane still visible.** `_hideOceanTileGameObjects`
  only hides tile GameObjects in the hierarchy view — the tiles still
  render. The fix is to disable the `OceanRenderer` component itself, which
  tears down all its ocean tiles + LOD dispatch on `OnDisable`. Setup wizard
  now sets `behaviour.enabled = false` AND `gameObject.SetActive(false)` on
  Crest Ocean. Runtime `CrestVoxelWaterBinder.HideCrestOceanTiles` now also
  scans the entire scene for `Crest.OceanRenderer` + `Crest.OceanChunkRenderer`
  and disables them (belt and braces — if anything re-enables Crest at
  runtime it gets killed the next binder tick).
- **Water looked flat / muddy on flat worlds.** The shader was using
  `radialUp = normalize(worldPos)` unconditionally, treating flat worlds as
  planet worlds with origin at 0,0,0 — this made the wave frame and
  Fresnel/lighting vectors point away from world origin instead of straight
  up. Fixed with a new `_VoxelWaterWorldUp` global (xyz = up, w = isPlanet
  flag) pushed by `CrestVoxelWaterBinder.PushWorldUpGlobal()` and a safety
  default in `WaterMeshBuilder.EnsureMats()`.
- **`_PlanetWaveBlend` was hardcoded 1.0** even on flat worlds, mixing in
  planet radial waves that pointed the wrong way. Setup wizard now
  auto-detects `SphereWorld` presence and sets it to 1 for planets, 0 for
  flat worlds.

### Added
- **Global shader input** `_VoxelWaterWorldUp` (xyz = world-up direction,
  w = 1.0 for planets, 0.0 for flat) sampled by both vertex and fragment
  stages of `VoxelWaterURP`. Fixes wave orientation and lighting on flat
  worlds.
- Premium material tuning (v3.23.1 defaults):
  - Shallow color → clean Caribbean turquoise `(0.16, 0.78, 0.86)`
  - Deep color → deep navy-teal `(0.02, 0.14, 0.30)`
  - Larger, slower deep waves; faster shallow ripple
  - Longer `_DepthFade` (2.5 → 4.0) so shallow water reads as clearer
  - Stronger shore foam (`_ShoreFoamIntensity` 1.2 → 1.4)
  - Stronger subsurface glow (`_SSSIntensity` 0.38 → 0.45)
  - Fresnel bumped 3.2 → 4.0 for a richer deep-water sheen

### Changed
- `CrestWaterSetupUtility.EnsureCrestOceanRendererInScene(...)` now leaves
  the OceanRenderer GO **disabled**. Kept in the scene so a future feature
  can re-enable it, but zero rendering cost until then.
- Setup dialog now reports the detected world type (PLANET / FLAT).
- `GameVersion` bumped to `3.23.1-dev`.

### Shader
- `Scripts/Rendering/VoxelWaterURP.shader` — vertex + fragment both consume
  `_VoxelWaterWorldUp` to derive the tangent frame. When the global is
  unset (all zeros), safely defaults to `(0,1,0)`.

### Manual Unity steps
1. Pull, let Unity recompile (shaders will auto-reimport).
2. Menu: **Tools → Voxel Engine → Configure Crest Water Integration**.
   Dialog now reads *"Voxel Water Authoritative + Premium Look configured"*
   and reports the auto-detected world type.
3. Confirm in the Hierarchy: the **Crest Ocean** GameObject is **disabled**
   (grey icon, unchecked). If it's still active, uncheck it manually or
   re-run the wizard.
4. Press Play. Expected:
   - No blue rectangle anywhere in the scene.
   - Voxel water shows clear turquoise near shore, deep navy in open water,
     rolling waves, shore foam where terrain meets water.
   - Console clean.
5. Tweak from `Assets/Resources/CrestOcean_VoxelBridge.mat` if you want
   different color/wave/foam values — the material is inspector-editable.

### Deferred
- Wake foam around grid hulls; swimming + underwater fog; world-gen lag.

---

## [3.23.0] — Voxel Water Authoritative (Kill Ocean Plane) *(superseded)*

**Type:** MINOR — Design pivot back from 3.22.0. Voxel water renders as the
one and only ocean visual using the stylized VoxelEngine/VoxelWaterURP shader
(waves, foam, flow, fresnel). Crest's infinite ocean plane is hidden per user
request. Save-compatible.

### Fixed
- **Voxel water was invisible after 3.22.0.** Two components in the scene were
  force-setting `WaterMeshBuilder.RenderingEnabled = false` at scene start:
    - `FluidPerformanceBootstrap` (Awake / OnEnable / `Apply()`)
    - `CrestOilOceanController` (OnEnable)
  Both are now fixed. `FluidPerformanceBootstrap` gained a
  `forceWaterMeshRendering` toggle (default `true`) that keeps voxel water on.
  `CrestOilOceanController` no longer touches `RenderingEnabled`.
- **Infinite Crest ocean plane still spawning.** Root cause: `SetBool(so,
  "_hideOceanTileGameObjects", false)` in v3.22.0 setup and matching
  `hideCrestOceanTiles = false` on the runtime binder. Both defaults reversed
  in v3.23.0. OceanRenderer stays alive (for future hooks) but its tile GOs
  are hidden.
- **Sea-level skip was hiding voxel water everywhere.** v3.22.0 introduced
  `SkipVoxelWaterAtOrBelowSeaLevel = true` to defer to Crest at sea level.
  Since Crest is now hidden, the skip default is `false` in v3.23.0 — voxel
  water renders everywhere.
- **Crest startup warnings** ("Foam is not enabled on the ocean material",
  "Flow is not enabled...", etc.) fully silenced. Setup wizard now disables
  every Crest subsystem AND zeroes every material feature keyword since
  OceanRenderer is silent.

### Added
- `FluidPerformanceBootstrap.forceWaterMeshRendering` (bool, default `true`).
- `Assets/Resources/CrestOcean_Hidden.mat` — Crest material used by
  OceanRenderer (invisible). Split from `CrestOcean_VoxelBridge.mat` so the
  two roles no longer race over the same asset.
- Setup wizard now scans and fixes any existing
  `FluidPerformanceBootstrap` in the scene (sets `forceWaterMeshRendering =
  true`, `enableCrestMode = false`).

### Changed
- `CrestVoxelWaterBinder.hideCrestOceanTiles` default: `false` → `true`.
- `WaterMeshBuilder.SkipVoxelWaterAtOrBelowSeaLevel` default: `true` → `false`.
- `CrestWaterSetupUtility.ConfigureSerializedCrestOcean(...)`:
  - `_hideOceanTileGameObjects` = **true** (was false)
  - `_createFoamSim` = **false** (was true)
  - `_createSeaFloorDepthData` = **false** (was true)
  - `_lodDataResolution` = 128 (was 384) — silent LOD driver, save GPU
  - `_geometryDownSampleFactor` = 4 (was 2)
  - `_lodCount` = 4 (was 7)
  - `_heightQueries` = false
- `CrestWaterSetupUtility.ConfigureCrestVoxelMaterialBridge(...)` now creates
  a fresh voxel material (`CreateOrUpdateVoxelWaterVisualMaterial()`) for the
  water MESH override instead of pushing the Crest material there (Crest's
  shader is topologically incompatible with our voxel heightfields).
- `LoadOrCopyCrestOceanMaterial()` writes to `CrestOcean_Hidden.mat` instead
  of `CrestOcean_VoxelBridge.mat` — no more asset-write race.
- `AlignCrestMaterialKeywords(...)` now passes ALL features off (matching the
  fact that every Crest subsystem is disabled).
- `GameVersion` bumped to `3.23.0-dev`.

### Manual Unity steps
1. Pull the branch, let Unity recompile.
2. Menu: **Tools → Voxel Engine → Configure Crest Water Integration**.
   Dialog now reads *"Voxel Water Authoritative configured — no infinite
   Crest plane."*
3. In the Hierarchy find **`Crest Ocean`**. Confirm in Inspector:
   - `Hide Ocean Tile Game Objects` = **on**
   - `Create Foam Sim`, `Create Flow Sim`, `Create Sea Floor Depth Data`,
     `Create Dynamic Wave Sim`, `Create Shadow Data` = **all off**
4. Find any `Fluid Performance Bootstrap` component in the scene (usually on
   `Liquid Visual Runtime` or the world root). Confirm:
   - `Force Water Mesh Rendering` = **on**
   - `Enable Crest Mode` = **off**
5. Press Play. You should see:
   - No infinite blue plane anywhere.
   - Voxel water surface visible at sea level everywhere (with waves, foam,
     shore blend from the VoxelWaterURP shader).
   - Clean console — no Crest "not enabled on material" warnings.
6. If water is still invisible: open **Window → Analysis → Frame Debugger**
   during Play, look for a draw call named "LiquidSurface". If it's absent,
   `WaterMeshBuilder.RenderingEnabled` is still being flipped off by
   something. Search the project for `RenderingEnabled = false` and ping me.

### Deferred
- Wake foam around grid hulls (Crest-driven or texture-driven).
- Swimming + underwater fog volume.
- World-gen lag pass — still on deck as `[3.23.1]` or `[3.24.0]`.

---

## [3.22.0] — Hybrid Crest Ocean + Voxel Lakes *(superseded by 3.23.0)*

**Type:** MINOR — Save-compatible visual pivot. Crest tiles are now visible
and *are* the ocean. Voxel water is used only for inland lakes above sea
level. No save-file impact.

### Why the pivot from [3.12.0]
The v3.12.0 attempt painted `Crest/Ocean` onto voxel water chunk meshes.
Crest's shader has a hard-coded vertex-snap step in `OceanVertHelpers.hlsl::SnapAndTransitionVertLayout()`
that only makes sense for Crest's own concentric regular-grid tile mesh —
it collapsed our voxel heightfield topology into dark, patchy triangles
(the untextured jagged water you saw in the last screenshot).

We now embrace Crest's own tiles as the ocean visual (which they render
beautifully) and restrict voxel water rendering to *above* sea level.

### Fixed
- **Crest ocean tiles are visible again.** The setup wizard was setting
  `_hideOceanTileGameObjects = true` on OceanRenderer and the runtime binder
  had `hideCrestOceanTiles = true`. Both now default to `false`. Crest's
  tiles render the ocean with proper Gerstner waves, foam, reflection.
- **"Flow is not enabled on the ocean material" warning.** Setup wizard
  was force-enabling `_createFlowSim` on OceanRenderer while the material
  had `_Flow` off. Now the wizard sets `_createFlowSim = false` (we don't
  supply any flow inputs) and aligns every material feature toggle to its
  matching subsystem: **Foam ON, Flow OFF, Shadows OFF, ClipSurface OFF,
  Albedo OFF, DynamicWaves OFF**. No more startup warnings.
- **Voxel water no longer z-fights with Crest at sea level.** New
  `WaterMeshBuilder.SkipVoxelWaterAtOrBelowSeaLevel` (default `true`)
  skips any water column whose surface Y ≤ `world.SeaLevel + bias` during
  meshing, so Crest owns the visual there.

### Added
- `WaterMeshBuilder.SkipVoxelWaterAtOrBelowSeaLevel` (bool, default `true`).
- `WaterMeshBuilder.SeaLevelSkipBiasVoxels` (float, default `-0.25`) –
  cells within a quarter-voxel *below* sea level are still dropped; anything
  clearly above the water line renders normally so lakes look correct.
- `CrestWaterSetupUtility.AlignCrestMaterialKeywords(...)` – single source
  of truth for `_Foam/_FOAM_ON`, `_Flow/_FLOW_ON`, `_Shadows/_SHADOWS_ON`,
  `_ClipSurface/_CLIPSURFACE_ON`, `_Albedo/_ALBEDO_ON` on the bridge material.

### Changed
- `CrestVoxelWaterBinder.hideCrestOceanTiles` default: `true` → `false`.
- `CrestVoxelWaterBinder.bridgeCrestMaterialToVoxelMesh` default: `true` →
  `false`. Field kept for scene-serialization compatibility but no longer
  drives any behaviour.
- `CrestVoxelWaterBinder.TryBridgeMaterialToVoxel()` – now a documented
  no-op. It used to swap Crest's ocean material onto the voxel mesh
  (which broke topology).
- `WaterMeshBuilder.IsVoxelWaterCompatible()` – reverted; no longer accepts
  `Crest/*` shaders. `IsCurrentMaterialCrest()` always returns `false`.
- `CrestWaterSetupUtility.ConfigureSerializedCrestOcean(...)`:
  `_createFlowSim = false`, `_createDynamicWaveSim = false`,
  `_createShadowData = false`, `_hideOceanTileGameObjects = false`,
  `_geometryDownSampleFactor = 2` (was 4 – bumped fidelity of the visible
  tiles now that they're the star).
- `GameVersion.cs` bumped to `3.22.0-dev`.

### Deprecated
- `VoxelCrestChunkBinder` – kept as a self-destructing empty stub so scenes
  serialized with v3.12.0 don't lose their script reference on load. The
  next chunk rebuild removes any live instance.

### Manual Unity steps
1. Pull the branch and let Unity recompile.
2. Menu: **Tools → Voxel Engine → Configure Crest Water Integration**.
   Dialog should read *"Hybrid Crest Ocean + Voxel Lakes configured"*.
3. In the scene, select the **Crest Ocean** GameObject and confirm:
   - `_material` = `Assets/Resources/CrestOcean_VoxelBridge.mat`
   - `_hideOceanTileGameObjects` = **off** (visible ocean)
   - `_createFlowSim` = **off**
   - `_createFoamSim` = **on**
   - `_createSeaFloorDepthData` = **on**
4. Select the `CrestOcean_VoxelBridge` material in the Project window and
   confirm `Foam → Enable` is **on** and `Flow → Enable` is **off**.
5. Press Play. Fly over ocean — Crest's textured, wavy, foamy surface
   should be the ocean everywhere at sea level. Fly onto a mountain lake
   above sea level — voxel water still renders there with the stylized
   shader. No warning spam in the console.
6. If you still see a big dark polygon where the ocean should be: your
   scene's `Crest Ocean` GameObject was manually disabled. Enable it, or
   just re-run the wizard.

### Not in this patch (still deferred by request)
- Swimming, underwater fog volume, boat wake foam around the grid, world-gen
  lag pass. On deck for `[3.23.0]` and `[3.22.1]`.

---

## [3.12.0] — Genuine Crest Ocean Bridge *(superseded by 3.22.0)*

**Type:** MINOR — Save-compatible visual overhaul. Voxel water now renders
through Crest's real `Crest/Ocean` shader with LOD cascades intact.

### Root cause of the "flat dark-blue polygon" water
`CrestVoxelWaterBinder` was **destroying `Crest.OceanRenderer`** at startup,
which starved the ocean shader of its LOD cascade textures (animated waves,
foam, flow, shadow, sea-floor depth). The custom bridge material then had
nothing to sample, so voxel water rendered as a flat colour with no waves.

### Fixed
- **Never destroy `Crest.OceanRenderer`.** It's the LOD cascade driver and
  must stay alive to feed the shader. The binder now only *hides* Crest's
  own infinite ocean tiles (`Renderer.enabled = false`) so the visible
  ocean remains 100% our procedural voxel mesh.
- Sea level is now aligned by moving `OceanRenderer.transform.y` to
  `world.SeaLevel * VOXEL_SIZE`, matching every LOD sampler to our voxel
  water plane. Previously we wrote the read-only `SeaLevel` property and
  silently no-op'd.
- Editor setup no longer nukes Crest twice per run.

### Added
- **`VoxelCrestChunkBinder`** — new per-chunk component that pushes
  `_LD_SliceIndex`, `_MeshScaleLerp`, `_GeometryData`, `_ChunkGeometryData`
  and `_ReflectionTex` through a `MaterialPropertyBlock`, emulating what
  `Crest.OceanChunkRenderer` does for Crest's own tiles. Without this
  MPB, the Crest shader has no valid LOD binding and renders flat.
- `WaterMeshBuilder.EnsureCrestBinder(...)` attaches the binder to every
  live water chunk whenever the active material is a Crest shader, and
  removes it when we swap back to the stylized fallback.
- `WaterMeshBuilder.SetMaterialOverrides(...)` now re-scans live water
  surface GameObjects and updates both material and binder, so
  swapping to/from Crest is applied without a chunk rebuild.
- `WaterMeshBuilder.IsCurrentMaterialCrest()` public helper.
- `WaterMeshBuilder.IsVoxelWaterCompatible` now accepts `Crest/*` shaders.

### Changed
- `CrestVoxelWaterBinder.disableCrestOceanPlane` → renamed to
  `hideCrestOceanTiles` (same intent, correct behaviour). Anywhere that
  set the old field has been updated (Editor tools).
- `CrestVoxelWaterBinder.SetCrestVisualActive` now keeps OceanRenderer
  enabled unconditionally and just re-applies the tile-hiding pass.
- `CrestWaterSetupUtility.ConfigureCrestWaterMaterial` now loads
  `Assets/Liquid/Crest/Crest/Materials/Ocean.mat`, copies it into
  `Assets/Resources/CrestOcean_VoxelBridge.mat`, and hands the copy to
  `WaterMeshBuilder`. If Crest's material is missing it falls back to the
  stylized voxel shader as before.
- `CrestWaterSetupUtility.Configure()` now calls
  `EnsureCrestOceanRendererInScene(...)` instead of `SafeNukeAllCrest()`
  around the ocean setup — an `OceanRenderer` is created if none exists
  and its material is set to the bridge material. Sea-level Y is aligned
  to `flatSeaLevel * VOXEL_SIZE`.
- Setup dialog updated to reflect the new "genuine Crest bridge" flow.

### Manual Unity steps
1. Pull the branch, let Unity reimport / recompile (no errors expected;
   ignore the auto-migration warning about `disableCrestOceanPlane` if it
   appears — it's the field rename).
2. Menu: **Tools → Voxel Engine → Configure Crest Water Integration**.
   The dialog should read *"Genuine Crest bridge configured"* with
   *"OceanRenderer KEPT ALIVE"* in the bullet list.
3. Open the `Game` scene. Confirm there is exactly one GameObject named
   **`Crest Ocean`** carrying `OceanRenderer`. Its Y position should
   equal `flatSeaLevel * 1` (defaults to 96 units).
4. Press Play. Fly the player to a body of water. You should see:
   - Rolling Gerstner waves (default `_globalWindSpeed = 150 km/h` on
     the OceanRenderer — tune it down in inspector for calmer seas).
   - Shore foam where water meets terrain.
   - Deeper cyan/blue gradient with distance.
5. If the water is still flat: select the **`Crest Ocean`** GameObject and
   drag `Assets/Resources/CrestOcean_VoxelBridge.mat` into its
   `_material` slot. Also confirm `Assets/Liquid/Crest/Crest/Materials/Ocean.mat`
   exists — the setup tool copies it as the source.
6. If Unity throws *"NullReferenceException in VoxelCrestChunkBinder"*:
   the Crest package assembly may not be named `Crest`. Check
   `Assets/Liquid/Crest/Crest/Scripts/Crest.asmdef → name`. If it differs,
   ping me and I'll widen the reflection lookup.

### Not in this patch (deferred by request)
- Swimming, underwater fog volume, boat wake foam around the grid system,
  world-gen lag pass. Coming next in `[3.12.1]` / `[3.13.0]`.

---
