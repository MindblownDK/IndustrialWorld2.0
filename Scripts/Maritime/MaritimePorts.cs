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
            "Port_WaterIntake", "Port_WaterOutlet",
        };

        /// <summary>Gas-only ports (oxygen feed, exhaust-gas tap, generic gas
        /// IO incl. steam). Gas pipes magnet onto these — and ONLY these.
        /// (The residual-heat steam port was removed by request — exhaust is
        /// the one and only hot-gas hookup.)</summary>
        public static readonly string[] GasPrefixes =
        {
            "Port_OxygenInput", "Port_OxygenOutput", "Port_ExhaustGasIO",
            "Port_GasInput", "Port_GasOutput", "Port_GasIO",
            "Port_HydrogenInput", "Port_HydrogenOutput",
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

        /// <summary>
        /// Aim-true port selection: nearest matching port to the AIM RAY LINE, not to a
        /// surface hit point. Deep ports (MGO fuel/coolant sit metres inside the engine
        /// hull) can be several metres from the hitbox face the player is aiming at, so
        /// pure hit-point proximity silently picks nothing; a ray-line pick selects the
        /// port the player is actually aiming across the machine at.
        /// Accepts ports within <paramref name="maxLineDistance"/> of the ray line and
        /// along-ray t in [0, <paramref name="maxRayT"/>]. Returns null when none qualify.
        /// </summary>
        public static Transform FindNearestToRay(Transform root, string[] prefixes, Ray ray,
            float maxLineDistance, float maxRayT = float.PositiveInfinity)
        {
            if (root == null || prefixes == null) return null;
            Transform best = null;
            float bestLineDistSqr = maxLineDistance * maxLineDistance;
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

                Vector3 toPort = child.position - ray.origin;
                float t = Vector3.Dot(toPort, ray.direction);
                if (t < 0f || t > maxRayT) continue;                     // behind camera / too far
                float lineDistSqr = (toPort - ray.direction * t).sqrMagnitude;
                if (lineDistSqr < bestLineDistSqr) { bestLineDistSqr = lineDistSqr; best = child; }
            }
            return best;
        }
        /// falling back to the block's own centre when it has no such port. This is what
        /// pipe visual arms should aim at — never the raw block centre when a named port
        /// exists, or arms visually skew through the machine body instead of towards it.</summary>
        public static Vector3 PortPositionOrCenter(GridBlock block, string[] prefixes, Vector3 fromPoint)
        {
            if (block == null) return fromPoint;
            var port = FindNearest(block.transform, prefixes, fromPoint);
            return port != null ? port.position : block.transform.position;
        }

        /// <summary>TRUE authored outward direction of a port in world space — read from its
        /// <see cref="MaritimePortFacing"/> tag (direction stored relative to the machine root,
        /// which every port is a direct child of). Falls back to the port transform's +Z when
        /// untagged, then to <paramref name="fallbackDir"/> when that is degenerate. Connectors
        /// must NEVER guess orientation from a position offset: ports near a machine's centre
        /// line (top exhaust collectors, deep fuel ports) silently mis-aim that way.</summary>
        public static Vector3 PortOutwardWorld(Transform port, Vector3 fallbackDir)
        {
            if (port != null)
            {
                var facing = port.GetComponent<MaritimePortFacing>();
                if (facing != null && facing.localOutward.sqrMagnitude > 0.0001f && port.parent != null)
                    return port.parent.TransformDirection(facing.localOutward.normalized).normalized;
                if (port.forward.sqrMagnitude > 0.0001f)
                    return port.forward.normalized;
            }
            return fallbackDir.sqrMagnitude > 0.0001f ? fallbackDir.normalized : Vector3.up;
        }
    }
}
