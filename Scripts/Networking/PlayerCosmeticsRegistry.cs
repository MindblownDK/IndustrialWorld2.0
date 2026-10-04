// Assets/Scripts/VoxelEngine/Networking/PlayerCosmeticsRegistry.cs
//
// 14.49.0-dev - personal player cosmetics: the chest text and the custom
// icon, edited in the main menu's EDIT PLAYER page and worn by the avatar.
//
// Same shape as TeamBannerRegistry, scoped to a PLAYER instead of a team:
// the server holds one state per stable player id, clients send their own
// cosmetics as an intent right after the identity handshake, the host
// validates (size cap, text sanitation) and rebroadcasts, and late joiners
// are replayed the whole set. Nothing persists on the host - cosmetics
// belong to the PLAYER's machine (a local PNG and a PlayerPrefs line), and
// every join re-uploads them, so they follow the player to every server.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VoxelEngine.Networking
{
    public class PlayerCosmeticsState
    {
        public string playerId = "";
        public string chestText = "";
        public byte[] iconPng;          // null/empty = no icon
        public Texture2D iconTexture;   // decoded lazily, owned by the registry
    }

    public static class PlayerCosmeticsRegistry
    {
        /// <summary>Same ceiling as the team banner - one icon is one small PNG.</summary>
        public const int MaxPngBytes = 300_000;
        public const int MaxChestTextLength = 24;

        /// <summary>Canonical icon size: drawn canvases and gallery imports
        /// are both resampled to this square.</summary>
        public const int IconSize = 128;

        /// <summary>Bumped on every applied change; avatars and panels poll it.</summary>
        public static int Version { get; private set; }

        /// <summary>Fired with the player id that changed ("" = everything).</summary>
        public static event Action<string> OnChanged;

        private static readonly Dictionary<string, PlayerCosmeticsState> _states = new();

        public static PlayerCosmeticsState Get(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            return _states.TryGetValue(playerId, out var s) ? s : null;
        }

        public static IEnumerable<PlayerCosmeticsState> All => _states.Values;

        /// <summary>Decoded icon for a player, or null. The texture is cached
        /// on the state and destroyed when the state changes.</summary>
        public static Texture2D TextureOf(string playerId)
        {
            var state = Get(playerId);
            if (state == null || state.iconPng == null || state.iconPng.Length == 0) return null;
            if (state.iconTexture == null)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                { name = "PlayerIcon_" + playerId, wrapMode = TextureWrapMode.Clamp };
                if (!tex.LoadImage(state.iconPng)) { UnityEngine.Object.Destroy(tex); return null; }
                state.iconTexture = tex;
            }
            return state.iconTexture;
        }

        public static string ChestTextOf(string playerId) => Get(playerId)?.chestText ?? "";

        // ───────────────────── local store (this machine) ─────────────────────

        private const string ChestTextKey = "iw_player_chest_text";

        // 14.51.0 - BOTH local stores are keyed by the identity slot, exactly
        // like the player id itself. Before this, two test instances on one
        // machine shared player_icon.png and the chest-text pref, so editing
        // one player's crest edited every simulated player at once.
        private static string ChestTextStoreKey => ChestTextKey + PlayerIdentity.StoreSlotSuffix;

        public static string LocalChestText
        {
            get => PlayerPrefs.GetString(ChestTextStoreKey, "");
            set
            {
                PlayerPrefs.SetString(ChestTextStoreKey, Sanitize(value));
                PlayerPrefs.Save();
            }
        }

        private static string IconFilePath => Path.Combine(Application.persistentDataPath,
            "player_icon" + PlayerIdentity.StoreSlotSuffix + ".png");

        /// <summary>The folder the player drops PNG/JPG icon sources into.</summary>
        public static string IconsFolder
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "PlayerIcons");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        public static byte[] LoadLocalIconPng()
        {
            try
            {
                if (File.Exists(IconFilePath))
                {
                    var bytes = File.ReadAllBytes(IconFilePath);
                    if (bytes.Length > 0 && bytes.Length <= MaxPngBytes) return bytes;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[Cosmetics] icon load: " + ex.Message); }
            return null;
        }

        /// <summary>Save the player's own cosmetics on this machine. A null
        /// png clears the icon. Called by the EDIT PLAYER page.</summary>
        public static void SaveLocal(byte[] png, string chestText)
        {
            LocalChestText = chestText;
            try
            {
                if (png == null || png.Length == 0) { if (File.Exists(IconFilePath)) File.Delete(IconFilePath); }
                else if (png.Length <= MaxPngBytes) File.WriteAllBytes(IconFilePath, png);
            }
            catch (Exception ex) { Debug.LogWarning("[Cosmetics] icon save: " + ex.Message); }

            // Mid-session edits go out live; in the menu this is a no-op and
            // the next join's upload carries them instead.
            UploadLocal();
        }

        /// <summary>Send this machine's stored cosmetics into the session -
        /// called right after the identity handshake and after local saves.
        /// Sends even when everything is empty: that is how a cleared icon
        /// reaches everyone else.</summary>
        public static void UploadLocal()
        {
            var boot = NetworkBootstrap.Instance;
            if (boot == null || !boot.IsOnline || NetworkSession.IsDedicated) return;
            boot.SendPlayerCosmeticsIntent(LoadLocalIconPng(), LocalChestText);
        }

        // ───────────────────── host / wire ─────────────────────

        /// <summary>Server: validate and adopt one player's cosmetics, then
        /// rebroadcast. Returns null on success, else the refusal.</summary>
        public static string HostApply(string playerId, byte[] png, string chestText)
        {
            if (string.IsNullOrEmpty(playerId)) return "Unknown player.";
            if (png != null && png.Length > MaxPngBytes)
                return "Icon too large - keep it under 300 KB.";

            var state = Get(playerId) ?? new PlayerCosmeticsState { playerId = playerId };
            state.chestText = Sanitize(chestText);
            state.iconPng = (png != null && png.Length > 0) ? png : null;
            if (state.iconTexture != null) { UnityEngine.Object.Destroy(state.iconTexture); state.iconTexture = null; }
            _states[playerId] = state;

            Bump(playerId);
            NetworkBootstrap.Instance?.BroadcastPlayerCosmeticsState(state);
            return null;
        }

        /// <summary>Client: adopt a state the server announced.</summary>
        public static void ApplyRemote(string playerId, byte[] png, string chestText)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            var state = Get(playerId) ?? new PlayerCosmeticsState { playerId = playerId };
            state.chestText = Sanitize(chestText);
            state.iconPng = (png != null && png.Length > 0) ? png : null;
            if (state.iconTexture != null) { UnityEngine.Object.Destroy(state.iconTexture); state.iconTexture = null; }
            _states[playerId] = state;
            Bump(playerId);
        }

        public static void ResetSession()
        {
            foreach (var s in _states.Values)
                if (s.iconTexture != null) UnityEngine.Object.Destroy(s.iconTexture);
            _states.Clear();
            Bump("");
        }

        private static void Bump(string playerId)
        {
            Version++;
            try { OnChanged?.Invoke(playerId); }
            catch (Exception ex) { Debug.LogError("[Cosmetics] OnChanged handler: " + ex.Message); }
        }

        /// <summary>One line, trimmed, hard-capped - the same posture as the
        /// banner text cap, enforced on the server side of the wire.</summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length > MaxChestTextLength) text = text.Substring(0, MaxChestTextLength);
            return text;
        }
    }
}
