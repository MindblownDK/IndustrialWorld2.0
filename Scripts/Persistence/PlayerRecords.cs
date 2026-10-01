// Assets/Scripts/VoxelEngine/Persistence/PlayerRecords.cs
//
// 14.24.0-dev - milestone 8, part (c): per-player state belongs to the HOST.
//
// Before this, a guest's inventory was their own local business. Two people
// could mine the same ore and both keep it; a guest who came back brought
// whatever happened to be on their own machine; and nothing a visitor did in
// your world was in your world's save afterwards. None of that is what joining
// a game means.
//
// The host now keeps one record per player, keyed by stable player id (the
// MP-readiness rule - a record must survive a rename and a reconnect), in a
// sidecar beside the world it belongs to. The record itself is the same
// SavedPlayer block the save file has always used, carried as opaque JSON, so
// the two formats cannot drift apart: whatever a save can describe about a
// player, a guest record describes identically.
//
// The host's OWN player is NOT in here - it stays in the world save's player
// block exactly as before, so an existing single-player save is untouched and
// a world that has never been hosted never grows this file.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VoxelEngine.Persistence
{
    public static class PlayerRecords
    {
        private const string FileName = "players.json";

        /// <summary>playerId -> SavedPlayer JSON. Host-side only.</summary>
        private static readonly Dictionary<string, string> _records = new Dictionary<string, string>();

        private static string _loadedForWorld = "";

        // ── the local guest's own copy, handed down at join ──────────────

        /// <summary>The record the host sent for THIS player, waiting to be
        /// applied by the spawner. Null once taken.</summary>
        public static string LocalPending { get; private set; }

        /// <summary>True once the host has answered about this player, either
        /// with a record or with "I have never seen you". The spawner waits on
        /// this rather than on a record existing, because a first-time visitor
        /// is a perfectly normal answer and must not stall behind a timeout.</summary>
        public static bool LocalSettled { get; private set; }

        /// <summary>Client: the host's answer has arrived.</summary>
        public static void ReceiveLocalRecord(string json)
        {
            LocalPending = string.IsNullOrEmpty(json) ? null : json;
            LocalSettled = true;
            Debug.Log("[PlayerRecords] host answered for this player: " +
                      (LocalPending == null ? "no stored record, fresh start." : LocalPending.Length + " chars."));
        }

        /// <summary>Client: give up waiting. Called by the spawner's timeout so
        /// a host that never answers costs seconds, not the session.</summary>
        public static void SettleLocalEmpty()
        {
            if (LocalSettled) return;
            LocalSettled = true;
            LocalPending = null;
        }

        /// <summary>Hand the pending record over exactly once.</summary>
        public static bool TryTakeLocalRecord(out string json)
        {
            json = LocalPending;
            LocalPending = null;
            return !string.IsNullOrEmpty(json);
        }

        /// <summary>Leaving a session as a GUEST. Only the local half is
        /// dropped: a host that stops hosting must keep the records it is
        /// holding, or the next save would write an empty file over them.</summary>
        public static void ClearLocal()
        {
            LocalPending = null;
            LocalSettled = false;
        }

        /// <summary>Leaving the world entirely. Everything goes, so the next
        /// world - single player or someone else's - inherits nothing.</summary>
        public static void Clear()
        {
            _records.Clear();
            _loadedForWorld = "";
            ClearLocal();
        }

        // ── host-side store ───────────────────────────────────────────────

        /// <summary>Host: remember what this player is carrying. The id comes
        /// from the connection table on the server, never from the message, so
        /// one client cannot write over another client's record.</summary>
        public static void Store(string playerId, string json)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(json)) return;
            _records[playerId] = json;
        }

        /// <summary>Host: what we hold for this player, or "" for a newcomer.</summary>
        public static string Get(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return "";
            return _records.TryGetValue(playerId, out var json) ? json : "";
        }

        public static int Count => _records.Count;

        // ── disk ──────────────────────────────────────────────────────────

        private static string PathFor(string worldName)
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            string folder = session != null
                ? session.WorldFolderPath(worldName)
                : Path.Combine(Application.persistentDataPath, "VoxelWorlds", worldName);
            return Path.Combine(folder, FileName);
        }

        /// <summary>Host: write the guest records beside the world save. Called
        /// from the normal save path, so guests are persisted on exactly the
        /// same cadence as everything else the host owns.</summary>
        public static void Save()
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session == null) return;
            // Never write an empty file. There is no legitimate way to go from
            // "this world has guests" to "this world has none", so an empty
            // store means something upstream lost them - and overwriting a good
            // file with that is how a guest's inventory would disappear for
            // good. Nothing to say is said by saying nothing.
            if (_records.Count == 0) return;

            // And never into a world we did not read from. A host that changed
            // worlds without a reload would otherwise drop one world's guests
            // into another world's folder.
            if (_loadedForWorld != session.worldName)
            {
                Debug.LogWarning($"[PlayerRecords] holding records for '{_loadedForWorld}' but the open world " +
                                 $"is '{session.worldName}' - not writing them anywhere.");
                return;
            }

            try
            {
                var payload = new Payload();
                foreach (var pair in _records)
                    payload.entries.Add(new Entry { playerId = pair.Key, json = pair.Value });

                string path = PathFor(session.worldName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(payload, true));
            }
            catch (Exception ex) { Debug.LogWarning("[PlayerRecords] Save: " + ex.Message); }
        }

        /// <summary>Host: read the guest records for the world being loaded.
        /// A world that has never been hosted simply has no file, which is not
        /// an error and is not logged as one.</summary>
        public static void Load()
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session == null) return;
            _records.Clear();
            _loadedForWorld = session.worldName;

            try
            {
                string path = PathFor(session.worldName);
                if (!File.Exists(path)) return;
                var payload = JsonUtility.FromJson<Payload>(File.ReadAllText(path));
                if (payload == null || payload.entries == null) return;
                foreach (var e in payload.entries)
                    if (e != null && !string.IsNullOrEmpty(e.playerId) && !string.IsNullOrEmpty(e.json))
                        _records[e.playerId] = e.json;
                Debug.Log($"[PlayerRecords] loaded {_records.Count} guest record(s) for '{session.worldName}'.");
            }
            catch (Exception ex) { Debug.LogWarning("[PlayerRecords] Load: " + ex.Message); }
        }

        // JsonUtility cannot serialize a dictionary, so the file is a list of
        // pairs. The record itself stays an opaque string: this file never has
        // to understand what a player is, which is why adding a field to
        // SavedPlayer can never break it.
        [Serializable] private class Entry { public string playerId; public string json; }
        [Serializable] private class Payload { public List<Entry> entries = new List<Entry>(); }
    }
}
