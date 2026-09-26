// Assets/Scripts/VoxelEngine/Power/PowerCable.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Networks;
using VoxelEngine.Transport;

namespace VoxelEngine.Power
{
    /// <summary>
    /// Industrial energy pipe / cable. Carries electrical power across networks.
    /// Supports multiple shape variants (Straight 1-5m, 90° bends, S-curves, vertical steps, 4-way/6-way junctions)
    /// selected via the EnergyPipeShapeWheel. Finite tiers (Copper, Iron, Gold) overload and explode
    /// if throughput exceeds their rated capacity.
    /// </summary>
    public class PowerCable : PowerNode
    {
        public override PowerNodeKind Kind => PowerNodeKind.Cable;

        [Header("Tier")]
        public ElectricalPipeDefinition wire;

        [Header("Shape Variant")]
        public EnergyPipeVariant variant = EnergyPipeVariant.Straight;
        [Range(1, 5)] public int straightLength = 1;

        [Header("Visual")]
        [Tooltip("Edge length of the central cable hub cube, in metres.")]
        [Range(0.1f, 0.9f)] public float coreSize = 0.35f;
        [Tooltip("Thickness (width × height) of each arm extending toward a neighbour.")]
        [Range(0.05f, 0.6f)] public float armThickness = 0.28f;
        public bool showUnusedFaceCaps = false;

        // ── Internals ─────────────────────────────────────────────
        private Transform _visualRoot;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private GameObject _superEnergyBeam;
        private bool _isOverloading;

        // One connection tolerance governs both topology and visuals. A cable link
        // must never carry power farther than the corresponding socket is treated
        // as occupied by the mesh.
        private const float EndpointLinkRadius = 0.85f;
        private const float EndpointLinkRadiusSqr = EndpointLinkRadius * EndpointLinkRadius;
        private const float MachineBridgeRadius = 2.0f;
        private const float MachineBridgeRadiusSqr = MachineBridgeRadius * MachineBridgeRadius;
        private const float DirectContactSqrEpsilon = 0.005f;
        private const float EndpointForwardDotMinimum = 0.15f;
        private const float SocketNormalOppositionMaximum = -0.15f;

        private static readonly Collider[] s_endpointTouchProbe = new Collider[32];
        private static readonly HashSet<PowerCable> s_refreshCandidates = new();

        // Track which face each neighbour is connected through
        private readonly Dictionary<PowerNode, CubeFace> _neighbourFaces = new();

        protected override void OnEnable()
        {
            float gs = gridSize > 0 ? gridSize : 1f;
            connectRadius = gs * Mathf.Max(6.0f, straightLength * 2.0f);

            requireGridAlignedNeighbours = false;
            connectionBlockingLayers = ~(1 << 2);

            base.OnEnable();

            // Hide legacy pre-baked prefab renderers
            var existingRenderers = GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in existingRenderers)
            {
                if (r.transform != transform && (_visualRoot == null || !r.transform.IsChildOf(_visualRoot)))
                    r.enabled = false;
            }

            EnsureVisualRoot();
            onNeighboursChanged += RebuildVisuals;
            RebuildVisuals();
        }

        protected override void OnDisable()
        {
            onNeighboursChanged -= RebuildVisuals;
            _neighbourFaces.Clear();
            base.OnDisable();
            RefreshNearbyCables(transform.position, 5f);
        }

        public static void RefreshNearbyCables(Vector3 center, float radius = 5f)
        {
            // Collect first, then rebuild. RebuildVisuals also uses the shared physics
            // probe, so rebuilding inside this enumeration could overwrite unread
            // entries and leave a neighbouring conduit stale.
            s_refreshCandidates.Clear();
            int count = Physics.OverlapSphereNonAlloc(center, radius, s_endpointTouchProbe, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var col = s_endpointTouchProbe[i];
                s_endpointTouchProbe[i] = null;
                var cable = col != null ? col.GetComponentInParent<PowerCable>() : null;
                if (cable != null && cable.isActiveAndEnabled)
                    s_refreshCandidates.Add(cable);
            }

            foreach (var cable in s_refreshCandidates)
                if (cable != null && cable.isActiveAndEnabled)
                    cable.RebuildVisuals();
            s_refreshCandidates.Clear();
        }

        public override bool CanLinkTo(PowerNode other)
        {
            if (other == null || other == this) return false;

            if (other is PowerCable otherCable)
                return HasAvailableCompatibleSocket(otherCable);

            // A machine edge uses the same open, outward-facing endpoint rule as
            // the visual bridge. An occupied or rear-facing socket cannot become an
            // invisible second power path through a nearby machine collider.
            return TryGetOpenMachineContact(other, out _);
        }

        /// <summary>
        /// Finds the closest valid machine surface reached by an unoccupied pipe
        /// socket. SurfacePowerTap uses this after load so its manual edge and the
        /// automatic topology obey the same physical endpoint contract.
        /// </summary>
        public bool TryGetOpenMachineContact(PowerNode other, out Vector3 contactWorld)
        {
            contactWorld = default;
            if (other == null || other == this || other is PowerCable) return false;

            var colliders = other.GetComponentsInChildren<Collider>(true);
            if (colliders == null || colliders.Length == 0)
                return TryGetOpenEndpointNearPoint(other.transform.position, out contactWorld);

            float bestDistanceSqr = float.MaxValue;
            var endpoints = EnergyPipeMeshBuilder.GetLocalEndpoints(variant, straightLength);
            for (int e = 0; e < endpoints.Count; e++)
            {
                Vector3 endpointWorld = transform.TransformPoint(endpoints[e].Position);
                Vector3 endpointNormalWorld = transform.TransformDirection(endpoints[e].Normal).normalized;
                if (HasCableSocketOccupant(endpointWorld, endpointNormalWorld, null)) continue;

                for (int c = 0; c < colliders.Length; c++)
                {
                    var collider = colliders[c];
                    if (collider == null || !collider.enabled || collider.isTrigger) continue;

                    Vector3 surface = collider.ClosestPoint(endpointWorld);
                    float distanceSqr = (surface - endpointWorld).sqrMagnitude;
                    if (!IsEndpointFacingContact(endpointWorld, endpointNormalWorld, surface, distanceSqr)
                        || distanceSqr >= bestDistanceSqr) continue;

                    bestDistanceSqr = distanceSqr;
                    contactWorld = surface;
                }
            }
            return bestDistanceSqr < float.MaxValue;
        }

        private bool HasAvailableCompatibleSocket(PowerCable otherCable)
        {
            if (otherCable == null || otherCable == this || !otherCable.isActiveAndEnabled) return false;

            var myEndpoints = EnergyPipeMeshBuilder.GetLocalEndpoints(variant, straightLength);
            var otherEndpoints = EnergyPipeMeshBuilder.GetLocalEndpoints(otherCable.variant, otherCable.straightLength);
            for (int i = 0; i < myEndpoints.Count; i++)
            {
                Vector3 myWorld = transform.TransformPoint(myEndpoints[i].Position);
                Vector3 myNormal = transform.TransformDirection(myEndpoints[i].Normal).normalized;
                for (int j = 0; j < otherEndpoints.Count; j++)
                {
                    Vector3 otherWorld = otherCable.transform.TransformPoint(otherEndpoints[j].Position);
                    Vector3 otherNormal = otherCable.transform.TransformDirection(otherEndpoints[j].Normal).normalized;
                    if (!AreSocketEndpointsCompatible(myWorld, myNormal, otherWorld, otherNormal)) continue;

                    // One pipe socket accepts one matching socket. The candidate
                    // being evaluated is ignored; every third cable is a real clash.
                    if (HasCableSocketOccupant(myWorld, myNormal, otherCable)) continue;
                    if (otherCable.HasCableSocketOccupant(otherWorld, otherNormal, this)) continue;
                    return true;
                }
            }
            return false;
        }

        private bool TryGetOpenEndpointNearPoint(Vector3 targetPosition, out Vector3 contactWorld)
        {
            contactWorld = default;
            float bestDistanceSqr = float.MaxValue;
            var endpoints = EnergyPipeMeshBuilder.GetLocalEndpoints(variant, straightLength);
            for (int i = 0; i < endpoints.Count; i++)
            {
                Vector3 endpointWorld = transform.TransformPoint(endpoints[i].Position);
                Vector3 endpointNormalWorld = transform.TransformDirection(endpoints[i].Normal).normalized;
                if (HasCableSocketOccupant(endpointWorld, endpointNormalWorld, null)) continue;

                float distanceSqr = (targetPosition - endpointWorld).sqrMagnitude;
                if (!IsEndpointFacingContact(endpointWorld, endpointNormalWorld, targetPosition, distanceSqr)
                    || distanceSqr >= bestDistanceSqr) continue;

                bestDistanceSqr = distanceSqr;
                contactWorld = targetPosition;
            }
            return bestDistanceSqr < float.MaxValue;
        }

        private bool HasCableSocketOccupant(Vector3 endpointWorld, Vector3 endpointNormalWorld,
            PowerCable allowedCable)
        {
            int count = Physics.OverlapSphereNonAlloc(endpointWorld, EndpointLinkRadius,
                s_endpointTouchProbe, ~0, QueryTriggerInteraction.Ignore);
            if (ContainsCableSocketOccupant(s_endpointTouchProbe, count, true, endpointWorld,
                    endpointNormalWorld, allowedCable))
                return true;
            if (count < s_endpointTouchProbe.Length) return false;

            // Preserve correctness in unusually dense builds where the reusable
            // non-alloc probe filled completely.
            var overflow = Physics.OverlapSphere(endpointWorld, EndpointLinkRadius, ~0,
                QueryTriggerInteraction.Ignore);
            return ContainsCableSocketOccupant(overflow, overflow.Length, false, endpointWorld,
                endpointNormalWorld, allowedCable);
        }

        private bool ContainsCableSocketOccupant(Collider[] colliders, int count, bool clearProbe,
            Vector3 endpointWorld, Vector3 endpointNormalWorld, PowerCable allowedCable)
        {
            int limit = Mathf.Min(count, colliders.Length);
            for (int i = 0; i < limit; i++)
            {
                var collider = colliders[i];
                if (clearProbe) colliders[i] = null;
                if (collider == null) continue;

                var cable = collider.GetComponentInParent<PowerCable>();
                if (cable == null || cable == this || cable == allowedCable || !cable.isActiveAndEnabled)
                    continue;

                var otherEndpoints = EnergyPipeMeshBuilder.GetLocalEndpoints(cable.variant, cable.straightLength);
                for (int e = 0; e < otherEndpoints.Count; e++)
                {
                    Vector3 otherWorld = cable.transform.TransformPoint(otherEndpoints[e].Position);
                    Vector3 otherNormal = cable.transform.TransformDirection(otherEndpoints[e].Normal).normalized;
                    if (AreSocketEndpointsCompatible(endpointWorld, endpointNormalWorld,
                            otherWorld, otherNormal))
                        return true;
                }
            }
            return false;
        }

        private static bool AreSocketEndpointsCompatible(Vector3 firstPosition, Vector3 firstNormal,
            Vector3 secondPosition, Vector3 secondNormal)
        {
            return (firstPosition - secondPosition).sqrMagnitude <= EndpointLinkRadiusSqr
                && Vector3.Dot(firstNormal, secondNormal) <= SocketNormalOppositionMaximum;
        }

        private static bool IsEndpointFacingContact(Vector3 endpointWorld, Vector3 endpointNormalWorld,
            Vector3 contactWorld, float distanceSqr)
        {
            if (distanceSqr <= DirectContactSqrEpsilon) return true;
            if (distanceSqr > MachineBridgeRadiusSqr) return false;
            return Vector3.Dot((contactWorld - endpointWorld).normalized, endpointNormalWorld)
                > EndpointForwardDotMinimum;
        }

        public void RecordConnectionFace(PowerNode target, CubeFace face)
        {
            _neighbourFaces[target] = face;
        }

        // ── Visual construction ──────────────────────────────────
        private void EnsureVisualRoot()
        {
            if (_visualRoot != null) return;
            var go = new GameObject("CableVisuals");
            _visualRoot = go.transform;
            _visualRoot.SetParent(transform, worldPositionStays: false);
            _meshFilter = go.AddComponent<MeshFilter>();
            _meshRenderer = go.AddComponent<MeshRenderer>();
        }

        public void RebuildVisuals()
        {
            EnsureVisualRoot();

            // Collect machine connection points to bridge visual gaps flush into machine surfaces
            List<Vector3> machineTargetsLocal = null;
            var myEndpoints = EnergyPipeMeshBuilder.GetLocalEndpoints(variant, straightLength);

            for (int e = 0; e < myEndpoints.Count; e++)
            {
                Vector3 endpointWorld = transform.TransformPoint(myEndpoints[e].Position);
                Vector3 endpointNormalWorld = transform.TransformDirection(myEndpoints[e].Normal).normalized;

                // The mesh uses the exact same compatible-socket tolerance as the
                // network graph. A linked socket cannot also grow a machine arm.
                if (HasCableSocketOccupant(endpointWorld, endpointNormalWorld, null)) continue;
                if (!TryFindVisualMachineContact(endpointWorld, endpointNormalWorld,
                        out Vector3 contactWorld)) continue;

                if (machineTargetsLocal == null) machineTargetsLocal = new List<Vector3>();
                machineTargetsLocal.Add(transform.InverseTransformPoint(contactWorld));
            }

            // Build procedural dual-conduit mesh for active variant & length
            var mesh = EnergyPipeMeshBuilder.BuildMesh(variant, straightLength, machineTargetsLocal);
            _meshFilter.sharedMesh = mesh;

            string tierName = wire != null ? wire.displayName : "Copper";
            Color tint = wire != null ? wire.tint : new Color(0.85f, 0.45f, 0.20f, 1f);
            var mat = EnergyPipeMeshBuilder.GetMaterialForTier(tierName, tint);
            _meshRenderer.sharedMaterial = mat;

            // Superconductor animated purple energy line
            bool isSuper = tierName != null && tierName.ToLowerInvariant().Contains("super");
            if (isSuper && _superEnergyBeam == null)
            {
                _superEnergyBeam = new GameObject("SuperconductorEnergyBeam");
                _superEnergyBeam.transform.SetParent(_visualRoot, worldPositionStays: false);
                var beamFilter = _superEnergyBeam.AddComponent<MeshFilter>();
                var beamRenderer = _superEnergyBeam.AddComponent<MeshRenderer>();
                var beamMesh = EnergyPipeMeshBuilder.BuildMesh(variant, straightLength);
                beamFilter.sharedMesh = beamMesh;

                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var beamMat = new Material(sh) { name = "SuperEnergyBeamMat" };
                beamMat.color = new Color(0.75f, 0.25f, 1.0f, 1f);
                if (beamMat.HasProperty("_BaseColor")) beamMat.SetColor("_BaseColor", beamMat.color);
                beamMat.EnableKeyword("_EMISSION");
                beamMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                beamMat.SetColor("_EmissionColor", new Color(4.5f, 1.5f, 6.0f));
                beamRenderer.sharedMaterial = beamMat;
                _superEnergyBeam.transform.localScale = Vector3.one * 0.45f;
            }
            else if (!isSuper && _superEnergyBeam != null)
            {
                Destroy(_superEnergyBeam);
                _superEnergyBeam = null;
            }

            // Adjust BoxCollider to tightly encapsulate active shape variant and length
            if (TryGetComponent<BoxCollider>(out var box) && mesh != null)
            {
                box.center = mesh.bounds.center;
                box.size = Vector3.Max(mesh.bounds.size, new Vector3(0.25f, 0.25f, 0.25f));
            }
        }

        private bool TryFindVisualMachineContact(Vector3 endpointWorld, Vector3 endpointNormalWorld,
            out Vector3 contactWorld)
        {
            contactWorld = default;
            float bestDistanceSqr = float.MaxValue;
            int count = Physics.OverlapSphereNonAlloc(endpointWorld, MachineBridgeRadius,
                s_endpointTouchProbe, ~0, QueryTriggerInteraction.Ignore);
            EvaluateVisualMachineContacts(s_endpointTouchProbe, count, true, endpointWorld,
                endpointNormalWorld, ref bestDistanceSqr, ref contactWorld);

            if (count == s_endpointTouchProbe.Length)
            {
                // A dense factory can fill the reusable probe. The allocating path
                // is rare, but keeps the closest legitimate machine arm deterministic.
                var overflow = Physics.OverlapSphere(endpointWorld, MachineBridgeRadius, ~0,
                    QueryTriggerInteraction.Ignore);
                EvaluateVisualMachineContacts(overflow, overflow.Length, false, endpointWorld,
                    endpointNormalWorld, ref bestDistanceSqr, ref contactWorld);
            }
            return bestDistanceSqr < float.MaxValue;
        }

        private void EvaluateVisualMachineContacts(Collider[] colliders, int count, bool clearProbe,
            Vector3 endpointWorld, Vector3 endpointNormalWorld, ref float bestDistanceSqr,
            ref Vector3 bestContactWorld)
        {
            int limit = Mathf.Min(count, colliders.Length);
            for (int i = 0; i < limit; i++)
            {
                var collider = colliders[i];
                if (clearProbe) colliders[i] = null;
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                if (collider.transform == transform || collider.transform.IsChildOf(transform)) continue;

                var node = collider.GetComponentInParent<PowerNode>();
                var placed = collider.GetComponentInParent<VoxelEngine.Building.PlacedBlock>();
                if (node is PowerCable || (node == null && placed == null)) continue;

                Vector3 contact = collider.ClosestPoint(endpointWorld);
                float distanceSqr = (contact - endpointWorld).sqrMagnitude;
                if (!IsEndpointFacingContact(endpointWorld, endpointNormalWorld, contact, distanceSqr)
                    || distanceSqr >= bestDistanceSqr) continue;

                bestDistanceSqr = distanceSqr;
                bestContactWorld = contact;
            }
        }

        /// <summary>
        /// Triggered when carried power exceeds this pipe's rated capacityWatts.
        /// Explodes with fiery flash, sparks, and burns red-hot for 2s before destruction.
        /// </summary>
        public void TriggerOverload(float throughWatts)
        {
            if (_isOverloading || !isActiveAndEnabled) return;
            _isOverloading = true;

            const float BurnSeconds = 2.0f;
            var heat = gameObject.GetComponent<OverheatedPowerCable>() ?? gameObject.AddComponent<OverheatedPowerCable>();
            heat.Begin(BurnSeconds);

            var flash = new GameObject("EnergyPipeOverloadFlash");
            flash.transform.position = transform.position;
            var light = flash.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.25f, 0.05f, 1f);
            light.intensity = 18f;
            light.range = 8f;
            Destroy(flash, 0.25f);

            float cap = wire != null ? wire.capacityWatts : 1000f;
            string tier = wire != null ? wire.displayName : "Conduit";
            Debug.LogWarning($"[Power] Energy Pipe overload: {throughWatts:0} W exceeds {cap:0} W rating ({tier}). Conduit burned and destroyed.", this);
            PowerNetworkManager.Instance?.SetDirty();
        }
    }

    [DisallowMultipleComponent]
    public sealed class OverheatedPowerCable : MonoBehaviour
    {
        private float _destroyAt;
        private readonly List<Renderer> _renderers = new();
        private readonly List<Material> _materials = new();

        public void Begin(float seconds)
        {
            _destroyAt = Time.time + Mathf.Max(0.1f, seconds);
            var cable = GetComponent<PowerCable>();
            if (cable != null) cable.enabled = false;

            _renderers.Clear();
            _materials.Clear();
            GetComponentsInChildren<Renderer>(true, _renderers);

            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                var mat = new Material(sh);
                mat.color = new Color(1f, 0.15f, 0.02f, 1f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(1f, 0.15f, 0.02f, 1f));
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", new Color(4.5f, 0.35f, 0.02f, 1f));
                r.sharedMaterial = mat;
                _materials.Add(mat);
            }
        }

        private void Update()
        {
            float pulse = 3.0f + Mathf.PingPong(Time.time * 8f, 3.5f);
            Color glow = new Color(pulse, pulse * 0.18f, 0.01f, 1f);
            foreach (var m in _materials)
            {
                if (m != null && m.HasProperty("_EmissionColor"))
                    m.SetColor("_EmissionColor", glow);
            }
            if (Time.time >= _destroyAt)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            foreach (var m in _materials)
                if (m != null) Destroy(m);
        }
    }
}
