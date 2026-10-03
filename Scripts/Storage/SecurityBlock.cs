// Assets/Scripts/VoxelEngine/Storage/SecurityBlock.cs
//
// 14.39.0 - Mass-storage Security Block. 14.40.0 - network membership.
//
// A placed block that guards the DIGITAL storage network and nothing else.
// Since 14.40.0 the guard is a NETWORK DEVICE like everything else: pipe it
// in or place it touching the system, and it guards that whole network -
// every terminal (wired or handheld-wireless), the Server Controller and
// every NAS shelf on it. No radius, no ambiguity: one network, one lock.
//
//   PRIVATE - only the owner may open the storage.
//   TEAM    - the owner's team (TeamRegistry) may open it. Default.
//   GLOBAL  - everyone may open it.
//
// Handheld WIRELESS access is stricter and never global: the owner always,
// the owner's team only when the owner ticks the share box.
//
// The guard draws its 40 W from the SYSTEM power budget (Power Stations).
// Deliberate raid mechanic unchanged: there is no hacking - destroy the
// block, or cut the system's power. Both are loud, visible base-assault
// acts. An unpowered or unowned guard fails open.
//
// Ownership follows the Bed pattern (14.38.0): guessed once at Awake on the
// placing machine, overridden explicitly by save restore and remote spawn.
// Owner, access mode and the wireless share flag ride the factory-runtime
// seam, so MachineSync replicates changes live and the save carries them.

using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Storage
{
    /// <summary>Who may open the guarded storage network.</summary>
    public enum StorageAccessMode
    {
        Private = 0,
        Team    = 1,
        Global  = 2
    }

    [RequireComponent(typeof(PlacedBlock))]
    public class SecurityBlock : MonoBehaviour
    {
        [Header("Ownership")]
        [Tooltip("Stable player id of the owner. Stamped at placement; save restore and remote spawn override it explicitly.")]
        public string ownerId = "";

        [Tooltip("0 = Private, 1 = Team, 2 = Global. New blocks default to Team.")]
        public int accessMode = (int)StorageAccessMode.Team;

        [Tooltip("Owner-set: may the owner's TEAM use handheld wireless access? Wireless is never global.")]
        public bool wirelessTeamShare;

        [Header("Power")]
        [Tooltip("System watts this guard adds to the network draw.")]
        public float wattsPerSecond = 40f;

        private bool _explicitOwner;
        private Renderer _statusLight;
        private MaterialPropertyBlock _mpb;
        private float _tick;
        private int _lastLightState = -1; // -1 unset, 0 dark, 1 armed

        /// <summary>System watts this guard draws (read by the controller).</summary>
        public float DrawWatts => wattsPerSecond;

        /// <summary>The network this guard protects (null = not connected).</summary>
        public ServerRack Controller => StorageNetwork.ControllerOf(this);

        /// <summary>True while the guard is actually enforcing: it sits on a
        /// network whose system power is up. Cut the Power Stations and the
        /// guard stands down - that is the raid path.</summary>
        public bool IsArmed
        {
            get
            {
                var c = Controller;
                return c != null && c.IsOnline;
            }
        }

        public StorageAccessMode Mode =>
            (StorageAccessMode)Mathf.Clamp(accessMode, 0, 2);

        private void Awake()
        {
            // Legacy prefabs carried a PowerConsumer (own grid draw, 14.39.0).
            // The guard is system-powered now - neutralize it.
            var legacyPower = GetComponent<VoxelEngine.Power.PowerConsumer>();
            if (legacyPower != null) legacyPower.wattsPerSecond = 0f;

            // Owner guess at placement (Bed pattern 14.38.0): never on remote
            // spawn, never after an explicit assignment.
            if (!_explicitOwner && string.IsNullOrEmpty(ownerId)
                && !Networking.BlockSync.IsApplyingRemote)
            {
                ownerId = Networking.NetworkSession.LocalPlayerId ?? "";
            }

            _statusLight = FindStatusLight();
            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>Explicit ownership assignment (save restore, remote spawn,
        /// machine-runtime apply). Always wins over the Awake guess.</summary>
        public void SetOwner(string playerId)
        {
            _explicitOwner = true;
            ownerId = playerId ?? "";
        }

        public void SetMode(int mode)
        {
            accessMode = Mathf.Clamp(mode, 0, 2);
        }

        public void SetWirelessTeamShare(bool share)
        {
            wirelessTeamShare = share;
        }

        /// <summary>May this player open storage guarded by this block?
        /// Unpowered or unowned (legacy) guards fail open.</summary>
        public bool Permits(string playerId)
        {
            if (!IsArmed) return true;
            if (string.IsNullOrEmpty(ownerId)) return true;
            string id = playerId ?? "";
            if (id == ownerId) return true;
            switch (Mode)
            {
                case StorageAccessMode.Global:  return true;
                case StorageAccessMode.Team:    return Networking.TeamRegistry.SameTeam(ownerId, id);
                default:                        return false;
            }
        }

        /// <summary>May this player use handheld WIRELESS access through this
        /// guard's network? Stricter than Permits and never global: the owner
        /// always, the team only when the owner ticked the share box.</summary>
        public bool PermitsWireless(string playerId)
        {
            if (!IsArmed) return true;
            if (string.IsNullOrEmpty(ownerId)) return true;
            string id = playerId ?? "";
            if (id == ownerId) return true;
            return wirelessTeamShare && Networking.TeamRegistry.SameTeam(ownerId, id);
        }

        public string ModeLabel()
        {
            switch (Mode)
            {
                case StorageAccessMode.Private: return "PRIVATE";
                case StorageAccessMode.Team:    return "TEAM";
                default:                        return "GLOBAL";
            }
        }

        // ─────────────────────── static access checks ───────────────────────

        private static readonly System.Collections.Generic.List<SecurityBlock> _guardBuf = new();

        /// <summary>The armed security block on this controller's network that
        /// denies the player, or null when access is allowed. When several
        /// guards sit on one network, ALL must permit - the strictest wins.</summary>
        public static SecurityBlock DenierForRack(ServerRack rack, string playerId)
        {
            if (rack == null) return null;
            StorageNetwork.MembersOf(rack, _guardBuf);
            foreach (var g in _guardBuf)
                if (g != null && !g.Permits(playerId)) return g;
            return null;
        }

        /// <summary>Wireless twin of DenierForRack: the guard that refuses
        /// handheld access, or null when wireless is allowed.</summary>
        public static SecurityBlock WirelessDenierForRack(ServerRack rack, string playerId)
        {
            if (rack == null) return null;
            StorageNetwork.MembersOf(rack, _guardBuf);
            foreach (var g in _guardBuf)
                if (g != null && !g.PermitsWireless(playerId)) return g;
            return null;
        }

        /// <summary>Orange refusal toast, shared by every enforcement site.</summary>
        public static void ShowDeniedToast(SecurityBlock denier)
        {
            string mode = denier != null ? denier.ModeLabel() : "PRIVATE";
            UI.BuildFeedbackHud.Show("Storage Secured",
                $"A security block guards this network ({mode}). Break it or cut its power to get in.",
                null, new Color(1f, 0.62f, 0.18f));
        }

        // ─────────────────────── status light ───────────────────────

        private void Update()
        {
            _tick += Time.deltaTime;
            if (_tick < 0.5f) return;
            _tick = 0f;
            UpdateStatusLight();
        }

        private void UpdateStatusLight()
        {
            if (_statusLight == null)
            {
                _statusLight = FindStatusLight();
                if (_statusLight == null) return;
            }
            int state = IsArmed ? 1 : 0;
            if (state == _lastLightState) return;
            _lastLightState = state;

            Color c = state == 1
                ? new Color(0.95f, 0.16f, 0.12f)   // armed - red
                : new Color(0.16f, 0.17f, 0.19f);  // no power - dark
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _statusLight.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            if (state == 1) _mpb.SetColor("_EmissionColor", c * 2.2f);
            else _mpb.SetColor("_EmissionColor", Color.black);
            _statusLight.SetPropertyBlock(_mpb);
        }

        private Renderer FindStatusLight()
        {
            var t = transform.Find("Generated_StatusLight");
            return t != null ? t.GetComponent<Renderer>() : null;
        }
    }
}
