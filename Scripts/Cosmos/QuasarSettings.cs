// Assets/Scripts/VoxelEngine/Cosmos/QuasarSettings.cs
using System;
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Background aesthetic: a colossal, glowing quasar pinned to the deep-space skybox.
    /// Purely visual (rendered in Phase 6) — its data lives in the system template so every
    /// solar system can feature a distinct backdrop.
    /// </summary>
    [Serializable]
    public class QuasarSettings
    {
        public bool enabled = true;

        [Tooltip("Bright accretion-disc core colour.")]
        public Color coreColor = new Color(0.62f, 0.82f, 1.0f, 1f);

        [Tooltip("Colour of the relativistic polar jets.")]
        public Color jetColor = new Color(0.40f, 0.60f, 1.0f, 0.9f);

        [Range(0f, 8f)]
        [Tooltip("Overall brightness/bloom strength.")]
        public float brightness = 1.4f;

        [Tooltip("Direction (from origin) the quasar sits at on the skybox. Normalised at runtime.")]
        public Vector3 skyDirection = new Vector3(0.3f, 0.4f, 1f);

        [Range(0.1f, 4f)]
        [Tooltip("Apparent angular size of the core on screen.")]
        public float apparentSize = 1.2f;
    }
}
