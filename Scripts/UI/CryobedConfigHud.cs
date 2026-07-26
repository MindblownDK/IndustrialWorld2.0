// Assets/Scripts/VoxelEngine/UI/CryobedConfigHud.cs
// Premium cryobed configuration panel: status, estimates, naming, ownership.

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
            }
        }

        public static void Close()
        {
            _staticBed = null; _gridBed = null;
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
            if (_blocking) { UIState.PopBlock(); _blocking = false; }
        }

        private static void Rebuild()
        {
            _overlay.Clear();
            _overlay.style.display = DisplayStyle.Flex;
            bool isGrid = _gridBed != null;
            string name = isGrid ? _gridBed.blockName : _staticBed != null ? _staticBed.displayName : "Cryobed";
            string status = isGrid ? _gridBed.AvailabilityText : _staticBed != null ? _staticBed.AvailabilityText : "OFFLINE";
            bool online = isGrid ? _gridBed.IsAvailableForRespawn : _staticBed != null && _staticBed.IsAvailableForRespawn;

            var panel = new VisualElement();
            panel.style.width = 520; panel.style.paddingLeft = 20; panel.style.paddingRight = 20; panel.style.paddingTop = 18; panel.style.paddingBottom = 18;
            panel.style.backgroundColor = new StyleColor(new Color(0.035f, 0.045f, 0.060f, 0.98f));
            T.Radius(panel, 14); T.Border(panel, 1, online ? new Color(0.30f, 0.95f, 0.62f, 0.45f) : new Color(0.95f, 0.30f, 0.18f, 0.45f));
            _overlay.Add(panel);

            var titleRow = new VisualElement(); titleRow.style.flexDirection = FlexDirection.Row; titleRow.style.alignItems = Align.Center; titleRow.style.marginBottom = 12;
            var title = new Label("CRYOBED CONTROL"); title.style.flexGrow = 1; title.style.fontSize = 18; title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.letterSpacing = 1.4f; title.style.color = new Color(0.45f,0.85f,1f); titleRow.Add(title);
            var pill = new Label(status); pill.style.fontSize = 10; pill.style.unityFontStyleAndWeight = FontStyle.Bold; pill.style.color = online ? new Color(0.30f,0.95f,0.62f) : new Color(0.95f,0.62f,0.18f); pill.style.backgroundColor = new StyleColor(new Color(0,0,0,0.35f)); pill.style.paddingLeft = 8; pill.style.paddingRight = 8; pill.style.paddingTop = 3; pill.style.paddingBottom = 3; T.Radius(pill, 10); titleRow.Add(pill);
            panel.Add(titleRow);

            var nameField = new TextField("Name") { value = name };
            nameField.style.marginBottom = 10;
            nameField.RegisterValueChangedCallback(evt => { SetName(evt.newValue); });
            panel.Add(nameField);

            AddStat(panel, "Power", PowerText());
            AddStat(panel, "Oxygen", OxygenText());
            AddStat(panel, "Ownership", IsOwned() ? (IsActiveSpawn() ? "Owned by you · active spawn" : "Owned by you") : "Unclaimed");

            var buttons = new VisualElement(); buttons.style.flexDirection = FlexDirection.Row; buttons.style.flexWrap = Wrap.Wrap; buttons.style.marginTop = 12;
            var claimButton = MakeButton(IsOwned() ? "Claimed" : "Claim", () => { Claim(); Rebuild(); }, online ? T.AccentCyan : Color.gray);
            claimButton.SetEnabled(online);
            buttons.Add(claimButton);
            buttons.Add(MakeButton("Remove", () => { RemoveOwnership(); Rebuild(); }, new Color(0.95f,0.62f,0.18f)));
            buttons.Add(MakeButton("Transfer", () => BuildFeedbackHud.Show("Transfer", "Multiplayer ownership transfer will unlock later", null, T.AccentAmber), new Color(0.55f,0.58f,0.66f)));
            buttons.Add(MakeButton("Close", Close, new Color(0.18f,0.22f,0.28f)));
            panel.Add(buttons);
        }

        private static void AddStat(VisualElement panel, string left, string right)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 5;
            var l = new Label(left); l.style.width = 110; l.style.color = new Color(0.62f,0.70f,0.78f); l.style.fontSize = 11; row.Add(l);
            var r = new Label(right); r.style.flexGrow = 1; r.style.color = Color.white; r.style.fontSize = 11; row.Add(r);
            panel.Add(row);
        }

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
            Vector3 pos = CurrentSpawnPos(); return (session.bedSpawnPoint - pos).sqrMagnitude < 1.5f;
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
