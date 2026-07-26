// Assets/Scripts/VoxelEngine/UI/CryobedConfigHud.cs
// Premium cryobed configuration panel: live status, oxygen tank visual, naming, ownership.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class CryobedConfigHud
    {
        private static VisualElement _root, _overlay;
        private static Cryobed _staticBed;
        private static GridCryobed _gridBed;
        private static bool _blocking;

        // Live references for ticking without full rebuild.
        private static Label _statusPill;
        private static Label _powerLabel;
        private static Label _oxygenLabel;
        private static Label _ownershipLabel;
        private static VisualElement _oxygenFill;
        private static Label _oxygenPctLabel;
        private static VisualElement _panelRef;
        private static float _nextRefreshTime;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _overlay != null && _overlay.parent == uiRoot) return;
            _root = uiRoot;
            if (_overlay != null) _overlay.RemoveFromHierarchy();
            _overlay = new VisualElement { name = "CryobedConfigHud" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0; _overlay.style.right = 0; _overlay.style.top = 0; _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.45f));
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.display = DisplayStyle.None;
            uiRoot.Add(_overlay);
        }

        public static void Open(Cryobed bed) { _staticBed = bed; _gridBed = null; Show(); }
        public static void Open(GridCryobed bed) { _gridBed = bed; _staticBed = null; Show(); }

        private static void Show()
        {
            if (_overlay == null) return;
            if (!_blocking) { UIState.PushBlock(); _blocking = true; }
            Rebuild();
        }

        public static void Tick()
        {
            if (_blocking && VoxelEngine.Settings.GameSettings.WasPressed(VoxelEngine.Settings.InputAction.Pause))
            {
                UIState.PauseConsumedFrame = Time.frameCount;
                Close();
                return;
            }

            if (_overlay == null || _overlay.style.display == DisplayStyle.None) return;
            if (_staticBed == null && _gridBed == null) return;

            // Live update ~10hz plus every frame for smooth tank? We'll tick every frame but throttle text heavy calc.
            if (Time.time >= _nextRefreshTime || Time.frameCount % 3 == 0)
            {
                RefreshLiveStats();
                _nextRefreshTime = Time.time + 0.10f;
            }
        }

        public static void Close()
        {
            _staticBed = null; _gridBed = null;
            _statusPill = null; _powerLabel = null; _oxygenLabel = null; _ownershipLabel = null;
            _oxygenFill = null; _oxygenPctLabel = null; _panelRef = null;
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
            if (_blocking) { UIState.PopBlock(); _blocking = false; }
        }

        private static void Rebuild()
        {
            _overlay.Clear();
            _overlay.style.display = DisplayStyle.Flex;
            bool isGrid = _gridBed != null;
            string name = isGrid ? (_gridBed.blockName ?? "Grid Cryobed") : _staticBed != null ? (_staticBed.displayName ?? "Cryobed") : "Cryobed";
            string status = isGrid ? _gridBed.AvailabilityText : _staticBed != null ? _staticBed.AvailabilityText : "OFFLINE";
            bool online = isGrid ? _gridBed.IsAvailableForRespawn : _staticBed != null && _staticBed.IsAvailableForRespawn;

            var panel = new VisualElement();
            _panelRef = panel;
            panel.style.width = 540; panel.style.maxWidth = new StyleLength(new Length(92f, LengthUnit.Percent));
            panel.style.paddingLeft = 20; panel.style.paddingRight = 20; panel.style.paddingTop = 18; panel.style.paddingBottom = 18;
            panel.style.backgroundColor = new StyleColor(new Color(0.035f, 0.045f, 0.060f, 0.98f));
            T.Radius(panel, 14); T.Border(panel, 1, online ? new Color(0.30f, 0.95f, 0.62f, 0.45f) : new Color(0.95f, 0.30f, 0.18f, 0.45f));
            _overlay.Add(panel);

            // Header
            var titleRow = new VisualElement(); titleRow.style.flexDirection = FlexDirection.Row; titleRow.style.alignItems = Align.Center; titleRow.style.marginBottom = 12;
            var title = new Label("CRYOBED CONTROL"); title.style.flexGrow = 1; title.style.fontSize = 18; title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.letterSpacing = 1.4f; title.style.color = new Color(0.45f,0.85f,1f); titleRow.Add(title);
            var pill = new Label(status); 
            _statusPill = pill;
            pill.style.fontSize = 10; pill.style.unityFontStyleAndWeight = FontStyle.Bold; 
            pill.style.color = online ? new Color(0.30f,0.95f,0.62f) : new Color(0.95f,0.62f,0.18f); 
            pill.style.backgroundColor = new StyleColor(new Color(0,0,0,0.35f)); 
            pill.style.paddingLeft = 8; pill.style.paddingRight = 8; pill.style.paddingTop = 3; pill.style.paddingBottom = 3; 
            T.Radius(pill, 10); titleRow.Add(pill);
            panel.Add(titleRow);

            var nameField = new TextField("Name") { value = name };
            nameField.style.marginBottom = 12;
            nameField.RegisterValueChangedCallback(evt => { SetName(evt.newValue); });
            panel.Add(nameField);

            // Power row
            var powerRow = new VisualElement(); powerRow.style.flexDirection = FlexDirection.Row; powerRow.style.marginBottom = 8; powerRow.style.alignItems = Align.Center;
            var powerLeft = new Label("Power"); powerLeft.style.width = 110; powerLeft.style.color = new Color(0.62f,0.70f,0.78f); powerLeft.style.fontSize = 11; powerRow.Add(powerLeft);
            var powerRight = new Label(PowerText()); powerRight.style.flexGrow = 1; powerRight.style.color = Color.white; powerRight.style.fontSize = 11; powerRight.style.whiteSpace = WhiteSpace.Normal;
            _powerLabel = powerRight;
            powerRow.Add(powerRight);
            panel.Add(powerRow);

            // Oxygen section: label + tank visual + details
            var oxygenRow = new VisualElement(); oxygenRow.style.flexDirection = FlexDirection.Row; oxygenRow.style.marginBottom = 10; oxygenRow.style.alignItems = Align.FlexStart;
            var oxyLeft = new Label("Oxygen"); oxyLeft.style.width = 110; oxyLeft.style.color = new Color(0.62f,0.70f,0.78f); oxyLeft.style.fontSize = 11; oxyLeft.style.marginTop = 4; oxygenRow.Add(oxyLeft);

            var oxyRightWrap = new VisualElement(); oxyRightWrap.style.flexGrow = 1; oxyRightWrap.style.flexDirection = FlexDirection.Row; oxyRightWrap.style.alignItems = Align.FlexStart;
            oxygenRow.Add(oxyRightWrap);

            // Tank graphic
            var tankOuter = new VisualElement { name = "OxygenTankOuter" };
            tankOuter.style.width = 58;
            tankOuter.style.height = 112;
            tankOuter.style.backgroundColor = new StyleColor(new Color(0.055f, 0.075f, 0.105f, 1f));
            tankOuter.style.borderTopWidth = 1; tankOuter.style.borderBottomWidth = 1; tankOuter.style.borderLeftWidth = 1; tankOuter.style.borderRightWidth = 1;
            tankOuter.style.borderTopColor = new Color(0.20f, 0.70f, 0.95f, 0.35f);
            tankOuter.style.borderBottomColor = new Color(0.20f, 0.70f, 0.95f, 0.35f);
            tankOuter.style.borderLeftColor = new Color(0.20f, 0.70f, 0.95f, 0.35f);
            tankOuter.style.borderRightColor = new Color(0.20f, 0.70f, 0.95f, 0.35f);
            T.Radius(tankOuter, 11);
            tankOuter.style.position = Position.Relative;
            tankOuter.style.overflow = Overflow.Hidden;
            tankOuter.style.marginRight = 12;

            // Fill element
            var fill = new VisualElement { name = "OxygenTankFill" };
            _oxygenFill = fill;
            fill.style.position = Position.Absolute;
            fill.style.left = 3; fill.style.right = 3; fill.style.bottom = 3;
            // height set live
            fill.style.backgroundColor = new StyleColor(new Color(0.30f, 0.88f, 1f, 0.95f));
            T.Radius(fill, 7);
            tankOuter.Add(fill);

            // Subtle inner highlight gradient line at top of fill handled via border? We'll add a gloss.
            var gloss = new VisualElement();
            gloss.style.position = Position.Absolute;
            gloss.style.left = 4; gloss.style.right = 4; gloss.style.top = 3; gloss.style.height = 18;
            gloss.style.backgroundColor = new StyleColor(new Color(1f,1f,1f,0.10f));
            T.Radius(gloss, 6);
            gloss.pickingMode = PickingMode.Ignore;
            tankOuter.Add(gloss);

            // Percentage label centered over tank
            var pctLabel = new Label("0%");
            _oxygenPctLabel = pctLabel;
            pctLabel.style.position = Position.Absolute;
            pctLabel.style.left = 0; pctLabel.style.right = 0; pctLabel.style.top = 0; pctLabel.style.bottom = 0;
            pctLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            pctLabel.style.fontSize = 11;
            pctLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            pctLabel.style.color = Color.white;
            pctLabel.pickingMode = PickingMode.Ignore;
            tankOuter.Add(pctLabel);

            oxyRightWrap.Add(tankOuter);

            var detailsCol = new VisualElement(); detailsCol.style.flexGrow = 1; detailsCol.style.flexDirection = FlexDirection.Column;
            var oxyText = new Label(OxygenText());
            _oxygenLabel = oxyText;
            oxyText.style.color = Color.white; oxyText.style.fontSize = 11; oxyText.style.whiteSpace = WhiteSpace.Normal;
            oxyText.style.marginBottom = 4;
            detailsCol.Add(oxyText);

            var oxyHint = new Label(isGrid ? "Tank level live · refills from piped O₂" : "Room / piped O₂ status");
            oxyHint.style.fontSize = 9; oxyHint.style.color = new Color(0.62f,0.70f,0.78f); oxyHint.style.marginTop = 2;
            detailsCol.Add(oxyHint);

            oxyRightWrap.Add(detailsCol);
            panel.Add(oxygenRow);

            // Ownership
            var ownRow = new VisualElement(); ownRow.style.flexDirection = FlexDirection.Row; ownRow.style.marginBottom = 5;
            var ownLeft = new Label("Ownership"); ownLeft.style.width = 110; ownLeft.style.color = new Color(0.62f,0.70f,0.78f); ownLeft.style.fontSize = 11; ownRow.Add(ownLeft);
            var ownRight = new Label(IsOwned() ? (IsActiveSpawn() ? "Owned by you · active spawn" : "Owned by you") : "Unclaimed");
            _ownershipLabel = ownRight;
            ownRight.style.flexGrow = 1; ownRight.style.color = Color.white; ownRight.style.fontSize = 11;
            ownRow.Add(ownRight);
            panel.Add(ownRow);

            // Position readout live
            var posRow = new VisualElement(); posRow.style.flexDirection = FlexDirection.Row; posRow.style.marginTop = 4; posRow.style.marginBottom = 4;
            var posLeft = new Label("Location"); posLeft.style.width = 110; posLeft.style.color = new Color(0.62f,0.70f,0.78f); posLeft.style.fontSize = 10; posRow.Add(posLeft);
            var posRight = new Label(FormatPosition(CurrentSpawnPos())); posRight.style.flexGrow = 1; posRight.style.color = new Color(0.80f,0.86f,0.92f); posRight.style.fontSize = 10; posRow.Add(posRight);
            panel.Add(posRow);

            var buttons = new VisualElement(); buttons.style.flexDirection = FlexDirection.Row; buttons.style.flexWrap = Wrap.Wrap; buttons.style.marginTop = 14;
            var claimButton = MakeButton(IsOwned() ? (IsActiveSpawn() ? "Active Spawn" : "Claimed") : "Claim", () => { Claim(); Rebuild(); }, online ? T.AccentCyan : Color.gray);
            buttons.Add(claimButton);
            buttons.Add(MakeButton("Remove", () => { RemoveOwnership(); Rebuild(); }, new Color(0.95f,0.62f,0.18f)));
            buttons.Add(MakeButton("Transfer", () => BuildFeedbackHud.Show("Transfer", "Multiplayer ownership transfer will unlock later", null, T.AccentAmber), new Color(0.55f,0.58f,0.66f)));
            buttons.Add(MakeButton("Close", Close, new Color(0.18f,0.22f,0.28f)));
            panel.Add(buttons);

            RefreshLiveStats();
        }

        private static void RefreshLiveStats()
        {
            if (_gridBed == null && _staticBed == null) return;
            bool isGrid = _gridBed != null;
            bool online = isGrid ? _gridBed.IsAvailableForRespawn : _staticBed != null && _staticBed.IsAvailableForRespawn;
            string status = isGrid ? _gridBed.AvailabilityText : _staticBed != null ? _staticBed.AvailabilityText : "OFFLINE";

            if (_statusPill != null)
            {
                _statusPill.text = status;
                _statusPill.style.color = online ? new Color(0.30f,0.95f,0.62f) : new Color(0.95f,0.62f,0.18f);
                if (_panelRef != null)
                    T.Border(_panelRef, 1, online ? new Color(0.30f,0.95f,0.62f,0.45f) : new Color(0.95f,0.30f,0.18f,0.45f));
            }

            if (_powerLabel != null) _powerLabel.text = PowerText();
            if (_oxygenLabel != null) _oxygenLabel.text = OxygenText();
            if (_ownershipLabel != null) _ownershipLabel.text = IsOwned() ? (IsActiveSpawn() ? "Owned by you · active spawn" : "Owned by you") : "Unclaimed";

            // Oxygen tank fill
            float pct = 0f;
            float stored = 0f, cap = 1f;
            if (isGrid)
            {
                stored = _gridBed.oxygenStored;
                cap = Mathf.Max(0.01f, _gridBed.oxygenCapacity);
                pct = Mathf.Clamp01(stored / cap);
            }
            else if (_staticBed != null)
            {
                pct = _staticBed.HasOxygenEnvironment ? 1f : 0f;
                stored = pct * 100f;
                cap = 100f;
            }

            if (_oxygenFill != null)
            {
                // Height in percent minus tiny border offset
                float h = Mathf.Clamp(pct * 100f, 0f, 100f);
                // Leave 3px bottom border, so use percent height but we set style.height percent
                _oxygenFill.style.height = new StyleLength(new Length(h, LengthUnit.Percent));
                // Color cue: empty = amber/red, low = yellow, ok = cyan/green
                Color fillColor;
                if (!online) fillColor = new Color(0.95f,0.35f,0.20f,0.90f); // offline/low O2
                else if (pct < 0.15f) fillColor = new Color(0.95f,0.62f,0.18f,0.92f);
                else if (pct < 0.35f) fillColor = new Color(0.95f,0.82f,0.25f,0.92f);
                else fillColor = new Color(0.30f,0.88f,1f,0.95f);
                _oxygenFill.style.backgroundColor = new StyleColor(fillColor);
            }
            if (_oxygenPctLabel != null)
            {
                _oxygenPctLabel.text = $"{pct*100f:0}%";
                // Make black text if high fill for contrast? Keep white with shadow for readability.
            }
        }

        private static string FormatPosition(Vector3 p) => $"{p.x:0}, {p.y:0}, {p.z:0}";

        private static Button MakeButton(string text, System.Action action, Color color)
        {
            var b = new Button(action) { text = text }; b.style.height = 30; b.style.minWidth = 78; b.style.marginRight = 6; b.style.marginBottom = 6; b.style.color = Color.white; b.style.backgroundColor = new StyleColor(color); b.style.unityFontStyleAndWeight = FontStyle.Bold; T.Radius(b, 6); return b;
        }

        private static bool IsOwned()
        {
            if (_gridBed != null) return _gridBed.claimedByLocalPlayer;
            if (_staticBed != null) return _staticBed.claimedByLocalPlayer;
            return false;
        }

        private static bool IsActiveSpawn()
        {
            var session = VoxelEngine.Menu.WorldSession.Instance; if (session == null || !session.hasBedSpawn) return false;
            Vector3 pos = CurrentSpawnPos(); return (session.bedSpawnPoint - pos).sqrMagnitude < 2.5f;
        }
        private static Vector3 CurrentSpawnPos()
        {
            if (_gridBed != null) return _gridBed.SpawnPoint;
            if (_staticBed != null) return _staticBed.SpawnPoint;
            return Vector3.zero;
        }
        private static void Claim() { if (_gridBed != null) _gridBed.ClaimAsSpawn(); else _staticBed?.ClaimAsSpawn(); }
        private static void RemoveOwnership()
        {
            if (_gridBed != null) _gridBed.claimedByLocalPlayer = false;
            if (_staticBed != null) _staticBed.claimedByLocalPlayer = false;
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session != null && IsActiveSpawn()) { session.hasBedSpawn = false; session.SaveSpawnSidecar(); }
            BuildFeedbackHud.Show("Cryobed", "Ownership removed", null, T.AccentAmber);
        }
        private static void SetName(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "Cryobed" : value.Trim();
            if (_gridBed != null) _gridBed.blockName = value;
            if (_staticBed != null) _staticBed.displayName = value;
        }
        private static string PowerText() => _gridBed != null ? _gridBed.PowerEstimateText : _staticBed != null ? _staticBed.PowerEstimateText : "Unknown";
        private static string OxygenText() => _gridBed != null ? _gridBed.OxygenEstimateText : _staticBed != null ? _staticBed.OxygenEstimateText : "Unknown";
    }
}
