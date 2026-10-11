// Assets/Scripts/VoxelEngine/Scattering/ScatterClear.cs
//
// Shared answers to two questions the building and quarry systems both need
// (17.19.0-dev): "is this collider biome scatter?" and "remove the scatter
// prop this collider belongs to". Scatter is decoration, never structure: it
// must not refuse placements, and a machine or quarry claiming ground removes
// the props standing on it.
//
// Trees destroyed through this path die as GAMEPLAY deaths: they are not
// stamped as streaming teardown, so their stumps are recorded by the Tree's
// own OnDestroy (17.17.2-dev) and they stay gone across save/load. Rocks and
// other props carry no persistence and simply go.

using UnityEngine;

namespace VoxelEngine.Scattering
{
    public static class ScatterClear
    {
        /// <summary>The holder name ChunkScatter parents every spawned prop under.</summary>
        public const string HolderName = "__scatter";

        /// <summary>True when the collider belongs to a biome scatter prop: a
        /// tree (component check, also covers editor-placed trees) or anything
        /// parented under a chunk's __scatter holder (rocks, plants, props).</summary>
        public static bool IsScatterCollider(Collider collider)
        {
            if (collider == null) return false;
            if (collider.GetComponentInParent<VoxelEngine.Trees.Tree>() != null) return true;
            for (var t = collider.transform; t != null; t = t.parent)
                if (t.name == HolderName) return true;
            return false;
        }

        /// <summary>The prop root to destroy for this collider: the child of the
        /// __scatter holder it hangs under, or the tree's own game object when
        /// the tree was not scatter-spawned. Null when not a scatter collider.</summary>
        public static GameObject PropRootOf(Collider collider)
        {
            if (collider == null) return null;
            for (var t = collider.transform; t != null; t = t.parent)
            {
                if (t.parent != null && t.parent.name == HolderName) return t.gameObject;
            }
            var tree = collider.GetComponentInParent<VoxelEngine.Trees.Tree>();
            return tree != null ? tree.gameObject : null;
        }

        /// <summary>Destroy the scatter prop this collider belongs to. Trees
        /// record their harvest through their own OnDestroy.</summary>
        public static bool DestroyProp(Collider collider)
        {
            var root = PropRootOf(collider);
            if (root == null) return false;
            Object.Destroy(root);
            return true;
        }

        /// <summary>Remove every scatter prop intersecting an oriented box.
        /// Returns how many props were destroyed. One-shot call (placement,
        /// quarry start) - deliberately simple, no persistent buffers.</summary>
        public static int ClearInBox(Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            var hits = Physics.OverlapBox(center, halfExtents, rotation, ~0, QueryTriggerInteraction.Collide);
            int destroyed = 0;
            for (int i = 0; i < hits.Length; i++)
            {
                if (!IsScatterCollider(hits[i])) continue;
                var root = PropRootOf(hits[i]);
                if (root == null) continue;
                Object.Destroy(root);
                destroyed++;
            }
            return destroyed;
        }
    }
}
