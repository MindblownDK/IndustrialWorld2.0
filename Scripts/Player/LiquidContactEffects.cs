// Assets/Scripts/VoxelEngine/Player/LiquidContactEffects.cs
//
// 9.16.0 (Liquids Overhaul, Part 3) — per-liquid CONTACT DAMAGE.
//
//   • Standing in hot engine coolant SCALDS: the armour-escalating burn (heated
//     metal hurts more — the same DoT the Ifrit's fireballs inflict).
//   • Standing in liquid fuel eats the skin: a caustic DoT mitigated by worn armour.
//   • Both apply while ANY part of the body touches the liquid — wading into a
//     shallow coolant puddle still hurts, full submersion just keeps refreshing it.
//
// Self-added by PlayerController next to PlayerWaterState, so every spawn carries it
// without any prefab/setup changes.
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.UI;

namespace VoxelEngine.Player
{
    public class LiquidContactEffects : MonoBehaviour
    {
        [Tooltip("How often contact damage and feedback re-apply (s).")]
        public float tickInterval = 0.5f;

        private PlayerWaterState _water;
        private PlayerStats _stats;
        private float _timer;
        private float _nextToastAt;

        private void Awake()
        {
            _water = GetComponent<PlayerWaterState>();
            _stats = GetComponent<PlayerStats>();
        }

        private void Update()
        {
            if (_water == null || _stats == null) return;

            _timer += Time.deltaTime;
            if (_timer < tickInterval) return;
            _timer = 0f;

            if (!_water.IsContactingLiquid) return;
            float dps = LiquidPlayerPhysics.ContactDps(_water.Liquid);
            if (dps <= 0f) return;

            if (_water.Liquid == LiquidType.MarineEngineCoolant)
                _stats.ApplyBurn(dps, tickInterval + 0.4f);
            else
                _stats.ApplyCaustic(dps, tickInterval + 0.4f);

            if (Time.time >= _nextToastAt)
            {
                _nextToastAt = Time.time + 3f;
                BuildFeedbackHud.Show(_water.Liquid.DisplayName(),
                    _water.Liquid == LiquidType.MarineEngineCoolant
                        ? "Scalding hot coolant!"
                        : "Caustic fuel is eating your skin!",
                    null, new Color(1f, 0.5f, 0.1f));
            }
        }
    }
}
