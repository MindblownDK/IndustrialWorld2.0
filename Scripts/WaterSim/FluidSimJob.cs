// Assets/Scripts/VoxelEngine/WaterSim/FluidSimJob.cs
//
// Burst-compiled cellular automata liquid simulation operating directly on voxel
// data. The volume byte is still named waterLevel for save compatibility, but the
// voxel material now identifies the liquid type:
//   • WaterLiquid = fast, clear water
//   • CrudeOil    = viscous, slower, heavier liquid
//
// Rules:
//  1. Gravity moves liquid down first; oil has a lower max transfer per tick.
//  2. Viscous horizontal equalization only spreads into empty/same-liquid cells.
//  3. Crude oil sinks through water slowly, keeping liquids separated.
//  4. Empty fluid voxels are cleaned back to Air so terrain/material queries stay sane.

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using VoxelEngine.Core;
using VoxelEngine.Materials;

namespace VoxelEngine.WaterSim
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct FluidSimJob : IJob
    {
        public NativeArray<Voxel> voxels; // padded chunk (CHUNK_SIZE_P^3)
        public int chunkSize;
        public int chunkSizeP;

        public NativeArray<int> changed; // single-element array, 0 or 1

        private const byte AirMat   = (byte)MaterialId.Air;
        private const byte WaterMat = (byte)MaterialId.WaterLiquid;
        private const byte OilMat   = (byte)MaterialId.CrudeOil;

        public void Execute()
        {
            int S = chunkSize;
            int SP = chunkSizeP;
            bool any = false;

            // Bottom-to-top lets a falling stream cascade several cells in one job pass.
            for (int y = 0; y < S; y++)
            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                int i = Pad(x, y, z, SP);
                var v = voxels[i];
                if (v.waterLevel == 0) continue;

                if (v.IsSolid)
                {
                    v.waterLevel = 0;
                    if (IsFluidMat(v.material)) v.material = AirMat;
                    voxels[i] = v;
                    any = true;
                    continue;
                }

                byte liquidMat = NormalizeFluidMaterial(ref v);
                bool isOil = liquidMat == OilMat;

                // Oil is treated as the heavier liquid for gameplay readability. If oil
                // rests on water, swap their cells slowly so crude ends up below water.
                int belowI = Pad(x, y - 1, z, SP);
                var below = voxels[belowI];
                if (isOil && IsWater(below) && !below.IsSolid && v.waterLevel > 8)
                {
                    byte oilLevel = v.waterLevel;
                    byte waterLevel = below.waterLevel;
                    below.material = OilMat;
                    below.waterLevel = oilLevel;
                    v.material = WaterMat;
                    v.waterLevel = waterLevel;
                    voxels[belowI] = below;
                    voxels[i] = v;
                    any = true;
                    continue;
                }

                // Rule 1: vertical down-flow into empty/same-liquid cells.
                below = voxels[belowI];
                if (!below.IsSolid && CanShareCell(below, liquidMat))
                {
                    int space = 255 - below.waterLevel;
                    if (space > 0)
                    {
                        int maxFall = isOil ? 96 : 255;
                        int transfer = Min3(v.waterLevel, space, maxFall);
                        if (transfer > 0)
                        {
                            v.waterLevel = (byte)(v.waterLevel - transfer);
                            below.waterLevel = (byte)(below.waterLevel + transfer);
                            below.material = liquidMat;
                            if (v.waterLevel == 0) v.material = AirMat;
                            voxels[belowI] = below;
                            voxels[i] = v;
                            any = true;
                            if (v.waterLevel == 0) continue;
                        }
                    }
                }

                // Rule 2: horizontal equalization, but only when supported below.
                below = voxels[belowI];
                bool belowBlocked = below.IsSolid || (CanShareCell(below, liquidMat) && below.waterLevel >= 254) || (!CanShareCell(below, liquidMat) && below.waterLevel > 0);
                if (belowBlocked && v.waterLevel > 1)
                {
                    int horizontalStep = isOil ? 16 : 48;
                    any |= TryFlowHorizontal(i, x + 1, y, z, SP, liquidMat, horizontalStep, ref v);
                    any |= TryFlowHorizontal(i, x - 1, y, z, SP, liquidMat, horizontalStep, ref v);
                    any |= TryFlowHorizontal(i, x, y, z + 1, SP, liquidMat, horizontalStep, ref v);
                    any |= TryFlowHorizontal(i, x, y, z - 1, SP, liquidMat, horizontalStep, ref v);
                    if (v.waterLevel == 0) v.material = AirMat;
                    voxels[i] = v;
                }
            }

            changed[0] = any ? 1 : 0;
        }

        private bool TryFlowHorizontal(int fromI, int nx, int ny, int nz, int sp, byte liquidMat, int maxStep, ref Voxel source)
        {
            if (source.waterLevel <= 1) return false;
            int ni = Pad(nx, ny, nz, sp);
            var n = voxels[ni];
            if (n.IsSolid || !CanShareCell(n, liquidMat)) return false;

            int diff = source.waterLevel - n.waterLevel;
            if (diff <= 1) return false;

            int transfer = diff / 2;
            if (transfer > maxStep) transfer = maxStep;
            if (transfer <= 0) return false;

            source.waterLevel = (byte)(source.waterLevel - transfer);
            n.waterLevel = (byte)(n.waterLevel + transfer);
            n.material = liquidMat;
            voxels[ni] = n;
            return true;
        }

        private static byte NormalizeFluidMaterial(ref Voxel voxel)
        {
            if (voxel.material == OilMat) return OilMat;
            // Backwards compatibility: old oceans/player water had Air + waterLevel.
            voxel.material = WaterMat;
            return WaterMat;
        }

        private static bool IsWater(Voxel voxel)
            => voxel.waterLevel > 0 && !voxel.IsSolid && voxel.material != OilMat;

        private static bool CanShareCell(Voxel voxel, byte liquidMat)
        {
            if (voxel.waterLevel == 0) return true;
            if (liquidMat == OilMat) return voxel.material == OilMat;
            return voxel.material != OilMat;
        }

        private static bool IsFluidMat(byte mat) => mat == WaterMat || mat == OilMat;

        private static int Min3(int a, int b, int c)
        {
            int m = a < b ? a : b;
            return m < c ? m : c;
        }

        private static int Pad(int x, int y, int z, int SP)
        {
            int px = x + 1; if (px < 0) px = 0; if (px >= SP) px = SP - 1;
            int py = y + 1; if (py < 0) py = 0; if (py >= SP) py = SP - 1;
            int pz = z + 1; if (pz < 0) pz = 0; if (pz >= SP) pz = SP - 1;
            return px + py * SP + pz * SP * SP;
        }
    }
}
