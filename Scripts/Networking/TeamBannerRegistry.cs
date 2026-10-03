// Assets/Scripts/VoxelEngine/Networking/TeamBannerRegistry.cs
//
// 14.37.0-dev - Every team flies ONE banner. The banner is edited in the
// pause menu's TEAMS page alone (owner and leaders); every display site -
// the placed banner block, the shield, the grid screen's TEAM BANNER mode,
// the teams page preview - merely mirrors this registry and repaints when
// it announces a change.
//
// A banner is a 256x384 cloth image plus three text lines (top / middle /
// bottom). The image is the COMPOSITED result of whatever the editor did
// (gallery picture, paint strokes, both); the registry neither knows nor
// cares about layers. A team without a stored image flies the default
// crusader emblem (procedural white cloth, red cross) - the default is
// code, never a file, so it can never go missing.
//
// Authority rides the TeamRegistry pattern exactly: clients send an
// identity-free intent, the host stamps the requester from its connection
// table, validates owner/leader rank against the roster, applies, persists
// and rebroadcasts the banner state to everyone. Banner images travel as
// PNG bytes (capped), texts as strings; a join snapshot hands a late joiner
// every live banner right after the roster.
//
// Persistence is host-side, beside teams.json: banners.json carries the
// text/version rows and banner_<teamId>.png carries each image. Clients
// hold a session mirror only and drop it with the session.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VoxelEngine.Networking
{
    [Serializable]
    public class TeamBannerState
    {
        public string teamId = "";
        public int version;
        public string textTop = "";
        public string textMiddle = "";
        public string textBottom = "";
        public bool hasImage;
        [NonSerialized] public byte[] png;   // travels the wire / rests as its own file
    }

    public static class TeamBannerRegistry
    {
        public const int ClothWidth = 256;
        public const int ClothHeight = 384;
        /// <summary>Hard cap on a banner PNG - a hand-painted 256x384 sits far
        /// below this; the cap exists so nobody ships a photograph archive.</summary>
        public const int MaxPngBytes = 300_000;

        /// <summary>Raised with the teamId whose banner changed ("" = all).</summary>
        public static event Action<string> OnBannerChanged;
        /// <summary>Bumped on every change - cheap polling for UI rebuilds.</summary>
        public static int Version { get; private set; }

        private static readonly Dictionary<string, TeamBannerState> _states = new();
        private static readonly Dictionary<string, Texture2D> _textureCache = new();
        private static Texture2D _defaultCloth;
        private static string _loadedForWorld = "";

        // ─────────────────────────────────────────────────────────────────
        //  Queries (every display site goes through these)
        // ─────────────────────────────────────────────────────────────────

        public static TeamBannerState Get(string teamId)
            => !string.IsNullOrEmpty(teamId) && _states.TryGetValue(teamId, out var s) ? s : null;

        /// <summary>The cloth to draw for a team - the stored image, or the
        /// default crusader emblem when the team never set one (or the id is
        /// empty/unknown, e.g. a banner placed by a teamless player).</summary>
        public static Texture2D ClothTexture(string teamId)
        {
            var state = Get(teamId);
            if (state == null || !state.hasImage || state.png == null || state.png.Length == 0)
                return DefaultCloth;

            if (_textureCache.TryGetValue(teamId, out var cached) && cached != null)
                return cached;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(state.png)) { UnityEngine.Object.Destroy(tex); return DefaultCloth; }
            tex.name = "TeamBanner_" + teamId;
            tex.wrapMode = TextureWrapMode.Clamp;
            _textureCache[teamId] = tex;
            return tex;
        }

        public static void TextsOf(string teamId, out string top, out string middle, out string bottom)
        {
            var state = Get(teamId);
            top = state?.textTop ?? "";
            middle = state?.textMiddle ?? "";
            bottom = state?.textBottom ?? "";
        }

        /// <summary>The default crusader emblem: off-white cloth, red cross -
        /// generated once, owned by the registry, never destroyed.</summary>
        public static Texture2D DefaultCloth
        {
            get
            {
                if (_defaultCloth != null) return _defaultCloth;
                _defaultCloth = BuildDefaultCloth();
                return _defaultCloth;
            }
        }

        private static Texture2D BuildDefaultCloth()
        {
            int w = ClothWidth, h = ClothHeight;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "TeamBanner_Default" };
            var cloth = new Color32(242, 238, 228, 255);     // aged white
            var edge = new Color32(214, 207, 190, 255);      // woven border
            var red = new Color32(168, 24, 28, 255);         // crusader red
            var pixels = new Color32[w * h];

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool border = x < 7 || x >= w - 7 || y < 7 || y >= h - 7;
                    pixels[y * w + x] = border ? edge : cloth;
                }

            // The cross. Texture y runs bottom-up; author in top-down terms.
            int barW = 38;
            int vx0 = w / 2 - barW / 2, vx1 = w / 2 + barW / 2;
            int vTop = 34, vBottom = h - 44;                  // from the top edge
            int hyTop = 104, hyBottom = hyTop + barW;         // crossbar in the upper third
            int hx0 = 52, hx1 = w - 52;
            for (int ty = 0; ty < h; ty++)
            {
                int y = h - 1 - ty;                           // ty = distance from top
                for (int x = 0; x < w; x++)
                {
                    bool vertical = x >= vx0 && x < vx1 && ty >= vTop && ty < vBottom;
                    bool horizontal = ty >= hyTop && ty < hyBottom && x >= hx0 && x < hx1;
                    if (vertical || horizontal) pixels[y * w + x] = red;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Intents (UI calls these; identity is never part of the message)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Set MY team's banner. png may be null to fly the default
        /// emblem with texts only. Validation happens host-side; offline and
        /// host machines apply directly.</summary>
        public static void RequestSet(byte[] png, string textTop, string textMiddle, string textBottom)
        {
            if (png != null && png.Length > MaxPngBytes)
            {
                UI.BuildFeedbackHud.Show("Banner", "Image too large - keep it under 300 KB", null, Color.yellow);
                return;
            }
            if (NetworkSession.Mode == SessionMode.Client)
            {
                NetworkBootstrap.Instance?.SendBannerIntent(png, textTop, textMiddle, textBottom);
                return;
            }
            string error = HostApply(NetworkSession.LocalPlayerId, png, textTop, textMiddle, textBottom);
            if (!string.IsNullOrEmpty(error))
                UI.BuildFeedbackHud.Show("Banner", error, null, Color.yellow);
        }

        /// <summary>HOST: validate and apply one banner edit for the
        /// requester's own team. Returns null on success, a reason on refusal.</summary>
        public static string HostApply(string requesterId, byte[] png,
            string textTop, string textMiddle, string textBottom)
        {
            var team = TeamRegistry.TeamOf(requesterId);
            if (team == null) return "You are not in a team.";
            if (!TeamRegistry.IsTeamLeader(team, requesterId))
                return "Only the team's owner or a leader edits the banner.";
            if (png != null && png.Length > MaxPngBytes)
                return "Image too large - keep it under 300 KB.";

            var state = Get(team.teamId) ?? new TeamBannerState { teamId = team.teamId };
            state.textTop = Sanitize(textTop);
            state.textMiddle = Sanitize(textMiddle);
            state.textBottom = Sanitize(textBottom);
            state.hasImage = png != null && png.Length > 0;
            state.png = state.hasImage ? png : null;
            state.version++;
            _states[team.teamId] = state;
            InvalidateTexture(team.teamId);

            Save();
            NetworkBootstrap.Instance?.BroadcastBannerState(state);
            RaiseChanged(team.teamId);
            return null;
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\n", " ").Replace("\r", " ").Trim();
            return text.Length > 24 ? text.Substring(0, 24) : text;
        }

        /// <summary>CLIENT: mirror a host-broadcast banner state.</summary>
        public static void ApplyRemote(string teamId, int version, byte[] png, bool hasImage,
            string textTop, string textMiddle, string textBottom)
        {
            if (string.IsNullOrEmpty(teamId)) return;
            var state = Get(teamId) ?? new TeamBannerState { teamId = teamId };
            if (state.version > version) return;   // stale reorder: ours is newer
            state.version = version;
            state.textTop = textTop ?? "";
            state.textMiddle = textMiddle ?? "";
            state.textBottom = textBottom ?? "";
            state.hasImage = hasImage && png != null && png.Length > 0;
            state.png = state.hasImage ? png : null;
            _states[teamId] = state;
            InvalidateTexture(teamId);
            RaiseChanged(teamId);
        }

        public static IEnumerable<TeamBannerState> All => _states.Values;

        private static void InvalidateTexture(string teamId)
        {
            if (_textureCache.TryGetValue(teamId, out var tex) && tex != null)
                UnityEngine.Object.Destroy(tex);
            _textureCache.Remove(teamId);
        }

        private static void RaiseChanged(string teamId)
        {
            Version++;
            OnBannerChanged?.Invoke(teamId ?? "");
        }

        // ─────────────────────────────────────────────────────────────────
        //  Disk (host-side, beside teams.json)
        // ─────────────────────────────────────────────────────────────────

        [Serializable] private class BannerFile { public List<TeamBannerState> states = new(); }

        private static string FolderFor(string worldName)
        {
            var session = Menu.WorldSession.Instance;
            return session != null
                ? session.WorldFolderPath(worldName)
                : Path.Combine(Application.persistentDataPath, "VoxelWorlds", worldName);
        }

        /// <summary>Host: write beside the world save, same cadence as teams.</summary>
        public static void Save()
        {
            var session = Menu.WorldSession.Instance;
            if (session == null) return;
            if (NetworkSession.Mode == SessionMode.Client) return;   // guests never write
            if (_loadedForWorld != session.worldName)
            {
                Debug.LogWarning($"[TeamBanner] holding banners for '{_loadedForWorld}' but the open world " +
                                 $"is '{session.worldName}' - not writing them anywhere.");
                return;
            }
            try
            {
                string folder = FolderFor(session.worldName);
                Directory.CreateDirectory(folder);

                // Drop banners whose team no longer exists - a dissolved team
                // takes its colours with it.
                var live = new HashSet<string>();
                foreach (var team in TeamRegistry.Teams)
                    if (team != null && !string.IsNullOrEmpty(team.teamId)) live.Add(team.teamId);
                var stale = new List<string>();
                foreach (var id in _states.Keys) if (!live.Contains(id)) stale.Add(id);
                foreach (var id in stale)
                {
                    _states.Remove(id);
                    InvalidateTexture(id);
                    try { File.Delete(Path.Combine(folder, "banner_" + id + ".png")); } catch { }
                }

                var file = new BannerFile();
                foreach (var state in _states.Values)
                {
                    file.states.Add(state);
                    string pngPath = Path.Combine(folder, "banner_" + state.teamId + ".png");
                    if (state.hasImage && state.png != null) File.WriteAllBytes(pngPath, state.png);
                    else { try { File.Delete(pngPath); } catch { } }
                }
                File.WriteAllText(Path.Combine(folder, "banners.json"), JsonUtility.ToJson(file));
            }
            catch (Exception ex) { Debug.LogWarning("[TeamBanner] Save: " + ex.Message); }
        }

        /// <summary>Load the open world's banners (called beside TeamRegistry.Load).</summary>
        public static void Load()
        {
            var session = Menu.WorldSession.Instance;
            if (session == null) return;
            ResetLocal();
            _loadedForWorld = session.worldName;
            try
            {
                string folder = FolderFor(session.worldName);
                string path = Path.Combine(folder, "banners.json");
                if (!File.Exists(path)) return;
                var file = JsonUtility.FromJson<BannerFile>(File.ReadAllText(path));
                if (file?.states == null) return;
                foreach (var state in file.states)
                {
                    if (state == null || string.IsNullOrEmpty(state.teamId)) continue;
                    if (state.hasImage)
                    {
                        string pngPath = Path.Combine(folder, "banner_" + state.teamId + ".png");
                        if (File.Exists(pngPath)) state.png = File.ReadAllBytes(pngPath);
                        else state.hasImage = false;
                    }
                    _states[state.teamId] = state;
                }
                RaiseChanged("");
            }
            catch (Exception ex) { Debug.LogWarning("[TeamBanner] Load: " + ex.Message); }
        }

        /// <summary>Drop every state and cached texture (world change, session end).</summary>
        public static void ResetLocal()
        {
            foreach (var id in new List<string>(_textureCache.Keys)) InvalidateTexture(id);
            _states.Clear();
            _loadedForWorld = "";
            RaiseChanged("");
        }
    }
}
