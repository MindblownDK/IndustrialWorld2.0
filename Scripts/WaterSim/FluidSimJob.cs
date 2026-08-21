// Assets/Scripts/VoxelEngine/WaterSim/FluidSimJob.cs
//
// Burst-compiled cellular automata liquid simulation operating directly on voxel
// data. Rebuilt with pressure-driven flow, viscosity differentiation, and clean
// separation of simulation logic.
//
// Liquids (9.16.0 — the overhaul):
//   • Water        — fast, low viscosity, high throughput
//   • CrudeOil     — dense, viscous, slow fall, slow spread, sinks below water
//   • RefinedOil   — light amber product: medium flow, FLOATS on water
//   • LiquidFuel   — lightest, volatile: runs fast, floats on everything
//   • HeavyFuelOil — tar-like bunker fuel: oozes, sinks below water
//   • MarineGasOil — thin pale distillate: light, quick
//   • Coolant      — watery glow-fluid: slightly denser than water
//
// Rules:
//  1. Gravity: liquid falls down with a per-liquid throughput cap
//  2. Density layering: FULL cells of different liquids swap vertically until
//     heavier liquids sit below lighter ones (fuel floats on water, water on
//     crude) — deterministic pulse-staggered swaps
//  3. Pressure equalization: horizontal flow from high to low pressure,
//     per-liquid spread rate
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
        public int simulationStep;

        private const byte AirMat         = (byte)MaterialId.Air;
        private const byte WaterMat       = (byte)MaterialId.WaterLiquid;
        private const byte WaterVoxelMat  = (byte)MaterialId.WaterVoxel;
        private const byte OilMat         = (byte)MaterialId.CrudeOil;
        private const byte RefinedOilMat  = (byte)MaterialId.RefinedOilLiquid;
        private const byte FuelMat        = (byte)MaterialId.LiquidFuelLiquid;
        private const byte HfoMat         = (byte)MaterialId.HeavyFuelOilLiquid;
        private const byte MgoMat         = (byte)MaterialId.MarineGasOilLiquid;
        private const byte CoolantMat     = (byte)MaterialId.CoolantLiquid;

        // Per-liquid flow constants (kept in sync with LiquidPhysics for the editor/tools).
        // 9.16.0 flow remake: horizontal steps raised so flow fronts advance ~1 cell/tick.
        // Water/Coolant fall at HALF a cell per tick (128) so a freshly mined pocket stays
        // visibly empty for a frame and the pour reads as a pour, not a teleport.
        private const int FuelMaxFall    = 150;  private const int FuelStep    = 80;  private const byte FuelRank    = 0;
        private const int RefinedMaxFall = 120;  private const int RefinedStep = 48;  private const byte RefinedRank = 1;
        private const int MgoMaxFall     = 110;  private const int MgoStep     = 40;  private const byte MgoRank     = 2;
        private const int WaterMaxFall   = 128;  private const int WaterStep   = 96;  private const byte WaterRank   = 3;
        private const int CoolantMaxFall = 128;  private const int CoolantStep = 84;  private const byte CoolantRank = 4;
        private const int HfoMaxFall     = 24;   private const int HfoStep     = 4;   private const byte HfoRank     = 5;
        private const int CrudeMaxFall   = 20;   private const int CrudeStep   = 2;   private const byte CrudeRank   = 6;

        private const int LayerSwapStride = 8;   // full-cell density swaps pulse at 1/8 of fluid ticks

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
                if (v.IsSolid)
                {
                    if (IsFluidMat(v.material) && v.waterLevel > 0)
                    {
                        // Convert solid fluid block → proper fluid voxel (legacy corruption repair)
                        byte savedLevel = v.waterLevel;
                        byte savedMat = IsLiquidMat(v.material) ? v.material : WaterMat;
                        v.density = -1;
                        v.material = savedMat;
                        v.waterLevel = savedLevel;
                        voxels[i] = v;
                        any = true;
                        continue;
                    }
                    // 9.16.0 — solid cells carrying a FLUID material with no level are
                    // authored SOAKED ROCK (the oil bore/reservoir casing). Keep the
                    // material so the terrain mesh and its colliders render it; erasing
                    // it to Air gutted the oil shaft and left pale holes. Only clear a
                    // stale level byte, and only report a change when one existed.
                    if (v.waterLevel != 0)
                    {
                        v.waterLevel = 0;
                        voxels[i] = v;
                        any = true;
                    }
                    continue;
                }

                byte liquidMat = NormalizeFluidMaterial(ref v);
                int maxFall, hStep;
                byte rank;
                LiquidStats(liquidMat, out maxFall, out hStep, out rank);

                int belowI = Pad(x + downX, y + downY, z + downZ, SP);
                var below = voxels[belowI];

                // ── Density layering: FULL cells of different liquids swap vertically
                // until the heavier liquid sits below (fuel floats on water, water on
                // crude). The one-material-per-voxel save format cannot represent a
                // partial mix, so only fully occupied cells swap, pulse-staggered so
                // dense liquids descend slowly and deterministically. ──
                if (below.waterLevel >= 250 && !below.IsSolid
                    && v.waterLevel >= 250 && below.material != liquidMat
                    && IsLiquidMat(below.material)
                    && rank > RankOf(below.material)
                    && ShouldLayerSwap(x, y, z))
                {
                    byte belowLevel = below.waterLevel;
                    byte myLevel = v.waterLevel;
                    byte belowMat = below.material;
                    below.material = liquidMat;
                    below.waterLevel = myLevel;
                    v.material = belowMat;
                    v.waterLevel = belowLevel;
                    voxels[belowI] = below;
                    voxels[i] = v;
                    any = true;
                    continue;
                }

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

            // 9.16.0 flow remake — transfer everything the cap allows (was diff/2, which
            // made spreading crawl). Keeping the diff-1 form guarantees monotonic
            // convergence: no ping-pong oscillation between neighbours.
            int transfer = diff - 1;
            if (transfer > maxStep) transfer = maxStep;
            if (transfer <= 0) return;

            source.waterLevel = (byte)(source.waterLevel - transfer);
            n.waterLevel      = (byte)(n.waterLevel + transfer);
            n.material        = liquidMat;
            voxels[ni] = n;
        }

        private bool ShouldLayerSwap(int x, int y, int z)
        {
            unchecked
            {
                int hash = x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ simulationStep * 265443577;
                return (hash & (LayerSwapStride - 1)) == 0;
            }
        }

        /// <summary>Per-liquid flow stats — Burst-safe constant switch.</summary>
        private static void LiquidStats(byte mat, out int maxFall, out int hStep, out byte rank)
        {
            switch (mat)
            {
                case OilMat:        maxFall = CrudeMaxFall;   hStep = CrudeStep;   rank = CrudeRank;   break;
                case RefinedOilMat: maxFall = RefinedMaxFall; hStep = RefinedStep; rank = RefinedRank; break;
                case FuelMat:       maxFall = FuelMaxFall;    hStep = FuelStep;    rank = FuelRank;    break;
                case HfoMat:        maxFall = HfoMaxFall;     hStep = HfoStep;     rank = HfoRank;     break;
                case MgoMat:        maxFall = MgoMaxFall;     hStep = MgoStep;     rank = MgoRank;     break;
                case CoolantMat:    maxFall = CoolantMaxFall; hStep = CoolantStep; rank = CoolantRank; break;
                default:            maxFall = WaterMaxFall;   hStep = WaterStep;   rank = WaterRank;   break;   // water + legacy
            }
        }

        private static byte RankOf(byte mat)
        {
            switch (mat)
            {
                case OilMat:        return CrudeRank;
                case RefinedOilMat: return RefinedRank;
                case FuelMat:       return FuelRank;
                case HfoMat:        return HfoRank;
                case MgoMat:        return MgoRank;
                case CoolantMat:    return CoolantRank;
                default:            return WaterRank;
            }
        }

        private static byte NormalizeFluidMaterial(ref Voxel voxel)
        {
            if (IsLiquidMat(voxel.material)) return voxel.material;
            voxel.material = WaterMat;   // legacy values + frozen WaterVoxel read as water
            return WaterMat;
        }

        private static bool IsWater(Voxel voxel)
            => voxel.waterLevel > 0 && !voxel.IsSolid && voxel.material != OilMat;

        private static bool CanShareCell(Voxel voxel, byte liquidMat)
        {
            if (voxel.waterLevel == 0) return true;
            return voxel.material == liquidMat;   // liquids never mix in one voxel
        }

        private static bool IsFluidMat(byte mat)
            => IsLiquidMat(mat) || mat == WaterVoxelMat;

        private static bool IsLiquidMat(byte mat)
            => mat == WaterMat || mat == OilMat || mat == RefinedOilMat || mat == FuelMat
            || mat == HfoMat || mat == MgoMat || mat == CoolantMat;

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
