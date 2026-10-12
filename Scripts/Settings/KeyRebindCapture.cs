// Assets/Scripts/VoxelEngine/Settings/KeyRebindCapture.cs
using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoxelEngine.Settings
{
    /// <summary>
    /// Captures the next key/mouse-button press and reports it as a string code
    /// compatible with GameSettings.SetKey(). Attach as a one-shot helper.
    /// </summary>
    public class KeyRebindCapture : MonoBehaviour
    {
        public Action<string> onCaptured;

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)   { Done("Mouse0"); return; }
                if (Mouse.current.rightButton.wasPressedThisFrame)  { Done("Mouse1"); return; }
                if (Mouse.current.middleButton.wasPressedThisFrame) { Done("Mouse2"); return; }
            }
            if (Keyboard.current != null)
            {
                foreach (Key k in System.Enum.GetValues(typeof(Key)))
                {
                    if (k == Key.None) continue;

                    // Skip any Key enum entry marked [Obsolete] (covers IMESelected and any
                    // future deprecations) without referencing them by name.
                    var member = typeof(Key).GetMember(k.ToString());
                    if (member.Length > 0 &&
                        member[0].IsDefined(typeof(System.ObsoleteAttribute), inherit: false))
                        continue;

                    try
                    {
                        if (Keyboard.current[k].wasPressedThisFrame)
                        {
                            // 17.20.1-dev: hold Ctrl while pressing a bindable key and
                            // the combo "Ctrl+X" is recorded - GameSettings.Read parses
                            // the prefix. Modifier keys themselves (Ctrl, Shift, Alt...)
                            // still bind plain, so Sprint/Slide keeps working.
                            string code = k.ToString();
                            if (!IsModifierName(code) && CtrlHeld()) code = "Ctrl+" + code;
                            Done(code);
                            return;
                        }
                    }
                    catch { /* some Key enum entries can throw on certain layouts */ }
                }
            }
#else
            if (Input.GetMouseButtonDown(0)) { Done("Mouse0"); return; }
            if (Input.GetMouseButtonDown(1)) { Done("Mouse1"); return; }
            if (Input.GetMouseButtonDown(2)) { Done("Mouse2"); return; }
            foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
                if (Input.GetKeyDown(kc))
                {
                    string code = kc.ToString();
                    if (!IsModifierName(code) && CtrlHeld()) code = "Ctrl+" + code;
                    Done(code);
                    return;
                }
#endif
        }

        /// <summary>True for the modifier keys themselves. Name-based so it works on
        /// both input backends: every modifier enum name starts Left/Right (arrows
        /// are the only bindable exceptions), plus the odd AltGr out.</summary>
        private static bool IsModifierName(string code)
            => code == "AltGr"
            || ((code.StartsWith("Left") || code.StartsWith("Right")) && !code.Contains("Arrow"));

        private static bool CtrlHeld()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
#else
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
        }

        private void Done(string code)
        {
            onCaptured?.Invoke(code);
            Destroy(this);
        }
    }
}
