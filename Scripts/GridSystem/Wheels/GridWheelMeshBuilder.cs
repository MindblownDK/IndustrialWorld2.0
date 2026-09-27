// Assets/Scripts/VoxelEngine/GridSystem/Wheels/GridWheelMeshBuilder.cs
//
// THE MODELS — a suspension you can read at a glance, and a tire that looks heavy.
//
//   BuildHub  — chassis mount, steering knuckle, upper and lower wishbones, a
//               coil-over with a visible spring, a telescoping damper, brake disc
//               and caliper, and the named rig transforms GridWheel animates.
//   BuildTire — lathe-built carcass with block tread, a dished rim, hub boss and
//               lug nuts.
//
// The hub is authored so the moving parts sit on named anchors. GridWheel aims and
// stretches UpperArm, LowerArm, Damper and Spring at the carrier every physics step,
// so the spring visibly compresses under load — the whole point of building the
// linkage out of unit-length meshes pivoted at their chassis end.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public static class GridWheelMeshBuilder
    {
        // ════════════════════════════════════════════════════════════════════
        //  HUB
        // ════════════════════════════════════════════════════════════════════

        public static void BuildHub(GameObject root, WheelSizeClass sizeClass, float cellSize, WheelMountSide side)
        {
            var preset = WheelTuning.For(sizeClass, cellSize);
            float cs = cellSize;
            float sign = side == WheelMountSide.Left ? -1f : 1f;
            float reach = cs * 0.5f + preset.TireWidth * 0.55f;
            float rest = preset.RestLength;

            var painted = WheelTextureFactory.PaintedSteel(new Color(0.31f, 0.33f, 0.36f), new Vector2(1.5f, 1.5f));
            var steel = WheelTextureFactory.Steel(new Vector2(2f, 2f));
            var chrome = WheelTextureFactory.Chrome(new Vector2(1f, 3f));
            var caliper = WheelTextureFactory.PaintedSteel(new Color(0.58f, 0.17f, 0.10f), new Vector2(1f, 1f));
            var springMat = WheelTextureFactory.PaintedSteel(new Color(0.16f, 0.17f, 0.19f), new Vector2(4f, 1f));

            // ── Chassis mount: fills its cell so it tiles with armour ───────
            Box(root, painted, Vector3.zero, new Vector3(cs * 0.98f, cs * 0.98f, cs * 0.98f));
            // Outboard face plate the suspension actually hangs from.
            Box(root, steel, new Vector3(sign * cs * 0.50f, 0f, 0f), new Vector3(cs * 0.08f, cs * 0.86f, cs * 0.86f));
            foreach (var bolt in MountBolts(cs, sign))
                Cylinder(root, chrome, bolt, cs * 0.04f, cs * 0.05f, new Vector3(0f, 0f, 90f));

            // ── Steering pivot (king pin) ───────────────────────────────────
            var steer = Child(root.transform, "SteerPivot", Vector3.zero);
            Cylinder(steer.gameObject, chrome, new Vector3(sign * cs * 0.52f, 0f, 0f), cs * 0.07f, cs * 0.55f,
                new Vector3(90f, 0f, 0f));

            // ── Linkage: unit-length parts pivoted at the chassis end ───────
            // GridWheel rotates each of these to face the carrier and scales X to reach
            // it, so every one of them animates with the travel.
            MakeLink(steer, "UpperArm", new Vector3(sign * cs * 0.52f, cs * 0.26f, 0f),
                WheelMeshFactory.Wishbone(), steel, cs * 0.30f);
            MakeLink(steer, "LowerArm", new Vector3(sign * cs * 0.52f, -cs * 0.26f, 0f),
                WheelMeshFactory.Wishbone(), steel, cs * 0.38f);

            // Coil-over: the spring is one mesh scaled along its length, so its coils
            // bunch together as the strut compresses.
            MakeLink(steer, "Spring", new Vector3(sign * cs * 0.34f, cs * 0.44f, 0f),
                WheelMeshFactory.CoilSpring(), springMat, cs * 0.30f);
            // Damper body rides inside the spring and telescopes over the rod.
            MakeLink(steer, "Damper", new Vector3(sign * cs * 0.34f, cs * 0.44f, 0f),
                null, chrome, cs * 0.10f);

            // ── Carrier: everything below the spring ────────────────────────
            var carrier = Child(steer, "SuspensionCarrier", new Vector3(sign * reach, -rest, 0f));
            // Upright / knuckle.
            Box(carrier.gameObject, painted, new Vector3(-sign * cs * 0.06f, 0f, 0f),
                new Vector3(cs * 0.26f, cs * 0.52f, cs * 0.30f));
            Cylinder(carrier.gameObject, steel, new Vector3(-sign * cs * 0.06f, cs * 0.24f, 0f), cs * 0.09f, cs * 0.16f,
                new Vector3(90f, 0f, 0f));
            Cylinder(carrier.gameObject, steel, new Vector3(-sign * cs * 0.06f, -cs * 0.24f, 0f), cs * 0.09f, cs * 0.16f,
                new Vector3(90f, 0f, 0f));

            // Brake disc + caliper, then the drive flange the tire bolts to.
            var disc = MeshPart(carrier.gameObject, "BrakeDisc", WheelMeshFactory.BrakeDisc(), steel,
                new Vector3(sign * cs * 0.10f, 0f, 0f));
            float discRadius = preset.TireRadius * 0.46f;
            disc.transform.localScale = new Vector3(cs * 0.05f, discRadius, discRadius);
            disc.transform.localRotation = Quaternion.identity;

            Box(carrier.gameObject, caliper, new Vector3(sign * cs * 0.10f, discRadius * 0.82f, 0f),
                new Vector3(cs * 0.13f, cs * 0.22f, cs * 0.14f));
            Cylinder(carrier.gameObject, chrome, new Vector3(sign * cs * 0.16f, 0f, 0f),
                preset.HubRadius * 0.42f, cs * 0.14f, new Vector3(0f, 0f, 90f));

            // ── Mount socket the tire snaps onto ────────────────────────────
            Child(carrier, "TireSocket", new Vector3(sign * (cs * 0.20f + preset.TireWidth * 0.5f), 0f, 0f));
        }

        /// <summary>
        /// One animated linkage part: an empty pivot at the chassis anchor holding a
        /// unit-length mesh (or a plain cylinder) that points down +X.
        /// </summary>
        private static Transform MakeLink(Transform parent, string name, Vector3 anchor, Mesh mesh,
            Material material, float thickness)
        {
            var link = Child(parent, name, anchor);
            var visual = link.Find("Visual");
            if (visual == null)
            {
                GameObject go;
                if (mesh != null)
                {
                    go = new GameObject("Visual");
                    go.transform.SetParent(link, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = material;
                }
                else
                {
                    // Plain telescoping tube: a cylinder rotated to lie along +X, with its
                    // base at the pivot so scaling the pivot stretches it outward only.
                    go = Primitive(PrimitiveType.Cylinder, link.gameObject, material);
                    go.name = "Visual";
                    go.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
                    go.transform.localPosition = new Vector3(0.5f, 0f, 0f);
                    go.transform.localScale = new Vector3(thickness, 0.5f, thickness);
                    return link;
                }
                go.transform.localScale = new Vector3(1f, thickness, thickness);
            }
            return link;
        }

        private static Vector3[] MountBolts(float cs, float sign)
        {
            float x = sign * cs * 0.54f;
            float o = cs * 0.36f;
            return new[]
            {
                new Vector3(x,  o,  o), new Vector3(x,  o, -o),
                new Vector3(x, -o,  o), new Vector3(x, -o, -o),
            };
        }

        // ════════════════════════════════════════════════════════════════════
        //  TIRE
        // ════════════════════════════════════════════════════════════════════

        public static void BuildTire(GameObject root, WheelSizeClass sizeClass, float cellSize)
        {
            var preset = WheelTuning.For(sizeClass, cellSize);
            float radius = preset.TireRadius;
            float width = preset.TireWidth;

            var spin = Child(root.transform, "TireSpin", Vector3.zero);

            var rubber = WheelTextureFactory.Rubber(new Vector2(6f, 2f));
            var rim = WheelTextureFactory.Steel(new Vector2(3f, 2f));
            var bronze = WheelTextureFactory.PaintedSteel(new Color(0.52f, 0.31f, 0.16f), new Vector2(2f, 2f));
            var dark = WheelTextureFactory.PaintedSteel(new Color(0.12f, 0.12f, 0.13f), new Vector2(2f, 2f));
            var chrome = WheelTextureFactory.Chrome(new Vector2(1f, 1f));

            // Carcass: one lathe mesh scaled to the size class. Radius 1 / width 1 unit.
            var carcass = MeshPart(spin.gameObject, "Carcass", WheelMeshFactory.Tire(sizeClass), rubber, Vector3.zero);
            carcass.transform.localScale = new Vector3(width, radius, radius);

            // Rim sits inside the bead and shares the tire's radius so the two never gap.
            var rimPart = MeshPart(spin.gameObject, "Rim", WheelMeshFactory.Rim(sizeClass), rim, Vector3.zero);
            rimPart.transform.localScale = new Vector3(width, radius, radius);

            // Bronze dish ring and dark hub face, the two colour breaks that stop a big
            // wheel from reading as one grey mass.
            Cylinder(spin.gameObject, bronze, new Vector3(width * 0.30f, 0f, 0f), radius * 0.40f, width * 0.10f,
                new Vector3(0f, 0f, 90f));
            Cylinder(spin.gameObject, dark, new Vector3(width * 0.36f, 0f, 0f), radius * 0.22f, width * 0.08f,
                new Vector3(0f, 0f, 90f));
            Cylinder(spin.gameObject, chrome, new Vector3(width * 0.40f, 0f, 0f), radius * 0.09f, width * 0.06f,
                new Vector3(0f, 0f, 90f));

            // Lug nuts around the hub face.
            int nuts = sizeClass == WheelSizeClass.Size_2x2 ? 8 : sizeClass == WheelSizeClass.Size_3x3 ? 10 : 12;
            for (int i = 0; i < nuts; i++)
            {
                float a = i / (float)nuts * Mathf.PI * 2f;
                Cylinder(spin.gameObject, chrome,
                    new Vector3(width * 0.40f, Mathf.Sin(a) * radius * 0.16f, Mathf.Cos(a) * radius * 0.16f),
                    radius * 0.028f, width * 0.05f, new Vector3(0f, 0f, 90f));
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

        private static GameObject MeshPart(GameObject parent, string name, Mesh mesh, Material material, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
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
