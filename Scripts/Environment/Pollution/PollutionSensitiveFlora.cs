// Assets/Scripts/VoxelEngine/Environment/Pollution/PollutionSensitiveFlora.cs
//
// Lightweight visual feedback for procedurally scattered living flora. Each
// instance samples infrequently and uses renderer property blocks, so shared
// materials are never cloned or permanently modified.

using UnityEngine;

namespace VoxelEngine.Environment
{
    [DisallowMultipleComponent]
    public sealed class PollutionSensitiveFlora : MonoBehaviour
    {
        private const float SampleInterval = 2.5f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Renderer[] _renderers;
        private Color[] _baseColors;
        private bool[] _hasBaseColor;
        private bool[] _hasColor;
        private MaterialPropertyBlock _block;
        private float _nextSample;
        private float _lastVitality = -1f;

        public float Vitality01 { get; private set; } = 1f;

        private void OnEnable()
        {
            CacheRenderers();
            int hash = Mathf.Abs(gameObject.GetEntityId().GetHashCode() % 1000);
            _nextSample = Time.time + SampleInterval * (hash / 1000f);
        }

        private void Update()
        {
            if (Time.time < _nextSample) return;
            _nextSample = Time.time + SampleInterval;

            EcologyReading reading = EcologyPressure.Sample(transform.position);
            Vitality01 = reading.SupportsNativeEcology ? reading.Vitality01 : 1f;
            if (Mathf.Abs(Vitality01 - _lastVitality) < 0.015f) return;
            _lastVitality = Vitality01;
            ApplyVitality(Vitality01, reading.Airborne01, reading.Runoff01);
        }

        private void CacheRenderers()
        {
            _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            _baseColors = new Color[_renderers.Length];
            _hasBaseColor = new bool[_renderers.Length];
            _hasColor = new bool[_renderers.Length];
            _block = new MaterialPropertyBlock();

            for (int i = 0; i < _renderers.Length; i++)
            {
                Material material = _renderers[i] != null ? _renderers[i].sharedMaterial : null;
                if (material == null)
                {
                    _baseColors[i] = Color.white;
                    continue;
                }

                _hasBaseColor[i] = material.HasProperty(BaseColorId);
                _hasColor[i] = material.HasProperty(ColorId);
                _baseColors[i] = _hasBaseColor[i]
                    ? material.GetColor(BaseColorId)
                    : (_hasColor[i] ? material.GetColor(ColorId) : Color.white);
            }
        }

        private void ApplyVitality(float vitality, float airborne, float runoff)
        {
            float stress = 1f - vitality;
            Color stressColour = Color.Lerp(
                new Color(0.39f, 0.30f, 0.16f, 1f),
                new Color(0.30f, 0.33f, 0.29f, 1f),
                Mathf.Clamp01(airborne * 0.75f));
            stressColour = Color.Lerp(stressColour,
                new Color(0.34f, 0.25f, 0.18f, 1f), Mathf.Clamp01(runoff * 0.65f));

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null || (!_hasBaseColor[i] && !_hasColor[i])) continue;

                Color original = _baseColors[i];
                Color tinted = Color.Lerp(original, stressColour * original.grayscale,
                    stress * 0.82f);
                tinted.a = original.a;
                renderer.GetPropertyBlock(_block);
                if (_hasBaseColor[i]) _block.SetColor(BaseColorId, tinted);
                if (_hasColor[i]) _block.SetColor(ColorId, tinted);
                renderer.SetPropertyBlock(_block);
            }
        }
    }
}
