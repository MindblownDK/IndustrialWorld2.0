# Water & Crest Bridge — Changelog

Branch: **Dev** · Semantic Versioning 2.0.0

---

## [3.22.0] — Hybrid Crest Ocean + Voxel Lakes

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
