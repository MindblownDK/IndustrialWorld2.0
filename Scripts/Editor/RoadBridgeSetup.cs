// Assets/Scripts/VoxelEngine/Editor/RoadBridgeSetup.cs
//
// SETUP STEP 73 — WATER CROSSINGS.
//
// Authored by a setup step and not by hand, for the same reason as every other asset in the column:
// a hand-made prefab is a prefab that exists on one machine, and the road system has to behave the
// same way on a fresh clone as on the one it was built on.
//
// NON-DESTRUCTIVE, like the rest of the setup. Everything here follows one rule: if the asset does
// not exist, create it; if it exists, connect it and leave its authored values alone. The guards
// (`UnsetName`, `UnsetStack`, `UnsetMass`, `<= 0` checks) are not defensive noise — they are what
// lets this step be re-run after someone has hand-tuned a bridge's health without silently writing
// their tuning back to the default. Re-running Step 73 must never destroy work.
//
// WHAT IT AUTHORS
//   • `Mat_RoadBridgeDeck`  — a flat iron-grey deck. A plain colour rather than a generated texture:
//                             a bridge deck is plate, not aggregate, and a flat read is the correct
//                             one at the scale this is seen at. Swap it in the Inspector for
//                             something richer; nothing else depends on it.
//   • `RoadBridge.prefab`   — the deck cell. Same `AsphaltRoad` component as every other road
//                             surface, with `surfaceKind = Bridge`, which is what puts it in deck
//                             mode: flat instead of draped, supported with no ground underneath.
//   • `Block_RoadBridge`    — the block the paver lays for deck cells.
//   • The paver's `bridgeBlock` / `bridgeMaterial` / `bridgeMaterialPerCell` wiring.
//
// WHAT IT DELIBERATELY DOES NOT AUTHOR
//   No crafting recipe and no research node. A bridge block is not something the player crafts and
//   carries: the paver spends the bridge material straight out of the inventory when it lays the
//   crossing, exactly as it spends asphalt. A recipe would create a second, contradictory way to pay
//   for the same structure. If a gate is wanted later it belongs on the paver's bridge capability,
//   not on the block.

using UnityEditor;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class RoadBridgeSetup
    {
        private const string PREFABS = AsphaltRoadSetup.PREFABS_FOLDER;
        private const string BLOCKS  = AsphaltRoadSetup.BLOCKS_FOLDER;

        private const string MAT_DECK   = PREFABS + "/Mat_RoadBridgeDeck.mat";
        private const string BRIDGE_PREFAB = PREFABS + "/RoadBridge.prefab";
        private const string BRIDGE_BLOCK  = BLOCKS  + "/Block_RoadBridge.asset";

        /// <summary>Deck cell size, in metres. Matches the wide carriageway cell so a crossing is
        /// the same width as the road it carries and the paver never has to change cell size
        /// mid-corridor, which would put a seam straight across the deck.</summary>
        private const float DECK_CELL = 4f;

        /// <summary>Bridge material per deck cell. Iron plate at six a cell against ten asphalt for
        /// a sixteen-square-metre carriageway cell: crossing water costs roughly six times the
        /// pavement per cell, which is the point. A bridge should be a decision.</summary>
        private const int BRIDGE_MATERIAL_PER_CELL = 6;

        /// <summary>Deck blocks are structural, so they are heavy and tough. Both values are only
        /// written when unset — see the note at the top of the file.</summary>
        private const float DECK_MASS   = 140f;
        private const int   DECK_HEALTH = 900;

        [MenuItem("Tools/Voxel Engine/Run Step 73 (Water Crossings)", priority = 73)]
        public static void RunStep73Menu() => RunStep73();

        public static void RunStep73()
        {
            Debug.Log("[RoadBridgeSetup] Step 73 - Water Crossings started.");
            int created = 0, preserved = 0;

            // ── 1) Deck material ──────────────────────────────────────────
            var deckMat = AssetDatabase.LoadAssetAtPath<Material>(MAT_DECK);
            if (deckMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                deckMat = new Material(shader);
                // Iron plate, slightly lighter and bluer than the asphalt so a crossing reads as a
                // different structure at a glance rather than as road that happens to float.
                SetBaseColour(deckMat, new Color(0.40f, 0.43f, 0.47f));
                SetMetallic(deckMat, 0.75f);
                SetSmoothness(deckMat, 0.45f);
                AssetDatabase.CreateAsset(deckMat, MAT_DECK);
                created++;
            }
            else preserved++;

            // ── 2) Deck prefab ────────────────────────────────────────────
            // Authored through the same helper as the asphalt and cobble prefabs, so a deck cell is
            // a road cell in every respect that matters: same mesh builder, same collider, same
            // surface registration, same wear decals. The crack and pothole overlays are reused
            // rather than re-authored — a deck that never shows damage would be the one road
            // surface that cannot wear out, and that is a lie the simulation should not tell.
            var crackMat   = AssetDatabase.LoadAssetAtPath<Material>(AsphaltRoadSetup.MAT_CRACK);
            var potholeMat = AssetDatabase.LoadAssetAtPath<Material>(AsphaltRoadSetup.MAT_POTHOLE);
            var deck = AsphaltRoadSetup.AuthorRoadPrefab(
                BRIDGE_PREFAB, "RoadBridge", DECK_CELL,
                deckMat, deckMat, crackMat, potholeMat,
                forceMaterials: false, ref created, ref preserved);

            // The one thing that makes it a bridge. Set after the prefab is saved, through the same
            // helper the pathway uses, because it edits the serialised prefab rather than a live
            // instance — setting it on `deck` here would be setting it on a reference that is about
            // to be discarded.
            AsphaltRoadSetup.SetSurfaceKind(BRIDGE_PREFAB, RoadSurfaceKind.Bridge);

            // ── 3) Block ──────────────────────────────────────────────────
            var bridgeBlock = AsphaltRoadSetup.GetOrCreate<BlockItem>(BRIDGE_BLOCK, ref created, ref preserved);
            bridgeBlock.itemId = "block_road_bridge";
            if (AsphaltRoadSetup.UnsetName(bridgeBlock.displayName)) bridgeBlock.displayName = "Bridge Deck";
            bridgeBlock.description =
                "A deck cell for crossing water. Laid by the road paver where the line reaches a " +
                "stream or a river, held up on piers rather than draped over the ground, so the " +
                "road runs straight over instead of diving in. Built from iron and stone: it has " +
                "to hold itself up over nothing, and that is why it costs what it costs.";
            bridgeBlock.iconTint = new Color(0.40f, 0.43f, 0.47f);
            bridgeBlock.category = "Building";
            bridgeBlock.placedPrefab = deck;
            bridgeBlock.gridSize = Vector3Int.one;
            bridgeBlock.allowStacking = false;
            if (AsphaltRoadSetup.UnsetStack(bridgeBlock.maxStack))  bridgeBlock.maxStack = 100;
            if (AsphaltRoadSetup.UnsetMass(bridgeBlock.massPerUnit)) bridgeBlock.massPerUnit = DECK_MASS;
            if (bridgeBlock.blockHealth <= 0) bridgeBlock.blockHealth = DECK_HEALTH;
            if (bridgeBlock.miningTier  <= 0) bridgeBlock.miningTier  = 2;   // iron structure, not rubble
            EditorUtility.SetDirty(bridgeBlock);

            // ── 4) Wire the paver ─────────────────────────────────────────
            // The paver is what lays these. Wired here rather than in the Inspector so a fresh
            // clone gets a paver that can cross water without anyone remembering to fill three
            // fields in.
            var paver = AssetDatabase.LoadAssetAtPath<RoadPaverTool>(AsphaltRoadSetup.PAVER_ITEM);
            if (paver != null)
            {
                paver.bridgeBlock = bridgeBlock;
                // Iron plate where the column makes it, stone where it does not. Either is a
                // legitimate deck material; a paver with no bridge material at all simply refuses
                // to cross water, which is handled and reported rather than crashing.
                if (paver.bridgeMaterial == null)
                    paver.bridgeMaterial = AsphaltRoadSetup.FindItem("Item_IronPlate")
                                        ?? AsphaltRoadSetup.FindItem("Item_Stone");
                if (paver.bridgeMaterialPerCell <= 0) paver.bridgeMaterialPerCell = BRIDGE_MATERIAL_PER_CELL;
                EditorUtility.SetDirty(paver);
                if (paver.bridgeMaterial == null)
                    Debug.LogWarning("[RoadBridgeSetup] No iron plate or stone item found - the paver " +
                                     "will refuse water crossings until bridgeMaterial is set.");
            }
            else
            {
                Debug.LogWarning("[RoadBridgeSetup] " + AsphaltRoadSetup.PAVER_ITEM + " not found. " +
                                 "Run Step 72 (Asphalt Roads) first, then re-run Step 73 to wire the " +
                                 "paver's bridge fields.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[RoadBridgeSetup] Step 73 - Water Crossings complete. " +
                      created + " created, " + preserved + " preserved.");
        }

        // URP and Standard disagree on which properties hold a base colour and a gloss value, and
        // both have to work: setting only `_Color` on a URP material gives a white bridge.
        private static void SetBaseColour(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
        }

        private static void SetMetallic(Material m, float v)
        {
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", v);
        }

        private static void SetSmoothness(Material m, float v)
        {
            if (m.HasProperty("_Glossiness"))     m.SetFloat("_Glossiness", v);
            if (m.HasProperty("_Smoothness"))     m.SetFloat("_Smoothness", v);
        }
    }
}
