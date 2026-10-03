// Assets/Scripts/VoxelEngine/UI/BeaconIdentityPanel.cs
//
// 14.30.0-dev - Multiplayer milestone 10: the beacon settings section.
//
// One builder serves both beacon panels - the grid Beacon and the stationary
// radar tower - because the decisions on a beacon are the same regardless of
// what it is bolted to: what it is called, how far its marker reaches, and
// WHO MAY SEE IT. The share row is the milestone's decided rule made into
// three buttons: do-not-share (default - nothing leaks until the owner says
// so), team (the owner's teammates, live since 14.33.0),
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
            // Themed + focus-guarded: the plain TextField rendered its label
            // black on the dark panel and the 4 Hz live rebuild recreated the
            // field mid-word, kicking the player out of the box.
            var nameField = T.NameField("Name", beacon.BeaconName, editable);
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
                BeaconShare.Team => "Your team sees this beacon's marker - the rest of the session does not.",
                _ => "Only you see this beacon's marker. Nothing is shared until you say so.",
            }));
            section.Add(T.Spacer(6));

            // ── colour (14.31.0 / 14.32.0) ────────────────────────────────
            // Eight presets plus a custom mixer to the LEFT of them: a swatch
            // is one honest click, the mixer reaches every colour the eight
            // do not, and the HUD marker, beam and lamp all repaint live
            // through BeaconTint either way.
            AddColourSection(section, beacon, editable, onChanged);
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

        // ── custom colour mixer (14.32.0) ────────────────────────────────

        // True while a custom-colour slider is being dragged. GameUIController
        // feeds this into its live-panel rebuild guard, so the mixer is never
        // destroyed mid-drag (the same class of bug that ate the name field
        // while typing).
        public static bool IsColourSliderHeld { get; private set; }

        private static bool MatchesAnyPreset(Color c)
        {
            foreach (var (preset, _) in Swatches)
            {
                if (Mathf.Abs(c.r - preset.r) < 0.02f &&
                    Mathf.Abs(c.g - preset.g) < 0.02f &&
                    Mathf.Abs(c.b - preset.b) < 0.02f) return true;
            }
            return false;
        }

        private static void AddColourSection(VisualElement section, IBeaconSource beacon,
            bool editable, System.Action onChanged)
        {
            section.Add(T.Subtitle("Colour"));

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;

            bool customSelected = !MatchesAnyPreset(beacon.BeaconTint);
            // The custom swatch shows the beacon's own colour when it is
            // already off-preset, and a neutral steel tone otherwise.
            var customColour = customSelected ? beacon.BeaconTint : new Color(0.55f, 0.60f, 0.68f);

            var mixer = new VisualElement();
            mixer.style.display = DisplayStyle.None;
            mixer.style.marginTop = 2;
            bool mixerOpen = false;
            bool mixerDirty = false;
            // Declared before the rows (their live callbacks repaint it) and
            // assigned after (its click opens them) - the callbacks only run
            // once the whole section is built, so the split is safe.
            Button customSwatch = null;

            // R/G/B rows. Live-dragging retints the beacon immediately (the
            // property setter refreshes beam, lamp and marker) but does NOT
            // fire onChanged - that would rebuild the panel and destroy the
            // slider under the pointer. The commit happens once, on release.
            var rRow = MakeMixerRow("R", new Color(0.90f, 0.25f, 0.25f), beacon.BeaconTint.r, v =>
            {
                mixerDirty = true;
                beacon.BeaconTint = new Color(v, beacon.BeaconTint.g, beacon.BeaconTint.b);
                customSwatch.style.backgroundColor = beacon.BeaconTint;
            });
            var gRow = MakeMixerRow("G", new Color(0.25f, 0.85f, 0.35f), beacon.BeaconTint.g, v =>
            {
                mixerDirty = true;
                beacon.BeaconTint = new Color(beacon.BeaconTint.r, v, beacon.BeaconTint.b);
                customSwatch.style.backgroundColor = beacon.BeaconTint;
            });
            var bRow = MakeMixerRow("B", new Color(0.30f, 0.55f, 0.95f), beacon.BeaconTint.b, v =>
            {
                mixerDirty = true;
                beacon.BeaconTint = new Color(beacon.BeaconTint.r, beacon.BeaconTint.g, v);
                customSwatch.style.backgroundColor = beacon.BeaconTint;
            });
            System.Action commitMix = () =>
            {
                if (!mixerDirty) return;
                mixerDirty = false;
                onChanged?.Invoke();
            };
            AttachMixerCommit(rRow.slider, commitMix);
            AttachMixerCommit(gRow.slider, commitMix);
            AttachMixerCommit(bRow.slider, commitMix);
            mixer.Add(rRow.row);
            mixer.Add(gRow.row);
            mixer.Add(bRow.row);

            customSwatch = new Button(() =>
            {
                mixerOpen = !mixerOpen;
                mixer.style.display = mixerOpen ? DisplayStyle.Flex : DisplayStyle.None;
                if (mixerOpen)
                {
                    // Open at the beacon's current colour, whatever it is.
                    rRow.slider.SetValueWithoutNotify(beacon.BeaconTint.r);
                    gRow.slider.SetValueWithoutNotify(beacon.BeaconTint.g);
                    bRow.slider.SetValueWithoutNotify(beacon.BeaconTint.b);
                }
                else
                {
                    // Closing the mixer commits any in-flight mix exactly once.
                    commitMix();
                }
            }) { tooltip = "Custom colour" };
            customSwatch.text = "";
            customSwatch.style.width = 24;
            customSwatch.style.height = 24;
            customSwatch.style.marginRight = 6;
            customSwatch.style.marginBottom = 4;
            customSwatch.style.backgroundColor = customColour;
            customSwatch.style.borderTopLeftRadius = customSwatch.style.borderTopRightRadius =
            customSwatch.style.borderBottomLeftRadius = customSwatch.style.borderBottomRightRadius = 4;
            var customRing = customSelected ? Color.white : new Color(0f, 0f, 0f, 0.45f);
            float customRingW = customSelected ? 2f : 1f;
            customSwatch.style.borderTopColor = customSwatch.style.borderBottomColor =
            customSwatch.style.borderLeftColor = customSwatch.style.borderRightColor = customRing;
            customSwatch.style.borderTopWidth = customSwatch.style.borderBottomWidth =
            customSwatch.style.borderLeftWidth = customSwatch.style.borderRightWidth = customRingW;
            customSwatch.SetEnabled(editable);

            row.Add(customSwatch);
            foreach (var (swatch, label) in Swatches)
                AddColourSwatch(row, beacon, swatch, label, editable, onChanged);
            section.Add(row);
            section.Add(mixer);
        }

        /// <summary>One R/G/B slider row for the mixer. `live` fires on every
        /// value change (including keyboard nudges) and owns the live retint.
        /// The value label tracks the integer the player sees.</summary>
        private static (VisualElement row, Slider slider) MakeMixerRow(string channel, Color handleColour, float initial,
            System.Action<float> live)
        {
            var rowEl = new VisualElement();
            rowEl.style.flexDirection = FlexDirection.Row;
            rowEl.style.alignItems = Align.Center;
            rowEl.style.marginBottom = 2;

            var ch = new Label(channel);
            ch.style.width = 14;
            ch.style.color = new StyleColor(T.TextSecondary);
            ch.style.fontSize = 10;
            ch.style.unityFontStyleAndWeight = FontStyle.Bold;
            ch.pickingMode = PickingMode.Ignore;

            var slider = new Slider(0f, 255f) { value = initial };
            slider.style.flexGrow = 1;
            var dragger = slider.Q(className: "unity-base-slider__dragger");
            if (dragger != null) dragger.style.backgroundColor = new StyleColor(handleColour);

            var valueLabel = new Label(Mathf.RoundToInt(initial).ToString());
            valueLabel.style.width = 28;
            valueLabel.style.color = new StyleColor(T.TextSecondary);
            valueLabel.style.fontSize = 10;
            valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            valueLabel.pickingMode = PickingMode.Ignore;

            slider.RegisterValueChangedCallback(evt =>
            {
                valueLabel.text = Mathf.RoundToInt(evt.newValue).ToString();
                live(evt.newValue);
            });

            rowEl.Add(ch);
            rowEl.Add(slider);
            rowEl.Add(valueLabel);
            return (rowEl, slider);
        }

        /// <summary>Holds the rebuild guard while the slider is dragged and
        /// commits the mix (once) when the drag or keyboard nudge ends.</summary>
        private static void AttachMixerCommit(Slider slider, System.Action commit)
        {
            if (slider == null) return;
            slider.RegisterCallback<PointerDownEvent>(_ => IsColourSliderHeld = true);
            slider.RegisterCallback<PointerUpEvent>(_ =>
            {
                IsColourSliderHeld = false;
                commit?.Invoke();
            });
            slider.RegisterCallback<KeyDownEvent>(evt =>
            {
                // Arrow-key nudging ends the edit on Enter.
                if (evt != null && evt.keyCode == KeyCode.Return) commit?.Invoke();
            });
            slider.RegisterCallback<FocusOutEvent>(_ =>
            {
                IsColourSliderHeld = false;
                commit?.Invoke();
            });
            slider.RegisterCallback<DetachFromPanelEvent>(_ => IsColourSliderHeld = false);
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
