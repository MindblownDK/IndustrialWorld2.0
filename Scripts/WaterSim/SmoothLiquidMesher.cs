using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Core;
using VoxelEngine.Items;

namespace VoxelEngine.WaterSim
{
    /// <summary>World-grid marching tetrahedra: no chunk-local up axis, skirt or height clamp.
    /// Shared samples give identical intersections on loaded chunk boundaries.</summary>
    public static class SmoothLiquidMesher
    {
        private static readonly Vector3Int[] Corners = {
            new Vector3Int(0,0,0), new Vector3Int(1,0,0), new Vector3Int(1,1,0), new Vector3Int(0,1,0),
            new Vector3Int(0,0,1), new Vector3Int(1,0,1), new Vector3Int(1,1,1), new Vector3Int(0,1,1) };
        private static readonly int[,] Tetra = { {0,5,1,6}, {0,1,2,6}, {0,2,3,6}, {0,3,7,6}, {0,7,4,6}, {0,4,5,6} };
        private static readonly int[,] Edges = { {0,1}, {0,2}, {0,3}, {1,2}, {1,3}, {2,3} };

        public static void Build(Chunk chunk)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            const int N = S + 1;
            var world = ActiveWorld.Current;
            if (world == null) return;
            var neighbours = new Dictionary<Vector3Int, Chunk>();
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                Vector3Int coord = chunk.coord + new Vector3Int(x,y,z);
                if (!world.TryGetChunk(coord, out Chunk c) || c == null || !c.isGenerated) continue;
                world.CompleteGenJobForChunk(c); world.CompleteMeshJobForChunk(c);
                neighbours.Add(coord,c);
            }
            Vector3Int origin = chunk.coord * S;
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            var uv = new List<Vector2>(); var flow = new List<Vector2>(); var colors = new List<Color>();
            var triangles = new List<int>[7];
            for (int i=0;i<7;i++) triangles[i] = new List<int>();
            const int H = S + 2;
            var halo = new Voxel[H * H * H];
            var present = new bool[7];
            for (int z=-1;z<=S;z++) for(int y=-1;y<=S;y++) for(int x=-1;x<=S;x++)
            {
                Vector3Int q = origin + new Vector3Int(x,y,z);
                Vector3Int coord = new Vector3Int(Mathf.FloorToInt(q.x/(float)S), Mathf.FloorToInt(q.y/(float)S), Mathf.FloorToInt(q.z/(float)S));
                if (!neighbours.TryGetValue(coord, out Chunk owner)) continue;
                Vector3Int local = q - coord * S;
                Voxel v = owner.GetVoxelLocal(local.x,local.y,local.z);
                halo[(x+1)+H*((y+1)+H*(z+1))] = v;
                if (FluidMaterialUtility.IsFluid(v)) present[(int)FluidMaterialUtility.LiquidFromVoxel(v)] = true;
            }
            Vector3 velocity = ConservativeFluidSolver.GetFlow(chunk);
            Vector2 visualFlow = new Vector2(Vector3.Dot(velocity,new Vector3(0.73f,0.39f,0.56f)),
                Vector3.Dot(velocity,new Vector3(-0.42f,0.86f,0.28f)));
            var field = new float[N*N*N];
            var relevant = new bool[N*N*N];
            var values = new float[8]; var positions = new Vector3[8];
            var intersections = new Vector3[4];
            for (int liquidIndex=0; liquidIndex<7; liquidIndex++)
            {
                if (!present[liquidIndex]) continue;
                var liquid = (LiquidType)liquidIndex;
                bool any = false;
                for (int z=0;z<N;z++) for(int y=0;y<N;y++) for(int x=0;x<N;x++)
                {
                    int index = x + N*(y+N*z);
                    field[index] = Sample(x,y,z, liquid, halo, out relevant[index]);
                    any |= relevant[index];
                }
                if (!any) continue;
                for(int z=0;z<S;z++) for(int y=0;y<S;y++) for(int x=0;x<S;x++)
                {
                    bool wet=false,positive=false,negative=false;
                    for(int c=0;c<8;c++)
                    {
                        Vector3Int q = new Vector3Int(x,y,z)+Corners[c];
                        int index=q.x+N*(q.y+N*q.z);
                        values[c]=field[index]; positions[c]=(Vector3)(origin+q);
                        wet |= relevant[index]; positive |= values[c]>=0; negative |= values[c]<0;
                    }
                    if (!wet || !positive || !negative) continue;
                    float depth = BankDepth((Vector3)origin + new Vector3(x+0.5f,y+0.5f,z+0.5f), neighbours);
                    Color bank = new Color(Mathf.Clamp01(depth/3f),1f,1f,Mathf.Clamp01(depth/8f));
                    for(int t=0;t<6;t++)
                    {
                        int count=0; Vector3 inside=Vector3.zero,outside=Vector3.zero; int ni=0,no=0;
                        for(int j=0;j<4;j++) {int c=Tetra[t,j]; if(values[c]>=0){inside+=positions[c];ni++;}else{outside+=positions[c];no++;}}
                        if(ni==0||no==0) continue;
                        Vector3 normal=(outside/no-inside/ni).normalized;
                        for(int e=0;e<6;e++)
                        {
                            int a=Tetra[t,Edges[e,0]], b=Tetra[t,Edges[e,1]];
                            if ((values[a]>=0)==(values[b]>=0)) continue;
                            Vector3 pa = positions[a], pb = positions[b];
                            // Canonical global endpoint order makes shared-edge arithmetic identical.
                            if (pa.x > pb.x || (pa.x == pb.x && (pa.y > pb.y || (pa.y == pb.y && pa.z > pb.z))))
                            { int swap = a; a = b; b = swap; }
                            intersections[count++]=Vector3.Lerp(positions[a],positions[b],values[a]/(values[a]-values[b]));
                        }
                        if(count<3) continue;
                        // Sort the planar polygon around its centre before triangulation.
                        Vector3 center=Vector3.zero;for(int j=0;j<count;j++) center+=intersections[j];center/=count;
                        Vector3 tangent=(intersections[0]-center).normalized, bitangent=Vector3.Cross(normal,tangent);
                        for(int a=0;a<count-1;a++)for(int b=a+1;b<count;b++)
                        {
                            Vector3 da=intersections[a]-center,db=intersections[b]-center;
                            if(Mathf.Atan2(Vector3.Dot(da,bitangent),Vector3.Dot(da,tangent))>Mathf.Atan2(Vector3.Dot(db,bitangent),Vector3.Dot(db,tangent)))
                            {Vector3 swap=intersections[a];intersections[a]=intersections[b];intersections[b]=swap;}
                        }
                        int start=vertices.Count;
                        for(int j=0;j<count;j++)
                        {
                            Vector3 p=intersections[j]; vertices.Add((p-(Vector3)origin)*VoxelConstants.VOXEL_SIZE);
                            Vector3 up=PlanetWaterUtility.IsPlanetWorld ? p.normalized : Vector3.up;
                            normals.Add(Vector3.Dot(normal,up)>0.3f ? up : normal);
                            uv.Add(new Vector2(p.x+p.y*0.19f,p.z+p.y*0.23f));
                            flow.Add(visualFlow);
                            colors.Add(bank);
                        }
                        for(int j=1;j<count-1;j++)
                        {
                            if (Vector3.Cross(vertices[start+j]-vertices[start],vertices[start+j+1]-vertices[start]).sqrMagnitude < 1e-10f) continue;
                            triangles[liquidIndex].Add(start);triangles[liquidIndex].Add(start+j);triangles[liquidIndex].Add(start+j+1);
                        }
                    }
                }
            }
            if(vertices.Count==0){chunk.waterMeshGO.SetActive(false);return;}
            if(chunk.waterMesh==null)chunk.waterMesh=new Mesh{name="ContinuousLiquidSurface"};
            var mesh=chunk.waterMesh;mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetUVs(1,flow);mesh.SetColors(colors);
            mesh.subMeshCount=7;for(int i=0;i<7;i++)mesh.SetTriangles(triangles[i],i);mesh.RecalculateBounds();
            chunk.waterMeshFilter.sharedMesh=mesh;chunk.waterMeshRenderer.sharedMaterials=WaterMeshBuilder.LiquidMaterialArray();
            chunk.waterMeshGO.SetActive(true);
        }

        private static float BankDepth(Vector3 p, Dictionary<Vector3Int,Chunk> chunks)
        {
            const int S=VoxelConstants.CHUNK_SIZE;
            Vector3 down=PlanetWaterUtility.IsPlanetWorld ? -p.normalized : Vector3.down;
            for (int i=1;i<=8;i++)
            {
                Vector3Int q=Vector3Int.FloorToInt(p+down*i);
                Vector3Int coord=new Vector3Int(Mathf.FloorToInt(q.x/(float)S),Mathf.FloorToInt(q.y/(float)S),Mathf.FloorToInt(q.z/(float)S));
                if(!chunks.TryGetValue(coord,out Chunk c)) return i;
                Vector3Int local=q-coord*S;
                if(c.GetVoxelLocal(local.x,local.y,local.z).IsSolid) return i-0.5f;
            }
            return 8f;
        }

        private static float Sample(int px, int py, int pz, LiquidType liquid, Voxel[] halo, out bool wet)
        {
            const int H = VoxelConstants.CHUNK_SIZE + 2;
            float sum = 0f;
            int fluidSamples = 0;
            wet = false;
            for (int z=0;z<=1;z++) for(int y=0;y<=1;y++) for(int x=0;x<=1;x++)
            {
                Voxel v = halo[(px+x)+H*((py+y)+H*(pz+z))];
                if (v.IsSolid) continue;
                bool matches = FluidMaterialUtility.Matches(v,liquid);
                wet |= matches;
                sum += matches ? v.waterLevel/255f-0.5f : -0.5f;
                fluidSamples++;
            }
            // Fully buried vertices lie on the bank, not in a fictitious positive water mass.
            // Shared vertex values are independent of cube/tetrahedron ownership.
            if (fluidSamples == 0) return -0.001f;
            float value = sum / fluidSamples;
            return Mathf.Abs(value) < 0.001f ? (value >= 0f ? 0.001f : -0.001f) : value;
        }
    }
}
