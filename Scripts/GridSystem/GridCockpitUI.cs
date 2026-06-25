// Assets/Scripts/VoxelEngine/GridSystem/GridCockpitUI.cs
//
// Cockpit UI with grid size switching buttons.

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.GridSystem
{
    public class GridCockpitUI : MonoBehaviour
    {
        public UIDocument uiDocument;
        public GridCockpit cockpit;

        private void Start()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            var root = uiDocument.rootVisualElement;

            var smallBtn = root.Q<Button>("SmallGridButton");
            var largeBtn = root.Q<Button>("LargeGridButton");

            if (smallBtn != null)
                smallBtn.clicked += () => cockpit?.SwitchToSmallGrid();

            if (largeBtn != null)
                largeBtn.clicked += () => cockpit?.SwitchToLargeGrid();
        }
    }
}