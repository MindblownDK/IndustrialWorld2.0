// Assets/Scripts/VoxelEngine/Power/Floodlight.cs
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Power
{
    /// <summary>
    /// A power-dependent light source.
    /// Toggles the associated light component based on the PowerConsumer's state.
    /// </summary>
    [RequireComponent(typeof(PowerConsumer))]
    public class Floodlight : MonoBehaviour
    {
        [Tooltip("The light source to toggle. Auto-found if null.")]
        public Light lightComponent;

        private PowerConsumer _consumer;

        private void Awake()
        {
            _consumer = GetComponent<PowerConsumer>();
            if (lightComponent == null) lightComponent = GetComponentInChildren<Light>();
        }

        private void Update()
        {
            if (lightComponent != null && _consumer != null)
            {
                lightComponent.enabled = _consumer.IsPowered;
            }
        }
    }
}
