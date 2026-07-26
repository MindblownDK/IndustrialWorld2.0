// Assets/Scripts/VoxelEngine/Simulation/MachineDefinition.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — MACHINE DEFINITION (ScriptableObject)       ║
// ║  Data-driven configuration for every processing machine block.  ║
// ║  Used by the VoxelEngineSetupWindow to generate prefabs without  ║
// ║  overwriting user-tuned balance values.                         ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.UI;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Category determines which machine implementation handles the block.
    /// </summary>
    public enum MachineCategory
    {
        Furnace,
        Crusher,
        Assembler,
        ChemicalPlant,
        Custom
    }

    [CreateAssetMenu(menuName = "Voxel Engine/Simulation/Machine Definition", fileName = "MachineDef_New")]
    public class MachineDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string machineId = "electric_furnace";
        public string displayName = "Electric Furnace";
        [TextArea(2, 4)] public string description;
        public Sprite icon;

        [Header("Category")]
        public MachineCategory category = MachineCategory.Furnace;

        [Header("Power")]
        [Tooltip("Watts drawn while actively processing.")]
        public float activeWatts = 200f;
        [Tooltip("Watts drawn while idle (keeping warm / standby).")]
        public float idleWatts = 5f;

        [Header("Speed")]
        [Tooltip("Base processing time multiplier. Lower = faster.")]
        public float speedMultiplier = 1f;

        [Header("Slots")]
        [Tooltip("Number of input slots.")]
        public int inputSlots = 1;
        [Tooltip("Number of output slots.")]
        public int outputSlots = 4;
        [Tooltip("Number of upgrade/module slots.")]
        public int upgradeSlots = 4;

        [Header("Visual")]
        [Tooltip("Optional per-machine UI theme override. IndustrialSteel means use the global theme unless useCustomAccent is enabled.")]
        public BuiltInUITheme themeOverride = BuiltInUITheme.IndustrialSteel;
        [Tooltip("When true, uiAccentOverride is used for this machine's UI accent.")]
        public bool useCustomAccent;
        public Color uiAccentOverride = new(0.18f, 0.72f, 0.88f, 1f);

        [Tooltip("Primary colour for the block mesh and UI accents.")]
        public Color primaryColor = new(0.35f, 0.40f, 0.48f, 1f);
        [Tooltip("Emissive colour for status LEDs when active.")]
        public Color activeEmissive = new(0.22f, 0.78f, 0.42f, 1f);
        [Tooltip("Emissive colour for status LEDs when idle.")]
        public Color idleEmissive = new(0.88f, 0.72f, 0.22f, 1f);

        [Header("Prefab")]
        [Tooltip("Optional prefab override. When null, the setup wizard generates one.")]
        public GameObject prefab;

        /// <summary>Fallback icon from the first output item if no icon assigned.</summary>
        public Sprite GetIcon() => icon;
    }
}
