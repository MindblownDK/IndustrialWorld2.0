// Assets/Scripts/VoxelEngine/Power/Wind/WindmillMonopole.cs
// Beautiful heavy-duty monopole for placing large windmills in water.
// Extends deep into seafloor. Stationary.

using UnityEngine;

namespace VoxelEngine.Power.Wind
{
    public class WindmillMonopole : MonoBehaviour
    {
        [Header("Monopole Settings")]
        [Tooltip("How far the monopole extends into the seafloor")]
        public float seafloorDepth = 22f;

        [Tooltip("Width of the monopole foundation")]
        public float diameter = 5.5f;

        public void PlaceAt(Vector3 surfacePosition)
        {
            transform.position = surfacePosition;

            // Extend downward
            var pole = transform.Find("Monopole") ?? CreatePole();
            pole.localPosition = new Vector3(0, -seafloorDepth / 2f - 1f, 0);
            pole.localScale = new Vector3(diameter, seafloorDepth / 2f, diameter);
        }

        private Transform CreatePole()
        {
            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Monopole";
            pole.transform.SetParent(transform);
            pole.transform.localPosition = Vector3.zero;

            var rend = pole.GetComponent<Renderer>();
            if (rend != null)
            {
                // Will be replaced by beautiful material from generator
                rend.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    color = new Color(0.48f, 0.5f, 0.55f)
                };
            }
            return pole.transform;
        }
    }
}
