// Assets/Scripts/VoxelEngine/UI/QuarryHud.cs
//
// Thin static bridge — panel building delegated to MachineUIs, Tick() here.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.Transport;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class QuarryHud
    {
        /// <summary>Called every frame by GameUIController for live label updates.</summary>
        public static void Tick(Quarry q)
        {
            if (q == null) return;
            // MachineUIs.QuarryPanel rebuilds periodically with fresh stats.
            // Tick here is a no-op; all updates happen through the rebuild cycle
            // which fires at ~1 Hz or when container contents change.
        }
    }
}
