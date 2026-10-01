// Assets/Scripts/VoxelEngine/UI/ChatOverlay.cs
//
// 14.19.0-dev - milestone 7, phase 1: proximity TEXT chat.
//
// A sleek bottom-left overlay on the existing HUD UIDocument: recent messages
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

        private VisualElement _rootHost;
        private VisualElement _container;
        private VisualElement _messageColumn;
        private TextField _field;
        private bool _inputOpen;

        private struct Entry { public Label Label; public float Born; }
        private readonly List<Entry> _entries = new List<Entry>();

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

            _container = new VisualElement();
            _container.style.position = Position.Absolute;
            _container.style.left = 14;
            _container.style.bottom = 110;
            _container.style.width = 430;
            _container.pickingMode = PickingMode.Ignore;

            _messageColumn = new VisualElement();
            _messageColumn.style.justifyContent = Justify.FlexEnd;
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

        private static UIDocument FindDocument()
        {
            var controller = GameUIController.Instance;
            if (controller != null)
            {
                var doc = controller.GetComponent<UIDocument>();
                if (doc != null) return doc;
            }
            return FindAnyObjectByType<UIDocument>();
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

            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if (!_inputOpen)
            {
                // Only in multiplayer, and never steal Enter from another text field.
                if (enter && !UIState.TextInputActive
                    && VoxelEngine.Networking.NetworkSession.Mode != VoxelEngine.Networking.SessionMode.Offline)
                    OpenInput();
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CloseInput(false);
                else if (enter) CloseInput(true);
            }
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
