// Assets/Scripts/VoxelEngine/Networks/WrenchTool.cs
//
// Player tool for connecting/disconnecting network anchors.
// LMB on an anchor = select first, then LMB on second = connect them.
// RMB on a connection = disconnect.
// Uses raycasting from the player camera.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Networks
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Wrench", fileName = "Tool_Wrench")]
    public class WrenchTool : ToolItem
    {
        public WrenchTool() { toolType = ToolType.Other; maxDurability = 1000; maxStack = 1; }
    }

    /// <summary>
    /// Runtime wrench interaction handler. Auto-attached to PlayerInteractionTool.
    /// </summary>
    public class WrenchInteraction
    {
        private ConnectionAnchor _selectedAnchor;
        private float _selectedTime;

        /// <summary>Called when the player LMB with the wrench.</summary>
        public void OnUse(RaycastHit hit, Player.PlayerInteractionTool tool)
        {
            var anchor = hit.collider.GetComponentInParent<ConnectionAnchor>();
            if (anchor == null)
            {
                ClearSelection();
                return;
            }

            if (_selectedAnchor == null)
            {
                // First click: select this anchor.
                _selectedAnchor = anchor;
                _selectedTime = Time.time;
                UI.BuildFeedbackHud.Show("Wrench: Selected",
                    $"{anchor.networkType} anchor",
                    null, UI.UITheme.AccentCyan);
            }
            else
            {
                // Second click: try to connect.
                if (_selectedAnchor == anchor)
                {
                    ClearSelection();
                    return;
                }

                if (_selectedAnchor.TryConnect(anchor))
                {
                    UI.BuildFeedbackHud.Show("Connected!",
                        $"{anchor.networkType} network",
                        null, UI.UITheme.AccentGreen);
                }
                else
                {
                    UI.BuildFeedbackHud.Show("Cannot connect",
                        "Incompatible types",
                        null, UI.UITheme.AccentRed);
                }
                ClearSelection();
            }
        }

        /// <summary>Called when the player RMB with the wrench — disconnect.</summary>
        public void OnAltUse(RaycastHit hit)
        {
            var anchor = hit.collider.GetComponentInParent<ConnectionAnchor>();
            if (anchor == null) return;

            if (anchor.connections.Count > 0)
            {
                anchor.DisconnectAll();
                UI.BuildFeedbackHud.Show("Disconnected",
                    $"All {anchor.networkType} connections removed",
                    null, UI.UITheme.AccentOrange);
            }
        }

        private void ClearSelection()
        {
            _selectedAnchor = null;
        }

        /// <summary>Auto-clear selection after 5 seconds.</summary>
        public void Tick()
        {
            if (_selectedAnchor != null && Time.time - _selectedTime > 5f)
                ClearSelection();
        }
    }
}
