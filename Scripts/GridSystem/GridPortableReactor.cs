// Assets/Scripts/VoxelEngine/GridSystem/GridPortableReactor.cs
//
// Ship-mountable version of the portable nuclear reactor.
// Provides power to the grid. Uses LEU pellets + ice.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridPortableReactor : GridBlock
    {
        [Header("Reactor")]
        public ItemDefinition leuPelletItem;
        public ItemDefinition iceItem;
        public ItemDefinition wasteItem;
        public float pelletBurnTime = 300f;
        public int icePerPellet = 2;
        public float wattsOutput = 800f;

        public ItemContainer fuelC;
        public ItemContainer iceC;
        public ItemContainer wasteC;

        public float FuelRemaining01 { get; private set; } = 1f;
        public bool IsRunning { get; private set; }

        public override float PowerOutput => Enabled && IsRunning ? wattsOutput : 0f;

        private float _burnTimer;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (fuelC == null) fuelC = new ItemContainer("LEU", 2);
            if (iceC == null) iceC = new ItemContainer("Ice", 4);
            if (wasteC == null) wasteC = new ItemContainer("Waste", 4);
        }

        private void Update()
        {
            if (fuelC == null) return;
            if (!Enabled) { IsRunning = false; return; }
            bool hasFuel = leuPelletItem != null && fuelC.CountOf(leuPelletItem) > 0;
            bool hasIce = iceItem != null && iceC.CountOf(iceItem) >= icePerPellet;

            if (hasFuel && hasIce)
            {
                IsRunning = true;
                _burnTimer += Time.deltaTime;
                FuelRemaining01 = 1f - Mathf.Clamp01(_burnTimer / pelletBurnTime);
                if (_burnTimer >= pelletBurnTime)
                {
                    fuelC.Remove(leuPelletItem, 1);
                    iceC.Remove(iceItem, icePerPellet);
                    if (wasteItem != null) wasteC.Insert(new ItemStack(wasteItem, 1));
                    _burnTimer = 0;
                }
            }
            else
            {
                IsRunning = false;
            }
        }
    }
}
