// Assets/Scripts/VoxelEngine/Combat/DefenseLogistics.cs
//
// Shared factory-logistics helpers for automated defense. Belts, chutes, funnels,
// and item pipes refill turret magazines (and the Auto Turret's bullet counter)
// without the player dragging ammo by hand. AcceptFilter on each magazine remains
// the single source of truth for which ammo types are valid.

using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;

namespace VoxelEngine.Combat
{
    /// <summary>
    /// Static helpers used by every placeable defense piece that implements
    /// <see cref="IItemConsumer"/> / <see cref="IDirectItemPortEndpoint"/>.
    /// </summary>
    public static class DefenseLogistics
    {
        /// <summary>How many units of <paramref name="item"/> still fit in <paramref name="mag"/>.</summary>
        public static int GetMagazineCapacity(ItemContainer mag, ItemDefinition item)
        {
            if (mag == null || item == null) return 0;
            mag.EnsureValid();

            // Honour AcceptFilter (shells / fuel / cells / AA rounds, etc.).
            if (mag.AcceptFilter != null && mag.AcceptFilter(item, 1) <= 0) return 0;

            int maxStack = ItemStack.MaxItemsPerStack(item);
            if (maxStack <= 0) maxStack = 1;

            int free = 0;
            for (int i = 0; i < mag.Slots.Count; i++)
            {
                var s = mag.GetSlot(i);
                if (s == null || s.IsEmpty) free += maxStack;
                else if (s.item == item) free += Mathf.Max(0, maxStack - s.count);
            }

            if (mag.AcceptFilter != null)
                free = Mathf.Min(free, Mathf.Max(0, mag.AcceptFilter(item, free)));

            return Mathf.Max(0, free);
        }

        /// <summary>Insert up to <paramref name="count"/> into the magazine. Returns accepted count.</summary>
        public static int InsertIntoMagazine(ItemContainer mag, ItemDefinition item, int count)
        {
            if (mag == null || item == null || count <= 0) return 0;
            var leftover = mag.Insert(new ItemStack(item, count));
            return count - (leftover?.count ?? 0);
        }

        /// <summary>Capacity for the Auto Turret's integer bullet counter.</summary>
        public static int GetBulletCapacity(int ammo, int maxAmmo, ItemDefinition item)
        {
            if (item == null || item.itemId != "item_bullets") return 0;
            return Mathf.Max(0, maxAmmo - ammo);
        }

        /// <summary>Load bullets into the Auto Turret counter. Returns accepted count.</summary>
        public static int InsertBullets(ref int ammo, int maxAmmo, ItemDefinition item, int count)
        {
            int cap = GetBulletCapacity(ammo, maxAmmo, item);
            if (cap <= 0 || count <= 0) return 0;
            int got = Mathf.Min(cap, count);
            ammo += got;
            return got;
        }
    }

    /// <summary>
    /// Mixin-style logistics surface every magazine-based defense piece shares.
    /// Implementors expose their magazine; the default methods handle belts + pipes.
    /// </summary>
    public interface IDefenseAmmoSink : IItemConsumer, IDirectItemPortEndpoint, IInventoryInterface
    {
        ItemContainer DefenseMagazine { get; }
    }

    /// <summary>Extension-style default implementations via static helpers (C# no default interface bodies on older Unity — call these).</summary>
    public static class DefenseAmmoSinkUtil
    {
        public static int GetInputCapacity(IDefenseAmmoSink sink, ItemDefinition item)
            => DefenseLogistics.GetMagazineCapacity(sink?.DefenseMagazine, item);

        public static int TryInsert(IDefenseAmmoSink sink, ItemDefinition item, int count)
            => DefenseLogistics.InsertIntoMagazine(sink?.DefenseMagazine, item, count);

        public static bool IsFaceConnectable(Vector3 _) => true;

        public static int TryAcceptFromPipe(IDefenseAmmoSink sink, Vector3 _, ItemDefinition item, int count)
            => TryInsert(sink, item, count);

        public static ItemContainer GetInputContainer(IDefenseAmmoSink sink) => sink?.DefenseMagazine;
        public static ItemContainer GetOutputContainer() => null;
        public static bool HasOutputReady => false;
        public static bool CanAcceptInput(IDefenseAmmoSink sink)
            => sink?.DefenseMagazine != null;
    }

    /// <summary>
    /// Conserve-ammo / reserve-stock policy shared by every automated defense piece.
    /// When conserve is on, auto-fire stops once stock is at or below the reserve.
    /// Manual cockpit fire (artillery) intentionally ignores the reserve.
    /// </summary>
    public interface IDefenseFirePolicy
    {
        bool ConserveAmmo { get; set; }
        int ReserveStock { get; set; }
        /// <summary>Current usable stock units (magazine count, bullets, or fuel cans).</summary>
        int CurrentStock { get; }
    }

    public static class DefenseFirePolicy
    {
        public const int DefaultReserve = 0;
        public const int MaxReserveClamp = 50;

        /// <summary>
        /// True when auto-fire is allowed to spend another unit.
        /// Manual fire should call with respectReserve: false.
        /// </summary>
        public static bool CanAutoSpend(IDefenseFirePolicy p)
        {
            if (p == null) return true;
            if (!p.ConserveAmmo) return p.CurrentStock > 0;
            return p.CurrentStock > Mathf.Max(0, p.ReserveStock);
        }

        public static int ClampReserve(int v) => Mathf.Clamp(v, 0, MaxReserveClamp);

        public static string Describe(IDefenseFirePolicy p)
        {
            if (p == null) return "";
            if (!p.ConserveAmmo) return "Reserve off";
            return $"Reserve {Mathf.Max(0, p.ReserveStock)}";
        }
    }

    /// <summary>
    /// Engagement range + horizontal firing arc for automated defenses.
    /// Arc is centred on the block's placed forward (transform.forward), projected
    /// onto the local tangent plane so it works on spherical worlds.
    /// </summary>
    public interface IDefenseEngagement
    {
        /// <summary>Hard maximum range the weapon can physically reach.</summary>
        float MaxRange { get; }
        /// <summary>Player-configured engagement range (clamped to MaxRange).</summary>
        float EngagementRange { get; set; }
        /// <summary>Full cone width in degrees (360 = omnidirectional). Clamped 15–360.</summary>
        float FiringArcDegrees { get; set; }
        Transform transform { get; }
    }

    public static class DefenseEngagement
    {
        public const float MinArc = 15f;
        public const float MaxArc = 360f;
        public const float MinEngage = 2f;

        public static float ClampRange(float value, float maxRange)
            => Mathf.Clamp(value, MinEngage, Mathf.Max(MinEngage, maxRange));

        public static float ClampArc(float value)
            => Mathf.Clamp(value, MinArc, MaxArc);

        /// <summary>
        /// True if <paramref name="targetPos"/> is within engagement range AND inside
        /// the horizontal firing arc. Arc 360° disables the angle check.
        /// </summary>
        public static bool IsInEngagement(IDefenseEngagement e, Vector3 targetPos)
        {
            if (e == null || e.transform == null) return false;
            Vector3 self = e.transform.position;
            float eng = ClampRange(e.EngagementRange, e.MaxRange);
            float sqr = (targetPos - self).sqrMagnitude;
            if (sqr > eng * eng) return false;

            float arc = ClampArc(e.FiringArcDegrees);
            if (arc >= 359.5f) return true;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(self);
            Vector3 fwd = Vector3.ProjectOnPlane(e.transform.forward, up);
            Vector3 to  = Vector3.ProjectOnPlane(targetPos - self, up);
            if (fwd.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f) return true;
            float ang = Vector3.Angle(fwd.normalized, to.normalized);
            return ang <= arc * 0.5f;
        }

        public static string Describe(IDefenseEngagement e)
        {
            if (e == null) return "";
            float eng = ClampRange(e.EngagementRange, e.MaxRange);
            float arc = ClampArc(e.FiringArcDegrees);
            if (arc >= 359.5f) return $"Range {eng:0}m · 360°";
            return $"Range {eng:0}m · Arc {arc:0}°";
        }
    }
}
