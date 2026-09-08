// Assets/Scripts/VoxelEngine/GridSystem/GridBlock.cs
//
// Base class for all ship/vehicle blocks. Every block on a grid is a GridBlock.
// Subclasses: GridArmor, GridThruster, GridCockpit, GridWheel, GridDrill,
//             GridSolarPanel, GridGasTank, GridBattery, GridDemolisher.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridBlock : MonoBehaviour
    {
        [Header("Block Properties")]
        public string blockName = "Armor Block";
        public float BlockMass = 100f;
        [Tooltip("Hit points of this block.")]
        public float maxHP = 200f;
        public float currentHP;

        [Header("Grid State")]
        public Vector3Int GridPos;
        [System.NonSerialized] public GridEntity Grid;
        /// <summary>Runtime source used by save/restore to recreate this exact authored block.</summary>
        [System.NonSerialized] public VoxelEngine.Items.ItemDefinition SourceItem;
        [System.NonSerialized] public bool IsPrecisionAttachment;
        [System.NonSerialized] public Vector3Int PrecisionGridPos;
        [System.NonSerialized] public Vector3Int PrecisionHostGridPos;

        /// <summary>Physical scale of this block inside the unified Grid.</summary>
        public GridSize EffectiveGridSize => IsPrecisionAttachment
            ? GridSize.Small
            : (Grid != null ? Grid.gridSize : GridSize.Large);
        public float EffectiveCellSize => EffectiveGridSize.CellSize();

        /// <summary>Master on/off toggle (set from the ship terminal). Functional
        /// blocks should respect this — a disabled block draws no power and does
        /// no work, matching ship-terminal toggle behavior.</summary>
        public bool Enabled = true;

        /// <summary>Power this block generates (W). Override in generators.</summary>
        public virtual float PowerOutput => 0f;
        /// <summary>Power this block consumes (W). Override in consumers.</summary>
        public virtual float PowerDraw => 0f;

        /// <summary>Extra mass (kg) from this block's contents — cargo items, stored
        /// fluids, ammunition, etc. Override in storage blocks. Added to BlockMass
        /// when the grid recalculates its total mass.</summary>
        public virtual float ContentMass => 0f;

        /// <summary>Total mass of this block including its contents.</summary>
        public float TotalMass => BlockMass + ContentMass;

        /// <summary>Called when placed on a grid.</summary>
        public virtual void OnPlaced() 
        { 
            currentHP = maxHP; 
            GridBlockVisuals.ApplyDefaultVisuals(this);
        }
        /// <summary>Called when removed from a grid.</summary>
        public virtual void OnRemoved() { }

        /// <summary>0..1 structural loss (0 = pristine, 1 = destroyed).</summary>
        public float Damage01 => maxHP > 0f ? Mathf.Clamp01(1f - currentHP / maxHP) : 0f;

        /// <summary>Apply damage. Returns true if destroyed.</summary>
        public bool Damage(float amount)
        {
            if (amount <= 0f) return false;
            currentHP -= amount;
            if (currentHP <= 0)
            {
                if (Grid != null)
                {
                    if (IsPrecisionAttachment)
                        Grid.GetComponent<GridPrecisionAttachmentLayer>()?.RemoveBlock(PrecisionGridPos);
                    else
                        Grid.RemoveBlock(GridPos);
                }
                return true;
            }
            // Every source of harm — weapons, collisions, heat, plumes — now leaves a
            // visible mark: cracks widen with structural loss (9.30.0).
            VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(this, Damage01);
            return false;
        }

        /// <summary>Restore hit points (welders, repair bays). Cracks heal as HP returns.</summary>
        public void Repair(float amount)
        {
            if (amount <= 0f || maxHP <= 0f) return;
            currentHP = Mathf.Min(maxHP, currentHP + amount);
            VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(this, Damage01);
        }

        /// <summary>
        /// Push the current HP into the damage visual. Save restore and setup tooling
        /// call this after overwriting <see cref="currentHP"/> so a reloaded, battered
        /// ship still looks battered.
        /// </summary>
        public void RefreshDamageVisual()
        {
            VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(this, Damage01);
        }

        /// <summary>Create a visible block with mesh and material.</summary>
        public static T CreateBlock<T>(string name, GridSize size, Color color) where T : GridBlock
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            float cs = size.CellSize();
            go.transform.localScale = Vector3.one * cs * 0.95f; // slight gap for visibility

            var mr = go.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.6f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.4f);
            mr.material = mat;

            return go.AddComponent<T>();
        }
    }
}
