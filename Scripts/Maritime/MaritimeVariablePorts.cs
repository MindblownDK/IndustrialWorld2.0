// Assets/Scripts/VoxelEngine/Maritime/MaritimeVariablePorts.cs
//
//  ╔══════════════════════════════════════════════════════════════════╗
//  ║  VARIABLE SERVICE PORTS — color-coded, "connect from anywhere"    ║
//  ║  attachment points for the HFO V8 (Medium) and MGO V12 (Giant)    ║
//  ║  maritime engines.                                                 ║
//  ║                                                                    ║
//  ║  Instead of forcing the player to thread pipes onto fixed ports   ║
//  ║  buried deep inside the engine hull, the player aims at ANY face  ║
//  ║  of the engine and a color-coded service port is born exactly at  ║
//  ║  the surface — always OUTSIDE the body, always visible, always    ║
//  ║  easy to chain more pipe onto.                                    ║
//  ║                                                                    ║
//  ║  Rules (per engine):                                              ║
//  ║    • 1 × Fuel   (amber)   • 1 × Coolant (teal)                    ║
//  ║    • 1 × Oxygen (sky-blue) • 2 × Exhaust (red)                    ║
//  ║  Trying to attach a pipe whose service is already at capacity is  ║
//  ║  rejected with feedback — no pipe spam on a single engine.        ║
//  ║                                                                    ║
//  ║  Each dynamic port is a plain child transform named with the SAME ║
//  ║  prefix as the authored ports (Port_FuelInput, Port_CoolantInput, ║
//  ║  Port_OxygenInput, Port_ExhaustOutput) and carries a              ║
//  ║  MaritimePortFacing tag, so EVERY existing system — pipe snapping ║
//  ║  (BuildSystem/GridBuilder), network topology (GridLiquidNetwork / ║
//  ║  GridGasNetwork) and machine consumption — works unchanged. The   ║
//  ║  authored model ports stay in place as save-compatible defaults;  ║
//  ║  dynamic ports are purely additive and serialize across saves.    ║
//  ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    /// <summary>The engine service categories a pipe can hook into.</summary>
    public enum PortService : byte
    {
        Fuel = 0,
        Coolant = 1,
        Oxygen = 2,
        Exhaust = 3,
        Item = 4,
    }

    /// <summary>Which pipe family the player is holding — drives which service a
    /// variable port takes on.</summary>
    public enum PipeFamily : byte
    {
        Liquid = 0,
        Gas = 1,
        Item = 2,
    }

    /// <summary>Serializable description of one dynamic service port. Stored on the
    /// engine and re-materialised on save/load — a dynamic port is fully defined by
    /// its service + engine-local position + engine-local outward direction.</summary>
    [System.Serializable]
    public class VariablePortRecord
    {
        public int service;
        public Vector3 localPos;
        public Vector3 localOutward;

        public VariablePortRecord() { }
        public VariablePortRecord(PortService s, Vector3 pos, Vector3 outward)
        {
            service = (int)s;
            localPos = pos;
            localOutward = outward;
        }

        public PortService Service => (PortService)service;
    }

    /// <summary>
    /// Lives on a maritime engine. Owns the set of player-installed dynamic service
    /// ports, enforces the per-service caps, and (re)builds the physical port
    /// GameObjects. Authored model ports are NOT tracked here — they remain the
    /// engine's built-in defaults and keep existing saves working untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public class MaritimeVariablePorts : MonoBehaviour
    {
        [SerializeField] private List<VariablePortRecord> _records = new List<VariablePortRecord>();
        private readonly List<Transform> _runtimePorts = new List<Transform>(4);

        // ── Per-service configuration ─────────────────────────────────
        /// <summary>Maximum dynamic ports of a service allowed on one engine.</summary>
        public static int MaxFor(PortService s)
        {
            switch (s)
            {
                case PortService.Fuel: return 1;
                case PortService.Coolant: return 1;
                case PortService.Oxygen: return 1;
                case PortService.Exhaust: return 2;
                case PortService.Item: return 1;
                default: return 1;
            }
        }

        /// <summary>Named-port prefix used by snapping + network code. The dynamic
        /// port transform is named "<prefix>_V" so StartsWith(prefix) still matches.</summary>
        public static string PrefixFor(PortService s)
        {
            switch (s)
            {
                case PortService.Fuel: return "Port_FuelInput";
                case PortService.Coolant: return "Port_CoolantInput";
                case PortService.Oxygen: return "Port_OxygenInput";
                case PortService.Exhaust: return "Port_ExhaustOutput";
                case PortService.Item: return "Port_ItemIntake";
                default: return "Port_LiquidIO";
            }
        }

        /// <summary>Color-coded ring material per service — the player reads the
        /// service of an installed port at a glance.</summary>
        public static Color ColorFor(PortService s)
        {
            switch (s)
            {
                case PortService.Fuel: return new Color(0.95f, 0.62f, 0.12f);    // amber
                case PortService.Coolant: return new Color(0.20f, 0.85f, 0.75f);  // teal
                case PortService.Oxygen: return new Color(0.45f, 0.75f, 1.00f);   // sky-blue
                case PortService.Exhaust: return new Color(0.90f, 0.20f, 0.12f);  // red
                case PortService.Item: return new Color(0.62f, 0.86f, 0.32f);     // green
                default: return Color.white;
            }
        }

        public static string LabelFor(PortService s)
        {
            switch (s)
            {
                case PortService.Fuel: return "Fuel input";
                case PortService.Coolant: return "Coolant input";
                case PortService.Oxygen: return "Oxygen input";
                case PortService.Exhaust: return "Exhaust output";
                case PortService.Item: return "Item intake";
                default: return "Service port";
            }
        }

        /// <summary>Which services an engine tier offers as variable ports. The Crude
        /// Inline-4 (Small, solid-fuel) takes only oxygen + item intake; the liquid
        /// HFO V8 (Medium) and MGO V12 (Giant) take fuel + coolant + oxygen. Exhaust
        /// always uses the engine's authored exhaust collector(s).</summary>
        public static bool IsServiceAllowed(EngineTier tier, PortService s)
        {
            switch (tier)
            {
                case EngineTier.Small:
                    return s == PortService.Oxygen || s == PortService.Item;
                case EngineTier.Medium:
                case EngineTier.Giant:
                    return s == PortService.Fuel || s == PortService.Coolant || s == PortService.Oxygen;
                default:
                    return false;
            }
        }

        // ── Queries ───────────────────────────────────────────────────
        public int Count(PortService s)
        {
            int n = 0;
            for (int i = 0; i < _records.Count; i++)
                if (_records[i] != null && _records[i].Service == s) n++;
            return n;
        }

        public bool HasAny(PortService s) => Count(s) > 0;

        public bool CanAdd(PortService s) => Count(s) < MaxFor(s);

        /// <summary>The transform of an already-installed dynamic port of this
        /// service, or null. Used so a second pipe of the same service re-snaps to
        /// the existing port instead of spawning a duplicate.</summary>
        public Transform FindExisting(PortService s)
        {
            string prefix = PrefixFor(s);
            for (int i = 0; i < _runtimePorts.Count; i++)
            {
                var t = _runtimePorts[i];
                if (t != null && t.name.StartsWith(prefix, System.StringComparison.Ordinal)) return t;
            }
            return null;
        }

        // ── Creation ──────────────────────────────────────────────────
        /// <summary>Install a new dynamic service port. Returns the port transform,
        /// or null when the service is already at capacity. The port is parented to
        /// this engine so it moves + persists with the grid.</summary>
        public Transform AddPort(PortService s, Vector3 localPos, Vector3 localOutward)
        {
            if (!CanAdd(s)) return null;
            if (localOutward.sqrMagnitude < 0.0001f) localOutward = Vector3.up;
            localOutward = localOutward.normalized;

            var record = new VariablePortRecord(s, localPos, localOutward);
            _records.Add(record);
            var port = BuildPortObject(record);
            if (port != null) _runtimePorts.Add(port);
            return port;
        }

        /// <summary>Rebuild every dynamic port from saved records (save/load). Clears
        /// any existing dynamic port objects first — idempotent.</summary>
        public void RebuildFromRecords(List<VariablePortRecord> records)
        {
            ClearDynamicObjects();
            _records.Clear();
            if (records == null) return;
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                if (r == null) continue;
                if (r.localOutward.sqrMagnitude < 0.0001f) r.localOutward = Vector3.up;
                _records.Add(r);
                var port = BuildPortObject(r);
                if (port != null) _runtimePorts.Add(port);
            }
        }

        /// <summary>Snapshot of the current records for the save system.</summary>
        public List<VariablePortRecord> CaptureRecords()
        {
            var copy = new List<VariablePortRecord>(_records.Count);
            for (int i = 0; i < _records.Count; i++)
            {
                var r = _records[i];
                if (r == null) continue;
                copy.Add(new VariablePortRecord(r.Service, r.localPos, r.localOutward));
            }
            return copy;
        }

        public bool HasRecords => _records != null && _records.Count > 0;

        private void ClearDynamicObjects()
        {
            for (int i = 0; i < _runtimePorts.Count; i++)
                if (_runtimePorts[i] != null) Destroy(_runtimePorts[i].gameObject);
            _runtimePorts.Clear();
        }

        // ── Physical port object ──────────────────────────────────────
        // A short color-coded collar + glowing eye sitting proud of the hull. The
        // container carries the MaritimePortFacing tag (single source of truth for
        // snap orientation) and is named with the service prefix so all existing
        // snapping / network code discovers it like an authored port.
        private Transform BuildPortObject(VariablePortRecord r)
        {
            var service = r.Service;
            var container = new GameObject(PrefixFor(service) + "_V");
            container.transform.SetParent(transform, false);
            container.transform.localPosition = r.localPos;

            // Orient +Z of the container along the authored outward so child discs
            // face the right way (matches MaritimeMeshBuilder.Port behaviour).
            Vector3 dir = r.localOutward.normalized;
            Vector3 guide = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            container.transform.localRotation = Quaternion.LookRotation(dir, guide);

            var facing = container.AddComponent<MaritimePortFacing>();
            facing.localOutward = dir;

            Color col = ColorFor(service);
            var ringMat = PortMaterial(col, emissive: col * 0.40f);
            var eyeMat = PortMaterial(col, emissive: col * 0.95f);

            // Low-profile color-coded collar that sits FLUSH on the hull (the
            // container itself is inset slightly into the surface by the planner, so
            // this disc reads as mounted on the engine — never floating off it).
            // Kept deliberately small so it doesn't swallow a thin gas pipe.
            var collar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            collar.name = "Collar";
            collar.transform.SetParent(container.transform, false);
            collar.transform.localPosition = new Vector3(0f, 0f, 0.005f);
            collar.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder axis → +Z
            collar.transform.localScale = new Vector3(0.12f, 0.02f, 0.12f); // ~0.24 m disc, 0.04 m thick
            ApplyVisual(collar, ringMat);

            // Small glowing centre eye the player aims at (sphere primitive is 1 m
            // across, so this is a ~0.09 m dot).
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "Eye";
            eye.transform.SetParent(container.transform, false);
            eye.transform.localPosition = new Vector3(0f, 0f, 0.016f);
            eye.transform.localScale = new Vector3(0.09f, 0.09f, 0.05f);
            ApplyVisual(eye, eyeMat);

            // Short, thin coupling nipple the pipe plugs onto — slim enough that a
            // gas pipe stays clearly visible around it (~0.07 m dia × 0.07 m long).
            var nipple = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            nipple.name = "Nipple";
            nipple.transform.SetParent(container.transform, false);
            nipple.transform.localPosition = new Vector3(0f, 0f, 0.04f);
            nipple.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            nipple.transform.localScale = new Vector3(0.035f, 0.035f, 0.035f);
            ApplyVisual(nipple, ringMat);

            return container.transform;
        }

        private static void ApplyVisual(GameObject go, Material mat)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = mat;
        }

        // Cached service materials (one per service colour) so we don't allocate
        // fresh materials for every port we spawn.
        private static readonly Dictionary<int, Material> s_matCache = new Dictionary<int, Material>();
        private static Material PortMaterial(Color c, Color emissive)
        {
            int key = (Mathf.RoundToInt(c.r * 255) << 16) | (Mathf.RoundToInt(c.g * 255) << 8) | Mathf.RoundToInt(c.b * 255);
            if (s_matCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.35f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.6f);
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emissive); }
            s_matCache[key] = m;
            return m;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  PORT PLANNER — used by BuildSystem for liquid / gas / item pipe
    //  snaps. Pure geometry + capacity logic; the caller decides whether to
    //  commit (placement) or just preview (ghost). Ghost ≡ placed because
    //  both run the same math. Exhaust uses the engine's authored exhaust
    //  collectors (no variable exhaust port), so it isn't planned here.
    // ════════════════════════════════════════════════════════════════════
    public static class MaritimePortPlanner
    {
        public struct Plan
        {
            public bool ok;               // a pipe can attach here (new or existing port)
            public bool reusesExisting;   // snapping to an already-installed port
            public bool atCap;            // rejected: service already at capacity
            public PortService service;
            public Vector3 portLocal;     // engine-local dynamic-port position
            public Vector3 outLocal;      // engine-local outward (for MaritimePortFacing)
            public Vector3 seatGridLocal; // grid-local pipe-hub seat (snapped to the detail lattice by the caller)
            public Vector3Int faceAxis;   // grid-space outward face axis
            public Transform existing;    // existing port transform when reusesExisting
        }

        /// <summary>Plan a liquid / gas / item pipe attachment onto an engine surface.</summary>
        public static Plan PlanPipe(GridEntity grid, GridMaritimeEngine engine, PipeFamily family,
            Vector3 hitPointWorld, Vector3 hitNormalWorld, float detailCell)
        {
            var plan = new Plan();
            if (grid == null || engine == null) return plan;

            var vports = engine.VariablePorts;
            PortService service = ResolveService(family, grid, vports, engine.tier, hitPointWorld);
            plan.service = service;

            // This engine tier doesn't offer that service (e.g. a liquid pipe on the
            // solid-fuel Crude engine) — decline so placement falls through to a normal
            // detail-lattice pipe instead of forcing an unsupported port.
            if (!MaritimeVariablePorts.IsServiceAllowed(engine.tier, service)) return plan;

            // Reuse an already-installed port of this service if one exists — the pipe
            // re-snaps to it instead of spawning a duplicate collar.
            var existing = vports.FindExisting(service);
            if (existing != null)
            {
                plan.ok = true;
                plan.reusesExisting = true;
                plan.existing = existing;
                FillSeatFromPort(grid, engine, existing, detailCell, ref plan);
                return plan;
            }

            if (!vports.CanAdd(service))
            {
                plan.atCap = true; // → caller shows "<service> already connected (max N)"
                return plan;
            }

            FillSeatFromSurface(grid, engine, hitPointWorld, hitNormalWorld, detailCell, ref plan);
            plan.ok = true;
            return plan;
        }

        // The dynamic port collar mounts FLUSH on the hull (inset a touch so it reads
        // as bolted to the engine — never floating off the collider). The pipe hub
        // claims the detail cell just OUTSIDE the surface; the caller snaps the hub
        // onto that lattice cell so placement is fine-grid aligned.
        private static void FillSeatFromSurface(GridEntity grid, GridMaritimeEngine engine,
            Vector3 hitPointWorld, Vector3 hitNormalWorld, float detailCell, ref Plan plan)
        {
            Vector3 outWorld = SnapOutwardToWorld(grid, hitNormalWorld);
            Vector3 portWorld = hitPointWorld - outWorld * 0.02f;
            Vector3 seatWorld = hitPointWorld + outWorld * (detailCell * 0.55f);

            plan.portLocal = engine.transform.InverseTransformPoint(portWorld);
            plan.outLocal = engine.transform.InverseTransformDirection(outWorld).normalized;
            plan.seatGridLocal = grid.transform.InverseTransformPoint(seatWorld);
            plan.faceAxis = UnifiedGridTopology.SnapFaceAxis(grid, outWorld);
            if (plan.faceAxis == Vector3Int.zero) plan.faceAxis = Vector3Int.up;
        }

        // When reusing an existing dynamic port, seat the pipe one stub-distance beyond
        // the port along its authored outward (identical look to a fresh port).
        private static void FillSeatFromPort(GridEntity grid, GridMaritimeEngine engine,
            Transform port, float detailCell, ref Plan plan)
        {
            Vector3 outWorld = MaritimePorts.PortOutwardWorld(port, engine.transform.up);
            Vector3 seatWorld = port.position + outWorld * (detailCell * 0.55f);
            plan.portLocal = engine.transform.InverseTransformPoint(port.position);
            plan.outLocal = engine.transform.InverseTransformDirection(outWorld).normalized;
            plan.seatGridLocal = grid.transform.InverseTransformPoint(seatWorld);
            plan.faceAxis = UnifiedGridTopology.SnapFaceAxis(grid, outWorld);
            if (plan.faceAxis == Vector3Int.zero) plan.faceAxis = Vector3Int.up;
        }

        /// <summary>Snap a world normal to the nearest grid-frame cardinal axis so
        /// ports + pipes always route along clean lattice lines.</summary>
        private static Vector3 SnapOutwardToWorld(GridEntity grid, Vector3 normalWorld)
        {
            Vector3 local = grid.transform.InverseTransformDirection(
                normalWorld.sqrMagnitude > 0.0001f ? normalWorld.normalized : Vector3.up);
            float ax = Mathf.Abs(local.x), ay = Mathf.Abs(local.y), az = Mathf.Abs(local.z);
            Vector3 card;
            if (ax >= ay && ax >= az) card = new Vector3(Mathf.Sign(LocalSign(local.x)), 0f, 0f);
            else if (ay >= ax && ay >= az) card = new Vector3(0f, Mathf.Sign(LocalSign(local.y)), 0f);
            else card = new Vector3(0f, 0f, Mathf.Sign(LocalSign(local.z)));
            Vector3 world = grid.transform.TransformDirection(card).normalized;
            return world.sqrMagnitude > 0.0001f ? world : Vector3.up;
        }
        private static float LocalSign(float v) => Mathf.Approximately(v, 0f) ? 1f : v;

        // ── Service resolution ────────────────────────────────────────
        private static PortService ResolveService(PipeFamily family, GridEntity grid,
            MaritimeVariablePorts vports, EngineTier tier, Vector3 hitPoint)
        {
            switch (family)
            {
                case PipeFamily.Liquid: return ResolveLiquidService(grid, vports, hitPoint);
                case PipeFamily.Gas:    return PortService.Oxygen;
                case PipeFamily.Item:   return PortService.Item;
                default:                return PortService.Fuel;
            }
        }

        // Liquid pipes are generic; the SERVICE (fuel vs coolant) is inferred from what
        // the pipe run being extended actually carries — queried through the cached
        // liquid network from the nearest already-placed liquid pipe. When nothing is
        // connected yet (building engine-first) we fall back to "what the engine still
        // needs". There is deliberately NO auto-switch when a service is full, so the
        // caller surfaces the "<service> already connected (max N)" message.
        private static PortService ResolveLiquidService(GridEntity grid, MaritimeVariablePorts vports, Vector3 hitPoint)
        {
            bool hasFuel = false, hasCoolant = false;
            var net = GridLiquidNetwork.Instance;
            var seed = FindNearestPipe(grid, hitPoint, isGas: false);
            if (net != null && seed != null)
            {
                hasFuel =
                    net.AvailableLiquidFor(seed, LiquidType.HeavyFuelOil) > 0.001f ||
                    net.AvailableLiquidFor(seed, LiquidType.MarineGasOil) > 0.001f ||
                    net.AvailableLiquidFor(seed, LiquidType.LiquidFuel) > 0.001f ||
                    net.AvailableLiquidFor(seed, LiquidType.CrudeOil) > 0.001f ||
                    net.AvailableLiquidFor(seed, LiquidType.RefinedOil) > 0.001f;
                hasCoolant =
                    net.AvailableLiquidFor(seed, LiquidType.MarineEngineCoolant) > 0.001f ||
                    net.AvailableLiquidFor(seed, LiquidType.Water) > 0.001f;
            }

            if (hasFuel && !hasCoolant) return PortService.Fuel;
            if (hasCoolant && !hasFuel) return PortService.Coolant;
            // Ambiguous (both / neither) — assign by what the engine still needs.
            return !vports.HasAny(PortService.Fuel) ? PortService.Fuel
                : !vports.HasAny(PortService.Coolant) ? PortService.Coolant
                : PortService.Fuel;
        }

        /// <summary>Nearest already-placed pipe of the family within ~3 m of the aim
        /// point — the run the player is extending. Used to read what it carries.</summary>
        private static GridBlock FindNearestPipe(GridEntity grid, Vector3 worldPos, bool isGas)
        {
            GridBlock best = null;
            float bestSq = 9f; // 3 m
            foreach (var b in grid.AllBlocks)
            {
                if (b == null) continue;
                bool match = isGas
                    ? b.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null
                    : b.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null;
                if (!match) continue;
                float d = (b.transform.position - worldPos).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = b; }
            }
            return best;
        }
    }
}
