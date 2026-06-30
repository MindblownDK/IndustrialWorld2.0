// Assets/Scripts/VoxelEngine/Power/FluidGenerator.cs
//
// Hydro/Fluid Kinetic & Potential Energy Power Generator.
// Reads surface flow velocity (kinetic energy) and height differential (potential energy)
// from simulated voxel liquid volumes to generate clean power on the electrical grid.

using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Fluids;
using VoxelEngine.Maritime;
using VoxelEngine.WaterSim;

namespace VoxelEngine.Power
{
    public class FluidGenerator : PowerGenerator
    {
        [Header("Fluid Generator Mechanics")]
        [Tooltip("Efficiency conversion multiplier from kinetic fluid joules to electrical watts.")]
        public float kineticEfficiency = 120f;
        [Tooltip("Efficiency conversion multiplier from height-difference potential energy to electrical watts.")]
        public float potentialEfficiency = 85f;
        [Tooltip("Connected water tank or pump for potential energy head pressure measurement.")]
        public WaterTank connectedTank;

        [Header("Runtime Telemetry")]
        [SerializeField] private float _currentFlowSpeed;
        [SerializeField] private float _currentHeadHeight;
        [SerializeField] private float _generatedWatts;

        public float CurrentFlowSpeed => _currentFlowSpeed;
        public float CurrentHeadHeight => _currentHeadHeight;
        public float GeneratedWatts => _generatedWatts;

        private void Update()
        {
            if (!isOn)
            {
                _generatedWatts = 0f;
                return;
            }

            // 1. Kinetic Energy calculation: E_k = v * m (proportional to velocity magnitude squared * fluid density mass)
            float3 flowVec = WaterProbeSystem.GetWaterFlow(new float3(transform.position.x, transform.position.y, transform.position.z));
            _currentFlowSpeed = math.length(flowVec);
            float fluidDensity = PlanetWaterUtility.SampleDensityAtWorldPos(transform.position);

            float kineticWatts = _currentFlowSpeed * _currentFlowSpeed * fluidDensity * kineticEfficiency;

            // 2. Potential Energy calculation: E_p = m * g * h (height difference when pumping into tank)
            float potentialWatts = 0f;
            if (connectedTank != null)
            {
                _currentHeadHeight = Mathf.Max(0f, connectedTank.transform.position.y - transform.position.y);
                float tankMass = connectedTank.water;
                if (tankMass > 0.1f && _currentHeadHeight > 0.1f)
                {
                    potentialWatts = Mathf.Sqrt(_currentHeadHeight) * Mathf.Min(tankMass, 1000f) * 0.01f * potentialEfficiency;
                }
            }
            else
            {
                _currentHeadHeight = 0f;
            }

            _generatedWatts = kineticWatts + potentialWatts;
            wattsPerSecond = _generatedWatts;
        }
    }
}
