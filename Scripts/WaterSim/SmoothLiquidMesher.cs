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

        public const int HaloSize = VoxelConstants.CHUNK_SIZE + 5; // [-2,S+2], including head-extension support
        public sealed class Snapshot
        {
            public Vector3Int origin;
            public bool planet;
            // Runtime-only spherical sea reference used to keep fractional ocean edge
            // samples from being reconstructed as detached films on the beach.
            internal float seaRadius;
            public Voxel[] voxels;
            public bool[] known;
            public Vector3 velocity;
        }
        public sealed class Surface
        {
            public readonly List<Vector3> vertices = new();
            public readonly List<Vector3> normals = new();
            public readonly List<Vector2> uv = new(), flow = new();
            public readonly List<Color> colors = new();
            public readonly List<int>[] triangles = { new(), new(), new(), new(), new(), new(), new() };
        }

        // Pure snapshot computation. No scene objects, jobs, NativeArrays or global world
        // state may be accessed here: this method also runs on a worker thread.
        public static Surface Extract(Snapshot snapshot)
        {
            const int S = VoxelConstants.CHUNK_SIZE, N = S + 1;
            Vector3Int origin = snapshot.origin;
            Voxel[] halo = snapshot.voxels;
            var output = new Surface();
            var vertices=output.vertices; var normals=output.normals;
            var uv=output.uv; var flow=output.flow; var colors=output.colors;
            var triangles=output.triangles;
            var present = new bool[7];
            foreach (Voxel v in halo)
                if (FluidMaterialUtility.IsFluid(v)) present[(int)FluidMaterialUtility.LiquidFromVoxel(v)] = true;
            Vector3 velocity = snapshot.velocity;
            Vector2 visualFlow = new Vector2(Vector3.Dot(velocity,new Vector3(0.73f,0.39f,0.56f)),
                Vector3.Dot(velocity,new Vector3(-0.42f,0.86f,0.28f)));
            var field = new float[N*N*N];
            var relevant = new bool[N*N*N];
            var values = new float[8]; var positions = new Vector3[8];
            var intersections = new Vector3[4];
            var intersectionAngles = new float[4];
            for (int liquidIndex=0; liquidIndex<7; liquidIndex++)
            {
                if (!present[liquidIndex]) continue;
                var liquid = (LiquidType)liquidIndex;
                bool any = false;
                for (int z=0;z<N;z++) for(int y=0;y<N;y++) for(int x=0;x<N;x++)
                {
                    int index = x + N*(y+N*z);
                    field[index] = Sample(x,y,z, liquid, snapshot, out relevant[index]);
                    any |= relevant[index];
                }
                if (!any) continue;
                for(int z=0;z<S;z++) for(int y=0;y<S;y++) for(int x=0;x<S;x++)
                {
                    bool wet=false,positive=false,negative=false,known=true;
                    for(int c=0;c<8;c++)
                    {
                        Vector3Int q = new Vector3Int(x,y,z)+Corners[c];
                        int index=q.x+N*(q.y+N*q.z);
                        known &= IsKnown(snapshot,q);
                        values[c]=field[index]; positions[c]=(Vector3)(origin+q);
                        wet |= relevant[index]; positive |= values[c]>=0; negative |= values[c]<0;
                    }
                    if (!known || !positive || !negative) continue;
                    float depth = BankDepth(new Vector3(x+0.5f,y+0.5f,z+0.5f), snapshot);
                    Color bank = new Color(Mathf.Clamp01(depth/3f),1f,Mathf.Clamp01(depth/8f),1f);
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
                        for (int j = 0; j < count; j++)
                        {
                            Vector3 delta = intersections[j] - center;
                            intersectionAngles[j] = Mathf.Atan2(
                                Vector3.Dot(delta, bitangent), Vector3.Dot(delta, tangent));
                        }
                        for (int j = 1; j < count; j++)
                        {
                            float angle = intersectionAngles[j];
                            Vector3 point = intersections[j];
                            int insert = j - 1;
                            while (insert >= 0 && intersectionAngles[insert] > angle)
                            {
                                intersectionAngles[insert + 1] = intersectionAngles[insert];
                                intersections[insert + 1] = intersections[insert];
                                insert--;
                            }
                            intersectionAngles[insert + 1] = angle;
                            intersections[insert + 1] = point;
                        }
                        int start=vertices.Count;
                        for(int j=0;j<count;j++)
                        {
                            Vector3 p=intersections[j]; vertices.Add((p-(Vector3)origin)*VoxelConstants.VOXEL_SIZE);
                            Vector3 up=snapshot.planet ? p.normalized : Vector3.up;
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
            return output;
        }

        public static void Apply(Chunk chunk, Surface surface)
        {
            if(surface.vertices.Count==0) { chunk.waterMeshGO.SetActive(false); return; }
            if(chunk.waterMesh==null) chunk.waterMesh=new Mesh{name="ContinuousLiquidSurface"};
            var mesh=chunk.waterMesh; mesh.Clear(); mesh.indexFormat=IndexFormat.UInt32;
            mesh.SetVertices(surface.vertices); mesh.SetNormals(surface.normals);
            mesh.SetUVs(0,surface.uv); mesh.SetUVs(1,surface.flow); mesh.SetColors(surface.colors);
            mesh.subMeshCount=7;
            for(int i=0;i<7;i++) mesh.SetTriangles(surface.triangles[i],i,false);
            mesh.RecalculateBounds();
            chunk.waterMeshFilter.sharedMesh=mesh;
            chunk.waterMeshRenderer.sharedMaterials=WaterMeshBuilder.LiquidMaterialArray();
            chunk.waterMeshGO.SetActive(true);
        }

        private static Voxel Read(Snapshot snapshot, Vector3Int p)
        {
            int x=p.x+2,y=p.y+2,z=p.z+2;
            const int H=HaloSize;
            if(x<0||y<0||z<0||x>=H||y>=H||z>=H) return Voxel.Empty;
            return snapshot.voxels[x+H*(y+H*z)];
        }
        private static float BankDepth(Vector3 local, Snapshot snapshot)
        {
            Vector3 down=snapshot.planet ? -(local+(Vector3)snapshot.origin).normalized : Vector3.down;
            for(int i=1;i<=8;i++)
                if(Read(snapshot, Vector3Int.RoundToInt(local+down*i)).IsSolid) return i-0.5f;
            return 8f;
        }
        private static bool IsKnown(Snapshot s, Vector3Int p)
        {
            int x=p.x+2,y=p.y+2,z=p.z+2;
            if(x<0||y<0||z<0||x>=HaloSize||y>=HaloSize||z>=HaloSize) return false;
            return s.known == null || s.known[x+HaloSize*(y+HaloSize*z)];
        }
        private static float Height(Snapshot snapshot, Vector3Int p)
            => snapshot.planet ? ((Vector3)(snapshot.origin+p)).magnitude : snapshot.origin.y+p.y;

        private static bool IsFractionalSeaSurfaceWater(Snapshot snapshot, Vector3Int p, Voxel voxel, LiquidType liquid)
        {
            return liquid == LiquidType.Water && snapshot.planet && snapshot.seaRadius > 0f
                && voxel.waterLevel < 128
                && Mathf.Abs(Height(snapshot, p) - snapshot.seaRadius) <= 1.25f;
        }

        private static float Sample(int x,int y,int z,LiquidType liquid,Snapshot snapshot,out bool wet)
        {
            Vector3Int p = new Vector3Int(x,y,z);
            Voxel v=Read(snapshot,p);
            wet=FluidMaterialUtility.Matches(v,liquid);
            float value = wet ? v.waterLevel/255f-0.5f : -0.5f;
            if(v.IsSolid)
            {
                // Extend known fluid head through every corner of a wet cube (26-ring).
                // Fully submerged/pressurised samples are not a measured surface height.
                for(int dz=-1;dz<=1;dz++) for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++)
                {
                    Vector3Int np=p+new Vector3Int(dx,dy,dz);
                    Voxel n=Read(snapshot,np);
                    if(!FluidMaterialUtility.Matches(n,liquid)) continue;
                    // Fractional generated ocean voxels sit just above the nominal sea
                    // shell. Extrapolating their negative fill into solid bank samples
                    // paints detached water slivers across otherwise dry beach cells.
                    if(IsFractionalSeaSurfaceWater(snapshot,np,n,liquid)) continue;
                    Vector3 radial=snapshot.planet ? ((Vector3)(snapshot.origin+np)).normalized : Vector3.up;
                    Vector3 abs=new Vector3(Mathf.Abs(radial.x),Mathf.Abs(radial.y),Mathf.Abs(radial.z));
                    Vector3Int up=abs.x>=abs.y && abs.x>=abs.z ? new Vector3Int(radial.x>=0?1:-1,0,0)
                        : abs.y>=abs.z ? new Vector3Int(0,radial.y>=0?1:-1,0) : new Vector3Int(0,0,radial.z>=0?1:-1);
                    Voxel above=Read(snapshot,np+up);
                    float head=n.waterLevel/255f-0.5f+Height(snapshot,np)-Height(snapshot,p);
                    if(n.waterLevel==255 && (above.IsSolid || (FluidMaterialUtility.Matches(above,liquid) && above.waterLevel==255))) head=0.5f;
                    value=Mathf.Max(value,Mathf.Clamp(head,-0.5f,0.5f));
                }
            }
            else if(wet && value <= 0f)
            {
                Vector3 down=snapshot.planet ? -((Vector3)(snapshot.origin+p)).normalized : Vector3.down;
                Voxel support=Read(snapshot,p+Vector3Int.RoundToInt(down));
                bool resolvedSurface=false;
                for(int dz=-1;dz<=1;dz++) for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++)
                {
                    Voxel n=Read(snapshot,p+new Vector3Int(dx,dy,dz));
                    if(FluidMaterialUtility.Matches(n,liquid) && n.waterLevel>=128) resolvedSurface=true;
                }
                // Keep isolated supported films for placed liquids, but do not lift the
                // fractional generated sea shell above its waterline; that fallback makes
                // one-cell beach fragments where no full neighbouring sample exists.
                if(support.IsSolid && !resolvedSurface
                    && !IsFractionalSeaSurfaceWater(snapshot,p,v,liquid))
                    value=0.01f+v.waterLevel/255f*0.04f;
            }
            return Mathf.Abs(value)<0.00001f ? 0.00001f : value;
        }
    }
}
