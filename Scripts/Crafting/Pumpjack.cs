// Assets/Scripts/VoxelEngine/Crafting/Pumpjack.cs
//
// PUMPJACK — draws liquid crude out of a crude-bearing body under its derrick and
// holds it in a tank on the machine. 11.0.0-dev replaces the old design, which
// filled barrel items into an output slot: crude is a liquid in this game's fluid
// chain, so a pump that produced an item forced the player to hand-carry drums
// instead of plumbing the well into the refinery.
//
// There are no item slots on this machine. It has one liquid tank. A canister is
// filled from that tank, and the machine exposes IFluidStore so the fluid chain
// can draw from it — the well joins every other liquid in the factory.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.Crafting
{
    /// <summary>Liquid crude producer. One tank, no item slots.</summary>
    [RequireComponent(typeof(PowerConsumer))]
    public class Pumpjack : MonoBehaviour, IMachineProcessState, IFluidStore
    {
        [Header("Production")]
        [Tooltip("Seconds of pumping to draw one batch out of the ground.")][Min(0.5f)] public float secondsPerCycle = 14f;
        [Tooltip("Litres drawn into the tank per completed batch.")][Min(1f)] public float litresPerCycle = 1000f;

        [Header("Power")]
        [Tooltip("Power drawn while actively pumping (W).")][Min(1f)] public float baseWattsPerSecond = 4000f;
        [Tooltip("Power drawn while idle (W).")][Min(0f)] public float idleWattsPerSecond = 120f;

        [Header("Well Detection")]
        [Tooltip("How far under the derrick the well is probed (m).")][Min(1)] public int scanDepth = 120;
        [Tooltip("Probe columns are laid out over this radius around the derrick.")][Min(0)] public int scanRadius = 3;

        [Header("Storage")]
        /// <summary>The well's tank — the only storage this machine has.</summary>
        public MachineFluidTank crudeTank = new MachineFluidTank("Crude Tank", 5000f, LiquidType.CrudeOil, autoType: false);

        // ---- State ----
        public float CycleProgress01 => secondsPerCycle <= 0f ? 0f : Mathf.Clamp01(_elapsed / secondsPerCycle);
        public float StoredLitres => crudeTank != null ? crudeTank.stored : 0f;
        public float TankCapacity => crudeTank != null ? crudeTank.capacity : 0f;
        public float Fill01 => crudeTank != null ? crudeTank.Fill01 : 0f;
        public bool IsPumping => _isPumping;
        public bool IsOnline => _consumer != null && _consumer.IsPowered;
        public bool HasReservoir => _reservoirFound;

        public float CurrentWattage =>
            _consumer != null ? _consumer.wattsPerSecond
            : (IsPumping ? baseWattsPerSecond : idleWattsPerSecond);

        private PowerConsumer _consumer;
        private float _elapsed;
        private bool _isPumping;
        private bool _reservoirFound;
        private float _rescanTimer;

        private void Awake()
        {
            EnsureContainers();
            _consumer = GetComponent<PowerConsumer>();
        }

        /// <summary>Keeps the tank present and locked to crude. Never touches what is stored.</summary>
        public void EnsureContainers()
        {
            crudeTank ??= new MachineFluidTank("Crude Tank", 5000f, LiquidType.CrudeOil, autoType: false);
            // Fixed-type: the tank is a crude well, so it must not adopt whatever a
            // pipe happens to push at it first.
            crudeTank.autoType = false;
            crudeTank.liquid = LiquidType.CrudeOil;
            if (crudeTank.capacity <= 0f) crudeTank.capacity = 5000f;
            crudeTank.stored = Mathf.Clamp(crudeTank.stored, 0f, crudeTank.capacity);
        }

        // ============================================================
        //        Fluid store — what the fluid chain draws from
        // ============================================================

        public IReadOnlyList<MachineFluidTank> FluidTanks => new[] { crudeTank };

        public float Available(LiquidType type) =>
            crudeTank != null && crudeTank.liquid == type ? crudeTank.stored : 0f;

        public float SpaceFor(LiquidType type) =>
            // A well is a source, not a sink: nothing can be pumped into it.
            0f;

        public float Draw(LiquidType type, float litres)
        {
            if (crudeTank == null || crudeTank.liquid != type) return 0f;
            return crudeTank.Remove(litres);
        }

        public float Fill(LiquidType type, float litres) => 0f;

        // ============================================================
        //                          Loop
        // ============================================================
        private void Update()
        {
            if (_consumer != null)
            {
                _consumer.wattsPerSecond = IsPumping ? baseWattsPerSecond : idleWattsPerSecond;
            }

            _rescanTimer -= Time.deltaTime;
            if (_rescanTimer <= 0f)
            {
                _rescanTimer = 1.0f;
                _reservoirFound = DetectReservoir();
            }

            _isPumping = _reservoirFound && IsOnline &&
                         crudeTank != null && crudeTank.stored < crudeTank.capacity - 0.001f;

            if (!_isPumping) return;

            _elapsed += Time.deltaTime;
            if (_elapsed >= secondsPerCycle)
            {
                _elapsed = 0f;
                // Whatever the tank could not take stays in the ground rather than
                // vanishing; the next batch picks it up once space frees.
                crudeTank.Add(LiquidType.CrudeOil, litresPerCycle);
            }
        }

        /// <summary>
        /// Fill a held canister from the tank. Returns false when the canister is not
        /// a liquid canister, is carrying another liquid, is already full, or the well
        /// is dry.
        /// </summary>
        public bool TryFillCanister(ItemStack canStack)
        {
            EnsureContainers();
            if (canStack == null || canStack.IsEmpty) return false;
            if (!(canStack.item is LiquidCanister)) return false;

            var carried = LiquidCanister.CarriedLiquid(canStack);
            if (carried.HasValue && carried.Value != LiquidType.CrudeOil) return false;

            float freeMl = LiquidCanister.CapacityMl - (canStack.durability > 0f ? canStack.durability : 0f);
            float availableMl = crudeTank.stored * 1000f;
            float takeMl = Mathf.Floor(Mathf.Min(freeMl, availableMl));
            if (takeMl <= 0f) return false;

            // Add first, draw only if the canister really took it, so the tank can
            // never be drained into a canister that refused the liquid.
            if (!LiquidCanister.AddMl(canStack, LiquidType.CrudeOil, Mathf.RoundToInt(takeMl))) return false;
            crudeTank.Remove(takeMl / 1000f);
            return true;
        }

        /// <summary>
        /// Probe under the derrick for a crude-bearing body. A body whose generated ore
        /// layers include crude is treated as having a well under it, so the pumpjack
        /// does not need a hand-placed ore node at the build site.
        ///
        /// The probe ignores anything that is not the body itself — the derrick's own
        /// collider, blocks the pump stands on, machines. The first version stopped at
        /// the first collider it found, so a pump on a foundation (or its own derrick)
        /// could never see the terrain and read "NO CRUDE BELOW" on oil worlds.
        /// </summary>
        private bool DetectReservoir()
        {
            Vector3 origin = transform.position + transform.up * 0.5f;
            Vector3 down = (transform.up != Vector3.zero ? -transform.up : Vector3.down).normalized;
            const int maxHops = 8;

            int half = Mathf.Max(0, scanRadius);
            for (int ox = -half; ox <= half; ox++)
            for (int oz = -half; oz <= half; oz++)
            {
                Vector3 pos = origin + transform.right * ox + transform.forward * oz;
                float remaining = scanDepth;

                for (int hop = 0; hop < maxHops && remaining > 0.01f; hop++)
                {
                    if (!Physics.Raycast(pos, down, out var hit, remaining)) break;   // no ground in range
                    if (hit.collider == null) break;

                    var body = hit.collider.GetComponentInParent<VoxelEngine.Cosmos.CelestialBody>();
                    if (body != null)
                    {
                        if (BodyHasCrude(body)) return true;
                        break;   // the world under here does not carry crude
                    }

                    // The derrick's own collider, or a block/machine the pump stands on —
                    // dig past it. A ray starting inside the derrick can report a zero
                    // distance, so the step is floored to keep the probe converging.
                    float step = Mathf.Max(hit.distance, 0.02f);
                    if (step >= remaining) break;
                    pos += down * step;
                    remaining -= step;
                }
            }
            return false;
        }

        private static bool BodyHasCrude(VoxelEngine.Cosmos.CelestialBody body)
        {
            var layers = body.BuildOreLayers();
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].material == VoxelEngine.Materials.MaterialId.CrudeOil) return true;
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 origin = transform.position + transform.up * 0.5f;
            Vector3 down = (transform.up != Vector3.zero ? -transform.up : Vector3.down).normalized;
            Gizmos.color = Color.cyan;
            int half = Mathf.Max(0, scanRadius);
            for (int ox = -half; ox <= half; ox++)
            for (int oz = -half; oz <= half; oz++)
            {
                Vector3 start = origin + transform.right * ox + transform.forward * oz;
                Gizmos.DrawLine(start, start + down * scanDepth);
            }
        }

        // ============================================================
        //          Batch progress that survives a save/reload
        // ============================================================

        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureContainers();

            // The part-batch is only worth saving while the jack is actually pumping.
            // A stalled well has no batch in progress, and writing a progress value for
            // one would hand it a head start it never earned on reload.
            state.progressSeconds = IsPumping ? Mathf.Max(0f, _elapsed) : 0f;
            MachineProcessPersistence.CaptureTanks(state, FluidTanks);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null || state.IsEmpty) return;
            EnsureContainers();

            // RestoreTanks writes the saved liquid type as well as the litres, so the
            // tank is put back on crude afterwards — a well is a well. The litres stay
            // exactly as saved, clamped into the capacity the prefab author gave it, so
            // a retuned tank size can never invent crude on load.
            MachineProcessPersistence.RestoreTanks(state, FluidTanks);
            EnsureContainers();

            _elapsed = IsPumping
                ? MachineProcessPersistence.ClampOr(state.progressSeconds, 0f, secondsPerCycle, 0f)
                : 0f;
        }

    }
}
