// Assets/Scripts/VoxelEngine/UI/BuildFeedbackHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║         NOTIFICATION FEED — Stacked above vitals bar           ║
// ║   Slim toast cards with accent left-bar, icon, title+detail.  ║
// ║   Entries slide in and fade out over their lifetime.           ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class BuildFeedbackHud
    {
        // ── State ──────────────────────────────────────────────────────
        private static VisualElement _root, _container;

        private struct FeedEntry
        {
            public VisualElement element;
            public float         spawnTime;
            public float         lifetime;
        }
        private static readonly List<FeedEntry> _entries  = new();
        private const float DEFAULT_LIFETIME  = 3.8f;
        private const float FADE_DURATION     = 0.55f;
        private const int   MAX_VISIBLE       = 6;

        // ── Mount ──────────────────────────────────────────────────────
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _container != null && _container.parent == uiRoot) return;
            _root = uiRoot;
            if (_container != null) _container.RemoveFromHierarchy();

            _container = new VisualElement { name = "FeedbackHud" };
            _container.style.position      = Position.Absolute;
            _container.style.right         = 18;
            _container.style.bottom        = 16 + VitalsHud.TOTAL_HEIGHT + 8;
            _container.style.width         = 250;
            _container.style.flexDirection = FlexDirection.ColumnReverse;
            _container.style.alignItems    = Align.Stretch;
            _container.pickingMode         = PickingMode.Ignore;
            uiRoot.Add(_container);
        }

        // ── Public API ─────────────────────────────────────────────────
        public static void Show(string title, string detail = "", Sprite icon = null, Color? tint = null)
        {
            if (_container == null) return;
            Color accent = tint ?? T.AccentGreen;

            // Toast card.
            var card = new VisualElement();
            card.style.flexDirection   = FlexDirection.Row;
            card.style.alignItems      = Align.Center;
            card.style.marginTop       = 4;
            card.style.paddingTop      = 7;
            card.style.paddingBottom   = 7;
            card.style.paddingLeft     = 0;
            card.style.paddingRight    = 10;
            card.style.backgroundColor = new StyleColor(new Color(T.BgPanel.r, T.BgPanel.g, T.BgPanel.b, 0.93f));
            T.Radius(card, 5);
            T.Border(card, 1, new Color(T.BorderDim.r, T.BorderDim.g, T.BorderDim.b, 0.70f));
            card.pickingMode = PickingMode.Ignore;

            // Accent left stripe.
            var stripe = new VisualElement();
            stripe.style.width            = 3;
            stripe.style.alignSelf        = Align.Stretch;
            stripe.style.backgroundColor  = new StyleColor(accent);
            stripe.style.marginLeft       = 0;
            stripe.style.marginRight      = 10;
            stripe.style.borderTopLeftRadius   = 5;
            stripe.style.borderBottomLeftRadius = 5;
            stripe.pickingMode = PickingMode.Ignore;
            card.Add(stripe);

            // Optional sprite icon.
            if (icon != null)
            {
                var img = new Image { sprite = icon };
                img.scaleMode = ScaleMode.ScaleToFit; // match BuildSlot: tight-cropped generated icons must fit, not crop (fixes blank recipe/crafter icons)
                img.style.width     = 20;
                img.style.height    = 20;
                img.style.marginRight = 8;
                img.style.flexShrink = 0;
                img.pickingMode = PickingMode.Ignore;
                card.Add(img);
            }

            // Text column.
            var textCol = new VisualElement();
            textCol.style.flexGrow  = 1;
            textCol.pickingMode     = PickingMode.Ignore;

            var t1 = new Label(title);
            t1.style.color                   = new StyleColor(T.TextPrimary);
            t1.style.fontSize                = 11;
            t1.style.unityFontStyleAndWeight = FontStyle.Bold;
            t1.pickingMode = PickingMode.Ignore;
            textCol.Add(t1);

            if (!string.IsNullOrEmpty(detail))
            {
                var t2 = new Label(detail);
                t2.style.color     = new StyleColor(T.TextMuted);
                t2.style.fontSize  = 9;
                t2.style.marginTop = 1;
                t2.pickingMode     = PickingMode.Ignore;
                textCol.Add(t2);
            }
            card.Add(textCol);

            // Accent dot on far right — colour matches tint.
            var dot = new VisualElement();
            dot.style.width           = 5;
            dot.style.height          = 5;
            dot.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.60f));
            T.Radius(dot, 3f);
            dot.style.marginLeft      = 8;
            dot.style.flexShrink      = 0;
            dot.pickingMode           = PickingMode.Ignore;
            card.Add(dot);

            // Insert newest at bottom of reversed column = visual top.
            if (_container.childCount > 0) _container.Insert(0, card);
            else _container.Add(card);

            _entries.Add(new FeedEntry
            {
                element   = card,
                spawnTime = Time.unscaledTime,
                lifetime  = DEFAULT_LIFETIME
            });

            // Cull oldest entries.
            while (_entries.Count > MAX_VISIBLE)
            {
                _entries[0].element.RemoveFromHierarchy();
                _entries.RemoveAt(0);
            }
        }

        // ── Convenience Overloads ──────────────────────────────────────
        public static void ShowItemUsed(string action, ItemDefinition item, int count)
        {
            if (item == null) return;
            Show(action,
                 count > 0 ? $"−{count}  {item.displayName}" : item.displayName,
                 item.icon, T.AccentOrange);
        }

        public static void ShowBlockPlaced(string name, ItemDefinition cost = null, int count = 0)
        {
            Show(name,
                 cost != null && count > 0 ? $"−{count}  {cost.displayName}" : "Placed",
                 cost?.icon, T.AccentGreen);
        }

        // ── Tick ───────────────────────────────────────────────────────
        public static void Tick()
        {
            if (_entries.Count == 0) return;
            float now = Time.unscaledTime;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e   = _entries[i];
                float age = now - e.spawnTime;

                // Fade in during first 0.15s.
                if (age < 0.15f)
                    e.element.style.opacity = age / 0.15f;
                // Fade out during last FADE_DURATION seconds.
                else if (age > e.lifetime - FADE_DURATION)
                    e.element.style.opacity = 1f - Mathf.Clamp01((age - (e.lifetime - FADE_DURATION)) / FADE_DURATION);
                else
                    e.element.style.opacity = 1f;

                if (age >= e.lifetime)
                {
                    e.element.RemoveFromHierarchy();
                    _entries.RemoveAt(i);
                }
            }
        }
    }
}
