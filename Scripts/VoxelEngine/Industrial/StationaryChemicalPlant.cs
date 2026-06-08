// Assets/Scripts/VoxelEngine/Industrial/StationaryChemicalPlant.cs
//
// Stationary Chemical Plant - Placeable in the world.
// Large grid only equivalent.

using UnityEngine;

namespace VoxelEngine.Industrial
{
    public class StationaryChemicalPlant : MonoBehaviour
    {
        [Header("Chemical Plant")]
        public float powerDraw = 720f;
        public float mixRate = 3.5f;

        private void FixedUpdate()
        {
        }
    }
}