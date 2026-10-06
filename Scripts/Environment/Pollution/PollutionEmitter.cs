// Assets/Scripts/VoxelEngine/Environment/Pollution/PollutionEmitter.cs

using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Gas;
using VoxelEngine.GridSystem;
using VoxelEngine.Industrial;
using VoxelEngine.Maritime;
using VoxelEngine.Networking;
using VoxelEngine.Power;
using VoxelEngine.Pressure;

namespace VoxelEngine.Environment
{
    /// <summary>
    /// Bridges an existing machine's real operating state into a data-driven direct
    /// emission profile. It never derives pollution from electricity consumption, so
    /// upstream generation is not counted a second time at every powered machine.
    /// </summary>
    public sealed class PollutionEmitter : MonoBehaviour
    {
        public PollutionSourceProfile profile;
        [Min(0f)] public float emissionMultiplier = 1f;
        [Min(0.1f)] public float reportInterval = 1f;

        public float Activity01 { get; private set; }
        public float CurrentAirbornePerSecond { get; private set; }
        public float LifetimeAirborneOutput { get; private set; }

        private float _timer;
        private CoalGeneratorFuel _coal;
        private Furnace _furnace;
        private ElectricFurnace _electricFurnace;
        private OilRefinery _refinery;
        private DistillationPlant _distillationPlant;
        private CatalyticCracker _catalyticCracker;
        private StationaryChemicalPlant _chemicalPlant;
        private GridRefinery _gridRefinery;
        private GridChemicalPlant _gridChemicalPlant;
        private FlareStack _flare;
        private GridFlareStack _gridFlare;
        private GridExhaustPipe _exhaust;

        private void Awake()
        {
            _coal = GetComponentInChildren<CoalGeneratorFuel>(true);
            _furnace = GetComponentInChildren<Furnace>(true);
            _electricFurnace = GetComponentInChildren<ElectricFurnace>(true);
            _refinery = GetComponentInChildren<OilRefinery>(true);
            _distillationPlant = GetComponentInChildren<DistillationPlant>(true);
            _catalyticCracker = GetComponentInChildren<CatalyticCracker>(true);
            _chemicalPlant = GetComponentInChildren<StationaryChemicalPlant>(true);
            _gridRefinery = GetComponentInChildren<GridRefinery>(true);
            _gridChemicalPlant = GetComponentInChildren<GridChemicalPlant>(true);
            _flare = GetComponentInChildren<FlareStack>(true);
            _gridFlare = GetComponentInChildren<GridFlareStack>(true);
            _exhaust = GetComponentInChildren<GridExhaustPipe>(true);
            _timer = Mathf.Abs(GetEntityId().GetHashCode() % 100) * 0.01f * Mathf.Max(0.1f, reportInterval);
        }

        private void Update()
        {
            if (NetworkSession.SimulationIsRemote)
            {
                Activity01 = 0f;
                CurrentAirbornePerSecond = 0f;
                return;
            }

            _timer += Time.deltaTime;
            float interval = Mathf.Max(0.1f, reportInterval);
            if (_timer < interval) return;
            float elapsed = _timer;
            _timer = 0f;

            Activity01 = ResolveActivity01();
            if (profile == null || Activity01 <= 0f || emissionMultiplier <= 0f)
            {
                CurrentAirbornePerSecond = 0f;
                return;
            }

            float scale = Activity01 * emissionMultiplier;
            PollutionLoad perSecond = profile.perSecond * scale;
            CurrentAirbornePerSecond = Mathf.Max(0f, perSecond.airborneSmog);
            PollutionLoad amount = perSecond * elapsed;
            Vector3 releasePoint = transform.TransformPoint(profile.localOffset);
            PollutionService.Emit(releasePoint, amount);
            LifetimeAirborneOutput += Mathf.Max(0f, amount.airborneSmog);
        }

        private float ExhaustActivity01()
        {
            float activity = _exhaust.PlumeLoad01 * (1f - _exhaust.CapturedShare01);
            var servedRoom = _exhaust.ServedRoom;
            var grid = _exhaust.Grid;
            if (servedRoom == null || grid == null || activity <= 0f) return Mathf.Clamp01(activity);

            // A powered room scrubber removes foul gas before it is discharged. Multiple
            // units compound rather than add, so filtration can never become negative.
            foreach (var block in grid.AllBlocks)
            {
                var scrubber = block != null ? block.GetComponent<GridExhaustScrubber>() : null;
                if (scrubber == null || !scrubber.IsWorking) continue;
                var rooms = scrubber.ServicedRooms;
                for (int i = 0; i < rooms.Count; i++)
                {
                    if (rooms[i] != servedRoom) continue;
                    activity *= 1f - Mathf.Clamp01(scrubber.scrubEfficiency);
                    break;
                }
            }
            return Mathf.Clamp01(activity);
        }

        private float ResolveActivity01()
        {
            // Routed exhaust is already the combustion outlet. Its captured share is
            // removed here, which makes existing gas taps/scrubbers mechanically useful.
            if (_exhaust != null)
                return ExhaustActivity01();
            if (_flare != null) return Mathf.Clamp01(_flare.BurnLoad01);
            if (_gridFlare != null) return Mathf.Clamp01(_gridFlare.BurnLoad01);
            if (_coal != null) return _coal.IsBurning ? 1f : 0f;
            if (_furnace != null) return _furnace.IsBurning ? 1f : 0f;
            if (_electricFurnace != null)
                return _electricFurnace.Current != null && _electricFurnace.IsOnline ? 1f : 0f;
            if (_refinery != null) return _refinery.Current != null && _refinery.IsOnline ? 1f : 0f;
            if (_distillationPlant != null)
                return _distillationPlant.Current != null && _distillationPlant.IsOnline ? 1f : 0f;
            if (_catalyticCracker != null)
                return _catalyticCracker.Current != null && _catalyticCracker.IsOnline ? 1f : 0f;
            if (_chemicalPlant != null)
                return _chemicalPlant.Current != null && _chemicalPlant.IsOnline ? 1f : 0f;
            if (_gridRefinery != null)
                return _gridRefinery.Enabled && _gridRefinery.Grid != null
                    && _gridRefinery.Grid.HasPower && _gridRefinery.Current != null ? 1f : 0f;
            if (_gridChemicalPlant != null)
                return _gridChemicalPlant.Enabled && _gridChemicalPlant.Grid != null
                    && _gridChemicalPlant.Grid.HasPower && _gridChemicalPlant.Current != null ? 1f : 0f;
            return 0f;
        }
    }
}
