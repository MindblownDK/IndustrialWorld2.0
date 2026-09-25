// Assets/Scripts/VoxelEngine/Building/PortalControllerBlock.cs
//
// Portal Controller — the block in front of the portal. NOT a grid block: a static
// placed block mounted beside or before the frame ring, powered off the cable
// network (PowerConsumer).
//
// What it owns, end to end:
//   • SHAPE  — scans the nearby frame blocks, keeps those lying in one axis plane,
//              flood-fills the enclosed interior and rejects anything bigger than
//              64x64 or not actually closed. The interior can be any closed loop:
//              square, ring, whatever the player built.
//   • POWER  — the whole point of big portals: charge draw and the open drain both
//              scale with interior cells. A 64x64 aperture is megawatts per second
//              while open; lose the supply and the portal collapses.
//   • PAIR   — portals link when NAME and CODE both match (player-set on the
//              panel). Two portals, one identity: that is the whole address book.
//   • TRANSIT— while open, the first hull (grid ship) or on-foot player inside the
//              aperture is handed to the linked portal's mouth: the same
//              floating-origin hop the warp drive uses, arrival just outside the
//              linked aperture at matched speed along its facing.
//
// The aperture surface is a generated mesh of one quad per interior cell with a
// procedural warp material — the portal looks like a portal, in any shape built.
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Power;

namespace VoxelEngine.Building
{
    public class PortalControllerBlock : MonoBehaviour
    {
        [Header("Portal Power")]
        [Tooltip("Charge draw (W) while the coils spin up.")]
        public float chargeWatts = 60000f;
        [Tooltip("Extra charge draw (W) per interior cell — big portals are heavy even to spin.")]
        public float chargeWattsPerCell = 60f;
        [Tooltip("Seconds of charging before the aperture can open.")]
        public float chargeSeconds = 45f;
        [Tooltip("Base power drain (W) while the aperture is open.")]
        public float openBaseWatts = 20000f;
        [Tooltip("Power drain (W) per interior cell while open — the big-portal tax.")]
        public float openWattsPerCell = 1500f;
        [Tooltip("Cooldown (s) after the aperture closes before it may charge again.")]
        public float cooldownSeconds = 60f;

        [Header("Portal Identity")]
        [Tooltip("Portals link when NAME and CODE both match.")]
        public string portalName = "Portal";
        [Tooltip("Portals link when NAME and CODE both match. Keep it secret, keep it safe.")]
        public string portalCode = "";

        [Header("Transit")]
        [Tooltip("Seconds a freshly transited ship ignores every aperture (no ping-pong).")]
        public float transitImmunitySeconds = 6f;
        [Tooltip("Cooldown (s) on THIS portal after it sends or receives a transit.")]
        public float transitCooldownSeconds = 3f;

        public const int MaxSpan = 64;

        // ── Runtime state ─────────────────────────────────────────
        public float Charge01 { get; private set; }
        public float Cooldown01 { get; private set; }
        public bool IsOpen { get; private set; }
        public bool IsPowered => _power != null && _power.IsPowered;
        public bool IsValid { get; private set; }
        public string InvalidReason { get; private set; } = "Not scanned yet";
        public int InteriorCells { get; private set; }
        public int InteriorWide { get; private set; }
        public int InteriorHigh { get; private set; }
        public PortalControllerBlock Linked { get; private set; }
        public float OpenWatts => IsOpen ? openBaseWatts + openWattsPerCell * InteriorCells : 0f;
        public float CurrentWatts { get; private set; }
        public Vector3 ApertureCentre { get; private set; }
        public Vector3 ApertureNormal { get; private set; } = Vector3.forward;
        public float ApertureRadius { get; private set; } = 1f;

        private PowerConsumer _power;
        private float _transitLockUntil;
        private float _scanAt;

        // Scan results used by the surface mesh (world-space plane basis).
        private Vector3 _planeBase;
        private Vector3 _uAxis, _vAxis;
        private float _cell = 1f;
        private int _planeMinU, _planeMinV, _dropAxis;
        private List<(int, int)> _interiorGrid = new();

        private GameObject _surface;
        private Material _surfaceMat;

        private void Awake()
        {
            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.connectRadius = 1.6f;
        }

        private void OnEnable()
        {
            _scanAt = 0f;   // scan on the first tick
        }

        private void OnDestroy()
        {
            if (_surface != null) Destroy(_surface);
            if (_surfaceMat != null) Destroy(_surfaceMat);   // runtime material, not an asset
        }

        private void Update()
        {
            if (Cooldown01 > 0f)
                Cooldown01 = Mathf.Max(0f, Cooldown01 - Time.deltaTime / Mathf.Max(1f, cooldownSeconds));

            // Periodic revalidation: frames change, structures grow, gates get griefed.
            if (Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 3f;
                ScanPortal();
                if (IsOpen && !IsValid) Collapse("portal shape lost");
            }

            bool powered = _power != null && _power.IsPowered;

            if (IsOpen)
            {
                // The open drain is the whole cost model: lose the supply, lose the wormhole.
                _power.wattsPerSecond = OpenWatts;
                CurrentWatts = powered ? OpenWatts : 0f;
                if (!powered) { Collapse("power lost"); return; }
                AnimateSurface();
                TryTransits();
                return;
            }

            bool canCharge = powered && Cooldown01 <= 0f && Time.unscaledTime >= _transitLockUntil && IsValid;
            _power.wattsPerSecond = canCharge
                ? chargeWatts + chargeWattsPerCell * InteriorCells
                : 0f;
            CurrentWatts = canCharge ? _power.wattsPerSecond : 0f;
            if (canCharge)
            {
                Charge01 = Mathf.MoveTowards(Charge01, 1f, Time.deltaTime / Mathf.Max(1f, chargeSeconds));
                if (Charge01 >= 1f) OpenPortal();
            }
        }

        // ── Shape scan ────────────────────────────────────────────────

        private readonly List<PortalFrameBlock> _frames = new();

        /// <summary>Re-read the frame network around this controller and rebuild the
        /// interior. Cheap enough to run every few seconds: BFS over local frames,
        /// flood fill over at most a 66x66 cell plane.</summary>
        public void ScanPortal()
        {
            IsValid = false;
            InteriorCells = 0;
            Linked = null;

            // 1. Frames within reach of the controller.
            _frames.Clear();
            Vector3 here = transform.position;
            foreach (var f in PortalFrameBlock.All)
            {
                if (f == null || !f.gameObject.activeInHierarchy) continue;
                if ((f.Bounds.ClosestPoint(here) - here).sqrMagnitude <= 64f) // 8 m
                    _frames.Add(f);
            }
            if (_frames.Count == 0) { InvalidReason = "No frame blocks in reach (8 m)"; RebuildSurface(); return; }

            // 2. Co-plane check per axis: frames must lie in one axis-aligned plane.
            Bounds fb = _frames[0].Bounds;
            foreach (var f in _frames) fb.Encapsulate(f.Bounds);
            Vector3 ext = fb.extents;
            int dropAxis = -1;
            if (ext.x <= PortalFrameCell(_frames) * 0.75f) dropAxis = 0;
            else if (ext.y <= PortalFrameCell(_frames) * 0.75f) dropAxis = 1;
            else if (ext.z <= PortalFrameCell(_frames) * 0.75f) dropAxis = 2;
            if (dropAxis < 0) { InvalidReason = "Frames are not in one plane"; RebuildSurface(); return; }

            float cell = PortalFrameCell(_frames);
            int ua = dropAxis == 0 ? 1 : 0;            // plane axes
            int va = dropAxis == 2 ? 1 : 2;

            // 3. Cell coordinates: quantise frame centres into the plane grid.
            var cells = new Dictionary<(int, int), bool>();
            int minU = int.MaxValue, maxU = int.MinValue, minV = int.MaxValue, maxV = int.MinValue;
            foreach (var f in _frames)
            {
                Vector3 c = f.Bounds.center;
                int u = Mathf.RoundToInt((c[ua] - fb.min[ua]) / cell);
                int v = Mathf.RoundToInt((c[va] - fb.min[va]) / cell);
                cells[(u, v)] = true;
                if (u < minU) minU = u; if (u > maxU) maxU = u;
                if (v < minV) minV = v; if (v > maxV) maxV = v;
            }
            int w = maxU - minU + 1, h = maxV - minV + 1;
            if (w > MaxSpan || h > MaxSpan)
            {
                InvalidReason = $"Portal too big: {w}x{h} (max {MaxSpan}x{MaxSpan})";
                RebuildSurface();
                return;
            }

            // 4. Flood the border: any empty cell reachable from outside is outside.
            //    What the flood cannot reach is the sealed interior.
            var outside = new bool[w + 2, h + 2];
            var stack = new Stack<(int, int)>();
            outside[0, 0] = true;
            stack.Push((0, 0));
            while (stack.Count > 0)
            {
                var (u, v) = stack.Pop();
                for (int d = 0; d < 4; d++)
                {
                    int nu = u + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int nv = v + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nu < 0 || nv < 0 || nu >= w + 2 || nv >= h + 2 || outside[nu, nv]) continue;
                    bool occupied = nu >= 1 && nu <= w && nv >= 1 && nv <= h
                                    && cells.ContainsKey((nu - 1 + minU, nv - 1 + minV));
                    if (occupied) continue;
                    outside[nu, nv] = true;
                    stack.Push((nu, nv));
                }
            }

            var interior = new List<(int, int)>();
            int minIu = int.MaxValue, maxIu = int.MinValue, minIv = int.MaxValue, maxIv = int.MinValue;
            for (int u = 1; u <= w; u++)
                for (int v = 1; v <= h; v++)
                {
                    if (outside[u, v] || cells.ContainsKey((u - 1 + minU, v - 1 + minV))) continue;
                    interior.Add((u, v));
                    if (u < minIu) minIu = u; if (u > maxIu) maxIu = u;
                    if (v < minIv) minIv = v; if (v > maxIv) maxIv = v;
                }
            if (interior.Count == 0)
            {
                InvalidReason = "The frame loop is not closed";
                RebuildSurface();
                return;
            }

            // 5. Interior metrics: the power cost and the aperture volume.
            InteriorCells = interior.Count;
            InteriorWide = maxIu - minIu + 1;
            InteriorHigh = maxIv - minIv + 1;

            // World-space plane basis for the surface mesh: cell (u,v) of the flood
            // grid spans [u-1+minU, u+minU] on the plane's first axis, same on the
            // second. The frame bounds' centre on the dropped axis is the plane.
            Vector3 baseV = Vector3.zero;
            baseV[ua] = fb.min[ua]; baseV[va] = fb.min[va]; baseV[dropAxis] = fb.center[dropAxis];
            _planeBase = baseV;
            _uAxis = Vector3.zero; _uAxis[ua] = 1f;
            _vAxis = Vector3.zero; _vAxis[va] = 1f;
            _cell = cell;
            _planeMinU = minU; _planeMinV = minV;
            _interiorGrid = interior;
            _dropAxis = dropAxis;

            Vector3 centre = Vector3.zero;
            centre[ua] = fb.min[ua] + (minIu - 1 + minU) * cell + InteriorWide * cell * 0.5f;
            centre[va] = fb.min[va] + (minIv - 1 + minV) * cell + InteriorHigh * cell * 0.5f;
            centre[dropAxis] = fb.center[dropAxis];
            ApertureCentre = centre;
            ApertureNormal = dropAxis == 0 ? Vector3.right : dropAxis == 1 ? Vector3.up : Vector3.forward;
            ApertureRadius = Mathf.Max(1f, Mathf.Min(InteriorWide, InteriorHigh) * cell * 0.5f);

            InvalidReason = "";
            IsValid = true;
            RebuildSurface();
        }

        private static float PortalFrameCell(List<PortalFrameBlock> frames)
        {
            float cell = 1f;
            foreach (var f in frames) { cell = f.CellSize; break; }
            return Mathf.Max(0.05f, cell);
        }

        // ── Open / close / collapse ───────────────────────────────────

        public void OpenPortal()
        {
            if (!IsValid) { VoxelEngine.UI.BuildFeedbackHud.Show("Portal", InvalidReason, null, new Color(1f, 0.7f, 0.25f)); return; }
            if (IsOpen || Cooldown01 > 0f) return;
            if (_power == null || !_power.IsPowered)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Portal", "No power on the controller", null, new Color(1f, 0.7f, 0.25f));
                return;
            }
            IsOpen = true;
            RefreshLink();
            VoxelEngine.UI.BuildFeedbackHud.Show("Portal",
                Linked != null ? $"OPEN — linked to {Linked.portalName}" : "OPEN — no matching portal yet (name and code)",
                null, new Color(0.55f, 0.85f, 1f));
        }

        public void ClosePortal(string why)
        {
            if (!IsOpen) return;
            Collapse(why);
        }

        private void Collapse(string why)
        {
            IsOpen = false;
            Charge01 = 0f;
            Cooldown01 = 1f;
            _power.wattsPerSecond = 0f;
            CurrentWatts = 0f;
            if (!string.IsNullOrEmpty(why))
                VoxelEngine.UI.BuildFeedbackHud.Show("Portal", $"Collapsed — {why}", null, new Color(1f, 0.7f, 0.25f));
        }

        /// <summary>The other end: same name AND same code, open, valid, oldest first.</summary>
        public void RefreshLink()
        {
            Linked = null;
            if (!IsOpen) return;
            string name = (portalName ?? "").Trim();
            string code = (portalCode ?? "").Trim();
            if (name.Length == 0 || code.Length == 0) return;
            foreach (var other in FindObjectsByType<PortalControllerBlock>(FindObjectsSortMode.None))
            {
                if (other == null || other == this || !other.IsOpen || !other.IsValid) continue;
                if (!string.Equals((other.portalName ?? "").Trim(), name, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals((other.portalCode ?? "").Trim(), code, System.StringComparison.Ordinal)) continue;
                Linked = other;
                return;   // FindObjectsByType is unordered but stable enough; first match wins
            }
        }

        // ── Transit ───────────────────────────────────────────────────

        private void TryTransits()
        {
            if (Time.unscaledTime < _transitLockUntil) return;
            RefreshLink();
            var pair = Linked;
            if (pair == null) return;

            int mask = ~0;
            var hits = Physics.OverlapSphere(ApertureCentre, ApertureRadius, mask, QueryTriggerInteraction.Collide);
            foreach (var hit in hits)
            {
                if (hit == null) continue;

                var ship = hit.GetComponentInParent<GridSystem.GridEntity>();
                if (ship != null && !RecentlyTransited(ship.transform))
                {
                    TransitShip(ship, pair);
                    return;
                }
                var player = hit.GetComponentInParent<Player.PlayerController>();
                if (player != null && !RecentlyTransited(player.transform))
                {
                    TransitPlayer(player, pair);
                    return;
                }
            }
        }

        // Transit immunity by transform id: a hull that just arrived is standing next
        // to the linked aperture, which is also open — without the stamp the two
        // portals would play ping-pong with every arrival.
        private static readonly Dictionary<int, float> s_transitAt = new();
        private const float TransitImmunitySeconds = 6f;

        private static bool RecentlyTransited(Transform t)
        {
            return s_transitAt.TryGetValue(t.GetInstanceID(), out float at)
                && Time.unscaledTime - at < TransitImmunitySeconds;
        }

        private static void MarkTransited(Transform t)
        {
            s_transitAt[t.GetInstanceID()] = Time.unscaledTime;
        }

        private void TransitShip(GridSystem.GridEntity ship, PortalControllerBlock pair)
        {
            var origin = SpaceOrigin.Instance;
            var registry = CosmicRegistry.Instance;
            if (origin == null || registry == null || !registry.IsReady) return;

            Vector3 keepVel = ship.Body != null ? ship.Body.linearVelocity : Vector3.zero;
            float speed = keepVel.magnitude;
            double3 shipKm = origin.GetCosmicKm(ship.transform.position);
            // Arrive just outside the linked aperture, flying out of its face.
            double3 destKm = origin.GetCosmicKm(
                pair.ApertureCentre + pair.ApertureNormal * (pair.ApertureRadius + 4f));
            if (GridSystem.GridWarpDrive.ArrivalBlocked(registry, destKm))
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Portal", $"{pair.portalName}'s mouth is obstructed", null, new Color(1f, 0.7f, 0.25f));
                return;
            }

            var subject = ship.transform;
            string from = portalName, to = pair.portalName;
            double distKm = math.length(destKm - shipKm);
            VoxelEngine.FX.WarpFx.PlayJump(ship, () =>
            {
                if (ship == null || subject == null) return;
                origin.RegisterRoot(subject);
                origin.TeleportSubjectToCosmic(subject, destKm);
                Vector3 exitVel = pair.ApertureNormal * speed;   // fly OUT of the far mouth
                GridSystem.GridWarpDrive.SettleGridAfterHop(ship, exitVel);
                MarkTransited(subject);
                VoxelEngine.FX.WarpFx.ReportArrival(ship, $"PORTAL {from} TO {to} - {distKm:0} km");
            });

            _transitLockUntil = Time.unscaledTime + Mathf.Max(1f, transitCooldownSeconds);
            pair._transitLockUntil = Time.unscaledTime + Mathf.Max(1f, pair.transitCooldownSeconds);
            VoxelEngine.UI.BuildFeedbackHud.Show("Portal", $"Transit — {to}", null, new Color(0.55f, 0.85f, 1f));
        }

        private void TransitPlayer(Player.PlayerController player, PortalControllerBlock pair)
        {
            var origin = SpaceOrigin.Instance;
            var registry = CosmicRegistry.Instance;
            if (origin == null || registry == null || !registry.IsReady) return;

            float speed = player.GetComponent<Rigidbody>() != null ? player.GetComponent<Rigidbody>().linearVelocity.magnitude : 0f;
            Vector3 exit = pair.ApertureCentre + pair.ApertureNormal * (pair.ApertureRadius + 2f);
            double3 destKm = origin.GetCosmicKm(exit);
            if (GridSystem.GridWarpDrive.ArrivalBlocked(registry, destKm)) return;

            var subject = player.transform;
            origin.RegisterRoot(subject);
            origin.TeleportSubjectToCosmic(subject, destKm);
            // Exit momentum through the movement controller's own impulse path —
            // the player walks out of the far mouth at their entry speed, which
            // then decays to walkable. Zeroing instead would strand them inside
            // the aperture volume of the portal they just arrived at.
            player.ApplyImpulse(pair.ApertureNormal * speed);
            MarkTransited(subject);
            _transitLockUntil = Time.unscaledTime + Mathf.Max(1f, transitCooldownSeconds);
        }

        // ── Aperture surface ──────────────────────────────────────────

        /// <summary>Rebuild the aperture mesh: one quad per sealed interior cell, in
        /// the portal's plane. A closed ring gets a glowing ring with a hole; a
        /// square gets a square of light — the aperture is exactly what was built.
        /// No valid portal, no surface.</summary>
        private void RebuildSurface()
        {
            if (!IsValid || InteriorCells == 0 || _interiorGrid.Count == 0)
            {
                if (_surface != null) Destroy(_surface);
                _surface = null;
                return;
            }

            if (_surface == null)
            {
                _surface = new GameObject("~PortalSurface");
                _surface.transform.SetParent(transform, false);
                _surface.AddComponent<MeshFilter>();
                var rend = _surface.AddComponent<MeshRenderer>();
                _surfaceMat = MakeSurfaceMaterial();
                if (_surfaceMat != null) rend.sharedMaterial = _surfaceMat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }

            float cell = Mathf.Max(0.05f, _cell);
            Vector3 nudge = ApertureNormal * 0.02f;
            var verts = new List<Vector3>(InteriorCells * 4);
            var tris = new List<int>(InteriorCells * 6);
            foreach (var (u, v) in _interiorGrid)
            {
                // Flood-grid cell (u,v) -> world rect on the plane.
                float a0 = (u - 1 + _planeMinU) * cell;
                float a1 = a0 + cell;
                float b0 = (v - 1 + _planeMinV) * cell;
                float b1 = b0 + cell;
                Vector3 c00 = _planeBase + _uAxis * a0 + _vAxis * b0 + nudge;
                Vector3 c10 = _planeBase + _uAxis * a1 + _vAxis * b0 + nudge;
                Vector3 c11 = _planeBase + _uAxis * a1 + _vAxis * b1 + nudge;
                Vector3 c01 = _planeBase + _uAxis * a0 + _vAxis * b1 + nudge;
                int i = verts.Count;
                verts.Add(c00); verts.Add(c10); verts.Add(c11); verts.Add(c01);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
            var mesh = new Mesh { name = "~PortalSurfaceMesh" };
            mesh.indexFormat = InteriorCells > 65000 / 4
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            var bounds = new Bounds(ApertureCentre, Vector3.zero);
            foreach (var v in verts) bounds.Encapsulate(v);
            bounds.Expand(1f);
            mesh.bounds = bounds;              // world-space vertices, identity transform
            mesh.RecalculateNormals();
            _surface.GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private void AnimateSurface()
        {
            if (_surfaceMat == null) return;
            float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 2.4f);
            if (_surfaceMat.HasProperty("_Intensity")) _surfaceMat.SetFloat("_Intensity", 1.2f * pulse);
            Color c = new Color(0.35f, 0.75f, 1f, 0.85f * pulse);
            if (_surfaceMat.HasProperty("_Color")) _surfaceMat.SetColor("_Color", c);
            if (_surfaceMat.HasProperty("_BaseColor")) _surfaceMat.SetColor("_BaseColor", c);
        }

        private static Material MakeSurfaceMaterial()
        {
            Shader sh = Shader.Find("VoxelEngine/WarpBubble")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");
            if (sh == null) return null;   // no shaders at all: keep the default material
            var mat = new Material(sh) { name = "Mat_PortalSurface" };
            Color c = new Color(0.35f, 0.75f, 1f, 0.8f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_Intensity")) mat.SetFloat("_Intensity", 1.1f);
            return mat;
        }

        // ── Save / restore ────────────────────────────────────────────

        /// <summary>Save-load restore: name, code and charge/cooldown. A portal never
        /// restores open — a player re-opens it on their own terms.</summary>
        public void RestorePersistentState(string name, string code, float charge01, float cooldown01)
        {
            portalName = name ?? "";
            portalCode = code ?? "";
            Charge01 = Mathf.Clamp01(charge01);
            Cooldown01 = Mathf.Clamp01(cooldown01);
            IsOpen = false;
        }
    }
}
