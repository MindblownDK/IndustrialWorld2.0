// Assets/Scripts/VoxelEngine/Environment/Pollution/AtmosphericCarbonHarvester.cs

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Networking;
using VoxelEngine.Power;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;

namespace VoxelEngine.Environment
{
    /// <summary>
    /// Powered, filter-free local cleanup machine. Captured airborne load is conserved
    /// into Carbon Concentrate, while soil/water runoff becomes Remediation Sludge.
    /// Setup authors Graphite recovery and sludge-stabilization recipes for both outputs.
    /// </summary>
    [RequireComponent(typeof(PowerConsumer))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public sealed class AtmosphericCarbonHarvester : MonoBehaviour,
        IItemPortHost, IItemProvider, IMachineProcessState
    {
        [Header("Output")]
        public ItemDefinition carbonConcentrate;
        public ItemDefinition remediationSludge;
        public ItemContainer outputC;
        [Min(1)] public int outputSlots = 4;

        [Header("Capture")]
        [Min(4f)] public float captureRadius = 96f;
        [Min(0.1f)] public float maxCaptureUnitsPerSecond = 8f;
        [Min(1f)] public float pollutionUnitsPerItem = 60f;
        [Min(4f)] public float runoffCaptureRadius = 72f;
        [Min(0.1f)] public float maxRunoffCaptureUnitsPerSecond = 3f;
        [Min(1f)] public float runoffUnitsPerSludgeItem = 80f;
        [Min(1f)] public float wattsPerSecond = 420f;
        [Min(0.1f)] public float captureInterval = 1f;
        public bool userEnabled = true;

        public float StoredPollutionUnits => _storedPollutionUnits;
        public float StoredRunoffUnits => _storedRunoffUnits;
        public float CurrentCaptureRate { get; private set; }
        public float CurrentRunoffCaptureRate { get; private set; }
        public float LocalAirPollution01 { get; private set; }
        public float LocalRunoffPollution01 { get; private set; }
        public bool IsPowered => _power != null && _power.IsPowered;
        public string Status { get; private set; } = "Idle";

        private float _storedPollutionUnits;
        private float _storedRunoffUnits;
        private float _timer;
        private PowerConsumer _power;
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;

        private const string StoredUnitsKey = "pollutionUnits";
        private const string StoredRunoffUnitsKey = "runoffPollutionUnits";
        private const string EnabledKey = "userEnabled";

        private void Awake()
        {
            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
            _portConfig = GetComponent<PortConfig>();
            _portConfig?.EnsureAllFaces();
        }

        public void EnsureContainers()
        {
            int slots = Mathf.Max(1, outputSlots);
            if (outputC == null) outputC = new ItemContainer("Recovered Pollution", slots);
            else outputC.Resize(slots);
        }

        private void Update()
        {
            EnsureContainers();
            if (_power == null) _power = GetComponent<PowerConsumer>();
            if (_power != null) _power.wattsPerSecond = userEnabled ? wattsPerSecond : 0f;

            LocalAirPollution01 = PollutionService.SampleAirborne01(transform.position);
            LocalRunoffPollution01 = PollutionService.SampleRunoff01(transform.position);
            if (NetworkSession.SimulationIsRemote)
            {
                CurrentCaptureRate = 0f;
                CurrentRunoffCaptureRate = 0f;
                Status = "Host controlled";
                return;
            }

            _timer += Time.deltaTime;
            float interval = Mathf.Max(0.1f, captureInterval);
            if (_timer < interval) return;
            float elapsed = _timer;
            _timer = 0f;
            CurrentCaptureRate = 0f;
            CurrentRunoffCaptureRate = 0f;

            if (!userEnabled) { Status = "Switched off"; return; }
            if (!IsPowered) { Status = "No power"; return; }
            if (carbonConcentrate == null && remediationSludge == null)
            {
                Status = "Output items missing";
                return;
            }

            FlushRecoveredMaterials();
            bool canCaptureAir = carbonConcentrate != null && outputC.HasSpace(carbonConcentrate, 1);
            bool canCaptureRunoff = remediationSludge != null && outputC.HasSpace(remediationSludge, 1);
            if (!canCaptureAir && !canCaptureRunoff)
            {
                Status = "Output full";
                return;
            }

            float capturedAir = canCaptureAir
                ? PollutionService.CaptureAirborne(transform.position, captureRadius,
                    maxCaptureUnitsPerSecond * elapsed)
                : 0f;
            float capturedRunoff = canCaptureRunoff
                ? PollutionService.CaptureRunoff(transform.position, runoffCaptureRadius,
                    maxRunoffCaptureUnitsPerSecond * elapsed)
                : 0f;
            _storedPollutionUnits += capturedAir;
            _storedRunoffUnits += capturedRunoff;
            CurrentCaptureRate = capturedAir / Mathf.Max(0.01f, elapsed);
            CurrentRunoffCaptureRate = capturedRunoff / Mathf.Max(0.01f, elapsed);
            FlushRecoveredMaterials();

            Status = capturedAir + capturedRunoff > 0.001f ? "Capturing"
                : LocalAirPollution01 > 0.01f || LocalRunoffPollution01 > 0.01f
                    ? "Trace collection" : "Local environment clear";
        }

        private void FlushRecoveredMaterials()
        {
            if (outputC == null) return;
            if (carbonConcentrate != null)
            {
                float units = Mathf.Max(1f, pollutionUnitsPerItem);
                while (_storedPollutionUnits >= units && outputC.HasSpace(carbonConcentrate, 1))
                {
                    ItemStack leftover = outputC.Insert(new ItemStack(carbonConcentrate, 1));
                    if (leftover != null && !leftover.IsEmpty) break;
                    _storedPollutionUnits -= units;
                }
            }
            if (remediationSludge != null)
            {
                float units = Mathf.Max(1f, runoffUnitsPerSludgeItem);
                while (_storedRunoffUnits >= units && outputC.HasSpace(remediationSludge, 1))
                {
                    ItemStack leftover = outputC.Insert(new ItemStack(remediationSludge, 1));
                    if (leftover != null && !leftover.IsEmpty) break;
                    _storedRunoffUnits -= units;
                }
            }
        }

        // ── Belt / item-port output ──────────────────────────────────────────────

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
            _portContainers[0] = new ItemPortContainer("Recovered Output", outputC,
                canInput: false, canOutput: true);
            return _portContainers;
        }

        public ItemDefinition PeekOutput(out int count)
        {
            count = 0;
            EnsureContainers();
            for (int i = 0; i < outputC.Size; i++)
            {
                ItemStack stack = outputC.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item == null) continue;
                count = stack.count;
                return stack.item;
            }
            return null;
        }

        public int TryExtract(ItemDefinition item, int count)
        {
            if (item == null || count <= 0 || outputC == null) return 0;
            return outputC.Remove(item, count);
        }

        // ── Existing machine runtime save/network seam ──────────────────────────

        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            state.SetExtra(StoredUnitsKey, Mathf.Max(0f, _storedPollutionUnits));
            state.SetExtra(StoredRunoffUnitsKey, Mathf.Max(0f, _storedRunoffUnits));
            state.SetExtra(EnabledKey, userEnabled ? 1f : 0f);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null || state.IsEmpty) return;
            _storedPollutionUnits = Mathf.Max(0f,
                state.GetExtra(StoredUnitsKey, _storedPollutionUnits));
            _storedRunoffUnits = Mathf.Max(0f,
                state.GetExtra(StoredRunoffUnitsKey, _storedRunoffUnits));
            userEnabled = state.GetExtraBool(EnabledKey, userEnabled);
        }
    }
}
