using UnityEngine.UIElements;
using VoxelEngine.UI;

namespace IndustrialWorld.Navigation
{
    /// <summary>Explicit foregrounds override Unity's light-theme field defaults on dark panels.</summary>
    public static class NavigationFieldStyle
    {
        public static void Apply(VisualElement field)
        {
            if (field == null) return;
            field.style.color = UITheme.TextPrimary;
            field.style.minHeight = 28;
            field.style.marginTop = 5;
            field.style.marginBottom = 5;
            Paint(field);
            field.RegisterCallback<AttachToPanelEvent>(_ => field.schedule.Execute(() => Paint(field)));
            if (field is TextField)
            {
                field.RegisterCallback<FocusInEvent>(_ => UIState.TextInputActive = true);
                field.RegisterCallback<FocusOutEvent>(_ => UIState.TextInputActive = false);
                field.RegisterCallback<DetachFromPanelEvent>(_ => UIState.TextInputActive = false);
            }
        }

        private static void Paint(VisualElement field)
        {
            field.Query<TextElement>().ForEach(text => text.style.color = UITheme.TextPrimary);
            field.Query<VisualElement>().ForEach(element =>
            {
                if (element.ClassListContains("unity-base-field__label"))
                    element.style.color = UITheme.TextPrimary;
                if (element.ClassListContains("unity-base-field__input")
                    || element.ClassListContains("unity-base-text-field__input"))
                {
                    element.style.backgroundColor = UITheme.BgSlot;
                    element.style.color = UITheme.TextPrimary;
                    element.style.borderBottomColor = UITheme.AccentCyan;
                    element.style.borderBottomWidth = 1;
                }
            });
        }
    }
}
