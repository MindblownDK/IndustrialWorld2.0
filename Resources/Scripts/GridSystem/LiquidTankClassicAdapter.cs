// Assets/Scripts/VoxelEngine/GridSystem/LiquidTankClassicAdapter.cs
//
// Gives the big grid LiquidTank a presence on the CLASSIC liquid pipe graph
// (FluidNetworkManager), so classic water/fuel pipes link to it with the same
// five-lattice-cell cardinal rule every other endpoint gets — and content
// mirrors both ways. The GridLiquidTank stays the single source of truth;
// this adapter is a thin WaterTank-shaped shim beside it.

using UnityEngine;
using VoxelEngine.Fluids;

namespace VoxelEngine.GridSystem
{
    [DisallowMultipleComponent]
    public class LiquidTankClassicAdapter : WaterTank
    {
        [Tooltip("The grid tank this adapter mirrors (auto-found on the same object).")]
        public GridLiquidTank gridTank;

        private float _lastClassicWater;
        private float _lastGridStored;
        private bool _primed;

        private void Start()
        {
            if (gridTank == null) gridTank = GetComponentInParent<GridLiquidTank>();
            Prime();
        }

        private void Prime()
        {
            if (gridTank == null) return;
            capacityLitres = gridTank.capacity;
            liquidType = gridTank.liquidType;
            water = gridTank.stored;
            _lastClassicWater = water;
            _lastGridStored = gridTank.stored;
            _primed = true;
        }

        private void LateUpdate()
        {
            if (gridTank == null)
            {
                gridTank = GetComponentInParent<GridLiquidTank>();
                if (gridTank == null) return;
            }
            if (!_primed) { Prime(); return; }

            // Classic side gained/lost litres (a classic pump/pipe used us directly):
            // push the delta into the grid tank.
            float dClassic = water - _lastClassicWater;
            if (dClassic > 0.0001f)
            {
                if (gridTank.stored <= 0.001f && liquidType != gridTank.liquidType)
                    gridTank.SetLiquidType(liquidType);
                water = _lastClassicWater + gridTank.Add(dClassic);
            }
            else if (dClassic < -0.0001f)
            {
                water = _lastClassicWater - gridTank.Remove(-dClassic);
            }

            // Grid side gained/lost litres (fill hoses, machines, the tank UI):
            // mirror the delta into the classic shim.
            float dGrid = gridTank.stored - _lastGridStored;
            if (dGrid > 0.0001f)
            {
                float accepted = AddSome(gridTank.liquidType, dGrid);
                gridTank.stored -= dGrid - accepted;
            }
            else if (dGrid < -0.0001f)
            {
                float taken = TakeSome(gridTank.liquidType, -dGrid);
                gridTank.stored += (-dGrid) - taken; // nothing the classic side couldn't cover stays missing
            }

            // Type/capacity mirror (type only follows while both sides are empty).
            capacityLitres = gridTank.capacity;
            if (water <= 0.001f && gridTank.stored <= 0.001f && liquidType != gridTank.liquidType)
                liquidType = gridTank.liquidType;

            _lastClassicWater = water;
            _lastGridStored = gridTank.stored;
        }
    }
}
