// Assets/Scripts/VoxelEngine/Items/ContainmentCanister.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║              PRESSURIZED CANISTER — portable containment             ║
// ║                                                                      ║
// ║  Antimatter and dark matter can never be carried by hand — they ride ║
// ║  inside pressurized canisters. While the canister sits in the        ║
// ║  PLAYER INVENTORY its field pressure bleeds down:                    ║
// ║                                                                      ║
// ║   • 100 pressure at fill; decays ~2.0/s in a pocket (on-grid         ║
// ║     storage and the Containment Vault hold it indefinitely).         ║
// ║   • Warnings at 30 and 15 pressure (HUD strip + toasts).             ║
// ║   • At ZERO the canister COLLAPSES: the stack is destroyed and the   ║
// ║     carrier is killed — "KILLED BY CONTAINMENT COLLAPSE".            ║
// ║   • Merging canisters averages their pressure (weighted by count)    ║
// ║     via the ItemContainer.MergeCharge hook.                          ║
// ║                                                                      ║
// ║  Pressure lives in ItemStack.charge (tenths: 1000 = full) — a        ║
// ║  save-compatible, per-stack field.                                   ║
// ╚══════════════════════════════════════════════════════════════════════╝
using UnityEngine;
using VoxelEngine.Player;

namespace VoxelEngine.Items
{
    public static class ContainmentCanister
    {
        /// <summary>Full field pressure (charge = pressure × 10).</summary>
        public const int FULL_CHARGE = 1000;

        /// <summary>Pressure lost per second while carried in the player inventory.</summary>
        public const float PLAYER_DECAY_PER_SEC = 2.0f;

        /// <summary>HUD warning threshold (pressure units).</summary>
        public const float WARN_PRESSURE = 30f;

        /// <summary>HUD critical threshold (pressure units).</summary>
        public const float CRITICAL_PRESSURE = 15f;

        private static GameObject _runtime;
        private static PlayerController _player;
        private static float _playerFindTimer;
        private static float _decayDebt;
        private static float _warnCooldown;

        /// <summary>Self-spawning ticker — no scene wiring needed.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntime()
        {
            if (_runtime != null) return;
            _runtime = new GameObject("ContainmentCanisterRuntime");
            _runtime.hideFlags = HideFlags.HideAndDontSave;
            _runtime.AddComponent<CanisterTickHost>();
            InstallMergeHook();
        }

        private sealed class CanisterTickHost : MonoBehaviour
        {
            private void Update() => ContainmentCanister.Tick();
        }

        /// <summary>Weighted-average pressure merge for canister stacks.</summary>
        private static void InstallMergeHook()
        {
            if (ItemContainer.MergeCharge == null)
                ItemContainer.MergeCharge = MergePressure;
        }

        private static int MergePressure(ItemStack existing, ItemStack incoming, int add)
        {
            if (existing == null || incoming == null || existing.item == null) return 0;
            if (!existing.item.isPressurizedCanister) return existing.charge;
            // Weighted average of the two fields (fresh fills boost older stock).
            float total = existing.count + add;
            if (total <= 0f) return FULL_CHARGE;
            float merged = (existing.charge * existing.count + incoming.charge * add) / total;
            return Mathf.Clamp(Mathf.RoundToInt(merged), 0, FULL_CHARGE);
        }

        /// <summary>True when the item is a pressurized canister.</summary>
        public static bool IsCanister(ItemDefinition item)
            => item != null && item.isPressurizedCanister;

        /// <summary>Pressure 0..100 of a stack (0 for non-canisters).</summary>
        public static float PressureOf(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty || stack.item == null || !stack.item.isPressurizedCanister)
                return -1f;
            return stack.charge / 10f;
        }

        private static void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            // ── Resolve the player (cached, periodic re-find) ──
            if (_player == null)
            {
                _playerFindTimer -= dt;
                if (_playerFindTimer <= 0f)
                {
                    _playerFindTimer = 2f;
                    _player = Object.FindAnyObjectByType<PlayerController>();
                }
            }
            if (_player == null) { UI.CanisterPressureHud.Hide(); return; }

            var inv = _player.GetComponentInParent<Inventory>();
            if (inv == null || inv.container == null) { UI.CanisterPressureHud.Hide(); return; }

            // ── Pressure bleed on carried canisters ──
            _decayDebt += PLAYER_DECAY_PER_SEC * 10f * dt;   // charge units owed
            int burn = Mathf.FloorToInt(_decayDebt);

            float lowest = -1f;
            int carried = 0;
            ItemStack collapsing = null;

            for (int i = 0; i < inv.container.Size; i++)
            {
                var s = inv.container.GetSlot(i);
                if (s == null || s.IsEmpty || s.item == null || !s.item.isPressurizedCanister) continue;
                carried += s.count;

                // Burn the debt evenly across carried canisters.
                if (burn > 0 && s.charge > 0)
                {
                    int take = Mathf.Min(burn, s.charge);
                    s.charge -= take;
                    burn -= take;
                }

                float p = s.charge / 10f;
                if (lowest < 0f || p < lowest) lowest = p;
                if (s.charge <= 0) collapsing = s;
            }
            if (burn > 0) _decayDebt = burn; else _decayDebt = 0f;

            // ── Collapse: destroy the stack, kill the carrier ──
            if (collapsing != null)
            {
                string name = collapsing.item.displayName;
                inv.container.Remove(collapsing.item, collapsing.count);
                KillCarrier(name);
                UI.CanisterPressureHud.Hide();
                return;
            }

            // ── HUD + warnings ──
            if (carried > 0 && lowest >= 0f)
            {
                UI.CanisterPressureHud.Show(lowest / 100f, $"{(int)lowest}", carried);
                _warnCooldown -= dt;
                if (lowest <= CRITICAL_PRESSURE && _warnCooldown <= 0f)
                {
                    _warnCooldown = 3f;
                    UI.BuildFeedbackHud.Show("CANISTER FIELD COLLAPSING",
                        $"Pressure {lowest:0} — collapse is fatal", null, new Color(1f, 0.2f, 0.1f));
                }
                else if (lowest <= WARN_PRESSURE && _warnCooldown <= 0f)
                {
                    _warnCooldown = 6f;
                    UI.BuildFeedbackHud.Show("CANISTER PRESSURE LOW",
                        $"Pressure {lowest:0} — return it to a Containment Vault", null, new Color(1f, 0.7f, 0.25f));
                }
            }
            else
            {
                UI.CanisterPressureHud.Hide();
            }
        }

        private static void KillCarrier(string canisterName)
        {
            var stats = PlayerStats.Instance != null ? PlayerStats.Instance : _player.GetComponent<PlayerStats>();
            PlayerStats.SetDeathCause("KILLED BY CONTAINMENT COLLAPSE");
            UI.BuildFeedbackHud.Show("CONTAINMENT COLLAPSE",
                $"{canisterName} imploded", null, new Color(1f, 0.1f, 0.05f));
            if (stats != null)
                stats.TakeDamage(1000000f);
        }
    }
}
