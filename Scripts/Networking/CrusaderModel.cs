// Assets/Scripts/VoxelEngine/Networking/CrusaderModel.cs
//
// 14.13.0-dev - Multiplayer milestone 6 begins: Real Crusaders.
//
// The procedural knight body every other player sees, replacing the
// placeholder capsule. Built entirely at RUNTIME from primitives on the
// existing avatar prefab - no editor step, no prefab change, self-healing
// after any FishNet reimport, and consistent with the game's procedural
// viewmodel style.
//
// Anatomy (1.85 m, pivot at the feet, +Z forward, matches PlayerController):
//   - great helm: flat-top cylinder, gold crown band, and the classic
//     cross-shaped face opening (horizontal eye slit + vertical breath slit),
//   - white tabard over a steel cuirass, red crusader cross front AND back
//     so the tier read of a player works from any side,
//   - pauldrons, mail arms and legs, faulds, leather belt,
//   - the RIGHT arm hangs from its own pivot ("RightArmPivot") so the
//     building pose (later this milestone) can raise it, and the held tool
//     rides in "RightHand" underneath it - HeldToolView models exactly as
//     before,
//   - "BackAnchor" marks where the jetpack + oxygen tank mount when
//     equipment display lands (next step of this milestone).
//
// Crouch: PlayerAvatar squashes the "Crusader" root exactly the way it
// squashed the old capsule - pivot at the feet makes a plain Y scale correct.

using UnityEngine;

namespace VoxelEngine.Networking
{
    public static class CrusaderModel
    {
        public const string RootName = "Crusader";
        public const string RightArmPivotName = "RightArmPivot";
        public const string RightHandName = "RightHand";
        public const string BackAnchorName = "BackAnchor";

        /// <summary>Build the knight under this avatar root if it is not already
        /// there. Idempotent and cheap when built; hides the legacy placeholder
        /// capsule instead of destroying it, so the prefab stays untouched.</summary>
        public static Transform EnsureBuilt(Transform avatarRoot)
        {
            if (avatarRoot == null) return null;
            var existing = avatarRoot.Find(RootName);
            if (existing != null) return existing;

            var legacyBody = avatarRoot.Find("Body");
            if (legacyBody != null) legacyBody.gameObject.SetActive(false);
            var legacyVisor = avatarRoot.Find("Visor");
            if (legacyVisor != null) legacyVisor.gameObject.SetActive(false);

            var root = new GameObject(RootName).transform;
            root.SetParent(avatarRoot, false);

            var steel   = Mat(new Color(0.60f, 0.63f, 0.67f), 0.85f, 0.60f);
            var mail    = Mat(new Color(0.40f, 0.42f, 0.46f), 0.70f, 0.35f);
            var dark    = Mat(new Color(0.04f, 0.04f, 0.05f), 0.20f, 0.20f);
            var cloth   = Mat(new Color(0.90f, 0.88f, 0.82f), 0.00f, 0.10f);
            var cross   = Mat(new Color(0.72f, 0.10f, 0.08f), 0.00f, 0.15f);
            var leather = Mat(new Color(0.30f, 0.20f, 0.12f), 0.05f, 0.25f);
            var gold    = Mat(new Color(0.85f, 0.68f, 0.25f), 0.90f, 0.70f);

            // ── legs & feet (mail) ──
            Part(root, PrimitiveType.Cube,    "FootL", new Vector3(-0.11f, 0.05f, 0.04f), new Vector3(0.16f, 0.10f, 0.28f), mail);
            Part(root, PrimitiveType.Cube,    "FootR", new Vector3( 0.11f, 0.05f, 0.04f), new Vector3(0.16f, 0.10f, 0.28f), mail);
            Part(root, PrimitiveType.Capsule, "LegL",  new Vector3(-0.11f, 0.46f, 0f),    new Vector3(0.18f, 0.36f, 0.18f), mail);
            Part(root, PrimitiveType.Capsule, "LegR",  new Vector3( 0.11f, 0.46f, 0f),    new Vector3(0.18f, 0.36f, 0.18f), mail);

            // ── hips & torso (steel cuirass under a tabard) ──
            Part(root, PrimitiveType.Cube, "Faulds", new Vector3(0f, 0.86f, 0f), new Vector3(0.42f, 0.18f, 0.30f), steel);
            Part(root, PrimitiveType.Cube, "Belt",   new Vector3(0f, 0.94f, 0f), new Vector3(0.48f, 0.08f, 0.33f), leather);
            Part(root, PrimitiveType.Cube, "Torso",  new Vector3(0f, 1.16f, 0f), new Vector3(0.46f, 0.52f, 0.30f), steel);

            // Tabard + crusader cross, front and back - readable from any side.
            Part(root, PrimitiveType.Cube, "TabardF", new Vector3(0f, 1.14f, 0.160f),  new Vector3(0.30f, 0.48f, 0.02f), cloth);
            Part(root, PrimitiveType.Cube, "TabardB", new Vector3(0f, 1.14f, -0.160f), new Vector3(0.30f, 0.48f, 0.02f), cloth);
            Part(root, PrimitiveType.Cube, "CrossVF", new Vector3(0f, 1.15f, 0.174f),  new Vector3(0.07f, 0.34f, 0.012f), cross);
            Part(root, PrimitiveType.Cube, "CrossHF", new Vector3(0f, 1.24f, 0.174f),  new Vector3(0.22f, 0.07f, 0.012f), cross);
            Part(root, PrimitiveType.Cube, "CrossVB", new Vector3(0f, 1.15f, -0.174f), new Vector3(0.07f, 0.34f, 0.012f), cross);
            Part(root, PrimitiveType.Cube, "CrossHB", new Vector3(0f, 1.24f, -0.174f), new Vector3(0.22f, 0.07f, 0.012f), cross);

            // ── shoulders & arms ──
            Part(root, PrimitiveType.Sphere,  "PauldronL", new Vector3(-0.27f, 1.40f, 0f), new Vector3(0.22f, 0.17f, 0.22f), steel);
            Part(root, PrimitiveType.Sphere,  "PauldronR", new Vector3( 0.27f, 1.40f, 0f), new Vector3(0.22f, 0.17f, 0.22f), steel);
            Part(root, PrimitiveType.Capsule, "ArmL",      new Vector3(-0.31f, 1.10f, 0f), new Vector3(0.14f, 0.28f, 0.14f), mail);
            Part(root, PrimitiveType.Sphere,  "GauntletL", new Vector3(-0.31f, 0.80f, 0f), new Vector3(0.16f, 0.16f, 0.16f), steel);

            // Right arm hangs from its own pivot so the building pose can raise it.
            var armPivot = new GameObject(RightArmPivotName).transform;
            armPivot.SetParent(root, false);
            armPivot.localPosition = new Vector3(0.31f, 1.38f, 0f);
            Part(armPivot, PrimitiveType.Capsule, "ArmR",      new Vector3(0f, -0.28f, 0f), new Vector3(0.14f, 0.28f, 0.14f), mail);
            Part(armPivot, PrimitiveType.Sphere,  "GauntletR", new Vector3(0f, -0.56f, 0f), new Vector3(0.16f, 0.16f, 0.16f), steel);

            var hand = new GameObject(RightHandName).transform;
            hand.SetParent(armPivot, false);
            hand.localPosition = new Vector3(0f, -0.60f, 0.06f);
            hand.localRotation = Quaternion.Euler(10f, -20f, 0f);   // same grip as the legacy anchor

            // ── great helm ──
            Part(root, PrimitiveType.Cylinder, "Helm",      new Vector3(0f, 1.71f, 0f),     new Vector3(0.34f, 0.14f, 0.34f), steel);
            Part(root, PrimitiveType.Cylinder, "CrownBand", new Vector3(0f, 1.84f, 0f),     new Vector3(0.355f, 0.012f, 0.355f), gold);
            Part(root, PrimitiveType.Cube,     "EyeSlit",   new Vector3(0f, 1.75f, 0.155f), new Vector3(0.20f, 0.030f, 0.035f), dark);
            Part(root, PrimitiveType.Cube,     "FaceSlit",  new Vector3(0f, 1.69f, 0.158f), new Vector3(0.030f, 0.14f, 0.030f), dark);

            // ── equipment anchor (jetpack + oxygen tank, next step) ──
            var back = new GameObject(BackAnchorName).transform;
            back.SetParent(root, false);
            back.localPosition = new Vector3(0f, 1.22f, -0.20f);

            return root;
        }

        private static Transform Part(Transform parent, PrimitiveType type, string name,
            Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);   // display only - never block rays or physics
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        private static Material Mat(Color c, float metallic, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { color = c };
            if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            return m;
        }
    }
}
