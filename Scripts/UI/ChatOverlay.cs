// Assets/Scripts/VoxelEngine/UI/ChatOverlay.cs
//
// 14.19.0-dev - milestone 7, phase 1: proximity TEXT chat.
// 14.19.1-dev - input read through the Input System (the legacy UnityEngine.Input
//               calls threw the moment Enter was pressed, so chat never opened).
// 14.21.0-dev - moved to the TOP-LEFT column, directly under the target card
//               (WorldInspectionHud). It used to sit bottom-left, on top of the
//               gravity readout. The three top-left HUD elements now form one
//               stack: target card, then chat, then the voice pills.
//
// A sleek top-left overlay on the existing HUD UIDocument: recent messages
// as softly fading cards, an input line that opens on Enter, closes on Escape,
// sends on Enter. While the input is open, UIState.TextInputActive keeps all
// player movement/hotkeys suppressed (GameUIController includes IsTyping in
// its keyboard-capture OR-list).
//
// Scope rules:
// - Chat exists only in multiplayer sessions (Enter does nothing offline).
// - Proximity is enforced SERVER-side in NetworkBootstrap (60 m) - by the
//   time a message reaches this overlay it has already earned the right to
//   be seen. The sender always sees their own message (instant local echo).
// - Phase 2 (proximity voice with directional sound) rides the same relay.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoxelEngine.UI
{
    public class ChatOverlay : MonoBehaviour
    {
        public static ChatOverlay Instance { get; private set; }

        /// <summary>True while the chat input owns the keyboard.</summary>
        public static bool IsTyping => Instance != null && Instance._inputOpen;

        private const int MaxVisible = 8;
        private const int MaxLength = 240;
        private const float ShowSeconds = 10f;
        private const float FadeSeconds = 2.5f;

        // ── top-left stack geometry ──
        /// <summary>Where chat sits when the target card is hidden. Chosen to
        /// clear a typical card, so chat does NOT bob up and down every time
        /// the player glances at a block - it only ever moves down, and only
        /// for a card tall enough to actually reach it.</summary>
        private const float BaselineTop = 152f;
        private const float InspectionGap = 10f;
        /// <summary>Fixed message viewport. A fixed height is what keeps the
        /// bottom edge still, which is what keeps the voice pills under it
        /// still. Messages are bottom-aligned inside it and the oldest one is
        /// clipped at the top, exactly as it read when chat grew upward.</summary>
        private const float MessageViewport = 170f;

        private UIDocument _doc;
        private VisualElement _rootHost;
        private VisualElement _container;
        private VisualElement _messageColumn;
        private TextField _field;
        private bool _inputOpen;

        private struct Entry { public Label Label; public float Born; }
        private readonly List<Entry> _entries = new List<Entry>();
        private float _appliedTop = -1f;

        /// <summary>Bottom edge of the whole overlay in panel pixels, or 0 when
        /// chat has not been built. The voice HUD stacks under this.</summary>
        public static float BottomEdge
        {
            get
            {
                var self = Instance;
                if (self == null || self._container == null || self._container.panel == null) return 0f;
                float bottom = self._container.layout.yMax;
                return float.IsNaN(bottom) ? 0f : bottom;
            }
        }

        public static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("ChatOverlay");
            go.AddComponent<ChatOverlay>();
        }

        /// <summary>Show one chat line. Safe to call from anywhere; drops the
        /// message silently when no HUD exists in this scene.</summary>
        public static void AddMessage(string sender, string text)
        {
            if (Instance == null) return;
            Instance.AddEntry(sender, text);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------- UI construction ----------

        private bool EnsureBuilt()
        {
            var doc = FindDocument();
            if (doc == null) return false;
            var root = doc.rootVisualElement;
            if (root == null || root.panel == null) return false;
            if (_container != null && _rootHost == root && _container.panel != null) return true;

            _rootHost = root;
            _entries.Clear();
            _inputOpen = false;

            _container = new VisualElement { name = "ChatOverlay" };
            _container.style.position = Position.Absolute;
            _container.style.left = 16;          // flush with the target card above
            _container.style.top = BaselineTop;
            _container.style.width = 430;
            _container.pickingMode = PickingMode.Ignore;
            _appliedTop = BaselineTop;

            _messageColumn = new VisualElement();
            _messageColumn.style.height = MessageViewport;
            _messageColumn.style.justifyContent = Justify.FlexEnd;
            _messageColumn.style.overflow = Overflow.Hidden;
            _messageColumn.pickingMode = PickingMode.Ignore;
            _container.Add(_messageColumn);

            _field = new TextField { maxLength = MaxLength };
            _field.style.display = DisplayStyle.None;
            _field.style.marginTop = 4;
            var inner = _field.Q(TextField.textInputUssName);
            if (inner != null)
            {
                inner.style.backgroundColor = new Color(0.03f, 0.04f, 0.05f, 0.72f);
                inner.style.color = new Color(0.93f, 0.93f, 0.93f);
                inner.style.borderTopLeftRadius = 6; inner.style.borderTopRightRadius = 6;
                inner.style.borderBottomLeftRadius = 6; inner.style.borderBottomRightRadius = 6;
                inner.style.paddingLeft = 8; inner.style.paddingRight = 8;
                inner.style.paddingTop = 4; inner.style.paddingBottom = 4;
                inner.style.fontSize = 13;
            }
            _container.Add(_field);

            root.Add(_container);
            return true;
        }

        /// <summary>Cached: the scan is only paid for when the cached document
        /// has gone away (scene change), never once per frame.</summary>
        private UIDocument FindDocument()
        {
            if (_doc != null) return _doc;
            var controller = GameUIController.Instance;
            if (controller != null) _doc = controller.GetComponent<UIDocument>();
            if (_doc == null) _doc = FindAnyObjectByType<UIDocument>();
            return _doc;
        }

        private void AddEntry(string sender, string text)
        {
            if (!EnsureBuilt()) return;
            var label = new Label("<b>" + Clean(sender) + "</b>  " + Clean(text));
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            label.style.color = new Color(0.93f, 0.93f, 0.93f);
            label.style.fontSize = 13;
            label.style.borderTopLeftRadius = 6; label.style.borderTopRightRadius = 6;
            label.style.borderBottomLeftRadius = 6; label.style.borderBottomRightRadius = 6;
            label.style.paddingLeft = 8; label.style.paddingRight = 8;
            label.style.paddingTop = 3; label.style.paddingBottom = 3;
            label.style.marginTop = 2;
            label.style.alignSelf = Align.FlexStart;   // card hugs its text width
            label.pickingMode = PickingMode.Ignore;
            _messageColumn.Add(label);
            _entries.Add(new Entry { Label = label, Born = Time.unscaledTime });
            while (_entries.Count > MaxVisible)
            {
                _messageColumn.Remove(_entries[0].Label);
                _entries.RemoveAt(0);
            }
        }

        /// <summary>Rich-text injection guard: names and messages render as
        /// text, never as markup.</summary>
        private static string Clean(string s)
        {
            return (s ?? "").Replace("<", "‹").Replace(">", "›");
        }

        // ---------- input handling ----------

        private void Update()
        {
            if (!EnsureBuilt()) return;

            LayoutUnderTargetCard();

            // Fade and expire message cards (frozen fully visible while typing).
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - _entries[i].Born;
                if (_inputOpen) { _entries[i].Label.style.opacity = 1f; continue; }
                if (age >= ShowSeconds + FadeSeconds)
                {
                    _messageColumn.Remove(_entries[i].Label);
                    _entries.RemoveAt(i);
                }
                else if (age > ShowSeconds)
                {
                    _entries[i].Label.style.opacity = 1f - (age - ShowSeconds) / FadeSeconds;
                }
            }

            bool enter = EnterPressed();
            if (!_inputOpen)
            {
                // Only in multiplayer, and never steal Enter from another text field.
                if (enter && !UIState.TextInputActive
                    && VoxelEngine.Networking.NetworkSession.Mode != VoxelEngine.Networking.SessionMode.Offline)
                    OpenInput();
            }
            else if (EscapePressed())
            {
                CloseInput(false);
                // Script execution order between this overlay and the HUD is not
                // fixed: claim the frame so the pause menu cannot also open.
                UIState.PauseConsumedFrame = Time.frameCount;
            }
            else if (enter)
            {
                CloseInput(true);
            }
        }

        /// <summary>Keeps chat clear of the target card above it. Only ever
        /// pushes DOWN from the baseline: a card short enough to leave room is
        /// ignored, so the chat log is not permanently in motion.</summary>
        private void LayoutUnderTargetCard()
        {
            float wanted = Mathf.Max(BaselineTop, WorldInspectionHud.BottomEdge + InspectionGap);
            if (Mathf.Abs(wanted - _appliedTop) < 0.5f) return;
            _appliedTop = wanted;
            _container.style.top = wanted;
        }

        // Input is read through the Input System, with the legacy path kept for
        // projects that still run the old backend. Calling UnityEngine.Input
        // while "Active Input Handling" is Input System THROWS - that exception
        // is what kept the chat line from ever opening (14.19.1).
        private static bool EnterPressed()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            return kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
        }

        private static bool EscapePressed()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private void OpenInput()
        {
            _inputOpen = true;
            _field.style.display = DisplayStyle.Flex;
            _field.SetValueWithoutNotify("");
            _field.schedule.Execute(() => _field.Focus());
        }

        private void CloseInput(bool send)
        {
            string text = _field.value;
            _inputOpen = false;
            _field.style.display = DisplayStyle.None;
            _field.Blur();
            if (!send) return;
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > MaxLength) text = text.Substring(0, MaxLength);

            // Instant local echo - your own words never depend on the wire.
            AddEntry(VoxelEngine.Networking.PlayerIdentity.LocalName, text);
            var boot = VoxelEngine.Networking.NetworkBootstrap.Instance;
            if (boot != null) boot.SendChatMessage(text);
        }
    }
}
