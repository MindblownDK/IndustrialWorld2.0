// Assets/Scripts/VoxelEngine/UI/DeathScreenHud.cs
//
// Premium full-screen death/respawn overlay. It lists the safe respawn anchors
// currently available: world spawn, the active linked spawn, and live beds / cryobeds.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;
using VoxelEngine.Player;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class DeathScreenHud
    {
        private static VisualElement _root;
        private static VisualElement _overlay;
        private static PlayerStats _deadPlayer;
        private static bool _visible;
        private static bool _blocking;

        private struct RespawnChoice
        {
            public string title;
            public string detail;
            public Vector3 position;
            public Color accent;
        }

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _overlay != null && _overlay.parent == uiRoot) return;
            _root = uiRoot;
            if (_overlay != null) _overlay.RemoveFromHierarchy();

            _overlay = new VisualElement { name = "DeathScreenHud" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.right = 0;
            _overlay.style.top = 0;
            _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new StyleColor(new Color(0.01f, 0.012f, 0.018f, 0.92f));
            _overlay.style.display = DisplayStyle.None;
            _overlay.pickingMode = PickingMode.Position;
            uiRoot.Add(_overlay);

            if (_visible) Rebuild();
        }

        public static void Show(PlayerStats player)
        {
            _deadPlayer = player;
            _visible = true;
            if (!_blocking)
            {
                UIState.PushBlock();
                _blocking = true;
            }
            if (_overlay != null) Rebuild();
        }

        public static void Hide()
        {
            _visible = false;
            _deadPlayer = null;
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
            if (_blocking)
            {
                UIState.PopBlock();
                _blocking = false;
            }
        }

        private static void Rebuild()
        {
            if (_overlay == null) return;
            _overlay.Clear();
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;

            var panel = new VisualElement();
            panel.style.width = 560;
            panel.style.maxWidth = new StyleLength(new Length(82f, LengthUnit.Percent));
            panel.style.paddingLeft = 26;
            panel.style.paddingRight = 26;
            panel.style.paddingTop = 24;
            panel.style.paddingBottom = 24;
            panel.style.backgroundColor = new StyleColor(new Color(0.035f, 0.040f, 0.055f, 0.96f));
            T.Radius(panel, 18f);
            T.Border(panel, 1, new Color(0.85f, 0.18f, 0.14f, 0.55f));
            _overlay.Add(panel);

            var title = new Label("CRUSADER DOWN");
            title.style.fontSize = 28;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 2.0f;
            title.style.color = new Color(1.0f, 0.30f, 0.22f);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(title);

            var subtitle = new Label("Select a respawn anchor");
            subtitle.style.marginTop = 4;
            subtitle.style.marginBottom = 18;
            subtitle.style.fontSize = 12;
            subtitle.style.letterSpacing = 1.0f;
            subtitle.style.color = new Color(0.72f, 0.78f, 0.86f);
            subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(subtitle);

            var choices = GatherRespawnChoices();
            for (int i = 0; i < choices.Count; i++)
                panel.Add(BuildChoiceButton(choices[i]));
        }

        private static VisualElement BuildChoiceButton(RespawnChoice choice)
        {
            var btn = new Button(() => Respawn(choice.position));
            btn.style.marginBottom = 8;
            btn.style.minHeight = 58;
            btn.style.paddingLeft = 14;
            btn.style.paddingRight = 14;
            btn.style.paddingTop = 8;
            btn.style.paddingBottom = 8;
            btn.style.backgroundColor = new StyleColor(new Color(0.075f, 0.085f, 0.11f, 0.98f));
            T.Radius(btn, 10f);
            T.Border(btn, 1, new Color(choice.accent.r, choice.accent.g, choice.accent.b, 0.45f));

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.pickingMode = PickingMode.Ignore;
            btn.Add(row);

            var title = new Label(choice.title);
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = choice.accent;
            title.pickingMode = PickingMode.Ignore;
            row.Add(title);

            var detail = new Label(choice.detail);
            detail.style.marginTop = 2;
            detail.style.fontSize = 10;
            detail.style.color = new Color(0.70f, 0.76f, 0.84f);
            detail.pickingMode = PickingMode.Ignore;
            row.Add(detail);
            return btn;
        }

        private static void Respawn(Vector3 position)
        {
            var player = _deadPlayer != null ? _deadPlayer : PlayerStats.Instance;
            Hide();
            if (player != null) player.RespawnAt(position);
        }

        private static List<RespawnChoice> GatherRespawnChoices()
        {
            var list = new List<RespawnChoice>(8);
            var session = VoxelEngine.Menu.WorldSession.Instance;
            Vector3 worldSpawn = session != null && session.worldSpawnInitialized
                ? session.worldSpawnPoint
                : new Vector3(0f, 250f, 0f);

            AddUnique(list, new RespawnChoice
            {
                title = "World Spawn",
                detail = FormatPosition(worldSpawn),
                position = worldSpawn,
                accent = new Color(0.42f, 0.75f, 1.0f)
            });

            if (session != null && session.hasBedSpawn)
            {
                AddUnique(list, new RespawnChoice
                {
                    title = "Linked Spawn",
                    detail = "Current bed / cryobed anchor · " + FormatPosition(session.bedSpawnPoint),
                    position = session.bedSpawnPoint,
                    accent = new Color(0.30f, 0.95f, 0.62f)
                });
            }

            foreach (var bed in Object.FindObjectsByType<Bed>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (bed == null) continue;
                Vector3 pos = bed.transform.position + Vector3.up * 1.2f;
                AddUnique(list, new RespawnChoice
                {
                    title = bed.displayName,
                    detail = "Bed · " + FormatPosition(pos),
                    position = pos,
                    accent = new Color(0.95f, 0.72f, 0.25f)
                });
            }

            foreach (var cryo in Object.FindObjectsByType<Cryobed>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (cryo == null) continue;
                Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(cryo.transform.position);
                if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
                Vector3 pos = cryo.transform.position + up.normalized * 1.35f;
                AddUnique(list, new RespawnChoice
                {
                    title = cryo.displayName,
                    detail = "Cryobed · " + FormatPosition(pos),
                    position = pos,
                    accent = new Color(0.45f, 0.85f, 1.0f)
                });
            }

            foreach (var cryo in Object.FindObjectsByType<GridCryobed>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (cryo == null) continue;
                Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(cryo.transform.position);
                if (up.sqrMagnitude < 0.0001f) up = cryo.Grid != null ? cryo.Grid.transform.up : Vector3.up;
                Vector3 pos = cryo.transform.position + up.normalized * 1.35f;
                AddUnique(list, new RespawnChoice
                {
                    title = cryo.blockName,
                    detail = "Grid Cryobed · " + FormatPosition(pos),
                    position = pos,
                    accent = new Color(0.45f, 0.85f, 1.0f)
                });
            }

            return list;
        }

        private static void AddUnique(List<RespawnChoice> list, RespawnChoice choice)
        {
            for (int i = 0; i < list.Count; i++)
                if ((list[i].position - choice.position).sqrMagnitude < 1.0f) return;
            list.Add(choice);
        }

        private static string FormatPosition(Vector3 p)
            => $"{p.x:0}, {p.y:0}, {p.z:0}";
    }
}
