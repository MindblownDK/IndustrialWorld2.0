// Assets/Scripts/VoxelEngine/Networking/BeaconSync.cs
//
// 14.30.0-dev - Multiplayer milestone 10: the beacon marker channel.
//
// The decided rule is "sharing is enforced where the beacon is SENT, not
// where it is drawn": a client is never told about a marker it has no right
// to see, so hiding a marker is not what keeps it secret. That rules out the
// existing broadcast seams - hull records and machine state go to everyone -
// and gives markers their own channel, filtered PER RECIPIENT on the host:
//
//   - The HOST walks the live beacon roster every few seconds and sends each
//     client only the markers that client's player id may see (global ones
//     plus their own; share-team behaves as do-not-share until milestone 11).
//     A per-connection signature skips the send when nothing changed, and a
//     beacon that goes dark or private disappears with the next sweep because
//     the sweep always sends the full visible set.
//   - A CLIENT renders what it was sent and nothing else. The HUD never looks
//     at the local roster in client mode - remote hulls DO carry beacon blocks
//     as world geometry, but their markers only exist if the host said so.
//   - HOST and OFFLINE build their own list locally through the same filter,
//     so single player, listen server and guest all draw from one rule.
//
// Positions travel in scene metres like every pose on the wire; ship-mounted
// beacons therefore update with each sweep and the HUD smooths between them.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Networking
{
    /// <summary>One visible beacon marker on the wire.</summary>
    public struct BeaconMarkerRecord
    {
        public string Id;
        public string Name;
        public string OwnerId;
        public Vector3 Position;
        public float RangeM;   // 0 = unlimited
    }

    public static class BeaconSync
    {
        // What the host last told THIS machine it may see (client mode only).
        private static readonly List<BeaconMarkerRecord> _remote = new();

        /// <summary>Every marker the given player may see, built from the live
        /// roster. The host calls this once per recipient per sweep; host and
        /// offline HUDs call it for the local player.</summary>
        public static List<BeaconMarkerRecord> VisibleTo(string viewerId)
        {
            var result = new List<BeaconMarkerRecord>();
            var all = BeaconRoster.All;
            for (int i = 0; i < all.Count; i++)
            {
                var beacon = all[i];
                if (beacon == null || !BeaconRoster.VisibleTo(beacon, viewerId)) continue;
                result.Add(new BeaconMarkerRecord
                {
                    Id = beacon.BeaconId ?? "",
                    Name = beacon.BeaconName ?? "",
                    OwnerId = beacon.BeaconOwnerId ?? "",
                    Position = beacon.BeaconWorldPosition,
                    RangeM = beacon.BeaconRangeM
                });
            }
            return result;
        }

        /// <summary>Client: the host's filtered sweep arrived. The list is the
        /// complete visible set - replace, never merge, so revoked markers die.</summary>
        public static void ApplyMarkers(List<BeaconMarkerRecord> markers)
        {
            _remote.Clear();
            if (markers != null) _remote.AddRange(markers);
        }

        /// <summary>What the local HUD may draw right now. One entry point for
        /// all three modes, so the HUD never re-implements the visibility rule.</summary>
        public static List<BeaconMarkerRecord> CurrentMarkers()
        {
            if (NetworkSession.Mode == SessionMode.Client)
                return _remote;
            return VisibleTo(NetworkSession.LocalPlayerId);
        }

        /// <summary>Session over - a stale marker list must not survive into
        /// the next world.</summary>
        public static void Clear() => _remote.Clear();
    }

    /// <summary>Host-side sweep timer. Every sweep asks NetworkBootstrap to
    /// send each connected guest its own filtered marker set; the bootstrap
    /// owns the connection-to-player-id map, so the filtering lives there.</summary>
    public sealed class BeaconSyncManager : MonoBehaviour
    {
        private const float SweepSeconds = 2.5f;
        private float _nextSweep;

        private void Update()
        {
            if (NetworkSession.Mode != SessionMode.Host) return;
            if (Time.unscaledTime < _nextSweep) return;
            _nextSweep = Time.unscaledTime + SweepSeconds;
            NetworkBootstrap.Instance?.BroadcastBeaconMarkers();
        }
    }
}
