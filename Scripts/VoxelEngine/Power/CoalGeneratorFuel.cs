// Assets/Scripts/VoxelEngine/Power/CoalGeneratorFuel.cs
//
// Adds a fuel slot + on/off behaviour to a PowerGenerator. The generator only produces
// power while a fuel item is burning. Burn time comes from ResourceItem.fuelSeconds.

using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Power
{
    [RequireComponent(typeof(PowerGenerator))]
    public class CoalGeneratorFuel : MonoBehaviour
    {
        public ItemContainer fuelC;
        public float fuelRemaining;
        public float fuelMaxDuration;

        public bool IsBurning => fuelRemaining > 0f;
        public float FuelProgress01 => fuelMaxDuration > 0 ? Mathf.Clamp01(fuelRemaining / fuelMaxDuration) : 0f;

        private PowerGenerator _gen;

        private void Awake()
        {
            EnsureContainers();
            _gen = GetComponent<PowerGenerator>();
            // Per-face PortConfig UI removed by design — cables auto-connect to all
            // sides of generators / consumers. Future per-resource I/O selection
            // belongs on the cable / pipe end, not the machine.
        }

        public void EnsureContainers()
        {
            if (fuelC == null) fuelC = new ItemContainer("Fuel", 1);
            else fuelC.Resize(1);
        }

        private void Update()
        {
            EnsureContainers();
            if (_gen == null) _gen = GetComponent<PowerGenerator>();
            // Burn down current fuel.
            if (fuelRemaining > 0f)
            {
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
                    fuelRemaining   = ri.fuelSeconds;
                    fuelMaxDuration = ri.fuelSeconds;
                    fuelC.Remove(ri, 1);
                    _gen.isOn = true;
                }
            }
        }
    }
}
