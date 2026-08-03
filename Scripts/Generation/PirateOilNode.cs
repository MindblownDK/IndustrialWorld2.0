// Assets/Scripts/VoxelEngine/Generation/PirateOilNode.cs
// Runtime marker for one rare, infinite Pirate World oil site. The terrain
// puddle/bore/reservoir remains the visual geology; this marker preserves the
// node's infinite extraction identity even if a player drains visible oil voxels.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Generation
{
    [DisallowMultipleComponent]
    public sealed class PirateOilNode : MonoBehaviour
    {
        private static readonly List<PirateOilNode> s_nodes = new(32);

        public SphereWorld World { get; private set; }
        public Vector3Int SurfaceVoxel { get; private set; }
        public Vector3Int ReservoirVoxel { get; private set; }
        public float PumpRadius { get; private set; } = 4.5f;

        private void OnEnable()
        {
            if (!s_nodes.Contains(this)) s_nodes.Add(this);
        }

        private void OnDisable()
        {
            s_nodes.Remove(this);
        }

        public static PirateOilNode Ensure(SphereWorld world, Vector3Int surfaceVoxel, Vector3Int reservoirVoxel)
        {
            if (world == null || world.body == null || world.body.settings == null
                || !world.body.settings.CanGenerateInfiniteJackPumpNodes) return null;
            for (int i = s_nodes.Count - 1; i >= 0; i--)
            {
                var node = s_nodes[i];
                if (node == null) { s_nodes.RemoveAt(i); continue; }
                if (node.World == world && (node.ReservoirVoxel - reservoirVoxel).sqrMagnitude <= 4)
                    return node;
            }

            var go = new GameObject("Infinite Pirate Oil Node");
            go.transform.SetParent(world.body.transform, false);
            go.transform.position = world.body.transform.TransformPoint(
                ((Vector3)surfaceVoxel + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE);
            var created = go.AddComponent<PirateOilNode>();
            created.World = world;
            created.SurfaceVoxel = surfaceVoxel;
            created.ReservoirVoxel = reservoirVoxel;
            created.PumpRadius = 4.5f;
            return created;
        }

        public static bool IsPumpableNear(SphereWorld world, Vector3 pumpWorldPosition, float extraReach = 0.75f)
        {
            if (world == null || world.body == null || world.body.settings == null
                || !world.body.settings.CanGenerateInfiniteJackPumpNodes) return false;
            for (int i = s_nodes.Count - 1; i >= 0; i--)
            {
                var node = s_nodes[i];
                if (node == null) { s_nodes.RemoveAt(i); continue; }
                if (node.World != world) continue;
                float reach = node.PumpRadius + Mathf.Max(0f, extraReach);
                if ((node.transform.position - pumpWorldPosition).sqrMagnitude <= reach * reach)
                    return true;
            }
            return false;
        }
    }
}
