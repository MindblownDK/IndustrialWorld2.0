// Assets/Scripts/VoxelEngine/Networking/PlayerIdentity.cs
//
// 14.0.0-dev - Multiplayer Foundation, part 1.
//
// The stable identity of the player on THIS machine. Created once, persisted
// forever, and used to key every piece of per-player state in the game (code
// lock authorizations today; ownership, claims and permissions tomorrow).
// When the Fish-Net layer lands, this id travels with the connection so the
// server can key state by player, not by session.

using System;
using UnityEngine;

namespace VoxelEngine.Networking
{
    public static class PlayerIdentity
    {
        private const string IdKey = "ve_player_id";
        private const string NameKey = "ve_player_name";

        private static string _localId;
        private static string _localName;

        /// <summary>Stable per-installation player id (GUID). Never changes once created.</summary>
        public static string LocalId
        {
            get
            {
                if (!string.IsNullOrEmpty(_localId)) return _localId;
                _localId = PlayerPrefs.GetString(IdKey, "");
                if (string.IsNullOrEmpty(_localId))
                {
                    _localId = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(IdKey, _localId);
                    PlayerPrefs.Save();
                }
                return _localId;
            }
        }

        /// <summary>Display name shown to other players. Freely changeable.</summary>
        public static string LocalName
        {
            get
            {
                if (!string.IsNullOrEmpty(_localName)) return _localName;
                _localName = PlayerPrefs.GetString(NameKey, "");
                if (string.IsNullOrEmpty(_localName)) _localName = "Crusader";
                return _localName;
            }
            set
            {
                _localName = string.IsNullOrEmpty(value) ? "Crusader" : value.Trim();
                PlayerPrefs.SetString(NameKey, _localName);
                PlayerPrefs.Save();
                // The roster entry is live, not a snapshot - and when online,
                // the server updates our avatar's name for everyone.
                NetworkSession.UpdateDisplayName(LocalId, _localName);
                if (NetworkBootstrap.Instance != null) NetworkBootstrap.Instance.AnnounceLocalName();
            }
        }
    }
}
