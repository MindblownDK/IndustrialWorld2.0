// Assets/Scripts/Editor/Environment/PollutionScentValidation.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.Environment;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Scene-free check of the industrial scent rules. It does not create, mutate
    /// or remove prefabs, items, recipes or research.
    /// </summary>
    public static class PollutionScentValidation
    {
        /// <summary>Called from the Voxel Engine Setup screen. Not a Tools menu item.</summary>
        public static void Run()
        {
            int failures = 0;
            failures += Require(Mathf.Approximately(PollutionScentRules.EscalatedRange(48f, 0f), 48f), "calm range");
            failures += Require(Mathf.Approximately(PollutionScentRules.EscalatedRange(48f, 0.5f), 72f), "mid range");
            failures += Require(Mathf.Approximately(PollutionScentRules.EscalatedRange(48f, 1f), 96f), "severe range");
            failures += Require(Mathf.Approximately(PollutionScentRules.SearchCeiling(48f), 96f), "search ceiling");
            failures += Require(Mathf.Approximately(PollutionScentRules.EscalatedRange(20f, 1f), 40f), "authored scale");
            failures += Require(PollutionScentRules.PackSize(0.24f) == 1, "watch pack");
            failures += Require(PollutionScentRules.PackSize(0.25f) == 2, "stressed pack");
            failures += Require(PollutionScentRules.PackSize(0.49f) == 2, "upper stressed pack");
            failures += Require(PollutionScentRules.PackSize(0.50f) == 3, "declining pack");
            failures += Require(PollutionScentRules.PackSize(1f) == PollutionScentRules.MaxPackSize, "pack ceiling");
            failures += Require(Mathf.Approximately(PollutionScentRules.EscalatedDetectionRange(16f, 0f), 16f), "calm detect");
            failures += Require(Mathf.Approximately(PollutionScentRules.EscalatedDetectionRange(16f, 1f), 28f), "severe detect");
            failures += Require(PollutionScentRules.AmbushCount(0.24f, 2) == 0, "watch ambush");
            failures += Require(PollutionScentRules.AmbushCount(0.25f, 1) == 0, "lone scout ambush");
            failures += Require(PollutionScentRules.AmbushCount(0.25f, 2) == 1, "stressed ambush");
            failures += Require(PollutionScentRules.AmbushCount(1f, 3) == 1, "ambush ceiling");
            failures += Require(!PollutionScentRules.RaidsLogistics(0.24f) && PollutionScentRules.RaidsLogistics(0.25f), "raid band");
            failures += Require(!PollutionScentRules.TryAmbushPoint(Vector3.zero, new Vector3(0f, 0f, 10f), Vector3.up, 0, out _), "close ambush rejected");
            failures += Require(PollutionScentRules.TryAmbushPoint(Vector3.zero, new Vector3(0f, 0f, 40f), Vector3.up, 0, out Vector3 ambush)
                && Mathf.Approximately(ambush.x, 8f) && Mathf.Approximately(ambush.z, 22f), "ambush point");

            Vector3 up = Vector3.up;
            Vector3 north = Vector3.forward;
            failures += Require(PollutionScentRules.FormatDirection(Vector3.zero, new Vector3(0f, 0f, 10f), up, north) == "N · 10 m", "north bearing");
            failures += Require(PollutionScentRules.FormatDirection(Vector3.zero, new Vector3(10f, 0f, 0f), up, north) == "E · 10 m", "east bearing");
            failures += Require(PollutionScentRules.FormatDirection(Vector3.zero, new Vector3(0f, 0f, -10f), up, north) == "S · 10 m", "south bearing");
            failures += Require(PollutionScentRules.FormatDirection(Vector3.zero, new Vector3(-10f, 0f, 0f), up, north) == "W · 10 m", "west bearing");
            failures += Require(PollutionScentRules.FormatDirection(Vector3.zero, new Vector3(10f, 0f, 10f), up, north) == "NE · 14 m", "northeast bearing");
            failures += Require(PollutionScentRules.FormatDirection(Vector3.zero, new Vector3(3f, 0f, 0f), up, north, "AT CELL") == "AT CELL", "here label");

            if (failures == 0)
            {
                Debug.Log("[PollutionScentValidation] PASS. Range, pack, ambush, raid and bearing rules match 17.3.0.");
                EditorUtility.DisplayDialog("Pollution Scent Rules",
                    "PASS\n\nRange, pack, ambush, raid and bearing rules match 17.3.0.\nNo content was changed.",
                    "OK");
            }
            else
            {
                Debug.LogError("[PollutionScentValidation] FAIL. " + failures + " check(s) failed.");
                EditorUtility.DisplayDialog("Pollution Scent Rules",
                    failures + " check(s) failed. See the Console.",
                    "OK");
            }
        }

        private static int Require(bool condition, string name)
        {
            if (condition) return 0;
            Debug.LogError("[PollutionScentValidation] Failed: " + name);
            return 1;
        }
    }
}
#endif
