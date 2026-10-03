// Assets/Scripts/VoxelEngine/UI/SecurityBlockHud.cs
//
// 14.39.0 - Security Block configuration panel.
//
// Premium side-docked panel (CryobedConfigHud pattern). Shows the guard's
// live status - armed/no power, draw, how many racks it covers - and lets
// THE OWNER pick the access mode: PRIVATE / TEAM / GLOBAL. Everyone else
// sees the panel read-only: the mode currently enforced and who owns it.
//
// A mode change marks the block as locally interacted (ContainerSync
// window) so MachineSync replicates the new setting through the existing
// factory-runtime seam - no new wire messages.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Storage;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class SecurityBlockHud
    {
        private static VisualElement _root, _overlay;
        private static SecurityBlock _block;
        private static bool _blocking;

        private static Label _statusPill;
        private static Label _powerLabel;
        private static Label _coverageLabel;
        private static float _nextRefreshTime;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _overlay != null && _overlay.parent == uiRoot) return;
            _root = uiRoot;
            if (_overlay != null) _overlay.RemoveFromHierarchy();
            _overlay = new VisualElement { name = "SecurityBlockHud" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0; _overlay.style.right = 0; _overlay.style.top = 0; _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0f));
            _overlay.style.display = DisplayStyle.None;
            uiRoot.Add(_overlay);
        }

        public static void Open(SecurityBlock block)
        {
            if (_overlay == null || block == null) return;
            _block = block;
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
            if (_block == null) { Close(); return; }

            if (Time.time >= _nextRefreshTime)
            {
                RefreshLiveStats();
                _nextRefreshTime = Time.time + 0.25f;
            }
        }

        public static void Close()
        {
            _block = null;
            _statusPill = null; _powerLabel = null; _coverageLabel = null;
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
            if (_blocking) { UIState.PopBlock(); _blocking = false; }
        }

        private static bool LocalIsOwner()
        {
            if (_block == null) return false;
            if (string.IsNullOrEmpty(_block.ownerId)) return true; // legacy/unowned: anyone may claim settings
            return (Networking.NetworkSession.LocalPlayerId ?? "") == _block.ownerId;
        }

        private static string OwnerText()
        {
            if (_block == null || string.IsNullOrEmpty(_block.ownerId)) return "Unowned (legacy)";
            string me = Networking.NetworkSession.LocalPlayerId ?? "";
            if (me == _block.ownerId) return "You";
            return Networking.TeamRegistry.SameTeam(_block.ownerId, me) ? "A teammate" : "Another player";
        }

        private static void RefreshLiveStats()
        {
            if (_block == null) return;
            bool armed = _block.IsArmed;
            if (_statusPill != null)
            {
                _statusPill.text = armed ? "ARMED" : "NO NETWORK";
                _statusPill.style.color = armed ? T.AccentGreen : T.AccentAmber;
            }
            if (_powerLabel != null)
                _powerLabel.text = armed
                    ? $"{_block.wattsPerSecond:0} W drawn from the system budget - guard active"
                    : $"adds {_block.wattsPerSecond:0} W to the system once linked - guard is DOWN";
            if (_coverageLabel != null)
            {
                // 14.40.0: coverage is network membership, not a radius.
                var controller = _block.Controller;
                if (controller == null)
                    _coverageLabel.text = "Not linked to any Server Controller - connect with Data Pipes or touching blocks";
                else if (!controller.IsOnline)
                    _coverageLabel.text = "Linked to a Server Controller (system offline - the guard stands down)";
                else
                    _coverageLabel.text = $"Guarding this network: {controller.NasCount} NAS, {controller.TerminalCount} terminals, {controller.TransmitterCount} wireless";
            }
        }

        private static void Rebuild()
        {
            if (_overlay == null || _block == null) return;
            _overlay.Clear();
            _overlay.style.display = DisplayStyle.Flex;

            bool armed = _block.IsArmed;
            bool owner = LocalIsOwner();

            var panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.top = 24; panel.style.bottom = 92; panel.style.right = 18;
            panel.style.width = new StyleLength(new Length(30f, LengthUnit.Percent));
            panel.style.minWidth = 320; panel.style.maxWidth = 480;
            panel.style.paddingLeft = 20; panel.style.paddingRight = 20;
            panel.style.paddingTop = 18; panel.style.paddingBottom = 18;
            panel.style.backgroundColor = new StyleColor(new Color(0.035f, 0.045f, 0.060f, 0.98f));
            T.Radius(panel, 14);
            T.Border(panel, 1, armed ? new Color(0.95f, 0.30f, 0.18f, 0.45f) : new Color(0.45f, 0.50f, 0.55f, 0.35f));
            _overlay.Add(panel);

            // ── Header ───────────────────────────────────────────────
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.style.marginBottom = 12;
            var shield = LucideIcons.Make(LucideIcons.Shield, 18, armed ? T.AccentRed : T.TextSecondary);
            shield.style.marginRight = 8;
            titleRow.Add(shield);
            var title = new Label("SECURITY BLOCK");
            title.style.flexGrow = 1; title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.4f;
            title.style.color = new Color(0.45f, 0.85f, 1f);
            titleRow.Add(title);
            var pill = new Label(armed ? "ARMED" : "NO POWER");
            _statusPill = pill;
            pill.style.fontSize = 10; pill.style.unityFontStyleAndWeight = FontStyle.Bold;
            pill.style.color = armed ? T.AccentGreen : T.AccentAmber;
            pill.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.35f));
            pill.style.paddingLeft = 8; pill.style.paddingRight = 8;
            pill.style.paddingTop = 3; pill.style.paddingBottom = 3;
            T.Radius(pill, 10);
            titleRow.Add(pill);
            panel.Add(titleRow);

            // ── Status rows ──────────────────────────────────────────
            panel.Add(StatRow("Power", out _powerLabel));
            panel.Add(StatRow("Coverage", out _coverageLabel));
            var ownerRow = StatRow("Owner", out var ownerLabel);
            ownerLabel.text = OwnerText();
            panel.Add(ownerRow);
            RefreshLiveStats();

            panel.Add(T.Spacer(10));

            // ── Access mode ──────────────────────────────────────────
            var modeHeader = new Label("ACCESS MODE");
            modeHeader.style.fontSize = 11;
            modeHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            modeHeader.style.letterSpacing = 1.1f;
            modeHeader.style.color = T.TextSecondary;
            modeHeader.style.marginBottom = 6;
            panel.Add(modeHeader);

            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.marginBottom = 8;
            modeRow.Add(ModeButton("PRIVATE", (int)StorageAccessMode.Private, owner,
                "Only you can open the guarded storage."));
            modeRow.Add(ModeButton("TEAM", (int)StorageAccessMode.Team, owner,
                "You and your team can open it."));
            modeRow.Add(ModeButton("GLOBAL", (int)StorageAccessMode.Global, owner,
                "Everyone can open it."));
            panel.Add(modeRow);

            var modeDetail = new Label(ModeDescription(_block.accessMode));
            modeDetail.style.fontSize = 11;
            modeDetail.style.color = T.TextSecondary;
            modeDetail.style.whiteSpace = WhiteSpace.Normal;
            modeDetail.style.marginBottom = 10;
            panel.Add(modeDetail);

            if (!owner)
            {
                var notice = new Label("Only the owner can change the access mode.");
                notice.style.fontSize = 11;
                notice.style.color = T.AccentAmber;
                notice.style.whiteSpace = WhiteSpace.Normal;
                notice.style.marginBottom = 10;
                panel.Add(notice);
            }

            // ── Wireless sharing (14.40.0) ───────────────────────────
            // Wireless access is NEVER global: the owner always has it, and
            // this checkbox optionally extends it to the owner's team.
            var wirelessHeader = new Label("WIRELESS ACCESS");
            wirelessHeader.style.fontSize = 11;
            wirelessHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            wirelessHeader.style.letterSpacing = 1.1f;
            wirelessHeader.style.color = T.TextSecondary;
            wirelessHeader.style.marginBottom = 6;
            panel.Add(wirelessHeader);

            var shareToggle = new Toggle("Share wireless access with my team")
            {
                value = _block.wirelessTeamShare
            };
            shareToggle.style.fontSize = 11;
            shareToggle.style.color = Color.white;
            shareToggle.style.marginBottom = 4;
            shareToggle.SetEnabled(owner);
            shareToggle.RegisterValueChangedCallback(evt =>
            {
                if (_block == null || !LocalIsOwner()) { shareToggle.SetValueWithoutNotify(_block != null && _block.wirelessTeamShare); return; }
                _block.SetWirelessTeamShare(evt.newValue);
                var pbw = _block.GetComponent<PlacedBlock>();
                if (pbw != null) Networking.ContainerSync.NotifyLocalInteraction(pbw);
                BuildFeedbackHud.Show("Security Updated",
                    evt.newValue ? "Teammates may now use wireless terminals on this network."
                                 : "Wireless access is now owner-only.",
                    null, T.AccentGreen);
            });
            panel.Add(shareToggle);

            var wirelessNote = new Label("Wireless access is never global. The owner always has it; " +
                                         "this setting only extends it to teammates with a handheld Wireless Terminal.");
            wirelessNote.style.fontSize = 10;
            wirelessNote.style.color = T.TextSecondary;
            wirelessNote.style.whiteSpace = WhiteSpace.Normal;
            wirelessNote.style.marginBottom = 10;
            panel.Add(wirelessNote);

            panel.Add(T.Spacer(6));

            // ── Raid rule footnote ───────────────────────────────────
            var hint = new Label("While armed, this block guards its whole storage network: " +
                                 "terminals, the controller and NAS shelves refuse anyone the mode excludes. " +
                                 "There is no hacking - raiders must destroy the block or cut the system's power.");
            hint.style.fontSize = 10;
            hint.style.color = T.TextSecondary;
            hint.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(hint);

            panel.Add(T.Spacer(10));

            var closeBtn = new Button(Close) { text = "CLOSE" };
            closeBtn.style.height = 30;
            closeBtn.style.fontSize = 12;
            closeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            panel.Add(closeBtn);
        }

        private static VisualElement StatRow(string label, out Label valueLabel)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 6;
            row.style.alignItems = Align.Center;
            var left = new Label(label);
            left.style.width = 90;
            left.style.color = new Color(0.62f, 0.70f, 0.78f);
            left.style.fontSize = 11;
            row.Add(left);
            valueLabel = new Label("-");
            valueLabel.style.flexGrow = 1;
            valueLabel.style.color = Color.white;
            valueLabel.style.fontSize = 11;
            valueLabel.style.whiteSpace = WhiteSpace.Normal;
            row.Add(valueLabel);
            return row;
        }

        private static string ModeDescription(int mode)
        {
            switch (Mathf.Clamp(mode, 0, 2))
            {
                case (int)StorageAccessMode.Private: return "Only the owner can open the guarded storage.";
                case (int)StorageAccessMode.Team:    return "The owner and their team can open the guarded storage.";
                default:                             return "Everyone can open the guarded storage.";
            }
        }

        private static VisualElement ModeButton(string label, int mode, bool interactable, string tooltip)
        {
            bool selected = _block != null && Mathf.Clamp(_block.accessMode, 0, 2) == mode;
            var btn = new Button(() =>
            {
                if (_block == null || !LocalIsOwner()) return;
                if (Mathf.Clamp(_block.accessMode, 0, 2) == mode) return;
                _block.SetMode(mode);
                // Mark the block player-edited so MachineSync replicates the
                // new mode through the factory-runtime seam (client window).
                var pb = _block.GetComponent<PlacedBlock>();
                if (pb != null) Networking.ContainerSync.NotifyLocalInteraction(pb);
                BuildFeedbackHud.Show("Security Updated",
                    $"Storage access is now {label}.", null, T.AccentGreen);
                Rebuild();
            }) { text = label, tooltip = tooltip };
            btn.style.flexGrow = 1;
            btn.style.height = 30;
            btn.style.fontSize = 11;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.marginRight = 4;
            if (selected)
            {
                btn.style.backgroundColor = new StyleColor(new Color(0.12f, 0.30f, 0.22f, 0.95f));
                btn.style.color = T.AccentGreen;
                T.Border(btn, 1, T.AccentGreen);
            }
            else
            {
                btn.style.backgroundColor = new StyleColor(new Color(0.08f, 0.10f, 0.13f, 0.95f));
                btn.style.color = interactable ? Color.white : T.TextSecondary;
            }
            btn.SetEnabled(interactable || selected);
            return btn;
        }
    }
}
