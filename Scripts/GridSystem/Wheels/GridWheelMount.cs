// Assets/Scripts/VoxelEngine/GridSystem/Wheels/GridWheelMount.cs
//
// SNAPPING A TIRE ONTO A HUB.
//
// A tire never free-places. While one is held, the builder asks this file for the
// hub socket the player is aiming at; the ghost is then drawn AT that socket, not
// under the crosshair, so what you see is exactly where the tire ends up — the
// single rule that makes snap placement feel trustworthy.
//
// Candidate search is deliberately cheap and deterministic:
//   1. the hub the crosshair is actually on (aim beats proximity, always),
//   2. otherwise the nearest free, compatible socket within a short radius of the
//      aim point, scored by angle to the view ray so two hubs side by side resolve
//      the way the player is looking rather than by raw distance.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    /// <summary>Result of a tire snap query.</summary>
    public struct WheelMountSnap
    {
        public GridWheel Hub;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Valid;
        public string Reason;
    }

    public static class GridWheelMount
    {
        /// <summary>How far from the aim point a hub may be and still catch the ghost.</summary>
        public const float SnapRadius = 6f;

        private static readonly Dictionary<GridBlockItem, GridWheelTire> s_tirePrefabCache = new();
        private static readonly Collider[] s_probe = new Collider[32];
        private static readonly List<GridWheel> s_candidates = new List<GridWheel>(16);

        /// <summary>True when this item places a tire rather than an ordinary block.</summary>
        public static bool IsTireItem(GridBlockItem item) => TirePrefab(item) != null;

        /// <summary>The tire component on an item's prefab, or null if it is not a tire.</summary>
        public static GridWheelTire TirePrefab(GridBlockItem item)
        {
            if (item == null || item.blockPrefab == null) return null;
            if (s_tirePrefabCache.TryGetValue(item, out var cached)) return cached;
            var tire = item.blockPrefab.GetComponent<GridWheelTire>();
            s_tirePrefabCache[item] = tire;
            return tire;
        }

        public static WheelSizeClass TireSize(GridBlockItem item)
        {
            var prefab = TirePrefab(item);
            return prefab != null ? prefab.sizeClass : WheelSizeClass.Size_3x3;
        }

        /// <summary>Finds the socket a held tire should snap to for this aim.</summary>
        public static WheelMountSnap FindSnap(GridBlockItem item, Ray aim, RaycastHit hit, bool hasHit)
        {
            var snap = new WheelMountSnap { Reason = "Aim at a wheel hub" };
            var tirePrefab = TirePrefab(item);
            if (tirePrefab == null) return snap;

            GridWheel best = null;
            float bestScore = float.MaxValue;
            Vector3 aimPoint = hasHit ? hit.point : aim.origin + aim.direction * SnapRadius;

            // 1) Direct aim wins outright.
            if (hasHit && hit.collider != null)
            {
                var direct = hit.collider.GetComponentInParent<GridWheel>();
                if (direct != null) best = direct;
            }

            // 2) Otherwise score nearby hubs by how close they are to the view ray.
            if (best == null)
            {
                s_candidates.Clear();
                int count = Physics.OverlapSphereNonAlloc(aimPoint, SnapRadius, s_probe, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var hub = s_probe[i] != null ? s_probe[i].GetComponentInParent<GridWheel>() : null;
                    if (hub == null || s_candidates.Contains(hub)) continue;
                    s_candidates.Add(hub);
                }

                foreach (var hub in s_candidates)
                {
                    var socket = hub.TireSocket;
                    if (socket == null) continue;
                    Vector3 toSocket = socket.position - aim.origin;
                    float along = Vector3.Dot(toSocket, aim.direction);
                    if (along <= 0f) continue;                       // behind the camera
                    float offAxis = Vector3.Cross(aim.direction, toSocket).magnitude;
                    float score = offAxis + along * 0.05f;           // prefer on-axis, then near
                    if (score < bestScore) { bestScore = score; best = hub; }
                }
            }

            if (best == null) return snap;

            var mount = best.TireSocket;
            if (mount == null) { snap.Reason = "Hub has no mount socket"; return snap; }

            snap.Hub = best;
            snap.Position = mount.position;
            snap.Rotation = mount.rotation;
            snap.Valid = true;

            if (best.HasTire)
            {
                snap.Valid = false;
                snap.Reason = "Hub already has a tire fitted";
            }
            else if (!best.AcceptsTireSize(tirePrefab.sizeClass))
            {
                snap.Valid = false;
                snap.Reason = $"This hub does not accept a {tirePrefab.sizeClass.Label()} tire";
            }
            else if (!best.Enabled)
            {
                snap.Reason = "Hub is switched off — the tire will fit but will not drive";
            }

            return snap;
        }

        /// <summary>Commits a snapped placement. Returns false without consuming the item on failure.</summary>
        public static bool Commit(in WheelMountSnap snap, GridBlockItem item)
        {
            if (!snap.Valid || snap.Hub == null || item == null) return false;
            return snap.Hub.MountTireFromItem(item);
        }

        /// <summary>
        /// Pulls a fitted tire back off a hub and returns it to an inventory. Anything
        /// that cannot be carried is dropped at the socket rather than deleted.
        /// </summary>
        public static bool Eject(GridWheel hub, VoxelEngine.Items.Inventory inventory)
        {
            if (hub == null || !hub.HasTire) return false;
            var item = hub.MountedTireItem;
            var tire = hub.EjectTire();
            if (tire != null) Object.Destroy(tire.gameObject);
            if (item == null) return true;

            var stack = new VoxelEngine.Items.ItemStack(item, 1);
            if (inventory != null && inventory.container != null)
            {
                var leftover = inventory.container.Insert(stack);
                if (leftover == null || leftover.IsEmpty) return true;
                stack = leftover;
            }

            VoxelEngine.Items.DroppedItem.Spawn(stack, hub.transform.position + hub.transform.up * 1.2f, hub.transform.up);
            return true;
        }
    }
}
