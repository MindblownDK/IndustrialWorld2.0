# Water & Crest Bridge — Changelog

Branch: **Dev** · Semantic Versioning 2.0.0

---

## [3.12.0] — Genuine Crest Ocean Bridge

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
