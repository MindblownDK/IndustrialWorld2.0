// Assets/Scripts/VoxelEngine/Building/Chest.cs
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building
{
    /// <summary>
    /// Simple storage container. Press the Interact key while looking at it to open
    /// the player's inventory + this chest's container side-by-side.
    /// </summary>
    public class Chest : MonoBehaviour
    {
        [Tooltip("Number of slots inside this chest.")]
        public int size = 30;
        [Tooltip("Display name shown above the panel.")]
        public string displayName = "Chest";

        public ItemContainer container;

        private void Awake()
        {
            if (container == null) container = new ItemContainer(displayName, size);
            else container.Resize(size);
        }
    }
}
