// Assets/Scripts/VoxelEngine/GridSystem/Wheels/GridWheelMeshBuilder.cs
//
// THE MODELS — a hub you can read at a glance, and a tire that looks heavy.
//
// Two builders, because the parts are now two blocks:
//   BuildHub  — mount plate, steering knuckle, double wishbone, coil-over strut,
//               brake caliper and the named rig transforms the hub script drives.
//   BuildTire — carcass, deep directional lugs, sidewall shoulder, rim with dish,
//               hub cap and lug nuts.
//
// Both are authored against the cell size so 2x2, 3x3 and 5x5 stay proportional,
// and both dress themselves with WheelTextureFactory maps instead of flat colour.
// The hub deliberately fills exactly one cell at its mount face so it tiles with
// armour like every other block, while the tire hangs entirely outside the lattice
// — which is what makes the snap-on placement read correctly.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public static class GridWheelMeshBuilder
    {
        // ════════════════════════════════════════════════════════════════════
        //  HUB
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Builds the suspension hub and the rig transforms GridWheel drives.</summary>
        public static void BuildHub(GameObject root, WheelSizeClass sizeClass, float cellSize, WheelMountSide side)
        {
            var preset = WheelTuning.For(sizeClass, cellSize);
            float cs = cellSize;
            float sign = side == WheelMountSide.Left ? -1f : 1f;
            float reach = cs * 0.5f + preset.TireWidth * 0.55f;

            var steel = WheelTextureFactory.Steel(new Vector2(2f, 2f));
            var painted = WheelTextureFactory.PaintedSteel(new Color(0.30f, 0.32f, 0.35f), new Vector2(1.5f, 1.5f));
            var chrome = WheelTextureFactory.Chrome(new Vector2(1f, 3f));
            var caliper = WheelTextureFactory.PaintedSteel(new Color(0.55f, 0.16f, 0.10f), new Vector2(1f, 1f));

            // ── Chassis mount: fills the cell so it tiles with armour ───────
            Box(root, painted, new Vector3(0f, 0f, 0f), new Vector3(cs * 0.98f, cs * 0.98f, cs * 0.98f));
            Box(root, steel, new Vector3(sign * cs * 0.48f, cs * 0.08f, 0f), new Vector3(cs * 0.10f, cs * 0.66f, cs * 0.82f));
            foreach (var bolt in MountBolts(cs, sign))
                Cylinder(root, chrome, bolt, cs * 0.045f, cs * 0.06f, new Vector3(0f, 0f, 90f));

            // ── Steering pivot ──────────────────────────────────────────────
            var steer = Child(root.transform, "SteerPivot", Vector3.zero);
            Cylinder(steer.gameObject, chrome, new Vector3(sign * cs * 0.34f, 0f, 0f), cs * 0.09f, cs * 0.70f, Vector3.zero);

            // ── Wishbones: authored 1 m along +X so the hub can scale them ──
            BuildArm(steer, "UpperArm", new Vector3(sign * cs * 0.34f, cs * 0.20f, 0f), steel, cs * 0.10f, sign);
            BuildArm(steer, "LowerArm", new Vector3(sign * cs * 0.34f, -cs * 0.14f, 0f), steel, cs * 0.13f, sign);

            // ── Coil-over strut sleeve (static half) ────────────────────────
            Cylinder(steer.gameObject, painted, new Vector3(sign * reach * 0.55f, -cs * 0.05f, 0f),
                cs * 0.11f, preset.MaxTravel * 0.55f, Vector3.zero);

            // ── Carrier: everything below the spring ────────────────────────
            var carrier = Child(steer, "SuspensionCarrier", new Vector3(sign * reach, -preset.RestLength, 0f));
            Cylinder(carrier.gameObject, chrome, new Vector3(-sign * reach * 0.42f, preset.RestLength * 0.42f, 0f),
                cs * 0.06f, preset.MaxTravel * 0.60f, Vector3.zero);
            Box(carrier.gameObject, steel, Vector3.zero, new Vector3(cs * 0.34f, cs * 0.36f, cs * 0.34f));
            // Brake disc and caliper read instantly as "this wheel can stop".
            Cylinder(carrier.gameObject, steel, new Vector3(sign * cs * 0.12f, 0f, 0f),
                preset.TireRadius * 0.42f, cs * 0.05f, new Vector3(0f, 0f, 90f));
            Box(carrier.gameObject, caliper, new Vector3(sign * cs * 0.12f, preset.TireRadius * 0.30f, 0f),
                new Vector3(cs * 0.14f, cs * 0.20f, cs * 0.12f));
            Cylinder(carrier.gameObject, chrome, new Vector3(sign * cs * 0.22f, 0f, 0f),
                preset.HubRadius * 0.45f, cs * 0.18f, new Vector3(0f, 0f, 90f));

            // ── Mount socket the tire snaps onto ────────────────────────────
            Child(carrier, "TireSocket", new Vector3(sign * cs * 0.26f, 0f, 0f));
        }

        private static void BuildArm(Transform parent, string name, Vector3 origin, Material mat, float thickness, float sign)
        {
            var arm = Child(parent, name, origin);
            // The bar is a unit-long child so the hub can scale the pivot on X alone.
            var bar = Box(arm.gameObject, mat, new Vector3(0.5f * sign, 0f, 0f), new Vector3(1f, thickness, thickness));
            bar.transform.localScale = new Vector3(1f, thickness, thickness);
            bar.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            Cylinder(arm.gameObject, mat, Vector3.zero, thickness * 0.75f, thickness * 1.4f, new Vector3(90f, 0f, 0f));
        }

        private static Vector3[] MountBolts(float cs, float sign)
        {
            float x = sign * cs * 0.52f;
            float o = cs * 0.34f;
            return new[]
            {
                new Vector3(x,  o,  o), new Vector3(x,  o, -o),
                new Vector3(x, -o,  o), new Vector3(x, -o, -o),
            };
        }

        // ════════════════════════════════════════════════════════════════════
        //  TIRE
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Builds a tire carcass under a "TireSpin" pivot so it can roll.</summary>
        public static void BuildTire(GameObject root, WheelSizeClass sizeClass, float cellSize)
        {
            var preset = WheelTuning.For(sizeClass, cellSize);
            float radius = preset.TireRadius;
            float width = preset.TireWidth;
            float tread = preset.TreadDepth;

            var spin = Child(root.transform, "TireSpin", Vector3.zero);
            var rubber = WheelTextureFactory.Rubber(new Vector2(Mathf.Max(2f, radius * 1.4f), 2f));
            var sidewall = WheelTextureFactory.Make("Wheel_Sidewall", new Color(0.62f, 0.62f, 0.64f),
                WheelTextureFactory.TreadAlbedo(), WheelTextureFactory.TreadNormal(), 0.04f, 0.26f, new Vector2(3f, 1f));
            var rim = WheelTextureFactory.Steel(new Vector2(2f, 2f));
            var chrome = WheelTextureFactory.Chrome(new Vector2(1f, 1f));

            // Carcass: three stacked cylinders give a crowned profile instead of a can.
            Cylinder(spin.gameObject, rubber, Vector3.zero, radius, width, new Vector3(0f, 0f, 90f));
            Cylinder(spin.gameObject, sidewall, new Vector3(-width * 0.52f, 0f, 0f), radius * 0.94f, width * 0.10f, new Vector3(0f, 0f, 90f));
            Cylinder(spin.gameObject, sidewall, new Vector3(width * 0.52f, 0f, 0f), radius * 0.94f, width * 0.10f, new Vector3(0f, 0f, 90f));

            // Directional lugs: alternate rows are offset so the pattern reads as a V.
            int lugCount = sizeClass == WheelSizeClass.Size_5x5 ? 26 : sizeClass == WheelSizeClass.Size_3x3 ? 20 : 16;
            for (int i = 0; i < lugCount; i++)
            {
                float a = i / (float)lugCount * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                for (int half = 0; half < 2; half++)
                {
                    float offset = (half == 0 ? -1f : 1f) * width * 0.24f;
                    float skew = (half == 0 ? -1f : 1f) * (Mathf.PI * 2f / lugCount) * 0.22f;
                    float sk = a + skew;
                    var lug = Box(spin.gameObject, rubber,
                        new Vector3(offset, Mathf.Sin(sk) * (radius + tread * 0.35f), Mathf.Cos(sk) * (radius + tread * 0.35f)),
                        new Vector3(width * 0.40f, tread, radius * 0.30f));
                    lug.transform.localRotation = Quaternion.Euler(Mathf.Rad2Deg * sk, 0f, 0f);
                }
                // Shoulder blocks bridge tread to sidewall and kill the "smooth can" look.
                Box(spin.gameObject, rubber,
                    new Vector3(0f, sa * radius * 0.99f, ca * radius * 0.99f),
                    new Vector3(width * 1.02f, tread * 0.55f, radius * 0.10f))
                    .transform.localRotation = Quaternion.Euler(Mathf.Rad2Deg * a, 0f, 0f);
            }

            // Rim: dished centre, barrel, and a bolted cap.
            Cylinder(spin.gameObject, rim, Vector3.zero, radius * 0.58f, width * 0.86f, new Vector3(0f, 0f, 90f));
            Cylinder(spin.gameObject, rim, new Vector3(-width * 0.30f, 0f, 0f), radius * 0.64f, width * 0.06f, new Vector3(0f, 0f, 90f));
            Cylinder(spin.gameObject, rim, new Vector3(width * 0.30f, 0f, 0f), radius * 0.64f, width * 0.06f, new Vector3(0f, 0f, 90f));
            Cylinder(spin.gameObject, chrome, new Vector3(width * 0.40f, 0f, 0f), radius * 0.26f, width * 0.10f, new Vector3(0f, 0f, 90f));

            int nuts = sizeClass == WheelSizeClass.Size_2x2 ? 6 : 8;
            for (int i = 0; i < nuts; i++)
            {
                float a = i / (float)nuts * Mathf.PI * 2f;
                Cylinder(spin.gameObject, chrome,
                    new Vector3(width * 0.46f, Mathf.Sin(a) * radius * 0.34f, Mathf.Cos(a) * radius * 0.34f),
                    radius * 0.045f, width * 0.08f, new Vector3(0f, 0f, 90f));
            }

            // Lightening holes in the dish: a 5x5 rim without them looks like a drum.
            int holes = sizeClass == WheelSizeClass.Size_5x5 ? 8 : 6;
            for (int i = 0; i < holes; i++)
            {
                float a = (i + 0.5f) / holes * Mathf.PI * 2f;
                Cylinder(spin.gameObject, rim,
                    new Vector3(width * 0.34f, Mathf.Sin(a) * radius * 0.46f, Mathf.Cos(a) * radius * 0.46f),
                    radius * 0.10f, width * 0.05f, new Vector3(0f, 0f, 90f));
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  PRIMITIVE HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                existing.localPosition = localPosition;
                return existing;
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        private static GameObject Box(GameObject parent, Material mat, Vector3 localPosition, Vector3 size)
        {
            var go = Primitive(PrimitiveType.Cube, parent, mat);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            return go;
        }

        private static GameObject Cylinder(GameObject parent, Material mat, Vector3 localPosition,
            float radius, float length, Vector3 euler)
        {
            var go = Primitive(PrimitiveType.Cylinder, parent, mat);
            go.transform.localPosition = localPosition;
            go.transform.localScale = new Vector3(radius * 2f, Mathf.Max(0.001f, length * 0.5f), radius * 2f);
            go.transform.localRotation = Quaternion.Euler(euler);
            return go;
        }

        private static GameObject Primitive(PrimitiveType type, GameObject parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var collider = go.GetComponent<Collider>();
            // Visual geometry only: the hub raycasts for ground and the grid owns collision.
            // Editor tooling builds prefabs outside play mode, so the destroy must match.
            if (collider != null)
            {
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }
            go.transform.SetParent(parent.transform, false);
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && mat != null) renderer.sharedMaterial = mat;
            return go;
        }
    }
}
