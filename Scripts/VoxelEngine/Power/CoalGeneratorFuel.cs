// Assets/Scripts/VoxelEngine/Power/CoalGeneratorFuel.cs
//
// Adds a fuel slot + on/off behaviour to a PowerGenerator. The generator only produces
// power while a fuel item is burning. Burn time comes from ResourceItem.fuelSeconds.
// Now uses PortConfig for face-based connection control.

using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Transport;

namespace VoxelEngine.Power
{
    [RequireComponent(typeof(PowerGenerator))]
    [RequireComponent(typeof(PortConfig))]
    public class CoalGeneratorFuel : MonoBehaviour
    {
        public ItemContainer fuelC;
        public float fuelRemaining;
        public float fuelMaxDuration;

        /// <summary>
        /// Player-controlled hard-disable toggle. When false the generator
        /// stops consuming fuel and sets the PowerGenerator to off, even if
        /// there's fuel in the input slot. Exposed via the UI's "ENABLED"
        /// pill to the left of the status badge.
        /// </summary>
        public bool userEnabled = true;

        public bool IsBurning => userEnabled && fuelRemaining > 0f;
        public float FuelProgress01 => fuelMaxDuration > 0 ? Mathf.Clamp01(fuelRemaining / fuelMaxDuration) : 0f;

        private PowerGenerator _gen;
        private PortConfig _portConfig;

        private void Awake()
        {
            EnsureContainers();
            _gen = GetComponent<PowerGenerator>();
            _portConfig = GetComponent<PortConfig>();

            // Setup default ports - only power output on +X face
            SetupDefaultPorts();
        }

        private void OnEnable()
        {
            // Refresh port indicators after setup
            if (_portConfig != null)
                _portConfig.RefreshIndicators();
        }

        /// <summary>
        /// Configure ports: only power cables can connect, and only as output.
        /// Adjust these to match your machine's design.
        /// </summary>
        private void SetupDefaultPorts()
        {
            if (_portConfig == null) return;

            // Set all faces to None first
            for (int i = 0; i < 6; i++)
            {
                var face = (CubeFace)i;
                _portConfig.SetDirection(face, PortDirection.None);
                _portConfig.SetNetworkType(face, PortNetworkType.Power);
                _portConfig.SetFaceEnabled(face, false); // Disable all faces by default
            }

            // Enable only the faces you want for output
            // Example: Enable +X and -Z faces for power output
            _portConfig.SetDirection(CubeFace.PosX, PortDirection.Output);
            _portConfig.SetNetworkType(CubeFace.PosX, PortNetworkType.Power);
            _portConfig.SetFaceEnabled(CubeFace.PosX, true);

            _portConfig.SetDirection(CubeFace.NegZ, PortDirection.Output);
            _portConfig.SetNetworkType(CubeFace.NegZ, PortNetworkType.Power);
            _portConfig.SetFaceEnabled(CubeFace.NegZ, true);
        }

        public void EnsureContainers()
        {
            if (fuelC == null) fuelC = new ItemContainer("Fuel", 1);
            else fuelC.Resize(1);
        }

        private void Update()
        {
            EnsureContainers();
            if (_gen == null) _gen = GetComponent<PowerGenerator>();

            // Player toggled the generator OFF — freeze fuel, stop generating.
            if (!userEnabled)
            {
                _gen.isOn = false;
                return;
            }

            // Burn down current fuel.
            if (fuelRemaining > 0f)
            {
                fuelRemaining -= Time.deltaTime;
                _gen.isOn = true;
            }
            else
            {
                _gen.isOn = false;
                // Try consume another fuel item.
                var s = fuelC.GetSlot(0);
                if (!s.IsEmpty && s.item is ResourceItem ri && ri.fuelSeconds > 0f)
                {
                    fuelRemaining = ri.fuelSeconds;
                    fuelMaxDuration = ri.fuelSeconds;
                    fuelC.Remove(ri, 1);
                    _gen.isOn = true;
                }
            }
        }
    }
}
