// Assets/Scripts/VoxelEngine/WaterSim/FluidSimJob.cs
//
// Burst-compiled cellular automata fluid simulation operating directly on voxel data.
// Runs per-chunk. Reads/writes waterLevel bytes in place.
//
// Rules:
//  1. Vertical down-flow: transfer water to cell below if not solid/full.
//  2. Horizontal out-flow: if below is solid/full, equalize with 4 horizontal neighbours.

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct FluidSimJob : IJob
    {
        public NativeArray<Voxel> voxels; // padded chunk (CHUNK_SIZE_P^3)
        public int chunkSize;
        public int chunkSizeP;

        // Output: did anything change? (for sleep detection)
        public NativeArray<int> changed; // single-element array, 0 or 1

        public void Execute()
        {
            int S = chunkSize;
            int SP = chunkSizeP;
            bool any = false;

            // Process bottom-to-top so gravity cascades in one pass.
            for (int y = 0; y < S; y++)
            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                int i = Pad(x, y, z, SP);
                var v = voxels[i];
                if (v.waterLevel == 0) continue;
                if (v.IsSolid) { v.waterLevel = 0; voxels[i] = v; any = true; continue; }

                // Rule 1: Vertical down-flow.
                if (y > 0 || true) // check cell below (may be in padding)
                {
                    int belowI = Pad(x, y - 1, z, SP);
                    var below = voxels[belowI];
                    if (!below.IsSolid)
                    {
                        int space = 255 - below.waterLevel;
                        if (space > 0)
                        {
                            int transfer = v.waterLevel < space ? v.waterLevel : space;
                            v.waterLevel -= (byte)transfer;
                            below.waterLevel += (byte)transfer;
                            voxels[belowI] = below;
                            voxels[i] = v;
                            any = true;
                            if (v.waterLevel == 0) continue;
                        }
                    }
                }

                // Rule 2: Horizontal out-flow (only if cell below is solid or full).
                {
                    int belowI = Pad(x, y - 1, z, SP);
                    var below = voxels[belowI];
                    bool belowBlocked = below.IsSolid || below.waterLevel >= 254;

                    if (belowBlocked && v.waterLevel > 1)
                    {
                        // Equalize with 4 horizontal neighbours.
                        int sum = v.waterLevel;
                        int count = 1;
                        int n0 = Pad(x + 1, y, z, SP);
                        int n1 = Pad(x - 1, y, z, SP);
                        int n2 = Pad(x, y, z + 1, SP);
                        int n3 = Pad(x, y, z - 1, SP);

                        var v0 = voxels[n0]; var v1 = voxels[n1];
                        var v2 = voxels[n2]; var v3 = voxels[n3];

                        if (!v0.IsSolid) { sum += v0.waterLevel; count++; }
                        if (!v1.IsSolid) { sum += v1.waterLevel; count++; }
                        if (!v2.IsSolid) { sum += v2.waterLevel; count++; }
                        if (!v3.IsSolid) { sum += v3.waterLevel; count++; }

                        if (count > 1)
                        {
                            int avg = sum / count;
                            int remainder = sum % count;

                            byte newLevel = (byte)(avg + (remainder > 0 ? 1 : 0));
                            if (newLevel != v.waterLevel)
                            {
                                v.waterLevel = newLevel;
                                voxels[i] = v;
                                any = true;
                            }

                            int ri = 1;
                            if (!v0.IsSolid) { byte nl = (byte)(avg + (ri < remainder ? 1 : 0)); ri++; if (nl != v0.waterLevel) { v0.waterLevel = nl; voxels[n0] = v0; any = true; } }
                            if (!v1.IsSolid) { byte nl = (byte)(avg + (ri < remainder ? 1 : 0)); ri++; if (nl != v1.waterLevel) { v1.waterLevel = nl; voxels[n1] = v1; any = true; } }
                            if (!v2.IsSolid) { byte nl = (byte)(avg + (ri < remainder ? 1 : 0)); ri++; if (nl != v2.waterLevel) { v2.waterLevel = nl; voxels[n2] = v2; any = true; } }
                            if (!v3.IsSolid) { byte nl = (byte)(avg + (ri < remainder ? 1 : 0)); ri++; if (nl != v3.waterLevel) { v3.waterLevel = nl; voxels[n3] = v3; any = true; } }
                        }
                    }
                }
            }

            changed[0] = any ? 1 : 0;
        }

        private static int Pad(int x, int y, int z, int SP)
        {
            // Clamp to padded range (-1 .. CHUNK_SIZE) → (0 .. CHUNK_SIZE_P-1)
            int px = x + 1; if (px < 0) px = 0; if (px >= SP) px = SP - 1;
            int py = y + 1; if (py < 0) py = 0; if (py >= SP) py = SP - 1;
            int pz = z + 1; if (pz < 0) pz = 0; if (pz >= SP) pz = SP - 1;
            return px + py * SP + pz * SP * SP;
        }
    }
}
