#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.WaterSim;
namespace VoxelEngine.EditorTools
{
    public static class SurfaceRegressionValidation
    {
        public static void Run()
        {
            const int h = SmoothLiquidMesher.HaloSize;
            var s = new SmoothLiquidMesher.Snapshot { voxels = new Voxel[h*h*h], known = new bool[h*h*h] };
            for(int z=-2;z<=34;z++) for(int y=-2;y<=34;y++) for(int x=-2;x<=34;x++)
            {
                int i=x+2+h*(y+2+h*(z+2));
                s.known[i]=true;
                s.voxels[i]= y<2 ? Voxel.Solid : y<5 ? new Voxel(-1,FluidMaterialUtility.WaterMaterial,255) : Voxel.Empty;
            }
            var a=SmoothLiquidMesher.Extract(s);
            Require(a.vertices.Count>0,"full pool missing");
            var b=System.Threading.Tasks.Task.Run(()=>SmoothLiquidMesher.Extract(s)).GetAwaiter().GetResult();
            Require(a.vertices.Count==b.vertices.Count,"worker differs");
            Require(a.flow.Count==a.vertices.Count,"flow channel length differs from vertices");
            for(int i=0;i<a.vertices.Count;i++)
            {
                Require(a.vertices[i]==b.vertices[i],"worker vertex differs");
                Require(!float.IsNaN(a.vertices[i].x),"invalid vertex");
                Require(a.colors[i].b>=0 && a.colors[i].b<=1,"depth channel");
            }

            const int chunkSize = VoxelConstants.CHUNK_SIZE;
            var localFlow = new Vector2[chunkSize*chunkSize];
            for(int z=8;z<24;z++) for(int x=8;x<24;x++)
                localFlow[x+z*chunkSize]=Vector2.right*0.8f;
            var flowSnapshot = new SmoothLiquidMesher.Snapshot
            {
                origin = s.origin,
                planet = s.planet,
                voxels = (Voxel[])s.voxels.Clone(),
                known = (bool[])s.known.Clone()
            };
            SetSnapshotField(flowSnapshot,"waterSurfaceFlow",localFlow);
            SetSnapshotField(flowSnapshot,"waterImpact",new float[chunkSize*chunkSize]);
            SetSnapshotField(flowSnapshot,"waterFlowTimestamp",12.5f);
            SetSnapshotField(flowSnapshot,"flowShaderMask",(byte)1);
            var flowingSurface=SmoothLiquidMesher.Extract(flowSnapshot);
            bool hasDirectionalFlow=false, hasZero=false;
            for(int i=0;i<flowingSurface.flow.Count;i++)
            {
                hasDirectionalFlow |= flowingSurface.flow[i].x>0.01f;
                hasZero |= flowingSurface.flow[i].sqrMagnitude<0.000001f;
            }
            Require(flowingSurface.flow.Count==flowingSurface.vertices.Count,"local flow channel length differs from vertices");
            Require(hasDirectionalFlow && hasZero,"per-column water flow was not projected and localized on the surface");

            var bankSupport = new SmoothLiquidMesher.Snapshot
            {
                voxels = new Voxel[h*h*h],
                known = new bool[h*h*h]
            };
            for(int z=-2;z<=34;z++) for(int y=-2;y<=34;y++) for(int x=-2;x<=34;x++)
            {
                int i=x+2+h*(y+2+h*(z+2));
                bankSupport.known[i]=true;
                bankSupport.voxels[i]=Voxel.Empty;
            }
            bankSupport.voxels[11+2+h*(10+2+h*(10+2))]
                = new Voxel(-1,FluidMaterialUtility.WaterMaterial,255);
            for(int z=10;z<=11;z++) for(int y=10;y<=11;y++) for(int x=12;x<=13;x++)
                bankSupport.voxels[x+2+h*(y+2+h*(z+2))]=Voxel.Solid;
            var bankMesh=SmoothLiquidMesher.Extract(bankSupport);
            Require(bankMesh.vertices.Count>0,"bank-support water surface missing");
            for(int i=0;i<bankMesh.vertices.Count;i++)
            {
                Vector3 p=bankMesh.vertices[i];
                // Vertices on a shared cube face can legitimately be emitted by the
                // adjacent mixed air/bank cube; flag only geometry strictly inside this cube.
                bool detachedBankSliver=p.x>12.0001f && p.x<12.9999f
                    && p.y>10.0001f && p.y<10.9999f
                    && p.z>10.0001f && p.z<10.9999f;
                Require(!detachedBankSliver,"all-solid support cube emitted an interior bank sliver");
            }

            var shoreline = new SmoothLiquidMesher.Snapshot
            {
                origin = new Vector3Int(0, 3200, 0),
                planet = true,
                voxels = new Voxel[h*h*h],
                known = new bool[h*h*h]
            };
            FieldInfo seaRadiusField = typeof(SmoothLiquidMesher.Snapshot)
                .GetField("seaRadius", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(seaRadiusField != null,"runtime sea-radius metadata missing");
            seaRadiusField.SetValue(shoreline,3201.83f);
            for(int z=-2;z<=34;z++) for(int y=-2;y<=34;y++) for(int x=-2;x<=34;x++)
            {
                int i=x+2+h*(y+2+h*(z+2));
                shoreline.known[i]=true;
                bool fractionalSeaWater = y==2 && x>=12 && x<=20 && z>=12 && z<=20;
                shoreline.voxels[i]=y<2 ? Voxel.Solid
                    : fractionalSeaWater ? new Voxel(-1,FluidMaterialUtility.WaterMaterial,64)
                    : Voxel.Empty;
            }
            Require(SmoothLiquidMesher.Extract(shoreline).vertices.Count==0,
                "fractional sea-level water emitted disconnected bank films");

            Array.Clear(s.known,0,s.known.Length);
            Require(SmoothLiquidMesher.Extract(s).vertices.Count==0,"unknown boundary rendered");
            Debug.Log("[SurfaceValidation] PASS: full pool, deterministic worker extraction, finite vertices, depth range, localized flow projection, solid-only bank sliver suppression, unknown boundaries, fractional sea-level film suppression. Shader appearance/FPS/mining/grass are NOT validated by this fixture.");
        }
        private static void SetSnapshotField(SmoothLiquidMesher.Snapshot snapshot,string name,object value)
        {
            FieldInfo field=typeof(SmoothLiquidMesher.Snapshot).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
            Require(field!=null,"render-flow snapshot field missing: "+name);
            field.SetValue(snapshot,value);
        }
        private static void Require(bool ok,string message) { if(!ok) throw new InvalidOperationException("Surface validation: "+message); }
    }
}
#endif
