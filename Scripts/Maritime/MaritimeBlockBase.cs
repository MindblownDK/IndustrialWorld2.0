// Assets/Scripts/VoxelEngine/Maritime/MaritimeBlockBase.cs
//
// Shared base for every grid block that participates in the maritime mechanical
// network. Provides:
//   • Default IMechanicalBlock implementations (override only what you need).
//   • Neighbour-exhaust checking (engines choke without an exhaust pipe).
//   • Liquid-fuel draw helper (scan grid tanks for a liquid type).
//   • Solid-fuel draw helper (scan grid cargo for burnable items).

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    /// <summary>Which fuel category an engine consumes.</summary>
    public enum MaritimeFuelKind
    {
        /// <summary>Solid fuel items (wood logs, planks, coal) drawn from cargo.</summary>
        Solid = 0,
        /// <summary>Liquid fuel drawn from GridLiquidTank blocks.</summary>
        Liquid = 1,
    }

    /// <summary>
    /// Base class for all maritime propulsion / power blocks. Implements
    /// <see cref="IMechanicalBlock"/> with sensible defaults so concrete blocks
    /// only override the methods they care about.
    /// </summary>
    public abstract class MaritimeBlockBase : GridBlock, IMechanicalBlock
    {
        public abstract MechanicalNodeType NodeType { get; }

        public virtual void PopulateMaritimeNode(ref MechanicalNode node) { }
        public virtual void RefreshMaritimeNode(ref MechanicalNode node, float throttle) { }
        public virtual void ApplyResults(in MechanicalNode node) { }

        // ── Exhaust-pipe neighbour check ──────────────────────────────
        private static readonly Vector3Int[] Faces =
        {
            new( 1, 0, 0), new(-1, 0, 0),
            new( 0, 1, 0), new( 0,-1, 0),
            new( 0, 0, 1), new( 0, 0,-1),
        };

        /// <summary>True if any of the 6 face-neighbours is an exhaust pipe.</summary>
        protected bool HasAdjacentExhaust()
        {
            if (Grid == null) return false;
            foreach (var off in Faces)
            {
                var nb = Grid.GetBlock(GridPos + off);
                if (nb is GridExhaustPipe) return true;
            }
            return false;
        }

        /// <summary>True if any face-neighbour is a turbocharger (for visual / chained checks).</summary>
        protected bool HasAdjacentTurbo()
        {
            if (Grid == null) return false;
            foreach (var off in Faces)
            {
                var nb = Grid.GetBlock(GridPos + off);
                if (nb is GridTurbocharger) return true;
            }
            return false;
        }

        // ── Liquid-fuel draw ──────────────────────────────────────────
        /// <summary>Pull up to <paramref name="litres"/> of the given liquid from the connected
        /// liquid pipe network. Falls back to legacy grid-wide tank access when the ship has no
        /// liquid pipes at all so older builds remain functional.</summary>
        protected float DrawLiquidFuel(LiquidType type, float litres)
        {
            if (Grid == null || litres <= 0f) return 0f;

            if (GridLiquidNetwork.Instance != null && GridLiquidNetwork.Instance.HasPipes(Grid))
                return GridLiquidNetwork.Instance.DrawLiquidFor(this, type, litres);

            float remaining = litres;
            foreach (var kv in Grid.Blocks)
            {
                if (remaining <= 0.01f) break;
                if (kv.Value is not GridLiquidTank tank) continue;
                if (tank.mode != GridTankMode.Auto) continue;
                if (tank.liquidType != type) continue;
                remaining -= tank.Remove(remaining);
            }
            return litres - remaining;
        }

        /// <summary>Total available litres of a liquid. Uses pipe topology when present and falls
        /// back to legacy grid-wide access when a ship has no liquid pipes yet.</summary>
        protected float AvailableLiquid(LiquidType type)
        {
            if (Grid == null) return 0f;

            if (GridLiquidNetwork.Instance != null && GridLiquidNetwork.Instance.HasPipes(Grid))
                return GridLiquidNetwork.Instance.AvailableLiquidFor(this, type);

            float total = 0f;
            foreach (var kv in Grid.Blocks)
            {
                if (kv.Value is not GridLiquidTank tank) continue;
                if (tank.mode != GridTankMode.Auto) continue;
                if (tank.liquidType != type) continue;
                total += tank.stored;
            }
            return total;
        }

        // ── Solid-fuel draw ───────────────────────────────────────────
        /// <summary>Try to pull ONE fuel item from grid cargo containers.
        /// Returns the fuelSeconds of the consumed item, or 0 if none found.</summary>
        protected float DrawSolidFuel()
        {
            if (Grid == null) return 0f;
            foreach (var kv in Grid.Blocks)
            {
                if (kv.Value is not GridCargoContainer cargo) continue;
                var container = cargo.container;
                if (container == null) continue;

                // Scan slots for a burnable ResourceItem.
                for (int s = 0; s < container.Size; s++)
                {
                    var stack = container.GetSlot(s);
                    if (stack == null || stack.IsEmpty) continue;
                    if (stack.item is not ResourceItem res) continue;
                    if (res.fuelSeconds <= 0f) continue;

                    int removed = container.Remove(res, 1);
                    if (removed > 0) return res.fuelSeconds;
                }
            }
            return 0f;
        }

        /// <summary>Total burn-seconds available in grid cargo.</summary>
        protected float AvailableSolidFuel()
        {
            if (Grid == null) return 0f;
            float total = 0f;
            foreach (var kv in Grid.Blocks)
            {
                if (kv.Value is not GridCargoContainer cargo) continue;
                var container = cargo.container;
                if (container == null) continue;
                for (int s = 0; s < container.Size; s++)
                {
                    var stack = container.GetSlot(s);
                    if (stack == null || stack.IsEmpty) continue;
                    if (stack.item is not ResourceItem res) continue;
                    if (res.fuelSeconds <= 0f) continue;
                    total += res.fuelSeconds * stack.count;
                }
            }
            return total;
        }
    }
}
