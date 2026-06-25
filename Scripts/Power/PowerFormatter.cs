// Assets/Scripts/VoxelEngine/Power/PowerFormatter.cs
using UnityEngine;
using System.Globalization;

namespace VoxelEngine.Power
{
    public static class PowerFormatter
    {
        public static string FormatWatts(float watts)
        {
            float absWatts = Mathf.Abs(watts);
            string sign = watts < 0 ? "-" : "";

            if (absWatts < 1000f) 
                return $"{sign}{watts:F2} W";
            if (absWatts < 1000000f) 
                return $"{sign}{(watts / 1000f):F2} kW";
            if (absWatts < 1000000000f) 
                return $"{sign}{(watts / 1000000f):F2} MW";
            if (absWatts < 1000000000000f) 
                return $"{sign}{(watts / 1000000000f):F2} GW";
            
            return $"{sign}{(watts / 1000000000000f):F2} TW";
        }
    }
}
