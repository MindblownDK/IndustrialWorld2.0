// Assets/Scripts/VoxelEngine/Networking/NetworkSession.cs
//
// 14.0.0-dev - Multiplayer Foundation, part 1.
//
// The single source of truth for "what kind of game is running and who is in
// it". Today the answer is always Offline with one local player - but every
// system that asks THIS class instead of assuming a lone player is already
// multiplayer-shaped. The Fish-Net bridge (next part of the milestone) will
// drive Mode and the player registry from real connections; gameplay code
// never talks to the transport directly.
//
// Authority rule (see README section 4): in Offline and Host modes this
// machine IS the server, so authority code runs locally. In Client mode,
// authority entry points must route through the server instead - the bridge
// will enforce that; until then IsAuthority is simply true.

using System;
using System.Collections.Generic;

namespace VoxelEngine.Networking
{
    public enum SessionMode
    {
        /// <summary>Single player. This machine is the server in all but name.</summary>
        Offline,
        /// <summary>Listen server: this machine plays AND serves 2-8 players.</summary>
        Host,
        /// <summary>Connected to someone else's server; no local authority.</summary>
        Client
    }

    /// <summary>One player known to the current session.</summary>
    public sealed class PlayerPresence
    {
        public string playerId;
        public string displayName;
    }

    public static class NetworkSession
    {
        private static readonly Dictionary<string, PlayerPresence> _players = new();
        private static bool _localRegistered;

        public static SessionMode Mode { get; private set; } = SessionMode.Offline;

        /// <summary>True when authority code (world edits, audits, damage,
        /// simulation) may run on this machine.</summary>
        public static bool IsAuthority => Mode != SessionMode.Client;

        public static string LocalPlayerId => PlayerIdentity.LocalId;

        public static event Action<PlayerPresence> PlayerJoined;
        public static event Action<PlayerPresence> PlayerLeft;

        /// <summary>All players in the session, local one included.</summary>
        public static IReadOnlyCollection<PlayerPresence> Players
        {
            get { EnsureLocalPlayer(); return _players.Values; }
        }

        public static PlayerPresence GetPlayer(string playerId)
        {
            EnsureLocalPlayer();
            return playerId != null && _players.TryGetValue(playerId, out var presence)
                ? presence : null;
        }

        // ── Bridge surface: only the networking layer calls these. ──

        public static void SetMode(SessionMode mode) => Mode = mode;

        public static void RegisterPlayer(string playerId, string displayName)
        {
            if (string.IsNullOrEmpty(playerId) || _players.ContainsKey(playerId)) return;
            var presence = new PlayerPresence { playerId = playerId, displayName = displayName };
            _players[playerId] = presence;
            PlayerJoined?.Invoke(presence);
        }

        public static void UnregisterPlayer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId) || !_players.TryGetValue(playerId, out var presence)) return;
            if (playerId == LocalPlayerId) return;   // the local player never leaves itself
            _players.Remove(playerId);
            PlayerLeft?.Invoke(presence);
        }

        private static void EnsureLocalPlayer()
        {
            if (_localRegistered && _players.ContainsKey(LocalPlayerId)) return;
            _localRegistered = true;
            if (!_players.ContainsKey(LocalPlayerId))
                _players[LocalPlayerId] = new PlayerPresence
                {
                    playerId = PlayerIdentity.LocalId,
                    displayName = PlayerIdentity.LocalName
                };
        }
    }
}
