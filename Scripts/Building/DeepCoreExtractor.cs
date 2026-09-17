// Assets/Scripts/VoxelEngine/Building/DeepCoreExtractor.cs
//
// THE DEEP CORE EXTRACTOR — the machine that makes a deep ore node worth finding.
//
// A deep node cannot be hand-mined. This is the only way to get at one, and that is
// the whole point: it converts "I found ore" into "I am going to build here", because
// the extractor needs power, and power needs infrastructure, and infrastructure is an
// outpost.
//
// WHY IT IS NOT JUST A FASTER DRILL
// The ship drill (`GridDrill`) chews voxels in front of it and needs a pilot present.
// This runs unattended on a fixed deposit and produces at a steady rate forever - until
// the node runs dry. Those are different verbs solving different problems, which is
// what stops the new machine from making the old one pointless:
//
//   Hand mining  - immediate, portable, tiny yield
//   Ship drill   - mobile, player-driven, follows visible veins
//   Extractor    - fixed, unattended, huge finite yield, needs a base
//
// DEPLETION IS THE DESIGN
// An infinite extractor would end the resource game the first time one was built. A
// finite one means an outpost has a LIFESPAN, so the player keeps surveying, keeps
// expanding, and eventually abandons and relocates. That loop is the reason the
// roadmap wanted finite nodes rather than just bigger veins.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Generation;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Building
{
    [DisallowMultipleComponent, RequireComponent(typeof(PlacedBlock))]
    public class DeepCoreExtractor : MonoBehaviour, IItemPortHost
    {
        [Header("Extraction")]
        [Tooltip("Items produced per second while powered and over a live node.")]
        public float itemsPerSecond = 1.6f;

        [Tooltip("Power drawn while running. Idle draw is a tenth of this.")]
        public float powerDraw = 480f;

        [Header("Output")]
        public int outputSlots = 6;

        /// <summary>Where extracted ore accumulates until a belt or pipe takes it.</summary>
        public ItemContainer output;

        // ── Runtime ──────────────────────────────────────────────────────────────
        public bool IsRunning { get; private set; }
        public string Status { get; private set; } = "Surveying";

        /// <summary>The node beneath this machine, refreshed as it depletes.</summary>
        public DeepOreNode Node => _node;
        public bool HasNode { get; private set; }

        private DeepOreNode _node;
        private PowerConsumer _power;
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;
        private MaterialRegistry _registry;

        private float _accumulator;
        private float _rescanTimer;

        /// <summary>Lifetime total this machine has pulled, for the console readout.</summary>
        public int TotalExtracted { get; private set; }

        private void Awake()
        {
            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
            if (_power != null) _power.wattsPerSecond = powerDraw * 0.1f;
        }

        private void OnEnable() => Rescan();

        public void EnsureContainers()
        {
            if (output == null) output = new ItemContainer("Extracted Ore", Mathf.Max(1, outputSlots));
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
            _portContainers ??= new ItemPortContainer[1];
            // Output only: nothing is ever inserted into an extractor, so offering an
            // input face would just let players jam it with unrelated cargo.
            _portContainers[0] = new ItemPortContainer("Extracted Ore", output,
                canInput: false, canOutput: true);
            return _portContainers;
        }

        // ── Simulation ───────────────────────────────────────────────────────────
        private void Update()
        {
            EnsureContainers();

            // The node is re-read periodically rather than cached forever, so a machine
            // shows depletion caused by a second extractor on the same deposit.
            _rescanTimer -= Time.deltaTime;
            if (_rescanTimer <= 0f)
            {
                _rescanTimer = 1f;
                Rescan();
            }

            if (!HasNode)
            {
                Stop("No deep deposit here");
                return;
            }

            if (_node.IsDepleted)
            {
                Stop($"{DeepOreField.MaterialName(_node.Material)} deposit exhausted");
                return;
            }

            bool hasPower = _power == null || _power.IsPowered;
            if (!hasPower)
            {
                Stop("No power");
                return;
            }

            var item = ResolveDropItem(_node.Material);
            if (item == null)
            {
                Stop("No item defined for this ore");
                return;
            }

            if (!output.HasSpace(item, 1))
            {
                Stop("Output full");
                return;
            }

            // Running.
            IsRunning = true;
            if (_power != null) _power.wattsPerSecond = powerDraw;

            _accumulator += itemsPerSecond * Time.deltaTime;
            int whole = Mathf.FloorToInt(_accumulator);
            if (whole > 0)
            {
                _accumulator -= whole;

                // Take from the node FIRST, then bank what was actually granted. Reversing
                // these would let two extractors on one node each mint the last item.
                int granted = DeepOreField.Extract(_node, whole);
                if (granted > 0)
                {
                    var leftover = output.Insert(new ItemStack { item = item, count = granted });
                    int stored = granted - (leftover?.count ?? 0);
                    TotalExtracted += stored;

                    // Anything the output refused is handed back to the deposit rather than
                    // destroyed, so a full buffer costs the player time but never ore.
                    if (leftover != null && leftover.count > 0)
                        DeepOreField.Refund(_node, leftover.count);
                }
                Rescan();
            }

            Status = $"Extracting {DeepOreField.MaterialName(_node.Material)}  ·  " +
                     $"{_node.Remaining:N0} left ({_node.Fraction01 * 100f:0}%)";
        }

        private void Stop(string reason)
        {
            IsRunning = false;
            Status = reason;
            _accumulator = 0f;
            if (_power != null) _power.wattsPerSecond = powerDraw * 0.1f;
        }

        private void Rescan()
        {
            HasNode = DeepOreField.TryGetNodeAt(transform.position, out _node);
        }

        private ItemDefinition ResolveDropItem(MaterialId material)
        {
            if (_registry == null) _registry = Resources.Load<MaterialRegistry>("MaterialRegistry");
            if (_registry == null)
            {
                var all = Resources.FindObjectsOfTypeAll<MaterialRegistry>();
                if (all != null && all.Length > 0) _registry = all[0];
            }
            if (_registry == null) return null;

            var def = _registry.Get(material);
            return def != null ? def.dropItem : null;
        }

        /// <summary>Percentage of the deposit remaining, for the console bar.</summary>
        public float Remaining01 => HasNode ? _node.Fraction01 : 0f;
    }
}
