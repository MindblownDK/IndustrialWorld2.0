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

        public static bool TryGetDetailPlacement(
            GridEntity grid,
            RaycastHit hit,
            out Vector3Int precisionPos,
            out Vector3Int hostStructuralPos,
            out Vector3Int faceAxis)
        {
            precisionPos = default;
            hostStructuralPos = default;
            faceAxis = default;
            if (grid == null || hit.collider == null) return false;

            faceAxis = SnapFaceAxis(grid, hit.normal);
            Vector3 localNormal = ((Vector3)faceAxis).normalized;
            var hitBlock = hit.collider.GetComponentInParent<GridBlock>();
            bool chainedDetail = hitBlock != null && hitBlock.Grid == grid && hitBlock.IsPrecisionAttachment;

            if (chainedDetail)
            {
                precisionPos = hitBlock.PrecisionGridPos + faceAxis;
                hostStructuralPos = hitBlock.PrecisionHostGridPos;
            }
            else
            {
                float detailSize = GridSize.Small.CellSize();
                Vector3 localCenter = grid.transform.InverseTransformPoint(hit.point)
                    + localNormal * (detailSize * 0.5f);
                precisionPos = new Vector3Int(
                    Mathf.RoundToInt(localCenter.x / detailSize),
                    Mathf.RoundToInt(localCenter.y / detailSize),
                    Mathf.RoundToInt(localCenter.z / detailSize));
                hostStructuralPos = hitBlock != null && hitBlock.Grid == grid
                    ? hitBlock.GridPos
                    : grid.WorldToGrid(hit.point - hit.normal * 0.02f);
            }

            var layer = grid.PrecisionAttachments;
            bool supported = chainedDetail
                ? layer != null && layer.HasNeighbor(precisionPos)
                : hitBlock != null && hitBlock.Grid == grid;
            if (!supported || (layer != null && !layer.CanPlace(precisionPos))) return false;

            if (chainedDetail)
            {
                Vector3 localPosition = (Vector3)precisionPos * GridSize.Small.CellSize();
                Vector3Int structuralCell = new(
                    Mathf.RoundToInt(localPosition.x / GridSize.Large.CellSize()),
                    Mathf.RoundToInt(localPosition.y / GridSize.Large.CellSize()),
                    Mathf.RoundToInt(localPosition.z / GridSize.Large.CellSize()));
                if (grid.GetBlock(structuralCell) != null) return false;
            }

            return true;
        }

        public static Vector3Int SnapFaceAxis(GridEntity grid, Vector3 worldNormal)
        {
            if (grid == null || worldNormal.sqrMagnitude < 0.0001f) return Vector3Int.up;
            Vector3 local = grid.transform.InverseTransformDirection(worldNormal.normalized);
            float ax = Mathf.Abs(local.x);
            float ay = Mathf.Abs(local.y);
            float az = Mathf.Abs(local.z);
            if (ax >= ay && ax >= az) return new Vector3Int(local.x >= 0f ? 1 : -1, 0, 0);
            if (ay >= ax && ay >= az) return new Vector3Int(0, local.y >= 0f ? 1 : -1, 0);
            return new Vector3Int(0, 0, local.z >= 0f ? 1 : -1);
        }
    }
}
