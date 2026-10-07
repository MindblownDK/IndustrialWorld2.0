// Assets/Scripts/VoxelEngine/WaterSim/WaterMeshBuilder.cs
//
// Queued, pooled-chunk-safe continuous liquid surface rendering.
//
// SmoothLiquidMesher extracts a shared world-grid scalar surface for all seven liquids.

using System.Collections.Generic;
using System.Threading.Tasks;
using VoxelEngine.Cosmos;
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

        private sealed class Pending
        {
            public Chunk chunk;
            public int epoch, worldGeneration;
            public Task<SmoothLiquidMesher.Surface> task;
        }
        private static readonly List<Pending> _pending = new();
        private static readonly Dictionary<Chunk,int> _queuedEpoch = new();
        private static readonly List<Chunk> _deadChunkScratch = new();
        private static int _worldGeneration;
        private static readonly Unity.Profiling.ProfilerMarker SnapshotMarker = new("Voxel.WaterSnapshot");
        private static readonly Unity.Profiling.ProfilerMarker UploadMarker = new("Voxel.WaterUpload");
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
            _worldGeneration++;
            _queuedEpoch.Clear();
            _deadChunkScratch.Clear();
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
            _queuedEpoch[c] = c.streamEpoch;
        }

        public static void Pump(int budget)
        {
            // Completed workers own managed snapshots only; world resets/pool reuse cannot
            // invalidate their memory. Old outputs are discarded on the main thread.
            int uploaded = 0;
            for(int i=_pending.Count-1;i>=0;i--)
            {
                Pending p=_pending[i];
                if(!p.task.IsCompleted) continue;
                if(p.task.IsFaulted)
                { Debug.LogException(p.task.Exception); _pending.RemoveAt(i); continue; }
                if(uploaded >= 1 && p.worldGeneration == _worldGeneration) continue;
                if(RenderingEnabled && p.worldGeneration == _worldGeneration && p.chunk != null
                    && p.chunk.streamEpoch == p.epoch && p.chunk.go != null && p.chunk.go.activeSelf)
                {
                    using(UploadMarker.Auto())
                    { EnsureGO(p.chunk); SmoothLiquidMesher.Apply(p.chunk,p.task.Result); }
                    uploaded++;
                }
                _pending.RemoveAt(i);
            }
            if(!RenderingEnabled) { _queuedEpoch.Clear(); return; }
            var world=ActiveWorld.Current;
            if(world == null) return;
            EnsureMats();
            // At most two owned snapshots and one main-thread capture per frame. Near-first
            // initial meshes outrank refreshes, preventing settled ocean refresh starvation.
            if(_pending.Count >= 2 || _queuedEpoch.Count == 0) return;
            Vector3 viewer = world.Viewer != null ? (Vector3)world.WorldToVoxel(world.Viewer.position) : Vector3.zero;
            Chunk nearest=null; float best=float.PositiveInfinity;
            _deadChunkScratch.Clear();
            foreach(var pair in _queuedEpoch)
            {
                Chunk c=pair.Key;
                if(c==null || c.streamEpoch!=pair.Value || c.go==null || !c.go.activeSelf)
                { _deadChunkScratch.Add(c); continue; }
                if(!c.isGenerated) continue;
                bool running=false;
                foreach(Pending p in _pending) if(p.chunk==c && p.epoch==c.streamEpoch) { running=true; break; }
                if(running) continue;
                float distance=((Vector3)(c.coord*VoxelConstants.CHUNK_SIZE)+Vector3.one*16f-viewer).sqrMagnitude;
                if(c.waterMesh == null) distance *= 0.25f;
                if(distance<best) { best=distance; nearest=c; }
            }
            foreach(Chunk c in _deadChunkScratch) _queuedEpoch.Remove(c);
            if(nearest==null) return;
            SmoothLiquidMesher.Snapshot snapshot;
            using(SnapshotMarker.Auto()) snapshot=Capture(world,nearest);
            if(snapshot==null) return;
            _queuedEpoch.Remove(nearest);
            _pending.Add(new Pending { chunk=nearest, epoch=nearest.streamEpoch, worldGeneration=_worldGeneration,
                task=Task.Run(()=>SmoothLiquidMesher.Extract(snapshot)) });
        }

        private static SmoothLiquidMesher.Snapshot Capture(IVoxelWorld world,Chunk chunk)
        {
            const int S=VoxelConstants.CHUNK_SIZE, H=SmoothLiquidMesher.HaloSize;
            var neighbours=new Chunk[27];
            for(int z=-1;z<=1;z++) for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++)
            {
                if(!world.TryGetChunk(chunk.coord+new Vector3Int(x,y,z),out Chunk c) || c==null || !c.isGenerated) continue;
                if(world is SphereWorld sphere && !sphere.CanReadVoxels(c)) return null;
                // Mesh jobs read voxels only: concurrent main-thread reads need no Complete().
                neighbours[x+1+3*(y+1+3*(z+1))]=c;
            }
            var snapshot=new SmoothLiquidMesher.Snapshot { origin=chunk.coord*S, planet=world is SphereWorld,
                velocity=ConservativeFluidSolver.GetFlow(chunk), voxels=new Voxel[H*H*H], known=new bool[H*H*H] };
            for(int z=-2;z<=S+2;z++) for(int y=-2;y<=S+2;y++) for(int x=-2;x<=S+2;x++)
            {
                int dx=x<0?-1:x>=S?1:0,dy=y<0?-1:y>=S?1:0,dz=z<0?-1:z>=S?1:0;
                Chunk c=neighbours[dx+1+3*(dy+1+3*(dz+1))];
                snapshot.known[x+2+H*(y+2+H*(z+2))] = c!=null;
                snapshot.voxels[x+2+H*(y+2+H*(z+2))] = c==null ? Voxel.Empty : c.GetVoxelLocal(x-dx*S,y-dy*S,z-dz*S);
            }
            return snapshot;
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

            bool missing = false;
            for (int i=0;i<7;i++) missing |= _liquidMats[i] == null;
            if (!missing) return;
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

        public static int GetChunkLodStride(Chunk c) => 1;

        private static void EnsureGO(Chunk c)
        {
            if (c.waterMeshGO != null)
            {
                return;
            }
            c.waterMeshGO = new GameObject("LiquidSurface");
            c.waterMeshGO.transform.SetParent(c.go.transform, false);
            c.waterMeshFilter = c.waterMeshGO.AddComponent<MeshFilter>();
            c.waterMeshRenderer = c.waterMeshGO.AddComponent<MeshRenderer>();
            c.waterMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            c.waterMeshRenderer.receiveShadows = false;

        }

    }
}
