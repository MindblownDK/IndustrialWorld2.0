// Assets/Scripts/VoxelEngine/Editor/PlayerControllerEditor.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.Player;
using VoxelEngine.Settings;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Adds a "Fly Mode" toggle button at the top of the PlayerController inspector.
    /// Reads/writes GameSettings.FlyMode (PlayerPrefs-backed) so the choice persists.
    /// </summary>
    [CustomEditor(typeof(PlayerController))]
    public class PlayerControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Quick Toggles", EditorStyles.boldLabel);

            bool isFly = GameSettings.FlyMode;
            string label = isFly ? "💨  FLY MODE: ON  (click to disable)"
                                 : "🚶  FLY MODE: OFF (click to enable)";
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = isFly ? new Color(0.4f, 0.85f, 0.4f) : new Color(0.85f, 0.45f, 0.45f);
            if (GUILayout.Button(label, GUILayout.Height(34)))
            {
                GameSettings.FlyMode = !isFly;
            }
            GUI.backgroundColor = prev;
            EditorGUILayout.HelpBox(
                "Fly mode lets the player move freely in 3D (great for testing). " +
                "It can also be toggled in-game with the 'ToggleFly' keybind (default F) " +
                "or in Settings > Camera. The state is saved in PlayerPrefs.",
                MessageType.Info);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Component", EditorStyles.boldLabel);
            DrawDefaultInspector();
        }
    }
}
#endif
