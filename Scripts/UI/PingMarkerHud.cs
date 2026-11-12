// Assets/Scripts/VoxelEngine/UI/PingMarkerHud.cs
//
// 18.16.0-dev - team pings, drawn.
//
// The marker layer for PingSync. One floating ring per live ping: the pinger's
// name, the live distance to it, and a ring that pulses so it reads as "here,
// now" rather than as scenery. Your own ping is drawn in the cool "you" cyan
// with a YOU tag; a teammate's is the warm amber that means somebody else is
// talking to you. A ping that is off screen does not vanish - it becomes an
// arrow pinned to the screen edge pointing at it, with the distance still
// attached, because a shout you cannot turn toward is useless.
//
// Like the beacon strip next door, this file never decides WHAT it may draw.
// PingSync.Current is the single entry point: a guest holds exactly what the
// host sent it, and the host holds exactly what the team rule allowed, so there
// is no secret for this file to keep and no way for it to leak one.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Networking;

namespace VoxelEngine.UI
{
    public static class PingMarkerHud
    {
        private const int MaxMarkers = 8;
        private const float EaseSpeed = 10f;       // appear / vanish, per second
        private const float FadeTailS = 1.4f;      // last seconds fade out
        private const float PulseHz = 1.15f;       // ring breathing rate
        private const float EdgeMargin = 54f;      // px kept clear at the screen edge
        private const float PopScale = 0.55f;      // first-frame scale, grows to 1

        // Amber for a teammate, cyan for you. Deliberately not the beacon's own
        // colours: a shout and a waypoint should never be mistaken for each other.
        private static readonly Color TeamInk = new(1f, 0.76f, 0.22f);
        private static readonly Color SelfInk = new(0.42f, 0.87f, 1f);

        private sealed class Entry
        {
            public VisualElement Root;
            public VisualElement Ring;
            public VisualElement Arrow;
            public Label Name;
            public Label Distance;
            public Vector2 PanelPos;
            public float Alpha;
            public bool WantedAlive;
            public bool HasPos;
        }

        private static VisualElement _layer;
        private static VisualElement _container;
        private static readonly Dictionary<string, Entry> _entries = new();
        private static readonly List<string> _dead = new();
        private static float _lastTickTime;

        public static void EnsureMounted(VisualElement hudLayer)
        {
            if (hudLayer == null) return;
            if (_layer == hudLayer && _container != null && _container.parent == hudLayer) return;

            _layer = hudLayer;
            _entries.Clear();
            _container = new VisualElement { name = "ping-markers", pickingMode = PickingMode.Ignore };
            _container.style.position = Position.Absolute;
            _container.style.left = 0; _container.style.right = 0;
            _container.style.top = 0; _container.style.bottom = 0;
            hudLayer.Insert(0, _container);   // behind every interactive strip
            _lastTickTime = Time.unscaledTime;
            _container.schedule.Execute(Tick).Every(33);
        }

        private static void Tick()
        {
            if (_container == null || _container.panel == null) return;
            float now = Time.unscaledTime;
            float dt = Mathf.Clamp(now - _lastTickTime, 0.001f, 0.2f);
            _lastTickTime = now;

            var cam = Camera.main;
            bool hudUp = cam != null && !UIState.IsBlocking;
            _container.style.display = hudUp ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hudUp) return;

            var pings = PingSync.Current;

            // Panel-space centre and half-extents: the anchor for the edge arrows
            // and the frame the pings are clamped into.
            Vector2 centre = RuntimePanelUtils.ScreenToPanel(_container.panel,
                new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            Vector2 half = new(Mathf.Max(60f, centre.x - EdgeMargin), Mathf.Max(60f, centre.y - EdgeMargin));

            foreach (var e in _entries.Values) e.WantedAlive = false;

            int drawn = 0;
            for (int i = pings.Count - 1; i >= 0 && drawn < MaxMarkers; i--)
            {
                var ping = pings[i];                 // newest first: the freshest shout wins the last slot
                if (string.IsNullOrEmpty(ping.Record.Id)) continue;
                drawn++;

                Vector3 world = ping.Record.Position;
                Vector3 sp = cam.WorldToScreenPoint(world);
                bool inFront = sp.z > 0f;
                if (!inFront) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }

                bool onScreen = inFront
                    && sp.x > 24f && sp.x < Screen.width - 24f
                    && sp.y > 24f && sp.y < Screen.height - 24f;

                Vector2 target = RuntimePanelUtils.ScreenToPanel(_container.panel,
                    new Vector2(sp.x, Screen.height - sp.y));

                if (!onScreen)
                {
                    // Pinned to the frame: walk the ray from the middle of the
                    // panel outwards until it touches the inset rectangle.
                    Vector2 dir = target - centre;
                    if (dir.sqrMagnitude < 0.0001f) dir = new Vector2(0f, -1f);
                    float tx = Mathf.Abs(dir.x) > 0.0001f ? half.x / Mathf.Abs(dir.x) : float.MaxValue;
                    float ty = Mathf.Abs(dir.y) > 0.0001f ? half.y / Mathf.Abs(dir.y) : float.MaxValue;
                    target = centre + dir * Mathf.Min(tx, ty);
                }

                if (!_entries.TryGetValue(ping.Record.Id, out var entry))
                {
                    entry = BuildEntry();
                    _entries[ping.Record.Id] = entry;
                }
                entry.WantedAlive = true;

                // Edge arrows snap (they are a compass, not a position); on-screen
                // markers glide so a ping on a moving ship stays readable.
                if (!entry.HasPos || !onScreen)
                    entry.PanelPos = target;
                else
                    entry.PanelPos = Vector2.Lerp(entry.PanelPos, target,
                        1f - Mathf.Exp(-14f * dt));
                entry.HasPos = true;

                Color ink = ping.IsMine ? SelfInk : TeamInk;
                float dist = Vector3.Distance(cam.transform.position, world);

                // Fade in fast, then sit at full strength until the tail.
                float alive = ping.RemainingS > FadeTailS
                    ? 1f
                    : Mathf.Clamp01(ping.RemainingS / FadeTailS);
                entry.Alpha = Mathf.MoveTowards(entry.Alpha, alive, EaseSpeed * dt);

                entry.Name.text = ping.IsMine ? "You" : FirstWord(ping.Record.OwnerName);
                entry.Name.style.color = ink;
                entry.Distance.text = FormatDistance(dist);
                entry.Ring.style.borderTopColor = entry.Ring.style.borderBottomColor =
                entry.Ring.style.borderLeftColor = entry.Ring.style.borderRightColor = ink;
                entry.Arrow.style.borderTopColor = entry.Arrow.style.borderRightColor = ink;

                if (onScreen)
                {
                    // Ring: a pop on arrival, then a slow breath. The breath is
                    // what makes a shouted point feel live rather than painted.
                    float breathe = 0.5f + 0.5f * Mathf.Sin(ping.Age * Mathf.PI * 2f * PulseHz);
                    float pop = Mathf.Lerp(PopScale, 1f, Mathf.Clamp01(ping.Age / 0.22f));
                    float scale = pop * Mathf.Lerp(0.94f, 1.10f, breathe);
                    entry.Ring.style.display = DisplayStyle.Flex;
                    entry.Arrow.style.display = DisplayStyle.None;
                    entry.Ring.style.scale = new Scale(new Vector2(scale, scale));
                }
                else
                {
                    entry.Ring.style.display = DisplayStyle.None;
                    entry.Arrow.style.display = DisplayStyle.Flex;
                    entry.Arrow.style.rotate = new Rotate(AimRotationDegrees(centre, target));
                }

                // Far shouts stay readable but get a little quieter with distance.
                float presence = Mathf.Lerp(1f, 0.6f, Mathf.InverseLerp(200f, 2000f, dist));
                float ease = entry.Alpha * entry.Alpha * (3f - 2f * entry.Alpha);
                entry.Root.style.opacity = presence * ease;
                entry.Root.style.display = DisplayStyle.Flex;
                entry.Root.style.left = entry.PanelPos.x - 70f;
                entry.Root.style.top = entry.PanelPos.y - 15f;
            }

            // Ease out whatever expired, then drop the element for good.
            _dead.Clear();
            foreach (var kv in _entries)
            {
                var entry = kv.Value;
                if (entry.WantedAlive) continue;
                entry.Alpha = Mathf.MoveTowards(entry.Alpha, 0f, EaseSpeed * dt);
                entry.Root.style.opacity = entry.Alpha;
                if (entry.Alpha <= 0.01f) _dead.Add(kv.Key);
            }
            foreach (var id in _dead)
            {
                _entries[id].Root.RemoveFromHierarchy();
                _entries.Remove(id);
            }
        }

        private static Entry BuildEntry()
        {
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.width = 140f;
            root.style.alignItems = Align.Center;
            root.style.opacity = 0f;

            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.style.width = 26f;
            ring.style.height = 26f;
            ring.style.borderTopWidth = ring.style.borderBottomWidth =
            ring.style.borderLeftWidth = ring.style.borderRightWidth = 2f;
            ring.style.borderTopLeftRadius = ring.style.borderTopRightRadius =
            ring.style.borderBottomLeftRadius = ring.style.borderBottomRightRadius = 13f;
            ring.style.backgroundColor = new Color(0.03f, 0.04f, 0.06f, 0.45f);
            root.Add(ring);

            // The chevron: a square carrying only its top and right edge, turned
            // 45 degrees, so it points the way the ping lies.
            var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
            arrow.style.width = 12f;
            arrow.style.height = 12f;
            arrow.style.borderTopWidth = 3f;
            arrow.style.borderRightWidth = 3f;
            arrow.style.marginTop = 6f;
            arrow.style.marginBottom = 6f;
            arrow.style.display = DisplayStyle.None;
            root.Add(arrow);

            var name = MakeLabel(11, FontStyle.Bold);
            name.style.marginTop = 3f;
            root.Add(name);

            var distance = MakeLabel(9, FontStyle.Normal);
            distance.style.color = new Color(0.86f, 0.89f, 0.94f, 0.9f);
            root.Add(distance);

            _container.Add(root);
            return new Entry { Root = root, Ring = ring, Arrow = arrow, Name = name, Distance = distance };
        }

        /// <summary>How far to rotate the edge chevron so its point aims at the
        /// marker.
        ///
        /// 18.17.1-dev - this used to be `angle - 45`, which put the arrow 90
        /// degrees clockwise of the truth (a ping to your left read as pointing
        /// down). The angle is measured in PANEL space, where y grows downwards,
        /// so Atan2 already counts clockwise from +X. The chevron is a square
        /// carrying only its top and right edge, which leaves its point at the
        /// square's TOP-RIGHT corner - that corner faces up-right, i.e. MINUS 45
        /// degrees in this same clockwise convention. Rotating by `angle + 45`
        /// therefore carries that corner exactly onto the marker's direction.</summary>
        private static float AimRotationDegrees(Vector2 centre, Vector2 target)
        {
            Vector2 dir = target - centre;
            if (dir.sqrMagnitude < 0.0001f) return 0f;
            return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + ChevronCornerDeg;
        }

        /// <summary>The chevron's own point, in panel degrees (y down, clockwise
        /// from +X): its top-right corner.</summary>
        private const float ChevronCornerDeg = 45f;

        private static Label MakeLabel(int size, FontStyle weight)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.unityFontStyleAndWeight = weight;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.letterSpacing = 0.5f;
            label.style.textShadow = new TextShadow
            { offset = new Vector2(0f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.9f) };
            return label;
        }

        /// <summary>A display name may carry a tag or a space; the marker only has
        /// room for the first word of it.</summary>
        private static string FirstWord(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Teammate";
            int space = name.IndexOf(' ');
            return space > 0 ? name.Substring(0, space) : name;
        }

        private static string FormatDistance(float metres)
        {
            if (metres < 1000f) return $"{metres:0} m";
            return metres < 10000f ? $"{metres / 1000f:0.0} km" : $"{metres / 1000f:0} km";
        }
    }
}
