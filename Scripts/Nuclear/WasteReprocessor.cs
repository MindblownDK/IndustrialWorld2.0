// Assets/Scripts/VoxelEngine/Nuclear/WasteReprocessor.cs
//
// Reprocesses nuclear waste:
//   - Spent fuel rods → recoverable uranium + high-level waste
//   - Depleted uranium → usable LEU pellets (at lower efficiency)
//
// Based on real PUREX (Plutonium Uranium Reduction EXtraction) process.
// Requires power. Slow process (reflects real 2-year cooling + processing).

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;
using System.Collections.Generic;

namespace VoxelEngine.Nuclear
{
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class WasteReprocessor : MonoBehaviour, IItemPortHost
    {
        [Header("Processing")]
        public float processTime = 60f;

        [Header("Spent Fuel Rod Reprocessing")]
        public ItemDefinition spentFuelRodItem;
        public ItemDefinition recoveredUranium;
        public int recoveredAmount = 2;
        public ItemDefinition highLevelWaste;

        [Header("Depleted Uranium Reprocessing")]
        public ItemDefinition depletedUraniumItem;
        public ItemDefinition outputLeuPellet;
        public int leuFromDepleted = 1;

        [Header("Containers")]
        public ItemContainer inputC;
        public ItemContainer outputC;
        public ItemContainer wasteOutputC;

        public float Progress01 => _timer / Mathf.Max(0.01f, processTime);
        public bool IsProcessing { get; private set; }

        private float _timer;
        private PowerConsumer _power;
        private enum Mode { None, SpentFuel, DepletedUranium }
        private Mode _mode = Mode.None;

        private void Awake()
        {
            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
        }

        public void EnsureContainers()
        {
            if (inputC == null) inputC = new ItemContainer("Input", 2);
            else inputC.Resize(2);
            if (outputC == null) outputC = new ItemContainer("Output", 4);
            else outputC.Resize(4);
            if (wasteOutputC == null) wasteOutputC = new ItemContainer("HL Waste", 2);
            else wasteOutputC.Resize(2);
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
            _portContainers[0] = new ItemPortContainer("Input",    inputC,       canInput: true,  canOutput: false);
            _portContainers[1] = new ItemPortContainer("Output",   outputC,      canInput: false, canOutput: true);
            _portContainers[2] = new ItemPortContainer("HL Waste", wasteOutputC, canInput: false, canOutput: true);
            return _portContainers;
        }

        private void Update()
        {
            EnsureContainers();
            if (_power != null && !_power.IsPowered) { IsProcessing = false; return; }

            // Determine what we can process.
            if (_mode == Mode.None)
            {
                if (spentFuelRodItem != null && inputC.CountOf(spentFuelRodItem) > 0)
                    _mode = Mode.SpentFuel;
                else if (depletedUraniumItem != null && inputC.CountOf(depletedUraniumItem) > 0)
                    _mode = Mode.DepletedUranium;
                else { IsProcessing = false; _timer = 0; return; }
            }

            IsProcessing = true;
            _timer += Time.deltaTime;

            if (_timer >= processTime)
            {
                _timer = 0f;
                switch (_mode)
                {
                    case Mode.SpentFuel:
                        inputC.Remove(spentFuelRodItem, 1);
                        if (recoveredUranium != null)
                            outputC.Insert(new ItemStack(recoveredUranium, recoveredAmount));
                        if (highLevelWaste != null)
                            wasteOutputC.Insert(new ItemStack(highLevelWaste, 1));
                        break;

                    case Mode.DepletedUranium:
                        inputC.Remove(depletedUraniumItem, 1);
                        if (outputLeuPellet != null)
                            outputC.Insert(new ItemStack(outputLeuPellet, leuFromDepleted));
                        break;
                }
                _mode = Mode.None; // re-evaluate next tick
            }
        }
    }
}
