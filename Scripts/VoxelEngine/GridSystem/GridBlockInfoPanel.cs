// Assets/Scripts/VoxelEngine/GridSystem/GridBlockInfoPanel.cs
//
// Detail panel showing stats and controls for a selected block.

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.GridSystem
{
    public class GridBlockInfoPanel : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<GridBlockInfoPanel> { }

        private Label _nameLabel;
        private Label _statsLabel;
        private Toggle _powerToggle;

        public GridBlockInfoPanel()
        {
            _nameLabel = new Label();
            _statsLabel = new Label();
            _powerToggle = new Toggle("Enabled");

            Add(_nameLabel);
            Add(_statsLabel);
            Add(_powerToggle);
        }

        public void ShowBlock(GridBlock block)
        {
            _nameLabel.text = block.blockName;
            _statsLabel.text = $"Mass: {block.BlockMass}kg\nHP: {block.currentHP}/{block.maxHP}";

            // Example: Show power draw
            if (block is GridH2O2Generator gen)
            {
                _statsLabel.text += $"\nPower Draw: {gen.powerDraw}W";
            }

            _powerToggle.value = true; // TODO: Bind to actual enabled state
        }
    }
}