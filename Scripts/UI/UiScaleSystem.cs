// Assets/Scripts/UI/UiScaleSystem.cs
//
// The single authority for how large every UI Toolkit panel draws.
//
// WHY THIS EXISTS
// Three scale laws used to fight over the same shared PanelSettings asset:
//   GameSettings.ApplyUiScaleAndFit  -> 1920x1080 reference, PanelScreenMatchMode.Shrink
//   UITheme.ApplyMenuScale           -> 1536x864 reference, matched on height
//   Resources/MenuPanelSettings.asset-> whichever of the two ran last
// Whichever panel initialised last won, so the HUD and the menus disagreed about
// their own size on the same screen.
//
// Shrink was the destructive one. Unity resolves it to
// max(screenW / refW, screenH / refH), so a 5120x1440 display scaled the HUD by
// max(2.667, 1.333) = 2.667x AND collapsed its layout area to 1920x540. Every
// element drawn for 1080 units of height was crammed into 540 of them while being
// rendered two and a half times too large. That is why large screens looked off.
//
// THE LAW NOW - one height-anchored, aspect-blind scale:
//
//   scale = clamp(Screen.height / DesignHeight, AutoMinScale, AutoMaxScale)
//
// written through PanelScaleMode.ConstantPixelSize + PanelSettings.scale. That is
// the only mode in which Unity honours an explicit multiplier: PanelSettings
// OnValidate resets scale to 1 for every other mode, and PanelSettingsInspector
// only exposes the field for Constant Pixel Size.
//
// The layout area Unity gives the panel is screen / scale, so the design height is
// a constant 1080 units on every supported display and only the width follows the
// aspect ratio:
//
//   1280x720   -> 0.667 -> 1920x1080   (floor keeps a small window legible)
//   1366x768   -> 0.711 -> 1921x1080
//   1920x1080  -> 1.000 -> 1920x1080
//   2560x1440  -> 1.333 -> 1920x1080
//   3440x1440  -> 1.333 -> 2580x1080
//   5120x1440  -> 1.333 -> 3840x1080   (identical sizing to 2560x1440)
//   3840x2160  -> 2.000 -> 1920x1080   (ceiling stops a 4K screen ballooning)
//
// Nothing is ever cropped and nothing is ever oversized, because aspect ratio no
// longer takes part in the calculation at all.
//
// Players who want a literal fixed size can leave Auto: Small, Normal and Large
// are absolute multipliers that bypass the screen entirely, and Normal is exactly
// one design unit per screen pixel.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    /// <summary>How the player wants the interface sized.</summary>
    public enum UiScalePreset
    {
        /// <summary>Height-anchored. The layout stays 1080 design units tall on any display.</summary>
        Auto = 0,

        /// <summary>Absolute 0.75 - one design unit is three quarters of a screen pixel.</summary>
        Small = 1,

        /// <summary>Absolute 1.00 - one design unit is exactly one screen pixel.</summary>
        Normal = 2,

        /// <summary>Absolute 1.50.</summary>
        Large = 3,

        /// <summary>The player's own absolute multiplier, taken from <see cref="UiScaleSystem.CustomScale"/>.</summary>
        Custom = 4
    }

    /// <summary>
    /// Computes, persists and applies the one UI scale law. Every panel in the game
    /// is configured through <see cref="Apply"/>; nothing else may touch
    /// <c>PanelSettings.scaleMode</c>, <c>scale</c> or <c>referenceResolution</c>.
    /// </summary>
    public static class UiScaleSystem
    {
        // ── Design constants ────────────────────────────────────────────
        /// <summary>Width the interface is authored against. Informational under the height law.</summary>
        public const int DesignWidth = 1920;

        /// <summary>Height the interface is authored against. This is the anchor of the whole law.</summary>
        public const int DesignHeight = 1080;

        // ── Auto law clamp ──────────────────────────────────────────────
        /// <summary>Smallest scale Auto may pick. 720p / 1080p: a 1280x720 window still fits the design.</summary>
        public const float AutoMinScale = 720f / 1080f;

        /// <summary>Largest scale Auto may pick. Stops a 4K or 8K display from ballooning the interface.</summary>
        public const float AutoMaxScale = 2.0f;

        // ── Absolute presets ────────────────────────────────────────────
        public const float SmallScale = 0.75f;
        public const float NormalScale = 1.00f;
        public const float LargeScale = 1.50f;

        // ── Custom slider ───────────────────────────────────────────────
        public const float CustomMinScale = 0.50f;
        public const float CustomMaxScale = 2.00f;

        // ── Hard safety ─────────────────────────────────────────────────
        // PanelSettings.scale must be greater than zero; these bounds also stop a
        // corrupt PlayerPrefs value from making the interface unreadable or from
        // pushing the layout area below one pixel.
        private const float HardMinScale = 0.30f;
        private const float HardMaxScale = 3.00f;

        /// <summary>DPI the interface is authored against. Only consulted if a panel is ever switched to Constant Physical Size.</summary>
        public const float ReferenceDpi = 96f;

        // ── Persistence ─────────────────────────────────────────────────
        private const string K_PRESET = "ve.uiScalePreset";
        private const string K_CUSTOM = "ve.uiScaleCustom";

        /// <summary>
        /// Resource name of the shared panel every document in the game loads.
        /// Resolves to Assets/Resources/MenuPanelSettings.asset.
        /// </summary>
        public const string SharedPanelResourceName = "MenuPanelSettings";

        /// <summary>Raised after the effective scale actually changes. Carries the new scale.</summary>
        public static event Action<float> OnScaleChanged;

        private static readonly List<PanelSettings> _tracked = new List<PanelSettings>(8);
        private static bool _settingsHooked;
        private static UiScaleDriver _driver;
        private static float _announced = -1f;

        // ══════════════════════════════════════════════════════════════
        //  PLAYER PREFERENCE
        // ══════════════════════════════════════════════════════════════

        /// <summary>The sizing mode the player picked. Auto is the default.</summary>
        public static UiScalePreset Preset
        {
            get
            {
                int raw = PlayerPrefs.GetInt(K_PRESET, (int)UiScalePreset.Auto);
                // Explicit range check rather than Enum.IsDefined: this getter runs
                // several times per frame from the screen-space conversions, and
                // IsDefined boxes its argument and walks the enum by reflection.
                if (raw < (int)UiScalePreset.Auto || raw > (int)UiScalePreset.Custom)
                    return UiScalePreset.Auto;
                return (UiScalePreset)raw;
            }
            set
            {
                if (Preset == value) return;
                PlayerPrefs.SetInt(K_PRESET, (int)value);
                PlayerPrefs.Save();
                ReapplyAll();
            }
        }

        /// <summary>
        /// The absolute multiplier used while <see cref="UiScalePreset.Custom"/> is selected.
        /// Persisted independently so leaving Custom and coming back restores the fine value.
        /// </summary>
        public static float CustomScale
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(K_CUSTOM, NormalScale), CustomMinScale, CustomMaxScale);
            set
            {
                float clamped = Mathf.Clamp(value, CustomMinScale, CustomMaxScale);
                if (Mathf.Approximately(CustomScale, clamped)) return;
                PlayerPrefs.SetFloat(K_CUSTOM, clamped);
                // Only Custom reads this value, so only Custom needs a re-apply.
                if (Preset == UiScalePreset.Custom)
                {
                    PlayerPrefs.Save();
                    ReapplyAll();
                }
            }
        }

        /// <summary>Restores Auto at 1.00x. Called by GameSettings.ResetToDefaults.</summary>
        public static void ResetToDefault()
        {
            PlayerPrefs.SetInt(K_PRESET, (int)UiScalePreset.Auto);
            PlayerPrefs.SetFloat(K_CUSTOM, NormalScale);
            PlayerPrefs.Save();
            ReapplyAll();
        }

        // ══════════════════════════════════════════════════════════════
        //  THE LAW
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// The screen-derived Auto scale, clamped to the supported display range.
        /// Aspect ratio deliberately takes no part: 5120x1440 and 2560x1440 agree.
        /// </summary>
        public static float AutoScale
        {
            get
            {
                int h = Screen.height;
                if (h <= 0) return 1f;   // headless, batchmode or a display that has not come up yet
                return Mathf.Clamp(h / (float)DesignHeight, AutoMinScale, AutoMaxScale);
            }
        }

        /// <summary>The absolute multiplier behind a fixed preset, ignoring the screen entirely.</summary>
        public static float PresetMultiplier(UiScalePreset preset)
        {
            switch (preset)
            {
                case UiScalePreset.Small:  return SmallScale;
                case UiScalePreset.Normal: return NormalScale;
                case UiScalePreset.Large:  return LargeScale;
                case UiScalePreset.Custom: return Mathf.Clamp(CustomScale, CustomMinScale, CustomMaxScale);
                default:                   return 1f;   // Auto applies no extra multiplier
            }
        }

        /// <summary>
        /// Design units per screen pixel for the current preset and display. This is
        /// the number written to <c>PanelSettings.scale</c> and the number every
        /// screen-space conversion in the interface must divide by.
        /// </summary>
        public static float EffectiveScale
        {
            get
            {
                UiScalePreset preset = Preset;
                float scale = preset == UiScalePreset.Auto
                    ? AutoScale
                    : PresetMultiplier(preset);
                return Mathf.Clamp(scale, HardMinScale, HardMaxScale);
            }
        }

        /// <summary>
        /// The layout area Unity hands the panel: screen size divided by the scale.
        /// Under Auto this is always <see cref="DesignHeight"/> units tall, with the
        /// width free to follow the aspect ratio.
        /// </summary>
        public static Vector2 DesignSize
        {
            get
            {
                int w = Screen.width, h = Screen.height;
                if (w <= 0 || h <= 0) return new Vector2(DesignWidth, DesignHeight);
                float s = EffectiveScale;
                return new Vector2(w / s, h / s);
            }
        }

        /// <summary>Converts a screen-pixel length into design units.</summary>
        public static float PixelsToUnits(float pixels) => pixels / EffectiveScale;

        /// <summary>Converts a design-unit length into screen pixels.</summary>
        public static float UnitsToPixels(float units) => units * EffectiveScale;

        /// <summary>
        /// Converts a Unity screen position (origin bottom-left, as returned by
        /// <c>Camera.WorldToScreenPoint</c>) into panel design units. Drop-in
        /// replacement for <c>RuntimePanelUtils.ScreenToPanel</c> when no panel is
        /// at hand.
        /// </summary>
        public static Vector2 UnityScreenToPanel(Vector2 unityScreenPos)
        {
            int h = Screen.height;
            if (h <= 0) return unityScreenPos;
            float s = EffectiveScale;
            return new Vector2(unityScreenPos.x / s, h / s - unityScreenPos.y / s);
        }

        /// <summary>
        /// Converts a top-left-origin screen position (Input System
        /// <c>Mouse.current.position</c>, or a <c>WorldToScreenPoint</c> result that
        /// has already been flipped) into panel design units.
        /// </summary>
        public static Vector2 MouseToPanel(Vector2 mouseTopLeftPos) => mouseTopLeftPos / EffectiveScale;

        // ══════════════════════════════════════════════════════════════
        //  APPLICATION
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Applies the law to the shared panel before any scene loads.
        ///
        /// UIDocument.OnEnable runs at DefaultExecutionOrder(-100), ahead of every
        /// runtime controller's Awake, so the panel is created from the asset's authored
        /// values first. Without this hook the opening frame would draw at the authored
        /// 1.00x and then pop to the resolved scale. Only the law is written here - the
        /// driver GameObject is deliberately left to the first real Apply, since creating
        /// a DontDestroyOnLoad root before the first scene is needlessly early.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyToSharedPanelBeforeScenes()
        {
            var shared = Resources.Load<PanelSettings>(SharedPanelResourceName);
            if (shared == null) return;
            Track(shared);
            WriteLaw(shared, EffectiveScale);
        }

        /// <summary>
        /// Writes the law onto a PanelSettings and registers it so later resolution
        /// or preference changes reach it. Safe to call repeatedly and safe to call
        /// on a runtime-created instance.
        /// </summary>
        public static void Apply(PanelSettings ps)
        {
            if (ps == null) return;
            Track(ps);
            WriteLaw(ps, EffectiveScale);
            EnsureRunning();
        }

        /// <summary>
        /// Editor-time entry point. Writes the neutral authored law (1.00x) onto an
        /// asset without registering it and without starting the runtime driver, so
        /// running a Setup Wizard step cannot bake the Game view's current height into
        /// the asset on disk, and cannot leave the asset tracked for live rewrites.
        /// </summary>
        public static void ApplyAuthored(PanelSettings ps)
        {
            if (ps == null) return;
            WriteLaw(ps, 1f);
        }

        /// <summary>
        /// Re-writes the current law onto every registered panel. Called when the
        /// screen size or the player preference changes. Idempotent and cheap: each
        /// field is compared before it is assigned, so a steady frame touches nothing.
        /// </summary>
        public static void ReapplyAll()
        {
            EnsureRunning();
            float scale = EffectiveScale;
            PruneTracked();
            for (int i = 0; i < _tracked.Count; i++)
                WriteLaw(_tracked[i], scale);

            if (!Mathf.Approximately(_announced, scale))
            {
                _announced = scale;
                OnScaleChanged?.Invoke(scale);
            }
        }



        private static void WriteLaw(PanelSettings ps, float scale)
        {
            // ScreenSpaceOverlay is part of the law: world-space quads clip the HUD
            // at the screen edges, which is what every former caller of
            // ApplyUiScaleAndFit relied on this method to prevent.
            if (ps.renderMode != PanelRenderMode.ScreenSpaceOverlay)
                ps.renderMode = PanelRenderMode.ScreenSpaceOverlay;

            // ConstantPixelSize is the only mode whose resolved scale is exactly the
            // multiplier we set: ResolveScale returns 1 for the mode and then divides
            // by PanelSettings.scale, so the panel ends up scaled by `scale` with a
            // layout area of screen / scale.
            if (ps.scaleMode != PanelScaleMode.ConstantPixelSize)
                ps.scaleMode = PanelScaleMode.ConstantPixelSize;

            if (!Mathf.Approximately(ps.scale, scale))
                ps.scale = scale;

            // Inert under ConstantPixelSize, but kept correct so the Inspector reads
            // sensibly and so a manual switch to Scale With Screen Size degrades to
            // the same height-anchored law instead of the old cropping behaviour.
            if (ps.referenceResolution.x != DesignWidth || ps.referenceResolution.y != DesignHeight)
                ps.referenceResolution = new Vector2Int(DesignWidth, DesignHeight);

            if (ps.screenMatchMode != PanelScreenMatchMode.MatchWidthOrHeight)
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;

            if (!Mathf.Approximately(ps.match, 1f))
                ps.match = 1f;

            if (!Mathf.Approximately(ps.referenceDpi, ReferenceDpi))
                ps.referenceDpi = ReferenceDpi;

            if (!Mathf.Approximately(ps.fallbackDpi, ReferenceDpi))
                ps.fallbackDpi = ReferenceDpi;
        }

        private static void Track(PanelSettings ps)
        {
            PruneTracked();
            for (int i = 0; i < _tracked.Count; i++)
                if (_tracked[i] == ps) return;
            _tracked.Add(ps);
        }

        private static void PruneTracked()
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
                if (_tracked[i] == null) _tracked.RemoveAt(i);
        }

        /// <summary>
        /// Makes sure something notices the screen changing. GameSettings.OnChanged
        /// covers a resolution picked in Settings; the driver covers a dragged window
        /// border and a monitor switch, neither of which raises it.
        /// </summary>
        private static void EnsureRunning()
        {
            if (!Application.isPlaying) return;

            if (!_settingsHooked)
            {
                _settingsHooked = true;
                VoxelEngine.Settings.GameSettings.OnChanged += ReapplyAll;
            }

            if (_driver != null) return;

            var go = new GameObject("[UiScaleDriver]");
            go.hideFlags = HideFlags.HideInHierarchy;
            // Qualified: DontDestroyOnLoad is an instance method on UnityEngine.Object,
            // so a static class has no inherited member to resolve it against.
            UnityEngine.Object.DontDestroyOnLoad(go);
            _driver = go.AddComponent<UiScaleDriver>();
            _driver.Initialise(Screen.width, Screen.height);
        }

        /// <summary>
        /// Clears the driver reference when Unity destroys it, so the next
        /// <see cref="Apply"/> builds a fresh one instead of writing to a dead component.
        /// </summary>
        internal static void ForgetDriver(UiScaleDriver driver)
        {
            if (_driver == driver) _driver = null;
        }

        // ══════════════════════════════════════════════════════════════
        //  PRESENTATION (Settings - Interface)
        // ══════════════════════════════════════════════════════════════

        /// <summary>Short label for a preset segment.</summary>
        public static string PresetLabel(UiScalePreset preset)
        {
            switch (preset)
            {
                case UiScalePreset.Auto:   return "Auto";
                case UiScalePreset.Small:  return "Small";
                case UiScalePreset.Normal: return "Normal";
                case UiScalePreset.Large:  return "Large";
                case UiScalePreset.Custom: return "Custom";
                default:                   return preset.ToString();
            }
        }

        /// <summary>
        /// Live two-line description of the current law for the Settings hint: what
        /// the mode does, and what it resolves to on this display right now.
        /// </summary>
        public static string Describe()
        {
            Vector2 design = DesignSize;
            string live = string.Format("Drawing at {0:0.00}x - layout area {1} x {2}.",
                EffectiveScale, Mathf.RoundToInt(design.x), Mathf.RoundToInt(design.y));

            switch (Preset)
            {
                case UiScalePreset.Auto:
                    return "Follows your screen height, so the interface keeps the same apparent size on "
                         + "every display from 720p to a 49 inch ultrawide. Aspect ratio is ignored: "
                         + "5120x1440 is sized exactly like 2560x1440.\n" + live;

                case UiScalePreset.Small:
                    return "Fixed at 0.75x on every display, whatever the resolution.\n" + live;

                case UiScalePreset.Normal:
                    return "Fixed at 1.00x: one design unit is always exactly one screen pixel.\n" + live;

                case UiScalePreset.Large:
                    return "Fixed at 1.50x on every display, whatever the resolution.\n" + live;

                default:
                    return "Your own fixed multiplier, applied identically on every display.\n" + live;
            }
        }
    }
}
