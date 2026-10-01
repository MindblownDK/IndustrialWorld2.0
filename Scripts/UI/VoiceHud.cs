// Assets/Scripts/VoxelEngine/UI/VoiceHud.cs
//
// 14.20.0-dev - milestone 7, phase 2: who is talking, at a glance.
//
// A column of slim pills under the chat overlay: one per player currently
// being heard, plus the local microphone pill while transmitting. Each pill
// carries a live level bar, so a dead microphone is obvious without opening a
// settings page - the single most common voice-chat support question answered
// by the HUD itself.
//
// Rules this follows (README section 2): nothing appears or disappears
// instantly - pills ease in, hold, and ease out; the whole thing is
// picking-transparent so it can never swallow a click; it draws nothing at all
// in single player.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    public class VoiceHud : MonoBehaviour
    {
        public static VoiceHud Instance { get; private set; }

        private static readonly Color LocalInk = new(0.42f, 0.92f, 0.62f);
        private static readonly Color RemoteInk = new(0.52f, 0.80f, 1.00f);
        private static readonly Color PillBack = new(0.02f, 0.03f, 0.04f, 0.62f);

        private sealed class Pill
        {
            public VisualElement Root;
            public VisualElement Dot;
            public VisualElement BarFill;
            public Label Name;
            public float Shown;      // 0-1 eased presence
            public bool Wanted;
        }

        private UIDocument _doc;
        private VisualElement _rootHost;
        private VisualElement _column;
        private readonly List<Pill> _pool = new();

        public static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("VoiceHud");
            go.AddComponent<VoiceHud>();
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

        // ─────────────────────────── construction ───────────────────────────

        private bool EnsureBuilt()
        {
            if (_doc == null)
            {
                var controller = GameUIController.Instance;
                if (controller != null) _doc = controller.GetComponent<UIDocument>();
                if (_doc == null) _doc = FindAnyObjectByType<UIDocument>();
                if (_doc == null) return false;
            }

            var root = _doc.rootVisualElement;
            if (root == null || root.panel == null) return false;
            if (_column != null && _rootHost == root && _column.panel != null) return true;

            _rootHost = root;
            _pool.Clear();

            _column = new VisualElement { name = "VoiceHud" };
            _column.style.position = Position.Absolute;
            _column.style.left = 14;
            _column.style.bottom = 74;
            _column.style.width = 300;
            _column.style.alignItems = Align.FlexStart;
            _column.pickingMode = PickingMode.Ignore;
            root.Add(_column);
            return true;
        }

        private Pill RentPill()
        {
            var pill = new Pill();

            pill.Root = new VisualElement();
            pill.Root.style.flexDirection = FlexDirection.Row;
            pill.Root.style.alignItems = Align.Center;
            pill.Root.style.backgroundColor = new StyleColor(PillBack);
            pill.Root.style.paddingLeft = 8; pill.Root.style.paddingRight = 10;
            pill.Root.style.paddingTop = 3; pill.Root.style.paddingBottom = 3;
            pill.Root.style.marginTop = 3;
            pill.Root.style.borderTopLeftRadius = 11; pill.Root.style.borderTopRightRadius = 11;
            pill.Root.style.borderBottomLeftRadius = 11; pill.Root.style.borderBottomRightRadius = 11;
            pill.Root.pickingMode = PickingMode.Ignore;

            pill.Dot = new VisualElement();
            pill.Dot.style.width = 7; pill.Dot.style.height = 7;
            pill.Dot.style.marginRight = 7;
            pill.Dot.style.borderTopLeftRadius = 4; pill.Dot.style.borderTopRightRadius = 4;
            pill.Dot.style.borderBottomLeftRadius = 4; pill.Dot.style.borderBottomRightRadius = 4;
            pill.Dot.pickingMode = PickingMode.Ignore;
            pill.Root.Add(pill.Dot);

            pill.Name = new Label();
            pill.Name.style.fontSize = 11;
            pill.Name.style.letterSpacing = 0.6f;
            pill.Name.style.marginRight = 8;
            pill.Name.style.color = new StyleColor(new Color(0.90f, 0.92f, 0.95f));
            pill.Name.pickingMode = PickingMode.Ignore;
            pill.Root.Add(pill.Name);

            // Level bar: the cheapest possible "your microphone works" signal.
            var barTrack = new VisualElement();
            barTrack.style.width = 54;
            barTrack.style.height = 3;
            barTrack.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.14f));
            barTrack.style.borderTopLeftRadius = 2; barTrack.style.borderTopRightRadius = 2;
            barTrack.style.borderBottomLeftRadius = 2; barTrack.style.borderBottomRightRadius = 2;
            barTrack.pickingMode = PickingMode.Ignore;

            pill.BarFill = new VisualElement();
            pill.BarFill.style.height = 3;
            pill.BarFill.style.width = Length.Percent(0);
            pill.BarFill.style.borderTopLeftRadius = 2; pill.BarFill.style.borderTopRightRadius = 2;
            pill.BarFill.style.borderBottomLeftRadius = 2; pill.BarFill.style.borderBottomRightRadius = 2;
            pill.BarFill.pickingMode = PickingMode.Ignore;
            barTrack.Add(pill.BarFill);
            pill.Root.Add(barTrack);

            _column.Add(pill.Root);
            _pool.Add(pill);
            return pill;
        }

        // ─────────────────────────── per-frame ───────────────────────────

        private void Update()
        {
            if (!EnsureBuilt()) return;

            int used = 0;
            bool offline = VoxelEngine.Networking.NetworkSession.Mode
                           == VoxelEngine.Networking.SessionMode.Offline;

            if (!offline)
            {
                // Remote speakers first - they are what the player must react to.
                var speakers = VoxelEngine.Networking.VoiceChat.Speakers;
                for (int i = 0; i < speakers.Count; i++)
                {
                    var speaker = speakers[i];
                    if (speaker == null || !speaker.IsSpeaking) continue;
                    Apply(used++, speaker.PlayerName, speaker.Level, RemoteInk, true);
                }

                if (VoxelEngine.Networking.VoiceChat.LocalTransmitting)
                    Apply(used++, "YOU", VoxelEngine.Networking.VoiceChat.LocalLevel, LocalInk, true);
            }

            for (int i = used; i < _pool.Count; i++) Apply(i, null, 0f, LocalInk, false);
        }

        /// <summary>Drives one pill toward its target state. Presence eases in
        /// over ~0.12 s and out over ~0.25 s, so pills never pop.</summary>
        private void Apply(int index, string name, float level, Color ink, bool wanted)
        {
            while (_pool.Count <= index) RentPill();
            var pill = _pool[index];
            pill.Wanted = wanted;

            float dt = Time.unscaledDeltaTime;
            pill.Shown = wanted
                ? Mathf.Min(1f, pill.Shown + dt / 0.12f)
                : Mathf.Max(0f, pill.Shown - dt / 0.25f);

            if (pill.Shown <= 0.001f)
            {
                if (pill.Root.style.display != DisplayStyle.None)
                    pill.Root.style.display = DisplayStyle.None;
                return;
            }

            if (pill.Root.style.display != DisplayStyle.Flex)
                pill.Root.style.display = DisplayStyle.Flex;

            // EaseOutCubic on both opacity and the slide-in, per the UI rules.
            float e = 1f - Mathf.Pow(1f - pill.Shown, 3f);
            pill.Root.style.opacity = e;
            pill.Root.style.translate = new Translate(new Length((1f - e) * -14f), 0f);

            if (!string.IsNullOrEmpty(name) && pill.Name.text != name) pill.Name.text = name;
            pill.Dot.style.backgroundColor = new StyleColor(ink);
            pill.BarFill.style.backgroundColor = new StyleColor(ink);
            pill.BarFill.style.width = Length.Percent(Mathf.Clamp01(level) * 100f);
        }
    }
}
