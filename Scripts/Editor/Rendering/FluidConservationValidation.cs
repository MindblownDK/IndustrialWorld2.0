#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Materials;
using VoxelEngine.WaterSim;

namespace VoxelEngine.EditorTools
{
    public static class FluidConservationValidation
    {
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool rendering = WaterMeshBuilder.RenderingEnabled;
            WaterMeshBuilder.RenderingEnabled = false;
            try
            {
                using(var world=new TestWorld())
                {
                    Chunk a=world.Add(Vector3Int.zero),b=world.Add(Vector3Int.right);
                    const int S=VoxelConstants.CHUNK_SIZE;
                    a.SetVoxelLocal(S-1,4,4,new Voxel(-1,(byte)MaterialId.WaterLiquid,255));
                    long before=world.Volume();
                    bool moved=ConservativeFluidSolver.Step(world,a,0);
                    Require(moved,"Water must move");Require(world.Volume()==before,"Seam transfer conservation");
                    Require(b.GetVoxelLocal(0,4,4).waterLevel>0,"Transfer must cross a chunk seam");
                    for(int tick=1;tick<=24;tick++){ConservativeFluidSolver.Step(world,a,tick);ConservativeFluidSolver.Step(world,b,tick);}
                    Require(world.Volume()==before,"Closed-world conservation over 25 steps");
                }
                using(var world=new TestWorld())
                {
                    Chunk c=world.Add(new Vector3Int(-2,-1,0));
                    c.SetVoxelLocal(3,8,3,new Voxel(-1,(byte)MaterialId.CrudeOil,255));
                    c.SetVoxelLocal(3,7,3,new Voxel(-1,(byte)MaterialId.WaterLiquid,255));
                    long oil=world.Volume((byte)MaterialId.CrudeOil), water=world.Volume((byte)MaterialId.WaterLiquid);
                    for(int tick=0;tick<12;tick++)ConservativeFluidSolver.Step(world,c,tick);
                    Require(world.Volume((byte)MaterialId.CrudeOil)==oil,"Oil conservation");
                    Require(world.Volume((byte)MaterialId.WaterLiquid)==water,"Water conservation with oil and negative coordinates");
                }
                Debug.Log("[FluidValidation] PASS: seam movement, total volume, separate water/oil volume, negative coordinates, unloaded closed boundaries.");
            }
            finally { WaterMeshBuilder.RenderingEnabled=rendering; ConservativeFluidSolver.Reset(); }
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("Fluid validation: "+message);}
        private sealed class TestWorld : IVoxelWorld, IDisposable
        {
            private readonly Dictionary<Vector3Int,Chunk> chunks=new();
            public Chunk Add(Vector3Int coord)
            {
                var c=new Chunk{coord=coord,isGenerated=true,voxels=new NativeArray<Voxel>(VoxelConstants.CHUNK_SIZE_P*VoxelConstants.CHUNK_SIZE_P*VoxelConstants.CHUNK_SIZE_P,Allocator.TempJob)};
                for(int i=0;i<c.voxels.Length;i++)c.voxels[i]=Voxel.Empty;
                chunks.Add(coord,c);return c;
            }
            public long Volume(byte? material=null){long sum=0;foreach(var c in chunks.Values)for(int z=0;z<VoxelConstants.CHUNK_SIZE;z++)for(int y=0;y<VoxelConstants.CHUNK_SIZE;y++)for(int x=0;x<VoxelConstants.CHUNK_SIZE;x++){var v=c.GetVoxelLocal(x,y,z);if(!material.HasValue||v.material==material.Value)sum+=v.waterLevel;}return sum;}
            public void Dispose(){foreach(var c in chunks.Values)if(c.voxels.IsCreated)c.voxels.Dispose();}
            public Voxel GetVoxelWorld(Vector3Int p){Vector3Int coord=new Vector3Int(Mathf.FloorToInt(p.x/(float)VoxelConstants.CHUNK_SIZE),Mathf.FloorToInt(p.y/(float)VoxelConstants.CHUNK_SIZE),Mathf.FloorToInt(p.z/(float)VoxelConstants.CHUNK_SIZE));if(!chunks.TryGetValue(coord,out Chunk c))return Voxel.Solid;Vector3Int l=p-coord*VoxelConstants.CHUNK_SIZE;return c.GetVoxelLocal(l.x,l.y,l.z);}
            public void SetVoxelWorld(Vector3Int p,Voxel v,bool remesh=true) => throw new NotSupportedException();
            public bool TryGetChunk(Vector3Int coord,out Chunk c)=>chunks.TryGetValue(coord,out c);
            public Vector3Int WorldToVoxel(Vector3 p)=>Vector3Int.FloorToInt(p);
            public Vector3Int WorldToChunk(Vector3 p)=>Vector3Int.FloorToInt(p/VoxelConstants.CHUNK_SIZE);
            public void ScheduleMeshJob(Chunk c){} public void CompleteGenJobForChunk(Chunk c){} public void CompleteMeshJobForChunk(Chunk c){}
            public MaterialRegistry MaterialRegistry=>null;public Transform Viewer=>null;public int SeaLevel=>96;public int Seed=>1;
        }
    }
}
#endif
