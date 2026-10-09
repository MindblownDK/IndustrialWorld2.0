// Assets/Scripts/VoxelEngine/Combat/ExposedLogistics.cs
//
// Static open-air transport a Ghoul can actually reach. Ship and grid runs are
// excluded: surface scouts do not path onto hulls, and a sealed compartment is
// not an exposed line.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Fluids;
using VoxelEngine.Gas;
using VoxelEngine.GridSystem;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;

namespace VoxelEngine.Combat
{
    public static class ExposedLogistics
    {
        public const float RaidRangeMetres = 3.5f;

        private static readonly Collider[] Buffer = new Collider[16];

        public static PlacedBlock FindNearest(Vector3 position, Vector3 up, PlacedBlock ignore)
        {
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            else up.Normalize();

            int count = Physics.OverlapSphereNonAlloc(
                position, RaidRangeMetres, Buffer, ~0, QueryTriggerInteraction.Ignore);
            PlacedBlock best = null;
            float bestSq = RaidRangeMetres * RaidRangeMetres;
            for (int i = 0; i < count; i++)
            {
                Collider hit = Buffer[i];
                Buffer[i] = null;
                if (hit == null) continue;
                PlacedBlock block = hit.GetComponentInParent<PlacedBlock>();
                if (block == null || block == ignore || block.Hp <= 0) continue;
                if (block.GetComponentInParent<GridEntity>() != null) continue;
                if (!IsOpenTransport(block)) continue;

                float distanceSq = Vector3.ProjectOnPlane(block.transform.position - position, up).sqrMagnitude;
                if (distanceSq > bestSq) continue;
                bestSq = distanceSq;
                best = block;
            }
            return best;
        }

        public static bool IsOpenTransport(PlacedBlock block)
        {
            if (block == null) return false;
            return block.GetComponentInChildren<ConveyorBelt>(true) != null
                || block.GetComponentInChildren<ConveyorChute>(true) != null
                || block.GetComponentInChildren<ConveyorSplitter>(true) != null
                || block.GetComponentInChildren<Funnel>(true) != null
                || block.GetComponentInChildren<ItemPipe>(true) != null
                || block.GetComponentInChildren<WaterPipe>(true) != null
                || block.GetComponentInChildren<GasPipe>(true) != null;
        }
    }
}
