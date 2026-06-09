// Assets/Scripts/VoxelEngine/GridSystem/GridBlockInfoPanel.cs
//
// Shows real stats from blocks (power, tanks, etc.)

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.GridSystem
{
    [UxmlElement]
    public partial class GridBlockInfoPanel : VisualElement
    {
        private Label _nameLabel;
        private Label _statsLabel;
        private Toggle _enabledToggle;

        public GridBlockInfoPanel()
        {
            _nameLabel = new Label { style = { fontSize = 20, color = Color.white } };
            _statsLabel = new Label { style = { color = new Color(0.8f, 0.8f, 0.8f) } };
            _enabledToggle = new Toggle("Enabled");

            Add(_nameLabel);
            Add(_statsLabel);
            Add(_enabledToggle);
        }

        public void ShowBlock(GridBlock block)
        {
            _nameLabel.text = block.blockName;
            string stats = $"Mass: {block.BlockMass} kg\nHP: {block.currentHP}/{block.maxHP}";

            // Real data from specific block types
            if (block is GridH2O2Generator gen)
            {
                stats += $"\nPower Draw: {gen.powerDraw} W";
            }
            else if (block is GridLiquidFuelTank tank)
            {
                stats += $"\nFuel: {tank.fuelStored:0} / {tank.capacity:0}";
            }
            else if (block is GridWaterTank water)
            {
                stats += $"\nWater: {water.waterStored:0} / {water.capacity:0}";
            }
            else if (block is GridRefinery refinery)
            {
                stats += $"\nPower Draw: {refinery.PowerDraw:0} W";
                if (refinery.Current != null) stats += $"\nRefining: {refinery.Current.GetDisplayName()} ({refinery.Progress01 * 100f:0}%)";
            }
            else if (block is GridChemicalPlant chem)
            {
                stats += $"\nPower Draw: {chem.PowerDraw:0} W";
                if (chem.Current != null) stats += $"\nMixing: {chem.Current.GetDisplayName()} ({chem.Progress01 * 100f:0}%)";
            }

            _statsLabel.text = stats;
        }
    }
}