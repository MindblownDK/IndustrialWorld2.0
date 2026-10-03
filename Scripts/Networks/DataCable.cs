// Assets/Scripts/VoxelEngine/Networks/DataCable.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║                   DATA PIPE — wired ItemNet                      ║
// ║  Carries connectivity (no per-tick balancing) between the       ║
// ║  Server Controller, NAS shelves, Power Stations, terminals,     ║
// ║  importers/exporters and External Storage bridges.              ║
// ║                                                                  ║
// ║  14.41.0: the pipe shares the energy pipe's NINE fitting        ║
// ║  shapes (straight 1-5 m, elbows, risers, S-curves, compound     ║
// ║  bends, 4/6-way hubs) picked on the same radial wheel - but     ║
// ║  renders as an actual DATA CABLE: slim braided trunk, RJ45-     ║
// ║  style plug heads, phosphor pulse rings. Links happen where     ║
// ║  plug meets plug (or plug meets device), plus the storage       ║
// ║  network's universal touching-blocks rule.                      ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Networks
{
    [DisallowMultipleComponent]
    public class DataCable : MonoBehaviour
    {
        [Header("Shape Variant")]
        public EnergyPipeVariant variant = EnergyPipeVariant.Straight;
        [Range(1, 5)] public int straightLength = 1;

        [Header("Grid")]
        [Tooltip("Build grid size used by the legacy cardinal-neighbour fallback.")]
        public float gridSize = 1f;
        [Tooltip("Distance tolerance for the legacy cardinal-neighbour fallback.")]
        public float positionTolerance = 0.15f;
        [Tooltip("Layers tested with a linecast between two link points. A hit " +
                 "(excluding the pipes themselves) blocks the link.")]
        public LayerMask losBlockingLayers = ~0;

        [Header("Visual")]
        public Color sheathColor = new(0.13f, 0.15f, 0.17f, 1f);
        public Color glowColor   = new(0.30f, 0.95f, 0.45f, 1f);

        // Legacy fields kept so old prefabs/presets deserialize silently.
        [HideInInspector] public float coreSize = 0.35f;
        [HideInInspector] public float armThickness = 0.28f;
        [HideInInspector] public Color tint = new(0.30f, 0.85f, 0.40f, 1f);
        [HideInInspector] public bool showUnusedFaceCaps = false;

        // ── Link tuning ──────────────────────────────────────────
        /// <summary>Plug-to-plug mating distance between two pipe endpoints.</summary>
        private const float ENDPOINT_LINK_RADIUS = 0.60f;
        /// <summary>Probe radius for a plug head looking for a device face.</summary>
        private const float DEVICE_PROBE_RADIUS = 0.45f;
        private const float SCAN_INTERVAL = 0.5f;

        // ── Runtime ──────────────────────────────────────────────
        public ConnectionAnchor anchor;          // exposed for inspectors / debugging
        private Transform _visualRoot;
        private Mesh _instanceMesh;   // bridge-arm mesh (per instance, never cached)
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private float _scanTimer;
        private EnergyPipeVariant _builtVariant;
        private int _builtLength = -1;
        private readonly Dictionary<ConnectionAnchor, CubeFace> _connectionFaces = new();

        private static readonly HashSet<DataCable> _AllCables = new();
        private static readonly Collider[]   s_overlapBuffer = new Collider[128];
        private static readonly RaycastHit[] s_rayBuffer     = new RaycastHit[48];
        private static readonly HashSet<DataCable> s_refreshCandidates = new();

        // Shared materials/meshes - one per look, not one per pipe.
        private static Material s_sheathMat;
        private static Material s_glowMat;
        private static readonly Dictionary<(EnergyPipeVariant, int), Mesh> s_meshCache = new();

        private void Awake()
        {
            EnsureAnchor();
            if (losBlockingLayers == ~0) losBlockingLayers = ~(1 << 2);
        }

        private void OnEnable()
        {
            _AllCables.Add(this);
            ScanAndLink();
            RebuildVisuals();
        }

        private void OnDisable()
        {
            _AllCables.Remove(this);
            _connectionFaces.Clear();
            if (anchor != null) anchor.DisconnectAll();
            RefreshNearbyDataCables(transform.position, 6f);
        }

        private void OnDestroy()
        {
            if (_instanceMesh != null)
            {
                if (Application.isPlaying) Destroy(_instanceMesh);
                else DestroyImmediate(_instanceMesh);
                _instanceMesh = null;
            }
        }

        private void Update()
        {
            _scanTimer += Time.deltaTime;
            if (_scanTimer < SCAN_INTERVAL) return;
            _scanTimer = 0f;
            bool changed = ScanAndLink();
            if (changed || _builtVariant != variant || _builtLength != straightLength)
                RebuildVisuals();
        }

        /// <summary>Rescan + redraw every data pipe near a point - the data
        /// twin of PowerCable.RefreshNearbyCables.</summary>
        public static void RefreshNearbyDataCables(Vector3 center, float radius = 6f)
        {
            s_refreshCandidates.Clear();
            foreach (var c in _AllCables)
                if (c != null && c.isActiveAndEnabled
                    && (c.transform.position - center).sqrMagnitude <= radius * radius)
                    s_refreshCandidates.Add(c);
            foreach (var c in s_refreshCandidates)
                if (c != null && c.isActiveAndEnabled)
                {
                    c.ScanAndLink();
                    c.RebuildVisuals();
                }
            s_refreshCandidates.Clear();
        }

        // ── Anchor ───────────────────────────────────────────────
        private void EnsureAnchor()
        {
            anchor = GetComponent<ConnectionAnchor>();
            if (anchor == null) anchor = gameObject.AddComponent<ConnectionAnchor>();
            anchor.networkType = NetworkType.Data;
        }

        // ── Endpoints (same shapes as the energy pipe) ───────────
        public List<EnergyPipeMeshBuilder.EndpointInfo> LocalEndpoints
            => EnergyPipeMeshBuilder.GetLocalEndpoints(variant, straightLength);

        public Vector3 EndpointWorld(EnergyPipeMeshBuilder.EndpointInfo ep)
            => transform.TransformPoint(ep.Position);

        public Vector3 EndpointNormalWorld(EnergyPipeMeshBuilder.EndpointInfo ep)
            => transform.TransformDirection(ep.Normal).normalized;

        // ── Neighbour scan + connect/disconnect ──────────────────
        private bool ScanAndLink()
        {
            if (anchor == null) EnsureAnchor();
            if (anchor == null) return false;
            bool changed = false;

            var desired = new HashSet<ConnectionAnchor>();
            var myEps = LocalEndpoints;

            // 1a) Other data pipes: plug meets plug. A legacy cardinal-
            //     adjacency fallback keeps pre-14.41 cube-pipe runs linked.
            foreach (var other in _AllCables)
            {
                if (other == null || other == this || other.anchor == null) continue;
                if (WrenchBlacklist.IsBlocked(this, other)) continue;

                bool mated = false;
                var otherEps = other.LocalEndpoints;
                for (int i = 0; i < myEps.Count && !mated; i++)
                {
                    Vector3 myWorld = EndpointWorld(myEps[i]);
                    for (int j = 0; j < otherEps.Count; j++)
                    {
                        if ((other.EndpointWorld(otherEps[j]) - myWorld).sqrMagnitude
                            > ENDPOINT_LINK_RADIUS * ENDPOINT_LINK_RADIUS) continue;
                        mated = true;
                        break;
                    }
                }
                if (!mated && IsStrictNeighbour(transform.position, other.transform.position)
                    && HasLineOfSight(transform.position, other.transform.position, other.anchor))
                    mated = true;

                if (mated) desired.Add(other.anchor);
            }

            // 1b) Storage devices: every plug head probes a small sphere just
            //     past its face. Devices get a Data-typed ConnectionAnchor
            //     synthesized on first contact, so storage blocks "just work"
            //     with pipes - no manual wrenching or asset wiring.
            for (int e = 0; e < myEps.Count; e++)
            {
                Vector3 epWorld = EndpointWorld(myEps[e]);
                Vector3 epNormal = EndpointNormalWorld(myEps[e]);
                Vector3 probeCenter = epWorld + epNormal * (DEVICE_PROBE_RADIUS * 0.5f);

                int count = Physics.OverlapSphereNonAlloc(probeCenter, DEVICE_PROBE_RADIUS,
                    s_overlapBuffer, ~0, QueryTriggerInteraction.Collide);
                for (int n = 0; n < count; n++)
                {
                    var h = s_overlapBuffer[n];
                    s_overlapBuffer[n] = null;
                    if (h == null || h.transform.IsChildOf(transform)) continue;
                    if (h.GetComponentInParent<DataCable>() != null) continue; // pipes handled above

                    var rootGo = h.transform.root.gameObject;
                    if (rootGo == gameObject) continue;

                    // Existing Data anchor on the device → reuse it.
                    var existing = h.GetComponentInParent<ConnectionAnchor>();
                    if (existing != null && existing.networkType != NetworkType.Data) existing = null;

                    if (existing == null)
                    {
                        if (!IsDataDevice(rootGo)) continue;
                        existing = rootGo.GetComponent<ConnectionAnchor>();
                        if (existing == null || existing.networkType != NetworkType.Data)
                        {
                            existing = rootGo.AddComponent<ConnectionAnchor>();
                            existing.networkType = NetworkType.Data;
                        }
                    }
                    if (existing == anchor) continue;
                    if (WrenchBlacklist.IsBlocked(gameObject, existing.gameObject)) continue;
                    if (!HasLineOfSight(epWorld, existing.transform.position, existing)) continue;

                    // Honour PortConfig face rules when the device has one.
                    var portConfig = existing.GetComponent<PortConfig>();
                    if (portConfig != null)
                    {
                        var match = portConfig.GetMatchingFace(epWorld, PortDirection.Input);
                        if (!match.HasValue) match = portConfig.GetMatchingFace(epWorld, PortDirection.Output);
                        if (!match.HasValue) continue;
                        if (!portConfig.AcceptsNetworkType(match.Value.face, NetworkType.Data)) continue;
                        _connectionFaces[existing] = match.Value.face;
                    }
                    desired.Add(existing);
                }
            }

            // 2) Drop stale connections.
            for (int i = anchor.connections.Count - 1; i >= 0; i--)
            {
                var c = anchor.connections[i];
                if (c == null) { anchor.connections.RemoveAt(i); changed = true; continue; }
                if (!desired.Contains(c)) { anchor.Disconnect(c); changed = true; }
            }

            // 3) Add new ones.
            foreach (var d in desired)
                if (!anchor.connections.Contains(d) && anchor.TryConnect(d))
                    changed = true;

            return changed;
        }

        private static bool IsDataDevice(GameObject rootGo)
        {
            return rootGo.GetComponent<Storage.ServerRack>()              != null ||
                   rootGo.GetComponent<Storage.StorageTerminal>()         != null ||
                   rootGo.GetComponent<Storage.StorageImporter>()         != null ||
                   rootGo.GetComponent<Storage.StorageExporter>()         != null ||
                   rootGo.GetComponent<Storage.NASBlock>()                != null ||
                   rootGo.GetComponent<Storage.DiskManipulator>()         != null ||
                   rootGo.GetComponent<Storage.PatternTerminal>()         != null ||
                   rootGo.GetComponent<Storage.CraftingTerminal>()        != null ||
                   rootGo.GetComponent<Storage.Powerstation>()            != null ||
                   rootGo.GetComponent<Storage.StorageDrawerController>() != null ||
                   rootGo.GetComponent<Storage.WirelessTransmitter>()     != null ||
                   rootGo.GetComponent<Storage.SecurityBlock>()           != null ||
                   rootGo.GetComponent<Storage.ExternalStorageBlock>()    != null ||
                   // 14.43.0: crafting stations plug into the storage network
                   // so patterns can demand them. InChildren: station prefabs
                   // may carry the component on a child, unlike storage blocks.
                   rootGo.GetComponentInChildren<Crafting.CraftingStation>(true) != null;
        }

        private bool IsStrictNeighbour(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float gs = gridSize > 0 ? gridSize : 1f;
            var myBlock = GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            if (myBlock != null && myBlock.Grid != null)
            {
                d = myBlock.Grid.transform.InverseTransformVector(d);
                gs = VoxelEngine.GridSystem.GridSizeExt.CellSize(VoxelEngine.GridSystem.GridSize.Small);
            }
            return PipeAdjacency.IsCardinalLinkDelta(
                d, gs, 1f, Mathf.Max(positionTolerance, gs * 0.12f));
        }

        private bool HasLineOfSight(Vector3 a, Vector3 b, ConnectionAnchor remoteAnchor)
        {
            Vector3 delta = b - a;
            float dist = delta.magnitude;
            if (dist < 0.001f) return true;
            Vector3 dir = delta / dist;
            const float SHRINK = 0.30f;
            float castDist = Mathf.Max(0f, dist - SHRINK * 2f);
            if (castDist <= 0f) return true;
            Vector3 origin = a + dir * SHRINK;

            int hitCount = Physics.RaycastNonAlloc(origin, dir, s_rayBuffer, castDist,
                losBlockingLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                var h = s_rayBuffer[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (remoteAnchor != null && h.collider.transform.IsChildOf(remoteAnchor.transform))
                    continue;
                return false;
            }
            return true;
        }

        // ── Visuals ──────────────────────────────────────────────
        public void RebuildVisuals()
        {
            EnsureVisualRoot();
            EnsureMaterials();

            // Auto-connect bridge arms (14.42.0): open plugs that sit against a
            // storage device grow a cable arm flush into its face - the data
            // twin of the energy pipe's machine bridges. Arms make the mesh
            // instance-specific, so only armless pipes use the shared cache.
            var bridgeTargets = CollectDeviceBridgeTargetsLocal();

            if (_instanceMesh != null)
            {
                if (Application.isPlaying) Destroy(_instanceMesh);
                else DestroyImmediate(_instanceMesh);
                _instanceMesh = null;
            }

            Mesh mesh;
            if (bridgeTargets == null || bridgeTargets.Count == 0)
                mesh = GetSharedMesh(variant, straightLength);
            else
            {
                _instanceMesh = DataPipeMeshBuilder.BuildMesh(variant, straightLength, bridgeTargets);
                mesh = _instanceMesh;
            }
            _meshFilter.sharedMesh = mesh;
            _meshRenderer.sharedMaterials = new[] { s_sheathMat, s_glowMat };
            _builtVariant = variant;
            _builtLength = straightLength;

            RebuildColliders();
        }

        /// <summary>Local-space device contact points for the visual bridge
        /// arms: every endpoint that is NOT plug-linked to another data pipe
        /// probes the same small sphere the link scan uses, and the closest
        /// facing device collider donates its contact point.</summary>
        private List<Vector3> CollectDeviceBridgeTargetsLocal()
        {
            // Ghost previews (disabled by StripGhost) never grow arms - the
            // ghost only rebuilds on selection change, so an arm sampled at
            // spawn position would stick to the preview as it moves.
            if (!isActiveAndEnabled) return null;

            List<Vector3> result = null;
            var eps = LocalEndpoints;
            for (int e = 0; e < eps.Count; e++)
            {
                Vector3 epWorld = EndpointWorld(eps[e]);
                Vector3 epNormal = EndpointNormalWorld(eps[e]);
                if (IsEndpointPlugLinked(epWorld)) continue;

                Vector3 probeCenter = epWorld + epNormal * (DEVICE_PROBE_RADIUS * 0.5f);
                int count = Physics.OverlapSphereNonAlloc(probeCenter, DEVICE_PROBE_RADIUS,
                    s_overlapBuffer, ~0, QueryTriggerInteraction.Collide);

                float bestSqr = float.MaxValue;
                Vector3 bestContact = default;
                for (int n = 0; n < count; n++)
                {
                    var h = s_overlapBuffer[n];
                    s_overlapBuffer[n] = null;
                    if (h == null || h.isTrigger || h.transform.IsChildOf(transform)) continue;
                    if (h.GetComponentInParent<DataCable>() != null) continue;

                    var rootGo = h.transform.root.gameObject;
                    if (rootGo == gameObject) continue;
                    var remoteAnchor = h.GetComponentInParent<ConnectionAnchor>();
                    bool isDevice = (remoteAnchor != null && remoteAnchor.networkType == NetworkType.Data)
                                    || IsDataDevice(rootGo);
                    if (!isDevice) continue;

                    Vector3 contact = h.ClosestPoint(epWorld);
                    Vector3 toContact = contact - epWorld;
                    float dSqr = toContact.sqrMagnitude;
                    // The arm must leave the plug roughly forward, never backward.
                    if (dSqr > 0.0004f && Vector3.Dot(toContact.normalized, epNormal) < -0.1f) continue;
                    if (dSqr >= bestSqr) continue;
                    bestSqr = dSqr;
                    bestContact = contact;
                }

                if (bestSqr < float.MaxValue)
                {
                    // Sink the arm tip slightly into the device face so the
                    // plug head reads as seated, not hovering.
                    Vector3 tip = bestContact + epNormal * 0.02f;
                    (result ??= new List<Vector3>()).Add(transform.InverseTransformPoint(tip));
                }
            }
            return result;
        }

        /// <summary>Is another data pipe's plug mated to this world-space endpoint?</summary>
        private bool IsEndpointPlugLinked(Vector3 epWorld)
        {
            foreach (var other in _AllCables)
            {
                if (other == null || other == this || !other.isActiveAndEnabled) continue;
                // Cheap reject: longest span is 5 m, link radius 0.6 m.
                if ((other.transform.position - epWorld).sqrMagnitude > 36f) continue;
                var otherEps = other.LocalEndpoints;
                for (int j = 0; j < otherEps.Count; j++)
                    if ((other.EndpointWorld(otherEps[j]) - epWorld).sqrMagnitude
                        <= ENDPOINT_LINK_RADIUS * ENDPOINT_LINK_RADIUS)
                        return true;
            }
            return false;
        }

        private void EnsureVisualRoot()
        {
            if (_visualRoot == null)
            {
                var existing = transform.Find("CableVisuals");
                if (existing != null) _visualRoot = existing;
                else
                {
                    var go = new GameObject("CableVisuals");
                    _visualRoot = go.transform;
                    _visualRoot.SetParent(transform, worldPositionStays: false);
                }
            }
            // Hide any pre-baked prefab meshes (ghost core etc.) - the pipe
            // draws itself.
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
                if (r.transform != transform && !r.transform.IsChildOf(_visualRoot))
                    r.enabled = false;

            if (_meshFilter == null)
            {
                _meshFilter = _visualRoot.GetComponent<MeshFilter>();
                if (_meshFilter == null) _meshFilter = _visualRoot.gameObject.AddComponent<MeshFilter>();
            }
            if (_meshRenderer == null)
            {
                _meshRenderer = _visualRoot.GetComponent<MeshRenderer>();
                if (_meshRenderer == null) _meshRenderer = _visualRoot.gameObject.AddComponent<MeshRenderer>();
            }
        }

        private void EnsureMaterials()
        {
            if (s_sheathMat == null)
            {
                s_sheathMat = GridCableVisuals.CreateTintedMaterial(sheathColor, "DataPipe_Sheath");
                if (s_sheathMat.HasProperty("_Metallic")) s_sheathMat.SetFloat("_Metallic", 0.35f);
                if (s_sheathMat.HasProperty("_Smoothness")) s_sheathMat.SetFloat("_Smoothness", 0.62f);
            }
            if (s_glowMat == null)
            {
                s_glowMat = GridCableVisuals.CreateTintedMaterial(glowColor, "DataPipe_Glow");
                s_glowMat.EnableKeyword("_EMISSION");
                if (s_glowMat.HasProperty("_EmissionColor"))
                    s_glowMat.SetColor("_EmissionColor", glowColor * 2.2f);
            }
        }

        private static Mesh GetSharedMesh(EnergyPipeVariant v, int len)
        {
            len = Mathf.Clamp(len, 1, 5);
            var key = (v, len);
            if (s_meshCache.TryGetValue(key, out var m) && m != null) return m;
            m = DataPipeMeshBuilder.BuildMesh(v, len);
            s_meshCache[key] = m;
            return m;
        }

        /// <summary>Box colliders along every straight span of the centreline,
        /// so long and bent pipes can be aimed at, wrenched and broken
        /// anywhere - and so the storage network's touching-AABB rule sees
        /// the pipe's real extent.</summary>
        private void RebuildColliders()
        {
            // Clear previous generated colliders.
            for (int i = _visualRoot.childCount - 1; i >= 0; i--)
            {
                var child = _visualRoot.GetChild(i);
                if (child.name.StartsWith("Generated_PipeCol", System.StringComparison.Ordinal))
                    DestroyImmediate(child.gameObject);
            }

            const float THICK = 0.18f;
            var segs = DataPipeMeshBuilder.GetColliderSegments(variant, straightLength);
            for (int i = 0; i < segs.Count; i++)
            {
                var (a, b) = segs[i];
                Vector3 mid = (a + b) * 0.5f;
                float len = (b - a).magnitude;
                if (len < 0.05f) continue;
                var go = new GameObject($"Generated_PipeCol_{i}");
                go.layer = gameObject.layer;
                go.transform.SetParent(_visualRoot, false);
                go.transform.localPosition = mid;
                go.transform.localRotation = Quaternion.LookRotation(
                    (b - a).normalized, Mathf.Abs(Vector3.Dot((b - a).normalized, Vector3.up)) > 0.9f
                        ? Vector3.forward : Vector3.up);
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(THICK, THICK, len + THICK * 0.5f);
            }

            // The prefab root's legacy 0.38 cube collider only matches the old
            // cube pipe at the origin; shrink it to the hub/plug so it stops
            // bulging out of slim straight runs. (Disabling it entirely would
            // orphan older prefabs that rely on a root collider existing.)
            var rootCol = GetComponent<BoxCollider>();
            if (rootCol != null)
            {
                rootCol.center = Vector3.zero;
                rootCol.size = Vector3.one * 0.22f;
            }
        }
    }
}
