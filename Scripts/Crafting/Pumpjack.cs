// Assets/Scripts/VoxelEngine/Crafting/Pumpjack.cs
//
// Pirate World Jack Pump. It can only run over a rare, infinite oil node
// generated on the Pirate spherical planet. The node is not depleted: the
// expensive pump turns Empty Barrels into Crude Oil Barrels while drawing heavy power.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Crafting
{
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class Pumpjack : MonoBehaviour, IItemPortHost
    {
        [Header("Fuel / Output")]
        [Tooltip("Empty Barrel item consumed each cycle.")]
        public ItemDefinition emptyBarrel;
        [Tooltip("Crude Oil Barrel item produced each cycle.")]
        public ItemDefinition crudeOilBarrel;

        [Header("Infinite Pirate Oil Node")]
        [Tooltip("Seconds per barrel from a rare Pirate World oil node.")]
        public float secondsPerCycle = 14f;
        [Tooltip("Heavy active draw in watts while lifting infinite node oil.")]
        public float baseWattsPerSecond = 4000f;
        [Tooltip("Standby draw in watts while connected but not pumping.")]
        public float idleWattsPerSecond = 120f;
        [Header("Legacy Compatibility")]
        [Tooltip("Retained for existing prefab/save compatibility. Infinite eligibility now uses the explicit Pirate oil-node marker.")]
        public int scanDepth = 120;
        [Tooltip("Retained for existing prefab/save compatibility. Infinite eligibility now uses the explicit Pirate oil-node marker.")]
        public int scanRadius = 3;

        [Header("Containers (auto-created)")]
        public ItemContainer inputC;
        public ItemContainer outputC;

        private PowerConsumer _power;
        private float _progress;
        private Transform _walkingBeam;
        private Transform _crankWheel;
        private Transform _polishedRod;
        private Quaternion _beamRestRotation;
        private Vector3 _rodRestPosition;
        private float _mechanismPhase;

        public float Progress01 => Mathf.Clamp01(_progress / Mathf.Max(0.1f, secondsPerCycle));
        public bool IsOnline => _power != null && _power.IsPowered;
        public bool HasReservoir { get; private set; }
        public float CurrentWattage { get; private set; }
        public bool IsPumping => IsOnline && HasReservoir && _progress > 0f;

        private void Awake()
        {
            // Repair the original low-cost Pumpjack defaults on already placed
            // legacy instances while leaving any deliberately custom tuning intact.
            if (Mathf.Approximately(secondsPerCycle, 8f)
                && Mathf.Approximately(baseWattsPerSecond, 250f)
                && Mathf.Approximately(idleWattsPerSecond, 10f))
            {
                secondsPerCycle = 14f;
                baseWattsPerSecond = 4000f;
                idleWattsPerSecond = 120f;
                scanDepth = Mathf.Max(scanDepth, 120);
                scanRadius = Mathf.Max(scanRadius, 3);
            }

            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.connectRadius = 2.2f;
            CacheMechanism();
        }

        public void EnsureContainers()
        {
            if (inputC == null) inputC = new ItemContainer("Empty Barrels", 1); else inputC.Resize(1);
            if (outputC == null) outputC = new ItemContainer("Crude Oil Output", 2); else outputC.Resize(2);
        }

        private void CacheMechanism()
        {
            _walkingBeam = transform.Find("JackPumpVisuals/WalkingBeam");
            _crankWheel = transform.Find("JackPumpVisuals/CrankWheel");
            _polishedRod = transform.Find("JackPumpVisuals/PolishedRod");
            if (_walkingBeam != null) _beamRestRotation = _walkingBeam.localRotation;
            if (_polishedRod != null) _rodRestPosition = _polishedRod.localPosition;
        }

        private void AnimateMechanism(bool pumping)
        {
            if (!pumping)
            {
                if (_walkingBeam != null) _walkingBeam.localRotation = Quaternion.Slerp(_walkingBeam.localRotation, _beamRestRotation, Time.deltaTime * 3f);
                if (_polishedRod != null) _polishedRod.localPosition = Vector3.Lerp(_polishedRod.localPosition, _rodRestPosition, Time.deltaTime * 3f);
                return;
            }

            _mechanismPhase += Time.deltaTime * Mathf.PI * 2f / Mathf.Max(0.6f, secondsPerCycle * 0.18f);
            float stroke = Mathf.Sin(_mechanismPhase);
            if (_walkingBeam != null)
                _walkingBeam.localRotation = _beamRestRotation * Quaternion.Euler(0f, 0f, stroke * 10f);
            if (_crankWheel != null)
                _crankWheel.localRotation = Quaternion.Euler(0f, _mechanismPhase * Mathf.Rad2Deg, 90f);
            if (_polishedRod != null)
                _polishedRod.localPosition = _rodRestPosition + Vector3.down * ((stroke + 1f) * 0.20f);
        }

        // ── IItemPortHost ───────────────────────────────────────────────────
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;

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
            _portContainers[0] = new ItemPortContainer("Empty Barrels", inputC, canInput: true, canOutput: false);
            _portContainers[1] = new ItemPortContainer("Crude Oil Output", outputC, canInput: false, canOutput: true);
            return _portContainers;
        }

        private void Update()
        {
            EnsureContainers();
            bool active = CanRun();
            CurrentWattage = active ? baseWattsPerSecond : idleWattsPerSecond;
            if (_power != null) _power.wattsPerSecond = CurrentWattage;

            if (!IsOnline || !active)
            {
                _progress = 0f;
                AnimateMechanism(false);
                return;
            }

            _progress += Time.deltaTime;
            AnimateMechanism(true);
            if (_progress >= secondsPerCycle)
            {
                _progress = 0f;
                PumpOneBarrel();
            }
        }

        private bool CanRun()
        {
            // Resolve the node first so the UI can distinguish "no node" from
            // "node present but no empty barrels / output space".
            HasReservoir = FindInfinitePirateOil(out _);
            if (!HasReservoir) return false;
            if (emptyBarrel == null || crudeOilBarrel == null) return false;
            if (inputC.CountOf(emptyBarrel) <= 0) return false;
            if (!outputC.HasSpace(crudeOilBarrel, 1)) return false;
            return true;
        }

        /// <summary>9.16.0 — fill a liquid canister with one click (0.5 L) of crude oil
        /// straight from this jack pump's infinite reservoir node. The node never drains,
        /// so filling the canister costs nothing but power (unpowered jacks refuse). Works
        /// for an empty canister and tops up one already carrying crude oil.</summary>
        public bool TryFillCanister(ItemStack can)
        {
            if (can == null || !(can.item is VoxelEngine.Items.LiquidCanister)) return false;
            if (_power != null && !_power.IsPowered) return false;
            if (!FindInfinitePirateOil(out _)) return false;
            return VoxelEngine.Items.LiquidCanister.AddMl(can, VoxelEngine.Items.LiquidType.CrudeOil,
                VoxelEngine.Items.LiquidCanister.PerClickMl);
        }

        private bool FindInfinitePirateOil(out Vector3Int oilVoxel)
        {
            oilVoxel = default;
            if (ActiveWorld.Current is not SphereWorld sphere || sphere.body == null || sphere.body.settings == null
                || !sphere.body.settings.CanGenerateInfiniteJackPumpNodes) return false;

            // A visible crude puddle alone is a finite seep. The Jack Pump must require the
            // explicit rare-node identity, otherwise it could turn every ordinary oil site
            // into an unintended infinite source.
            if (!VoxelEngine.Generation.PirateOilNode.IsPumpableNear(sphere, transform.position))
                return false;

            oilVoxel = sphere.WorldToVoxel(transform.position);
            return true;
        }

        private void PumpOneBarrel()
        {
            // Infinite node: never drain the crude voxel. The rare site, head-gated
            // construction cost, slow cycle, and 4 kW draw are the balance levers.
            if (!FindInfinitePirateOil(out _)) return;
            if (inputC.Remove(emptyBarrel, 1) <= 0) return;
            outputC.Insert(new ItemStack(crudeOilBarrel, 1));
        }
    }
}
