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
    /// <summary>The four engine service categories a pipe can hook into.</summary>
    public enum PortService : byte
    {
        Fuel = 0,
        Coolant = 1,
        Oxygen = 2,
        Exhaust = 3,
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
                default: return "Service port";
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
            var ringMat = PortMaterial(col, emissive: col * 0.35f);
            var eyeMat = PortMaterial(col, emissive: col * 0.9f);

            // Outer collar (a thin disc facing outward along local +Z).
            var collar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            collar.name = "Collar";
            collar.transform.SetParent(container.transform, false);
            collar.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            collar.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder axis → +Z
            collar.transform.localScale = new Vector3(0.34f, 0.06f, 0.34f);
            ApplyVisual(collar, ringMat);

            // Glowing centre eye the player aims at.
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "Eye";
            eye.transform.SetParent(container.transform, false);
            eye.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            eye.transform.localScale = new Vector3(0.16f, 0.16f, 0.10f);
            ApplyVisual(eye, eyeMat);

            // Short nipple the pipe visually plugs onto.
            var nipple = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            nipple.name = "Nipple";
            nipple.transform.SetParent(container.transform, false);
            nipple.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            nipple.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            nipple.transform.localScale = new Vector3(0.16f, 0.16f, 0.16f);
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
    //  PORT PLANNER — shared by BuildSystem (liquid/gas pipes) and
    //  GridBuilder (exhaust pipes). Pure geometry + capacity logic; the
    //  caller decides whether to actually commit (placement) or just
    //  preview (ghost). Ghost ≡ placed because both run the same math.
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
            public Vector3 seatGridLocal; // grid-local pipe-hub seat (where the pipe sits)
            public Vector3Int faceAxis;   // grid-space outward face axis
            public Transform existing;    // existing port transform when reusesExisting
        }

        /// <summary>Plan a liquid or gas pipe attachment onto an engine surface.</summary>
        public static Plan PlanPipe(GridEntity grid, GridMaritimeEngine engine, bool isGas,
            Vector3 hitPointWorld, Vector3 hitNormalWorld, float detailCell)
        {
            var plan = new Plan();
            if (grid == null || engine == null) return plan;

            var vports = engine.VariablePorts;
            PortService service = isGas
                ? ResolveGasService(grid, vports)
                : ResolveLiquidService(grid, vports);
            plan.service = service;

            // Reuse an already-installed port of this service if one exists — the
            // pipe re-snaps to it instead of spawning a duplicate collar.
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
                plan.atCap = true;
                return plan;
            }

            FillSeatFromSurface(grid, engine, hitPointWorld, hitNormalWorld, detailCell, ref plan);
            plan.ok = true;
            return plan;
        }

        /// <summary>Plan an exhaust-pipe attachment onto an engine surface.</summary>
        public static Plan PlanExhaust(GridEntity grid, GridMaritimeEngine engine,
            Vector3 hitPointWorld, Vector3 hitNormalWorld, float detailCell)
        {
            var plan = new Plan { service = PortService.Exhaust };
            if (grid == null || engine == null) return plan;

            var vports = engine.VariablePorts;
            var existing = vports.FindExisting(PortService.Exhaust);
            if (existing != null)
            {
                plan.ok = true;
                plan.reusesExisting = true;
                plan.existing = existing;
                FillSeatFromPort(grid, engine, existing, detailCell, ref plan);
                return plan;
            }
            if (!vports.CanAdd(PortService.Exhaust))
            {
                plan.atCap = true;
                return plan;
            }

            FillSeatFromSurface(grid, engine, hitPointWorld, hitNormalWorld, detailCell, ref plan);
            plan.ok = true;
            return plan;
        }

        // Seat the pipe just OUTSIDE the hit surface, along a cardinal-snapped
        // outward. The dynamic port collar sits on the surface; the pipe hub sits a
        // comfortable stub-distance beyond it so it is plainly visible and easy to
        // chain the next pipe onto.
        private static void FillSeatFromSurface(GridEntity grid, GridMaritimeEngine engine,
            Vector3 hitPointWorld, Vector3 hitNormalWorld, float detailCell, ref Plan plan)
        {
            Vector3 outWorld = SnapOutwardToWorld(grid, hitNormalWorld);

            Vector3 portWorld = hitPointWorld + outWorld * (detailCell * 0.12f);
            Vector3 seatWorld = hitPointWorld + outWorld * (detailCell * 0.85f);

            plan.portLocal = engine.transform.InverseTransformPoint(portWorld);
            plan.outLocal = engine.transform.InverseTransformDirection(outWorld).normalized;
            plan.seatGridLocal = grid.transform.InverseTransformPoint(seatWorld);
            plan.faceAxis = UnifiedGridTopology.SnapFaceAxis(grid, outWorld);
            if (plan.faceAxis == Vector3Int.zero) plan.faceAxis = Vector3Int.up;
        }

        // When reusing an existing dynamic port, seat the pipe one stub-distance
        // beyond the port along its authored outward (identical look to a fresh port).
        private static void FillSeatFromPort(GridEntity grid, GridMaritimeEngine engine,
            Transform port, float detailCell, ref Plan plan)
        {
            Vector3 outWorld = MaritimePorts.PortOutwardWorld(port, engine.transform.up);
            Vector3 seatWorld = port.position + outWorld * (detailCell * 0.70f);
            plan.portLocal = engine.transform.InverseTransformPoint(port.position);
            plan.outLocal = engine.transform.InverseTransformDirection(outWorld).normalized;
            plan.seatGridLocal = grid.transform.InverseTransformPoint(seatWorld);
            plan.faceAxis = UnifiedGridTopology.SnapFaceAxis(grid, outWorld);
            if (plan.faceAxis == Vector3Int.zero) plan.faceAxis = Vector3Int.up;
        }

        /// <summary>Snap a world normal to the nearest grid-frame cardinal axis so
        /// pipes always route along clean lattice lines.</summary>
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
        // The liquid/gas pipes are generic; the SERVICE a port takes on is inferred
        // from what the grid's tanks actually hold, with an "engine still needs it"
        // tiebreaker. Functionality never depends on the label — an engine draws
        // fuel AND coolant from whichever tanks are connected through the liquid
        // family — so the inference only drives the colour + the per-service cap.
        private static PortService ResolveLiquidService(GridEntity grid, MaritimeVariablePorts vports)
        {
            bool hasFuel = false, hasCoolant = false;
            foreach (var block in grid.AllBlocks)
            {
                if (block is GridLiquidTank tank && tank.Enabled)
                {
                    if (IsCoolantLiquid(tank.liquidType)) hasCoolant = true;
                    else hasFuel = true; // CrudeOil/RefinedOil/LiquidFuel/HFO/MGO
                }
            }

            PortService choice;
            if (hasFuel && !hasCoolant) choice = PortService.Fuel;
            else if (hasCoolant && !hasFuel) choice = PortService.Coolant;
            else choice = !vports.HasAny(PortService.Fuel) ? PortService.Fuel
                : !vports.HasAny(PortService.Coolant) ? PortService.Coolant
                : PortService.Fuel;

            // Steer toward a service that still has capacity.
            if (choice == PortService.Fuel && !vports.CanAdd(PortService.Fuel) && vports.CanAdd(PortService.Coolant))
                choice = PortService.Coolant;
            else if (choice == PortService.Coolant && !vports.CanAdd(PortService.Coolant) && vports.CanAdd(PortService.Fuel))
                choice = PortService.Fuel;
            return choice;
        }

        private static PortService ResolveGasService(GridEntity grid, MaritimeVariablePorts vports)
        {
            bool hasOxygen = false, hasExhaust = false;
            foreach (var block in grid.AllBlocks)
            {
                if (block is GridGasTank tank && tank.Enabled)
                {
                    if (tank.gasType == VoxelEngine.Gas.GasType.ExhaustGas) hasExhaust = true;
                    else if (tank.gasType == VoxelEngine.Gas.GasType.Oxygen) hasOxygen = true;
                }
            }

            PortService choice;
            if (hasOxygen && !hasExhaust) choice = PortService.Oxygen;
            else if (hasExhaust && !hasOxygen) choice = PortService.Exhaust;
            else choice = !vports.HasAny(PortService.Oxygen) ? PortService.Oxygen
                : vports.CanAdd(PortService.Exhaust) ? PortService.Exhaust
                : PortService.Oxygen;

            if (choice == PortService.Oxygen && !vports.CanAdd(PortService.Oxygen) && vports.CanAdd(PortService.Exhaust))
                choice = PortService.Exhaust;
            else if (choice == PortService.Exhaust && !vports.CanAdd(PortService.Exhaust) && vports.CanAdd(PortService.Oxygen))
                choice = PortService.Oxygen;
            return choice;
        }

        private static bool IsCoolantLiquid(LiquidType t)
            => t == LiquidType.Water || t == LiquidType.MarineEngineCoolant;
    }
}
