// Assets/Scripts/VoxelEngine/Research/ResearchTree.cs
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Research
{
    [CreateAssetMenu(menuName = "Voxel Engine/Research/Tree", fileName = "ResearchTree")]
    public class ResearchTree : ScriptableObject
    {
        public List<ResearchNode> nodes = new();
    }
}
