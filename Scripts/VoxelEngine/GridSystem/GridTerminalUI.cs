// Assets/Scripts/VoxelEngine/GridSystem/GridTerminalUI.cs
//
// Main Grid Terminal (Space Engineers style).
// Shows all blocks on the grid and allows selection.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.GridSystem
{
    public class GridTerminalUI : MonoBehaviour
    {
        public static GridTerminalUI Instance { get; private set; }

        [Header("UI Document")]
        public UIDocument uiDocument;

        private VisualElement _root;
        private ScrollView _blockList;
        private GridBlockInfoPanel _infoPanel;

        private GridEntity _currentGrid;
        private List<Button> _blockButtons = new List<Button>();

        private void Awake()
        {
            if (Instance != null && Instance != this) Destroy(gameObject);
            else Instance = this;
        }

        public void Open(GridEntity grid)
        {
            _currentGrid = grid;
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;

            _root = uiDocument.rootVisualElement;
            _root.style.display = DisplayStyle.Flex;

            BuildBlockList();
        }

        public void Close()
        {
            if (_root != null)
                _root.style.display = DisplayStyle.None;
        }

        private void BuildBlockList()
        {
            _blockList = _root.Q<ScrollView>("BlockList");
            _blockList.Clear();
            _blockButtons.Clear();

            if (_currentGrid == null) return;

            foreach (var kv in _currentGrid.Blocks)
            {
                var block = kv.Value;
                var button = new Button();
                button.text = block.blockName;
                button.clicked += () => ShowBlockInfo(block);

                _blockList.Add(button);
                _blockButtons.Add(button);
            }
        }

        private void ShowBlockInfo(GridBlock block)
        {
            if (_infoPanel == null)
                _infoPanel = _root.Q<GridBlockInfoPanel>("BlockInfoPanel");

            if (_infoPanel != null)
                _infoPanel.ShowBlock(block);
        }
    }
}