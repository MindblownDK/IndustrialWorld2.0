using UnityEngine;

namespace VoxelEngine.Rendering
{
    [CreateAssetMenu(menuName = "Voxel Engine/Terrain Texture Library")]
    public sealed class TerrainTextureLibrary : ScriptableObject
    {
        public Texture2DArray albedo;
        public Texture2DArray normal;
        public Texture2DArray mask;
        public Vector4[] entries = new Vector4[256]; // slice+1, normal present, mask present, metres per tile

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bind()
        {
            Shader.SetGlobalFloat("_TerrainPbrReady", 0f);
            var library = Resources.Load<TerrainTextureLibrary>("VoxelEngineRuntime/TerrainTextureLibrary");
            if (library == null || library.albedo == null || library.normal == null || library.mask == null
                || library.entries == null || library.entries.Length != 256 || !SystemInfo.supports2DArrayTextures) return;
            Shader.SetGlobalTexture("_TerrainPbrAlbedo", library.albedo);
            Shader.SetGlobalTexture("_TerrainPbrNormal", library.normal);
            Shader.SetGlobalTexture("_TerrainPbrMask", library.mask);
            Shader.SetGlobalVectorArray("_TerrainPbrEntries", library.entries);
            Shader.SetGlobalFloat("_TerrainPbrReady", 1f);
        }
    }
}
