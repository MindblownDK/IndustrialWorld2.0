// Assets/Scripts/VoxelEngine/GridSystem/BeaconSettings.cs
//
// 14.30.0-dev - Multiplayer milestone 10: beacon identity and sharing.
//
// A beacon used to be a lamp. This file is what turns it into a STATEMENT:
// who placed it, what they called it, how far it announces itself, and - the
// decided rule - WHO IS ALLOWED TO KNOW IT EXISTS. Three values, chosen on
// the beacon itself: share global (everyone in the session), share team
// (the owner's team, stored from day one and honoured the moment milestone 11
// lands), and do not share (owner only). The default is do-not-share, so
// nothing a player builds leaks to the session until they say so.
//
// Two blocks carry this identity - the grid-mounted Beacon and the stationary
// radar tower - and both expose it through one interface so the save system,
// the sync layer and the HUD never care which one they are talking to.
//
// Ownership is keyed by STABLE PLAYER ID, never by name or connection
// (MP-readiness checklist): a beacon survives its owner renaming themselves,
// reconnecting, and reloading the world.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    /// <summary>Who may see a beacon's marker. The numeric values are the wire
    /// and save format - never reorder them.</summary>
    public enum BeaconShare : byte
    {
        /// <summary>Owner only. The default: nothing leaks until the player says so.</summary>
        Private = 0,
        /// <summary>The owner's team. Stored from day one; until teams exist
        /// (milestone 11) it behaves as Private for everyone except the owner,
        /// and the setting is never silently rewritten.</summary>
        Team = 1,
        /// <summary>Everyone in the session.</summary>
        Global = 2,
    }

    /// <summary>A block that is a beacon: it has an identity, an owner, a share
    /// rule, and a position worth marking. The grid Beacon and the stationary
    /// radar tower both implement this, so persistence, sync and the HUD talk
    /// to one shape.</summary>
    public interface IBeaconSource
    {
        /// <summary>Stable id minted at placement. Keys HUD marker reuse and
        /// survives save/load and the wire.</summary>
        string BeaconId { get; }

        /// <summary>Stable player id of whoever placed it. Empty only in the
        /// brief window before a remote placement's identity arrives; an
        /// unowned beacon is visible to nobody, which fails safe.</summary>
        string BeaconOwnerId { get; }

        /// <summary>The name the owner typed. What the marker shows.</summary>
        string BeaconName { get; set; }

        /// <summary>Who may see the marker.</summary>
        BeaconShare BeaconShareMode { get; set; }

        /// <summary>Marker range in metres - how far away the marker is still
        /// shown. 0 means unlimited, which is the default.</summary>
        float BeaconRangeM { get; set; }

        /// <summary>Whether the beacon is actually broadcasting right now. An
        /// unlit beacon emits no marker, paints no map contact and offers no
        /// warp rendezvous - same rule for everyone, owner included.</summary>
        bool BeaconLit { get; }

        /// <summary>Where the marker points, scene metres, live.</summary>
        Vector3 BeaconWorldPosition { get; }

        /// <summary>Save/wire restore: overwrite identity from a persisted
        /// payload. Id and owner only land when non-empty so a stale empty
        /// record can never erase a freshly minted identity.</summary>
        void RestoreBeaconIdentity(string id, string ownerId, string name, int share, float range);
    }

    /// <summary>Every live beacon in the scene, and the one visibility rule.
    /// The host filters the marker channel through <see cref="VisibleTo"/>, so
    /// a client is never told about a beacon it has no right to see - hiding a
    /// marker is not what keeps it secret.</summary>
    public static class BeaconRoster
    {
        private static readonly List<IBeaconSource> s_all = new();

        /// <summary>Live beacons of every kind, registration order.</summary>
        public static IReadOnlyList<IBeaconSource> All => s_all;

        public static void Register(IBeaconSource beacon)
        {
            if (beacon != null && !s_all.Contains(beacon)) s_all.Add(beacon);
        }

        public static void Unregister(IBeaconSource beacon)
        {
            s_all.Remove(beacon);
        }

        /// <summary>Mint a fresh beacon id. "N" format: compact, no braces on the wire.</summary>
        public static string MintId() => System.Guid.NewGuid().ToString("N");

        /// <summary>THE visibility rule. Global shares with everyone; Team is
        /// stored but behaves as Private until milestone 11 delivers teams;
        /// Private is owner only. An unlit beacon is visible to nobody, and so
        /// is an unowned one - both fail closed.</summary>
        public static bool VisibleTo(IBeaconSource beacon, string viewerId)
        {
            if (beacon == null || !beacon.BeaconLit) return false;
            if (beacon.BeaconShareMode == BeaconShare.Global) return true;
            // Team == Private until teams exist. The stored value is honoured
            // the moment milestone 11 lands; it is never rewritten here.
            return !string.IsNullOrEmpty(beacon.BeaconOwnerId)
                && !string.IsNullOrEmpty(viewerId)
                && beacon.BeaconOwnerId == viewerId;
        }

        /// <summary>Visibility for the machine we are running on.</summary>
        public static bool VisibleToLocal(IBeaconSource beacon)
            => VisibleTo(beacon, Networking.NetworkSession.LocalPlayerId);
    }
}
