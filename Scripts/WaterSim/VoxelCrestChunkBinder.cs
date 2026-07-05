// Assets/Scripts/VoxelEngine/WaterSim/VoxelCrestChunkBinder.cs
//
// v3.22.0 – DEPRECATED stub. Kept only so scenes and prefabs serialized in
// v3.12.x don't lose their script reference on load. The hybrid Crest ocean
// model (v3.22.0) no longer paints the Crest/Ocean shader onto voxel water
// meshes because the shader's vertex-snap logic is designed for Crest's own
// concentric tile mesh and collapsed our voxel heightfields.
//
// The component is auto-removed by WaterMeshBuilder.EnsureGO() on the next
// water chunk rebuild.

using UnityEngine;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Deprecated. Auto-removed by WaterMeshBuilder in v3.22.0+.
    /// </summary>
    [System.Obsolete("v3.22.0 hybrid ocean model no longer binds Crest per-chunk. Component auto-removed on next rebuild.")]
    [AddComponentMenu("")] // hide from Add Component menu
    public sealed class VoxelCrestChunkBinder : MonoBehaviour
    {
        private void OnEnable()
        {
            // Self-destruct at runtime so nothing tries to bind Crest per chunk.
            if (Application.isPlaying) Destroy(this);
        }
    }
}
