// Assets/Scripts/VoxelEngine/Maritime/MaritimeMechanicalPorts.cs
//
// Shared mechanical-port contract for maritime drivetrains. Placement, snapping,
// and the propulsion graph all use the same authored port locations/directions so
// a visually connected shaft is also a logically valid connection.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    public enum MechanicalPortRole : byte
    {
        Input = 0,
        Output = 1,
        Bidirectional = 2,
    }

    public struct MechanicalPort
    {
        public MechanicalPortRole Role;
        public Vector3 LocalPosition;
        public Vector3 LocalOutward;
        public Vector3 WorldPosition;
        public Vector3 WorldOutward;
        public string Name;
    }

    /// <summary>
    /// Resolves named shaft/rotation ports and validates face-to-face mechanical
    /// connections. Gearboxes and drive shafts are bidirectional carriers: whichever
    /// connected side receives rotation becomes their input for that live chain.
    /// </summary>
    public static class MaritimeMechanicalPorts
    {
        private const float FacingDotThreshold = -0.55f;

        public static bool TryFindNearestPort(GridBlock owner, Vector3 nearPoint, out MechanicalPort port)
        {
            port = default;
            if (owner == null) return false;
            var ports = new List<MechanicalPort>(4);
            CollectPorts(owner.transform, owner, owner.EffectiveCellSize, ports);
            if (ports.Count == 0) return false;

            float best = float.PositiveInfinity;
            for (int i = 0; i < ports.Count; i++)
            {
                float distance = (ports[i].WorldPosition - nearPoint).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                port = ports[i];
            }
            return best < float.PositiveInfinity;
        }

        /// <summary>
        /// Selects an attachment port from a held item prefab that can mate with
        /// <paramref name="targetRole"/>. The returned position/direction are in
        /// prefab-root local space and can be used to build an exact world pose.
        /// </summary>
        public static bool TryGetPlacementPort(GridBlockItem item, MechanicalPortRole targetRole,
            out Vector3 localPosition, out Vector3 localOutward, out MechanicalPortRole role)
        {
            localPosition = Vector3.zero;
            localOutward = Vector3.forward;
            role = MechanicalPortRole.Bidirectional;
            if (item == null || item.blockPrefab == null) return false;

            var block = item.blockPrefab.GetComponentInChildren<GridBlock>(true);
            float cellSize = item.gridSize.CellSize();
            var ports = new List<MechanicalPort>(4);
            CollectPorts(item.blockPrefab.transform, block, cellSize, ports);
            if (ports.Count == 0) return false;

            int bestIndex = -1;
            int bestScore = int.MinValue;
            for (int i = 0; i < ports.Count; i++)
            {
                if (!RolesCanMate(targetRole, ports[i].Role) && !RolesCanMate(ports[i].Role, targetRole))
                    continue;

                int score = RolePreference(targetRole, ports[i].Role);
                // Stable tie-break: prefer the port farther from the root, which
                // keeps long shafts/extensions visually continuous.
                score += Mathf.RoundToInt(ports[i].LocalPosition.sqrMagnitude * 10f);
                if (score <= bestScore) continue;
                bestScore = score;
                bestIndex = i;
            }

            if (bestIndex < 0) return false;
            var best = ports[bestIndex];
            localPosition = best.LocalPosition;
            localOutward = best.LocalOutward;
            role = best.Role;
            return true;
        }

        /// <summary>Builds a stable world rotation that points a held port directly back into a target port.</summary>
        public static Quaternion BuildAttachmentRotation(Vector3 placedPortLocalOutward,
            Vector3 desiredWorldOutward, Vector3 preferredWorldUp)
        {
            Vector3 localOut = placedPortLocalOutward.sqrMagnitude > 0.0001f
                ? placedPortLocalOutward.normalized
                : Vector3.forward;
            Vector3 desiredOut = desiredWorldOutward.sqrMagnitude > 0.0001f
                ? desiredWorldOutward.normalized
                : Vector3.forward;

            Quaternion align = Quaternion.FromToRotation(localOut, desiredOut);
            Vector3 currentUp = align * Vector3.up;
            Vector3 desiredUp = Vector3.ProjectOnPlane(preferredWorldUp, desiredOut);
            if (desiredUp.sqrMagnitude < 0.0001f)
                desiredUp = Vector3.ProjectOnPlane(Vector3.up, desiredOut);
            if (desiredUp.sqrMagnitude < 0.0001f)
                return align;

            float twist = Vector3.SignedAngle(currentUp, desiredUp.normalized, desiredOut);
            return Quaternion.AngleAxis(twist, desiredOut) * align;
        }

        public static bool CanConnect(GridBlock a, GridBlock b, float cellSize)
        {
            if (a == null || b == null) return false;
            var aPorts = new List<MechanicalPort>(4);
            var bPorts = new List<MechanicalPort>(4);
            CollectPorts(a.transform, a, cellSize, aPorts);
            CollectPorts(b.transform, b, cellSize, bPorts);
            return FindCompatiblePair(aPorts, bPorts, cellSize, false);
        }

        /// <summary>True only when rotation may travel from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static bool CanTransfer(GridBlock from, GridBlock to, float cellSize)
        {
            if (from == null || to == null) return false;
            var fromPorts = new List<MechanicalPort>(4);
            var toPorts = new List<MechanicalPort>(4);
            CollectPorts(from.transform, from, cellSize, fromPorts);
            CollectPorts(to.transform, to, cellSize, toPorts);
            return FindCompatiblePair(fromPorts, toPorts, cellSize, true);
        }

        public static Vector3Int SnapToCardinalAxis(Vector3 localDirection)
        {
            if (localDirection.sqrMagnitude < 0.0001f) return Vector3Int.zero;
            float x = Mathf.Abs(localDirection.x);
            float y = Mathf.Abs(localDirection.y);
            float z = Mathf.Abs(localDirection.z);
            if (x >= y && x >= z) return localDirection.x >= 0f ? Vector3Int.right : Vector3Int.left;
            if (y >= x && y >= z) return localDirection.y >= 0f ? Vector3Int.up : Vector3Int.down;
            return localDirection.z >= 0f ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
        }

        private static bool FindCompatiblePair(List<MechanicalPort> aPorts, List<MechanicalPort> bPorts,
            float cellSize, bool requireAtoB)
        {
            float maxDistance = Mathf.Max(0.18f, cellSize * 0.42f);
            float maxDistanceSqr = maxDistance * maxDistance;
            for (int i = 0; i < aPorts.Count; i++)
            {
                for (int j = 0; j < bPorts.Count; j++)
                {
                    var a = aPorts[i];
                    var b = bPorts[j];
                    if ((a.WorldPosition - b.WorldPosition).sqrMagnitude > maxDistanceSqr) continue;
                    if (Vector3.Dot(a.WorldOutward, b.WorldOutward) > FacingDotThreshold) continue;

                    if (requireAtoB)
                    {
                        if (RolesCanMate(a.Role, b.Role)) return true;
                    }
                    else if (RolesCanMate(a.Role, b.Role) || RolesCanMate(b.Role, a.Role))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool RolesCanMate(MechanicalPortRole from, MechanicalPortRole to)
        {
            bool canSend = from == MechanicalPortRole.Output || from == MechanicalPortRole.Bidirectional;
            bool canReceive = to == MechanicalPortRole.Input || to == MechanicalPortRole.Bidirectional;
            return canSend && canReceive;
        }

        private static int RolePreference(MechanicalPortRole target, MechanicalPortRole candidate)
        {
            if (target == MechanicalPortRole.Output && candidate == MechanicalPortRole.Input) return 30;
            if (target == MechanicalPortRole.Input && candidate == MechanicalPortRole.Output) return 30;
            if (target == MechanicalPortRole.Bidirectional && candidate == MechanicalPortRole.Bidirectional) return 25;
            if (candidate == MechanicalPortRole.Bidirectional) return 20;
            return 10;
        }

        private static void CollectPorts(Transform root, GridBlock owner, float cellSize, List<MechanicalPort> ports)
        {
            ports.Clear();
            if (root == null) return;

            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == root) continue;
                if (!TryGetRole(child.name, out var role)) continue;
                if (IsBidirectionalCarrier(owner)) role = MechanicalPortRole.Bidirectional;

                Vector3 localPosition = root.InverseTransformPoint(child.position);
                Vector3 fallbackLocalOutward = (Vector3)SnapToCardinalAxis(localPosition);
                if (fallbackLocalOutward.sqrMagnitude < 0.0001f) fallbackLocalOutward = Vector3.forward;
                Vector3 worldOutward = MaritimePorts.PortOutwardWorld(child, root.TransformDirection(fallbackLocalOutward));
                Vector3 localOutward = root.InverseTransformDirection(worldOutward).normalized;
                ports.Add(new MechanicalPort
                {
                    Role = role,
                    LocalPosition = localPosition,
                    LocalOutward = localOutward,
                    WorldPosition = child.position,
                    WorldOutward = worldOutward.normalized,
                    Name = child.name
                });
            }

            if (ports.Count == 0)
                AddFallbackPorts(root, owner, cellSize, ports);
        }

        private static bool TryGetRole(string name, out MechanicalPortRole role)
        {
            role = MechanicalPortRole.Bidirectional;
            if (string.IsNullOrEmpty(name)) return false;

            if (name.StartsWith("Port_ShaftIO", System.StringComparison.Ordinal)
                || name.StartsWith("Port_ShaftIO_", System.StringComparison.Ordinal)
                || name.StartsWith("Propeller mount point", System.StringComparison.Ordinal))
            {
                role = MechanicalPortRole.Bidirectional;
                return true;
            }
            if (name.StartsWith("Port_ShaftInput", System.StringComparison.Ordinal)
                || name.StartsWith("Port_RotationInput", System.StringComparison.Ordinal)
                || name.StartsWith("Rotation input point", System.StringComparison.Ordinal))
            {
                role = MechanicalPortRole.Input;
                return true;
            }
            if (name.StartsWith("Port_ShaftOutput", System.StringComparison.Ordinal)
                || name.StartsWith("Port_RotationOutput", System.StringComparison.Ordinal))
            {
                role = MechanicalPortRole.Output;
                return true;
            }
            return false;
        }

        private static bool IsBidirectionalCarrier(GridBlock owner)
        {
            return owner is GridDriveShaft
                || owner is GridGearbox
                || owner is GridEncasedChainDrive;
        }

        private static void AddFallbackPorts(Transform root, GridBlock owner, float cellSize, List<MechanicalPort> ports)
        {
            if (owner == null) return;
            float y = 0f;
            if (owner is GridDriveShaft) y = cellSize * 0.015f;

            if (owner is GridDriveShaft || owner is GridGearbox || owner is GridEncasedChainDrive)
            {
                AddFallback(root, ports, new Vector3(0f, y, -cellSize * 0.50f), Vector3.back, MechanicalPortRole.Bidirectional);
                AddFallback(root, ports, new Vector3(0f, y, cellSize * 0.50f), Vector3.forward, MechanicalPortRole.Bidirectional);
                return;
            }
            if (owner is GridMaritimeGenerator)
            {
                AddFallback(root, ports, new Vector3(0f, cellSize * 0.02f, -cellSize * 0.72f), Vector3.back, MechanicalPortRole.Input);
                return;
            }
            if (owner is GridPropeller)
            {
                AddFallback(root, ports, new Vector3(0f, 0f, -cellSize * 0.50f), Vector3.back, MechanicalPortRole.Input);
                return;
            }
            if (owner is GridMaritimeEngine engine)
            {
                float z = engine.tier == EngineTier.Giant ? 2.04f : engine.tier == EngineTier.Medium ? 1.18f : 0.66f;
                float engineY = engine.tier == EngineTier.Giant ? -0.34f : engine.tier == EngineTier.Medium ? -0.10f : -0.20f;
                AddFallback(root, ports, new Vector3(0f, engineY * cellSize, z * cellSize), Vector3.forward, MechanicalPortRole.Output);
            }
        }

        private static void AddFallback(Transform root, List<MechanicalPort> ports,
            Vector3 localPosition, Vector3 localOutward, MechanicalPortRole role)
        {
            ports.Add(new MechanicalPort
            {
                Role = role,
                LocalPosition = localPosition,
                LocalOutward = localOutward.normalized,
                WorldPosition = root.TransformPoint(localPosition),
                WorldOutward = root.TransformDirection(localOutward).normalized,
                Name = "Fallback"
            });
        }
    }
}
