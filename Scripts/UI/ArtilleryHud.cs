// Assets/Scripts/VoxelEngine/UI/ArtilleryHud.cs
//
// Defense control panel shown when the player looks at an Artillery piece or Auto Turret.
// Sets the targeting faction filter (Enemies / Players / Passive — any combination) and
// the auto/manual toggle, and shows ammo + variant/type.

using UnityEngine;
using UnityEngine.UIElements;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class ArtilleryHud
    {
        private static VisualElement _root, _panel;
        private static Toggle _tEnemy, _tPlayer, _tPassive, _tAuto;
        private static Label _title, _ammoLabel;
        private static Component _bound;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_panel != null && _panel.parent == null && _root == uiRoot) uiRoot.Add(_panel);
            if (_root == uiRoot && _panel != null && _panel.parent == uiRoot) return;

            _root = uiRoot;
            if (_panel != null) _panel.RemoveFromHierarchy();

            _panel = new VisualElement { name = "DefenseHud" };
            _panel.style.position = Position.Absolute;
            _panel.style.left = Length.Percent(35); _panel.style.right = Length.Percent(35);
            _panel.style.top = 64;
            _panel.style.flexDirection = FlexDirection.Column;
            _panel.style.backgroundColor = new StyleColor(new Color(0.05f, 0.06f, 0.09f, 0.92f));
            _panel.style.paddingTop = 6; _panel.style.paddingBottom = 6;
            _panel.style.paddingLeft = 10; _panel.style.paddingRight = 10;
            T.Radius(_panel, 6); T.Border(_panel, 1, new Color(0.9f, 0.5f, 0.15f, 0.6f));
            _panel.style.display = DisplayStyle.None;

            _title = new Label("DEFENSE");
            _title.style.color = new Color(1f, 0.6f, 0.2f);
            _title.style.fontSize = 11; _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.unityTextAlign = TextAnchor.MiddleCenter; _title.style.marginBottom = 4;
            _panel.Add(_title);

            _ammoLabel = new Label("");
            _ammoLabel.style.color = new Color(0.7f, 0.8f, 0.9f);
            _ammoLabel.style.fontSize = 10; _ammoLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _ammoLabel.style.marginBottom = 4;
            _panel.Add(_ammoLabel);

            _tEnemy = MakeToggle("Target Enemies");
            _tPlayer = MakeToggle("Target Players");
            _tPassive = MakeToggle("Target Passive");
            _tAuto = MakeToggle("Auto-Fire");
            _tEnemy.RegisterValueChangedCallback(e => SetFlag(VoxelEngine.Combat.TargetFilter.Enemies, e.newValue));
            _tPlayer.RegisterValueChangedCallback(e => SetFlag(VoxelEngine.Combat.TargetFilter.Players, e.newValue));
            _tPassive.RegisterValueChangedCallback(e => SetFlag(VoxelEngine.Combat.TargetFilter.Passive, e.newValue));
            _tAuto.RegisterValueChangedCallback(e => SetAuto(e.newValue));
            _panel.Add(_tEnemy); _panel.Add(_tPlayer); _panel.Add(_tPassive); _panel.Add(_tAuto);

            uiRoot.Add(_panel);
        }

        private static Toggle MakeToggle(string label)
        {
            var t = new Toggle(label);
            t.style.color = Color.white;
            t.style.fontSize = 11;
            t.style.marginTop = 1; t.style.marginBottom = 1;
            return t;
        }

        // --- helpers that work on either Artillery or Turret ---
        private static VoxelEngine.Combat.TargetFilter GetFilter()
        {
            if (_bound is VoxelEngine.Combat.Artillery a) return a.filter;
            if (_bound is VoxelEngine.Combat.Turret t) return t.filter;
            return VoxelEngine.Combat.TargetFilter.None;
        }

        private static void SetFilter(VoxelEngine.Combat.TargetFilter f)
        {
            if (_bound is VoxelEngine.Combat.Artillery a) a.filter = f;
            else if (_bound is VoxelEngine.Combat.Turret t) t.filter = f;
        }

        private static bool GetAuto()
        {
            if (_bound is VoxelEngine.Combat.Artillery a) return a.autoMode;
            if (_bound is VoxelEngine.Combat.Turret t) return t.autoMode;
            return false;
        }

        private static void SetAuto(bool v)
        {
            if (_bound is VoxelEngine.Combat.Artillery a) a.autoMode = v;
            else if (_bound is VoxelEngine.Combat.Turret t) t.autoMode = v;
        }

        private static void SetFlag(VoxelEngine.Combat.TargetFilter f, bool on)
        {
            var cur = GetFilter();
            SetFlagDirectly(f, on);
        }

        private static void SetFlagDirectly(VoxelEngine.Combat.TargetFilter f, bool on)
        {
            var cur = GetFilter();
            if (on) cur |= f; else cur &= ~f;
            SetFilter(cur);
        }

        private static void SyncToggles()
        {
            var f = GetFilter();
            _tEnemy.SetValueWithoutNotify((f & VoxelEngine.Combat.TargetFilter.Enemies) != 0);
            _tPlayer.SetValueWithoutNotify((f & VoxelEngine.Combat.TargetFilter.Players) != 0);
            _tPassive.SetValueWithoutNotify((f & VoxelEngine.Combat.TargetFilter.Passive) != 0);
            _tAuto.SetValueWithoutNotify(GetAuto());
        }

        public static void Tick(Camera cam, float reach)
        {
            if (_panel == null) return;
            Component target = null;
            if (cam != null)
            {
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
                if (Physics.Raycast(ray, out var hit, reach, ~0, QueryTriggerInteraction.Ignore))
                {
                    target = hit.collider.GetComponentInParent<VoxelEngine.Combat.Artillery>();
                    if (target == null)
                        target = hit.collider.GetComponentInParent<VoxelEngine.Combat.Turret>();
                }
            }

            if (target != null)
            {
                if (_bound != target) { _bound = target; SyncToggles(); }
                _panel.style.display = DisplayStyle.Flex;

                string name;
                int ammo, maxAmmo;
                if (target is VoxelEngine.Combat.Artillery a)
                { name = a.variant.ToString(); ammo = a.ammo; maxAmmo = a.maxAmmo; }
                else
                { var t = (VoxelEngine.Combat.Turret)target; name = "Auto Turret"; ammo = t.ammo; maxAmmo = t.maxAmmo; }

                _title.text = name.ToUpper();
                _ammoLabel.text = $"Ammo {ammo}/{maxAmmo}   Filter: {FilterText(GetFilter())}";
            }
            else
            {
                if (_panel.style.display != DisplayStyle.None) _panel.style.display = DisplayStyle.None;
                _bound = null;
            }
        }

        private static string FilterText(VoxelEngine.Combat.TargetFilter f)
        {
            if (f == VoxelEngine.Combat.TargetFilter.None) return "None";
            var parts = new System.Collections.Generic.List<string>();
            if ((f & VoxelEngine.Combat.TargetFilter.Enemies) != 0) parts.Add("Enemy");
            if ((f & VoxelEngine.Combat.TargetFilter.Players) != 0) parts.Add("Player");
            if ((f & VoxelEngine.Combat.TargetFilter.Passive) != 0) parts.Add("Passive");
            return string.Join("+", parts);
        }
    }
}
