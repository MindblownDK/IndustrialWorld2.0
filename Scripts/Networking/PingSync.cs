// Assets/Scripts/VoxelEngine/Networking/PingSync.cs
//
// 18.16.0-dev - team pings.
//
// A ping is a shout, not a waypoint. Beacons (14.30.0) are persistent markers a
// player owns and can leave behind; a ping is a brief gesture that says "THERE,
// look there" and then goes away on its own. That difference decides
// the design: a beacon rides a 2.5 s sweep because it lives for hours, a ping
// travels the moment it is made and expires on a local timer, so it never
// costs a sweep and never needs a removal message.
//
// Who may see one follows the same rule the beacons already use - sharing is
// enforced where the ping is SENT, not where it is drawn:
//
//   - The PINGER always sees their own ping. It is added locally the instant
//     they press the key, so the feedback is as fast as the press itself.
//   - In a session the HOST decides who else hears the shout. On receiving a
//     request it stamps the sender from its connection table (identity in the
//     message is advisory at most), rejects a position further than MaxRangeM
//     from that sender's own avatar, and forwards it only to connections whose
//     player id stands on the same team as the pinger. Nobody else is told.
//   - Offline there is nobody else to ask, so the local marker stands alone;
//     on a guest the team rule has already been applied before the message
//     arrives, so single player, listen server and guest all draw from one
//     answer without any of them re-deciding it.
//
// A ping carries no authority of any kind. It cannot move a player, unlock a
// door or touch a machine - the worst a modified client can do is make its team
// look at a wrong piece of sky. That is why the position is checked only for
// gross range and never re-simulated.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Networking
{
    /// <summary>One ping as it travels on the wire. Primitives and a Vector3
    /// only, so FishNet's generic writer carries it without a custom serializer.</summary>
    public struct PingRecord
    {
        public string Id;          // unique per ping, stable for the HUD's keying
        public string OwnerId;     // stable player id, never a connection
        public string OwnerName;   // shown on the marker
        public Vector3 Position;   // scene metres, like every pose on the wire
        public float LifetimeS;    // seconds the marker stays up, clamped on arrival
    }

    /// <summary>One ping this machine is currently drawing: the wire record
    /// plus the moment it arrived here, which is what its life is measured from.</summary>
    public sealed class LivePing
    {
        public PingRecord Record;
        public float SpawnTime;
        public bool IsMine;

        public float Age => Mathf.Max(0f, Time.unscaledTime - SpawnTime);
        public float RemainingS => Record.LifetimeS - Age;
        /// <summary>1 at birth, 0 at expiry - the HUD fades on the tail of it.</summary>
        public float Life01 => Mathf.Clamp01(RemainingS / Mathf.Max(0.01f, Record.LifetimeS));
    }

    public static class PingSync
    {
        // ── tuning ────────────────────────────────────────────────────────

        /// <summary>Long enough to walk a few steps toward it and look, short
        /// enough that the screen is never a museum of old shouts.</summary>
        public const float DefaultLifetimeS = 8f;
        public const float LifetimeMinS = 2f;
        public const float LifetimeMaxS = 30f;

        /// <summary>One ping per player per this many seconds. Spam is the only
        /// abuse a ping allows, and this is the whole answer to it.</summary>
        public const float RateLimitS = 1.5f;

        /// <summary>Keep the marker layer tidy: the oldest shout is dropped when
        /// a ninth arrives, so the screen never fills up in a firefight.</summary>
        public const int MaxLive = 8;

        /// <summary>How far from their own avatar a player may place a ping. The
        /// client resolves the aim point locally; the host refuses anything that
        /// claims a target across the map, which is the only cheat worth closing.</summary>
        public const float MaxRangeM = 300f;

        // ── state ─────────────────────────────────────────────────────────

        private static readonly List<LivePing> _live = new();
        private static float _lastLocalAt = -999f;
        private static int _localSeq;

        /// <summary>Every ping this machine is drawing, oldest first. Expiry is
        /// applied here, so a caller never has to sweep on its own.</summary>
        public static IReadOnlyList<LivePing> Current
        {
            get { Prune(); return _live; }
        }

        public static int Count { get { Prune(); return _live.Count; } }

        public static bool RateLimited => Time.unscaledTime - _lastLocalAt < RateLimitS;

        public static float RateLimitRemainingS
            => Mathf.Max(0f, RateLimitS - (Time.unscaledTime - _lastLocalAt));

        // ── making a ping ─────────────────────────────────────────────────

        /// <summary>Ping a world position. Called from player code with the aim
        /// point it resolved; returns false when the rate limit swallows it.
        /// The local marker goes up first and unconditionally - you always see
        /// your own ping, whatever the host later decides about the others.</summary>
        public static bool Ping(Vector3 worldPosition)
        {
            if (RateLimited) return false;
            _lastLocalAt = Time.unscaledTime;

            string ownerId = NetworkSession.LocalPlayerId ?? "";
            string ownerName = PlayerIdentity.LocalName;
            string id = (string.IsNullOrEmpty(ownerId) ? "local" : ownerId)
                        + "#" + Time.frameCount + "#" + (++_localSeq);

            var record = new PingRecord
            {
                Id = id,
                OwnerId = ownerId,
                OwnerName = string.IsNullOrEmpty(ownerName) ? "You" : ownerName,
                Position = worldPosition,
                LifetimeS = DefaultLifetimeS
            };

            Add(new LivePing { Record = record, SpawnTime = Time.unscaledTime, IsMine = true });

            // A guest asks the host to tell the team; the host (and single
            // player) is the authority and forwards it itself.
            if (NetworkSession.Mode == SessionMode.Client)
                NetworkBootstrap.Instance?.SendPingRequest(record);
            else
                NetworkBootstrap.Instance?.HostRelayPing(record);

            return true;
        }

        /// <summary>Client: a teammate's ping arrived from the host. The host has
        /// already applied the team rule, so this machine draws it and asks no
        /// further questions - and it never re-adds our own echo.</summary>
        public static void ApplyRemote(PingRecord record)
        {
            if (string.IsNullOrEmpty(record.Id)) return;
            if (record.LifetimeS <= 0.01f) record.LifetimeS = DefaultLifetimeS;
            record.LifetimeS = Mathf.Clamp(record.LifetimeS, LifetimeMinS, LifetimeMaxS);

            string localId = NetworkSession.LocalPlayerId ?? "";
            bool mine = !string.IsNullOrEmpty(record.OwnerId) && record.OwnerId == localId;

            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i].Record.Id != record.Id) continue;
                if (mine) return;                       // our own ping is already up
                _live.RemoveAt(i);                      // duplicate delivery: replace
                break;
            }

            Add(new LivePing { Record = record, SpawnTime = Time.unscaledTime, IsMine = mine });

            // A shout you are looking away from is invisible. One chat line makes
            // it audible without turning the ping into a notification system -
            // our own ping says nothing, because we just watched it appear.
            if (!mine)
                VoxelEngine.UI.ChatOverlay.AddMessage(
                    string.IsNullOrEmpty(record.OwnerName) ? "Teammate" : record.OwnerName,
                    "Pinged a location");
        }

        /// <summary>Session over, or the world is going away: a stale shout must
        /// not survive into the next world.</summary>
        public static void Clear()
        {
            _live.Clear();
            _localSeq = 0;
            _lastLocalAt = -999f;
        }

        // ── internals ─────────────────────────────────────────────────────

        private static void Add(LivePing ping)
        {
            _live.Add(ping);
            Prune();
            while (_live.Count > MaxLive) _live.RemoveAt(0);   // oldest shout goes first
        }

        private static void Prune()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
                if (_live[i].RemainingS <= 0f) _live.RemoveAt(i);
        }
    }
}
