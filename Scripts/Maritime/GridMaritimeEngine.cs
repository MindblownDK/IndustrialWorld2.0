// Assets/Scripts/VoxelEngine/Maritime/GridMaritimeEngine.cs
//
// Maritime engine block. Three tiers share one class:
//
//   Small  (1×1×1 visual starter block) — burns Wood/Coal items
//   Medium (4×3×2 visual ship engine)   — burns Heavy Fuel Oil
//   Giant  (6×5×3 visual ship engine)   — burns Marine Gas Oil
//
// Fuel is drawn from grid storage (cargo for solids, liquid tanks for liquids)
// into an internal buffer. FuelAvailable01 = buffer fill × throttle.
//
// REQUIRES an adjacent Exhaust Pipe — without one the engine chokes and
// produces zero torque. Turbochargers only boost when mounted on named engine attachment points.

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

    public class GridMaritimeEngine : MaritimeBlockBase
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

        /// <summary>Current litres/s fuel consumption (for UI).</summary>
        public float CurrentUsage { get; private set; }

        /// <summary>Current torque output (for UI).</summary>
        public float CurrentTorque { get; private set; }

        /// <summary>0..1 stress level (torque vs max, with exhaust penalty).</summary>
        public float Stress01 { get; private set; }

        /// <summary>True when the engine is overstressed (torque demand exceeds safe limits).</summary>
        public bool IsOverstressed => Stress01 > 0.95f;

        /// <summary>Number of turbochargers connected to this engine (for UI).</summary>
        public int ConnectedTurboCount { get; private set; }
        /// <summary>Total turbo boost multiplier (1.0 = none, 1.4 = one small, etc.).</summary>
        public float TurboBoostTotal { get; private set; }
        /// <summary>Max turbo slots this engine supports.</summary>
        public int MaxTurboSlots => tier switch
        {
            EngineTier.Small  => 1,
            EngineTier.Medium => 2,
            EngineTier.Giant  => 4,
            _ => 0,
        };

        public override float ContentMass
        {
            get
            {
                float m = 0f;
                if (fuelKind == MaritimeFuelKind.Liquid)
                    m += FuelBuffer * liquidFuel.DensityKgPerL();
                else if (SolidFuelInput != null)
                    m += MassUtil.ContainerMass(SolidFuelInput);
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
                    break;
            }
            FuelBuffer = Mathf.Min(FuelBuffer, fuelBufferCapacity);
            EnsureSolidFuelInput();
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

        private Vector3 GetTurboAttachmentMarkerPosition(int slotIndex, float cellSize)
        {
            return tier switch
            {
                EngineTier.Small => new Vector3(cellSize * 0.40f, cellSize * 0.16f, -cellSize * 0.10f),
                EngineTier.Medium => slotIndex == 0
                    ? new Vector3(cellSize * 0.88f, cellSize * 1.18f, -cellSize * 0.40f)
                    : new Vector3(-cellSize * 0.88f, cellSize * 1.18f, -cellSize * 0.40f),
                EngineTier.Giant => slotIndex switch
                {
                    0 => new Vector3(cellSize * 1.38f, cellSize * 1.98f, -cellSize * 0.54f),
                    1 => new Vector3(-cellSize * 1.38f, cellSize * 1.98f, -cellSize * 0.54f),
                    2 => new Vector3(0f, cellSize * 3.30f, 0f),
                    _ => new Vector3(0f, cellSize * 1.58f, -cellSize * 1.82f),
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

            if (tier == EngineTier.Giant)
                node.SetFlag(MechanicalFlags.GiantDiesel);
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            float dt = Time.fixedDeltaTime;

            // Exhaust check — need an exhaust pipe adjacent to vent gas.
            HasExhaust = HasAdjacentExhaust();

            // ── Exhaust gas accumulation ────────────────────────────────
            // Gas builds up while running; vents through an adjacent exhaust pipe.
            if (IsRunning)
            {
                ExhaustGas = Mathf.Min(exhaustGasCapacity, ExhaustGas + exhaustGasRate * throttle * dt);
            }
            if (HasExhaust)
            {
                ExhaustGas = Mathf.Max(0f, ExhaustGas - exhaustVentRate * dt);
            }

            // ── Engine running conditions ───────────────────────────────
            bool exhaustChoked = ExhaustFill01 >= 0.99f;

            // Coolant: HFO and MGO engines REQUIRE coolant to run.
            bool needsCoolant = tier == EngineTier.Medium || tier == EngineTier.Giant;
            if (needsCoolant)
                RefillCoolant(dt); // allow a dry engine to prime from connected liquid pipes before evaluating run state

            if (!Enabled || !HasExhaust || exhaustChoked || (needsCoolant && !HasCoolant))
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
                CoolantBuffer = Mathf.Max(0f, CoolantBuffer - coolantConsumptionRate * throttle * dt);

            // Consume fuel from the internal buffer.
            // Marine Engine Coolant reduces fuel consumption by 33%.
            float fuelMultiplier = UsingPremiumCoolant ? 0.67f : 1f;
            float consumption = fuelConsumptionRate * throttle * fuelMultiplier * dt;
            FuelBuffer = Mathf.Max(0f, FuelBuffer - consumption);
            CurrentUsage = fuelConsumptionRate * throttle * fuelMultiplier;

            // Refill from grid storage.
            RefillBuffer(dt);

            // Exhaust back-pressure reduces power.
            float exhaustPenalty = 1f;
            if (IsChoked)
            {
                float overChoke = (ExhaustFill01 - exhaustChokeThreshold) / (1f - exhaustChokeThreshold);
                exhaustPenalty = 1f - overChoke * 0.7f; // lose up to 70% power near full
            }

            IsRunning = FuelBuffer > 0.01f && throttle > 0.01f;
            float effectiveFuel = IsRunning ? FuelFill01 * throttle * exhaustPenalty : 0f;

            // Count connected turbos and apply stacked boost to the torque.
            CountTurbos();
            node.MaxTorque = maxTorque * TurboBoostTotal;

            node.FuelAvailable01 = effectiveFuel;

            // Stress = how hard we're pushing relative to max.
            CurrentTorque = node.MaxTorque * effectiveFuel;
            Stress01 = Mathf.Clamp01(effectiveFuel * (1f + ExhaustFill01 * 0.3f));
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
    }
}
