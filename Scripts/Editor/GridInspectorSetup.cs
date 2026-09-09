// Assets/Scripts/VoxelEngine/Editor/GridInspectorSetup.cs
//
// Step 68 — GRID INSPECTOR OVERLAY research (9.37.0-dev): authors the three research
// nodes that gate the reader modes of the inspector overlay — integrity scan first
// (structural awareness), thermal scan second (engine-room awareness), centre of mass
// last (ship design). The step is deliberately small: the round ships a viewing mode,
// not a block, so there is no prefab, no item and no recipe to author — only the nodes
// that earn the view, chained under Grid Utilities in the order the design settled.
//
// Non-destructive, like every other authored step:
//   • an existing node keeps its cost, research time, label, tier and description;
//     the step only fills values still sitting at a Unity default and appends the
//     missing prerequisite link rather than replacing a chain a player or a later
//     round has tuned;
//   • nodes are added to the ResearchTree only when missing.
// A second run creates nothing and re-links nothing twice.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    public static class GridInspectorSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string NODES = ASSET_ROOT + "/Research/Nodes";
        private const string TREE_PATH = ASSET_ROOT + "/Research/ResearchTree.asset";
        private const string UTILS_NODE_PATH = NODES + "/res_grid_utilities.asset";

        // Keep in step with VoxelEngine.UI.GridInspectorHud.NodeDamage / NodeHeat / NodeCom.
        public const string DAMAGE_PATH = NODES + "/res_grid_inspector_damage.asset";
        public const string HEAT_PATH = NODES + "/res_grid_inspector_heat.asset";
        public const string COM_PATH = NODES + "/res_grid_inspector_com.asset";

        [MenuItem("Tools/Voxel Engine/Setup Step 68 — Grid Inspector Overlay Research")]
        public static void RunStep68Menu() => RunStep68();

        public static void RunStep68()
        {
            Debug.Log("[GridInspectorSetup] Step 68 — Grid Inspector research started.");

            EnsureFolder(NODES);

            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(TREE_PATH);
            var utilsNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(UTILS_NODE_PATH);

            int created = 0, preserved = 0;

            var damage = AuthorNode(DAMAGE_PATH, "res_grid_inspector_damage",
                "Integrity Scan",
                "Reads any hull like an X-ray: blocks step from teal through amber to red by the damage they have actually taken. The first Grid Inspector mode, and the one that makes a wounded ship legible before anything else does.",
                new Color(0.22f, 0.78f, 0.42f), 3, 8,
                ref created, ref preserved);

            var heat = AuthorNode(HEAT_PATH, "res_grid_inspector_heat",
                "Thermal Scan",
                "Tints every block by how far it sits through its own heat tolerance — glass fails long before machinery, so each block's red line is its own — and pins a marker on the worst plate in view. The second Grid Inspector mode.",
                new Color(0.92f, 0.60f, 0.12f), 4, 7,
                ref created, ref preserved);

            var com = AuthorNode(COM_PATH, "res_grid_inspector_com",
                "Centre of Mass",
                "Draws a ship's true mass centre and the line its drive actually pushes along, and colours the ball by how far apart the two are. The last Grid Inspector mode, because it is the one that makes ship design teachable.",
                new Color(0.18f, 0.72f, 0.88f), 5, 7,
                ref created, ref preserved);

            // The chain is append-only and each link is only added when the target node
            // exists, so a partial run can never leave a dangling prerequisite.
            if (damage != null && utilsNode != null) Link(damage, utilsNode);
            if (heat != null && damage != null) Link(heat, damage);
            if (com != null && heat != null) Link(com, heat);

            foreach (var n in new[] { damage, heat, com })
            {
                if (n == null) continue;
                if (tree != null && !tree.nodes.Contains(n))
                {
                    tree.nodes.Add(n);
                    EditorUtility.SetDirty(tree);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[GridInspectorSetup] Step 68 complete — created " + created + ", preserved " + preserved + ".");
            EditorUtility.DisplayDialog("Voxel Engine — Grid Inspector Overlay Research (Step 68)",
                "Grid Inspector research authored.\n\n" +
                "• Created: " + created + " (" + preserved + " existing preserved)\n" +
                "• INTEGRITY SCAN — tier 3, under Grid Utilities: unlocks the damage read\n" +
                "• THERMAL SCAN — tier 4, requires Integrity Scan: unlocks the heat read\n" +
                "• CENTRE OF MASS — tier 5, requires Thermal Scan: unlocks the mass-balance read\n" +
                "• The runtime gates each mode on its node, in that order, so a hotkey press\n" +
                "  with nothing researched says why in one line and does nothing else\n" +
                "• Nothing you have tuned was rewritten: existing nodes keep their cost,\n" +
                "  research time, labels and descriptions on a re-run", "OK");
        }

        private static ResearchNode AuthorNode(string path, string nodeId, string displayName,
            string description, Color iconTint, int tier, int column,
            ref int created, ref int preserved)
        {
            var node = GetOrCreate<ResearchNode>(path, ref created, ref preserved);
            node.nodeId = nodeId;
            if (string.IsNullOrEmpty(node.displayName) || node.displayName == "New Research")
                node.displayName = displayName;
            if (string.IsNullOrEmpty(node.description))
                node.description = description;
            if (node.category == ResearchCategory.Environment && node.subCategory == ResearchSubCategory.General)
            {
                // Fresh node (still at Unity defaults): place it next to Grid Utilities
                // (tier 3, column 7) and let the chain read left-to-right across tier
                // blocks: Integrity Scan at (3,8), Thermal Scan at (4,7), Centre of
                // Mass at (5,7). A node that was already tuned keeps its own spot.
                node.subCategory = ResearchSubCategory.Logistics;
                node.tier = tier;
                node.column = column;
                node.iconTint = iconTint;
            }
            if (node.researchSeconds <= 0.01f)
                node.researchSeconds = tier <= 3 ? 45f : (tier <= 4 ? 60f : 75f);
            if (node.maxRanks < 1) node.maxRanks = 1;
            if (node.cost == null || node.cost.Length == 0)
                node.cost = new ResearchNode.ScienceCost[0];   // lab-time unlock, like the nav utility nodes
            EditorUtility.SetDirty(node);
            return node;
        }

        /// <summary>Append a prerequisite, preserving any the node already has.</summary>
        private static void Link(ResearchNode node, ResearchNode prerequisite)
        {
            var pre = new List<ResearchNode>(node.prerequisites ?? new ResearchNode[0]);
            if (pre.Contains(prerequisite)) return;
            pre.Add(prerequisite);
            node.prerequisites = pre.ToArray();
            EditorUtility.SetDirty(node);
        }

        static T GetOrCreate<T>(string path, ref int created, ref int preserved) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) { preserved++; return a; }
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            created++;
            return a;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash <= 0) return;
            string parent = path.Substring(0, slash), leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
