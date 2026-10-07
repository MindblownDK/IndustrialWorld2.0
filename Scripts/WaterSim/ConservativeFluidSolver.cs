using UnityEngine;
using System.Collections.Generic;
using VoxelEngine.Core;
using VoxelEngine.Items;

namespace VoxelEngine.WaterSim
{
    /// <summary>Bounded pairwise hydraulic-head transfers. Writes real cells, never padding.
    /// Closed/unloaded boundaries are impermeable; each transfer subtracts exactly what it adds.
    /// A tick rotates its axis and scan ordering to avoid a permanent preferred direction.</summary>
    public static class ConservativeFluidSolver
    {
        private sealed class FlowState { public int epoch; public Vector3 velocity; }
        private static readonly Dictionary<Chunk, FlowState> Flows = new();
        public static void Reset() => Flows.Clear();
        public static Vector3 GetFlow(Chunk chunk)
            => Flows.TryGetValue(chunk, out FlowState state) && state.epoch == chunk.streamEpoch ? state.velocity : Vector3.zero;
        private static void SetFlow(Chunk chunk, Vector3 flow)
        {
            if (!Flows.TryGetValue(chunk,out FlowState state)) { state = new FlowState(); Flows.Add(chunk,state); }
            if (state.epoch != chunk.streamEpoch) state.velocity = Vector3.zero;
            state.epoch = chunk.streamEpoch; state.velocity = Vector3.Lerp(state.velocity, flow, 0.5f);
        }
        private static readonly Vector3Int[] Axes = { Vector3Int.right, Vector3Int.up, new Vector3Int(0, 0, 1) };

        public static bool Step(IVoxelWorld world, Chunk source, int step)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            bool changed = false;
            Vector3 netFlow = Vector3.zero; int transfers = 0;
            var dirtyNeighbours = new bool[3];
            Vector3Int origin = source.coord * S;
            var targets = new Chunk[3];
            for (int axisIndex = 0; axisIndex < 3; axisIndex++)
            {
                if (world.TryGetChunk(source.coord + Axes[axisIndex], out Chunk neighbour)
                    && neighbour != null && neighbour.isGenerated)
                {
                    world.CompleteGenJobForChunk(neighbour); world.CompleteMeshJobForChunk(neighbour);
                    targets[axisIndex] = neighbour;
                }
            }
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3Int axis = Axes[(step + pass) % 3];
                for (int iz = 0; iz < S; iz++)
                for (int iy = 0; iy < S; iy++)
                for (int ix = 0; ix < S; ix++)
                {
                    int x = (step & 1) == 0 ? ix : S - 1 - ix;
                    int y = (step & 1) == 0 ? iy : S - 1 - iy;
                    int z = (step & 1) == 0 ? iz : S - 1 - iz;
                    Vector3Int p = origin + new Vector3Int(x, y, z);
                    Vector3Int q = p + axis;
                    int axisIndex = (step + pass) % 3;
                    bool border = axisIndex == 0 ? x == S-1 : axisIndex == 1 ? y == S-1 : z == S-1;
                    Chunk target = border ? targets[axisIndex] : source;
                    if (target == null) continue;
                    Vector3Int localQ = q - target.coord * S;
                    Voxel a = source.GetVoxelLocal(x, y, z);
                    Voxel b = target.GetVoxelLocal(localQ.x, localQ.y, localQ.z);
                    if (a.IsSolid || b.IsSolid || (a.waterLevel == 0 && b.waterLevel == 0)) continue;
                    float ra = PlanetWaterUtility.IsPlanetWorld ? ((Vector3)p).magnitude : p.y;
                    float rb = PlanetWaterUtility.IsPlanetWorld ? ((Vector3)q).magnitude : q.y;
                    bool different = a.waterLevel > 0 && b.waterLevel > 0
                        && FluidMaterialUtility.LiquidFromVoxel(a) != FluidMaterialUtility.LiquidFromVoxel(b);
                    if (different)
                    {
                        // Density separation only swaps whole cells, conserving each liquid.
                        if (a.waterLevel == 255 && b.waterLevel == 255 && Mathf.Abs(ra - rb) > 0.5f)
                        {
                            int rankA = LiquidPhysics.DensityRank(FluidMaterialUtility.LiquidFromVoxel(a));
                            int rankB = LiquidPhysics.DensityRank(FluidMaterialUtility.LiquidFromVoxel(b));
                            if ((ra - rb) * (rankA - rankB) > 0f)
                            {
                                source.SetVoxelLocal(x, y, z, b);
                                target.SetVoxelLocal(localQ.x, localQ.y, localQ.z, a);
                                if (target != source) dirtyNeighbours[axisIndex] = true; changed = true;
                            }
                        }
                        continue;
                    }
                    float head = (ra - rb) * 255f + a.waterLevel - b.waterLevel;
                    if (Mathf.Abs(head) < 2f) continue;
                    bool fromA = head > 0f;
                    Voxel donor = fromA ? a : b;
                    Voxel receiver = fromA ? b : a;
                    if (donor.waterLevel == 0 || receiver.waterLevel == 255) continue;
                    LiquidType liquid = FluidMaterialUtility.LiquidFromVoxel(donor);
                    int rate = liquid == LiquidType.CrudeOil || liquid == LiquidType.HeavyFuelOil ? 8 : 64;
                    int amount = Mathf.Min(Mathf.Min(rate, Mathf.FloorToInt(Mathf.Abs(head) * 0.25f)),
                        Mathf.Min(donor.waterLevel, 255 - receiver.waterLevel));
                    if (amount <= 0) continue;
                    netFlow += (Vector3)axis * (fromA ? amount : -amount) / 255f; transfers++;
                    donor.waterLevel -= (byte)amount;
                    if (donor.waterLevel == 0) FluidMaterialUtility.ClearLiquid(ref donor);
                    receiver.density = -1;
                    FluidMaterialUtility.SetLiquid(ref receiver, liquid, (byte)(receiver.waterLevel + amount));
                    source.SetVoxelLocal(x, y, z, fromA ? donor : receiver);
                    target.SetVoxelLocal(localQ.x, localQ.y, localQ.z, fromA ? receiver : donor);
                    if (target != source) dirtyNeighbours[axisIndex] = true; changed = true;
                }
            }
            SetFlow(source, transfers > 0 ? netFlow / transfers * 10f : Vector3.zero);
            for (int i=0;i<3;i++) if(dirtyNeighbours[i]) { SetFlow(targets[i], GetFlow(source)); Dirty(targets[i]); }
            if (changed) Dirty(source);
            return changed;
        }

        private static void Dirty(Chunk chunk)
        {
            chunk.isModified = true;
            chunk.isDirty = true;
            WaterMeshBuilder.Schedule(chunk);
            if (Application.isPlaying) FluidManager.Instance?.MarkActive(chunk.coord);
        }
    }
}
