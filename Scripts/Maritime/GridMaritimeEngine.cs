// Assets/Scripts/VoxelEngine/Maritime/GridMaritimeEngine.cs
//
// Maritime engine block. Three tiers share one class:
//
//   Small  (Crude Inline-4 visual, 1×1×1 block)   — burns Wood/Coal items
//   Medium (Heavy Fuel Oil V8 visual)              — burns Heavy Fuel Oil
//   Giant  (MGO Marine V12 visual)                 — burns Marine Gas Oil
//
// Fuel is drawn from grid storage (cargo for solids, liquid tanks for liquids)
// into an internal buffer. FuelAvailable01 = buffer fill × throttle × penalties.
//
// REQUIRES an adjacent Exhaust Pipe — without one the engine chokes and
// produces zero torque. Turbochargers only boost when mounted on named engine
// attachment points.
//
// v6.10.0-dev — Modular Upgrade Modules + Dynamic Heat & Coolant Penalty System:
//   • Module Slots (2/3/4 by tier) accept EngineModuleItem upgrades.
//   • Live temperature model: <90°C normal, ≥90°C knocking (-25% fuel
//     efficiency), ≥100°C mechanical failure (shaft stops, black smoke).
//   • Efficiency Tuning Chip unlocks a mandatory active-coolant requirement
//     (engine overheats in ~15 s without coolant flow).
//   • Super-Cooler Radiator Jacket needs a continuous fresh/sea water feed.
//   • Honest fuel ETA: EstimatedFuelSecondsRemaining replaces the misleading
//     "buffer seconds" readout (burn time at the CURRENT throttle draw rate).

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    /// <summary>Engine size tier — drives torque, RPM, fuel type, mass and turbo slots.</summary>
    public enum EngineTier : byte
    {
        /// <summary>Starter crude engine with a single 1×1×1 large-grid visual block. 1 small turbo slot.</summary>
        Small = 0,
        /// <summary>Large 4×3×2 visual heavy-fuel ship engine. 2 turbo slots (small or large).</summary>
        Medium = 1,
        /// <summary>Colossal 6×5×3 visual MGO ship engine. 4 turbo slots.</summary>
        Giant = 2,
    }

    public class GridMaritimeEngine : MaritimeBlockBase, IGridDataProvider
    {
        private const string TurboAttachmentNamePrefix = "Turbo attachment point ";
        private static Material _turboAttachmentMaterial;

        public override MechanicalNodeType NodeType => MechanicalNodeType.Engine;

        [Header("Engine Tier")]
        public EngineTier tier = EngineTier.Small;

        [Header("Performance")]
        [Tooltip("Maximum torque output in N·m (before turbo boost).")]
        public float maxTorque = 8000f;
        [Tooltip("Maximum rotational speed in rev/min.")]
        public float maxRPM = 1500f;
        [Tooltip("If enabled, the engine idles whenever it is fueled, cooled, enabled, and has exhaust. This keeps the machinery visibly alive and provides low RPM shaft output even before helm throttle increases.")]
        public bool idleWhenEnabled = true;
        [Range(0f, 0.35f)]
        [Tooltip("Minimum throttle fraction used while idling.")]
        public float idleThrottleFraction = 0.08f;

        [Header("Fuel")]
        public MaritimeFuelKind fuelKind = MaritimeFuelKind.Solid;
        [Tooltip("Liquid fuel type consumed (when fuelKind = Liquid).")]
        public LiquidType liquidFuel = LiquidType.LiquidFuel;
        [Tooltip("Internal fuel buffer capacity. Solid = burn-seconds, Liquid = litres.")]
        public float fuelBufferCapacity = 60f;
        [Tooltip("Fuel consumed per second at full throttle. Solid = burn-sec/sec, Liquid = litres/sec.")]
        public float fuelConsumptionRate = 1f;
        [Tooltip("Litres pulled from connected liquid pipe networks per second when refilling.")]
        public float liquidRefillRate = 10f;
        public const int SolidFuelSlotCount = 4;
        public ItemContainer SolidFuelInput { get; private set; }

        [Header("Exhaust Gas")]
        [Tooltip("Maximum exhaust gas backlog before the engine chokes and stops.")]
        public float exhaustGasCapacity = 100f;
        [Tooltip("Exhaust gas produced per second at full throttle.")]
        public float exhaustGasRate = 8f;
        [Tooltip("Exhaust gas vented per second through an adjacent Exhaust Pipe.")]
        public float exhaustVentRate = 12f;
        [Tooltip("At this fill ratio (0..1) the engine starts losing power from back-pressure.")]
        [Range(0.5f, 0.99f)] public float exhaustChokeThreshold = 0.8f;

        [Header("Coolant")]
        [Tooltip("Internal coolant buffer capacity (litres).")]
        public float coolantCapacity = 50f;
        [Tooltip("Coolant consumed per second at full throttle (L/s).")]
        public float coolantConsumptionRate = 0.5f;
        [Tooltip("Coolant pulled from grid tanks per second when refilling.")]
        public float coolantRefillRate = 5f;
        [Tooltip("Current coolant buffer level (L).")]
        public float CoolantBuffer { get; private set; }
        /// <summary>0..1 coolant fill ratio.</summary>
        public float CoolantFill01 => coolantCapacity > 0f ? Mathf.Clamp01(CoolantBuffer / coolantCapacity) : 0f;
        /// <summary>True if using Marine Engine Coolant (vs plain water).</summary>
        public bool UsingPremiumCoolant { get; private set; }
        /// <summary>True if the engine has coolant available.</summary>
        public bool HasCoolant => CoolantBuffer > 0.01f;

        [Header("Thermal Management")]
        [Tooltip("Heat generated per second at full throttle (°C/s) before module bonuses.")]
        public float baseHeatRate = 1.4f;
        [Tooltip("Passive heat dissipation per second (°C/s) with no coolant and no radiator.")]
        public float baseDissipationRate = 1.0f;
        [Tooltip("Extra dissipation per second (°C/s) while coolant is flowing.")]
        public float coolantDissipationRate = 2.0f;
        /// <summary>Engine temperature in °C. Rises with load, sinks from dissipation.</summary>
        public float TemperatureC { get; private set; } = AmbientTemperatureC;
        /// <summary>Anchored fault state — stays latched until the engine cools below RecoverTemperatureC.</summary>
        public bool CriticalFailure { get; private set; }

        // ── Thermal thresholds (spec) ────────────────────────────────
        /// <summary>Low/normal operating heat.</summary>
        public const float AmbientTemperatureC = 25f;
        /// <summary>Comfort ceiling — below this the engine is perfectly happy.</summary>
        public const float NormalTemperatureC = 70f;
        /// <summary>Overheating: engine knocks, fuel efficiency drops 25%.</summary>
        public const float KnockingTemperatureC = 90f;
        /// <summary>Critical heat: mechanical failure — output shaft stops, heavy black smoke.</summary>
        public const float CriticalTemperatureC = 100f;
        /// <summary>Failure latch releases below this temperature (hysteresis band).</summary>
        public const float RecoverTemperatureC = 80f;
        /// <summary>Hard ceiling so temperature never runs away to silly numbers.</summary>
        public const float MaxTemperatureC = 130f;
        /// <summary>Forced heat rise (°C/s) when an Efficiency Tuning Chip is installed
        /// but no coolant is actively flowing. 25°C → 100°C in roughly 15 s (spec).</summary>
        public const float EfficiencyChipDryHeatRate = 5.2f;

        /// <summary>0..1 engine heat normalized against the critical point (UI bars).</summary>
        public float Heat01 => Mathf.Clamp01(TemperatureC / CriticalTemperatureC);
        /// <summary>≥ 90°C — engine knocks and burns 25% more fuel for the same work.</summary>
        public bool IsOverheating => TemperatureC >= KnockingTemperatureC;
        /// <summary>≥ 100°C or latched failure — output shaft is stopped mechanically.</summary>
        public bool IsCriticalHeat => CriticalFailure;

        // ── Modules / upgrades ───────────────────────────────────────
        /// <summary>Module container. Socket EngineModuleItems to boost output —
        /// high-tier upgrades add logistics requirements (coolant, water).</summary>
        public ItemContainer ModuleSlots { get; private set; }
        /// <summary>Module slot count per tier: Inline-4 = 2, V8 = 3, V12 = 4.</summary>
        public int MaxModuleSlots => tier switch
        {
            EngineTier.Small  => 2,
            EngineTier.Medium => 3,
            EngineTier.Giant  => 4,
            _ => 1,
        };

        // Computed module tallies (refreshed every simulation tick).
        public int TurboModuleCount { get; private set; }
        public int EfficiencyChipCount { get; private set; }
        public int InjectorModuleCount { get; private set; }
        public int RadiatorModuleCount { get; private set; }
        /// <summary>Total output multiplier from socketed modules (1 = stock).</summary>
        public float ModuleOutputMultiplier { get; private set; } = 1f;
        /// <summary>Total RPM cap multiplier from socketed modules (1 = stock).</summary>
        public float ModuleSpeedCapMultiplier { get; private set; } = 1f;
        /// <summary>Total fuel-use multiplier from socketed modules (1 = stock).</summary>
        public float ModuleFuelUseMultiplier { get; private set; } = 1f;
        /// <summary>True while a Super-Cooler Radiator Jacket is socketed AND water is flowing.</summary>
        public bool RadiatorCoolingActive { get; private set; }
        /// <summary>Water intake state of the radiator (0..1 of demand met this tick).</summary>
        public float RadiatorWaterFill01 { get; private set; }

        // ── Smoke VFX modifiers (read by adjacent GridExhaustPipe) ───
        /// <summary>Exhaust smoke velocity multiplier (High-Flow Turbocharger module).</summary>
        public float SmokeSpeedMultiplier { get; private set; } = 1f;
        /// <summary>True while Overclocked Fuel Injectors dirty the exhaust.</summary>
        public bool SmokeDirty => InjectorModuleCount > 0;
        /// <summary>0..1 live engine speed — normalized RPM against the module-raised cap.
        /// Drives the visible crankshaft RPM and the piston playback rate simultaneously.</summary>
        public float EngineSpeed01
        {
            get
            {
                float cap = maxRPM * ModuleSpeedCapMultiplier;
                return cap > 0.01f ? Mathf.Clamp01(CurrentRPM / cap) : 0f;
            }
        }

        [Header("State (read-only)")]
        /// <summary>Current fuel buffer level (0..capacity).</summary>
        public float FuelBuffer { get; private set; }

        /// <summary>0..1 fill ratio of the internal fuel buffer.</summary>
        public float FuelFill01 => fuelBufferCapacity > 0f ? Mathf.Clamp01(FuelBuffer / fuelBufferCapacity) : 0f;

        /// <summary>Current exhaust gas backlog (0..capacity).</summary>
        public float ExhaustGas { get; private set; }

        /// <summary>0..1 fill ratio of the exhaust gas backlog.</summary>
        public float ExhaustFill01 => exhaustGasCapacity > 0f ? Mathf.Clamp01(ExhaustGas / exhaustGasCapacity) : 0f;

        /// <summary>True while the engine is actively producing torque.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>True if exhaust gas backlog is critically high (engine losing power).</summary>
        public bool IsChoked => ExhaustFill01 >= exhaustChokeThreshold;

        /// <summary>True if an exhaust pipe is adjacent (otherwise the engine chokes).</summary>
        public bool HasExhaust { get; private set; }

        /// <summary>Current RPM (written back by ApplyResults).</summary>
        public float CurrentRPM { get; private set; }

        /// <summary>Current fuel consumption per second (solid = burn-units/s, liquid = L/s).</summary>
        public float CurrentUsage { get; private set; }

        /// <summary>Current torque output (for UI).</summary>
        public float CurrentTorque { get; private set; }

        /// <summary>0..1 stress level (torque vs max, with exhaust + heat penalties).</summary>
        public float Stress01 { get; private set; }

        /// <summary>True when the engine is overstressed (torque demand exceeds safe limits).</summary>
        public bool IsOverstressed => Stress01 > 0.95f;

        /// <summary>Number of turbochargers connected to this engine (for UI).</summary>
        public int ConnectedTurboCount { get; private set; }
        /// <summary>Total turbo boost multiplier (1.0 = none, 1.4 = one small, etc.).</summary>
        public float TurboBoostTotal { get; private set; } = 1f;
        /// <summary>Max turbo slots this engine supports.</summary>
        public int MaxTurboSlots => tier switch
        {
            EngineTier.Small  => 1,
            EngineTier.Medium => 2,
            EngineTier.Giant  => 4,
            _ => 0,
        };

        /// <summary>Honest burn-time estimate: seconds of fuel remaining at the CURRENT
        /// draw rate. When the engine is not consuming yet, the idle-rate estimate is
        /// shown so the number is still meaningful before throttle-up.</summary>
        public float EstimatedFuelSecondsRemaining
        {
            get
            {
                float rate = CurrentUsage;
                if (rate <= 0.0001f)
                {
                    float idleFrac = idleWhenEnabled ? Mathf.Max(0.02f, idleThrottleFraction) : 1f;
                    rate = fuelConsumptionRate * idleFrac;
                }
                if (IsOverheating) rate *= 4f / 3f; // knocking wastes 25% efficiency
                return rate > 0.0001f ? FuelBuffer / rate : 0f;
            }
        }

        /// <summary>Formats seconds as a compact human duration (e.g. "2m 14s", "43s").</summary>
        public static string FormatDuration(float seconds)
        {
            if (seconds <= 0.5f) return "0s";
            if (seconds < 90f) return $"{seconds:0}s";
            int total = Mathf.FloorToInt(seconds);
            int m = total / 60;
            int s = total % 60;
            if (m < 60) return $"{m}m {s:00}s";
            int h = m / 60;
            m %= 60;
            return $"{h}h {m:00}m";
        }

        public override float ContentMass
        {
            get
            {
                float m = 0f;
                if (fuelKind == MaritimeFuelKind.Liquid)
                    m += FuelBuffer * liquidFuel.DensityKgPerL();
                else if (SolidFuelInput != null)
                    m += MassUtil.ContainerMass(SolidFuelInput);
                if (ModuleSlots != null)
                    m += MassUtil.ContainerMass(ModuleSlots);
                // Exhaust gas adds mass too (compressed gas is heavy).
                m += ExhaustGas * 0.01f;
                return m;
            }
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            // Auto-configure based on tier.
            switch (tier)
            {
                case EngineTier.Small:
                    blockName = "Crude Engine";
                    fuelKind = MaritimeFuelKind.Solid;
                    if (Mathf.Approximately(maxTorque, 8000f)) maxTorque = 18000f;
                    if (Mathf.Approximately(fuelBufferCapacity, 60f)) fuelBufferCapacity = 120f;
                    if (Mathf.Approximately(fuelConsumptionRate, 1f)) fuelConsumptionRate = 1f;
                    if (Mathf.Approximately(baseHeatRate, 1.4f)) baseHeatRate = 1.0f;
                    if (Mathf.Approximately(baseDissipationRate, 1.0f)) baseDissipationRate = 1.1f;
                    break;
                case EngineTier.Medium:
                    blockName = "Heavy Fuel Oil Engine";
                    fuelKind = MaritimeFuelKind.Liquid;
                    liquidFuel = LiquidType.HeavyFuelOil;
                    if (Mathf.Approximately(maxTorque, 40000f)) maxTorque = 125000f;
                    if (Mathf.Approximately(fuelBufferCapacity, 80f)) fuelBufferCapacity = 240f;
                    if (Mathf.Approximately(fuelConsumptionRate, 2f)) fuelConsumptionRate = 2f;
                    if (Mathf.Approximately(liquidRefillRate, 8f)) liquidRefillRate = 28f;
                    if (Mathf.Approximately(coolantCapacity, 50f)) coolantCapacity = 180f;
                    if (Mathf.Approximately(coolantRefillRate, 5f)) coolantRefillRate = 20f;
                    if (Mathf.Approximately(baseHeatRate, 1.4f)) baseHeatRate = 2.4f;
                    if (Mathf.Approximately(coolantDissipationRate, 2.0f)) coolantDissipationRate = 2.9f;
                    break;
                case EngineTier.Giant:
                    blockName = "MGO Engine";
                    fuelKind = MaritimeFuelKind.Liquid;
                    liquidFuel = LiquidType.MarineGasOil;
                    if (Mathf.Approximately(maxTorque, 500000f)) maxTorque = 950000f;
                    if (Mathf.Approximately(fuelBufferCapacity, 300f) || Mathf.Approximately(fuelBufferCapacity, 500f)) fuelBufferCapacity = 1200f;
                    if (Mathf.Approximately(fuelConsumptionRate, 6f) || Mathf.Approximately(fuelConsumptionRate, 12f)) fuelConsumptionRate = 12f;
                    if (Mathf.Approximately(liquidRefillRate, 25f) || Mathf.Approximately(liquidRefillRate, 40f)) liquidRefillRate = 110f;
                    if (Mathf.Approximately(coolantCapacity, 50f)) coolantCapacity = 800f;
                    if (Mathf.Approximately(coolantRefillRate, 5f)) coolantRefillRate = 60f;
                    if (Mathf.Approximately(baseHeatRate, 1.4f)) baseHeatRate = 3.4f;
                    if (Mathf.Approximately(coolantDissipationRate, 2.0f)) coolantDissipationRate = 4.2f;
                    break;
            }
            FuelBuffer = Mathf.Min(FuelBuffer, fuelBufferCapacity);
            if (TemperatureC < AmbientTemperatureC) TemperatureC = AmbientTemperatureC;
            EnsureSolidFuelInput();
            EnsureModuleSlots();
            EnsureTurboAttachmentMarkers();
        }

        public void EnsureSolidFuelInput()
        {
            if (fuelKind != MaritimeFuelKind.Solid)
            {
                SolidFuelInput = null;
                return;
            }

            if (SolidFuelInput == null) SolidFuelInput = new ItemContainer("Fuel Hopper", SolidFuelSlotCount);
            else SolidFuelInput.Resize(SolidFuelSlotCount);
            SolidFuelInput.AcceptFilter = (item, wanted) => IsValidSolidFuel(item) ? wanted : 0;
        }

        /// <summary>Ensure (or right-size) the module socket container. Re-applies the
        /// tier-compatibility accept gate so sockets migrate cleanly when a prefab is
        /// re-loaded by the persistence layer.</summary>
        public void EnsureModuleSlots()
        {
            int count = MaxModuleSlots;
            if (ModuleSlots == null) ModuleSlots = new ItemContainer("Module Slots", count);
            else ModuleSlots.Resize(count);
            ModuleSlots.AcceptFilter = (item, wanted) => CanSocketModule(item) ? wanted : 0;
        }

        /// <summary>Module container accessor that guarantees the container exists (UI use).</summary>
        public ItemContainer GetModuleSlots()
        {
            EnsureModuleSlots();
            return ModuleSlots;
        }

        /// <summary>True when the item may be socketed into this engine's module slots.</summary>
        public bool CanSocketModule(ItemDefinition item)
        {
            return item is EngineModuleItem module && module.IsCompatibleWithTier(tier);
        }

        private static bool IsValidSolidFuel(ItemDefinition item)
        {
            return item is ResourceItem resource && resource.fuelSeconds > 0f;
        }

        /// <summary>Returns true when the supplied grid cell is one of this engine's named turbo slots.</summary>
        public bool CanAttachTurboAt(Vector3Int turboGridPosition, TurboTier turboTier)
        {
            return IsTurboTierCompatible(tier, turboTier) && TryGetTurboAttachmentIndex(turboGridPosition, out _);
        }

        /// <summary>Finds the attachment-slot index occupied by <paramref name="turboGridPosition"/>.</summary>
        public bool TryGetTurboAttachmentIndex(Vector3Int turboGridPosition, out int index)
        {
            int slotCount = MaxTurboSlots;
            for (int i = 0; i < slotCount; i++)
            {
                if (GridPos + TransformLocalSlotOffsetToGrid(GetTurboAttachmentLocalOffset(i)) == turboGridPosition)
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        /// <summary>Small turbos fit every engine tier; large turbos start at HFO/MGO engines.</summary>
        public static bool IsTurboTierCompatible(EngineTier engineTier, TurboTier turboTier)
        {
            return turboTier != TurboTier.Large || engineTier != EngineTier.Small;
        }

        private Vector3Int GetTurboAttachmentLocalOffset(int slotIndex)
        {
            switch (tier)
            {
                case EngineTier.Small:
                    return Vector3Int.right;
                case EngineTier.Medium:
                    return slotIndex == 0 ? Vector3Int.right : Vector3Int.left;
                case EngineTier.Giant:
                    switch (slotIndex)
                    {
                        case 0: return Vector3Int.right;
                        case 1: return Vector3Int.left;
                        case 2: return Vector3Int.up;
                        default: return new Vector3Int(0, 0, -1);
                    }
                default:
                    return Vector3Int.right;
            }
        }

        private Vector3Int TransformLocalSlotOffsetToGrid(Vector3Int localOffset)
        {
            if (Grid == null) return localOffset;

            Vector3 worldDirection = transform.TransformDirection(new Vector3(localOffset.x, localOffset.y, localOffset.z));
            Vector3 gridDirection = Grid.transform.InverseTransformDirection(worldDirection);
            return SnapToGridCardinal(gridDirection);
        }

        private static Vector3Int SnapToGridCardinal(Vector3 direction)
        {
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right;
            float ax = Mathf.Abs(direction.x);
            float ay = Mathf.Abs(direction.y);
            float az = Mathf.Abs(direction.z);

            if (ax >= ay && ax >= az) return direction.x >= 0f ? Vector3Int.right : Vector3Int.left;
            if (ay >= ax && ay >= az) return direction.y >= 0f ? Vector3Int.up : Vector3Int.down;
            return direction.z >= 0f ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
        }

        private void EnsureTurboAttachmentMarkers()
        {
            int slotCount = MaxTurboSlots;
            float cs = Grid != null ? Grid.gridSize.CellSize() : VoxelEngine.GridSystem.GridSize.Large.CellSize();
            Vector3 markerScale = tier switch
            {
                EngineTier.Giant => Vector3.one * cs * 0.30f,
                EngineTier.Medium => Vector3.one * cs * 0.22f,
                _ => Vector3.one * cs * 0.14f,
            };

            for (int i = 0; i < slotCount; i++)
            {
                string markerName = $"{TurboAttachmentNamePrefix}{i}";
                Transform existing = transform.Find(markerName);
                Vector3 markerPosition = GetTurboAttachmentMarkerPosition(i, cs);
                if (existing != null)
                {
                    existing.localPosition = markerPosition;
                    existing.localRotation = Quaternion.identity;
                    if (existing.childCount > 0)
                    {
                        existing.localScale = Vector3.one;
                        for (int childIndex = 0; childIndex < existing.childCount; childIndex++)
                            existing.GetChild(childIndex).localScale = markerScale;
                    }
                    else
                    {
                        existing.localScale = markerScale;
                    }
                    continue;
                }

                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = markerName;
                marker.transform.SetParent(transform, false);
                marker.transform.localPosition = markerPosition;
                marker.transform.localRotation = Quaternion.identity;
                marker.transform.localScale = markerScale;

                var collider = marker.GetComponent<Collider>();
                if (collider != null) Destroy(collider);

                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = GetTurboAttachmentMaterial();
            }
        }

        /// <summary>Visual socket positions on the v6.10 engine models (Inline-4 / V8 / V12).
        /// The persistent mesh builder draws matching flange bases at these exact spots so
        /// the runtime snapping markers sit flush on the machined turbo mounting pads.</summary>
        private Vector3 GetTurboAttachmentMarkerPosition(int slotIndex, float cellSize)
        {
            return tier switch
            {
                // Inline-4: rectangular exhaust-manifold port, top-right.
                EngineTier.Small => new Vector3(cellSize * 0.16f, cellSize * 0.30f, -cellSize * 0.06f),
                // HFO V8: two flanged turbo mounts in the valley, toward the front & back.
                EngineTier.Medium => slotIndex == 0
                    ? new Vector3(cellSize * 0.58f, cellSize * 0.42f, -cellSize * 0.16f)
                    : new Vector3(-cellSize * 0.58f, cellSize * 0.42f, -cellSize * 0.16f),
                // MGO V12: four mounts in a 2×2 grid on the central exhaust plenum.
                EngineTier.Giant => slotIndex switch
                {
                    0 => new Vector3(cellSize * 1.24f, cellSize * 0.66f, -cellSize * 0.24f),
                    1 => new Vector3(-cellSize * 1.24f, cellSize * 0.66f, -cellSize * 0.24f),
                    2 => new Vector3(0f, cellSize * 0.94f, cellSize * 0.36f),
                    _ => new Vector3(0f, cellSize * 0.82f, -cellSize * 0.90f),
                },
                _ => new Vector3(GetTurboAttachmentLocalOffset(slotIndex).x, GetTurboAttachmentLocalOffset(slotIndex).y, GetTurboAttachmentLocalOffset(slotIndex).z) * (cellSize * 0.52f)
            };
        }

        private static Material GetTurboAttachmentMaterial()
        {
            if (_turboAttachmentMaterial != null) return _turboAttachmentMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _turboAttachmentMaterial = new Material(shader)
            {
                name = "Turbo Attachment Point",
                color = new Color(0.10f, 0.85f, 1.00f, 1f)
            };
            if (_turboAttachmentMaterial.HasProperty("_BaseColor"))
                _turboAttachmentMaterial.SetColor("_BaseColor", new Color(0.10f, 0.85f, 1.00f, 1f));
            if (_turboAttachmentMaterial.HasProperty("_EmissionColor"))
            {
                _turboAttachmentMaterial.EnableKeyword("_EMISSION");
                _turboAttachmentMaterial.SetColor("_EmissionColor", new Color(0.02f, 0.35f, 0.50f, 1f));
            }
            if (_turboAttachmentMaterial.HasProperty("_Metallic")) _turboAttachmentMaterial.SetFloat("_Metallic", 0.25f);
            if (_turboAttachmentMaterial.HasProperty("_Smoothness")) _turboAttachmentMaterial.SetFloat("_Smoothness", 0.85f);
            return _turboAttachmentMaterial;
        }

        // ══════════════════════════════════════════════════════════════
        //  IMechanicalBlock
        // ══════════════════════════════════════════════════════════════
        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.MaxTorque = maxTorque;
            node.MaxRPM = maxRPM;
            node.GearRatio = 1f;
            node.PropellerSize = 1f;
            node.OutputMultiplier = 1f;

            if (tier == EngineTier.Giant)
                node.SetFlag(MechanicalFlags.GiantDiesel);
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            float dt = Time.fixedDeltaTime;
            EnsureModuleSlots();
            RefreshModuleTotals();

            float requestedThrottle = Enabled && idleWhenEnabled
                ? Mathf.Max(throttle, idleThrottleFraction)
                : throttle;

            // Exhaust check — need an exhaust pipe adjacent to vent gas.
            HasExhaust = HasAdjacentExhaust();

            // ── Exhaust gas accumulation ────────────────────────────────
            if (IsRunning)
            {
                ExhaustGas = Mathf.Min(exhaustGasCapacity, ExhaustGas + exhaustGasRate * requestedThrottle * dt);
            }
            if (HasExhaust)
            {
                ExhaustGas = Mathf.Max(0f, ExhaustGas - exhaustVentRate * dt);
            }

            // ── Thermal model ──────────────────────────────────────────
            TickThermal(dt, requestedThrottle);

            // ── Engine running conditions ───────────────────────────────
            bool exhaustChoked = ExhaustFill01 >= 0.99f;

            // Coolant: HFO and MGO engines REQUIRE coolant to run.
            bool needsCoolant = tier == EngineTier.Medium || tier == EngineTier.Giant;
            if (needsCoolant)
                RefillCoolant(dt); // allow a dry engine to prime from connected liquid pipes before evaluating run state

            // Latched mechanical failure — only clears once the block cools below 80°C.
            if (CriticalFailure && TemperatureC <= RecoverTemperatureC)
                CriticalFailure = false;

            if (!Enabled || !HasExhaust || exhaustChoked || CriticalFailure || (needsCoolant && !HasCoolant))
            {
                node.FuelAvailable01 = 0f;
                IsRunning = false;
                CurrentUsage = 0f;
                node.SetFlag(MechanicalFlags.Broken);
                return;
            }
            node.ClearFlag(MechanicalFlags.Broken);

            // Consume coolant (if needed).
            if (needsCoolant && IsRunning)
                CoolantBuffer = Mathf.Max(0f, CoolantBuffer - coolantConsumptionRate * requestedThrottle * dt);

            // Consume fuel from the internal buffer.
            // Marine Engine Coolant reduces fuel consumption by 33%.
            float fuelMultiplier = UsingPremiumCoolant ? 0.67f : 1f;
            fuelMultiplier *= ModuleFuelUseMultiplier;
            if (IsOverheating) fuelMultiplier *= 4f / 3f; // knocking: fuel efficiency drops 25%
            float consumption = fuelConsumptionRate * requestedThrottle * fuelMultiplier * dt;
            FuelBuffer = Mathf.Max(0f, FuelBuffer - consumption);
            CurrentUsage = fuelConsumptionRate * requestedThrottle * fuelMultiplier;

            // Refill from grid storage.
            RefillBuffer(dt);

            // Exhaust back-pressure reduces power.
            float exhaustPenalty = 1f;
            if (IsChoked)
            {
                float overChoke = (ExhaustFill01 - exhaustChokeThreshold) / (1f - exhaustChokeThreshold);
                exhaustPenalty = 1f - overChoke * 0.7f; // lose up to 70% power near full
            }

            IsRunning = FuelBuffer > 0.01f && requestedThrottle > 0.01f;
            float effectiveFuel = IsRunning ? FuelFill01 * requestedThrottle * exhaustPenalty : 0f;

            // Count connected turbos and apply stacked boost to the torque.
            CountTurbos();
            node.MaxTorque = maxTorque * TurboBoostTotal * ModuleOutputMultiplier;
            node.MaxRPM = maxRPM * ModuleSpeedCapMultiplier;
            node.OutputMultiplier = 1f;

            node.FuelAvailable01 = effectiveFuel;

            // Stress = how hard we're pushing relative to max.
            CurrentTorque = node.MaxTorque * effectiveFuel;
            Stress01 = Mathf.Clamp01(effectiveFuel * (1f + ExhaustFill01 * 0.3f + Heat01 * 0.15f));
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
        }

        /// <summary>Scan only named turbo attachment slots and compute stacked boost.</summary>
        private void CountTurbos()
        {
            ConnectedTurboCount = 0;
            float boost = 1f;

            if (Grid != null)
            {
                int slotCount = MaxTurboSlots;
                for (int i = 0; i < slotCount; i++)
                {
                    Vector3Int turboPos = GridPos + TransformLocalSlotOffsetToGrid(GetTurboAttachmentLocalOffset(i));
                    if (Grid.GetBlock(turboPos) is GridTurbocharger tc && IsTurboTierCompatible(tier, tc.tier))
                    {
                        ConnectedTurboCount++;
                        boost += tc.tier == TurboTier.Large ? 0.25f : 0.15f;
                    }
                }
            }

            TurboBoostTotal = boost;
        }

        // ══════════════════════════════════════════════════════════════
        //  MODULES — socketed EngineModuleItems drive output/heat/smoke
        // ══════════════════════════════════════════════════════════════
        /// <summary>Re-tally socketed modules and derive all multipliers. Runs every
        /// fixed tick: modules hot-swap instantly without needing a graph rebuild.</summary>
        private void RefreshModuleTotals()
        {
            TurboModuleCount = 0;
            EfficiencyChipCount = 0;
            InjectorModuleCount = 0;
            RadiatorModuleCount = 0;

            float outputBonus = 0f;
            float speedBonus = 0f;
            float fuelModifier = 0f;
            float heatBonus = 0f;
            float dissipationMul = 1f;
            float smokeSpeed = 1f;
            bool any = ModuleSlots != null && ModuleSlots.Size > 0;

            if (any)
            {
                for (int i = 0; i < ModuleSlots.Size; i++)
                {
                    var stack = ModuleSlots.GetSlot(i);
                    if (stack == null || stack.IsEmpty) continue;
                    if (stack.item is not EngineModuleItem module) continue;
                    int n = Mathf.Max(1, stack.count);

                    switch (module.moduleKind)
                    {
                        case EngineModuleKind.HighFlowTurbocharger: TurboModuleCount += n; break;
                        case EngineModuleKind.EfficiencyTuningChip: EfficiencyChipCount += n; break;
                        case EngineModuleKind.OverclockedFuelInjectors: InjectorModuleCount += n; break;
                        case EngineModuleKind.SuperCoolerRadiatorJacket: RadiatorModuleCount += n; break;
                    }

                    outputBonus += module.outputPowerBonus * n;
                    speedBonus += module.speedCapBonus * n;
                    fuelModifier += module.fuelUseModifier * n;
                    heatBonus += module.heatGenerationBonus * n;
                    dissipationMul *= Mathf.Pow(Mathf.Max(1f, module.dissipationMultiplier), n);
                    smokeSpeed *= Mathf.Pow(Mathf.Max(0.1f, module.exhaustSmokeVelocityMul), n);
                }
            }

            ModuleOutputMultiplier = Mathf.Max(0.05f, 1f + outputBonus);
            ModuleSpeedCapMultiplier = Mathf.Max(0.5f, 1f + speedBonus);
            ModuleFuelUseMultiplier = Mathf.Max(0.05f, 1f + fuelModifier);
            _moduleHeatBonus = heatBonus;
            _moduleDissipationMultiplier = dissipationMul;
            SmokeSpeedMultiplier = smokeSpeed;
        }

        private float _moduleHeatBonus;
        private float _moduleDissipationMultiplier = 1f;

        /// <summary>True while an Efficiency Tuning Chip is socketed and therefore a
        /// continuous ACTIVE coolant flow is mandatory during operation.</summary>
        public bool RequiresActiveCoolantFlow => EfficiencyChipCount > 0;

        // ══════════════════════════════════════════════════════════════
        //  THERMAL MODEL — heat builds with load, coolant + radiator sink it
        // ══════════════════════════════════════════════════════════════
        private void TickThermal(float dt, float requestedThrottle)
        {
            // Radiator jackets draw fresh/sea water from grid tanks while running.
            RadiatorWaterFill01 = 0f;
            RadiatorCoolingActive = false;
            if (RadiatorModuleCount > 0 && IsRunning)
            {
                float want = RadiatorWaterDrawPerModule * RadiatorModuleCount * dt;
                float got = want > 0.0001f ? DrawLiquidFuel(LiquidType.Water, want) : 0f;
                RadiatorWaterFill01 = want > 0.0001f ? Mathf.Clamp01(got / want) : 1f;
                RadiatorCoolingActive = RadiatorWaterFill01 > 0.5f;
            }

            bool loadActive = IsRunning && requestedThrottle > 0.01f;

            // Heat generation: throttle load × injector bonus (+50% per injector type module).
            float heatGen = loadActive
                ? baseHeatRate * requestedThrottle * (1f + Mathf.Max(0f, _moduleHeatBonus))
                : 0f;

            // Dissipation: passive base + flowing coolant (premium coolant sinks 25% better),
            // multiplied by ACTIVE radiator jackets (water must be flowing for the bonus).
            float dissipation = baseDissipationRate;
            if (HasCoolant)
                dissipation += coolantDissipationRate * (UsingPremiumCoolant ? 1.25f : 1f);
            if (RadiatorModuleCount > 0 && RadiatorCoolingActive)
                dissipation *= _moduleDissipationMultiplier;

            float net = heatGen - dissipation;

            // Efficiency Tuning Chip: without a continuous ACTIVE coolant flow the engine
            // overheats within ~15 seconds of operation (spec).
            if (loadActive && RequiresActiveCoolantFlow && !HasCoolant)
                net += EfficiencyChipDryHeatRate;

            // No coolant flow at all on a liquid engine slowly bakes the block too.
            TemperatureC = Mathf.Clamp(TemperatureC + net * dt, AmbientTemperatureC, MaxTemperatureC);

            // Critical mechanical failure at 100°C — shaft stops, heavy black smoke.
            if (TemperatureC >= CriticalTemperatureC)
                CriticalFailure = true;
        }

        /// <summary>Water demand (L/s at full throttle) per socketed radiator jacket.</summary>
        public const float RadiatorWaterDrawPerModule = 2f;

        // ══════════════════════════════════════════════════════════════
        //  FUEL MANAGEMENT
        // ══════════════════════════════════════════════════════════════
        private void RefillBuffer(float dt)
        {
            float space = fuelBufferCapacity - FuelBuffer;
            if (space < 0.01f) return;

            if (fuelKind == MaritimeFuelKind.Solid)
            {
                // Only pull a new fuel item when the buffer is getting low
                // (avoids draining a hopper/cargo line one item per frame).
                if (FuelBuffer < fuelBufferCapacity * 0.25f)
                {
                    float burnSec = DrawSolidFuelFromInput();
                    if (burnSec <= 0f) burnSec = DrawSolidFuel();
                    if (burnSec > 0f)
                        FuelBuffer = Mathf.Min(fuelBufferCapacity, FuelBuffer + burnSec);
                }
            }
            else
            {
                float want = Mathf.Min(space, liquidRefillRate * dt);
                float drawn = DrawLiquidFuel(liquidFuel, want);
                FuelBuffer += drawn;
            }
        }

        private float DrawSolidFuelFromInput()
        {
            if (SolidFuelInput == null) return 0f;
            for (int i = 0; i < SolidFuelInput.Size; i++)
            {
                var stack = SolidFuelInput.GetSlot(i);
                if (stack == null || stack.IsEmpty) continue;
                if (stack.item is not ResourceItem resource || resource.fuelSeconds <= 0f) continue;
                int removed = SolidFuelInput.Remove(resource, 1);
                if (removed > 0) return resource.fuelSeconds;
            }
            return 0f;
        }

        /// <summary>Refill coolant from grid tanks. Prefers Marine Engine Coolant, falls back to Water.</summary>
        private void RefillCoolant(float dt)
        {
            float space = coolantCapacity - CoolantBuffer;
            if (space < 0.01f) return;

            // Try Marine Engine Coolant first (premium — gives -33% fuel).
            float want = Mathf.Min(space, coolantRefillRate * dt);
            float drawn = DrawLiquidFuel(LiquidType.MarineEngineCoolant, want);
            if (drawn > 0.01f)
            {
                CoolantBuffer += drawn;
                UsingPremiumCoolant = true;
                return;
            }

            // Fall back to plain water (no bonus, but keeps the engine alive).
            drawn = DrawLiquidFuel(LiquidType.Water, want);
            if (drawn > 0.01f)
            {
                CoolantBuffer += drawn;
                UsingPremiumCoolant = false;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  IGridDataProvider — live data for Grid Screens
        // ══════════════════════════════════════════════════════════════
        public string SourceName => blockName;
        public string DataCategory => "Maritime Engines";
        public string GetDisplayData()
        {
            string status =
                CriticalFailure ? "CRITICAL HEAT — SHAFT STOPPED" :
                IsOverheating ? "KNOCKING — OVERHEATING" :
                IsRunning ? "RUNNING" :
                !HasExhaust ? "NO EXHAUST" : "IDLE";
            string fuel = fuelKind == MaritimeFuelKind.Liquid
                ? $"FUEL {FuelFill01 * 100f:0}% ({FuelBuffer:0} L)"
                : $"FUEL {FuelFill01 * 100f:0}% (≈{FormatDuration(EstimatedFuelSecondsRemaining)})";
            return
                $"ENGINE {status}\n" +
                $"{CurrentRPM:0} RPM · {CurrentTorque:0} N·m\n" +
                $"{fuel}\n" +
                $"HEAT {Heat01 * 100f:0}% ({TemperatureC:0}°C)\n" +
                $"EXHAUST {ExhaustFill01 * 100f:0}%";
        }
    }
}
