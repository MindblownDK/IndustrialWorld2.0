// Assets/Scripts/VoxelEngine/Maritime/MechanicalBeltNetwork.cs
//
// Persistent, per-grid mechanical belt links. A belt is intentionally not a
// placed block: the player uses one Mechanical Belt item to connect two parallel
// shaft pulleys, then every parallel shaft placed through that belt run becomes a
// live take-off point for additional outputs.

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    /// <summary>Save-friendly local addresses for one belt's two end pulleys.</summary>
    [Serializable]
    public struct MechanicalBeltLink
    {
        public Vector3Int endpointA;
        public Vector3Int endpointB;

        public MechanicalBeltLink(Vector3Int a, Vector3Int b)
        {
            endpointA = a;
            endpointB = b;
        }
    }

    /// <summary>Managed graph edge consumed by <see cref="MaritimePropulsionSystem"/> during rebuild.</summary>
    public struct MechanicalBeltEdge
    {
        public int A;
        public int B;

        public MechanicalBeltEdge(int a, int b)
        {
            A = a;
            B = b;
        }
    }

    /// <summary>
    /// Owns a movable grid's belt links, their lightweight runtime visuals, and
    /// their graph edges. The component is added only to grids that actually use
    /// a belt, keeping normal constructs allocation-free.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MechanicalBeltNetwork : MonoBehaviour
    {
        private const float DefaultMinSpan = 0.75f;
        private const float DefaultMaxSpan = 20f;
        private const int DefaultMaxLinks = 64;
        private const float AxisParallelDot = 0.965f;

        [SerializeField] private List<MechanicalBeltLink> _links = new();

        private GridEntity _grid;
        private GameObject _visualRoot;
        private bool _visualsDirty = true;

        private static Material s_beltMaterial;
        private static Material s_pulleyMaterial;
        private static Material s_indicatorMaterial;

        private struct BeltParticipant
        {
            public GridBlock Block;
            public float Along;

            public BeltParticipant(GridBlock block, float along)
            {
                Block = block;
                Along = along;
            }
        }

        public IReadOnlyList<MechanicalBeltLink> Links => _links;
        public int LinkCount => _links != null ? _links.Count : 0;
        public GridEntity Grid => _grid != null ? _grid : (_grid = GetComponent<GridEntity>());

        /// <summary>Returns or installs the belt owner for a movable grid.</summary>
        public static MechanicalBeltNetwork GetOrAdd(GridEntity grid)
        {
            if (grid == null) return null;
            var network = grid.GetComponent<MechanicalBeltNetwork>();
            return network != null ? network : grid.gameObject.AddComponent<MechanicalBeltNetwork>();
        }

        /// <summary>
        /// Only direct shaft carriers may receive a belt. Gearboxes remain direct-port
        /// equipment so a belt can never bypass their selected ratio.
        /// </summary>
        public static bool IsBeltEligibleShaft(GridBlock block)
        {
            return block is GridDriveShaft || block is GridShaftHousing;
        }

        private void Awake()
        {
            _grid = GetComponent<GridEntity>();
        }

        private void OnEnable()
        {
            _visualsDirty = true;
        }

        private void LateUpdate()
        {
            if (_visualsDirty) RebuildVisuals();
        }

        private void OnDestroy()
        {
            ClearVisuals();
        }

        /// <summary>Checks all gameplay and physical constraints without mutating the grid.</summary>
        public bool CanCreateLink(GridBlock first, GridBlock second, MechanicalBeltItem beltItem, out string failure)
        {
            failure = null;
            var grid = Grid;
            if (grid == null)
            {
                failure = "This belt needs a movable grid.";
                return false;
            }
            if (first == null || second == null)
            {
                failure = "Aim at two drive shafts or watertight shaft housings.";
                return false;
            }
            if (first == second || first.GridPos == second.GridPos)
            {
                failure = "Choose two different shaft pulleys.";
                return false;
            }
            if (first.Grid != grid || second.Grid != grid)
            {
                failure = "A mechanical belt can only link shafts on the same grid.";
                return false;
            }
            if (!IsBeltEligibleShaft(first) || !IsBeltEligibleShaft(second))
            {
                failure = "Mechanical belts attach only to drive shafts or watertight shaft housings.";
                return false;
            }

            int cap = beltItem != null ? beltItem.EffectiveMaxBeltsPerGrid : DefaultMaxLinks;
            if (_links != null && _links.Count >= cap)
            {
                failure = $"This grid already has its {cap} belt safety limit.";
                return false;
            }
            if (HasLink(first.GridPos, second.GridPos))
            {
                failure = "Those two shafts already share a mechanical belt.";
                return false;
            }

            float minSpan = beltItem != null ? beltItem.EffectiveMinSpan : DefaultMinSpan;
            float maxSpan = beltItem != null ? beltItem.EffectiveMaxSpan : DefaultMaxSpan;
            return ValidatePulleyGeometry(first, second, minSpan, maxSpan, out failure);
        }

        /// <summary>Adds one belt after validation. The caller owns item consumption.</summary>
        public bool TryCreateLink(GridBlock first, GridBlock second, MechanicalBeltItem beltItem, out string failure)
        {
            if (!CanCreateLink(first, second, beltItem, out failure)) return false;
            _links ??= new List<MechanicalBeltLink>();
            _links.Add(new MechanicalBeltLink(first.GridPos, second.GridPos));
            NotifyTopologyChanged();
            return true;
        }

        /// <summary>Removes the exact belt between two endpoints, if present.</summary>
        public bool RemoveLink(GridBlock first, GridBlock second)
        {
            if (first == null || second == null || _links == null) return false;
            for (int i = _links.Count - 1; i >= 0; i--)
            {
                if (!Matches(_links[i], first.GridPos, second.GridPos)) continue;
                _links.RemoveAt(i);
                NotifyTopologyChanged();
                return true;
            }
            return false;
        }

        /// <summary>Removes every belt touching a selected shaft and returns the count.</summary>
        public int RemoveLinksAttachedTo(GridBlock shaft)
        {
            if (shaft == null || _links == null || _links.Count == 0) return 0;
            int removed = 0;
            for (int i = _links.Count - 1; i >= 0; i--)
            {
                var link = _links[i];
                if (link.endpointA != shaft.GridPos && link.endpointB != shaft.GridPos) continue;
                _links.RemoveAt(i);
                removed++;
            }
            if (removed > 0) NotifyTopologyChanged();
            return removed;
        }

        /// <summary>
        /// Rehydrates links after the structural grid blocks are restored. Invalid or
        /// stale links are ignored rather than corrupting a legacy save.
        /// </summary>
        public void RestoreLinks(IEnumerable<MechanicalBeltLink> savedLinks)
        {
            _links ??= new List<MechanicalBeltLink>();
            _links.Clear();
            if (savedLinks != null)
            {
                foreach (var link in savedLinks)
                {
                    if (link.endpointA == link.endpointB || HasLink(link.endpointA, link.endpointB)) continue;
                    if (!TryResolveLink(link, out var a, out var b)) continue;
                    // Persistence is trusted to retain an already-created link, but
                    // still rejects degenerate/corrupt data without applying current
                    // item length caps retroactively to an existing ship.
                    if (!ValidatePulleyGeometry(a, b, 0.05f, 5000f, out _)) continue;
                    _links.Add(link);
                }
            }
            NotifyTopologyChanged();
        }

        /// <summary>
        /// Called by GridEntity after block edits. A removed endpoint invalidates its
        /// belt immediately; newly placed shafts can become automatic belt take-offs.
        /// </summary>
        public void NotifyGridTopologyChanged()
        {
            PruneMissingEndpointsInternal();
            NotifyTopologyChanged();
        }

        /// <summary>Removes links whose endpoint block no longer exists. Useful before saving.</summary>
        public bool PruneMissingEndpoints()
        {
            bool changed = PruneMissingEndpointsInternal();
            if (changed) NotifyTopologyChanged();
            return changed;
        }

        /// <summary>
        /// Emits the bidirectional belt bus edges for the propulsion graph. Every
        /// aligned shaft whose centre lies inside a belt run is included, so players
        /// can insert shaft take-offs through the belt to create more outputs.
        /// </summary>
        public void CollectMechanicalEdges(Dictionary<GridBlock, int> mechanicalIndexByBlock,
            List<MechanicalBeltEdge> output)
        {
            if (mechanicalIndexByBlock == null || output == null || _links == null || _links.Count == 0)
                return;

            var participants = new List<BeltParticipant>(8);
            var indices = new List<int>(8);
            var seenEdges = new HashSet<long>();

            for (int linkIndex = 0; linkIndex < _links.Count; linkIndex++)
            {
                var link = _links[linkIndex];
                if (!TryResolveLink(link, out var a, out var b)) continue;

                GatherParticipants(a, b, participants);
                indices.Clear();
                for (int i = 0; i < participants.Count; i++)
                {
                    var candidate = participants[i].Block;
                    if (candidate == null || !mechanicalIndexByBlock.TryGetValue(candidate, out int index)) continue;
                    if (!indices.Contains(index)) indices.Add(index);
                }

                // A belt acts as one shared pulley bus. All participating shafts are
                // mutually connected, allowing an inserted shaft to become a branch
                // output no matter which end currently receives engine rotation.
                for (int i = 0; i < indices.Count; i++)
                {
                    for (int j = i + 1; j < indices.Count; j++)
                    {
                        int lo = Mathf.Min(indices[i], indices[j]);
                        int hi = Mathf.Max(indices[i], indices[j]);
                        long key = ((long)lo << 32) | (uint)hi;
                        if (!seenEdges.Add(key)) continue;
                        output.Add(new MechanicalBeltEdge(lo, hi));
                    }
                }
            }
        }

        /// <summary>Marks the generated belt model for a safe, end-of-frame rebuild.</summary>
        public void MarkVisualsDirty()
        {
            _visualsDirty = true;
        }

        /// <summary>Recreates simple no-collider visual belts beneath the moving grid.</summary>
        public void RebuildVisuals()
        {
            _visualsDirty = false;
            ClearVisuals();

            var grid = Grid;
            if (grid == null || _links == null || _links.Count == 0) return;

            _visualRoot = new GameObject("MechanicalBelts_Runtime")
            {
                hideFlags = HideFlags.DontSave
            };
            _visualRoot.transform.SetParent(grid.transform, false);

            var participants = new List<BeltParticipant>(8);
            for (int i = 0; i < _links.Count; i++)
            {
                if (!TryResolveLink(_links[i], out var a, out var b)) continue;
                GatherParticipants(a, b, participants);
                if (participants.Count < 2) continue;

                if (!TryGetShaftAxis(a, out var axisWorld)) continue;
                Vector3 axisLocal = grid.transform.InverseTransformDirection(axisWorld).normalized;
                if (axisLocal.sqrMagnitude < 0.0001f) axisLocal = Vector3.up;

                for (int p = 0; p < participants.Count; p++)
                {
                    var pulley = participants[p].Block;
                    if (pulley == null) continue;
                    Vector3 localPos = grid.transform.InverseTransformPoint(pulley.transform.position);
                    CreatePulley(localPos, axisLocal, pulley.EffectiveCellSize);
                }

                for (int p = 0; p < participants.Count - 1; p++)
                {
                    var startBlock = participants[p].Block;
                    var endBlock = participants[p + 1].Block;
                    if (startBlock == null || endBlock == null) continue;
                    Vector3 start = grid.transform.InverseTransformPoint(startBlock.transform.position);
                    Vector3 end = grid.transform.InverseTransformPoint(endBlock.transform.position);
                    CreateBeltRun(start, end, axisLocal,
                        Mathf.Min(startBlock.EffectiveCellSize, endBlock.EffectiveCellSize));
                }
            }
        }

        private bool PruneMissingEndpointsInternal()
        {
            if (_links == null || _links.Count == 0) return false;
            bool changed = false;
            for (int i = _links.Count - 1; i >= 0; i--)
            {
                if (TryResolveLink(_links[i], out _, out _)) continue;
                _links.RemoveAt(i);
                changed = true;
            }
            return changed;
        }

        private void NotifyTopologyChanged()
        {
            _visualsDirty = true;
            Grid?.Maritime?.MarkDirty();
        }

        private bool TryResolveLink(MechanicalBeltLink link, out GridBlock a, out GridBlock b)
        {
            a = null;
            b = null;
            var grid = Grid;
            if (grid == null) return false;
            a = grid.GetBlock(link.endpointA);
            b = grid.GetBlock(link.endpointB);
            return IsBeltEligibleShaft(a) && IsBeltEligibleShaft(b) && a != b;
        }

        private bool HasLink(Vector3Int a, Vector3Int b)
        {
            if (_links == null) return false;
            for (int i = 0; i < _links.Count; i++)
                if (Matches(_links[i], a, b)) return true;
            return false;
        }

        private static bool Matches(MechanicalBeltLink link, Vector3Int a, Vector3Int b)
        {
            return (link.endpointA == a && link.endpointB == b)
                || (link.endpointA == b && link.endpointB == a);
        }

        private static bool ValidatePulleyGeometry(GridBlock first, GridBlock second,
            float minSpan, float maxSpan, out string failure)
        {
            failure = null;
            if (!TryGetShaftAxis(first, out var firstAxis) || !TryGetShaftAxis(second, out var secondAxis))
            {
                failure = "Could not resolve both shaft axes.";
                return false;
            }
            if (Mathf.Abs(Vector3.Dot(firstAxis, secondAxis)) < AxisParallelDot)
            {
                failure = "Belt pulleys must be parallel.";
                return false;
            }

            Vector3 offset = second.transform.position - first.transform.position;
            float axialOffset = Mathf.Abs(Vector3.Dot(offset, firstAxis));
            float planeTolerance = Mathf.Max(0.18f, Mathf.Min(first.EffectiveCellSize, second.EffectiveCellSize) * 0.22f);
            if (axialOffset > planeTolerance)
            {
                failure = "Belt pulleys must share the same shaft plane.";
                return false;
            }

            Vector3 planarOffset = Vector3.ProjectOnPlane(offset, firstAxis);
            float span = planarOffset.magnitude;
            if (span < Mathf.Max(0.05f, minSpan))
            {
                failure = "Those pulleys are too close; use a direct shaft coupling.";
                return false;
            }
            if (span > Mathf.Max(minSpan, maxSpan))
            {
                failure = $"Belt span is too long ({span:0.0} m / {maxSpan:0.0} m).";
                return false;
            }
            return true;
        }

        private static bool TryGetShaftAxis(GridBlock block, out Vector3 axis)
        {
            axis = Vector3.forward;
            if (block == null) return false;
            if (MaritimeMechanicalPorts.TryGetCarrierAxis(block, out axis)) return true;
            axis = block.transform.forward;
            return axis.sqrMagnitude > 0.0001f;
        }

        private void GatherParticipants(GridBlock a, GridBlock b, List<BeltParticipant> output)
        {
            output.Clear();
            if (a == null || b == null || Grid == null) return;

            output.Add(new BeltParticipant(a, 0f));
            output.Add(new BeltParticipant(b, 1f));
            if (!TryGetShaftAxis(a, out var axis))
            {
                output.Sort((left, right) => left.Along.CompareTo(right.Along));
                return;
            }

            Vector3 start = a.transform.position;
            Vector3 end = b.transform.position;
            Vector3 span = end - start;
            float spanSqr = span.sqrMagnitude;
            if (spanSqr < 0.0001f)
            {
                output.Sort((left, right) => left.Along.CompareTo(right.Along));
                return;
            }

            float cellSize = Mathf.Min(a.EffectiveCellSize, b.EffectiveCellSize);
            float tapTolerance = Mathf.Max(0.18f, cellSize * 0.34f);
            float tapToleranceSqr = tapTolerance * tapTolerance;

            foreach (var candidate in Grid.AllBlocks)
            {
                if (candidate == null || candidate == a || candidate == b) continue;
                if (!IsBeltEligibleShaft(candidate)) continue;
                if (!TryGetShaftAxis(candidate, out var candidateAxis)) continue;
                if (Mathf.Abs(Vector3.Dot(axis, candidateAxis)) < AxisParallelDot) continue;

                Vector3 toCandidate = candidate.transform.position - start;
                float along = Mathf.Clamp01(Vector3.Dot(toCandidate, span) / spanSqr);
                if (along <= 0.02f || along >= 0.98f) continue;
                Vector3 closest = start + span * along;
                if ((candidate.transform.position - closest).sqrMagnitude > tapToleranceSqr) continue;

                output.Add(new BeltParticipant(candidate, along));
            }

            output.Sort((left, right) => left.Along.CompareTo(right.Along));
        }

        private void CreateBeltRun(Vector3 start, Vector3 end, Vector3 shaftAxis, float cellSize)
        {
            Vector3 rawDirection = end - start;
            Vector3 direction = Vector3.ProjectOnPlane(rawDirection, shaftAxis);
            float length = direction.magnitude;
            if (length < 0.03f) return;
            direction /= length;
            Vector3 side = Vector3.Cross(shaftAxis, direction).normalized;
            if (side.sqrMagnitude < 0.0001f) return;

            float runGap = Mathf.Max(0.06f, cellSize * 0.10f);
            float runWidth = Mathf.Max(0.035f, cellSize * 0.040f);
            float thickness = Mathf.Max(0.018f, cellSize * 0.018f);
            Quaternion rotation = Quaternion.LookRotation(direction, shaftAxis);
            Vector3 center = (start + end) * 0.5f;

            CreateVisualCube("Belt_Run_A", center + side * runGap, rotation,
                new Vector3(runWidth, thickness, length), BeltMaterial);
            CreateVisualCube("Belt_Run_B", center - side * runGap, rotation,
                new Vector3(runWidth, thickness, length), BeltMaterial);

            // A short central travel marker keeps the dark rubber legible against
            // iron hulls without turning the belt into a glowing cable.
            CreateVisualCube("Belt_TravelMarker", center, rotation,
                new Vector3(runWidth * 0.45f, thickness * 0.55f, Mathf.Min(length * 0.24f, cellSize * 0.45f)), IndicatorMaterial);
        }

        private void CreatePulley(Vector3 localPosition, Vector3 axisLocal, float cellSize)
        {
            if (_visualRoot == null) return;
            var pulley = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pulley.name = "Belt_Pulley";
            pulley.hideFlags = HideFlags.DontSave;
            pulley.transform.SetParent(_visualRoot.transform, false);
            pulley.transform.localPosition = localPosition;
            pulley.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axisLocal);
            float radius = Mathf.Max(0.10f, cellSize * 0.115f);
            float width = Mathf.Max(0.035f, cellSize * 0.030f);
            pulley.transform.localScale = new Vector3(radius * 2f, width, radius * 2f);
            DisableCollider(pulley);
            var renderer = pulley.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = PulleyMaterial;
        }

        private void CreateVisualCube(string name, Vector3 localPosition, Quaternion rotation,
            Vector3 scale, Material material)
        {
            if (_visualRoot == null) return;
            var run = GameObject.CreatePrimitive(PrimitiveType.Cube);
            run.name = name;
            run.hideFlags = HideFlags.DontSave;
            run.transform.SetParent(_visualRoot.transform, false);
            run.transform.localPosition = localPosition;
            run.transform.localRotation = rotation;
            run.transform.localScale = scale;
            DisableCollider(run);
            var renderer = run.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        private static void DisableCollider(GameObject go)
        {
            var collider = go != null ? go.GetComponent<Collider>() : null;
            if (collider != null) collider.enabled = false;
        }

        private void ClearVisuals()
        {
            if (_visualRoot == null) return;
            if (Application.isPlaying) Destroy(_visualRoot);
            else DestroyImmediate(_visualRoot);
            _visualRoot = null;
        }

        private static Material BeltMaterial => s_beltMaterial ??= CreateMaterial(
            "MechanicalBelt_Rubber", new Color(0.055f, 0.060f, 0.070f), 0.05f, 0.42f, null);

        private static Material PulleyMaterial => s_pulleyMaterial ??= CreateMaterial(
            "MechanicalBelt_Pulley", new Color(0.38f, 0.40f, 0.44f), 0.78f, 0.56f, null);

        private static Material IndicatorMaterial => s_indicatorMaterial ??= CreateMaterial(
            "MechanicalBelt_Travel", new Color(0.85f, 0.61f, 0.13f), 0.45f, 0.60f,
            new Color(0.23f, 0.10f, 0.01f));

        private static Material CreateMaterial(string name, Color color, float metallic, float smoothness, Color? emission)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Standard")
                            ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var material = new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.DontSave,
                color = color
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
            }
            return material;
        }
    }
}
