// Assets/Scripts/VoxelEngine/Player/WaterLensEffect.cs
//
// Camera lens droplet effect when transitioning from underwater to above water.
// Renders a procedurally animated droplet overlay on the screen using UI Toolkit.
// The effect fades in when surfacing and naturally drips/evaporates over ~3 seconds.
//
// Unity Setup:
//   1. Add this component to the same GameObject as the Camera (typically under PlayerController)
//   2. The component creates its own UIDocument at runtime — no scene setup needed
//   3. Requires the UIDocument package (com.unity.ui)

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.Player
{
    [RequireComponent(typeof(Camera))]
    public class WaterLensEffect : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds for droplets to fully evaporate after surfacing.")]
        public float dryTime = 3.0f;
        [Tooltip("Maximum opacity of the droplet overlay.")]
        public float maxOpacity = 0.35f;

        private UnderwaterEffect _uwEffect;
        private bool _wasUnderwater;
        private float _wetTimer; // counts up from 0 when surfacing
        private VisualElement _overlay;
        private UIDocument _doc;

        private void Awake()
        {
            _uwEffect = GetComponent<UnderwaterEffect>();
        }

        private void Update()
        {
            bool isUnderwater = _uwEffect != null && _uwEffect.IsUnderwater;

            // Detect surface transition
            if (_wasUnderwater && !isUnderwater)
            {
                _wetTimer = 0f;
                ShowOverlay();
            }

            _wasUnderwater = isUnderwater;

            // If we just dove back under, hide immediately
            if (isUnderwater)
            {
                HideOverlay();
                return;
            }

            // Animate drying
            if (_wetTimer < dryTime && _overlay != null)
            {
                _wetTimer += Time.deltaTime;
                float t = _wetTimer / dryTime;

                // Droplets shrink and fade as they "evaporate"
                float opacity = maxOpacity * (1f - t) * (1f - t);
                _overlay.style.opacity = opacity;

                // Subtle scale animation — droplets run down the screen
                float scaleY = 1f + t * 0.15f;
                _overlay.style.scale = new Scale(new Vector3(1f, scaleY, 1f));

                if (t >= 1f) HideOverlay();
            }
        }

        private void ShowOverlay()
        {
            EnsureDocument();
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.Flex;
                _overlay.style.opacity = maxOpacity;
                _overlay.style.scale = ScaleInitial;
            }
        }

        private void HideOverlay()
        {
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.None;
            }
        }

        private void EnsureDocument()
        {
            if (_doc != null) return;

            // Find or create a UIDocument for the droplet overlay
            _doc = GetComponent<UIDocument>();
            if (_doc == null)
            {
                var go = new GameObject("WaterLensOverlay");
                go.transform.SetParent(transform, false);
                _doc = go.AddComponent<UIDocument>();
            }

            var root = _doc.rootVisualElement;
            root.Clear();

            // Full-screen overlay with procedural droplet appearance
            _overlay = new VisualElement();
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0; _overlay.style.top = 0;
            _overlay.style.right = 0; _overlay.style.bottom = 0;
            _overlay.style.display = DisplayStyle.None;

            // Simulate water droplets using a semi-transparent blue-tinted overlay
            // with a subtle gradient that mimics light distortion through water
            _overlay.style.backgroundColor = new Color(0.4f, 0.7f, 0.95f, 0.12f);
            _overlay.style.opacity = 0f;

            // Add several "droplet" circles as child elements
            for (int i = 0; i < 12; i++)
            {
                var drop = new VisualElement();
                float x = Mathf.PerlinNoise(i * 0.7f, 0.3f) * 90f;
                float y = Mathf.PerlinNoise(0.5f, i * 0.9f) * 90f;
                float size = 8 + Mathf.PerlinNoise(i * 1.3f, i * 0.4f) * 20f;

                drop.style.position = Position.Absolute;
                drop.style.left = new Length(x, LengthUnit.Percent);
                drop.style.top = new Length(y, LengthUnit.Percent);
                drop.style.width = size;
                drop.style.height = size;
                drop.style.backgroundColor = new Color(0.5f, 0.8f, 1.0f, 0.25f);
                var radius = size * 0.45f;
                drop.style.borderTopLeftRadius = radius;
                drop.style.borderTopRightRadius = radius;
                drop.style.borderBottomLeftRadius = radius;
                drop.style.borderBottomRightRadius = radius;
                drop.pickingMode = PickingMode.Ignore;

                _overlay.Add(drop);
            }

            _overlay.pickingMode = PickingMode.Ignore;
            root.Add(_overlay);
        }

        private static readonly Scale ScaleInitial = new Scale(Vector3.one);
    }
}
