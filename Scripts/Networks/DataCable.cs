// Assets/Scripts/VoxelEngine/Networks/DataCable.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║                    DATA CABLE — wired ItemNet                   ║
// ║   Carries connectivity (no per-tick balancing) between Server   ║
// ║   Racks, Storage Terminals, Importers and Exporters. Snaps      ║
// ║   onto the 1 m build grid and auto-links to ±X/±Y/±Z adjacent   ║
// ║   data devices with an unobstructed line of sight.              ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Transport;

namespace VoxelEngine.Networks
{
    [DisallowMultipleComponent]
    public class DataCable : MonoBehaviour
    {
        [Header("Grid")]
        [Tooltip("Build grid size used to detect cardinal neighbours.")]
        public float gridSize = 1f;
        [Tooltip("Distance tolerance when looking for neighbours one grid step away.")]
        public float positionTolerance = 0.15f;
        [Tooltip("Layers tested with a linecast to detect solid blocks between two cables. " +
                 "A hit on these layers (excluding the cables themselves) blocks the link.")]
        public LayerMask losBlockingLayers = ~0;

        [Header("Visual")]
        [Range(0.1f, 0.9f)] public float coreSize     = 0.35f;
        [Range(0.05f, 0.6f)] public float armThickness = 0.28f;
        public Color tint = new(0.30f, 0.85f, 0.40f, 1f);
        public bool  showUnusedFaceCaps = false;

        // ── Runtime ──────────────────────────────────────────────
        public ConnectionAnchor anchor;          // exposed for inspectors / debugging
        private Transform _visualRoot;
        private Material  _material;
        private readonly List<Vector3> _neighbourPositionsBuf = new(6);
        private float _scanTimer;
        private const float SCAN_INTERVAL = 0.5f;   // re-evaluate twice/second

        // Track which face each connection uses
        private readonly Dictionary<ConnectionAnchor, CubeFace> _connectionFaces = new();

        // Cached, shared registry — every DataCable adds itself so neighbour lookups
        // are O(k) instead of FindObjectsOfType every frame.
        private static readonly HashSet<DataCable> _AllCables = new();

        // Shared non-alloc physics buffers — every cable probes every 0.5 s and the
        // allocating Overlap/Raycast variants were the cable-count GC spike source.
        private static readonly Collider[]   s_overlapBuffer = new Collider[128];
        private static readonly RaycastHit[] s_rayBuffer     = new RaycastHit[48];

        private void Awake()
        {
            EnsureAnchor();
            EnsureVisualRoot();
            EnsureMaterial();
            // Default mask: hit everything EXCEPT Ignore-Raycast (Unity layer 2).
            if (losBlockingLayers == ~0) losBlockingLayers = ~(1 << 2);
        }

        private void OnEnable()
        {
            _AllCables.Add(this);
            // Force one immediate scan so a freshly placed cable connects without delay.
            ScanAndLink();
            RebuildVisuals();
        }

        private void OnDisable()
        {
            _AllCables.Remove(this);
            _connectionFaces.Clear();
            // Notify neighbours so they drop their arms next rebuild.
            if (anchor != null) anchor.DisconnectAll();
            // Adjacent cables need to know they just lost a neighbour.
            foreach (var c in _AllCables)
                if (c != null && IsCardinalNeighbour(transform.position, c.transform.position))
                    c.RebuildVisuals();
            // Force a visual rebuild so this cable's own arms disappear immediately.
            // When anchor has no connections, RebuildVisuals clears all arms.
            RebuildVisuals();
        }

        private void Update()
        {
            _scanTimer += Time.deltaTime;
            if (_scanTimer < SCAN_INTERVAL) return;
            _scanTimer = 0f;
            if (ScanAndLink()) RebuildVisuals();
        }

        // ── Anchor / Visual setup ────────────────────────────────
        private void EnsureAnchor()
        {
            anchor = GetComponent<ConnectionAnchor>();
            if (anchor == null) anchor = gameObject.AddComponent<ConnectionAnchor>();
            anchor.networkType = NetworkType.Data;
        }

        private void EnsureVisualRoot()
        {
            if (_visualRoot != null) return;
            // Hide any pre-existing prefab meshes so we render arms ourselves.
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
                if (r.transform != transform) r.enabled = false;

            var go = new GameObject("CableVisuals");
            _visualRoot = go.transform;
            _visualRoot.SetParent(transform, worldPositionStays: false);
        }

        private void EnsureMaterial()
        {
            if (_material == null)
                _material = GridCableVisuals.CreateTintedMaterial(tint, $"{name}_DataMat");
        }

        // ── Neighbour scan + connect/disconnect ──────────────────
        /// <summary>
        /// Walks the registry of placed data cables (and ConnectionAnchors) for ±1 grid
        /// step cardinal neighbours, performs an LOS check, and updates the anchor's
        /// connection list. Returns true if anything changed (so the visual can rebuild).
        /// </summary>
        private bool ScanAndLink()
        {
            if (anchor == null) return false;
            bool changed = false;

            // 1) Build set of currently desired neighbour anchors.
            var desired = new HashSet<ConnectionAnchor>();

            // 1a) Other DataCables.
            foreach (var other in _AllCables)
            {
                if (other == null || other == this) continue;
                if (!IsCardinalNeighbour(transform.position, other.transform.position)) continue;
                if (!HasLineOfSight(transform.position, other.transform.position, other.anchor)) continue;
                // Wrench blacklist — honour explicit player disconnects.
                if (WrenchBlacklist.IsBlocked(this, other)) continue;
                if (other.anchor != null) desired.Add(other.anchor);
            }

            // 1b) Storage devices (Server Rack, Storage Terminal, Importer/Exporter,
            //     NAS Block) — search a generous 2-grid radius and auto-spawn a
            //     Data-typed ConnectionAnchor on the device if one doesn't exist.
            //     This means storage blocks "just work" with cables without any
            //     manual wrenching or asset wiring.
            float gs = gridSize > 0 ? gridSize : 1f;
            float probeRange = gs * 2.5f;
            Vector3 self = transform.position;
            int nearbyCount = Physics.OverlapBoxNonAlloc(self,
                Vector3.one * probeRange, s_overlapBuffer, Quaternion.identity, ~0,
                QueryTriggerInteraction.Collide);

            for (int nIdx = 0; nIdx < nearbyCount; nIdx++)
            {
                var h = s_overlapBuffer[nIdx];
                if (h == null) continue;
                if (h.transform.IsChildOf(transform)) continue;  // ignore self

                // Already has a Data anchor → use it.
                var existing = h.GetComponentInParent<ConnectionAnchor>();
                if (existing != null && existing.networkType == NetworkType.Data
                    && existing != anchor)
                {
                    if (!HasLineOfSight(self, existing.transform.position, existing)) continue;
                    // Anti-redundancy: if another cable is closer on the same axis, skip.
                    if (IsConnectionShadowed(self, existing.transform.position)) continue;
                    // Wrench blacklist — explicit player disconnect persists.
                    if (WrenchBlacklist.IsBlocked(gameObject, existing.gameObject)) continue;
                    
                    // Check PortConfig if the device has one
                    var portConfig = existing.GetComponent<PortConfig>();
                    if (portConfig != null)
                    {
                        var match = portConfig.GetMatchingFace(self, PortDirection.Input);
                        if (!match.HasValue) match = portConfig.GetMatchingFace(self, PortDirection.Output);
                        if (!match.HasValue) continue;
                        if (!portConfig.AcceptsNetworkType(match.Value.face, NetworkType.Data)) continue;
                        
                        // Record which face this connection uses
                        _connectionFaces[existing] = match.Value.face;
                    }
                    
                    desired.Add(existing);
                    continue;
                }

                // Find a storage device on the root GameObject and synthesize an anchor.
                var rootGo = h.transform.root.gameObject;
                if (rootGo == gameObject) continue;
                bool isDataDevice =
                    rootGo.GetComponent<Storage.ServerRack>()       != null ||
                    rootGo.GetComponent<Storage.StorageTerminal>()  != null ||
                    rootGo.GetComponent<Storage.StorageImporter>()  != null ||
                    rootGo.GetComponent<Storage.StorageExporter>()  != null ||
                    rootGo.GetComponent<Storage.NASBlock>()         != null ||
                    rootGo.GetComponent<Storage.DiskManipulator>()  != null ||
                    rootGo.GetComponent<Storage.PatternTerminal>()  != null ||
                    rootGo.GetComponent<Storage.CraftingTerminal>() != null;
                if (!isDataDevice) continue;

                var newAnchor = rootGo.AddComponent<ConnectionAnchor>();
                newAnchor.networkType = NetworkType.Data;
                if (!HasLineOfSight(self, newAnchor.transform.position, newAnchor)) continue;
                // Anti-redundancy: if another cable is closer on the same axis, skip.
                if (IsConnectionShadowed(self, newAnchor.transform.position)) continue;
                // Wrench blacklist — honour explicit player disconnects.
                if (WrenchBlacklist.IsBlocked(gameObject, newAnchor.gameObject)) continue;

                // Check PortConfig if the device has one. Renamed from `portConfig`
                // to avoid C# scope-collision with the identically-named local in the
                // earlier `if (existing != null)` branch (the C# 9+ scope rules treat
                // both branches as one enclosing scope inside the foreach body).
                var newPortConfig = newAnchor.GetComponent<PortConfig>();
                if (newPortConfig != null)
                {
                    var match = newPortConfig.GetMatchingFace(self, PortDirection.Input);
                    if (!match.HasValue) match = newPortConfig.GetMatchingFace(self, PortDirection.Output);
                    if (!match.HasValue) continue;
                    if (!newPortConfig.AcceptsNetworkType(match.Value.face, NetworkType.Data)) continue;

                    // Record which face this connection uses
                    _connectionFaces[newAnchor] = match.Value.face;
                }

                desired.Add(newAnchor);
            }

            // 2) Drop connections that are no longer desired.
            for (int i = anchor.connections.Count - 1; i >= 0; i--)
            {
                var c = anchor.connections[i];
                if (c == null) { anchor.connections.RemoveAt(i); changed = true; continue; }
                if (!desired.Contains(c)) { anchor.Disconnect(c); changed = true; }
            }

            // 3) Add new connections.
            foreach (var d in desired)
                if (!anchor.connections.Contains(d) && anchor.TryConnect(d))
                    changed = true;

            return changed;
        }

        /// <summary>
        /// Checks if there's another DataCable closer to the same target on the same axis.
        /// If so, this cable's connection to that target is "shadowed" and should be skipped.
        /// </summary>
        private bool IsConnectionShadowed(Vector3 myPos, Vector3 targetPos)
        {
            Vector3 delta = targetPos - myPos;
            Vector3 axisDir = NearestAxis(delta);
            float myDist = delta.magnitude;

            foreach (var other in _AllCables)
            {
                if (other == null || other == this) continue;

                Vector3 otherPos = other.transform.position;
                Vector3 otherDelta = targetPos - otherPos;

                // Check if other is on the same axis direction toward target
                if (Vector3.Dot(otherDelta, axisDir) <= 0) continue; // not toward target
                if (Vector3.Dot(otherDelta, axisDir) >= Vector3.Dot(delta, axisDir)) continue; // not closer

                // Check axis alignment
                Vector3 otherAxis = NearestAxis(otherDelta);
                if (otherAxis != axisDir) continue;

                // Other is closer on the same axis — this connection is shadowed
                return true;
            }

            return false;
        }

        private Vector3 NearestAxis(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0, 0);
            if (ay >= ax && ay >= az) return new Vector3(0, Mathf.Sign(v.y), 0);
            return new Vector3(0, 0, Mathf.Sign(v.z));
        }

        private bool IsCardinalNeighbour(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float gs = gridSize > 0 ? gridSize : 1f;
            float dx = Mathf.Abs(d.x), dy = Mathf.Abs(d.y), dz = Mathf.Abs(d.z);
            float tol = positionTolerance;
            int oneStepAxes = 0;
            if (Mathf.Abs(dx - gs) < tol) oneStepAxes++; else if (dx > tol) return false;
            if (Mathf.Abs(dy - gs) < tol) oneStepAxes++; else if (dy > tol) return false;
            if (Mathf.Abs(dz - gs) < tol) oneStepAxes++; else if (dz > tol) return false;
            return oneStepAxes == 1;
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
                // Ignore ourselves.
                if (h.collider.transform.IsChildOf(transform)) continue;
                // Ignore the remote endpoint (its own collider counts as "us" from its POV).
                if (remoteAnchor != null && h.collider.transform.IsChildOf(remoteAnchor.transform))
                    continue;
                // A solid object blocks the connection.
                return false;
            }
            return true;
        }

        // ── Visuals ──────────────────────────────────────────────
        public void RebuildVisuals()
        {
            EnsureVisualRoot();
            EnsureMaterial();
            _neighbourPositionsBuf.Clear();
            if (anchor != null)
            {
                // Pass real neighbour positions; the helper snaps to the nearest
                // face and grows arms to actually meet the device (works for
                // big multi-voxel server racks placed beside the cable).
                foreach (var c in anchor.connections)
                {
                    if (c == null) continue;
                    
                    // If we have a recorded face for this connection, use the face point
                    if (_connectionFaces.TryGetValue(c, out var face))
                    {
                        var portConfig = c.GetComponent<PortConfig>();
                        if (portConfig != null)
                        {
                            _neighbourPositionsBuf.Add(portConfig.FaceWorldPoint(face));
                            continue;
                        }
                    }
                    
                    _neighbourPositionsBuf.Add(c.transform.position);
                }
            }
            GridCableVisuals.Rebuild(
                _visualRoot,
                transform.position,
                _neighbourPositionsBuf,
                gridSize > 0 ? gridSize : 1f,
                coreSize,
                armThickness,
                _material,
                showUnusedFaceCaps);
        }
    }
}
