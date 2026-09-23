// Assets/Scripts/VoxelEngine/UI/ConfirmDialogHud.cs
//
// Small player-facing decision card (Accept / Abort) for choices the game
// cannot make alone — e.g. firing a partial warp jump. Top-center, below the
// cockpit alert banner: this is the player's channel, not a machine UI.
// One dialog at a time; showing a new one replaces the old.

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    public static class ConfirmDialogHud
    {
        private static VisualElement _root;
        private static VisualElement _card;
        private static Label _titleLabel;
        private static Label _detailLabel;
        private static Button _acceptBtn;
        private static Button _abortBtn;
        private static Action _onAccept;

        public static bool IsOpen { get; private set; }

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _card != null && _card.parent == uiRoot) return;
            _root = uiRoot;
            if (_card != null) _card.RemoveFromHierarchy();
            IsOpen = false;
            _onAccept = null;

            _card = new VisualElement { name = "ConfirmDialogHud" };
            _card.style.position = Position.Absolute;
            _card.style.top = Length.Percent(30f);
            _card.style.left = Length.Percent(50f);
            _card.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f, 0f);
            _card.style.width = new StyleLength(new Length(60f, LengthUnit.Percent));
            _card.style.maxWidth = 560;
            _card.style.flexDirection = FlexDirection.Column;
            _card.style.alignItems = Align.Center;
            _card.style.paddingTop = 12;
            _card.style.paddingBottom = 12;
            _card.style.paddingLeft = 16;
            _card.style.paddingRight = 16;
            _card.style.backgroundColor = new StyleColor(new Color(0.05f, 0.07f, 0.10f, 0.96f));
            UITheme.Radius(_card, 10f);
            UITheme.Border(_card, 1, new Color(0.35f, 0.65f, 0.85f, 0.9f));
            _card.style.display = DisplayStyle.None;
            uiRoot.Add(_card);

            _titleLabel = new Label("CONFIRM");
            _titleLabel.style.fontSize = 13;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.letterSpacing = 2.0f;
            _titleLabel.style.color = new Color(0.55f, 0.85f, 1f);
            _titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _titleLabel.pickingMode = PickingMode.Ignore;
            _card.Add(_titleLabel);

            _detailLabel = new Label();
            _detailLabel.style.marginTop = 6;
            _detailLabel.style.fontSize = 11;
            _detailLabel.style.color = new Color(0.90f, 0.92f, 0.94f);
            _detailLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _detailLabel.style.whiteSpace = WhiteSpace.Normal;
            _detailLabel.pickingMode = PickingMode.Ignore;
            _card.Add(_detailLabel);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.Center;
            row.style.marginTop = 10;
            row.pickingMode = PickingMode.Ignore;
            _card.Add(row);

            _acceptBtn = MakeButton("Accept", new Color(0.30f, 0.75f, 0.45f), () =>
            {
                var cb = _onAccept;
                Hide();
                try { cb?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            });
            row.Add(_acceptBtn);

            var spacer = new VisualElement();
            spacer.style.width = 12;
            spacer.pickingMode = PickingMode.Ignore;
            row.Add(spacer);

            _abortBtn = MakeButton("Abort", new Color(0.85f, 0.40f, 0.35f), Hide);
            row.Add(_abortBtn);
        }

        private static Button MakeButton(string text, Color accent, Action onClick)
        {
            var btn = new Button(() => onClick?.Invoke()) { text = text };
            btn.style.fontSize = 11;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.color = new StyleColor(Color.white);
            btn.style.backgroundColor = new StyleColor(new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f, 1f));
            UITheme.Border(btn, 1, new Color(accent.r, accent.g, accent.b, 0.9f));
            UITheme.Radius(btn, 4f);
            btn.style.paddingTop = 6;
            btn.style.paddingBottom = 6;
            btn.style.paddingLeft = 22;
            btn.style.paddingRight = 22;
            return btn;
        }

        public static void Show(string title, string detail, string acceptText, string abortText, Action onAccept)
        {
            if (_card == null) return;
            if (_titleLabel != null) _titleLabel.text = title ?? "CONFIRM";
            if (_detailLabel != null) _detailLabel.text = detail ?? "";
            if (_acceptBtn != null) _acceptBtn.text = acceptText ?? "Accept";
            if (_abortBtn != null) _abortBtn.text = abortText ?? "Abort";
            _onAccept = onAccept;
            IsOpen = true;
            _card.style.display = DisplayStyle.Flex;
            _card.BringToFront();
        }

        public static void Hide()
        {
            IsOpen = false;
            _onAccept = null;
            if (_card != null) _card.style.display = DisplayStyle.None;
        }
    }
}
