// Assets/Scripts/VoxelEngine/Networking/TeamRegistry.cs
//
// 14.33.0-dev - Multiplayer milestone 11: teams.
//
// The one source of truth for "which players stand together". A team is a
// named roster keyed by STABLE PLAYER ID (never a connection, never a name),
// so membership survives a rename, a reconnect and a session restart - the
// roster is part of the host's world, persisted as teams.json beside the save
// exactly like the per-player records are.
//
// Authority follows the locked rule: the HOST owns the roster. Clients send
// intents (create / invite / accept / decline / leave / kick) through the
// bootstrap; the server stamps the requester from its connection table, so a
// client can never act in another player's name. Every accepted intent
// rebroadcasts the whole roster - at 2-8 players the entire truth is smaller
// than a delta scheme and a late joiner needs exactly one message.
//
// Everything downstream asks one question: SameTeam(a, b). Beacon sharing
// (the TEAM share rule stored since 14.30.0) is the first consumer; shared
// build costs, friendly fire and base permissions hang off the same answer
// later.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VoxelEngine.Networking
{
    // ── data model (JsonUtility-friendly: plain fields, no dictionaries) ──

    [Serializable]
    public sealed class TeamData
    {
        public string teamId = "";
        public string name = "";
        /// <summary>The team's OWNER (kept as `leaderId` for wire/save
        /// compatibility): the founder, or whoever ownership passed to.
        /// Renames the team, appoints and strips co-leaders.</summary>
        public string leaderId = "";
        /// <summary>Member player ids in join order; the owner is a member
        /// too. Join order is what ownership passes to when an owner goes.</summary>
        public List<string> memberIds = new();
        /// <summary>Appointed co-leaders (14.34.0). May invite and remove
        /// plain members; only the owner touches this list.</summary>
        public List<string> coLeaderIds = new();
    }

    [Serializable]
    public sealed class TeamInviteData
    {
        public string teamId = "";
        public string invitedId = "";
        /// <summary>Unix time (UTC seconds) after which the invite is void.
        /// Kept as double for sub-second honesty, compared with UtcNow.</summary>
        public double expiresAtUtc;
    }

    /// <summary>The whole team state as one serializable object: the wire
    /// payload, the save file and the live registry are the same shape.</summary>
    [Serializable]
    public sealed class TeamSnapshot
    {
        public List<TeamData> teams = new();
        public List<TeamInviteData> invites = new();
        /// <summary>Host-editable session limits (roadmap: they live in
        /// settings, not in code). Persisted with the world, honoured on new
        /// intents only - shrinking a limit never breaks up an existing team.</summary>
        public int maxTeams = DefaultMaxTeams;
        public int maxMembers = DefaultMaxMembers;

        public const int DefaultMaxTeams = 4;
        public const int DefaultMaxMembers = 8;
    }

    // ── the registry ──────────────────────────────────────────────────────

    /// <summary>Live team state for THIS machine. On the host (or offline)
    /// it is the authority; on a client it is a mirror of the host's last
    /// broadcast. Same API either way - gameplay code never branches.</summary>
    public static class TeamRegistry
    {
        /// <summary>An invite is answerable for this long. Long enough to
        /// notice it, short enough that a stale one cannot surprise anyone.</summary>
        public const double InviteLifetimeSeconds = 90.0;

        private static TeamSnapshot _state = new();
        private static string _loadedForWorld = "";

        /// <summary>Bumped on every state change, so UI that rebuilds on a
        /// timer can cheaply detect "something happened".</summary>
        public static int Version { get; private set; }

        public static IReadOnlyList<TeamData> Teams => _state.teams;
        public static int MaxTeams => _state.maxTeams;
        public static int MaxMembers => _state.maxMembers;

        // ── queries ───────────────────────────────────────────────────────

        /// <summary>THE question every team feature asks. Two players stand
        /// together when both are in the same team; being in no team, or in
        /// different teams, is the same answer for every consumer.</summary>
        public static bool SameTeam(string playerIdA, string playerIdB)
        {
            if (string.IsNullOrEmpty(playerIdA) || string.IsNullOrEmpty(playerIdB)) return false;
            if (playerIdA == playerIdB) return false;   // yourself is not "a teammate"
            var team = TeamOf(playerIdA);
            return team != null && team.memberIds.Contains(playerIdB);
        }

        /// <summary>The player's team, or null. A player is in at most one
        /// team - enforced on every join.</summary>
        public static TeamData TeamOf(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            for (int i = 0; i < _state.teams.Count; i++)
                if (_state.teams[i].memberIds.Contains(playerId))
                    return _state.teams[i];
            return null;
        }

        public static TeamData TeamById(string teamId)
            => string.IsNullOrEmpty(teamId) ? null
               : _state.teams.Find(t => t != null && t.teamId == teamId);

        /// <summary>The player founded this team or inherited it.</summary>
        public static bool IsTeamOwner(TeamData team, string playerId)
            => team != null && !string.IsNullOrEmpty(playerId) && team.leaderId == playerId;

        /// <summary>Owner or appointed co-leader: may invite and remove
        /// plain members. The one permission question team features ask.</summary>
        public static bool IsTeamLeader(TeamData team, string playerId)
            => team != null && !string.IsNullOrEmpty(playerId)
               && (team.leaderId == playerId
                   || (team.coLeaderIds != null && team.coLeaderIds.Contains(playerId)));

        /// <summary>Case-insensitive name collision check. Two teams with the
        /// same banner-name would be indistinguishable everywhere the name is
        /// the identity a player reads, so the host refuses the second one.</summary>
        private static bool NameTaken(string name, string exceptTeamId)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < _state.teams.Count; i++)
            {
                var team = _state.teams[i];
                if (team == null || team.teamId == exceptTeamId) continue;
                if (string.Equals(team.name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Answerable invites for this player. 14.46.1: the expiry
        /// stamp is written by the HOST's wall clock, so only the authority
        /// compares it against its own clock. A client whose clock ran a
        /// minute ahead of the server used to see every invite as already
        /// dead and silently hid it - now clients trust the roster as sent,
        /// and the host's periodic prune (NetworkBootstrap) retires expired
        /// invites for everyone within seconds.</summary>
        public static List<TeamInviteData> InvitesFor(string playerId)
        {
            var live = new List<TeamInviteData>();
            if (string.IsNullOrEmpty(playerId)) return live;
            bool authority = NetworkSession.IsAuthority;
            double now = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            for (int i = 0; i < _state.invites.Count; i++)
            {
                var inv = _state.invites[i];
                if (inv != null && inv.invitedId == playerId && (!authority || inv.expiresAtUtc > now))
                    live.Add(inv);
            }
            return live;
        }

        /// <summary>A unexpired invite from this team to this player.</summary>
        public static bool HasInvite(string teamId, string playerId)
            => InvitesFor(playerId).Exists(i => i.teamId == teamId);

        // ── intents: what a player WANTS (never an outcome) ───────────────
        //
        // Offline and Host modes apply locally - this machine is the server.
        // Client mode forwards to the host, which applies and rebroadcasts.

        public static void RequestCreate(string name)
            => Route(TeamOp.Create, "", name, "");

        public static void RequestInvite(string teamId, string targetPlayerId)
            => Route(TeamOp.Invite, teamId, "", targetPlayerId);

        public static void RequestAccept(string teamId)
            => Route(TeamOp.Accept, teamId, "", "");

        public static void RequestDecline(string teamId)
            => Route(TeamOp.Decline, teamId, "", "");

        public static void RequestLeave()
            => Route(TeamOp.Leave, "", "", "");

        public static void RequestKick(string targetPlayerId)
            => Route(TeamOp.Kick, "", "", targetPlayerId);

        public static void RequestPromote(string targetPlayerId)
            => Route(TeamOp.Promote, "", "", targetPlayerId);

        public static void RequestDemote(string targetPlayerId)
            => Route(TeamOp.Demote, "", "", targetPlayerId);

        public static void RequestRename(string newName)
            => Route(TeamOp.Rename, "", newName, "");

        /// <summary>Host-only: adjust the session limits. Applies at once,
        /// rebroadcasts so guests' panels show honest numbers, and rides the
        /// next save. Guests never see the controls.</summary>
        public static void RequestLimits(int maxTeams, int maxMembers)
        {
            if (NetworkSession.Mode == SessionMode.Client) return;   // settings are the host's
            _state.maxTeams = Mathf.Clamp(maxTeams, 1, 8);
            _state.maxMembers = Mathf.Clamp(maxMembers, 2, 8);
            Version++;
            Save();
            NetworkBootstrap.Instance?.BroadcastTeamRoster("");
        }

        private static void Route(byte op, string teamId, string name, string targetId)
        {
            if (NetworkSession.Mode == SessionMode.Client)
            {
                NetworkBootstrap.Instance?.SendTeamIntent(new TeamIntentBroadcast
                {
                    Op = op, TeamId = teamId ?? "", Name = name ?? "", TargetId = targetId ?? ""
                });
                return;
            }
            // This machine is the server: apply, persist, and tell everyone.
            // The diff in Commit() surfaces the same local notices a client
            // derives from the roster broadcast, so the host never reads its
            // own good news from a different channel than a guest would.
            string error = HostApply(op, NetworkSession.LocalPlayerId, teamId, name, targetId);
            if (!string.IsNullOrEmpty(error))
                VoxelEngine.UI.BuildFeedbackHud.Show("Teams", error, null, new Color(0.82f, 0.22f, 0.18f));
            else
                NetworkBootstrap.Instance?.BroadcastTeamRoster("");
        }

        // ── host-side intent application ──────────────────────────────────

        /// <summary>Applies one intent as the authority. Returns an error
        /// line when the intent is refused (shown to the requester), or
        /// null when it was applied - in which case the state changed, the
        /// file wants saving and everyone wants the new roster. Safe to call
        /// from the server handler for a remote requester: the diff notices
        /// that come out of it are filtered to the LOCAL player.</summary>
        public static string HostApply(byte op, string requesterId, string teamId, string name, string targetId)
        {
            if (string.IsNullOrEmpty(requesterId)) return "Not connected.";
            PruneExpired();
            var before = CloneState();

            switch (op)
            {
                case TeamOp.Create:
                {
                    if (TeamOf(requesterId) != null) return "Leave your current team before founding another.";
                    string trimmed = (name ?? "").Trim();
                    if (trimmed.Length < 2) return "A team name needs at least 2 characters.";
                    if (trimmed.Length > 24) trimmed = trimmed.Substring(0, 24);
                    if (NameTaken(trimmed, "")) return "A team with that name already exists.";
                    if (_state.teams.Count >= _state.maxTeams)
                        return $"The session's team limit is reached ({_state.maxTeams}).";
                    _state.teams.Add(new TeamData
                    {
                        teamId = "T" + Guid.NewGuid().ToString("N"),
                        name = trimmed,
                        leaderId = requesterId,
                        memberIds = { requesterId }
                    });
                    Commit(before);
                    return null;
                }

                case TeamOp.Invite:
                {
                    var team = TeamOf(requesterId);
                    if (team == null) return "You are not in a team.";
                    if (!IsTeamLeader(team, requesterId)) return "Only the team's owner or a leader can invite.";
                    if (string.IsNullOrEmpty(targetId) || targetId == requesterId)
                        return "Pick a player to invite.";
                    if (NetworkSession.GetPlayer(targetId) == null)
                        return "That player is not in the session right now.";
                    if (TeamOf(targetId) != null) return "That player is already in a team.";
                    if (team.memberIds.Count >= _state.maxMembers)
                        return $"The team is full ({_state.maxMembers}).";
                    if (HasInvite(team.teamId, targetId)) return "That player already has an invite.";
                    _state.invites.Add(new TeamInviteData
                    {
                        teamId = team.teamId,
                        invitedId = targetId,
                        expiresAtUtc = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
                                       + InviteLifetimeSeconds
                    });
                    Commit(before);
                    return null;
                }

                case TeamOp.Accept:
                {
                    if (TeamOf(requesterId) != null) return "Leave your current team before joining another.";
                    var team = TeamById(teamId);
                    if (team == null) return "That team no longer exists.";
                    if (!HasInvite(team.teamId, requesterId)) return "That invite is no longer answerable.";
                    if (team.memberIds.Count >= _state.maxMembers)
                        return $"The team is full ({_state.maxMembers}).";
                    _state.invites.RemoveAll(i => i != null && i.invitedId == requesterId);
                    team.memberIds.Add(requesterId);
                    Commit(before);
                    return null;
                }

                case TeamOp.Decline:
                {
                    _state.invites.RemoveAll(
                        i => i != null && i.invitedId == requesterId
                            && (!string.IsNullOrEmpty(teamId) && i.teamId == teamId));
                    Commit(before);
                    return null;
                }

                case TeamOp.Leave:
                {
                    var team = TeamOf(requesterId);
                    if (team == null) return "You are not in a team.";
                    LeaveTeam(team, requesterId);
                    Commit(before);
                    return null;
                }

                case TeamOp.Kick:
                {
                    var team = TeamOf(requesterId);
                    if (team == null) return "You are not in a team.";
                    if (!IsTeamLeader(team, requesterId)) return "Only the team's owner or a leader can remove members.";
                    if (string.IsNullOrEmpty(targetId) || targetId == requesterId)
                        return "Leave the team instead of removing yourself.";
                    if (!team.memberIds.Contains(targetId)) return "That player is not in your team.";
                    if (team.leaderId == targetId) return "The team's owner cannot be removed.";
                    if (!IsTeamOwner(team, requesterId) && IsTeamLeader(team, targetId))
                        return "Only the owner can remove another leader.";
                    LeaveTeam(team, targetId);
                    Commit(before);
                    return null;
                }

                case TeamOp.Promote:
                {
                    var team = TeamOf(requesterId);
                    if (team == null) return "You are not in a team.";
                    if (!IsTeamOwner(team, requesterId)) return "Only the team's owner appoints leaders.";
                    if (string.IsNullOrEmpty(targetId) || targetId == requesterId)
                        return "Pick a member to appoint.";
                    if (!team.memberIds.Contains(targetId)) return "That player is not in your team.";
                    team.coLeaderIds ??= new List<string>();
                    if (team.coLeaderIds.Contains(targetId)) return "That player is already a leader.";
                    team.coLeaderIds.Add(targetId);
                    Commit(before);
                    return null;
                }

                case TeamOp.Demote:
                {
                    var team = TeamOf(requesterId);
                    if (team == null) return "You are not in a team.";
                    if (!IsTeamOwner(team, requesterId)) return "Only the team's owner strips leadership.";
                    if (team.coLeaderIds == null || !team.coLeaderIds.Remove(targetId))
                        return "That player is not a leader.";
                    Commit(before);
                    return null;
                }

                case TeamOp.Rename:
                {
                    var team = TeamOf(requesterId);
                    if (team == null) return "You are not in a team.";
                    if (!IsTeamOwner(team, requesterId)) return "Only the team's owner renames the team.";
                    string renamed = (name ?? "").Trim();
                    if (renamed.Length < 2) return "A team name needs at least 2 characters.";
                    if (renamed.Length > 24) renamed = renamed.Substring(0, 24);
                    if (string.Equals(team.name, renamed, StringComparison.Ordinal)) return "That is already the team's name.";
                    if (NameTaken(renamed, team.teamId)) return "A team with that name already exists.";
                    team.name = renamed;
                    Commit(before);
                    return null;
                }
            }
            return "Unknown request.";
        }

        /// <summary>Removes a member; when the owner goes, ownership passes
        /// to the earliest-joined co-leader (an appointed leader outranks
        /// seniority), else the earliest-joined remaining member; a team with
        /// nobody left dissolves (and its invites die with it).</summary>
        private static void LeaveTeam(TeamData team, string playerId)
        {
            team.memberIds.Remove(playerId);
            team.coLeaderIds?.Remove(playerId);
            _state.invites.RemoveAll(i => i != null && i.invitedId == playerId);
            if (team.memberIds.Count == 0)
            {
                _state.teams.Remove(team);
                _state.invites.RemoveAll(i => i != null && i.teamId == team.teamId);
                return;
            }
            if (team.leaderId == playerId)
            {
                string heir = null;
                if (team.coLeaderIds != null)
                    foreach (var id in team.memberIds)
                        if (team.coLeaderIds.Contains(id)) { heir = id; break; }
                team.leaderId = heir ?? team.memberIds[0];
                team.coLeaderIds?.Remove(team.leaderId);   // the owner needs no second hat
            }
        }

        private static void PruneExpired()
        {
            double now = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            _state.invites.RemoveAll(i => i == null || i.expiresAtUtc <= now);
        }

        /// <summary>14.46.1 - host-side timer prune. Clients no longer judge
        /// expiry with their own clocks, so the host must retire dead invites
        /// even when nothing else changes. Returns true when something was
        /// removed, so the caller knows to rebroadcast the roster.</summary>
        public static bool PruneExpiredTick()
        {
            int before = _state.invites.Count;
            PruneExpired();
            return _state.invites.Count != before;
        }

        /// <summary>State changed: bump the version, save the file, and let
        /// the local player hear what happened to THEM - the same diff every
        /// client runs on an incoming roster.</summary>
        private static void Commit(TeamSnapshot before)
        {
            Version++;
            Save();
            DiffForLocalPlayer(before, _state);
        }

        /// <summary>Deep copy via the same serializer the wire and the save
        /// file use - a few hundred bytes, never a drift risk.</summary>
        private static TeamSnapshot CloneState()
            => JsonUtility.FromJson<TeamSnapshot>(JsonUtility.ToJson(_state));

        // ── the wire mirror ───────────────────────────────────────────────

        /// <summary>Client: replace the mirror with the host's roster. Local
        /// notices (an invite arrived, you joined, a teammate changed) are
        /// derived by diffing - the wire carries no per-player text.</summary>
        public static void ApplySnapshot(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var incoming = JsonUtility.FromJson<TeamSnapshot>(json);
                if (incoming == null) return;
                var previous = _state;
                _state = incoming;
                Version++;
                // 14.46.2: confirms the roster actually crossed the wire -
                // if this line never prints, the broadcast path is the fault.
                Debug.Log($"[Teams] roster applied: {incoming.teams?.Count ?? 0} team(s), " +
                          $"{incoming.invites?.Count ?? 0} invite(s).");
                DiffForLocalPlayer(previous, incoming);
            }
            catch (Exception ex) { Debug.LogWarning("[TeamRegistry] ApplySnapshot: " + ex.Message); }
        }

        /// <summary>Both host and client run the same diff against the local
        /// player, so a notice appears identically no matter which machine
        /// changed the truth.</summary>
        private static void DiffForLocalPlayer(TeamSnapshot oldState, TeamSnapshot newState)
        {
            string me = NetworkSession.LocalPlayerId;
            if (string.IsNullOrEmpty(me)) return;

            var oldTeam = TeamIn(oldState, me);
            var newTeam = TeamIn(newState, me);

            // Joined / left.
            if (oldTeam == null && newTeam != null)
                Toast($"You joined {newTeam.name}.", good: true);
            else if (oldTeam != null && newTeam == null)
                Toast($"You are no longer in {oldTeam.name}.", good: false);

            // Same team, new banner-name or a changed hat.
            if (newTeam != null && oldTeam != null && oldTeam.teamId == newTeam.teamId)
            {
                if (!string.Equals(oldTeam.name, newTeam.name, StringComparison.Ordinal))
                    Toast($"Your team is now named {newTeam.name}.", good: true);

                bool wasLeader = oldTeam.leaderId == me
                                 || (oldTeam.coLeaderIds != null && oldTeam.coLeaderIds.Contains(me));
                bool isLeader = newTeam.leaderId == me
                                || (newTeam.coLeaderIds != null && newTeam.coLeaderIds.Contains(me));
                if (!wasLeader && isLeader)
                    Toast($"You now lead {newTeam.name}.", good: true);
                else if (wasLeader && !isLeader)
                    Toast($"You no longer lead {newTeam.name}.", good: false);
            }

            // Teammates coming and going.
            if (newTeam != null && oldTeam != null && oldTeam.teamId == newTeam.teamId)
            {
                foreach (var id in newTeam.memberIds)
                    if (!string.IsNullOrEmpty(id) && id != me && !oldTeam.memberIds.Contains(id))
                        Toast($"{NameOf(id)} joined {newTeam.name}.", good: true);
                foreach (var id in oldTeam.memberIds)
                    if (!string.IsNullOrEmpty(id) && id != me && !newTeam.memberIds.Contains(id))
                        Toast($"{NameOf(id)} left {newTeam.name}.", good: false);
            }

            // A fresh invite for me.
            var oldInvites = oldState != null ? oldState.invites : null;
            foreach (var inv in newState.invites)
            {
                if (inv == null || inv.invitedId != me) continue;
                if (oldInvites != null && oldInvites.Exists(
                        i => i != null && i.teamId == inv.teamId && i.invitedId == me)) continue;
                var team = newState.teams.Find(t => t != null && t.teamId == inv.teamId);
                if (team != null)
                    Toast($"Invited to {team.name} - answer it under PAUSE > TEAMS.", good: true);
            }
        }

        private static TeamData TeamIn(TeamSnapshot snapshot, string playerId)
            => snapshot?.teams.Find(t => t != null && t.memberIds.Contains(playerId));

        private static string NameOf(string playerId)
        {
            var presence = NetworkSession.GetPlayer(playerId);
            return presence != null && !string.IsNullOrEmpty(presence.displayName)
                ? presence.displayName : playerId;
        }

        private static void Toast(string text, bool good)
            => VoxelEngine.UI.BuildFeedbackHud.Show("Teams", text, null,
                good ? new Color(0.22f, 0.78f, 0.42f) : new Color(0.92f, 0.60f, 0.12f));

        /// <summary>The roster as one JSON string - the wire payload.</summary>
        public static string ToJson()
        {
            PruneExpired();
            return JsonUtility.ToJson(_state);
        }

        // ── disk (host-side; mirrors PlayerRecords) ───────────────────────

        private const string FileName = "teams.json";

        private static string PathFor(string worldName)
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            string folder = session != null
                ? session.WorldFolderPath(worldName)
                : Path.Combine(Application.persistentDataPath, "VoxelWorlds", worldName);
            return Path.Combine(folder, FileName);
        }

        /// <summary>Host: write the roster beside the world save. Called from
        /// the normal save path, so teams persist on exactly the cadence the
        /// rest of the host-owned world does. Unlike guest records, an empty
        /// roster IS writable - a session where the last team dissolved is a
        /// real state, not a lost one. The guard that remains is the world:
        /// a roster is never written into a world it was not loaded from.</summary>
        public static void Save()
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session == null) return;
            if (NetworkSession.Mode == SessionMode.Client) return;   // guests never write the host's world
            if (_loadedForWorld != session.worldName)
            {
                Debug.LogWarning($"[TeamRegistry] holding teams for '{_loadedForWorld}' but the open world " +
                                 $"is '{session.worldName}' - not writing them anywhere.");
                return;
            }
            try
            {
                string path = PathFor(session.worldName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, ToJson());
            }
            catch (Exception ex) { Debug.LogWarning("[TeamRegistry] Save: " + ex.Message); }
        }

        /// <summary>Read the roster for the world being loaded. A world that
        /// never had teams simply has no file - which is not an error.</summary>
        public static void Load()
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session == null) return;
            _state = new TeamSnapshot();
            _loadedForWorld = session.worldName;

            try
            {
                string path = PathFor(session.worldName);
                if (!File.Exists(path)) return;
                var loaded = JsonUtility.FromJson<TeamSnapshot>(File.ReadAllText(path));
                if (loaded != null)
                {
                    loaded.teams ??= new List<TeamData>();
                    loaded.invites ??= new List<TeamInviteData>();
                    // Sanitize co-leaders (additive 14.34.0 field): only
                    // members hold the hat, and the owner never needs it.
                    foreach (var team in loaded.teams)
                    {
                        if (team == null) continue;
                        team.coLeaderIds ??= new List<string>();
                        team.coLeaderIds.RemoveAll(
                            id => string.IsNullOrEmpty(id) || id == team.leaderId
                                  || team.memberIds == null || !team.memberIds.Contains(id));
                    }
                    loaded.maxTeams = Mathf.Clamp(loaded.maxTeams == 0 ? TeamSnapshot.DefaultMaxTeams : loaded.maxTeams, 1, 8);
                    loaded.maxMembers = Mathf.Clamp(loaded.maxMembers == 0 ? TeamSnapshot.DefaultMaxMembers : loaded.maxMembers, 2, 8);
                    _state = loaded;
                }
                Debug.Log($"[TeamRegistry] loaded {_state.teams.Count} team(s) for '{session.worldName}'.");
            }
            catch (Exception ex) { Debug.LogWarning("[TeamRegistry] Load: " + ex.Message); }
        }

        /// <summary>Session over as a GUEST: drop the mirror (the host keeps
        /// the truth and will re-send it next join). The caller says whether
        /// this machine was a guest, because by the time teardown runs the
        /// session mode has already fallen back to Offline.</summary>
        public static void ClearMirror(bool wasGuest)
        {
            if (!wasGuest) return;   // a stopped host keeps its own roster
            _state = new TeamSnapshot();
            Version++;
        }

        /// <summary>Leaving the world entirely: the next world inherits
        /// nothing, not even by accident.</summary>
        public static void ClearAll()
        {
            _state = new TeamSnapshot();
            _loadedForWorld = "";
            Version++;
        }
    }

    /// <summary>Intent op codes for the team channel. Kept as a static class
    /// of bytes so the wire struct stays a plain byte field.</summary>
    public static class TeamOp
    {
        public const byte Create = 0;
        public const byte Invite = 1;
        public const byte Accept = 2;
        public const byte Decline = 3;
        public const byte Leave = 4;
        public const byte Kick = 5;
        public const byte Promote = 6;   // owner appoints a co-leader (14.34.0)
        public const byte Demote = 7;    // owner strips a co-leader
        public const byte Rename = 8;    // owner renames the team
    }
}
