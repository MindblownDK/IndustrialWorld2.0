// Assets/Scripts/VoxelEngine/GridSystem/GridHydrogenEngine.cs
//
// Ship-mounted hydrogen engine. Burns the grid hydrogen pool into electrical
// power, giving ships a compact fuel-based generator that pairs with H2/O2
// generators and gas tanks.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridHydrogenEngine : GridBlock
    {
        [Header("Hydrogen Engine")]
        [Tooltip("Electrical output while hydrogen is available.")]
        public float wattsOutput = 12000f;

        [Tooltip("Hydrogen consumed per second while producing power.")]
        public float hydrogenPerSecond = 6f;

        [Tooltip("Do not start unless at least this much hydrogen is available.")]
        public float minHydrogenToRun = 0.25f;

        public bool IsRunning { get; private set; }
        public float LastHydrogenConsumed { get; private set; }

        public override float PowerOutput => Enabled && IsRunning ? wattsOutput : 0f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Hydrogen Engine";
        }

        private void FixedUpdate()
        {
            LastHydrogenConsumed = 0f;
            if (!Enabled || Grid == null)
            {
                IsRunning = false;
                return;
            }

            if (Grid.HydrogenStored < minHydrogenToRun)
            {
                IsRunning = false;
                return;
            }

            float want = hydrogenPerSecond * Time.fixedDeltaTime;
            float take = Mathf.Min(Grid.HydrogenStored, want);
            Grid.HydrogenStored -= take;
            LastHydrogenConsumed = take;
            IsRunning = take > 0f;
        }
    }
}
