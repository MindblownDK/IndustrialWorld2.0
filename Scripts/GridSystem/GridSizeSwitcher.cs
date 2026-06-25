// Assets/Scripts/VoxelEngine/GridSystem/GridSizeSwitcher.cs
//
// Full Small ↔ Large grid switching system.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridSizeSwitcher : MonoBehaviour
    {
        public static GridSizeSwitcher Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) Destroy(gameObject);
            else Instance = this;
        }

        /// <summary>
        /// Switches an entire grid from one size to another.
        /// </summary>
        public void SwitchGrid(GridEntity grid, GridSize targetSize)
        {
            if (grid == null || grid.gridSize == targetSize) return;

            List<GridBlock> oldBlocks = new List<GridBlock>(grid.Blocks.Values);
            Vector3 originalPosition = grid.transform.position;

            Destroy(grid.gameObject);

            GridEntity newGrid = GridEntity.Create(originalPosition, targetSize);

            foreach (var oldBlock in oldBlocks)
            {
                Vector3 worldPos = oldBlock.transform.position;
                Vector3Int newGridPos = newGrid.WorldToGrid(worldPos);

                GridBlock newBlock = GridBlock.CreateBlock<GridBlock>(
                    oldBlock.blockName,
                    targetSize,
                    Color.gray
                );

                newBlock.blockName = oldBlock.blockName;
                newBlock.BlockMass = CalculateNewMass(oldBlock.BlockMass, targetSize);
                newBlock.maxHP = oldBlock.maxHP;

                newGrid.AddBlock(newGridPos, newBlock);
            }

            Debug.Log($"[GridSizeSwitcher] Grid converted to {targetSize}");
        }

        private float CalculateNewMass(float oldMass, GridSize newSize)
        {
            return newSize == GridSize.Large ? oldMass * 5f : oldMass / 5f;
        }
    }
}