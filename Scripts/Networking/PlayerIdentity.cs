// Assets/Scripts/VoxelEngine/Networking/PlayerIdentity.cs
//
// 14.0.0-dev - Multiplayer Foundation, part 1. (14.1.4-dev: per-instance
// identity slots, so two copies of the game on ONE machine stop sharing an
// identity.)
//
// The stable identity of the player on THIS machine. Created once, persisted
// forever, and used to key every piece of per-player state in the game (code
// lock authorizations today; ownership, claims and permissions tomorrow).
// The id travels with the connection so the server can key state by player,
// not by session.
//
// Why slots exist: Unity stores PlayerPrefs per company/product, so every
// build of the game on the same machine reads the SAME prefs - two local test
// instances would share one player id and the server would treat them as one
// person. A system-wide mutex hands each running instance a slot; slot 0
// keeps the original pref keys (nobody's identity changes), later slots get
// suffixed keys with their own id and name. Real players run one instance
// per machine and always sit in slot 0.

using System;
using System.Threading;
using UnityEngine;

namespace VoxelEngine.Networking
{
    public static class PlayerIdentity
    {
        private const string IdKey = "ve_player_id";
        private const string NameKey = "ve_player_name";
        private const string SkinKey = "ve_player_skin";
        private const string MutexPrefix = "IndustrialWorld.PlayerSlot.";
        private const int MaxSlots = 16;

        private static string _localId;
        private static string _localName;
        private static int _instanceSlot = -1;
        private static Mutex _slotMutex;   // held for the lifetime of this instance

        /// <summary>Stable per-installation player id (GUID). Never changes once created.</summary>
        public static string LocalId
        {
            get
            {
                if (!string.IsNullOrEmpty(_localId)) return _localId;
                _localId = PlayerPrefs.GetString(IdStoreKey, "");
                if (string.IsNullOrEmpty(_localId))
                {
                    _localId = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(IdStoreKey, _localId);
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
                _localName = PlayerPrefs.GetString(NameStoreKey, "");
                if (string.IsNullOrEmpty(_localName)) _localName = "Crusader";
                return _localName;
            }
            set
            {
                _localName = string.IsNullOrEmpty(value) ? "Crusader" : value.Trim();
                // 14.47.1 - names are capped at 20 characters everywhere:
                // here at the source, in the fields, and on the server.
                if (_localName.Length > 20) _localName = _localName.Substring(0, 20).Trim();
                if (_localName.Length == 0) _localName = "Crusader";
                PlayerPrefs.SetString(NameStoreKey, _localName);
                PlayerPrefs.Save();
                // The roster entry is live, not a snapshot - and when online,
                // the server updates our avatar's name for everyone.
                NetworkSession.UpdateDisplayName(LocalId, _localName);
                if (NetworkBootstrap.Instance != null) NetworkBootstrap.Instance.AnnounceLocalName();
            }
        }

        // ─────────────────────────── instance slots ───────────────────────────

        private static string IdStoreKey => SlotSuffix.Length == 0 ? IdKey : IdKey + SlotSuffix;
        private static string NameStoreKey => SlotSuffix.Length == 0 ? NameKey : NameKey + SlotSuffix;
        private static string SkinStoreKey => SlotSuffix.Length == 0 ? SkinKey : SkinKey + SlotSuffix;

        private static int _localSkinTone = -1;

        /// <summary>Chosen skin tone index (14.15.0). Stored per instance slot like
        /// the name; the avatar's pose mirror picks changes up automatically, so
        /// setting it while online restyles the body for everyone within a tick.</summary>
        public static int LocalSkinTone
        {
            get
            {
                if (_localSkinTone < 0)
                    _localSkinTone = Mathf.Clamp(
                        PlayerPrefs.GetInt(SkinStoreKey, 2), 0, CrusaderModel.SkinToneCount - 1);
                return _localSkinTone;
            }
            set
            {
                _localSkinTone = Mathf.Clamp(value, 0, CrusaderModel.SkinToneCount - 1);
                PlayerPrefs.SetInt(SkinStoreKey, _localSkinTone);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Slot 0 keeps the original pref keys; later instances get
        /// "_2", "_3"... so every concurrent instance owns its own identity.</summary>
        private static string SlotSuffix
        {
            get
            {
                int slot = InstanceSlot;
                return slot == 0 ? "" : "_" + (slot + 1);
            }
        }

        /// <summary>14.51.0 - the slot suffix, published so every OTHER piece
        /// of per-player local state (cosmetics, future preferences) can key
        /// its storage the same way. Two test instances on one machine must
        /// disagree about who they are in every file, not just the id.</summary>
        public static string StoreSlotSuffix => SlotSuffix;

        private static int InstanceSlot
        {
            get
            {
                if (_instanceSlot >= 0) return _instanceSlot;
                _instanceSlot = 0;   // fallback: single instance / mutex unsupported
                try
                {
                    for (int i = 0; i < MaxSlots; i++)
                    {
                        var mutex = new Mutex(false, MutexPrefix + i);
                        bool acquired;
                        try { acquired = mutex.WaitOne(0); }
                        catch (AbandonedMutexException) { acquired = true; }   // previous owner died - slot is ours
                        if (acquired)
                        {
                            _slotMutex = mutex;   // hold it until the process exits
                            _instanceSlot = i;
                            break;
                        }
                        mutex.Dispose();
                    }
                }
                catch (Exception)
                {
                    // Named mutexes unavailable on this platform - one local
                    // instance is the normal case anyway.
                }
                return _instanceSlot;
            }
        }
    }
}
