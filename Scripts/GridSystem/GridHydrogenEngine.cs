// Assets/Scripts/VoxelEngine/GridSystem/GridHydrogenEngine.cs
//
// Ship-mounted hydrogen engine. Buffers a small amount of hydrogen internally,
// then burns it into grid power. The internal buffer is refilled from the shared
// gas pool, which is fed by gas tanks/H2 generators through gas pipes.

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

        [Tooltip("Small internal hydrogen buffer in litres.")]
        public float internalTankCapacity = 75f;

        [Tooltip("Current hydrogen in the internal buffer.")]
        public float internalHydrogen;

        [Tooltip("Do not start unless at least this much internal hydrogen is available.")]
        public float minHydrogenToRun = 0.25f;

        [Tooltip("Maximum hydrogen pulled from the shared gas pool per second.")]
        public float refillRate = 25f;

        public bool IsRunning { get; private set; }
        public float LastHydrogenConsumed { get; private set; }
        public float Fill01 => internalTankCapacity > 0f ? Mathf.Clamp01(internalHydrogen / internalTankCapacity) : 0f;

        public override float PowerOutput => Enabled && IsRunning ? wattsOutput : 0f;
        public override float ContentMass => internalHydrogen * 0.05f;

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

            RefillInternalTank(Time.fixedDeltaTime);

            if (internalHydrogen < minHydrogenToRun)
            {
                IsRunning = false;
                return;
            }

            float want = hydrogenPerSecond * Time.fixedDeltaTime;
            float take = Mathf.Min(internalHydrogen, want);
            internalHydrogen -= take;
            LastHydrogenConsumed = take;
            IsRunning = take > 0f;
        }

        private void RefillInternalTank(float dt)
        {
            if (Grid == null || internalHydrogen >= internalTankCapacity) return;
            float space = internalTankCapacity - internalHydrogen;
            float want = Mathf.Min(space, refillRate * dt);
            float take = Mathf.Min(Grid.HydrogenStored, want);
            if (take <= 0f) return;
            Grid.HydrogenStored -= take;
            internalHydrogen += take;
        }
    }
}
