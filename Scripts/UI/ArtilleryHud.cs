// Assets/Scripts/VoxelEngine/UI/ArtilleryHud.cs
//
// Artillery control panel shown when the player looks at an artillery piece. Sets the
// targeting faction filter (Enemies / Players / Passive — any combination) and the
// auto/manual toggle, and shows ammo + variant.

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
        private static VoxelEngine.Combat.Artillery _bound;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_panel != null && _panel.parent == null && _root == uiRoot) uiRoot.Add(_panel);
            if (_root == uiRoot && _panel != null && _panel.parent == uiRoot) return;

            _root = uiRoot;
            if (_panel != null) _panel.RemoveFromHierarchy();

            _panel = new VisualElement { name = "ArtilleryHud" };
            _panel.style.position = Position.Absolute;
            _panel.style.left = Length.Percent(35); _panel.style.right = Length.Percent(35);
            _panel.style.top = 64;
            _panel.style.flexDirection = FlexDirection.Column;
            _panel.style.backgroundColor = new StyleColor(new Color(0.05f, 0.06f, 0.09f, 0.92f));
            _panel.style.paddingTop = 6; _panel.style.paddingBottom = 6;
            _panel.style.paddingLeft = 10; _panel.style.paddingRight = 10;
            T.Radius(_panel, 6); T.Border(_panel, 1, new Color(0.9f, 0.5f, 0.15f, 0.6f));
            _panel.style.display = DisplayStyle.None;

            _title = new Label("ARTILLERY");
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
            _tAuto.RegisterValueChangedCallback(e => { if (_bound != null) _bound.autoMode = e.newValue; });
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

        private static void SetFlag(VoxelEngine.Combat.TargetFilter f, bool on)
        {
            if (_bound == null) return;
            if (on) _bound.filter |= f;
            else _bound.filter &= ~f;
        }

        private static void SyncToggles()
        {
            if (_bound == null) return;
            _tEnemy.SetValueWithoutNotify((_bound.filter & VoxelEngine.Combat.TargetFilter.Enemies) != 0);
            _tPlayer.SetValueWithoutNotify((_bound.filter & VoxelEngine.Combat.TargetFilter.Players) != 0);
            _tPassive.SetValueWithoutNotify((_bound.filter & VoxelEngine.Combat.TargetFilter.Passive) != 0);
            _tAuto.SetValueWithoutNotify(_bound.autoMode);
        }

        public static void Tick(Camera cam, float reach)
        {
            if (_panel == null) return;
            VoxelEngine.Combat.Artillery a = null;
            if (cam != null)
            {
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
                if (Physics.Raycast(ray, out var hit, reach, ~0, QueryTriggerInteraction.Ignore))
                    a = hit.collider.GetComponentInParent<VoxelEngine.Combat.Artillery>();
            }

            if (a != null)
            {
                if (_bound != a) { _bound = a; SyncToggles(); }
                _panel.style.display = DisplayStyle.Flex;
                _title.text = "ARTILLERY — " + a.variant.ToString().ToUpper();
                _ammoLabel.text = $"Ammo {a.ammo}/{a.maxAmmo}   Filter: {FilterText(a.filter)}";
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
