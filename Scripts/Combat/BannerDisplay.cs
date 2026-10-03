// Assets/Scripts/VoxelEngine/Combat/BannerDisplay.cs
//
// 14.37.0-dev - the placeable STATIC banner block. Plant it and it flies
// the placer's team banner; the banner is edited in the TEAMS page, never
// here, and every planted banner of that team repaints live on edit
// (BannerCloth subscribes to the registry).
//
// The team is captured ONCE at placement - the moment the pole goes into
// the ground, it is that team's banner. It travels to other machines in
// the BlockSnapshot and into the save file as an additive field, exactly
// like a conveyor's shape.

using UnityEngine;

namespace VoxelEngine.Combat
{
    public class BannerDisplay : MonoBehaviour
    {
        /// <summary>The team whose banner this pole flies. Empty = the
        /// default crusader emblem (a teamless player's banner).</summary>
        public string bannerTeamId = "";

        private BannerCloth _cloth;
        private bool _explicitTeam;

        private void Awake()
        {
            _cloth = GetComponentInChildren<BannerCloth>();
            if (_cloth == null)
            {
                var holder = new GameObject("BannerCloth_Runtime");
                holder.transform.SetParent(transform, false);
                _cloth = holder.AddComponent<BannerCloth>();
            }

            // Fresh LOCAL placement: capture the placer's team right now,
            // before the block is announced to the network. A remote spawn
            // or a save restore overrides this through SetTeam immediately
            // after Instantiate, so a wrong local guess never survives.
            if (!_explicitTeam && string.IsNullOrEmpty(bannerTeamId)
                && !Networking.BlockSync.IsApplyingRemote)
            {
                var team = Networking.TeamRegistry.TeamOf(Networking.NetworkSession.LocalPlayerId);
                bannerTeamId = team != null ? team.teamId : "";
            }

            _cloth.Bind(bannerTeamId);
        }

        /// <summary>Authoritative team assignment from the network spawn or
        /// the save restore - always wins over the Awake guess.</summary>
        public void SetTeam(string teamId)
        {
            _explicitTeam = true;
            bannerTeamId = teamId ?? "";
            if (_cloth != null) _cloth.Bind(bannerTeamId);
        }
    }
}
