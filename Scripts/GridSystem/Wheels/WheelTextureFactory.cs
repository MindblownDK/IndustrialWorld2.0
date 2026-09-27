// Assets/Scripts/VoxelEngine/GridSystem/Wheels/WheelTextureFactory.cs
//
// PROCEDURAL RUBBER AND STEEL.
//
// The wheel overhaul needed real surface detail without dragging an art pack into
// the repo, so every map here is generated: tread rubber, machined hub steel,
// chromed strut. Each map is produced once per session, cached by key, and handed
// to the optional persister so editor tooling can bake it to a .png asset — a
// prefab must never reference a runtime-only texture, which is exactly how blocks
// end up magenta after a domain reload.
//
// Everything is authored tile-able on both axes and generated at modest resolution:
// these are read at a distance on a moving vehicle, and a 512² albedo plus its
// normal is far cheaper than the lighting cost of the wheel it dresses.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public static class WheelTextureFactory
    {
        /// <summary>
        /// Editor hook: (texture, suggestedAssetName) → the texture to actually use.
        /// Left null at runtime so play-mode generation simply uses the memory texture.
        /// </summary>
        public static System.Func<Texture2D, string, Texture2D> TexturePersister;

        private static readonly Dictionary<string, Texture2D> s_cache = new Dictionary<string, Texture2D>(8);

        public static void ClearCache() => s_cache.Clear();

        // ════════════════════════════════════════════════════════════════════
        //  PUBLIC MAPS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Deep-lug tread rubber. Dark, matte, with block lugs and sipes.</summary>
        public static Texture2D TreadAlbedo(int size = 512) => Get($"WheelTread_A_{size}", () =>
        {
            var tex = New(size, "WheelTread_Albedo");
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float lug = LugMask(u, v);
                    float grain = Noise(u * 64f, v * 64f) * 0.06f;
                    float wear = Mathf.Lerp(0.85f, 1.0f, Noise(u * 6f, v * 6f));
                    float value = Mathf.Lerp(0.055f, 0.115f, lug) * wear + grain;
                    pixels[y * size + x] = Gray(value, 1f);
                }
            }
            tex.SetPixels32(pixels); tex.Apply(true, false);
            return tex;
        });

        /// <summary>Matching tread normal map: the lugs stand proud, the sipes cut in.</summary>
        public static Texture2D TreadNormal(int size = 512) => Get($"WheelTread_N_{size}", () =>
            NormalFromHeight(size, "WheelTread_Normal", (u, v) => LugMask(u, v) * 0.9f + Noise(u * 48f, v * 48f) * 0.08f, 3.4f));

        /// <summary>Machined hub steel: brushed circumferentially with bolt-ring speckle.</summary>
        public static Texture2D HubSteelAlbedo(int size = 512) => Get($"HubSteel_A_{size}", () =>
        {
            var tex = New(size, "HubSteel_Albedo");
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float brush = Noise(u * 220f, v * 3f) * 0.16f;          // fine lathe lines
                    float plate = Mathf.Lerp(0.42f, 0.56f, Noise(u * 5f, v * 5f));
                    float grime = Mathf.Clamp01(Noise(u * 11f + 3.1f, v * 11f - 2.4f) - 0.55f) * 0.5f;
                    float value = Mathf.Clamp01(plate + brush - grime);
                    var c = new Color(value * 0.98f, value * 0.99f, value);  // faintly cold steel
                    pixels[y * size + x] = c;
                }
            }
            tex.SetPixels32(pixels); tex.Apply(true, false);
            return tex;
        });

        public static Texture2D HubSteelNormal(int size = 512) => Get($"HubSteel_N_{size}", () =>
            NormalFromHeight(size, "HubSteel_Normal", (u, v) => Noise(u * 180f, v * 4f) * 0.35f + Noise(u * 9f, v * 9f) * 0.3f, 1.4f));

        /// <summary>Chromed strut rod: near-mirror with a faint drawn-tube streak.</summary>
        public static Texture2D ChromeAlbedo(int size = 256) => Get($"Chrome_A_{size}", () =>
        {
            var tex = New(size, "Chrome_Albedo");
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float streak = Noise(u * 320f, v * 2f) * 0.1f;
                    float value = Mathf.Clamp01(0.78f + streak - Mathf.Clamp01(Noise(u * 7f, v * 7f) - 0.7f) * 0.6f);
                    pixels[y * size + x] = Gray(value, 1f);
                }
            }
            tex.SetPixels32(pixels); tex.Apply(true, false);
            return tex;
        });

        // ════════════════════════════════════════════════════════════════════
        //  MATERIALS
        // ════════════════════════════════════════════════════════════════════

        private static Shader Lit => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        /// <summary>Textured URP material; routes through the mesh builder's persister.</summary>
        public static Material Make(string name, Color tint, Texture2D albedo, Texture2D normal,
            float metallic, float smoothness, Vector2 tiling)
        {
            var mat = new Material(Lit) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            mat.color = tint;
            if (albedo != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", albedo);
                mat.mainTexture = albedo;
                if (mat.HasProperty("_BaseMap")) mat.SetTextureScale("_BaseMap", tiling);
                mat.mainTextureScale = tiling;
            }
            if (normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.EnableKeyword("_NORMALMAP");
                mat.SetTexture("_BumpMap", normal);
                mat.SetTextureScale("_BumpMap", tiling);
            }
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

            var persister = GridBlockMeshBuilder.MaterialPersister;
            return persister != null ? persister(mat, name) : mat;
        }

        public static Material Rubber(Vector2 tiling) => Make("Wheel_Rubber",
            new Color(0.80f, 0.80f, 0.82f), TreadAlbedo(), TreadNormal(), 0.05f, 0.18f, tiling);

        public static Material Steel(Vector2 tiling) => Make("Wheel_HubSteel",
            new Color(0.78f, 0.80f, 0.84f), HubSteelAlbedo(), HubSteelNormal(), 0.88f, 0.52f, tiling);

        public static Material PaintedSteel(Color tint, Vector2 tiling) => Make("Wheel_Painted",
            tint, HubSteelAlbedo(), HubSteelNormal(), 0.45f, 0.42f, tiling);

        public static Material Chrome(Vector2 tiling) => Make("Wheel_Chrome",
            new Color(0.92f, 0.94f, 0.97f), ChromeAlbedo(), null, 0.98f, 0.88f, tiling);

        // ════════════════════════════════════════════════════════════════════
        //  GENERATION HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static Texture2D Get(string key, System.Func<Texture2D> build)
        {
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var tex = build();
            var persister = TexturePersister;
            if (persister != null)
            {
                var persisted = persister(tex, key);
                if (persisted != null) tex = persisted;
            }
            s_cache[key] = tex;
            return tex;
        }

        private static Texture2D New(int size, string name) => new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = name,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4
        };

        private static Color32 Gray(float value, float alpha)
        {
            byte b = (byte)(Mathf.Clamp01(value) * 255f);
            return new Color32(b, b, b, (byte)(Mathf.Clamp01(alpha) * 255f));
        }

        /// <summary>Directional lug blocks with a chamfered shoulder and a central rib.</summary>
        private static float LugMask(float u, float v)
        {
            const int lugRows = 14;
            float row = v * lugRows;
            int rowIndex = Mathf.FloorToInt(row);
            float rowFrac = row - rowIndex;
            // Alternate rows shift so the tread reads as a directional V pattern.
            float shift = (rowIndex % 2 == 0) ? 0f : 0.5f;
            float su = Mathf.Repeat(u + shift + rowFrac * 0.18f, 1f);

            float rib = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(su - 0.5f) * 7f);       // centre rib
            float block = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(Mathf.Repeat(su * 4f, 1f) - 0.5f) * 3.2f);
            float groove = Mathf.SmoothStep(0f, 1f, Mathf.Abs(rowFrac - 0.5f) * 2.6f);   // lateral groove
            return Mathf.Clamp01(Mathf.Max(rib, block) * groove);
        }

        /// <summary>Cheap value noise — deterministic, tile-safe, no Unity Perlin seams.</summary>
        private static float Noise(float x, float y)
        {
            float s = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        /// <summary>Sobel-derives a tangent-space normal map from a height function.</summary>
        private static Texture2D NormalFromHeight(int size, string name, System.Func<float, float, float> height, float strength)
        {
            var tex = New(size, name);
            var pixels = new Color32[size * size];
            float step = 1f / size;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x * step, v = y * step;
                    float hL = height(Mathf.Repeat(u - step, 1f), v);
                    float hR = height(Mathf.Repeat(u + step, 1f), v);
                    float hD = height(u, Mathf.Repeat(v - step, 1f));
                    float hU = height(u, Mathf.Repeat(v + step, 1f));
                    Vector3 n = new Vector3((hL - hR) * strength, (hD - hU) * strength, 1f).normalized;
                    pixels[y * size + x] = new Color32(
                        (byte)((n.x * 0.5f + 0.5f) * 255f),
                        (byte)((n.y * 0.5f + 0.5f) * 255f),
                        (byte)((n.z * 0.5f + 0.5f) * 255f),
                        255);
                }
            }
            tex.SetPixels32(pixels); tex.Apply(true, false);
            return tex;
        }
    }
}
