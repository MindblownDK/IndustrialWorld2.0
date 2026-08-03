// Assets/Scripts/VoxelEngine/WaterSim/FluidSimJob.cs
//
// Burst-compiled cellular automata liquid simulation operating directly on voxel
// data. Rebuilt with pressure-driven flow, viscosity differentiation, and clean
// separation of simulation logic.
//
// Liquids:
//   • Water (WaterLiquid) — fast, low viscosity, high throughput
//   • Crude Oil (CrudeOil) — viscous, slow fall, slow spread, floats over water
//
// Rules:
//  1. Gravity: liquid falls down; oil capped at 64/tick vs water 255
//  2. Oil remains above water so authored surface puddles stay visible
//  3. Pressure equalization: horizontal flow from high to low pressure
//     Viscosity limits transfer step per tick (oil 10, water 48)
//  4. Micro-cleanup: tiny floating drops (< 3 level) with air below just fall
//  5. Clean-up: empty fluid voxels reset to Air so terrain queries stay sane

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
        
        public int downX;
        public int downY;
        public int downZ;

        public NativeArray<int> changed; // single-element array, 0 or 1

        private const byte AirMat        = (byte)MaterialId.Air;
        private const byte WaterMat      = (byte)MaterialId.WaterLiquid;
        private const byte WaterVoxelMat = (byte)MaterialId.WaterVoxel;
        private const byte OilMat        = (byte)MaterialId.CrudeOil;

        // Viscosity parameters — oil is much more viscous than water
        private const int WaterMaxFall        = 255;
        private const int OilMaxFall          = 32;   // slow — viscous drag
        private const int WaterHorizontalStep = 64;
        private const int OilHorizontalStep   = 6;    // very slow horizontal spread

        public void Execute()
        {
            int S = chunkSize;
            int SP = chunkSizeP;
            bool any = false;

            int startY = downY == 1 ? S - 1 : 0; int stepY = downY == 1 ? -1 : 1;
            int startZ = downZ == 1 ? S - 1 : 0; int stepZ = downZ == 1 ? -1 : 1;
            int startX = downX == 1 ? S - 1 : 0; int stepX = downX == 1 ? -1 : 1;

            // Iterate such that we process "bottom" cells first to allow falling streams to cascade
            for (int iY = 0; iY < S; iY++)
            for (int iZ = 0; iZ < S; iZ++)
            for (int iX = 0; iX < S; iX++)
            {
                int y = startY + iY * stepY;
                int z = startZ + iZ * stepZ;
                int x = startX + iX * stepX;

                int i = Pad(x, y, z, SP);
                var v = voxels[i];
                if (v.waterLevel == 0) continue;

                // Solid cells with residual waterLevel — convert or clean up.
                // If the solid block is a fluid material (WaterVoxel from old saves,
                // or a corrupted WaterLiquid/Oil), convert it to a proper fluid voxel
                // instead of just clearing the waterLevel (which would leave an invisible
                // solid block or a blue terrain face).
                if (v.IsSolid)
                {
                    if (IsFluidMat(v.material) && v.waterLevel > 0)
                    {
                        // Convert solid fluid block → proper fluid voxel
                        byte savedLevel = v.waterLevel;
                        byte savedMat = v.material == OilMat ? OilMat : WaterMat;
                        v.density = -1;
                        v.material = savedMat;
                        v.waterLevel = savedLevel;
                        voxels[i] = v;
                        any = true;
                        continue;
                    }
                    v.waterLevel = 0;
                    if (IsFluidMat(v.material)) v.material = AirMat;
                    voxels[i] = v;
                    any = true;
                    continue;
                }

                byte liquidMat = NormalizeFluidMaterial(ref v);
                bool isOil = liquidMat == OilMat;
                int maxFall = isOil ? OilMaxFall : WaterMaxFall;
                int hStep   = isOil ? OilHorizontalStep : WaterHorizontalStep;

                // Oil is less dense than water. Do not swap it downward through a
                // water cell: a reservoir seep must remain a readable surface puddle.
                int belowI = Pad(x + downX, y + downY, z + downZ, SP);
                var below = voxels[belowI];

                // --- Rule 1: gravity — vertical down-flow ---
                if (!below.IsSolid && CanShareCell(below, liquidMat))
                {
                    int space = 255 - below.waterLevel;
                    if (space > 0)
                    {
                        int transfer = Min3(v.waterLevel, space, maxFall);
                        if (transfer > 0)
                        {
                            v.waterLevel     = (byte)(v.waterLevel - transfer);
                            below.waterLevel = (byte)(below.waterLevel + transfer);
                            below.material   = liquidMat;
                            if (v.waterLevel == 0) v.material = AirMat;
                            voxels[belowI] = below;
                            voxels[i]      = v;
                            any = true;
                            if (v.waterLevel == 0) continue;
                        }
                    }
                }

                // --- Rule 2: horizontal pressure equalization (only when supported below) ---
                below = voxels[belowI];
                bool belowBlocked = below.IsSolid
                    || (CanShareCell(below, liquidMat) && below.waterLevel >= 254)
                    || (!CanShareCell(below, liquidMat) && below.waterLevel > 0);

                if (belowBlocked && v.waterLevel > 1)
                {
                    int rightX = downY != 0 ? 1 : (downZ != 0 ? 1 : 0);
                    int rightY = downX != 0 ? 1 : 0;
                    int rightZ = 0;
                    
                    int fwdX = 0;
                    int fwdY = downZ != 0 ? 1 : 0;
                    int fwdZ = downX != 0 ? 1 : (downY != 0 ? 1 : 0);

                    TryFlowHorizontal(i, x + rightX, y + rightY, z + rightZ, SP, liquidMat, hStep, ref v);
                    TryFlowHorizontal(i, x - rightX, y - rightY, z - rightZ, SP, liquidMat, hStep, ref v);
                    TryFlowHorizontal(i, x + fwdX, y + fwdY, z + fwdZ, SP, liquidMat, hStep, ref v);
                    TryFlowHorizontal(i, x - fwdX, y - fwdY, z - fwdZ, SP, liquidMat, hStep, ref v);
                    if (v.waterLevel == 0) v.material = AirMat;
                    voxels[i] = v;
                }

                // --- Rule 3: micro-cleanup — tiny floating drops just fall ---
                if (v.waterLevel > 0 && v.waterLevel <= 2 && !below.IsSolid && below.waterLevel == 0)
                {
                    below.waterLevel = v.waterLevel;
                    below.material   = liquidMat;
                    v.waterLevel     = 0;
                    v.material       = AirMat;
                    voxels[belowI] = below;
                    voxels[i]      = v;
                    any = true;
                }
            }

            changed[0] = any ? 1 : 0;
        }

        private void TryFlowHorizontal(int fromI, int nx, int ny, int nz, int sp, byte liquidMat, int maxStep, ref Voxel source)
        {
            if (source.waterLevel <= 1) return;
            int ni = Pad(nx, ny, nz, sp);
            var n = voxels[ni];
            if (n.IsSolid || !CanShareCell(n, liquidMat)) return;

            int diff = source.waterLevel - n.waterLevel;
            if (diff <= 1) return;

            int transfer = diff / 2;
            if (transfer > maxStep) transfer = maxStep;
            if (transfer <= 0) return;

            source.waterLevel = (byte)(source.waterLevel - transfer);
            n.waterLevel      = (byte)(n.waterLevel + transfer);
            n.material        = liquidMat;
            voxels[ni] = n;
        }

        private static byte NormalizeFluidMaterial(ref Voxel voxel)
        {
            if (voxel.material == OilMat) return OilMat;
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

        private static bool IsFluidMat(byte mat) => mat == WaterMat || mat == OilMat || mat == WaterVoxelMat;

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
