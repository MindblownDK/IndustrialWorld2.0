// Assets/Scripts/VoxelEngine/Rendering/GPUResidentDrawerValidator.cs
//
// In some Unity 6 point releases the GPU Resident Drawer types live in
// UnityEngine.Rendering.Universal, in others they were moved to a separate
// Unity.RenderPipelines.GPUDriven.Runtime assembly. To stay version-agnostic
// (and avoid hard-linking against an assembly that may or may not exist), we
// query everything via reflection. No package manifest changes required.

using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelEngine.Rendering
{
    [DefaultExecutionOrder(-1000)]
    public class GPUResidentDrawerValidator : MonoBehaviour
    {
        public bool logOnStart = true;

        private void Start()
        {
            if (logOnStart) Validate();
        }

        public static void Validate()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null)
            {
                Debug.LogWarning("[VoxelEngine] No active Render Pipeline. " +
                                 "GPU Resident Drawer requires URP (Forward+) or HDRP.");
                return;
            }

            // Look for a property named 'gpuResidentDrawerMode' on the URP/HDRP asset.
            var prop = rp.GetType().GetProperty(
                "gpuResidentDrawerMode",
                BindingFlags.Public | BindingFlags.Instance);

            if (prop == null)
            {
                Debug.Log("[VoxelEngine] GPU Resident Drawer property not found on " +
                          rp.GetType().Name +
                          " — your Unity version may expose it elsewhere. " +
                          "Check URP Asset ▸ Rendering ▸ GPU Resident Drawer manually.");
                return;
            }

            var value = prop.GetValue(rp);
            string mode = value != null ? value.ToString() : "null";

            // Enum names across versions: 'InstancedDrawing' (URP) — anything that
            // isn't 'Disabled' counts as enabled.
            bool enabled = !string.Equals(mode, "Disabled", System.StringComparison.OrdinalIgnoreCase);

            if (enabled)
            {
                Debug.Log($"[VoxelEngine] GPU Resident Drawer: ENABLED ({mode}) ✅");
            }
            else
            {
                Debug.LogWarning(
                    "[VoxelEngine] GPU Resident Drawer is OFF.\n" +
                    "Enable it via: URP Asset ▸ Rendering ▸ GPU Resident Drawer = 'Instanced Drawing'.\n" +
                    "Also requires: Forward+ rendering path, SRP Batcher ON, " +
                    "BatchRendererGroup Variants = 'Keep All', Static Batching OFF.");
            }
        }
    }
}
