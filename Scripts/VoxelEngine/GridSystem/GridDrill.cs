// Assets/Scripts/VoxelEngine/GridSystem/GridDrill.cs
//
// Ship-mounted drill block. Mines terrain when activated.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridDrill : GridBlock
    {
        [Header("Drill")]
        public float drillRadius = 2f;
        public float drillStrength = 120f;
        public float drillRate = 3f;

        public override float PowerDraw => _isActive ? 450f : 0f;

        public bool IsActive => _isActive;

        private bool _isActive;
        private float _drillTimer;

        private void Update()
        {
            if (Grid == null || !Grid.IsControlled || !Grid.HasPower) { _isActive = false; return; }

            _isActive = Input.GetMouseButton(0);

            if (!_isActive) return;

            _drillTimer += Time.deltaTime;
            if (_drillTimer < 1f / drillRate) return;
            _drillTimer = 0;

            // Mining logic would go here (integrate with VoxelEditor)
        }
    }
}