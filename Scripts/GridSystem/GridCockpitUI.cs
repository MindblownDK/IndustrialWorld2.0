// Assets/Scripts/VoxelEngine/GridSystem/GridCockpitUI.cs
//
// Cockpit UI for the unified grid workflow. Legacy scale-switch controls are
// hidden; structural and detail blocks now share one construct.

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
            if (uiDocument == null) return;

            var root = uiDocument.rootVisualElement;
            if (root == null) return;

            var smallButton = root.Q<Button>("SmallGridButton");
            var largeButton = root.Q<Button>("LargeGridButton");
            if (smallButton != null) smallButton.style.display = DisplayStyle.None;
            if (largeButton != null) largeButton.style.display = DisplayStyle.None;

            var modeLabel = root.Q<Label>("UnifiedGridLabel");
            if (modeLabel == null)
            {
                modeLabel = new Label("UNIFIED GRID · DETAIL + STRUCTURAL") { name = "UnifiedGridLabel" };
                modeLabel.style.fontSize = 10;
                modeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                modeLabel.style.letterSpacing = 1.1f;
                modeLabel.style.color = new StyleColor(new Color(0.20f, 0.78f, 0.96f));
                modeLabel.style.marginTop = 4;
                modeLabel.style.marginBottom = 4;
                root.Add(modeLabel);
            }
        }
    }
}
