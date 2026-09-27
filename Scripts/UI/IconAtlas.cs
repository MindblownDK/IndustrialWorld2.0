// Assets/Scripts/VoxelEngine/UI/IconAtlas.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║             INDUSTRIAL WORLD — PROCEDURAL ICON CACHE              ║
// ║                                                                  ║
// ║  One session-lifetime cache behind every drawn icon in the game. ║
// ║  Callers describe a drawing once; the mask is rasterised on the  ║
// ║  first request and handed back on every request after that.      ║
// ║                                                                  ║
// ║  Masks are pure white with an alpha channel, so a single texture ║
// ║  serves idle, hovered, unaffordable and locked states: the UI    ║
// ║  tints it at draw time instead of storing four bitmaps.          ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.UI
{
    public static class IconAtlas
    {
        public const int   DefaultSize   = 128;
        public const float DefaultStroke = 4.2f;
        public const float DefaultPad    = 9f;

        private static readonly Dictionary<string, Texture2D> _cache = new(64);

        /// <summary>
        /// Mask for <paramref name="key"/>, rasterising <paramref name="draw"/> the
        /// first time it is asked for. The key must be unique across the whole game;
        /// prefix it with the owning system (for example "build:Wall").
        /// </summary>
        public static Texture2D Get(string key, Action<LineArtBuilder> draw,
                                    int size = DefaultSize, float stroke = DefaultStroke, float pad = DefaultPad)
        {
            if (string.IsNullOrEmpty(key) || draw == null) return null;
            if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var builder = new LineArtBuilder();
            draw(builder);
            var texture = builder.Rasterize(size, stroke, pad, "Icon_" + key);
            _cache[key] = texture;
            return texture;
        }

        /// <summary>Releases every cached mask. Rarely needed — masks are tiny and shared.</summary>
        public static void Clear()
        {
            foreach (var pair in _cache)
            {
                if (pair.Value == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(pair.Value);
                else UnityEngine.Object.DestroyImmediate(pair.Value);
            }
            _cache.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad() => _cache.Clear();
    }
}
