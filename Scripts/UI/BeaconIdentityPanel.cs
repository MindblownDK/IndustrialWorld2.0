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
