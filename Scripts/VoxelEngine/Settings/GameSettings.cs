// Assets/Scripts/VoxelEngine/Settings/GameSettings.cs
//
// Static, PlayerPrefs-backed settings store. Hardened against bad/missing
// keybinds: any unknown / empty / "None" code is treated as "do nothing".
//
// On launch, MigrateIfNeeded() repairs old saves so every action has a default.

using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoxelEngine.Settings
{
    /// <summary>Catalogue of every action the player can rebind.</summary>
    public enum InputAction
    {
        Forward, Back, Left, Right, Up, Down,
        Sprint, Crouch, Jump,
        Mine, Build, Pause, ToggleFly,
        Inventory, Interact, BuildToggleGrid, BuildRotate, Research, BuildWheel, Map,
        Hotbar1, Hotbar2, Hotbar3, Hotbar4, Hotbar5,
        Hotbar6, Hotbar7, Hotbar8, Hotbar9, Hotbar0,
        EnterCockpit, ExitCockpit
    }

    public static class GameSettings
    {
        // ----- PlayerPrefs keys -----
        private const string K_FOV          = "ve.fov";
        private const string K_SENS         = "ve.mouseSens";
        private const string K_INVERT_Y     = "ve.invertY";
        private const string K_VOL          = "ve.masterVolume";
        private const string K_VOL_MUSIC    = "ve.musicVolume";
        private const string K_VOL_SFX      = "ve.sfxVolume";
        private const string K_AUTOSAVE     = "ve.autosaveSeconds";
        private const string K_QUALITY      = "ve.quality";
        private const string K_DISPLAY      = "ve.display";
        private const string K_FULLSCREEN   = "ve.fullscreenMode";
        private const string K_VSYNC        = "ve.vsync";
        private const string K_RES_W        = "ve.resW";
        private const string K_RES_H        = "ve.resH";
        private const string K_REFRESH      = "ve.refresh";
        private const string K_VIEWDIST     = "ve.viewDistance";
        private const string K_KEY_PREFIX   = "ve.key.";
        private const string K_FLY_MODE     = "ve.flyMode";
        private const string K_VERSION      = "ve.settingsVersion";

        // Bump this when default keybinds change to force a one-time migration
        // that fills in missing or invalid bindings on old saves.
        private const int    CURRENT_VERSION = 9;

        // ----- defaults -----
        public const float DEFAULT_FOV       = 75f;
        public const float DEFAULT_SENS      = 0.15f;
        public const bool  DEFAULT_INVERT_Y  = false;
        public const float DEFAULT_VOLUME    = 1.0f;
        public const float DEFAULT_MUSIC     = 0.7f;
        public const float DEFAULT_SFX       = 1.0f;
        public const int   DEFAULT_QUALITY   = -1;
        public const int   DEFAULT_DISPLAY   = 0;
        public const int   DEFAULT_VSYNC     = 1;
        public const int   DEFAULT_VIEWDIST  = 6;
        public const int   DEFAULT_AUTOSAVE  = 30;   // seconds; 0 = disabled
        // Discrete autosave choices offered in the UI (seconds). 0 = "Off".
        public static readonly int[] AUTOSAVE_CHOICES = { 0, 15, 30, 60, 120, 300 };

        public static event Action OnChanged;

        // ----- Display / Quality -----
        public static int   Quality          { get => PlayerPrefs.GetInt(K_QUALITY, DEFAULT_QUALITY); set { PlayerPrefs.SetInt(K_QUALITY, value); Apply(); } }
        public static int   DisplayIndex     { get => PlayerPrefs.GetInt(K_DISPLAY, DEFAULT_DISPLAY); set { PlayerPrefs.SetInt(K_DISPLAY, value); Apply(); } }
        public static int   VSync            { get => PlayerPrefs.GetInt(K_VSYNC, DEFAULT_VSYNC); set { PlayerPrefs.SetInt(K_VSYNC, value); Apply(); } }
        public static FullScreenMode FullscreenMode
        {
            get => (FullScreenMode)PlayerPrefs.GetInt(K_FULLSCREEN, (int)FullScreenMode.FullScreenWindow);
            set { PlayerPrefs.SetInt(K_FULLSCREEN, (int)value); Apply(); }
        }
        public static int ResolutionWidth   { get => PlayerPrefs.GetInt(K_RES_W, Screen.currentResolution.width);  set { PlayerPrefs.SetInt(K_RES_W, value);  Apply(); } }
        public static int ResolutionHeight  { get => PlayerPrefs.GetInt(K_RES_H, Screen.currentResolution.height); set { PlayerPrefs.SetInt(K_RES_H, value);  Apply(); } }
        public static int RefreshRate       { get => PlayerPrefs.GetInt(K_REFRESH, (int)Mathf.Round((float)Screen.currentResolution.refreshRateRatio.value)); set { PlayerPrefs.SetInt(K_REFRESH, value); Apply(); } }

        // ----- Camera / Input -----
        public static float Fov              { get => PlayerPrefs.GetFloat(K_FOV, DEFAULT_FOV);   set { PlayerPrefs.SetFloat(K_FOV, value);  Notify(); } }
        public static float MouseSensitivity { get => PlayerPrefs.GetFloat(K_SENS, DEFAULT_SENS); set { PlayerPrefs.SetFloat(K_SENS, value); Notify(); } }
        public static bool  InvertY          { get => PlayerPrefs.GetInt(K_INVERT_Y, DEFAULT_INVERT_Y ? 1 : 0) != 0; set { PlayerPrefs.SetInt(K_INVERT_Y, value ? 1 : 0); Notify(); } }

        // ----- Gameplay -----
        public static bool  FlyMode          { get => PlayerPrefs.GetInt(K_FLY_MODE, 0) != 0; set { PlayerPrefs.SetInt(K_FLY_MODE, value ? 1 : 0); Notify(); } }

        // ----- Audio -----
        public static float MasterVolume     { get => PlayerPrefs.GetFloat(K_VOL, DEFAULT_VOLUME); set { PlayerPrefs.SetFloat(K_VOL, value); Apply(); } }
        public static float MusicVolume      { get => PlayerPrefs.GetFloat(K_VOL_MUSIC, DEFAULT_MUSIC); set { PlayerPrefs.SetFloat(K_VOL_MUSIC, value); Apply(); } }
        public static float SfxVolume        { get => PlayerPrefs.GetFloat(K_VOL_SFX, DEFAULT_SFX); set { PlayerPrefs.SetFloat(K_VOL_SFX, value); Apply(); } }

        // ----- Saving -----
        /// <summary>Background autosave cadence in seconds. 0 disables autosave.</summary>
        public static int   AutosaveSeconds  { get => PlayerPrefs.GetInt(K_AUTOSAVE, DEFAULT_AUTOSAVE); set { PlayerPrefs.SetInt(K_AUTOSAVE, value); Notify(); } }

        // ----- Streaming -----
        public static int   ViewDistance     { get => PlayerPrefs.GetInt(K_VIEWDIST, DEFAULT_VIEWDIST); set { PlayerPrefs.SetInt(K_VIEWDIST, value); Notify(); } }

        // ----- Keybinds -----
        public static string GetKey(InputAction a)
        {
            string s = PlayerPrefs.GetString(K_KEY_PREFIX + a, DefaultKey(a));
            return string.IsNullOrEmpty(s) ? DefaultKey(a) : s;
        }
        public static void SetKey(InputAction a, string code)
        {
            if (string.IsNullOrEmpty(code)) code = "None";
            PlayerPrefs.SetString(K_KEY_PREFIX + a, code);
            PlayerPrefs.Save();
            Notify();
        }

        public static string DefaultKey(InputAction a) => a switch
        {
            InputAction.Forward         => "W",
            InputAction.Back            => "S",
            InputAction.Left            => "A",
            InputAction.Right           => "D",
            InputAction.Up              => "Space",
            InputAction.Down            => "LeftCtrl",
            InputAction.Sprint          => "LeftShift",
            InputAction.Crouch          => "C",
            InputAction.Jump            => "Space",
            InputAction.Mine            => "Mouse0",
            InputAction.Build           => "Mouse1",
            InputAction.Pause           => "Escape",
            InputAction.ToggleFly       => "F",
            InputAction.Inventory       => "I",
            InputAction.Interact        => "E",
            InputAction.BuildToggleGrid => "G",
            InputAction.BuildRotate     => "R",
            InputAction.Research        => "Y",
            InputAction.BuildWheel      => "B",
            InputAction.Map             => "M",
            InputAction.Hotbar1         => "Digit1",
            InputAction.Hotbar2         => "Digit2",
            InputAction.Hotbar3         => "Digit3",
            InputAction.Hotbar4         => "Digit4",
            InputAction.Hotbar5         => "Digit5",
            InputAction.Hotbar6         => "Digit6",
            InputAction.Hotbar7         => "Digit7",
            InputAction.Hotbar8         => "Digit8",
            InputAction.Hotbar9         => "Digit9",
            InputAction.Hotbar0         => "Digit0",
            InputAction.EnterCockpit    => "H",
            InputAction.ExitCockpit     => "F",
            _ => "None"
        };

        // ----- Apply / Notify -----
        public static void ApplyAll()
        {
            MigrateIfNeeded();
            Apply();
            Notify();
        }

        // Repairs old saves where new actions had no binding (would default to "None")
        // and rewrites any binding currently stored as "None" / empty back to its default.
        public static void MigrateIfNeeded()
        {
            int saved = PlayerPrefs.GetInt(K_VERSION, 0);
            if (saved >= CURRENT_VERSION) return;

            foreach (InputAction a in System.Enum.GetValues(typeof(InputAction)))
            {
                string current = PlayerPrefs.GetString(K_KEY_PREFIX + a, "");
                if (string.IsNullOrEmpty(current) || current == "None")
                    PlayerPrefs.SetString(K_KEY_PREFIX + a, DefaultKey(a));
            }
            PlayerPrefs.SetInt(K_VERSION, CURRENT_VERSION);
            PlayerPrefs.Save();
            Debug.Log("[GameSettings] Migrated keybinds to version " + CURRENT_VERSION);
        }

        private static void Apply()
        {
            int q = Quality;
            if (q >= 0 && q < QualitySettings.names.Length)
                QualitySettings.SetQualityLevel(q, applyExpensiveChanges: true);

            QualitySettings.vSyncCount = Mathf.Clamp(VSync, 0, 4);

            // Route all three volumes through the AudioManager (AudioMixer when
            // present, AudioListener fallback otherwise).
            VoxelEngine.FX.AudioManager.ApplyVolumes(
                Mathf.Clamp01(MasterVolume), Mathf.Clamp01(MusicVolume), Mathf.Clamp01(SfxVolume));

            int targetDisplay = Mathf.Clamp(DisplayIndex, 0, Mathf.Max(0, Display.displays.Length - 1));
            if (targetDisplay > 0 && targetDisplay < Display.displays.Length && !Display.displays[targetDisplay].active)
                Display.displays[targetDisplay].Activate();

            int rw = ResolutionWidth, rh = ResolutionHeight;
            var fsm = FullscreenMode;
            int curHz = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
            bool resChanged     = rw > 0 && rh > 0 && (rw != Screen.width || rh != Screen.height);
            bool modeChanged    = fsm != Screen.fullScreenMode;
            bool refreshChanged = RefreshRate > 0 && RefreshRate != curHz;
            if (rw > 0 && rh > 0 && (resChanged || modeChanged || refreshChanged))
                Screen.SetResolution(rw, rh, fsm,
                    new RefreshRate { numerator = (uint)Mathf.Max(1, RefreshRate), denominator = 1 });

            PlayerPrefs.Save();
            Notify();
        }

        private static void Notify() => OnChanged?.Invoke();

        // ----- Reset -----
        public static void ResetToDefaults()
        {
            Fov              = DEFAULT_FOV;
            MouseSensitivity = DEFAULT_SENS;
            InvertY          = DEFAULT_INVERT_Y;
            MasterVolume     = DEFAULT_VOLUME;
            MusicVolume      = DEFAULT_MUSIC;
            SfxVolume        = DEFAULT_SFX;
            AutosaveSeconds  = DEFAULT_AUTOSAVE;
            Quality          = DEFAULT_QUALITY;
            DisplayIndex     = DEFAULT_DISPLAY;
            VSync            = DEFAULT_VSYNC;
            ViewDistance     = DEFAULT_VIEWDIST;
            FullscreenMode   = FullScreenMode.FullScreenWindow;
            ResolutionWidth  = Screen.currentResolution.width;
            ResolutionHeight = Screen.currentResolution.height;
            FlyMode          = false;
            // Reset every keybind to its hard-coded default (NOT to whatever was previously saved).
            foreach (InputAction a in System.Enum.GetValues(typeof(InputAction)))
                PlayerPrefs.SetString(K_KEY_PREFIX + a, DefaultKey(a));
            PlayerPrefs.SetInt(K_VERSION, CURRENT_VERSION);
            PlayerPrefs.Save();
            Apply();
            Notify();
        }

        // ============================================================
        //  Input helpers — exception-proof. Anything weird returns false.
        // ============================================================
#if ENABLE_INPUT_SYSTEM
        public static bool IsHeld(InputAction a)        => Read(a, false);
        public static bool WasPressed(InputAction a)    => Read(a, true);

        private static bool Read(InputAction a, bool downEdge)
        {
            string code = GetKey(a);
            if (string.IsNullOrEmpty(code) || code == "None") return false;

            if (code.Length >= 5 && code[0] == 'M' && code[1] == 'o' && code[2] == 'u' && code[3] == 's' && code[4] == 'e')
            {
                if (Mouse.current == null) return false;
                switch (code)
                {
                    case "Mouse0": return downEdge ? Mouse.current.leftButton.wasPressedThisFrame   : Mouse.current.leftButton.isPressed;
                    case "Mouse1": return downEdge ? Mouse.current.rightButton.wasPressedThisFrame  : Mouse.current.rightButton.isPressed;
                    case "Mouse2": return downEdge ? Mouse.current.middleButton.wasPressedThisFrame : Mouse.current.middleButton.isPressed;
                    default: return false;
                }
            }

            if (Keyboard.current == null) return false;
            if (!System.Enum.TryParse<Key>(code, true, out var k)) return false;
            if (k == Key.None) return false;            // Keyboard indexer throws on Key.None
            if ((int)k <= 0)   return false;            // catches any other invalid enum entries

            try
            {
                var btn = Keyboard.current[k];
                if (btn == null) return false;
                return downEdge ? btn.wasPressedThisFrame : btn.isPressed;
            }
            catch (System.ArgumentOutOfRangeException) { return false; }
            catch (System.IndexOutOfRangeException)    { return false; }
            catch (System.NullReferenceException)      { return false; }
        }
#else
        public static bool IsHeld(InputAction a)
        {
            string code = GetKey(a);
            if (string.IsNullOrEmpty(code) || code == "None") return false;
            if (code == "Mouse0") return Input.GetMouseButton(0);
            if (code == "Mouse1") return Input.GetMouseButton(1);
            if (code == "Mouse2") return Input.GetMouseButton(2);
            code = MapToLegacy(code);
            if (!System.Enum.TryParse<KeyCode>(code, true, out var kc)) return false;
            if (kc == KeyCode.None) return false;
            return Input.GetKey(kc);
        }
        public static bool WasPressed(InputAction a)
        {
            string code = GetKey(a);
            if (string.IsNullOrEmpty(code) || code == "None") return false;
            if (code == "Mouse0") return Input.GetMouseButtonDown(0);
            if (code == "Mouse1") return Input.GetMouseButtonDown(1);
            if (code == "Mouse2") return Input.GetMouseButtonDown(2);
            code = MapToLegacy(code);
            if (!System.Enum.TryParse<KeyCode>(code, true, out var kc)) return false;
            if (kc == KeyCode.None) return false;
            return Input.GetKeyDown(kc);
        }
        private static string MapToLegacy(string code)
        {
            if (code != null && code.StartsWith("Digit")) return "Alpha" + code.Substring(5);
            return code;
        }
#endif
    }
}
