// Assets/Scripts/VoxelEngine/Gas/HydrogenEngine.cs
//
// Burns hydrogen gas to generate electricity. Needs a hydrogen gas tank
// connected via gas pipe as a buffer. Fuel cell style — clean power.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Power;

namespace VoxelEngine.Gas
{
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PowerGenerator))]
    public class HydrogenEngine : MonoBehaviour
    {
        [Header("Hydrogen Consumption")]
        [Tooltip("Gas units of hydrogen consumed per second.")]
        public float hydrogenPerSecond = 5f;

        [Header("Power Output")]
        [Tooltip("Watts generated while running.")]
        public float wattsOutput = 2000f;

        [Header("Internal Buffer")]
        [Tooltip("Internal hydrogen buffer capacity.")]
        public float bufferCapacity = 100f;
        public float bufferAmount;

        public bool IsRunning { get; private set; }
        public float Buffer01 => bufferCapacity > 0 ? Mathf.Clamp01(bufferAmount / bufferCapacity) : 0;

        private PowerGenerator _gen;
        private float _refillTimer;

        private void Awake()
        {
            _gen = GetComponent<PowerGenerator>();
        }

        private void Update()
        {
            // Refill buffer from connected gas tanks.
            _refillTimer += Time.deltaTime;
            if (_refillTimer >= 1f)
            {
                _refillTimer = 0;
                RefillBuffer();
            }

            // Burn hydrogen.
            float needed = hydrogenPerSecond * Time.deltaTime;
            if (bufferAmount >= needed)
            {
                bufferAmount -= needed;
                IsRunning = true;
                _gen.wattsPerSecond = wattsOutput;
                _gen.isOn = true;
            }
            else
            {
                IsRunning = false;
                _gen.isOn = false;
                _gen.wattsPerSecond = 0;
            }
        }

        private void RefillBuffer()
        {
            float space = bufferCapacity - bufferAmount;
            if (space <= 0) return;

            // Check nearby gas tanks for hydrogen.
            var tank = GasNetwork.Instance?.FindTankNear(transform.position, GasType.Hydrogen, true);
            if (tank != null)
            {
                float taken = tank.TryTake(GasType.Hydrogen, space);
                bufferAmount += taken;
            }
        }
    }
}
