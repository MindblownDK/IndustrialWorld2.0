// Assets/Scripts/VoxelEngine/Transport/CargoLaunchPad.cs
//
// THE INTERPLANETARY CARGO PAD — bulk freight between worlds.
//
// This is the missing tier of the logistics ladder. Everything built so far moves goods
// within one place:
//
//   Belts        - metres, inside a factory
//   Trains       - kilometres, across one planet's surface
//   Drone ports  - 400 m, point to point on one planet
//   Cargo pads   - BETWEEN BODIES
//
// Without this, a second planet is a place you visit rather than a place you can
// industrialise, because nothing you build there can feed anything you built at home.
//
// WHY THIS IS NOT A ROCKET VEHICLE
// The roadmap also lists a buildable multi-stage rocket. That is deliberately NOT what
// this is, and not what shipped here, because the game already has a way to fly to
// orbit: build a grid with thrusters and fly it. A separate rocket entity would be a
// second flying thing that is not a player-built grid, which is exactly the split the
// rail rework is being done to remove. Manned flight stays a grid you build.
//
// So a cargo pad handles the thing a piloted grid is BAD at: unattended, repeatable,
// scheduled bulk freight that keeps running while the player is somewhere else. The two
// answer different problems, which is what keeps both worth having.
//
// FLIGHTS ARE SIMULATED, NOT FLOWN
// A launch is a timer and a manifest, not a physics object. The same reasoning as trains
// walking a graph and satellites on analytic rails: a thing the player expects to keep
// working while they are elsewhere must not depend on being simulated. A cargo flight
// therefore completes whether or not either planet is loaded.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;

namespace VoxelEngine.Transport
{
    /// <summary>What a pad does with its hold.</summary>
    public enum PadRole
    {
        /// <summary>Loads its hold into outgoing flights.</summary>
        Send = 0,
        /// <summary>Receives incoming flights into its hold.</summary>
        Receive = 1,
    }

    /// <summary>A shipment in transit between two bodies.</summary>
    public class CargoFlight
    {
        public string OriginPad;
        public string DestinationPad;
        public string DestinationBody;
        public ItemDefinition Item;
        public int Count;

        /// <summary>Seconds of flight remaining.</summary>
        public float Remaining;
        public float Total;

        public float Progress01 => Total > 0f ? Mathf.Clamp01(1f - Remaining / Total) : 0f;
    }

    [DisallowMultipleComponent, RequireComponent(typeof(VoxelEngine.Building.PlacedBlock))]
    public class CargoLaunchPad : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Name other pads address. Matched by name, not by reference, so a pad " +
                 "can be demolished and rebuilt without breaking a route.")]
        [SerializeField] private string _padName = "";

        [Tooltip("Body this pad sits on. Resolved automatically on the surface it is built.")]
        [SerializeField] private string _bodyName = "";

        [Header("Operation")]
        public PadRole role = PadRole.Send;

        [Tooltip("Pad this one ships to, by name. Only meaningful for a SEND pad.")]
        public string destinationPad = "";

        [Tooltip("Items per launch. A launch is all-or-nothing: a pad waits until it has " +
                 "a full load, so the player is not billed a whole flight for one ingot.")]
        public int launchSize = 100;

        [Tooltip("Power drawn while fuelling a launch.")]
        public float powerDraw = 1500f;

        [Header("Hold")]
        public int holdSlots = 12;

        public ItemContainer Hold
        {
            get
            {
                _hold ??= new ItemContainer("Cargo Hold", Mathf.Max(1, holdSlots));
                return _hold;
            }
        }
        [SerializeField] private ItemContainer _hold;

        public string Status { get; private set; } = "Idle";

        // ── Registry ─────────────────────────────────────────────────────────────
        private static readonly List<CargoLaunchPad> s_all = new();
        public static IReadOnlyList<CargoLaunchPad> All => s_all;

        private void OnEnable()
        {
            if (!s_all.Contains(this)) s_all.Add(this);
            ResolveBody();
        }

        private void OnDisable() => s_all.Remove(this);

        public string PadName
        {
            get => string.IsNullOrWhiteSpace(_padName) ? FallbackName : _padName;
            set => _padName = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }

        public bool HasCustomName => !string.IsNullOrWhiteSpace(_padName);

        public string BodyName
        {
            get
            {
                if (string.IsNullOrEmpty(_bodyName)) ResolveBody();
                return string.IsNullOrEmpty(_bodyName) ? "Unknown" : _bodyName;
            }
        }

        private string FallbackName
        {
            get
            {
                Vector3 p = transform.position;
                return $"Pad {Mathf.RoundToInt(p.x)},{Mathf.RoundToInt(p.z)}";
            }
        }

        /// <summary>
        /// Records which body this pad was built on. Captured once at placement rather
        /// than sampled continuously: a pad is a fixed installation, and the active body
        /// changes as the PLAYER travels, so reading it live would make a pad think it had
        /// moved to whatever world its owner is currently standing on.
        /// </summary>
        private void ResolveBody()
        {
            if (!string.IsNullOrEmpty(_bodyName)) return;
            var body = GravityProvider.ActiveBody;
            if (body != null && body.settings != null) _bodyName = body.settings.bodyName;
        }

        /// <summary>Restores the recorded body after a load.</summary>
        public void SetBodyName(string bodyName)
        {
            if (!string.IsNullOrWhiteSpace(bodyName)) _bodyName = bodyName;
        }

        public static CargoLaunchPad Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            for (int i = 0; i < s_all.Count; i++)
            {
                var pad = s_all[i];
                if (pad != null && pad.PadName == name) return pad;
            }
            return null;
        }

        public static void CollectNames(List<string> results, CargoLaunchPad exclude = null)
        {
            results.Clear();
            for (int i = 0; i < s_all.Count; i++)
            {
                var pad = s_all[i];
                if (pad == null || pad == exclude) continue;
                string name = pad.PadName;
                if (!results.Contains(name)) results.Add(name);
            }
            results.Sort();
        }

        // ════════════════════════════════════════════════════════════════
        //  LAUNCHING
        // ════════════════════════════════════════════════════════════════

        private VoxelEngine.Power.PowerConsumer _power;
        private float _tick;

        private void Awake()
        {
            _power = GetComponent<VoxelEngine.Power.PowerConsumer>();
            if (_power != null) _power.wattsPerSecond = powerDraw * 0.1f;
        }

        private void Update()
        {
            // The flight registry is advanced from here so the feature needs no scene
            // wiring: if a pad exists anywhere, shipments are moving.
            CargoFlightRegistry.Tick(Time.deltaTime);

            _tick -= Time.deltaTime;
            if (_tick > 0f) return;
            _tick = 1f;

            if (role == PadRole.Receive)
            {
                int inbound = CargoFlightRegistry.CountInboundTo(PadName);
                Status = inbound > 0
                    ? $"Receiving  ·  {inbound} flight(s) inbound"
                    : "Receiving  ·  nothing inbound";
                return;
            }

            TrySend();
        }

        private void TrySend()
        {
            if (string.IsNullOrWhiteSpace(destinationPad))
            {
                Status = "No destination set";
                return;
            }

            var target = Find(destinationPad);
            if (target == null)
            {
                Status = $"No pad named '{destinationPad}'";
                return;
            }

            if (target == this)
            {
                Status = "Destination is this pad";
                return;
            }

            bool hasPower = _power == null || _power.IsPowered;
            if (!hasPower)
            {
                Status = "No power";
                if (_power != null) _power.wattsPerSecond = powerDraw * 0.1f;
                return;
            }

            // A launch is all-or-nothing. Shipping partial loads would mean a trickle of
            // flights each costing a full fuelling cycle, which is the opposite of what
            // bulk freight is for.
            if (!TryTakeFullLoad(out var item, out int count))
            {
                Status = $"Loading  ·  need {launchSize} of one item";
                if (_power != null) _power.wattsPerSecond = powerDraw * 0.1f;
                return;
            }

            if (_power != null) _power.wattsPerSecond = powerDraw;

            float seconds = CargoFlightRegistry.EstimateFlightSeconds(BodyName, target.BodyName);
            CargoFlightRegistry.Launch(new CargoFlight
            {
                OriginPad = PadName,
                DestinationPad = target.PadName,
                DestinationBody = target.BodyName,
                Item = item,
                Count = count,
                Remaining = seconds,
                Total = seconds,
            });

            Status = $"Launched {count} x {item.displayName} to {target.PadName}";
        }

        /// <summary>
        /// Removes a full launch load of a single item, or nothing at all. Returns false
        /// when no single item has reached the launch size.
        /// </summary>
        private bool TryTakeFullLoad(out ItemDefinition item, out int count)
        {
            item = null;
            count = 0;

            var hold = Hold;
            for (int i = 0; i < hold.Size; i++)
            {
                var slot = hold.GetSlot(i);
                if (slot == null || slot.IsEmpty || slot.item == null) continue;

                int available = hold.CountOf(slot.item);
                if (available < launchSize) continue;

                int taken = hold.Remove(slot.item, launchSize);
                if (taken <= 0) continue;

                hold.RaiseChanged();
                item = slot.item;
                count = taken;
                return true;
            }
            return false;
        }

        /// <summary>Puts a delivered shipment into this pad's hold.</summary>
        public int Deliver(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;

            var leftover = Hold.Insert(new ItemStack { item = item, count = count });
            int stored = count - (leftover?.count ?? 0);
            if (stored > 0) Hold.RaiseChanged();
            return stored;
        }
    }
}
