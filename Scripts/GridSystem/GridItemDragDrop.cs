// Assets/Scripts/VoxelEngine/GridSystem/GridItemDragDrop.cs
//
// Full drag-and-drop system with pointer events and visual feedback.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public static class GridItemDragDrop
    {
        public static ItemStack DraggedItem { get; private set; }
        public static GridCargoContainer SourceContainer { get; private set; }
        public static VisualElement DragVisual { get; private set; }

        public static void StartDrag(ItemStack item, GridCargoContainer source, VisualElement root)
        {
            DraggedItem = item;
            SourceContainer = source;

            // Create floating drag visual
            DragVisual = new VisualElement();
            DragVisual.style.position = Position.Absolute;
            DragVisual.style.width = 60;
            DragVisual.style.height = 60;
            DragVisual.style.backgroundColor = new Color(0.2f, 0.6f, 1f, 0.8f);
            DragVisual.style.borderTopLeftRadius = 8;
            DragVisual.style.borderTopRightRadius = 8;
            DragVisual.style.borderBottomLeftRadius = 8;
            DragVisual.style.borderBottomRightRadius = 8;

            root.Add(DragVisual);
        }

        public static void UpdateDragPosition(Vector2 mousePosition)
        {
            if (DragVisual != null)
            {
                DragVisual.style.left = mousePosition.x - 30;
                DragVisual.style.top = mousePosition.y - 30;
            }
        }

        public static void DropOn(GridCargoContainer target)
        {
            if (DraggedItem == null || target == null || SourceContainer == null) return;

            var leftover = target.container.Insert(DraggedItem);
            if (leftover == null || leftover.count <= 0)
            {
                SourceContainer.container.Remove(DraggedItem.item, DraggedItem.count);
            }

            CleanupDrag();
        }

        public static void CancelDrag()
        {
            CleanupDrag();
        }

        private static void CleanupDrag()
        {
            if (DragVisual != null)
            {
                DragVisual.RemoveFromHierarchy();
                DragVisual = null;
            }

            DraggedItem = null;
            SourceContainer = null;
        }
    }
}