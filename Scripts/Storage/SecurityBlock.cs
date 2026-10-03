// Assets/Scripts/VoxelEngine/Storage/SecurityBlock.cs
//
// 14.39.0 - Mass-storage Security Block.
//
// A placed block that guards the DIGITAL storage network and nothing else:
// while it is powered (armed), every player access to the data it covers -
// storage/crafting/pattern terminals (wired and wireless), the server rack
// itself and NAS disk shelves - is checked against the owner's access mode:
//
//   PRIVATE - only the owner may open the storage.
//   TEAM    - the owner's team (TeamRegistry) may open it. Default.
//   GLOBAL  - everyone may open it (the block is effectively a status light).
//
// The guard is spatial: a security block protects every rack and NAS within
// guardRadius of itself. Terminals are checked against the position of the
// RACK they are connected to, so a wireless terminal 50 m away is denied just
// the same - the data is guarded, not the doorway.
//
// Deliberate raid mechanic: there is no hacking. An unauthorized player gets
// in by physically DESTROYING the security block, or by cutting its power -
// an unpowered guard stands down. Both are loud, visible base-assault acts.
//
// Ownership follows the Bed pattern (14.38.0): guessed once at Awake on the
// placing machine, overridden explicitly by save restore and remote spawn
// (BlockSync). Access mode and owner ride the factory-runtime seam, so
// MachineSync replicates a mode change live and the save file carries it.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Power;

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

        [Header("Guard")]
        [Tooltip("Every ServerRack and NAS within this radius is protected while the block is powered.")]
        public float guardRadius = 10f;

        [Header("Power")]
        [Tooltip("Constant draw while placed. No power = guard stands down.")]
        public float wattsPerSecond = 40f;

        private bool _explicitOwner;
        private PowerConsumer _power;
        private Renderer _statusLight;
        private MaterialPropertyBlock _mpb;
        private float _tick;
        private int _lastLightState = -1; // -1 unset, 0 dark, 1 armed

        /// <summary>True while the guard is actually enforcing: it has power.</summary>
        public bool IsArmed => _power != null && _power.IsPowered;

        public StorageAccessMode Mode =>
            (StorageAccessMode)Mathf.Clamp(accessMode, 0, 2);

        private void Awake()
        {
            _power = GetComponent<PowerConsumer>();
            if (_power == null)
            {
                _power = gameObject.AddComponent<PowerConsumer>();
                _power.wattsPerSecond = wattsPerSecond;
            }
            else
            {
                // A prefab-authored consumer is the balance knob: a hand-tweaked
                // draw survives (non-destructive rule), this field just mirrors it.
                wattsPerSecond = _power.wattsPerSecond;
            }

            // Owner guess at placement (Bed pattern 14.38.0): never on remote
            // spawn, never after an explicit assignment. Save restore and
            // BlockSync both call SetOwner right after instantiation.
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

        /// <summary>The armed security block that denies this player at a given
        /// data position (rack or NAS), or null when access is allowed. When
        /// several guards overlap, ALL of them must permit - the strictest wins.</summary>
        public static SecurityBlock DenierAt(Vector3 dataPosition, string playerId)
        {
            var guards = FindObjectsByType<SecurityBlock>(FindObjectsSortMode.None);
            foreach (var g in guards)
            {
                if (g == null || !g.isActiveAndEnabled) continue;
                float r = Mathf.Max(0.5f, g.guardRadius);
                if ((g.transform.position - dataPosition).sqrMagnitude > r * r) continue;
                if (!g.Permits(playerId)) return g;
            }
            return null;
        }

        /// <summary>Convenience for terminals: checks against the position of the
        /// rack the terminal is connected to. A terminal with no network is never
        /// denied - there is no data behind it to guard.</summary>
        public static SecurityBlock DenierForRack(ServerRack rack, string playerId)
        {
            if (rack == null) return null;
            return DenierAt(rack.transform.position, playerId);
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
