// Assets/Scripts/VoxelEngine/Industrial/StationaryRefinery.cs
//
// Stationary Refinery - Placeable in the world (not on grids).
// Large and expensive. Cannot be placed on Small grids.

using UnityEngine;

namespace VoxelEngine.Industrial
{
    public class StationaryRefinery : MonoBehaviour
    {
        [Header("Refinery")]
        public float crudeConsumptionRate = 8f;
        public float keroseneProductionRate = 5f;
        public float powerDraw = 850f;

        private bool _isProcessing;

        private void FixedUpdate()
        {
            _isProcessing = true; // Would check power network here
        }
    }
}