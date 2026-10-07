// Assets/Scripts/VoxelEngine/WaterSim/WaterMeshBuilder.cs
//
// Queued, pooled-chunk-safe continuous liquid surface rendering.
//
// SmoothLiquidMesher extracts a shared world-grid scalar surface for all seven liquids.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;

namespace VoxelEngine.WaterSim
{
    public static class WaterMeshBuilder
    {
        // Water meshes are scheduled independently from terrain jobs. Chunks are pooled, so
        // capture the rent epoch here as well; an old liquid rebuild must never attach itself to
        // a later coordinate that reused the same Chunk object after streaming movement.
        private readonly struct QueuedChunk
        {
            public readonly Chunk chunk;
            public readonly int epoch;
            public QueuedChunk(Chunk chunk)
            {
                this.chunk = chunk;
                epoch = chunk != null ? chunk.streamEpoch : 0;
            }
        }

        private static readonly Queue<QueuedChunk> _queue = new();
        private static readonly Dictionary<Chunk, int> _queuedEpoch = new();
        private static Material _waterMat;
        private static Material _oilMat;
        private static Material _externalWaterMat;
        private static Material _externalOilMat;
        private static bool _missingShaderReported;

        // 9.16.0 — one material slot per liquid (enum order: Water, CrudeOil,
        // RefinedOil, LiquidFuel, HeavyFuelOil, MarineGasOil, Coolant).
        private static Material[] _liquidMats = new Material[7];

        public static bool RenderingEnabled { get; set; } = true;

        /// <summary>
        /// Legacy compatibility switch. Native water now renders every real generated liquid
        /// surface, including ocean basins; keep this false to prevent a fake wrapped sea shell.
        /// </summary>
        public static bool SkipVoxelWaterAtOrBelowSeaLevel { get; set; } = false;

        /// <summary>
        /// Small negative bias in voxel units for the native patch/voxel hand-off.
        /// Shore cells just above the sea shell keep their detailed voxel surface.
        /// </summary>
        public static float SeaLevelSkipBiasVoxels { get; set; } = -0.25f;

        private const byte WaterVoxelMat  = (byte)MaterialId.WaterVoxel;
        private const byte WaterLiquidMat = (byte)MaterialId.WaterLiquid;
        private const byte OilMat         = (byte)MaterialId.CrudeOil;

        public static void ResetForNewWorld()
        {
            _queue.Clear();
            _queuedEpoch.Clear();
            // 9.16.0 — reset the whole 7-slot registry; profiles rebuild on demand.
            for (int i = 0; i < _liquidMats.Length; i++)
            {
                var m = _liquidMats[i];
                bool external = m != null && (m == _externalWaterMat || m == _externalOilMat);
                if (m != null && !external)
                {
                    if (Application.isPlaying) Object.Destroy(m);
                    else Object.DestroyImmediate(m);
                }
                _liquidMats[i] = null;
            }
            _liquidMats[0] = _externalWaterMat;
            _liquidMats[1] = _externalOilMat;
            _waterMat = _externalWaterMat;
            _oilMat = _externalOilMat;
            _missingShaderReported = false;
        }

        public static void Schedule(Chunk c)
        {
            if (!RenderingEnabled || c == null) return;
            int epoch = c.streamEpoch;
            if (_queuedEpoch.TryGetValue(c, out int queuedEpoch) && queuedEpoch == epoch) return;
            _queuedEpoch[c] = epoch;
            _queue.Enqueue(new QueuedChunk(c));
        }

        public static void Pump(int budget)
        {
            if (!RenderingEnabled)
            {
                _queue.Clear();
                _queuedEpoch.Clear();
                return;
            }
            EnsureMats();
            int done = 0;
            while (done < budget && _queue.Count > 0)
            {
                QueuedChunk queued = _queue.Dequeue();
                Chunk c = queued.chunk;
                if (c == null || c.streamEpoch != queued.epoch || c.go == null || !c.go.activeSelf) continue;
                if (_queuedEpoch.TryGetValue(c, out int queuedEpoch) && queuedEpoch == queued.epoch)
                    _queuedEpoch.Remove(c);
                if (!c.isGenerated) continue;

                // Water mesh generation reads voxel NativeArrays on the main thread. Make sure
                // world generation/terrain meshing jobs are complete first, especially for
                // spherical worlds where SphereChunkGenJob can still own the voxel buffer.
                var world = ActiveWorld.Current;
                if (world != null)
                {
                    world.CompleteGenJobForChunk(c);
                    world.CompleteMeshJobForChunk(c);
                }

                Build(c);
                done++;
            }
        }

        public static void SetMaterialOverrides(Material waterMaterial, Material oilMaterial)
        {
            Material compatibleWater = IsVoxelWaterCompatible(waterMaterial) ? waterMaterial : null;
            if (_externalWaterMat == compatibleWater && _externalOilMat == oilMaterial) return;
            var previousExternalWater = _externalWaterMat;
            var previousExternalOil = _externalOilMat;

            _externalWaterMat = IsVoxelWaterCompatible(waterMaterial) ? waterMaterial : null;
            _externalOilMat = oilMaterial;

            if (_externalWaterMat != null)
                _liquidMats[0] = _externalWaterMat;
            else if (previousExternalWater != null && _liquidMats[0] == previousExternalWater)
                _liquidMats[0] = null;

            if (oilMaterial != null)
                _liquidMats[1] = oilMaterial;
            else if (previousExternalOil != null && _liquidMats[1] == previousExternalOil)
                _liquidMats[1] = null;

            _waterMat = _liquidMats[0];
            _oilMat = _liquidMats[1];

            // Propagate native material changes to live liquid chunk meshes.
            RefreshLiveWaterRenderers();
        }

        /// <summary>Reassigns native materials to every live liquid chunk mesh.</summary>
        private static void RefreshLiveWaterRenderers()
        {
            if (_waterMat == null && _oilMat == null) return;
            var filters = Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include);
            for (int i = 0; i < filters.Length; i++)
            {
                var f = filters[i];
                if (f == null || f.gameObject == null) continue;
                if (f.gameObject.name != "LiquidSurface") continue;
                var r = f.GetComponent<MeshRenderer>();
                if (r == null) continue;
                r.sharedMaterials = LiquidMaterialArray();
            }
        }

        /// <summary>The full 7-slot material array (built on demand).</summary>
        public static Material[] LiquidMaterialArray()
        {
            EnsureMats();
            return _liquidMats;
        }

        private static void EnsureMats()
        {
            // 14.46.2: a dedicated build ships no shaders - every retry here
            // printed the engine's bare shader warning. Liquid surfaces stay
            // material-less headless; nobody is looking at them.
            if (VoxelEngine.Networking.NetworkSession.IsDedicated) return;
            if (_externalWaterMat != null) _liquidMats[0] = _externalWaterMat;
            if (_externalOilMat != null) _liquidMats[1] = _externalOilMat;

            Material runtimeTemplate = Resources.Load<Material>("VoxelEngineRuntime/VoxelWaterRuntime");
            var sh = runtimeTemplate != null ? runtimeTemplate.shader : null;
            if (sh == null || !sh.isSupported)
                sh = Shader.Find("VoxelEngine/VoxelWaterURP");
            if (sh == null || !sh.isSupported)
                sh = Shader.Find("VoxelEngine/VoxelWater");
            if (sh == null || !sh.isSupported)
                sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null || !sh.isSupported)
                sh = Shader.Find("Standard");

            if (sh == null || !sh.isSupported)
            {
                if (!_missingShaderReported)
                {
                    _missingShaderReported = true;
                    Debug.LogError("[WaterMeshBuilder] No supported liquid shader is present in this player build. Run Voxel Engine Setup step 103 and rebuild.");
                }
                return;
            }

            // Every liquid receives a private runtime material so its visual profile cannot
            // mutate the build-anchor material shared through Resources.
            for (int i = 0; i < 7; i++)
            {
                if (_liquidMats[i] != null) continue;
                var t = (VoxelEngine.Items.LiquidType)i;
                var mat = runtimeTemplate != null && runtimeTemplate.shader == sh
                    ? new Material(runtimeTemplate)
                    : new Material(sh);
                mat.name = "VoxelLiquid_" + t;
                ConfigureTransparent(mat);
                LiquidVisualProfile.For(t).ApplyTo(mat);
                _liquidMats[i] = mat;
            }

            _waterMat = _liquidMats[0];
            _oilMat = _liquidMats[1];
        }

        private static bool IsVoxelWaterCompatible(Material mat)
        {
            if (mat == null || mat.shader == null) return false;
            string shaderName = mat.shader.name;
            // Only shaders authored for voxel/natively generated topology are valid here.
            return shaderName == "VoxelEngine/VoxelWaterURP" ||
                   shaderName == "VoxelEngine/VoxelWater" ||
                   shaderName == "Universal Render Pipeline/Lit" ||
                   shaderName == "Standard";
        }

        public static Material GetWaterMaterial()
        {
            EnsureMats();
            return _waterMat;
        }

        private static void ConfigureTransparent(Material mat)
        {
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mat.SetInt("_Cull", (int)CullMode.Off);
            mat.SetFloat("_Surface", 1.0f);
            mat.SetFloat("_Blend", 0.0f);
            mat.SetColor("_BaseColor", new Color(0.08f, 0.52f, 0.82f, 0.88f));
            mat.SetColor("_Color", new Color(0.08f, 0.52f, 0.82f, 0.88f));
        }

        private static void Build(Chunk c)
        {
            EnsureGO(c);
            SmoothLiquidMesher.Build(c);
        }

        public static int GetChunkLodStride(Chunk c) => 1;

        private static void EnsureGO(Chunk c)
        {
            if (c.waterMeshGO != null)
            {
                foreach (var col in c.waterMeshGO.GetComponents<Collider>()) Object.Destroy(col);
                var existingLod = c.waterMeshGO.GetComponent<WaterSurfaceLodController>();
                if (existingLod != null) existingLod.Configure(c);
                return;
            }
            c.waterMeshGO = new GameObject("LiquidSurface");
            c.waterMeshGO.transform.SetParent(c.go.transform, false);
            c.waterMeshFilter = c.waterMeshGO.AddComponent<MeshFilter>();
            c.waterMeshRenderer = c.waterMeshGO.AddComponent<MeshRenderer>();
            c.waterMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            c.waterMeshRenderer.receiveShadows = false;
            c.waterMeshGO.AddComponent<WaterSurfaceLodController>().Configure(c);
        }

    }
}
