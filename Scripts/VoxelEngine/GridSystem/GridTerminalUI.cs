// Assets/Scripts/VoxelEngine/GridSystem/GridTerminalUI.cs
//
// Grid Terminal with drag-and-drop support between containers.

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.GridSystem
{
    public class GridTerminalUI : MonoBehaviour
    {
        public static GridTerminalUI Instance { get; private set; }

        public UIDocument uiDocument;
        public VisualTreeAsset terminalUxml;

        private VisualElement _root;
        private ScrollView _blockList;

        private GridEntity _currentGrid;

        private void Awake()
        {
            if (Instance != null && Instance != this) Destroy(gameObject);
            else Instance = this;
        }

        public void Open(GridEntity grid)
        {
            _currentGrid = grid;

            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (terminalUxml != null && uiDocument.rootVisualElement.childCount == 0)
                terminalUxml.CloneTree(uiDocument.rootVisualElement);

            _root = uiDocument.rootVisualElement;
            _root.style.display = DisplayStyle.Flex;

            _blockList = _root.Q<ScrollView>("BlockList");
            BuildBlockList();
        }

        public void Close()
        {
            if (_root != null) _root.style.display = DisplayStyle.None;
        }

        private void BuildBlockList()
        {
            _blockList.Clear();

            foreach (var kv in _currentGrid.Blocks)
            {
                var block = kv.Value;
                var btn = new Button { text = block.blockName };
                btn.clicked += () => OpenBlockInfo(block);
                _blockList.Add(btn);
            }
        }

        private void OpenBlockInfo(GridBlock block)
        {
            // TODO: Show detailed info panel
            Debug.Log($"Selected block: {block.blockName}");
        }
    }
}