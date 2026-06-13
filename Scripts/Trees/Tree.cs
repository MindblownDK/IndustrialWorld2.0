// Assets/Scripts/VoxelEngine/Trees/Tree.cs
using UnityEngine;
using VoxelEngine.Items;

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
        public int   minLogs = 2, maxLogs = 4;
        [Tooltip("Required tool tier to harvest faster (axe). Hands work but slower.")]
        public ToolType preferredTool = ToolType.Axe;

        [HideInInspector] public int hp;

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
            if (usedTool != preferredTool) dealt = Mathf.Max(1, damage / 3);
            hp -= dealt;
            if (hp <= 0)
            {
                int yield = Random.Range(minLogs, maxLogs + 1);
                if (logItem != null)
                {
                    var inv = Object.FindAnyObjectByType<Inventory>();
                    if (inv != null) inv.Add(logItem, yield);
                }
                Destroy(gameObject);
            }
            return dealt;
        }
    }
}
