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
using System.IO;
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

        // ── banner editor drafts (14.37.0) ────────────────────────────────
        // The whole banner edit lives here as a session draft so a live
        // roster refresh mid-painting cannot eat a half-finished cloth.
        // Nothing reaches the team until SAVE & SHARE sends one intent.
        private static string _bannerDraftTeamId;
        private static Texture2D _bannerDraftCloth;
        private static Color32[] _bannerDraftPixels;
        private static bool _bannerDraftCustom;
        private static string _bannerDraftTop, _bannerDraftMiddle, _bannerDraftBottom;
        private static bool _bannerPainting;
        private static Color32 _brushColor = new Color32(168, 24, 28, 255);
        private static bool _bannerErasing;   // 14.52.0 - eraser paints the blank cloth color
        private static int _brushRadius = 8;
        private static readonly Dictionary<string, Texture2D> _galleryCache = new();

        /// <summary>Scroll offset carried across rebuilds. Every roster tick,
        /// button press and brush stroke rebuilds this page; without this the
        /// view snapped back to the top each time (14.37.1).</summary>
        private static float _savedScroll;

        public static VisualElement Build(Action rebuild)
        {
            var scroll = new ScrollView();
            scroll.style.maxHeight = 560;
            scroll.mode = ScrollViewMode.Vertical;
            var content = new VisualElement();
            scroll.Add(content);

            // Remember where the player was and restore after the fresh layout
            // lands. The offset is captured on a slow poll (never from teardown
            // events, which report a bogus 0) and restored once geometry is
            // real - restoring earlier gets clamped to 0 by an empty scroller.
            float restoreTo = _savedScroll;
            bool restored = false;
            content.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (restored) return;
                restored = true;
                if (restoreTo > 0f)
                    scroll.schedule.Execute(() =>
                        scroll.scrollOffset = new Vector2(0f, restoreTo));
                scroll.schedule.Execute(() =>
                {
                    if (scroll.panel != null) _savedScroll = scroll.scrollOffset.y;
                }).Every(120);
            });

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

                // ── team banner (14.37.0) ─────────────────────────────────
                BuildBannerSection(content, myTeam, amLeader, rebuild);
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

        // ── team banner editor (14.37.0) ──────────────────────────────────

        /// <summary>The banner card under YOUR TEAM. Every member sees the
        /// live banner; the owner and leaders get the full editor - gallery,
        /// painting board, three text lines - and one SAVE & SHARE that sends
        /// the whole draft as a single intent.</summary>
        private static void BuildBannerSection(VisualElement content, TeamData team, bool amLeader, Action rebuild)
        {
            content.Add(SectionLabel("TEAM BANNER"));
            var card = Card(border: T.AccentAmber);
            bool canEdit = amLeader;
            var state = TeamBannerRegistry.Get(team.teamId);

            if (canEdit) EnsureBannerDraft(team.teamId, state);

            bool paintingAllowed = VoxelEngine.Menu.WorldSession.Instance == null
                || VoxelEngine.Menu.WorldSession.Instance.allowBannerPainting;

            // ── preview (and painting board for editors) ─────────────────
            // With the brush active the cloth grows into a proper canvas -
            // painting pixel art on a postage stamp was misery (14.37.1).
            bool bigCanvas = canEdit && paintingAllowed && _bannerPainting;
            int previewW = bigCanvas ? 300 : 176;
            int previewH = bigCanvas ? 450 : 264;
            var previewHolder = new VisualElement();
            previewHolder.style.alignSelf = Align.Center;
            previewHolder.style.width = previewW;
            previewHolder.style.height = previewH;
            previewHolder.style.marginTop = 4;
            previewHolder.style.marginBottom = 6;

            var preview = new Image { scaleMode = ScaleMode.StretchToFill };
            preview.image = canEdit ? _bannerDraftCloth : TeamBannerRegistry.ClothTexture(team.teamId);
            preview.style.width = previewW;
            preview.style.height = previewH;
            T.Border(preview, 2, new Color(0.85f, 0.68f, 0.21f, 0.8f));   // the gold frame, in UI form
            previewHolder.Add(preview);

            // The three text lines render as overlays - the cloth is image
            // only, exactly how the 3D displays draw it.
            previewHolder.Add(BannerOverlayLabel(0.10f, () => canEdit ? _bannerDraftTop : state?.textTop));
            previewHolder.Add(BannerOverlayLabel(0.44f, () => canEdit ? _bannerDraftMiddle : state?.textMiddle));
            previewHolder.Add(BannerOverlayLabel(0.64f, () => canEdit ? _bannerDraftBottom : state?.textBottom));
            card.Add(previewHolder);

            if (!canEdit)
            {
                // Members: live view only. Track edits while the page is open.
                Action<string> onChanged = teamId =>
                {
                    if (!string.IsNullOrEmpty(teamId) && teamId != team.teamId) return;
                    preview.image = TeamBannerRegistry.ClothTexture(team.teamId);
                    preview.MarkDirtyRepaint();
                };
                preview.RegisterCallback<AttachToPanelEvent>(_ => TeamBannerRegistry.OnBannerChanged += onChanged);
                preview.RegisterCallback<DetachFromPanelEvent>(_ => TeamBannerRegistry.OnBannerChanged -= onChanged);
                card.Add(T.Muted("Your team's banner - every placed banner, shield and screen flies it. " +
                                 "The owner and leaders edit it here."));
                content.Add(card);
                content.Add(T.Spacer(10));
                return;
            }

            // ── editor: painting board hookup ─────────────────────────────
            preview.RegisterCallback<PointerDownEvent>(e =>
            {
                if (!_bannerPainting || !paintingAllowed) return;
                preview.CapturePointer(e.pointerId);
                PaintDraftAt(preview, e.localPosition);
                e.StopPropagation();
            });
            preview.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_bannerPainting || !paintingAllowed) return;
                if (!preview.HasPointerCapture(e.pointerId)) return;
                PaintDraftAt(preview, e.localPosition);
            });
            preview.RegisterCallback<PointerUpEvent>(e =>
            {
                if (preview.HasPointerCapture(e.pointerId)) preview.ReleasePointer(e.pointerId);
            });

            // ── text lines ────────────────────────────────────────────────
            card.Add(T.Muted("TEXTS (top / middle / bottom - leave empty for none)"));
            var topField = ThemedField(_bannerDraftTop);
            topField.maxLength = 24;   // 14.47.2 - the server's own cap, visible while typing
            topField.RegisterValueChangedCallback(evt => _bannerDraftTop = evt.newValue);
            card.Add(topField);
            var middleField = ThemedField(_bannerDraftMiddle);
            middleField.maxLength = 24;
            middleField.RegisterValueChangedCallback(evt => _bannerDraftMiddle = evt.newValue);
            card.Add(middleField);
            var bottomField = ThemedField(_bannerDraftBottom);
            bottomField.maxLength = 24;
            bottomField.RegisterValueChangedCallback(evt => _bannerDraftBottom = evt.newValue);
            card.Add(bottomField);

            // ── cloth sources: default emblem + the Banners folder ────────
            card.Add(T.Spacer(6));
            card.Add(T.Muted("CLOTH"));
            var sourceRow = Row();
            sourceRow.Add(SmallBtn("DEFAULT", () =>
            {
                LoadDraftFrom(TeamBannerRegistry.DefaultCloth);
                _bannerDraftCustom = false;
                preview.MarkDirtyRepaint();
            }, T.BgSlot));
            sourceRow.Add(SmallBtn("BLANK CLOTH", () =>
            {
                // Wipe the image entirely - plain cloth, a fresh start for
                // painting from scratch (14.37.1).
                FillDraft(ClothBlank);
                _bannerDraftCustom = true;
                preview.MarkDirtyRepaint();
            }, T.BgSlot));
            sourceRow.Add(SmallBtn("OPEN FOLDER", () =>
            {
                string dir = BannersFolder();
                Application.OpenURL("file:///" + dir.Replace('\\', '/'));
            }, T.BgSlot));
            sourceRow.Add(SmallBtn("RESCAN", () =>
            {
                foreach (var tex in _galleryCache.Values)
                    if (tex != null) UnityEngine.Object.Destroy(tex);
                _galleryCache.Clear();
                rebuild?.Invoke();
            }, T.BgSlot));
            card.Add(sourceRow);
            card.Add(T.Muted("Drop PNG or JPG images into the Banners folder and RESCAN - portrait works best."));

            // ── gallery thumbnails ────────────────────────────────────────
            var galleryRow = new VisualElement();
            galleryRow.style.flexDirection = FlexDirection.Row;
            galleryRow.style.flexWrap = Wrap.Wrap;
            galleryRow.style.marginTop = 4;
            int shown = 0;
            foreach (var path in GalleryFiles())
            {
                if (shown >= 24) break;
                var thumbTex = LoadGalleryTexture(path);
                if (thumbTex == null) continue;
                shown++;
                var thumb = new Image { image = thumbTex, scaleMode = ScaleMode.StretchToFill };
                thumb.style.width = 50;
                thumb.style.height = 75;
                thumb.style.marginRight = 4;
                thumb.style.marginBottom = 4;
                T.Border(thumb, 1, T.BorderDim);
                var captured = thumbTex;
                thumb.RegisterCallback<ClickEvent>(_ =>
                {
                    LoadDraftFrom(captured);
                    _bannerDraftCustom = true;
                    preview.MarkDirtyRepaint();
                });
                galleryRow.Add(thumb);
            }
            if (shown > 0) card.Add(galleryRow);
            else card.Add(T.Muted("No images in the Banners folder yet."));

            // ── painting board controls ───────────────────────────────────
            card.Add(T.Spacer(6));
            if (paintingAllowed)
            {
                card.Add(T.Muted("PAINTING BOARD"));
                var paintRow = Row();
                paintRow.Add(SmallBtn(_bannerPainting ? "PAINTING: ON" : "PAINTING: OFF", () =>
                {
                    _bannerPainting = !_bannerPainting;
                    rebuild?.Invoke();
                }, _bannerPainting ? T.AccentGreen : T.BgSlot));
                card.Add(paintRow);

                if (_bannerPainting)
                {
                    card.Add(T.Muted("Click and drag on the banner above to paint."));
                    var swatchRow = Row();
                    foreach (var swatch in BrushPalette())
                    {
                        var c = swatch;
                        var b = new Button(() => { _brushColor = c; _bannerErasing = false; rebuild?.Invoke(); }) { text = "" };
                        b.style.width = 24; b.style.height = 24; b.style.marginRight = 4;
                        b.style.backgroundColor = new StyleColor((Color)c);
                        T.Radius(b, 4);
                        bool selected = !_bannerErasing && c.r == _brushColor.r && c.g == _brushColor.g
                            && c.b == _brushColor.b && c.a == _brushColor.a;
                        T.Border(b, selected ? 2 : 1, selected ? Color.white : T.BorderDim);
                        swatchRow.Add(b);
                    }
                    card.Add(swatchRow);
                    var sizeRow = Row();
                    sizeRow.Add(T.Muted("BRUSH "));
                    foreach (var (label, radius) in new[] { ("S", 4), ("M", 8), ("L", 16) })
                    {
                        int r = radius;
                        sizeRow.Add(SmallBtn(label, () => { _brushRadius = r; rebuild?.Invoke(); },
                            _brushRadius == r ? T.AccentCyan : T.BgSlot));
                    }
                    // 14.52.0 - the eraser: paints the blank cloth color, so
                    // a slip is undone with the same drag that caused it.
                    sizeRow.Add(SmallBtn("ERASER", () => { _bannerErasing = !_bannerErasing; rebuild?.Invoke(); },
                        _bannerErasing ? T.AccentCyan : T.BgSlot));
                    card.Add(sizeRow);
                }
            }
            else
            {
                card.Add(T.Muted("Banner painting is disabled in this world's settings - " +
                                 "gallery images, texts and the default emblem still work."));
            }

            // ── save ──────────────────────────────────────────────────────
            card.Add(T.Spacer(8));
            card.Add(SmallBtn("SAVE + SHARE WITH TEAM", () =>
            {
                byte[] png = _bannerDraftCustom && _bannerDraftCloth != null
                    ? _bannerDraftCloth.EncodeToPNG() : null;
                TeamBannerRegistry.RequestSet(png, _bannerDraftTop, _bannerDraftMiddle, _bannerDraftBottom);
                rebuild?.Invoke();
            }, T.AccentGreen, LucideIcons.Flag));
            card.Add(T.Muted("Shares the banner with the whole team - every placed banner, " +
                             "shield and grid screen updates at once."));

            content.Add(card);
            content.Add(T.Spacer(10));
        }

        /// <summary>One overlaid banner text line at a relative height of the
        /// preview. Reads through a getter so the label always shows the
        /// value the preview is currently previewing.</summary>
        private static Label BannerOverlayLabel(float top01, Func<string> text)
        {
            var label = new Label(text() ?? "") { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.left = 6; label.style.right = 6;
            label.style.top = Length.Percent(top01 * 100f);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.color = new Color(0.14f, 0.10f, 0.08f);
            label.style.fontSize = top01 > 0.3f && top01 < 0.5f ? 15 : 12;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.overflow = Overflow.Hidden;
            label.schedule.Execute(() => label.text = text() ?? "").Every(250);
            return label;
        }

        /// <summary>Make sure the draft matches MY team: on first open (or
        /// after a team switch) seed it from the live banner state.</summary>
        private static void EnsureBannerDraft(string teamId, TeamBannerState state)
        {
            if (_bannerDraftTeamId == teamId && _bannerDraftCloth != null) return;
            _bannerDraftTeamId = teamId;
            _bannerDraftTop = state?.textTop ?? "";
            _bannerDraftMiddle = state?.textMiddle ?? "";
            _bannerDraftBottom = state?.textBottom ?? "";
            _bannerDraftCustom = state != null && state.hasImage;
            if (_bannerDraftCloth == null)
            {
                _bannerDraftCloth = new Texture2D(TeamBannerRegistry.ClothWidth,
                    TeamBannerRegistry.ClothHeight, TextureFormat.RGBA32, false)
                { name = "BannerDraft", wrapMode = TextureWrapMode.Clamp };
            }
            LoadDraftFrom(TeamBannerRegistry.ClothTexture(teamId));
        }

        /// <summary>Flood the draft cloth with one flat color - the BLANK
        /// CLOTH action, for painting a banner from nothing.</summary>
        /// <summary>The blank cloth color - what the eraser paints with.</summary>
        private static readonly Color32 ClothBlank = new Color32(242, 238, 228, 255);

        private static void FillDraft(Color32 color)
        {
            if (_bannerDraftCloth == null) return;
            int w = _bannerDraftCloth.width, h = _bannerDraftCloth.height;
            if (_bannerDraftPixels == null || _bannerDraftPixels.Length != w * h)
                _bannerDraftPixels = new Color32[w * h];
            for (int i = 0; i < _bannerDraftPixels.Length; i++)
                _bannerDraftPixels[i] = color;
            _bannerDraftCloth.SetPixels32(_bannerDraftPixels);
            _bannerDraftCloth.Apply(false, false);
        }

        /// <summary>Copy any readable texture into the draft cloth, nearest-
        /// neighbor resampled to the canonical 256x384.</summary>
        private static void LoadDraftFrom(Texture2D source)
        {
            if (_bannerDraftCloth == null || source == null) return;
            int w = _bannerDraftCloth.width, h = _bannerDraftCloth.height;
            var src = source.GetPixels32();
            int sw = source.width, sh = source.height;
            _bannerDraftPixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int sy = Mathf.Clamp(y * sh / h, 0, sh - 1);
                for (int x = 0; x < w; x++)
                {
                    int sx = Mathf.Clamp(x * sw / w, 0, sw - 1);
                    var px = src[sy * sw + sx];
                    px.a = 255;   // the cloth is opaque - the swallow-tail is geometry
                    _bannerDraftPixels[y * w + x] = px;
                }
            }
            _bannerDraftCloth.SetPixels32(_bannerDraftPixels);
            _bannerDraftCloth.Apply(false, false);
        }

        /// <summary>Stamp one brush circle where the pointer sits on the
        /// preview image, in cloth pixel space.</summary>
        private static void PaintDraftAt(Image preview, Vector2 local)
        {
            if (_bannerDraftCloth == null || _bannerDraftPixels == null) return;
            float uiW = preview.resolvedStyle.width, uiH = preview.resolvedStyle.height;
            if (uiW <= 1f || uiH <= 1f) return;
            int w = _bannerDraftCloth.width, h = _bannerDraftCloth.height;
            int cx = Mathf.RoundToInt(local.x / uiW * w);
            int cy = Mathf.RoundToInt((1f - local.y / uiH) * h);
            int r = Mathf.Max(1, _brushRadius);
            int r2 = r * r;
            for (int y = Mathf.Max(0, cy - r); y <= Mathf.Min(h - 1, cy + r); y++)
            {
                int dy = y - cy;
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(w - 1, cx + r); x++)
                {
                    int dx = x - cx;
                    if (dx * dx + dy * dy > r2) continue;
                    _bannerDraftPixels[y * w + x] = _bannerErasing ? ClothBlank : _brushColor;
                }
            }
            _bannerDraftCloth.SetPixels32(_bannerDraftPixels);
            _bannerDraftCloth.Apply(false, false);
            _bannerDraftCustom = true;
            preview.MarkDirtyRepaint();
        }

        private static Color32[] BrushPalette() => new Color32[]
        {
            new(168, 24, 28, 255),    // crusader red
            new(242, 238, 228, 255),  // cloth white
            new(24, 24, 28, 255),     // black
            new(217, 174, 54, 255),   // gold
            new(32, 72, 148, 255),    // royal blue
            new(28, 110, 52, 255),    // forest green
            new(94, 58, 26, 255),     // oak brown
            new(118, 32, 120, 255),   // imperial purple
        };

        private static string BannersFolder()
        {
            string dir = Path.Combine(Application.persistentDataPath, "Banners");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        private static List<string> GalleryFiles()
        {
            var files = new List<string>();
            try
            {
                string dir = BannersFolder();
                foreach (var pattern in new[] { "*.png", "*.jpg", "*.jpeg" })
                    files.AddRange(Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly));
                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return files;
        }

        /// <summary>Load one gallery file, resampled to cloth size and cached
        /// for the session (RESCAN clears the cache).</summary>
        private static Texture2D LoadGalleryTexture(string path)
        {
            if (_galleryCache.TryGetValue(path, out var cached) && cached != null) return cached;
            try
            {
                var bytes = File.ReadAllBytes(path);
                var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!raw.LoadImage(bytes)) { UnityEngine.Object.Destroy(raw); return null; }
                var tex = new Texture2D(TeamBannerRegistry.ClothWidth, TeamBannerRegistry.ClothHeight,
                    TextureFormat.RGBA32, false)
                { name = "BannerGallery_" + Path.GetFileName(path), wrapMode = TextureWrapMode.Clamp };
                int w = tex.width, h = tex.height, sw = raw.width, sh = raw.height;
                var src = raw.GetPixels32();
                var dst = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    int sy = Mathf.Clamp(y * sh / h, 0, sh - 1);
                    for (int x = 0; x < w; x++)
                    {
                        int sx = Mathf.Clamp(x * sw / w, 0, sw - 1);
                        var px = src[sy * sw + sx];
                        px.a = 255;
                        dst[y * w + x] = px;
                    }
                }
                tex.SetPixels32(dst);
                tex.Apply(false, false);
                UnityEngine.Object.Destroy(raw);
                _galleryCache[path] = tex;
                return tex;
            }
            catch { return null; }
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
