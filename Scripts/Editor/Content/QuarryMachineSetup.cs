// Assets/Scripts/VoxelEngine/EditorTools/QuarryMachineSetup.cs
//
// 17.27.0-dev - THE QUARRY MACHINE.
//
// The quarry used to be a single grey cube: `MakePref` gave it one 2 x 2.4 x 2 box whose
// centre sat on the pivot, so half of the "machine" was under the ground, its tape and
// frame were measured from that sunk box, and there was nothing on it to read or to aim
// power at. It is now a large, detailed, hand-composed industrial machine - crawler
// tracks, painted chassis, gantry, cutter wheel with teeth, spoil conveyor, cab, stacks,
// floodlights, hazard stripes - and it carries two things the player asked for:
//
//   * a POWER INLET on the front-left with its own labelled, glowing socket, so a cable
//     laid against the face has an obvious place to bond, and
//   * a front MULTIMETER (screen + needle dial + state lamp) driven by
//     QuarryMultimeter: live draw, maximum draw, current depth and the reason there is
//     no power (no cable / no supply).
//
// The root pivot is the machine's ground contact plane and the prefab declares it
// (PlacementPivotAnchor), so the machine seats on the surface the ghost showed and its
// tape/frame are measured from the ground instead of from a hidden centre. Collision is
// four volumes - body, tow blade, power inlet and cutter wheel - so the fittings on the
// front are solid while the panel and the inlet stay walkable and readable.
//
// NON-DESTRUCTIVE: the prefab is rebuilt in place (same path, same GUID, so every placed
// quarry and the Block_Quarry item keep their references), materials are reused exactly as
// they are found once they exist, and the machine's power tuning is set to the range a
// machine of this size can actually take a cable anywhere on. Existing placed quarries
// keep their own saved upgrade modules, output contents and progress: only the visual body
// and the component wiring are authored here.
//
// Run: Tools > Voxel Engine > Voxel Engine Setup > Factory, Industry & Power >
//      "Rebuild the Quarry machine and its front multimeter (17.27.0-dev)".

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.EditorTools
{
    public static class QuarryMachineSetup
    {
        private const string SurvivalRoot  = "Assets/VoxelEngineAssets/Survival";
        private const string PrefabFolder  = SurvivalRoot + "/MiscPrefabs";
        private const string DefaultPrefab = PrefabFolder + "/Quarry.prefab";
        private const string BlockPath     = SurvivalRoot + "/MiscBlocks/Block_Quarry.asset";

        public static void RunStep121()
        {
            string path = ResolvePrefabPath();
            var prefab = Build(path);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "The quarry prefab could not be written to '" + path + "'.\n" +
                    "Run 'Build survival and industrial logistics content' first, then this step.", "OK");
                return;
            }

            var block = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.BlockItem>(BlockPath);
            if (block != null && block.placedPrefab != prefab)
            {
                block.placedPrefab = prefab;
                EditorUtility.SetDirty(block);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[QuarryMachineSetup] Rebuilt the quarry machine at '" + path +
                      "' with tracks, cutter wheel, conveyor, cab, front power inlet and front multimeter. " +
                      "Placed quarries pick the new body up on the next load.");
        }

        /// <summary>The item's own prefab reference wins, so a user who moved the machine
        /// keeps it; otherwise the wizard's usual path is used.</summary>
        private static string ResolvePrefabPath()
        {
            var block = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.BlockItem>(BlockPath);
            if (block != null && block.placedPrefab != null)
            {
                string path = AssetDatabase.GetAssetPath(block.placedPrefab);
                if (!string.IsNullOrEmpty(path)) return path;
            }
            return DefaultPrefab;
        }

        public static GameObject Build(string path)
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                Debug.LogWarning("[QuarryMachineSetup] '" + PrefabFolder + "' does not exist yet - run " +
                                 "'Build survival and industrial logistics content' first.");
                return null;
            }

            var root = new GameObject("Quarry");

            var body   = Mat("Body",   new Color(0.19f, 0.21f, 0.25f), 0.55f, 0.35f);
            var paint  = Mat("Paint",  new Color(0.87f, 0.44f, 0.09f), 0.25f, 0.42f);
            var steel  = Mat("Steel",  new Color(0.55f, 0.58f, 0.62f), 0.75f, 0.45f);
            var dark   = Mat("Dark",   new Color(0.08f, 0.08f, 0.09f), 0.30f, 0.25f);
            var rubber = Mat("Rubber", new Color(0.05f, 0.05f, 0.06f), 0.05f, 0.15f);
            var hazard = Mat("Hazard", new Color(0.95f, 0.76f, 0.10f), 0.20f, 0.40f);
            var copper = Mat("Copper", new Color(0.72f, 0.42f, 0.18f), 0.85f, 0.55f);
            var glass  = Mat("Glass",  new Color(0.10f, 0.20f, 0.28f, 0.55f), 0.20f, 0.85f);
            var screenMat = Mat("Screen", new Color(0.015f, 0.03f, 0.04f), 0.10f, 0.60f);
            var dialFace  = Mat("Dial",   new Color(0.90f, 0.88f, 0.80f), 0.05f, 0.30f);
            var lampWarm  = Mat("LampWarm", new Color(1f, 0.86f, 0.60f), 0f, 0.40f, new Color(1f, 0.72f, 0.35f));
            var lampCyan  = Mat("LampCyan", new Color(0.35f, 0.92f, 1f), 0f, 0.40f, new Color(0.30f, 0.85f, 1f));

            var t = root.transform;

            // ═══ CRAWLER TRACKS ════════════════════════════════════════
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 3.35f;
                Box(t, "Track" + side, rubber, new Vector3(x, 0.55f, -0.30f), new Vector3(1.50f, 1.10f, 6.80f));
                Box(t, "TrackFrame" + side, steel, new Vector3(x - side * 0.55f, 1.05f, -0.30f), new Vector3(0.55f, 0.70f, 6.20f));
                for (int i = -2; i <= 2; i++)
                    Cyl(t, "Wheel" + side + "_" + i, steel, new Vector3(x, 0.55f, -0.30f + i * 1.45f),
                        1.00f, 0.26f, new Vector3(0f, 0f, 90f));
                Box(t, "TrackGuard" + side, paint, new Vector3(x, 1.22f, 2.95f), new Vector3(1.62f, 0.20f, 1.30f));
            }

            // ═══ CHASSIS DECK ══════════════════════════════════════════
            Box(t, "Deck", steel, new Vector3(0f, 1.35f, -0.45f), new Vector3(8.40f, 0.50f, 5.90f));
            Box(t, "DeckSkirtFront", paint, new Vector3(0f, 1.20f, 2.42f), new Vector3(7.60f, 0.60f, 0.24f));
            Box(t, "DeckSkirtBack", paint, new Vector3(0f, 1.20f, -3.30f), new Vector3(7.60f, 0.60f, 0.24f));
            Box(t, "ChassisFrameL", dark, new Vector3(-4.18f, 1.30f, -0.45f), new Vector3(0.22f, 0.70f, 5.40f));
            Box(t, "ChassisFrameR", dark, new Vector3(4.18f, 1.30f, -0.45f), new Vector3(0.22f, 0.70f, 5.40f));
            // Rear counterweight + tow eyes
            Box(t, "Counterweight", dark, new Vector3(0f, 1.95f, -3.55f), new Vector3(6.80f, 1.30f, 0.70f));
            for (int side = -1; side <= 1; side += 2)
                Box(t, "TowEye" + side, steel, new Vector3(side * 2.60f, 1.90f, -3.95f), new Vector3(0.35f, 0.35f, 0.35f), new Vector3(45f, 0f, 0f));

            // ═══ MAIN BODY ═════════════════════════════════════════════
            Box(t, "Body", body, new Vector3(0f, 2.90f, -0.55f), new Vector3(7.20f, 2.60f, 4.30f));
            Box(t, "BodyTop", steel, new Vector3(0f, 4.26f, -0.55f), new Vector3(7.00f, 0.26f, 4.10f));
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 3.66f;
                Box(t, "SidePanel" + side, paint, new Vector3(x, 3.15f, 0.55f), new Vector3(0.10f, 1.60f, 2.60f));
                Box(t, "SideStripe" + side, hazard, new Vector3(x + side * 0.05f, 2.28f, 0.55f), new Vector3(0.06f, 0.22f, 2.60f));
                for (int v = 0; v < 5; v++)
                    Box(t, "Vent" + side + "_" + v, dark, new Vector3(x + side * 0.04f, 3.80f, 1.55f - v * 0.42f),
                        new Vector3(0.08f, 0.30f, 0.26f));
                // Access hatch with hinges
                Box(t, "Hatch" + side, steel, new Vector3(x + side * 0.06f, 2.60f, -1.30f), new Vector3(0.08f, 1.00f, 1.40f));
                Box(t, "HatchHandle" + side, dark, new Vector3(x + side * 0.14f, 2.60f, -0.72f), new Vector3(0.06f, 0.30f, 0.10f));
            }

            // ═══ GANTRY ════════════════════════════════════════════════
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 2.55f;
                Box(t, "GantryPostF" + side, steel, new Vector3(x, 5.30f, 1.55f), new Vector3(0.34f, 2.10f, 0.34f));
                Box(t, "GantryPostR" + side, steel, new Vector3(x, 5.30f, -0.95f), new Vector3(0.34f, 2.10f, 0.34f));
                Box(t, "GantryBrace" + side, dark, new Vector3(x, 5.05f, 0.30f), new Vector3(0.14f, 0.14f, 2.60f), new Vector3(28f, 0f, 0f));
            }
            Box(t, "GantryBeamF", steel, new Vector3(0f, 6.28f, 1.55f), new Vector3(5.60f, 0.32f, 0.32f));
            Box(t, "GantryBeamR", steel, new Vector3(0f, 6.28f, -0.95f), new Vector3(5.60f, 0.32f, 0.32f));
            for (int i = 0; i < 7; i++)   // hazard chevrons on the front beam
                Box(t, "GantryStripe" + i, (i % 2 == 0) ? hazard : dark,
                    new Vector3(-2.40f + i * 0.80f, 6.00f, 1.55f), new Vector3(0.80f, 0.22f, 0.24f));

            // ═══ BOOM + CUTTER WHEEL ═══════════════════════════════════
            Box(t, "Boom", body, new Vector3(0f, 4.10f, 2.55f), new Vector3(0.90f, 0.55f, 4.10f), new Vector3(-14f, 0f, 0f));
            Box(t, "BoomRib", steel, new Vector3(0f, 4.48f, 2.55f), new Vector3(0.30f, 0.24f, 3.90f), new Vector3(-14f, 0f, 0f));
            for (int side = -1; side <= 1; side += 2)   // hydraulic rams
                Cyl(t, "Ram" + side, steel, new Vector3(side * 1.05f, 4.15f, 1.30f), 0.24f, 3.10f, new Vector3(72f, 0f, 0f));

            var wheel = new Vector3(0f, 2.95f, 4.35f);
            Cyl(t, "CutterWheel", body, wheel, 3.70f, 0.60f, new Vector3(0f, 0f, 90f));
            Cyl(t, "CutterHub", steel, wheel, 0.90f, 0.80f, new Vector3(0f, 0f, 90f));
            Box(t, "CutterGuard", paint, new Vector3(0f, 4.92f, 4.10f), new Vector3(0.90f, 0.26f, 2.70f), new Vector3(12f, 0f, 0f));
            for (int i = 0; i < 18; i++)
            {
                float a = i * (360f / 18f);
                float rad = a * Mathf.Deg2Rad;
                var pos = new Vector3(0f, wheel.y + Mathf.Sin(rad) * 1.92f, wheel.z + Mathf.Cos(rad) * 1.92f);
                Box(t, "Tooth" + i, (i % 2 == 0) ? hazard : steel, pos, new Vector3(0.66f, 0.16f, 0.30f), new Vector3(-a, 0f, 0f));
            }
            for (int i = 0; i < 6; i++)   // spokes
            {
                float a = i * 60f;
                float rad = a * Mathf.Deg2Rad;
                var pos = new Vector3(0f, wheel.y + Mathf.Sin(rad) * 0.95f, wheel.z + Mathf.Cos(rad) * 0.95f);
                Box(t, "Spoke" + i, steel, pos, new Vector3(0.72f, 0.22f, 1.60f), new Vector3(-a, 0f, 0f));
            }

            // ═══ SPOIL CONVEYOR + DISCHARGE ════════════════════════════
            Box(t, "Belt", dark, new Vector3(1.75f, 3.55f, 0.90f), new Vector3(1.15f, 0.14f, 6.20f), new Vector3(-21f, 0f, 0f));
            Box(t, "BeltRailL", steel, new Vector3(1.15f, 3.68f, 0.90f), new Vector3(0.12f, 0.34f, 6.20f), new Vector3(-21f, 0f, 0f));
            Box(t, "BeltRailR", steel, new Vector3(2.35f, 3.68f, 0.90f), new Vector3(0.12f, 0.34f, 6.20f), new Vector3(-21f, 0f, 0f));
            for (int i = 0; i < 6; i++)
                Box(t, "Cleat" + i, rubber, new Vector3(1.75f, 3.72f - i * 0.34f, 2.30f - i * 0.88f), new Vector3(1.05f, 0.10f, 0.16f), new Vector3(-21f, 0f, 0f));
            for (int i = 0; i < 3; i++)
                Cyl(t, "BeltStrut" + i, steel, new Vector3(2.15f, 2.55f, 1.90f - i * 2.10f), 0.16f, 2.10f, new Vector3(18f, 0f, 0f));
            Box(t, "DischargeChute", steel, new Vector3(1.75f, 4.95f, -2.45f), new Vector3(1.30f, 1.20f, 1.30f));
            Box(t, "ChuteMouth", dark, new Vector3(1.75f, 4.50f, -3.05f), new Vector3(1.05f, 0.80f, 0.10f));
            Box(t, "ChuteHood", paint, new Vector3(1.75f, 5.60f, -2.45f), new Vector3(1.55f, 0.18f, 1.55f));

            // ═══ CAB + STACKS + LIGHTS ═════════════════════════════════
            Box(t, "Cab", glass, new Vector3(2.95f, 4.95f, -1.45f), new Vector3(2.00f, 1.70f, 1.90f));
            Box(t, "CabPostL", steel, new Vector3(1.95f, 4.95f, -1.45f), new Vector3(0.14f, 1.80f, 0.16f));
            Box(t, "CabPostR", steel, new Vector3(3.95f, 4.95f, -1.45f), new Vector3(0.14f, 1.80f, 0.16f));
            Box(t, "CabRoof", steel, new Vector3(2.95f, 5.88f, -1.45f), new Vector3(2.25f, 0.16f, 2.15f));
            Box(t, "CabSeat", dark, new Vector3(2.95f, 4.35f, -1.65f), new Vector3(0.70f, 0.55f, 0.60f));
            Box(t, "CabConsole", dark, new Vector3(2.95f, 4.40f, -0.75f), new Vector3(1.30f, 0.30f, 0.40f));
            Sphere(t, "BeaconLamp", lampWarm, new Vector3(3.75f, 6.05f, -1.45f), 0.28f);

            Cyl(t, "StackA", dark, new Vector3(-2.55f, 5.95f, -2.05f), 0.44f, 3.10f, Vector3.zero);
            Cyl(t, "StackB", dark, new Vector3(-1.45f, 6.20f, -2.05f), 0.38f, 3.70f, Vector3.zero);
            Cyl(t, "StackRingA", steel, new Vector3(-2.55f, 7.20f, -2.05f), 0.56f, 0.18f, Vector3.zero);
            Cyl(t, "StackRingB", steel, new Vector3(-1.45f, 7.78f, -2.05f), 0.50f, 0.18f, Vector3.zero);

            for (int i = -1; i <= 1; i += 2)   // floodlight bar
            {
                Box(t, "FloodHousing" + i, dark, new Vector3(i * 1.35f, 6.50f, 1.70f), new Vector3(0.60f, 0.40f, 0.30f));
                Box(t, "FloodLens" + i, lampWarm, new Vector3(i * 1.35f, 6.50f, 1.87f), new Vector3(0.50f, 0.30f, 0.06f));
                Box(t, "FloodHousingR" + i, dark, new Vector3(i * 2.60f, 6.20f, -1.20f), new Vector3(0.50f, 0.34f, 0.26f));
                Box(t, "FloodLensR" + i, lampWarm, new Vector3(i * 2.60f, 6.20f, -1.36f), new Vector3(0.42f, 0.26f, 0.06f));
            }
            Box(t, "LightBar", steel, new Vector3(0f, 6.50f, 1.70f), new Vector3(2.10f, 0.16f, 0.16f));

            // ═══ FRONT: TOW BLADE, POWER INLET, MULTIMETER ═════════════
            Box(t, "Blade", paint, new Vector3(0f, 1.05f, 2.95f), new Vector3(7.80f, 0.80f, 0.40f), new Vector3(16f, 0f, 0f));
            Box(t, "BladeEdge", steel, new Vector3(0f, 0.72f, 3.10f), new Vector3(7.80f, 0.18f, 0.44f), new Vector3(16f, 0f, 0f));
            for (int i = 0; i < 8; i++)
                Box(t, "BladeStripe" + i, (i % 2 == 0) ? hazard : dark,
                    new Vector3(-3.15f + i * 0.90f, 1.05f, 3.20f), new Vector3(0.90f, 0.62f, 0.10f), new Vector3(16f, 0f, 0f));

            // -- power inlet: a protruding, labelled, lit socket on the front-left --
            var inlet = new Vector3(-3.15f, 2.55f, 1.95f);
            Box(t, "PowerInletHousing", steel, inlet, new Vector3(1.10f, 1.10f, 0.80f));
            Box(t, "PowerInletCollar", hazard, new Vector3(inlet.x, inlet.y + 0.66f, inlet.z), new Vector3(1.26f, 0.18f, 0.94f));
            Box(t, "PowerInletSocket", lampCyan, new Vector3(inlet.x, inlet.y, inlet.z + 0.44f), new Vector3(0.62f, 0.62f, 0.06f));
            Box(t, "PowerInletRimTop", dark, new Vector3(inlet.x, inlet.y + 0.38f, inlet.z + 0.46f), new Vector3(0.78f, 0.10f, 0.10f));
            Box(t, "PowerInletRimBottom", dark, new Vector3(inlet.x, inlet.y - 0.38f, inlet.z + 0.46f), new Vector3(0.78f, 0.10f, 0.10f));
            Box(t, "PowerInletRimLeft", dark, new Vector3(inlet.x - 0.38f, inlet.y, inlet.z + 0.46f), new Vector3(0.10f, 0.78f, 0.10f));
            Box(t, "PowerInletRimRight", dark, new Vector3(inlet.x + 0.38f, inlet.y, inlet.z + 0.46f), new Vector3(0.10f, 0.78f, 0.10f));
            Box(t, "BusBar", copper, new Vector3(0.60f, 2.30f, 1.72f), new Vector3(6.30f, 0.14f, 0.14f));
            Text(t, "InletLabel", new Vector3(inlet.x - 0.62f, inlet.y + 0.78f, inlet.z - 0.10f), "POWER IN",
                0.28f, TextAnchor.LowerLeft, Color.white);

            // -- multimeter panel: screen + needle dial + state lamp --
            Box(t, "MeterCabinet", dark, new Vector3(1.05f, 3.45f, 1.58f), new Vector3(3.10f, 2.10f, 0.30f));
            Box(t, "MeterBezel", body, new Vector3(1.05f, 3.45f, 1.76f), new Vector3(2.80f, 1.80f, 0.12f));
            Box(t, "MeterScreenBack", screenMat, new Vector3(1.55f, 3.55f, 1.84f), new Vector3(1.75f, 1.15f, 0.06f));
            Box(t, "MeterScreenLipTop", steel, new Vector3(1.55f, 4.14f, 1.86f), new Vector3(1.85f, 0.08f, 0.10f));
            Box(t, "MeterScreenLipBottom", steel, new Vector3(1.55f, 2.96f, 1.86f), new Vector3(1.85f, 0.08f, 0.10f));

            var dialCentre = new Vector3(0.05f, 3.42f, 1.88f);
            Cyl(t, "MeterDial", dark, dialCentre, 0.72f, 0.10f, new Vector3(90f, 0f, 0f));
            Cyl(t, "MeterDialFace", dialFace, new Vector3(dialCentre.x, dialCentre.y, dialCentre.z + 0.04f), 0.62f, 0.06f, new Vector3(90f, 0f, 0f));
            for (int i = -1; i <= 1; i++)
            {
                float a = i * 52f * Mathf.Deg2Rad;
                Box(t, "MeterTick" + i, dark,
                    new Vector3(dialCentre.x + Mathf.Sin(a) * 0.25f, dialCentre.y + Mathf.Cos(a) * 0.25f, dialCentre.z + 0.06f),
                    new Vector3(0.035f, 0.09f, 0.02f), new Vector3(0f, 0f, -i * 52f));
            }
            var needlePivot = new GameObject("MeterNeedlePivot");
            needlePivot.transform.SetParent(t, false);
            needlePivot.transform.localPosition = new Vector3(dialCentre.x, dialCentre.y, dialCentre.z + 0.06f);
            Box(needlePivot.transform, "MeterNeedle", paint, new Vector3(0f, 0.16f, 0f), new Vector3(0.05f, 0.30f, 0.03f));
            Sphere(needlePivot.transform, "MeterNeedleHub", dark, Vector3.zero, 0.10f);

            var lampGo = Sphere(t, "MeterStateLamp", lampWarm, new Vector3(0.05f, 3.95f, 1.92f), 0.26f);
            Box(t, "MeterLampHood", steel, new Vector3(0.05f, 4.10f, 1.94f), new Vector3(0.34f, 0.12f, 0.16f));

            var textScreen = Text(t, "MeterReadout", new Vector3(0.72f, 4.02f, 1.90f), "QUARRY DRILL  IDLE",
                0.16f, TextAnchor.UpperLeft, new Color(0.82f, 1f, 0.88f));
            textScreen.lineSpacing = 1.12f;

            // ═══ NAME PLATE + REAR SERVICE DOOR ════════════════════════
            Box(t, "NamePlate", steel, new Vector3(-1.30f, 4.42f, 1.66f), new Vector3(2.10f, 0.46f, 0.06f));
            Text(t, "NamePlateText", new Vector3(-2.28f, 4.24f, 1.72f), "QUARRY DRILL Mk III",
                0.20f, TextAnchor.LowerLeft, new Color(0.10f, 0.10f, 0.11f));
            Box(t, "RearDoor", steel, new Vector3(0f, 2.95f, -2.72f), new Vector3(3.00f, 2.20f, 0.14f));
            Box(t, "RearDoorFrame", paint, new Vector3(0f, 4.12f, -2.72f), new Vector3(3.40f, 0.22f, 0.18f));
            Box(t, "RearHandle", dark, new Vector3(1.30f, 2.60f, -2.84f), new Vector3(0.30f, 0.10f, 0.10f));
            for (int i = 0; i < 6; i++)
                Box(t, "RearVent" + i, dark, new Vector3(-2.60f, 3.70f - i * 0.30f, -2.76f), new Vector3(1.40f, 0.16f, 0.10f));

            // ═══ RIGGING ═══════════════════════════════════════════════
            // One solid collider on the root: the body the player walks into, the volume
            // a cable bonds to and the volume the tape/frame is measured from.
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 2.60f, -0.55f);
            collider.size = new Vector3(8.60f, 5.20f, 4.30f);
            // The front fittings are solid on their own, so the space between the body,
            // the inlet and the meter stays walkable and the readouts are readable from
            // arm's length instead of through an invisible wall.
            var bladeCollider = root.AddComponent<BoxCollider>();
            bladeCollider.center = new Vector3(0f, 1.05f, 2.95f);
            bladeCollider.size = new Vector3(7.80f, 0.86f, 0.60f);
            var inletCollider = root.AddComponent<BoxCollider>();
            inletCollider.center = new Vector3(-3.15f, 2.55f, 1.95f);
            inletCollider.size = new Vector3(1.10f, 1.16f, 0.86f);
            // The root IS the ground contact plane, so the build system may seat this
            // prefab by its pivot instead of guessing from collider bounds.
            root.AddComponent<VoxelEngine.Building.PlacementPivotAnchor>();
            // Second collider for the cutter wheel: the body box stops short of it, and a
            // 3.7 m steel disc should not be something the player can walk through. Its
            // bottom sits 1.1 m up, so nothing blocks the ground under the boom.
            var wheelCollider = root.AddComponent<BoxCollider>();
            wheelCollider.center = new Vector3(0f, 2.95f, 4.35f);
            wheelCollider.size = new Vector3(0.80f, 3.70f, 3.70f);

            var pc = root.AddComponent<PowerConsumer>();
            pc.wattsPerSecond = 500f;
            // A machine this size must be able to take a cable ANYWHERE on its body: the
            // old 2 m radius was measured pivot-to-pivot and missed cables laid against
            // the flank, which is exactly how the drill ended up permanently unpowered.
            pc.connectRadius = 6f;

            var ports = root.AddComponent<PortConfig>();
            ports.ports = new[]
            {
                new PortConfig.FacePort { face = CubeFace.PosX, direction = PortDirection.Output, networkType = PortNetworkType.Any,   enabled = true },
                new PortConfig.FacePort { face = CubeFace.NegX, direction = PortDirection.None,   networkType = PortNetworkType.Any,   enabled = true },
                new PortConfig.FacePort { face = CubeFace.PosY, direction = PortDirection.None,   networkType = PortNetworkType.Any,   enabled = true },
                new PortConfig.FacePort { face = CubeFace.NegY, direction = PortDirection.None,   networkType = PortNetworkType.Any,   enabled = true },
                new PortConfig.FacePort { face = CubeFace.NegZ, direction = PortDirection.None,   networkType = PortNetworkType.Any,   enabled = true },
                // Power comes in at the front, where the lit inlet and the meter are.
                new PortConfig.FacePort { face = CubeFace.PosZ, direction = PortDirection.Input,  networkType = PortNetworkType.Power, enabled = true },
            };

            var quarry = root.AddComponent<Quarry>();
            quarry.defaultSize = 16;
            quarry.baseMineInterval = 0.5f;
            quarry.quarryTier = 3;
            quarry.maximumMiningDepth = 64;
            // The pit must start in front of the CUTTER, not in front of an invisible
            // pivot: the wheel reaches ~6 m forward, so the frame begins just past it.
            quarry.forwardOffset = 4.2f;
            quarry.frameBuildInterval = 0.06f;
            quarry.frameColor = new Color(0.18f, 0.19f, 0.22f);
            quarry.frameAccentColor = new Color(0.92f, 0.52f, 0.08f, 0.85f);
            quarry.outputSlots = 6;
            quarry.EnsureUpgrades();

            var meter = root.AddComponent<QuarryMultimeter>();
            meter.screen = textScreen;
            meter.needle = needlePivot.transform;
            meter.stateLamp = lampGo.GetComponent<Renderer>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) return null;
            // Report the machine's real size so the setup log is checkable.
            var bounds = prefab.GetComponent<BoxCollider>();
            if (bounds != null)
                Debug.Log($"[QuarryMachineSetup] Quarry body: {bounds.size.x:0.0} x {bounds.size.y:0.0} x {bounds.size.z:0.0} m, "
                          + "root pivot at ground level, inlet + meter on the front face.");
            return prefab;
        }

        // ═══ authoring helpers ════════════════════════════════════════

        private static GameObject Box(Transform parent, string name, Material mat, Vector3 pos, Vector3 scale, Vector3? euler = null)
            => Part(parent, name, mat, pos, scale, euler, PrimitiveType.Cube);

        /// <summary>Unity's cylinder primitive is 1 unit across and 2 units tall, so a
        /// diameter is the X/Z scale and a height is half the Y scale - the same reading
        /// the rest of the setup tool uses.</summary>
        private static GameObject Cyl(Transform parent, string name, Material mat, Vector3 pos, float diameter, float height, Vector3 euler)
            => Part(parent, name, mat, pos, new Vector3(diameter, height * 0.5f, diameter), euler, PrimitiveType.Cylinder);

        private static GameObject Sphere(Transform parent, string name, Material mat, Vector3 pos, float diameter)
            => Part(parent, name, mat, pos, Vector3.one * diameter, null, PrimitiveType.Sphere);

        /// <summary>Detail parts never carry their own collider - the root's four boxes are
        /// the machine's solid volume - and never cast shadows where it would be noise.</summary>
        private static GameObject Part(Transform parent, string name, Material mat, Vector3 pos, Vector3 scale,
            Vector3? euler, PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (euler.HasValue) go.transform.localEulerAngles = euler.Value;

            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }
            return go;
        }

        private static TextMesh Text(Transform parent, string name, Vector3 pos, string content,
            float glyphHeight, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = content;
            tm.fontSize = 48;
            tm.characterSize = glyphHeight / 4.8f;   // world glyph height = fontSize * characterSize * 0.1
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.LowerLeft || anchor == TextAnchor.UpperLeft
                ? TextAlignment.Left
                : TextAlignment.Center;
            tm.color = color;
            var renderer = tm.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.shadowCastingMode = ShadowCastingMode.Off;
            VoxelEngine.Rendering.WorldTextMaterial.Apply(tm);
            return tm;
        }

        /// <summary>Materials are created once and then LEFT ALONE: a re-run never reverts
        /// a colour the player has adjusted in the inspector.</summary>
        private static Material Mat(string name, Color color, float metallic, float smoothness, Color? emission = null)
        {
            string path = $"{PrefabFolder}/Mat_Quarry_{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "Mat_Quarry_" + name };
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", color.a < 0.999f ? 1f : 0f);   // glass is transparent
            if (color.a < 0.999f)
            {
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            if (emission.HasValue && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
#endif
