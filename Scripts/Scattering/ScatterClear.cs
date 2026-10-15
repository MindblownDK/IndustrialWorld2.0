// Assets/Scripts/VoxelEngine/Scattering/ScatterClear.cs
//
// Shared answers to two questions the building and quarry systems both need
// (17.19.0-dev): "is this collider biome scatter?" and "remove the scatter
// prop this collider belongs to". Scatter is decoration, never structure: it
// must not refuse placements, and a machine or quarry claiming ground removes
// the props standing on it.
//
// Trees and non-mob scatter props destroyed through this path record their
// removal in TreeHarvestPersistence so they stay gone across save/load and
// only regrow via the in-game timer when unblocked and unpolluted.

using UnityEngine;

namespace VoxelEngine.Scattering
{
    public static class ScatterClear
    {
        /// <summary>The holder name ChunkScatter parents every spawned prop under.</summary>
        public const string HolderName = "__scatter";
        public const string LegacyHolderName = "Scatter";

        private static bool IsScatterHolder(Transform t)
        {
            if (t == null) return false;
            string n = t.name;
            return n == HolderName || n == LegacyHolderName;
        }

        /// <summary>True when the collider belongs to a biome scatter prop: a
        /// tree, a tracked non-mob scatter prop, or anything parented under a
        /// chunk's Scatter holder (rocks, plants, props).</summary>
        public static bool IsScatterCollider(Collider collider)
        {
            if (collider == null) return false;
            if (collider.GetComponentInParent<VoxelEngine.Trees.Tree>() != null) return true;
            if (collider.GetComponentInParent<ScatterHarvestTracker>() != null) return true;
            for (var t = collider.transform; t != null; t = t.parent)
                if (IsScatterHolder(t)) return true;
            return false;
        }

        /// <summary>The prop root to destroy for this collider: the child of the
        /// Scatter holder it hangs under, or the tree's / tracker's own game object
        /// when not under a holder. Null when not a scatter collider.</summary>
        public static GameObject PropRootOf(Collider collider)
        {
            if (collider == null) return null;
            for (var t = collider.transform; t != null; t = t.parent)
            {
                if (IsScatterHolder(t.parent)) return t.gameObject;
            }
            var tracker = collider.GetComponentInParent<ScatterHarvestTracker>();
            if (tracker != null) return tracker.gameObject;
            var tree = collider.GetComponentInParent<VoxelEngine.Trees.Tree>();
            return tree != null ? tree.gameObject : null;
        }

        private static void RecordPropRemoval(GameObject root)
        {
            if (root == null) return;
            ScatterHarvestTracker.RecordHarvestOnRoot(root);
            var tree = root.GetComponentInChildren<VoxelEngine.Trees.Tree>(true);
            if (tree != null)
                tree.RecordHarvestPersistence();
        }

        /// <summary>Destroy the scatter prop this collider belongs to and persist its removal.</summary>
        public static bool DestroyProp(Collider collider)
        {
            var root = PropRootOf(collider);
            if (root == null) return false;
            RecordPropRemoval(root);
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
                RecordPropRemoval(root);
                Object.Destroy(root);
                destroyed++;
            }
            return destroyed;
        }
    }
}
