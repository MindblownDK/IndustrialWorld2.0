// Assets/Scripts/VoxelEngine/UI/BeaconIdentityPanel.cs
//
// 14.30.0-dev - Multiplayer milestone 10: the beacon settings section.
//
// One builder serves both beacon panels - the grid Beacon and the stationary
// radar tower - because the decisions on a beacon are the same regardless of
// what it is bolted to: what it is called, how far its marker reaches, and
// WHO MAY SEE IT. The share row is the milestone's decided rule made into
// three buttons: do-not-share (default - nothing leaks until the owner says
// so), team (stored from day one, honoured the moment milestone 11 lands),
// and global. Only the owner can change any of it; everyone else gets a
// read-only view and an honest sentence saying so.
//
// The caller supplies onChanged, which is where each host/guest panel hooks
// its own sync notification - this file knows nothing about the network.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.GridSystem;
using VoxelEngine.Networking;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class BeaconIdentityPanel
    {
        /// <summary>The identity + sharing rows for one beacon. Appended into a
        /// themed panel by the caller.</summary>
        public static VisualElement Build(IBeaconSource beacon, System.Action onChanged)
        {
            var section = new VisualElement();
            if (beacon == null) return section;

            string localId = NetworkSession.LocalPlayerId;
            bool owned = !string.IsNullOrEmpty(beacon.BeaconOwnerId);
            bool editable = !owned || beacon.BeaconOwnerId == localId;

            // ── owner ──────────────────────────────────────────────────────
            string ownerText;
            if (!owned) ownerText = "Unclaimed";
            else if (beacon.BeaconOwnerId == localId) ownerText = "You";
            else
            {
                var presence = NetworkSession.GetPlayer(beacon.BeaconOwnerId);
                ownerText = presence != null && !string.IsNullOrEmpty(presence.displayName)
                    ? presence.displayName : "Another player";
            }
            section.Add(T.StatRow("⌂", "Owner", ownerText,
                editable ? T.AccentTeal : T.AccentDim));
            section.Add(T.Spacer(6));

            // ── name ───────────────────────────────────────────────────────
            var nameField = new TextField("Name") { value = beacon.BeaconName, isDelayed = true };
            nameField.SetEnabled(editable);
            nameField.RegisterValueChangedCallback(evt =>
            {
                beacon.BeaconName = evt.newValue;
                onChanged?.Invoke();
            });
            section.Add(nameField);
            section.Add(T.Spacer(6));

            // ── sharing ────────────────────────────────────────────────────
            section.Add(T.Subtitle("Sharing"));
            var shareRow = new VisualElement();
            shareRow.style.flexDirection = FlexDirection.Row;
            shareRow.style.flexWrap = Wrap.Wrap;
            AddShareButton(shareRow, beacon, BeaconShare.Private, "PRIVATE", editable, onChanged);
            AddShareButton(shareRow, beacon, BeaconShare.Team, "TEAM", editable, onChanged);
            AddShareButton(shareRow, beacon, BeaconShare.Global, "GLOBAL", editable, onChanged);
            section.Add(shareRow);
            section.Add(T.Spacer(2));
            section.Add(T.Muted(beacon.BeaconShareMode switch
            {
                BeaconShare.Global => "Everyone in the session sees this beacon's marker.",
                BeaconShare.Team => "Your team will see this beacon once teams arrive. Until then, only you see it.",
                _ => "Only you see this beacon's marker. Nothing is shared until you say so.",
            }));
            section.Add(T.Spacer(6));

            // ── colour (14.31.0) ───────────────────────────────────────────
            // Eight presets, not a picker: a swatch is one honest click, reads
            // identically on every screen, and the HUD marker, beam and lamp
            // all repaint live through BeaconTint.
            section.Add(T.Subtitle("Colour"));
            var colourRow = new VisualElement();
            colourRow.style.flexDirection = FlexDirection.Row;
            colourRow.style.flexWrap = Wrap.Wrap;
            foreach (var (swatch, label) in Swatches)
                AddColourSwatch(colourRow, beacon, swatch, label, editable, onChanged);
            section.Add(colourRow);
            section.Add(T.Spacer(6));

            // ── marker range ───────────────────────────────────────────────
            section.Add(T.Subtitle("Marker Range"));
            var rangeRow = new VisualElement();
            rangeRow.style.flexDirection = FlexDirection.Row;
            rangeRow.style.flexWrap = Wrap.Wrap;
            AddRangeButton(rangeRow, beacon, 500f, "500 M", editable, onChanged);
            AddRangeButton(rangeRow, beacon, 2000f, "2 KM", editable, onChanged);
            AddRangeButton(rangeRow, beacon, 10000f, "10 KM", editable, onChanged);
            AddRangeButton(rangeRow, beacon, 0f, "UNLIMITED", editable, onChanged);
            section.Add(rangeRow);
            section.Add(T.Spacer(2));
            section.Add(T.Muted(beacon.BeaconRangeM > 0f
                ? $"The marker shows while you are within {FormatRange(beacon.BeaconRangeM)} of the beacon."
                : "The marker shows from any distance."));

            if (!editable)
            {
                section.Add(T.Spacer(4));
                section.Add(T.Muted("Only the owner can change this beacon's name, sharing, or range."));
            }
            return section;
        }

        // The classic sky-cyan first, because it is the authored default on
        // both beacon blocks and most beacons will simply stay on it.
        private static readonly (Color colour, string label)[] Swatches =
        {
            (new Color(0.30f, 0.80f, 1.00f), "Sky"),
            (new Color(0.25f, 0.45f, 1.00f), "Blue"),
            (new Color(0.25f, 0.95f, 0.55f), "Green"),
            (new Color(1.00f, 0.92f, 0.30f), "Yellow"),
            (new Color(1.00f, 0.60f, 0.15f), "Amber"),
            (new Color(1.00f, 0.30f, 0.25f), "Red"),
            (new Color(0.95f, 0.35f, 0.95f), "Magenta"),
            (new Color(0.95f, 0.96f, 1.00f), "White"),
        };

        private static void AddColourSwatch(VisualElement row, IBeaconSource beacon,
            Color colour, string label, bool editable, System.Action onChanged)
        {
            var current = beacon.BeaconTint;
            bool selected = Mathf.Abs(current.r - colour.r) < 0.02f
                         && Mathf.Abs(current.g - colour.g) < 0.02f
                         && Mathf.Abs(current.b - colour.b) < 0.02f;

            var swatch = new Button(() =>
            {
                beacon.BeaconTint = colour;
                onChanged?.Invoke();
            }) { tooltip = label };
            swatch.text = "";
            swatch.style.width = 24;
            swatch.style.height = 24;
            swatch.style.marginRight = 4;
            swatch.style.marginBottom = 4;
            swatch.style.backgroundColor = colour;
            swatch.style.borderTopLeftRadius = swatch.style.borderTopRightRadius =
            swatch.style.borderBottomLeftRadius = swatch.style.borderBottomRightRadius = 4;
            var ring = selected ? Color.white : new Color(0f, 0f, 0f, 0.45f);
            float ringW = selected ? 2f : 1f;
            swatch.style.borderTopColor = swatch.style.borderBottomColor =
            swatch.style.borderLeftColor = swatch.style.borderRightColor = ring;
            swatch.style.borderTopWidth = swatch.style.borderBottomWidth =
            swatch.style.borderLeftWidth = swatch.style.borderRightWidth = ringW;
            swatch.SetEnabled(editable);
            row.Add(swatch);
        }

        private static void AddShareButton(VisualElement row, IBeaconSource beacon,
            BeaconShare mode, string label, bool editable, System.Action onChanged)
        {
            bool selected = beacon.BeaconShareMode == mode;
            var btn = T.SmallButton(label, () =>
            {
                if (beacon.BeaconShareMode == mode) return;
                beacon.BeaconShareMode = mode;
                onChanged?.Invoke();
            }, selected ? T.AccentCyan : T.BgSlot);
            btn.SetEnabled(editable);
            btn.style.marginRight = 4;
            btn.style.marginBottom = 4;
            row.Add(btn);
        }

        private static void AddRangeButton(VisualElement row, IBeaconSource beacon,
            float metres, string label, bool editable, System.Action onChanged)
        {
            bool selected = Mathf.Approximately(beacon.BeaconRangeM, metres);
            var btn = T.SmallButton(label, () =>
            {
                if (Mathf.Approximately(beacon.BeaconRangeM, metres)) return;
                beacon.BeaconRangeM = metres;
                onChanged?.Invoke();
            }, selected ? T.AccentCyan : T.BgSlot);
            btn.SetEnabled(editable);
            btn.style.marginRight = 4;
            btn.style.marginBottom = 4;
            row.Add(btn);
        }

        private static string FormatRange(float metres)
            => metres < 1000f ? $"{metres:0} m" : $"{metres / 1000f:0.#} km";
    }
}
