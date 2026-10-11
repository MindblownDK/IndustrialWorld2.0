// Assets/Scripts/UI/UiScaleDriver.cs

using UnityEngine;

namespace VoxelEngine.UI
{
    /// <summary>
    /// Watches the screen size and re-applies the UI scale law when it changes.
    ///
    /// GameSettings.OnChanged already covers a resolution picked in Settings. This
    /// covers the two cases that never raise it: a dragged window border and a
    /// monitor switch. Both change Screen.width / Screen.height with no event, and
    /// under the height-anchored law a changed height means a changed scale.
    ///
    /// Created on demand by UiScaleSystem.EnsureRunning. Never place one in a scene
    /// and never add it to a prefab - it is a hidden singleton that dies with play mode.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class UiScaleDriver : MonoBehaviour
    {
        private int _width;
        private int _height;

        /// <summary>Records the screen size to diff against. Called by UiScaleSystem on creation.</summary>
        internal void Initialise(int width, int height)
        {
            _width = width;
            _height = height;
        }

        private void Update()
        {
            int w = Screen.width;
            int h = Screen.height;
            if (w == _width && h == _height) return;

            _width = w;
            _height = h;
            UiScaleSystem.ReapplyAll();
        }

        private void OnDestroy()
        {
            // A domain reload or a play-mode exit leaves the static holder pointing at
            // a destroyed component; clearing it lets the next Apply rebuild one.
            UiScaleSystem.ForgetDriver(this);
        }
    }
}
