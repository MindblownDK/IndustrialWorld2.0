// Assets/Scripts/VoxelEngine/UI/UIState.cs
using UnityEngine;

namespace VoxelEngine.UI
{
    /// <summary>
    /// Global flag set by ANY full-screen UI panel (inventory, container view, pause menu).
    /// Player movement / interaction code checks this every frame to know when to ignore input.
    /// Also drives cursor lock/visibility so we have ONE source of truth.
    /// </summary>
    public static class UIState
    {
        public static bool IsBlocking { get; private set; }
        private static int _blockCount;

        // One-frame guards for input that was already handled by another UI panel.
        // Set TRUE when the inventory closes via Escape, so the pause menu can skip
        // its own Escape check that same frame.
        public static int PauseConsumedFrame { get; set; } = -1;
        public static bool PauseConsumedThisFrame =>
            PauseConsumedFrame == UnityEngine.Time.frameCount;

        public static void PushBlock()
        {
            _blockCount++;
            UpdateState();
        }
        public static void PopBlock()
        {
            _blockCount = System.Math.Max(0, _blockCount - 1);
            UpdateState();
        }
        public static void Reset()
        {
            _blockCount = 0;
            UpdateState();
        }

        private static void UpdateState()
        {
            IsBlocking = _blockCount > 0;
            if (IsBlocking)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
            }
        }
    }
}
