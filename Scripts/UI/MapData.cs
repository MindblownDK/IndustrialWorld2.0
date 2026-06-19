// Assets/Scripts/VoxelEngine/UI/MapData.cs
//
// Top-down voxel map texture renderer. Time-sliced to avoid frame hitches.
//
// Performance notes:
//   - RenderMap walks the world voxels one column at a time. Each column scans top→down.
//   - We early-out at the first solid voxel (avg ~10 reads instead of 256).
//   - For minimap (96px), we render the WHOLE image once per call; the caller throttles.
//   - For the full map (512px), use RenderMapAsync — yields between rows to spread cost.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.UI
{
    [System.Serializable]
    public class Waypoint
    {
        public string name = "Waypoint";
        public Vector3 worldPos;
        public Color color = Color.yellow;
    }

    public static class MapData
    {
        public static readonly List<Waypoint> Waypoints = new();

        public static void AddWaypoint(string name, Vector3 worldPos)
        {
            Waypoints.Add(new Waypoint { name = name, worldPos = worldPos, color = Color.yellow });
            Save();
        }
        public static void RemoveWaypoint(Waypoint wp) { Waypoints.Remove(wp); Save(); }

        public static void Save()
        {
            var session = Menu.WorldSession.Instance;
            string key = "ve.waypoints." + (session != null ? session.worldName : "default");
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            for (int i = 0; i < Waypoints.Count; i++)
            {
                var w = Waypoints[i];
                sb.Append($"{{\"n\":\"{w.name}\",\"x\":{w.worldPos.x.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                          $"\"y\":{w.worldPos.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                          $"\"z\":{w.worldPos.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
                if (i < Waypoints.Count - 1) sb.Append(",");
            }
            sb.Append("]");
            PlayerPrefs.SetString(key, sb.ToString());
            PlayerPrefs.Save();
        }

        public static void Load()
        {
            Waypoints.Clear();
            var session = Menu.WorldSession.Instance;
            string key = "ve.waypoints." + (session != null ? session.worldName : "default");
            string s = PlayerPrefs.GetString(key, "[]");
            var rx = new System.Text.RegularExpressions.Regex(
                "\"n\":\"([^\"]*)\",\"x\":(-?[0-9.eE+-]+),\"y\":(-?[0-9.eE+-]+),\"z\":(-?[0-9.eE+-]+)");
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(s))
            {
                Waypoints.Add(new Waypoint {
                    name = m.Groups[1].Value,
                    worldPos = new Vector3(
                        float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture)),
                    color = Color.yellow
                });
            }
        }

        // ============================================================
        //                          RENDER
        // ============================================================
        /// <summary>Synchronous render — full texture in one call. Suited for SMALL minimap textures.</summary>
        public static void RenderMap(Texture2D tex, Vector3 centerWorld, int radius, int texSize)
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return;
            var pixels = tex.GetPixels32();
            int cx = Mathf.FloorToInt(centerWorld.x);
            int cz = Mathf.FloorToInt(centerWorld.z);
            float scale = (radius * 2f) / texSize;

            for (int py = 0; py < texSize; py++)
            {
                int wzBase = cz + Mathf.RoundToInt((py - texSize * 0.5f) * scale);
                int rowOffset = py * texSize;
                for (int px = 0; px < texSize; px++)
                {
                    int wx = cx + Mathf.RoundToInt((px - texSize * 0.5f) * scale);
                    pixels[rowOffset + px] = SampleColumnColor(world, wx, wzBase);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);
        }

        /// <summary>Coroutine-friendly time-sliced render. Yields after every `rowsPerYield` rows.</summary>
        public static IEnumerator RenderMapAsync(Texture2D tex, Vector3 centerWorld, int radius, int texSize, int rowsPerYield = 16)
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) yield break;
            var pixels = tex.GetPixels32();
            int cx = Mathf.FloorToInt(centerWorld.x);
            int cz = Mathf.FloorToInt(centerWorld.z);
            float scale = (radius * 2f) / texSize;

            for (int py = 0; py < texSize; py++)
            {
                int wzBase = cz + Mathf.RoundToInt((py - texSize * 0.5f) * scale);
                int rowOffset = py * texSize;
                for (int px = 0; px < texSize; px++)
                {
                    int wx = cx + Mathf.RoundToInt((px - texSize * 0.5f) * scale);
                    pixels[rowOffset + px] = SampleColumnColor(world, wx, wzBase);
                }
                if ((py % rowsPerYield) == 0) yield return null;
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);
        }

        public static Color32 SampleColumn(VoxelEngine.Core.IVoxelWorld world, int wx, int wz) => SampleColumnColor(world, wx, wz);

        public static Color32 SampleColumnColor(VoxelEngine.Core.IVoxelWorld world, int wx, int wz)
        {
            // Walk DOWN from the world ceiling. Most surface columns hit a solid in <10 reads.
            // Cap the scan at WORLD_HEIGHT to avoid runaway costs.
            for (int wy = VoxelConstants.WORLD_HEIGHT_VOXELS - 1; wy >= 1; wy--)
            {
                var v = world.GetVoxelWorld(new Vector3Int(wx, wy, wz));
                if (v.density <= 0) continue;
                byte mat = v.material;
                if (mat == (byte)Materials.MaterialId.WaterVoxel || mat == (byte)Materials.MaterialId.WaterLiquid)
                    return new Color32(40, 90, 180, 255);
                return MaterialPalette(mat);
            }
            return new Color32(20, 20, 25, 255);
        }

        private static Color32 MaterialPalette(byte mat)
        {
            switch ((Materials.MaterialId)mat)
            {
                case Materials.MaterialId.Stone:     return new Color32(110, 110, 115, 255);
                case Materials.MaterialId.Sand:      return new Color32(225, 210, 140, 255);
                case Materials.MaterialId.Clay:      return new Color32(110,  80,  50, 255);
                case Materials.MaterialId.Ice:       return new Color32(200, 230, 250, 255);
                case Materials.MaterialId.Iron:      return new Color32(140, 100,  85, 255);
                case Materials.MaterialId.Copper:    return new Color32(180, 115,  55, 255);
                case Materials.MaterialId.Coal:      return new Color32( 40,  40,  45, 255);
                case Materials.MaterialId.Gold:      return new Color32(230, 200,  60, 255);
                case Materials.MaterialId.Silver:    return new Color32(210, 215, 220, 255);
                case Materials.MaterialId.Platinum:  return new Color32(195, 200, 210, 255);
                case Materials.MaterialId.Uranium:   return new Color32( 80, 140,  50, 255);
                case Materials.MaterialId.CrudeOil:  return new Color32( 25,  20,  20, 255);
                default:                              return new Color32(150, 150, 150, 255);
            }
        }
    }
}
