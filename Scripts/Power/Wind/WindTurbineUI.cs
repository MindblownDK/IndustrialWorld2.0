// Assets/Scripts/VoxelEngine/Power/Wind/WindTurbineUI.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  WIND TURBINE PANEL — premium industrial dashboard.             ║
// ║                                                                  ║
// ║  Right-click ANY part of a turbine to open. Shows:              ║
// ║   • Assembly checklist — every required part; missing parts     ║
// ║     are flagged with an italic  "- missing".                    ║
// ║   • Live power output + efficiency dial while running.          ║
// ║   • Per-part condition bars (worst part throttles the turbine). ║
// ║   • One-click full repair (steel plates).                       ║
// ║                                                                  ║
// ║  Styling: clean metallic — cool steel surfaces, a single signal ║
// ║  blue accent, generous negative space. Built 100% via UITheme.  ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Power.Wind
{
    public static class WindTurbineUI
    {
        // Signature turbine-OEM accent: deep signal blue on brushed steel.
        private static readonly Color AccentTurbine = new(0.22f, 0.56f, 0.92f);
        private static readonly Color SteelBright   = new(0.72f, 0.76f, 0.82f);

        // Scroll position survives the 4 Hz live-refresh rebuild — without this the
        // panel snapped back to the top every refresh tick. Reset per turbine.
        private static float _savedScroll;
        private static int   _savedScrollOwner;

        public static VisualElement BuildPanel(WindTurbineController c, Inventory inventory)
        {
            var p = T.MachinePanel();
            p.style.width = 512;   // a touch wider so no row ever overflows sideways
            if (c == null)
            {
                p.Add(T.Title("Wind Turbine"));
                p.Add(T.Muted("Turbine unavailable."));
                return p;
            }

            bool complete = c.IsComplete;

            // ── Header ────────────────────────────────────────────────
            string status; Color statusColor;
            if (!complete)                     { status = "INCOMPLETE";  statusColor = T.AccentAmber; }
            else if (c.CurrentOutputWatts > 1f){ status = "GENERATING";  statusColor = T.AccentGreen; }
            else                               { status = "IDLE";        statusColor = T.AccentDim;   }

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems    = Align.Center;
            headerRow.style.marginBottom  = 8;
            headerRow.pickingMode = PickingMode.Ignore;
            headerRow.Add(T.IconBadge(c.vertical ? "🌀" : "🌬", AccentTurbine));
            var title = T.Title(c.displayName);
            title.style.flexGrow = 1;
            headerRow.Add(title);
            var (pill, _) = T.StatusPill(status, statusColor);
            headerRow.Add(pill);
            p.Add(headerRow);

            var sub = T.Muted(c.vertical
                ? $"Vertical-axis turbine · rated {PowerFormatter.FormatWatts(c.ratedPowerWatts)}"
                : $"Horizontal-axis turbine · {c.rotorDiameter:0} m rotor · rated {PowerFormatter.FormatWatts(c.ratedPowerWatts)}");
            sub.style.marginTop = 0;
            p.Add(sub);
            p.Add(T.AccentDivider(AccentTurbine));

            // Vertical-only scroller: horizontal is explicitly off and inner content
            // is clamped to the viewport width so it can never overflow sideways.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility   = ScrollerVisibility.Auto;
            scroll.contentContainer.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            scroll.contentContainer.style.paddingRight = 6;   // breathing room next to the scrollbar

            // Restore the player's scroll position after a live-refresh rebuild —
            // exactly once (first layout pass), so it never fights active scrolling.
            if (_savedScrollOwner != c.GetInstanceID()) { _savedScroll = 0f; _savedScrollOwner = c.GetInstanceID(); }
            bool restored = false;
            scroll.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (restored) return;
                restored = true;
                if (_savedScroll > 0f)
                    scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0f, _savedScroll));
            });
            scroll.verticalScroller.valueChanged += v => _savedScroll = v;
            p.Add(scroll);

            // ── Live output ───────────────────────────────────────────
            if (complete)
            {
                scroll.Add(T.Subtitle("Live Output"));
                var outCard = T.Card();

                var bigRow = new VisualElement();
                bigRow.style.flexDirection = FlexDirection.Row;
                bigRow.style.alignItems    = Align.FlexEnd;
                bigRow.style.marginBottom  = 6;
                bigRow.pickingMode = PickingMode.Ignore;

                var watts = new Label(PowerFormatter.FormatWatts(c.CurrentOutputWatts));
                watts.style.fontSize = 26;
                watts.style.unityFontStyleAndWeight = FontStyle.Bold;
                watts.style.color = new StyleColor(SteelBright);
                watts.style.flexGrow = 1;
                watts.pickingMode = PickingMode.Ignore;
                bigRow.Add(watts);

                var rated = new Label($"/ {PowerFormatter.FormatWatts(c.ratedPowerWatts)} rated");
                rated.style.fontSize = 11;
                rated.style.color = new StyleColor(T.TextMuted);
                rated.style.marginBottom = 4;
                rated.pickingMode = PickingMode.Ignore;
                bigRow.Add(rated);
                outCard.Add(bigRow);

                float eff01 = Mathf.Clamp01(c.CurrentEfficiency);
                var (effBar, _) = T.ProgressBar(eff01, AccentTurbine, 8);
                outCard.Add(effBar);
                outCard.Add(T.StatRow("⚡", "Efficiency", $"{c.CurrentEfficiency * 100f:0.0} %",
                    c.CurrentEfficiency > 0.65f ? T.AccentGreen : (Color?)null));
                outCard.Add(T.StatRow("🌪", "Wind speed",
                    WindSystem.Instance != null ? $"{WindSystem.Instance.GetWindSpeed():0.0} m/s" : "—"));
                outCard.Add(T.StatRow("⟳", "Rotor speed", $"{c.CurrentRpm:0.0} RPM"));
                scroll.Add(outCard);
                scroll.Add(T.Spacer(10));
            }

            // ── Assembly checklist ────────────────────────────────────
            scroll.Add(T.Subtitle("Assembly"));
            var asmCard = T.Card();
            if (c.vertical)
            {
                asmCard.Add(PartRow("Rotor",  c.RootPart));
                asmCard.Add(PartRow("Blades", c.BladesInstalled > 0 ? FirstBlade(c) : null));
            }
            else
            {
                asmCard.Add(PartRow("Tower",     c.RootPart));
                asmCard.Add(PartRow("Nacelle",   c.Nacelle));
                asmCard.Add(PartRow("Gearbox",   c.Gearbox));
                asmCard.Add(PartRow("Generator", c.Generator));
                asmCard.Add(PartRow("Hub",       c.Hub));
                asmCard.Add(BladeRow(c));
            }
            scroll.Add(asmCard);

            if (!complete)
            {
                var hint = T.Muted(c.vertical
                    ? "Place the missing blade set on the rotor to start generating."
                    : "Place the missing parts on the tower — they snap into position automatically. The turbine only generates once every part is installed.");
                scroll.Add(hint);
            }

            scroll.Add(T.Spacer(10));

            // ── Condition ─────────────────────────────────────────────
            scroll.Add(T.Subtitle("Condition"));
            var condCard = T.Card();
            bool anyPart = false;
            foreach (var part in c.EnumerateAttached())
            {
                anyPart = true;
                condCard.Add(ConditionRow(PartName(part.kind, c.vertical), part));
            }
            if (!anyPart) condCard.Add(T.Muted("No parts installed."));
            condCard.Add(T.Divider());
            condCard.Add(T.StatRow("▣", "Structural integrity", $"{c.WorstCondition:0.0} %",
                ConditionColor(c.WorstCondition)));
            condCard.Add(T.Muted("Output is limited by the most stressed part. The gearbox and blades wear fastest."));
            scroll.Add(condCard);
            scroll.Add(T.Spacer(8));

            // ── Repair ────────────────────────────────────────────────
            var repairRow = new VisualElement();
            repairRow.style.flexDirection  = FlexDirection.Row;
            repairRow.style.alignItems     = Align.Center;
            repairRow.style.justifyContent = Justify.SpaceBetween;

            var costLbl = T.Muted($"Full service: {c.repairPlateCost}× Steel Plate");
            costLbl.style.marginTop = 0;
            repairRow.Add(costLbl);

            var repairBtn = T.ActionButton("🔧  SERVICE TURBINE", () =>
            {
                if (c.TryRepairAll(inventory))
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, c.NeedsRepair ? AccentTurbine : T.AccentDim);
            repairBtn.SetEnabled(c.NeedsRepair);
            repairRow.Add(repairBtn);
            scroll.Add(repairRow);

            // ── Grid connection hint ──────────────────────────────────
            scroll.Add(T.Spacer(6));
            scroll.Add(T.Muted(c.vertical
                ? "Connect your power line to the marked port square at the rotor base."
                : "Connect your power line to the marked port square at the tower base."));

            return p;
        }

        // ────────────────────────────────────────────────────────────────
        //  Row builders
        // ────────────────────────────────────────────────────────────────
        private static VisualElement PartRow(string label, WindTurbinePart part)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.minHeight     = 22;
            row.pickingMode = PickingMode.Ignore;

            bool present = part != null;

            var dot = new Label(present ? "◆" : "◇");
            dot.style.fontSize    = 11;
            dot.style.minWidth    = 18;
            dot.style.color       = new StyleColor(present ? T.AccentGreen : T.AccentAmber);
            dot.pickingMode = PickingMode.Ignore;
            row.Add(dot);

            var name = new Label(label);
            name.style.fontSize = 12;
            name.style.color    = new StyleColor(present ? T.TextPrimary : T.TextSecondary);
            name.pickingMode    = PickingMode.Ignore;
            row.Add(name);

            if (!present)
            {
                var missing = new Label(" - missing");
                missing.style.fontSize = 12;
                missing.style.unityFontStyleAndWeight = FontStyle.Italic;
                missing.style.color = new StyleColor(T.AccentAmber);
                missing.pickingMode = PickingMode.Ignore;
                row.Add(missing);
            }
            return row;
        }

        private static VisualElement BladeRow(WindTurbineController c)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.minHeight     = 22;
            row.pickingMode = PickingMode.Ignore;

            int have = c.BladesInstalled;
            int need = c.bladeCount;
            bool full = have >= need;

            var dot = new Label(full ? "◆" : "◇");
            dot.style.fontSize = 11;
            dot.style.minWidth = 18;
            dot.style.color    = new StyleColor(full ? T.AccentGreen : T.AccentAmber);
            dot.pickingMode = PickingMode.Ignore;
            row.Add(dot);

            var name = new Label($"Blades  {have}/{need}");
            name.style.fontSize = 12;
            name.style.color    = new StyleColor(full ? T.TextPrimary : T.TextSecondary);
            name.pickingMode    = PickingMode.Ignore;
            row.Add(name);

            if (!full)
            {
                var missing = new Label(" - missing");
                missing.style.fontSize = 12;
                missing.style.unityFontStyleAndWeight = FontStyle.Italic;
                missing.style.color = new StyleColor(T.AccentAmber);
                missing.pickingMode = PickingMode.Ignore;
                row.Add(missing);
            }
            return row;
        }

        private static VisualElement ConditionRow(string label, WindTurbinePart part)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.minHeight     = 20;
            row.style.marginBottom  = 3;
            row.pickingMode = PickingMode.Ignore;

            var name = new Label(label);
            name.style.fontSize = 11;
            name.style.color    = new StyleColor(T.TextSecondary);
            name.style.width    = 96;
            name.style.flexShrink = 0;
            name.pickingMode    = PickingMode.Ignore;
            row.Add(name);

            float cond01 = part.condition / 100f;
            var (bar, _) = T.ProgressBar(cond01, ConditionColor(part.condition), 6, true);
            bar.style.marginLeft  = 4;
            bar.style.marginRight = 8;
            row.Add(bar);

            var val = new Label($"{part.condition:0.0} %");
            val.style.fontSize = 11;
            val.style.unityFontStyleAndWeight = FontStyle.Bold;
            val.style.color    = new StyleColor(ConditionColor(part.condition));
            val.style.width    = 52;
            val.style.flexShrink = 0;
            val.style.unityTextAlign = TextAnchor.MiddleRight;
            val.pickingMode    = PickingMode.Ignore;
            row.Add(val);
            return row;
        }

        private static Color ConditionColor(float condition)
        {
            if (condition >= 70f) return T.AccentGreen;
            if (condition >= 40f) return T.AccentAmber;
            return T.AccentRed;
        }

        private static string PartName(WindTurbinePartKind kind, bool vertical) => kind switch
        {
            WindTurbinePartKind.Tower         => "Tower",
            WindTurbinePartKind.Nacelle       => "Nacelle",
            WindTurbinePartKind.Gearbox       => "Gearbox",
            WindTurbinePartKind.Generator     => "Generator",
            WindTurbinePartKind.Hub           => "Hub",
            WindTurbinePartKind.Blade         => "Blade",
            WindTurbinePartKind.VerticalRotor => "Rotor",
            WindTurbinePartKind.VerticalBlade => "Blades",
            _                                 => kind.ToString(),
        };

        private static WindTurbinePart FirstBlade(WindTurbineController c)
        {
            foreach (var p in c.EnumerateAttached())
                if (p.kind == WindTurbinePartKind.VerticalBlade || p.kind == WindTurbinePartKind.Blade)
                    return p;
            return null;
        }
    }
}
