// Assets/Scripts/VoxelEngine/Networking/ServerAdminRegistry.cs
//
// 14.47.0-dev - Server administration: owner, admins, kick/ban, whitelist,
// join password and live world-rule editing.
//
// Authority follows the locked rule: the HOST owns this state, clients send
// intents and mirror the broadcast truth. Ownership is a property of the
// WORLD (persisted in a sidecar next to the save), so one player can own
// any number of servers - each world remembers its own owner by stable
// player id.
//
// How a server gets its owner:
//   - Listen server: the hosting machine's local player IS the owner, always
//     (they hold the save file; no registry entry can outrank the disk).
//   - Dedicated server: the first player who ever joins a fresh world is
//     auto-claimed as owner, and the adminPassword in server_config.json
//     always works as a claim/recovery path from the Administration tab.
//
// Ranks: Owner > Admin > None. Admins can kick/ban, manage the whitelist
// and edit world rules; only the owner promotes/demotes admins and sets the
// join password. Neither kick nor ban can ever touch a peer or superior
// rank. Owner and admins bypass the whitelist and the join password, so an
// owner can never lock themselves out.
//
// Bans are permanent (untilTicks == 0) or timed (host-clock UTC ticks).
// Expiry is judged ONLY by the host's clock - the 14.46.1 lesson: never
// compare server-authored timestamps against client clocks. Snapshots carry
// the host's "now" so clients display remaining time as a delta.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VoxelEngine.Networking
{
    [Serializable]
    public class AdminPlayerEntry
    {
        public string id = "";
        public string name = "";
    }

    [Serializable]
    public class AdminBanEntry
    {
        public string id = "";
        public string name = "";
        public string reason = "";
        /// <summary>Host UTC ticks when the ban lifts; 0 = permanent.</summary>
        public long untilTicks;
    }

    /// <summary>The whole administrative truth of one world. Serialized to
    /// the world sidecar verbatim; serialized to the wire with the password
    /// blanked (clients only ever learn whether one is set).</summary>
    [Serializable]
    public class AdminState
    {
        public string ownerId = "";
        public string ownerName = "";
        public List<AdminPlayerEntry> admins = new();
        public List<AdminBanEntry> bans = new();
        public bool whitelistEnabled;
        public List<AdminPlayerEntry> whitelist = new();
        /// <summary>Join password. Empty = open server. Never leaves the host.</summary>
        public string password = "";

        // ── wire-only fields (host fills them at snapshot time) ──
        public bool passwordSet;
        public bool dedicated;
        public long nowTicks;
    }

    public static class ServerAdminRegistry
    {
        public const int RankNone = 0;
        public const int RankAdmin = 1;
        public const int RankOwner = 2;

        // ── intent ops (client -> host) ──
        public const byte OpKick = 1;
        public const byte OpBan = 2;              // Number = duration seconds, 0 = permanent
        public const byte OpUnban = 3;
        public const byte OpPromote = 4;          // owner only
        public const byte OpDemote = 5;           // owner only
        public const byte OpWhitelistEnable = 6;  // Number = 1 on / 0 off
        public const byte OpWhitelistAdd = 7;     // Text = name or id
        public const byte OpWhitelistRemove = 8;  // TargetId = entry id OR Text = entry name
        public const byte OpSetPassword = 9;      // owner only; Text = new password, "" clears
        public const byte OpClaimOwner = 10;      // Text = admin password attempt (dedicated only)
        public const byte OpSetRule = 11;         // TargetId = rule key, Text = value

        private const string FileName = "server_admin.json";

        private static AdminState _state = new();
        private static bool _loaded;
        private static string _loadedWorld = "";

        /// <summary>Bumped on every change (host mutation or client snapshot)
        /// so panels know when to rebuild - the TeamRegistry pattern.</summary>
        public static int Version { get; private set; }

        /// <summary>Client mirror of its own rank, delivered with every
        /// snapshot. Hosts compute their rank locally instead.</summary>
        public static int ClientRank { get; private set; }

        /// <summary>The current truth. Host: authoritative. Client: the last
        /// broadcast mirror (password always blank there).</summary>
        public static AdminState State => _state;

        // ───────────────────────── rank ─────────────────────────

        /// <summary>Host-side rank of a player id. The local player of a
        /// LISTEN host is always owner - they hold the save file.</summary>
        public static int RankOf(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return RankNone;
            EnsureLoaded();
            if (!DedicatedServer.IsActive
                && NetworkSession.Mode == SessionMode.Host
                && playerId == NetworkSession.LocalPlayerId)
                return RankOwner;
            if (playerId == _state.ownerId) return RankOwner;
            foreach (var a in _state.admins)
                if (a.id == playerId) return RankAdmin;
            return RankNone;
        }

        /// <summary>The LOCAL player's effective rank, host or client.</summary>
        public static int LocalRank()
        {
            if (NetworkSession.Mode == SessionMode.Client) return ClientRank;
            if (NetworkSession.Mode == SessionMode.Host) return RankOf(NetworkSession.LocalPlayerId);
            return RankNone;
        }

        // ───────────────────────── admission (host) ─────────────────────────

        /// <summary>Host: may this player join? Returns null to admit or a
        /// human reason to refuse. Owner and admins bypass whitelist and
        /// password - the owner can never lock themselves out.</summary>
        public static string AdmissionCheck(string playerId, string playerName, string password)
        {
            EnsureLoaded();
            PruneExpiredBans();

            foreach (var ban in _state.bans)
            {
                if (ban.id != playerId) continue;
                string until = ban.untilTicks == 0
                    ? "permanently"
                    : "for another " + Describe(TimeSpan.FromTicks(Math.Max(0, ban.untilTicks - DateTime.UtcNow.Ticks)));
                return $"You are banned from this server {until}."
                       + (string.IsNullOrEmpty(ban.reason) ? "" : $" Reason: {ban.reason}");
            }

            bool privileged = RankOf(playerId) >= RankAdmin;
            if (!privileged && _state.whitelistEnabled && !OnWhitelist(playerId, playerName))
                return "This server runs a whitelist and you are not on it.";

            // 14.47.1 - two different doors closed: no password offered at
            // all gets the invitation to type one, a wrong one gets told so.
            if (!privileged && !string.IsNullOrEmpty(_state.password) && _state.password != (password ?? ""))
                return string.IsNullOrEmpty(password)
                    ? "This server is password protected. Please enter the server password and join again."
                    : "Wrong server password.";

            return null;
        }

        /// <summary>Host, dedicated only: a fresh world adopts its first ever
        /// player as owner. Returns true when a claim happened.</summary>
        public static bool HostMaybeAutoClaim(string playerId, string playerName)
        {
            if (!DedicatedServer.IsActive) return false;
            EnsureLoaded();
            if (!string.IsNullOrEmpty(_state.ownerId)) return false;
            if (string.IsNullOrEmpty(playerId)) return false;
            _state.ownerId = playerId;
            _state.ownerName = playerName ?? "";
            Touch();
            Debug.Log($"[Admin] '{playerName}' ({playerId}) is the first player on this fresh " +
                      "dedicated world and has been claimed as its OWNER.");
            return true;
        }

        // ───────────────────────── intents ─────────────────────────

        /// <summary>UI entry point, host or client. Hosts apply directly and
        /// surface the verdict themselves; clients send the intent and let
        /// the notice broadcast carry the verdict back.</summary>
        public static void Route(byte op, string targetId, string text, long number)
        {
            if (NetworkSession.Mode == SessionMode.Client)
            {
                NetworkBootstrap.Instance?.SendAdminIntent(op, targetId ?? "", text ?? "", number);
                return;
            }
            string error = HostApply(op, NetworkSession.LocalPlayerId, targetId ?? "", text ?? "", number);
            if (!string.IsNullOrEmpty(error))
                VoxelEngine.UI.BuildFeedbackHud.Show("Server", error, null, new Color(0.82f, 0.22f, 0.18f));
        }

        /// <summary>Host: apply one administrative intent. Returns null when
        /// applied or a human refusal. Every applied change saves, bumps the
        /// version and rebroadcasts the admin state.</summary>
        public static string HostApply(byte op, string requesterId, string targetId, string text, long number)
        {
            EnsureLoaded();
            int rank = RankOf(requesterId);

            switch (op)
            {
                case OpClaimOwner:
                {
                    if (!DedicatedServer.IsActive)
                        return "A listen server's host already owns it - claiming applies to dedicated servers.";
                    string adminPw = DedicatedServer.Config.adminPassword ?? "";
                    if (string.IsNullOrEmpty(adminPw))
                        return "This server has no adminPassword in server_config.json, so claiming is disabled.";
                    if (adminPw != (text ?? ""))
                        return "Wrong admin password.";
                    if (requesterId == _state.ownerId)
                        return "You already own this server.";
                    // The previous owner steps down to admin rather than out.
                    if (!string.IsNullOrEmpty(_state.ownerId) && _state.ownerId != requesterId)
                        AddAdmin(_state.ownerId, _state.ownerName);
                    RemoveAdmin(requesterId);
                    _state.ownerId = requesterId;
                    _state.ownerName = DisplayName(requesterId);
                    Touch();
                    Debug.Log($"[Admin] ownership claimed by '{_state.ownerName}' ({requesterId}) via admin password.");
                    return null;
                }

                case OpKick:
                case OpBan:
                {
                    if (rank < RankAdmin) return "Only the owner and admins can do that.";
                    if (string.IsNullOrEmpty(targetId)) return "No target.";
                    if (targetId == requesterId) return "You cannot " + (op == OpKick ? "kick" : "ban") + " yourself.";
                    if (RankOf(targetId) >= rank)
                        return "You cannot act on a player of equal or higher rank.";

                    string name = DisplayName(targetId);
                    // 14.47.1 - the admin's parting words ride along, capped
                    // at 40 characters no matter what the client claimed.
                    string reason = (text ?? "").Trim();
                    if (reason.Length > 40) reason = reason.Substring(0, 40).Trim();
                    if (op == OpBan)
                    {
                        PruneExpiredBans();
                        _state.bans.RemoveAll(b => b.id == targetId);
                        _state.bans.Add(new AdminBanEntry
                        {
                            id = targetId,
                            name = name,
                            reason = reason,
                            untilTicks = number <= 0 ? 0 : DateTime.UtcNow.Ticks + TimeSpan.FromSeconds(number).Ticks
                        });
                    }
                    string goodbye = op == OpKick
                        ? "You were kicked from the server."
                        : "You were banned from the server" +
                          (number <= 0 ? "." : $" for {Describe(TimeSpan.FromSeconds(number))}.");
                    if (!string.IsNullOrEmpty(reason)) goodbye += $"\n\n\"{reason}\"";
                    bool online = NetworkBootstrap.Instance != null
                        && NetworkBootstrap.Instance.DisconnectPlayer(targetId,
                            op == OpKick ? NetworkBootstrap.NoticeKicked : NetworkBootstrap.NoticeBanned,
                            goodbye);
                    if (op == OpKick && !online) return $"{name} is not online.";
                    Touch();
                    Debug.Log($"[Admin] {(op == OpKick ? "kick" : "ban")} applied to '{name}' ({targetId}) " +
                              $"by {requesterId}" + (op == OpBan && number > 0 ? $" for {number}s." : "."));
                    return null;
                }

                case OpUnban:
                {
                    if (rank < RankAdmin) return "Only the owner and admins can do that.";
                    int removed = _state.bans.RemoveAll(b => b.id == targetId);
                    if (removed == 0) return "No such ban.";
                    Touch();
                    return null;
                }

                case OpPromote:
                {
                    if (rank < RankOwner) return "Only the owner promotes admins.";
                    if (string.IsNullOrEmpty(targetId)) return "No target.";
                    if (targetId == _state.ownerId || RankOf(targetId) == RankOwner)
                        return "The owner needs no promotion.";
                    if (RankOf(targetId) == RankAdmin) return "Already an admin.";
                    AddAdmin(targetId, DisplayName(targetId));
                    Touch();
                    return null;
                }

                case OpDemote:
                {
                    if (rank < RankOwner) return "Only the owner demotes admins.";
                    if (!RemoveAdmin(targetId)) return "Not an admin.";
                    Touch();
                    return null;
                }

                case OpWhitelistEnable:
                {
                    if (rank < RankAdmin) return "Only the owner and admins can do that.";
                    _state.whitelistEnabled = number != 0;
                    Touch();
                    WritebackDedicated();
                    return null;
                }

                case OpWhitelistAdd:
                {
                    if (rank < RankAdmin) return "Only the owner and admins can do that.";
                    string entry = (text ?? "").Trim();
                    if (string.IsNullOrEmpty(entry)) return "Type a player name or id first.";
                    // An online player matching by name or id pins the entry
                    // to their stable id; otherwise the raw text matches by
                    // name at the door until they first show up.
                    string id = "", name = entry;
                    foreach (var p in NetworkSession.Players)
                    {
                        if (p.playerId == entry ||
                            string.Equals(p.displayName, entry, StringComparison.OrdinalIgnoreCase))
                        { id = p.playerId; name = p.displayName; break; }
                    }
                    foreach (var w in _state.whitelist)
                        if ((id != "" && w.id == id) ||
                            string.Equals(w.name, name, StringComparison.OrdinalIgnoreCase))
                            return $"{name} is already on the whitelist.";
                    _state.whitelist.Add(new AdminPlayerEntry { id = id, name = name });
                    Touch();
                    return null;
                }

                case OpWhitelistRemove:
                {
                    if (rank < RankAdmin) return "Only the owner and admins can do that.";
                    int removed = _state.whitelist.RemoveAll(w =>
                        (!string.IsNullOrEmpty(targetId) && w.id == targetId) ||
                        (!string.IsNullOrEmpty(text) &&
                         string.Equals(w.name, text, StringComparison.OrdinalIgnoreCase)));
                    if (removed == 0) return "No such whitelist entry.";
                    Touch();
                    return null;
                }

                case OpSetPassword:
                {
                    if (rank < RankOwner) return "Only the owner sets the server password.";
                    _state.password = (text ?? "").Trim();
                    Touch();
                    WritebackDedicated();
                    return null;
                }

                case OpSetRule:
                {
                    if (rank < RankAdmin) return "Only the owner and admins change world settings.";
                    return HostApplyRule(targetId ?? "", text ?? "");
                }

                default:
                    return "Unknown administrative action.";
            }
        }

        // ───────────────────────── world rules ─────────────────────────

        /// <summary>Host: apply one rule, persist, and sync it. World rules
        /// go to every client as a WorldRuleBroadcast; team limits ride the
        /// roster broadcast they always rode; server-machine rules (autosave,
        /// server name) touch only the host.</summary>
        private static string HostApplyRule(string key, string value)
        {
            switch (key)
            {
                case "maxTeams":
                case "maxMembers":
                {
                    if (!int.TryParse(value, out int n)) return "Not a number.";
                    TeamRegistry.RequestLimits(
                        key == "maxTeams" ? n : TeamRegistry.MaxTeams,
                        key == "maxMembers" ? n : TeamRegistry.MaxMembers);
                    Version++;
                    return null;   // RequestLimits saved + rebroadcast the roster itself
                }

                case "autosaveSeconds":
                {
                    if (!int.TryParse(value, out int s)) return "Not a number.";
                    s = Mathf.Clamp(s, 0, 3600);
                    VoxelEngine.Settings.GameSettings.AutosaveSeconds = s;
                    if (DedicatedServer.IsActive)
                    {
                        DedicatedServer.Config.autosaveSeconds = s;
                        DedicatedServer.SaveConfig();
                    }
                    Version++;
                    return null;
                }

                case "serverName":
                {
                    if (!DedicatedServer.IsActive) return "Only a dedicated server has a server name.";
                    DedicatedServer.Config.serverName =
                        string.IsNullOrWhiteSpace(value) ? "IndustrialCrusaders Server" : value.Trim();
                    DedicatedServer.SaveConfig();
                    Version++;
                    return null;
                }

                default:
                {
                    if (!ApplyRuleLocal(key, value)) return $"Unknown setting '{key}'.";
                    VoxelEngine.Menu.WorldSession.Instance?.SaveWorldSettings();
                    WritebackDedicated();
                    NetworkBootstrap.Instance?.BroadcastWorldRule(key, value);
                    return null;
                }
            }
        }

        /// <summary>Apply one world rule to the local WorldSession - the SAME
        /// parser on host and client, so the mirrored value cannot drift.
        /// Returns false for unknown keys. Bumps Version so open panels
        /// refresh.</summary>
        public static bool ApplyRuleLocal(string key, string value)
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session == null) return false;
            bool on = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

            switch (key)
            {
                case "friendlyFire": session.friendlyFire = on; break;
                case "allowRuinLootRespawn": session.allowRuinLootRespawn = on; break;
                case "allowBannerPainting": session.allowBannerPainting = on; break;
                case "allowTeammateTeleport": session.allowTeammateTeleport = on; break;
                case "offlineDeath": session.offlineDeath = on; break;
                case "showDropVoidWarning": session.showDropVoidWarning = on; break;
                case "maxDroppedItems":
                    if (!int.TryParse(value, out int drops)) return false;
                    session.maxDroppedItems = Mathf.Clamp(drops, 50, 10000);
                    break;
                case "inventoryWeightPercent":
                    if (!int.TryParse(value, out int inv)) return false;
                    session.inventoryWeightPercent = Mathf.Clamp(inv, 25, 1000);
                    break;
                case "containerWeightPercent":
                    if (!int.TryParse(value, out int cont)) return false;
                    session.containerWeightPercent = Mathf.Clamp(cont, 25, 1000);
                    break;
                default: return false;
            }
            Version++;
            return true;
        }

        // ───────────────────────── wire ─────────────────────────

        /// <summary>Host: the state as sent to owner/admin clients. The
        /// password never travels; clients only learn whether one is set.
        /// Carries the host's UTC "now" so remaining ban time is displayed
        /// as a delta - never judged against a client clock.</summary>
        public static string ToWireJson()
        {
            EnsureLoaded();
            PruneExpiredBans();
            var copy = JsonUtility.FromJson<AdminState>(JsonUtility.ToJson(_state)) ?? new AdminState();
            copy.passwordSet = !string.IsNullOrEmpty(copy.password);
            copy.password = "";
            copy.dedicated = DedicatedServer.IsActive;
            copy.nowTicks = DateTime.UtcNow.Ticks;
            return JsonUtility.ToJson(copy);
        }

        /// <summary>True when the server this client sits on is dedicated -
        /// delivered with every state broadcast, honest even at rank None.</summary>
        public static bool ClientDedicated { get; private set; }

        /// <summary>Client: adopt the host's broadcast verbatim.</summary>
        public static void ApplySnapshot(string json, int yourRank, bool dedicated)
        {
            ClientRank = Mathf.Clamp(yourRank, RankNone, RankOwner);
            ClientDedicated = dedicated;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var incoming = JsonUtility.FromJson<AdminState>(json);
                    if (incoming != null) _state = incoming;
                }
                catch (Exception ex) { Debug.LogWarning("[Admin] ApplySnapshot: " + ex.Message); }
            }
            else
            {
                _state = new AdminState();   // rank None sees no roster
            }
            Version++;
        }

        /// <summary>Session teardown: forget the mirror (client) or force a
        /// reload for the next hosted world (host).</summary>
        public static void ResetSession()
        {
            _loaded = false;
            _loadedWorld = "";
            _state = new AdminState();
            ClientRank = RankNone;
            ClientDedicated = false;
            Version++;
        }

        // ───────────────────────── config boot (dedicated) ─────────────────────────

        /// <summary>Dedicated boot: server_config.json may pin the join
        /// password and whitelist switch. Applied once the world is up,
        /// before the first player can knock.</summary>
        public static void HostAdoptConfig(ServerConfig cfg)
        {
            if (cfg == null) return;
            EnsureLoaded();
            bool changed = false;
            if (!string.IsNullOrEmpty(cfg.serverPassword) && _state.password != cfg.serverPassword)
            { _state.password = cfg.serverPassword; changed = true; }
            if (cfg.whitelistEnabled >= 0 && _state.whitelistEnabled != (cfg.whitelistEnabled == 1))
            { _state.whitelistEnabled = cfg.whitelistEnabled == 1; changed = true; }
            if (changed) Touch();
        }

        // ───────────────────────── internals ─────────────────────────

        private static void Touch()
        {
            Version++;
            Save();
            NetworkBootstrap.Instance?.BroadcastAdminState();
        }

        /// <summary>Dedicated servers keep server_config.json honest: the
        /// file an operator reads always shows the live values.</summary>
        private static void WritebackDedicated()
        {
            if (!DedicatedServer.IsActive) return;
            var cfg = DedicatedServer.Config;
            cfg.serverPassword = _state.password;
            cfg.whitelistEnabled = _state.whitelistEnabled ? 1 : 0;
            DedicatedServer.MirrorWorldRules(cfg);
            DedicatedServer.SaveConfig();
        }

        private static bool OnWhitelist(string playerId, string playerName)
        {
            foreach (var w in _state.whitelist)
            {
                if (!string.IsNullOrEmpty(w.id) && w.id == playerId) return true;
                if (!string.IsNullOrEmpty(w.name) &&
                    string.Equals(w.name, playerName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static void PruneExpiredBans()
        {
            long now = DateTime.UtcNow.Ticks;
            int removed = _state.bans.RemoveAll(b => b.untilTicks != 0 && b.untilTicks <= now);
            if (removed > 0) { Version++; Save(); }
        }

        private static void AddAdmin(string id, string name)
        {
            if (string.IsNullOrEmpty(id)) return;
            foreach (var a in _state.admins) if (a.id == id) return;
            _state.admins.Add(new AdminPlayerEntry { id = id, name = name ?? "" });
        }

        private static bool RemoveAdmin(string id)
            => _state.admins.RemoveAll(a => a.id == id) > 0;

        private static string DisplayName(string playerId)
        {
            var presence = NetworkSession.GetPlayer(playerId);
            return presence != null && !string.IsNullOrEmpty(presence.displayName)
                ? presence.displayName : (playerId ?? "?");
        }

        private static string Describe(TimeSpan span)
        {
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
            return $"{Math.Max(1, (int)span.TotalMinutes)}m";
        }

        // ── persistence: one sidecar per world, like PlayerRecords ──

        private static string PathForCurrentWorld()
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            string worldName = session != null ? session.worldName : "DefaultWorld";
            string folder = session != null
                ? session.WorldFolderPath(worldName)
                : Path.Combine(Application.persistentDataPath, "VoxelWorlds", worldName);
            return Path.Combine(folder, FileName);
        }

        private static void EnsureLoaded()
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            string worldName = session != null ? session.worldName : "DefaultWorld";
            if (_loaded && _loadedWorld == worldName) return;
            _loaded = true;
            _loadedWorld = worldName;
            _state = new AdminState();
            try
            {
                string path = PathForCurrentWorld();
                if (File.Exists(path))
                {
                    var loaded = JsonUtility.FromJson<AdminState>(File.ReadAllText(path));
                    if (loaded != null) _state = loaded;
                    Debug.Log($"[Admin] loaded {FileName}: owner " +
                              (string.IsNullOrEmpty(_state.ownerId) ? "UNCLAIMED" : $"'{_state.ownerName}'") +
                              $", {_state.admins.Count} admin(s), {_state.bans.Count} ban(s), whitelist " +
                              (_state.whitelistEnabled ? "ON" : "off") + ".");
                }
            }
            catch (Exception ex) { Debug.LogWarning("[Admin] load: " + ex.Message); }
            Version++;
        }

        private static void Save()
        {
            if (NetworkSession.Mode == SessionMode.Client) return;   // mirrors never write
            try
            {
                string path = PathForCurrentWorld();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(_state, true));
            }
            catch (Exception ex) { Debug.LogWarning("[Admin] save: " + ex.Message); }
        }
    }
}
