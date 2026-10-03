// Assets/Scripts/VoxelEngine/Networking/DedicatedServer.cs
//
// 14.45.0-dev - Milestone 12, part 1: the server stands alone.
//
// Everything "dedicated server" in one place: detection, configuration and
// the headless runner. A dedicated session is a HOST with nobody in the
// chair - SessionMode stays Host, authority code runs exactly as it always
// has, and every connected player is a guest whose record lives in
// PlayerRecords. There is no local player, no camera, no UI.
//
// Detection (first match wins, decided once per process):
//   1. UNITY_SERVER        - the build was made with the Dedicated Server
//                            build target; that build IS the server.
//   2. -server             - any player build launched with this argument
//                            runs headless-in-spirit (window stays, nothing
//                            is played in it); mostly for quick testing.
//   3. IW_DEDICATED=1      - environment variable; the editor test hook
//                            (Tools -> Voxel Engine -> Dedicated Server)
//                            sets it for one play-mode session.
//
// Configuration: server_config.json next to the executable (the project
// root in the editor). Written as a template on first run so an admin has
// something to edit. Command-line overrides: -world, -port, -maxplayers,
// -autosave, -servername, -seed.

using System;
using System.IO;
using UnityEngine;

namespace VoxelEngine.Networking
{
    /// <summary>Everything an admin may configure. JSON on disk, overridable
    /// per launch from the command line.</summary>
    [Serializable]
    public class ServerConfig
    {
        [Tooltip("Shown in logs; purely cosmetic.")]
        public string serverName = "Industrial World Server";

        [Tooltip("Save folder to host. Created on first boot when missing.")]
        public string worldName = "DedicatedWorld";

        [Tooltip("UDP port the transport listens on.")]
        public int port = 7770;

        [Tooltip("Connection cap, clamped 1-64.")]
        public int maxPlayers = 8;

        [Tooltip("World autosave cadence in seconds. 0 disables autosave, -1 keeps the machine's own setting.")]
        public int autosaveSeconds = 300;

        [Tooltip("Seed used ONLY when worldName does not exist yet. 0 = random.")]
        public int newWorldSeed = 0;
    }

    public static class DedicatedServer
    {
        public const string ConfigFileName = "server_config.json";

        private static bool? _active;
        private static ServerConfig _config;

        /// <summary>True when this process is a dedicated server. Decided
        /// once; every headless guard in the game reads this.</summary>
        public static bool IsActive
        {
            get
            {
                if (_active.HasValue) return _active.Value;
                bool active = false;
#if UNITY_SERVER
                active = true;
#endif
                if (!active)
                {
                    var args = System.Environment.GetCommandLineArgs();
                    for (int i = 0; i < args.Length && !active; i++)
                        if (string.Equals(args[i], "-server", StringComparison.OrdinalIgnoreCase))
                            active = true;
                    if (!active && System.Environment.GetEnvironmentVariable("IW_DEDICATED") == "1")
                        active = true;
                }
                _active = active;
                if (active) Debug.Log("[Server] Dedicated server mode is ACTIVE for this process.");
                return active;
            }
        }

        /// <summary>The effective configuration (file + command line), loaded
        /// once per process.</summary>
        public static ServerConfig Config => _config ??= LoadConfig();

        /// <summary>server_config.json lives next to the executable; in the
        /// editor that resolves to the project root.</summary>
        public static string ConfigPath
        {
            get
            {
                var parent = Directory.GetParent(Application.dataPath);
                return Path.Combine(parent != null ? parent.FullName : Application.dataPath, ConfigFileName);
            }
        }

        /// <summary>Writes a template config if none exists. Returns the path
        /// either way (the editor tool shows it to the user).</summary>
        public static string WriteTemplateConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                File.WriteAllText(ConfigPath, JsonUtility.ToJson(new ServerConfig(), prettyPrint: true));
                Debug.Log($"[Server] Wrote template config: {ConfigPath}");
            }
            return ConfigPath;
        }

        private static ServerConfig LoadConfig()
        {
            var cfg = new ServerConfig();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(ConfigPath), cfg);
                    Debug.Log($"[Server] Loaded {ConfigPath}");
                }
                else
                {
                    WriteTemplateConfig();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Server] Could not read {ConfigPath} ({ex.Message}) - using defaults.");
            }

            // Command-line overrides beat the file.
            if (TryArg("-world", out string world)) cfg.worldName = world;
            if (TryArg("-servername", out string name)) cfg.serverName = name;
            if (TryArg("-port", out string port) && int.TryParse(port, out int p)) cfg.port = p;
            if (TryArg("-maxplayers", out string mp) && int.TryParse(mp, out int m)) cfg.maxPlayers = m;
            if (TryArg("-autosave", out string auto) && int.TryParse(auto, out int a)) cfg.autosaveSeconds = a;
            if (TryArg("-seed", out string seed) && int.TryParse(seed, out int s)) cfg.newWorldSeed = s;

            cfg.port = Mathf.Clamp(cfg.port, 1, 65535);
            cfg.maxPlayers = Mathf.Clamp(cfg.maxPlayers, 1, 64);
            if (string.IsNullOrWhiteSpace(cfg.worldName)) cfg.worldName = "DedicatedWorld";
            return cfg;
        }

        private static bool TryArg(string flag, out string value)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) continue;
                value = args[i + 1];
                return !string.IsNullOrWhiteSpace(value);
            }
            value = null;
            return false;
        }

        /// <summary>Creates the persistent runner. Idempotent; the menu's
        /// dedicated launch path calls this before loading the game scene.</summary>
        public static void InstallRunner()
        {
            if (UnityEngine.Object.FindAnyObjectByType<DedicatedServerRunner>() != null) return;
            var go = new GameObject("DedicatedServerRunner");
            go.AddComponent<DedicatedServerRunner>();
        }
    }

    /// <summary>The headless chaperone. Survives the scene load, strips the
    /// client-side scene (local player, cameras, audio) the moment the game
    /// scene finishes Awake, starts the server-only connection once the world
    /// is up, and prints a heartbeat so an admin tailing the log can see the
    /// server is alive.</summary>
    public sealed class DedicatedServerRunner : MonoBehaviour
    {
        private const float HeartbeatSeconds = 60f;

        private bool _stripped;
        private bool _serverStarted;
        private float _nextHeartbeatAt;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            // A headless loop runs as fast as the CPU allows unless told
            // otherwise - cap it so one idle server does not cook a core.
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            var cfg = DedicatedServer.Config;
            if (cfg.autosaveSeconds >= 0)
                VoxelEngine.Settings.GameSettings.AutosaveSeconds = cfg.autosaveSeconds;

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>sceneLoaded fires after every Awake/OnEnable in the new
        /// scene but BEFORE any Start - the exact window where the local
        /// player can be put to sleep without a single frame of simulation.
        /// With the player inactive, persistence restores the world but never
        /// finds an Inventory to move, PlayerSpawner never runs its spawn
        /// search, and the saved local-player block is carried forward
        /// untouched (see WorldStatePersistence.SaveAll).</summary>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                   UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            _stripped = false;   // re-strip whatever the new scene brought
        }

        private void StripClientSideScene()
        {
            var spawner = FindAnyObjectByType<VoxelEngine.Player.PlayerSpawner>();
            if (spawner != null)
            {
                spawner.gameObject.SetActive(false);
                Debug.Log("[Server] Local player object disabled - this machine serves, nobody plays on it.");
            }

            foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
                cam.enabled = false;
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                listener.enabled = false;
        }

        private void Update()
        {
            // Only act inside the game scene - persistence exists only there.
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) return;

            if (!_stripped)
            {
                StripClientSideScene();
                _stripped = true;
            }

            if (!_serverStarted && NetworkBootstrap.Instance != null)
            {
                var cfg = DedicatedServer.Config;
                NetworkBootstrap.Instance.StartDedicated((ushort)cfg.port, cfg.maxPlayers);
                _serverStarted = true;
                Debug.Log($"[Server] '{cfg.serverName}' is hosting world " +
                          $"'{VoxelEngine.Menu.WorldSession.Instance?.worldName}' on UDP {cfg.port} " +
                          $"(max {cfg.maxPlayers} players, autosave " +
                          (VoxelEngine.Settings.GameSettings.AutosaveSeconds > 0
                              ? $"every {VoxelEngine.Settings.GameSettings.AutosaveSeconds}s)."
                              : "disabled)."));
            }

            if (Time.unscaledTime >= _nextHeartbeatAt)
            {
                _nextHeartbeatAt = Time.unscaledTime + HeartbeatSeconds;
                Heartbeat();
            }
        }

        private void Heartbeat()
        {
            int players = 0;
            foreach (var _ in NetworkSession.Players) players++;
            var up = TimeSpan.FromSeconds(Time.realtimeSinceStartup);
            Debug.Log($"[Server] up {(int)up.TotalHours:00}:{up.Minutes:00}:{up.Seconds:00} | " +
                      $"{players} player(s) online | world '{VoxelEngine.Menu.WorldSession.Instance?.worldName}' | " +
                      (NetworkBootstrap.Instance != null && NetworkBootstrap.Instance.IsOnline
                          ? "listening" : "NOT LISTENING"));
        }
    }
}
