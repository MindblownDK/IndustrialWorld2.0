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
            for(int i=0;i<a.vertices.Count;i++)
            {
                Require(a.vertices[i]==b.vertices[i],"worker vertex differs");
                Require(!float.IsNaN(a.vertices[i].x),"invalid vertex");
                Require(a.colors[i].b>=0 && a.colors[i].b<=1,"depth channel");
            }

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
            Debug.Log("[SurfaceValidation] PASS: full pool, deterministic worker extraction, finite vertices, depth range, solid-only bank sliver suppression, unknown boundaries, fractional sea-level film suppression. Visuals/FPS/mining/grass are NOT validated by this fixture.");
        }
        private static void Require(bool ok,string message) { if(!ok) throw new InvalidOperationException("Surface validation: "+message); }
    }
}
#endif
