// Assets/Scripts/VoxelEngine/Maritime/MaritimePorts.cs
//
// One source of truth for the named attachment-port locators the Maritime
// mesh builder puts on its machine models. Runtime code references these
// prefixes for snapping (Builder/BuildSystem), visual pipe arms (WaterPipe/
// GasPipe), network topology (GridLiquidNetwork) and machine consumption
// (engine oxygen feed) — never hard-code a prefix outside this file.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    public static class MaritimePorts
    {
        /// <summary>Liquid-only ports (fuel / coolant / generic liquid IO).
        /// Liquid pipes magnet onto these — and ONLY these. Steam moved to the
        /// gas family (it is a gas and belongs on GasPipe runs).</summary>
        public static readonly string[] LiquidPrefixes =
        {
            "Port_FuelInput", "Port_CoolantInput",
            "Port_LiquidInput", "Port_WaterInput", "Port_LiquidIO", "Port_WaterIO",
        };

        /// <summary>Gas-only ports (oxygen feed, exhaust-gas tap, generic gas
        /// IO incl. steam). Gas pipes magnet onto these — and ONLY these.</summary>
        public static readonly string[] GasPrefixes =
        {
            "Port_OxygenInput", "Port_OxygenOutput", "Port_ExhaustGasIO",
            "Port_GasInput", "Port_GasOutput", "Port_GasIO",
            "Port_HydrogenInput", "Port_HydrogenOutput", "Port_SteamHeat",
        };

        /// <summary>Engine exhaust outputs (exhaust pipes + gas taps snap here).</summary>
        public static readonly string[] ExhaustOutputPrefixes = { "Port_ExhaustOutput" };

        /// <summary>Shaft/rotation ports (drive shafts, gearboxes, generators, propellers).</summary>
        public static readonly string[] ShaftPrefixes =
        {
            "Port_ShaftOutput", "Port_ShaftInput", "Port_RotationInput", "Port_RotationOutput",
            "Port_RotationOutput_Straight", "Port_RotationOutput_Up", "Port_RotationOutput_Down",
            "Port_ShaftIO", "Propeller mount point 0", "Propeller mount point 1", "Rotation input point 0",
        };

        /// <summary>Nearest child transform under <paramref name="root"/> whose name starts
        /// with one of <paramref name="prefixes"/>. Returns null when none is within reach.</summary>
        public static Transform FindNearest(Transform root, string[] prefixes, Vector3 nearPoint,
            float maxDistance = float.PositiveInfinity)
        {
            if (root == null || prefixes == null) return null;
            Transform best = null;
            float bestDist = maxDistance * maxDistance;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == root) continue;
                string childName = child.name;
                bool matches = false;
                for (int i = 0; i < prefixes.Length; i++)
                {
                    if (childName.StartsWith(prefixes[i], System.StringComparison.Ordinal)) { matches = true; break; }
                }
                if (!matches) continue;
                float d = (child.position - nearPoint).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = child; }
            }
            return best;
        }

        /// <summary>World position of the nearest matching port on <paramref name="block"/>,
        /// falling back to the block's own centre when it has no such port. This is what
        /// pipe visual arms should aim at — never the raw block centre when a named port
        /// exists, or arms visually skew through the machine body instead of towards it.</summary>
        public static Vector3 PortPositionOrCenter(GridBlock block, string[] prefixes, Vector3 fromPoint)
        {
            if (block == null) return fromPoint;
            var port = FindNearest(block.transform, prefixes, fromPoint);
            return port != null ? port.position : block.transform.position;
        }
    }
}
