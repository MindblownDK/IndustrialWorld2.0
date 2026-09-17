// Assets/Scripts/VoxelEngine/Building/RailStation.cs
//
// THE RAIL STATION — a named stop on the network, and the only place a train trades.
//
// A station is where the railway meets the factory. It owns a cargo hold, a named
// identity that schedules refer to, and a loading rule that decides whether it fills
// trains or empties them.
//
// WHY THE HOLD LIVES HERE AND NOT ON THE TRAIN
// A train is in motion and often unloaded; a station is fixed and always addressable.
// Putting the buffer at the station means the factory either side of it can fill or
// drain the hold on its own schedule, and the train only has to show up. That is what
// lets a railway run asynchronously instead of demanding the player babysit arrivals.
//
// Stations are matched to trains BY NAME, not by reference. A schedule that names
// "North Pit" keeps working when the player demolishes and rebuilds that station,
// which is the behaviour the drone ports already established for this codebase.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building
{
    /// <summary>What a station does to a train that stops at it.</summary>
    public enum StationRole
    {
        /// <summary>Moves cargo from the station hold INTO the train.</summary>
        Load = 0,
        /// <summary>Moves cargo from the train INTO the station hold.</summary>
        Unload = 1,
        /// <summary>A timing point only: the train stops but no cargo moves.</summary>
        Passing = 2,
    }

    [DisallowMultipleComponent, RequireComponent(typeof(PlacedBlock))]
    public class RailStation : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Name schedules refer to. Two stations may share a name: a train will use " +
                 "whichever it reaches first, which is how a multi-platform yard works.")]
        [SerializeField] private string _stationName = "";

        [Header("Operation")]
        public StationRole role = StationRole.Load;

        [Tooltip("How many items move per second while a train is docked. The rate is what " +
                 "makes a long train worth building rather than many short ones.")]
        public float transferPerSecond = 20f;

        [Tooltip("Seconds a train waits after cargo stops moving, before departing.")]
        public float dwellSeconds = 3f;

        [Header("Hold")]
        public int holdSlots = 12;

        /// <summary>The station's buffer. Created on demand so a fresh block is valid immediately.</summary>
        public ItemContainer Hold
        {
            get
            {
                if (_hold == null)
                {
                    _hold = new ItemContainer("Station Hold", Mathf.Max(1, holdSlots));
                }
                return _hold;
            }
        }
        [SerializeField] private ItemContainer _hold;

        /// <summary>
        /// Which item this station handles. Null means "anything", which is the right default
        /// for an unload station and usually the wrong one for a load station.
        /// </summary>
        public ItemDefinition filter;

        // ── Registry ─────────────────────────────────────────────────────────────
        private static readonly List<RailStation> s_all = new();
        public static IReadOnlyList<RailStation> All => s_all;

        private void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
        private void OnDisable() { s_all.Remove(this); }

        public string StationName
        {
            get => string.IsNullOrWhiteSpace(_stationName) ? FallbackName : _stationName;
            set => _stationName = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }

        public bool HasCustomName => !string.IsNullOrWhiteSpace(_stationName);

        private string FallbackName
        {
            get
            {
                Vector3 p = transform.position;
                return $"Stop {Mathf.RoundToInt(p.x)},{Mathf.RoundToInt(p.z)}";
            }
        }

        public string RoleLabel => role switch
        {
            StationRole.Unload => "UNLOAD",
            StationRole.Passing => "PASSING",
            _ => "LOAD",
        };

        /// <summary>
        /// The track cell this station serves. Resolved lazily and re-resolved when the
        /// cached one dies, so rebuilding the track under a station does not orphan it.
        /// </summary>
        public RailTrack ServedTrack
        {
            get
            {
                if (_servedTrack == null)
                    _servedTrack = RailNetwork.FindNearest(transform.position, ServiceRadius);
                return _servedTrack;
            }
        }
        private RailTrack _servedTrack;

        /// <summary>How far from the station a track cell may be and still count as its platform.</summary>
        public const float ServiceRadius = 4f;

        /// <summary>Finds a station by name. Null when no station carries that name.</summary>
        public static RailStation Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            for (int i = 0; i < s_all.Count; i++)
            {
                var station = s_all[i];
                if (station != null && station.StationName == name) return station;
            }
            return null;
        }

        /// <summary>Every distinct station name on the network, for the schedule picker.</summary>
        public static void CollectNames(List<string> results)
        {
            results.Clear();
            for (int i = 0; i < s_all.Count; i++)
            {
                var station = s_all[i];
                if (station == null) continue;
                string name = station.StationName;
                if (!results.Contains(name)) results.Add(name);
            }
            results.Sort();
        }

        // ── Cargo transfer ───────────────────────────────────────────────────────

        /// <summary>
        /// Moves cargo between this station and a docked train for one tick. Returns the
        /// number of items moved, so the caller can tell a finished transfer from a stalled
        /// one — a station with nothing to give and a train with nothing to take both
        /// return zero, and the dwell timer starts either way.
        /// </summary>
        public int ServiceTrain(ItemContainer trainHold, float deltaTime)
        {
            if (role == StationRole.Passing || trainHold == null) return 0;

            int budget = Mathf.FloorToInt(transferPerSecond * deltaTime);
            if (budget <= 0) return 0;

            return role == StationRole.Load
                ? Transfer(Hold, trainHold, budget)
                : Transfer(trainHold, Hold, budget);
        }

        /// <summary>
        /// Moves up to <paramref name="budget"/> items from one container to another,
        /// honouring the station filter. Anything the destination refuses is put back,
        /// so a full train never destroys cargo.
        /// </summary>
        private int Transfer(ItemContainer from, ItemContainer to, int budget)
        {
            if (from == null || to == null) return 0;

            int moved = 0;
            for (int i = 0; i < from.Size && moved < budget; i++)
            {
                var slot = from.GetSlot(i);
                if (slot == null || slot.IsEmpty) continue;
                if (filter != null && slot.item != filter) continue;

                int take = Mathf.Min(slot.count, budget - moved);
                if (take <= 0) continue;

                var parcel = new ItemStack
                {
                    item = slot.item,
                    count = take,
                    durability = slot.durability,
                    payload = slot.payload,
                };

                var leftover = to.Insert(parcel);
                int accepted = take - (leftover?.count ?? 0);
                if (accepted <= 0) continue;

                from.Remove(slot.item, accepted);
                moved += accepted;
            }

            if (moved > 0)
            {
                from.RaiseChanged();
                to.RaiseChanged();
            }
            return moved;
        }

        /// <summary>Whether this station still has work for a docked train.</summary>
        public bool HasWorkFor(ItemContainer trainHold)
        {
            if (role == StationRole.Passing || trainHold == null) return false;

            var source = role == StationRole.Load ? Hold : trainHold;
            var destination = role == StationRole.Load ? trainHold : Hold;

            for (int i = 0; i < source.Size; i++)
            {
                var slot = source.GetSlot(i);
                if (slot == null || slot.IsEmpty) continue;
                if (filter != null && slot.item != filter) continue;
                if (destination.HasSpace(slot.item, 1)) return true;
            }
            return false;
        }
    }
}
