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

        /// <summary>
        /// Clears stale UI blockers without touching cursor state. Use when changing
        /// scenes because the old scene's UI objects are about to be destroyed and
        /// may not get a chance to PopBlock() cleanly.
        /// </summary>
        public static void ClearSceneBlocks()
        {
            _blockCount = 0;
            IsBlocking = false;
            _hardPauseCount = 0;
            IsHardPause = false;
            TextInputActive = false;
            PauseConsumedFrame = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ClearOnSceneLoad()
        {
            ClearSceneBlocks();
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

        // ── Hard pause (pause menu / death screen) ──────────────────────────
        // Soft UIs (inventory, machine panels, wheels, ...) NO LONGER freeze the
        // player — the world keeps running while they are open (multiplayer-ready
        // behaviour). Only a HARD pause stops gameplay: the pause menu also zeros
        // Time.timeScale itself.
        public static bool IsHardPause { get; private set; }
        private static int _hardPauseCount;

        public static void PushHardPause()
        {
            _hardPauseCount++;
            IsHardPause = _hardPauseCount > 0;
        }
        public static void PopHardPause()
        {
            _hardPauseCount = System.Math.Max(0, _hardPauseCount - 1);
            IsHardPause = _hardPauseCount > 0;
        }

        // ── Keyboard capture by UI text fields ──────────────────────────────
        // Set by UI panels while a text field owns the keyboard (recipe search).
        // PlayerController ignores movement / jetpack keys while this is true so
        // typing "w a s d" into the search bar doesn't fly the player around.
        public static bool TextInputActive { get; set; }
    }
}
