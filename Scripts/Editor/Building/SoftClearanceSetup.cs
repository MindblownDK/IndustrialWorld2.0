#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Non-destructive Step 126 (18.13.0-dev) - the soft-clearance authoring pass
    /// deferred by 17.44.0-dev.
    ///
    /// 17.44.0-dev added <see cref="BlockItem.softClearance"/> (default OFF, so nothing
    /// existing changed): a soft decorative item may brush past another soft item -
    /// railing meets trim meets lamp - while soft-versus-hard is still refused. This
    /// step opts the decorative lamp and signage families in by setting
    /// softClearance = true on exactly those assets. It never unsets the flag, never
    /// touches any other field, saves only the assets it changed, and reports
    /// marked / already-soft / missing per item id, so running it twice is a no-op
    /// ("0 marked").
    ///
    /// Grid-mounted decorative items (grid light, both spotlight sizes, camera,
    /// radar beacon) are GridBlockItem, not BlockItem: the flag does not exist there
    /// and grid placement is cell-based, so they are logged as skipped by design.
    /// </summary>
    public static class SoftClearanceSetup
    {
        // Decorative families confirmed for the pass: lamp/light + signage.
        private static readonly string[] TargetItemIds =
        {
            "block_light",            // VoxelEngineAssets/Power/Blocks/Block_Light
            "block_ledstripfactory",  // VoxelEngineAssets/Factory/Items/Block_LEDStripFactory
            "railsignal",             // VoxelEngineAssets/Blocks/Block_RailSignal
            "hangingdepartureboard",  // VoxelEngineAssets/Blocks/Block_HangingDepartureBoard
            "nixiereadout",           // VoxelEngineAssets/Blocks/Block_NixieReadout
            "displaycabinet",         // VoxelEngineAssets/Blocks/Block_DisplayCabinet
        };

        // Grid-mounted decorative items: GridBlockItem has no softClearance field.
        private static readonly string[] SkippedGridItemIds =
        {
            "gitem_gridlightblock", "gitem_largegridspotlight",
            "gitem_dualgridspotlightsmall", "gitem_dualgridspotlightlarge",
            "camera_block", "stationary_radar_beacon",
        };

        public static void RunStep126()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 126 - Soft-clearance authoring pass (18.13.0-dev) started.");
            Debug.Log("[VoxelEngineSetupWindow] Step 126 - Non-destructive: sets softClearance = true on the decorative lamp/signage BlockItems only; never unsets, never touches other fields.");

            var wanted = new HashSet<string>(TargetItemIds);
            var found = new HashSet<string>();
            int marked = 0, already = 0;
            var changed = new List<BlockItem>();

            foreach (var guid in AssetDatabase.FindAssets("t:BlockItem"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
                if (item == null || string.IsNullOrEmpty(item.itemId)) continue;
                if (!wanted.Contains(item.itemId)) continue;
                found.Add(item.itemId);
                if (item.softClearance) { already++; continue; }
                item.softClearance = true;
                EditorUtility.SetDirty(item);
                changed.Add(item);
                marked++;
                Debug.Log($"[VoxelEngineSetupWindow] Step 126 - softClearance = true on '{item.itemId}' ({path}).");
            }

            if (changed.Count > 0) AssetDatabase.SaveAssets();

            var missing = new List<string>();
            foreach (var id in TargetItemIds)
                if (!found.Contains(id)) missing.Add(id);

            Debug.Log($"[VoxelEngineSetupWindow] Step 126 - done: {marked} marked, {already} already soft, {missing.Count} missing" +
                      (missing.Count > 0 ? " (" + string.Join(", ", missing) + ")" : "") + ".");
            foreach (var id in SkippedGridItemIds)
                Debug.Log($"[VoxelEngineSetupWindow] Step 126 - skipped '{id}': GridBlockItem has no softClearance field (grid placement is cell-based).");
            if (marked == 0 && missing.Count == 0)
                Debug.Log("[VoxelEngineSetupWindow] Step 126 - nothing to do: every target is already soft. Idempotent.");
        }
    }
}
#endif
