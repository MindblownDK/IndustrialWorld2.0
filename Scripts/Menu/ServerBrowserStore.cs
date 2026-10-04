// Assets/Scripts/VoxelEngine/Menu/ServerBrowserStore.cs
//
// 14.48.0-dev - the server browser's memory.
//
// One flat list of servers THIS machine knows, persisted machine-wide in
// persistentDataPath (it is the player's address book, not a world's):
// manually added entries, favorites (a flag, so any entry can be starred),
// and recents (every successful join stamps the entry with the time). No
// master server, by locked decision - this file plus LAN discovery IS the
// browser's entire data model.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VoxelEngine.Menu
{
    [Serializable]
    public class ServerBrowserEntry
    {
        public string name = "";
        public string address = "";
        public bool favorite;
        /// <summary>UTC ticks of the last successful join; 0 = never.</summary>
        public long lastJoinedTicks;
    }

    public static class ServerBrowserStore
    {
        [Serializable]
        private class Payload { public List<ServerBrowserEntry> entries = new(); }

        private const string FileName = "server_browser.json";
        private const int MaxRecents = 10;

        private static Payload _data;

        /// <summary>Bumped on every change so the browser page knows when
        /// to rebuild - the registry pattern.</summary>
        public static int Version { get; private set; }

        public static IReadOnlyList<ServerBrowserEntry> All
        {
            get { Ensure(); return _data.entries; }
        }

        public static List<ServerBrowserEntry> Favorites()
        {
            Ensure();
            var list = new List<ServerBrowserEntry>();
            foreach (var e in _data.entries) if (e.favorite) list.Add(e);
            return list;
        }

        /// <summary>Most recently joined first, never more than ten.</summary>
        public static List<ServerBrowserEntry> Recents()
        {
            Ensure();
            var list = new List<ServerBrowserEntry>();
            foreach (var e in _data.entries) if (e.lastJoinedTicks > 0) list.Add(e);
            list.Sort((a, b) => b.lastJoinedTicks.CompareTo(a.lastJoinedTicks));
            if (list.Count > MaxRecents) list.RemoveRange(MaxRecents, list.Count - MaxRecents);
            return list;
        }

        public static ServerBrowserEntry Find(string address)
        {
            Ensure();
            address = Normalize(address);
            foreach (var e in _data.entries)
                if (string.Equals(e.address, address, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        /// <summary>Manual add, or a rename of an existing entry. The
        /// address is the identity; the name is a label.</summary>
        public static void AddOrUpdate(string name, string address)
        {
            address = Normalize(address);
            if (string.IsNullOrEmpty(address)) return;
            var entry = Find(address);
            if (entry == null)
            {
                entry = new ServerBrowserEntry { address = address };
                _data.entries.Add(entry);
            }
            if (!string.IsNullOrWhiteSpace(name)) entry.name = name.Trim();
            if (string.IsNullOrEmpty(entry.name)) entry.name = address;
            Touch();
        }

        public static void Remove(string address)
        {
            var entry = Find(address);
            if (entry == null) return;
            _data.entries.Remove(entry);
            Touch();
        }

        public static void ToggleFavorite(string address)
        {
            var entry = Find(address);
            if (entry == null) return;
            entry.favorite = !entry.favorite;
            Touch();
        }

        /// <summary>A successful join stamps the clock (creating the entry
        /// when the address was typed by hand), and adopts the server's own
        /// name unless the player labeled it themselves.</summary>
        public static void NoteJoined(string address, string nameHint)
        {
            address = Normalize(address);
            if (string.IsNullOrEmpty(address)) return;
            var entry = Find(address);
            if (entry == null)
            {
                entry = new ServerBrowserEntry { address = address, name = address };
                _data.entries.Add(entry);
            }
            if (!string.IsNullOrWhiteSpace(nameHint) &&
                (string.IsNullOrEmpty(entry.name) || entry.name == entry.address))
                entry.name = nameHint.Trim();
            entry.lastJoinedTicks = DateTime.UtcNow.Ticks;
            Touch();
        }

        // ───────────────────────── internals ─────────────────────────

        private static string Normalize(string address) => (address ?? "").Trim();

        private static string PathFile => Path.Combine(Application.persistentDataPath, FileName);

        private static void Ensure()
        {
            if (_data != null) return;
            _data = new Payload();
            try
            {
                if (File.Exists(PathFile))
                {
                    var loaded = JsonUtility.FromJson<Payload>(File.ReadAllText(PathFile));
                    if (loaded != null && loaded.entries != null) _data = loaded;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[ServerBrowser] load: " + ex.Message); }
        }

        private static void Touch()
        {
            Version++;
            try { File.WriteAllText(PathFile, JsonUtility.ToJson(_data, true)); }
            catch (Exception ex) { Debug.LogWarning("[ServerBrowser] save: " + ex.Message); }
        }
    }
}
