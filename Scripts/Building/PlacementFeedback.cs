// Assets/Scripts/VoxelEngine/Building/PlacementFeedback.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║      PLACEMENT FEEDBACK — the click, the thud, the dust          ║
// ║                                                                  ║
// ║  Half of what makes a builder feel good is not placement at all,  ║
// ║  it is confirmation: the magnetic clink when the hologram picks  ║
// ║  up a snap point, the heavier thud when a block actually lands,  ║
// ║  and a small breath of dust where it landed.                     ║
// ║                                                                  ║
// ║  Both sounds are synthesised by SfxLibrary, so this ships with   ║
// ║  no imported audio, and the dust is one pooled ParticleSystem    ║
// ║  that is built on first use and then reused forever. Nothing     ║
// ║  here touches placement state: it only announces what happened.  ║
// ║                                                                  ║
// ║  Every entry point degrades to silence if the world has no       ║
// ║  camera, no shader or no particle support - a missing effect is  ║
// ║  never allowed to break a build.                                 ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.FX;

namespace VoxelEngine.Building
{
    /// <summary>One-shot build sounds and dust. Stateless apart from a cached emitter.</summary>
    public static class PlacementFeedback
    {
        private const float SnapMinInterval = 0.07f;

        private static float _nextSnap;
        private static ParticleSystem _dust;
        private static Material _dustMaterial;
        private static bool _dustUnavailable;

        /// <summary>The crisp clink of the hologram finding a new snap point.</summary>
        public static void Snap(Vector3 position, int variant = 0)
        {
            if (Time.unscaledTime < _nextSnap) return;
            _nextSnap = Time.unscaledTime + SnapMinInterval;
            var clip = SfxLibrary.Get(Sfx.BuildSnap, variant & 1);
            AudioManager.PlayAt(clip, position, 0.32f, Random.Range(1.06f, 1.16f), 12f);
        }

        /// <summary>A block, or a run of blocks, has landed.</summary>
        public static void Placed(Vector3 position, int count = 1)
        {
            var clip = SfxLibrary.Get(Sfx.BuildThud);
            // A run is one solid gesture, not ten taps: scale up but never stack up.
            float volume = Mathf.Clamp(0.55f + 0.06f * Mathf.Max(0, count - 1), 0.55f, 1f);
            float pitch = Random.Range(0.94f, 1.02f) - 0.01f * Mathf.Min(6, Mathf.Max(0, count - 1));
            AudioManager.PlayAt(clip, position, volume, pitch, 26f);
            Dust(position, Mathf.Clamp(6 + count * 2, 8, 40));
        }

        /// <summary>A click that was refused. Quiet, dull, and never a scold.</summary>
        public static void Refused(Vector3 position)
        {
            var clip = SfxLibrary.Get(Sfx.BuildRefuse);
            AudioManager.PlayAt(clip, position, 0.30f, 1f, 10f);
        }

        /// <summary>
        /// A short, low, outward breath of dust at the base of the block. Deliberately
        /// subtle: dust is a confirmation, not a firework, and it must read on a misty
        /// morning and in a factory hall alike.
        /// </summary>
        public static void Dust(Vector3 position, int amount = 14)
        {
            var system = EnsureDust();
            if (system == null) return;

            system.transform.position = position;
            var emit = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = true,
                startLifetime = Random.Range(0.30f, 0.62f),
                startSize = Random.Range(0.10f, 0.26f),
                startColor = new Color(0.78f, 0.76f, 0.70f, 0.34f),
                velocity = Vector3.up * Random.Range(0.35f, 0.95f),
            };
            system.Emit(emit, Mathf.Clamp(amount, 4, 48));
        }

        private static ParticleSystem EnsureDust()
        {
            if (_dust != null) return _dust;
            if (_dustUnavailable) return null;

            try
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Sprites/Default");
                if (shader == null) { _dustUnavailable = true; return null; }

                var go = new GameObject("~PlacementDust");
                Object.DontDestroyOnLoad(go);

                if (_dustMaterial == null) _dustMaterial = new Material(shader) { name = "PlacementDust" };
                _dustMaterial.color = new Color(1f, 1f, 1f, 1f);
                if (_dustMaterial.HasProperty("_Surface"))
                {
                    _dustMaterial.SetFloat("_Surface", 1f);
                    _dustMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }
                _dustMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

                _dust = go.AddComponent<ParticleSystem>();
                var main = _dust.main;
                main.playOnAwake = false;
                main.loop = false;
                main.startSpeed = 0.9f;
                main.startLifetime = 0.5f;
                main.startSize = 0.18f;
                main.gravityModifier = 0.25f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 256;

                var emission = _dust.emission;
                emission.enabled = false;                    // every burst is emitted by hand

                var shape = _dust.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = 0.22f;

                var colorOverLifetime = _dust.colorOverLifetime;
                colorOverLifetime.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

                var renderer = _dust.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    renderer.sharedMaterial = _dustMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                _dust.Play();
                return _dust;
            }
            catch (System.Exception)
            {
                // A particle system is a garnish. If this platform refuses it, the build
                // still sounds and still works.
                _dustUnavailable = true;
                _dust = null;
                return null;
            }
        }
    }
}
