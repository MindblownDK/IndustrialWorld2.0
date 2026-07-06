// Assets/Scripts/VoxelEngine/Power/Wind/WindTurbineController.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  WIND TURBINE CONTROLLER — the brain of a modular wind turbine. ║
// ║                                                                  ║
// ║  Lives on the FIRST placed part of a turbine:                   ║
// ║    • Horizontal (T-series): the Tower                           ║
// ║    • Vertical: the Rotor base                                   ║
// ║                                                                  ║
// ║  Other parts (Nacelle, Gearbox, Generator, Hub, Blades) snap    ║
// ║  into exact sockets when placed. The turbine only produces      ║
// ║  power when every socket is filled. Output scales with live     ║
// ║  wind speed, hub height and the condition of the most-stressed  ║
// ║  part (slow degradation under load, repairable).                ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Power.Wind
{
    [RequireComponent(typeof(PowerGenerator))]
    public class WindTurbineController : MonoBehaviour, VoxelEngine.Building.ICustomBlockDrop
    {
        // ── Static registry ───────────────────────────────────────────────
        private static readonly List<WindTurbineController> _all = new();
        public static IReadOnlyList<WindTurbineController> All => _all;

        // ── Identity & rating ─────────────────────────────────────────────
        [Header("Identity")]
        public string tierId = "t90";
        public string displayName = "T90 - 2 MW";
        [Tooltip("True for vertical-axis turbines (Rotor + Blades).")]
        public bool vertical = false;

        [Header("Rating")]
        [Tooltip("Nameplate output in Watts at rated wind speed.")]
        public float ratedPowerWatts = 2_000_000f;
        [Tooltip("Rotor diameter in metres — drives blade sweep + RPM.")]
        public float rotorDiameter = 46f;
        [Tooltip("Hub height above the base in metres.")]
        public float hubHeight = 40f;
        public int bladeCount = 3;

        [Header("Sockets (local space)")]
        public Vector3 yawPivotLocal;                    // top of tower (HAWT) / top of rotor drum (VAWT)
        public Vector3 nacelleSocket;                    // local to yaw pivot
        public Vector3 gearboxSocket;                    // local to yaw pivot (inside nacelle)
        public Vector3 generatorSocket;                  // local to yaw pivot (inside nacelle)
        public Vector3 hubSocket;                        // local to yaw pivot (rotor centre)
        [Tooltip("Distance from spin centre to a blade root.")]
        public float bladeMountRadius = 1.2f;
        public Vector3 verticalBladeSocket;              // local to spin root (VAWT)

        [Header("Repair")]
        [Tooltip("Steel plates consumed by a full repair.")]
        public int repairPlateCost = 4;

        // ── Runtime state ─────────────────────────────────────────────────
        public WindTurbinePart Nacelle    { get; private set; }
        public WindTurbinePart Gearbox    { get; private set; }
        public WindTurbinePart Generator  { get; private set; }
        public WindTurbinePart Hub        { get; private set; }
        public WindTurbinePart RootPart   { get; private set; }   // Tower / VerticalRotor
        private WindTurbinePart[] _blades;

        public float CurrentOutputWatts { get; private set; }
        public float CurrentEfficiency  { get; private set; }     // 0..1+ vs nameplate
        public float CurrentRpm         { get; private set; }

        private PowerGenerator _generator;
        private Transform _yaw;      // yaws into the wind (HAWT); static for VAWT
        private Transform _spin;     // spins with the wind (hub + blades / VAWT cage)
        private float _spinAngle;

        // Degradation: percent lost per REAL-TIME hour at 100% load, before the
        // per-part stress weight. Deliberately slow — days of runtime per repair.
        private const float DEGRADE_PER_HOUR_FULL_LOAD = 0.9f;

        // ────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ────────────────────────────────────────────────────────────────
        private void Awake()
        {
            _generator = GetComponent<PowerGenerator>();
            _generator.wattsPerSecond = 0f;
            _generator.connectRadius = 7f;
            _generator.requireGridAlignedNeighbours = false;

            _blades = new WindTurbinePart[Mathf.Max(1, bladeCount)];

            // Build the yaw / spin pivot chain. Prefab visuals for the root part
            // live directly under this transform; attached parts get parented to
            // these pivots so the whole head yaws + the rotor spins as one.
            _yaw = new GameObject("YawPivot").transform;
            _yaw.SetParent(transform, false);
            _yaw.localPosition = yawPivotLocal;

            _spin = new GameObject("SpinPivot").transform;
            if (vertical)
            {
                _spin.SetParent(transform, false);
                _spin.localPosition = yawPivotLocal;      // VAWT: spin cage sits on the drum
            }
            else
            {
                _spin.SetParent(_yaw, false);
                _spin.localPosition = hubSocket;
            }

            RootPart = GetComponent<WindTurbinePart>();
            if (RootPart != null)
            {
                RootPart.Controller = this;
                RootPart.kind = vertical ? WindTurbinePartKind.VerticalRotor : WindTurbinePartKind.Tower;
                RootPart.tierId = tierId;
            }

            _all.Add(this);
        }

        private void Start()
        {
            // Build ghosts are prefab clones without a PlacedBlock — a ghost tower
            // must never register as a live turbine or accept parts.
            _live = GetComponent<VoxelEngine.Building.PlacedBlock>() != null;
            if (!_live)
            {
                _all.Remove(this);
                // Never let a preview register into the power network.
                if (_generator != null) _generator.enabled = false;
                enabled = false;
                return;
            }

            var placed = GetComponent<VoxelEngine.Building.PlacedBlock>();
            placed.onGrid = false;
            WindSystem.EnsureInstance();
        }

        private bool _live;

        private void OnDestroy() => _all.Remove(this);

        /// <summary>
        /// Called by PlacedBlock.Damage when the TOWER / ROTOR base is mined.
        /// The whole structure comes down — refund every attached part as a
        /// dropped item (the hierarchy is still intact at this point), then
        /// hand back the base block itself.
        /// </summary>
        public ItemStack CreateBlockDrop(BlockItem originalItem)
        {
            foreach (var part in EnumerateAttached())
            {
                if (part == null || part == RootPart) continue;
                var pb = part.GetComponent<VoxelEngine.Building.PlacedBlock>();
                if (pb != null && pb.Item != null)
                    DroppedItem.Spawn(new ItemStack(pb.Item, 1),
                        part.transform.position + Vector3.up * 0.5f, Vector3.up);
            }
            return new ItemStack(originalItem, 1);
        }

        // ────────────────────────────────────────────────────────────────
        //  Attachment
        // ────────────────────────────────────────────────────────────────

        /// <summary>Finds the closest controller that can accept this part.</summary>
        public static WindTurbineController FindBestFor(WindTurbinePart part, Vector3 worldPos)
        {
            WindTurbineController best = null;
            float bestDist = float.MaxValue;
            foreach (var c in _all)
            {
                if (c == null || !c.Accepts(part, worldPos, out float d)) continue;
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        /// <summary>
        /// True if this controller needs the part's kind and the part sits close to
        /// its intended socket. Snap placement spawns parts exactly at sockets;
        /// save-restore can drift up to ~2× the nacelle length when the tower
        /// reloads with a different yaw, so the accept radius scales with size.
        /// </summary>
        public bool Accepts(WindTurbinePart part, Vector3 worldPos, out float distance)
        {
            distance = float.MaxValue;
            if (!_live || part == null || part.tierId != tierId) return false;
            if (!NeedsKind(part.kind)) return false;
            if (!TryGetSocketWorld(part.kind, out Vector3 socket, out _)) return false;

            float acceptRadius = Mathf.Max(12f, rotorDiameter * 0.18f);
            distance = Vector3.Distance(worldPos, socket);
            return distance <= acceptRadius;
        }

        public bool NeedsKind(WindTurbinePartKind kind)
        {
            switch (kind)
            {
                // Build order: Tower → Nacelle → (Gearbox + Generator) → Hub → Blades ×3.
                case WindTurbinePartKind.Nacelle:       return !vertical && Nacelle == null;
                case WindTurbinePartKind.Gearbox:       return !vertical && Nacelle != null && Gearbox == null;
                case WindTurbinePartKind.Generator:     return !vertical && Nacelle != null && Generator == null;
                case WindTurbinePartKind.Hub:           return !vertical && Nacelle != null && Hub == null;
                case WindTurbinePartKind.Blade:         return !vertical && Hub != null && FreeBladeSlot() >= 0;
                case WindTurbinePartKind.VerticalBlade: return vertical && _blades[0] == null;
                default: return false;
            }
        }

        /// <summary>World-space socket pose for a part kind (next free slot for blades).</summary>
        public bool TryGetSocketWorld(WindTurbinePartKind kind, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            switch (kind)
            {
                case WindTurbinePartKind.Nacelle:
                    pos = _yaw.TransformPoint(nacelleSocket);   rot = _yaw.rotation;  return true;
                case WindTurbinePartKind.Gearbox:
                    pos = _yaw.TransformPoint(gearboxSocket);   rot = _yaw.rotation;  return true;
                case WindTurbinePartKind.Generator:
                    pos = _yaw.TransformPoint(generatorSocket); rot = _yaw.rotation;  return true;
                case WindTurbinePartKind.Hub:
                    pos = _yaw.TransformPoint(hubSocket);       rot = _yaw.rotation;  return true;
                case WindTurbinePartKind.Blade:
                {
                    int slot = FreeBladeSlot();
                    if (slot < 0) return false;
                    Quaternion local = Quaternion.Euler(0f, 0f, slot * (360f / Mathf.Max(1, bladeCount)));
                    pos = _spin.TransformPoint(local * new Vector3(0f, bladeMountRadius, 0f));
                    rot = _spin.rotation * local;
                    return true;
                }
                case WindTurbinePartKind.VerticalBlade:
                    pos = _spin.TransformPoint(verticalBladeSocket); rot = _spin.rotation; return true;
                default: return false;
            }
        }

        /// <summary>Parents the part to its pivot and hard-snaps it into the socket.</summary>
        public void Attach(WindTurbinePart part)
        {
            if (part == null || !NeedsKind(part.kind)) return;

            switch (part.kind)
            {
                case WindTurbinePartKind.Nacelle:
                    Nacelle = part; Snap(part, _yaw, nacelleSocket, Quaternion.identity);
                    _roofLid = part.transform.Find("RoofLid");
                    break;
                case WindTurbinePartKind.Gearbox:
                    Gearbox = part; Snap(part, _yaw, gearboxSocket, Quaternion.identity); break;
                case WindTurbinePartKind.Generator:
                    Generator = part; Snap(part, _yaw, generatorSocket, Quaternion.identity); break;
                case WindTurbinePartKind.Hub:
                    Hub = part; Snap(part, _spin, Vector3.zero, Quaternion.identity); break;
                case WindTurbinePartKind.Blade:
                {
                    int slot = FreeBladeSlot();
                    if (slot < 0) return;
                    _blades[slot] = part;
                    part.SlotIndex = slot;
                    Quaternion local = Quaternion.Euler(0f, 0f, slot * (360f / Mathf.Max(1, bladeCount)));
                    Snap(part, _spin, local * new Vector3(0f, bladeMountRadius, 0f), local);
                    break;
                }
                case WindTurbinePartKind.VerticalBlade:
                    _blades[0] = part; part.SlotIndex = 0;
                    Snap(part, _spin, verticalBladeSocket, Quaternion.identity); break;
                default: return;
            }
            part.Controller = this;
            part.ApplyWeathering();   // restored-from-save parts show their age immediately
        }

        public void Detach(WindTurbinePart part)
        {
            if (part == null) return;
            if (Nacelle   == part) Nacelle   = null;
            if (Gearbox   == part) Gearbox   = null;
            if (Generator == part) Generator = null;
            if (Hub       == part) Hub       = null;
            for (int i = 0; i < _blades.Length; i++)
                if (_blades[i] == part) _blades[i] = null;
            part.Controller = null;
        }

        private static void Snap(WindTurbinePart part, Transform parent, Vector3 localPos, Quaternion localRot)
        {
            part.transform.SetParent(parent, true);
            part.transform.localPosition = localPos;
            part.transform.localRotation = localRot;
        }

        private int FreeBladeSlot()
        {
            for (int i = 0; i < _blades.Length; i++)
                if (_blades[i] == null) return i;
            return -1;
        }

        // ────────────────────────────────────────────────────────────────
        //  Assembly status
        // ────────────────────────────────────────────────────────────────
        public bool IsComplete
        {
            get
            {
                if (vertical) return _blades[0] != null;
                if (Nacelle == null || Gearbox == null || Generator == null || Hub == null) return false;
                for (int i = 0; i < _blades.Length; i++)
                    if (_blades[i] == null) return false;
                return true;
            }
        }

        public int BladesInstalled
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _blades.Length; i++)
                    if (_blades[i] != null) n++;
                return n;
            }
        }

        public IEnumerable<WindTurbinePart> EnumerateAttached()
        {
            if (RootPart   != null) yield return RootPart;
            if (Nacelle    != null) yield return Nacelle;
            if (Gearbox    != null) yield return Gearbox;
            if (Generator  != null) yield return Generator;
            if (Hub        != null) yield return Hub;
            for (int i = 0; i < _blades.Length; i++)
                if (_blades[i] != null) yield return _blades[i];
        }

        /// <summary>Worst condition across attached parts (100 when pristine).</summary>
        public float WorstCondition
        {
            get
            {
                float worst = 100f;
                foreach (var p in EnumerateAttached())
                    if (p.condition < worst) worst = p.condition;
                return worst;
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  Simulation
        // ────────────────────────────────────────────────────────────────
        private void Update()
        {
            UpdateRoofLid();

            var wind = WindSystem.Instance;
            if (wind == null || !IsComplete)
            {
                CurrentOutputWatts = 0f;
                CurrentEfficiency  = 0f;
                CurrentRpm = Mathf.MoveTowards(CurrentRpm, 0f, Time.deltaTime * 2f);
                ApplySpin();
                if (_generator != null) _generator.wattsPerSecond = 0f;
                return;
            }

            float windSpeed = wind.GetWindSpeed();
            bool obstructed = false; // cheap: only re-check occasionally
            if (Time.frameCount % 90 == 0)
                _obstructedCache = wind.IsObstructed(transform.position + Vector3.up * (hubHeight * 0.9f), rotorDiameter);
            obstructed = _obstructedCache;

            float hubWorldHeight = transform.position.y + hubHeight;
            float eff = wind.GetWindEfficiencyMultiplier(hubWorldHeight, obstructed, 220f);
            if (vertical) eff *= 0.82f;   // VAWTs trade efficiency for footprint

            // Condition of the most stressed part throttles output. Degradation is
            // integrated at 1 Hz — per-frame precision buys nothing at these rates.
            _degradeAccum += Time.deltaTime;
            if (_degradeAccum >= 1f)
            {
                DegradeParts(_degradeAccum);
                _worstConditionCache = WorstCondition;
                _degradeAccum = 0f;
            }
            float conditionMult = Mathf.Lerp(0.25f, 1f, _worstConditionCache / 100f);

            CurrentEfficiency  = Mathf.Clamp(eff * conditionMult, 0f, 1.35f);
            CurrentOutputWatts = ratedPowerWatts * CurrentEfficiency;
            if (_generator != null)
            {
                _generator.isOn = true;
                _generator.wattsPerSecond = CurrentOutputWatts;
            }

            // Rotor RPM — big rotors spin slow, verticals spin fast.
            float tipSpeed = Mathf.Clamp(windSpeed * (vertical ? 2.4f : 5.2f), 4f, 90f);
            float targetRpm = (tipSpeed / (Mathf.PI * Mathf.Max(4f, rotorDiameter))) * 60f;
            if (vertical) targetRpm *= 2.2f;
            CurrentRpm = Mathf.Lerp(CurrentRpm, targetRpm, Time.deltaTime * 0.8f);
            ApplySpin();

            // Yaw the head into the wind (HAWT only) — slow, heavy, realistic.
            if (!vertical && _yaw != null)
            {
                Vector3 dir = wind.GetWindDirection(); dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetYaw = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    _yaw.rotation = Quaternion.RotateTowards(_yaw.rotation, targetYaw, Time.deltaTime * 2.5f);
                }
            }
        }

        private bool  _obstructedCache;
        private float _degradeAccum;
        private float _worstConditionCache = 100f;

        // ────────────────────────────────────────────────────────────────
        //  Nacelle roof lid — swings open when the player walks up holding
        //  a Gearbox or Generator for THIS turbine, so they can watch the
        //  part snap into the machinery bay. Eases shut again afterwards.
        // ────────────────────────────────────────────────────────────────
        private Transform _roofLid;
        private float _lidAngle;            // current hinge angle (0 = closed)
        private const float LID_OPEN_ANGLE = 115f;

        /// <summary>True while the roof should be open: an internals socket is
        /// still empty AND the local player is nearby holding a matching part.</summary>
        private bool WantsRoofOpen()
        {
            if (vertical || Nacelle == null) return false;
            if (Gearbox != null && Generator != null) return false;

            var ui = VoxelEngine.UI.GameUIController.Instance;
            var inv = ui != null ? ui.inventory : null;
            if (inv == null) return false;

            var stack = inv.ActiveStack;
            if (stack.IsEmpty || !(stack.item is BlockItem bi) || bi.placedPrefab == null) return false;
            var proto = bi.placedPrefab.GetComponent<WindTurbinePart>();
            if (proto == null || proto.tierId != tierId) return false;
            if (proto.kind != WindTurbinePartKind.Gearbox && proto.kind != WindTurbinePartKind.Generator) return false;
            if (!NeedsKind(proto.kind)) return false;

            // Within working range of the nacelle (build reach + a little slack).
            float range = Mathf.Max(14f, hubHeight * 0.35f);
            return Vector3.Distance(inv.transform.position, _yaw.position) <= range;
        }

        private void UpdateRoofLid()
        {
            if (_roofLid == null) return;
            float target = WantsRoofOpen() ? LID_OPEN_ANGLE : 0f;
            // Heavy hydraulic ease — fast to start, settles softly.
            _lidAngle = Mathf.Lerp(_lidAngle, target, 1f - Mathf.Exp(-Time.deltaTime * 3.5f));
            if (Mathf.Abs(_lidAngle - target) < 0.01f) _lidAngle = target;
            // Hinge sits on the left edge → negative Z-roll swings the lid up+out.
            _roofLid.localRotation = Quaternion.Euler(0f, 0f, _lidAngle);
        }

        private void ApplySpin()
        {
            if (_spin == null) return;
            _spinAngle = (_spinAngle + CurrentRpm * 6f * Time.deltaTime) % 360f;
            _spin.localRotation = vertical
                ? Quaternion.Euler(0f, _spinAngle, 0f)     // VAWT: spin about vertical axis
                : Quaternion.Euler(0f, 0f, _spinAngle);    // HAWT: spin about rotor axis
        }

        private void DegradeParts(float dt)
        {
            float load = ratedPowerWatts > 0f ? Mathf.Clamp01(CurrentOutputWatts / ratedPowerWatts) : 0f;
            if (load <= 0.01f) return;
            float basePerSecond = (DEGRADE_PER_HOUR_FULL_LOAD / 3600f) * load * dt;

            foreach (var p in EnumerateAttached())
            {
                float weight = StressWeight(p.kind);
                p.condition = Mathf.Max(0f, p.condition - basePerSecond * weight);
                p.ApplyWeathering();   // rust / soot creeps in as condition drops
            }
        }

        /// <summary>Relative wear rate — drivetrain suffers the most.</summary>
        public static float StressWeight(WindTurbinePartKind kind) => kind switch
        {
            WindTurbinePartKind.Gearbox       => 1.00f,
            WindTurbinePartKind.Blade         => 0.85f,
            WindTurbinePartKind.VerticalBlade => 0.90f,
            WindTurbinePartKind.Hub           => 0.70f,
            WindTurbinePartKind.Generator     => 0.60f,
            WindTurbinePartKind.VerticalRotor => 0.55f,
            WindTurbinePartKind.Nacelle       => 0.30f,
            _                                 => 0.20f,   // Tower
        };

        // ────────────────────────────────────────────────────────────────
        //  Repair
        // ────────────────────────────────────────────────────────────────

        /// <summary>True if any attached part is below factory condition.</summary>
        public bool NeedsRepair => WorstCondition < 99.5f;

        /// <summary>
        /// Consumes steel plates from the inventory and restores every attached
        /// part to 100. Returns false when the player can't afford the repair.
        /// </summary>
        public bool TryRepairAll(Inventory inventory)
        {
            if (!NeedsRepair || inventory == null || inventory.container == null) return false;
            var plates = FindSteelPlates();
            if (plates == null) return false;
            if (inventory.container.CountOf(plates) < repairPlateCost) return false;

            inventory.container.Remove(plates, repairPlateCost);
            foreach (var p in EnumerateAttached())
            {
                p.condition = 100f;
                p.ApplyWeathering();   // full service restores the factory finish
            }
            return true;
        }

        private static ItemDefinition _steelPlatesCache;

        public ItemDefinition FindSteelPlates()
        {
            if (_steelPlatesCache != null) return _steelPlatesCache;
            foreach (var it in Resources.LoadAll<ItemDefinition>(""))
                if (it != null && it.itemId == "steel_plate") { _steelPlatesCache = it; return it; }
            // Fallback for assets outside Resources/ — any loaded instance qualifies.
            foreach (var it in Resources.FindObjectsOfTypeAll<ItemDefinition>())
                if (it != null && it.itemId == "steel_plate") { _steelPlatesCache = it; return it; }
            return null;
        }

        // ────────────────────────────────────────────────────────────────
        //  Build-time snapping (called by BuildSystem for ghost + placement)
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// If the player is aiming at this turbine while holding a matching part,
        /// returns the exact world socket POSE the part should snap to — position
        /// AND rotation, so blades arrive pre-rotated into their 120° slot and can
        /// never be placed in a wrong orientation.
        /// </summary>
        public static bool TryGetSnapPoint(GameObject heldPrefab, RaycastHit hit, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (heldPrefab == null || hit.collider == null) return false;
            var partProto = heldPrefab.GetComponent<WindTurbinePart>();
            if (partProto == null || heldPrefab.GetComponent<WindTurbineController>() != null) return false;

            var controller = hit.collider.GetComponentInParent<WindTurbineController>();
            if (controller == null || controller.tierId != partProto.tierId) return false;
            if (!controller.NeedsKind(partProto.kind)) return false;
            if (!controller.TryGetSocketWorld(partProto.kind, out pos, out rot)) return false;
            return true;
        }
    }
}
