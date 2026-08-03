// Assets/Scripts/VoxelEngine/UI/CraftingScreen.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          INDUSTRIAL WORLD — RUST-STYLE CRAFTING SCREEN         ║
// ║                                                                  ║
// ║  A premium, self-contained crafting surface inspired by Rust's  ║
// ║  blueprint menu but rebuilt to our sleek dark-steel design       ║
// ║  language:                                                       ║
// ║                                                                  ║
// ║   ┌────────┬──────────────────────┬──────────────────────┐     ║
// ║   │ CATS   │  RECIPE TILE GRID     │  DETAIL + QUEUE       │     ║
// ║   │ All    │  [▦][▦][▦][▦]         │  [icon] Name xN       │     ║
// ║   │ Tools  │  [▦][▦][▦][▦]         │  ⏱ 3.0s              │     ║
// ║   │ ...    │   …                   │  ingredients          │     ║
// ║   │        │                       │  [- N +]  [ CRAFT ]   │     ║
// ║   │        │                       │  ── queue ──          │     ║
// ║   └────────┴──────────────────────┴──────────────────────┘     ║
// ║                                                                  ║
// ║  • Player toggles the screen open/closed from the inventory.    ║
// ║  • Visibility + last category/search/selection PERSIST across   ║
// ║    UI rebuilds AND across game sessions (PlayerPrefs).          ║
// ║  • Pure UI Toolkit, fully code-built, zero scene dependencies.  ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    /// <summary>
    /// Stateless renderer for the crafting panel. All transient state
    /// (selected recipe, category, search text, craft amount) is keyed by a
    /// caller-supplied <c>panelId</c> so the inventory pane and any workstation
    /// pane each keep their own independent selection.
    /// </summary>
    public static class CraftingScreen
    {
        // ── Persistent open/closed state (survives sessions) ───────────────
        private const string VisKey = "iw.craft.visible";
        private static int _visCache = -1;

        /// <summary>Whether the crafting screen is currently shown. Persisted.</summary>
        public static bool Visible
        {
            get
            {
                if (_visCache < 0) _visCache = PlayerPrefs.GetInt(VisKey, 1); // default: shown
                return _visCache == 1;
            }
            set
            {
                int v = value ? 1 : 0;
                if (v == _visCache) return;
                _visCache = v;
                PlayerPrefs.SetInt(VisKey, v);
                PlayerPrefs.Save();
            }
        }

        // ── Per-panel transient selection state ────────────────────────────
        private static readonly Dictionary<string, string> _category  = new();
        private static readonly Dictionary<string, string> _search    = new();
        private static readonly Dictionary<string, string> _selected  = new(); // RecipeDefinition.GetName()
        private static readonly Dictionary<string, int>    _amount    = new();

        private static string GetCat(string id)  => _category.TryGetValue(id, out var v) ? v : "All";
        private static string GetSearch(string id) => _search.TryGetValue(id, out var v) ? v : "";
        private static string GetSel(string id)  => _selected.TryGetValue(id, out var v) ? v : null;
        private static int    GetAmt(string id)  => _amount.TryGetValue(id, out var v) ? Mathf.Max(1, v) : 1;

        // ── Tunables ───────────────────────────────────────────────────────
        private const float TILE      = 58f;
        private const float TILE_ICON = 44f;
        private const float RAIL_W    = 96f;
        private const float DETAIL_W  = 224f;
        private const int   QUEUE_CAP = 10; // matches Crafter/queue behaviour

        // ───────────────────────────────────────────────────────────────────
        //  TOGGLE BUTTON — drop this into the inventory panel header.
        // ───────────────────────────────────────────────────────────────────
        /// <summary>
        /// Builds the "show / hide crafting" pill the player clicks inside the
        /// inventory. Calls <paramref name="onToggled"/> after flipping state so
        /// the host can re-render.
        /// </summary>
        public static VisualElement ToggleButton(Action onToggled)
        {
            bool open = Visible;

            var btn = new VisualElement();
            btn.style.flexDirection  = FlexDirection.Row;
            btn.style.alignItems     = Align.Center;
            btn.style.justifyContent = Justify.SpaceBetween;
            btn.style.height         = 34;
            btn.style.paddingLeft    = 12;
            btn.style.paddingRight   = 12;
            btn.style.marginTop      = 10;
            Color baseBg = open ? LcdHudTheme.Glass : LcdHudTheme.GlassDark;
            btn.style.backgroundColor = new StyleColor(baseBg);
            T.Radius(btn, 1f);
            T.Border(btn, 1, open ? LcdHudTheme.Phosphor : LcdHudTheme.Bezel);

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Row;
            left.style.alignItems    = Align.Center;
            left.pickingMode         = PickingMode.Ignore;

            var icon = new Label("\u2692"); // ⚒ hammer & pick
            icon.style.fontSize   = 15;
            icon.style.color      = new StyleColor(open ? LcdHudTheme.Phosphor : LcdHudTheme.PhosphorDim);
            icon.style.marginRight = 8;
            icon.pickingMode      = PickingMode.Ignore;
            left.Add(icon);

            var label = new Label("CRAFTING");
            label.style.fontSize              = 11;
            label.style.letterSpacing         = 1.6f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color                 = new StyleColor(open ? LcdHudTheme.Phosphor : T.TextSecondary);
            label.pickingMode                 = PickingMode.Ignore;
            left.Add(label);
            btn.Add(left);

            var chev = new Label(open ? "\u25BC" : "\u25B6"); // ▼ / ▶
            chev.style.fontSize = 10;
            chev.style.color    = new StyleColor(open ? LcdHudTheme.Phosphor : T.TextMuted);
            chev.pickingMode    = PickingMode.Ignore;
            btn.Add(chev);

            // Micro-interaction: hover sheen + subtle scale.
            btn.style.transitionProperty = new List<StylePropertyName> { "scale", "background-color" };
            btn.style.transitionDuration = new List<TimeValue> { new TimeValue(0.10f, TimeUnit.Second), new TimeValue(0.10f, TimeUnit.Second) };
            btn.RegisterCallback<MouseEnterEvent>(_ =>
            {
                btn.style.scale = new StyleScale(new Scale(new Vector3(1.015f, 1.015f, 1f)));
                btn.style.backgroundColor = new StyleColor(open
                    ? new Color(LcdHudTheme.Phosphor.r, LcdHudTheme.Phosphor.g, LcdHudTheme.Phosphor.b, 0.20f)
                    : new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.85f));
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                btn.style.scale = new StyleScale(new Scale(Vector3.one));
                btn.style.backgroundColor = new StyleColor(baseBg);
            });
            VoxelEngine.FX.UiAudio.MarkClickable(btn);
            btn.RegisterCallback<ClickEvent>(_ =>
            {
                Visible = !Visible;
                onToggled?.Invoke();
            });

            return btn;
        }

        // ───────────────────────────────────────────────────────────────────
        //  MAIN RENDER — fills a host panel with the full crafting surface.
        // ───────────────────────────────────────────────────────────────────
        /// <summary>
        /// Populates <paramref name="panel"/> (a pre-positioned UITheme panel) with
        /// the crafting UI.
        /// </summary>
        /// <param name="recipes">Recipes available at the current station tier.</param>
        /// <param name="source">Container ingredients are pulled FROM (inventory / network).</param>
        /// <param name="dest">Container outputs go TO.</param>
        /// <param name="resolveQueue">Maps a recipe to the queue it would run in (may return null).</param>
        /// <param name="refresh">Host callback to fully re-render the owning UI.</param>
        /// <param name="setSearchFocus">Host hook: true while the search field owns the keyboard.</param>
        /// <param name="panelId">Stable id keeping per-pane selection independent.</param>
        public static void Populate(
            VisualElement panel,
            List<RecipeDefinition> recipes,
            IItemContainer source,
            IItemContainer dest,
            Func<RecipeDefinition, CraftQueue> resolveQueue,
            Action refresh,
            Action<bool> setSearchFocus,
            string panelId)
        {
            recipes ??= new List<RecipeDefinition>();
            LcdHudTheme.ApplyChassis(panel, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.96f), 3f);

            // ── Header: badge + title + search ──────────────────────────────
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems    = Align.Center;
            header.style.marginBottom  = 6;

            header.Add(T.IconBadge("\u2692", LcdHudTheme.Phosphor));
            var title = T.Title("CRAFTING");
            title.style.flexGrow = 1;
            title.style.color = new StyleColor(LcdHudTheme.Phosphor);
            header.Add(title);

            var search = new TextField { value = GetSearch(panelId) };
            search.style.width      = 150;
            search.style.minHeight  = 26;
            search.style.alignSelf  = Align.Center;
            header.Add(search);
            panel.Add(header);
            panel.Add(T.AccentDivider(LcdHudTheme.Phosphor));

            // ── Body: 3 columns ─────────────────────────────────────────────
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow      = 1;
            panel.Add(body);

            // Empty-state shortcut.
            if (recipes.Count == 0)
            {
                body.Add(T.Muted("No recipes available yet — craft a Crafting Bench to unlock more."));
                WireSearch(search, panelId, setSearchFocus, null);
                return;
            }

            // Categories from output items.
            var catSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in recipes)
            {
                if (r == null || r.outputItem == null) continue;
                catSet.Add(string.IsNullOrEmpty(r.outputItem.category) ? "Misc" : r.outputItem.category);
            }
            var cats = new List<string> { "All" };
            cats.AddRange(catSet);

            // ── Column 1: category rail ─────────────────────────────────────
            var rail = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(rail);   // themed slim scrollbar
            rail.style.width      = RAIL_W;
            rail.style.flexShrink = 0;
            rail.style.marginRight = 8;
            body.Add(rail);

            // ── Column 2: tile grid ─────────────────────────────────────────
            var gridScroll = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(gridScroll);   // themed slim scrollbar
            gridScroll.style.flexGrow = 1;
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap      = Wrap.Wrap;
            gridScroll.Add(grid);
            body.Add(gridScroll);

            // ── Column 3: detail + queue ────────────────────────────────────
            var detail = new VisualElement();
            detail.style.width      = DETAIL_W;
            detail.style.flexShrink = 0;
            detail.style.marginLeft = 8;
            detail.style.paddingLeft   = 12;
            detail.style.paddingRight  = 12;
            detail.style.paddingTop    = 12;
            detail.style.paddingBottom = 12;
            detail.style.backgroundColor = new StyleColor(T.BgCard);
            T.Radius(detail, T.CardRadius);
            T.Border(detail, 1, T.BorderDim);
            body.Add(detail);

            // ── Local re-render helpers (avoid full Refresh → keeps focus) ───
            void RebuildRail()
            {
                rail.Clear();
                string active = GetCat(panelId);
                foreach (var c in cats)
                {
                    bool isActive = string.Equals(c, active, StringComparison.OrdinalIgnoreCase);
                    var pill = new VisualElement();
                    pill.style.height        = 28;
                    pill.style.marginBottom  = 4;
                    pill.style.paddingLeft   = 9;
                    pill.style.justifyContent = Justify.Center;
                    pill.style.backgroundColor = new StyleColor(isActive
                        ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.18f)
                        : T.BgSlot);
                    T.Radius(pill, 5);
                    T.Border(pill, 1, isActive ? T.BorderBright : T.BorderSubtle);

                    var l = new Label(c);
                    l.style.fontSize = 10;
                    l.style.unityFontStyleAndWeight = FontStyle.Bold;
                    l.style.letterSpacing = 0.6f;
                    l.style.color = new StyleColor(isActive ? T.AccentCyan : T.TextSecondary);
                    l.style.overflow = Overflow.Hidden;
                    l.style.textOverflow = TextOverflow.Ellipsis;
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    l.pickingMode = PickingMode.Ignore;
                    pill.Add(l);

                    string cap = c;
                    pill.RegisterCallback<MouseEnterEvent>(_ => { if (!IsCat(panelId, cap)) pill.style.backgroundColor = new StyleColor(T.BgHover); });
                    pill.RegisterCallback<MouseLeaveEvent>(_ => { if (!IsCat(panelId, cap)) pill.style.backgroundColor = new StyleColor(T.BgSlot); });
                    VoxelEngine.FX.UiAudio.MarkClickable(pill);
                    pill.RegisterCallback<ClickEvent>(_ =>
                    {
                        _category[panelId] = cap;
                        RebuildRail();
                        RebuildGrid();
                    });
                    rail.Add(pill);
                }
            }

            void RebuildGrid()
            {
                grid.Clear();
                string cat = GetCat(panelId);
                string q   = (GetSearch(panelId) ?? "").Trim().ToLowerInvariant();
                string sel = GetSel(panelId);
                int shown  = 0;

                foreach (var r in recipes)
                {
                    if (r == null) continue;
                    string rc = (r.outputItem != null && !string.IsNullOrEmpty(r.outputItem.category)) ? r.outputItem.category : "Misc";
                    if (!string.Equals(cat, "All", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(cat, rc, StringComparison.OrdinalIgnoreCase)) continue;
                    if (q.Length > 0 && !r.GetName().ToLowerInvariant().Contains(q)) continue;

                    grid.Add(BuildTile(r, source, resolveQueue, panelId, string.Equals(r.GetName(), sel, StringComparison.Ordinal),
                        () => { _selected[panelId] = r.GetName(); RebuildGrid(); RebuildDetail(); }));
                    shown++;
                }

                if (shown == 0)
                {
                    var none = T.Muted("No recipes match your filter.");
                    none.style.marginLeft = 4;
                    grid.Add(none);
                }
            }

            void RebuildDetail()
            {
                detail.Clear();
                var sel = ResolveSelected(recipes, panelId);
                if (sel == null)
                {
                    var hint = T.Muted("Select an item on the left to view its recipe and craft it.");
                    hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                    hint.style.marginTop = 24;
                    detail.Add(hint);
                    return;
                }
                BuildDetail(detail, sel, source, dest, resolveQueue, refresh, panelId, RebuildDetail, RebuildGrid);
            }

            // Wire the search field to rebuild ONLY the grid (keeps focus).
            WireSearch(search, panelId, setSearchFocus, RebuildGrid);

            RebuildRail();
            RebuildGrid();
            RebuildDetail();
        }

        private static bool IsCat(string id, string c) =>
            string.Equals(GetCat(id), c, StringComparison.OrdinalIgnoreCase);

        private static RecipeDefinition ResolveSelected(List<RecipeDefinition> recipes, string panelId)
        {
            string sel = GetSel(panelId);
            if (string.IsNullOrEmpty(sel)) return null;
            foreach (var r in recipes)
                if (r != null && string.Equals(r.GetName(), sel, StringComparison.Ordinal)) return r;
            return null;
        }

        private static void WireSearch(TextField field, string panelId, Action<bool> setSearchFocus, Action onChanged)
        {
            field.RegisterCallback<FocusInEvent>(_  => setSearchFocus?.Invoke(true));
            field.RegisterCallback<FocusOutEvent>(_ => setSearchFocus?.Invoke(false));
            field.RegisterValueChangedCallback(e =>
            {
                _search[panelId] = e.newValue;
                onChanged?.Invoke();
            });
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) { field.Blur(); e.StopPropagation(); }
            });
        }

        // ───────────────────────────────────────────────────────────────────
        //  RECIPE TILE
        // ───────────────────────────────────────────────────────────────────
        private static VisualElement BuildTile(
            RecipeDefinition recipe, IItemContainer source,
            Func<RecipeDefinition, CraftQueue> resolveQueue,
            string panelId, bool selected, Action onClick)
        {
            bool craftable = Crafter.HasIngredients(source, recipe);

            var tile = new VisualElement();
            tile.style.width  = TILE; tile.style.height = TILE;
            tile.style.marginRight = 5; tile.style.marginBottom = 5;
            tile.style.alignItems     = Align.Center;
            tile.style.justifyContent = Justify.Center;
            tile.style.backgroundColor = new StyleColor(selected ? T.BgActive : T.BgSlot);
            T.Radius(tile, 6);
            T.Border(tile, selected ? 2 : 1, selected ? T.AccentCyan : T.BorderDim);
            tile.style.opacity = craftable ? 1f : 0.45f;

            // Icon
            var sprite = recipe.GetIcon();
            if (sprite != null)
            {
                var img = new Image { sprite = sprite };
                img.scaleMode = ScaleMode.ScaleToFit; // match BuildSlot: tight-cropped generated icons must fit, not crop (fixes blank recipe/crafter icons)
                img.style.width = TILE_ICON; img.style.height = TILE_ICON;
                img.pickingMode = PickingMode.Ignore;
                tile.Add(img);
            }
            else
            {
                var box = new VisualElement();
                box.style.width = TILE_ICON - 8; box.style.height = TILE_ICON - 8;
                box.style.backgroundColor = new StyleColor(recipe.outputItem != null ? recipe.outputItem.iconTint : Color.gray);
                T.Radius(box, 4);
                box.pickingMode = PickingMode.Ignore;
                tile.Add(box);
            }

            // Output count badge
            if (recipe.outputCount > 1)
            {
                var cnt = new Label(recipe.outputCount.ToString());
                cnt.style.position = Position.Absolute;
                cnt.style.bottom = 2; cnt.style.right = 4;
                cnt.style.fontSize = 11;
                cnt.style.unityFontStyleAndWeight = FontStyle.Bold;
                cnt.style.color = Color.white;
                cnt.style.paddingLeft = 3; cnt.style.paddingRight = 3;
                cnt.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.6f));
                T.Radius(cnt, 3);
                cnt.pickingMode = PickingMode.Ignore;
                tile.Add(cnt);
            }

            // Queued-count chip (top-left) when this recipe is already crafting.
            int queued = CountQueued(recipe, resolveQueue);
            if (queued > 0)
            {
                var chip = new Label("x" + queued);
                chip.style.position = Position.Absolute;
                chip.style.top = 2; chip.style.left = 3;
                chip.style.fontSize = 9;
                chip.style.unityFontStyleAndWeight = FontStyle.Bold;
                chip.style.color = new StyleColor(T.AccentGold);
                chip.style.paddingLeft = 3; chip.style.paddingRight = 3;
                chip.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.55f));
                T.Radius(chip, 3);
                chip.pickingMode = PickingMode.Ignore;
                tile.Add(chip);
            }

            // Micro-interaction: hover pop.
            tile.style.transitionProperty = new List<StylePropertyName> { "scale", "background-color" };
            tile.style.transitionDuration = new List<TimeValue> { new TimeValue(0.09f, TimeUnit.Second), new TimeValue(0.09f, TimeUnit.Second) };
            tile.RegisterCallback<MouseEnterEvent>(_ =>
            {
                tile.style.scale = new StyleScale(new Scale(new Vector3(1.07f, 1.07f, 1f)));
                if (!selected) { tile.style.backgroundColor = new StyleColor(T.BgHover); T.Border(tile, 1, T.BorderBright); }
            });
            tile.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                tile.style.scale = new StyleScale(new Scale(Vector3.one));
                if (!selected) { tile.style.backgroundColor = new StyleColor(T.BgSlot); T.Border(tile, 1, T.BorderDim); }
            });
            VoxelEngine.FX.UiAudio.MarkClickable(tile);
            tile.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());

            return tile;
        }

        // ───────────────────────────────────────────────────────────────────
        //  DETAIL PANEL  (selected recipe: ingredients, stepper, craft, queue)
        // ───────────────────────────────────────────────────────────────────
        private static void BuildDetail(
            VisualElement detail, RecipeDefinition recipe,
            IItemContainer source, IItemContainer dest,
            Func<RecipeDefinition, CraftQueue> resolveQueue,
            Action refresh, string panelId,
            Action rebuildDetail, Action rebuildGrid)
        {
            // Header: icon + name.
            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems    = Align.Center;
            top.style.marginBottom  = 8;

            var iconWrap = new VisualElement();
            iconWrap.style.width = 48; iconWrap.style.height = 48;
            iconWrap.style.marginRight = 10;
            iconWrap.style.alignItems = Align.Center; iconWrap.style.justifyContent = Justify.Center;
            iconWrap.style.backgroundColor = new StyleColor(T.BgSlot);
            T.Radius(iconWrap, 6); T.Border(iconWrap, 1, T.BorderDim);
            var sprite = recipe.GetIcon();
            if (sprite != null)
            {
                var img = new Image { sprite = sprite };
                img.scaleMode = ScaleMode.ScaleToFit; // match BuildSlot: tight-cropped generated icons must fit, not crop (fixes blank recipe/crafter icons)
                img.style.width = 40; img.style.height = 40;
                img.pickingMode = PickingMode.Ignore;
                iconWrap.Add(img);
            }
            top.Add(iconWrap);

            var nameCol = new VisualElement();
            nameCol.style.flexGrow = 1;
            var nm = new Label(recipe.outputCount > 1 ? $"{recipe.GetName()}  ×{recipe.outputCount}" : recipe.GetName());
            nm.style.color = new StyleColor(T.TextPrimary);
            nm.style.fontSize = 14;
            nm.style.unityFontStyleAndWeight = FontStyle.Bold;
            nm.style.whiteSpace = WhiteSpace.Normal;
            nameCol.Add(nm);

            string timeStr = recipe.craftSeconds > 0f ? $"\u23F1  {recipe.craftSeconds:0.0}s" : "\u23F1  Instant";
            var tm = new Label(timeStr);
            tm.style.color = new StyleColor(recipe.craftSeconds > 0f ? T.AccentGold : T.TextMuted);
            tm.style.fontSize = 10;
            tm.style.marginTop = 2;
            nameCol.Add(tm);
            top.Add(nameCol);
            detail.Add(top);

            // Optional description.
            if (recipe.outputItem != null && !string.IsNullOrEmpty(recipe.outputItem.description))
            {
                var desc = T.Muted(recipe.outputItem.description);
                desc.style.marginBottom = 4;
                detail.Add(desc);
            }

            detail.Add(T.AccentDivider());

            // Ingredients.
            detail.Add(T.Subtitle("Required"));
            int maxByIngredients = int.MaxValue;
            if (recipe.inputs != null)
            {
                foreach (var ing in recipe.inputs)
                {
                    if (ing.item == null || ing.count <= 0) continue;
                    int have = source.CountOf(ing.item);
                    bool ok = have >= ing.count;
                    maxByIngredients = Mathf.Min(maxByIngredients, have / ing.count);

                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems    = Align.Center;
                    row.style.marginBottom  = 3;

                    // tiny swatch
                    var sw = new VisualElement();
                    sw.style.width = 18; sw.style.height = 18; sw.style.marginRight = 7;
                    sw.style.alignItems = Align.Center; sw.style.justifyContent = Justify.Center;
                    T.Radius(sw, 3);
                    if (ing.item.icon != null)
                    {
                        var im = new Image { sprite = ing.item.icon };
                        im.scaleMode = ScaleMode.ScaleToFit; // match BuildSlot: tight-cropped generated icons must fit, not crop (fixes blank recipe/crafter icons)
                        im.style.width = 18; im.style.height = 18;
                        im.pickingMode = PickingMode.Ignore;
                        sw.Add(im);
                    }
                    else
                    {
                        sw.style.backgroundColor = new StyleColor(ing.item.iconTint);
                    }
                    sw.pickingMode = PickingMode.Ignore;
                    row.Add(sw);

                    var nlbl = new Label(ing.item.displayName);
                    nlbl.style.color = new StyleColor(T.TextSecondary);
                    nlbl.style.fontSize = 11;
                    nlbl.style.flexGrow = 1;
                    nlbl.pickingMode = PickingMode.Ignore;
                    row.Add(nlbl);

                    var qty = new Label($"{have}/{ing.count}");
                    qty.style.fontSize = 11;
                    qty.style.unityFontStyleAndWeight = FontStyle.Bold;
                    qty.style.color = new StyleColor(ok ? T.AccentGreen : T.TextDanger);
                    qty.pickingMode = PickingMode.Ignore;
                    row.Add(qty);

                    detail.Add(row);
                }
            }
            if (maxByIngredients == int.MaxValue) maxByIngredients = 0;

            detail.Add(T.Spacer(8));

            // Amount stepper + CRAFT.
            var queue = resolveQueue?.Invoke(recipe);
            int alreadyQueued = CountQueued(recipe, resolveQueue);
            bool usesQueue = recipe.craftSeconds > 0f && queue != null;
            int queueRoom = usesQueue ? Mathf.Max(0, QUEUE_CAP - alreadyQueued) : int.MaxValue;

            int amount = Mathf.Clamp(GetAmt(panelId), 1, Mathf.Max(1, maxByIngredients));
            _amount[panelId] = amount;

            var stepperRow = new VisualElement();
            stepperRow.style.flexDirection = FlexDirection.Row;
            stepperRow.style.alignItems    = Align.Center;
            stepperRow.style.marginBottom  = 8;

            Button StepBtn(string txt, Action act)
            {
                var b = new Button(act) { text = txt };
                b.style.width = 28; b.style.height = 28;
                b.style.fontSize = 14;
                b.style.unityFontStyleAndWeight = FontStyle.Bold;
                b.style.color = Color.white;
                b.style.backgroundColor = new StyleColor(T.BgHover);
                T.Radius(b, 4); T.Border(b, 0, Color.clear);
                return b;
            }

            stepperRow.Add(StepBtn("\u2212", () => { _amount[panelId] = Mathf.Max(1, amount - 1); rebuildDetail(); }));

            var amtBox = new Label(amount.ToString());
            amtBox.style.flexGrow = 1;
            amtBox.style.height = 28;
            amtBox.style.unityTextAlign = TextAnchor.MiddleCenter;
            amtBox.style.fontSize = 14;
            amtBox.style.unityFontStyleAndWeight = FontStyle.Bold;
            amtBox.style.color = new StyleColor(T.TextPrimary);
            amtBox.style.marginLeft = 4; amtBox.style.marginRight = 4;
            amtBox.style.backgroundColor = new StyleColor(T.BgSlot);
            T.Radius(amtBox, 4);
            amtBox.pickingMode = PickingMode.Ignore;
            stepperRow.Add(amtBox);

            stepperRow.Add(StepBtn("+", () => { _amount[panelId] = Mathf.Min(Mathf.Max(1, maxByIngredients), amount + 1); rebuildDetail(); }));

            var maxBtn = new Button(() => { _amount[panelId] = Mathf.Max(1, maxByIngredients); rebuildDetail(); }) { text = "MAX" };
            maxBtn.style.height = 28; maxBtn.style.marginLeft = 6;
            maxBtn.style.fontSize = 9; maxBtn.style.paddingLeft = 8; maxBtn.style.paddingRight = 8;
            maxBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            maxBtn.style.color = Color.white;
            maxBtn.style.backgroundColor = new StyleColor(T.BgHover);
            T.Radius(maxBtn, 4); T.Border(maxBtn, 0, Color.clear);
            stepperRow.Add(maxBtn);
            detail.Add(stepperRow);

            // CRAFT button.
            bool canCraft = maxByIngredients >= 1 && queueRoom >= 1;
            int toCraft = Mathf.Min(amount, maxByIngredients, queueRoom);
            var craftBtn = new Button(() =>
            {
                int n = Mathf.Min(GetAmt(panelId), queueRoom);
                int done = 0;
                for (int i = 0; i < n; i++)
                {
                    var q = resolveQueue?.Invoke(recipe);
                    if (!Crafter.TryCraft(source, dest, recipe, q)) break;
                    done++;
                }
                if (done > 0) { _amount[panelId] = 1; refresh?.Invoke(); }
            })
            { text = canCraft ? (toCraft > 1 ? $"CRAFT  ×{toCraft}" : "CRAFT") : (queueRoom < 1 ? "QUEUE FULL" : "MISSING ITEMS") };
            craftBtn.style.height = 38;
            craftBtn.style.fontSize = 13;
            craftBtn.style.letterSpacing = 1.2f;
            craftBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            craftBtn.style.color = Color.white;
            craftBtn.style.backgroundColor = new StyleColor(canCraft
                ? new Color(T.AccentGreen.r, T.AccentGreen.g, T.AccentGreen.b, 0.92f)
                : new Color(0.30f, 0.30f, 0.34f));
            T.Radius(craftBtn, T.ButtonRadius); T.Border(craftBtn, 0, Color.clear);
            craftBtn.SetEnabled(canCraft);
            if (canCraft)
            {
                craftBtn.style.transitionProperty = new List<StylePropertyName> { "scale", "background-color" };
                craftBtn.style.transitionDuration = new List<TimeValue> { new TimeValue(0.10f, TimeUnit.Second), new TimeValue(0.10f, TimeUnit.Second) };
                craftBtn.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    craftBtn.style.scale = new StyleScale(new Scale(new Vector3(1.02f, 1.02f, 1f)));
                    craftBtn.style.backgroundColor = new StyleColor(T.AccentGreen);
                });
                craftBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    craftBtn.style.scale = new StyleScale(new Scale(Vector3.one));
                    craftBtn.style.backgroundColor = new StyleColor(new Color(T.AccentGreen.r, T.AccentGreen.g, T.AccentGreen.b, 0.92f));
                });
            }
            detail.Add(craftBtn);

            // Active queue for this station.
            if (queue != null && queue.HasWork)
            {
                detail.Add(T.Divider());
                detail.Add(T.Subtitle("In Progress"));

                var qscroll = new ScrollView(ScrollViewMode.Vertical);
                VoxelEngine.UI.UITheme.StyleScroller(qscroll);   // themed slim scrollbar
                qscroll.style.maxHeight = 150;
                detail.Add(qscroll);

                for (int i = 0; i < queue.entries.Count; i++)
                {
                    var e = queue.entries[i];
                    if (e?.recipe == null) continue;
                    int idx = i;

                    var qrow = new VisualElement();
                    qrow.style.flexDirection = FlexDirection.Row;
                    qrow.style.alignItems    = Align.Center;
                    qrow.style.marginBottom  = 4;

                    var qn = new Label(e.recipe.GetName());
                    qn.style.color = new StyleColor(T.TextSecondary);
                    qn.style.fontSize = 10;
                    qn.style.flexGrow = 1;
                    qn.style.overflow = Overflow.Hidden;
                    qn.style.textOverflow = TextOverflow.Ellipsis;
                    qn.style.whiteSpace = WhiteSpace.NoWrap;
                    qrow.Add(qn);

                    float pct = e.recipe.craftSeconds > 0 ? Mathf.Clamp01(e.progressSeconds / e.recipe.craftSeconds) : 1f;
                    var (bar, _) = T.ProgressBar(pct, T.AccentCyan, 6, flexGrow: false);
                    bar.style.width = 58; bar.style.marginRight = 6;
                    qrow.Add(bar);

                    var cancel = new Button(() => { queue.Cancel(idx); refresh?.Invoke(); }) { text = "\u2715" };
                    cancel.style.width = 22; cancel.style.height = 22;
                    cancel.style.fontSize = 11;
                    cancel.style.color = Color.white;
                    cancel.style.backgroundColor = new StyleColor(new Color(T.AccentRed.r, T.AccentRed.g, T.AccentRed.b, 0.85f));
                    T.Radius(cancel, 4); T.Border(cancel, 0, Color.clear);
                    qrow.Add(cancel);

                    qscroll.Add(qrow);
                }
            }
        }

        // ── Queue helpers ──────────────────────────────────────────────────
        private static int CountQueued(RecipeDefinition recipe, Func<RecipeDefinition, CraftQueue> resolveQueue)
        {
            var q = resolveQueue?.Invoke(recipe);
            if (q == null) return 0;
            int n = 0;
            foreach (var e in q.entries)
                if (e != null && e.recipe == recipe) n++;
            return n;
        }
    }
}
