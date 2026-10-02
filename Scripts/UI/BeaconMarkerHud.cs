// Assets/Scripts/VoxelEngine/UI/BeaconMarkerHud.cs
//
// 14.30.0-dev - Multiplayer milestone 10: on-screen beacon markers.
//
// The marker is the half of a beacon you can use: the beam is scenery, the
// diamond on the HUD is navigation. This strip draws one floating marker per
// beacon the local player is allowed to see - and it never decides WHAT that
// set is. BeaconSync.CurrentMarkers() is the single entry point: offline and
// host machines build the list locally through the share rule, a guest draws
// exactly what the host sent and nothing else, so there is no secret for this
// file to keep and no way for it to leak one.
//
// Each marker shows the beacon's name and live distance, tinted cyan for your
// own beacons and amber for ones shared with you. Markers ease in when they
// appear, glide instead of teleporting when a ship-mounted beacon's position
// updates between host sweeps, and fade out when revoked, powered down, or
// carried out of the beacon's chosen range. Only the nearest twelve draw, so
// a beacon-happy session degrades into a tidy HUD rather than a wall of text.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Networking;

namespace VoxelEngine.UI
{
    public static class BeaconMarkerHud
    {
        private const int MaxMarkers = 12;
        private const float EaseSpeed = 10f;     // appear/vanish, per second
        private const float GlideFactor = 12f;   // position smoothing, per second
        private const float SnapDistance = 260f; // px; beyond this, jump instead of glide

        private static readonly Color OwnInk = new(0.30f, 0.85f, 1.00f);
        private static readonly Color SharedInk = new(0.95f, 0.72f, 0.28f);

        private sealed class Entry
        {
            public VisualElement Root;
            public VisualElement Diamond;
            public Label Name;
            public Label Distance;
            public Vector2 PanelPos;
            public float Alpha;       // eased visibility 0..1
            public bool WantedAlive;  // seen in the current marker set this tick
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
            _container = new VisualElement { name = "beacon-markers", pickingMode = PickingMode.Ignore };
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

            // What may we draw, and of that, which markers are in range and nearest?
            var markers = BeaconSync.CurrentMarkers();
            var drawn = new List<(BeaconMarkerRecord record, float dist)>();
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (string.IsNullOrEmpty(m.Id)) continue;
                float dist = Vector3.Distance(eye, m.Position);
                if (m.RangeM > 0f && dist > m.RangeM) continue;   // out of the beacon's reach
                drawn.Add((m, dist));
            }
            if (drawn.Count > MaxMarkers)
            {
                drawn.Sort((a, b) => a.dist.CompareTo(b.dist));
                drawn.RemoveRange(MaxMarkers, drawn.Count - MaxMarkers);
            }

            foreach (var e in _entries.Values) e.WantedAlive = false;

            string localId = NetworkSession.LocalPlayerId;
            for (int i = 0; i < drawn.Count; i++)
            {
                var (m, dist) = drawn[i];

                // Behind the camera or outside the frame: the marker simply is
                // not on screen this tick. It keeps its entry so it reappears
                // without re-easing when the player turns back around.
                Vector3 screen = cam.WorldToScreenPoint(m.Position);
                bool onScreen = screen.z > 0f
                    && screen.x > -40f && screen.x < Screen.width + 40f
                    && screen.y > -40f && screen.y < Screen.height + 40f;

                if (!_entries.TryGetValue(m.Id, out var entry))
                {
                    entry = BuildEntry();
                    _entries[m.Id] = entry;
                }
                entry.WantedAlive = true;
                if (!onScreen) { entry.Root.style.display = DisplayStyle.None; continue; }

                Vector2 target = RuntimePanelUtils.ScreenToPanel(_container.panel,
                    new Vector2(screen.x, Screen.height - screen.y));
                if (!entry.HasPos || (target - entry.PanelPos).sqrMagnitude > SnapDistance * SnapDistance)
                    entry.PanelPos = target;   // first sighting / warp jump: no silly glide across the screen
                else
                    entry.PanelPos = Vector2.Lerp(entry.PanelPos, target,
                        1f - Mathf.Exp(-GlideFactor * dt));
                entry.HasPos = true;

                entry.Alpha = Mathf.MoveTowards(entry.Alpha, 1f, EaseSpeed * dt);
                bool own = !string.IsNullOrEmpty(localId) && m.OwnerId == localId;
                Color ink = own ? OwnInk : SharedInk;

                entry.Name.text = string.IsNullOrEmpty(m.Name) ? "Beacon" : m.Name;
                entry.Name.style.color = ink;
                entry.Distance.text = FormatDistance(dist);
                entry.Diamond.style.borderTopColor = entry.Diamond.style.borderBottomColor =
                entry.Diamond.style.borderLeftColor = entry.Diamond.style.borderRightColor = ink;

                // Distance keeps far markers quiet: full presence close by,
                // a faint pin at the horizon.
                float presence = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(300f, 6000f, dist));
                float ease = entry.Alpha * entry.Alpha * (3f - 2f * entry.Alpha); // smoothstep
                entry.Root.style.opacity = presence * ease;
                entry.Root.transform.scale = Vector3.one * Mathf.Lerp(0.7f, 1f, ease);
                entry.Root.style.display = DisplayStyle.Flex;
                entry.Root.style.left = entry.PanelPos.x - 60f;
                entry.Root.style.top = entry.PanelPos.y - 7f;
            }

            // Ease out whatever the host revoked, the owner switched off, or
            // the player walked out of range of - then drop the element.
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
            root.style.width = 120f;
            root.style.alignItems = Align.Center;
            root.style.opacity = 0f;

            var diamond = new VisualElement { pickingMode = PickingMode.Ignore };
            diamond.style.width = 9f;
            diamond.style.height = 9f;
            diamond.style.borderTopWidth = diamond.style.borderBottomWidth =
            diamond.style.borderLeftWidth = diamond.style.borderRightWidth = 2f;
            diamond.style.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 0.55f);
            diamond.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            root.Add(diamond);

            var name = MakeLabel(11, FontStyle.Bold);
            name.style.marginTop = 4f;
            root.Add(name);

            var distance = MakeLabel(9, FontStyle.Normal);
            distance.style.color = new Color(0.80f, 0.85f, 0.92f, 0.85f);
            root.Add(distance);

            _container.Add(root);
            return new Entry { Root = root, Diamond = diamond, Name = name, Distance = distance };
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
            if (metres < 1000f) return $"{metres:0} m";
            return metres < 10000f ? $"{metres / 1000f:0.0} km" : $"{metres / 1000f:0} km";
        }
    }
}
