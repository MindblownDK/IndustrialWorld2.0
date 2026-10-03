// Assets/Scripts/VoxelEngine/Storage/Powerstation.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║                     POWER STATION (14.40.0)                      ║
// ║  THE grid power input of the storage network. Holds 4 PSU       ║
// ║  modules whose combined rating is what this station can feed    ║
// ║  into the system. The Server Controller distributes the total   ║
// ║  system draw across every grid-powered station on the network   ║
// ║  (pipe or touch - no radius). More stations = more headroom.    ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class Powerstation : MonoBehaviour
    {
        [Header("PSU Slots (4)")]
        public ItemContainer psuSlots;

        // 14.40.0: legacy radius kept for prefab/setup compatibility -
        // connectivity is network membership (Data Pipes / touching) now.
        [HideInInspector] public float searchRadius = 8f;

        /// <summary>Combined rating of the installed PSU modules - how many
        /// watts this station can feed into the storage network.</summary>
        public float RatingWatts { get; private set; }

        /// <summary>The share of the system draw assigned by the controller.</summary>
        public float AssignedLoadWatts { get; private set; }

        /// <summary>True when the station's own grid connection is live.</summary>
        public bool IsGridPowered => _power != null && _power.IsPowered;

        /// <summary>The controller this station feeds (null = not on a network).</summary>
        public ServerRack Controller { get; private set; }

        private PowerConsumer _power;
        private float _tickTimer;

        // ── Unity ──────────────────────────────────────────────────
        private void Awake()
        {
            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            // Idle draw until the controller assigns a real load.
            _power.wattsPerSecond = 1f;
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
            if (psuSlots == null) psuSlots = new ItemContainer("PSU Slots", 4);
            else psuSlots.Resize(4);
            psuSlots.OnChanged -= ValidatePsuSlots;
            psuSlots.OnChanged += ValidatePsuSlots;
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
            EnsureContainers();
            RatingWatts = 0f;
            for (int i = 0; i < psuSlots.Size; i++)
            {
                var s = psuSlots.GetSlot(i);
                if (!s.IsEmpty && s.item is ServerComponent sc && sc.componentType == ComponentType.PSU)
                    RatingWatts += sc.value * s.count;
            }
            Controller = StorageNetwork.ControllerOf(this);
        }

        /// <summary>Called by the Server Controller every tick: this station's
        /// share of the total system draw. The station pulls exactly that from
        /// the power grid (plus a 1 W idle heartbeat).</summary>
        public void AssignLoad(float watts)
        {
            AssignedLoadWatts = Mathf.Max(0f, watts);
            if (_power != null) _power.wattsPerSecond = Mathf.Max(1f, AssignedLoadWatts);
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
