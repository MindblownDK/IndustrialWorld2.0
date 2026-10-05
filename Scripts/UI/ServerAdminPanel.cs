// Assets/Scripts/VoxelEngine/UI/ServerAdminPanel.cs
//
// 14.47.0-dev - the SERVER ADMINISTRATION page.
//
// Everything an owner or admin can DO with a server happens here: kick and
// ban players (timed or permanent), lift bans, promote and demote admins,
// run the whitelist, set the join password, and edit every world rule live.
// Every control only ASKS (ServerAdminRegistry.Route): the host decides,
// and the answer comes back as the refreshed state this panel is drawn
// from. Nothing is applied optimistically.
//
// A player without rank sees the claim box instead: on a dedicated server
// whose server_config.json carries an adminPassword, entering it here makes
// them the owner (first-join auto-claim covers fresh worlds; this covers
// recovery and handovers). One player can own any number of servers - each
// world remembers its own owner.
//
// The pause menu supplies the chrome and the live refresh (it rebuilds
// whenever ServerAdminRegistry.Version moves); this file supplies content.

using System;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Networking;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class ServerAdminPanel
    {
        // Drafts survive live rebuilds - a state refresh mid-typing must
        // never eat what the player wrote (the TeamsPanel lesson).
        private static string _draftClaim = "";
        private static string _draftWhitelist = "";
        private static string _draftPassword = "";
        private static string _draftServerName;
        private static string _actionTargetId;   // player whose kick/ban row is open
        private static bool _actionIsBan;        // which of the two rows it is
        private static string _draftReason = ""; // the parting words, max 40 chars

        private static float _savedScroll;

        /// <summary>14.47.1 - everything this page draws, folded into one
        /// number. The pause menu rebuilds when it moves: admin state and
        /// team limits (versions), players joining/leaving/renaming (the
        /// presence fold), and the minute bucket so ban countdowns tick.
        /// Watching a signature instead of rebuilding on a timer means an
        /// admin mid-typing is never interrupted without cause.</summary>
        public static int LiveSignature()
        {
            unchecked
            {
                int sig = ServerAdminRegistry.Version * 31 + TeamRegistry.Version;
                // 14.49.0 - player icons show on the cards, so a crest change
                // redraws the page like any other live fact.
                sig = sig * 31 + VoxelEngine.Networking.PlayerCosmeticsRegistry.Version;
                foreach (var p in NetworkSession.Players)
                {
                    sig = sig * 31 + (p.playerId != null ? p.playerId.GetHashCode() : 0);
                    sig = sig * 31 + (p.displayName != null ? p.displayName.GetHashCode() : 0);
                }
                sig = sig * 31 + (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute % 100000);
                return sig;
            }
        }

        public static VisualElement Build(Action rebuild)
        {
            var scroll = new ScrollView();
            scroll.style.maxHeight = 560;
            scroll.mode = ScrollViewMode.Vertical;
            UITheme.StyleScroller(scroll);
            var content = new VisualElement();
            scroll.Add(content);

            float restoreTo = _savedScroll;
            bool restored = false;
            content.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (restored) return;
                restored = true;
                if (restoreTo > 0f)
                    scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0f, restoreTo));
                scroll.schedule.Execute(() =>
                {
                    if (scroll.panel != null) _savedScroll = scroll.scrollOffset.y;
                }).Every(120);
            });

            int rank = ServerAdminRegistry.LocalRank();
            var state = ServerAdminRegistry.State;
            bool isHostMachine = NetworkSession.Mode == SessionMode.Host;
            bool dedicated = isHostMachine
                ? DedicatedServer.IsActive
                : ServerAdminRegistry.ClientDedicated;

            // ── header line: who am I here ────────────────────────────────
            string rankName = rank == ServerAdminRegistry.RankOwner ? "OWNER"
                            : rank == ServerAdminRegistry.RankAdmin ? "ADMIN" : "PLAYER";
            var who = T.StatLabel(
                $"You are {rankName} on this {(dedicated ? "dedicated" : "listen")} server.",
                rank > 0 ? T.AccentGreen : T.TextSecondary);
            content.Add(who);
            content.Add(T.Spacer(10));

            if (rank < ServerAdminRegistry.RankAdmin)
            {
                BuildClaimSection(content, dedicated);
                return scroll;
            }

            BuildPlayersSection(content, rank, rebuild);
            BuildBansSection(content, state);
            BuildWhitelistSection(content, state, rebuild);
            if (rank >= ServerAdminRegistry.RankOwner)
                BuildPasswordSection(content, state);
            BuildRulesSection(content, rank, dedicated, isHostMachine);

            return scroll;
        }

        // ───────────────────────── claim (rank: none) ─────────────────────────

        private static void BuildClaimSection(VisualElement content, bool dedicated)
        {
            var blurb = T.Muted(dedicated
                ? "Only the server owner and admins can open this panel.\n\n" +
                  "A fresh dedicated server belongs to the first player who ever joins it. " +
                  "An existing one can be claimed with the admin password from the server's " +
                  "server_config.json:"
                : "Only the host of a listen server administrates it - ask the player " +
                  "hosting this world.");
            blurb.style.whiteSpace = WhiteSpace.Normal;
            content.Add(blurb);

            if (!dedicated) return;

            content.Add(T.Spacer(10));
            var field = ThemedField(_draftClaim);
            field.isPasswordField = true;
            field.RegisterValueChangedCallback(e => _draftClaim = e.newValue);
            content.Add(field);
            content.Add(T.Spacer(6));
            var row = Row();
            row.style.justifyContent = Justify.FlexEnd;
            row.Add(SmallBtn("CLAIM OWNERSHIP", () =>
            {
                ServerAdminRegistry.Route(ServerAdminRegistry.OpClaimOwner, "", _draftClaim, 0);
                _draftClaim = "";
            }, T.AccentAmber));
            content.Add(row);
        }

        // ───────────────────────── players online ─────────────────────────

        private static void BuildPlayersSection(VisualElement content, int myRank, Action rebuild)
        {
            content.Add(SectionLabel("PLAYERS ONLINE"));
            string me = NetworkSession.LocalPlayerId;
            int others = 0;

            foreach (var p in NetworkSession.Players)
            {
                if (p.playerId == me) continue;
                others++;
                int theirRank = RankShown(p.playerId);
                var card = Card(null);

                // 14.49.0 - the player's custom icon rides the name line.
                var head = Row();
                head.style.alignItems = Align.Center;
                var crest = VoxelEngine.Networking.PlayerCosmeticsRegistry.TextureOf(p.playerId);
                if (crest != null)
                {
                    var img = new Image { image = crest, scaleMode = ScaleMode.ScaleToFit };
                    img.style.width = 20;
                    img.style.height = 20;
                    img.style.marginRight = 6;
                    T.Radius(img, 3);
                    head.Add(img);
                }
                var line = T.Body(p.displayName
                    + (theirRank == ServerAdminRegistry.RankOwner ? "  (owner)"
                     : theirRank == ServerAdminRegistry.RankAdmin ? "  (admin)" : ""));
                line.style.whiteSpace = WhiteSpace.Normal;
                line.style.flexShrink = 1;
                head.Add(line);
                card.Add(head);

                bool actionable = theirRank < myRank;
                var row = Row();
                row.style.justifyContent = Justify.FlexEnd;
                row.style.marginTop = 6;

                if (actionable)
                {
                    string id = p.playerId;
                    row.Add(SmallBtn("KICK...", () =>
                    {
                        bool wasOpen = _actionTargetId == id && !_actionIsBan;
                        _actionTargetId = wasOpen ? null : id;
                        _actionIsBan = false;
                        rebuild();
                    }, T.AccentRed));
                    row.Add(SmallBtn("BAN...", () =>
                    {
                        bool wasOpen = _actionTargetId == id && _actionIsBan;
                        _actionTargetId = wasOpen ? null : id;
                        _actionIsBan = true;
                        rebuild();
                    }, T.AccentRed));

                    if (myRank >= ServerAdminRegistry.RankOwner)
                    {
                        if (theirRank == ServerAdminRegistry.RankAdmin)
                            row.Add(SmallBtn("DEMOTE", () =>
                                ServerAdminRegistry.Route(ServerAdminRegistry.OpDemote, id, "", 0), T.BgSlot));
                        else
                            row.Add(SmallBtn("MAKE ADMIN", () =>
                                ServerAdminRegistry.Route(ServerAdminRegistry.OpPromote, id, "", 0), T.AccentCyan));
                    }
                }
                else
                {
                    var note = T.Muted("equal or higher rank");
                    row.Add(note);
                }

                // 14.60.3 - the owner's house key: teleport to any player,
                // regardless of the teammate-teleport world rule.
                if (myRank >= ServerAdminRegistry.RankOwner)
                {
                    string tpId = p.playerId;
                    row.Add(SmallBtn("TELEPORT", () =>
                        VoxelEngine.Player.PlayerTeleport.ToPlayer(tpId), T.AccentCyan));
                }
                card.Add(row);

                // The open kick/ban row, right under its player: the parting
                // words (max 40 characters, shown in their goodbye modal and
                // stored on the ban), then the confirmation.
                if (_actionTargetId == p.playerId && actionable)
                {
                    string id = p.playerId;
                    var reasonField = ThemedField(_draftReason);
                    reasonField.maxLength = 40;
                    reasonField.RegisterValueChangedCallback(e => _draftReason = e.newValue);
                    card.Add(T.Muted(_actionIsBan ? "BAN MESSAGE (OPTIONAL, MAX 40)" : "KICK MESSAGE (OPTIONAL, MAX 40)"));
                    card.Add(reasonField);

                    var confirm = Row();
                    confirm.style.justifyContent = Justify.FlexEnd;
                    confirm.style.marginTop = 4;
                    if (_actionIsBan)
                    {
                        confirm.Add(SmallBtn("1 HOUR", () => Act(id, true, 3600), T.AccentRed));
                        confirm.Add(SmallBtn("24 HOURS", () => Act(id, true, 86400), T.AccentRed));
                        confirm.Add(SmallBtn("7 DAYS", () => Act(id, true, 604800), T.AccentRed));
                        confirm.Add(SmallBtn("PERMANENT", () => Act(id, true, 0), T.AccentRed));
                    }
                    else
                    {
                        confirm.Add(SmallBtn("CONFIRM KICK", () => Act(id, false, 0), T.AccentRed));
                    }
                    card.Add(confirm);
                }

                content.Add(card);
            }

            if (others == 0)
            {
                var none = T.Muted("Nobody else is online right now.");
                none.style.whiteSpace = WhiteSpace.Normal;
                content.Add(none);
            }
            content.Add(T.Spacer(12));
        }

        private static void Act(string playerId, bool ban, long seconds)
        {
            _actionTargetId = null;
            string reason = (_draftReason ?? "").Trim();
            _draftReason = "";
            ServerAdminRegistry.Route(
                ban ? ServerAdminRegistry.OpBan : ServerAdminRegistry.OpKick,
                playerId, reason, seconds);
        }

        /// <summary>Rank as this machine can see it: the host asks the
        /// registry, a privileged client reads the mirrored roster.</summary>
        private static int RankShown(string playerId)
        {
            if (NetworkSession.Mode == SessionMode.Host) return ServerAdminRegistry.RankOf(playerId);
            var state = ServerAdminRegistry.State;
            if (playerId == state.ownerId) return ServerAdminRegistry.RankOwner;
            foreach (var a in state.admins)
                if (a.id == playerId) return ServerAdminRegistry.RankAdmin;
            return ServerAdminRegistry.RankNone;
        }

        // ───────────────────────── bans ─────────────────────────

        private static void BuildBansSection(VisualElement content, AdminState state)
        {
            content.Add(SectionLabel("BANS"));
            if (state.bans.Count == 0)
            {
                content.Add(T.Muted("No active bans."));
            }
            else
            {
                // Remaining time is a delta against the HOST's clock. The
                // host computes live; a client uses the snapshot's pair of
                // (until, now) ticks - never its own wall clock.
                long now = NetworkSession.Mode == SessionMode.Host
                    ? DateTime.UtcNow.Ticks : state.nowTicks;
                foreach (var ban in state.bans)
                {
                    var card = Card(T.AccentRed);
                    string when = ban.untilTicks == 0
                        ? "permanent"
                        : Remaining(TimeSpan.FromTicks(Math.Max(0, ban.untilTicks - now))) + " left";
                    var line = T.Body($"{ban.name}  -  {when}"
                        + (string.IsNullOrEmpty(ban.reason) ? "" : $"  ({ban.reason})"));
                    line.style.whiteSpace = WhiteSpace.Normal;
                    card.Add(line);

                    var row = Row();
                    row.style.justifyContent = Justify.FlexEnd;
                    row.style.marginTop = 6;
                    string id = ban.id;
                    row.Add(SmallBtn("UNBAN", () =>
                        ServerAdminRegistry.Route(ServerAdminRegistry.OpUnban, id, "", 0), T.AccentGreen));
                    card.Add(row);
                    content.Add(card);
                }
            }
            content.Add(T.Spacer(12));
        }

        private static string Remaining(TimeSpan span)
        {
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
            return $"{Math.Max(1, (int)span.TotalMinutes)}m";
        }

        // ───────────────────────── whitelist ─────────────────────────

        private static void BuildWhitelistSection(VisualElement content, AdminState state, Action rebuild)
        {
            content.Add(SectionLabel("WHITELIST"));

            var toggleRow = Row();
            var label = T.Body(state.whitelistEnabled
                ? "ON - only listed players may join."
                : "OFF - anyone may join (password permitting).");
            label.style.flexGrow = 1;
            label.style.whiteSpace = WhiteSpace.Normal;
            toggleRow.Add(label);
            toggleRow.Add(SmallBtn(state.whitelistEnabled ? "TURN OFF" : "TURN ON", () =>
                ServerAdminRegistry.Route(ServerAdminRegistry.OpWhitelistEnable, "", "",
                    state.whitelistEnabled ? 0 : 1),
                state.whitelistEnabled ? T.AccentRed : T.AccentGreen));
            content.Add(toggleRow);
            content.Add(T.Spacer(6));

            foreach (var entry in state.whitelist)
            {
                var row = Row();
                var name = T.Body(entry.name + (string.IsNullOrEmpty(entry.id) ? "  (by name)" : ""));
                name.style.flexGrow = 1;
                name.style.whiteSpace = WhiteSpace.Normal;
                row.Add(name);
                string id = entry.id, text = entry.name;
                row.Add(SmallBtn("REMOVE", () =>
                    ServerAdminRegistry.Route(ServerAdminRegistry.OpWhitelistRemove, id, text, 0), T.BgSlot));
                content.Add(row);
            }

            var field = ThemedField(_draftWhitelist);
            field.RegisterValueChangedCallback(e => _draftWhitelist = e.newValue);
            content.Add(field);
            content.Add(T.Spacer(4));
            var addRow = Row();
            addRow.style.justifyContent = Justify.FlexEnd;
            addRow.Add(SmallBtn("ADD", () =>
            {
                ServerAdminRegistry.Route(ServerAdminRegistry.OpWhitelistAdd, "", _draftWhitelist, 0);
                _draftWhitelist = "";
                rebuild();
            }, T.AccentCyan));
            content.Add(addRow);

            var hint = T.Muted("Add by player name or id. An online player's entry pins to their " +
                               "stable id; a typed name matches at the door until they first join.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            content.Add(hint);
            content.Add(T.Spacer(12));
        }

        // ───────────────────────── password (owner) ─────────────────────────

        private static void BuildPasswordSection(VisualElement content, AdminState state)
        {
            content.Add(SectionLabel("SERVER PASSWORD"));
            bool set = NetworkSession.Mode == SessionMode.Host
                ? !string.IsNullOrEmpty(state.password) : state.passwordSet;
            content.Add(T.Muted(set
                ? "A join password is SET. You and your admins never need it."
                : "No join password - the server is open."));
            content.Add(T.Spacer(4));

            var field = ThemedField(_draftPassword);
            field.isPasswordField = true;
            field.RegisterValueChangedCallback(e => _draftPassword = e.newValue);
            content.Add(field);
            content.Add(T.Spacer(4));
            var row = Row();
            row.style.justifyContent = Justify.FlexEnd;
            row.Add(SmallBtn("SET", () =>
            {
                ServerAdminRegistry.Route(ServerAdminRegistry.OpSetPassword, "", _draftPassword, 0);
                _draftPassword = "";
            }, T.AccentCyan));
            if (set)
                row.Add(SmallBtn("CLEAR", () =>
                    ServerAdminRegistry.Route(ServerAdminRegistry.OpSetPassword, "", "", 0), T.AccentRed));
            content.Add(row);
            content.Add(T.Spacer(12));
        }

        // ───────────────────────── world rules ─────────────────────────

        private static void BuildRulesSection(VisualElement content, int rank, bool dedicated, bool isHostMachine)
        {
            content.Add(SectionLabel("WORLD SETTINGS"));
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session == null)
            {
                content.Add(T.Muted("No world session."));
                return;
            }

            content.Add(RuleToggle("Friendly fire", session.friendlyFire, "friendlyFire"));
            content.Add(RuleToggle("Ruin loot respawn", session.allowRuinLootRespawn, "allowRuinLootRespawn"));
            content.Add(RuleToggle("Banner painting", session.allowBannerPainting, "allowBannerPainting"));
            content.Add(RuleToggle("Teammate teleport", session.allowTeammateTeleport, "allowTeammateTeleport"));
            content.Add(RuleToggle("Drop-void warning", session.showDropVoidWarning, "showDropVoidWarning"));
            content.Add(RuleStepper("Max dropped items", session.maxDroppedItems, 50, 10000, 100, "maxDroppedItems"));
            content.Add(RuleStepper("Inventory weight %", session.inventoryWeightPercent, 25, 1000, 25, "inventoryWeightPercent"));
            content.Add(RuleStepper("Container weight %", session.containerWeightPercent, 25, 1000, 25, "containerWeightPercent"));
            content.Add(RuleStepper("Max teams", TeamRegistry.MaxTeams, 1, 8, 1, "maxTeams"));
            content.Add(RuleStepper("Max team members", TeamRegistry.MaxMembers, 2, 8, 1, "maxMembers"));

            if (dedicated && rank >= ServerAdminRegistry.RankOwner)
            {
                content.Add(T.Spacer(8));
                content.Add(SectionLabel("SERVER MACHINE"));
                int autosave = isHostMachine
                    ? VoxelEngine.Settings.GameSettings.AutosaveSeconds
                    : -1;
                if (autosave >= 0)
                    content.Add(RuleStepper("Autosave seconds (0 = off)", autosave, 0, 3600, 60, "autosaveSeconds"));
                else
                    content.Add(AutosaveRemoteRow());

                if (_draftServerName == null)
                    _draftServerName = isHostMachine && DedicatedServer.IsActive
                        ? DedicatedServer.Config.serverName : "";
                var nameField = ThemedField(_draftServerName);
                nameField.RegisterValueChangedCallback(e => _draftServerName = e.newValue);
                content.Add(T.Muted("SERVER NAME"));
                content.Add(nameField);
                content.Add(T.Spacer(4));
                var row = Row();
                row.style.justifyContent = Justify.FlexEnd;
                row.Add(SmallBtn("APPLY NAME", () =>
                    ServerAdminRegistry.Route(ServerAdminRegistry.OpSetRule, "serverName", _draftServerName, 0),
                    T.AccentCyan));
                content.Add(row);
            }

            var note = T.Muted("Changes apply at once for every player and ride the next save." +
                               (dedicated ? " On a dedicated server they are also written back to server_config.json." : ""));
            note.style.whiteSpace = WhiteSpace.Normal;
            content.Add(T.Spacer(6));
            content.Add(note);
        }

        /// <summary>Remote admins cannot read the server machine's autosave
        /// value; offer the common choices instead of a lying stepper.</summary>
        private static VisualElement AutosaveRemoteRow()
        {
            var row = Row();
            var label = T.Body("Autosave");
            label.style.flexGrow = 1;
            row.Add(label);
            row.Add(SmallBtn("OFF", () => SetRule("autosaveSeconds", "0"), T.BgSlot));
            row.Add(SmallBtn("5 MIN", () => SetRule("autosaveSeconds", "300"), T.BgSlot));
            row.Add(SmallBtn("10 MIN", () => SetRule("autosaveSeconds", "600"), T.BgSlot));
            return row;
        }

        private static VisualElement RuleToggle(string label, bool value, string key)
        {
            var row = Row();
            var name = T.Body(label);
            name.style.flexGrow = 1;
            row.Add(name);
            var state = T.StatLabel(value ? "ON" : "OFF", value ? T.AccentGreen : T.TextSecondary);
            state.style.minWidth = 36;
            state.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(state);
            row.Add(SmallBtn(value ? "TURN OFF" : "TURN ON", () =>
                SetRule(key, value ? "0" : "1"), value ? T.AccentRed : T.AccentGreen));
            return row;
        }

        private static VisualElement RuleStepper(string label, int value, int min, int max, int step, string key)
        {
            var row = Row();
            var name = T.Body(label);
            name.style.flexGrow = 1;
            row.Add(name);

            var valueLabel = T.Body(value.ToString());
            valueLabel.style.minWidth = 44;
            valueLabel.style.unityTextAlign = TextAnchor.MiddleCenter;

            row.Add(StepBtn("−", () =>
            {
                if (value > min) SetRule(key, Mathf.Max(min, value - step).ToString());
            }));
            row.Add(valueLabel);
            row.Add(StepBtn("+", () =>
            {
                if (value < max) SetRule(key, Mathf.Min(max, value + step).ToString());
            }));
            return row;
        }

        private static void SetRule(string key, string value)
            => ServerAdminRegistry.Route(ServerAdminRegistry.OpSetRule, key, value, 0);

        // ───────────────────────── shared chrome ─────────────────────────

        private static Label SectionLabel(string text)
        {
            var label = T.Muted(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.letterSpacing = 1.5f;
            label.style.marginBottom = 4;
            return label;
        }

        private static VisualElement Card(Color? border)
        {
            var card = new VisualElement();
            card.style.backgroundColor = new StyleColor(new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.55f));
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.marginBottom = 6;
            T.Radius(card, T.ButtonRadius);
            if (border != null) T.Border(card, 1, border.Value);
            else T.Border(card, 1, T.BorderDim);
            return card;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3;
            return row;
        }

        private static Button SmallBtn(string text, Action onClick, Color bg)
        {
            var b = new Button(onClick) { text = text };
            b.style.minHeight = 28;
            b.style.minWidth = 78;
            b.style.fontSize = 11;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.letterSpacing = 0.6f;
            b.style.marginLeft = 6;
            b.style.flexShrink = 0;
            b.style.color = Color.white;
            b.style.backgroundColor = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.85f));
            T.Radius(b, T.ButtonRadius);
            T.Border(b, 0, Color.clear);
            LcdHudTheme.AddMenuInteractions(b, bg, new Color(bg.r, bg.g, bg.b, 0.85f));
            return b;
        }

        private static Button StepBtn(string glyph, Action onClick)
        {
            var b = new Button(onClick) { text = glyph };
            b.style.minHeight = 26;
            b.style.minWidth = 30;
            b.style.fontSize = 14;
            b.style.marginLeft = 4;
            b.style.color = Color.white;
            b.style.backgroundColor = new StyleColor(new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.85f));
            T.Radius(b, T.ButtonRadius);
            T.Border(b, 1, T.BorderDim);
            LcdHudTheme.AddMenuInteractions(b, T.AccentCyan,
                new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.85f));
            return b;
        }

        private static TextField ThemedField(string value)
        {
            var field = new TextField { value = value ?? "" };
            field.style.minHeight = 30;
            field.style.fontSize = 13;
            field.style.marginTop = 4;
            var input = field.Q("unity-text-input");
            if (input != null)
            {
                input.style.backgroundColor = new StyleColor(new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.95f));
                input.style.color = new StyleColor(T.TextPrimary);
                T.Radius(input, T.ButtonRadius);
                T.Border(input, 1, T.BorderDim);
                input.style.paddingLeft = 8;
                input.style.paddingRight = 8;
            }
            return field;
        }
    }
}
