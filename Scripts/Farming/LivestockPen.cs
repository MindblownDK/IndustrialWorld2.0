// Assets/Scripts/VoxelEngine/Farming/LivestockPen.cs
//
// THE LIVESTOCK PEN — the block that turns loose animals into a farm.
//
// The pen is what makes husbandry a BASE activity rather than a wandering one. It
// holds the feed, holds the water, counts the herd, and does the tedious parts
// automatically so the player manages a farm instead of hand-feeding each animal.
//
// WHY THE PEN OWNS THE AUTOMATION
// Exactly the same argument as the rail station owning the cargo hold: the animal
// moves and is often not where the player is, while the pen is fixed and always
// addressable. Put the trough on the pen and the factory can pipe feed into it on its
// own schedule; the player only has to keep the pen stocked, not chase cows.
//
// POPULATION LIMIT IS THE LOAD-BEARING RULE
// Uncapped breeding is how a farm feature destroys a game's performance and its
// economy at the same time. The cap is per-pen and enforced at the moment of breeding,
// and the pen deliberately reports how full it is, so hitting the ceiling reads as a
// designed limit rather than as the feature quietly breaking.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Fauna;
using VoxelEngine.Transport;

namespace VoxelEngine.Farming
{
    [DisallowMultipleComponent, RequireComponent(typeof(PlacedBlock))]
    public class LivestockPen : MonoBehaviour, IItemPortHost
    {
        [Header("Pen")]
        [Tooltip("Animals within this radius belong to the pen and are fed by it.")]
        public float penRadius = 12f;

        [Tooltip("Maximum animals this pen will breed up to. The single most important " +
                 "number here: uncapped breeding wrecks both performance and the economy.")]
        public int populationLimit = 8;

        [Tooltip("Animals inside the pen count as sheltered, halving their consumption " +
                 "and speeding production.")]
        public bool providesShelter = true;

        [Header("Feeding")]
        [Tooltip("Hunger restored per unit of feed consumed.")]
        public float hungerPerFeed = 34f;

        [Tooltip("Thirst restored per unit of water consumed.")]
        public float thirstPerWater = 34f;

        [Tooltip("An animal is topped up when its need drops below this.")]
        public float feedThreshold = 55f;

        [Header("Breeding")]
        [Tooltip("Seconds between breeding attempts while conditions allow.")]
        public float breedCheckSeconds = 30f;

        [Header("Products")]
        // Direct references, assigned by setup step 87, rather than a runtime lookup.
        // Resources.LoadAll cannot see these: Item_Wool lives under VoxelEngineAssets,
        // not under a Resources folder, so a name-based search would silently find
        // nothing and stall every sheep. An explicit reference also survives an item
        // being renamed.
        [Tooltip("Item produced by cows. Assigned by setup step 87.")]
        public ItemDefinition milkItem;

        [Tooltip("Item produced by sheep. Assigned by setup step 87.")]
        public ItemDefinition woolItem;

        [Header("Containers")]
        public int feedSlots = 4;
        public int outputSlots = 6;

        /// <summary>Feed and water go in here; a belt or pipe can fill it.</summary>
        public ItemContainer supply;

        /// <summary>Milk, wool and anything harvested lands here.</summary>
        public ItemContainer output;

        // ── Runtime ──────────────────────────────────────────────────────────────
        public string Status { get; private set; } = "Idle";
        public int Population { get; private set; }
        public int ReadyToHarvest { get; private set; }

        private readonly List<LivestockHusbandry> _members = new(16);
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;

        private float _scanTimer;
        private float _breedTimer;

        // ── Registry ─────────────────────────────────────────────────────────────
        private static readonly List<LivestockPen> s_all = new();
        public static IReadOnlyList<LivestockPen> All => s_all;

        private void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }

        private void OnDisable()
        {
            s_all.Remove(this);
            // Release members so an animal whose pen is removed does not keep a dead
            // reference and stay anchored to a pen that no longer exists.
            for (int i = 0; i < _members.Count; i++)
                if (_members[i] != null && _members[i].Pen == this) _members[i].Pen = null;
            _members.Clear();
        }

        public void EnsureContainers()
        {
            if (supply == null) supply = new ItemContainer("Feed & Water", Mathf.Max(1, feedSlots));
            else supply.Resize(Mathf.Max(1, feedSlots));

            if (output == null) output = new ItemContainer("Produce", Mathf.Max(1, outputSlots));
            else output.Resize(Mathf.Max(1, outputSlots));
        }

        // ── Ports ────────────────────────────────────────────────────────────────
        public PortConfig PortConfig
        {
            get
            {
                if (_portConfig == null)
                {
                    _portConfig = GetComponent<PortConfig>();
                    if (_portConfig == null) _portConfig = gameObject.AddComponent<PortConfig>();
                    _portConfig.EnsureAllFaces();
                }
                return _portConfig;
            }
        }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsureContainers();
            _portContainers ??= new ItemPortContainer[2];
            _portContainers[0] = new ItemPortContainer("Feed & Water", supply, canInput: true, canOutput: false);
            _portContainers[1] = new ItemPortContainer("Produce", output, canInput: false, canOutput: true);
            return _portContainers;
        }

        // ── Simulation ───────────────────────────────────────────────────────────
        private void Update()
        {
            EnsureContainers();
            float dt = Time.deltaTime;

            _scanTimer -= dt;
            if (_scanTimer <= 0f)
            {
                _scanTimer = 1f;
                Rescan();
                ServeHerd();
                CollectProduce();
            }

            _breedTimer -= dt;
            if (_breedTimer <= 0f)
            {
                _breedTimer = breedCheckSeconds;
                TryBreed();
            }

            UpdateStatus();
        }

        /// <summary>Finds which animals currently belong to this pen.</summary>
        private void Rescan()
        {
            _members.Clear();
            float rSq = penRadius * penRadius;
            Vector3 here = transform.position;

            var all = LivestockHusbandry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var animal = all[i];
                if (animal == null || !animal.IsAlive) continue;
                if ((animal.transform.position - here).sqrMagnitude > rSq) continue;

                // First pen wins. Without this an animal standing between two pens would be
                // fed twice and counted against both population caps.
                if (animal.Pen != null && animal.Pen != this) continue;

                animal.Pen = this;
                animal.Sheltered = providesShelter;
                _members.Add(animal);
            }

            Population = _members.Count;
        }

        /// <summary>Tops up hungry and thirsty animals from the supply container.</summary>
        private void ServeHerd()
        {
            for (int i = 0; i < _members.Count; i++)
            {
                var animal = _members[i];
                if (animal == null || !animal.IsAlive) continue;

                if (animal.Hunger < feedThreshold && TryConsume(IsFeed))
                    animal.Feed(hungerPerFeed);

                if (animal.Thirst < feedThreshold && TryConsume(IsWater))
                    animal.Water(thirstPerWater);
            }
        }

        /// <summary>Harvests ready animals into the output container.</summary>
        private void CollectProduce()
        {
            ReadyToHarvest = 0;

            for (int i = 0; i < _members.Count; i++)
            {
                var animal = _members[i];
                if (animal == null || !animal.IsAlive || !animal.HasProduct) continue;

                var item = ResolveProductItem(animal.Product);
                if (item == null) { ReadyToHarvest++; continue; }

                if (!output.HasSpace(item, 1)) { ReadyToHarvest++; continue; }

                // Only clear the animal's product once the item is genuinely stored, or a
                // full output would consume the milk and hand back nothing.
                if (!animal.TryHarvest()) continue;

                var leftover = output.Insert(new ItemStack { item = item, count = 1 });
                if (leftover != null && leftover.count > 0)
                {
                    // The container refused it after all (a weight cap can reject what
                    // HasSpace allowed). Hand the product back to the animal rather than
                    // destroying it, and report it as still waiting.
                    animal.RestoreProduct();
                    ReadyToHarvest++;
                }
                else
                {
                    output.RaiseChanged();
                }
            }
        }

        /// <summary>Breeds one pair if the pen has room and two willing adults.</summary>
        private void TryBreed()
        {
            if (Population >= populationLimit) return;

            LivestockHusbandry first = null;
            for (int i = 0; i < _members.Count; i++)
            {
                var animal = _members[i];
                if (animal == null || !animal.IsAlive) continue;
                if (!animal.CanBreed(out _)) continue;

                // Only like breeds with like, so a mixed pen does not produce nonsense.
                if (first == null) { first = animal; continue; }
                if (first.Species != animal.Species) continue;

                SpawnOffspring(first, animal);
                first.NoteBred();
                animal.NoteBred();
                return;
            }
        }

        private void SpawnOffspring(LivestockHusbandry a, LivestockHusbandry b)
        {
            // Cloned from a parent so the calf inherits the authored prefab exactly -
            // mesh, colliders, drops - without the pen needing a prefab reference per kind.
            var clone = Instantiate(a.gameObject, MidPoint(a, b), a.transform.rotation);
            clone.name = a.gameObject.name;

            var calf = clone.GetComponent<LivestockHusbandry>();
            if (calf != null)
            {
                // Reset the clone's husbandry state: without this a calf inherits its
                // parent's age and would be born adult and instantly breedable.
                calf.MarkNewborn();
                calf.Pen = this;
                calf.Sheltered = providesShelter;
            }
        }

        private Vector3 MidPoint(LivestockHusbandry a, LivestockHusbandry b)
        {
            Vector3 mid = (a.transform.position + b.transform.position) * 0.5f;
            Vector2 jitter = Random.insideUnitCircle * 1.2f;
            return mid + new Vector3(jitter.x, 0.4f, jitter.y);
        }

        private void UpdateStatus()
        {
            if (Population == 0)
            {
                Status = "No animals in range";
                return;
            }

            bool hasFeed = HasAny(IsFeed);
            bool hasWater = HasAny(IsWater);

            if (!hasFeed && !hasWater) Status = $"{Population} animals  -  NO FEED OR WATER";
            else if (!hasFeed) Status = $"{Population} animals  -  NO FEED";
            else if (!hasWater) Status = $"{Population} animals  -  NO WATER";
            else if (Population >= populationLimit)
                Status = $"{Population}/{populationLimit} animals  -  pen full";
            else
                Status = $"{Population}/{populationLimit} animals  -  healthy";
        }

        // ── Supply helpers ───────────────────────────────────────────────────────

        private bool TryConsume(System.Func<ItemDefinition, bool> predicate)
        {
            for (int i = 0; i < supply.Size; i++)
            {
                var slot = supply.GetSlot(i);
                if (slot == null || slot.IsEmpty || slot.item == null) continue;
                if (!predicate(slot.item)) continue;

                supply.Remove(slot.item, 1);
                supply.RaiseChanged();
                return true;
            }
            return false;
        }

        private bool HasAny(System.Func<ItemDefinition, bool> predicate)
        {
            for (int i = 0; i < supply.Size; i++)
            {
                var slot = supply.GetSlot(i);
                if (slot == null || slot.IsEmpty || slot.item == null) continue;
                if (predicate(slot.item)) return true;
            }
            return false;
        }

        /// <summary>
        /// Feed is matched by id substring rather than a hard item reference, so the pen
        /// works with whatever crops the project has authored and keeps working when more
        /// are added. A missing crop set degrades to "no feed" rather than a null reference.
        /// </summary>
        private static bool IsFeed(ItemDefinition item)
        {
            if (item == null) return false;
            string id = item.itemId ?? "";
            return id.Contains("wheat") || id.Contains("grain") || id.Contains("hay")
                || id.Contains("biomass") || id.Contains("potato") || id.Contains("carrot")
                || id.Contains("corn") || id.Contains("feed");
        }

        private static bool IsWater(ItemDefinition item)
        {
            if (item == null) return false;
            string id = item.itemId ?? "";
            return id.Contains("water");
        }

        private ItemDefinition ResolveProductItem(LivestockProduct product) => product switch
        {
            LivestockProduct.Milk => milkItem,
            LivestockProduct.Wool => woolItem,
            _ => null,
        };

        /// <summary>Herd snapshot for the console.</summary>
        public IReadOnlyList<LivestockHusbandry> Members => _members;
    }
}
