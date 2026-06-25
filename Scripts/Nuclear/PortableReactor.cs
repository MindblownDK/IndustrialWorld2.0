// Assets/Scripts/VoxelEngine/Nuclear/PortableReactor.cs
//
// Small RTG-style reactor. Uses LEU pellets + ice for direct electricity.
// No water pipes needed — just ice in the input and fuel rods.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Nuclear
{
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PowerGenerator))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class PortableReactor : MonoBehaviour, IItemPortHost
    {
        [Header("Fuel")]
        public ItemDefinition leuPelletItem;
        public float pelletBurnTime = 300f;
        public ItemDefinition wasteItem;

        [Header("Coolant")]
        [Tooltip("Ice item used as coolant (melts slowly).")]
        public ItemDefinition iceItem;
        [Tooltip("Ice consumed per fuel pellet.")]
        public int icePerPellet = 2;

        [Header("Power Output")]
        public float wattsOutput = 800f;

        [Header("Containers")]
        public ItemContainer fuelC;
        public ItemContainer iceC;
        public ItemContainer wasteC;

        public float FuelRemaining01 { get; private set; } = 1f;
        public bool IsRunning { get; private set; }

        private PowerGenerator _gen;
        private float _burnTimer;

        private void Awake()
        {
            EnsureContainers();
            _gen = GetComponent<PowerGenerator>();
        }

        public void EnsureContainers()
        {
            if (fuelC == null) fuelC = new ItemContainer("LEU Fuel", 2);
            else fuelC.Resize(2);
            if (iceC == null) iceC = new ItemContainer("Ice Coolant", 4);
            else iceC.Resize(4);
            if (wasteC == null) wasteC = new ItemContainer("Waste", 4);
            else wasteC.Resize(4);
        }

        // ── IItemPortHost ───────────────────────────────────────────────────
        private PortConfig _portConfig;
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
            _portContainers ??= new ItemPortContainer[3];
            _portContainers[0] = new ItemPortContainer("LEU Fuel",    fuelC,  canInput: true,  canOutput: false);
            _portContainers[1] = new ItemPortContainer("Ice Coolant", iceC,   canInput: true,  canOutput: false);
            _portContainers[2] = new ItemPortContainer("Waste",       wasteC, canInput: false, canOutput: true);
            return _portContainers;
        }

        private void Update()
        {
            EnsureContainers();
            bool hasFuel = leuPelletItem != null && fuelC.CountOf(leuPelletItem) > 0;
            bool hasIce = iceItem != null && iceC.CountOf(iceItem) >= icePerPellet;

            if (hasFuel && hasIce)
            {
                IsRunning = true;
                _gen.wattsPerSecond = wattsOutput;
                _gen.isOn = true;
                _burnTimer += Time.deltaTime;
                FuelRemaining01 = 1f - Mathf.Clamp01(_burnTimer / pelletBurnTime);

                if (_burnTimer >= pelletBurnTime)
                {
                    fuelC.Remove(leuPelletItem, 1);
                    iceC.Remove(iceItem, icePerPellet);
                    if (wasteItem != null) wasteC.Insert(new ItemStack(wasteItem, 1));
                    _burnTimer = 0f;
                }
            }
            else
            {
                IsRunning = false;
                _gen.isOn = false;
                _gen.wattsPerSecond = 0;
            }
        }
    }
}
