// Assets/Scripts/VoxelEngine/Settings/GameSettings.cs
//
// Static, PlayerPrefs-backed settings store. Hardened against bad/missing
// keybinds: any unknown / empty / "None" code is treated as "do nothing".
//
// On launch, MigrateIfNeeded() repairs old saves so every action has a default.

using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoxelEngine.Settings
{
    /// <summary>Catalogue of every action the player can rebind.</summary>
    public enum InputAction
    {
        Forward, Back, Left, Right, Up, Down,
        Sprint, Crouch, Slide, Jump,
        Mine, Build, Pause, ToggleFly,
        RollLeft, RollRight,   // 6DOF flight roll (Q / E) — grid systems style
        Inventory, Interact, BuildToggleGrid, BuildRotate, Research, BuildWheel, DropItem,
        ToolCycle,
        Hotbar1, Hotbar2, Hotbar3, Hotbar4, Hotbar5,
        Hotbar6, Hotbar7, Hotbar8, Hotbar9, Hotbar0,
        EnterCockpit, ExitCockpit,
        WarpDrive,
        TrajectoryCamera,  // predicted flight path overlay while piloting (11.12.0-dev)
        OrbitalMap,        // system-wide orbital map, requires an equipped Orbital Map device (11.13.0-dev)
        LogisticsMap,      // local-surface map of rail, drone, road and base networks (11.16.0-dev)
        ConstructRegistry, // name/classify the piloted construct and commit it to orbit (11.13.0-dev)
        GridInspector,  // the Grid Inspector Overlay hotkey (9.37.0-dev): one key walks OFF → HEAT → DAMAGE → CENTRE OF MASS
        Autopilot,      // fly-to-nav-target cruise control (12.22.0-dev)
        PushToTalk,     // hold to speak on proximity voice (14.20.0-dev)
        Dampeners       // personal inertia dampeners; Ctrl+key locks a relative target (14.62.0-dev)
    }

    /// <summary>How the microphone decides it is your turn to speak.
    /// One control, four states - including Off, so the whole feature is
    /// reachable from a single row in Settings - Audio.</summary>
    public enum VoiceTalkMode
    {
        Off = 0,          // nothing captured, nothing played, no CPU, no bandwidth
        PushToTalk = 1,   // hold the key (the default)
        Toggle = 2,       // tap the same key on, tap it off
        OpenMic = 3       // transmits whenever you are louder than the threshold
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
        private const string K_VOICE_MODE   = "ve.voiceMode";
        private const string K_VOICE_ON     = "ve.voiceEnabled";   // pre-v21, migrated
        // pre-v21, migrated
        private const string K_VOICE_OPEN   = "ve.voiceOpenMic";
        private const string K_VOICE_GATE   = "ve.voiceActivation";
        private const string K_VOICE_VOL    = "ve.voiceVolume";
        private const string K_VOICE_DEV    = "ve.voiceDevice";
        private const string K_LAST_HOST    = "ve.lastHostAddress";
        private const string K_VOICE_MUTED  = "ve.voiceMuted";
        private const string K_VERSION      = "ve.settingsVersion";

        // Bump this when default keybinds change to force a one-time migration
        // that fills in missing or invalid bindings on old saves.
        private const int    CURRENT_VERSION = 24;   // v24: monitor-auto refresh is the default when no explicit override is saved

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
        // Zero means follow the monitor's highest supported mode at the selected resolution.
        public const int   DEFAULT_REFRESH_RATE = 0;
        public const int   DEFAULT_VIEWDIST  = 6;
        public const int   DEFAULT_AUTOSAVE  = 300;  // seconds; 0 = disabled
        public const float DEFAULT_VOICE_VOL = 1.0f;
        public const float DEFAULT_VOICE_GATE = 0.035f;  // RMS; quiet room noise sits well below this
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
        /// <summary>Explicit refresh override in Hz, or zero to follow the monitor.</summary>
        public static int RefreshRate       { get => PlayerPrefs.GetInt(K_REFRESH, DEFAULT_REFRESH_RATE); set { PlayerPrefs.SetInt(K_REFRESH, Mathf.Max(0, value)); Apply(); } }
        /// <summary>The explicit override when set, otherwise the fastest supported mode
        /// matching the selected resolution (falling back to the current display mode).</summary>
        public static int EffectiveRefreshRate
        {
            get
            {
                int selected = RefreshRate;
                return selected > 0
                    ? selected
                    : ResolveMonitorRefreshRate(ResolutionWidth, ResolutionHeight);
            }
        }

        private static int ResolveMonitorRefreshRate(int width, int height)
        {
            int best = 0;
            Resolution[] supported = Screen.resolutions;
            if (supported != null)
            {
                for (int i = 0; i < supported.Length; i++)
                {
                    var mode = supported[i];
                    if (width > 0 && height > 0 && (mode.width != width || mode.height != height)) continue;
                    int hz = Mathf.RoundToInt((float)mode.refreshRateRatio.value);
                    if (hz > best) best = hz;
                }
            }
            if (best <= 0)
                best = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
            return Mathf.Max(1, best);
        }

        // ----- Camera / Input -----
        public static float Fov              { get => PlayerPrefs.GetFloat(K_FOV, DEFAULT_FOV);   set { PlayerPrefs.SetFloat(K_FOV, value);  Notify(); } }
        public static float MouseSensitivity { get => PlayerPrefs.GetFloat(K_SENS, DEFAULT_SENS); set { PlayerPrefs.SetFloat(K_SENS, value); Notify(); } }
        public static bool  InvertY          { get => PlayerPrefs.GetInt(K_INVERT_Y, DEFAULT_INVERT_Y ? 1 : 0) != 0; set { PlayerPrefs.SetInt(K_INVERT_Y, value ? 1 : 0); Notify(); } }

        // ----- Gameplay -----
        // ── FlyMode (14.26.2) ───────────────────────────────────────
        //
        // Whether you are flying RIGHT NOW is player state, not a preference, and it
        // must never have been written to PlayerPrefs. PlayerPrefs is keyed by company
        // and product name, so every copy of the game on one machine shares it - two
        // Editor clones, or a build next to the Editor, are all reading and writing a
        // single value.
        //
        // That turned into a cross-player bug the moment two people played: the fly
        // model kicks a player out of flight when they lose flight permission, and it
        // did so by writing this shared key. So a second player with no jetpack sat
        // there clearing the flag every frame, and the first player - who did have a
        // jetpack - could not stay airborne. Flight appeared to require that EVERYONE
        // owned a jetpack.
        //
        // The live value is per-process now. PlayerPrefs is read once to seed it, so
        // the dev convenience of starting in fly mode survives, and it is only written
        // back when something deliberately saves a preference rather than on every
        // toggle in the air.
        private static bool _flyMode;
        private static bool _flyModeSeeded;

        public static bool FlyMode
        {
            get
            {
                if (!_flyModeSeeded)
                {
                    _flyMode = PlayerPrefs.GetInt(K_FLY_MODE, 0) != 0;
                    _flyModeSeeded = true;
                }
                return _flyMode;
            }
            set
            {
                _flyModeSeeded = true;
                if (_flyMode == value) return;
                _flyMode = value;
                Notify();
            }
        }

        /// <summary>Write the current fly mode out as a saved preference. Only the
        /// settings screen and the editor inspector call this - gameplay must not, or
        /// the shared-key problem above comes straight back.</summary>
        public static void PersistFlyModePreference()
        {
            PlayerPrefs.SetInt(K_FLY_MODE, FlyMode ? 1 : 0);
            PlayerPrefs.Save();
        }
        public static bool  ScreenShake      { get => PlayerPrefs.GetInt("ve_screenshake", 1) != 0; set { PlayerPrefs.SetInt("ve_screenshake", value ? 1 : 0); Notify(); } }
        /// <summary>Developer-only damage bypass. Release players cannot read or set it.</summary>
        public static bool InfiniteHealth
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return PlayerPrefs.GetInt("ve_infinitehealth", 0) != 0;
#else
                return false;
#endif
            }
            set
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                PlayerPrefs.SetInt("ve_infinitehealth", value ? 1 : 0);
                Notify();
#endif
            }
        }

        // ----- Audio -----
        public static float MasterVolume     { get => PlayerPrefs.GetFloat(K_VOL, DEFAULT_VOLUME); set { PlayerPrefs.SetFloat(K_VOL, value); Apply(); } }
        public static float MusicVolume      { get => PlayerPrefs.GetFloat(K_VOL_MUSIC, DEFAULT_MUSIC); set { PlayerPrefs.SetFloat(K_VOL_MUSIC, value); Apply(); } }
        public static float SfxVolume        { get => PlayerPrefs.GetFloat(K_VOL_SFX, DEFAULT_SFX); set { PlayerPrefs.SetFloat(K_VOL_SFX, value); Apply(); } }

        // ----- Proximity voice (14.20.0-dev) -----
        /// <summary>Off / Push To Talk / Toggle / Open Mic. This is the single
        /// source of truth: the two older booleans below are derived from it so
        /// nothing can report a state the mode does not actually have.</summary>
        public static VoiceTalkMode VoiceMode
        {
            get
            {
                int raw = PlayerPrefs.GetInt(K_VOICE_MODE, (int)VoiceTalkMode.PushToTalk);
                return raw < 0 || raw > (int)VoiceTalkMode.OpenMic
                    ? VoiceTalkMode.PushToTalk : (VoiceTalkMode)raw;
            }
            set { PlayerPrefs.SetInt(K_VOICE_MODE, (int)value); Notify(); }
        }

        /// <summary>Master switch for the microphone. Off = nothing is captured
        /// and nothing is played back, so a player who never wants voice pays
        /// no CPU and no bandwidth for it.</summary>
        public static bool  VoiceEnabled     => VoiceMode != VoiceTalkMode.Off;
        /// <summary>True while the gate is the microphone level rather than a key.</summary>
        public static bool  VoiceOpenMic     => VoiceMode == VoiceTalkMode.OpenMic;
        /// <summary>True while the talk key latches instead of being held.</summary>
        public static bool  VoiceToggleToTalk => VoiceMode == VoiceTalkMode.Toggle;
        /// <summary>Open-mic trigger level as microphone RMS, 0.005 - 0.25.</summary>
        public static float VoiceActivation  { get => Mathf.Clamp(PlayerPrefs.GetFloat(K_VOICE_GATE, DEFAULT_VOICE_GATE), 0.005f, 0.25f); set { PlayerPrefs.SetFloat(K_VOICE_GATE, Mathf.Clamp(value, 0.005f, 0.25f)); Notify(); } }
        /// <summary>Playback volume for other players' voices, 0 - 2.</summary>
        public static float VoiceVolume      { get => Mathf.Clamp(PlayerPrefs.GetFloat(K_VOICE_VOL, DEFAULT_VOICE_VOL), 0f, 2f); set { PlayerPrefs.SetFloat(K_VOICE_VOL, Mathf.Clamp(value, 0f, 2f)); Notify(); } }
        /// <summary>Chosen capture device name; empty = the system default.</summary>
        public static string VoiceDevice     { get => PlayerPrefs.GetString(K_VOICE_DEV, ""); set { PlayerPrefs.SetString(K_VOICE_DEV, value ?? ""); Notify(); } }

        // Muted players are keyed by STABLE PLAYER ID, never by name or
        // connection - a mute must survive a rename, a reconnect and a new
        // session (MP-readiness checklist).
        private static HashSet<string> _mutedCache;

        private static HashSet<string> MutedSet()
        {
            if (_mutedCache != null) return _mutedCache;
            _mutedCache = new HashSet<string>();
            string raw = PlayerPrefs.GetString(K_VOICE_MUTED, "");
            if (!string.IsNullOrEmpty(raw))
                foreach (var part in raw.Split(','))
                    if (part.Length > 0) _mutedCache.Add(part);
            return _mutedCache;
        }

        public static bool IsPlayerMuted(string playerId)
            => !string.IsNullOrEmpty(playerId) && MutedSet().Contains(playerId);

        public static void SetPlayerMuted(string playerId, bool muted)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            var set = MutedSet();
            if (muted ? !set.Add(playerId) : !set.Remove(playerId)) return;
            PlayerPrefs.SetString(K_VOICE_MUTED, string.Join(",", set));
            PlayerPrefs.Save();
            Notify();
        }

        // ----- Multiplayer -----
        /// <summary>Last address the player joined from the main menu, so a
        /// friend's address is typed once and not every evening.</summary>
        public static string LastHostAddress
        {
            get => PlayerPrefs.GetString(K_LAST_HOST, "");
            set { PlayerPrefs.SetString(K_LAST_HOST, (value ?? "").Trim()); PlayerPrefs.Save(); }
        }

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
            InputAction.Down            => "C",
            InputAction.Sprint          => "LeftShift",
            InputAction.Crouch          => "C",
            InputAction.Slide           => "LeftAlt",
            InputAction.Jump            => "Space",
            InputAction.Mine            => "Mouse0",
            InputAction.Build           => "Mouse1",
            InputAction.Pause           => "Escape",
            InputAction.ToggleFly       => "F",
            InputAction.RollLeft        => "Q",
            InputAction.RollRight       => "E",
            InputAction.Inventory       => "I",
            InputAction.Interact        => "E",
            InputAction.BuildToggleGrid => "G",
            InputAction.BuildRotate     => "R",
            InputAction.Research        => "Y",
            InputAction.BuildWheel      => "B",
            InputAction.ToolCycle       => "T",

            InputAction.DropItem        => "O",
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
            InputAction.WarpDrive       => "U",
            InputAction.TrajectoryCamera => "J",
            InputAction.OrbitalMap      => "M",
            InputAction.LogisticsMap    => "L",
            InputAction.ConstructRegistry => "N",
            InputAction.GridInspector   => "K",
            InputAction.Autopilot       => "F3",
            // Backquote: the one easy-to-hold key left of the number row that
            // no vehicle, build or map hotkey already claims. Rebindable like
            // everything else, in Settings - Keybinds.
            InputAction.PushToTalk      => "Backquote",
            // Z mirrors the cockpit dampener key: one muscle memory, two contexts —
            // seated it is the SHIP's switch (GridCockpit), on foot it is YOURS.
            InputAction.Dampeners       => "Z",
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

            // v11: Fly/jetpack descent moved from Ctrl to C so Ctrl remains a
            // dedicated placement-rotation modifier. Preserve custom Down binds,
            // but migrate the old default.
            string down = PlayerPrefs.GetString(K_KEY_PREFIX + InputAction.Down, "");
            if (string.IsNullOrEmpty(down) || down == "LeftCtrl" || down == "RightCtrl")
                PlayerPrefs.SetString(K_KEY_PREFIX + InputAction.Down, "C");

            // v12: autosave default is now 5 minutes. Existing profiles that were
            // still on the old 30-second default migrate to 5 minutes; custom
            // values such as Off/15s/1m/2m are preserved.
            int autosave = PlayerPrefs.GetInt(K_AUTOSAVE, 30);
            if (autosave == 30) PlayerPrefs.SetInt(K_AUTOSAVE, DEFAULT_AUTOSAVE);

            // v17: the construct registry took N (the conventional rename key) and the
            // warp drive moved to U. Only migrate profiles still sitting on the old
            // defaults - a player who deliberately rebound either key keeps their choice.
            string warp = PlayerPrefs.GetString(K_KEY_PREFIX + InputAction.WarpDrive, "");
            if (string.IsNullOrEmpty(warp) || warp == "N")
                PlayerPrefs.SetString(K_KEY_PREFIX + InputAction.WarpDrive, "U");
            string registry = PlayerPrefs.GetString(K_KEY_PREFIX + InputAction.ConstructRegistry, "");
            if (string.IsNullOrEmpty(registry) || registry == "U")
                PlayerPrefs.SetString(K_KEY_PREFIX + InputAction.ConstructRegistry, "N");

// v19: autopilot moved off P (landing gear / parking) to F3. Only migrate
            // profiles still sitting on the old default - a deliberate rebind is kept.
            string ap = PlayerPrefs.GetString(K_KEY_PREFIX + InputAction.Autopilot, "");
            if (string.IsNullOrEmpty(ap) || ap == "P")
                PlayerPrefs.SetString(K_KEY_PREFIX + InputAction.Autopilot, "F3");

            // v20: push-to-talk is new; old profiles have no binding for it and
            // the loop above already filled it with the default. Nothing else to do.

            // v21: the voice on/off switch and the push-to-talk/open-mic switch
            // became one four-state mode (Off / Push To Talk / Toggle / Open Mic).
            // Fold the two old booleans into it so a 14.20 profile keeps exactly
            // the behaviour it had, then leave the old keys alone - unread keys
            // cost nothing and a player who rolls a build back keeps their choice.
            if (!PlayerPrefs.HasKey(K_VOICE_MODE))
            {
                bool wasOn   = PlayerPrefs.GetInt(K_VOICE_ON, 1) != 0;
                bool wasOpen = PlayerPrefs.GetInt(K_VOICE_OPEN, 0) != 0;
                PlayerPrefs.SetInt(K_VOICE_MODE, (int)(!wasOn
                    ? VoiceTalkMode.Off
                    : wasOpen ? VoiceTalkMode.OpenMic : VoiceTalkMode.PushToTalk));
            }

            // v23: the generic pass above gives the new Slide action LeftAlt when
            // no valid binding exists; existing Crouch and non-empty custom binds stay.
            // v24: an unset refresh preference now means Monitor (Auto); an explicit
            // saved refresh choice is preserved as a deliberate player override.
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
            int effectiveRefreshRate = RefreshRate > 0
                ? RefreshRate
                : ResolveMonitorRefreshRate(rw, rh);
            bool resChanged     = rw > 0 && rh > 0 && (rw != Screen.width || rh != Screen.height);
            bool modeChanged    = fsm != Screen.fullScreenMode;
            bool refreshChanged = effectiveRefreshRate > 0 && effectiveRefreshRate != curHz;
            if (rw > 0 && rh > 0 && (resChanged || modeChanged || refreshChanged))
            {
                if (fsm == FullScreenMode.Windowed)
                {
                    int maxW = Screen.currentResolution.width;
                    int maxH = Screen.currentResolution.height;
                    if (rw >= maxW || rh >= maxH)
                    {
                        rw = Mathf.RoundToInt(maxW * 0.8f);
                        rh = Mathf.RoundToInt(maxH * 0.8f);
                    }
                }
                Screen.SetResolution(rw, rh, fsm,
                    new UnityEngine.RefreshRate { numerator = (uint)Mathf.Max(1, effectiveRefreshRate), denominator = 1 });
            }

            PlayerPrefs.Save();
            Notify();
        }

        public static void ApplyUiScaleAndFit(UnityEngine.UIElements.PanelSettings ps)
        {
            if (ps == null) return;
            ps.renderMode = UnityEngine.UIElements.PanelRenderMode.ScreenSpaceOverlay;
            ps.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = UnityEngine.UIElements.PanelScreenMatchMode.Shrink; // Shrink ensures the UI scales safely without cropping regardless of aspect ratio
            ps.referenceDpi = 96;
            ps.fallbackDpi = 96;
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
            RefreshRate      = DEFAULT_REFRESH_RATE;
            FlyMode          = false;
            PersistFlyModePreference();
            VoiceMode        = VoiceTalkMode.PushToTalk;
            VoiceActivation  = DEFAULT_VOICE_GATE;
            VoiceVolume      = DEFAULT_VOICE_VOL;
            VoiceDevice      = "";
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
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
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
