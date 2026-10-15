// Assets/Scripts/VoxelEngine/Trees/Tree.cs
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Scattering;

namespace VoxelEngine.Trees
{
    /// <summary>
    /// Chop-down receiver attached to scatter tree prefabs. Hitting the tree (raycast +
    /// LMB while holding any tool) reduces hp; on death drops Wood Logs.
    /// </summary>
    public class Tree : MonoBehaviour
    {
        public int   maxHp = 80;
        public ItemDefinition logItem;          // assign a "Wood Log" ItemDefinition asset
        public ItemDefinition saplingItem;      // optional "Tree Sapling" ItemDefinition asset
        public int   minLogs = 2, maxLogs = 4;
        [Tooltip("Required tool tier to harvest faster (axe). Hands work but slower.")]
        public ToolType preferredTool = ToolType.Axe;

        private static ItemDefinition _cachedSaplingItem;

        [HideInInspector] public int hp;
        [HideInInspector] public string scatterPersistencePath;
        [HideInInspector] public string stableScatterIdentity;

        // 17.17.2-dev: stamped by ChunkScatter when the holder dies for streaming
        // reasons (chunk pool return, repopulate, world dispose). Only un-stamped
        // deaths are harvests.
        private bool _streamingTeardown;

        /// <summary>Set by the scatter system before it destroys this tree's holder
        /// for streaming reasons; OnDestroy then skips the harvest record.</summary>
        public void MarkStreamingTeardown() => _streamingTeardown = true;

        private void OnDestroy()
        {
            // 17.17.2-dev: ANY gameplay death of a scatter tree is a harvest - not
            // only a felling blow through Tree.Hit. Before this, a tree destroyed by
            // any other path (blast, fire, a future tool) vanished for the session
            // and regrew on the next load, because nothing had recorded the stump.
            // Streaming deaths (chunk pool returns, repopulates, world dispose,
            // scene unload) are stamped or scene-guarded and never record.
            if (_streamingTeardown) return;
            if (!gameObject.scene.isLoaded) return;
            if (ChunkScatter.WorldTeardownActive) return;
            RecordHarvestPersistence();
        }

        /// <summary>The recorded store path, or the live world's when this instance
        /// predates the wiring (legacy spawn, editor-placed tree).</summary>
        private string ResolveStorePath()
        {
            if (!string.IsNullOrEmpty(scatterPersistencePath)) return scatterPersistencePath;
            return TreeHarvestPersistence.GetStorePath(VoxelEngine.Core.ActiveWorld.Current
                as VoxelEngine.Cosmos.IChunkScatterWorld);
        }

        /// <summary>The recorded surface-cell identity, or the cell under this trunk
        /// when the instance carries none. The three-quarter-voxel step down lands
        /// inside the solid surface cell the scatter pass keyed the tree by.</summary>
        private string ResolveIdentity()
        {
            if (!string.IsNullOrEmpty(stableScatterIdentity)) return stableScatterIdentity;
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return null;
            Vector3 baseCell = transform.position - transform.up * (VoxelEngine.Core.VoxelConstants.VOXEL_SIZE * 0.75f);
            return TreeHarvestPersistence.MakeIdentity(world.WorldToVoxel(baseCell));
        }

        private void Awake()
        {
            hp = maxHp;

            // Make sure trees have a collider so raycasts hit them.
            if (GetComponentInChildren<Collider>() == null)
            {
                var box = gameObject.AddComponent<CapsuleCollider>();
                box.height = 4f; box.radius = 0.5f;
                box.center = new Vector3(0, 2f, 0);
            }
        }

        /// <summary>Return value: damage actually dealt (so caller can apply tool durability).</summary>
        public int Hit(int damage, ToolType usedTool)
        {
            if (hp <= 0) return 0;
            int dealt = damage;
            if (usedTool != preferredTool) dealt = Mathf.Max(4, damage / 2);
            hp -= dealt;
            if (hp <= 0)
            {
                RecordHarvestPersistence();
                int yield = Random.Range(minLogs, maxLogs + 1);
                var inv = Object.FindAnyObjectByType<Inventory>();
                if (inv != null)
                {
                    if (logItem != null) inv.Add(logItem, yield);
                    var sapling = saplingItem != null ? saplingItem : ResolveSaplingItem();
                    if (sapling != null)
                    {
                        int saplings = Random.Range(1, 3);
                        inv.Add(sapling, saplings);
                    }
                }
                Destroy(ResolveScatterRoot());
            }
            return dealt;
        }

        private static ItemDefinition ResolveSaplingItem()
        {
            if (_cachedSaplingItem != null) return _cachedSaplingItem;
            var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            for (int i = 0; i < all.Length; i++)
            {
                var it = all[i];
                if (it == null) continue;
                if (it.itemId == "seed_tree_sapling" || it.itemId == "item_tree_sapling" || it.displayName == "Tree Sapling")
                {
                    _cachedSaplingItem = it;
                    return it;
                }
            }
            return null;
        }

        public void RecordHarvestPersistence()
        {
            _streamingTeardown = true;
            GameObject root = ResolveScatterRoot();
            ScatterHarvestTracker.RecordHarvestOnRoot(root);

            var sphere = GetComponentInParent<VoxelEngine.Cosmos.SphereWorld>()
                ?? (VoxelEngine.Core.ActiveWorld.Current as VoxelEngine.Cosmos.SphereWorld);
            string storePath = ResolveStorePath();

            Transform bodyRoot = sphere != null && sphere.body != null ? sphere.body.transform : root.transform.root;
            Vector3 localPos = bodyRoot != null ? bodyRoot.InverseTransformPoint(root.transform.position) : root.transform.position;

            string identity = ResolveIdentity();
            if (string.IsNullOrWhiteSpace(identity))
            {
                Vector3Int cell = new Vector3Int(
                    Mathf.RoundToInt(localPos.x / VoxelEngine.Core.VoxelConstants.VOXEL_SIZE),
                    Mathf.RoundToInt(localPos.y / VoxelEngine.Core.VoxelConstants.VOXEL_SIZE),
                    Mathf.RoundToInt(localPos.z / VoxelEngine.Core.VoxelConstants.VOXEL_SIZE));
                identity = TreeHarvestPersistence.MakeIdentity(cell);
            }

            TreeHarvestPersistence.RecordHarvested(storePath, identity, localPos, isTree: true);
        }

        private GameObject ResolveScatterRoot()
        {
            for (Transform t = transform; t != null; t = t.parent)
            {
                if (t.parent != null && (t.parent.name == "__scatter" || t.parent.name == "Scatter"))
                    return t.gameObject;
            }
            return gameObject;
        }
    }
}
