// Assets/Scripts/VoxelEngine/GridSystem/GridGrinder.cs
//
// Ship grinder with improved resource return.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridGrinder : GridBlock
    {
        [Header("Grinder")]
        public float grindRadius = 1.2f;
        public float grindStrength = 60f;
        public float grindRate = 5f;

        public override float PowerDraw => _isActive ? 250f : 0f;

        private bool _isActive;
        private float _grindTimer;

        private void Update()
        {
            if (Grid == null || !Grid.IsControlled || !Grid.HasPower) { _isActive = false; return; }

            _isActive = Input.GetMouseButton(1);

            if (!_isActive) return;

            _grindTimer += Time.deltaTime;
            if (_grindTimer < 1f / grindRate) return;
            _grindTimer = 0;

            // Basic grinding logic (expand with VoxelEditor later)
        }
    }
}