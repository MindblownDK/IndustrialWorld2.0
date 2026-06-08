// Assets/Scripts/VoxelEngine/FX/UiAudio.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║              INDUSTRIAL WORLD — UI AUDIO FEEDBACK              ║
// ║                                                                  ║
// ║  Premium click + hover sounds for the whole UI, wired ONCE per   ║
// ║  panel root instead of touching every button. We register        ║
// ║  trickle-down handlers on the root that detect interactive       ║
// ║  controls (Button / clickable elements) and play a tasteful      ║
// ║  click on press and a soft blip on hover.                        ║
// ║                                                                  ║
// ║  All sounds route through the SFX bus (UI is feedback, not       ║
// ║  music) and are rate-limited so rapid moves never machine-gun.   ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.FX
{
    public static class UiAudio
    {
        private static float _lastHoverTime = -1f;
        private static float _lastClickTime = -1f;
        private const float HOVER_COOLDOWN = 0.04f;
        private const float CLICK_COOLDOWN = 0.03f;

        // Master enable — UI clicks/hover can be globally toggled if ever desired.
        public static bool Enabled = true;

        // Weak-keyed sets so we never leak VisualElements: entries vanish when the
        // element is garbage-collected. Replaces the GetProperty/SetProperty API
        // (which isn't public in this Unity version).
        private static readonly ConditionalWeakTable<VisualElement, object> _attached = new();
        private static readonly ConditionalWeakTable<VisualElement, object> _clickable = new();

        /// <summary>
        /// Wire click + hover audio onto a panel root. Safe to call repeatedly on
        /// the same root — it only registers once (tracked in a weak set).
        /// </summary>
        public static void Attach(VisualElement root)
        {
            if (root == null) return;
            // Guard against double-registration when the HUD rebuilds.
            if (_attached.TryGetValue(root, out _)) return;
            _attached.Add(root, BoolBox);

            // PointerDown trickles down from the root through every ancestor of the
            // target, so one handler covers the whole tree — including elements
            // created later (events are evaluated live against the current target).
            root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerOverEvent>(OnPointerOver, TrickleDown.TrickleDown);
        }

        // Shared boxed marker stored as the table value (we only care about key presence).
        private static readonly object BoolBox = new();

        private static void OnPointerDown(PointerDownEvent evt)
        {
            if (!Enabled) return;
            if (evt.target is not VisualElement ve) return;
            if (!IsInteractive(ve)) return;

            if (Time.unscaledTime - _lastClickTime < CLICK_COOLDOWN) return;
            _lastClickTime = Time.unscaledTime;
            AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiClick), 0.55f, Random.Range(0.98f, 1.03f));
        }

        private static void OnPointerOver(PointerOverEvent evt)
        {
            if (!Enabled) return;
            if (evt.target is not VisualElement ve) return;
            if (!IsInteractive(ve)) return;

            if (Time.unscaledTime - _lastHoverTime < HOVER_COOLDOWN) return;
            _lastHoverTime = Time.unscaledTime;
            AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiHover), 0.30f, Random.Range(0.98f, 1.04f));
        }

        /// <summary>
        /// Walks up from the event target to decide whether the hovered/clicked
        /// element is an interactive control worth a sound (Button, Toggle, or any
        /// element that opted in via PickingMode.Position + a clickable feel).
        /// </summary>
        private static bool IsInteractive(VisualElement ve)
        {
            for (var e = ve; e != null; e = e.parent)
            {
                switch (e)
                {
                    case Button:
                    case Toggle:
                    case DropdownField:
                    case Slider:
                    case SliderInt:
                        return true;
                }
                // Our custom code-built "buttons" are plain VisualElements that
                // registered a ClickEvent and use PickingMode.Position. We tag
                // those explicitly via MarkClickable() below.
                if (_clickable.TryGetValue(e, out _)) return true;
            }
            return false;
        }

        /// <summary>
        /// Opt a custom (non-Button) clickable VisualElement into UI audio —
        /// call this on code-built pill/tile buttons that use ClickEvent.
        /// </summary>
        public static void MarkClickable(VisualElement ve)
        {
            if (ve == null) return;
            if (!_clickable.TryGetValue(ve, out _)) _clickable.Add(ve, BoolBox);
        }
    }
}
