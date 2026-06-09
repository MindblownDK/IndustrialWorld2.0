// Assets/Scripts/VoxelEngine/Gas/Electrolyser.cs
//
// Hydrogen/Oxygen generator. Electrolyses ice (H₂O) into hydrogen and oxygen.
// Requires power. Has internal buffer tanks for both gases.
//
// If only a hydrogen tank/engine is connected: 100% hydrogen output.
// If only an oxygen tank is connected: 100% oxygen output.
// If both are connected: 50/50 split (scientifically 2:1 H₂:O₂ by moles).
// Separate gas outputs for each type.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;
using System.Collections.Generic;

namespace VoxelEngine.Gas
{
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class Electrolyser : MonoBehaviour, IItemPortHost
    {
        [Header("Input")]
        [Tooltip("Ice item consumed for electrolysis.")]
        public ItemDefinition iceItem;
        [Tooltip("Ice consumed per cycle.")]
        public int icePerCycle = 1;
        [Tooltip("Seconds per electrolysis cycle.")]
        public float cycleTime = 10f;

        [Header("Output per cycle")]
        [Tooltip("Gas units of hydrogen produced per ice.")]
        public float hydrogenPerCycle = 20f;
        [Tooltip("Gas units of oxygen produced per ice.")]
        public float oxygenPerCycle = 10f;

        [Header("Internal Buffer Tanks")]
        public float bufferCapacity = 200f;

        [Header("Containers")]
        public ItemContainer iceInputC;

        // Internal gas buffers
        public float HydrogenBuffer { get; private set; }
        public float OxygenBuffer { get; private set; }
        public float BufferCapacity => bufferCapacity;
        public float Progress01 => _timer / Mathf.Max(0.01f, cycleTime);
        public bool IsRunning { get; private set; }

        private float _timer;
        private PowerConsumer _power;
        private float _pushTimer;

        private void Awake()
        {
            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
        }

        public void EnsureContainers()
        {
            if (iceInputC == null) iceInputC = new ItemContainer("Ice Input", 2);
            else iceInputC.Resize(2);
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
            _portContainers ??= new ItemPortContainer[1];
            _portContainers[0] = new ItemPortContainer("Ice Input", iceInputC, canInput: true, canOutput: false);
            return _portContainers;
        }

        private void Update()
        {
            EnsureContainers();
            if (_power != null && !_power.IsPowered) { IsRunning = false; return; }

            // Check for ice input.
            if (iceItem == null || iceInputC.CountOf(iceItem) < icePerCycle)
            { IsRunning = false; _timer = 0; return; }

            // Check buffer space.
            if (HydrogenBuffer >= bufferCapacity && OxygenBuffer >= bufferCapacity)
            { IsRunning = false; return; }

            IsRunning = true;
            _timer += Time.deltaTime;

            if (_timer >= cycleTime)
            {
                _timer = 0f;
                iceInputC.Remove(iceItem, icePerCycle);

                // Determine output ratio based on connected tanks.
                bool hasH2Tank = GasNetwork.Instance?.FindTankNear(transform.position, GasType.Hydrogen, false) != null;
                bool hasO2Tank = GasNetwork.Instance?.FindTankNear(transform.position, GasType.Oxygen, false) != null;

                // Check for hydrogen engine too.
                var hits = Physics.OverlapSphere(transform.position, 3f);
                foreach (var col in hits)
                {
                    if (col.GetComponent<HydrogenEngine>() != null) hasH2Tank = true;
                }

                float h2Out = 0, o2Out = 0;
                if (hasH2Tank && hasO2Tank)
                {
                    // 50/50 split (scientifically ~66/33 but gameplay = 50/50).
                    h2Out = hydrogenPerCycle;
                    o2Out = oxygenPerCycle;
                }
                else if (hasH2Tank)
                {
                    h2Out = hydrogenPerCycle + oxygenPerCycle; // 100% hydrogen
                }
                else if (hasO2Tank)
                {
                    o2Out = hydrogenPerCycle + oxygenPerCycle; // 100% oxygen
                }
                else
                {
                    // No tanks connected — fill internal buffer with both.
                    h2Out = hydrogenPerCycle;
                    o2Out = oxygenPerCycle;
                }

                HydrogenBuffer = Mathf.Min(bufferCapacity, HydrogenBuffer + h2Out);
                OxygenBuffer = Mathf.Min(bufferCapacity, OxygenBuffer + o2Out);
            }

            // Push gas to connected tanks every 0.5s.
            _pushTimer += Time.deltaTime;
            if (_pushTimer >= 0.5f)
            {
                _pushTimer = 0;
                PushGasToTanks();
            }
        }

        private void PushGasToTanks()
        {
            if (HydrogenBuffer > 0)
            {
                var tank = GasNetwork.Instance?.FindTankNear(transform.position, GasType.Hydrogen, false);
                if (tank != null)
                {
                    float pushed = tank.TryAdd(GasType.Hydrogen, HydrogenBuffer);
                    HydrogenBuffer -= pushed;
                }
            }
            if (OxygenBuffer > 0)
            {
                var tank = GasNetwork.Instance?.FindTankNear(transform.position, GasType.Oxygen, false);
                if (tank != null)
                {
                    float pushed = tank.TryAdd(GasType.Oxygen, OxygenBuffer);
                    OxygenBuffer -= pushed;
                }
            }
        }
    }
}
