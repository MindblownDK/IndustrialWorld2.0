// Assets/Scripts/VoxelEngine/Maritime/GridShippingContainer.cs
//
// Maritime shipping container. A high-capacity cargo block styled after real
// intermodal containers and unlocked through maritime research.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Transport;

namespace VoxelEngine.Maritime
{
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class GridShippingContainer : GridCargoContainer, IItemPortHost
    {
        [Header("Shipping Container")]
        [Tooltip("Large cargo container equivalent capacity multiplier.")]
        public float largeContainerMultiplier = 5f;

        private PortConfig _ports;
        private ItemPortRouting _routing;
        private ItemPortContainer[] _portContainers;

        public PortConfig PortConfig { get { EnsurePortRefs(); return _ports; } }
        public ItemPortRouting Routing { get { EnsurePortRefs(); return _routing; } }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsurePortRefs();
            _portContainers ??= new ItemPortContainer[1];
            _portContainers[0] = new ItemPortContainer("Storage", container, canInput: true, canOutput: true);
            return _portContainers;
        }

        public override void OnPlaced()
        {
            blockName = "Shipping Container";
            slots = Mathf.Max(slots, 60);
            maxMassKg = Mathf.Max(maxMassKg, 1_000_000f * largeContainerMultiplier);
            BlockMass = Mathf.Max(BlockMass, 1800f);
            maxHP = Mathf.Max(maxHP, 1200f);
            base.OnPlaced();
            if (container == null) container = new ItemContainer("Shipping Container", slots);
            else container.Resize(slots);
            ApplyFilter();
            EnsurePortRefs();
        }

        private void EnsurePortRefs()
        {
            if (container == null) container = new ItemContainer("Shipping Container", slots);

            if (_ports == null)
            {
                _ports = GetComponent<PortConfig>();
                if (_ports == null) _ports = gameObject.AddComponent<PortConfig>();
                _ports.EnsureAllFaces();
            }

            if (_routing == null)
            {
                _routing = GetComponent<ItemPortRouting>();
                if (_routing == null) _routing = gameObject.AddComponent<ItemPortRouting>();
            }
        }
    }
}
