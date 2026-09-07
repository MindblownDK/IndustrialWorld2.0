// Assets/Scripts/VoxelEngine/Items/ProspectingScanner.cs
//
// Geological Prospecting Scanner — a handheld acoustic radar tool for field prospectors.
// RMB emits a focused subterranean acoustic ping along the planetary radial down vector,
// scanning sub-surface voxel layers (up to 24m depth) for valuable mineral deposits.

using UnityEngine;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Prospecting Scanner", fileName = "Tool_ProspectingScanner")]
    public class ProspectingScanner : ToolItem
    {
        [Header("Prospecting Radar")]
        [Tooltip("Maximum depth to probe below the scanned surface (meters).")]
        public float scanDepthMeters = 24f;

        [Tooltip("Horizontal radius of the subterranean scan cone (meters).")]
        public float scanRadiusMeters = 6f;

        public ProspectingScanner()
        {
            toolType = ToolType.Other;
            maxDurability = 120; // 120 pings per scanner
            maxStack = 1;
            category = "Tools";
            displayName = "Geological Prospecting Scanner";
            itemId = "tool_prospecting_scanner";
            description = "Handheld acoustic radar scanner. Right-click the ground to ping for sub-surface ore deposits up to 24 meters deep.";
            iconTint = new Color(0.35f, 0.85f, 1.0f);
        }
    }
}
