// Assets/Scripts/VoxelEngine/Power/Wind/WindmillMonopole.cs
using UnityEngine;

namespace VoxelEngine.Power.Wind
{
    public class WindmillMonopole : MonoBehaviour
    {
        [Tooltip("Depth the pole goes into the seafloor")]
        public float seafloorDepth = 10f;
        
        public void PlaceAt(Vector3 position)
        {
            transform.position = position;
            // The pole goes down from the surface
            transform.localPosition = new Vector3(0, -seafloorDepth / 2f, 0);
        }
    }
}
