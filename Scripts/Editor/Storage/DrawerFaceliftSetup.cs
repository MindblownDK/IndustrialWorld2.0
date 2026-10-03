#if UNITY_EDITOR
// Assets/Scripts/Editor/Storage/DrawerFaceliftSetup.cs
//
// Step 110 - Drawer facelift (14.42.0).
// Rebuilds the Storage Drawer and Drawer Controller prefab visuals as proper
// industrial steel furniture - framed fronts, recessed faces, handles, bolts
// and an LED fill strip - the Functional-Storage silhouette in a metallic
// theme instead of wood. Non-destructive: only the generated display children
// are rebuilt and the component references are re-pointed; the collider, the
// components and all authored values survive. Also refreshes the drawer item
// description (front-face insertion now accepts placeable blocks too).

using UnityEngine;
using UnityEditor;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class DrawerFaceliftSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string MATS = ASSET_ROOT + "/Storage/Network/Prefabs/Mats";

        private static int _created, _touched;

        public static void RunStep110()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 110 - Drawer facelift started.");
            _created = 0; _touched = 0;
            EnsureFolder(ASSET_ROOT + "/Storage");
            EnsureFolder(ASSET_ROOT + "/Storage/Network");
            EnsureFolder(ASSET_ROOT + "/Storage/Network/Prefabs");
            EnsureFolder(MATS);

            FaceliftDrawer(ASSET_ROOT + "/Survival/StorageBlocks/Block_StorageDrawer.asset");
            FaceliftController(ASSET_ROOT + "/Survival/StorageBlocks/Block_StorageDrawerController.asset");

            // 14.42.0: the handheld terminal now has a home in the COMMS &
            // NAVIGATION equipment card - teach it in the description.
            var handheld = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                ASSET_ROOT + "/Storage/Network/Items/Item_WirelessTerminal.asset");
            if (handheld != null)
            {
                handheld.description = "Handheld uplink to your mass-storage network. Equip it in the COMMS & " +
                                       "NAVIGATION slot of your equipment console (carrying it in the backpack " +
                                       "also works) and stand inside a powered Wireless Transmitter's range to " +
                                       "browse, store and craft from the network remotely. The network's " +
                                       "Security Block decides who gets a signal.";
                EditorUtility.SetDirty(handheld);
                _touched++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Step 110] Drawer facelift complete - {_created} created, {_touched} touched. " +
                      "Storage Drawer and Drawer Controller now wear framed steel fronts.");
        }

        // ────────────────────────────────────────────────────────────
        //  STORAGE DRAWER
        // ────────────────────────────────────────────────────────────

        private static void FaceliftDrawer(string blockAssetPath)
        {
            var blockItem = AssetDatabase.LoadAssetAtPath<BlockItem>(blockAssetPath);
            if (blockItem == null || blockItem.placedPrefab == null)
            {
                Debug.LogWarning($"[Step 110] Missing asset: {blockAssetPath} - run the storage steps first.");
                return;
            }

            blockItem.description = "Single-item industrial drawer in a steel chassis. Right-click the front " +
                                    "to insert the held stack - any item, placeable blocks included; left-click " +
                                    "the front to take items out (Shift for a full stack). Stores 2,000 items " +
                                    "by default and accepts 12 upgrades. The LED strip shows how full it is.";
            EditorUtility.SetDirty(blockItem);

            string prefabPath = AssetDatabase.GetAssetPath(blockItem.placedPrefab);
            if (string.IsNullOrEmpty(prefabPath)) return;
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var drawer = root.GetComponent<VoxelEngine.Storage.StorageDrawer>();
                if (drawer == null)
                {
                    Debug.LogWarning("[Step 110] StorageDrawer component missing on prefab - skipped.");
                    return;
                }

                StripOldDisplay(root, "RecessedFrontPanel", "ItemIcon", "AmountText");
                ClearGenerated(root);
                SwapBodyMaterial(root, BodyMat());

                var vis = new GameObject("Generated_DrawerFace");
                vis.transform.SetParent(root.transform, false);
                var frameMat = FrameMat();
                var faceMat = FaceMat();
                var darkMat = DarkMat();
                var accentMat = AccentMat();

                // Front frame - four proud steel bars around the face rim.
                Box(vis, "FrameTop", new Vector3(0f, 0.465f, 0.51f), new Vector3(1.17f, 0.075f, 0.05f), frameMat);
                Box(vis, "FrameBottom", new Vector3(0f, -0.465f, 0.51f), new Vector3(1.17f, 0.075f, 0.05f), frameMat);
                Box(vis, "FrameLeft", new Vector3(-0.5475f, 0f, 0.51f), new Vector3(0.075f, 1.0f, 0.05f), frameMat);
                Box(vis, "FrameRight", new Vector3(0.5475f, 0f, 0.51f), new Vector3(0.075f, 1.0f, 0.05f), frameMat);

                // Recessed drawer face.
                Box(vis, "Face", new Vector3(0f, 0.025f, 0.503f), new Vector3(0.96f, 0.80f, 0.025f), faceMat);

                // Icon backplate - the dark label area the item sprite sits on.
                Box(vis, "IconPlate", new Vector3(0f, 0.14f, 0.518f), new Vector3(0.46f, 0.46f, 0.014f), darkMat);

                // Corner bolts.
                Box(vis, "BoltTL", new Vector3(-0.50f, 0.42f, 0.522f), Vector3.one * 0.05f, darkMat);
                Box(vis, "BoltTR", new Vector3(0.50f, 0.42f, 0.522f), Vector3.one * 0.05f, darkMat);
                Box(vis, "BoltBL", new Vector3(-0.50f, -0.42f, 0.522f), Vector3.one * 0.05f, darkMat);
                Box(vis, "BoltBR", new Vector3(0.50f, -0.42f, 0.522f), Vector3.one * 0.05f, darkMat);

                // LED fill strip - the runtime lerps this renderer's colour
                // from dark steel to teal as the drawer fills up.
                var strip = Box(vis, "FillStrip", new Vector3(0f, -0.295f, 0.515f), new Vector3(0.80f, 0.045f, 0.02f), darkMat);
                drawer.fillRenderer = strip.GetComponent<Renderer>();

                // Handle - bar on two stand-offs, low on the face.
                Box(vis, "HandleStandL", new Vector3(-0.17f, -0.395f, 0.525f), new Vector3(0.05f, 0.05f, 0.045f), frameMat);
                Box(vis, "HandleStandR", new Vector3(0.17f, -0.395f, 0.525f), new Vector3(0.05f, 0.05f, 0.045f), frameMat);
                Box(vis, "HandleBar", new Vector3(0f, -0.395f, 0.548f), new Vector3(0.44f, 0.055f, 0.045f), accentMat);

                // Item icon + amount text, re-pointed on the component.
                var iconGo = new GameObject("Generated_ItemIcon");
                iconGo.transform.SetParent(root.transform, false);
                iconGo.transform.localPosition = new Vector3(0f, 0.14f, 0.532f);
                drawer.itemIconRenderer = iconGo.AddComponent<SpriteRenderer>();

                var txtGo = new GameObject("Generated_AmountText");
                txtGo.transform.SetParent(root.transform, false);
                txtGo.transform.localPosition = new Vector3(0f, -0.145f, 0.535f);
                var txt = txtGo.AddComponent<TextMesh>();
                txt.anchor = TextAnchor.MiddleCenter;
                txt.alignment = TextAlignment.Center;
                txt.characterSize = 0.09f;
                txt.fontSize = 48;
                txt.text = "EMPTY";
                drawer.amountText = txt;

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                _touched++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ────────────────────────────────────────────────────────────
        //  DRAWER CONTROLLER
        // ────────────────────────────────────────────────────────────

        private static void FaceliftController(string blockAssetPath)
        {
            var blockItem = AssetDatabase.LoadAssetAtPath<BlockItem>(blockAssetPath);
            if (blockItem == null || blockItem.placedPrefab == null)
            {
                Debug.LogWarning($"[Step 110] Missing asset: {blockAssetPath} - run the storage steps first.");
                return;
            }
            string prefabPath = AssetDatabase.GetAssetPath(blockItem.placedPrefab);
            if (string.IsNullOrEmpty(prefabPath)) return;

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                ClearGenerated(root);
                SwapBodyMaterial(root, BodyMat());

                var vis = new GameObject("Generated_ControllerFace");
                vis.transform.SetParent(root.transform, false);
                var frameMat = FrameMat();
                var faceMat = FaceMat();
                var darkMat = DarkMat();
                var accentMat = AccentMat();

                // Controller body is 1.0 x 1.2 x 0.8 - front face sits at z = +0.4.
                Box(vis, "FrameTop", new Vector3(0f, 0.565f, 0.41f), new Vector3(1.02f, 0.075f, 0.05f), frameMat);
                Box(vis, "FrameBottom", new Vector3(0f, -0.565f, 0.41f), new Vector3(1.02f, 0.075f, 0.05f), frameMat);
                Box(vis, "FrameLeft", new Vector3(-0.475f, 0f, 0.41f), new Vector3(0.075f, 1.2f, 0.05f), frameMat);
                Box(vis, "FrameRight", new Vector3(0.475f, 0f, 0.41f), new Vector3(0.075f, 1.2f, 0.05f), frameMat);

                Box(vis, "Face", new Vector3(0f, 0f, 0.403f), new Vector3(0.84f, 1.04f, 0.025f), faceMat);

                // The controller "eye": dark plate with an emissive teal core
                // rotated 45 degrees - the network heart of the drawer bank.
                Box(vis, "CorePlate", new Vector3(0f, 0.16f, 0.418f), new Vector3(0.40f, 0.40f, 0.015f), darkMat);
                var core = Box(vis, "Core", new Vector3(0f, 0.16f, 0.43f), new Vector3(0.20f, 0.20f, 0.02f), accentMat);
                core.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var coreRing = Box(vis, "CoreRing", new Vector3(0f, 0.16f, 0.424f), new Vector3(0.30f, 0.30f, 0.012f), frameMat);
                coreRing.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

                // Status LEDs.
                for (int i = 0; i < 3; i++)
                    Box(vis, $"Led_{i}", new Vector3(-0.14f + i * 0.14f, 0.50f, 0.418f),
                        new Vector3(0.05f, 0.022f, 0.012f), accentMat);

                // Link port bezel near the floor of the face.
                Box(vis, "PortBezel", new Vector3(0f, -0.38f, 0.415f), new Vector3(0.24f, 0.18f, 0.022f), darkMat);
                Box(vis, "PortGlow", new Vector3(0f, -0.38f, 0.428f), new Vector3(0.14f, 0.06f, 0.012f), accentMat);

                // Corner bolts.
                Box(vis, "BoltTL", new Vector3(-0.42f, 0.52f, 0.422f), Vector3.one * 0.05f, darkMat);
                Box(vis, "BoltTR", new Vector3(0.42f, 0.52f, 0.422f), Vector3.one * 0.05f, darkMat);
                Box(vis, "BoltBL", new Vector3(-0.42f, -0.52f, 0.422f), Vector3.one * 0.05f, darkMat);
                Box(vis, "BoltBR", new Vector3(0.42f, -0.52f, 0.422f), Vector3.one * 0.05f, darkMat);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                _touched++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ────────────────────────────────────────────────────────────
        //  HELPERS
        // ────────────────────────────────────────────────────────────

        private static Material BodyMat() => GetMat("Mat_DrawerSteel_Body", new Color(0.50f, 0.53f, 0.57f), 0.80f);
        private static Material FrameMat() => GetMat("Mat_DrawerSteel_Frame", new Color(0.22f, 0.24f, 0.27f), 0.75f);
        private static Material FaceMat() => GetMat("Mat_DrawerSteel_Face", new Color(0.35f, 0.38f, 0.42f), 0.55f);
        private static Material DarkMat() => GetMat("Mat_DrawerSteel_Dark", new Color(0.09f, 0.10f, 0.12f), 0.30f);
        private static Material AccentMat() => GetMat("Mat_DrawerSteel_Accent", new Color(0.10f, 0.78f, 0.65f), 0.35f, emissive: true);

        private static GameObject Box(GameObject parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            return go;
        }

        private static void StripOldDisplay(GameObject root, params string[] names)
        {
            foreach (var n in names)
            {
                var t = root.transform.Find(n);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
        }

        private static void ClearGenerated(GameObject root)
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        private static void SwapBodyMaterial(GameObject root, Material mat)
        {
            var mesh = root.transform.Find("Mesh");
            if (mesh == null) return;
            var r = mesh.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
        }

        private static Material GetMat(string name, Color color, float metallic, bool emissive = false)
        {
            string path = MATS + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { _touched++; return existing; }
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name, color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", metallic > 0.6f ? 0.62f : 0.40f);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 1.8f);
            }
            AssetDatabase.CreateAsset(mat, path);
            _created++;
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
