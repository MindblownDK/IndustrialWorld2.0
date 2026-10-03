// Assets/Scripts/VoxelEngine/UI/TeamsPanel.cs
//
// 14.33.0-dev - Multiplayer milestone 11, the panel half of teams.
//
// Everything a player can DO with teams happens here: found one, invite a
// teammate, answer an invitation, leave, remove somebody, and - as the host -
// tune the session's limits. Every button only ASKS (TeamRegistry.Request*):
// the host's registry decides, and the answer comes back as the refreshed
// roster this panel is already drawn from. Nothing is applied optimistically,
// so two players racing for the last team slot can never disagree.
//
// The pause menu supplies the panel, the header and the live refresh (it
// rebuilds whenever TeamRegistry.Version moves); this file supplies content.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Networking;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    /// <summary>Content builder for the pause menu's TEAMS page. Stateless
    /// between rebuilds except the remembered draft team name, which must
    /// survive a live refresh while it is being typed.</summary>
    public static class TeamsPanel
    {
        /// <summary>Draft team name, kept across rebuilds so a live roster
        /// refresh cannot eat what the player is typing.</summary>
        private static string _draftName = "";

        /// <summary>Draft rename, kept across rebuilds for the same reason.
        /// Null means "mirror the current team name on next build".</summary>
        private static string _draftRename;

        public static VisualElement Build(Action rebuild)
        {
            var scroll = new ScrollView();
            scroll.style.maxHeight = 560;
            scroll.mode = ScrollViewMode.Vertical;
            var content = new VisualElement();
            scroll.Add(content);

            string me = NetworkSession.LocalPlayerId;
            var myTeam = TeamRegistry.TeamOf(me);
            bool amOwner = TeamRegistry.IsTeamOwner(myTeam, me);
            bool amLeader = TeamRegistry.IsTeamLeader(myTeam, me);   // owner or co-leader

            // ── invitations for me ────────────────────────────────────────
            var invites = TeamRegistry.InvitesFor(me);
            if (invites.Count > 0)
            {
                content.Add(SectionLabel("INVITATIONS"));
                foreach (var invite in invites)
                {
                    var team = TeamRegistry.TeamById(invite.teamId);
                    if (team == null) continue;
                    var leader = PlayerName(team.leaderId);
                    var card = Card(border: T.AccentAmber);

                    // Name on its own wrapping line - the answer buttons live
                    // on their own row below, so a long team name can never
                    // push them off the card (14.34.0).
                    var line = T.Body($"{team.name} - invited by {leader}");
                    line.style.whiteSpace = WhiteSpace.Normal;
                    card.Add(line);

                    var row = Row();
                    row.style.justifyContent = Justify.FlexEnd;
                    row.style.marginTop = 6;
                    row.Add(SmallBtn("ACCEPT", () =>
                    {
                        TeamRegistry.RequestAccept(team.teamId);
                        rebuild?.Invoke();
                    }, T.AccentGreen));
                    row.Add(SmallBtn("DECLINE", () =>
                    {
                        TeamRegistry.RequestDecline(team.teamId);
                        rebuild?.Invoke();
                    }, T.AccentRed));
                    card.Add(row);
                    card.Add(T.Muted($"Answer within {TeamRegistry.InviteLifetimeSeconds:0} seconds, or it lapses."));
                    content.Add(card);
                }
                content.Add(T.Spacer(10));
            }

            // ── my team ───────────────────────────────────────────────────
            if (myTeam != null)
            {
                var yourLabel = SectionLabel($"YOUR TEAM - {myTeam.name.ToUpper()}");
                yourLabel.style.whiteSpace = WhiteSpace.Normal;
                content.Add(yourLabel);
                var card = Card(border: T.AccentCyan);

                foreach (var memberId in myTeam.memberIds)
                {
                    if (string.IsNullOrEmpty(memberId)) continue;
                    var memberRow = Row();
                    bool online = NetworkSession.GetPlayer(memberId) != null;
                    bool isMe = memberId == me;
                    bool targetIsOwner = TeamRegistry.IsTeamOwner(myTeam, memberId);
                    bool targetIsLeader = !targetIsOwner && TeamRegistry.IsTeamLeader(myTeam, memberId);

                    var dot = new Label(online ? "●" : "○");
                    dot.style.color = new StyleColor(online ? T.AccentGreen : T.TextSecondary);
                    dot.style.marginRight = 8;
                    dot.style.fontSize = 11;
                    dot.style.flexShrink = 0;
                    memberRow.Add(dot);

                    // Rank glyph: crown for the owner, shield for a leader.
                    if (targetIsOwner || targetIsLeader)
                    {
                        var rank = LucideIcons.Make(targetIsOwner ? LucideIcons.Crown : LucideIcons.Shield,
                            12, targetIsOwner ? T.AccentAmber : T.AccentCyan);
                        rank.style.marginRight = 6;
                        rank.style.flexShrink = 0;
                        memberRow.Add(rank);
                    }

                    string rankTag = targetIsOwner ? "   (owner)" : targetIsLeader ? "   (leader)" : "";
                    var name = T.Body(PlayerName(memberId) + (isMe ? "   (you)" : "") + rankTag);
                    name.style.flexGrow = 1;
                    name.style.flexShrink = 1;
                    name.style.overflow = Overflow.Hidden;
                    name.style.textOverflow = TextOverflow.Ellipsis;
                    memberRow.Add(name);

                    // Owner appoints and strips leaders (14.34.0).
                    if (amOwner && !isMe)
                    {
                        string captured = memberId;
                        memberRow.Add(SmallBtn(targetIsLeader ? "DEMOTE" : "PROMOTE", () =>
                        {
                            if (targetIsLeader) TeamRegistry.RequestDemote(captured);
                            else TeamRegistry.RequestPromote(captured);
                            rebuild?.Invoke();
                        }, targetIsLeader ? T.BgSlot : T.AccentCyan));
                    }

                    // Removal: the owner removes anyone; a leader removes
                    // plain members only. Nobody removes the owner.
                    bool canRemove = !isMe && !targetIsOwner && (amOwner || (amLeader && !targetIsLeader));
                    if (canRemove)
                    {
                        string captured = memberId;
                        memberRow.Add(SmallBtn("REMOVE", () =>
                        {
                            TeamRegistry.RequestKick(captured);
                            rebuild?.Invoke();
                        }, T.AccentRed));
                    }
                    card.Add(memberRow);
                }

                // ── rename (owner only, 14.34.0) ──────────────────────────
                if (amOwner)
                {
                    card.Add(T.Spacer(8));
                    card.Add(T.Muted("TEAM NAME"));
                    if (_draftRename == null) _draftRename = myTeam.name;
                    var renameField = ThemedField(_draftRename);
                    renameField.RegisterValueChangedCallback(evt => _draftRename = evt.newValue);
                    card.Add(renameField);
                    var renameRow = Row();
                    renameRow.style.justifyContent = Justify.FlexEnd;
                    renameRow.Add(SmallBtn("RENAME", () =>
                    {
                        TeamRegistry.RequestRename(_draftRename ?? "");
                        rebuild?.Invoke();
                    }, T.AccentCyan));
                    card.Add(renameRow);
                }

                if (amLeader)
                {
                    card.Add(T.Spacer(6));
                    card.Add(T.Muted("INVITE A PLAYER"));
                    int candidates = 0;
                    foreach (var presence in NetworkSession.Players)
                    {
                        var id = presence.playerId;
                        if (id == me) continue;
                        if (TeamRegistry.TeamOf(id) != null) continue;
                        if (TeamRegistry.HasInvite(myTeam.teamId, id)) continue;
                        candidates++;
                        var inviteRow = Row();
                        inviteRow.style.marginTop = 3;
                        var label = T.Body(presence.displayName ?? id);
                        label.style.flexGrow = 1;
                        inviteRow.Add(label);
                        string captured = id;
                        inviteRow.Add(SmallBtn("INVITE", () =>
                        {
                            TeamRegistry.RequestInvite(myTeam.teamId, captured);
                            rebuild?.Invoke();
                        }, T.AccentCyan));
                        card.Add(inviteRow);
                    }
                    if (candidates == 0)
                        card.Add(T.Muted("Nobody to invite - players must be online and teamless."));
                }

                card.Add(T.Spacer(8));
                card.Add(SmallBtn(amOwner ? "LEAVE TEAM (OWNERSHIP PASSES ON)" : "LEAVE TEAM", () =>
                {
                    TeamRegistry.RequestLeave();
                    rebuild?.Invoke();
                }, T.BgSlot));
                if (amOwner && myTeam.memberIds.Count == 1)
                    card.Add(T.Muted("You are the last member - leaving disbands the team."));

                content.Add(card);
                content.Add(T.Spacer(10));
            }
            else
            {
                // ── found a team ──────────────────────────────────────────
                _draftRename = null;   // no team, no rename draft to keep
                content.Add(SectionLabel("FOUND A TEAM"));
                var card = Card(border: T.AccentCyan);
                card.Add(T.Muted($"TEAM NAME  ({TeamRegistry.Teams.Count}/{TeamRegistry.MaxTeams} teams exist)"));
                // Stored on every keystroke: a live roster refresh rebuilds
                // this page, and the draft must come back with it.
                var nameField = ThemedField(_draftName);
                nameField.RegisterValueChangedCallback(evt => _draftName = evt.newValue);
                card.Add(nameField);
                card.Add(T.Spacer(6));
                card.Add(SmallBtn("FOUND TEAM", () =>
                {
                    TeamRegistry.RequestCreate(_draftName);
                    rebuild?.Invoke();
                }, T.AccentGreen, LucideIcons.Flag));
                card.Add(T.Muted("You become the owner. Invite players from this page; team names are unique."));
                content.Add(card);
                content.Add(T.Spacer(10));
            }

            // ── every team in the session ─────────────────────────────────
            content.Add(SectionLabel("TEAMS IN THIS SESSION"));
            if (TeamRegistry.Teams.Count == 0)
            {
                content.Add(T.Muted("None yet. Teams persist with the world - found one and it is here next session."));
            }
            else
            {
                foreach (var team in TeamRegistry.Teams)
                {
                    if (team == null) continue;
                    var card = Card(border: null);
                    var head = Row();
                    var title = T.Body(team.name);
                    title.style.unityFontStyleAndWeight = FontStyle.Bold;
                    title.style.flexGrow = 1;
                    title.style.flexShrink = 1;
                    title.style.whiteSpace = WhiteSpace.Normal;
                    head.Add(title);
                    head.Add(T.Muted($"{team.memberIds.Count} member{(team.memberIds.Count == 1 ? "" : "s")}"));
                    card.Add(head);
                    var members = T.Muted(string.Join(", ", MemberNames(team)));
                    members.style.whiteSpace = WhiteSpace.Normal;
                    card.Add(members);
                    content.Add(card);
                }
            }
            content.Add(T.Spacer(10));

            // ── host-only session limits ──────────────────────────────────
            if (NetworkSession.IsAuthority)
            {
                content.Add(SectionLabel("SESSION LIMITS (HOST)"));
                var card = Card(border: null);
                card.Add(T.Muted("Apply to new teams and invites only - existing teams are never broken up."));
                card.Add(T.Spacer(4));
                card.Add(LimitRow("MAX TEAMS", TeamRegistry.MaxTeams, 1, 8,
                    v => TeamRegistry.RequestLimits(v, TeamRegistry.MaxMembers)));
                card.Add(LimitRow("MAX MEMBERS PER TEAM", TeamRegistry.MaxMembers, 2, 8,
                    v => TeamRegistry.RequestLimits(TeamRegistry.MaxTeams, v)));
                content.Add(card);
            }
            else
            {
                content.Add(T.Muted($"Limits set by the host: max {TeamRegistry.MaxMembers} members per team, " +
                                    $"{TeamRegistry.MaxTeams} teams per session."));
            }

            return scroll;
        }

        // ── small builders ────────────────────────────────────────────────

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

        private static Button SmallBtn(string text, Action onClick, Color bg, string lucideIcon = null)
        {
            var b = new Button(onClick) { text = lucideIcon == null ? text : string.Empty };
            b.style.minHeight = 28;
            b.style.minWidth = 78;
            b.style.fontSize = 11;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.letterSpacing = 0.6f;
            b.style.marginLeft = 6;
            b.style.flexShrink = 0;   // long names wrap; buttons never crush
            b.style.color = Color.white;
            b.style.backgroundColor = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.85f));
            T.Radius(b, T.ButtonRadius);
            T.Border(b, 0, Color.clear);

            if (lucideIcon != null)
            {
                b.style.flexDirection = FlexDirection.Row;
                b.style.alignItems = Align.Center;
                b.style.justifyContent = Justify.Center;
                var ic = LucideIcons.Make(lucideIcon, 12, Color.white);
                ic.style.marginRight = 7;
                b.Add(ic);
                var lbl = new Label(text) { pickingMode = PickingMode.Ignore };
                lbl.style.color = Color.white;
                lbl.style.fontSize = 11;
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                lbl.style.letterSpacing = 0.6f;
                lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                b.Add(lbl);
            }

            LcdHudTheme.AddMenuInteractions(b, bg, new Color(bg.r, bg.g, bg.b, 0.85f));
            return b;
        }

        /// <summary>The shared dark themed text field this panel uses for
        /// the found-team and rename drafts.</summary>
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

        /// <summary>A - value + stepper row for one host limit. Applies at
        /// once on every click; the roster broadcast carries the new value
        /// to the guests' read-only line.</summary>
        private static VisualElement LimitRow(string label, int value, int min, int max, Action<int> apply)
        {
            var row = Row();
            var name = T.Body(label);
            name.style.flexGrow = 1;
            row.Add(name);

            var valueLabel = T.Body(value.ToString());
            valueLabel.style.minWidth = 30;
            valueLabel.style.unityTextAlign = TextAnchor.MiddleCenter;

            row.Add(StepBtn("−", () =>
            {
                if (value > min) apply(value - 1);
            }));
            row.Add(valueLabel);
            row.Add(StepBtn("+", () =>
            {
                if (value < max) apply(value + 1);
            }));
            return row;
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

        private static string PlayerName(string playerId)
        {
            var presence = NetworkSession.GetPlayer(playerId);
            if (presence != null && !string.IsNullOrEmpty(presence.displayName))
                return presence.displayName;
            return string.IsNullOrEmpty(playerId) ? "?" : playerId;
        }

        /// <summary>Member names for the session overview; offline members
        /// are marked so a stale roster never masquerades as a full house.</summary>
        private static List<string> MemberNames(TeamData team)
        {
            var names = new List<string>();
            foreach (var id in team.memberIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                string name = PlayerName(id);
                if (id == team.leaderId) name += " (owner)";
                else if (TeamRegistry.IsTeamLeader(team, id)) name += " (leader)";
                if (NetworkSession.GetPlayer(id) == null) name += " (offline)";
                names.Add(name);
            }
            return names;
        }
    }
}
