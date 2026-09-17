// Assets/Scripts/VoxelEngine/UI/LivestockPenHud.cs
//
// The livestock pen console: herd roster, supply state, and the population cap.
//
// The roster is the point. A pen that only showed totals would tell the player their
// farm was "fine" right up until animals started dying, because an average hides the
// one starving sheep. Listing every animal with its own condition means neglect is
// visible as a specific animal with a specific problem, which is something the player
// can actually act on.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Farming;
using Cursor = UnityEngine.Cursor;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class LivestockPenHud
    {
        private static VisualElement _root, _scrim, _body;
        private static bool _open, _blocking;
        private static LivestockPen _pen;
        private static float _refreshTimer;

        public static bool IsOpen => _open;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _scrim != null && _scrim.parent == uiRoot) return;
            _root = uiRoot;
            if (_scrim != null) _scrim.RemoveFromHierarchy();

            _scrim = new VisualElement { name = "LivestockPenHud" };
            _scrim.style.position = Position.Absolute;
            _scrim.style.left = 0; _scrim.style.top = 0;
            _scrim.style.right = 0; _scrim.style.bottom = 0;
            _scrim.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
            _scrim.style.alignItems = Align.Center;
            _scrim.style.justifyContent = Justify.Center;
            _scrim.style.display = DisplayStyle.None;
            _root.Add(_scrim);

            _body = new VisualElement();
            _body.style.width = 440;
            _body.style.maxHeight = Length.Percent(86);
            _body.style.paddingLeft = 16; _body.style.paddingRight = 16;
            _body.style.paddingTop = 14; _body.style.paddingBottom = 14;
            _body.style.backgroundColor = new StyleColor(new Color(0.055f, 0.070f, 0.055f, 0.99f));
            T.Radius(_body, 5f);
            T.Border(_body, 1f, new Color(0.30f, 0.44f, 0.26f, 0.95f));
            _scrim.Add(_body);
        }

        public static void Open(LivestockPen pen)
        {
            if (pen == null || _scrim == null) return;
            _pen = pen;
            _open = true;
            _scrim.style.display = DisplayStyle.Flex;
            if (!_blocking) { UIState.PushBlock(); _blocking = true; }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Rebuild();
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false;
            if (_scrim != null) _scrim.style.display = DisplayStyle.None;
            if (_blocking) { UIState.PopBlock(); _blocking = false; }
            _pen = null;
        }

        public static void Tick()
        {
            if (!_open) return;

            if (!UIState.TextInputActive
                && VoxelEngine.Settings.GameSettings.WasPressed(VoxelEngine.Settings.InputAction.Pause))
            {
                Close();
                UIState.PauseConsumedFrame = Time.frameCount;
                return;
            }

            if (_pen == null) { Close(); return; }

            // Half-second refresh: needs drain slowly, so a per-frame rebuild would burn
            // layout work to show numbers that have not visibly changed.
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 0.5f;
                Rebuild();
            }
        }

        private static void Rebuild()
        {
            if (_body == null || _pen == null) return;
            _body.Clear();

            // ── Header ──
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            head.style.marginBottom = 10;

            var title = new Label("LIVESTOCK PEN");
            title.style.flexGrow = 1;
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.4f;
            title.style.color = new StyleColor(new Color(0.62f, 0.90f, 0.52f));
            head.Add(title);

            bool full = _pen.Population >= _pen.populationLimit;
            var pill = new Label($"{_pen.Population}/{_pen.populationLimit}");
            pill.style.fontSize = 9;
            pill.style.unityFontStyleAndWeight = FontStyle.Bold;
            pill.style.color = new StyleColor(full ? T.AccentAmber : new Color(0.45f, 0.88f, 0.52f));
            pill.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.35f));
            pill.style.paddingLeft = 8; pill.style.paddingRight = 8;
            pill.style.paddingTop = 3; pill.style.paddingBottom = 3;
            T.Radius(pill, 9f);
            head.Add(pill);
            _body.Add(head);

            Info(_pen.Status, _pen.Population == 0 ? T.TextMuted : Color.white);

            // ── Herd roster ──
            Section("HERD");
            var members = _pen.Members;
            if (members.Count == 0)
            {
                Info("No animals within the pen radius. Lead animals here, or place the " +
                     "pen where they already graze.", T.TextMuted);
            }

            for (int i = 0; i < members.Count; i++)
            {
                var animal = members[i];
                if (animal == null) continue;
                _body.Add(AnimalRow(animal));
            }

            // ── Supply ──
            Section("SUPPLY");
            Info("Feed accepts wheat, grain, hay, biomass and root crops. Water accepts " +
                 "any water item. Both can be piped or belted in.", T.TextMuted);

            var openSupply = new Button(() =>
            {
                var pen = _pen;
                Close();
                pen.EnsureContainers();
                GameUIController.Instance?.OpenContainer(pen.supply);
            })
            { text = "OPEN FEED & WATER" };
            openSupply.style.height = 26;
            openSupply.style.fontSize = 10;
            openSupply.style.marginTop = 6;
            _body.Add(openSupply);

            var openOutput = new Button(() =>
            {
                var pen = _pen;
                Close();
                pen.EnsureContainers();
                GameUIController.Instance?.OpenContainer(pen.output);
            })
            { text = "OPEN PRODUCE" };
            openOutput.style.height = 26;
            openOutput.style.fontSize = 10;
            openOutput.style.marginTop = 4;
            _body.Add(openOutput);

            if (_pen.ReadyToHarvest > 0)
            {
                Info($"{_pen.ReadyToHarvest} product(s) waiting - the produce container is " +
                     "full or the item is not authored.", T.AccentAmber);
            }

            var close = new Button(Close) { text = "CLOSE" };
            close.style.marginTop = 12;
            close.style.height = 26;
            close.style.fontSize = 9;
            _body.Add(close);
        }

        private static VisualElement AnimalRow(LivestockHusbandry animal)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            row.style.paddingLeft = 6; row.style.paddingRight = 6;
            row.style.paddingTop = 3; row.style.paddingBottom = 3;
            row.style.backgroundColor = new StyleColor(new Color(0.075f, 0.09f, 0.075f, 0.95f));
            T.Radius(row, 3f);

            bool unwell = animal.Hunger < 25f || animal.Thirst < 25f || animal.Health01 < 0.5f;
            T.Border(row, 1f, unwell
                ? new Color(0.85f, 0.55f, 0.25f, 0.85f)
                : new Color(0.16f, 0.24f, 0.16f, 0.9f));

            var name = new Label(animal.SpeciesLabel);
            name.style.width = 56;
            name.style.fontSize = 10;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.color = new StyleColor(Color.white);
            row.Add(name);

            row.Add(Meter("FOOD", animal.Hunger / 100f, new Color(0.85f, 0.72f, 0.32f)));
            row.Add(Meter("WATER", animal.Thirst / 100f, new Color(0.38f, 0.72f, 0.95f)));

            var condition = new Label(animal.ConditionLabel);
            condition.style.flexGrow = 1;
            condition.style.unityTextAlign = TextAnchor.MiddleRight;
            condition.style.fontSize = 9;
            condition.style.color = new StyleColor(
                animal.HasProduct ? new Color(0.45f, 0.90f, 0.55f)
                : unwell ? T.AccentAmber
                : T.TextSecondary);
            row.Add(condition);

            return row;
        }

        private static VisualElement Meter(string label, float fill01, Color ink)
        {
            var wrap = new VisualElement();
            wrap.style.marginRight = 8;

            var caption = new Label(label);
            caption.style.fontSize = 7;
            caption.style.letterSpacing = 0.8f;
            caption.style.color = new StyleColor(new Color(0.45f, 0.52f, 0.45f));
            wrap.Add(caption);

            var track = new VisualElement();
            track.style.width = 46;
            track.style.height = 5;
            track.style.backgroundColor = new StyleColor(new Color(0.04f, 0.05f, 0.04f));
            T.Radius(track, 3f);
            track.style.overflow = Overflow.Hidden;
            wrap.Add(track);

            var fill = new VisualElement();
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(Mathf.Clamp01(fill01) * 100f);
            fill.style.backgroundColor = new StyleColor(fill01 < 0.25f ? T.AccentAmber : ink);
            track.Add(fill);

            return wrap;
        }

        private static void Section(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 9;
            label.style.letterSpacing = 1.3f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(new Color(0.50f, 0.62f, 0.50f));
            label.style.marginTop = 10;
            label.style.marginBottom = 4;
            _body.Add(label);
        }

        private static void Info(string text, Color? color = null)
        {
            var label = new Label(text);
            label.style.fontSize = 10;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = new StyleColor(color ?? T.TextSecondary);
            _body.Add(label);
        }
    }
}
