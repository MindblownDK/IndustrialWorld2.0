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

        /// <summary>Apply damage. Returns true if destroyed.</summary>
        public bool Damage(float amount)
        {
            currentHP -= amount;
            if (currentHP <= 0)
            {
                if (Grid != null) Grid.RemoveBlock(GridPos);
                return true;
            }
            return false;
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
