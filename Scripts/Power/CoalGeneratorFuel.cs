// Assets/Scripts/VoxelEngine/Power/CoalGeneratorFuel.cs
//
// Adds a fuel slot + on/off behaviour to a PowerGenerator. The generator only produces
// power while a fuel item is burning. Burn time comes from ResourceItem.fuelSeconds.
// Now uses PortConfig for face-based connection control.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Transport;

namespace VoxelEngine.Power
{
    [RequireComponent(typeof(PowerGenerator))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class CoalGeneratorFuel : MonoBehaviour, IItemPortHost
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

        public bool IsBurning => userEnabled && fuelRemaining > 0f && !IsPausedByFullBattery;
        public bool IsPausedByFullBattery { get; private set; }
        public float BatteryFill01 { get; private set; }
        public bool HasNetworkBattery { get; private set; }
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

        // ── IItemPortHost ───────────────────────────────────────────────────
        // Reuses the existing _portConfig field. Exposes the single Fuel slot so
        // pipes can auto-feed coal/wood through configured INPUT faces.
        private ItemPortContainer[] _portContainers;

        public PortConfig PortConfig
        {
            get
            {
                if (_portConfig == null)
                {
                    _portConfig = GetComponent<PortConfig>();
                    if (_portConfig == null) _portConfig = gameObject.AddComponent<PortConfig>();
                    _portConfig.EnsureAllFaces();
                }
                return _portConfig;
            }
        }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsureContainers();
            _portContainers ??= new ItemPortContainer[1];
            _portContainers[0] = new ItemPortContainer("Fuel", fuelC, canInput: true, canOutput: false);
            return _portContainers;
        }

        private void RefreshBatteryPauseState()
        {
            IsPausedByFullBattery = false;
            HasNetworkBattery = false;
            BatteryFill01 = 0f;
            if (_gen == null || _gen.network == null) return;

            float capacity = 0f;
            float charge = 0f;
            foreach (var node in _gen.network.nodes)
            {
                if (node is PowerBattery battery)
                {
                    HasNetworkBattery = true;
                    capacity += Mathf.Max(0f, battery.capacityWattHours);
                    charge += Mathf.Clamp(battery.charge, 0f, Mathf.Max(0f, battery.capacityWattHours));
                }
            }

            BatteryFill01 = capacity > 0f ? Mathf.Clamp01(charge / capacity) : 0f;
            // Pause at full reserve even if idle consumers exist. The battery will
            // supply the next bit of demand, drop below full, and wake the generator
            // on a later tick only when stored power is actually being used.
            IsPausedByFullBattery = HasNetworkBattery && BatteryFill01 >= 0.999f;
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

            RefreshBatteryPauseState();

            // Burn down current fuel. If the connected battery reserve is full
            // and no consumer is requesting power, pause fuel burn until demand
            // appears again.
            if (fuelRemaining > 0f)
            {
                if (IsPausedByFullBattery)
                {
                    _gen.isOn = false;
                    return;
                }

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
