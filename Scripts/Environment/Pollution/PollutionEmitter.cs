// Assets/Scripts/VoxelEngine/Environment/Pollution/PollutionEmitter.cs

using System.Collections.Generic;
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
        private static readonly List<PollutionEmitter> s_active = new(128);

        public PollutionSourceProfile profile;
        [Min(0f)] public float emissionMultiplier = 1f;
        [Min(0.1f)] public float reportInterval = 1f;

        public float Activity01 { get; private set; }
        public float CurrentAirbornePerSecond { get; private set; }
        public float CurrentRunoffPerSecond { get; private set; }
        public float LifetimeAirborneOutput { get; private set; }
        public float LifetimeRunoffOutput { get; private set; }
        public float RatedAirbornePerSecond => profile != null
            ? Mathf.Max(0f, profile.perSecond.airborneSmog * emissionMultiplier)
            : 0f;
        public float RatedRunoffPerSecond => profile != null
            ? Mathf.Max(0f, profile.perSecond.runoff * emissionMultiplier)
            : 0f;
        public bool IsActivelyEmitting => isActiveAndEnabled
            && CurrentAirbornePerSecond + CurrentRunoffPerSecond > 0.0001f;
        public string DisplayName => SourceDisplayName();

        /// <summary>
        /// Finds the strongest live industrial outlet around a world position without
        /// allocating a temporary collection. Surface-only callers may reject grid-mounted
        /// exhaust because ground creatures cannot meaningfully path onto moving hulls.
        /// </summary>
        public static bool TryFindStrongestActiveNear(Vector3 worldPosition, float radiusMetres,
            out PollutionEmitter strongest, bool surfaceSourcesOnly = false)
        {
            strongest = null;
            float radius = Mathf.Max(1f, radiusMetres);
            float radiusSq = radius * radius;
            float bestRate = 0.0001f;
            int bestId = int.MaxValue;

            for (int i = s_active.Count - 1; i >= 0; i--)
            {
                PollutionEmitter emitter = s_active[i];
                if (emitter == null)
                {
                    s_active.RemoveAt(i);
                    continue;
                }
                if (!emitter.IsActivelyEmitting) continue;
                if (surfaceSourcesOnly
                    && emitter.GetComponentInParent<GridEntity>() != null) continue;
                if ((emitter.ReleasePoint - worldPosition).sqrMagnitude > radiusSq) continue;

                float rate = emitter.CurrentAirbornePerSecond + emitter.CurrentRunoffPerSecond;
                int id = emitter.GetEntityId().GetHashCode();
                if (rate < bestRate || (Mathf.Approximately(rate, bestRate) && id >= bestId))
                    continue;
                bestRate = rate;
                bestId = id;
                strongest = emitter;
            }
            return strongest != null;
        }

        /// <summary>
        /// Resolves the emitters whose readings should be presented for a selected machine.
        /// Most machines own their emitter. Maritime engines deliberately route combustion
        /// through exhaust pipes, so selecting an engine reports the serving pipe(s) instead.
        /// </summary>
        public static void CollectForDisplay(Component source, List<PollutionEmitter> results)
        {
            if (results == null) return;
            results.Clear();
            if (source == null) return;

            AddUnique(results, source.GetComponent<PollutionEmitter>());
            AddUnique(results, source.GetComponentInParent<PollutionEmitter>());
            var descendants = source.GetComponentsInChildren<PollutionEmitter>(true);
            for (int i = 0; i < descendants.Length; i++) AddUnique(results, descendants[i]);

            var engine = source.GetComponent<GridMaritimeEngine>()
                ?? source.GetComponentInParent<GridMaritimeEngine>();
            if (engine == null || engine.Grid == null) return;

            foreach (var block in engine.Grid.AllBlocks)
            {
                var pipe = block != null ? block.GetComponent<GridExhaustPipe>() : null;
                if (pipe == null || !pipe.ServesEngine(engine)) continue;
                AddUnique(results, pipe.GetComponent<PollutionEmitter>());
            }
        }

        private static void AddUnique(List<PollutionEmitter> results, PollutionEmitter emitter)
        {
            if (emitter != null && !results.Contains(emitter)) results.Add(emitter);
        }

        /// <summary>
        /// Returns live, active outlets near a map cell, ordered by combined air/runoff rate.
        /// This is current-source attribution rather than invented history: a residual plume
        /// with every machine switched off correctly reports no active source nearby.
        /// </summary>
        public static void CollectActiveNear(Vector3 worldPosition, float radiusMetres,
            List<PollutionSourceReading> results)
        {
            if (results == null) return;
            results.Clear();
            float radiusSq = Mathf.Max(1f, radiusMetres) * Mathf.Max(1f, radiusMetres);
            for (int i = s_active.Count - 1; i >= 0; i--)
            {
                PollutionEmitter emitter = s_active[i];
                if (emitter == null)
                {
                    s_active.RemoveAt(i);
                    continue;
                }
                float air = emitter.CurrentAirbornePerSecond;
                float runoff = emitter.CurrentRunoffPerSecond;
                if (air + runoff <= 0.0001f) continue;
                Vector3 releasePoint = emitter.ReleasePoint;
                if ((releasePoint - worldPosition).sqrMagnitude > radiusSq) continue;
                results.Add(new PollutionSourceReading(emitter.SourceDisplayName(), releasePoint,
                    air, runoff));
            }
            results.Sort((a, b) => b.TotalPerSecond.CompareTo(a.TotalPerSecond));
        }

        public Vector3 ReleasePoint => profile != null
            ? transform.TransformPoint(profile.localOffset)
            : transform.position;

        private string SourceDisplayName()
        {
            // Routed exhaust is an outlet shared by the engine room; its authored source
            // name is more honest than calling the whole contribution merely "Exhaust Pipe".
            if (_exhaust != null && profile != null && !string.IsNullOrWhiteSpace(profile.displayName))
                return profile.displayName;

            var placed = GetComponentInParent<VoxelEngine.Building.PlacedBlock>();
            if (placed != null && placed.Item != null
                && !string.IsNullOrWhiteSpace(placed.Item.displayName))
                return placed.Item.displayName;

            var gridBlock = GetComponentInParent<GridBlock>();
            if (gridBlock != null)
            {
                if (gridBlock.SourceItem != null && !string.IsNullOrWhiteSpace(gridBlock.SourceItem.displayName))
                    return gridBlock.SourceItem.displayName;
                if (!string.IsNullOrWhiteSpace(gridBlock.blockName)) return gridBlock.blockName;
            }

            if (profile != null && !string.IsNullOrWhiteSpace(profile.displayName))
                return profile.displayName;
            string fallback = gameObject.name.Replace("(Clone)", string.Empty).Trim();
            return string.IsNullOrEmpty(fallback) ? "Industrial Source" : fallback;
        }

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

        private void OnEnable()
        {
            if (!s_active.Contains(this)) s_active.Add(this);
        }

        private void OnDisable()
        {
            s_active.Remove(this);
        }

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
                // Guests never author pollution, but replicated machine/exhaust state can still
                // drive an honest live panel instead of falsely reporting a clean machine.
                Activity01 = ResolveActivity01();
                CurrentAirbornePerSecond = profile != null
                    ? Mathf.Max(0f, profile.perSecond.airborneSmog * emissionMultiplier * Activity01)
                    : 0f;
                CurrentRunoffPerSecond = profile != null
                    ? Mathf.Max(0f, profile.perSecond.runoff * emissionMultiplier * Activity01)
                    : 0f;
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
                CurrentRunoffPerSecond = 0f;
                return;
            }

            float scale = Activity01 * emissionMultiplier;
            PollutionLoad perSecond = profile.perSecond * scale;
            CurrentAirbornePerSecond = Mathf.Max(0f, perSecond.airborneSmog);
            CurrentRunoffPerSecond = Mathf.Max(0f, perSecond.runoff);
            PollutionLoad amount = perSecond * elapsed;
            Vector3 releasePoint = transform.TransformPoint(profile.localOffset);
            PollutionService.Emit(releasePoint, amount);
            LifetimeAirborneOutput += Mathf.Max(0f, amount.airborneSmog);
            LifetimeRunoffOutput += Mathf.Max(0f, amount.runoff);
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
