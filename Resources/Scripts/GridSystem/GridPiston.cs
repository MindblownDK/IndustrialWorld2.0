// Assets/Scripts/VoxelEngine/GridSystem/GridPiston.cs
using UnityEngine;
using System.Collections;

namespace VoxelEngine.GridSystem
{
    /// <summary>
    /// A grid-based piston that can push and pull blocks above it.
    /// </summary>
    public class GridPiston : GridBlock
    {
        [Header("Piston Settings")]
        public float targetLength = 0f;
        public float currentLength = 0f;
        public float extensionSpeed = 2f;
        public float maxExtension = 20f;
        public bool isExtended = false;

        [Header("Visuals")]
        public GameObject pistonHead;
        public GameObject pistonBase;

        private Coroutine _moveCoroutine;

        public override void OnPlaced()
        {
            base.OnPlaced();
            // Ensure visuals are set up
            if (pistonHead == null) pistonHead = transform.Find("Head")?.gameObject;
            if (pistonBase == null) pistonBase = transform.Find("Base")?.gameObject;
        }

        public void Toggle()
        {
            isExtended = !isExtended;
            float goal = isExtended ? targetLength : 0f;
            
            if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);
            _moveCoroutine = StartCoroutine(MovePiston(goal));
        }

        private IEnumerator MovePiston(float goal)
        {
            while (!Mathf.Approximately(currentLength, goal))
            {
                float step = Time.deltaTime * extensionSpeed;
                currentLength = Mathf.MoveTowards(currentLength, goal, step);
                
                // Update Visuals
                if (pistonHead != null)
                {
                    pistonHead.transform.localPosition = new Vector3(0, currentLength, 0);
                }

                // Update Target Block Position
                UpdateTargetPosition();
                
                yield return null;
            }
        }

        private void UpdateTargetPosition()
        {
            // Find the block directly above the piston in the grid
            if (Grid == null) return;
            
            Vector3Int abovePos = GridPos + Vector3Int.up;
            GridBlock target = Grid.GetBlock(abovePos);

            if (target != null)
            {
                // We apply a local offset to the target block to simulate the push
                // This assumes GridBlock handles local offsets for visual movement
                target.transform.localPosition += Vector3.up * (currentLength - (currentLength - Time.deltaTime * extensionSpeed));
                // Note: Real grid shifting would require modifying GridEntity.Blocks
                // For this implementation, we translate the transform.
            }
        }

        // UI hook for the player to change settings
        public void SetLength(float len) => targetLength = Mathf.Clamp(len, 0, maxExtension);
        public void SetSpeed(float spd) => extensionSpeed = Mathf.Max(0.1f, spd);
    }
}
