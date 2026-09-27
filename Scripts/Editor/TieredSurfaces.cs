#if UNITY_EDITOR
// Assets/Scripts/VoxelEngine/Editor/TieredSurfaces.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        INDUSTRIAL WORLD — TIERED CONSTRUCTION SURFACE SET         ║
// ║                                                                  ║
// ║  Four build tiers, each with three roles, authored once as real  ║
// ║  assets so a designer can retune them and never lose the change. ║
// ║                                                                  ║
// ║    SKIN   the strong exterior face - logs, masonry, corrugated   ║
// ║           scrap, armour plate                                     ║
// ║    FRAME  the weak interior face - the structure holding the     ║
// ║           skin up: posts and braces, rough rubble, L-beams,      ║
// ║           tread-plate bracing                                     ║
// ║    TRIM   the hardware - iron bands, mortar caps, rivets, bolts  ║
// ║                                                                  ║
// ║  A piece is modelled with the skin on one side and the frame on  ║
// ║  the other, so which way a wall faces is legible at a glance -   ║
// ║  the raider's question and the builder's answer in one read.     ║
// ║                                                                  ║
// ║  Everything is procedural: no imported art, no atlas to keep in  ║
// ║  sync, and the whole set regenerates from this file alone.       ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEditor;
using UnityEngine;
using VoxelEngine.Building.Tiered;

namespace VoxelEngine.EditorTools
{
    /// <summary>Which face of a piece a material belongs to.</summary>
    public enum PieceSurface { Skin, Frame, Trim, Glass }

    public static class TieredSurfaces
    {
        private const string Root = "Assets/VoxelEngineAssets/Tiered";
        private const string MatFolder = Root + "/Materials";
        private const int TexSize = 256;

        // Materials are looked up per part of every prefab, so the AssetDatabase
        // round trip is cached for the duration of the run.
        private static readonly System.Collections.Generic.Dictionary<string, Material> _cache = new();

        // ── Tier palettes ────────────────────────────────────────────────
        // Tier 0 Wood, 1 Stone, 2 Sheet Metal, 3 Armoured. The colours are the
        // weathered, low-chroma end of each material: bright building pieces
        // read as toys, and these have to sit in a landscape.

        private static readonly Color[] SkinColor =
        {
            new(0.42f, 0.29f, 0.17f),   // seasoned log
            new(0.52f, 0.51f, 0.48f),   // fitted masonry
            new(0.49f, 0.47f, 0.44f),   // galvanised scrap
            new(0.13f, 0.135f, 0.145f), // matte armour plate
        };

        private static readonly Color[] FrameColor =
        {
            new(0.50f, 0.37f, 0.23f),   // pale sawn timber
            new(0.40f, 0.39f, 0.37f),   // rough rubble
            new(0.36f, 0.37f, 0.39f),   // structural steel
            new(0.20f, 0.21f, 0.23f),   // tread plate
        };

        private static readonly Color[] TrimColor =
        {
            new(0.36f, 0.25f, 0.15f),   // dark seasoned timber
            new(0.60f, 0.58f, 0.54f),   // mortar and capstone
            new(0.33f, 0.30f, 0.27f),   // rusted bolt work
            new(0.28f, 0.29f, 0.32f),   // hardened fasteners
        };

        private static readonly float[] SkinMetallic   = { 0.00f, 0.00f, 0.68f, 0.82f };
        private static readonly float[] SkinSmoothness = { 0.16f, 0.12f, 0.30f, 0.26f };

        /// <summary>Material for one tier and one role, created on first request.</summary>
        public static Material Get(BuildTier tier, PieceSurface surface)
        {
            int t = Mathf.Clamp((int)tier, 0, 3);
            if (surface == PieceSurface.Glass) return Glass();

            string name = $"Mat_Build_{TierWord(t)}_{surface}";
            if (_cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var existing = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/{name}.mat");
            if (existing != null) { _cache[name] = existing; return existing; }

            Color color = surface switch
            {
                PieceSurface.Skin => SkinColor[t],
                PieceSurface.Frame => FrameColor[t],
                _ => TrimColor[t]
            };

            float metallic = surface == PieceSurface.Trim && t > 0
                ? Mathf.Max(0.35f, SkinMetallic[t])
                : SkinMetallic[t];
            float smoothness = surface == PieceSurface.Frame
                ? SkinSmoothness[t] * 0.75f
                : SkinSmoothness[t];

            return Create(name, color, Pattern(t, surface), metallic, smoothness);
        }

        /// <summary>Shared window glazing. One asset for every tier and every piece.</summary>
        public static Material Glass()
        {
            const string name = "Mat_Build_Glass";
            if (_cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var existing = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/{name}.mat");
            if (existing != null) { _cache[name] = existing; return existing; }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name, enableInstancing = true };
            var tint = new Color(0.58f, 0.70f, 0.74f, 0.34f);

            // Transparent surface type on URP/Lit needs the keyword set as well as
            // the float, or the material renders opaque until someone opens it.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            mat.color = tint;
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.1f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.92f);

            EnsureFolders();
            AssetDatabase.CreateAsset(mat, $"{MatFolder}/{name}.mat");
            _cache[name] = mat;
            return mat;
        }

        /// <summary>Authors every tier surface up front, before any batched asset work.</summary>
        public static void Prewarm()
        {
            for (int t = 0; t < 4; t++)
            {
                Get((BuildTier)t, PieceSurface.Skin);
                Get((BuildTier)t, PieceSurface.Frame);
                Get((BuildTier)t, PieceSurface.Trim);
            }
            Glass();
        }

        public static string TierWord(int tier) => tier switch
        {
            0 => "Wood",
            1 => "Stone",
            2 => "SheetMetal",
            _ => "Armoured"
        };

        /// <summary>Human-facing tier name, matching what the surfaces actually depict.</summary>
        public static string TierDisplayName(BuildTier tier) => tier switch
        {
            BuildTier.Wood => "Wood",
            BuildTier.Stone => "Stone",
            BuildTier.Iron => "Sheet Metal",
            _ => "Armoured"
        };

        private static int Pattern(int tier, PieceSurface surface) => surface switch
        {
            PieceSurface.Skin => tier,              // 0 logs, 1 masonry, 2 corrugated, 3 plate
            PieceSurface.Frame => 4 + tier,         // 4 timber, 5 rubble, 6 L-beam, 7 tread
            // The wood tier's hardware is the framing timber itself - corner posts
            // and rails. Speckled iron there made a log cabin look bolted together.
            _ => tier == 0 ? 4 : 8
        };

        // ══════════════════════════════════════════════════════════════════
        //  TEXTURE SYNTHESIS
        // ══════════════════════════════════════════════════════════════════

        private static Material Create(string name, Color baseColor, int pattern, float metallic, float smoothness)
        {
            EnsureFolders();
            var texture = Bake($"Tex_{name}", baseColor, pattern);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name, enableInstancing = true };

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            mat.color = Color.white;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

            AssetDatabase.CreateAsset(mat, $"{MatFolder}/{name}.mat");
            _cache[name] = mat;
            return mat;
        }

        private static Texture2D Bake(string assetName, Color baseColor, int pattern)
        {
            string path = $"{MatFolder}/{assetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true)
            {
                name = assetName,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };

            var px = new Color[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float u = x / (float)TexSize, v = y / (float)TexSize;
                px[y * TexSize + x] = Shade(baseColor, pattern, u, v, x, y);
            }

            tex.SetPixels(px);
            tex.Apply(true, false);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        private static Color Shade(Color baseColor, int pattern, float u, float v, int x, int y)
        {
            float shade = 0f;
            switch (pattern)
            {
                case 0: // Horizontal log courses: rounded shading per course plus grain.
                {
                    const float courses = 5f;
                    float t = Mathf.Repeat(v * courses, 1f);
                    shade += Mathf.Cos((t - 0.5f) * Mathf.PI) * 0.34f - 0.14f;   // barrel highlight
                    if (t < 0.045f) shade -= 0.42f;                              // course shadow gap
                    shade += (Noise(u * 40f, v * 6f) - 0.5f) * 0.22f;            // long grain
                    break;
                }
                case 1: // Running-bond masonry with mortar courses.
                {
                    const float rows = 6f, cols = 4f;
                    float ry = v * rows;
                    int row = Mathf.FloorToInt(ry);
                    float offset = (row & 1) == 0 ? 0f : 0.5f;
                    float rx = Mathf.Repeat(u * cols + offset, 1f);
                    float fy = Mathf.Repeat(ry, 1f);
                    bool mortar = rx < 0.05f || rx > 0.95f || fy < 0.07f || fy > 0.93f;
                    shade += mortar ? 0.16f : -0.03f;
                    shade += (Noise(u * 26f + row * 3.1f, v * 26f) - 0.5f) * (mortar ? 0.10f : 0.26f);
                    if (!mortar) shade += Mathf.Cos((fy - 0.5f) * Mathf.PI) * 0.06f;
                    break;
                }
                case 2: // Corrugated scrap sheet: vertical ribs, seams, rust blooms.
                {
                    shade += Mathf.Sin(u * Mathf.PI * 2f * 16f) * 0.20f;
                    if (Mathf.Repeat(u * 3f, 1f) < 0.02f) shade -= 0.26f;        // sheet seams
                    float rust = Mathf.Clamp01(Noise(u * 5f, v * 5f) * 1.5f - 0.65f);
                    shade -= rust * 0.20f;
                    shade += (Noise(u * 60f, v * 60f) - 0.5f) * 0.10f;
                    return Blend(baseColor, new Color(0.40f, 0.20f, 0.10f), rust * 0.55f, shade);
                }
                case 3: // Armour plate: large panels, bevelled edges, corner rivets.
                {
                    const float panel = 2f;
                    float px2 = Mathf.Repeat(u * panel, 1f), py = Mathf.Repeat(v * panel, 1f);
                    if (px2 < 0.03f || py < 0.03f) shade -= 0.30f;
                    else if (px2 < 0.06f || py < 0.06f) shade += 0.12f;
                    float dx = Mathf.Min(px2, 1f - px2), dy = Mathf.Min(py, 1f - py);
                    if (dx < 0.14f && dy < 0.14f
                        && new Vector2(dx - 0.09f, dy - 0.09f).sqrMagnitude < 0.0022f) shade += 0.30f;
                    shade += (Noise(u * 70f, v * 70f) - 0.5f) * 0.07f;
                    break;
                }
                case 4: // Sawn timber framing: vertical studs and knotted grain.
                {
                    shade += (Noise(u * 8f, v * 44f) - 0.5f) * 0.28f;
                    if (Mathf.Repeat(u * 6f, 1f) < 0.05f) shade -= 0.22f;
                    break;
                }
                case 5: // Rough rubble: chunky, high-contrast, hand-chiselled.
                {
                    float n = Noise(u * 12f, v * 12f);
                    shade += (n - 0.5f) * 0.62f;
                    shade += (Noise(u * 45f, v * 45f) - 0.5f) * 0.22f;
                    break;
                }
                case 6: // Structural steel: flange shadows and bolt heads.
                {
                    shade += (Noise(u * 30f, v * 30f) - 0.5f) * 0.10f;
                    if (Mathf.Repeat(v * 8f, 1f) < 0.08f) shade -= 0.18f;
                    float bx = Mathf.Repeat(u * 8f, 1f) - 0.5f, by = Mathf.Repeat(v * 8f, 1f) - 0.28f;
                    if (bx * bx + by * by < 0.010f) shade += 0.26f;
                    break;
                }
                case 7: // Diamond tread plate.
                {
                    float a = Mathf.Repeat((u + v) * 9f, 1f);
                    float b = Mathf.Repeat((u - v) * 9f, 1f);
                    float d = Mathf.Min(Mathf.Abs(a - 0.5f), Mathf.Abs(b - 0.5f));
                    shade += d < 0.16f ? 0.26f - d : -0.06f;
                    shade += (Noise(u * 50f, v * 50f) - 0.5f) * 0.08f;
                    break;
                }
                default: // Hardware: dark speckled metal with fastener dots.
                {
                    shade += (Noise(u * 36f, v * 36f) - 0.5f) * 0.24f;
                    float hx = Mathf.Repeat(u * 6f, 1f) - 0.5f, hy = Mathf.Repeat(v * 6f, 1f) - 0.5f;
                    if (hx * hx + hy * hy < 0.012f) shade += 0.30f;
                    break;
                }
            }
            return Blend(baseColor, baseColor, 0f, shade);
        }

        private static Color Blend(Color baseColor, Color mix, float mixAmount, float shade)
        {
            Color c = Color.Lerp(baseColor, mix, Mathf.Clamp01(mixAmount));
            c *= 1f + shade;
            c.r = Mathf.Clamp01(c.r);
            c.g = Mathf.Clamp01(c.g);
            c.b = Mathf.Clamp01(c.b);
            c.a = 1f;
            return c;
        }

        /// <summary>Tiling value noise. Perlin alone bands badly at these frequencies.</summary>
        private static float Noise(float x, float y)
            => Mathf.PerlinNoise(x, y) * 0.65f + Mathf.PerlinNoise(x * 2.7f + 13.1f, y * 2.7f + 7.3f) * 0.35f;

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/VoxelEngineAssets"))
                AssetDatabase.CreateFolder("Assets", "VoxelEngineAssets");
            if (!AssetDatabase.IsValidFolder(Root))
                AssetDatabase.CreateFolder("Assets/VoxelEngineAssets", "Tiered");
            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder(Root, "Materials");
        }
    }
}
#endif
