// Assets/Scripts/VoxelEngine/Gas/GasPipe.cs
//
// Universal gas transport pipe. Carries steam, hydrogen, oxygen between
// machines. Auto-connects to neighbours within connectRadius.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Gas
{
    [RequireComponent(typeof(PlacedBlock))]
    public class GasPipe : MonoBehaviour
    {
        [Tooltip("Max pressure this pipe can handle (arbitrary units).")]
        public float maxPressure = 100f;
        public float connectRadius = 3.0f;

        [System.NonSerialized] public List<GasPipe> neighbours = new();

        private void OnEnable()  { GasNetwork.EnsureInstance(); GasNetwork.Instance?.Register(this); }
        private void OnDisable() => GasNetwork.Instance?.Unregister(this);
    }
}
