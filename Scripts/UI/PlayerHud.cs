// Assets/Scripts/VoxelEngine/UI/PlayerHud.cs
//
// DEPRECATED — the old bottom-left HP/SP bars.
// Now a no-op stub so existing code that calls PlayerHud.EnsureMounted / Tick compiles
// without changes. All vitals are rendered by VitalsHud instead.

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    public static class PlayerHud
    {
        // No-op stubs — VitalsHud handles everything now.
        public static void EnsureMounted(VisualElement uiRoot) { }
        public static void Tick() { }
    }
}
