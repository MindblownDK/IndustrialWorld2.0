// Assets/Scripts/VoxelEngine/Storage/Powerstation.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║                        POWERSTATION                            ║
// ║  Dedicated block that holds 4 PSU modules.                     ║
// ║  Each PSU increases the power capacity of the nearby rack.     ║
// ║  Place adjacent to a Server Rack (within searchRadius).        ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class Powerstation : MonoBehaviour
    {
        [Header("PSU Slots (4)")]
        public ItemContainer psuSlots;

        [Header("Range")]
        [Tooltip("Radius in which this station contributes its PSU wattage to a ServerRack.")]
        public float searchRadius = 8f;

        public float TotalWatts { get; private set; }

        private float          _tickTimer;
        private ServerRack     _connectedRack;

        // ── Unity ──────────────────────────────────────────────────
        private void Awake()
        {
            EnsureContainers();
            if (psuSlots != null) psuSlots.OnChanged += ValidatePsuSlots;
        }

        private void OnDestroy() => DropAllItems();

        private void Update()
        {
            _tickTimer += Time.deltaTime;
            if (_tickTimer < 0.5f) return;
            _tickTimer = 0;
            Recalculate();
        }

        // ── Setup ──────────────────────────────────────────────────
        public void EnsureContainers()
        {
            if (psuSlots == null)
            {
                psuSlots = new ItemContainer("PSU Slots", 4);
                psuSlots.OnChanged += ValidatePsuSlots;
            }
            else psuSlots.Resize(4);
        }

        private void ValidatePsuSlots()
        {
            for (int i = 0; i < psuSlots.Size; i++)
            {
                var s = psuSlots.GetSlot(i);
                if (!s.IsEmpty &&
                    !(s.item is ServerComponent sc && sc.componentType == ComponentType.PSU))
                {
                    psuSlots.SetSlot(i, new ItemStack());
                    Items.DroppedItem.Spawn(s.Clone(),
                        transform.position + Vector3.up * 0.6f, Vector3.up);
                }
            }
        }

        // ── Recalculation ──────────────────────────────────────────
        private void Recalculate()
        {
            TotalWatts = 0f;
            for (int i = 0; i < psuSlots.Size; i++)
            {
                var s = psuSlots.GetSlot(i);
                if (!s.IsEmpty && s.item is ServerComponent sc && sc.componentType == ComponentType.PSU)
                    TotalWatts += sc.value;
            }

            // Find nearest rack and contribute our wattage.
            _connectedRack = null;
            float bestSqr  = searchRadius * searchRadius;
            var   racks    = FindObjectsByType<ServerRack>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var r in racks)
            {
                float d = (r.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; _connectedRack = r; }
            }

            if (_connectedRack != null)
                _connectedRack.RegisterExternalPsu(TotalWatts, this);
        }

        // ── Drop items on destroy ──────────────────────────────────
        private void DropAllItems()
        {
            if (psuSlots == null) return;
            for (int i = 0; i < psuSlots.Size; i++)
            {
                var s = psuSlots.GetSlot(i);
                if (!s.IsEmpty)
                    Items.DroppedItem.Spawn(s.Clone(),
                        transform.position + Vector3.up * 0.5f, Vector3.up);
            }
        }
    }
}
