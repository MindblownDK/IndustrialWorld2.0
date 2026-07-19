// Assets/Scripts/VoxelEngine/GridSystem/UnifiedGridTopology.cs
//
// Shared physical adjacency and screen-address helpers for the one-Grid system.
// Detail and Structural blocks are matched by touching world-space faces rather
// than by assuming one coordinate scale for the complete construct.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public static class UnifiedGridTopology
    {
        private const int PrecisionAddressMarker = 1000000000;
        private const int PrecisionAddressThreshold = 900000000;

        public static IEnumerable<GridBlock> AdjacentBlocks(GridEntity grid, GridBlock source)
        {
            if (grid == null || source == null) yield break;

            Vector3 sourceCenter = grid.transform.InverseTransformPoint(source.transform.position);
            float sourceHalf = source.EffectiveCellSize * 0.5f;
            foreach (var candidate in grid.AllBlocks)
            {
                if (candidate == null || candidate == source) continue;
                Vector3 candidateCenter = grid.transform.InverseTransformPoint(candidate.transform.position);
                float combinedHalf = sourceHalf + candidate.EffectiveCellSize * 0.5f;
                Vector3 delta = candidateCenter - sourceCenter;
                float ax = Mathf.Abs(delta.x);
                float ay = Mathf.Abs(delta.y);
                float az = Mathf.Abs(delta.z);
                const float touchTolerance = 0.035f;
                const float overlapEpsilon = 0.002f;

                bool touchX = Mathf.Abs(ax - combinedHalf) <= touchTolerance
                    && ay < combinedHalf - overlapEpsilon && az < combinedHalf - overlapEpsilon;
                bool touchY = Mathf.Abs(ay - combinedHalf) <= touchTolerance
                    && ax < combinedHalf - overlapEpsilon && az < combinedHalf - overlapEpsilon;
                bool touchZ = Mathf.Abs(az - combinedHalf) <= touchTolerance
                    && ax < combinedHalf - overlapEpsilon && ay < combinedHalf - overlapEpsilon;
                if (touchX || touchY || touchZ) yield return candidate;
            }
        }

        public static Vector3Int AddressOf(GridBlock block)
        {
            if (block == null) return default;
            return block.IsPrecisionAttachment
                ? EncodePrecisionAddress(block.PrecisionGridPos)
                : block.GridPos;
        }

        public static bool TryResolveAddress(GridEntity grid, Vector3Int address, out GridBlock block)
        {
            block = null;
            if (grid == null) return false;
            if (IsPrecisionAddress(address))
            {
                var layer = grid.PrecisionAttachments;
                if (layer == null) return false;
                block = layer.GetBlock(DecodePrecisionAddress(address));
                return block != null;
            }

            block = grid.GetBlock(address);
            return block != null;
        }

        public static Vector3Int EncodePrecisionAddress(Vector3Int precisionPos)
            => new(precisionPos.x, PrecisionAddressMarker + precisionPos.y, precisionPos.z);

        public static bool IsPrecisionAddress(Vector3Int address)
            => address.y >= PrecisionAddressThreshold;

        public static Vector3Int DecodePrecisionAddress(Vector3Int address)
            => new(address.x, address.y - PrecisionAddressMarker, address.z);
    }
}
