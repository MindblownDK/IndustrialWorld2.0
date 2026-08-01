// Assets/Scripts/VoxelEngine/Combat/DefenseStatus.cs
//
// Shared status / low-ammo helpers for the automated defense network.
// World inspection, interaction prompts, and empty-magazine toasts all read here
// so every turret kind stays consistent.

using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.UI;

namespace VoxelEngine.Combat
{
    public static class DefenseStatus
    {
        private static float _nextEmptyToastAt;

        public struct Info
        {
            public string title;
            public string detail;   // DEFENSE · AUTO / MANUAL
            public string status;   // ammo + filter line
            public float health01;
            public string healthText;
            public bool showHealth;
            public bool isEmpty;
            public bool isLow;
        }

        public static bool TryDescribe(Component c, out Info info)
        {
            info = default;
            if (c == null) return false;

            if (c is Artillery art) { Fill(ref info, art.variant.ToString(), art.autoMode, art.filter, CountMagazine(art.ShellMagazine), 6, art); info.status = $"Shells: {CountMagazine(art.ShellMagazine)} · {FilterLabel(art.filter)}{PolicySuffix(art)}"; ApplyReserveLow(ref info, art); return true; }
            if (c is Turret tur) { Fill(ref info, "Auto Turret", tur.autoMode, tur.filter, tur.ammo, tur.maxAmmo, tur); info.status = $"Ammo: {tur.ammo}/{tur.maxAmmo} · {FilterLabel(tur.filter)}{PolicySuffix(tur)}"; info.isEmpty = tur.ammo <= 0; info.isLow = tur.ammo > 0 && tur.ammo <= Mathf.Max(5, tur.maxAmmo / 5); ApplyReserveLow(ref info, tur); return true; }
            if (c is FlamethrowerTurret flame)
            {
                int fuelItems = CountMagazine(flame.FuelMagazine);
                float fuelSec = flame.FuelSeconds;
                Fill(ref info, "Flamethrower Turret", flame.autoMode, flame.filter, fuelItems, 6, flame);
                info.status = $"Fuel: {fuelSec:0.0}s + {fuelItems} cans · {FilterLabel(flame.filter)}{PolicySuffix(flame)}";
                info.isEmpty = fuelSec <= 0.05f && fuelItems <= 0;
                info.isLow = !info.isEmpty && fuelSec < 3f && fuelItems <= 1;
                ApplyReserveLow(ref info, flame);
                return true;
            }
            if (c is MortarTurret mortar) { Fill(ref info, "Mortar Turret", mortar.autoMode, mortar.filter, CountMagazine(mortar.ShellMagazine), 6, mortar); info.status = $"Shells: {CountMagazine(mortar.ShellMagazine)} · {FilterLabel(mortar.filter)}{PolicySuffix(mortar)}"; ApplyReserveLow(ref info, mortar); return true; }
            if (c is GiantShellTurret giant) { Fill(ref info, "Giant Shell Turret", giant.autoMode, giant.filter, CountMagazine(giant.ShellMagazine), 4, giant); info.status = $"Giant Shells: {CountMagazine(giant.ShellMagazine)} · {FilterLabel(giant.filter)}{PolicySuffix(giant)}"; ApplyReserveLow(ref info, giant); return true; }
            if (c is AntiAirTurret aa)
            {
                Fill(ref info, "Anti-Air Turret", aa.autoMode, aa.filter, CountMagazine(aa.AmmoMagazine), 12, aa);
                info.status = $"AA Ammo: {CountMagazine(aa.AmmoMagazine)} · {(aa.preferAerialOnly ? "Aerial" : "All")} · {FilterLabel(aa.filter)}{PolicySuffix(aa)}";
                ApplyReserveLow(ref info, aa);
                return true;
            }
            if (c is EnergyRelicTurret energy) { Fill(ref info, "Energy / Relic Turret", energy.autoMode, energy.filter, CountMagazine(energy.CellMagazine), 8, energy); info.status = $"Cells: {CountMagazine(energy.CellMagazine)} · {FilterLabel(energy.filter)}{PolicySuffix(energy)}"; ApplyReserveLow(ref info, energy); return true; }

            // Also accept any child Damageable host looked at via collider.
            var d = c.GetComponentInParent<Damageable>();
            if (d != null && !ReferenceEquals(d, c))
                return TryDescribe(d, out info);

            return false;
        }

        public static bool TryGetLookPrompt(Component c, out string prompt)
        {
            prompt = null;
            if (c is Artillery art && Artillery.ActiveArtilleryCockpit == null)
            {
                prompt = "Configure (RMB to enter)";
                return true;
            }
            if (c is Turret || c is FlamethrowerTurret || c is MortarTurret ||
                c is GiantShellTurret || c is AntiAirTurret || c is EnergyRelicTurret)
            {
                prompt = "Configure defense";
                return true;
            }
            var d = c != null ? c.GetComponentInParent<Damageable>() : null;
            if (d != null && !ReferenceEquals(d, c))
                return TryGetLookPrompt(d, out prompt);
            return false;
        }

        /// <summary>Throttled toast when a defense piece runs dry mid-fight.</summary>
        public static void NotifyEmpty(string defenseName)
        {
            if (Time.time < _nextEmptyToastAt) return;
            _nextEmptyToastAt = Time.time + 2.8f;
            BuildFeedbackHud.Show(
                string.IsNullOrEmpty(defenseName) ? "Defense" : defenseName,
                "Out of ammo — resupply via belt / pipe / panel",
                null,
                new Color(1f, 0.75f, 0.25f));
        }

        public static int CountMagazine(ItemContainer mag)
        {
            if (mag == null) return 0;
            mag.EnsureValid();
            int n = 0;
            for (int i = 0; i < mag.Slots.Count; i++)
            {
                var s = mag.GetSlot(i);
                if (s != null && !s.IsEmpty) n += s.count;
            }
            return n;
        }

        private static void Fill(ref Info info, string title, bool auto, TargetFilter filter, int stock, int lowThreshold, Damageable hp)
        {
            info.title = title;
            info.detail = auto ? "DEFENSE · AUTO" : "DEFENSE · MANUAL";
            info.isEmpty = stock <= 0;
            info.isLow = !info.isEmpty && stock <= lowThreshold;
            if (hp != null && hp.maxHealth > 0f)
            {
                info.showHealth = true;
                info.health01 = Mathf.Clamp01(hp.Health / hp.maxHealth);
                info.healthText = $"{Mathf.Max(0f, hp.Health):0}/{hp.maxHealth:0}";
            }
        }

        private static string FilterLabel(TargetFilter f)
        {
            if (f == TargetFilter.None) return "No targets";
            bool e = (f & TargetFilter.Enemies) != 0;
            bool p = (f & TargetFilter.Players) != 0;
            bool a = (f & TargetFilter.Passive) != 0;
            if (e && !p && !a) return "Enemies";
            if (e && p && a) return "All factions";
            var parts = new System.Collections.Generic.List<string>(3);
            if (e) parts.Add("Enemies");
            if (p) parts.Add("Players");
            if (a) parts.Add("Passive");
            return string.Join("+", parts);
        }
    }
}
