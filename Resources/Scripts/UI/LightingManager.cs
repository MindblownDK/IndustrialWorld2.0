// Assets/Scripts/VoxelEngine/UI/LightingManager.cs
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.UI
{
    public class LightingManager : MonoBehaviour
    {
        public static LightingManager Instance { get; private set; }

        public VoxelLightController SelectedLight { get; private set; }
        public System.Action<VoxelLightController> OnLightSelected;
        public System.Action OnLightDeselected;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        public void SelectLight(VoxelLightController light)
        {
            SelectedLight = light;
            OnLightSelected?.Invoke(light);
        }

        public void DeselectLight()
        {
            SelectedLight = null;
            OnLightDeselected?.Invoke();
        }
    }
}
