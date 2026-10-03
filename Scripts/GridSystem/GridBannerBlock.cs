// Assets/Scripts/VoxelEngine/GridSystem/GridBannerBlock.cs
//
// 14.37.0-dev - the banner as a SHIP block: plant your colours on the hull.
// Identical visual to the static BannerDisplay (one BannerCloth, same gold
// frame), identical rule: the team is captured once at placement, the
// cloth repaints live whenever that team edits its banner in TEAMS.
//
// State rides the grid save seam as an additive SavedGridBlock field
// (hasBannerState/bannerTeamId) and reaches other machines through
// MachineSync's grid snapshot like every other grid-block field.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridBannerBlock : GridBlock
    {
        /// <summary>The team whose banner this block flies ("" = default emblem).</summary>
        public string bannerTeamId = "";

        private Combat.BannerCloth _cloth;
        private bool _explicitTeam;

        private void Awake()
        {
            EnsureCloth();

            // Fresh LOCAL placement captures the placer's team; restores and
            // remote spawns call SetTeam right after Instantiate and win.
            if (!_explicitTeam && string.IsNullOrEmpty(bannerTeamId)
                && !Networking.BlockSync.IsApplyingRemote)
            {
                var team = Networking.TeamRegistry.TeamOf(Networking.NetworkSession.LocalPlayerId);
                bannerTeamId = team != null ? team.teamId : "";
            }

            _cloth.Bind(bannerTeamId);
        }

        /// <summary>Authoritative assignment from save restore / network spawn.</summary>
        public void SetTeam(string teamId)
        {
            _explicitTeam = true;
            bannerTeamId = teamId ?? "";
            EnsureCloth();
            _cloth.Bind(bannerTeamId);
        }

        private void EnsureCloth()
        {
            if (_cloth != null) return;
            _cloth = GetComponentInChildren<Combat.BannerCloth>();
            if (_cloth == null)
            {
                var holder = new GameObject("BannerCloth_Runtime");
                holder.transform.SetParent(transform, false);
                // A grid cell is 2.5 m (Large); keep the pole inside it.
                holder.transform.localPosition = new Vector3(0f, -1.1f, 0f);
                _cloth = holder.AddComponent<Combat.BannerCloth>();
                _cloth.poleHeight = 2.1f;
                _cloth.clothWidth = 0.78f;
                _cloth.clothHeight = 1.15f;
            }
        }
    }
}
