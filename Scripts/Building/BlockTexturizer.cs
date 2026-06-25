// Assets/Scripts/VoxelEngine/Building/BlockTexturizer.cs
//
// Applies a BlockItem.placedMaterial / texture override to every renderer on the block
// the moment it's placed. The texturizer self-removes after application so it doesn't
// re-apply each frame.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building
{
    public class BlockTexturizer : MonoBehaviour
    {
        public Material overrideMaterial;
        public Texture2D overrideTexture;

        private void Start()
        {
            Apply();
            Destroy(this); // one-shot
        }

        public void Apply()
        {
            if (overrideMaterial == null && overrideTexture == null) return;
            var renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (overrideMaterial != null)
                {
                    var arr = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < arr.Length; i++) arr[i] = overrideMaterial;
                    r.sharedMaterials = arr;
                }
                if (overrideTexture != null)
                {
                    // Apply to the first material's main texture (works for URP Lit / Standard).
                    var mat = r.sharedMaterial;
                    if (mat != null && mat.HasProperty("_BaseMap"))    mat.SetTexture("_BaseMap", overrideTexture);
                    else if (mat != null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", overrideTexture);
                }
            }
        }
    }
}
