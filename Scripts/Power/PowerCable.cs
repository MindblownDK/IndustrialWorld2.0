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
        private static readonly Collider[] s_endpointTouchProbe = new Collider[32];

        // Track which face each neighbour is connected through
        private readonly Dictionary<PowerNode, CubeFace> _neighbourFaces = new();

        protected override void OnEnable()
        {
            float gs = gridSize > 0 ? gridSize : 1f;
            connectRadius = gs * Mathf.Max(3.0f, straightLength * 1.5f);

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
        }

        public override bool CanLinkTo(PowerNode other)
        {
            if (other == null || other == this) return false;

            if (other is PowerCable otherCable)
            {
                var myEps = EnergyPipeMeshBuilder.GetLocalEndpoints(variant, straightLength);
                var otherEps = EnergyPipeMeshBuilder.GetLocalEndpoints(otherCable.variant, otherCable.straightLength);
                for (int i = 0; i < myEps.Count; i++)
                {
                    Vector3 myWorld = transform.TransformPoint(myEps[i].Position);
                    for (int j = 0; j < otherEps.Count; j++)
                    {
                        Vector3 otherWorld = otherCable.transform.TransformPoint(otherEps[j].Position);
                        if ((myWorld - otherWorld).sqrMagnitude <= 0.45f * 0.45f)
                            return true;
                    }
                }
                return false;
            }

            // Connecting to a machine, generator, battery, or consumer
            return TouchesPowerEndpoint(other);
        }

        private bool TouchesPowerEndpoint(PowerNode other)
        {
            if (other == null) return false;
            var myEps = EnergyPipeMeshBuilder.GetLocalEndpoints(variant, straightLength);
            var otherColliders = other.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < myEps.Count; i++)
            {
                Vector3 epWorld = transform.TransformPoint(myEps[i].Position);
                // Proximity to node transform
                if ((other.transform.position - epWorld).sqrMagnitude <= 2.2f * 2.2f)
                    return true;

                // Proximity to node colliders
                for (int c = 0; c < otherColliders.Length; c++)
                {
                    var col = otherColliders[c];
                    if (col == null || !col.enabled) continue;
                    Vector3 closest = col.ClosestPoint(epWorld);
                    if ((closest - epWorld).sqrMagnitude <= 1.4f * 1.4f)
                        return true;
                }
            }
            return false;
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

            // Collect machine connection points to bridge visual gaps flush to machine surfaces
            List<Vector3> machineTargetsLocal = null;
            if (neighbours != null && neighbours.Count > 0)
            {
                for (int i = 0; i < neighbours.Count; i++)
                {
                    var nb = neighbours[i];
                    if (nb != null && !(nb is PowerCable))
                    {
                        var cols = nb.GetComponentsInChildren<Collider>(true);
                        Vector3 targetWorld = nb.transform.position;
                        if (cols != null && cols.Length > 0)
                        {
                            float bestD = float.MaxValue;
                            for (int c = 0; c < cols.Length; c++)
                            {
                                if (cols[c] != null && cols[c].enabled)
                                {
                                    Vector3 cl = cols[c].ClosestPoint(transform.position);
                                    float d = (cl - transform.position).sqrMagnitude;
                                    if (d < bestD) { bestD = d; targetWorld = cl; }
                                }
                            }
                        }
                        if (machineTargetsLocal == null) machineTargetsLocal = new List<Vector3>();
                        machineTargetsLocal.Add(transform.InverseTransformPoint(targetWorld));
                    }
                }
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
