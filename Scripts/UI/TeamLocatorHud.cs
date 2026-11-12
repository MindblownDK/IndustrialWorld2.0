// Assets/Scripts/VoxelEngine/UI/TeamLocatorHud.cs
//
// 18.17.0-dev - the team locator.
//
// Teams made "who is on my side" a question with an answer (14.33.0), beacons
// made a place findable (14.30.0) and pings made a moment shareable (18.16.0).
// What was still missing is the plainest one: where is my teammate right now.
// A nameplate only works while they are in front of you, and a beacon only
// works if somebody remembered to place one.
//
// So this strip draws one marker per teammate - name, live distance and health
// - and pins anyone outside the frame to the screen edge with an arrow aimed at
// them. It answers nothing else: no revealing of enemies, no map, no history.
//
// Three rules keep it honest and cheap:
//   - TEAM ONLY, read from the same TeamRegistry.SameTeam every other team
//     feature asks. No team means no markers, so a session where nobody teams
//     up looks exactly as it did before this file existed.
//   - NO NETWORK. Every machine already replicates its own avatar's transform,
//     so a client can read a teammate's position without a single new message.
//     A locator that needed its own channel would be a locator that lies when
//     the channel is busy.
//   - NEARLY CLOSE IS NOT A MARKER. Inside four metres the in-world nameplate is
//     already on screen, so the marker steps aside instead of shouting over it.
//
// The marker dims when the teammate is behind something (a throttled ray, never
// per frame per teammate) and says "elsewhere" instead of a number when the gap
// is measured in thousands of metres, where the distance has stopped meaning
// anything you could act on.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Networking;

namespace VoxelEngine.UI
{
    public static class TeamLocatorHud
    {
        private const int MaxMarkers = 8;            // a full team, per TeamSnapshot.DefaultMaxMembers
        private const float MinDistanceM = 4f;       // closer than this, the in-world nameplate owns it
        private const float ElsewhereM = 20000f;     // past this the distance is not a number you can use
        private const float EaseSpeed = 8f;          // appear / vanish, per second
        private const float FadeSpeed = 10f;
        private const float EdgeMargin = 54f;        // px kept clear at the screen edge
        private const float OcclusionIntervalS = 0.15f;
        private const float OccludedAlpha = 0.42f;

        // Teammates read green, a downed teammate red. Neither colour is used by
        // the beacon strip (owner-chosen) or the ping strip (cyan / amber), so
        // three marker layers can share a screen without a legend.
        private static readonly Color TeamInk = new(0.46f, 0.93f, 0.63f);
        private static readonly Color DownInk = new(0.95f, 0.36f, 0.30f);
        private static readonly Color HealthFull = new(0.42f, 0.90f, 0.50f);
        private static readonly Color HealthEmpty = new(0.92f, 0.28f, 0.24f);

        private const float BarWidth = 52f;

        private sealed class Entry
        {
            public VisualElement Root;
            public VisualElement Dot;
            public VisualElement Arrow;
            public VisualElement BarTrack;
            public VisualElement BarFill;
            public Label Name;
            public Label Distance;
            public Vector2 PanelPos;
            public float Alpha;
            public bool WantedAlive;
            public bool HasPos;
            public bool Occluded;
            public float NextProbe;
        }

        private static VisualElement _layer;
        private static VisualElement _container;
        private static readonly Dictionary<string, Entry> _entries = new();
        private static readonly List<string> _dead = new();
        private static readonly RaycastHit[] _probeHits = new RaycastHit[8];
        private static float _lastTickTime;

        public static void EnsureMounted(VisualElement hudLayer)
        {
            if (hudLayer == null) return;
            if (_layer == hudLayer && _container != null && _container.parent == hudLayer) return;

            _layer = hudLayer;
            _entries.Clear();
            _container = new VisualElement { name = "team-locator", pickingMode = PickingMode.Ignore };
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
            string localId = NetworkSession.LocalPlayerId ?? "";
            bool enabled = VoxelEngine.Settings.GameSettings.TeamLocatorEnabled;
            bool show = enabled
                        && cam != null
                        && !UIState.IsBlocking
                        && !string.IsNullOrEmpty(localId)
                        && TeamRegistry.TeamOf(localId) != null;   // no team, no markers

            if (!show)
            {
                // Off, or no team: the markers are gone the same frame the switch
                // goes off. There is nothing to ease out - they are not expiring,
                // they are disallowed.
                foreach (var stale in _entries.Values) stale.Root.RemoveFromHierarchy();
                _entries.Clear();
                _container.style.display = DisplayStyle.None;
                return;
            }
            _container.style.display = DisplayStyle.Flex;

            // Who counts as a teammate right now: a live avatar, not us, on our
            // team. Distance decides the order, so the nearest never loses a slot.
            var found = new List<(PlayerAvatar avatar, float dist)>();
            Vector3 eye = cam.transform.position;
            foreach (var avatar in PlayerAvatar.All)
            {
                if (avatar == null) continue;
                string id = avatar.PlayerId;
                if (string.IsNullOrEmpty(id) || id == localId) continue;
                if (!TeamRegistry.SameTeam(localId, id)) continue;
                float dist = Vector3.Distance(eye, avatar.transform.position);
                if (dist < MinDistanceM) continue;      // the nameplate is already saying it
                found.Add((avatar, dist));
            }
            if (found.Count > MaxMarkers)
            {
                found.Sort((a, b) => a.dist.CompareTo(b.dist));
                found.RemoveRange(MaxMarkers, found.Count - MaxMarkers);
            }

            Vector2 centre = RuntimePanelUtils.ScreenToPanel(_container.panel,
                new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            Vector2 half = new(Mathf.Max(60f, centre.x - EdgeMargin), Mathf.Max(60f, centre.y - EdgeMargin));

            foreach (var e in _entries.Values) e.WantedAlive = false;

            for (int i = 0; i < found.Count; i++)
            {
                var (avatar, dist) = found[i];
                string id = avatar.PlayerId;

                Vector3 world = avatar.transform.position;
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
                    Vector2 dir = target - centre;
                    if (dir.sqrMagnitude < 0.0001f) dir = new Vector2(0f, -1f);
                    float tx = Mathf.Abs(dir.x) > 0.0001f ? half.x / Mathf.Abs(dir.x) : float.MaxValue;
                    float ty = Mathf.Abs(dir.y) > 0.0001f ? half.y / Mathf.Abs(dir.y) : float.MaxValue;
                    target = centre + dir * Mathf.Min(tx, ty);
                }

                if (!_entries.TryGetValue(id, out var entry))
                {
                    entry = BuildEntry();
                    _entries[id] = entry;
                }
                entry.WantedAlive = true;

                if (!entry.HasPos || !onScreen) entry.PanelPos = target;
                else entry.PanelPos = Vector2.Lerp(entry.PanelPos, target, 1f - Mathf.Exp(-14f * dt));
                entry.HasPos = true;

                // Occlusion is a compass detail, not a per-frame truth: probe a few
                // times a second and let the marker sit with the answer.
                if (now >= entry.NextProbe && onScreen)
                {
                    entry.NextProbe = now + OcclusionIntervalS;
                    entry.Occluded = IsOccluded(eye, world, dist);
                }

                bool down = avatar.HealthPercent <= 0;
                Color ink = down ? DownInk : TeamInk;
                string name = string.IsNullOrEmpty(avatar.PlayerName) ? "Teammate" : avatar.PlayerName;

                entry.Name.text = name;
                entry.Name.style.color = ink;
                entry.Distance.text = down ? "DOWN" : FormatDistance(dist);
                entry.Distance.style.color = down ? DownInk : new Color(0.82f, 0.88f, 0.86f, 0.9f);
                entry.Dot.style.backgroundColor = ink;
                entry.Arrow.style.borderTopColor = entry.Arrow.style.borderRightColor = ink;

                int health = Mathf.Clamp(avatar.HealthPercent, 0, 100);
                entry.BarTrack.style.display = health < 100 ? DisplayStyle.Flex : DisplayStyle.None;
                entry.BarFill.style.width = BarWidth * (health / 100f);
                entry.BarFill.style.backgroundColor = Color.Lerp(HealthEmpty, HealthFull, health / 100f);

                entry.Alpha = Mathf.MoveTowards(entry.Alpha, 1f, EaseSpeed * dt);

                if (onScreen)
                {
                    entry.Dot.style.display = DisplayStyle.Flex;
                    entry.Arrow.style.display = DisplayStyle.None;
                }
                else
                {
                    float angle = Mathf.Atan2(target.y - centre.y, target.x - centre.x) * Mathf.Rad2Deg;
                    entry.Dot.style.display = DisplayStyle.None;
                    entry.Arrow.style.display = DisplayStyle.Flex;
                    entry.Arrow.style.rotate = new Rotate(angle - 45f);
                }

                // Behind a wall the marker stays, quieter - you can still find them,
                // you just cannot pretend you can see them.
                float seen = entry.Occluded && onScreen ? OccludedAlpha : 1f;
                float ease = entry.Alpha * entry.Alpha * (3f - 2f * entry.Alpha);
                entry.Root.style.opacity = seen * ease;
                entry.Root.style.display = DisplayStyle.Flex;
                entry.Root.style.left = entry.PanelPos.x - 70f;
                entry.Root.style.top = entry.PanelPos.y - 12f;
            }

            FadeEverythingOut(dt);
        }

        /// <summary>Ease out every marker that was not wanted this tick, then drop
        /// the element for good.</summary>
        private static void FadeEverythingOut(float dt)
        {
            _dead.Clear();
            foreach (var kv in _entries)
            {
                var entry = kv.Value;
                if (entry.WantedAlive) continue;
                entry.Alpha = Mathf.MoveTowards(entry.Alpha, 0f, FadeSpeed * dt);
                entry.Root.style.opacity = entry.Alpha;
                if (entry.Alpha <= 0.01f) _dead.Add(kv.Key);
            }
            foreach (var id in _dead)
            {
                _entries[id].Root.RemoveFromHierarchy();
                _entries.Remove(id);
            }
        }

        /// <summary>Is the line of sight to that teammate blocked? One short ray,
        /// ignoring the first half metre so the local player's own body, mount or
        /// hull cannot dim everything around them.</summary>
        private static bool IsOccluded(Vector3 eye, Vector3 target, float distance)
        {
            if (distance > ElsewhereM || distance <= 1f) return false;
            Vector3 dir = (target - eye) / distance;
            int count = Physics.RaycastNonAlloc(new Ray(eye, dir), _probeHits,
                distance - 1f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (_probeHits[i].distance > 0.5f) return true;
            return false;
        }

        private static Entry BuildEntry()
        {
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.width = 140f;
            root.style.alignItems = Align.Center;
            root.style.opacity = 0f;

            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.style.width = 9f;
            dot.style.height = 9f;
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius =
            dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 5f;
            dot.style.backgroundColor = TeamInk;
            dot.style.borderTopWidth = dot.style.borderBottomWidth =
            dot.style.borderLeftWidth = dot.style.borderRightWidth = 1f;
            dot.style.borderTopColor = dot.style.borderBottomColor =
            dot.style.borderLeftColor = dot.style.borderRightColor = new Color(0.03f, 0.05f, 0.04f, 0.85f);
            root.Add(dot);

            // The chevron: a square carrying only its top and right edge, turned
            // 45 degrees, so it points the way the teammate lies.
            var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
            arrow.style.width = 12f;
            arrow.style.height = 12f;
            arrow.style.borderTopWidth = 3f;
            arrow.style.borderRightWidth = 3f;
            arrow.style.marginTop = 5f;
            arrow.style.marginBottom = 5f;
            arrow.style.display = DisplayStyle.None;
            root.Add(arrow);

            var name = MakeLabel(11, FontStyle.Bold);
            name.style.marginTop = 3f;
            root.Add(name);

            var distance = MakeLabel(9, FontStyle.Normal);
            distance.style.color = new Color(0.82f, 0.88f, 0.86f, 0.9f);
            root.Add(distance);

            // Health only appears once it has something to say (health < 100).
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.width = BarWidth;
            track.style.height = 3f;
            track.style.marginTop = 3f;
            track.style.backgroundColor = new Color(0.05f, 0.07f, 0.06f, 0.75f);
            track.style.display = DisplayStyle.None;

            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = 3f;
            fill.style.width = BarWidth;
            fill.style.backgroundColor = HealthFull;
            track.Add(fill);
            root.Add(track);

            _container.Add(root);
            return new Entry
            {
                Root = root, Dot = dot, Arrow = arrow,
                BarTrack = track, BarFill = fill,
                Name = name, Distance = distance
            };
        }

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

        private static string FormatDistance(float metres)
        {
            if (metres > ElsewhereM) return "elsewhere";
            if (metres < 1000f) return $"{metres:0} m";
            return metres < 10000f ? $"{metres / 1000f:0.0} km" : $"{metres / 1000f:0} km";
        }
    }
}
